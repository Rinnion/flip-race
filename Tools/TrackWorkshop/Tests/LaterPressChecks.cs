using FlipRace.Core;
using TrackWorkshop;

internal static class LaterPressChecks
{
    internal static object Run(WorkshopService service, Action<bool, string> check)
    {
        string empty = Source(850, []);
        double returnTime = 2 * FlightPresets.Chosen.JumpImpulse / FlightPresets.Chosen.GravityAcceleration;
        var original = new LaterPressRequest(empty, "Chosen", .8, "time", [0, returnTime + .05], 1);
        var all = LaterPressAnalysis.Find(original, default);
        check(all is { Status: "ready", Complete: true, Attempted: 21, CandidateCount: 21, StepMs: 50, SpanMs: 1000 } &&
            all.Alternatives.Select(item => item.DelayMs).SequenceEqual(Enumerable.Range(0, 21).Select(i => 200 + 50*i)),
            "empty track finds all 21 later presses from the default 200 ms threshold");
        VerifyAlternatives(original, all);

        var atContact = original with { Inputs = [0, returnTime + .001] };
        var recoveredBoundary = LaterPressAnalysis.Find(atContact, default);
        var shortStep = service.Replay(new(empty, "Chosen", .8, "time", atContact.Inputs)).LandingIntervals.Single();
        var delayedStep = service.Replay(new(empty, "Chosen", .8, "time", recoveredBoundary.Alternatives[0].Inputs)).LandingIntervals.Single();
        check(recoveredBoundary is { Status: "ready", Attempted: 21 } && recoveredBoundary.Alternatives.Length == 21 &&
            Math.Abs(shortStep.LandingX - delayedStep.LandingX) > 1e-5 &&
            Math.Abs(shortStep.LandingTime - delayedStep.LandingTime) <= 1e-7,
            "an original press one millisecond after contact can change the sampled X without changing the landing");

        var distance = original with { Axis = "distance", Inputs = original.Inputs.Select(time => time * RaceConfig.Speed).ToArray() };
        var normalized = LaterPressAnalysis.Find(distance, default);
        check(normalized is { Status: "ready", Complete: true } && normalized.Alternatives.Length == all.Alternatives.Length &&
            normalized.Baseline.Inputs.Zip(original.Inputs).All(pair => Math.Abs(pair.First - pair.Second) <= 1e-7),
            "distance tape is converted to equivalent actual times before perturbing one command");
        VerifyAlternatives(distance, normalized);

        var linear = new WorkshopSurfaceRunSettings(true, 5, "Linear", 40, 200, true);
        var accelerated = original with { Profile = "FastFlip", WarningDuration = .6,
            SurfaceRunSettings = linear, Inputs = [0, .01, .3] , InputIndex = 2, MinimumMs = 250 };
        var acceleratedReport = LaterPressAnalysis.Find(accelerated, default);
        check(acceleratedReport.Status == "ready" && acceleratedReport.Alternatives is [{ DelayMs: 250 }, ..],
            "FastFlip with acceleration honors the requested minimum and preserves settings");
        VerifyAlternatives(accelerated, acceleratedReport);

        var withSuffix = original with { Inputs = [0, returnTime + .05, returnTime + .4] };
        var clipped = LaterPressAnalysis.Find(withSuffix, default);
        check(clipped is { CandidateCount: 4, Attempted: 4, Complete: true } && clipped.Alternatives.Length > 0,
            "candidate grid is clipped strictly before the next unchanged press");
        VerifyAlternatives(withSuffix, clipped);
        var none = LaterPressAnalysis.Find(withSuffix with { MinimumMs = 400 }, default);
        check(none is { Status: "ready", CandidateCount: 0, Attempted: 0, Complete: true, Alternatives: [] },
            "a next command at the minimum delay leaves no eligible candidate");

        var stone = new Obstacle { Id = "stone", Kind = "floor", X = 160, Width = 100, Height = 48 };
        var stoneReward = new RiskZone { Id = "stone-mark", Kind = "landing", ObstacleId = "stone", X = 205, Y = 192, LandingWidth = 76 };
        var stoneRequest = original with { Json = Source(850, [stone], [stoneReward]), Inputs = [0, .95], MinimumMs = 100 };
        var walkedOff = LaterPressAnalysis.Find(stoneRequest, default);
        var stoneBaseline = service.Replay(new(stoneRequest.Json, "Chosen", .8, "time", stoneRequest.Inputs));
        check(walkedOff is { Status: "ready", CandidateCount: 21, Attempted: 21, Complete: true } &&
            walkedOff.Alternatives.Length is > 0 and < 21 && walkedOff.Alternatives.All(item => item.InputTime < 1.22),
            $"alternatives stop at the end of the original stone support: {walkedOff.Status}, {walkedOff.Message}; " +
            $"landing {walkedOff.Baseline?.LandingTime:R}, {string.Join(",", walkedOff.Alternatives.Select(item => item.InputTime))}; " +
            $"finish {stoneBaseline.Summary.FinishTime}, length range {stoneBaseline.Samples[^1].X}, commands {string.Join(",",stoneBaseline.Inputs.Select(input => input.Status))}");
        VerifyAlternatives(stoneRequest, walkedOff);

