using FlipRace.Core;
using TrackWorkshop;

internal static class InputToleranceChecks
{
    internal static object Run(WorkshopService service, Action<bool, string> check)
    {
        const double press = .4;
        double duration = 2 * FlightPresets.Chosen.JumpImpulse / FlightPresets.Chosen.GravityAcceleration;
        string empty = Source(850, []);
        double[] originalTape = [press];
        var original = new InputToleranceRequest(empty, "Chosen", .8, "time", originalTape, 0);
        var clean = Find(original);
        check(clean is { Status: "ready", Complete: true, Attempted: 14, StepMs: 50, SpanMs: 300 } &&
            clean.Probes.Select(probe => probe.OffsetMs).SequenceEqual(Enumerable.Range(0, 13).Select(i => (int?)(50 * i - 300))) &&
            clean.Probes.All(probe => probe.Status == "safe" && probe.Collisions == 0), "all 13 shifts and last omission on a flat track");
        Near(clean.Baseline.StartTime, press, "standard jump starts at input time");
        Near(clean.Baseline.EndTime, press + duration, "standard jump uses the Core's real landing time");
        Near(clean.Baseline.EndX, (press + duration) * RaceConfig.Speed, "standard jump uses position at real landing");
        var negative = Preview(original, -300);
        Near(negative.Probe.EndTime.Value, clean.Baseline.EndTime, "early clean landing checks remaining distance on support");
        Near(negative.Probe.EndX.Value, clean.Baseline.EndX, "early clean landing checks through original X");
        var zero = Preview(original, 0);
        var sourceReplay = service.Replay(new(empty, "Chosen", .8, "time", originalTape));
        check(zero.Probe == Probe(clean, 0) && originalTape.SequenceEqual([press]) &&
            !ReferenceEquals(clean.Baseline.Inputs, originalTape) && clean.Baseline.Inputs.SequenceEqual(originalTape),
            "zero and preview agree and the source tape is immutable");
        Near(zero.Samples[^1].Time, clean.Baseline.EndTime, "zero preview ends at the original contact");
        check(zero.Samples.Length < sourceReplay.Samples.Count && zero.Samples[^1] is { Flipping: false, Support: "Wall" },
            $"zero preview stops at supported endpoint: {zero.Samples[^1]}");
        for (int i = 0; i < zero.Samples.Length - 1; i++)
        {
            Near(zero.Samples[i].X, sourceReplay.Samples[i].X, "zero reproduces source X");
            Near(zero.Samples[i].Y, sourceReplay.Samples[i].Y, "zero reproduces source Y");
        }

        var chosenChain = original with { Inputs = [.25, .5, .65], InputIndex = 1 };
        var chosen = Find(chosenChain);
        check(chosen is { Status: "ready" } && chosen.Baseline.EndTime > .65 && Probe(chosen, -100).Status == "safe",
            $"Chosen air reversals use the whole continuous flight: {Grid(chosen)}");
        Near(chosen.Baseline.StartTime, .25, "Chosen original air reversal starts at takeoff");
        Near(Probe(chosen, -100).StartTime.Value, .25, "Chosen changed air reversal starts at takeoff");
        var joinsEarlierFlight = Find(original with { Inputs = [0, 1.2], InputIndex = 1 });
        check(joinsEarlierFlight.Baseline.StartTime > 1 && Probe(joinsEarlierFlight, -250).Status == "safe",
            $"a moved press can join the preceding flight: {Grid(joinsEarlierFlight)}");
        Near(Probe(joinsEarlierFlight, -250).StartTime.Value, 0, "moved press includes the preceding takeoff");

        var fastChain = original with { Profile = "FastFlip", Inputs = [.2, .6, .72], InputIndex = 1 };
        var fast = Find(fastChain);
        check(fast is { Status: "ready" } && Probe(fast, 0).Status == "safe" &&
            Probe(fast, -150) is { Status: "rejected", Collisions: 0 } &&
            Probe(fast, 100) is { Status: "safe", MinimumIntervalMs: 20, BotCompatible: false },
            $"FastFlip rejects an unavailable frozen air repeat and keeps legal short gaps: {Grid(fast)}");
        var fastAir = Find(fastChain with { InputIndex = 2 });
        Near(fastAir.Baseline.StartTime, .6, "FastFlip air command uses original launch time");
        check(Find(fastChain with { Inputs = [.2, .45, .72], InputIndex = 2 }) is { Status: "unsupported", Baseline: null },
            "a rejected original action cannot provide a reference maneuver");
        check(Probe(Find(fastChain with { Inputs = [.2, .6, .7] }), 100).Status == "invalid" && Probe(fast, 150).Status == "invalid" &&
            Probe(Find(original with { Inputs = [.2] }), -250).Status == "invalid",
            "equal, crossed and negative scheduled times are invalid");

