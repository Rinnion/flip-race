using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;
static class ShortMasterCheck
{
 internal static int Run(bool fixedOutput=false)
 {
  string output="Tools/StochasticLocalProbe/"+(fixedOutput?"master-short-fixed":"master-short-repro");
  var rows=new List<object>();bool all=true;
  for(int repeat=0;repeat<2;repeat++)
  {
   var track=RaceTracks.Get(TracksFull.ShortId);var flight=FlightPresets.FastFlip;var map=StochasticTrackMap.Create(track,flight);
   var game=RaceSimulation.CreateStochasticBotRace(track,flight,BotDifficulty.Master,0,PartyCamera.Lagger);var controller=new StochasticBotController(game);
   RaceSimulation.Press(game,0);var replay=RaceSnapshot.Clone(game);var presses=new List<object>();var contacts=new List<object>();int observed=0,plans=0;
   var planRows=new List<object>();var hits=new HashSet<string>();var successes=new HashSet<string>();var failed=new HashSet<string>();
   controller.Observer=s=>
   {
    if(s.Kind=="advance")RaceSimulation.Advance(replay,s.Delta);else if(s.Kind=="press")
    {bool accepted=RaceSimulation.Press(replay,1);if(accepted!=s.Accepted)throw new Exception("Press replay mismatch");presses.Add(new{s.Time,s.Distance,s.Accepted,s.Cursor,referenceNearest=map.ReferenceInputs.OrderBy(x=>Math.Abs(x-s.Distance)).First()});}
    var p=game.Players[1];if(Program.State(p)!=Program.State(replay.Players[1]))throw new Exception("Immediate ordinary player replay mismatch");observed++;
    if(controller.PlanningCalls!=plans){plans=controller.PlanningCalls;planRows.Add(new{number=plans,time=game.Elapsed,distance=p.Distance,p.Y,p.VerticalSpeed,p.Support,p.Gravity,controller.LastPlanningFailure,decisions=controller.Decisions.Count});}
    foreach(string id in p.HitObstacleIds)if(hits.Add(id))contacts.Add(new{kind="hit",id,time=game.Elapsed,distance=p.Distance,p.Y,p.VerticalSpeed,planningCall=controller.PlanningCalls});
    foreach(string id in p.SuccessfulRiskIds)if(successes.Add(id))contacts.Add(new{kind="success",id,time=game.Elapsed,distance=p.Distance,p.Y,p.VerticalSpeed,planningCall=controller.PlanningCalls});
    foreach(string id in p.FailedRiskIds)if(failed.Add(id))contacts.Add(new{kind="failed",id,time=game.Elapsed,distance=p.Distance,p.Y,p.VerticalSpeed,planningCall=controller.PlanningCalls});
   };
   for(int frame=0;frame<12000&&game.Phase!="finished";frame++)controller.Advance(1d/30);
   var player=game.Players[1];int expected=map.Opportunities.Count(o=>o.Kind==BotOpportunityKind.Positive);
   bool passed=player.Status==PlayerStatus.Finished&&player.Collisions==0&&player.SlowsEarned==0&&player.BoostsEarned==expected&&player.CompletedBoosts==expected&&player.FinishBoostsEarned==1&&player.FinishBoostElapsed>=.25-1e-8&&Math.Abs(player.FinishTime.Value-map.MasterTime)<1e-6;
   if(fixedOutput)
   {
    Console.WriteLine("[SHORT-boundary] MasterClean="+passed+" near2400="+string.Join(",",presses.Select(o=>(double)o.GetType().GetProperty("Distance").GetValue(o)).Where(x=>x>2380&&x<2420)));
    int coincident=presses.Count(o=>Math.Abs((double)o.GetType().GetProperty("Distance").GetValue(o)-2400)<=12+2e-7);
    if(coincident!=1)throw new Exception("Coincident reference edge 2400 or its validated local retime must be emitted exactly once");
    var method=typeof(StochasticBotController).GetMethod("Clip",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
    var isolated=RaceSimulation.CreateSolo(track,flight,PartyCamera.Lagger,-288.8,1520);var boundaryController=new StochasticBotController(isolated,0,1,0);
    foreach(double start in new[]{2400-1.2e-7,2400d,2400+1e-7})
    {var clipped=(double[])method.Invoke(boundaryController,new object[]{new[]{2400d},start,3400d});if(clipped.Length!=1||clipped[0]!=Math.Max(start,2400))throw new Exception("Unexecuted boundary edge lost");}
    typeof(StochasticBotController).GetField("lastEmittedDistance",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(boundaryController,2400d);
    foreach(double start in new[]{2400-1.2e-7,2400d,2400+1e-7})if(((double[])method.Invoke(boundaryController,new object[]{new[]{2400d},start,3400d})).Length!=0)throw new Exception("Consumed reference edge resurrected");
   }
   all&=passed;
   rows.Add(new{repeat,passed,trackId=track.Id,profile="FastFlip",seed=0u,expectedTime=map.MasterTime,expectedBoosts=expected,time=player.FinishTime,hits=player.HitObstacleIds,successes=player.SuccessfulRiskIds,failures=player.FailedRiskIds,player.BoostsEarned,player.CompletedBoosts,player.FinishBoostsEarned,player.FinishBoostElapsed,replayObservations=observed,presses,contacts,plans=planRows,decisions=controller.Decisions});
   Console.WriteLine("Short/FastFlip/Master/0 repeat="+repeat+" "+(passed?"PASS":"FAIL")+" time="+player.FinishTime+" hits="+player.Collisions+" boosts="+player.CompletedBoosts+" expected="+map.MasterTime+" firstHit="+hits.FirstOrDefault());
  }
  var report=new{status=all?"PASS":"FAIL",sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Assets/Scripts/Core/StochasticBotController.cs"))),rows};
  File.WriteAllText(output+".json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));File.WriteAllText(output+".txt",report.status+" deterministic actual metadata factory/ctor Short FastFlip seed0; strict Master time/all ordinary pulses/full finish/zero hits and immediate ordinary replay. See JSON.");return all?0:1;
 }
}