using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlipRace.Core;
static class IntentWitnessCheck
{
 internal sealed class Witness
 {
  public string Track{get;set;}public string Profile{get;set;}public string Difficulty{get;set;}public uint Seed{get;set;}public string Id{get;set;}public string InitialIntent{get;set;}public string InitialOutcome{get;set;}
 }
 static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
 static readonly JsonSerializerOptions Json=new JsonSerializerOptions{WriteIndented=true,IncludeFields=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,PropertyNameCaseInsensitive=true};
 static JsonElement Player(PlayerState p){return JsonSerializer.SerializeToElement(p,Json);}
 static object Field(object value,string name){return value.GetType().GetField(name,Private).GetValue(value);}
 static GameState Personal(GameState source)
 {
  var copy=RaceSnapshot.Clone(source);var g=RaceSimulation.CreateSolo(source.Track,source.Flight,PartyCamera.Lagger,-288.8,1520);g.Players=new[]{copy.Players[1]};g.Elapsed=source.Elapsed;g.Phase="racing";g.Motion=source.Motion;g.Profile=source.Profile;g.CountdownRemaining=0;return g;
 }
 static string Raw(string id){return id.Substring(id.IndexOf(':')+1);}
 static string Outcome(PlayerState p,BotOpportunity item)
 {
  if(item.Kind==BotOpportunityKind.Obstacle)return p.HitObstacleIds.Contains(item.ObstacleId)?"hit":p.Distance>item.EndPosition+2e-7||p.Status==PlayerStatus.Finished?"avoided":"pending";
  if(item.Kind==BotOpportunityKind.Finish?p.FinishBoostsEarned>0:p.SuccessfulRiskIds.Contains(Raw(item.Id)))return "collected";
  return p.FailedRiskIds.Contains(Raw(item.Id))?"failed":p.Distance>item.EndPosition+2e-7||p.Status==PlayerStatus.Finished?"missed":"pending";
 }
 static bool Met(string intent,string outcome){return intent=="hit"?outcome=="hit":intent=="avoid"?outcome=="avoided":intent=="collect"?outcome=="collected":outcome=="missed"||outcome=="failed";}
 static object Audit(GameState before,double[] inputs,double end,StochasticBotDecision[] pending,BotOpportunity item,string intent)
 {
  var shadow=new StochasticBotController(RaceSnapshot.Clone(before));var type=typeof(StochasticBotController);var pendingType=type.GetNestedType("Pending",BindingFlags.NonPublic);var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pendingType));var map=StochasticTrackMap.Create(before.Track,before.Flight);
  foreach(var d in pending){var request=Activator.CreateInstance(pendingType,true);pendingType.GetField("Opportunity",Private).SetValue(request,map.Opportunities.Single(o=>o.Id==d.Id));pendingType.GetField("Intent",Private).SetValue(request,d.Intent);list.Add(request);}
  var result=type.GetMethod("Evaluate",Private).Invoke(shadow,new object[]{Personal(before),inputs,end,list});var unmet=(string[])Field(result,"UnmetIds");
  var fine=Personal(before);var p=fine.Players[0];int edge=0,steps=0,rejected=0;
  while(p.Status==PlayerStatus.Running&&p.Distance<end-2e-7&&steps<15000)
  {
   if(p.Recovering<=0&&edge<inputs.Length&&inputs[edge]-p.Distance<=2e-7){if(!RaceSimulation.Press(fine,0))rejected++;edge++;continue;}
   if(p.Recovering>0)while(edge<inputs.Length&&inputs[edge]<p.Distance-2e-7)edge++;
   double dt=1d/120;if(p.Recovering>0)dt=Math.Min(dt,p.Recovering);else{double target=edge<inputs.Length?Math.Min(end,inputs[edge]):end;dt=Math.Min(dt,Math.Max(1e-10,(target-p.Distance)/440));}RaceSimulation.Advance(fine,dt);steps++;
  }
  string outcome=Outcome(p,item);bool resolved=item.EndPosition<=end+2e-7||p.Status==PlayerStatus.Finished;
  return new{coarseScore=Field(result,"Score"),coarseCompleted=Field(result,"Completed"),coarseRejected=Field(result,"Rejected"),coarseUnmetIds=unmet,targetResolvedWithinHorizon=resolved,coarseTargetIntentMet=!unmet.Contains(item.Id),fineTargetOutcome=outcome,fineTargetIntentMet=Met(intent,outcome),fineCompleted=p.Distance>=end-2e-7||p.Status==PlayerStatus.Finished,fineRejected=rejected,fineSteps=steps,finePlayer=Player(p),note="Independent bounded projection only. No source/controller state is modified. Coarse-feasible vs fine-failed is a concrete prediction mismatch; failed bounded candidates do not prove global physical impossibility."};
 }
 internal static int Run(string inputPath)
 {
  const string output="Tools/StochasticLocalProbe/intent-witnesses-eb0fix";if(File.Exists(output+".json"))throw new Exception("Preserve existing witness proof; choose new output explicitly before rerun");
  var witnesses=JsonSerializer.Deserialize<Witness[]>(File.ReadAllText(inputPath),Json);if(witnesses.Length<1||witnesses.Length>5)throw new Exception("Expected1..5 exact witnesses");
  string core="Assets/Scripts/Core/StochasticBotController.cs",hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core)));if(hash!="EB0C4B445CD6DCE8EC421BB5B43194120D91164D4A816B13796ECDC696D963B9")throw new Exception("Witness mode requires frozen EB0 clipping fix");var rows=new List<object>();var sourceHashes=Directory.GetFiles("Assets/Scripts/Core","*.cs").OrderBy(x=>x,StringComparer.Ordinal).ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
  foreach(var witness in witnesses)
  {
   var track=RaceTracks.Get(witness.Track);FlightSettings flight=witness.Profile=="Chosen"?FlightPresets.Chosen:witness.Profile=="FastFlip"?FlightPresets.FastFlip:throw new Exception("Exact profile required");var map=StochasticTrackMap.Create(track,flight);var item=map.Opportunities.Single(x=>x.Id==witness.Id);var difficulty=Enum.Parse<BotDifficulty>(witness.Difficulty);
   var g=RaceSimulation.CreateStochasticBotRace(track,flight,difficulty,witness.Seed,PartyCamera.Lagger);var controller=new StochasticBotController(g);RaceSimulation.Press(g,0);var replay=RaceSnapshot.Clone(g);
   var plans=new List<object>();var actions=new List<object>();var events=new List<object>();var knownOutcomes=new Dictionary<string,string>();var hits=new HashSet<string>();var successes=new HashSet<string>();var failures=new HashSet<string>();int seenPlans=0,observed=0;string error="";bool predictionMismatch=false;
   controller.Observer=s=>
   {
    if(controller.PlanningCalls!=seenPlans)
    {
     var before=RaceSnapshot.Clone(replay);var snapshots=controller.PlanningSamples.Skip(seenPlans).ToArray();var currentInputs=(double[])typeof(StochasticBotController).GetField("planned",Private).GetValue(controller);var fresh=controller.Decisions.Where(d=>!knownOutcomes.ContainsKey(d.Id)).ToArray();
     foreach(var sample in snapshots)
     {
      bool finalPlan=sample.Number==controller.PlanningCalls;double[] selected=finalPlan?(double[])currentInputs.Clone():fresh.FirstOrDefault()?.SelectedInputs;
      var pending=controller.Decisions.Where(d=>(!knownOutcomes.TryGetValue(d.Id,out string previous)||previous=="pending")&&map.Opportunities.Single(o=>o.Id==d.Id).EndPosition>=before.Players[1].Distance-2e-7).ToArray();var watched=pending.FirstOrDefault(d=>d.Id==witness.Id);
      object audit=null;if(selected!=null&&watched!=null&&item.EndPosition<=sample.HorizonEndDistance+2e-7){audit=Audit(before,selected,sample.HorizonEndDistance,pending,item,watched.Intent);var auditType=audit.GetType();if((bool)auditType.GetProperty("coarseCompleted").GetValue(audit)&&(bool)auditType.GetProperty("coarseTargetIntentMet").GetValue(audit)&&(bool)auditType.GetProperty("fineCompleted").GetValue(audit)&&!(bool)auditType.GetProperty("fineTargetIntentMet").GetValue(audit))predictionMismatch=true;}
      plans.Add(new{sample.Number,sourceTime=before.Elapsed,sourceDistance=before.Players[1].Distance,sourcePlayer=Player(before.Players[1]),sample.AtDistance,sample.HorizonEndDistance,sample.Candidates,sample.ForecastSteps,sample.Rollbacks,selectedInputs=selected,inputCapture=finalPlan?"current private chosen plan":"fresh decision immutable input copy",inputsAvailable=selected!=null,planFailure=finalPlan?controller.LastPlanningFailure:null,pending=pending.Select(d=>new{d.Id,d.Intent,d.Probability,d.Roll}),targetAudit=audit});
     }
     seenPlans=controller.PlanningCalls;
    }
    if(s.Kind=="advance")RaceSimulation.Advance(replay,s.Delta);else if(s.Kind=="press"){if(RaceSimulation.Press(replay,1)!=s.Accepted)throw new Exception("Press replay mismatch");actions.Add(new{s.Time,s.Distance,s.Accepted,planningCall=controller.PlanningCalls,state=Player(g.Players[1])});}
    if(Program.State(g.Players)!=Program.State(replay.Players)||g.Elapsed!=replay.Elapsed||g.Phase!=replay.Phase)throw new Exception("Immediate ordinary shared world replay mismatch");observed++;
    var p=g.Players[1];foreach(string id in p.HitObstacleIds)if(hits.Add(id))events.Add(new{kind="hit",id,time=g.Elapsed,distance=p.Distance,planningCall=controller.PlanningCalls,state=Player(p)});foreach(string id in p.SuccessfulRiskIds)if(successes.Add(id))events.Add(new{kind="success",id,time=g.Elapsed,distance=p.Distance,planningCall=controller.PlanningCalls,state=Player(p)});foreach(string id in p.FailedRiskIds)if(failures.Add(id))events.Add(new{kind="failed",id,time=g.Elapsed,distance=p.Distance,planningCall=controller.PlanningCalls,state=Player(p)});
    foreach(var d in controller.Decisions)knownOutcomes[d.Id]=d.Outcome;
   };
   try{for(int frame=0;frame<12000&&g.Players[1].Status==PlayerStatus.Running;frame++)controller.Advance(1d/60);}catch(Exception ex){error=ex.ToString();}
   var decision=controller.Decisions.FirstOrDefault(d=>d.Id==witness.Id);var player=g.Players[1];rows.Add(new{witness,error,predictionMismatch,settings=g.StochasticBot,time=player.FinishTime,player.Status,targetDecision=decision,actualTargetOutcome=Outcome(player,item),replayObservations=observed,plans,actions,events,allDecisions=controller.Decisions,finalPlayer=Player(player)});
   bool stable=hash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core)));File.WriteAllText(output+".json",JsonSerializer.Serialize(new{status=error.Length>0?"ERROR":predictionMismatch?"PREDICTION_MISMATCH":rows.Count==witnesses.Length?"CAPTURED":"RUNNING",sourceHash=hash,sourceStable=stable,coreSourceHashes=sourceHashes,allCoreSourceStable=sourceHashes.All(x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Key)))==x.Value),witnessInputPath=inputPath,maximumCases=5,cadence=1d/60,calibration="None; initial model only; no held-out seeds",limits="Bounded local audit can exhibit a feasible maneuver or prediction mismatch. Failure to find one does not establish mathematical unreachability.",rows},Json));Console.WriteLine("CAPTURED "+witness.Track+"/"+witness.Profile+"/"+witness.Difficulty+"/"+witness.Seed+" "+witness.Id+" intent="+decision?.Intent+" outcome="+decision?.Outcome+" plans="+plans.Count+" error="+error.Length);if(error.Length>0)return 1;if(predictionMismatch){File.WriteAllText(output+".txt","PREDICTION_MISMATCH: selected coarse forecast met target, independent ordinary fine forecast did not. Stopped after this actual case; inspect per-plan prestate/combo audit. No Core mutation.\n");return 2;}
  }
  File.WriteAllText(output+".txt","CAPTURED bounded actual-factory initial-settings witness runs at1/60 with immediate ordinary shared-world replay; pre-Plan states/selected local inputs and independent coarse/fine local audits in JSON. No global unreachability claim, calibration, held-out run or Core mutation.\n");return 0;
 }
}