        var lowStone = new Obstacle { Id = "floor-after-landing", Kind = "floor", X = 275, Width = 16, Height = 15 };
        var floorRequest = original with { Json = Source(850, [lowStone]) };
        var floor = Find(floorRequest);
        var landedEarly = Preview(floorRequest, -300);
        check(floor.Status == "ready" && Probe(floor, 0).Status == "safe" && landedEarly.Probe.Status == "collision" &&
            landedEarly.Probe.EndTime > .1 + duration && landedEarly.Probe.EndX < floor.Baseline.EndX &&
            landedEarly.Samples.Any(point => point.Time > .1 + duration && point.Support == "Wall" && point.Collisions == 0),
            $"an early landing cannot hide a collision before the original maneuver ends: {Grid(floor)}");
        var originalImpact = Find(floorRequest with { Inputs = [.1] });
        check(originalImpact is { Status: "ready" } && originalImpact.Baseline.EndX < landedEarly.Probe.EndX,
            "baseline collision after real local endpoint does not invalidate local analysis");

        var ceiling = new Obstacle { Id = "ceiling-gap", Kind = "ceiling", X = 232, Width = 12, Height = 120 };
        var ceilingRequest = original with { Json = Source(850, [ceiling]) };
        var gap = Find(ceilingRequest);
        var hit = gap.Probes.FirstOrDefault(probe => probe.Status == "collision");
        check(gap.Status == "ready" && hit != null && gap.Probes.Any(probe => probe.OffsetMs < hit.OffsetMs && probe.Status == "safe") &&
            gap.Probes.Any(probe => probe.OffsetMs > hit.OffsetMs && probe.Status == "safe"),
            $"a collision splits otherwise safe timing points: {gap.Message} {Grid(gap)}");
        check(Find(ceilingRequest with { Inputs = [press + hit.OffsetMs.Value / 1000d] }) is { Status: "unsupported", Baseline: null },
            "a hit inside the original affected flight is diagnosed explicitly");
        var afterRecovery = ceilingRequest with { Json = Source(1250, [ceiling]), Inputs = [.55, 2.2, 3.5], InputIndex = 1 };
        var recovered = Find(afterRecovery);
        check(recovered is { Status: "ready" } && Probe(recovered, 0).Status == "safe" &&
            Probe(recovered, -300) is { Status: "collision", Collisions: 1 },
            $"a shift into the earlier interrupted flight includes its hit even after recovery: {recovered.Message} {Grid(recovered)}");

        var suffix = original with { Inputs = [press, 1.6] };
        var omitted = Preview(suffix, null);
        check(omitted.Probe == Find(suffix).Omission && omitted.Probe is
            { Status: "safe", Omit: true, InputTime: null, Message: "Без удара до следующей команды" } &&
            omitted.Samples[^1] is { Flipping: false, Support: "Wall" }, "omission stops before executing the following press");
        Near(omitted.Probe.EndTime.Value, 1.6, "omission includes motion up to the cutoff without executing next press");
        Near(clean.Omission.EndX.Value, 850, "last omission checks through finish");
        check(floor.Omission.Status == "collision", "last omission reports a collision");
        var nextUnreached = Find(original with { Json = Source(400, []), Inputs = [.5, 100] });
        check(nextUnreached is { Status: "ready", Omission: { Status: "incomplete", EndX: 400 } },
            "a non-last omission that ends the race before the next command is incomplete");
        var farNext = Find(original with { Inputs = [.5, 1e308] });
        check(Probe(farNext, 0) is { Status: "safe", MinimumIntervalMs: null, BotCompatible: true },
            "a finite time gap too large for milliseconds uses the nullable metric");
        var capped = Find(original with { Json = Source(1000000, []) });
        check(capped is { Status: "ready", Attempted: 13, Complete: true, Omission: { Status: "incomplete", EndTime: not null } },
            "last omission on an unfinished track reports the measured limit");
        Near(capped.Omission.EndTime.Value, WorkshopService.MaximumTime, "last omission respects the simulation time cap");

