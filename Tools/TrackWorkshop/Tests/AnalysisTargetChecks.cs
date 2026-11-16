using FlipRace.Core;
using TrackWorkshop;

internal static class AnalysisTargetChecks
{
    internal static object Run(WorkshopService service, Action<bool, string> check)
    {
        string empty = Source(1200, []);
        var fast = new AnalysisTargetRequest(empty, "FastFlip", .8, "time", [.5, .6], 0);
        var firstFast = Expect(fast, "pair", 0);
        var secondFast = Expect(fast with { InputIndex = 1 }, "pair", 0);
        Expect(fast with { Inputs = [.5] }, "single");
        var separate = fast with { Inputs = [.5, 1.5] };
        Expect(separate, "single");
        Expect(separate with { InputIndex = 1 }, "single");

        var simultaneous = fast with { Inputs = [.5, .50000001] };
        var dispatched = Replay(simultaneous).Inputs;
        check(dispatched.All(command => command.Status == "accepted") && dispatched[0].Time == dispatched[1].Time,
            "distinct source timestamps can dispatch two accepted commands at the same Core instant");
        Expect(simultaneous, "single");
        Expect(simultaneous with { InputIndex = 1 }, "single");

        var repeat = fast with { Inputs = [.5, .6, .65, .9] };
        Expect(repeat with { InputIndex = 2 }, "unsupported");
        // The rejected command stays on the original tape. Inputs at indices 1 and 3
        // are both accepted here and cannot form an adjacent pair.
        check(Replay(repeat).Inputs.Select(command => command.Status).SequenceEqual(["accepted", "accepted", "ignored", "accepted"]),
            "FastFlip fixture has an ignored command between accepted inputs");
        Expect(repeat with { InputIndex = 3 }, "single");
        var late = fast with { Inputs = [.5, 100] };
        Expect(late, "single");
        Expect(late with { InputIndex = 1 }, "unsupported");
        Expect(late with { Axis = "distance", Inputs = [110, 1300], InputIndex = 1 }, "unsupported");

        var settings = new WorkshopSurfaceRunSettings(true, 5, "Linear", 40, 200, true);
        double[] chosenTape = [.4, .6, .75];
        var chosen = fast with { Profile = "Chosen", Inputs = chosenTape, SurfaceRunSettings = settings };
        var firstChosen = Expect(chosen, "pair", 0);
        var middleChosen = Expect(chosen with { InputIndex = 1 }, "pair", 0);
        var thirdChosen = Expect(chosen with { InputIndex = 2 }, "pair", 1);
        foreach (var timed in new[] { fast, chosen })
        {
            var commands = Replay(timed).Inputs;
            check(commands.All(command => command.Status == "accepted"), $"{timed.Profile} distance fixture is accepted in time");
            double[] positions = commands.Select(command => command.X.Value).ToArray();
            var byDistance = timed with { Axis = "distance", Inputs = (double[])positions.Clone() };
            for (int i = 0; i < positions.Length; i++) Expect(byDistance with { InputIndex = i }, "pair", i < 2 ? 0 : 1);
            check(byDistance.Inputs.SequenceEqual(positions), "distance target preserves the original command tape");
        }
        check(chosenTape.SequenceEqual([.4, .6, .75]), "time target preserves the original command tape");

        string hitJson = Source(1200, [new Obstacle { Id = "analysis-hit", Kind = "ceiling", X = 172, Width = 12, Height = 190 }]);
        var withHit = fast with { Json = hitJson };
        var collisionAfterPair = Replay(withHit);
        check(collisionAfterPair.Inputs.All(command => command.Status == "accepted") &&
            collisionAfterPair.Samples.Any(sample => sample.Collisions > 0 && sample.Time > .6 && sample.Flipping),
            "impact fixture accepts the original pair before an airborne collision");
        Expect(withHit, "pair", 0);
        Expect(withHit with { InputIndex = 1 }, "pair", 0);
        var hitMatrix = PairToleranceAnalysis.Find(new(hitJson, withHit.Profile, withHit.WarningDuration, withHit.Axis,
            withHit.Inputs, 0, Offsets: []), default);
        check(hitMatrix is { Status: "unsupported", Baseline: null } && hitMatrix.Message.Contains("Столкновение"),
            "pair analysis diagnoses the impact after the target is resolved");

