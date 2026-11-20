using System.Text.Json.Serialization;
using FlipRace.Core;

namespace TrackWorkshop;

public sealed record ToleranceBatchTarget(string Kind, [property: JsonRequired] int InputIndex, int? FirstInputIndex, string Message,
    InputToleranceResult Single = null, PairToleranceResult Pair = null, int[] InputIndices = null, ManeuverToleranceResult Maneuver = null)
{
    internal bool Finished => Kind == "unsupported" || Maneuver?.Complete == true;
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
            int first = i, targetIndex = targets.Count;
            if (commands[i]?.Status == "accepted")
            {
                while (i + 1 < commands.Length && pairFrom[i]) i++;
                var group = Enumerable.Range(first, i - first + 1).ToArray();
                targets.Add(new("maneuver", first, first, "", InputIndices: group));
            }
            else targets.Add(new("unsupported", first, null, commands[i] == null
                ? "Заезд закончился до исполнения выбранной команды."
                : $"Исходная команда не принята: {commands[i].Reason}.", InputIndices: [first]));
            for (int command = first; command <= i; command++) indices[command] = targetIndex;
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
            var target = Load(checkpoint, out int index, cancellation);
            if (!storage.TryAdd(checkpoint)) throw new ArgumentException(ToleranceBatchStorage.SizeMessage);
            targets[index] = target;
        }
    }

    ToleranceBatchTarget Load(ToleranceCheckpoint checkpoint, out int index, CancellationToken cancellation)
    {
        if (checkpoint == null || checkpoint.Identity != storage.KeyForIdentity)
            throw new ArgumentException("Сохранённая матрица относится к другому повтору или настройкам.");
        index = Array.BinarySearch(targets, new ToleranceBatchTarget("", checkpoint.InputIndex, null, ""), TargetOrder);
        if (index < 0 || targets[index].Kind == "unsupported")
            throw new ArgumentException("Сохранённая матрица отсутствует в плане повтора.");
        var target = storage.Decode(checkpoint, targets[index] with { Single = null, Pair = null, Maneuver = null });
        ManeuverToleranceAnalysis.ValidateRestored(request, target, cancellation);
        return target;
    }

    static readonly IComparer<ToleranceBatchTarget> TargetOrder = Comparer<ToleranceBatchTarget>.Create(
        (a, b) => a.InputIndex.CompareTo(b.InputIndex));

    internal (ToleranceBatchTarget Target, ToleranceCheckpoint Checkpoint) Evaluate(int index, CancellationToken cancellation,
        TimeSpan? budget = null)
    {
        var target = targets[index];
        if (target.Finished) return (null, null);
        target = target with { Maneuver = ManeuverToleranceAnalysis.Advance(request, target, cancellation, budget) };
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

    internal static ManeuverToleranceResponse Selected(ManeuverToleranceRequest request, CancellationToken cancellation)
    {
        if (request == null) throw new ArgumentException("Пустой запрос.");
        var batch = Prepare(new(request.Json, request.Profile, request.WarningDuration, request.Axis, request.Inputs,
            request.SurfaceRunSettings), cancellation);
        batch.Restore(cancellation);
        if (request.InputIndex < 0 || request.InputIndex >= batch.commandTargetIndices.Length)
            throw new ArgumentException("Укажите номер команды.");
        int index = batch.commandTargetIndices[request.InputIndex];
        var (target, checkpoint) = batch.Evaluate(index, cancellation);
        if (target != null && checkpoint == null) throw new ArgumentException(ToleranceBatchStorage.SizeMessage);
        var saved = batch.Save("time-budget");
        return new(saved.CanonicalJson, saved.Batch, target ?? batch.targets[index], checkpoint);
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
            var target = batch.Load(checkpoint, out int index, cancellation);
            if (!unique.Add(index)) throw new ArgumentException("Повтор сохранённой матрицы.");
            if (!batch.storage.TryAdd(checkpoint)) throw new ArgumentException(ToleranceBatchStorage.SizeMessage);
            batch.targets[index] = target;
        }
        return batch.Save("cancelled");
    }
}
