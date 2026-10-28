using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.BotPrototype;

internal static class RuntimeCheck
{
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
    static readonly Dictionary<Type,(Func<object,object,bool> Equal,FieldInfo[] Scalar,FieldInfo[] Nested)> Comparers=new Dictionary<Type,(Func<object,object,bool>,FieldInfo[],FieldInfo[])>();
    static readonly List<object> Evidence=new List<object>();
    static readonly List<string> Lines=new List<string>();
    static readonly Dictionary<string,double[]> HumanMasterRoutes=new Dictionary<string,double[]>();
    static long snapshots,fieldComparisons,observedAdvances,observedPresses;
    static double maxReferenceTimeError,maxReferenceDistanceError,maxReferenceEdgeTimeError;
    static int cases;
    static string context;
    sealed class Plan
    {
        public string Id,TrackId,Profile,Level;
        public int Index;
        public double[] Distances;
        public JsonElement Constant,Lagger;
        public Track Track=>RaceTracks.All.Single(t=>t.Id==TrackId);
        public FlightSettings Flight=>Profile=="Jump"?FlightPresets.Chosen:FlightPresets.FastFlip;
        public BotDifficulty Difficulty=>Enum.Parse<BotDifficulty>(Level);
    }
    sealed class Event
    {
        public string Kind;
        public int Slot;
        public double Delta,Time;
        public bool Accepted;
    }
    static JsonElement Get(JsonElement e,string name)
    {foreach(var p in e.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return p.Value;throw new Exception("Missing "+name);}
    static bool Try(JsonElement e,string name,out JsonElement found)
    {foreach(var p in e.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)){found=p.Value;return true;}found=default;return false;}
    static string Text(JsonElement e,string name)=>Get(e,name).GetString();
    static void Check(bool ok,string message){if(!ok)throw new Exception(context+": "+message);}
    static string HashFile(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static bool Scalar(Type t)=>t.IsPrimitive||t.IsEnum||t==typeof(string)||Nullable.GetUnderlyingType(t)!=null;
    static (Func<object,object,bool> Equal,FieldInfo[] Scalar,FieldInfo[] Nested) Comparator(Type type)
    {
        if(Comparers.TryGetValue(type,out var c))return c;
        var fields=type.GetFields(BindingFlags.Public|BindingFlags.Instance);
        var scalars=fields.Where(f=>Scalar(f.FieldType)).ToArray();var nested=fields.Where(f=>!Scalar(f.FieldType)).ToArray();
        var a=Expression.Parameter(typeof(object),"a");var b=Expression.Parameter(typeof(object),"b");Expression equal=Expression.Constant(true);
        foreach(var f in scalars)equal=Expression.AndAlso(equal,Expression.Equal(Expression.Field(Expression.Convert(a,type),f),Expression.Field(Expression.Convert(b,type),f)));
        c=(Expression.Lambda<Func<object,object,bool>>(equal,a,b).Compile(),scalars,nested);Comparers[type]=c;return c;
    }
    static void Same(object a,object b,string path="player")
    {
        if(a==null||b==null){Check(a==null&&b==null,path+" null mismatch");return;}
        Type t=a.GetType();Check(t==b.GetType(),path+" type");
        if(Scalar(t)){Check(a.Equals(b),path+" scalar mismatch "+a+" vs "+b);fieldComparisons++;return;}
        if(a is HashSet<string> set){Check(set.SetEquals((HashSet<string>)b),path+" set mismatch");fieldComparisons+=set.Count;return;}
        if(a is IEnumerable list)
        {
            var x=list.GetEnumerator();var y=((IEnumerable)b).GetEnumerator();int i=0;
            while(x.MoveNext()){Check(y.MoveNext(),path+" shorter");Same(x.Current,y.Current,path+"["+i+++"]");}Check(!y.MoveNext(),path+" longer");return;
        }
        var c=Comparator(t);fieldComparisons+=c.Scalar.Length;
        if(!c.Equal(a,b))foreach(var f in c.Scalar)Check(Equals(f.GetValue(a),f.GetValue(b)),path+"."+f.Name+" "+f.GetValue(a)+" vs "+f.GetValue(b));
        foreach(var f in c.Nested)Same(f.GetValue(a),f.GetValue(b),path+"."+f.Name);
    }
    static List<Plan> Load()
    {
        var result=new List<Plan>();
        foreach(string file in new[]{"Verification/bots-first-levels.json","Verification/bots-complex-levels.json"})
        {
            using var document=JsonDocument.Parse(File.ReadAllText(file));
            foreach(var pair in Get(document.RootElement,"evidence").EnumerateArray())
            foreach(var level in (Try(pair,"libraries",out var ls)?ls:Get(pair,"levels")).EnumerateArray())
            {
                int index=0;
                foreach(var p in Get(level,"plans").EnumerateArray())result.Add(new Plan{Id=Text(p,"Id"),TrackId=Text(pair,"track"),Profile=Text(pair,"profile"),Level=Text(level,"level"),Index=index++,Distances=Get(p,"Distances").EnumerateArray().Select(x=>x.GetDouble()).ToArray(),Constant=Get(p,"constant").Clone(),Lagger=Get(p,"lagger").Clone()});
            }
        }
        return result;
    }
    static string[] Goals(Track t,PlayerState p)
    {
        var done=new List<string>();
        foreach(var o in t.Obstacles)if(!p.HitObstacleIds.Contains(o.Id)&&p.Distance>=ObstacleGeometry.Right(o)+RaceConfig.PlayerRadius-1e-7)done.Add("obstacle:"+o.Id);
        foreach(var z in t.RiskZones.Where(z=>!z.IsCoin&&!z.IsNegative))if(z.IsFinish?(p.FinishBoostsEarned>0&&p.FinishBoostElapsed>=RaceConfig.FinishBoostDuration-1e-7):p.SuccessfulRiskIds.Contains(z.Id))done.Add((z.IsFinish?"finish:":"positive:")+z.Id);
        return done.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    }
    static void Say(string s){Console.WriteLine(s);Lines.Add(s);}
    static void Factory()
    {
        context="factory";var t=RaceTracks.All[0];var f=FlightPresets.Chosen;
        var g=RaceSimulation.CreateBotRace(t,f,BotDifficulty.Novice,0);
        Check(g.IsBot&&!g.IsParty&&!g.IsSolo&&g.Mode=="bot"&&g.UsesSharedRaceCamera,"classification");
        Check(g.Players.Length==2&&!g.Players[0].Ready&&g.Players[1].Ready&&g.Phase=="ready","bot auto-ready only");
        var c=new BotRaceController(g);c.Advance(1);Check(g.Phase=="ready"&&g.Elapsed==0&&c.Cursor==0,"human readiness gates start");
        Check(RaceSimulation.Press(g,0)&&g.Phase=="countdown","one human starts");c.Advance(3);Check(g.Phase=="racing"&&g.Elapsed==0&&c.Cursor==0,"countdown no bot edges");
        bool rejected=false;try{RaceSimulation.CreateBotRace(t,FlightPresets.Moderate,BotDifficulty.Novice,0);}catch(ArgumentException){rejected=true;}Check(rejected,"custom settings rejected");
        bool ordinaryRejected=false;try{new BotRaceController(RaceSimulation.CreateParty(t,f,2,PartyCamera.Constant,-288.8,1520));}catch(ArgumentException){ordinaryRejected=true;}Check(ordinaryRejected,"controller rejects ordinary party");
        foreach(int seed in new[]{int.MinValue,-1,0,1,int.MaxValue})Check(BotPlanCatalog.Select(t.Id,f,BotDifficulty.Novice,seed).PlanIndex>=0,"all signed seeds supported");
    }
    static object Run(Plan plan,PartyCamera camera,int frameMode,string human,int seed,string coverage,bool saveTape=false)
    {
        context=plan.Id+" "+camera+" frame"+frameMode+" human="+human+" seed="+seed;
        var t=plan.Track;var f=plan.Flight;
        var game=RaceSimulation.CreateBotRace(t,f,plan.Difficulty,seed,camera,-288.8,1520);
        Check(game.BotPlan.Id==plan.Id&&game.BotPlan.Distances.SequenceEqual(plan.Distances),"production selected exact finite plan");
        var ordinary=RaceSimulation.CreateParty(t,f,2,camera,-288.8,1520);
        // Factory auto-readiness is matched through ordinary public readiness Press.
        Check(RaceSimulation.Press(ordinary,1),"ordinary setup bot-ready Press");Same(game.Players[1],ordinary.Players[1]);
        var controller=new BotRaceController(game);var tape=saveTape?new List<Event>():null;
        var edges=new List<(double Time,bool Accepted)>();long beforeSnapshots=snapshots,beforeAdvances=observedAdvances,beforePresses=observedPresses;
        Action compare=()=>{Same(game.Players[1],ordinary.Players[1]);Check(game.Phase==ordinary.Phase&&game.Elapsed==ordinary.Elapsed,"ordinary replay world time/phase");snapshots++;};
        Action<int> press=slot=>{bool a=RaceSimulation.Press(game,slot),b=RaceSimulation.Press(ordinary,slot);Check(a==b,"ordinary human Press acceptance");tape?.Add(new Event{Kind="press",Slot=slot,Time=game.Elapsed,Accepted=a});compare();};
        controller.Observer=s=>
        {
            if(s.Kind=="advance"){RaceSimulation.Advance(ordinary,s.Delta);observedAdvances++;tape?.Add(new Event{Kind="advance",Delta=s.Delta,Time=s.Time});}
            else{bool accepted=RaceSimulation.Press(ordinary,1);Check(accepted==s.Accepted,"ordinary bot Press acceptance");observedPresses++;edges.Add((s.Time,s.Accepted));tape?.Add(new Event{Kind="press",Slot=1,Time=s.Time,Accepted=s.Accepted});}
            compare();
        };
        press(0);Check(game.Phase=="countdown","actual one-human countdown");
        double[][] frames={new[]{1d/30},new[]{1d/60},new[]{.005,.027,.011,.073,.001,.049,.016,.211}};
        string humanKey=t.Id+"/"+plan.Profile;
        if(!HumanMasterRoutes.ContainsKey(humanKey))HumanMasterRoutes[humanKey]=Probe.Master(t,f);
        double[] humanTimes=human=="success"?HumanMasterRoutes[humanKey]:human=="errors"?Enumerable.Range(1,240).Select(i=>i*.37).ToArray():Array.Empty<double>();
        int humanCursor=0,frame=0;bool countdownPaused=false,racePaused=false;int pauseChecks=0;double? terminalMinimum=null;int minimumFreezeChecks=0;
        Action pause=()=>
        {
            string phase=game.Phase;string snapshot=JsonSerializer.Serialize(game.Players,Json);double elapsed=game.Elapsed,left=game.PartyCameraLeft;int cursor=controller.Cursor;long count=snapshots;
            controller.Advance(9,true);controller.Advance(double.NaN);controller.Advance(double.PositiveInfinity);controller.Advance(-1);controller.Advance(0);
            Check(snapshot==JsonSerializer.Serialize(game.Players,Json)&&elapsed==game.Elapsed&&left==game.PartyCameraLeft&&cursor==controller.Cursor&&count==snapshots,"paused/invalid delta freeze and no events");
            game.Phase="paused";ordinary.Phase="paused";tape?.Add(new Event{Kind="paused",Time=game.Elapsed});controller.Advance(.19);Check(game.Elapsed==elapsed&&snapshot==JsonSerializer.Serialize(game.Players,Json),"paused phase freeze");
            game.Phase=phase;ordinary.Phase=phase;tape?.Add(new Event{Kind="resume-"+phase,Time=game.Elapsed});pauseChecks++;
        };
        while(game.Phase=="countdown"||game.Phase=="racing")
        {
            Check(frame++<20000,"race finite frame guard");
            if(!countdownPaused&&game.Phase=="countdown"&&game.CountdownRemaining<2.8){pause();countdownPaused=true;}
            if(!racePaused&&game.Phase=="racing"&&game.Elapsed>.2){pause();racePaused=true;}
            double remaining=frames[frameMode][(frame-1)%frames[frameMode].Length];int splitGuard=0;
            while(remaining>1e-12&&(game.Phase=="countdown"||game.Phase=="racing"))
            {
                Check(++splitGuard<1000,"human boundary guard");
                if(game.Phase=="racing"&&humanCursor<humanTimes.Length&&game.Players[0].Status==PlayerStatus.Running)
                {
                    double until=humanTimes[humanCursor]-(human=="success"?game.Players[0].Distance:game.Elapsed);
                    if(until<=(human=="success"?2e-7:1e-9)){press(0);humanCursor++;continue;}
                    double bound=human=="success"?(game.Players[0].Recovering>0?game.Players[0].Recovering:Math.Max(1e-9,until/RaceConfig.FinishBoostSpeed)):until;
                    double part=Math.Min(remaining,bound);controller.Advance(part);remaining-=part;
                }
                else{controller.Advance(remaining);remaining=0;}
            }
            Check(game.Elapsed<120,"all participants reach actual terminal");
            if(game.Players[1].Status!=PlayerStatus.Running){if(terminalMinimum.HasValue){Check(controller.MinimumSampledMargin==terminalMinimum.Value,"personal terminal minimum freezes while human continues");minimumFreezeChecks++;}else terminalMinimum=controller.MinimumSampledMargin;}
        }
        Check(game.Phase=="finished"&&game.Players.All(p=>p.Status!=PlayerStatus.Running),"wait for both actual outcomes");
        Check(controller.Cursor==edges.Count,"all bot edge attempts observed exactly once");
        if(human=="success")Check(game.Players[0].Status==PlayerStatus.Finished&&Goals(t,game.Players[0]).Length==BotPlanCatalog.GoalCount(t.Id,f),"successful human completes every objective through legal live-state inputs");
        var p=game.Players[1];var reference=camera==PartyCamera.Constant?plan.Constant:plan.Lagger;
        string[] expected=Get(reference,"CompletedIds").EnumerateArray().Select(x=>x.GetString()).ToArray();
        string[] completed=Goals(t,p);Check(expected.SequenceEqual(completed),"runtime completed goals match full plan reference");
        Check(Text(reference,"Status")==p.Status.ToString()&&Get(reference,"Collisions").GetInt32()==p.Collisions,"reference actual status/collisions");
        double timeError=Math.Abs(Get(reference,"Time").GetDouble()-p.TerminalTime.Value);
        double referenceDistance=Try(reference,"p",out var oldDistance)?oldDistance.GetDouble():t.Length;
        double distanceError=Math.Abs(referenceDistance-p.Distance);
        Check(timeError<2e-7&&distanceError<5e-5,"reference timing/distance tolerance "+timeError+" / "+distanceError);
        Check(Get(reference,"Accepted").GetInt32()==edges.Count(e=>e.Accepted)&&Get(reference,"Rejected").GetInt32()==edges.Count(e=>!e.Accepted),"reference input acceptance");
        var expectedEdges=Get(reference,"Actions").EnumerateArray().ToArray();Check(expectedEdges.Length==edges.Count,"reference edge count");
        double edgeTimeError=expectedEdges.Select((e,i)=>Math.Abs(Get(e,"Time").GetDouble()-edges[i].Time)).DefaultIfEmpty(0).Max();Check(edgeTimeError<2e-7,"reference input time error "+edgeTimeError);
        maxReferenceTimeError=Math.Max(maxReferenceTimeError,timeError);maxReferenceDistanceError=Math.Max(maxReferenceDistanceError,distanceError);maxReferenceEdgeTimeError=Math.Max(maxReferenceEdgeTimeError,edgeTimeError);
        // Terminal participants and the plan cursor remain frozen after all outcomes.
        string terminal=JsonSerializer.Serialize(game.Players,Json);int endCursor=controller.Cursor;controller.Advance(7);Check(terminal==JsonSerializer.Serialize(game.Players,Json)&&controller.Cursor==endCursor,"terminal physical states and controller freeze");
        if(saveTape)
        {
            var replay=RaceSimulation.CreateParty(t,f,2,camera,-288.8,1520);RaceSimulation.Press(replay,1);
            foreach(var e in tape)
            {
                if(e.Kind=="advance")RaceSimulation.Advance(replay,e.Delta);
                else if(e.Kind=="press")Check(RaceSimulation.Press(replay,e.Slot)==e.Accepted,"saved tape acceptance");
                else if(e.Kind=="paused")replay.Phase="paused";
                else if(e.Kind.StartsWith("resume-",StringComparison.Ordinal))replay.Phase=e.Kind.Substring(7);
            }
            Same(game.Players[1],replay.Players[1]);
            File.WriteAllText("Verification/bots-runtime-tape.json",JsonSerializer.Serialize(new{plan.Id,plan.TrackId,plan.Profile,plan.Level,seed,camera=camera.ToString(),frameMode,human,setup="ordinary CreateParty two players; ordinary Press(slot1) matches factory auto-ready; thereafter recorded actual calls in order",eventCount=tape.Count,tape,finalPlayer=game.Players[1]},Json));
        }
        cases++;
        return new{coverage,plan.Id,plan.TrackId,plan.Profile,plan.Level,seed,camera=camera.ToString(),frameMode,human,status=p.Status.ToString(),distance=p.Distance,time=p.TerminalTime,terminalDistance=p.Distance,terminalTime=p.TerminalTime,completedIds=completed,completed=completed.Length,total=BotPlanCatalog.GoalCount(t.Id,f),p.Collisions,p.CompletedRecoveries,recoverySeconds=p.CompletedRecoveries*RaceConfig.RecoveryDuration+p.RecoveryElapsed,boosts=p.BoostsEarned,finishBoosts=p.FinishBoostsEarned,accepted=edges.Count(e=>e.Accepted),rejected=edges.Count(e=>!e.Accepted),minimumMarginLowerBound=controller.ConservativeMargin,timeError,distanceError,edgeTimeError,immediateSnapshots=snapshots-beforeSnapshots,actualAdvanceCalls=observedAdvances-beforeAdvances,actualBotPressCalls=observedPresses-beforePresses,maximumReplayNumericError=0,pauseChecks,minimumFreezeChecks,humanStatus=game.Players[0].Status.ToString(),humanGoals=Goals(t,game.Players[0]).Length,bothTerminal=true};
    }
    static object OrderCheck(Plan plan)
    {
        context="simultaneous input order "+plan.Id;
        var actual=RaceSimulation.CreateBotRace(plan.Track,plan.Flight,plan.Difficulty,plan.Index,PartyCamera.Constant);
        var first=RaceSimulation.CreateParty(plan.Track,plan.Flight,2,PartyCamera.Constant,-288.8,1520);
        var second=RaceSimulation.CreateParty(plan.Track,plan.Flight,2,PartyCamera.Constant,-288.8,1520);
        RaceSimulation.Press(actual,0);
        foreach(var g in new[]{first,second}){RaceSimulation.Press(g,1);RaceSimulation.Press(g,0);}
        var controller=new BotRaceController(actual);long comparisons=0;int simultaneous=0;
        controller.Observer=e=>
        {
            if(e.Kind=="advance"){RaceSimulation.Advance(first,e.Delta);RaceSimulation.Advance(second,e.Delta);}
            else
            {
                bool ha=RaceSimulation.Press(first,0),ba=RaceSimulation.Press(first,1);
                bool bb=RaceSimulation.Press(second,1),hb=RaceSimulation.Press(second,0);
                Check(ha==hb&&ba==bb,"simultaneous human/bot order acceptance");simultaneous++;
            }
            Same(first.Players[1],second.Players[1]);Same(first.Players[0],second.Players[0]);comparisons++;
        };
        int guard=0;while(actual.Phase!="finished"){controller.Advance(1d/30);Check(++guard<10000,"order test termination");}
        Check(first.Players[1].Status!=PlayerStatus.Running&&second.Players[1].Status!=PlayerStatus.Running,"order bot terminal");
        return new{plan.Id,simultaneousPressPairs=simultaneous,immediateTwoParticipantSnapshots=comparisons,maximumNumericError=0,verified=true};
    }
    static int Main(string[] args)
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
        var files=Directory.GetFiles("Assets/Scripts/Core","*.cs").Concat(new[]{"Tools/BotPrototype/RuntimeCheck.cs","Tools/BotPrototype/SharedProbe.cs","Verification/bots-first-levels.json","Verification/bots-complex-levels.json"}).ToArray();
        var sourceHashes=files.ToDictionary(p=>p.Replace((char)92,(char)47),HashFile);
        try
        {
            Factory();var plans=Load();Check(plans.Count==343&&BotPlanCatalog.TotalPlans==plans.Count,"343-plan source coverage");
            bool smoke=args.Contains("--smoke")||args.Contains("--short-smoke");var all=smoke?(args.Contains("--short-smoke")?plans.Where(p=>p.TrackId==TracksFull.ShortId&&p.Profile=="Jump"):plans).Take(1).ToList():plans;
            foreach(var pair in all.GroupBy(p=>p.TrackId+"/"+p.Profile))
            {
                foreach(var p in pair)foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})for(int mode=0;mode<3;mode++)
                    Evidence.Add(Run(p,camera,mode,"idle",p.Index,"every-plan",cases==0));
                Say("All plans: "+pair.Key+" plans="+pair.Count()+" cases="+cases+" immediateSnapshots="+snapshots);
            }
            foreach(var library in all.GroupBy(p=>p.TrackId+"/"+p.Profile+"/"+p.Level))
            {
                var first=library.First();var choices=plans.Where(p=>p.TrackId==first.TrackId&&p.Profile==first.Profile&&p.Level==first.Level).OrderBy(p=>p.Index).ToArray();
                for(int seed=0;seed<(smoke?3:20);seed++)foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
                    Evidence.Add(Run(choices[seed%choices.Length],camera,seed%3,new[]{"idle","success","errors"}[seed%3],seed,"fixed-seed"));
                Say("Fixed seeds: "+library.Key+" cases="+cases);
            }
            var orderEvidence=plans.Where(p=>p.Level=="Master"&&p.Index==0).Select(OrderCheck).ToArray();
            bool sourceStable=sourceHashes.All(p=>HashFile(p.Key)==p.Value);Check(sourceStable,"sources unchanged during observer replay proof");
            var result=new{status="PASS",scope="BOT-05 production BotRaceController observer-driven actual action replay on independent ordinary CreateParty slot1, complete public PlayerState after each call",smoke,plansCovered=all.Count,cases,snapshots,fieldComparisons,observedAdvances,observedPresses,maximumReplayNumericError=0,maxReferenceTimeError,maxReferenceDistanceError,maxReferenceEdgeTimeError,sourceHashes,sourceStable,orderEvidence,referenceDistanceProvenance="Original Complex calibration report may omit/null Distance; every runtime case records authoritative actual terminalDistance, terminalTime, status, completedIds and margin directly from game.Players[1].",provenance="Every actual controller Advance(dt) and Press accepted/rejected is replayed immediately in the same order on ordinary participant. Goal/status comparison uses saved reference metrics. Successful human inputs follow live player distance/recovery and legal Master distances; no independent Probe.Run is used as action replay.",evidence=Evidence};
            string prefix=smoke?"Verification/bots-runtime-smoke":"Verification/bots-runtime-check";
            File.WriteAllText(prefix+".json",JsonSerializer.Serialize(result,Json));Say("PASS runtime actual replay: "+cases+" cases; "+snapshots+" complete immediate snapshots; "+fieldComparisons+" field comparisons; maximum replay error0");File.WriteAllLines(prefix+".txt",Lines);return 0;
        }
        catch(Exception e){Say("FAIL "+e);File.WriteAllText("Verification/bots-runtime-failure.json",JsonSerializer.Serialize(new{status="FAIL",context,cases,snapshots,fieldComparisons,error=e.ToString(),sourceHashes},Json));return 1;}
    }
}