        // The original jump has landed by this low ceiling. Some delayed
        // jumps hit it, while a sufficiently late jump starts beyond it.
        var ceiling = new Obstacle { Id = "ceiling", Kind = "ceiling", X = 499, Width = 24, Height = 155 };
        var collisionRequest = original with { Json = Source(850, [ceiling]) };
        var gap = LaterPressAnalysis.Find(collisionRequest, default);
        var hitTimes = (double[])collisionRequest.Inputs.Clone();
        hitTimes[1] = gap.Baseline.LandingTime + .55;
        var addedHit = service.Replay(new(collisionRequest.Json, "Chosen", .8, "time", hitTimes));
        check(gap is { Attempted: 21, Complete: true } && gap.Baseline.Collisions == 0 &&
            addedHit.Summary.FinishTime != null && addedHit.Summary.HitIds.Contains("ceiling") &&
            gap.Alternatives.All(item => item.DelayMs != 550) &&
            gap.Alternatives.Any(item => item.DelayMs < 550) && gap.Alternatives.Any(item => item.DelayMs > 550),
            $"a finished candidate with a new hit is excluded and later candidates are still evaluated: " +
            $"baseline {gap.Baseline.Collisions}, delayed {addedHit.Summary.Collisions}, successes " +
            string.Join(",",gap.Alternatives.Select(item => item.DelayMs)));
        VerifyAlternatives(collisionRequest, gap);

        var baseline = service.Replay(new(empty, "Chosen", .8, "time", original.Inputs)).Summary;
        check(!LaterPressAnalysis.PreservesOutcome(baseline, baseline with { Collisions = 1, HitIds = ["stone"] }) &&
            !LaterPressAnalysis.PreservesOutcome(baseline, baseline with { Slows = 1 }) &&
            !LaterPressAnalysis.PreservesOutcome(baseline, baseline with { FailedIds = ["missed"] }) &&
            !LaterPressAnalysis.PreservesOutcome(baseline with { SuccessfulIds = ["coin"], Coins = 1 }, baseline) &&
            !LaterPressAnalysis.PreservesOutcome(baseline with { InstantBoosts = 1 }, baseline) &&
            LaterPressAnalysis.PreservesOutcome(baseline with { CollectedIds = ["trap"], Slows = 1 }, baseline),
            "candidate outcome rejects lost rewards and new harms while allowing an avoided trap");

        check(LaterPressAnalysis.Find(original with { Inputs = [0, returnTime + .05, 99] }, default).Status == "unsupported" &&
            LaterPressAnalysis.Find(original with { InputIndex = 0 }, default).Status == "unsupported" &&
            LaterPressAnalysis.Find(original with { Profile = "FastFlip", Inputs = [0, .01, .02, .3], InputIndex = 3 }, default).Status == "unsupported",
            "unreached or rejected input and an unmarked source press are ineligible");
        Reject(original with { MinimumMs = 2001 });
        Reject(original with { InputIndex = -1 });
        Reject(original with { Axis = "bad" });

        var overdue = LaterPressAnalysis.Find(original, default, TimeSpan.Zero);
        check(overdue is { Complete: false, StopReason: "time-budget", Attempted: 0, Alternatives: [] },
            "expired work budget never reports that untested alternatives failed");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool canceled = false;
        try { LaterPressAnalysis.Find(original, cancelled.Token); }
        catch (OperationCanceledException) { canceled = true; }
        check(canceled, "request cancellation propagates to caller");

        return new { defaultCandidates = all.Attempted, distanceCandidates = normalized.Attempted,
            fastWithAcceleration = acceleratedReport.Alternatives.Length, nextPressCandidates = clipped.Attempted,
            sameStoneSupport = walkedOff.Alternatives.Length, rejectedStoneCandidates = walkedOff.Attempted - walkedOff.Alternatives.Length,
            collisionGap = gap.Alternatives.Select(item => item.DelayMs).ToArray(),
            budgetStop = overdue.StopReason };

        void Reject(LaterPressRequest request)
        {
            bool rejected = false;
            try { LaterPressAnalysis.Find(request, default); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "malformed search request is rejected");
        }

        void VerifyAlternatives(LaterPressRequest request, LaterPressResult report)
        {
            foreach (var alternative in report.Alternatives)
            {
                var replay = service.Replay(new(request.Json, request.Profile, request.WarningDuration, "time", alternative.Inputs,
                    request.SurfaceRunSettings));
                var landing = replay.LandingIntervals.Single(item => item.InputIndex == request.InputIndex);
                check(alternative.Inputs.Length == request.Inputs.Length && alternative.Inputs.Zip(report.Baseline.Inputs)
                        .Where((_, index) => index != request.InputIndex).All(pair => pair.First == pair.Second) &&
                    landing.InputTime > report.Baseline.InputTime && landing.InputTime == alternative.InputTime &&
                    Math.Abs(landing.LandingTime - report.Baseline.LandingTime) < 1e-7 &&
                    Math.Abs((landing.InputTime - landing.LandingTime)*1000 - alternative.DelayMs) < 1e-4 &&
                    replay.Summary.FinishTime == alternative.FinishTime && replay.Summary.Collisions == alternative.Collisions &&
                    replay.Summary.AcceptedInputs == request.Inputs.Length && replay.Samples.Count > 0,
                    "successful alternative replays its complete trajectory with every other input time fixed");
            }
        }
    }

    static string Source(double length, Obstacle[] obstacles, RiskZone[] rewards = null) => TrackJson.Write(new TrackPackage(new Track(length, obstacles)
        { Id = "later-press-test", Name = "Later press test", RiskZones = rewards ?? [] }));
}
