using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using static TrackWorkshop.InputToleranceAnalysis;

namespace TrackWorkshop;

public sealed record ManeuverSafeWindow(double EarliestMs, double LatestMs);
public sealed record ManeuverToleranceCell(int InputIndex, int OffsetMs, string Total, string Valid, string Computed,
    string Safe, double? Percent, int[] WitnessOffsetsMs);
public sealed record ManeuverToleranceBaseline(double[] Inputs, double StartTime, double EndTime, double EndX);
public sealed record ManeuverToleranceResult(int RulesVersion, string Status, string Message, int SpanMs, int StepMs,
    bool Complete, string StopReason, string Total, string Cursor, string Valid, string Computed, string Safe,
    double? Percent, ManeuverSafeWindow SafeWindow, ManeuverToleranceBaseline Baseline, ManeuverToleranceCell[] Cells);
public sealed record ManeuverToleranceRequest(string Json, string Profile, double WarningDuration, string Axis,
    double[] Inputs, [property: JsonRequired] int InputIndex, WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record ManeuverToleranceResponse(string CanonicalJson, ToleranceBatchReport Batch,
    ToleranceBatchTarget Target, ToleranceCheckpoint Checkpoint);
public sealed record ManeuverTolerancePreviewRequest(string Json, string Profile, double WarningDuration, string Axis,
    double[] Inputs, [property: JsonRequired] int InputIndex, [property: JsonRequired] int OffsetMs,
    int[] OffsetsMs, WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record ManeuverTolerancePreview(string Status, string Message, Sample[] Samples, double[] Inputs,
    InputResult[] PressPoints, int[] OffsetsMs, double? StartTime = null, double? EndTime = null, double? EndX = null);

// The joint grid is a radix-25 cursor, never an array of 25^N tuples. Every
// successful evaluation contributes once to the maneuver and once per row.
internal static class ManeuverToleranceAnalysis
{
    internal const int RulesVersion = 2, ChunkSize = 256;
    internal static BigInteger Count(int dimensions) => BigInteger.Pow(ToleranceBatch.Width, dimensions);
    internal static string Text(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);
    internal static bool Number(string value, out BigInteger number) =>
        BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0 && Text(number) == value;
    internal static double? Percentage(BigInteger safe, BigInteger valid) => valid.IsZero ? null :
        (double)(safe * 1000000000 / valid) / 10000000;

    internal static ManeuverToleranceResult Empty(ToleranceBatchTarget target)
    {
        int n = target.InputIndices.Length;
        string cellTotal = Text(Count(n - 1));
        return new(RulesVersion, "pending", "", 600, 50, false, "time-budget", Text(Count(n)), "0", "0", "0", "0",
            null, null, null, target.InputIndices.SelectMany(index => Enumerable.Range(0, 25)
                .Select(offset => new ManeuverToleranceCell(index, offset * 50 - 600, cellTotal, "0", "0", "0", null, null))).ToArray());
    }

    internal static ManeuverToleranceResult Advance(ReplayRequest request, ToleranceBatchTarget target,
        CancellationToken cancellation, TimeSpan? budget = null, int chunkSize = ChunkSize)
    {
        cancellation.ThrowIfCancellationRequested();
        var previous = target.Maneuver ?? Empty(target);
        var cells = (ManeuverToleranceCell[])previous.Cells.Clone();
        var total = Count(target.InputIndices.Length);
        BigInteger cursor = BigInteger.Parse(previous.Cursor), valid = BigInteger.Parse(previous.Valid), safe = BigInteger.Parse(previous.Safe);
        var baseline = previous.Baseline;
        var safeWindow = previous.SafeWindow;
        var duration = budget ?? TimeSpan.FromMilliseconds(500);
        using var deadline = Deadline(cancellation, duration == TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        ManeuverToleranceResult Result(string status, string message, bool complete, string reason) =>
            previous with { Status = status, Message = message, Complete = complete, StopReason = reason,
                Cursor = Text(cursor), Valid = Text(valid), Computed = Text(valid), Safe = Text(safe),
                Percent = Percentage(safe, valid), SafeWindow = safeWindow, Baseline = baseline, Cells = cells };
        try
        {
            var source = ReadSource(request, token, out string message);
            if (source == null) return Result("unsupported", message, true, "completed");
            int first = target.InputIndices[0], last = target.InputIndices[^1];
            if (baseline == null)
            {
                var reference = Shift(source, source.Replay.Inputs, first, null, false, token, first == last ? null : last);
                if (reference.Status != "safe") return Result("unsupported", $"Исходный манёвр: {reference.Message}", true, "completed");
                baseline = new(source.Replay.Inputs, reference.StartTime, reference.End.Time, reference.End.X);
            }
            var prefix = BuildPrefix(source, Math.Max(0, baseline.Inputs[first] - .6), token);
            // Preparation (including distance conversion) has its own safety
            // deadline. Work pacing begins afterwards and never interrupts a
            // completed tuple; even a tiny chunk gets one atomic unit.
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var initialCursor = cursor;
            int attempted = 0;
            while (cursor < total && attempted < chunkSize && (cursor == initialCursor || clock.Elapsed < duration))
            {
                token.ThrowIfCancellationRequested();
                int[] offsets = Offsets(cursor, target.InputIndices.Length);
                var times = Shifted(baseline.Inputs, target.InputIndices, offsets);
                int invalidAt = InvalidAt(times, target.InputIndices);
                int impossiblePrefix = ImpossiblePrefixAt(times, baseline.Inputs, target.InputIndices);
                if (impossiblePrefix >= 0) invalidAt = invalidAt < 0 ? impossiblePrefix : Math.Min(invalidAt, impossiblePrefix);
                if (invalidAt >= 0)
                {
                    // An inadmissible prefix invalidates every assignment of
                    // its remaining commands. Skip its entire radix block.
                    var block = Count(offsets.Length - invalidAt - 1);
                    cursor = (cursor / block + 1) * block;
                    continue;
                }
                var window = Shift(source, times, first, new(baseline.StartTime, baseline.EndX), false, token,
                    first == last ? null : last, prefix);
                // Only a fully evaluated schedule advances the cursor. A
                // deadline midway through physics retries this same tuple.
                bool success = window.Status == "safe";
                valid++;
                if (success)
                {
                    safe++;
                    double begin = window.InputTime.Value * 1000, end = (window.SecondInputTime ?? window.InputTime).Value * 1000;
                    safeWindow = safeWindow == null ? new(begin, end) : new(Math.Min(begin, safeWindow.EarliestMs), Math.Max(end, safeWindow.LatestMs));
                }
                for (int row = 0; row < offsets.Length; row++)
                {
                    int cellIndex = row * 25 + (offsets[row] + 600) / 50;
                    var cell = cells[cellIndex];
                    BigInteger cellValid = BigInteger.Parse(cell.Valid) + 1, cellSafe = BigInteger.Parse(cell.Safe) + (success ? 1 : 0);
                    int[] witness = cell.WitnessOffsetsMs;
                    if (success && Closer(offsets, witness)) witness = (int[])offsets.Clone();
                    cells[cellIndex] = cell with { Valid = Text(cellValid), Computed = Text(cellValid), Safe = Text(cellSafe),
                        Percent = Percentage(cellSafe, cellValid), WitnessOffsetsMs = witness };
                }
                cursor++;
                attempted++;
            }
            return Result("ready", "", cursor == total, cursor == total ? "completed" : "chunk-limit");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return Result(baseline == null ? "pending" : "ready", BudgetMessage, false,
                cancellation.IsCancellationRequested ? "cancelled" : "time-budget");
        }
    }

    internal static int[] Offsets(BigInteger cursor, int n)
    {
        var offsets = new int[n];
        for (int i = n - 1; i >= 0; i--) { cursor = BigInteger.DivRem(cursor, 25, out var digit); offsets[i] = (int)digit * 50 - 600; }
        return offsets;
    }

    internal static BigInteger Rank(int[] offsets)
    {
        BigInteger result = 0;
        foreach (int offset in offsets) result = result * 25 + (offset + 600) / 50;
        return result;
    }

    internal static bool Grid(int[] offsets, int count) => offsets?.Length == count &&
        offsets.All(offset => offset is >= -600 and <= 600 && offset % 50 == 0);

    internal static bool Closer(int[] proposed, int[] current)
    {
        if (current == null) return true;
        long a = proposed.Sum(value => Math.Abs((long)value)), b = current.Sum(value => Math.Abs((long)value));
        if (a != b) return a < b;
        for (int i = 0; i < proposed.Length; i++) if (proposed[i] != current[i]) return proposed[i] < current[i];
        return false;
    }

    internal static double[] Shifted(double[] baseline, int[] indices, int[] offsets)
    {
        var times = (double[])baseline.Clone();
        for (int i = 0; i < indices.Length; i++) times[indices[i]] = baseline[indices[i]] + offsets[i] / 1000d;
        return times;
    }

    // 1e-10 seconds only absorbs floating-point representation of exact 100ms.
    // A real 99ms gap remains inadmissible. Check fixed immediate neighbors too.
    internal static int InvalidAt(double[] times, int[] indices)
    {
        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i];
            if (times[index] < 0 || index > 0 && times[index] - times[index - 1] < .1 - ScheduleTolerance) return i;
        }
        int last = indices[^1];
        return last + 1 < times.Length && times[last + 1] - times[last] < .1 - ScheduleTolerance ? indices.Length - 1 : -1;
    }

    static int ImpossiblePrefixAt(double[] times, double[] baseline, int[] indices)
    {
        double latest = double.PositiveInfinity;
        int last = indices[^1];
        if (last + 1 < times.Length) latest = times[last + 1] - .1 * indices.Length;
        // Latest feasible time of the first command implied by every suffix
        // bound; this also prunes all-invalid large groups without traversing
        // the Cartesian grid. Tighten the bound while walking backwards.
        var latestByRow = new double[indices.Length];
        for (int row = indices.Length - 1; row >= 0; row--)
        {
            latest = Math.Min(latest, baseline[indices[row]] + .6 - .1 * row);
            latestByRow[row] = latest + .1 * row;
        }
        for (int row = 0; row < indices.Length; row++)
            if (times[indices[row]] > latestByRow[row] + ScheduleTolerance) return row;
        return -1;
    }

    internal static void ValidateRestored(ReplayRequest request, ToleranceBatchTarget target, CancellationToken cancellation)
    {
        var baseline = target.Maneuver?.Baseline;
        if (baseline == null) return;
        using var deadline = Deadline(cancellation, null);
        try
        {
            var source = ReadSource(request, deadline.Token, out _);
            if (source == null || !source.Replay.Inputs.SequenceEqual(baseline.Inputs)) throw new ArgumentException("Изменена исходная временная лента манёвра.");
            int first = target.InputIndices[0], last = target.InputIndices[^1];
            var reference = Shift(source, source.Replay.Inputs, first, null, false, deadline.Token, first == last ? null : last);
            if (reference.Status != "safe" || reference.StartTime != baseline.StartTime || reference.End.Time != baseline.EndTime ||
                reference.End.X != baseline.EndX) throw new ArgumentException("Изменена исходная граница манёвра.");
            // Witnesses are checked structurally (grid, selected cell, cursor,
            // schedule) on import. Preview always replays physics before
            // exposing a safe route. Replaying all witnesses on every bounded
            // continuation would dominate the joint computation itself.
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new ArgumentException("Истёк лимит повторной проверки сохранённых вариантов."); }
    }

    internal static ManeuverTolerancePreview Preview(ManeuverTolerancePreviewRequest request, CancellationToken cancellation)
    {
        if (request == null) throw new ArgumentException("Пустой запрос.");
        var replay = new ReplayRequest(request.Json, request.Profile, request.WarningDuration, request.Axis, request.Inputs, request.SurfaceRunSettings);
        var batch = ToleranceBatch.Prepare(replay, cancellation);
        var plan = batch.Report("pending");
        if (request.InputIndex < 0 || request.InputIndex >= plan.CommandTargetIndices.Length) throw new ArgumentException("Укажите номер команды.");
        var target = plan.Targets[plan.CommandTargetIndices[request.InputIndex]];
        int row = Array.IndexOf(target.InputIndices, request.InputIndex);
        if (target.Kind != "maneuver" || row < 0 || !Grid(request.OffsetsMs, target.InputIndices.Length) || request.OffsetsMs[row] != request.OffsetMs)
            throw new ArgumentException("Неверный безопасный вариант выбранной клетки.");
        using var deadline = Deadline(cancellation, null);
        var source = ReadSource(replay, deadline.Token, out string message);
        if (source == null) return new("invalid", message, [], [], [], request.OffsetsMs);
        int first = target.InputIndices[0], last = target.InputIndices[^1];
        var reference = Shift(source, source.Replay.Inputs, first, null, false, deadline.Token, first == last ? null : last);
        if (reference.Status != "safe") return new("invalid", reference.Message, [], [], [], request.OffsetsMs);
        var times = Shifted(source.Replay.Inputs, target.InputIndices, request.OffsetsMs);
        if (InvalidAt(times, target.InputIndices) >= 0) return new("invalid", "Недопустимый интервал между командами.", [], times, [], request.OffsetsMs);
        var window = Shift(source, times, first, new(reference.StartTime, reference.End.X), true, deadline.Token, first == last ? null : last);
        if (window.Status != "safe") return new(window.Status, window.Message, [], times, [], request.OffsetsMs);
        var full = WorkshopService.Replay(source.Replay with { Inputs = times }, source.Track, source.Flight,
            includeTrajectory: false, cancellation: deadline.Token);
        double start = window.InputTime.Value;
        int firstPoint = Array.FindIndex(window.Samples, point => point.Time >= start - TimeTolerance);
        if (firstPoint >= 0)
        {
            int at = firstPoint;
            while (at > 0 && window.Samples[at - 1].Flipping) at--;
            start = Math.Min(start, window.Samples[at].Time);
        }
        var samples = window.Samples.Where(point => point.Time >= start - TimeTolerance).ToArray();
        return new("safe", "", samples, times, full.Inputs.ToArray(), request.OffsetsMs, start, window.End.Time, window.End.X);
    }
}
