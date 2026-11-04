using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;
using Baseline = FlipRace.AcceptedMapBaseline;

static class Program
{
    static int checks;
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    static readonly (string id, FlightSettings flight)[] Pairs = {
        (TracksHidden.InterceptsId,FlightPresets.Chosen),(TracksHidden.InterceptsId,FlightPresets.FastFlip),
        (TracksHidden.ManifestationId,FlightPresets.Chosen),(TracksHidden.ManifestationId,FlightPresets.FastFlip),
        (TracksRethink.Id,FlightPresets.Chosen) };
    static readonly List<object> Results = new();
    static Dictionary<string,string> SourceHashes() => Directory.GetFiles("Assets/Scripts/Core","*.cs").OrderBy(p=>p)
        .ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    static void Need(bool ok,string reason) { checks++;if(!ok)throw new Exception(reason); }
    static void Near(double a,double b,string reason,double tolerance=2e-7) { Need(Math.Abs(a-b)<tolerance,reason+": "+a+" vs "+b); }
    static GameState Forecast(StochasticBotController c) => (GameState)typeof(StochasticBotController).GetMethod("PersonalForecast",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,null);
    static Track Course(string id) => id==TracksRethink.Id?TracksRethink.Get():TracksHidden.Get(id);
    static string Fingerprint(GameState g) => JsonSerializer.Serialize(g,Json);
    static GameState Create(string id,FlightSettings f,BotDifficulty difficulty,uint seed,double delay,PartyCamera camera=PartyCamera.Lagger)
    { var g=RaceSimulation.CreateStochasticBotRace(Course(id),f,difficulty,seed,camera);HiddenObstacles.ConfigureWarning(g,delay);return g; }

