using System.Text;
using System.Text.Json.Nodes;
using FlipRace.Core;

namespace TrackWorkshop;

public sealed record AnalyzeRequest(string Json);
public sealed record ReplayRequest(string Json, string Profile, double WarningDuration, string Axis, double[] Inputs);
public sealed record CatalogEntry(string Id, string Name, string File, string[] Profiles);
public sealed record Band(int Index, double X, double Y, double Width, double Height);
public sealed record Stroke(double X1, double Y1, double X2, double Y2);
public sealed record Reward(string Id, string Kind, double X, double Y, double Radius, string ObstacleId,
    string Placement, bool FreeReward, double ClearX, string Shape, double? Left, double? Right, double? PlaneY,
    Stroke[] Segments, double? StrokeWidth);
public sealed record Sample(double Time, double X, double Y, double Speed, bool Flipping, double Recovering,
    string Support, int SupportSide, int Collisions, int Boosts, int Slows, int Coins, bool FinishBoost, string Status);
public sealed record InputResult(int Index, double Value, string Status, double? Time, double? X, double? Y, string Reason);
public sealed record ReplayEvent(string Type, string Id, double Time, double X, double Y, string Phase = null);
public sealed record HiddenState(string Id, string Phase, double? RevealedAt, double? SolidAt, bool ActivationPostponed);
public sealed record Summary(string Status, double? FinishTime, double Elapsed, bool CleanFinish, int Collisions,
    int Boosts, int InstantBoosts, int Slows, int Coins, int FinishBoosts, int AcceptedInputs, int IgnoredInputs,
    int UnreachedInputs, string[] CollectedIds, string[] SuccessfulIds, string[] FailedIds, string[] HitIds);
public sealed record ReplayResult(string Profile, string Axis, double WarningDuration, bool StaleReferences,
    string ReferenceError, List<Sample> Samples, List<InputResult> Inputs, List<ReplayEvent> Events,
    HiddenState[] HiddenStates, Summary Summary);

public sealed class WorkshopService
{
    public const double MaximumTime = 120, SampleStep = 1d / 120;
    readonly Dictionary<string, (CatalogEntry Entry, string Json)> catalog = new(StringComparer.Ordinal);
    public WorkshopService(string catalogDirectory)
    {
        if (!TrackJson.TryReadManifest(File.ReadAllText(Path.Combine(catalogDirectory, "index.json")), out var manifest, out var error))
            throw new InvalidOperationException(error);
        foreach (string file in manifest.Files)
        {
            string json = File.ReadAllText(Path.Combine(catalogDirectory, file));
            if (!TrackJson.TryRead(json, out var p, out error)) throw new InvalidOperationException(file + ": " + error);
            var entry = new CatalogEntry(p.Track.Id, p.Track.Name, file, p.References.Select(r => r.ProfileId).ToArray());
            if (!catalog.TryAdd(entry.Id, (entry, json))) throw new InvalidOperationException("Duplicate track ID.");
        }
    }
    public CatalogEntry[] Catalog() => catalog.Values.Select(v => v.Entry).ToArray();
    public string CatalogJson(string id) => catalog.TryGetValue(id, out var entry) ? entry.Json : null;

