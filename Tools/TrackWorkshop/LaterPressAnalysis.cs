namespace TrackWorkshop;

public sealed record LaterPressRequest(string Json, string Profile, double WarningDuration, string Axis, double[] Inputs,
    int InputIndex, int MinimumMs = 200, WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record LaterPressBaseline(double[] Inputs, double FinishTime, double LandingTime, double InputTime,
    int InputIndex, int Collisions);
public sealed record LaterPressAlternative(int DelayMs, double InputTime, double FinishTime, double FinishDelta,
    int Collisions, double[] Inputs, Summary Summary);
public sealed record LaterPressResult(string Status, string Message, int Attempted, int CandidateCount, bool Complete,
    string StopReason, int StepMs, int SpanMs, LaterPressBaseline Baseline, LaterPressAlternative[] Alternatives);

internal static class LaterPressAnalysis
{
    internal const string Path = "/api/later-press";
    const int MaximumThresholdMs = 2000, StepMs = 50, SpanMs = 1000;
    const double TimeTolerance = 1e-7, PositionTolerance = 1e-5, MinimumScheduleGap = 1e-6;
    static readonly TimeSpan WorkBudget = TimeSpan.FromSeconds(10);

    internal static LaterPressResult Find(LaterPressRequest request, CancellationToken cancellation, TimeSpan? budget = null)
    {
        if (request == null) throw new ArgumentException("Пустой запрос.");
        cancellation.ThrowIfCancellationRequested();
        WorkshopService.ValidateAxis(request.Axis);
        WorkshopService.ValidateWarning(request.WarningDuration);
        WorkshopService.ValidateInputs(request.Inputs);
        if (request.InputIndex < 0 || request.InputIndex >= request.Inputs.Length)
            throw new ArgumentException("Укажите номер исходного нажатия.");
        if (request.MinimumMs is < 0 or > MaximumThresholdMs)
            throw new ArgumentException("Минимальный интервал должен быть целым числом от 0 до 2000 мс.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var duration = budget ?? WorkBudget;
        if (duration == TimeSpan.Zero) deadline.Cancel();
        else deadline.CancelAfter(duration);
        var token = deadline.Token;
        LaterPressBaseline baseline = null;
        var alternatives = new List<LaterPressAlternative>();
        int attempted = 0, candidateCount = 0;
        LaterPressResult Result(string status = "ready", string message = null, bool complete = true,
            string stopReason = "completed") => new(status, message, attempted, candidateCount, complete, stopReason,
            StepMs, SpanMs, baseline, alternatives.ToArray());
        try
        {
            token.ThrowIfCancellationRequested();
            var parsed = WorkshopService.Read(request.Json);
            var flight = WorkshopService.GetFlight(parsed.Package, request.Profile);
            var original = new ReplayRequest(request.Json, request.Profile, request.WarningDuration, request.Axis,
                request.Inputs, request.SurfaceRunSettings);
            ReplayResult Replay(ReplayRequest replay) => WorkshopService.Replay(replay, parsed, flight,
                includeTrajectory: false, cancellation: token);

            var reference = Replay(original);
            if (!FinishedWithEveryInput(reference))
                return Result("unsupported", "Исходный забег должен завершиться с принятием каждого нажатия.");
            var landing = reference.LandingIntervals.SingleOrDefault(item => item.InputIndex == request.InputIndex);
            if (landing == null || Math.Round((landing.InputTime - landing.LandingTime) * 1000, 7) >= request.MinimumMs)
                return Result("unsupported", "Выберите отмеченный короткий интервал после посадки.");

            // Keep existing time coordinates. Distance input uses the exact
            // scheduler timestamp for every accepted press, once reproduced.
            var times = request.Axis == "time" ? request.Inputs : reference.Inputs.Select(input => input.Time.Value).ToArray();
            var timed = original with { Axis = "time", Inputs = times };
            if (request.Axis == "distance")
            {
                if (times.Zip(times.Skip(1)).Any(pair => pair.First >= pair.Second) || !Equivalent(reference, Replay(timed)))
                    return Result("unsupported", "Временная лента не воспроизвела исходный забег по дистанции.");
            }

            double finishTime = reference.Summary.FinishTime.Value;
            baseline = new(times, finishTime, landing.LandingTime, landing.InputTime, landing.InputIndex, reference.Summary.Collisions);
            double next = landing.InputIndex + 1 < times.Length ? times[landing.InputIndex + 1] : WorkshopService.MaximumTime;
            var candidates = new List<(int Delay, double Time)>();
            for (int offset = 0; offset <= SpanMs; offset += StepMs)
            {
                int delay = request.MinimumMs + offset;
                double time = landing.LandingTime + delay / 1000d;
                if (time <= landing.InputTime || time >= next - MinimumScheduleGap) continue;
                candidates.Add((delay, time));
            }
            candidateCount = candidates.Count;

            foreach (var candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                var inputs = (double[])times.Clone();
                inputs[landing.InputIndex] = candidate.Time;
                var replay = Replay(timed with { Inputs = inputs });
                attempted++;
                var newLanding = replay.LandingIntervals.SingleOrDefault(item => item.InputIndex == landing.InputIndex);
                var summary = replay.Summary;
                if (!FinishedWithEveryInput(replay) || !PreservesOutcome(reference.Summary, summary) ||
                    !SameLanding(landing, newLanding) ||
                    newLanding.InputTime - newLanding.LandingTime < request.MinimumMs / 1000d - TimeTolerance) continue;
                double newFinish = summary.FinishTime.Value;
                alternatives.Add(new(candidate.Delay, newLanding.InputTime, newFinish, newFinish - finishTime,
                    summary.Collisions, inputs, summary));
            }
            cancellation.ThrowIfCancellationRequested();
            return Result();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return Result(baseline == null ? "unsupported" : "ready", "Истёк лимит времени проверки. Показаны завершённые пробы.",
                complete: false, stopReason: "time-budget");
        }
    }

    static bool FinishedWithEveryInput(ReplayResult replay) => replay.Summary.FinishTime != null &&
        replay.Summary.IgnoredInputs == 0 && replay.Summary.UnreachedInputs == 0;

    static bool Close(double? a, double? b, double tolerance) =>
        !a.HasValue ? !b.HasValue : b.HasValue && Math.Abs(a.Value - b.Value) <= tolerance;

    static bool SameLanding(LandingInterval a, LandingInterval b) => b != null && a.InputIndex == b.InputIndex &&
        Close(a.LandingTime, b.LandingTime, TimeTolerance);

    static bool Equivalent(ReplayResult a, ReplayResult b) => FinishedWithEveryInput(b) &&
        Close(a.Summary.FinishTime, b.Summary.FinishTime, TimeTolerance) &&
        PreservesOutcome(a.Summary, b.Summary) && PreservesOutcome(b.Summary, a.Summary) &&
        SameIds(a.Summary.CollectedIds, b.Summary.CollectedIds) &&
        a.Inputs.Count == b.Inputs.Count && a.Inputs.Zip(b.Inputs).All(pair =>
            pair.First.Index == pair.Second.Index && pair.First.Status == pair.Second.Status &&
            Close(pair.First.Time, pair.Second.Time, TimeTolerance) && Close(pair.First.X, pair.Second.X, PositionTolerance) &&
            Close(pair.First.Y, pair.Second.Y, PositionTolerance)) &&
        a.LandingIntervals.Count == b.LandingIntervals.Count && a.LandingIntervals.Zip(b.LandingIntervals).All(pair =>
            SameLanding(pair.First, pair.Second) && Close(pair.First.InputTime, pair.Second.InputTime, TimeTolerance) &&
            Close(pair.First.LandingX, pair.Second.LandingX, PositionTolerance) &&
            Close(pair.First.LandingY, pair.Second.LandingY, PositionTolerance) &&
            Close(pair.First.InputX, pair.Second.InputX, PositionTolerance) && Close(pair.First.InputY, pair.Second.InputY, PositionTolerance));

    static bool SameIds(string[] a, string[] b) => a.ToHashSet(StringComparer.Ordinal).SetEquals(b);
    static bool ContainsIds(string[] all, string[] subset) => all.ToHashSet(StringComparer.Ordinal).IsSupersetOf(subset);

    internal static bool PreservesOutcome(Summary reference, Summary candidate) =>
        candidate.Collisions <= reference.Collisions && candidate.Boosts >= reference.Boosts &&
        candidate.InstantBoosts >= reference.InstantBoosts && candidate.Coins >= reference.Coins &&
        candidate.FinishBoosts >= reference.FinishBoosts && candidate.Slows <= reference.Slows &&
        ContainsIds(reference.HitIds, candidate.HitIds) && ContainsIds(reference.FailedIds, candidate.FailedIds) &&
        ContainsIds(candidate.SuccessfulIds, reference.SuccessfulIds);
}
