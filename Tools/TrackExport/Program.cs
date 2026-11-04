using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlipRace.Core;

// Export tool only. Production Track/physics/bot classes are read and executed unchanged.
internal static class Program
{
    static readonly JsonSerializerOptions Json = new() {
        WriteIndented = true, IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
    static object Stored(object value)
    {
        if (value == null) return null;
        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) return value;
        if (value is IEnumerable sequence) return sequence.Cast<object>().Select(Stored).ToArray();
        return type.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(f => JsonNamingPolicy.CamelCase.ConvertName(f.Name), f => Stored(f.GetValue(value)));
    }
    static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    static void Save(string folder, string name, object value)
    {
        string path = Path.Combine(folder, name);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(file, value, Json);
    }
    static void MoveTo(GameState game, double target)
    {
        var p = game.Players[0];
        if (target < p.Distance - 1e-7) throw new Exception("Out-of-order input");
        int guard = 0;
        while (p.Distance < target - 1e-8 && game.Phase == "racing")
        {
            if (++guard > 100000) throw new Exception("Stalled input");
            if (p.Recovering > 0) { RaceSimulation.Advance(game, Math.Min(1d/120, p.Recovering)); continue; }
            double dt = Math.Min(1d/120, (target - p.Distance) / p.CurrentSpeed);
            if (p.ActiveEffect != null) dt = Math.Min(dt, p.ActiveEffect.Remaining);
            var trial = RaceSnapshot.Clone(game);
            RaceSimulation.Advance(trial, dt);
            if (trial.Players[0].Distance > target + 1e-8)
            {
                double lo = 0, hi = dt;
                for (int n = 0; n < 45; n++) {
                    double mid = (lo + hi) / 2;
                    trial = RaceSnapshot.Clone(game); RaceSimulation.Advance(trial, mid);
                    if (trial.Players[0].Distance < target) lo = mid; else hi = mid;
                }
                dt = (lo + hi) / 2;
            }
            RaceSimulation.Advance(game, dt);
        }
        if (Math.Abs(p.Distance - target) > 1e-6) throw new Exception("Exact distance not reached");
    }
    static object Replay(Track track, FlightSettings settings, string profile, string routeName, double[] inputs)
    {
        var game = RaceSimulation.CreateTrial(track, settings);
        RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1);
        RaceSimulation.Advance(game, RaceConfig.CountdownDuration);
        var edges = new List<object>(); int rejected = 0; string error = null;
        try {
            foreach (double x in inputs) {
                MoveTo(game, x); var p = game.Players[0];
                edges.Add(new { x=p.Distance, time=game.Elapsed, centerY=RaceSimulation.GetPlayerCenterY(p),
                    verticalSpeed=p.VerticalSpeed, support=p.Support.ToString(), side=p.SupportSide,
                    airborne=p.Flipping, airFlipAvailable=p.AirFlipAvailable, speed=p.CurrentSpeed });
                bool first = RaceSimulation.Press(game, 0), second = RaceSimulation.Press(game, 1);
                if (!first || !second) rejected++;
            }
            int guard=0;
            while (game.Phase=="racing" && guard++ < 100000) RaceSimulation.Advance(game, 1d/120);
        } catch (Exception e) { error=e.Message; }
        var player=game.Players[0];
        return new { profile, route=routeName, inputCount=inputs.Length, rejected, error,
            clean=error==null && rejected==0 && player.Collisions==0 && player.Status==PlayerStatus.Finished,
            collisions=player.Collisions, finishTime=player.FinishTime,
            ordinaryBoosts=player.BoostsEarned, completedBoosts=player.CompletedBoosts,
            finishBoosts=player.FinishBoostsEarned, finishBoostElapsed=player.FinishBoostElapsed,
            slows=player.SlowsEarned, coins=player.CoinsEarned,
            collected=player.CollectedRiskIds.Order().ToArray(), successful=player.SuccessfulRiskIds.Order().ToArray(), edges };
    }
    static void Main(string[] args)
    {
        string folder = Path.GetFullPath(args.Length>0 ? args[0] : "../flip-race/docs/authoring/mixed-tricks");
        Directory.CreateDirectory(folder);
        string[] sources=Directory.GetFiles("Assets/Scripts/Core", "*.cs").Order().ToArray();
        var before=sources.ToDictionary(x=>x,Hash);
        var track=TracksMixed.Get();
        object plain=Stored(track);
        string serialized=JsonSerializer.Serialize(plain,Json);
        var restored=JsonSerializer.Deserialize<Track>(serialized,Json);
        if (JsonSerializer.Serialize(Stored(restored),Json)!=serialized) throw new Exception("Stored-field roundtrip differs");
        Save(folder,"mixed-tricks.track.json",plain);
        var profiles=new[]{(id:"Chosen",flight:FlightPresets.Chosen),(id:"FastFlip",flight:FlightPresets.FastFlip)};
        var maps=profiles.Select(pair=> {
            var map=StochasticTrackMap.Create(track,pair.flight);
            if(map.Opportunities.Length!=21)throw new Exception("Unexpected goal count");
            return new { profile=pair.id, map.ProfileId,map.BaseTime,map.MasterTime,map.BonusGain,map.CollisionCostSum,
                opportunities=map.Opportunities, referenceInputs=map.ReferenceInputs,
                levels=Enum.GetValues<BotDifficulty>().Select(d=>Stored(StochasticBotSettings.CreateInitial(track,pair.flight,d,1))).ToArray() };
        }).ToArray();
        Save(folder,"mixed-tricks.bot-map.json",new {
            trackId=track.Id, kind="stochastic-opportunities-and-reference", legacyTapeCatalogUsed=false,
            controllerLimits=new { StochasticBotController.MaxCandidatesPerPlanningCall,
                StochasticBotController.MaxMasterTimingCandidates, StochasticBotController.MaxHorizonDistance,
                StochasticBotController.MaxInputs, StochasticBotController.DecisionLeadDistance,
                StochasticBotController.MaxForecastSlice,StochasticBotController.MaxSlice,
                StochasticBotController.MaxForecastStepsPerCandidate }, profiles=maps });
        Save(folder,"mixed-tricks.context.json",new {
            exportedUtc=DateTime.UtcNow, sourceRevision=args.Length>1?args[1]:"See source hashes", format="Stored C# fields exported as camelCase; not a runtime JSON import format",
            world=new { speed=RaceConfig.Speed,corridorHeight=RaceConfig.CorridorHeight,
                playerRadius=RaceConfig.PlayerRadius, recoveryDuration=RaceConfig.RecoveryDuration,
                baseDuration=track.Length/RaceConfig.Speed, boostMultiplier=RaceConfig.BoostMultiplier,
                boostDuration=RaceConfig.BoostDuration, boostTimeGain=RaceConfig.BoostTimeGain,
                slowMultiplier=RaceConfig.SlowMultiplier, slowDuration=RaceConfig.SlowDuration,
                finishBoostSpeed=RaceConfig.FinishBoostSpeed, finishBoostDuration=RaceConfig.FinishBoostDuration,
                finishTargetStrokeWidth=FinishTargetGeometry.StrokeWidth,
                finishTargetSegments=Enumerable.Range(0,FinishTargetGeometry.SegmentCount).Select(i=>Stored(FinishTargetGeometry.GetSegment(i))).ToArray() },
            profiles=profiles.Select(pair=>new {id=pair.id, values=Stored(pair.flight)}).ToArray(),
            authorWitnesses=profiles.Select(pair=>new {profile=pair.id,safe=TracksMixed.SafeRoute(pair.flight),
                full=TracksMixed.FullRoute(pair.flight), coinsBoth=TracksMixed.CoinRoute(pair.flight),
                negativeMask3=TracksMixed.NegativeRoute(pair.flight), contentTrapMask3=TracksMixed.ContentTrapRoute(pair.flight)}).ToArray(),
            physicalBands=track.Obstacles.Select(o=>new {o.Id,bands=Enumerable.Range(0,2).Select(i=> {
                bool found=ObstacleGeometry.TryGetBand(o,i,out var band); return found?Stored(band):null;
            }).Where(x=>x!=null).ToArray()}).ToArray(),
            sources=before.Select(pair=>new {path=pair.Key.Replace('\\','/'),sha256=pair.Value}).ToArray() });
        var checks=new List<object>();
        foreach(var pair in profiles) {
            checks.Add(Replay(track,pair.flight,pair.id,"safe",TracksMixed.SafeRoute(pair.flight)));
            checks.Add(Replay(track,pair.flight,pair.id,"full",TracksMixed.FullRoute(pair.flight)));
            checks.Add(Replay(track,pair.flight,pair.id,"bot-reference",StochasticTrackMap.Create(track,pair.flight).ReferenceInputs));
        }
        foreach (var entry in before) if(Hash(entry.Key)!=entry.Value)throw new Exception("Production changed while exporting");
        Save(folder,"mixed-tricks.export-checks.json",new {
            checksScope="Stored-field JSON roundtrip, unchanged Core hashes, six actual production Press/Advance replays at max 1/120s. No timing robustness, all-FPS, bot calibration, or playtest claim.",
            storedFieldRoundtrip=true, productionHashesUnchanged=true, obstacles=track.Obstacles.Length,
            rewardZones=track.RiskZones.Length, opportunityCount=maps[0].opportunities.Length, replays=checks });
        Console.WriteLine($"Exported {track.Id}: {track.Obstacles.Length} obstacles, {track.RiskZones.Length} zones, 21 bot goals per profile; stored-field roundtrip OK; {checks.Count} route replays.");
    }
}
