using FlipRace.Core;

namespace TrackWorkshop;

public record InputToleranceRequest(string Json, string Profile, double WarningDuration, string Axis, double[] Inputs,
    int InputIndex, WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record InputTolerancePreviewRequest(string Json, string Profile, double WarningDuration, string Axis,
    double[] Inputs, int InputIndex, int? OffsetMs = null, bool Omit = false, WorkshopSurfaceRunSettings SurfaceRunSettings = null)
    : InputToleranceRequest(Json, Profile, WarningDuration, Axis, Inputs, InputIndex, SurfaceRunSettings);
public sealed record InputToleranceBaseline(double[] Inputs, double InputTime, double StartTime, double EndTime, double EndX);
public sealed record InputToleranceProbe(int? OffsetMs, bool Omit, string Status, string Message, double? InputTime,
    double? StartTime, double? EndTime, double? EndX, int Collisions, double? MinimumIntervalMs, bool BotCompatible);
public sealed record InputToleranceResult(string Status, string Message, int InputIndex, int StepMs, int SpanMs, int Attempted,
    bool Complete, string StopReason, InputToleranceBaseline Baseline, InputToleranceProbe[] Probes, InputToleranceProbe Omission);
public sealed record InputTolerancePreview(InputToleranceProbe Probe, Sample[] Samples);

internal static class InputToleranceAnalysis
{
    const int StepMs = 50, SpanMs = 300, BotMinimumMs = 100;
    const double PositionTolerance = 1e-5;
    internal const double TimeTolerance = 1e-7, ScheduleTolerance = 1e-10;
    static readonly TimeSpan WorkBudget = TimeSpan.FromSeconds(10);
    internal const string BudgetMessage = "Истёк лимит времени проверки.";

    internal static InputToleranceResult Find(InputToleranceRequest request, CancellationToken cancellation, TimeSpan? budget = null)
    {
        cancellation.ThrowIfCancellationRequested();
        Validate(request);
        using var deadline = Deadline(cancellation, budget);
        var token = deadline.Token;
        var probes = new List<InputToleranceProbe>();
        InputToleranceBaseline baseline = null;
        InputToleranceProbe omission = null;
        InputToleranceResult Result(string status, string message, bool complete = true) => new(status, message,
            request.InputIndex, StepMs, SpanMs, probes.Count(Computed) + (Computed(omission) ? 1 : 0), complete,
            complete ? "completed" : "time-budget", baseline, probes.ToArray(), omission);
        try
        {
            var prepared = Prepare(request, token);
            token.ThrowIfCancellationRequested();
            if (prepared.Reference == null) return Result("unsupported", prepared.Message);
            baseline = prepared.Reference;
            for (int offset = -SpanMs; offset <= SpanMs; offset += StepMs)
                probes.Add(Evaluate(prepared, offset, capture: false, token).Probe);
            omission = Evaluate(prepared, null, capture: false, token).Probe;
            cancellation.ThrowIfCancellationRequested();
            return Result("ready", "");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            for (int offset = -SpanMs + StepMs * probes.Count; offset <= SpanMs; offset += StepMs)
                probes.Add(Unavailable(offset, "incomplete", BudgetMessage));
            omission ??= Unavailable(null, "incomplete", BudgetMessage);
            return Result(baseline == null ? "unsupported" : "ready", BudgetMessage, complete: false);
        }
    }

    internal static InputTolerancePreview Preview(InputTolerancePreviewRequest request, CancellationToken cancellation, TimeSpan? budget = null)
    {
        cancellation.ThrowIfCancellationRequested();
        Validate(request);
        int? offset = request.Omit ? null : request.OffsetMs;
        if (!request.Omit && (offset is null or < -SpanMs or > SpanMs || offset % StepMs != 0))
            throw new ArgumentException("Сдвиг должен быть кратен 50 мс и находиться между −300 и +300 мс.");
        using var deadline = Deadline(cancellation, budget);
        try
        {
            var prepared = Prepare(request, deadline.Token);
            var result = prepared.Reference == null
                ? new InputTolerancePreview(Unavailable(offset, "invalid", prepared.Message), [])
                : Evaluate(prepared, offset, capture: true, deadline.Token);
            cancellation.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return new(Unavailable(offset, "incomplete", BudgetMessage), []);
        }
    }

    static void Validate(InputToleranceRequest request)
    {
        if (request == null) throw new ArgumentException("Пустой запрос.");
        WorkshopService.ValidateAxis(request.Axis);
        WorkshopService.ValidateWarning(request.WarningDuration);
        WorkshopService.ValidateInputs(request.Inputs);
        if (request.InputIndex < 0 || request.InputIndex >= request.Inputs.Length)
            throw new ArgumentException("Укажите номер исходного нажатия.");
    }

    internal static CancellationTokenSource Deadline(CancellationToken cancellation, TimeSpan? budget)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var duration = budget ?? WorkBudget;
        if (duration == TimeSpan.Zero) deadline.Cancel();
        else deadline.CancelAfter(duration);
        return deadline;
    }

    internal sealed record Source(ReplayRequest Replay, WorkshopService.Preview Track, FlightSettings Flight)
    {
        public GameState Start()
        {
            var game = WorkshopService.StartSolo(Track.Package.Track, Flight, Replay.WarningDuration, Replay.SurfaceRunSettings);
            RaceSimulation.Advance(game, RaceConfig.CountdownDuration);
            return game;
        }
    }

    sealed record Prepared(Source Source, int Index, InputToleranceBaseline Reference, string Message);

    static Prepared Prepare(InputToleranceRequest request, CancellationToken token)
    {
        var source = ReadSource(new(request.Json, request.Profile, request.WarningDuration, request.Axis,
            request.Inputs, request.SurfaceRunSettings), token, out string message);
        if (source == null) return new(null, request.InputIndex, null, message);
        var times = source.Replay.Inputs;
        var reference = Shift(source, times, request.InputIndex, null, capture: false, token);
        if (reference.Status != "safe")
            return new(source, request.InputIndex, null, $"Исходный манёвр: {reference.Message}");
        var baseline = new InputToleranceBaseline(times, reference.InputTime.Value, reference.StartTime,
            reference.End.Time, reference.End.X);
        return new(source, request.InputIndex, baseline, "");
    }

    internal static Source ReadSource(ReplayRequest replay, CancellationToken token, out string message)
    {
        token.ThrowIfCancellationRequested();
        var parsed = WorkshopService.Read(replay.Json);
        var flight = WorkshopService.GetFlight(parsed.Package, replay.Profile);
        replay = replay with { Inputs = (double[])replay.Inputs.Clone() };
        message = "";
        double[] times = replay.Inputs;
        if (replay.Axis == "distance")
        {
            var distance = WorkshopService.Replay(replay, parsed, flight, includeTrajectory: false, cancellation: token);
            if (distance.Inputs.Any(input => !input.Time.HasValue))
            {
                message = "Нельзя закрепить время команд, до которых исходный забег не дошёл.";
                return null;
            }
            times = distance.Inputs.Select(input => input.Time.Value).ToArray();
            var timed = replay with { Axis = "time", Inputs = times };
            if (times.Zip(times.Skip(1)).Any(pair => pair.First >= pair.Second) ||
                !Equivalent(distance, WorkshopService.Replay(timed, parsed, flight, includeTrajectory: false, cancellation: token)))
            {
                message = "Временная лента не воспроизвела исходную ленту по дистанции.";
                return null;
            }
        }
        return new(replay with { Axis = "time", Inputs = times }, parsed, flight);
    }

    static bool Equivalent(ReplayResult distance, ReplayResult timed)
    {
        var a = distance.Summary;
        var b = timed.Summary;
        return a.Status == b.Status && Close(a.FinishTime, b.FinishTime, TimeTolerance) &&
            a.Collisions == b.Collisions && a.Boosts == b.Boosts && a.InstantBoosts == b.InstantBoosts &&
            a.Slows == b.Slows && a.Coins == b.Coins && a.FinishBoosts == b.FinishBoosts &&
            a.HitIds.SequenceEqual(b.HitIds) && a.CollectedIds.SequenceEqual(b.CollectedIds) &&
            a.SuccessfulIds.SequenceEqual(b.SuccessfulIds) && a.FailedIds.SequenceEqual(b.FailedIds) &&
            distance.Inputs.Zip(timed.Inputs).All(pair => pair.First.Status == pair.Second.Status &&
                Close(pair.First.Time, pair.Second.Time, TimeTolerance) && Close(pair.First.X, pair.Second.X, PositionTolerance) &&
                Close(pair.First.Y, pair.Second.Y, PositionTolerance));
    }

    static bool Close(double? a, double? b, double epsilon) =>
        a.HasValue == b.HasValue && (!a.HasValue || Math.Abs(a.Value - b.Value) <= epsilon);

    static bool Computed(InputToleranceProbe probe) => probe?.Status is "safe" or "collision" or "rejected";

    static InputToleranceProbe Unavailable(int? offset, string status, string message) =>
        new(offset, !offset.HasValue, status, message, null, null, null, null, 0, null, false);

    static InputTolerancePreview Evaluate(Prepared prepared, int? offset, bool capture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reference = prepared.Reference;
        var times = reference.Inputs;
        int index = prepared.Index;
        double? previous = index > 0 ? times[index - 1] : null;
        double? next = index + 1 < times.Length ? times[index + 1] : null;
        double? minimumInterval;
        Window window;
        if (offset.HasValue)
        {
            double proposed = times[index] + offset.Value / 1000d;
            if (proposed < -ScheduleTolerance)
                return new(Unavailable(offset, "invalid", "Сдвиг переносит нажатие до начала заезда."), []);
            proposed = Math.Max(0, proposed);
            if (offset != 0 && (proposed - previous <= ScheduleTolerance || next - proposed <= ScheduleTolerance))
                return new(Unavailable(offset, "invalid", "Сдвиг совпадает с соседней командой или меняет порядок команд."), []);
            minimumInterval = MinimumInterval((proposed - previous) * 1000, (next - proposed) * 1000);
            var shifted = (double[])times.Clone();
            shifted[index] = proposed;
            window = Shift(prepared.Source, shifted, index, new(reference.StartTime, reference.EndX), capture, token);
        }
        else
        {
            minimumInterval = MinimumInterval((next - previous) * 1000);
            var remaining = times.Where((_, i) => i != index).ToArray();
            window = Omit(prepared.Source, remaining, reference.InputTime, next, capture, token);
        }

        var probe = new InputToleranceProbe(offset, !offset.HasValue, window.Status, window.Message, window.InputTime,
            window.StartTime, window.End.Time, window.End.X, window.Status == "collision" ? 1 : 0,
            minimumInterval, minimumInterval == null || minimumInterval >= BotMinimumMs);
        return new(probe, window.Samples);
    }

    static double? MinimumInterval(double? previous, double? next = null)
    {
        double? value = previous.HasValue && next.HasValue ? Math.Min(previous.Value, next.Value) : previous ?? next;
        return !value.HasValue || !double.IsFinite(value.Value) ? null : Math.Round(value.Value, 6);
    }

    internal sealed record Failure(string Status, string Message, Sample At);
    internal sealed record Window(string Status, string Message, double? InputTime, double StartTime, Sample End,
        Sample[] Samples, double? SecondInputTime = null);
    internal sealed record FlightTarget(double StartTime, double EndX);
    internal sealed record Prefix(GameState Game, int Next, List<Failure> Failures);

    internal static Prefix BuildPrefix(Source source, double earliestChange, CancellationToken token)
    {
        var run = new Timeline(source, source.Replay.Inputs, capture: false, token);
        var failures = new List<Failure>();
        // Reuse a normal scheduler boundary. Inserting a synthetic advance to
        // the earliest branch time could alter the subsequent replay's slices.
        while (run.Racing && run.Game.Elapsed + WorkshopService.SampleStep < earliestChange - TimeTolerance)
            run.StepAndObserve(failures);
        return new(run.Game, run.Next, failures);
    }

    internal static Window Shift(Source source, double[] times, int index, FlightTarget reference, bool capture,
        CancellationToken token, int? secondIndex = null, Prefix prefix = null)
    {
        var run = new Timeline(source, times, capture, token, prefix);
        var failures = prefix == null ? new List<Failure>() : new List<Failure>(prefix.Failures);
        double? inputTime = null;
        double? secondTime = null;
        FlightMeasurement flightBeforeFirst = null;
        double start = reference?.StartTime ?? times[index];
        bool requireSameFlight = reference == null && secondIndex.HasValue;
        bool landed = false;
        while (run.Racing)
        {
            var previousFlight = run.Player.LastFlight;
            double? activeStart = run.Player.FlightActive ? run.Game.Elapsed - run.Player.FlightDuration : null;
            // Recovery closes the measurement at impact, but it can leave the
            // same airborne pose until real support. A press then still belongs
            // to the flight that hit, even if it changes direction after recovery.
            if (!activeStart.HasValue && run.Player.Flipping && previousFlight is { Interrupted: true })
                activeStart = Math.Max(0, previousFlight.EndedAt - previousFlight.Duration);
            double? lastPress = secondIndex.HasValue ? secondTime : inputTime;
            var before = lastPress.HasValue ? RaceSnapshot.Clone(run.Game) : null;
            var command = run.StepAndObserve(failures);
            if (command?.Index == index)
            {
                inputTime = command.Time.Value;
                start = Math.Min(activeStart ?? inputTime.Value, reference?.StartTime ?? inputTime.Value);
                flightBeforeFirst = previousFlight;
            }
            if (command != null && command.Index == secondIndex)
            {
                secondTime = command.Time.Value;
                if (requireSameFlight && command.Status == "accepted" &&
                    (!activeStart.HasValue || previousFlight != flightBeforeFirst || secondTime <= inputTime))
                    return run.Result("rejected", "Выбранные команды должны быть приняты в одном непрерывном полёте.",
                        inputTime, start, run.Current, secondTime);
            }
            if (!inputTime.HasValue) continue;

            Sample end = null;
            var flight = run.Player.LastFlight;
            // Solo's terminal event also ends the flight clock. An airborne
            // finish has no contact and leaves the player's flight pose intact.
            if (lastPress.HasValue && flight != previousFlight && flight is { Interrupted: false } &&
                flight.EndedAt >= lastPress.Value - TimeTolerance &&
                !(run.Player.Flipping && flight.EndedAt == run.Player.FinishTime))
            {
                landed = true;
                // Core reports the exact contact time. Use its own motion at that
                // instant for X, including reserve changes and swept rewards.
                var contact = AtTime(before, flight.EndedAt);
                if (reference == null || contact.X >= reference.EndX - PositionTolerance) end = contact;
            }
            if (end == null && landed && reference != null && run.Player.Distance >= reference.EndX - PositionTolerance &&
                (before.Players[0].Distance < reference.EndX || LandingIntervals.IsSupported(before.Players[0])))
            {
                var crossing = AtDistance(before, run.Game, reference.EndX, token);
                if (!crossing.Flipping && crossing.Recovering == 0 && crossing.Support is "Wall" or "Stone") end = crossing;
            }

            var failure = FirstFailure(failures, start, end?.Time);
            if (failure != null) return run.Result(failure.Status, failure.Message, inputTime, start, failure.At, secondTime);
            if (end != null) return run.Result("safe", "", inputTime, start, end, secondTime);
        }

        var priorFailure = FirstFailure(failures, start, null);
        if (priorFailure != null) return run.Result(priorFailure.Status, priorFailure.Message, inputTime, start, priorFailure.At, secondTime);
        string message = inputTime == null || (secondIndex.HasValue && secondTime == null) ? "Заезд закончился до исполнения выбранной команды."
            : landed ? "Не достигнута позиция окончания исходного манёвра."
            : "До окончания заезда не было настоящей опоры после нажатия.";
        return run.Result("incomplete", message, inputTime, Math.Min(start, run.Current.Time), run.Current, secondTime);
    }

    static Failure FirstFailure(List<Failure> failures, double start, double? end) => failures.FirstOrDefault(failure =>
        failure.At.Time >= start - TimeTolerance && (end == null || failure.At.Time <= end.Value + TimeTolerance));

    static Window Omit(Source source, double[] times, double start, double? next, bool capture, CancellationToken token)
    {
        var run = new Timeline(source, times, capture, token);
        while (run.Racing && (next == null || run.Game.Elapsed < next.Value))
        {
            int collisions = run.Player.Collisions;
            run.Step(next);
            if (run.Player.Collisions != collisions && run.Player.LastHit.At >= start - TimeTolerance)
            {
                var impact = run.Impact();
                return run.Result(impact.Status, impact.Message, null, start, impact.At);
            }
        }
        if (run.Current.Time < start - TimeTolerance)
            return run.Result("incomplete", "Заезд закончился до времени пропущенной команды.", null, run.Current.Time, run.Current);
        bool reachedNext = next.HasValue && run.Game.Elapsed >= next.Value - TimeTolerance;
        if (next.HasValue && !reachedNext && run.Player.FinishTime.HasValue)
            return run.Result("incomplete", "Заезд закончился до времени следующей команды.", null, start, run.Current);
        if (!reachedNext && !run.Player.FinishTime.HasValue)
            return run.Result("incomplete", "Достигнут лимит времени заезда без удара.", null, start, run.Current);
        string message = reachedNext ? "Без удара до следующей команды" : "Без удара до конца заезда";
        return run.Result("safe", message, null, start, run.Current);
    }

    static Sample AtTime(GameState before, double time)
    {
        RaceSimulation.Advance(before, time - before.Elapsed);
        return TrialObservation.Capture(before) with { Time = time };
    }

    static Sample AtDistance(GameState before, GameState after, double x, CancellationToken token)
    {
        if (after.Players[0].Distance < x) return TrialObservation.Capture(after);
        double lo = 0, hi = after.Elapsed - before.Elapsed;
        for (int i = 0; i < 28; i++)
        {
            token.ThrowIfCancellationRequested();
            double mid = (lo + hi) / 2;
            var test = RaceSnapshot.Clone(before);
            RaceSimulation.Advance(test, mid);
            if (test.Players[0].Distance < x) lo = mid;
            else hi = mid;
        }
        return AtTime(before, before.Elapsed + hi);
    }

    sealed class Timeline
    {
        readonly double[] times;
        readonly CancellationToken token;
        readonly List<Sample> samples;
        int next;
        public GameState Game { get; }
        public PlayerState Player => Game.Players[0];
        public bool Racing => WorkshopService.IsRacing(Game);
        public Sample Current => TrialObservation.Capture(Game);
        public int Next => next;

        public Timeline(Source source, double[] times, bool capture, CancellationToken token, Prefix prefix = null)
        {
            this.times = times;
            this.token = token;
            Game = prefix == null ? source.Start() : RaceSnapshot.Clone(prefix.Game);
            next = prefix?.Next ?? 0;
            if (capture) samples = [Current];
        }

        public InputResult Step(double? stopBeforeTime = null)
        {
            var command = WorkshopService.ReplayStep(Game, "time", times, ref next, cancellationToken: token,
                stopBeforeTime: stopBeforeTime);
            if (samples != null)
            {
                var point = Current;
                if (Math.Abs(samples[^1].Time - point.Time) < 1e-12) samples[^1] = point;
                else samples.Add(point);
            }
            return command;
        }

        public Failure Impact() => new("collision", $"Столкновение: {Player.LastHit.Id}.",
            Current with { Time = Player.LastHit.At });

        public InputResult StepAndObserve(List<Failure> failures)
        {
            int previousCollisions = Player.Collisions;
            var command = Step();
            if (Player.Collisions != previousCollisions) failures.Add(Impact());
            if (command?.Status == "ignored") failures.Add(new("rejected", command.Reason, Current));
            return command;
        }

        public Window Result(string status, string message, double? inputTime, double start, Sample end,
            double? secondInputTime = null)
        {
            if (samples == null) return new(status, message, inputTime, start, end, [], secondInputTime);
            var segment = samples.TakeWhile(point => point.Time < end.Time - ScheduleTolerance).Append(end).ToArray();
            return new(status, message, inputTime, start, end, segment, secondInputTime);
        }
    }
}
