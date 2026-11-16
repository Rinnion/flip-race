using FlipRace.Core;

namespace TrackWorkshop;

public sealed record AnalyzeRequest(string Json);
public sealed record ReplayRequest(string Json, string Profile, double WarningDuration, string Axis, double[] Inputs,
    WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record CatalogEntry(string Id, string Name, string File, string[] Profiles);
public sealed record Band(int Index, double X, double Y, double Width, double Height);
public sealed record Stroke(double X1, double Y1, double X2, double Y2);
public sealed record Reward(string Id, string Kind, double X, double Y, double Radius, string ObstacleId,
    string Placement, bool FreeReward, double ClearX, string Shape, double? Left, double? Right, double? PlaneY,
    Stroke[] Segments, double? StrokeWidth);
public sealed record Sample(double Time, double X, double Y, double Speed, bool Flipping, double Recovering,
    string Support, int SupportSide, int Collisions, int Boosts, int Slows, int Coins, bool FinishBoost, string Status);
public sealed record InputResult(int Index, double Value, string Status, double? Time, double? X, double? Y, string Reason);
public sealed record LandingInterval(double LandingTime, double LandingX, double LandingY, int InputIndex,
    double InputTime, double InputX, double InputY);
public sealed record ReplayEvent(string Type, string Id, double Time, double X, double Y, string Phase = null);
public sealed record HiddenState(string Id, string Phase, double? RevealedAt, double? SolidAt, bool ActivationPostponed);
public sealed record Summary(string Status, double? FinishTime, double Elapsed, bool CleanFinish, int Collisions,
    int Boosts, int InstantBoosts, int Slows, int Coins, int FinishBoosts, int AcceptedInputs, int IgnoredInputs,
    int UnreachedInputs, string[] CollectedIds, string[] SuccessfulIds, string[] FailedIds, string[] HitIds);
public sealed record ReplayResult(string Profile, string Axis, double WarningDuration, bool StaleReferences,
    string ReferenceError, List<Sample> Samples, List<InputResult> Inputs, List<ReplayEvent> Events,
    HiddenState[] HiddenStates, Summary Summary, List<LandingInterval> LandingIntervals, WorkshopSurfaceRunSettings SurfaceRunSettings);

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

    internal sealed record Preview(TrackPackage Package, bool Stale, string ReferenceError, DeveloperDocument Document);
    internal static Preview Read(string json)
    {
        var document = DeveloperDocument.Read(json);
        bool valid = TrackJson.TryRead(document.GameJson, out var package, out string error);
        // Only a document whose entire non-reference content passes the exact
        // production validator is eligible for explicitly marked preview.
        if (!valid)
        {
            if (!error.StartsWith("$.botReferences", StringComparison.Ordinal)) throw new ArgumentException(error);
            string bareJson = DeveloperDocument.Read(document.WithoutReferences).GameJson;
            if (!TrackJson.TryRead(bareJson, out package, out _)) throw new ArgumentException(error);
        }
        return new Preview(package, !valid, valid ? null : error, document);
    }
    public object Analyze(AnalyzeRequest request)
    {
        if(request==null)throw new ArgumentException("Пустой запрос.");
        var parsed = Read(request.Json); var p = parsed.Package; var t = p.Track;
        return new {
            id=t.Id, name=t.Name, length=t.Length, corridorHeight=RaceConfig.CorridorHeight, playerRadius=RaceConfig.PlayerRadius,
            profilePolicy=TrackJson.ProfilePolicyName(p.ProfilePolicy),
            warningMode=p.WarningMode==TrackWarningMode.Selected?"selected":"authored", contentHash=p.ContentHash,
            gameValid=!parsed.Stale, staleReferences=parsed.Stale, referenceError=parsed.ReferenceError,
            warnings=parsed.Stale?new[]{"Эталоны не прошли проверку. Предпросмотр выполняется без botReferences; сохраните JSON без них."}:Array.Empty<string>(),
            canonicalJson=parsed.Stale?null:parsed.Document.HasAnalysis?request.Json:TrackJson.Write(p),
            cleanedJson=parsed.Document.HasAnalysis?parsed.Document.WithoutReferences:TrackJson.Write(new TrackPackage(t,p.ProfilePolicy,p.WarningMode)),
            routeAnalysis=parsed.Document.AnalysisFor(p),
            toleranceBatch=parsed.Document.ToleranceBatchFor(p),
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
        var parsed=Read(request.Json);
        var flight=GetFlight(parsed.Package,request.Profile);
        ValidateAxis(request.Axis);
        ValidateWarning(request.WarningDuration);
        ValidateInputs(request.Inputs);
        return Replay(request,parsed,flight,includeTrajectory:true);
    }

    internal static ReplayResult Replay(ReplayRequest request, Preview parsed, FlightSettings flight,
        bool includeTrajectory, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var game=StartSolo(parsed.Package.Track,flight,request.WarningDuration,request.SurfaceRunSettings);
        RaceSimulation.Advance(game,RaceConfig.CountdownDuration);
        var samples=new List<Sample>();var inputs=new List<InputResult>();
        var observation=includeTrajectory?new TrialObservation(game):null;
        var landingIntervals = new LandingIntervals();
        var player = game.Players[0];
        int next=0;
        void Observe()
        {
            if(observation==null)return;
            var sample=observation.Observe();
            if(samples.Count>0&&Math.Abs(samples[^1].Time-sample.Time)<1e-12) samples[^1]=sample;
            else samples.Add(sample);
        }
        Observe();
        while (IsRacing(game))
        {
            bool startedFromSupport = LandingIntervals.IsSupported(player);
            var previousFlight = player.LastFlight;
            int previousCollisions = player.Collisions;
            var input = ReplayStep(game, request.Axis, request.Inputs, ref next, cancellationToken:cancellation);
            if (input != null)
            {
                inputs.Add(input);
                if (input.Status == "accepted" && startedFromSupport) landingIntervals.Accept(input);
            }
            landingIntervals.Observe(player, previousFlight, previousCollisions);
            Observe();
        }
        for(;next<request.Inputs.Length;next++)inputs.Add(UnreachedPress(next,request.Inputs[next]));
        var summary=Summarize(game,inputs.Count(i=>i.Status=="accepted"),inputs.Count(i=>i.Status=="ignored"),inputs.Count(i=>i.Status=="unreached"));
        return new(request.Profile,request.Axis,request.WarningDuration,parsed.Stale,parsed.ReferenceError,samples,inputs,
            observation?.Events ?? new(),observation?.HiddenStates().ToArray() ?? [],summary,landingIntervals.Results,
            WorkshopSurfaceRunSettings.FromCore(game.SurfaceRunEnabled,game.SurfaceRunSettings));
    }

    public RouteSearchResponse SearchRoutes(RouteSearchRequest request, CancellationToken cancellation = default) =>
        SearchRoutes(request, cancellation, progress: null);

    internal RouteSearchResponse SearchRoutes(RouteSearchRequest request, CancellationToken cancellation,
        Action<RouteSearchProgress> progress)
    {
        if (request == null) throw new ArgumentException("Пустой запрос.");
        cancellation.ThrowIfCancellationRequested();
        var preview = Read(request.Json);
        var flight = GetFlight(preview.Package, request.Profile);
        ValidateWarning(request.WarningDuration);
        var surfaceRun = request.SurfaceRunSettings ?? WorkshopSurfaceRunSettings.Legacy;
        surfaceRun.ToCore();
        var update = preview.Document.AnalysisToUpdate(new(request.Profile, request.WarningDuration, surfaceRun));
        var run = new RouteSearch(preview.Package, flight, request.Profile, request.WarningDuration, cancellation: cancellation,
            surfaceRunSettings: surfaceRun).Find(progress);
        cancellation.ThrowIfCancellationRequested();
        progress?.Invoke(new(99, "save"));
        cancellation.ThrowIfCancellationRequested();
        var result = preview.Document.Save(run, preview.Package, update);
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    internal static bool IsRacing(GameState game) =>
        game.Phase == "racing" && game.Elapsed < MaximumTime - 1e-10;

    internal static void ValidateAxis(string axis)
    {
        if (axis is not ("distance" or "time")) throw new ArgumentException("axis должен быть distance или time.");
    }

    internal static void ValidateInputs(double[] inputs)
    {
        if (inputs == null || inputs.Length > 4096) throw new ArgumentException("Нужно не более 4096 нажатий.");
        double previous = -1;
        foreach (double value in inputs)
        {
            if (!double.IsFinite(value) || value < 0 || value <= previous)
                throw new ArgumentException("Нажатия должны быть конечными неотрицательными числами в строго возрастающем порядке.");
            previous = value;
        }
    }

    // One scheduled press or one Core advance. A prefix stops at the first
    // ordinary sample that reaches its marker. Excluded presses cannot split
    // a step, so the resulting time list can replay on the same schedule.
    internal static InputResult ReplayStep(GameState game, string axis, double[] inputs, ref int next,
        double? stopAtX = null, CancellationToken cancellationToken = default, double? stopBeforeTime = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        double x = game.Players[0].Distance;
        double step = Math.Min(SampleStep, Math.Min(stopBeforeTime ?? MaximumTime, MaximumTime) - game.Elapsed);
        if (next < inputs.Length && (stopBeforeTime == null || inputs[next] < stopBeforeTime.Value))
        {
            double target = inputs[next];
            bool byTime = axis == "time";
            bool include = byTime || !stopAtX.HasValue || target < stopAtX.Value;
            if (include)
            {
                double coordinate = byTime ? game.Elapsed : x;
                if (Math.Abs(coordinate - target) <= 1e-7) return ApplyPress(game, next++, target);
                if (byTime)
                {
                    double untilPress = target - game.Elapsed;
                    if (untilPress < step && (!stopAtX.HasValue || untilPress <= 0 || ForecastX(game, untilPress) < stopAtX.Value))
                        step = untilPress;
                }
                else if (target > x && ForecastX(game, step) >= target)
                {
                    // A swept reward can change speed inside this interval.
                    double lo = 0, hi = step;
                    for (int i = 0; i < 40; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        double mid = (lo + hi) / 2;
                        if (ForecastX(game, mid) < target) lo = mid;
                        else hi = mid;
                    }
                    step = hi;
                }
            }
        }
        if (step <= 0) throw new InvalidOperationException("Input scheduler made no progress.");
        RaceSimulation.Advance(game, step);
        return null;
    }

    static double ForecastX(GameState game, double step)
    {
        var probe = RaceSnapshot.Clone(game);
        RaceSimulation.Advance(probe, step);
        return probe.Players[0].Distance;
    }

    internal static FlightSettings GetFlight(TrackPackage package, string profile)
    {
        var flight=profile=="Chosen"?FlightPresets.Chosen:profile=="FastFlip"?FlightPresets.FastFlip:null;
        if(flight==null)throw new ArgumentException("Неизвестный профиль управления.");
        if(!package.SupportsProfile(flight))throw new ArgumentException("Трасса разрешает только Основное управление (Chosen).");
        return flight;
    }

    internal static void ValidateWarning(double duration)
    {
        if(duration is not (.4 or .6 or .8 or 1 or 1.2))throw new ArgumentException("Недопустимая длительность предупреждения.");
    }

    internal static GameState StartSolo(Track track, FlightSettings flight, double warningDuration,
        WorkshopSurfaceRunSettings surfaceRunSettings = null)
    {
        var game=RaceSimulation.CreateSolo(track,flight,PartyCamera.Lagger,-288.8,1520);
        HiddenObstacles.ConfigureWarning(game,warningDuration);
        var settings = surfaceRunSettings ?? WorkshopSurfaceRunSettings.Legacy;
        RaceSimulation.ConfigureSurfaceRun(game, settings.Enabled, settings.ToCore());
        RaceSimulation.Press(game,0);
        return game;
    }

    internal static InputResult ApplyPress(GameState game, int index, double value)
    {
        var player=game.Players[0];
        bool accepted=game.Phase=="racing"&&RaceSimulation.Press(game,0);
        string reason=accepted?null:game.Phase=="countdown"?"Идёт обратный отсчёт":player.Recovering>0?"Восстановление после удара":"Повтор в воздухе недоступен";
        return new(index,value,accepted?"accepted":"ignored",game.Elapsed,player.Distance,RaceSimulation.GetPlayerCenterY(player),reason);
    }

    internal static InputResult UnreachedPress(int index, double value) =>
        new(index,value,"unreached",null,null,null,"Гонка завершилась или достигнут предел 120 с");

    internal static Summary Summarize(GameState game, int accepted, int ignored, int unreached)
    {
        var final=game.Players[0];
        return new(final.FinishTime.HasValue?"Finished":"TimedOut",final.FinishTime,game.Elapsed,final.FinishTime.HasValue&&final.Collisions==0,
            final.Collisions,final.BoostsEarned,final.InstantBoostsEarned,final.SlowsEarned,final.CoinsEarned,final.FinishBoostsEarned,
            accepted,ignored,unreached,
            final.CollectedRiskIds.Order().ToArray(),final.SuccessfulRiskIds.Order().ToArray(),final.FailedRiskIds.Order().ToArray(),final.HitObstacleIds.Order().ToArray());
    }
}

internal sealed class LandingIntervals
{
    sealed record Landing(double Time, double X, double Y, SupportKind Kind, int Side, string ObstacleId);
    Landing pending;
    public List<LandingInterval> Results { get; } = new();

    public static bool IsSupported(PlayerState player) =>
        player.Recovering == 0 && !player.Flipping && player.Support is SupportKind.Wall or SupportKind.Stone;

    public void Observe(PlayerState player, FlightMeasurement previousFlight, int previousCollisions)
    {
        if (!IsSupported(player) || player.Collisions != previousCollisions)
        {
            pending = null;
            return;
        }

        // A fresh, uninterrupted Core flight ending on support gives the
        // contact time even when a press shares this sample's timestamp.
        var flight = player.LastFlight;
        if (flight != previousFlight && flight is { Interrupted: false })
        {
            pending = new(flight.EndedAt, player.Distance, RaceSimulation.GetPlayerCenterY(player),
                player.Support, player.SupportSide, player.SupportObstacleId);
        }
        else if (pending != null && (pending.Kind != player.Support || pending.Side != player.SupportSide
            || pending.ObstacleId != player.SupportObstacleId)) pending = null;
    }

    public void Accept(InputResult input)
    {
        if (pending == null) return;
        Results.Add(new(pending.Time, pending.X, pending.Y, input.Index,
            input.Time.Value, input.X.Value, input.Y.Value));
        pending = null;
    }
}

internal sealed class TrialObservation
{
    readonly GameState game;
    readonly Obstacle[] hidden;
    readonly Dictionary<string,HashSet<string>> seen = new();
    readonly Dictionary<string,HiddenObstaclePhase> phases = new();
    public List<ReplayEvent> Events { get; } = new();

    public TrialObservation(GameState game)
    {
        this.game=game;
        hidden=game.Track.Obstacles.Where(o=>o.Hidden).ToArray();
    }

    public Sample Observe()
    {
        var player=game.Players[0];double y=RaceSimulation.GetPlayerCenterY(player);
        var sample=Capture(game);
        void Changes(string type,HashSet<string> values)
        {
            if(!seen.TryGetValue(type,out var set))seen[type]=set=new();
            if(set.Count==values.Count)return;
            foreach(string id in values)if(set.Add(id))Events.Add(new(type,id,game.Elapsed,player.Distance,y));
        }
        void Latest(string type,HitInfo hit)
        {
            if(hit==null)return;
            if(!seen.TryGetValue(type,out var set))seen[type]=set=new();
            if(set.Add(hit.Id))Events.Add(new(type,hit.Id,game.Elapsed,player.Distance,y));
        }
        Changes("collision",player.HitObstacleIds);Changes("collected",player.CollectedRiskIds);Changes("reward",player.SuccessfulRiskIds);
        Changes("failedReward",player.FailedRiskIds);Changes("negative",player.NegativeRiskIds);
        Latest("coin",player.LastCoin);Latest("finishBoost",player.LastFinishBoost);
        foreach(var obstacle in hidden)
        {
            var phase=HiddenObstacles.GetPhase(game,obstacle);
            if(!phases.TryGetValue(obstacle.Id,out var old)||phase!=old)
            {
                phases[obstacle.Id]=phase;double time=game.Elapsed;
                if(game.HiddenObstacles.TryGetValue(obstacle.Id,out var state))time=(phase==HiddenObstaclePhase.Solid?state.SolidAt:state.RevealedAt)??time;
                Events.Add(new("hidden",obstacle.Id,time,player.Distance,y,phase.ToString()));
            }
        }
        return sample;
    }

    internal static Sample Capture(GameState game)
    {
        var p = game.Players[0];
        return new(game.Elapsed, p.Distance, RaceSimulation.GetPlayerCenterY(p), p.CurrentSpeed, p.Flipping, p.Recovering,
            p.Support.ToString(), p.SupportSide, p.Collisions, p.BoostsEarned, p.SlowsEarned, p.CoinsEarned,
            p.FinishBoost, p.Status.ToString());
    }

    public IEnumerable<HiddenState> HiddenStates()
    {
        foreach(var obstacle in hidden)
        {
            game.HiddenObstacles.TryGetValue(obstacle.Id,out var state);
            yield return new(obstacle.Id,HiddenObstacles.GetPhase(game,obstacle).ToString(),state?.RevealedAt,state?.SolidAt,state?.ActivationPostponed??false);
        }
    }
}