    sealed record Preview(TrackPackage Package, bool Stale, string ReferenceError, string CleanedJson);
    static Preview Read(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > TrackJson.MaximumBytes)
            throw new ArgumentException("JSON отсутствует или превышает 2 МиБ.");
        bool valid = TrackJson.TryRead(json, out var package, out string error);
        // Only a document whose entire non-reference content passes the exact
        // production validator is eligible for explicitly marked preview.
        TrackPackage bare;
        if (valid) bare = new TrackPackage(package.Track, package.ProfilePolicy, package.WarningMode);
        else
        {
            if (!error.StartsWith("$.botReferences", StringComparison.Ordinal)) throw new ArgumentException(error);
            JsonObject root;
            try { root = JsonNode.Parse(json, documentOptions: new() { MaxDepth = 32 }) as JsonObject; }
            catch (System.Text.Json.JsonException) { throw new ArgumentException(error); }
            if (root == null || !root.ContainsKey("botReferences")) throw new ArgumentException(error);
            root["botReferences"] = new JsonArray();
            if (!TrackJson.TryRead(root.ToJsonString(), out bare, out _)) throw new ArgumentException(error);
            package = bare;
        }
        return new Preview(package, !valid, valid ? null : error, TrackJson.Write(bare));
    }
    public object Analyze(AnalyzeRequest request)
    {
        if(request==null)throw new ArgumentException("Пустой запрос.");
        var parsed = Read(request.Json); var p = parsed.Package; var t = p.Track;
        return new {
            id=t.Id, name=t.Name, length=t.Length, corridorHeight=RaceConfig.CorridorHeight, playerRadius=RaceConfig.PlayerRadius,
            profilePolicy=p.ProfilePolicy==TrackProfilePolicy.Any?"any":"chosen-only",
            warningMode=p.WarningMode==TrackWarningMode.Selected?"selected":"authored", contentHash=p.ContentHash,
            gameValid=!parsed.Stale, staleReferences=parsed.Stale, referenceError=parsed.ReferenceError,
            warnings=parsed.Stale?new[]{"Эталоны не прошли проверку. Предпросмотр выполняется без botReferences; сохраните JSON без них."}:Array.Empty<string>(),
            canonicalJson=parsed.Stale?null:TrackJson.Write(p), cleanedJson=parsed.CleanedJson,
            references=p.References.Select(r=>new{profileId=r.ProfileId,trackHash=r.TrackHash,inputs=r.Inputs,rewardIds=r.RewardIds,bonusGain=r.BonusGain}),
            obstacles=t.Obstacles.Select(o=>new{id=o.Id,kind=o.Kind,x=o.X,width=o.Width,height=o.Height,gapTop=o.GapTop,gapBottom=o.GapBottom,
                topOffsetX=o.TopOffsetX,bottomOffsetX=o.BottomOffsetX,hidden=o.Hidden,hiddenRevealDistance=o.HiddenRevealDistance,
                hiddenWarningDuration=o.HiddenWarningDuration,bands=Bands(o)}),
            rewards=t.RiskZones.Select(z=>Shape(t,z)), cues=t.Cues.Select(c=>new{x=c.X,text=c.Text}),safeRoute=t.SafeRoute,riskRoute=t.RiskRoute
        };
    }
    static Band[] Bands(Obstacle o)
    {
        var bands=new List<Band>(); for(int i=0;i<2;i++) if(ObstacleGeometry.TryGetBand(o,i,out var b)) bands.Add(new(i,b.X,b.Y,b.Width,b.Height));
        return bands.ToArray();
    }
    static Reward Shape(Track track,RiskZone z)
    {
        double? left=null,right=null,plane=null; var strokes=new List<Stroke>();
        if(z.IsLanding)
        {
            var stone=track.Obstacles.Single(o=>o.Id==z.ObstacleId);
            left=Math.Max(stone.X,z.X-z.LandingWidth/2);right=Math.Min(stone.X+stone.Width,z.X+z.LandingWidth/2);
            plane=stone.Kind=="floor"?RaceConfig.CorridorHeight-stone.Height:stone.Height;
        }
        if(z.IsFinish) for(int i=0;i<FinishTargetGeometry.SegmentCount;i++) {var s=FinishTargetGeometry.GetSegment(i);strokes.Add(new(z.X+s.X1,z.Y+s.Y1,z.X+s.X2,z.Y+s.Y2));}
        return new(z.Id,z.Kind,z.X,z.Y,z.Radius,z.ObstacleId,z.Placement,z.FreeReward,z.ClearX,z.IsLanding?"landing":z.IsFinish?"finish":"circle",left,right,plane,
            strokes.ToArray(),z.IsFinish?FinishTargetGeometry.StrokeWidth:null);
    }

    public ReplayResult Replay(ReplayRequest request)
    {
        if(request==null)throw new ArgumentException("Пустой запрос.");
        var parsed=Read(request.Json);var p=parsed.Package;
        var flight=request.Profile=="Chosen"?FlightPresets.Chosen:request.Profile=="FastFlip"?FlightPresets.FastFlip:null;
        if(flight==null)throw new ArgumentException("Неизвестный профиль управления.");
        if(!p.SupportsProfile(flight))throw new ArgumentException("Трасса разрешает только Основное управление (Chosen).");
        if(request.Axis!="distance"&&request.Axis!="time")throw new ArgumentException("axis должен быть distance или time.");
        if(!new[]{.4,.6,.8,1,1.2}.Contains(request.WarningDuration))throw new ArgumentException("Недопустимая длительность предупреждения.");
        if(request.Inputs==null||request.Inputs.Length>4096)throw new ArgumentException("Нужно не более 4096 нажатий.");
        double previous=-1;foreach(double value in request.Inputs){if(!double.IsFinite(value)||value<0||value<=previous)throw new ArgumentException("Нажатия должны быть конечными неотрицательными числами в строго возрастающем порядке.");previous=value;}
        var game=RaceSimulation.CreateSolo(p.Track,flight,PartyCamera.Lagger,-288.8,1520);
        HiddenObstacles.ConfigureWarning(game,request.WarningDuration);
        RaceSimulation.Press(game,0);RaceSimulation.Advance(game,RaceConfig.CountdownDuration);
        var samples=new List<Sample>();var inputs=new List<InputResult>();var events=new List<ReplayEvent>();
        var seen=new Dictionary<string,HashSet<string>>();var phases=new Dictionary<string,HiddenObstaclePhase>();
        int next=0;
        void Observe()
        {
            var player=game.Players[0];double y=RaceSimulation.GetPlayerCenterY(player);
            var sample=new Sample(game.Elapsed,player.Distance,y,player.CurrentSpeed,player.Flipping,player.Recovering,
                player.Support.ToString(),player.SupportSide,player.Collisions,player.BoostsEarned,player.SlowsEarned,player.CoinsEarned,player.FinishBoost,player.Status.ToString());
            if(samples.Count>0&&Math.Abs(samples[^1].Time-sample.Time)<1e-12) samples[^1]=sample;
            else samples.Add(sample);
            void Changes(string type,IEnumerable<string> values)
            {
                if(!seen.TryGetValue(type,out var set))seen[type]=set=new();
                foreach(string id in values)if(set.Add(id)) events.Add(new(type,id,game.Elapsed,player.Distance,y));
            }
            Changes("collision",player.HitObstacleIds);Changes("collected",player.CollectedRiskIds);Changes("reward",player.SuccessfulRiskIds);
            Changes("failedReward",player.FailedRiskIds);Changes("negative",player.NegativeRiskIds);
            if(player.LastCoin!=null)Changes("coin",new[]{player.LastCoin.Id});
            if(player.LastFinishBoost!=null)Changes("finishBoost",new[]{player.LastFinishBoost.Id});
            foreach(var obstacle in p.Track.Obstacles.Where(o=>o.Hidden))
            {
                var phase=HiddenObstacles.GetPhase(game,obstacle);
                if(!phases.TryGetValue(obstacle.Id,out var old)||phase!=old)
                {
                    phases[obstacle.Id]=phase;double time=game.Elapsed;
                    if(game.HiddenObstacles.TryGetValue(obstacle.Id,out var state))time=(phase==HiddenObstaclePhase.Solid?state.SolidAt:state.RevealedAt)??time;
                    events.Add(new("hidden",obstacle.Id,time,player.Distance,y,phase.ToString()));
                }
            }
        }
        Observe();
        while(game.Phase=="racing"&&game.Elapsed<MaximumTime-1e-10)
        {
            var player=game.Players[0];double coordinate=request.Axis=="time"?game.Elapsed:player.Distance;
            if(next<request.Inputs.Length&&Math.Abs(coordinate-request.Inputs[next])<=1e-7)
            {
                bool accepted=RaceSimulation.Press(game,0);
                inputs.Add(new(next,request.Inputs[next],accepted?"accepted":"ignored",game.Elapsed,player.Distance,RaceSimulation.GetPlayerCenterY(player),accepted?null:player.Recovering>0?"Восстановление после удара":"Повтор в воздухе недоступен"));
                next++;Observe();continue;
            }
            double step=Math.Min(SampleStep,MaximumTime-game.Elapsed);
            if(next<request.Inputs.Length)
            {
                double target=request.Inputs[next];
                if(request.Axis=="time")step=Math.Min(step,target-game.Elapsed);
                else
                {
                    var trial=RaceSnapshot.Clone(game);RaceSimulation.Advance(trial,step);
                    if(trial.Players[0].Distance>=target&&target>player.Distance)
                    {
                        // Search independent production timelines: speed may change
                        // at a swept reward boundary inside this very interval.
                        double lo=0,hi=step;
                        for(int i=0;i<40;i++){double mid=(lo+hi)/2;var probe=RaceSnapshot.Clone(game);RaceSimulation.Advance(probe,mid);if(probe.Players[0].Distance<target)lo=mid;else hi=mid;}
                        step=hi;
                    }
                }
            }
            if(step<=0)throw new InvalidOperationException("Input scheduler made no progress.");
            RaceSimulation.Advance(game,step);Observe();
        }
        for(;next<request.Inputs.Length;next++)inputs.Add(new(next,request.Inputs[next],"unreached",null,null,null,"Гонка завершилась или достигнут предел 120 с"));
        var final=game.Players[0];
        var hidden=p.Track.Obstacles.Where(o=>o.Hidden).Select(o=>{game.HiddenObstacles.TryGetValue(o.Id,out var s);return new HiddenState(o.Id,HiddenObstacles.GetPhase(game,o).ToString(),s?.RevealedAt,s?.SolidAt,s?.ActivationPostponed??false);}).ToArray();
        var summary=new Summary(final.FinishTime.HasValue?"Finished":"TimedOut",final.FinishTime,game.Elapsed,final.FinishTime.HasValue&&final.Collisions==0,
            final.Collisions,final.BoostsEarned,final.InstantBoostsEarned,final.SlowsEarned,final.CoinsEarned,final.FinishBoostsEarned,
            inputs.Count(i=>i.Status=="accepted"),inputs.Count(i=>i.Status=="ignored"),inputs.Count(i=>i.Status=="unreached"),
            final.CollectedRiskIds.Order().ToArray(),final.SuccessfulRiskIds.Order().ToArray(),final.FailedRiskIds.Order().ToArray(),final.HitObstacleIds.Order().ToArray());
        return new(request.Profile,request.Axis,request.WarningDuration,parsed.Stale,parsed.ReferenceError,samples,inputs,events,hidden,summary);
    }
}