    static void OldMapInvariance()
    {
        foreach(var track in TracksFull.All.Concat(TracksRhythm.All).Concat(new[]{TracksMixed.Get()}))
        foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        {
            var old=Baseline.StochasticTrackMap.Create(track,f);var now=StochasticTrackMap.Create(track,f);
            Need(now.ProfileId==old.ProfileId&&now.BaseTime==old.BaseTime&&now.MasterTime==old.MasterTime
                &&now.BonusGain==old.BonusGain&&now.CollisionCostSum==old.CollisionCostSum,"old scalar map changed");
            Need(now.ReferenceInputs.SequenceEqual(old.ReferenceInputs),"old exact input doubles changed");
            string Item(object item) => JsonSerializer.Serialize(item,item.GetType(),Json);
            Need(now.Opportunities.Select(Item).SequenceEqual(old.Opportunities.Select(Item)),"old exact opportunities changed");
            foreach(BotDifficulty d in Enum.GetValues<BotDifficulty>())
            {
                var s=StochasticBotSettings.CreateInitial(track,f,d,123);
                Near(s.HitProbability,now.BonusGain/now.CollisionCostSum*(1-s.Quality),"old formula");
            }
        }
    }
    static void NewMapContract()
    {
        foreach(var (id,f) in Pairs)
        {
            var track=Course(id);var map=StochasticTrackMap.Create(track,f);var r=BotReferenceRoutes.Get(id,f);
            Need(StochasticTrackMap.SupportsTrack(id),"new catalogue support");
            Need(map.ReferenceInputs.SequenceEqual(r.Inputs),"new map does not use authored reference");
            Need(map.Opportunities.Count(o=>o.Kind==BotOpportunityKind.Obstacle)==track.Obstacles.Select(o=>o.Id).Distinct().Count(),"all logical obstacles retained");
            var goalIds=map.Opportunities.Where(o=>o.Kind!=BotOpportunityKind.Obstacle).Select(o=>o.Id[(o.Id.IndexOf(':')+1)..]).ToHashSet();
            Need(goalIds.SetEquals(r.RewardIds),"only selected goals create opportunities");
            Need(goalIds.All(goal=>track.RiskZones.Any(z=>z.Id==goal)),"selected physical goal absent");
            Near(map.BonusGain,r.BonusGain,"selected benefit");Near(map.MasterTime,36-map.BonusGain,"selected Master time");
            foreach(BotDifficulty d in Enum.GetValues<BotDifficulty>())
            { var s=StochasticBotSettings.CreateInitial(track,f,d,123);Near(s.CollectProbability,s.Quality,"collect q");Near(s.HitProbability,map.BonusGain/map.CollisionCostSum*(1-s.Quality),"new shared formula"); }
        }
        Need(!StochasticTrackMap.SupportsTrack("unknown")&&!StochasticTrackMap.SupportsTrack(null),"unsupported catalogue ID");
        bool rejected=false;try{StochasticTrackMap.Create(TracksRethink.Get(),FlightPresets.FastFlip);}catch(NotSupportedException){rejected=true;}
        Need(rejected,"Rethink Fast must remain unsupported");
    }
    static void SharedForecasts()
    {
        foreach(var (id,f) in Pairs)
        foreach(double delay in new[]{.4,.6,.8,1,1.2})
        {
            var g=Create(id,f,BotDifficulty.Master,0,delay,PartyCamera.Constant);
            var c=new StochasticBotController(g);RaceSimulation.Press(g,0);RaceSimulation.Advance(g,3);
            // Structural seam: an already-revealed, overdue warning currently
            // contains the human. Physical movement after this setup is ordinary.
            var stone=g.Track.Obstacles.First(o=>o.Hidden);g.Elapsed=10;
            g.Players[0].Distance=stone.X+10;g.Players[0].Y=stone.Kind=="floor"?1:0;g.Players[0].SupportSide=stone.Kind=="floor"?1:0;
            g.Players[1].Distance=stone.X-500;g.Players[1].Y=stone.Kind=="floor"?0:1;
            g.HiddenObstacles[stone.Id]=new HiddenObstacleState{Phase=HiddenObstaclePhase.Warning,RevealedAt=8,TriggerPlayerId=0,ActivationPostponed=true};
            var other=g.Track.Obstacles.First(o=>o.Hidden&&o.Id!=stone.Id);
            g.HiddenObstacles[other.Id]=new HiddenObstacleState{Phase=HiddenObstaclePhase.Solid,RevealedAt=7,SolidAt=8,TriggerPlayerId=0};
            string original=Fingerprint(g);var a=Forecast(c);var b=Forecast(c);string sibling=Fingerprint(b);
            Need(a.PartyCamera==PartyCamera.Lagger&&a.Players.Length==g.Players.Length,"forecast keeps bodies and excludes Constant elimination");
            Need(a.Players[0].Id==g.Players[1].Id&&a.Players[1].Id==g.Players[0].Id,"forecast reorders slots without changing stable IDs");
            Near(a.HiddenWarningDuration,delay,"selected warning copied before forecast advance");
            Need(!ReferenceEquals(a.HiddenObstacles,g.HiddenObstacles)&&!ReferenceEquals(a.HiddenObstacles[stone.Id],g.HiddenObstacles[stone.Id]),"hidden mutable states independent");
            RaceSimulation.Advance(a,.01);
            Need(HiddenObstacles.GetPhase(a,stone)==HiddenObstaclePhase.Warning&&a.HiddenObstacles[stone.Id].SolidAt==null,"current human body postpones overdue warning");
            Need(HiddenObstacles.GetPhase(a,other)==HiddenObstaclePhase.Solid,"existing solid must not restart warning");
            RaceSimulation.Advance(a,1);
            Need(HiddenObstacles.GetPhase(a,stone)==HiddenObstaclePhase.Solid,"normal copied-body movement eventually clears volume");
            Need(Fingerprint(g)==original&&Fingerprint(b)==sibling,"source and sibling unchanged by all forecast advances");
            var ordinary=RaceSnapshot.Clone(g);RaceSimulation.Advance(g,.02);RaceSimulation.Advance(ordinary,.02);
            Same(g,ordinary,"live world receives exactly one ordinary advance, including terminal camera rules");
        }
    }