        var acceleration = new WorkshopSurfaceRunSettings(true, 5, "Linear", 40, 200, true);
        var accelerated = original with { SurfaceRunSettings = acceleration, Inputs = [press, press + duration + .001] };
        var accelerating = Find(accelerated);
        var frozenSuffix = Preview(accelerated, -300);
        check(accelerating.Baseline.EndX > clean.Baseline.EndX && frozenSuffix.Probe.Status == "safe" &&
            frozenSuffix.Probe.EndTime > accelerated.Inputs[1] + .5 && frozenSuffix.Samples[^1] is { Flipping: false, Support: "Wall" },
            $"a frozen suffix that restarts flight before the checkpoint requires the next real contact: {frozenSuffix.Probe}");
        CheckDistance(suffix);
        CheckDistance(accelerated);
        var unfinished = Find(original with { Json = Source(200, []), Inputs = [.5] });
        check(unfinished is { Status: "unsupported", Baseline: null }, "finish in air does not provide a reference landing");

        var budget = InputToleranceAnalysis.Find(original, default, TimeSpan.Zero);
        check(budget is { Complete: false, StopReason: "time-budget", Attempted: 0, Omission.Status: "incomplete" } &&
            budget.Probes.Length == 13 && budget.Probes.All(probe => probe.Status == "incomplete"),
            "work-budget exhaustion keeps every untested point explicitly incomplete");
        var budgetPreview = InputToleranceAnalysis.Preview(PreviewRequest(original, 0), default, TimeSpan.Zero);
        check(budgetPreview is { Probe.Status: "incomplete", Samples: [] }, "expired preview has no unmeasured trajectory");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Reject<OperationCanceledException>(() => InputToleranceAnalysis.Find(original, cancelled.Token));
        Reject<OperationCanceledException>(() => InputToleranceAnalysis.Preview(PreviewRequest(original, 0), cancelled.Token));
        Reject<ArgumentException>(() => InputToleranceAnalysis.Preview(PreviewRequest(original, 36), default));
        Reject<ArgumentException>(() => Find(original with { InputIndex = 1 }));
        return new { standard = clean.Attempted, chosenChain = chosen.Baseline, fast = Grid(fast), collisionGap = Grid(gap),
            earlyLandingCollision = landedEarly.Probe, omissionCutoff = omitted.Probe.EndTime, omissionLimit = capped.Omission,
            frozenSuffix = frozenSuffix.Probe, budget = budget.StopReason };

        void Near(double actual, double expected, string message) =>
            check(Math.Abs(actual - expected) < 1e-5, $"{message}: {actual:R} vs {expected:R}");

        void Reject<TException>(Action action) where TException : Exception
        {
            bool rejected = false;
            try { action(); }
            catch (TException) { rejected = true; }
            check(rejected, $"{typeof(TException).Name} propagates");
        }

        void CheckDistance(InputToleranceRequest timed)
        {
            var reference = Find(timed);
            var replay = service.Replay(new(timed.Json, timed.Profile, timed.WarningDuration, timed.Axis, timed.Inputs, timed.SurfaceRunSettings));
            var distance = Find(timed with { Axis = "distance", Inputs = replay.Inputs.Select(input => input.X.Value).ToArray() });
            check(distance is { Status: "ready" } && Grid(distance) == Grid(reference),
                $"distance tape reproduces time tape geometry and classifications: {distance.Message}");
            foreach (var pair in distance.Baseline.Inputs.Zip(reference.Baseline.Inputs)) Near(pair.First, pair.Second, "frozen input timestamps match");
            Near(distance.Baseline.EndX, reference.Baseline.EndX, "distance landing position matches");
        }
    }

    static InputToleranceResult Find(InputToleranceRequest request) => InputToleranceAnalysis.Find(request, default);
    static InputTolerancePreview Preview(InputToleranceRequest request, int? offset) =>
        InputToleranceAnalysis.Preview(PreviewRequest(request, offset), default);
    static InputTolerancePreviewRequest PreviewRequest(InputToleranceRequest request, int? offset) =>
        new(request.Json, request.Profile, request.WarningDuration, request.Axis, request.Inputs, request.InputIndex,
            offset, !offset.HasValue, request.SurfaceRunSettings);
    static InputToleranceProbe Probe(InputToleranceResult result, int offset) => result.Probes.Single(probe => probe.OffsetMs == offset);
    static string Grid(InputToleranceResult result) => string.Join(", ", result.Probes.Select(probe => $"{probe.OffsetMs}:{probe.Status}"));
    static string Source(double length, Obstacle[] obstacles) => TrackJson.Write(new TrackPackage(new Track(length, obstacles)
        { Id = "input-tolerance-test", Name = "Input tolerance test" }));
}
