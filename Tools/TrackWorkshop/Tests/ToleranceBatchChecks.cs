using System.Text.Json.Nodes;
using FlipRace.Core;
using TrackWorkshop;

internal static class ToleranceBatchChecks
{
    internal static object Run(Action<bool, string> check)
    {
        var package = new TrackPackage(new Track(1500, [])
            { Id = "batch-checks", Name = "Tolerance batch" });
        string json = TrackJson.Write(package);
        var request = new ReplayRequest(json, "Chosen", .8, "time", [.4, .6, .75, 2, 100]);
        var batch = ToleranceBatch.Prepare(request, default);
        var plan = batch.Report("pending");
        check(plan.Targets.Select(target => target.Kind).SequenceEqual(["pair", "pair", "single", "unsupported"]),
            "batch covers adjacent pairs, independent single, and unreached source command");
        check(plan.CommandTargetIndices.SequenceEqual([0, 0, 1, 2, 3]),
            "source command mapping uses previous pair precedence without dropping the overlap");

        var timedOut = batch.Evaluate(0, default, TimeSpan.Zero);
        check(!timedOut.Target.Pair.Complete && timedOut.Target.Pair.Baseline == null,
            "baseline timeout keeps the target pending");
        var second = batch.Evaluate(1, default);
        var single = batch.Evaluate(2, default);
        check(second.Target.Pair is { Complete: true, Cells.Length: 625 }, "pair grid is 25 by 25");
        check(single.Target.Single is { Complete: true, Probes.Length: 25 }, "single grid has 25 shifted cells");
        var partial = batch.Save("time-budget");
        check(!partial.Batch.Complete && partial.Batch.StopReason == "time-budget", "stored partial status");
        check(TrackJson.TryRead(partial.CanonicalJson, out var gamePackage, out _) && gamePackage.ContentHash == package.ContentHash,
            "prepared matrix metadata preserves production track validation and identity");
        var status = WorkshopService.Read(partial.CanonicalJson).Document.ToleranceBatchFor(package);
        check(status is { Status: "ready", Batch.StopReason: "time-budget" }, "partial batch reopens");

        var continuation = ToleranceBatch.Prepare(request with { Json = partial.CanonicalJson }, default);
        continuation.Restore(default);
        check(continuation.Completed == 3 && continuation.Evaluate(1, default).Target == null,
            "resume skips complete matrices");
        var first = continuation.Evaluate(0, default);
        var complete = continuation.Save("time-budget");
        check(first.Target.Pair is { Complete: true, Cells.Length: 625 } && complete.Batch.Complete,
            "resume finishes previously pending original baseline");

        ToleranceBatchCheckpointRequest checkpointRequest = new(json, request.Profile, request.WarningDuration, request.Axis,
            request.Inputs, request.SurfaceRunSettings, [second.Checkpoint]);
        var checkpointResponse = ToleranceBatch.SaveCheckpoint(checkpointRequest, default);
        check(checkpointResponse.Batch.StopReason == "cancelled" && !checkpointResponse.Batch.Complete,
            "cancelled cutoff persists just the delivered matrices");
        Reject(() => ToleranceBatch.SaveCheckpoint(checkpointRequest with { Inputs = [.45, .6, .75, 2, 100] }, default));
        Reject(() => ToleranceBatch.SaveCheckpoint(checkpointRequest with { Checkpoints = [second.Checkpoint, second.Checkpoint] }, default));
        var broken = second.Checkpoint with { Payload = ["aGVsbG8="] };
        Reject(() => ToleranceBatch.SaveCheckpoint(checkpointRequest with { Checkpoints = [broken] }, default));
        var source = JsonNode.Parse(checkpointResponse.CanonicalJson);
        source["workshopAnalysis"]["toleranceBatch"]["checkpoints"][0]["payload"] = new JsonArray("aGVsbG8=");
        var invalid = WorkshopService.Read(source.ToJsonString()).Document.ToleranceBatchFor(package);
        check(invalid.Status == "invalid", "bad packed matrices do not invalidate geometry");

        var existing = new InputTolerancePreviewRequest(json, request.Profile, .8, "time", [2], 0, 600);
        Reject(() => InputToleranceAnalysis.Preview(existing, default));
        check(InputToleranceAnalysis.Preview(existing with { SpanMs = 600 }, default).Probe.OffsetMs == 600,
            "the expanded single grid can preview either endpoint");
        return new { pairCells = 625, singleCells = 25, complete = complete.Batch.Complete };

        void Reject(Action action)
        {
            bool rejected = false;
            try { action(); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "invalid batch request rejected");
        }
    }
}
