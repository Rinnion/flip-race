using System.Text.Json.Serialization;
using static TrackWorkshop.InputToleranceAnalysis;

namespace TrackWorkshop;

public sealed record PairToleranceOffset([property: JsonRequired] int FirstOffsetMs, [property: JsonRequired] int SecondOffsetMs);
public sealed record PairToleranceRequest(string Json, string Profile, double WarningDuration, string Axis, double[] Inputs,
    [property: JsonRequired] int FirstInputIndex, int SpanMs = 300, PairToleranceOffset[] Offsets = null,
    WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record PairTolerancePreviewRequest(string Json, string Profile, double WarningDuration, string Axis, double[] Inputs,
    [property: JsonRequired] int FirstInputIndex, [property: JsonRequired] int FirstOffsetMs, [property: JsonRequired] int SecondOffsetMs,
    WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record PairToleranceBaseline(double[] Inputs, double FirstInputTime, double SecondInputTime, double StartTime,
    double EndTime, double EndX);
public sealed record PairToleranceCell(int FirstOffsetMs, int SecondOffsetMs, string Status, string Message, bool Computed,
    double? FirstInputTime, double? SecondInputTime, double? PairIntervalMs, double? MinimumIntervalMs,
    double? StartTime, double? EndTime, double? EndX, int Collisions);
public sealed record PairToleranceResult(string Status, string Message, int FirstInputIndex, int SpanMs, int StepMs,
    bool Complete, string StopReason, PairToleranceBaseline Baseline, PairToleranceCell[] Cells);
public sealed record PairTolerancePreview(PairToleranceCell Cell, Sample[] Samples);

internal static class PairToleranceAnalysis
{
    const int StepMs = 50, MaximumSpanMs = 1000;

    internal static PairToleranceResult Find(PairToleranceRequest request, CancellationToken cancellation, TimeSpan? budget = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (request == null) throw new ArgumentException("Пустой запрос.");
        var replay = new ReplayRequest(request.Json, request.Profile, request.WarningDuration, request.Axis,
            request.Inputs, request.SurfaceRunSettings);
        Validate(replay, request.FirstInputIndex);
        var offsets = GetOffsets(request.SpanMs, request.Offsets);
        using var deadline = Deadline(cancellation, budget);
        var token = deadline.Token;
        PairToleranceBaseline baseline = null;
        var cells = new PairToleranceCell[offsets.Length];
        PairToleranceResult Result(string message, bool complete = true) => new(baseline == null ? "unsupported" : "ready",
            message, request.FirstInputIndex, request.SpanMs, StepMs, complete, complete ? "completed" : "time-budget",
            baseline, baseline == null ? [] : cells);
        try
        {
            var prepared = Prepare(replay, request.FirstInputIndex, token);
            token.ThrowIfCancellationRequested();
            baseline = prepared.Baseline;
            if (baseline == null) return Result(prepared.Message);
            var zero = Array.FindIndex(offsets, offset => offset is { FirstOffsetMs: 0, SecondOffsetMs: 0 });
            if (zero >= 0) cells[zero] = Evaluate(prepared, offsets[zero], capture: false, token).Cell;
            Prefix prefix = null;
            if (offsets.Length > 1)
            {
                // If every requested shift is later, removing the original
                // command is still the first possible change to the timeline.
                int earliestOffset = Math.Min(0, offsets.Min(offset => offset.FirstOffsetMs));
                double earliestChange = baseline.Inputs[request.FirstInputIndex] + earliestOffset / 1000d;
                prefix = BuildPrefix(prepared.Source, earliestChange, token);
            }
            for (int i = 0; i < offsets.Length; i++)
                cells[i] ??= Evaluate(prepared, offsets[i], capture: false, token, prefix).Cell;
            cancellation.ThrowIfCancellationRequested();
            return Result("");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            for (int i = 0; i < offsets.Length; i++)
                cells[i] ??= Unavailable(offsets[i], "incomplete", BudgetMessage, computed: false);
            return Result(BudgetMessage, complete: false);
        }
    }

    internal static PairTolerancePreview Preview(PairTolerancePreviewRequest request, CancellationToken cancellation, TimeSpan? budget = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (request == null) throw new ArgumentException("Пустой запрос.");
        var replay = new ReplayRequest(request.Json, request.Profile, request.WarningDuration, request.Axis,
            request.Inputs, request.SurfaceRunSettings);
        Validate(replay, request.FirstInputIndex);
        var offset = new PairToleranceOffset(request.FirstOffsetMs, request.SecondOffsetMs);
        ValidateOffset(offset, MaximumSpanMs);
        using var deadline = Deadline(cancellation, budget);
        var token = deadline.Token;
        try
        {
            var prepared = Prepare(replay, request.FirstInputIndex, token);
            token.ThrowIfCancellationRequested();
            var result = prepared.Baseline == null
                ? new PairTolerancePreview(Unavailable(offset, "invalid", prepared.Message), [])
                : Evaluate(prepared, offset, capture: true, token);
            cancellation.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return new(Unavailable(offset, "incomplete", BudgetMessage, computed: false), []);
        }
    }

    static void Validate(ReplayRequest replay, int first)
    {
        WorkshopService.ValidateAxis(replay.Axis);
        WorkshopService.ValidateWarning(replay.WarningDuration);
        WorkshopService.ValidateInputs(replay.Inputs);
        if (first < 0 || first >= replay.Inputs.Length)
            throw new ArgumentException("Укажите номер первой исходной команды.");
    }

    static PairToleranceOffset[] GetOffsets(int span, PairToleranceOffset[] selection)
    {
        if (span is not (300 or 600 or 1000)) throw new ArgumentException("Диапазон должен быть 300, 600 или 1000 мс.");
        int width = 2 * span / StepMs + 1;
        if (selection == null)
        {
            return Enumerable.Range(0, width * width).Select(index =>
                new PairToleranceOffset(index % width * StepMs - span, index / width * StepMs - span)).ToArray();
        }
        if (selection.Length > width * width) throw new ArgumentException("Слишком много клеток в запросе.");
        var unique = new HashSet<PairToleranceOffset>();
        foreach (var offset in selection)
        {
            ValidateOffset(offset, span);
            if (!unique.Add(offset)) throw new ArgumentException("Каждая клетка должна встречаться только один раз.");
        }
        return selection;
    }

    static void ValidateOffset(PairToleranceOffset offset, int span)
    {
        if (offset == null || !Valid(offset.FirstOffsetMs) || !Valid(offset.SecondOffsetMs))
            throw new ArgumentException("Оба сдвига должны быть кратны 50 мс и входить в выбранный диапазон.");

        bool Valid(int value) => value >= -span && value <= span && value % StepMs == 0;
    }

    sealed record Prepared(Source Source, int First, PairToleranceBaseline Baseline, string Message);

    static Prepared Prepare(ReplayRequest replay, int first, CancellationToken token)
    {
        if (first + 1 == replay.Inputs.Length)
            return new(null, first, null, "После выбранной команды нет второй команды.");
        var source = ReadSource(replay, token, out string message);
        if (source == null) return new(null, first, null, message);
        var times = source.Replay.Inputs;
        var flight = Shift(source, times, first, null, capture: false, token, secondIndex: first + 1);
        if (flight.Status != "safe") return new(source, first, null, $"Исходная пара: {flight.Message}");
        var baseline = new PairToleranceBaseline(times, flight.InputTime.Value, flight.SecondInputTime.Value,
            flight.StartTime, flight.End.Time, flight.End.X);
        return new(source, first, baseline, "");
    }

    static PairTolerancePreview Evaluate(Prepared prepared, PairToleranceOffset offset, bool capture, CancellationToken token,
        Prefix prefix = null)
    {
        token.ThrowIfCancellationRequested();
        var baseline = prepared.Baseline;
        var times = baseline.Inputs;
        int first = prepared.First, second = first + 1;
        double firstTime = times[first] + offset.FirstOffsetMs / 1000d;
        double secondTime = times[second] + offset.SecondOffsetMs / 1000d;
        if (firstTime < -ScheduleTolerance || secondTime < -ScheduleTolerance)
            return new(Unavailable(offset, "invalid", "Сдвиг переносит команду до начала заезда."), []);
        firstTime = Math.Max(0, firstTime);
        secondTime = Math.Max(0, secondTime);
        double pairGap = secondTime - firstTime;
        double leftGap = first > 0 ? firstTime - times[first - 1] : double.PositiveInfinity;
        double rightGap = second + 1 < times.Length ? times[second + 1] - secondTime : double.PositiveInfinity;
        double shortestGap = Math.Min(pairGap, Math.Min(leftGap, rightGap));
        // The replay scheduler treats commands within its dispatch tolerance
        // as simultaneous even when their supplied timestamps differ slightly.
        if (shortestGap <= TimeTolerance && (offset.FirstOffsetMs != 0 || offset.SecondOffsetMs != 0))
            return new(Unavailable(offset, "invalid", "Сдвиг совмещает соседние команды или меняет их порядок."), []);

        var shifted = (double[])times.Clone();
        shifted[first] = firstTime;
        shifted[second] = secondTime;
        var window = Shift(prepared.Source, shifted, first, new(baseline.StartTime, baseline.EndX), capture, token, second, prefix);
        var cell = new PairToleranceCell(offset.FirstOffsetMs, offset.SecondOffsetMs, window.Status, window.Message, true,
            window.InputTime, window.SecondInputTime, Math.Round(pairGap * 1000, 6), Math.Round(shortestGap * 1000, 6),
            window.StartTime, window.End.Time, window.End.X, window.Status == "collision" ? 1 : 0);
        return new(cell, window.Samples);
    }

    static PairToleranceCell Unavailable(PairToleranceOffset offset, string status, string message, bool computed = true) =>
        new(offset.FirstOffsetMs, offset.SecondOffsetMs, status, message, computed, null, null, null, null, null, null, null, 0);
}
