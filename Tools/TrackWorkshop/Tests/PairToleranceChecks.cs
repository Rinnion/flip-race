using FlipRace.Core;
using TrackWorkshop;

internal static class PairToleranceChecks
{
    internal static object Run(WorkshopService service, Action<bool, string> check)
    {
        string empty = Source(1200, []);
        double[] tape = [.5, .6];
        var fast = new PairToleranceRequest(empty, "FastFlip", .8, "time", tape, 0, 300);
        var initial = Find(fast);
        var wider = Find(fast with { SpanMs = 600 });
        var widest = Find(fast with { SpanMs = 1000 });
        CheckGrid(initial, 300);
        CheckGrid(wider, 600);
        CheckGrid(widest, 1000);
        check(initial.Cells.All(cell => cell == Cell(widest, cell.FirstOffsetMs, cell.SecondOffsetMs)),
            "wider grids reproduce every previously computed cell exactly");
        check(wider.Cells.All(cell => cell == Cell(widest, cell.FirstOffsetMs, cell.SecondOffsetMs)),
            "expanded grids keep the same absolute origin");

        PairToleranceOffset[] selection = [new(350, 350), new(100, 150), new(-300, -300), new(250, -50), new(0, 0)];
        var subset = Find(fast with { SpanMs = 600, Offsets = selection });
        check(subset.Cells.SequenceEqual(selection.Select(offset => Cell(wider, offset.FirstOffsetMs, offset.SecondOffsetMs))),
            "subset preserves request order and exactly reproduces authoritative full cells");
        var allLater = Find(fast with { Offsets = [new(50, 50), new(100, 100)] });
        check(allLater.Cells.SequenceEqual(new[] { Cell(initial, 50, 50), Cell(initial, 100, 100) }),
            "all-later subset does not execute an original command into the shared prefix");
        check(Find(fast with { Offsets = [] }) is { Status: "ready", Complete: true, Cells: [] }, "empty subset computes only baseline");

        var zero = Preview(fast, 0, 0);
        var originalReplay = service.Replay(new(empty, "FastFlip", .8, "time", tape));
        check(zero.Cell == Cell(initial, 0, 0) && zero.Cell is { Status: "safe", PairIntervalMs: 100, MinimumIntervalMs: 100 } &&
            initial.Baseline.Inputs.SequenceEqual(tape) && !ReferenceEquals(initial.Baseline.Inputs, tape) && tape.SequenceEqual([.5, .6]),
            "zero preview, matrix and immutable source tape agree");
        Near(zero.Cell.EndTime.Value, initial.Baseline.EndTime, "zero uses exact original flight end time");
        Near(zero.Cell.EndX.Value, initial.Baseline.EndX, "zero uses exact original flight end position");
        check(zero.Samples[^1] is { Flipping: false, Support: "Wall" } && zero.Samples.Length < originalReplay.Samples.Count,
            "zero preview stops on original support and does not replay to finish");
        for (int i = 0; i < zero.Samples.Length - 1; i++)
        {
            Near(zero.Samples[i].X, originalReplay.Samples[i].X, "zero reproduces original X");
            Near(zero.Samples[i].Y, originalReplay.Samples[i].Y, "zero reproduces original Y");
        }
        check(Cell(initial, 50, 0) is { Status: "safe", PairIntervalMs: 50, MinimumIntervalMs: 50 },
            "a legal 50ms pair is physically checked independently of search cooldown");
        check(Cell(initial, 100, 0) is { Status: "invalid", Computed: true } &&
            Cell(initial, 150, 0) is { Status: "invalid", Computed: true }, "coincident and crossed pair times are invalid");
        var nearlyCoincident = Find(fast with { Inputs = [.5, .55000005], Offsets = [new(50, 0)] });
        check(nearlyCoincident.Cells is [{ Status: "invalid", Computed: true }],
            "distinct supplied times within Core's replay dispatch tolerance cannot execute simultaneously");
        check(Cell(widest, -550, 0) is { Status: "invalid", Computed: true }, "negative shifted time is invalid");

        var neighbours = fast with { Inputs = [.35, .8, .95, 1.6], FirstInputIndex = 1, SpanMs = 1000,
            Offsets = [new(0, 0), new(0, 600), new(0, 650)] };
        var gapReport = Find(neighbours);
        check(Cell(gapReport, 0, 0) is { PairIntervalMs: 150, MinimumIntervalMs: 150 } &&
            Cell(gapReport, 0, 600) is { PairIntervalMs: 750, MinimumIntervalMs: 50 } &&
            Cell(gapReport, 0, 650) is { Status: "invalid", Computed: true },
            $"minimum interval also checks the unchanged suffix, independent of pair's own interval: {gapReport.Message}");

