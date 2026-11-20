using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;
using TrackWorkshop;

internal static class ToleranceBatchChecks
{
    internal static object Run(Action<bool, string> check)
    {
        var package = new TrackPackage(new Track(1500, []) { Id = "batch-checks", Name = "Tolerance batch" });
        string json = TrackJson.Write(package);
        var request = new ReplayRequest(json, "Chosen", .8, "time", [.4, .6, .75, 2, 100]);
        var batch = ToleranceBatch.Prepare(request, default);
        var plan = batch.Report("pending");
        check(plan.Targets.Select(target => target.Kind).SequenceEqual(["maneuver", "maneuver", "unsupported"]),
            "all consecutive accepted commands of a flight form one maneuver");
        check(plan.Targets[0].InputIndices.SequenceEqual([0, 1, 2]) && plan.CommandTargetIndices.SequenceEqual([0, 0, 0, 1, 2]),
            "takeoff and every repeat share one fixed original group");
        var timedOut = batch.Evaluate(0, default, TimeSpan.Zero);
        check(timedOut.Target.Maneuver is { Complete: false, Baseline: null, Cursor: "0" }, "baseline timeout remains resumable");
        var single = Finish(batch, 1);
        check(single.Maneuver is { Complete: true, Total: "25", Cells.Length: 25 }, "single uses same exhaustive semantics");
        var firstChunk = batch.Evaluate(0, default, TimeSpan.FromSeconds(5));
        check(firstChunk.Target.Maneuver is { Complete: false, Cells.Length: 75, Total: "15625" }, "triple keeps only 25*N marginals");
        var partial = batch.Save("time-budget");
        check(!partial.Batch.Complete && TrackJson.TryRead(partial.CanonicalJson, out var gamePackage, out _) && gamePackage.ContentHash == package.ContentHash,
            "partial map metadata preserves production track identity");
        var status = WorkshopService.Read(partial.CanonicalJson).Document.ToleranceBatchFor(package);
        check(status is { Status: "ready", Batch.Complete: false }, "partial cursor and marginals reopen");
        var continuation = ToleranceBatch.Prepare(request with { Json = partial.CanonicalJson }, default);
        continuation.Restore(default);
        var resumed = Finish(continuation, 0).Maneuver;
        var direct = Finish(ToleranceBatch.Prepare(request, default), 0).Maneuver;
        check(JsonSerializer.Serialize(resumed) == JsonSerializer.Serialize(direct), "resume is identical to uninterrupted enumeration including witnesses");
        check(continuation.Save("time-budget").Batch.Complete && resumed.Cursor == resumed.Total, "complete means exhaustive grid");
        BigInteger globalValid = BigInteger.Parse(resumed.Valid), globalSafe = BigInteger.Parse(resumed.Safe);
        foreach (int row in Enumerable.Range(0, 3))
        {
            check(resumed.Cells.Skip(row * 25).Take(25).Aggregate(BigInteger.Zero, (sum, cell) => sum + BigInteger.Parse(cell.Valid)) == globalValid &&
                resumed.Cells.Skip(row * 25).Take(25).Aggregate(BigInteger.Zero, (sum, cell) => sum + BigInteger.Parse(cell.Safe)) == globalSafe,
                "each joint tuple counted once per conditional row");
        }
        check(resumed.Percent == ManeuverToleranceAnalysis.Percentage(globalSafe, globalValid), "whole score uses unique G/V, never row means");
        check(resumed.SafeWindow is { EarliestMs: >= 0 } && resumed.SafeWindow.LatestMs >= resumed.SafeWindow.EarliestMs, "aggregate actual safe time window");
        check(ManeuverToleranceAnalysis.InvalidAt([.5, .599], [0, 1]) == 1 &&
            ManeuverToleranceAnalysis.InvalidAt([.5, .6], [0, 1]) == -1, "99ms excluded and exact100ms included");
        check(ManeuverToleranceAnalysis.InvalidAt([.45, .5, 1], [1]) == 0 &&
            ManeuverToleranceAnalysis.InvalidAt([0, .95, 1], [1]) == 0, "fixed external neighbors enforce100ms");
        check(ManeuverToleranceAnalysis.Percentage(0, 0) == null, "zero admissible combinations are unknown");
        var pairRequest = request with { Inputs = [.4, .6] };
        var pair = Finish(ToleranceBatch.Prepare(pairRequest, default), 0).Maneuver;
        check(pair is { Total: "625", Cells.Length: 50, Complete: true }, "pair enumerates exact625 joint schedules");
        var zeroRequest = request with { Inputs = Enumerable.Range(0, 15).Select(i => i * .00001).ToArray() };
        var zero = Finish(ToleranceBatch.Prepare(zeroRequest, default), 0).Maneuver;
        check(zero is { Complete: true, Valid: "0", Safe: "0", Percent: null } && zero.Cursor == zero.Total,
            "large impossible timing domain exhaustively skips prefixes and reports unknown, not zero percent");
        string ceiling = TrackJson.Write(new TrackPackage(new Track(1500,
            [new Obstacle { Id = "deep-ceiling", Kind = "ceiling", X = 196, Width = 18, Height = 80 }])
            { Id = "deep-collision", Name = "Deep collision" }));
        var collisionRequest = request with { Json = ceiling, Profile = "FastFlip", Inputs = [.5, .6] };
        var mixed = Finish(ToleranceBatch.Prepare(collisionRequest, default), 0).Maneuver;
        check(BigInteger.Parse(mixed.Safe) > 0 && BigInteger.Parse(mixed.Safe) < BigInteger.Parse(mixed.Valid),
            "physical collision and rejection failures stay in admissible denominator");
        var rowMeans = mixed.Cells.Where(cell => cell.Percent.HasValue).Average(cell => cell.Percent.Value);
        check(Math.Abs(rowMeans - mixed.Percent.Value) > .01, "whole maneuver is weighted joint G/V, not mean of conditional rows");
        var sourceForOracle = InputToleranceAnalysis.ReadSource(collisionRequest, default, out _);
        BigInteger oracleValid = 0, oracleSafe = 0;
        for (int a = -600; a <= 600; a += 50)
        for (int b = -600; b <= 600; b += 50)
        {
            var times = new[] { .5 + a / 1000d, .6 + b / 1000d };
            if (times[0] < 0 || times[1] < 0 || times[1] - times[0] < .1 - 1e-10) continue;
            oracleValid++;
            var outcome = InputToleranceAnalysis.Shift(sourceForOracle, times, 0,
                new(mixed.Baseline.StartTime, mixed.Baseline.EndX), false, default, 1);
            if (outcome.Status == "safe") oracleSafe++;
        }
        check(mixed.Valid == oracleValid.ToString() && mixed.Safe == oracleSafe.ToString(),
            "radix pruning and shared-prefix physics exactly match independent exhaustive pair oracle");
        var selected = ToleranceBatch.Selected(new(json, "Chosen", .8, "time", request.Inputs, 2), default);
        check(selected.Target.InputIndices.SequenceEqual([0, 1, 2]) && selected.Target.Maneuver.Cells.Length == 75,
            "selected member resolves the whole original maneuver");
        var safeCell = resumed.Cells.First(cell => cell.WitnessOffsetsMs != null);
        var preview = ManeuverToleranceAnalysis.Preview(new(json, "Chosen", .8, "time", request.Inputs,
            safeCell.InputIndex, safeCell.OffsetMs, safeCell.WitnessOffsetsMs), default);
        check(preview.Status == "safe" && preview.Samples.Length > 0 && preview.Inputs.Length == request.Inputs.Length &&
            preview.PressPoints.Length == request.Inputs.Length && preview.Samples[^1].Support is "Wall" or "Stone",
            "safe preview reruns whole witness and returns full alternative press points");
        Reject(() => ManeuverToleranceAnalysis.Preview(new(json, "Chosen", .8, "time", request.Inputs,
            safeCell.InputIndex, safeCell.OffsetMs + 50, safeCell.WitnessOffsetsMs), default));
        var lateRequest = request with { Inputs = [15, 15.2], Axis = "time" };
        lateRequest = lateRequest with { Json = TrackJson.Write(new TrackPackage(new Track(10000, []) { Id = "late", Name = "Late" })) };
        var lateTarget = ToleranceBatch.Prepare(lateRequest, default).Report("pending").Targets[0];
        var tiny = ManeuverToleranceAnalysis.Advance(lateRequest, lateTarget, default, TimeSpan.FromTicks(1));
        check(BigInteger.Parse(tiny.Cursor) > 0, "preparation does not consume a tiny work budget before first atomic progress");
        var checkpoints = batch.Checkpoints;
        ToleranceBatchCheckpointRequest saveRequest = new(json, "Chosen", .8, "time", request.Inputs, null, checkpoints);
        var cancelled = ToleranceBatch.SaveCheckpoint(saveRequest, default);
        check(cancelled.Batch.StopReason == "cancelled" && !cancelled.Batch.Complete, "cancelled delivered chunk persists");
        Reject(() => ToleranceBatch.SaveCheckpoint(saveRequest with { Inputs = [.45, .6, .75, 2, 100] }, default));
        Reject(() => ToleranceBatch.SaveCheckpoint(saveRequest with { Checkpoints = [checkpoints[0], checkpoints[0]] }, default));
        Reject(() => ToleranceBatch.SaveCheckpoint(saveRequest with { Checkpoints = [checkpoints[0] with { Payload = ["aGVsbG8="] }] }, default));
        var source = JsonNode.Parse(cancelled.CanonicalJson);
        source["workshopAnalysis"]["toleranceBatch"]["identity"]["rulesVersion"] = 1;
        check(WorkshopService.Read(source.ToJsonString()).Document.ToleranceBatchFor(package).Status == "stale", "old pair rules are stale");
        source = JsonNode.Parse(cancelled.CanonicalJson);
        source["workshopAnalysis"]["toleranceBatch"]["checkpoints"][0]["payload"] = new JsonArray("aGVsbG8=");
        check(WorkshopService.Read(source.ToJsonString()).Document.ToleranceBatchFor(package).Status == "invalid", "invalid packed state does not invalidate geometry");
        var corrupt = JsonSerializer.SerializeToNode(firstChunk.Target);
        corrupt["Maneuver"]["Cursor"] = "99999999999999999999";
        Reject(() => ToleranceBatch.SaveCheckpoint(saveRequest with { Checkpoints = [Pack(firstChunk.Checkpoint, corrupt)] }, default));
        using var token = new CancellationTokenSource(); token.Cancel();
        bool rejectedCancellation = false;
        try { batch.Evaluate(0, token.Token); } catch (OperationCanceledException) { rejectedCancellation = true; }
        check(rejectedCancellation, "pre-cancelled work cannot mutate checkpoints");
        var four = ToleranceBatch.Prepare(request with { Inputs = [.4, .6, .75, .9] }, default).Report("pending");
        check(four.Targets is [{ InputIndices.Length: 4 }], "four-command fixture includes all takeoff/repeats");
        return new { tripleTotal = resumed.Total, resumed.Valid, resumed.Safe, resumed.Percent, pairTotal = pair.Total,
            fixtures = new { three = new[] { .4, .6, .75 }, four = new[] { .4, .6, .75, .9 } } };

        ToleranceBatchTarget Finish(ToleranceBatch current, int index)
        {
            for (int i = 0; i < 1000; i++)
            {
                var target = current.Evaluate(index, default, TimeSpan.FromSeconds(5)).Target;
                if (target == null) return current.Report("pending").Targets[index];
                if (target.Maneuver.Complete) return target;
            }
            throw new Exception("Exceeded test continuation bound");
        }
        void Reject(Action action)
        {
            bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
            check(rejected, "invalid persisted/requested state rejected");
        }
    }

    static ToleranceCheckpoint Pack(ToleranceCheckpoint original, JsonNode target)
    {
        // Convert the test's PascalCase node through the same web serializer.
        var typed = target.Deserialize<ToleranceBatchTarget>();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(typed, ToleranceBatchStorage.Json);
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionMode.Compress, true)) gzip.Write(bytes);
        return original with { Length = bytes.Length, Payload = [Convert.ToBase64String(stream.ToArray())] };
    }
}
