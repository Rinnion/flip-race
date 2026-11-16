using System.Text.Json.Serialization;
using FlipRace.Core;

namespace TrackWorkshop;

public sealed record ToleranceBatchTarget(string Kind, [property: JsonRequired] int InputIndex, int? FirstInputIndex, string Message,
    InputToleranceResult Single = null, PairToleranceResult Pair = null)
{
    internal bool Finished => Kind == "unsupported" || Single?.Complete == true || Pair?.Complete == true;
}

public sealed record ToleranceBatchReport(string Profile, double WarningDuration, WorkshopSurfaceRunSettings SurfaceRunSettings,
    string Axis, double[] Inputs, int SpanMs, int StepMs, ToleranceBatchTarget[] Targets, int[] CommandTargetIndices,
    bool Complete, string StopReason);
public sealed record ToleranceBatchStatus(string Status, ToleranceBatchReport Batch = null);
public sealed record ToleranceBatchResponse(string CanonicalJson, ToleranceBatchReport Batch);
public sealed record ToleranceCheckpoint(string Identity, [property: JsonRequired] int InputIndex,
    [property: JsonRequired] int Length, string[] Payload);
public sealed record ToleranceBatchCheckpointRequest(string Json, string Profile, double WarningDuration, string Axis,
    double[] Inputs, WorkshopSurfaceRunSettings SurfaceRunSettings, ToleranceCheckpoint[] Checkpoints);

internal sealed class ToleranceBatch
{
    internal const int SpanMs = 600, StepMs = 50, Width = 2 * SpanMs / StepMs + 1;
    readonly ReplayRequest request;
    readonly ToleranceBatchStorage storage;
    readonly ToleranceBatchTarget[] targets;
    readonly int[] commandTargetIndices;

