using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlipRace.Core;
static class RejectedInputCheck
{
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static readonly JsonSerializerOptions Json=new JsonSerializerOptions{WriteIndented=true,IncludeFields=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
 static JsonElement Player(PlayerState p){return JsonSerializer.SerializeToElement(p,Json);}
 static object Audit(GameState source,StochasticBotDecision[] pending,double[] inputs,double end)
 {
  var shadow=new StochasticBotController(RaceSnapshot.Clone(source));var type=typeof(StochasticBotController);var requestType=type.GetNestedType("Pending",BindingFlags.NonPublic);var requests=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(requestType));var map=StochasticTrackMap.Create(source.Track,source.Flight);
  foreach(var decision in pending){var request=Activator.CreateInstance(requestType,true);requestType.GetField("Opportunity",Private).SetValue(request,map.Opportunities.Single(x=>x.Id==decision.Id));requestType.GetField("Intent",Private).SetValue(request,decision.Intent);requests.Add(request);}
  GameState Personal(){var copy=RaceSnapshot.Clone(source);var g=RaceSimulation.CreateSolo(source.Track,source.Flight,PartyCamera.Lagger,-288.8,1520);g.Players=new[]{copy.Players[1]};g.Elapsed=source.Elapsed;g.Phase="racing";g.Motion=source.Motion;g.Profile=source.Profile;g.CountdownRemaining=0;return g;}
  var coarse=type.GetMethod("Evaluate",Private).Invoke(shadow,new object[]{Personal(),inputs,end,requests});object Value(string name){return coarse.GetType().GetField(name,Private).GetValue(coarse);}
  var fine=Personal();var omitted=RaceSnapshot.Clone(fine);var p=fine.Players[0];var trace=new List<object>();int edge=0,steps=0,rejected=0,comparison=0;bool exactOmission=true;
  while(p.Status==PlayerStatus.Running&&p.Distance<end-2e-7&&steps<15000)
  {
   if(p.Recovering<=0&&edge<inputs.Length&&inputs[edge]-p.Distance<=2e-7){var before=Player(p);bool accepted=RaceSimulation.Press(fine,0);if(accepted){if(!RaceSimulation.Press(omitted,0))throw new Exception("Omitted rejection branch acceptance differs");}else rejected++;trace.Add(new{edge,plannedDistance=inputs[edge],time=fine.Elapsed,distance=p.Distance,accepted,before,after=Player(p)});edge++;}
   else
   {
    if(p.Recovering>0)while(edge<inputs.Length&&inputs[edge]<p.Distance-2e-7)edge++;
    double dt=1d/120;if(p.Recovering>0)dt=Math.Min(dt,p.Recovering);else{double target=edge<inputs.Length?Math.Min(end,inputs[edge]):end;dt=Math.Min(dt,Math.Max(1e-10,(target-p.Distance)/440));}RaceSimulation.Advance(fine,dt);RaceSimulation.Advance(omitted,dt);steps++;
   }
   exactOmission&=Program.State(fine.Players)==Program.State(omitted.Players)&&fine.Elapsed==omitted.Elapsed&&fine.Phase==omitted.Phase;comparison++;
  }
  return new{sourceTime=source.Elapsed,sourcePlayer=Player(source.Players[1]),inputs,end,pending=pending.Select(d=>new{d.Id,d.Intent,d.Probability,d.Roll}),coarseScore=Value("Score"),coarseRejected=Value("Rejected"),coarseUnmetIds=Value("UnmetIds"),coarseCompleted=Value("Completed"),fineRejected=rejected,fineSteps=steps,trace,fineFinal=Player(p),omittingRejectedPressPreservesExactState=exactOmission,omissionReplayComparisons=comparison,note="Both omission branches receive identical ordinary Advance slices; the second skips only calls already observed false in the first. This isolates false Press physical no-op without altering legal input rules."};
 }
 internal static int Run(bool audit=false)
 {
  string output="Tools/StochasticLocalProbe/"+(audit?"rejected-input-audit":"rejected-input-repro");if(File.Exists(output+".json"))throw new Exception("Preserve prior proof: output exists");
  var hashes=Directory.GetFiles("Assets/Scripts/Core","*.cs").OrderBy(x=>x,StringComparer.Ordinal).ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));var rows=new List<object>();int totalRejected=0;
  for(int repeat=0;repeat<(audit?1:2);repeat++)
  {
   var track=RaceTracks.Get(TracksFull.LongId);var g=RaceSimulation.CreateStochasticBotRace(track,FlightPresets.FastFlip,BotDifficulty.Novice,0,PartyCamera.Lagger);var controller=new StochasticBotController(g);RaceSimulation.Press(g,0);var replay=RaceSnapshot.Clone(g);
   var actions=new List<object>();var plans=new List<object>();int observed=0,accepted=0,rejected=0,lastPlan=0;object firstRejected=null,lastPlanCapture=null;GameState lastPlanOrigin=null;StochasticBotDecision[] lastPending=null;double[] lastInputs=null;double lastEnd=0;var knownOutcomes=new Dictionary<string,string>();
   controller.Observer=s=>
   {
    if(controller.PlanningCalls!=lastPlan)
    {
     var inputs=(double[])typeof(StochasticBotController).GetField("planned",Private).GetValue(controller);lastPlanOrigin=RaceSnapshot.Clone(replay);lastInputs=(double[])inputs.Clone();lastEnd=controller.PlanningSamples.Last().HorizonEndDistance;lastPending=controller.Decisions.Where(d=>(!knownOutcomes.TryGetValue(d.Id,out string outcome)||outcome=="pending")&&StochasticTrackMap.Create(g.Track,g.Flight).Opportunities.Single(x=>x.Id==d.Id).EndPosition>=replay.Players[1].Distance-2e-7).ToArray();lastPlanCapture=new{number=controller.PlanningCalls,sourceTime=replay.Elapsed,sourcePlayer=Player(replay.Players[1]),inputs=(double[])inputs.Clone(),controller.LastPlanningFailure,samples=controller.PlanningSamples.Skip(lastPlan).ToArray()};plans.Add(lastPlanCapture);lastPlan=controller.PlanningCalls;
    }
    if(s.Kind=="advance")RaceSimulation.Advance(replay,s.Delta);
    else if(s.Kind=="press")
    {
     var before=Player(replay.Players[1]);bool ordinary=RaceSimulation.Press(replay,1);if(ordinary!=s.Accepted)throw new Exception("Press acceptance replay mismatch");
     var action=new{s.Time,s.Distance,s.Cursor,s.Accepted,before,after=Player(g.Players[1]),plannedCursor=(int)typeof(StochasticBotController).GetField("plannedCursor",Private).GetValue(controller),plannedInputs=(double[])((double[])typeof(StochasticBotController).GetField("planned",Private).GetValue(controller)).Clone(),precedingPlan=lastPlanCapture};actions.Add(action);
     if(s.Accepted)accepted++;else{rejected++;if(firstRejected==null)firstRejected=action;}
    }
    if(Program.State(g.Players)!=Program.State(replay.Players)||g.Elapsed!=replay.Elapsed||g.Phase!=replay.Phase)throw new Exception("Immediate ordinary shared-world replay mismatch");observed++;foreach(var decision in controller.Decisions)knownOutcomes[decision.Id]=decision.Outcome;
   };
   for(int frame=0;frame<12000&&g.Players[1].Status==PlayerStatus.Running&&(!audit||rejected==0);frame++)controller.Advance(1d/60);
   var p=g.Players[1];totalRejected+=rejected;rows.Add(new{repeat,passed=rejected==0,settings=g.StochasticBot,time=p.FinishTime,p.Status,accepted,rejected,p.Collisions,p.CompletedBoosts,p.FinishBoostsEarned,replayObservations=observed,firstRejected,localAudit=audit?Audit(lastPlanOrigin,lastPending,lastInputs,lastEnd):null,actions,plans,decisions=controller.Decisions});
   Console.WriteLine("Long/FastFlip/Novice/0 repeat="+repeat+" "+(rejected==0?"PASS":"FAIL")+" time="+p.FinishTime+" accepted="+accepted+" rejected="+rejected+" hits="+p.Collisions+" boosts="+p.CompletedBoosts);
  }
  bool stable=hashes.All(x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Key)))==x.Value);var report=new{status=totalRejected==0?"PASS":"FAIL",assertion="Zero actual rejected Press calls",coreSourceHashes=hashes,sourceStable=stable,physicalRuns=audit?1:2,cadence=1d/60,rows};File.WriteAllText(output+".json",JsonSerializer.Serialize(report,Json));File.WriteAllText(output+".txt",report.status+" actual-factory Long/FastFlip/Novice seed0 red reproduction(s) at1/60, strict zero actual rejected Press criterion; ordinary shared-world replay checked immediately after every emitted slice/action. SourceStable="+stable+". See firstRejected prestate/currentcombo/precedingPlan in JSON. No Core mutation.\n");return totalRejected==0?0:1;
 }
}