    static readonly Dictionary<Type,FieldInfo[]> FieldCache=new();
    static void SelectedMasterCriteria()
    {
        var game=Create(TracksHidden.InterceptsId,FlightPresets.Chosen,BotDifficulty.Master,0,.8);
        var controller=new StochasticBotController(game);var map=StochasticTrackMap.Create(game.Track,game.Flight);
        var goalIds=map.Opportunities.Where(o=>o.Kind!=BotOpportunityKind.Obstacle).Select(o=>o.Id[(o.Id.IndexOf(':')+1)..]).ToArray();
        string alternative=game.Track.RiskZones.First(z=>!z.IsNegative&&!z.IsCoin&&!goalIds.Contains(z.Id)).Id;
        var origin=Forecast(controller);var p=origin.Players[0];p.Status=PlayerStatus.Finished;p.Distance=game.Track.Length;
        foreach(string id in goalIds)p.SuccessfulRiskIds.Add(id);
        int count=map.Opportunities.Count(o=>o.Kind==BotOpportunityKind.Positive);
        p.CompletedBoosts=count;p.FinishBoostsEarned=1;p.FinishBoostElapsed=.25;
        var evaluate=typeof(StochasticBotController).GetMethod("Evaluate",BindingFlags.NonPublic|BindingFlags.Instance);
        bool Clean()
        {
            object requests=Activator.CreateInstance(evaluate.GetParameters()[3].ParameterType);
            object result=evaluate.Invoke(controller,new object[]{origin,Array.Empty<double>(),game.Track.Length,requests});
            return (bool)result.GetType().GetField("MasterClean",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(result);
        }
        Need(Clean(),"selected terminal fixture is clean");
        p.FailedRiskIds.Add(alternative);Need(Clean(),"failed unselected alternative does not invalidate Master");p.FailedRiskIds.Clear();
        p.SuccessfulRiskIds.Add(alternative);p.CompletedBoosts++;Need(Clean(),"incidental extra completed pulse does not invalidate Master");
        p.SuccessfulRiskIds.Remove(goalIds[0]);Need(!Clean(),"incidental extra cannot replace missing selected goal");p.SuccessfulRiskIds.Add(goalIds[0]);
        p.FailedRiskIds.Add(goalIds[0]);Need(!Clean(),"failed selected goal invalidates Master");p.FailedRiskIds.Clear();
        p.CompletedBoosts=count-1;Need(!Clean(),"incomplete aggregate selected pulse guard");p.CompletedBoosts=count;
        p.FinishBoostElapsed=.24;Need(!Clean(),"incomplete finish pulse guard");
    }
    static void AdaptiveHiddenBoundaries()
    {
        foreach(double duration in new[]{.1,1d/240*1.1})
        foreach(double dt in new[]{.317,10})
        foreach(bool boost in new[]{false,true})
        {
            var stone=new Obstacle{Id="adaptive",Kind="floor",X=200,Width=64,Height=68,Hidden=true,HiddenRevealDistance=180,HiddenWarningDuration=duration};
            var track=new Track(10000,new[]{stone});
            if(boost)track.RiskZones=new[]{new RiskZone{Id="pre-reveal",Kind="instant",X=25,Y=226,Radius=6}};
            var g=RaceSimulation.CreateParty(track,FlightPresets.Chosen,2,PartyCamera.Lagger,-288.8,1520);
            RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);RaceSimulation.Advance(g,3);g.Players[1].Distance=.2;
            var reverse=RaceSnapshot.Clone(g);Array.Reverse(reverse.Players);
            RaceSimulation.Advance(g,dt);RaceSimulation.Advance(reverse,dt);
            double reveal=boost?(5-.2)/220+15d/330:(20-.2)/220;
            foreach(var world in new[]{g,reverse})
            {
                var state=world.HiddenObstacles[stone.Id];Need(state.Phase==HiddenObstaclePhase.Solid,"single large Advance crosses reveal and deadline");
                Near(state.RevealedAt.Value,reveal,"single Advance exact reveal");Near(state.SolidAt.Value,reveal+duration,"single Advance exact deadline");
                Need(state.TriggerPlayerId==1,"single Advance source order does not change first participant");
            }
        }
        var overlap=new Obstacle{Id="occupied",Kind="floor",X=0,Width=1000,Height=68,Hidden=true,HiddenWarningDuration=1d/240*1.1};
        foreach(double dt in new[]{.317,10})
        {
            var g=RaceSimulation.CreateParty(new Track(10000,new[]{overlap}),FlightPresets.Chosen,2,PartyCamera.Lagger,-288.8,1520);
            RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);RaceSimulation.Advance(g,3);g.Players[1].Distance=.2;
            var reverse=RaceSnapshot.Clone(g);Array.Reverse(reverse.Players);RaceSimulation.Advance(g,dt);RaceSimulation.Advance(reverse,dt);
            foreach(var world in new[]{g,reverse})
            {
                var state=world.HiddenObstacles[overlap.Id];Need(state.ActivationPostponed,"single Advance keeps occupied warning");
                if(dt<1)Need(state.Phase==HiddenObstaclePhase.Warning&&world.Players.All(p=>p.Collisions==0),"overlap stays intangible");
                else{double clear=1014d/220;Need(state.Phase==HiddenObstaclePhase.Solid&&state.SolidAt>=clear-1e-9&&state.SolidAt<=clear+1d/240+1e-8,"body clearance still sampled within 1/240");}
            }
        }
    }
    static void Same(object a,object b,string path)
    {
        if(a==null||b==null){Need(a==null&&b==null,path);return;}
        Type type=a.GetType();Need(type==b.GetType(),path+" type");
        if(type.IsPrimitive||type.IsEnum||type==typeof(string)||type==typeof(decimal)){Need(a.Equals(b),path);return;}
        if(a is HashSet<string> set){Need(set.SetEquals((HashSet<string>)b),path);return;}
        if(a is IDictionary ad){var bd=(IDictionary)b;Need(ad.Count==bd.Count,path+" size");foreach(var key in ad.Keys){Need(bd.Contains(key),path+" key");Same(ad[key],bd[key],path+"["+key+"]");}return;}
        if(a is IEnumerable ae){var be=((IEnumerable)b).GetEnumerator();foreach(var value in ae){Need(be.MoveNext(),path+" shorter");Same(value,be.Current,path+"[]");}Need(!be.MoveNext(),path+" longer");return;}
        if(!FieldCache.TryGetValue(type,out var fields))FieldCache[type]=fields=type.GetFields(BindingFlags.Public|BindingFlags.Instance);
        foreach(var field in fields)
        {if(type==typeof(GameState)&&(field.Name=="Track"||field.Name=="Flight"||field.Name=="Profile")){Need(ReferenceEquals(field.GetValue(a),field.GetValue(b)),path+" readonly");continue;}Same(field.GetValue(a),field.GetValue(b),path+"."+field.Name);}
    }

    static object RunCase(string id,FlightSettings flight,BotDifficulty difficulty,uint seed,double delay,bool human=false,double frame=1d/60,bool resumedLead=false)
    {
        string name=id+"/"+(flight==FlightPresets.Chosen?"Chosen":"Fast")+"/"+difficulty+"/"+seed+"/"+delay+(human?"/human":"");
        var watch=Stopwatch.StartNew();var game=Create(id,flight,difficulty,seed,delay);
        var replay=RaceSimulation.CreateStochasticBotRace(game.Track,flight,game.StochasticBot,game.PartyCamera);
        HiddenObstacles.ConfigureWarning(replay,delay);
        var controller=new StochasticBotController(game);var map=StochasticTrackMap.Create(game.Track,flight);
        var humanTape=human?map.ReferenceInputs:Array.Empty<double>();int humanEdge=0,lastPlan=-1,advances=0;bool humanTriggered=false,earlyHumanTriggered=false,pauseChecked=false;
        var inputs=new List<double>();
        controller.Observer=s=>
        {
            if(s.Kind=="advance"){RaceSimulation.Advance(replay,s.Delta);advances++;}
            else if(s.Kind=="press"){Need(RaceSimulation.Press(replay,1)==s.Accepted,name+" ordinary input replay");inputs.Add(s.Distance);}
            Near(game.Elapsed,replay.Elapsed,name+" one world advance",1e-9);
            foreach(var pair in game.HiddenObstacles)if(pair.Value.TriggerPlayerId==0)
            {humanTriggered=true;var stone=game.Track.Obstacles.First(o=>o.Id==pair.Key);
                if(game.Players[1].Distance<ObstacleGeometry.Left(stone)-HiddenObstacles.RevealDistanceFor(stone)-1e-6)earlyHumanTriggered=true;}
            if(controller.PlanningCalls!=lastPlan||s.Kind=="press"||game.Phase=="finished")
            {Same(game,replay,name+" replay");lastPlan=controller.PlanningCalls;
                if(controller.PlanningSamples.Count>0&&controller.PlanningSamples[^1].Milliseconds>=1000)
                    Console.WriteLine("SLOW PLAN "+name+" ms="+controller.PlanningSamples[^1].Milliseconds);}
        };
        RaceSimulation.Press(game,0);RaceSimulation.Press(replay,0);controller.Advance(3);
        // Explicit resumed-state fixture, not a claim of fresh-start lead creation.
        // All subsequent human/bot inputs and movement use production physics.
        if(resumedLead){game.Players[0].Distance=20;replay.Players[0].Distance=20;}
        while(game.Phase=="racing"&&game.Elapsed<90)
        {
            if(!pauseChecked&&controller.PlanningCalls>0)
            {string paused=Fingerprint(game);int cursor=controller.Cursor,decisions=controller.Decisions.Count;controller.Advance(5,true);
                Need(paused==Fingerprint(game)&&cursor==controller.Cursor&&decisions==controller.Decisions.Count,name+" pause preserves world/input/intent");pauseChecked=true;}
            while(humanEdge<humanTape.Length&&game.Players[0].Distance>=humanTape[humanEdge]-2e-7)
            {Need(RaceSimulation.Press(game,0)==RaceSimulation.Press(replay,0),"human input replay");humanEdge++;}
            double step=frame;
            if(humanEdge<humanTape.Length&&game.Players[0].Recovering<=0)
                step=Math.Min(step,Math.Max(1e-10,(humanTape[humanEdge]-game.Players[0].Distance)/RaceConfig.FinishBoostSpeed));
            controller.Advance(step);
        }
        if(resumedLead)Need(earlyHumanTriggered,name+" resumed leading human actually reveals before bot threshold");
        var bot=game.Players[1];Need(bot.Status!=PlayerStatus.Running,name+" bounded completion");
        Need(controller.MaximumRequestedHorizon<=1000+2e-7&&controller.MaximumTrialHorizon<=1055+2e-7
            &&controller.MaximumTrialOvershoot<=55+2e-7&&controller.ForecastHorizonViolations==0,name+" original forecast limits");
        Need(controller.ForecastObservations==controller.ForecastSteps,name+" all forecasts observed");
        Need(controller.PlanningSamples.All(s=>s.Candidates<=(difficulty==BotDifficulty.Master?13:96)),name+" unchanged candidate caps");
        Need(controller.Decisions.Select(d=>d.Id).Distinct().Count()==controller.Decisions.Count,name+" one intention per object");
        if(difficulty==BotDifficulty.Master)
        {
            Need(bot.Status==PlayerStatus.Finished&&bot.Collisions==0&&bot.SlowsEarned==0,name+" Master clean");
            foreach(var item in map.Opportunities.Where(o=>o.Kind!=BotOpportunityKind.Obstacle))
                Need(bot.SuccessfulRiskIds.Contains(item.Id[(item.Id.IndexOf(':')+1)..]),name+" selected Master goal "+item.Id);
            Need(bot.CompletedBoosts>=map.Opportunities.Count(o=>o.Kind==BotOpportunityKind.Positive)&&bot.FinishBoostElapsed>=.25-1e-8,name+" selected aggregate pulse service");
            Near(bot.FinishTime.Value,map.MasterTime,name+" Master actual time");
        }
        double[] times=controller.PlanningSamples.Select(s=>s.Milliseconds).OrderBy(x=>x).ToArray();
        double maximum=times.Length==0?0:times[^1],p95=times.Length==0?0:times[(int)Math.Ceiling(times.Length*.95)-1];
        Console.WriteLine(name+" hits="+bot.Collisions+" rewards="+bot.BoostsEarned+" finish="+bot.FinishTime+" planMaxMs="+maximum.ToString("F1")+" p95="+p95.ToString("F1"));
        return new {name,bot.Status,bot.Collisions,bot.BoostsEarned,bot.CompletedBoosts,bot.FinishTime,humanTriggered,earlyHumanTriggered,resumedLead,advances,
            controller.PlanningCalls,controller.CandidateSimulations,controller.ForecastSteps,controller.SuppressedInputs,controller.LastPlanningFailure,
            maximumPlanningMilliseconds=maximum,p95PlanningMilliseconds=p95,milliseconds=watch.Elapsed.TotalMilliseconds,inputs,
            decisions=controller.Decisions.Select(d=>new{d.Id,d.Intent,d.Roll,d.Probability,d.Outcome,d.UnreachableReason}).ToArray()};
    }
    static int PerfLoop(string label)
    {
        const uint seed=2944602636;int targetPlans=label=="min"?1:2;
        object Prefix(bool warm)
        {
            var g=Create(TracksHidden.InterceptsId,FlightPresets.Chosen,BotDifficulty.Novice,seed,.8,PartyCamera.Constant);
            var c=new StochasticBotController(g);var edges=new List<double>();c.Observer=s=>{if(s.Kind=="press")edges.Add(s.Distance);};
            RaceSimulation.Press(g,0);c.Advance(3);var clock=Stopwatch.StartNew();
            while(c.PlanningCalls<targetPlans&&g.Elapsed<8)c.Advance(1d/60);
            Need(c.PlanningCalls==targetPlans,"fixed seed reaches exact planning prefix");
            double maximum=c.PlanningSamples.Max(s=>s.Milliseconds);
            Console.WriteLine((warm?"WARMUP":"MEASURED")+" seed="+seed+" plans="+c.PlanningCalls+" maxMs="+maximum.ToString("F2")+" elapsed="+g.Elapsed.ToString("F3"));
            return new {maximum,milliseconds=clock.Elapsed.TotalMilliseconds,g.Elapsed,edges,
                players=g.Players.Select(p=>new{p.Id,p.Distance,p.Y,p.VerticalSpeed,p.Recovering,p.Collisions,p.BoostsEarned,p.CompletedBoosts,p.FinishBoostsEarned,
                    hits=p.HitObstacleIds.OrderBy(x=>x).ToArray(),successful=p.SuccessfulRiskIds.OrderBy(x=>x).ToArray()}).ToArray(),
                plans=c.PlanningSamples.Select(s=>new{s.Number,s.AtDistance,s.HorizonEndDistance,s.Candidates,s.ForecastSteps,s.Rollbacks,s.Milliseconds}).ToArray(),
                decisions=c.Decisions.Select(d=>new{d.Id,d.Intent,d.Roll,d.Probability,d.Outcome,inputs=d.SelectedInputs}).ToArray()};
        }
        var warmup=Prefix(true);var runs=new[]{Prefix(false),Prefix(false),Prefix(false)};
        double worst=runs.Max(r=>JsonSerializer.SerializeToElement(r,Json).GetProperty("maximum").GetDouble());
        string path="Verification/v542-bot-perf-"+label+".json";
        File.WriteAllText(path,JsonSerializer.Serialize(new{status=worst<=50?"PASS":"FAIL",seed,targetPlans,targetMilliseconds=50,maximum=worst,sources=SourceHashes(),warmup,runs,
            limitation="Release managed actual Advance-to-Plan prefix; this is a fast red-capable gate, not a substitute for browser measurement."},Json));
        Console.WriteLine((worst<=50?"PASS":"FAIL")+" warm fixed-seed planning <=50ms; actual="+worst.ToString("F2"));
        return worst<=50?0:1;
    }
    static Track WithoutHidden(Track source)
    {
        var obstacles=source.Obstacles.Select(o=>new Obstacle{Id=o.Id,X=o.X,Width=o.Width,Height=o.Height,GapTop=o.GapTop,GapBottom=o.GapBottom,
            TopOffsetX=o.TopOffsetX,BottomOffsetX=o.BottomOffsetX,Kind=o.Kind,Hidden=false,HiddenRevealDistance=o.HiddenRevealDistance,HiddenWarningDuration=o.HiddenWarningDuration}).ToArray();
        return new Track(source.Length,obstacles,source.Cues){Id=source.Id,Name=source.Name,RiskZones=source.RiskZones,SafeRoute=source.SafeRoute,RiskRoute=source.RiskRoute};
    }
    static int PerfCausalControl()
    {
        var game=Create(TracksHidden.InterceptsId,FlightPresets.Chosen,BotDifficulty.Novice,2944602636,.8,PartyCamera.Constant);
        var controller=new StochasticBotController(game);RaceSimulation.Press(game,0);controller.Advance(3);
        while(controller.PlanningCalls<1)controller.Advance(1d/60);
        var origin=Forecast(controller);double end=origin.Players[0].Distance+1000;
        Need(end<game.Track.Obstacles.Where(o=>o.Hidden).Min(o=>o.X-HiddenObstacles.RevealDistanceFor(o)),"control never reaches any hidden reveal boundary");
        var controlTrack=WithoutHidden(origin.Track);var tape=StochasticTrackMap.Create(game.Track,game.Flight).ReferenceInputs.Where(x=>x>=origin.Players[0].Distance&&x<=end).ToArray();
        GameState Sim(bool noHidden,bool terminalHuman)
        {
            var g=RaceSnapshot.Clone(origin);if(noHidden)g.Track=controlTrack;if(terminalHuman)g.Players[1].Status=PlayerStatus.Eliminated;
            int edge=0,guard=0;var p=g.Players[0];
            while(p.Distance<end-2e-7)
            {
                if(++guard>10000)throw new Exception("causal control stalled");
                if(edge<tape.Length&&p.Distance>=tape[edge]-2e-7){RaceSimulation.Press(g,0);edge++;continue;}
                double target=edge<tape.Length?Math.Min(end,tape[edge]):end;
                RaceSimulation.Advance(g,Math.Min(.125,Math.Max(1e-10,(target-p.Distance)/RaceConfig.FinishBoostSpeed)));
            }
            return g;
        }
        // Warm all branches, then compare one variable at a time on the same
        // actual first-plan origin, trial inputs and two-player Lagger shell.
        Sim(false,false);Sim(true,false);Sim(false,true);
        const int repetitions=32;
        (double ms,long allocated,GameState last) Measure(bool noHidden,bool terminalHuman)
        {
            long allocated=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();GameState last=null;
            for(int i=0;i<repetitions;i++)last=Sim(noHidden,terminalHuman);
            return(clock.Elapsed.TotalMilliseconds/repetitions,(GC.GetAllocatedBytesForCurrentThread()-allocated)/repetitions,last);
        }
        var baseline=Measure(false,false);var hiddenOff=Measure(true,false);var humanOff=Measure(false,true);
        void SamePhysics(PlayerState a,PlayerState b)
        {
            Near(a.Distance,b.Distance,"causal same-input distance");Near(a.Y,b.Y,"causal same-input height");Near(a.VerticalSpeed,b.VerticalSpeed,"causal same-input momentum");
            Near(a.Recovering,b.Recovering,"causal same-input recovery");Need(a.Collisions==b.Collisions&&a.Support==b.Support&&a.SupportObstacleId==b.SupportObstacleId,"causal same-input contacts");
            Need(a.HitObstacleIds.SetEquals(b.HitObstacleIds)&&a.SuccessfulRiskIds.SetEquals(b.SuccessfulRiskIds),"causal same-input hit/reward IDs");
        }
        for(int i=0;i<baseline.last.Players.Length;i++)SamePhysics(baseline.last.Players[i],hiddenOff.last.Players[i]);
        SamePhysics(baseline.last.Players[0],humanOff.last.Players[0]);Near(baseline.last.Elapsed,hiddenOff.last.Elapsed,"causal same-input world time");
        int checkpoints=controller.ForecastCheckpoints;var cloneClock=Stopwatch.StartNew();GameState copy=null;
        for(int i=0;i<checkpoints;i++)copy=RaceSnapshot.Clone(origin);
        double cloneMs=cloneClock.Elapsed.TotalMilliseconds;Need(copy!=null&&checkpoints>0,"checkpoint control exercised");
        var result=new{checks,seed=2944602636u,repetitions,origin=origin.Players[0].Distance,end,participants=origin.Players.Length,
            sameInputPhysics=true,baselineMs=baseline.ms,hiddenOffMs=hiddenOff.ms,humanOffMs=humanOff.ms,
            baselineAllocatedBytes=baseline.allocated,hiddenOffAllocatedBytes=hiddenOff.allocated,humanOffAllocatedBytes=humanOff.allocated,
            hiddenOffSpeedup=baseline.ms/hiddenOff.ms,humanOffSpeedup=baseline.ms/humanOff.ms,checkpoints,cloneMs,
            actualFirstPlanMs=controller.PlanningSamples[0].Milliseconds,sources=SourceHashes()};
        File.WriteAllText("Verification/v542-bot-perf-control.json",JsonSerializer.Serialize(result,Json));
        Console.WriteLine(JsonSerializer.Serialize(result));return 0;
    }
    static int ComparePerfSemantics()
    {
        // A different analytic partition can add one final sub-epsilon world
        // slice at terminal rounding. Compare its physical result and inputs,
        // not that observer-count detail; search counts remain compared.
        var ignored=new HashSet<string>{"maximum","milliseconds","Milliseconds","maximumPlanningMilliseconds","p95PlanningMilliseconds","advances"};
        void Compare(JsonElement a,JsonElement b,string path)
        {
            Need(a.ValueKind==b.ValueKind,path+" type");
            if(a.ValueKind==JsonValueKind.Object)
            {foreach(var p in a.EnumerateObject())if(!ignored.Contains(p.Name)){Need(b.TryGetProperty(p.Name,out _),path+" property");Compare(p.Value,b.GetProperty(p.Name),path+"."+p.Name);}return;}
            if(a.ValueKind==JsonValueKind.Array)
            {Need(a.GetArrayLength()==b.GetArrayLength(),path+" length");for(int i=0;i<a.GetArrayLength();i++)Compare(a[i],b[i],path+"["+i+"]");return;}
            if(a.ValueKind==JsonValueKind.Number){Near(a.GetDouble(),b.GetDouble(),path);return;}
            Need(a.GetRawText()==b.GetRawText(),path);
        }
        using var before=JsonDocument.Parse(File.ReadAllText("Verification/v542-bot-perf-before.json"));
        using var after=JsonDocument.Parse(File.ReadAllText("Verification/v542-bot-perf-after.json"));
        for(int i=0;i<after.RootElement.GetProperty("runs").GetArrayLength();i++)
            Compare(before.RootElement.GetProperty("runs")[0],after.RootElement.GetProperty("runs")[i],"actual two-plan prefix");
        using var raceBefore=JsonDocument.Parse(File.ReadAllText("Verification/v542-new-track-bots-perf-race-before.json"));
        using var raceAfter=JsonDocument.Parse(File.ReadAllText("Verification/v542-new-track-bots-perf-race-after.json"));
        Compare(raceBefore.RootElement.GetProperty("results")[0],raceAfter.RootElement.GetProperty("results")[0],"actual full fixed-seed race");
        File.WriteAllText("Verification/v542-bot-perf-semantics.json",JsonSerializer.Serialize(new{status="PASS",checks,seed=2944602636u,
            scope="Same actual inputs, selected forecast inputs, intentions, rolls, probabilities, outcomes, search counts and captured physical state; floating tolerance 2e-7. Performance timings and terminal-rounding observer slice count excluded."},Json));
        Console.WriteLine("PASS same fixed-seed semantics checks="+checks);return 0;
    }
    static int Main(string[] args)
    {
        string mode=args.FirstOrDefault()??"--smoke";string output="Verification/v542-new-track-bots"+mode.Replace("--","-")+".json";
        if(mode=="--perf-before"||mode=="--perf-after"||mode=="--perf-min")return PerfLoop(mode.Replace("--perf-",""));
        if(mode=="--perf-control")return PerfCausalControl();
        if(mode=="--compare-perf")return ComparePerfSemantics();
        if(mode.StartsWith("--postfix-",StringComparison.Ordinal))mode=mode.Replace("--postfix-","--");
        var sources=SourceHashes();
        try
        {
            OldMapInvariance();NewMapContract();SharedForecasts();SelectedMasterCriteria();AdaptiveHiddenBoundaries();Console.WriteLine("PASS map/forecast contracts checks="+checks);
            if(mode=="--lower") Results.Add(RunCase(TracksHidden.InterceptsId,FlightPresets.Chosen,BotDifficulty.Novice,123,.8));
            else if(mode=="--perf-race-before"||mode=="--perf-race-after")Results.Add(RunCase(TracksHidden.InterceptsId,FlightPresets.Chosen,BotDifficulty.Novice,2944602636,.8));
            else if(mode=="--shared") Results.Add(RunCase(TracksRethink.Id,FlightPresets.Chosen,BotDifficulty.Master,0,.8,true,resumedLead:true));
            else if(mode!="--contract")
            {
                if(mode!="--matrix")foreach(var (id,f) in Pairs)Results.Add(RunCase(id,f,BotDifficulty.Master,0,.8));
                if(mode=="--matrix")
                {
                    File.WriteAllText(output,JsonSerializer.Serialize(new{status="RUNNING",mode,sources,
                        schedule="Five declared pairs: Novice/Amateur/Confident/Strong seed123 delay.8; Master seed0 delays.4/.6/1/1.2; Confident seed456 delay.8. Master seed0 delay.8 already in smoke evidence. One additional Confident cadence replay."},Json));
                    var medium=new Dictionary<string,object>();
                    foreach(var (id,f) in Pairs)
                    {
                        string pair=id+(f==FlightPresets.Chosen?"/Chosen":"/Fast");
                        foreach(var difficulty in new[]{BotDifficulty.Novice,BotDifficulty.Amateur,BotDifficulty.Confident,BotDifficulty.Strong})
                        {var result=RunCase(id,f,difficulty,123,.8);Results.Add(result);if(difficulty==BotDifficulty.Confident)medium[pair]=result;}
                        foreach(double delay in new[]{.4,.6,1,1.2})Results.Add(RunCase(id,f,BotDifficulty.Master,0,delay));
                        var alternate=RunCase(id,f,BotDifficulty.Confident,456,.8);Results.Add(alternate);
                        string Signature(object value)=>string.Join("|",JsonSerializer.SerializeToElement(value,Json).GetProperty("inputs").EnumerateArray().Select(x=>Math.Round(x.GetDouble(),5)));
                        Need(Signature(medium[pair])!=Signature(alternate),"different seeds produce distinct actual middle-level inputs "+pair);
                    }
                    var cadence=RunCase(TracksHidden.InterceptsId,FlightPresets.FastFlip,BotDifficulty.Confident,123,.8,frame:1d/30);Results.Add(cadence);
                    var first=JsonSerializer.SerializeToElement(medium[TracksHidden.InterceptsId+"/Fast"],Json);
                    var second=JsonSerializer.SerializeToElement(cadence,Json);
                    double[] a=first.GetProperty("inputs").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
                    double[] b=second.GetProperty("inputs").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
                    Need(a.Length==b.Length,"cadence actual input count");for(int i=0;i<a.Length;i++)Near(a[i],b[i],"cadence actual input distance");
                    Need(first.GetProperty("decisions").GetRawText()==second.GetProperty("decisions").GetRawText(),"cadence preserves saved intentions/rolls/outcomes");
                }
            }
            Need(sources.SequenceEqual(SourceHashes()),"production sources frozen through targeted run");
            File.WriteAllText(output,JsonSerializer.Serialize(new{status="PASS",checks,mode,sources,results=Results,limitations="Managed targeted integration/replay cases, not statistics or WebGL frame-budget proof. Unknown future human input is not predicted. Shared resumed-lead fixture begins with an explicitly structural 20-unit human lead; it does not claim a fresh-start human playthrough."},Json));
            Console.WriteLine("PASS checks="+checks+" cases="+Results.Count);return 0;
        }
        catch(Exception error)
        {File.WriteAllText(output,JsonSerializer.Serialize(new{status="FAIL",checks,mode,sources,error=error.ToString(),completed=Results},Json));Console.Error.WriteLine(error);return 1;}
    }
}