    ToleranceBatch(ReplayRequest request, WorkshopService.Preview preview, FlightSettings flight, CancellationToken cancellation)
    {
        this.request = request;
        using var deadline = InputToleranceAnalysis.Deadline(cancellation, null);
        try { (targets, commandTargetIndices) = Plan(request, preview.Package, flight, deadline.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new ArgumentException(InputToleranceAnalysis.BudgetMessage); }
        storage = new(preview.Document, ToleranceBatchIdentity.Capture(request, preview.Package, flight), targets, commandTargetIndices);
    }

    internal static ToleranceBatch Prepare(ReplayRequest request, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (request == null) throw new ArgumentException("Пустой запрос.");
        WorkshopService.ValidateAxis(request.Axis);
        WorkshopService.ValidateWarning(request.WarningDuration);
        WorkshopService.ValidateInputs(request.Inputs);
        if (request.Inputs.Length == 0) throw new ArgumentException("Выберите повтор с нажатиями.");
        var surface = request.SurfaceRunSettings ?? WorkshopSurfaceRunSettings.Legacy;
        surface.ToCore();
        var preview = WorkshopService.Read(request.Json);
        var flight = WorkshopService.GetFlight(preview.Package, request.Profile);
        return new(request with { SurfaceRunSettings = surface }, preview, flight, cancellation);
    }

    static (ToleranceBatchTarget[] Targets, int[] CommandTargetIndices) Plan(ReplayRequest request, TrackPackage track,
        FlightSettings flight, CancellationToken cancellation)
    {
        var game = WorkshopService.StartSolo(track.Track, flight, request.WarningDuration, request.SurfaceRunSettings);
        RaceSimulation.Advance(game, RaceConfig.CountdownDuration);
        var player = game.Players[0];
        var commands = new InputResult[request.Inputs.Length];
        var pairFrom = new bool[commands.Length];
        InputResult previous = null;
        bool activeAtPrevious = false;
        FlightMeasurement lastFlightAtPrevious = null;
        int next = 0;
        while (next < commands.Length && WorkshopService.IsRacing(game))
        {
            bool continuingFlight = activeAtPrevious && player.FlightActive && player.LastFlight == lastFlightAtPrevious;
            var command = WorkshopService.ReplayStep(game, request.Axis, request.Inputs, ref next, cancellationToken: cancellation);
            if (command == null) continue;
            commands[command.Index] = command;
            if (command.Status == "accepted" && previous?.Status == "accepted" &&
                continuingFlight && command.Time > previous.Time)
                pairFrom[previous.Index] = true;
            previous = command;
            activeAtPrevious = player.FlightActive;
            lastFlightAtPrevious = player.LastFlight;
        }
        var targets = new List<ToleranceBatchTarget>();
        var indices = new int[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            bool hasPreviousPair = i > 0 && pairFrom[i - 1];
            indices[i] = hasPreviousPair ? targets.Count - 1 : targets.Count;
            if (pairFrom[i]) targets.Add(new("pair", i, i, ""));
            else if (commands[i] == null)
                targets.Add(new("unsupported", i, null, "Заезд закончился до исполнения выбранной команды."));
            else if (commands[i].Status != "accepted")
                targets.Add(new("unsupported", i, null, $"Исходная команда не принята: {commands[i].Reason}."));
            else if (!hasPreviousPair)
                targets.Add(new("single", i, null, ""));
        }
        return (targets.ToArray(), indices);
    }

    internal int Total => targets.Length;
    internal int Completed => targets.Count(target => target.Finished);
    internal ToleranceCheckpoint[] Checkpoints => storage.Checkpoints;
    internal ToleranceBatchReport Report(string reason) => storage.Identity.Report(targets, commandTargetIndices,
        Completed == Total ? "completed" : reason);

    internal void Restore(CancellationToken cancellation)
    {
        var unique = new HashSet<int>();
        foreach (var checkpoint in storage.SavedCheckpoints())
        {
            cancellation.ThrowIfCancellationRequested();
            if (checkpoint == null || !unique.Add(checkpoint.InputIndex))
                throw new ArgumentException("Повтор сохранённой матрицы.");
            var target = Load(checkpoint, out int index);
            if (!storage.TryAdd(checkpoint)) throw new ArgumentException(ToleranceBatchStorage.SizeMessage);
            targets[index] = target;
        }
    }

    ToleranceBatchTarget Load(ToleranceCheckpoint checkpoint, out int index)
    {
        if (checkpoint == null || checkpoint.Identity != storage.KeyForIdentity)
            throw new ArgumentException("Сохранённая матрица относится к другому повтору или настройкам.");
        index = Array.BinarySearch(targets, new ToleranceBatchTarget("", checkpoint.InputIndex, null, ""), TargetOrder);
        if (index < 0 || targets[index].Kind == "unsupported")
            throw new ArgumentException("Сохранённая матрица отсутствует в плане повтора.");
        return storage.Decode(checkpoint, targets[index] with { Single = null, Pair = null });
    }

    static readonly IComparer<ToleranceBatchTarget> TargetOrder = Comparer<ToleranceBatchTarget>.Create(
        (a, b) => a.InputIndex.CompareTo(b.InputIndex));

    internal (ToleranceBatchTarget Target, ToleranceCheckpoint Checkpoint) Evaluate(int index, CancellationToken cancellation,
        TimeSpan? budget = null)
    {
        var target = targets[index];
        if (target.Finished) return (null, null);
        if (target.Kind == "single")
        {
            var report = InputToleranceAnalysis.Find(new(request.Json, request.Profile, request.WarningDuration,
                request.Axis, request.Inputs, target.InputIndex, request.SurfaceRunSettings, SpanMs), cancellation, budget);
            target = target with { Single = report };
        }
        else
        {
            var previous = target.Pair;
            var offsets = previous?.Baseline == null ? null : previous.Cells.Where(cell => !cell.Computed)
                .Select(cell => new PairToleranceOffset(cell.FirstOffsetMs, cell.SecondOffsetMs)).ToArray();
            var report = PairToleranceAnalysis.Find(new(request.Json, request.Profile, request.WarningDuration,
                request.Axis, request.Inputs, target.InputIndex, SpanMs, offsets, request.SurfaceRunSettings), cancellation, budget);
            if (previous?.Baseline != null)
            {
                if (report.Baseline == null) return (target, storage.Pack(target));
                var additions = report.Cells.ToDictionary(cell => (cell.FirstOffsetMs, cell.SecondOffsetMs));
                report = report with { Cells = previous.Cells.Select(cell => cell.Computed
                    ? cell : additions[(cell.FirstOffsetMs, cell.SecondOffsetMs)]).ToArray() };
            }
            target = target with { Pair = report };
        }
        cancellation.ThrowIfCancellationRequested();
        var checkpoint = storage.Pack(target);
        if (!storage.TryAdd(checkpoint)) return (target, null);
        targets[index] = target;
        return (target, checkpoint);
    }

    internal ToleranceBatchResponse Save(string reason)
    {
        var batch = Report(reason);
        return new(storage.Save(batch.StopReason), batch);
    }

    internal static ToleranceBatchResponse SaveCheckpoint(ToleranceBatchCheckpointRequest request, CancellationToken cancellation)
    {
        if (request?.Checkpoints == null || request.Checkpoints.Length > 4096)
            throw new ArgumentException("Передайте список готовых матриц.");
        var batch = Prepare(new(request.Json, request.Profile, request.WarningDuration, request.Axis, request.Inputs,
            request.SurfaceRunSettings), cancellation);
        var unique = new HashSet<int>();
        foreach (var checkpoint in request.Checkpoints)
        {
            cancellation.ThrowIfCancellationRequested();
            var target = batch.Load(checkpoint, out int index);
            if (!unique.Add(index)) throw new ArgumentException("Повтор сохранённой матрицы.");
            if (!batch.storage.TryAdd(checkpoint)) throw new ArgumentException(ToleranceBatchStorage.SizeMessage);
            batch.targets[index] = target;
        }
        return batch.Save("cancelled");
    }
}
