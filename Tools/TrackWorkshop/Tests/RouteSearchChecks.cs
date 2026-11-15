using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;
using TrackWorkshop;

internal static class RouteSearchChecks
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static object Run(WorkshopService service, Action<bool, string> check)
    {
        string source = "{\n  \"schemaVersion\":1, \"physicsVersion\":1,\n  \"track\":{\"id\":\"tiny-route\",\"name\":\"Проба\",\"length\":75,\"obstacles\":[]},\n  \"extensions\" : {\"unrecognized\" : 1.2300e-3}}\r\n";
        var updates = new List<RouteSearchProgress>();
        var small = service.SearchRoutes(new(source, "Chosen", .8), default, updates.Add);
        var tiny = small.Analysis.Runs.Single();
        check(updates[0] == new RouteSearchProgress(0, "search") && updates.Contains(new(70, "verify")) &&
            updates.Contains(new(98, "verify")) && updates[^1] == new RouteSearchProgress(99, "save") &&
            updates.Zip(updates.Skip(1)).All(pair => pair.First.Percent < pair.Second.Percent) &&
            updates.All(update => update.Phase == (update.Percent < 70 ? "search" : update.Percent < 99 ? "verify" : "save")),
            "progress counts verified candidates, advances through ordered phases, and leaves completion to the result consumer");
        var interrupted = new List<RouteSearchProgress>();
        using (var pending = new CancellationTokenSource())
        {
            bool canceledBeforeSave = false;
            try
            {
                service.SearchRoutes(new(source, "Chosen", .8), pending.Token, update =>
                {
                    interrupted.Add(update);
                    if (update.Percent == 70) pending.Cancel();
                });
            }
            catch (OperationCanceledException) { canceledBeforeSave = true; }
            check(canceledBeforeSave && interrupted[^1] == new RouteSearchProgress(70, "verify"),
                "cancellation during progress reporting stops the work before any result or save phase");
        }
        var expected = new HashSet<string>(StringComparer.Ordinal);
        // The empty track has a fixed finish time. Enumerate every tape on the
        // 50 ms grid with no adjacent ticks, independently of beam pruning.
        double finishTime = Replay(service, source, tiny, []).Summary.FinishTime.Value;
        int decisionCount = Enumerable.Range(0, RouteSearch.MaximumTicks).TakeWhile(tick => tick / 20d < finishTime).Count();
        for (int mask = 0; mask < (1 << decisionCount); mask++)
        {
            if ((mask & (mask >> 1)) != 0) continue;
            int[] ticks = Enumerable.Range(0, decisionCount).Where(tick => (mask & (1 << tick)) != 0).ToArray();
            var replay = Replay(service, source, tiny, ticks);
            check(replay.Summary.CleanFinish && replay.Summary.AcceptedInputs == ticks.Length,
                "tiny exhaustive fixture reaches the finish with every subset of legal edges");
            expected.Add(string.Join(",", ticks));
        }
        check(!tiny.BeamPruned && tiny.Routes.Length == expected.Count &&
            expected.SetEquals(tiny.Routes.Select(item => string.Join(",", item.InputTicks))) &&
            tiny.Routes.Any(item => item.InputTicks.Any(tick => tick % 2 == 1)),
            "tiny search agrees with exhaustive authoritative replay, including empty tape and equal outcomes");
        check(small.SourceJson.StartsWith(source[..source.LastIndexOf('}')], StringComparison.Ordinal)
            && small.SourceJson.EndsWith("}\r\n", StringComparison.Ordinal), "saved source outside metadata is unchanged byte for byte");
        check(TrackJson.TryRead(small.SourceJson, out _, out _), "small authoring extension is compatible with the game reader");
        var smallAnalysis = JsonNode.Parse(small.SourceJson)["workshopAnalysis"];
        var smallGroup = smallAnalysis["groups"][0];
        check(smallAnalysis["version"].GetValue<int>() == 2 && smallAnalysis["runs"] == null &&
            smallGroup["settings"]["profile"].GetValue<string>() == "Chosen" &&
            smallGroup["settings"]["warningDuration"].GetValue<double>() == .8 &&
            smallGroup["settings"]["surfaceRunSettings"]["enabled"].GetValue<bool>() == false &&
            new[] { "profile", "warningDuration", "surfaceRun", "surfaceRunSettings" }
                .All(field => !smallGroup["runs"][0].AsObject().ContainsKey(field)),
            "settings are stored once per group and are restored in the normalized response");

        string longSource = source.Replace("\"length\":75", "\"length\":500");
        var full = service.SearchRoutes(new(longSource, "Chosen", .8));
        var run = full.Analysis.Runs.Single();
        check(run.SurfaceRun == false && run.StartX == 0 && run is { RulesVersion: 2, StepMs: 50,
            SearchParameters.MinimumInputIntervalMs: 100 }, "recorded run settings");
        check(run.StoredCount == 1000 && run.VerifiedCount >= 1000 && run.FoundCount >= run.VerifiedCount
            && run.Routes.Select(item => string.Join(",", item.InputTicks)).Distinct().Count() == 1000,
            "default search stores 1000 distinct finished variants");
        double earlier = 0;
        foreach (var route in run.Routes)
        {
            var replay = Replay(service, full.SourceJson, run, route.InputTicks);
            check(replay.Summary.FinishTime == route.FinishTime && replay.Summary.AcceptedInputs == route.InputTicks.Length &&
                replay.Summary.IgnoredInputs == 0 && replay.Summary.UnreachedInputs == 0 &&
                replay.Summary.Collisions == route.Collisions && replay.Summary.Boosts == route.Boosts &&
                replay.Summary.Slows == route.Slows && replay.Summary.Coins == route.Coins &&
                replay.Summary.FinishBoosts == route.FinishBoosts && route.FinishTime >= earlier &&
                route.InputTicks.Zip(route.InputTicks.Skip(1)).All(pair => pair.Second - pair.First >= 2),
                "all 1000 stored tapes really finish in fastest-first order and match outcomes");
            earlier = route.FinishTime;
        }

        var tagged = JsonNode.Parse(full.SourceJson).AsObject();
        var taggedAnalysis = tagged["workshopAnalysis"].AsObject();
        taggedAnalysis["futureEnvelopeField"] = new JsonObject { ["data"] = 8 };
        taggedAnalysis["groups"][0]["futureGroupField"] = 12;
        taggedAnalysis["groups"][0]["settings"]["futureSettingsField"] = true;
        taggedAnalysis["groups"][0]["runs"][0]["futureRunField"] = "kept";
        string taggedSource = tagged.ToJsonString();
        var twoProfiles = service.SearchRoutes(new(taggedSource, "FastFlip", .6));
        var fast = twoProfiles.Analysis.Runs.Single(item => item.Profile == "FastFlip");
        var twoRoot = JsonNode.Parse(twoProfiles.SourceJson)["workshopAnalysis"];
        check(twoProfiles.Analysis.Runs.Length == 2 && fast.WarningDuration == .6 && fast.Routes.Length > 0
            && fast.FlightSettings != run.FlightSettings && twoRoot["futureEnvelopeField"]["data"].GetValue<int>() == 8
            && JsonNode.DeepEquals(twoRoot["groups"][0], taggedAnalysis["groups"][0]),
            "switching profile preserves separate run and future extension fields");
        foreach (var route in fast.Routes)
        {
            var replay = Replay(service, longSource, fast, route.InputTicks);
            check(replay.Summary.FinishTime == route.FinishTime && replay.Summary.AcceptedInputs == route.InputTicks.Length,
                "independent FastFlip route replay");
        }
        var replacement = service.SearchRoutes(new(twoProfiles.SourceJson, "Chosen", .8));
        var replacementRoot = JsonNode.Parse(replacement.SourceJson)["workshopAnalysis"];
        check(replacement.Analysis.Runs.Length == 2 && replacement.Analysis.Runs[0].WarningDuration == .8 &&
            replacementRoot["groups"][0]["runs"][0]["futureRunField"].GetValue<string>() == "kept" &&
            replacementRoot["groups"][0]["futureGroupField"].GetValue<int>() == 12 &&
            replacementRoot["groups"][0]["settings"]["futureSettingsField"].GetValue<bool>() &&
            JsonNode.DeepEquals(twoRoot["groups"][1], replacementRoot["groups"][1]),
            "exact-settings replacement preserves group extensions and the other profile's result");

        RouteAnalysisStatus Inspect(string json)
        {
            var preview = WorkshopService.Read(json);
            return preview.Document.AnalysisFor(preview.Package);
        }
        check(Inspect(full.SourceJson).Status == "ready" && Inspect(longSource).Status == "missing",
            "new and reopened report statuses");
        check(Inspect(full.SourceJson.Replace("\"length\":500", "\"length\":501")).Status == "stale",
            "changing geometry invalidates stored results");
        check(Inspect(full.SourceJson.Replace("\"version\":2,\"groups\"", "\"version\":3,\"groups\"")).Status == "unsupported",
            "future analysis format is not trusted");

        var oldRoutes = tiny.Routes.Where(route => route.InputTicks.All(tick => tick % 2 == 0))
            .Select(route => route with { InputTicks = Array.ConvertAll(route.InputTicks, tick => tick / 2) }).ToArray();
        var oldPayload = JsonSerializer.SerializeToNode(tiny with
        {
            RulesVersion = 1, StepMs = 100, Routes = oldRoutes, StoredCount = oldRoutes.Length,
            VerifiedCount = oldRoutes.Length, FoundCount = oldRoutes.Length
        }, Json).AsObject();
        oldPayload["searchParameters"].AsObject().Remove("minimumInputIntervalMs");
        oldPayload.Remove("surfaceRunSettings");
        var oldDocument = JsonNode.Parse(source).AsObject();
        oldDocument["workshopAnalysis"] = new JsonObject { ["version"] = 1, ["runs"] = new JsonArray(oldPayload) };
        string oldSource = oldDocument.ToJsonString();
        var oldRun = Inspect(oldSource).Analysis?.Runs.SingleOrDefault();
        check(oldRun is { RulesVersion: 1, StepMs: 100, SearchParameters.MinimumInputIntervalMs: 100 } &&
            oldRun.Routes.Length == oldRoutes.Length && oldRun.Routes.Any(route => route.InputTicks.Length >= 2) &&
            oldRun.Routes.All(route =>
            {
                var summary = Replay(service, oldSource, oldRun, route.InputTicks).Summary;
                return summary.FinishTime == route.FinishTime && summary.AcceptedInputs == route.InputTicks.Length;
            }), "legacy tapes are replayed at 100 ms with their original timing");
        var migrated = service.SearchRoutes(new(oldSource, "Chosen", .6));
        var migratedRuns = migrated.Analysis.Runs;
        check(migratedRuns.Length == 2 && migratedRuns[0] is { RulesVersion: 1, StepMs: 100, WarningDuration: .8 } &&
            migratedRuns[1] is { RulesVersion: 2, StepMs: 50, WarningDuration: .6 } &&
            JsonNode.DeepEquals(JsonNode.Parse(migrated.SourceJson)["workshopAnalysis"]["groups"][0]["runs"][0]["routes"],
                oldPayload["routes"]), "saving a new settings group preserves legacy route ticks and their replay grid");
        void CheckInvalid(Action<JsonArray> edit, string message, bool rejectSearch = true)
        {
            var invalid = JsonNode.Parse(small.SourceJson);
            edit(invalid["workshopAnalysis"]["groups"].AsArray());
            string json = invalid.ToJsonString();
            check(Inspect(json).Status == "invalid", message);
            if (rejectSearch)
            {
                bool rejected = false;
                try { service.SearchRoutes(new(json, "Chosen", .8)); }
                catch (ArgumentException) { rejected = true; }
                check(rejected, "malformed groups are retained rather than overwritten by a search");
            }
        }
        CheckInvalid(groups => groups[0]["runs"][0]["routes"][0]["inputTicks"] = JsonNode.Parse("[3,2]"),
            "invalid stored tape cannot be selected", rejectSearch: false);
        CheckInvalid(groups => groups[0]["runs"][0]["routes"][0]["inputTicks"] = JsonNode.Parse("[0,1]"),
            "rules version 2 rejects commands only 50 ms apart", rejectSearch: false);
        CheckInvalid(groups => groups[0]["runs"][0]["searchParameters"]["minimumInputIntervalMs"] = 50,
            "rules version 2 rejects mismatched cooldown metadata", rejectSearch: false);
        CheckInvalid(groups => groups[0]["runs"][0]["searchParameters"].AsObject().Remove("minimumInputIntervalMs"),
            "rules version 2 requires explicit cooldown metadata", rejectSearch: false);
        CheckInvalid(groups => groups.Add(groups[0].DeepClone()), "duplicate groups are invalid");
        CheckInvalid(groups => groups[0]["runs"].AsArray().Clear(), "empty run lists are invalid");
        CheckInvalid(groups => groups[0]["runs"].AsArray().Add(groups[0]["runs"][0].DeepClone()), "multiple runs are invalid");
        CheckInvalid(groups => groups[0]["settings"].AsObject().Remove("warningDuration"), "settings require an explicit warning value");
        CheckInvalid(groups => groups[0]["runs"][0]["surfaceRun"] = false, "persisted run settings cannot duplicate group settings");
        CheckInvalid(groups => groups[0]["settings"]["surfaceRunSettings"]["ratePercent"] = 6,
            "disabled surface settings must also contain valid values");
        var exhausted = new RouteSearch(WorkshopService.Read(source).Package, FlightPresets.Chosen, "Chosen", .8,
            new(MaximumStates: 1)).Find();
        check(exhausted.StopReason == "state-limit" && exhausted.ExpandedStates == 1 && exhausted.StoredCount == 0,
            "tiny budget reports no found route without claiming impossibility");
        var budgetUpdates = new List<RouteSearchProgress>();
        var budgetRun = new RouteSearch(WorkshopService.Read(longSource).Package, FlightPresets.Chosen, "Chosen", .8,
            new(MaximumStates: 20)).Find(budgetUpdates.Add);
        check(budgetRun.StopReason == "state-limit" && budgetRun.ExpandedStates == 20 &&
            budgetUpdates.Any(update => update.Phase == "search" && update.Percent > 0) &&
            budgetUpdates[^1] == new RouteSearchProgress(70, "verify"),
            "search progress advances with expansion budget spent even when there are no finishes to verify");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool canceled = false;
        try { service.SearchRoutes(new(source, "Chosen", .8), cancelled.Token); }
        catch (OperationCanceledException) { canceled = true; }
        check(canceled, "cancellation stops route analysis");

        string padded = new string(' ', TrackJson.MaximumBytes - Encoding.UTF8.GetByteCount(longSource) - 2500) + longSource;
        var truncated = service.SearchRoutes(new(padded, "Chosen", .8));
        var constrained = truncated.Analysis.Runs.Single();
        check(constrained.StorageLimited && constrained.StoredCount > 0 && constrained.StoredCount < run.StoredCount
            && constrained.Routes.Select(item => string.Join(",", item.InputTicks))
                .SequenceEqual(run.Routes.Take(constrained.StoredCount).Select(item => string.Join(",", item.InputTicks)))
            && Encoding.UTF8.GetByteCount(truncated.SourceJson) <= TrackJson.MaximumBytes,
            "explicit file-size limit retains the fastest prefix and every original source byte");
        bool fullRejected = false;
        try { service.SearchRoutes(new(truncated.SourceJson, "Chosen", 1)); }
        catch (ArgumentException error) when (error.Message.Contains("2 МиБ")) { fullRejected = true; }
        check(fullRejected, "an existing group is never discarded to make room for another search");

        return new { exactTiny = tiny.StoredCount, chosen = run.StoredCount, fastFlip = fast.StoredCount,
            limitedStorage = constrained.StoredCount, combinedBytes = Encoding.UTF8.GetByteCount(twoProfiles.SourceJson),
            progress = updates, interruptedProgress = interrupted, stateBudgetProgress = budgetUpdates };
    }

    static ReplayResult Replay(WorkshopService service, string json, RouteSearchRun run, int[] ticks) => service.Replay(
        new(json, run.Profile, run.WarningDuration, "time", Array.ConvertAll(ticks, tick => tick * run.StepMs / 1000d)));
}
