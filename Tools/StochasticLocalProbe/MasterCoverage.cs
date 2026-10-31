using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;
static class MasterCoverage
{
 sealed class Row
 {
  public string Track{get;set;}public string Profile{get;set;}public uint Seed{get;set;}public int Cadence{get;set;}public string Camera{get;set;}public string Status{get;set;}public string Error{get;set;}
  public double? Time{get;set;}public double ExpectedTime{get;set;}public int Hits{get;set;}public int Boosts{get;set;}public int CompletedBoosts{get;set;}public int FinishBoosts{get;set;}public double FinishElapsed{get;set;}
  public double[] Inputs{get;set;}public int ReplayObservations{get;set;}public int Plans{get;set;}public int ForecastSteps{get;set;}public int ForecastObservations{get;set;}public int ForecastViolations{get;set;}
  public double Requested{get;set;}public double Trial{get;set;}public double Overshoot{get;set;}public string[] HitIds{get;set;}public string[] MissedIds{get;set;}
 }
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
 static Row Run(Track track,FlightSettings flight,uint seed,int cadence,PartyCamera camera)
 {
  var map=StochasticTrackMap.Create(track,flight);var g=RaceSimulation.CreateStochasticBotRace(track,flight,BotDifficulty.Master,seed,camera);var controller=new StochasticBotController(g);
  RaceSimulation.Press(g,0);var replay=RaceSnapshot.Clone(g);var inputs=new List<double>();int observations=0;
  var row=new Row{Track=track.Id,Profile=map.ProfileId,Seed=seed,Cadence=cadence,Camera=camera.ToString(),Status="FAIL",ExpectedTime=map.MasterTime};
  try
  {
   controller.Observer=s=>
   {
    if(s.Kind=="advance")RaceSimulation.Advance(replay,s.Delta);
    else if(s.Kind=="press"){Need(replay.Players[1].Status==PlayerStatus.Running,"Press after target terminal");Need(RaceSimulation.Press(replay,1)==s.Accepted&&s.Accepted,"Press acceptance/replay");inputs.Add(s.Distance);}
    Need(Program.State(g.Players)==Program.State(replay.Players)&&g.Elapsed==replay.Elapsed&&g.Phase==replay.Phase,"Immediate ordinary shared-world replay");observations++;
   };
   double[][] cadenceInputs={new[]{1d/30},new[]{1d/60},new[]{.005,.027,.011,.073,.001,.049,.016,.211}};int frame=0;
   while(g.Players[1].Status==PlayerStatus.Running&&frame<12000)controller.Advance(cadenceInputs[cadence][frame++%cadenceInputs[cadence].Length]);
   var p=g.Players[1];int count=map.Opportunities.Count(x=>x.Kind==BotOpportunityKind.Positive);
   Need(p.Status==PlayerStatus.Finished&&p.Collisions==0&&p.SlowsEarned==0&&p.BoostsEarned==count&&p.CompletedBoosts==count&&p.FinishBoostsEarned==1&&p.FinishBoostElapsed>=.25-1e-8&&p.FinishTime.HasValue&&Math.Abs(p.FinishTime.Value-map.MasterTime)<1e-6,"Master actual full clean reference/pulse ledger");
   Need(controller.MaximumRequestedHorizon<=1000+2e-7&&controller.MaximumTrialHorizon<=1055+2e-7&&controller.MaximumTrialOvershoot<=55+2e-7&&controller.ForecastHorizonViolations==0&&controller.ForecastObservations==controller.ForecastSteps,"All actual transient/requested horizon bounds");
   Need(controller.PlanningSamples.All(s=>s.Candidates<=13),"Master candidate cap");
   foreach(var d in controller.Decisions){Need(d.Intent==(d.Kind==BotOpportunityKind.Obstacle?"avoid":"collect"),"Master intention");Need(d.Outcome==(d.Kind==BotOpportunityKind.Obstacle?"avoided":"collected")&&d.UnreachableReason.Length==0,"Master actual intention outcome");var selected=d.SelectedInputs;Need(selected.Length<=12&&selected.All(x=>!double.IsNaN(x)&&!double.IsInfinity(x)&&x>=d.AtDistance-2e-7&&x<=d.HorizonEndDistance),"Local selected inputs bound");for(int i=1;i<selected.Length;i++)Need(selected[i]>selected[i-1],"Local selected input ordering");}
   if(track.Id==TracksFull.ShortId&&ReferenceEquals(flight,FlightPresets.FastFlip)&&seed==0)Need(inputs.Count(x=>Math.Abs(x-2400)<=12+2e-7)==1,"Coincident 2400 edge or validated retime exactly once");
   row.Status="PASS";
  }
  catch(Exception ex){row.Error=ex.Message;}
  var player=g.Players[1];row.Time=player.FinishTime;row.Hits=player.Collisions;row.Boosts=player.BoostsEarned;row.CompletedBoosts=player.CompletedBoosts;row.FinishBoosts=player.FinishBoostsEarned;row.FinishElapsed=player.FinishBoostElapsed;row.Inputs=inputs.ToArray();row.ReplayObservations=observations;row.Plans=controller.PlanningCalls;row.ForecastSteps=controller.ForecastSteps;row.ForecastObservations=controller.ForecastObservations;row.ForecastViolations=controller.ForecastHorizonViolations;row.Requested=controller.MaximumRequestedHorizon;row.Trial=controller.MaximumTrialHorizon;row.Overshoot=controller.MaximumTrialOvershoot;row.HitIds=player.HitObstacleIds.OrderBy(x=>x).ToArray();row.MissedIds=map.Opportunities.Where(x=>x.Kind==BotOpportunityKind.Positive&&!player.SuccessfulRiskIds.Contains(x.Id.Substring(x.Id.IndexOf(':')+1))).Select(x=>x.Id).ToArray();return row;
 }
 internal static int RunAll(bool target=false)
 {
  string path="Tools/StochasticLocalProbe/"+(target?"master-clipping-target":"master-all-pairs-clippingfix");
  if(File.Exists(path+".json"))throw new Exception("Preserve existing proof; choose an explicit new report name before rerun: "+path);
  string source="Assets/Scripts/Core/StochasticBotController.cs",hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));var clock=Stopwatch.StartNew();var rows=new List<Row>();bool green=true;
  void Write(string status)
  {
   bool sourceStable=hash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
   var report=new{status,sourceHash=hash,sourceStable,declaredTrackCount=target?1:5,declaredProfiles=new[]{"Chosen","FastFlip"},declaredSeeds=Enumerable.Range(0,target?1:12),declaredCadences=new[]{0,1,2},declaredCameras=new[]{"Lagger","Constant"},expectedCases=target?12:720,completedCases=rows.Count,replayObservations=rows.Sum(x=>x.ReplayObservations),elapsedMilliseconds=clock.Elapsed.TotalMilliseconds,actualInputVariation=rows.Where(x=>x.Cadence==0).GroupBy(x=>x.Track+"/"+x.Profile+"/"+x.Camera).Select(g=>new{pair=g.Key,distinctTapes=g.Select(x=>string.Join("|",x.Inputs.Select(v=>v.ToString("F5",CultureInfo.InvariantCulture)))).Distinct().Count()}),cases=rows};
   File.WriteAllText(path+".json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));File.WriteAllText(path+".txt",status+" cases="+rows.Count+" sourceStable="+sourceStable+" controller="+hash+"\nActual factory/metadata controller, strict full clean Master time/pulses, immediate ordinary shared-world replay, unchanged cadence tolerance5e-5, requested/trial bounds and candidate/input caps. Managed correctness proof; no runtime performance claim.\n");
  }
  foreach(var track in RaceTracks.All.Where(t=>!target||t.Id==TracksFull.ShortId))foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
  {
   for(uint seed=0;seed<(target?1:12);seed++)foreach(var camera in new[]{PartyCamera.Lagger,PartyCamera.Constant})
   {
    Row baseline=null;for(int cadence=0;cadence<3;cadence++)
    {
     var row=Run(track,flight,seed,cadence,camera);rows.Add(row);
     if(baseline==null)baseline=row;else if(row.Status=="PASS"&&(row.Inputs.Length!=baseline.Inputs.Length||row.Inputs.Where((x,i)=>Math.Abs(x-baseline.Inputs[i])>=5e-5).Any())){row.Status="FAIL";row.Error="Cadence actual input mismatch";}
     if(row.Status!="PASS"){green=false;Write("FAIL");Console.WriteLine("FAIL "+row.Track+"/"+row.Profile+"/"+seed+"/"+camera+"/"+cadence+" "+row.Error+" time="+row.Time+" hits="+row.Hits);return 1;}
    }
   }
   Write("RUNNING");Console.WriteLine("PASS "+track.Id+"/"+StochasticTrackMap.Create(track,flight).ProfileId+" cases="+rows.Count+" elapsedSec="+clock.Elapsed.TotalSeconds.ToString("F2",CultureInfo.InvariantCulture));
  }
  if(!target)foreach(var group in rows.Where(x=>x.Cadence==0).GroupBy(x=>x.Track+"/"+x.Profile+"/"+x.Camera))Need(group.Select(x=>string.Join("|",x.Inputs.Select(v=>v.ToString("F5",CultureInfo.InvariantCulture)))).Distinct().Count()>=2,"Master actual timing variation absent "+group.Key);
  Need(hash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),"Controller source changed");Write(green?"PASS":"FAIL");Console.WriteLine("PASS master coverage cases="+rows.Count+" replayObservations="+rows.Sum(x=>x.ReplayObservations));return green?0:1;
 }
}