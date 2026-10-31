using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.BotPrototype;
static class StochasticRisk
{
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
    static readonly Dictionary<Type,(Func<object,object,bool> Equal,FieldInfo[] Scalar,FieldInfo[] Nested)> Comparers=new Dictionary<Type,(Func<object,object,bool>,FieldInfo[],FieldInfo[])>();
    static readonly List<CaseResult> Results=new List<CaseResult>();
    static readonly List<string> Lines=new List<string>();
    static long snapshots,fieldComparisons;
    static string context="prepare";
    static double horizonLimit=1000;
    static double trialHorizonLimit=1055,trialOverrunLimit=55;
    const double HorizonTolerance=2e-7;
    static long candidatesPerPlanLimit=-1;
    static bool ReproSeed4;
    static string OutputStem="Verification/stochastic-bots-risk";
    sealed class CaseSpec {public string Track,Profile,Camera;public double Q;public uint Seed;public int Frame;public string Key=>Track+"/"+Profile+"/"+Q.ToString("R",CultureInfo.InvariantCulture)+"/"+Seed+"/"+Camera+"/"+Frame;}
    sealed class DecisionRecord
    {
        public string Id,Kind,Intent,Outcome,UnreachableReason;public double Probability,Roll,AtDistance,AtTime,HorizonEndDistance;public double[] SelectedInputs;
    }
    sealed class Fact
    {
        public string Kind,Id;public double ObservedAtTime,ObservedAtDistance,IntervalStartTime,IntervalEndTime;public double? ExactEventTime;
        public string[] PendingBefore,PendingAfter,FailedAfter,UntouchedPositiveAfter;
    }
    sealed class CaseResult
    {
        public CaseSpec Case;public string Status,Phase,Error;public double Time,Distance,WallMilliseconds,P95AdvanceMilliseconds,MaximumAdvanceMilliseconds;
        public string[] SuccessfulIds,FailedIds,CollectedIds,HitIds,MissedPositiveIds;public double ReferenceMasterTime;
        public int Collisions,Boosts,FinishBoosts,AcceptedInputs,RejectedInputs,Cursor;public long ImmediateSnapshots,FieldComparisons;
        public long PlanningCalls,CandidateSimulations,ForecastSteps,ForecastObservations,ForecastHorizonViolations;
        public double MaximumRequestedHorizon,MaximumTrialHorizon,MaximumTrialOvershoot;
        public List<DecisionRecord> Decisions=new List<DecisionRecord>();public List<Fact> Facts=new List<Fact>();public List<object> Inputs=new List<object>();
        public object BranchCoverage;public bool ReplayEqual,TerminalFrozen,PauseFrozen;
    }
    static void Need(bool ok,string why){if(!ok)throw new Exception(context+": "+why);}
    static string HashFile(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static void Say(string text){Lines.Add(text);Console.WriteLine(text);}
    static double Percentile(List<double> values,double q){var a=values.OrderBy(x=>x).ToArray();return a.Length==0?0:a[(int)Math.Floor((a.Length-1)*q)];}
    static string State(PlayerState p)=>JsonSerializer.Serialize(p,Json);
    static string[] Pending(PlayerState p)=>p.CollectedRiskIds.Except(p.SuccessfulRiskIds).Except(p.FailedRiskIds).Except(p.NegativeRiskIds).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    static string[] Positives(Track t)=>t.RiskZones.Where(z=>!z.IsCoin&&!z.IsNegative&&!z.IsFinish).Select(z=>z.Id).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    static DecisionRecord Record(StochasticBotDecision d)=>new DecisionRecord{Id=d.Id,Kind=d.Kind.ToString(),Intent=d.Intent,Probability=d.Probability,Roll=d.Roll,AtDistance=d.AtDistance,AtTime=d.AtTime,HorizonEndDistance=d.HorizonEndDistance,Outcome=d.Outcome,UnreachableReason=d.UnreachableReason,SelectedInputs=d.SelectedInputs};
    static string ChoiceKey(DecisionRecord d)=>JsonSerializer.Serialize(new{d.Id,d.Kind,d.Intent,d.Probability,d.Roll,d.AtDistance,d.AtTime,d.HorizonEndDistance,d.SelectedInputs},Json);
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
        if(a==null||b==null){Need(a==null&&b==null,path+" null mismatch");return;}
        Type t=a.GetType();Need(t==b.GetType(),path+" type");
        if(Scalar(t)){Need(a.Equals(b),path+" scalar mismatch "+a+" vs "+b);fieldComparisons++;return;}
        if(a is HashSet<string> set){Need(set.SetEquals((HashSet<string>)b),path+" set mismatch");fieldComparisons+=set.Count;return;}
        if(a is IEnumerable list)
        {
            var x=list.GetEnumerator();var y=((IEnumerable)b).GetEnumerator();int i=0;
            while(x.MoveNext()){Need(y.MoveNext(),path+" shorter");Same(x.Current,y.Current,path+"["+i+++"]");}Need(!y.MoveNext(),path+" longer");return;
        }
        var c=Comparator(t);fieldComparisons+=c.Scalar.Length;
        if(!c.Equal(a,b))foreach(var f in c.Scalar)Need(Equals(f.GetValue(a),f.GetValue(b)),path+"."+f.Name+" "+f.GetValue(a)+" vs "+f.GetValue(b));
        foreach(var f in c.Nested)Same(f.GetValue(a),f.GetValue(b),path+"."+f.Name);
    }
    static readonly Dictionary<string,double> MasterTimes=new Dictionary<string,double>();
    static CaseResult Run(CaseSpec spec)
    {
        context=spec.Key;Track track=RaceTracks.Get(spec.Track);FlightSettings flight=spec.Profile=="Chosen"?FlightPresets.Chosen:FlightPresets.FastFlip;
        var result=new CaseResult{Case=spec};Results.Add(result);var watch=Stopwatch.StartNew();long startSnapshots=snapshots,startFields=fieldComparisons;
        var game=RaceSimulation.CreateSolo(track,flight,Enum.Parse<PartyCamera>(spec.Camera),-288.8,1520);
        var ordinary=RaceSimulation.CreateSolo(track,flight,Enum.Parse<PartyCamera>(spec.Camera),-288.8,1520);var p=game.Players[0];
        long observerTicks=0;var calls=new List<double>();var choices=new Dictionary<string,string>(StringComparer.Ordinal);var lastDecisions=new Dictionary<string,DecisionRecord>(StringComparer.Ordinal);
        var successSeen=new HashSet<string>();var hitSeen=new HashSet<string>();var failedSeen=new HashSet<string>();var collectedSeen=new HashSet<string>();var pendingSeen=new HashSet<string>();
        var logical=StochasticTrackMap.Create(track,flight).Opportunities.ToDictionary(o=>o.Kind+"/"+o.Id,o=>o,StringComparer.Ordinal);
        string[] positiveIds=Positives(track);double lastObservedTime=0;string[] previousPending=Array.Empty<string>();int completedRecoveries=0;
        StochasticBotController controller=null;
        Action compare=()=>{result.ReplayEqual=false;Same(p,ordinary.Players[0]);Need(game.Phase==ordinary.Phase&&game.Elapsed==ordinary.Elapsed&&game.CountdownRemaining==ordinary.CountdownRemaining&&game.PartyCameraLeft==ordinary.PartyCameraLeft,"ordinary observer replay world time/phase/camera");snapshots++;result.ReplayEqual=true;};
        Action decisions=()=>
        {
            var current=new HashSet<string>(StringComparer.Ordinal);
            foreach(var decision in controller.Decisions)
            {
                var d=Record(decision);string key=d.Kind+"/"+d.Id;
                Need(logical.ContainsKey(key),"controller decision does not identify a mapped logical opportunity");
                Need(!string.IsNullOrWhiteSpace(d.Id)&&current.Add(key),"logical opportunity has more than one saved decision");
                Need(double.IsFinite(d.Probability)&&d.Probability>=0&&d.Probability<=1&&double.IsFinite(d.Roll)&&d.Roll>=0&&d.Roll<1,"invalid published probability/roll");
                Need(double.IsFinite(d.AtDistance)&&double.IsFinite(d.AtTime)&&double.IsFinite(d.HorizonEndDistance)&&d.HorizonEndDistance>=d.AtDistance&&d.HorizonEndDistance-d.AtDistance<=horizonLimit+HorizonTolerance,"invalid decision/requested horizon");
                Need(d.SelectedInputs!=null&&d.SelectedInputs.Length<=StochasticBotController.MaxInputs,"selected input count exceeded published cap");
                for(int j=0;j<d.SelectedInputs.Length;j++)Need(double.IsFinite(d.SelectedInputs[j])&&d.SelectedInputs[j]>=d.AtDistance-1e-7&&d.SelectedInputs[j]<=d.HorizonEndDistance+1e-7&&(j==0||d.SelectedInputs[j]>=d.SelectedInputs[j-1]),"selected input outside bounded initial distance horizon");
                string saved=ChoiceKey(d);if(choices.TryGetValue(key,out string first))Need(saved==first,"decision was rerolled or selected inputs rewritten after commitment");else choices.Add(key,saved);
                lastDecisions[key]=d;
            }
            Need(current.Count==choices.Count,"a saved logical decision vanished");
        };
        Action facts=()=>
        {
            string[] pendingNow=Pending(p);foreach(string id in pendingNow)pendingSeen.Add(id);
            Action<string,string,HitInfo> add=(kind,id,known)=>result.Facts.Add(new Fact{Kind=kind,Id=id,ObservedAtTime=game.Elapsed,ObservedAtDistance=p.Distance,IntervalStartTime=lastObservedTime,IntervalEndTime=game.Elapsed,ExactEventTime=known!=null&&known.Id==id?(double?)known.At:null,PendingBefore=previousPending,PendingAfter=pendingNow,FailedAfter=p.FailedRiskIds.OrderBy(x=>x).ToArray(),UntouchedPositiveAfter=positiveIds.Where(x=>!p.CollectedRiskIds.Contains(x)&&!p.FailedRiskIds.Contains(x)&&!p.SuccessfulRiskIds.Contains(x)).ToArray()});
            foreach(string id in p.CollectedRiskIds.Where(x=>collectedSeen.Add(x)))add("collected-contact-observed",id,null);
            foreach(string id in p.HitObstacleIds.Where(x=>hitSeen.Add(x)))add("hit",id,p.LastHit);
            foreach(string id in p.SuccessfulRiskIds.Where(x=>successSeen.Add(x)))
            {
                if(positiveIds.Contains(id))add("positive-effect",id,p.LastSuccess);
                else if(track.RiskZones.Any(z=>z.Id==id&&z.IsCoin))add("coin-effect",id,p.LastCoin);
            }
            foreach(string id in p.FailedRiskIds.Where(x=>failedSeen.Add(x)))add("failed",id,null);
            if(p.CompletedRecoveries>completedRecoveries){add("recovery-complete",p.LastHit==null?"unknown-hit":p.LastHit.Id,null);completedRecoveries=p.CompletedRecoveries;}
            if(p.LastFinishBoost!=null&&!result.Facts.Any(x=>x.Kind=="finish-effect"&&x.Id==p.LastFinishBoost.Id))add("finish-effect",p.LastFinishBoost.Id,p.LastFinishBoost);
            previousPending=pendingNow;lastObservedTime=game.Elapsed;
        };
        try
        {
            controller=new StochasticBotController(game,0,spec.Q,spec.Seed);compare();decisions();
            controller.Observer=slice=>
            {
                long observerStart=Stopwatch.GetTimestamp();try {
                if(slice.Kind=="advance")RaceSimulation.Advance(ordinary,slice.Delta);
                else if(slice.Kind=="press")
                {
                    bool accepted=RaceSimulation.Press(ordinary,0);Need(accepted==slice.Accepted,"ordinary replay Press acceptance");
                    result.Inputs.Add(new{slice.Time,slice.Distance,slice.Accepted,slice.Cursor});if(accepted)result.AcceptedInputs++;else result.RejectedInputs++;
                }
                else throw new Exception(context+": unknown actual observer slice "+slice.Kind);
                compare();decisions();facts();
                } finally { observerTicks+=Stopwatch.GetTimestamp()-observerStart; }
            };
            var readyPlayer=p;Need(RaceSimulation.Press(game,0)==RaceSimulation.Press(ordinary,0),"ordinary readiness Press");
            p=game.Players[0];Need(!ReferenceEquals(readyPlayer,p),"ready replaces the player; replay must bind the actual reset participant");compare();
            Action pause=()=>
            {
                string state=State(p),choice=JsonSerializer.Serialize(controller.Decisions,Json);double time=game.Elapsed,left=game.PartyCameraLeft;int cursor=controller.Cursor;long count=snapshots;
                controller.Advance(.17,true);controller.Advance(0);controller.Advance(-1);controller.Advance(double.NaN);controller.Advance(double.PositiveInfinity);
                Need(state==State(p)&&time==game.Elapsed&&left==game.PartyCameraLeft&&cursor==controller.Cursor&&count==snapshots&&choice==JsonSerializer.Serialize(controller.Decisions,Json),"pause/invalid delta must freeze physical state, decisions and observer");result.PauseFrozen=true;
            };
            pause();bool activePause=false;int frame=0;
            double[][] frameSizes={new[]{1d/30},new[]{1d/60},new[]{.005,.027,.011,.073,.001,.049,.016,.211}};
            while(game.Phase=="countdown"||game.Phase=="racing")
            {
                Need(frame<20000&&game.Elapsed<120,"finite physical completion guard");
                if(!activePause&&game.Phase=="racing"&&game.Elapsed>.7){pause();activePause=true;}
                double dt=frameSizes[spec.Frame][frame++%frameSizes[spec.Frame].Length];long stamp=Stopwatch.GetTimestamp(),observerBefore=observerTicks;controller.Advance(dt);long exclusiveTicks=Stopwatch.GetTimestamp()-stamp-(observerTicks-observerBefore);calls.Add(1000d*exclusiveTicks/Stopwatch.Frequency);
                compare();decisions();facts();
            }
            Need(game.Phase=="finished"&&p.Status!=PlayerStatus.Running,"solo reaches a real physical outcome");
            string terminal=State(p),terminalChoices=JsonSerializer.Serialize(controller.Decisions,Json);int terminalCursor=controller.Cursor;long terminalSnapshots=snapshots;
            controller.Advance(1);Need(State(p)==terminal&&controller.Cursor==terminalCursor&&terminalChoices==JsonSerializer.Serialize(controller.Decisions,Json),"terminal participant/controller decisions freeze");result.TerminalFrozen=true;
            Need(controller.Cursor==result.Inputs.Count,"one observed input edge per controller cursor");
            Need(double.IsFinite(controller.MaximumRequestedHorizon)&&controller.MaximumRequestedHorizon<=horizonLimit+HorizonTolerance,"published requested horizon exceeded");
            Need(double.IsFinite(controller.MaximumTrialHorizon)&&controller.MaximumTrialHorizon<=controller.MaximumRequestedHorizon+trialOverrunLimit+HorizonTolerance&&controller.MaximumTrialHorizon<=trialHorizonLimit+HorizonTolerance,"all-trial physical extent exceeded requested-plus-slice or absolute bound");
            Need(double.IsFinite(controller.MaximumTrialOvershoot)&&controller.MaximumTrialOvershoot>=0&&controller.MaximumTrialOvershoot<=trialOverrunLimit+HorizonTolerance,"pointwise forecast trial exceeded its own requested end plus55");
            Need(controller.ForecastHorizonViolations==0&&controller.ForecastObservations==controller.ForecastSteps,"every forecast Advance including discarded trials must have a pointwise extent observation and zero violations");
            if(candidatesPerPlanLimit>=0)Need(controller.CandidateSimulations<=controller.PlanningCalls*candidatesPerPlanLimit,"published candidate budget exceeded");
            if(spec.Q==1)
            {
                string key=track.Id+"/"+spec.Profile;if(!MasterTimes.TryGetValue(key,out double time))
                {
                    var baseline=Probe.Run(track,flight,Probe.Master(track,flight),PartyCamera.Lagger);Need(baseline.Finished&&baseline.Collisions==0&&baseline.Completed==baseline.Total,"test-only compatible Master reference invalid");MasterTimes.Add(key,time=baseline.Time);
                }
                result.ReferenceMasterTime=time;
                Need(p.Status==PlayerStatus.Finished&&p.Collisions==0&&positiveIds.All(p.SuccessfulRiskIds.Contains)&&p.FinishBoostsEarned==1&&p.FinishBoostElapsed>=RaceConfig.FinishBoostDuration-1e-7,"q1 must finish clean with all positive effects and full finish dash");
                Need(Math.Abs(p.TerminalTime.Value-time)<2e-7,"q1 must realize full compatible reference gain");
                Need(controller.Decisions.All(d=>d.Intent!="hit"&&d.Intent!="skip"),"q1 must not choose a hit or skip");
            }
            result.ReplayEqual=true;
        }
        catch(Exception e){result.Error=e.ToString();throw;}
        finally
        {
            result.Status=p.Status.ToString();result.Phase=game.Phase;result.Time=p.TerminalTime??game.Elapsed;result.Distance=p.Distance;result.Collisions=p.Collisions;result.Boosts=p.BoostsEarned;result.FinishBoosts=p.FinishBoostsEarned;
            result.SuccessfulIds=p.SuccessfulRiskIds.OrderBy(x=>x).ToArray();result.FailedIds=p.FailedRiskIds.OrderBy(x=>x).ToArray();result.CollectedIds=p.CollectedRiskIds.OrderBy(x=>x).ToArray();result.HitIds=p.HitObstacleIds.OrderBy(x=>x).ToArray();result.MissedPositiveIds=positiveIds.Except(p.SuccessfulRiskIds).ToArray();
            result.Cursor=controller==null?0:controller.Cursor;result.PlanningCalls=controller==null?0:controller.PlanningCalls;result.CandidateSimulations=controller==null?0:controller.CandidateSimulations;result.ForecastSteps=controller==null?0:controller.ForecastSteps;result.MaximumRequestedHorizon=controller==null?0:controller.MaximumRequestedHorizon;result.MaximumTrialHorizon=controller==null?0:controller.MaximumTrialHorizon;result.MaximumTrialOvershoot=controller==null?0:controller.MaximumTrialOvershoot;result.ForecastObservations=controller==null?0:controller.ForecastObservations;result.ForecastHorizonViolations=controller==null?0:controller.ForecastHorizonViolations;
            result.Decisions=controller==null?new List<DecisionRecord>():controller.Decisions.Select(Record).ToList();
            bool skipThenCollect=result.Decisions.Any(d=>d.Intent=="skip"&&result.Decisions.Any(later=>later.Intent=="collect"&&later.AtDistance>d.AtDistance&&result.SuccessfulIds.Any(id=>later.Id==id||later.Id=="positive:"+id)));
            bool hitRecoveryCollect=result.Facts.Any(h=>h.Kind=="hit"&&result.Facts.Any(r=>r.Kind=="recovery-complete"&&r.ObservedAtTime>=h.ObservedAtTime&&result.Facts.Any(c=>c.Kind=="positive-effect"&&c.ObservedAtTime>r.ObservedAtTime)));
            bool untouchedLinked=result.Facts.Any(h=>h.Kind=="hit"&&track.RiskZones.Any(z=>z.ObstacleId==h.Id&&h.UntouchedPositiveAfter.Contains(z.Id)&&result.Facts.Any(c=>c.Kind=="positive-effect"&&c.Id==z.Id&&c.ObservedAtTime>h.ObservedAtTime)));
            result.BranchCoverage=new{skipIntentThenLaterRealCollect=skipThenCollect,hitThenRealRecoveryThenLaterCollect=hitRecoveryCollect,untouchedLinkedRewardCollectedAfterHit=untouchedLinked,pendingObservedThenFailed=result.FailedIds.Where(pendingSeen.Contains).ToArray(),failedWithoutObservedPriorPending=result.FailedIds.Where(id=>!pendingSeen.Contains(id)).ToArray(),note="Observed set transitions identify intervals; only matching LastHit/LastSuccess/LastFinishBoost supply exact event timestamps. No exact contact distance is inferred from an endpoint."};
            result.ImmediateSnapshots=snapshots-startSnapshots;result.FieldComparisons=fieldComparisons-startFields;result.WallMilliseconds=watch.Elapsed.TotalMilliseconds;result.P95AdvanceMilliseconds=Percentile(calls,.95);result.MaximumAdvanceMilliseconds=calls.DefaultIfEmpty(0).Max();
        }
        Say("CASE "+spec.Key+" "+result.Status+" time="+result.Time.ToString("F6",CultureInfo.InvariantCulture)+" hits="+result.Collisions+" boosts="+result.Boosts+" decisions="+result.Decisions.Count+" forecast="+result.ForecastSteps);return result;
    }
    static void CompareCadence(CaseResult a,CaseResult b)
    {
        context="cadence "+a.Case.Key+" vs "+b.Case.Key;
        Need(a.Status==b.Status&&a.SuccessfulIds.SequenceEqual(b.SuccessfulIds)&&a.HitIds.SequenceEqual(b.HitIds)&&a.FailedIds.SequenceEqual(b.FailedIds),"same seed render cadence changed real outcomes");
        Need(Math.Abs(a.Time-b.Time)<2e-7&&Math.Abs(a.Distance-b.Distance)<5e-5,"same seed cadence physical tolerance");
        Need(a.Decisions.Count==b.Decisions.Count,"same seed cadence changed number of logical choices");
        for(int i=0;i<a.Decisions.Count;i++)
        {
            var x=a.Decisions[i];var y=b.Decisions[i];Need(x.Id==y.Id&&x.Kind==y.Kind&&x.Intent==y.Intent&&x.Probability==y.Probability&&x.Roll==y.Roll,"same seed cadence changed intent/roll");
            Need(Math.Abs(x.AtDistance-y.AtDistance)<5e-5&&Math.Abs(x.AtTime-y.AtTime)<2e-7&&x.SelectedInputs.Length==y.SelectedInputs.Length,"same seed cadence changed decision state or selected input count");
            for(int j=0;j<x.SelectedInputs.Length;j++)Need(Math.Abs(x.SelectedInputs[j]-y.SelectedInputs[j])<5e-5,"same seed cadence changed selected input timing");
        }
    }
    static List<CaseSpec> Schedule(bool smoke)
    {
        var result=new List<CaseSpec>();foreach(string track in new[]{TracksRhythm.CascadeId,TracksMixed.Id})foreach(string profile in new[]{"Chosen","FastFlip"})foreach(double q in new[]{.5,.75,1d})foreach(uint seed in smoke?new[]{0u}:Enumerable.Range(0,12).Select(i=>(uint)i))foreach(string camera in q==1?new[]{"Lagger","Constant"}:new[]{"Lagger"})foreach(int frame in smoke?new[]{0}:new[]{0,1,2})result.Add(new CaseSpec{Track=track,Profile=profile,Q=q,Seed=seed,Camera=camera,Frame=frame});return result;
    }
    static void Save(string status,List<CaseSpec> scheduled,Dictionary<string,string> sources,bool smoke,Exception failure)
    {
        bool stable=sources.All(x=>File.Exists(x.Key)&&HashFile(x.Key)==x.Value);if(!stable&&status=="PASS")throw new Exception("Source freeze changed before report save");
        var ran=new HashSet<string>(Results.Select(r=>r.Case.Key));var unrun=scheduled.Where(s=>!ran.Contains(s.Key)).Select(s=>s.Key).ToArray();
        var variation=Results.GroupBy(r=>r.Case.Track+"/"+r.Case.Profile+"/"+r.Case.Q.ToString("R",CultureInfo.InvariantCulture)+"/"+r.Case.Camera+"/"+r.Case.Frame).Select(g=>new{key=g.Key,announcedSeeds=ReproSeed4?new uint[]{4}:smoke?new uint[]{0}:Enumerable.Range(0,12).Select(i=>(uint)i).ToArray(),ranSeeds=g.Select(r=>r.Case.Seed).ToArray(),intentSignatures=g.Select(r=>string.Join("|",r.Decisions.Select(d=>d.Id+":"+d.Intent))).Distinct().Count(),inputSignatures=g.Select(r=>string.Join("|",r.Inputs.Select(x=>JsonSerializer.Serialize(x,Json)))).Distinct().Count(),physicalEventSets=g.Select(r=>string.Join(",",r.SuccessfulIds)+"/"+string.Join(",",r.HitIds)).Distinct().Count(),terminalOutcomes=g.GroupBy(r=>r.Status).Select(s=>new{status=s.Key,count=s.Count()}),meanTime=g.Average(r=>r.Time),minimumTime=g.Min(r=>r.Time),maximumTime=g.Max(r=>r.Time)}).ToArray();
        var report=new{status,smoke,reproCadenceSeed4=ReproSeed4,scope="BSTO00/03/04 risk prototype. Actual solo Lagger and q1 Constant; every emitted actual Press/Advance immediately replayed on ordinary solo. Complete public player fields/queues/sets and world time/phase/camera compared. No UI/browser/performance acceptance claim, no old per-run goal bands, no seed filtering. Test-only Probe.Master establishes the independent compatible reference.",announcedSeeds=ReproSeed4?new uint[]{4}:smoke?new uint[]{0}:Enumerable.Range(0,12).Select(i=>(uint)i).ToArray(),scheduledCases=scheduled.Count,ranCases=Results.Count,unrunCases=unrun,failedCase=failure==null?null:context,error=failure==null?null:failure.ToString(),advanceCostScope="Managed controller Advance includes normal world and planning; excludes test observer replay/audits. Not an isolated planner or native WebGL benchmark.",maximumReplayNumericError=Results.All(r=>r.ReplayEqual)?(double?)0:null,snapshots,fieldComparisons,requestedHorizonLimit=horizonLimit,actualTrialHorizonLimit=trialHorizonLimit,pointwiseTrialOverrunLimit=trialOverrunLimit,horizonTolerance=HorizonTolerance,horizonMeasurementScope="Requested and actual trial extents are distinct. MaximumTrialOvershoot is measured against that trial own requested end after every forecast Advance, including discarded trials; observation count must equal ForecastSteps and violations must be zero. Legacy requested-only MaximumObservedHorizon is not exported as actual.",candidatesPerPlanLimit,sourceHashes=sources,sourceStable=stable,results=Results,variation};
        File.WriteAllText(OutputStem+".json",JsonSerializer.Serialize(report,Json));File.WriteAllLines(OutputStem+".txt",Lines.Concat(new[]{"STATUS "+status+" cases="+Results.Count+"/"+scheduled.Count+" snapshots="+snapshots+" sourceStable="+stable}));
    }
    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;bool smoke=args.Contains("--smoke");ReproSeed4=args.Contains("--repro-cadence-seed4");if(smoke&&ReproSeed4)throw new ArgumentException("Choose smoke or targeted repro, not both");
        if(ReproSeed4)OutputStem="Verification/stochastic-bots-risk-repro-seed4";
        foreach(string arg in args){if(arg.StartsWith("--max-horizon="))horizonLimit=double.Parse(arg.Substring(14),CultureInfo.InvariantCulture);if(arg.StartsWith("--max-trial-horizon="))trialHorizonLimit=double.Parse(arg.Substring(20),CultureInfo.InvariantCulture);if(arg.StartsWith("--max-trial-overrun="))trialOverrunLimit=double.Parse(arg.Substring(20),CultureInfo.InvariantCulture);if(arg.StartsWith("--max-candidates="))candidatesPerPlanLimit=long.Parse(arg.Substring(17),CultureInfo.InvariantCulture);}
        // Each new verification run must preserve previously accepted evidence.
        var outputArguments=args.Where(arg=>arg.StartsWith("--output-stem=",StringComparison.Ordinal)).ToArray();
        if(outputArguments.Length>1)throw new ArgumentException("Choose one output stem");
        if(outputArguments.Length==1)
        {
            string requested=outputArguments[0].Substring("--output-stem=".Length);
            if(!System.Text.RegularExpressions.Regex.IsMatch(requested,@"\AVerification/stochastic-bots-risk-[a-z0-9-]+\z"))throw new ArgumentException("Output must be a named stochastic risk proof within Verification");
            OutputStem=requested;
        }
        if(File.Exists(OutputStem+".json")||File.Exists(OutputStem+".txt"))throw new IOException("Preserve existing proof; choose a new --output-stem=Verification/stochastic-bots-risk-name");
        Need(double.IsFinite(horizonLimit)&&horizonLimit>0&&horizonLimit<=1000,"requested CLI limit must stay within approved1000");
        Need(double.IsFinite(trialHorizonLimit)&&trialHorizonLimit>0&&trialHorizonLimit<=1055,"actual trial CLI limit must stay within approved1055");
        Need(double.IsFinite(trialOverrunLimit)&&trialOverrunLimit>=0&&trialOverrunLimit<=55,"pointwise overrun CLI limit must stay within approved55");
        var scheduled=ReproSeed4?new[]{0,1,2}.Select(frame=>new CaseSpec{Track=TracksRhythm.CascadeId,Profile="Chosen",Q=.5,Seed=4,Camera="Lagger",Frame=frame}).ToList():Schedule(smoke);var sources=Directory.GetFiles("Assets/Scripts/Core","*.cs").Concat(new[]{"Tools/StochasticRisk/Program.cs","Tools/BotPrototype/SharedProbe.cs"}).ToDictionary(x=>x.Replace((char)92,(char)47),HashFile);Exception failure=null;
        try
        {
            foreach(var spec in scheduled){var actual=Run(spec);if(spec.Frame>0){var first=Results.First(r=>r.Case.Track==spec.Track&&r.Case.Profile==spec.Profile&&r.Case.Q==spec.Q&&r.Case.Seed==spec.Seed&&r.Case.Camera==spec.Camera&&r.Case.Frame==0);CompareCadence(first,actual);}}
            Need(Results.Count==scheduled.Count,"all announced cases retained");Need(sources.All(x=>HashFile(x.Key)==x.Value),"source freeze changed during prototype");Save("PASS",scheduled,sources,smoke,null);return 0;
        }
        catch(Exception e){failure=e;Say("FAIL "+context+": "+e);Save("FAIL",scheduled,sources,smoke,e);return 1;}
    }
}