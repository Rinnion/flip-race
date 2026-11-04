using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace FlipRace.Core
{
    public sealed class StochasticBotDecision
    {
        readonly double[] inputs;
        public string Id { get; }
        public BotOpportunityKind Kind { get; }
        public string Intent { get; }
        public double Probability { get; }
        public double Roll { get; }
        public double AtDistance { get; }
        public double AtTime { get; }
        public double HorizonEndDistance { get; }
        public string Outcome { get; internal set; }
        public string UnreachableReason { get; internal set; }
        public double[] SelectedInputs { get { return (double[])inputs.Clone(); } }
        internal readonly BotOpportunity Opportunity;
        internal StochasticBotDecision(BotOpportunity opportunity,string intent,double probability,double roll,double distance,double time,double horizon,double[] selected,string reason)
        {Opportunity=opportunity;Id=opportunity.Id;Kind=opportunity.Kind;Intent=intent;Probability=probability;Roll=roll;AtDistance=distance;AtTime=time;HorizonEndDistance=horizon;inputs=(double[])selected.Clone();Outcome="pending";UnreachableReason=reason;}
    }
    // Wall-clock observations never participate in scoring, budgets, or random choices.
    public sealed class BotPlanningSample
    {
        public int Number { get; }
        public double AtDistance { get; }
        public double HorizonEndDistance { get; }
        public int Candidates { get; }
        public int ForecastSteps { get; }
        public int Rollbacks { get; }
        public double Milliseconds { get; }
        internal BotPlanningSample(int number,double at,double end,int candidates,int steps,int rollbacks,double milliseconds)
        {Number=number;AtDistance=at;HorizonEndDistance=end;Candidates=candidates;ForecastSteps=steps;Rollbacks=rollbacks;Milliseconds=milliseconds;}
    }
    // Bounded personal forecasts; only ordinary Press/Advance touches the shared race.
    public sealed class StochasticBotController
    {
        public const int MaxCandidatesPerPlanningCall=96;
        public const double MaxHorizonDistance=1000;
        public const int MaxInputs=12;
        public const int MaxMasterTimingCandidates=13;
        public const double MaxForecastSlice=.125;
        public const double MaxTrialOverrunDistance=MaxForecastSlice*RaceConfig.FinishBoostSpeed;
        public const double MaxTrialHorizonDistance=MaxHorizonDistance+MaxTrialOverrunDistance;
        public const double DecisionLeadDistance=380;
        public const double MaxSlice=1d/120;
        public const int MaxForecastStepsPerCandidate=640;
        const double EdgeTolerance=2e-7;
        readonly GameState game;readonly int slot;readonly uint seed;readonly double q,collectProbability,hitProbability;readonly int ordinaryGoalCount;
        readonly BotOpportunity[] opportunities;readonly double[] reference;
        readonly List<StochasticBotDecision> decisions=new List<StochasticBotDecision>();
        readonly ReadOnlyCollection<StochasticBotDecision> readOnlyDecisions;
        readonly Dictionary<string,StochasticBotDecision> saved=new Dictionary<string,StochasticBotDecision>(StringComparer.Ordinal);
        double[] planned=Array.Empty<double>();int plannedCursor,nextOpportunity,cursor;bool wasRecovering;
        double lastEmittedDistance=double.NegativeInfinity;
        public int Cursor { get { return cursor; } }
        public int SuppressedInputs { get; private set; }
        public IReadOnlyList<StochasticBotDecision> Decisions { get { return readOnlyDecisions; } }
        public int PlanningCalls { get; private set; }
        public int CandidateSimulations { get; private set; }
        public int ForecastSteps { get; private set; }
        public int ForecastTrialRollbacks { get; private set; }
        public int ForecastCheckpoints { get; private set; }
        readonly List<BotPlanningSample> planningSamples=new List<BotPlanningSample>();
        readonly ReadOnlyCollection<BotPlanningSample> readOnlyPlanningSamples;
        public IReadOnlyList<BotPlanningSample> PlanningSamples { get { return readOnlyPlanningSamples; } }
        public double MaximumRequestedHorizon { get; private set; }
        public double MaximumTrialHorizon { get; private set; }
        public double MaximumTrialOvershoot { get; private set; }
        public int ForecastObservations { get; private set; }
        public int ForecastHorizonViolations { get; private set; }
        // Compatibility alias: this has always represented the REQUESTED endpoint.
        public double MaximumObservedHorizon { get { return MaximumRequestedHorizon; } }
        public string LastPlanningFailure { get; private set; } = "";
        public StochasticBotSettings Settings { get; }
        public Action<BotSlice> Observer;
        public StochasticBotController(GameState game,int slot,double q,uint seed)
        {
            if(game==null)throw new ArgumentNullException(nameof(game));
            if(slot<0||slot>=game.Players.Length)throw new ArgumentOutOfRangeException(nameof(slot));
            // Raw initial-model training must not override immutable race metadata.
            if(game.StochasticBot!=null)throw new ArgumentException("Use the immutable stochastic race settings constructor.",nameof(game));
            if(game.BotPlan!=null)throw new ArgumentException("A legacy tape race cannot use a stochastic controller.",nameof(game));
            if(double.IsNaN(q)||double.IsInfinity(q)||q<.5||q>1)throw new ArgumentOutOfRangeException(nameof(q));
            var map=StochasticTrackMap.Create(game.Track,game.Flight);double probability=q==1?0:map.BonusGain/map.CollisionCostSum*(1-q);
            if(double.IsNaN(probability)||double.IsInfinity(probability)||probability<0||probability>1)throw new NotSupportedException("Initial hit probability outside [0,1]; no clamping.");
            this.game=game;this.slot=slot;this.q=q;collectProbability=q;this.seed=seed;hitProbability=probability;opportunities=map.Opportunities;foreach(var item in opportunities)if(item.Kind==BotOpportunityKind.Positive)ordinaryGoalCount++;reference=map.ReferenceInputs;readOnlyDecisions=decisions.AsReadOnly();readOnlyPlanningSamples=planningSamples.AsReadOnly();
        }
        static StochasticBotSettings RaceSettings(GameState game)
        {
            if(game==null)throw new ArgumentNullException(nameof(game));
            if(game.StochasticBot==null)throw new ArgumentException("This race has no stochastic bot settings.",nameof(game));
            return game.StochasticBot;
        }
        public StochasticBotController(GameState game,int slot=1):this(game,slot,RaceSettings(game)) { }
        // Explicit immutable settings also support an ordinary SOLO/PARTY training race.
        public StochasticBotController(GameState game,int slot,StochasticBotSettings settings)
        {
            if(game==null)throw new ArgumentNullException(nameof(game));
            if(slot<0||slot>=game.Players.Length)throw new ArgumentOutOfRangeException(nameof(slot));
            if(settings==null)throw new ArgumentNullException(nameof(settings));
            var map=settings.RequireCompatible(game.Track,game.Flight);
            if(game.StochasticBot!=null&&!game.StochasticBot.SameAs(settings))throw new ArgumentException("Controller settings differ from immutable race metadata.",nameof(settings));
            if(game.BotPlan!=null)throw new ArgumentException("A legacy tape race cannot use a stochastic controller.",nameof(game));
            this.game=game;this.slot=slot;this.q=settings.Quality;collectProbability=settings.CollectProbability;hitProbability=settings.HitProbability;this.seed=settings.Seed;Settings=settings;
            opportunities=map.Opportunities;foreach(var item in opportunities)if(item.Kind==BotOpportunityKind.Positive)ordinaryGoalCount++;reference=map.ReferenceInputs;readOnlyDecisions=decisions.AsReadOnly();readOnlyPlanningSamples=planningSamples.AsReadOnly();
        }
        static uint Mix(uint value){unchecked{value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;return value^(value>>16);}}
        uint Hash(string id,uint salt){unchecked{uint value=2166136261u^seed^salt;foreach(char c in id){value^=c;value*=16777619u;}return Mix(value);}}
        double Roll(string id){return(Hash(id,0x9e3779b9u)+.5)/4294967296d;}
        string RawId(BotOpportunity item){return item.Id.Substring(item.Id.IndexOf(':')+1);}
        bool Hit(PlayerState p,BotOpportunity item){return p.HitObstacleIds.Contains(item.ObstacleId);}
        bool Collected(PlayerState p,BotOpportunity item){return item.Kind==BotOpportunityKind.Finish?p.FinishBoostsEarned>0:p.SuccessfulRiskIds.Contains(RawId(item));}
        bool IsFinal(StochasticBotDecision d){return d.Outcome!="pending";}
        void UpdateOutcomes()
        {
            var p=game.Players[slot];foreach(var d in decisions)
            {
                if(IsFinal(d))continue;var item=d.Opportunity;string id=RawId(item);
                if(item.Kind==BotOpportunityKind.Obstacle){if(Hit(p,item))d.Outcome="hit";else if(p.Distance>item.EndPosition+EdgeTolerance||p.Status==PlayerStatus.Finished)d.Outcome="avoided";}
                else if(Collected(p,item))d.Outcome="collected";else if(p.FailedRiskIds.Contains(id))d.Outcome="failed";else if(p.Distance>item.EndPosition+EdgeTolerance||p.Status==PlayerStatus.Finished)d.Outcome="missed";
                if(d.Outcome=="pending"&&p.Status==PlayerStatus.Eliminated)d.Outcome="eliminated";
                if(IsFinal(d))
                {
                    bool met=d.Intent=="hit"?d.Outcome=="hit":d.Intent=="avoid"?d.Outcome=="avoided":d.Intent=="collect"?d.Outcome=="collected":d.Outcome=="missed"||d.Outcome=="failed";
                    if(met)d.UnreachableReason="";
                    else if(d.Outcome=="eliminated")d.UnreachableReason="actual-terminal: eliminated before this opportunity resolved";
                    else if(d.UnreachableReason.Length==0)d.UnreachableReason="actual-outcome: saved intention was not physically realized after local forecasting/replanning";
                }
            }
        }
        GameState PersonalForecast()
        {
            var copy=RaceSnapshot.Clone(game);
            bool sharedHidden=HiddenObstacles.HasHidden(game.Track);
            // Constant-camera elimination remains excluded inside forecasts.
            // Hidden geometry needs every current body and the shared clocks.
            // Other participants continue ordinary physics without future Press
            // inputs; unknown human inputs remain forecast uncertainty.
            var personal=sharedHidden&&copy.Players.Length>1
                ? RaceSimulation.CreateParty(game.Track,game.Flight,copy.Players.Length,PartyCamera.Lagger,-288.8,1520)
                : RaceSimulation.CreateSolo(game.Track,game.Flight,PartyCamera.Lagger,-288.8,1520);
            if(sharedHidden)
            {
                personal.Players=copy.Players;
                var bot=personal.Players[slot];personal.Players[slot]=personal.Players[0];personal.Players[0]=bot;
                personal.HiddenObstacles=copy.HiddenObstacles;personal.HiddenWarningDuration=copy.HiddenWarningDuration;
                personal.Winner=copy.Winner;personal.WinnerAt=copy.WinnerAt;personal.PartyLastCameraLeft=copy.PartyLastCameraLeft;
            }
            else personal.Players=new[]{copy.Players[slot]};
            personal.Elapsed=game.Elapsed;personal.Phase="racing";personal.Motion=game.Motion;personal.Profile=game.Profile;personal.CountdownRemaining=0;return personal;
        }
        double[] Clip(IEnumerable<double> source,double start,double end)
        {
            var list=new List<double>();foreach(double v in source)if(!double.IsNaN(v)&&!double.IsInfinity(v)&&v>=start-EdgeTolerance&&v<=end&&v>lastEmittedDistance+EdgeTolerance)list.Add(Math.Max(start,v));
            list.Sort();for(int i=list.Count-1;i>0;i--)if(list[i]-list[i-1]<1e-6)list.RemoveAt(i);if(list.Count>MaxInputs)list.RemoveRange(MaxInputs,list.Count-MaxInputs);return list.ToArray();
        }
        // Canonical identity only: original validated input doubles and all physical
        // player state remain untouched. Source-relative generic anchors use this grid.
        string CandidateKey(double[] values){return string.Join("|",Array.ConvertAll(values,v=>v.ToString("F5",System.Globalization.CultureInfo.InvariantCulture)));}
        sealed class Evaluation{internal double[] Inputs;internal int Score,IntentMismatches,Rejected;internal bool Completed,MasterClean;internal string[] UnmetIds;}
        sealed class Pending{internal BotOpportunity Opportunity;internal string Intent;internal double Probability,Roll;}
        void ForecastAdvance(GameState forecast,double delta,double origin,double end)
        {
            RaceSimulation.Advance(forecast,delta);ForecastSteps++;ForecastObservations++;
            // Observe BEFORE rollback: discarded contacts are excluded from scoring,
            // but their actual transient distance still counts toward the bound.
            double distance=forecast.Players[0].Distance;
            double extent=Math.Max(0,distance-origin),overrun=Math.Max(0,distance-end);
            MaximumTrialHorizon=Math.Max(MaximumTrialHorizon,extent);
            MaximumTrialOvershoot=Math.Max(MaximumTrialOvershoot,overrun);
            if(delta>MaxForecastSlice+1e-12||extent>MaxTrialHorizonDistance+EdgeTolerance||overrun>MaxTrialOverrunDistance+EdgeTolerance)
            {ForecastHorizonViolations++;throw new InvalidOperationException("Forecast transient horizon bound exceeded");}
        }
        Evaluation Evaluate(GameState origin,double[] inputs,double end,List<Pending> pending)
        {
            CandidateSimulations++;var g=RaceSnapshot.Clone(origin);var p=g.Players[0];int edge=0,rejected=0,steps=0;
            while(p.Status==PlayerStatus.Running&&p.Distance<end-EdgeTolerance&&steps<MaxForecastStepsPerCandidate)
            {
                if(p.Recovering<=0&&edge<inputs.Length&&inputs[edge]-p.Distance<=EdgeTolerance){if(!RaceSimulation.Press(g,0))rejected++;edge++;continue;}
                if(p.Recovering>0)while(edge<inputs.Length&&inputs[edge]<p.Distance-EdgeTolerance)edge++;
                double target=end;if(edge<inputs.Length&&p.Recovering<=0)target=Math.Min(target,inputs[edge]);
                if(p.Recovering>0)
                {
                    ForecastAdvance(g,Math.Min(MaxForecastSlice,p.Recovering),origin.Players[0].Distance,end);steps++;
                    continue;
                }
                // The trial may accelerate inside its continuous swept timeline.
                // Keep a deep checkpoint: any target overshoot discards ALL trial
                // clocks, effects and contacts before retrying the old safe step.
                var checkpoint=RaceSnapshot.Clone(g);ForecastCheckpoints++;
                double gap=target-p.Distance;
                double dt=Math.Min(MaxForecastSlice,Math.Max(1e-10,gap/p.CurrentSpeed));
                ForecastAdvance(g,dt,origin.Players[0].Distance,end);steps++;
                if(p.Distance>target+EdgeTolerance)
                {
                    g=checkpoint;p=g.Players[0];ForecastTrialRollbacks++;
                    if(steps>=MaxForecastStepsPerCandidate)break;
                    double safe=Math.Min(MaxForecastSlice,Math.Max(1e-10,(target-p.Distance)/RaceConfig.FinishBoostSpeed));
                    ForecastAdvance(g,safe,origin.Players[0].Distance,end);steps++;
                }
            }
            bool completed=p.Distance>=end-EdgeTolerance||p.Status==PlayerStatus.Finished;int mismatches=0,unintended=0;var unmet=new List<string>();
            foreach(var request in pending)
            {
                var item=request.Opportunity;bool resolved=item.EndPosition<=end+EdgeTolerance||p.Status==PlayerStatus.Finished;
                bool mismatch;
                if(item.Kind==BotOpportunityKind.Obstacle){bool hit=Hit(p,item);mismatch=request.Intent=="avoid"?hit:resolved&&!hit;}
                else{bool collect=Collected(p,item);mismatch=request.Intent=="skip"?collect:(resolved&&!collect)||(q==1&&p.FailedRiskIds.Contains(RawId(item)));}
                if(mismatch){mismatches++;unmet.Add(item.Id);}
            }
            foreach(var item in opportunities)
            {
                if(item.Kind!=BotOpportunityKind.Obstacle||item.Position<origin.Players[0].Distance||item.Position>end||!Hit(p,item)||Hit(origin.Players[0],item))continue;
                bool desired=false;foreach(var r in pending)if(r.Opportunity.Id==item.Id&&r.Intent=="hit"){desired=true;break;}if(!desired)unintended++;
            }
            bool selectedFailed=false,selectedCollected=true;
            foreach(var item in opportunities)if(item.Kind!=BotOpportunityKind.Obstacle)
            { string id=RawId(item);selectedFailed|=p.FailedRiskIds.Contains(id);selectedCollected&=p.SuccessfulRiskIds.Contains(id); }
            // Alternatives stay physical. Their success/failure or extra pulses
            // do not invalidate the selected compatible reference. Pulse service
            // is an aggregate guard; the engine does not expose per-ID service
            // for merged instant effects.
            bool masterClean=q!=1||(p.Collisions==0&&p.SlowsEarned==0&&!selectedFailed
                &&(end<game.Track.Length-EdgeTolerance||(p.Status==PlayerStatus.Finished&&selectedCollected
                    &&p.CompletedBoosts>=ordinaryGoalCount&&p.CompletedSlows==0&&p.FinishBoostsEarned==1
                    &&p.FinishBoostElapsed>=RaceConfig.FinishBoostDuration-1e-8)));
            int score=mismatches*10000+unintended*1000+rejected*100+(p.SlowsEarned-origin.Players[0].SlowsEarned)*10+(completed?0:1000000)+(masterClean?0:100000);
            return new Evaluation{Inputs=inputs,Score=score,IntentMismatches=mismatches,Rejected=rejected,Completed=completed,MasterClean=masterClean,UnmetIds=unmet.ToArray()};
        }
        void Plan(List<Pending> fresh)
        {
            long planStarted=System.Diagnostics.Stopwatch.GetTimestamp();int initialCandidates=CandidateSimulations,initialSteps=ForecastSteps,initialRollbacks=ForecastTrialRollbacks;
            var player=game.Players[slot];double start=player.Distance,end=Math.Min(game.Track.Length,start+MaxHorizonDistance);MaximumRequestedHorizon=Math.Max(MaximumRequestedHorizon,end-start);PlanningCalls++;
            var pending=new List<Pending>();foreach(var d in decisions)if(!IsFinal(d)&&d.Opportunity.EndPosition>=start-EdgeTolerance)pending.Add(new Pending{Opportunity=d.Opportunity,Intent=d.Intent,Probability=d.Probability,Roll=d.Roll});pending.AddRange(fresh);
            // q1 future intentions are already certain. These local constraints do
            // not create decisions or draw extra rolls before their spatial lead.
            if(q==1)foreach(var item in opportunities)
            {
                if(item.Position>end||item.EndPosition<start-EdgeTolerance)continue;
                bool exists=false;foreach(var request in pending)if(request.Opportunity.Id==item.Id){exists=true;break;}
                if(!exists)pending.Add(new Pending{Opportunity=item,Intent=item.Kind==BotOpportunityKind.Obstacle?"avoid":"collect",Probability=1});
            }
            var origin=PersonalForecast();var candidates=new HashSet<string>(StringComparer.Ordinal);var ties=new List<Evaluation>();Evaluation best=null;int calls=0,candidateLimit=q==1?MaxMasterTimingCandidates:MaxCandidatesPerPlanningCall;
            Evaluation Try(IEnumerable<double> values)
            {
                if(calls>=candidateLimit)return best;var input=Clip(values,start,end);string key=CandidateKey(input);if(!candidates.Add(key))return best;
                calls++;var result=Evaluate(origin,input,end,pending);if(best==null||result.Score<best.Score){best=result;ties.Clear();ties.Add(result);}else if(result.Score==best.Score)ties.Add(result);return result;
            }
            // A spatial decision may coincide with an unexecuted reference edge.
            // lastEmittedDistance excludes consumed edges; a start offset can lose a pending one.
            var baseline=Clip(reference,start,end);Try(baseline);
            if(q==1&&baseline.Length>0)
            {
                double nextLead=nextOpportunity<opportunities.Length?Math.Max(0,opportunities[nextOpportunity].Position-DecisionLeadDistance):game.Track.Length+1;
                // Original AND shifted edges must precede the next decision: a negative
                // retime must not resurrect its old reference position on replanning.
                // Both changed edges of a pair must execute before another decision
                // can replace the future plan. Original physical state is untouched.
                foreach(double shift in new[]{-2d,2,-6,6,-12,12})
                {
                    if(baseline[0]<nextLead-EdgeTolerance&&baseline[0]+shift>start+EdgeTolerance&&baseline[0]+shift<nextLead-EdgeTolerance)
                    {var single=(double[])baseline.Clone();single[0]+=shift;Try(single);}
                    if(baseline.Length>1&&baseline[1]<nextLead-EdgeTolerance&&baseline[0]+shift>start+EdgeTolerance&&baseline[1]+shift<nextLead-EdgeTolerance)
                    {var pair=(double[])baseline.Clone();pair[0]+=shift;pair[1]+=shift;Try(pair);}
                }
            }
            if(q!=1)
            {
                var remaining=new List<double>();for(int i=plannedCursor;i<planned.Length;i++)remaining.Add(planned[i]);Try(remaining);Try(Array.Empty<double>());double[] shifts={-80,80,-32,32,-12,12,-160,160};
                for(int pass=0;pass<2&&calls<MaxCandidatesPerPlanningCall;pass++)
                {
                    double[] basis=(double[])best.Inputs.Clone();for(int i=0;i<basis.Length&&calls<MaxCandidatesPerPlanningCall;i++)
                    {
                        var remove=new List<double>(basis);remove.RemoveAt(i);Try(remove);
                        foreach(double shift in shifts){var single=(double[])basis.Clone();single[i]+=shift;Try(single);if(i+1<basis.Length){var pair=(double[])basis.Clone();pair[i]+=shift;pair[i+1]+=shift;Try(pair);}if(calls>=MaxCandidatesPerPlanningCall)break;}
                    }
                    foreach(double offset in new[]{0d,24,60,120,200,300}){var add=new List<double>(best.Inputs){Math.Round(start,5)+offset};Try(add);var pair=new List<double>(best.Inputs){Math.Round(start,5)+offset,Math.Round(start,5)+offset+45};Try(pair);}
                }
            }
            string choiceId=fresh.Count>0?fresh[0].Opportunity.Id:pending.Count>0?pending[0].Opportunity.Id:"recovery";
            // Rank each validated combination independently. A borderline candidate
            // entering/leaving the pool must not remap all other timing choices.
            Evaluation selected=ties[0];string selectedKey=CandidateKey(selected.Inputs);uint selectedRank=Hash(choiceId+"|"+selectedKey,0x85ebca6bu);
            for(int i=1;i<ties.Count;i++){string key=CandidateKey(ties[i].Inputs);uint rank=Hash(choiceId+"|"+key,0x85ebca6bu);if(rank<selectedRank||(rank==selectedRank&&string.CompareOrdinal(key,selectedKey)<0)){selected=ties[i];selectedKey=key;selectedRank=rank;}}
            planned=(double[])selected.Inputs.Clone();plannedCursor=0;
            LastPlanningFailure=!selected.Completed?"bounded-local-search: forecast step cap":!selected.MasterClean?"master-local-forecast: hit, slow, failed reward, or incomplete terminal pulse ledger":selected.IntentMismatches>0?"bounded-local-search: selected forecast unmet IDs: "+string.Join(",",selected.UnmetIds):"";
            string Reason(string id){return Array.IndexOf(selected.UnmetIds,id)>=0?"bounded-local-search: selected forecast did not realize this saved intention; no better tested local candidate":"";}
            foreach(var d in decisions)if(!IsFinal(d))d.UnreachableReason=Reason(d.Id);
            foreach(var r in fresh){var d=new StochasticBotDecision(r.Opportunity,r.Intent,r.Probability,r.Roll,start,game.Elapsed,end,planned,Reason(r.Opportunity.Id));saved.Add(d.Id,d);decisions.Add(d);}
            planningSamples.Add(new BotPlanningSample(PlanningCalls,start,end,CandidateSimulations-initialCandidates,ForecastSteps-initialSteps,ForecastTrialRollbacks-initialRollbacks,1000d*(System.Diagnostics.Stopwatch.GetTimestamp()-planStarted)/System.Diagnostics.Stopwatch.Frequency));
        }
        bool Decide()
        {
            var p=game.Players[slot];var fresh=new List<Pending>();while(nextOpportunity<opportunities.Length&&Math.Max(0,opportunities[nextOpportunity].Position-DecisionLeadDistance)-p.Distance<=EdgeTolerance)
            {
                var item=opportunities[nextOpportunity++];if(saved.ContainsKey(item.Id))continue;double probability=item.Kind==BotOpportunityKind.Obstacle?hitProbability:collectProbability,roll=Roll(item.Id);string intent=item.Kind==BotOpportunityKind.Obstacle?(roll<probability?"hit":"avoid"):(roll<probability?"collect":"skip");fresh.Add(new Pending{Opportunity=item,Intent=intent,Probability=probability,Roll=roll});
            }
            if(fresh.Count==0)return false;
            if(p.Recovering>0){double end=Math.Min(game.Track.Length,p.Distance+MaxHorizonDistance);foreach(var r in fresh){var d=new StochasticBotDecision(r.Opportunity,r.Intent,r.Probability,r.Roll,p.Distance,game.Elapsed,end,Array.Empty<double>(),"");saved.Add(d.Id,d);decisions.Add(d);}}
            else Plan(fresh);return true;
        }
        void Step(double dt){RaceSimulation.Advance(game,dt);UpdateOutcomes();Observer?.Invoke(new BotSlice{Kind="advance",Delta=dt,Time=game.Elapsed,Distance=game.Players[slot].Distance,Cursor=cursor});}
        public void Advance(double delta,bool paused=false)
        {
            if(paused||double.IsNaN(delta)||double.IsInfinity(delta)||delta<=0)return;double remaining=delta;int guard=0;
            while(remaining>0)
            {
                if(++guard>100000)throw new InvalidOperationException("Stochastic controller step guard");
                if(game.Phase=="countdown"){double part=Math.Min(remaining,Math.Max(1e-9,game.CountdownRemaining));Step(part);remaining-=part;continue;}
                if(game.Phase!="racing"){Step(remaining);break;}var p=game.Players[slot];
                if(p.Status==PlayerStatus.Running)
                {
                    UpdateOutcomes();Decide();if(p.Recovering>0)wasRecovering=true;else if(wasRecovering){wasRecovering=false;planned=Array.Empty<double>();plannedCursor=0;Plan(new List<Pending>());}
                    if(p.Recovering<=0&&plannedCursor<planned.Length&&planned[plannedCursor]-p.Distance<=EdgeTolerance){bool legal=RaceSimulation.CanPress(game,slot),accepted=false;if(legal)accepted=RaceSimulation.Press(game,slot);else SuppressedInputs++;lastEmittedDistance=p.Distance;Observer?.Invoke(new BotSlice{Kind=legal?"press":"suppressed",Time=game.Elapsed,Distance=p.Distance,Cursor=cursor,Accepted=accepted});cursor++;plannedCursor++;continue;}
                }
                double dt=Math.Min(remaining,MaxSlice);
                if(p.Status==PlayerStatus.Running){if(p.Recovering>0)dt=Math.Min(dt,p.Recovering);else{double target=double.PositiveInfinity;if(plannedCursor<planned.Length)target=planned[plannedCursor];if(nextOpportunity<opportunities.Length)target=Math.Min(target,Math.Max(0,opportunities[nextOpportunity].Position-DecisionLeadDistance));if(!double.IsInfinity(target))dt=Math.Min(dt,Math.Max(1e-10,(target-p.Distance)/RaceConfig.FinishBoostSpeed));}}
                Step(dt);remaining-=dt;
            }
        }
    }
}