        var interrupted = withHit with { Inputs = [.5, 1.3] };
        var collisionBetween = Replay(interrupted);
        check(collisionBetween.Inputs.All(command => command.Status == "accepted") &&
            collisionBetween.Samples.Any(sample => sample.Collisions > 0 && sample.Time < 1.3) &&
            collisionBetween.Samples.Where(sample => sample.Time > .5 && sample.Time < 1.3).All(sample => sample.Flipping),
            "interrupted fixture is airborne without support across impact and recovery before the second accepted input");
        Expect(interrupted, "single");
        Expect(interrupted with { InputIndex = 1 }, "single");

        var hitAndStone = TrackJson.Write(new TrackPackage(new Track(1200,
            [new Obstacle { Id = "analysis-hit", Kind = "ceiling", X = 172, Width = 12, Height = 190 },
             new Obstacle { Id = "analysis-stone", Kind = "floor", X = 270, Width = 38, Height = 20 }])
        {
            Id = "analysis-target-stone-test", Name = "Analysis target stone test",
            RiskZones = [new RiskZone { Id = "analysis-landing", Kind = "landing", ObstacleId = "analysis-stone",
                X = 292, Y = 220, LandingWidth = 30 }]
        }));
        var walkOff = interrupted with { Json = hitAndStone, Inputs = [.5, 1.24, 1.95], InputIndex = 2 };
        var walkOffReplay = Replay(walkOff);
        check(walkOffReplay.Inputs.All(command => command.Status == "accepted") &&
            walkOffReplay.Samples.Any(sample => sample.Time > 1.24 && sample.Time < 1.95 && sample.Support == "Stone") &&
            walkOffReplay.Samples.Last(sample => sample.Time < 1.95) is { Flipping: true, X: > 308 },
            "recovered airborne press and the press after walking off a stone straddle genuine support");
        Expect(walkOff, "single");
        Expect(walkOff with { InputIndex = 1 }, "single");

        check(AnalysisTarget.Resolve(fast, default, TimeSpan.Zero).Message == InputToleranceAnalysis.BudgetMessage,
            "target lookup obeys its calculation deadline");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Reject<OperationCanceledException>(() => AnalysisTarget.Resolve(fast, cancelled.Token));
        Reject<ArgumentException>(() => AnalysisTarget.Resolve(fast with { InputIndex = 2 }, default));
        Reject<ArgumentException>(() => AnalysisTarget.Resolve(fast with { Axis = "invalid" }, default));

        return new { firstFast, secondFast, firstChosen, middleChosen, thirdChosen };

        ReplayResult Replay(AnalysisTargetRequest request) => service.Replay(new(request.Json, request.Profile,
            request.WarningDuration, request.Axis, request.Inputs, request.SurfaceRunSettings));

        AnalysisTargetResult Expect(AnalysisTargetRequest request, string kind, int? first = null)
        {
            var result = AnalysisTarget.Resolve(request, default);
            check(result is { } && result.Kind == kind && result.InputIndex == request.InputIndex && result.FirstInputIndex == first &&
                (kind == "unsupported" ? result.Message.Length > 0 : result.Message.Length == 0),
                $"{request.Profile} {request.Axis} [{string.Join(", ", request.Inputs)}] input {request.InputIndex} expected {kind} {first}, got {result}");
            return result;
        }

        void Reject<TException>(Action action) where TException : Exception
        {
            bool rejected = false;
            try { action(); }
            catch (TException) { rejected = true; }
            check(rejected, $"target lookup propagates {typeof(TException).Name}");
        }
    }

    static string Source(double length, Obstacle[] obstacles) => TrackJson.Write(new TrackPackage(new Track(length, obstacles)
        { Id = "analysis-target-test", Name = "Analysis target test" }));
}
