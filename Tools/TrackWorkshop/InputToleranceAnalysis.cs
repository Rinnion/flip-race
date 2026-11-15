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
    const double TimeTolerance = 1e-7, PositionTolerance = 1e-5, ScheduleTolerance = 1e-10;
    static readonly TimeSpan WorkBudget = TimeSpan.FromSeconds(10);
    const string BudgetMessage = "Истёк лимит времени проверки.";

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

    static CancellationTokenSource Deadline(CancellationToken cancellation, TimeSpan? budget)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var duration = budget ?? WorkBudget;
        if (duration == TimeSpan.Zero) deadline.Cancel();
        else deadline.CancelAfter(duration);
        return deadline;
    }

    sealed record Source(ReplayRequest Replay, WorkshopService.Preview Track, FlightSettings Flight)
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
        token.ThrowIfCancellationRequested();
        var parsed = WorkshopService.Read(request.Json);
        var flight = WorkshopService.GetFlight(parsed.Package, request.Profile);
        var replay = new ReplayRequest(request.Json, request.Profile, request.WarningDuration, request.Axis,
            (double[])request.Inputs.Clone(), request.SurfaceRunSettings);
        double[] times = replay.Inputs;
        if (replay.Axis == "distance")
        {
            var distance = WorkshopService.Replay(replay, parsed, flight, includeTrajectory: false, cancellation: token);
            if (distance.Inputs.Any(input => !input.Time.HasValue))
                return new(null, request.InputIndex, null, "Нельзя закрепить время команд, до которых исходный забег не дошёл.");
            times = distance.Inputs.Select(input => input.Time.Value).ToArray();
            var timed = replay with { Axis = "time", Inputs = times };
            if (times.Zip(times.Skip(1)).Any(pair => pair.First >= pair.Second) ||
                !Equivalent(distance, WorkshopService.Replay(timed, parsed, flight, includeTrajectory: false, cancellation: token)))
                return new(null, request.InputIndex, null, "Временная лента не воспроизвела исходную ленту по дистанции.");
        }

        var source = new Source(replay with { Axis = "time", Inputs = times }, parsed, flight);
        var reference = Shift(source, times, request.InputIndex, null, capture: false, token);
        if (reference.Status != "safe")
            return new(source, request.InputIndex, null, $"Исходный манёвр: {reference.Message}");
        var baseline = new InputToleranceBaseline(times, reference.InputTime.Value, reference.StartTime,
            reference.End.Time, reference.End.X);
        return new(source, request.InputIndex, baseline, "");
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
            window = Shift(prepared.Source, shifted, index, reference, capture, token);
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

    sealed record Failure(string Status, string Message, Sample At);
    sealed record Window(string Status, string Message, double? InputTime, double StartTime, Sample End, Sample[] Samples);

    static Window Shift(Source source, double[] times, int index, InputToleranceBaseline reference, bool capture,
        CancellationToken token)
    {
        var run = new Timeline(source, times, capture, token);
        var failures = new List<Failure>();
        double? inputTime = null;
        double start = reference?.StartTime ?? times[index];
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
            var before = inputTime.HasValue ? RaceSnapshot.Clone(run.Game) : null;
            int previousCollisions = run.Player.Collisions;
            var command = run.Step();
            if (run.Player.Collisions != previousCollisions) failures.Add(run.Impact());
            if (command?.Status == "ignored") failures.Add(new("rejected", command.Reason, run.Current));
            if (command?.Index == index)
            {
                inputTime = command.Time.Value;
                start = Math.Min(activeStart ?? inputTime.Value, reference?.StartTime ?? inputTime.Value);
            }
            if (!inputTime.HasValue) continue;

            Sample end = null;
            var flight = run.Player.LastFlight;
            // Solo's terminal event also ends the flight clock. An airborne
            // finish has no contact and leaves the player's flight pose intact.
            if (flight != previousFlight && flight is { Interrupted: false } &&
                flight.EndedAt >= inputTime.Value - TimeTolerance &&
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
            if (failure != null) return run.Result(failure.Status, failure.Message, inputTime, start, failure.At);
            if (end != null) return run.Result("safe", "", inputTime, start, end);
        }

        var priorFailure = FirstFailure(failures, start, null);
        if (priorFailure != null) return run.Result(priorFailure.Status, priorFailure.Message, inputTime, start, priorFailure.At);
        string message = inputTime == null ? "Заезд закончился до исполнения выбранной команды."
            : landed ? "Не достигнута позиция окончания исходного манёвра."
            : "До окончания заезда не было настоящей опоры после нажатия.";
        return run.Result("incomplete", message, inputTime, Math.Min(start, run.Current.Time), run.Current);
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

        public Timeline(Source source, double[] times, bool capture, CancellationToken token)
        {
            this.times = times;
            this.token = token;
            Game = source.Start();
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

        public Window Result(string status, string message, double? inputTime, double start, Sample end)
        {
            if (samples == null) return new(status, message, inputTime, start, end, []);
            var segment = samples.TakeWhile(point => point.Time < end.Time - ScheduleTolerance).Append(end).ToArray();
            return new(status, message, inputTime, start, end, segment);
        }
    }
}