        var ceiling = new Obstacle { Id = "ceiling-pair", Kind = "ceiling", X = 196, Width = 18, Height = 80 };
        var together = fast with { Json = Source(1200, [ceiling]) };
        var coupled = Find(together);
        check(Cell(coupled, 0, 200) is { Status: "collision", Computed: true } &&
            Cell(coupled, 200, 200) is { Status: "safe", PairIntervalMs: 100 },
            "direct joint shift succeeds even though moving the second press alone hits the ceiling");
        check(Preview(together, 200, 200).Cell == Cell(coupled, 200, 200), "joint success preview matches matrix cell");
        check(Preview(together, 0, 200).Cell == Cell(coupled, 0, 200), "joint collision preview matches matrix cell");

        var split = Preview(fast, -300, 600);
        check(split.Cell is { Status: "safe", SecondInputTime: not null, EndTime: not null } &&
            split.Cell.EndTime > split.Cell.SecondInputTime &&
            split.Samples.Any(sample => sample.X >= initial.Baseline.EndX && sample.Time < split.Cell.SecondInputTime &&
                sample.Support == "Wall" && !sample.Flipping),
            $"a new first landing beyond original X cannot end the check before the second command: {split.Cell}");

        var floor = new Obstacle { Id = "floor-after-pair", Kind = "floor", X = 135, Width = 10, Height = 12 };
        var floorRequest = fast with { Json = Source(1200, [floor]), Inputs = [.4, .5] };
        var floorReport = Find(floorRequest);
        var afterLanding = Preview(floorRequest, -300, -300);
        check(floorReport.Status == "ready" && afterLanding.Cell is { Status: "collision", SecondInputTime: not null } &&
            afterLanding.Cell.EndX < floorReport.Baseline.EndX &&
            afterLanding.Samples.Any(sample => sample.Time > afterLanding.Cell.SecondInputTime &&
                sample.Support == "Wall" && sample.Collisions == 0),
            $"early landing after both commands still checks the rest of original X: {floorReport.Message} {afterLanding.Cell}");

        var repeat = fast with { Inputs = [.2, .6, .72], FirstInputIndex = 1 };
        check(Cell(Find(repeat), -150, 0) is { Status: "rejected", SecondInputTime: not null, Collisions: 0 },
            "a shift that consumes FastFlip's air repeat rejects the second command");
        var hit = new Obstacle { Id = "interrupted-pair", Kind = "ceiling", X = 232, Width = 12, Height = 120 };
        var recovery = fast with { Json = Source(1250, [hit]), Profile = "Chosen", Inputs = [.55, 2.2, 2.3], FirstInputIndex = 1 };
        var interrupted = Find(recovery);
        check(interrupted is { Status: "ready" } && Cell(interrupted, 0, 0).Status == "safe" &&
            Cell(interrupted, -300, -300) is { Status: "collision", Collisions: 1, SecondInputTime: null },
            $"the restarted input still belongs to the interrupted flight when it never found support: {interrupted.Message}");

        var settings = new WorkshopSurfaceRunSettings(true, 5, "Linear", 40, 200, true);
        var chosen = fast with { Profile = "Chosen", Inputs = [.4, .6, .75], SurfaceRunSettings = settings };
        var accelerated = Find(chosen);
        var unaccelerated = Find(chosen with { SurfaceRunSettings = settings with { Enabled = false } });
        CheckGrid(accelerated, 300);
        check(accelerated.Baseline.EndTime > chosen.Inputs[2] && accelerated.Baseline.EndX > unaccelerated.Baseline.EndX,
            "Chosen pair preserves acceleration and the unchanged third press in the same flight");
        CheckDistance(chosen, accelerated);
        CheckDistance(fast, initial);
        CheckDistance(fast with { SurfaceRunSettings = settings }, Find(fast with { SurfaceRunSettings = settings }));

        var shortTrack = fast with { Json = Source(230, []), Inputs = [.45, .55] };
        var shortReport = Find(shortTrack);
        check(shortReport.Complete && Cell(shortReport, 300, 300) is { Status: "incomplete", Computed: true, SecondInputTime: not null },
            "an airborne finish after both accepted commands is a physically completed incomplete result");
        check(Find(shortTrack with { Inputs = [.8, .9] }) is { Status: "unsupported", Baseline: null, Cells: [] },
            "airborne finish never supplies a reference landing");
        check(Find(fast with { Inputs = [.5, 1.5] }) is { Status: "unsupported", Baseline: null, Cells: [] },
            "two originally accepted commands in separate flights are ineligible");
        check(Find(fast with { Inputs = [.1, .15, .2], FirstInputIndex = 1 }) is { Status: "unsupported", Baseline: null, Cells: [] },
            "an ignored original command is ineligible");
        check(Find(fast with { FirstInputIndex = 1 }) is { Status: "unsupported", Baseline: null, Cells: [] },
            "a last command has no pair");

