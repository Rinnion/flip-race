using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

static class Program
{
    static int checks;
    static void Require(bool condition, string reason) { checks++; if (!condition) throw new InvalidOperationException(reason); }
    sealed class ActionEdge { public double Distance { get; set; } public double Time { get; set; } public bool Accepted { get; set; } }
    sealed class Actual
    {
        public GameState Game; public List<ActionEdge> Actions = new List<ActionEdge>(); public List<object> Events = new List<object>();
        public double MinMargin = double.PositiveInfinity; public int Advances; public Dictionary<string,double> HitY = new Dictionary<string,double>();
    }
    static Actual Run(Track track, FlightSettings flight, double[] distances, PartyCamera camera)
    {
        var r = new Actual { Game = RaceSimulation.CreateSolo(track, flight, camera, -288.8, 1520) };
        var g = r.Game; Require(RaceSimulation.Press(g, 0), "ready rejected");
        RaceSimulation.Advance(g, RaceConfig.CountdownDuration); Require(g.Phase == "racing", "countdown"); var p = g.Players[0];
        var previousHit = new HashSet<string>(); var previousSuccess = new HashSet<string>(); var previousFailed = new HashSet<string>();
        Action<double> advance = dt => {
            double before = g.Elapsed, beforeDistance = p.Distance;
            RaceSimulation.Advance(g, dt); r.Advances++;
            r.MinMargin = Math.Min(r.MinMargin, p.Distance + RaceConfig.PlayerRadius - g.PartyCameraLeft);
            foreach (string id in p.HitObstacleIds) if (previousHit.Add(id)) { r.HitY.Add(id,p.Y); r.Events.Add(new { kind = "hit", id, time = p.LastHit.At, y = p.Y, distanceInterval = new[] { beforeDistance, p.Distance } }); }
            foreach (string id in p.SuccessfulRiskIds) if (previousSuccess.Add(id)) r.Events.Add(new { kind = "success", id, timeInterval = new[] { before, g.Elapsed }, distanceInterval = new[] { beforeDistance, p.Distance } });
            foreach (string id in p.FailedRiskIds) if (previousFailed.Add(id)) r.Events.Add(new { kind = "failed", id, timeInterval = new[] { before, g.Elapsed }, distanceInterval = new[] { beforeDistance, p.Distance } });
        };
        foreach (double target in distances)
        {
            int guard = 0;
            while (g.Phase == "racing" && target - p.Distance > 2e-7)
            {
                advance(p.Recovering > 0 ? Math.Min(1d / 120, p.Recovering) : Math.Min(1d / 120, Math.Max(1e-9, (target - p.Distance) / 440)));
                if (++guard > 200000) throw new InvalidOperationException("distance loop");
            }
            if (g.Phase != "racing") break;
            r.Actions.Add(new ActionEdge { Distance = p.Distance, Time = g.Elapsed, Accepted = RaceSimulation.Press(g, 0) });
        }
        while (g.Phase == "racing" && g.Elapsed < 150) advance(1d / 120);
        Require(g.Phase != "racing", "terminal timeout"); return r;
    }
    static object Metrics(Actual r)
    {
        var p = r.Game.Players[0];
        return new { status = p.Status.ToString(), time = p.TerminalTime, distance = p.Distance, collisions = p.Collisions,
            hitObstacleIds = p.HitObstacleIds.OrderBy(x => x).ToArray(), collectedRiskIds = p.CollectedRiskIds.OrderBy(x => x).ToArray(),
            successfulRiskIds = p.SuccessfulRiskIds.OrderBy(x => x).ToArray(), failedRiskIds = p.FailedRiskIds.OrderBy(x => x).ToArray(),
            negativeRiskIds = p.NegativeRiskIds.OrderBy(x => x).ToArray(), p.CompletedBoosts, p.CompletedSlows,
            activeEffect = p.ActiveEffect == null ? null : p.ActiveEffect.RingId, pendingCount = p.PendingEffects.Count,
            p.FinishBoostsEarned, p.FinishBoostElapsed, p.FinishBoostDistanceGained,
            accepted = r.Actions.Count(a => a.Accepted), rejected = r.Actions.Count(a => !a.Accepted), minMargin = r.MinMargin,
            advances = r.Advances, actions = r.Actions, events = r.Events };
    }
    static string Sha(string file) { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))); }
    static Dictionary<string, string> Hashes(string root)
    {
        var files = Directory.GetFiles(Path.Combine(root, "Assets/Scripts/Core"), "*.cs")
            .Where(f => !Path.GetFileName(f).StartsWith("Bot", StringComparison.Ordinal))
            .Concat(new[] { Path.Combine(root, "Tools/StochasticMapCheck/Program.cs"), Path.Combine(root, "Tools/StochasticMapCheck/StochasticMapCheck.csproj") });
        return files.OrderBy(x => x).ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), Sha);
    }
    static void Main(string[] args)
    {
        string root = Directory.GetCurrentDirectory(); if (!Directory.Exists(Path.Combine(root, "Assets/Scripts/Core"))) throw new Exception("Run from Unity project root.");
        var before = Hashes(root); var pairs = new List<object>(); var directed = new List<object>();
        foreach (var track in RaceTracks.All)
        foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
        {
            var map = StochasticTrackMap.Create(track, flight); var opportunities = map.Opportunities; var inputs = map.ReferenceInputs;
            Require(opportunities.Select(x => x.Id).Distinct().Count() == opportunities.Length, "duplicate canonical opportunity");
            Require(opportunities.Count(x => x.Kind == BotOpportunityKind.Obstacle) == track.Obstacles.Select(x => x.Id).Distinct().Count(), "logical obstacle count");
            Require(opportunities.Count(x => x.Kind == BotOpportunityKind.Positive) == 9, "ordinary positive count");
            Require(opportunities.Count(x => x.Kind == BotOpportunityKind.Finish) == 1, "finish count");
            Require(!opportunities.Any(x => x.Id.Contains("negative:") || x.Id.Contains("coin:")), "excluded opportunities");
            Require(inputs.Length > 1 && inputs.Zip(inputs.Skip(1), (a, b) => a < b).All(x => x), "strictly ordered reference inputs");
            var copy = map.ReferenceInputs; copy[0] = -123; Require(map.ReferenceInputs[0] == inputs[0], "input defensive copy");
            var copiedItems = map.Opportunities; copiedItems[0] = null; Require(map.Opportunities[0] != null, "opportunity defensive copy");
            Require(typeof(BotOpportunity).GetFields(BindingFlags.Public | BindingFlags.Instance).All(f => f.IsInitOnly), "mutable opportunity field");
            var cameras = new List<object>();
            foreach (var camera in new[] { PartyCamera.Lagger, PartyCamera.Constant })
            {
                var actual = Run(track, flight, inputs, camera); var p = actual.Game.Players[0];
                Require(p.Status == PlayerStatus.Finished, track.Id + " " + map.ProfileId + " " + camera + " finish");
                Require(p.Collisions == 0 && p.HitObstacleIds.Count == 0, track.Id + " " + map.ProfileId + " " + camera + " clean witness collisions=" + p.Collisions + " ids=" + string.Join(",",p.HitObstacleIds));
                Require(actual.Actions.Count == inputs.Length && actual.Actions.All(x => x.Accepted), "witness input rejection/truncation");
                Require(track.RiskZones.Where(z => !z.IsNegative && !z.IsCoin).All(z => p.SuccessfulRiskIds.Contains(z.Id)), "missing positive/finish goal");
                Require(p.FailedRiskIds.Count == 0 && p.NegativeRiskIds.Count == 0, "failed/negative reference goal");
                Require(p.CompletedBoosts == 9 && p.CompletedSlows == 0 && p.ActiveEffect == null && p.PendingEffects.Count == 0, "incomplete ordinary effects");
                Require(p.FinishBoostsEarned == 1 && Math.Abs(p.FinishBoostElapsed - .25) < 1e-9 && Math.Abs(p.FinishBoostDistanceGained - 55) < 1e-7, "incomplete finish pulse");
                Require(Math.Abs(p.TerminalTime.Value - map.MasterTime) < 1e-8, "master time model");
                var baseline = Run(new Track(track.Length, Array.Empty<Obstacle>()), flight, Array.Empty<double>(), camera);
                Require(baseline.Game.Players[0].Status == PlayerStatus.Finished && Math.Abs(baseline.Game.Players[0].TerminalTime.Value - map.BaseTime) < 1e-8, "zero-reward/no-collision base time");
                cameras.Add(new { camera = camera.ToString(), actual = Metrics(actual), baselineActual = Metrics(baseline) });
            }
            var probabilities = new List<object>();
            foreach (double q in new[] { .5, .65, .75, .9, 1d })
            {
                double hit = map.BonusGain / map.CollisionCostSum * (1 - q);
                bool supported = map.BonusGain > 0 && map.CollisionCostSum > 0 && hit >= 0 && hit <= 1;
                Require(supported, "unsupported initial probability (no clamp)");
                probabilities.Add(new { q, pCollect = q, pHit = hit, targetMeanTime = map.BaseTime - map.BonusGain * (2 * q - 1), supported });
            }
            pairs.Add(new { trackId = track.Id, profile = map.ProfileId, baseTime = map.BaseTime, masterTime = map.MasterTime,
                bonusGain = map.BonusGain, collisionCostSum = map.CollisionCostSum, opportunities = opportunities.Select(o => new { id = o.Id, kind = o.Kind.ToString(), position = o.Position, endPosition = o.EndPosition, obstacleId = o.ObstacleId }).ToArray(), referenceInputs = inputs, probabilities, cameras });
            foreach (var obstacle in track.Obstacles.GroupBy(x => x.Id).Select(g => g.First()))
            foreach (bool ceiling in obstacle.Kind == "gate" ? new[] { false, true } : new[] { obstacle.Kind == "ceiling" })
            {
                var isolated = new Track(Math.Max(track.Length, ObstacleGeometry.Right(obstacle) + 1000), new[] { obstacle }) { Id = "directed:" + obstacle.Id };
                double[] approach = ceiling ? (flight.FirstAction == FirstAction.Jump ? new[] { 0d, 55d } : new[] { 0d }) : Array.Empty<double>();
                var actual = Run(isolated, flight, approach, PartyCamera.Lagger); var p = actual.Game.Players[0];
                double delay = p.TerminalTime.Value - isolated.Length / RaceConfig.Speed;
                Require(p.Status == PlayerStatus.Finished && p.Collisions == 1 && p.HitObstacleIds.SetEquals(new[] { obstacle.Id }), "directed single obstacle " + obstacle.Id + " ceiling=" + ceiling);
                Require(ceiling ? actual.HitY[obstacle.Id] < .5 : actual.HitY[obstacle.Id] > .5, "directed physical band approach " + obstacle.Id);
                Require(p.CompletedRecoveries == 1 && Math.Abs(delay - RaceConfig.RecoveryDuration) < 1e-8, "single hit penalty " + obstacle.Id);
                Require(actual.Actions.All(a => a.Accepted), "directed input rejected");
                directed.Add(new { trackId = track.Id, profile = map.ProfileId, obstacleId = obstacle.Id, approach = ceiling ? "ceiling" : "floor", delay, referenceInputs = approach, actual = Metrics(actual) });
            }
        }
        // Explicit rejection and no-clamp model diagnostics, not gameplay calibration.
        bool customRejected = false, courseRejected = false, zeroCRejected = false, outOfRangeRejected = false, inactiveRejected = false;
        try { StochasticTrackMap.Create(RaceTracks.All[0], FlightPresets.Moderate); } catch (NotSupportedException) { customRejected = true; }
        try { StochasticTrackMap.Create(new Track(7920, Array.Empty<Obstacle>()), FlightPresets.Chosen); } catch (NotSupportedException) { courseRejected = true; }
        try { StochasticTrackMap.Create(new Track(7920, Array.Empty<Obstacle>()) { Id = TracksFull.LongId, RiskZones = new[] { new RiskZone { Id = "test-finish", Kind = "finish", X = 7780 } } }, FlightPresets.Chosen); } catch (NotSupportedException) { zeroCRejected = true; }
        try { StochasticTrackMap.Create(new Track(7920, new[] { RaceTracks.All[0].Obstacles[0] }) { Id = TracksFull.LongId, RiskZones = Enumerable.Range(0,9).Select(i => new RiskZone { Id = "test-positive-" + i, Kind = "instant", X = 100 + i * 100 }).Concat(new[] { new RiskZone { Id = "test-finish", Kind = "finish", X = 7780 } }).ToArray() }, FlightPresets.Chosen); } catch (NotSupportedException) { outOfRangeRejected = true; }
        try { StochasticTrackMap.Create(RaceTracks.All[0], new FlightSettings(FirstAction.Jump, MotionLaw.Gravity, AirRepeatLimit.Unlimited, 662.5, 0, 350, 266, 112)); } catch (NotSupportedException) { inactiveRejected = true; }
        Require(StochasticTrackMap.Create(RaceTracks.All[0], new FlightSettings(FirstAction.Jump, MotionLaw.Gravity, AirRepeatLimit.Unlimited, 662.5, 0, 350)).ProfileId == "Chosen", "exact value clone rejected");
        Require(customRejected && courseRejected && zeroCRejected && outOfRangeRejected && inactiveRejected, "unsupported map input accepted");
        var unsupportedModels = new[] {
            new { supported = false, bonusGain = 0d, collisionCostSum = 6d, q = .5, reason = "B<=0" },
            new { supported = false, bonusGain = 2.95, collisionCostSum = 0d, q = .5, reason = "C<=0" },
            new { supported = false, bonusGain = 3d, collisionCostSum = .5, q = .5, reason = "pHit>1 (unsupported, not clamped)" }
        };
        var after = Hashes(root); bool stable = before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var h) && h == kv.Value);
        Require(stable, "source changed during verification");
        string mapSource = File.ReadAllText(Path.Combine(root, "Assets/Scripts/Core/StochasticTrackMap.cs"));
        Require(!mapSource.Contains("BotPlanCatalog") && !mapSource.Contains("Probe.") && !mapSource.Contains("RaceSimulation."), "map must not use prior catalogue/probes/simulation");
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        object BuildReport() => new { status = "PASS", requirements = new[] { "BSTO01", "BSTO02", "CONTROL", "REWARD", "FINISH", "GATE" },
            checks, sourceStable = stable, sourceHashes = before, cleanRuns = 20, emptyBaselineRuns = 20, directedSingleCollisionRuns = directed.Count,
            simulation = "Ordinary CreateSolo/ready Press/Advance countdown, then only real Press and Advance; no state injection. Max step 1/120. This is independent physical validation, not runtime action replay.",
            timeModel = "Tbase=L/220; B=Tbase-Tmaster; C=sum one actual recovery delay per logical obstacle ID; pCollect=q; pHit=(B/C)*(1-q); Tq=Tbase-B*(2q-1). No calibration adjustment or clamp.",
            bounds = "Obstacle Position=physical left, EndPosition=physical right+14+1e-7. Positive Position=centerX; EndPosition=max(circle-contact right, pending ClearX, linked physical right+14+1e-7); landing uses actual landing interval right. Finish EndPosition=centerX+26 is last possible stroke contact, not pulse completion.",
            catalogueDependency = "StochasticTrackMap has no old catalogue/helper/simulation references. Checker compiles production Core unchanged; old catalogue methods are never called by map or solo simulation.",
            unsupportedModels, pairs, directedCollisions = directed };
        // Validate the serialized schema, not only the in-memory objects: readonly
        // public fields are not emitted by System.Text.Json's default options.
        string preliminaryJson = JsonSerializer.Serialize(BuildReport(), options);
        using (var document = JsonDocument.Parse(preliminaryJson))
        {
            var exportedPairs = document.RootElement.GetProperty("pairs");
            Require(exportedPairs.GetArrayLength() == 10, "exported pair count");
            var pairKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exportedPair in exportedPairs.EnumerateArray())
            {
                string trackId = exportedPair.GetProperty("trackId").GetString();
                string profile = exportedPair.GetProperty("profile").GetString();
                Require(profile == "Chosen" || profile == "FastFlip", "exported exact profile");
                Require(pairKeys.Add(trackId + "/" + profile), "duplicate exported pair");
                Track sourceTrack = RaceTracks.All.Single(t => t.Id == trackId);
                var actualMap = StochasticTrackMap.Create(sourceTrack, profile == "Chosen" ? FlightPresets.Chosen : FlightPresets.FastFlip);
                var actualOpportunities = actualMap.Opportunities;
                var exportedOpportunities = exportedPair.GetProperty("opportunities");
                Require(exportedOpportunities.ValueKind == JsonValueKind.Array && exportedOpportunities.GetArrayLength() == actualOpportunities.Length, "exported opportunity count does not match actual map");
                int index = 0;
                foreach (var exported in exportedOpportunities.EnumerateArray())
                {
                    var actual = actualOpportunities[index++];
                    Require(exported.ValueKind == JsonValueKind.Object && exported.EnumerateObject().Count() == 5, "opportunity must export exactly five inspectable fields");
                    foreach (string field in new[] { "id", "kind", "position", "endPosition", "obstacleId" })
                        Require(exported.TryGetProperty(field, out _), "missing exported opportunity field " + field);
                    Require(exported.GetProperty("id").ValueKind == JsonValueKind.String && exported.GetProperty("id").GetString() == actual.Id, "exported id differs from actual map");
                    Require(exported.GetProperty("kind").ValueKind == JsonValueKind.String && exported.GetProperty("kind").GetString() == actual.Kind.ToString(), "exported kind differs from actual map");
                    Require(exported.GetProperty("position").ValueKind == JsonValueKind.Number && double.IsFinite(exported.GetProperty("position").GetDouble()) && exported.GetProperty("position").GetDouble() == actual.Position, "exported position differs from actual map");
                    Require(exported.GetProperty("endPosition").ValueKind == JsonValueKind.Number && double.IsFinite(exported.GetProperty("endPosition").GetDouble()) && exported.GetProperty("endPosition").GetDouble() == actual.EndPosition, "exported endPosition differs from actual map");
                    var linkedId = exported.GetProperty("obstacleId");
                    Require(actual.ObstacleId == null ? linkedId.ValueKind == JsonValueKind.Null : linkedId.ValueKind == JsonValueKind.String && linkedId.GetString() == actual.ObstacleId, "exported obstacleId differs from actual map");
                }
            }
        }
        Directory.CreateDirectory(Path.Combine(root, "Verification"));
        File.WriteAllText(Path.Combine(root, "Verification/stochastic-bots-map.json"), JsonSerializer.Serialize(BuildReport(), options));
        File.WriteAllText(Path.Combine(root, "Verification/stochastic-bots-map.txt"),
            "PASS — BSTO01/BSTO02 immutable map and initial time model\n" + checks + " assertions; 20 clean full-reference ordinary solo simulations (10 pairs x two cameras); 20 empty-track baseline simulations; " + directed.Count + " isolated actual collision simulations.\n" +
            "Every clean reference: finished; all logical goals; nine completed ordinary effects; one full .25 s finish pulse; zero hits/rejections/negative effects/pending effects.\n" +
            "All pairs Tbase=36, Tmaster=33.05, B=2.95 s. C=6 s for Long/Short/Flow, 10.5 s for Cascade, 5.5 s for Mixed. Gate halves tested on both surface approaches but counted once per logical ID.\n" +
            "No online choices/calibration/statistical acceptance claimed. Initial q values .5/.65/.75/.9/1 are supported without clamping; B<=0/C<=0/out-of-range are explicitly unsupported.\n" +
            "Opportunity export uses five explicit DTO fields (id/kind/position/endPosition/obstacleId); serialized JSON round-trip matches every actual map entry in all ten pairs. Source hashes stable during this run, including current controller compilation provenance. Geometry and physics untouched. See JSON for exact inputs/actions/event bounds, terminal metrics, probability rows and source hashes.\n");
        Console.WriteLine("PASS checks=" + checks + " cleanRuns=20 collisionRuns=" + directed.Count + " sourceStable=" + stable);
    }
}