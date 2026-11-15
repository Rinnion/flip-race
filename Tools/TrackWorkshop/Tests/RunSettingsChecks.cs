using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;
using TrackWorkshop;

internal static class RunSettingsChecks
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static object Run(WorkshopService service, Action<bool, string> check)
    {
        var track = new Track(4000, Array.Empty<Obstacle>())
        {
            Id = "running-settings-test", Name = "Running settings test",
            RiskZones = new[] { new RiskZone { Id = "finish", Kind = "finish", X = 2500, Y = 226 } }
        };
        string source = TrackJson.Write(new TrackPackage(track));
        var linear = new WorkshopSurfaceRunSettings(true, 5, "Linear", 40, 200, true);
        var exponential = new WorkshopSurfaceRunSettings(true, 2, "Exponential", 20, 50, false);
        var noInputs = new ReplayRequest(source, "Chosen", .8, "time", Array.Empty<double>());
        var off = service.Replay(noInputs);
        var explicitOff = service.Replay(noInputs with { SurfaceRunSettings = exponential with { Enabled = false } });
        var on = service.Replay(noInputs with { SurfaceRunSettings = linear });
        var otherwise = service.Replay(noInputs with { SurfaceRunSettings = exponential });
        check(off.SurfaceRunSettings == WorkshopSurfaceRunSettings.Legacy &&
            explicitOff.SurfaceRunSettings == exponential with { Enabled = false } &&
            off.Samples.SequenceEqual(explicitOff.Samples), "legacy requests stay OFF and an explicit disabled configuration does not change physics");
        check(on.SurfaceRunSettings == linear && otherwise.SurfaceRunSettings == exponential &&
            on.Summary.FinishTime < otherwise.Summary.FinishTime && otherwise.Summary.FinishTime < off.Summary.FinishTime,
            "different actual surface settings change finish time");

        var highSpeed = on.Samples.First(sample => sample.FinishBoost && sample.Speed > 500);
        double pressX = highSpeed.X + 1;
        var byX = service.Replay(noInputs with { Axis = "distance", Inputs = new[] { pressX }, SurfaceRunSettings = linear });
        var edge = byX.Inputs.Single();
        check(edge.Status == "accepted" && Math.Abs(edge.X.Value - pressX) < 1e-7 && highSpeed.Speed > RaceConfig.FinishBoostSpeed,
            "X scheduler locates a swept edge above the base 440-unit speed bound");
        var byTime = service.Replay(noInputs with { Inputs = new[] { edge.Time.Value }, SurfaceRunSettings = linear });
        check(byTime.Inputs.Single().Status == edge.Status && Math.Abs(byTime.Inputs.Single().X.Value-edge.X.Value) < 1e-7 &&
            Math.Abs(byTime.Summary.FinishTime.Value - byX.Summary.FinishTime.Value) < 1e-7,
            "accelerated X replay and exact seconds replay agree");

        var report = new List<object>();
        string shortSource = TrackJson.Write(new TrackPackage(new Track(500, Array.Empty<Obstacle>())
            { Id = "running-search", Name = "Running search" }));
        foreach (var (profile, settings) in new[] { ("Chosen", linear), ("FastFlip", exponential) })
        {
            var search = service.SearchRoutes(new(shortSource, profile, .6, settings));
            var run = search.Analysis.Runs.Single();
            check(run.SurfaceRun && run.SurfaceRunSettings == settings && run.Routes.Length > 0, "search records exact enabled configuration");
            foreach (var route in run.Routes)
            {
                var replay = service.Replay(new(shortSource, run.Profile, run.WarningDuration, run.Axis,
                    route.InputTicks.Select(tick => tick * run.StepMs / 1000d).ToArray(), run.SurfaceRunSettings));
                check(replay.Summary.FinishTime == route.FinishTime && replay.Summary.AcceptedInputs == route.InputTicks.Length &&
                    replay.Summary.Collisions == route.Collisions && replay.Summary.Boosts == route.Boosts &&
                    replay.Summary.Slows == route.Slows && replay.Summary.FinishBoosts == route.FinishBoosts,
                    "every stored accelerated route reproduces exact finish, legal tape and outcomes");
            }
            var reopened = JsonNode.Parse(search.SourceJson);
            reopened["workshopAnalysis"]["groups"][0]["settings"].AsObject().Remove("surfaceRunSettings");
            check(Inspect(reopened.ToJsonString()).Status == "invalid", "ON route cannot omit any settings");
            report.Add(new { profile, settings, run.StoredCount, best=run.Routes[0].FinishTime });
        }
        string tinySource = shortSource.Replace("\"length\": 500", "\"length\": 75");
        check(WorkshopService.Read(tinySource).Package.Track.Length == 75, "grouping fixture uses the short track");
        var legacy = service.SearchRoutes(new(tinySource, "Chosen", .8));
        var fastLegacy = service.SearchRoutes(new(tinySource, "FastFlip", .6, linear));
        var legacyJson = JsonNode.Parse(tinySource);
        var legacyOffRun = JsonSerializer.SerializeToNode(legacy.Analysis.Runs.Single(), Json).AsObject();
        legacyOffRun.Remove("surfaceRunSettings");
        legacyOffRun["futureRunField"] = "saved";
        var legacyOnRun = JsonSerializer.SerializeToNode(fastLegacy.Analysis.Runs.Single(), Json);
        legacyOnRun["surfaceRunSettings"]["futureSurfaceField"] = "retained";
        legacyJson["workshopAnalysis"] = new JsonObject
        {
            ["version"] = 1,
            ["runs"] = new JsonArray(legacyOffRun, legacyOnRun),
            ["futureRootField"] = 27
        };
        var restored = Inspect(legacyJson.ToJsonString());
        check(restored.Status == "ready" && restored.Analysis.Version == 2 && restored.Analysis.Runs.Length == 2 &&
            restored.Analysis.Runs[0].SurfaceRunSettings == WorkshopSurfaceRunSettings.Legacy &&
            restored.Analysis.Runs[1].SurfaceRunSettings == linear,
            "0.5.12 metadata migrates to explicit OFF defaults in the response");

        var migrated = service.SearchRoutes(new(legacyJson.ToJsonString(), "Chosen", .8, linear));
        var groups = JsonNode.Parse(migrated.SourceJson)["workshopAnalysis"];
        check(groups["version"].GetValue<int>() == 2 && groups["runs"] == null &&
            groups["futureRootField"].GetValue<int>() == 27 &&
            groups["groups"][0]["runs"][0]["futureRunField"].GetValue<string>() == "saved" &&
            groups["groups"][1]["settings"]["surfaceRunSettings"]["futureSurfaceField"].GetValue<string>() == "retained" &&
            migrated.Analysis.Runs.Length == 3 && migrated.Analysis.Runs[0].Routes.Length == restored.Analysis.Runs[0].Routes.Length &&
            migrated.Analysis.Runs[1].Routes.Length == restored.Analysis.Runs[1].Routes.Length,
            "next search migrates legacy runs and extensions without replacing a different setting of the same profile");

        var differentWarning = service.SearchRoutes(new(migrated.SourceJson, "Chosen", 1, linear));
        var differentCurve = service.SearchRoutes(new(differentWarning.SourceJson, "Chosen", 1, exponential));
        var disabled = exponential with { Enabled = false };
        var distinctOff = service.SearchRoutes(new(differentCurve.SourceJson, "Chosen", .8, disabled));
        var requests = distinctOff.Analysis.Runs.Where(run => run.Profile == "Chosen")
            .Select(run => (run.WarningDuration, run.SurfaceRunSettings)).ToArray();
        check(requests.SequenceEqual(new[] { (.8, WorkshopSurfaceRunSettings.Legacy), (.8, linear),
            (1d, linear), (1d, exponential), (.8, disabled) }),
            "each exact same-profile configuration survives, including different disabled settings");

        var existing = JsonNode.Parse(distinctOff.SourceJson);
        var existingGroups = existing["workshopAnalysis"]["groups"].AsArray();
        existingGroups[4]["runs"][0]["elapsedMilliseconds"] = long.MaxValue;
        var replacement = service.SearchRoutes(new(existing.ToJsonString(), "Chosen", 1, exponential));
        var replacedGroups = JsonNode.Parse(replacement.SourceJson)["workshopAnalysis"]["groups"].AsArray();
        check(replacedGroups.Count == existingGroups.Count &&
            replacedGroups[4]["runs"][0]["elapsedMilliseconds"].GetValue<long>() != long.MaxValue &&
            Enumerable.Range(0, existingGroups.Count).Where(index => index != 4)
                .All(index => JsonNode.DeepEquals(existingGroups[index], replacedGroups[index])),
            "re-search replaces the exact matching group and leaves every other saved group intact");

        legacyOffRun["surfaceRunSettings"] = JsonSerializer.SerializeToNode(linear, Json);
        check(Inspect(legacyJson.ToJsonString()).Status == "invalid", "conflicting legacy flag and actual settings rejected");

        foreach (var invalid in new[] { linear with { RatePercent = 6 }, linear with { Curve = "0" },
            linear with { CapPercent = 25 }, linear with { AirDecayPercent = 150 } })
        {
            bool rejected = false;
            try { service.Replay(noInputs with { SurfaceRunSettings = invalid }); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "out-of-game setting rejected");
        }
        return new { off=off.Summary.FinishTime, on=on.Summary.FinishTime, distinctCurve=otherwise.Summary.FinishTime,
            highSpeed=highSpeed.Speed, checkedAtX=pressX, profiles=report };

        RouteAnalysisStatus Inspect(string json)
        {
            var preview = WorkshopService.Read(json);
            return preview.Document.AnalysisFor(preview.Package);
        }
    }
}