        var expired = PairToleranceAnalysis.Find(fast, default, TimeSpan.Zero);
        var expiredPreview = PairToleranceAnalysis.Preview(PreviewRequest(fast, 0, 0), default, TimeSpan.Zero);
        check(expired is { Status: "unsupported", Complete: false, StopReason: "time-budget", Baseline: null, Cells: [] },
            "expired reference budget has no matrix");
        check(expiredPreview is { Cell: { Status: "incomplete", Computed: false }, Samples: [] }, "expired preview has no trajectory");
        var partial = PairToleranceAnalysis.Find(fast with { SpanMs = 1000 }, default, TimeSpan.FromMilliseconds(20));
        if (partial is { Status: "ready", Complete: false })
            check(partial is { StopReason: "time-budget", Cells.Length: 1681 } && partial.Cells.Any(cell => !cell.Computed) &&
                partial.Cells.Where(cell => cell.Computed).All(cell => cell == Cell(widest, cell.FirstOffsetMs, cell.SecondOffsetMs)) &&
                partial.Cells.Where(cell => !cell.Computed).All(cell => cell is { Status: "incomplete", EndX: null }),
                "partial grid preserves completed cells and marks only unmeasured points pending");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Reject<OperationCanceledException>(() => PairToleranceAnalysis.Find(fast, cancelled.Token));
        Reject<OperationCanceledException>(() => PairToleranceAnalysis.Preview(PreviewRequest(fast, 0, 0), cancelled.Token));
        Reject<ArgumentException>(() => Find(fast with { SpanMs = 301 }));
        Reject<ArgumentException>(() => Find(fast with { Offsets = [new(0, 0), new(0, 0)] }));
        Reject<ArgumentException>(() => Find(fast with { Offsets = [new(350, 0)] }));
        Reject<ArgumentException>(() => Find(fast with { Offsets = [new(5, 0)] }));
        Reject<ArgumentException>(() => Preview(fast, 1050, 0));
        return new { grids = new[] { initial.Cells.Length, wider.Cells.Length, widest.Cells.Length },
            coupledCollision = Cell(coupled, 0, 200), coupledSafe = Cell(coupled, 200, 200), split = split.Cell,
            afterLanding = afterLanding.Cell, interrupted = Cell(interrupted, -300, -300), accelerated = accelerated.Baseline,
            partial = new { partial.Status, partial.Complete, computed = partial.Cells.Count(cell => cell.Computed) } };

        void CheckGrid(PairToleranceResult report, int span)
        {
            int width = 1 + span / 25;
            check(report.Status == "ready" && report.Complete && report.SpanMs == span && report.StepMs == 50 &&
                report.Cells.Length == width * width && report.Cells.Select((cell, i) => cell.Computed &&
                    cell.FirstOffsetMs == i % width * 50 - span && cell.SecondOffsetMs == i / width * 50 - span).All(valid => valid),
                $"full {width}x{width} grid is evaluated in second-offset rows: {report.Message}");
        }

        void Near(double actual, double expected, string message) =>
            check(Math.Abs(actual - expected) < 1e-5, $"{message}: {actual:R} vs {expected:R}");

        void Reject<TException>(Action action) where TException : Exception
        {
            bool rejected = false;
            try { action(); }
            catch (TException) { rejected = true; }
            check(rejected, $"{typeof(TException).Name} propagates");
        }

        void CheckDistance(PairToleranceRequest timed, PairToleranceResult report)
        {
            var replay = service.Replay(new(timed.Json, timed.Profile, timed.WarningDuration, timed.Axis, timed.Inputs, timed.SurfaceRunSettings));
            var distance = Find(timed with { Axis = "distance", Inputs = replay.Inputs.Select(input => input.X.Value).ToArray() });
            check(distance.Status == "ready" && distance.Cells.Select(cell => cell.Status).SequenceEqual(report.Cells.Select(cell => cell.Status)),
                $"equivalent distance-derived times preserve geometric classifications ({timed.Profile}): {distance.Message}");
            foreach (var pair in distance.Baseline.Inputs.Zip(report.Baseline.Inputs)) Near(pair.First, pair.Second, "derived absolute time agrees");
            Near(distance.Baseline.EndX, report.Baseline.EndX, "distance baseline reaches same ending position");
        }
    }

    static PairToleranceResult Find(PairToleranceRequest request) => PairToleranceAnalysis.Find(request, default);
    static PairTolerancePreview Preview(PairToleranceRequest request, int first, int second) =>
        PairToleranceAnalysis.Preview(PreviewRequest(request, first, second), default);
    static PairTolerancePreviewRequest PreviewRequest(PairToleranceRequest request, int first, int second) =>
        new(request.Json, request.Profile, request.WarningDuration, request.Axis, request.Inputs, request.FirstInputIndex,
            first, second, request.SurfaceRunSettings);
    static PairToleranceCell Cell(PairToleranceResult result, int first, int second) =>
        result.Cells.SingleOrDefault(cell => cell.FirstOffsetMs == first && cell.SecondOffsetMs == second) ??
        throw new InvalidOperationException($"Missing cell {first},{second}: {result.Status}, {result.Message}");
    static string Source(double length, Obstacle[] obstacles) => TrackJson.Write(new TrackPackage(new Track(length, obstacles)
        { Id = "pair-tolerance-test", Name = "Pair tolerance test" }));
}
