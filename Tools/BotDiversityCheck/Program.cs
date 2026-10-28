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

internal static class BotDiversityRuntimeCheck
{
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=false};
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
        public string Id,TrackId,Profile,Level,SignatureId;
        public int Index;
        public double[] Distances;
        public JsonElement Constant,Lagger;
        public Track Track=>RaceTracks.All.Single(t=>t.Id==TrackId);
        public FlightSettings Flight=>(Profile=="Jump"||Profile=="Chosen")?FlightPresets.Chosen:FlightPresets.FastFlip;
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
        var result=new List<Plan>();using var document=JsonDocument.Parse(File.ReadAllText("Verification/bot-diversity-plans.json"));
        Check(Text(document.RootElement,"status")=="PASS","final complete release artifact required");
        foreach(var library in Get(document.RootElement,"libraries").EnumerateArray())
        {
            int index=0;
            foreach(var p in Get(library,"plans").EnumerateArray())
                result.Add(new Plan{Id=Text(p,"id"),SignatureId=Text(p,"signatureId"),TrackId=Text(library,"trackId"),Profile=Text(library,"profile"),Level=Text(library,"difficulty"),Index=index++,Distances=Get(p,"distances").EnumerateArray().Select(x=>x.GetDouble()).ToArray(),Constant=Get(p,"constant").Clone(),Lagger=Get(p,"lagger").Clone()});
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
    static object Run(Plan plan,PartyCamera camera,int frameMode,string human,int seed,string coverage,bool saveTape=false,long sequenceIndex=-1)
    {
        context=plan.Id+" "+camera+" frame"+frameMode+" human="+human+" seed="+seed;
        var t=plan.Track;var f=plan.Flight;
        var chosen=BotPlanCatalog.SelectAt(t.Id,f,plan.Difficulty,plan.Index,seed);
        var game=RaceSimulation.CreateBotRace(t,f,chosen,camera,-288.8,1520);
        Check(game.BotPlan.Seed==seed&&game.BotPlan.PlanIndex==plan.Index&&game.BotPlan.SignatureId==plan.SignatureId,"direct selected index/attempt metadata retained");
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
        double referenceDistance=Try(reference,"terminalDistance",out var actualDistance)?actualDistance.GetDouble():Try(reference,"p",out var oldDistance)?oldDistance.GetDouble():t.Length;
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
            File.WriteAllText("Verification/bot-diversity-runtime-tape.json",JsonSerializer.Serialize(new{plan.Id,plan.TrackId,plan.Profile,plan.Level,seed,camera=camera.ToString(),frameMode,human,setup="ordinary CreateParty two players; ordinary Press(slot1) matches factory auto-ready; thereafter recorded actual calls in order",eventCount=tape.Count,tape,finalPlayer=game.Players[1]},Json));
        }
        cases++;
        return new{coverage,plan.Id,plan.SignatureId,plan.TrackId,plan.Profile,plan.Level,seed,sequenceIndex,camera=camera.ToString(),frameMode,human,status=p.Status.ToString(),distance=p.Distance,time=p.TerminalTime,terminalDistance=p.Distance,terminalTime=p.TerminalTime,completedIds=completed,completed=completed.Length,total=BotPlanCatalog.GoalCount(t.Id,f),p.Collisions,p.CompletedRecoveries,recoverySeconds=p.CompletedRecoveries*RaceConfig.RecoveryDuration+p.RecoveryElapsed,boosts=p.BoostsEarned,finishBoosts=p.FinishBoostsEarned,accepted=edges.Count(e=>e.Accepted),rejected=edges.Count(e=>!e.Accepted),minimumMarginLowerBound=controller.ConservativeMargin,timeError,distanceError,edgeTimeError,immediateSnapshots=snapshots-beforeSnapshots,actualAdvanceCalls=observedAdvances-beforeAdvances,actualBotPressCalls=observedPresses-beforePresses,maximumReplayNumericError=0,pauseChecks,minimumFreezeChecks,humanStatus=game.Players[0].Status.ToString(),humanGoals=Goals(t,game.Players[0]).Length,bothTerminal=true};
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
    static int Option(string[] args,string key,int fallback)
    {string value=args.FirstOrDefault(x=>x.StartsWith(key+"=",StringComparison.Ordinal));return value==null?fallback:int.Parse(value.Substring(key.Length+1));}
    static int Main(string[] args)
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
        int batch=Option(args,"--batch",0),parts=Option(args,"--parts",1);if(parts<=0||batch<0||batch>=parts)throw new ArgumentException("Invalid batch/parts");
        bool smoke=args.Contains("--smoke");string prefix="Verification/bot-diversity-runtime-"+(smoke?"smoke-":"")+batch;
        var files=Directory.GetFiles("Assets/Scripts/Core","*.cs").Concat(new[]{"Tools/BotDiversityCheck/Program.cs","Tools/BotPrototype/SharedProbe.cs","Verification/bot-diversity-plans.json"}).ToArray();
        var sourceHashes=files.ToDictionary(p=>p.Replace((char)92,(char)47),HashFile);
        try
        {
            Factory();var plans=Load();Check(BotPlanCatalog.TotalPlans==plans.Count,"all published production plan coverage");
            Check(plans.Select(p=>p.TrackId+"/"+p.Profile+"/"+p.Level).Distinct().Count()==50,"all50 libraries");
            var all=smoke?plans.GroupBy(p=>p.TrackId+"/"+p.Profile+"/"+p.Level).Take(2).Select(g=>g.First()).ToList():plans;
            int serial=0,assigned=0;
            Action<Plan,PartyCamera,int,string,int,string,long> add=(plan,camera,frame,human,seed,coverage,sequence)=>
            {
                int number=serial++;if(number%parts!=batch)return;assigned++;
                Evidence.Add(Run(plan,camera,frame,human,seed,coverage,number==0,sequence));
            };
            foreach(var pair in all.GroupBy(p=>p.TrackId+"/"+p.Profile))
            {
                foreach(var p in pair)foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})for(int frame=0;frame<3;frame++)add(p,camera,frame,"idle",p.Index,"every-plan",-1);
                Say("Published plans "+pair.Key+" n="+pair.Count()+" assigned="+cases+" snapshots="+snapshots);
            }
            uint sessionSeed=0x51a21u;var shuffle=new BotPlanShuffle(sessionSeed);
            foreach(var library in all.GroupBy(p=>p.TrackId+"/"+p.Profile+"/"+p.Level))
            {
                var first=library.First();var choices=plans.Where(p=>p.TrackId==first.TrackId&&p.Profile==first.Profile&&p.Level==first.Level).OrderBy(p=>p.Index).ToArray();
                var groups=BotPlanCatalog.SignatureGroups(first.TrackId,first.Flight,first.Difficulty);var signatures=new HashSet<string>();int previous=-1;
                for(int i=0;i<(smoke?3:20);i++)
                {
                    var choice=shuffle.Next(library.Key,groups);Check(choice.PlanIndex>=0&&choice.PlanIndex<choices.Length,"selected plan index in library");
                    var selected=choices[choice.PlanIndex];Check(selected.SignatureId==choices[groups[choice.SignatureIndex][0]].SignatureId,"selected group matches actual signature");
                    if(groups.Length>1){if(i%groups.Length==0)signatures.Clear();Check(signatures.Add(selected.SignatureId),"no signature repeat before bag exhaustion");Check(choice.SignatureIndex!=previous,"no seam adjacent signature repeat");}previous=choice.SignatureIndex;
                    foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})add(selected,camera,i%3,new[]{"idle","success","errors"}[i%3],choice.AttemptSeed,"shuffled-session-sequence",choice.SequenceIndex);
                }
                Say("Session library "+library.Key+" assigned="+cases);
            }
            var orderEvidence=plans.Where(p=>p.Level=="Master"&&p.Index==0).Select((p,i)=>(p,i)).Where(x=>x.i%parts==batch).Select(x=>OrderCheck(x.p)).ToArray();
            bool sourceStable=sourceHashes.All(p=>HashFile(p.Key)==p.Value);Check(sourceStable,"final sources unchanged during actual observer replay");Check(cases==assigned,"no assigned cases filtered");
            var result=new{status="PASS",scope="BVAR06 actual production controller observer Advance/Press replay on ordinary player; complete public personal fields immediately",smoke,batch,parts,totalScheduledCases=serial,assignedCases=assigned,plansCovered=all.Count,cases,snapshots,fieldComparisons,observedAdvances,observedPresses,maximumReplayNumericError=0,maxReferenceTimeError,maxReferenceDistanceError,maxReferenceEdgeTimeError,sessionSeed,sourceHashes,sourceStable,orderEvidence,provenance="Each actual controller Advance(dt)/Press acceptance replayed in identical order on ordinary participant; all published tapes/cameras/frames and unfiltered signature-shuffle sequence. No physical state injection. Pause phase fixtures are matched in both games; selected plans use direct index, never AttemptSeed modulo.",evidence=Evidence};
            File.WriteAllText(prefix+".json",JsonSerializer.Serialize(result,Json));Say("PASS actual runtime replay batch"+batch+"/"+parts+": "+cases+" cases, "+snapshots+" complete immediate snapshots, replay error0");File.WriteAllLines(prefix+".txt",Lines);return 0;
        }
        catch(Exception e){Say("FAIL "+e);File.WriteAllText(prefix+"-failure.json",JsonSerializer.Serialize(new{status="FAIL",context,batch,parts,cases,snapshots,fieldComparisons,error=e.ToString(),sourceHashes},Json));return 1;}
    }
}