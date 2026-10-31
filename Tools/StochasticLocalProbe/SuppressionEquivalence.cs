using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlipRace.Core;
static class SuppressionEquivalence
{
 const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;
 static void Need(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
 static object Read(object value,string name){var type=value.GetType();var property=type.GetProperty(name,Any);return property!=null?property.GetValue(value):type.GetField(name,Any).GetValue(value);}
 static void Set(object value,string name,object assigned){value.GetType().GetField(name,Any).SetValue(value,assigned);}
 static string Hash(string path){return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));}
 static string ControllerState(object controller)
 {
  var result=new StringBuilder();foreach(string name in new[]{"Cursor","PlanningCalls","CandidateSimulations","ForecastSteps","ForecastTrialRollbacks","ForecastCheckpoints","MaximumRequestedHorizon","MaximumTrialHorizon","MaximumTrialOvershoot","ForecastObservations","ForecastHorizonViolations","LastPlanningFailure","Settings","planned","plannedCursor","nextOpportunity","cursor","wasRecovering","lastEmittedDistance"})result.Append(name).Append('=').Append(Program.State(Read(controller,name)));
  foreach(object decision in (IEnumerable)Read(controller,"Decisions")){result.Append("decision:");foreach(string name in new[]{"Id","Kind","Intent","Probability","Roll","AtDistance","AtTime","HorizonEndDistance","Outcome","UnreachableReason","SelectedInputs"})result.Append(name).Append('=').Append(Program.State(Read(decision,name)));}
  foreach(object plan in (IEnumerable)Read(controller,"PlanningSamples")){result.Append("plan:");foreach(string name in new[]{"Number","AtDistance","HorizonEndDistance","Candidates","ForecastSteps","Rollbacks"})result.Append(name).Append('=').Append(Program.State(Read(plan,name)));}return result.ToString();
 }
 sealed class OldCore
 {
  readonly Assembly assembly;readonly Type simulation,tracks,presets,controller,camera,difficulty,game;
  internal OldCore(string path){assembly=new AssemblyLoadContext("SuppressionBaseline",true).LoadFromAssemblyPath(Path.GetFullPath(path));Type T(string name){return assembly.GetType("FlipRace.Core."+name,true);}simulation=T("RaceSimulation");tracks=T("RaceTracks");presets=T("FlightPresets");controller=T("StochasticBotController");camera=T("PartyCamera");difficulty=T("BotDifficulty");game=T("GameState");}
  object Track(string id){return tracks.GetMethod("Get",new[]{typeof(string)}).Invoke(null,new object[]{id});}
  object Flight(string profile){return presets.GetField(profile).GetValue(null);}
  internal object Race(string id,string profile,string level,uint seed,string cam){var method=simulation.GetMethods().Single(m=>m.Name=="CreateStochasticBotRace"&&m.GetParameters()[2].ParameterType==difficulty);return method.Invoke(null,new[]{Track(id),Flight(profile),Enum.Parse(difficulty,level),(object)seed,Enum.Parse(camera,cam),-288.8,1520d});}
  internal object Solo(){return simulation.GetMethod("CreateSolo").Invoke(null,new[]{Track(TracksFull.LongId),Flight("FastFlip"),Enum.Parse(camera,"Lagger"),(object)(-288.8),1520d});}
  internal object Controller(object race){return controller.GetConstructor(new[]{game,typeof(int)}).Invoke(new[]{race,(object)1});}
  internal bool Press(object race,int slot){return (bool)simulation.GetMethod("Press").Invoke(null,new[]{race,(object)slot});}
  internal void Advance(object bot,double delta,bool paused=false){controller.GetMethod("Advance").Invoke(bot,new object[]{delta,paused});}
  internal void Observe(object bot,Action<object> callback){var field=controller.GetField("Observer");var parameter=Expression.Parameter(field.FieldType.GetGenericArguments()[0],"slice");var body=Expression.Call(Expression.Constant(callback),typeof(Action<object>).GetMethod("Invoke"),Expression.Convert(parameter,typeof(object)));field.SetValue(bot,Expression.Lambda(field.FieldType,body,parameter).Compile());}
 }
 sealed class Snapshot
 {
  internal string Kind,World,Controller;internal double Delta,Time,Distance;internal int Cursor;internal bool Accepted;
 }
 static int QueryChecks(OldCore old)
 {
  int count=0;
  foreach(string phase in new[]{"ready","countdown","racing","finished"})foreach(bool lazy in new[]{false,true})
  {
   var current=RaceSimulation.CreateSolo(RaceTracks.Get(TracksFull.LongId),FlightPresets.FastFlip,PartyCamera.Lagger,-288.8,1520);var prior=old.Solo();current.Phase=phase;Set(prior,"Phase",phase);if(lazy){current.Flight=null;Set(prior,"Flight",null);}
   string before=Program.State(current);bool can=RaceSimulation.CanPress(current,0);Need(before==Program.State(current),"CanPress mutates "+phase+" lazy="+lazy);Need(can==old.Press(prior,0),"CanPress differs from old Press "+phase);Need(RaceSimulation.Press(current,0)==can,"New Press differs from query "+phase);Need(Program.State(current)==Program.State(prior),"Ready/countdown/reset/lazy behavior differs "+phase);count++;
  }
  foreach(string fixture in new[]{"invalid-negative","invalid-high","recovery","terminal-status","terminal-time","air-used","air-available"})
  {
   var current=RaceSimulation.CreateSolo(RaceTracks.Get(TracksFull.LongId),FlightPresets.FastFlip,PartyCamera.Lagger,-288.8,1520);var prior=old.Solo();current.Phase="racing";Set(prior,"Phase","racing");var p=current.Players[0];var previous=((Array)Read(prior,"Players")).GetValue(0);int slot=fixture=="invalid-negative"?-1:fixture=="invalid-high"?1:0;
   if(fixture=="recovery"){p.Recovering=.2;Set(previous,"Recovering",.2);}if(fixture=="terminal-status"){p.Status=PlayerStatus.Eliminated;Set(previous,"Status",Enum.Parse(previous.GetType().GetField("Status").FieldType,"Eliminated"));}if(fixture=="terminal-time"){p.FinishTime=1;Set(previous,"FinishTime",1d);}if(fixture.StartsWith("air-")){p.Flipping=true;p.AirFlipAvailable=fixture=="air-available";Set(previous,"Flipping",true);Set(previous,"AirFlipAvailable",p.AirFlipAvailable);}
   string before=Program.State(current);bool can=RaceSimulation.CanPress(current,slot);Need(before==Program.State(current),"CanPress mutates "+fixture);Need(can==old.Press(prior,slot),"CanPress differs from old Press "+fixture);Need(RaceSimulation.Press(current,slot)==can,"New Press differs "+fixture);Need(Program.State(current)==Program.State(prior),"Racing guard semantics differ "+fixture);count++;
  }
  return count;
 }
 static object Case(OldCore old,string trackId,string profile,string level,uint seed,int cadence,string camera)
 {
  var track=RaceTracks.Get(trackId);var flight=profile=="Chosen"?FlightPresets.Chosen:FlightPresets.FastFlip;var g=RaceSimulation.CreateStochasticBotRace(track,flight,Enum.Parse<BotDifficulty>(level),seed,Enum.Parse<PartyCamera>(camera));var bot=new StochasticBotController(g);var oldGame=old.Race(trackId,profile,level,seed,camera);var oldBot=old.Controller(oldGame);
  Need(RaceSimulation.Press(g,0)==old.Press(oldGame,0),"Initial readiness");Need(Program.State(g)==Program.State(oldGame),"Initial world");var ordinary=RaceSnapshot.Clone(g);var snapshots=new Queue<Snapshot>();int accepted=0,rejected=0,suppressed=0,oldRejected=0,worldComparisons=0,controllerComparisons=0,replayComparisons=0;var suppressedActions=new List<object>();
  bot.Observer=s=>
  {
   if(s.Kind=="advance")RaceSimulation.Advance(ordinary,s.Delta);else if(s.Kind=="press"){Need(RaceSimulation.Press(ordinary,1)==s.Accepted,"Ordinary acceptance replay");if(s.Accepted)accepted++;else rejected++;}else if(s.Kind=="suppressed"){Need(!s.Accepted&&!RaceSimulation.CanPress(ordinary,1),"Suppression legality");suppressed++;suppressedActions.Add(new{s.Time,s.Distance,s.Cursor});}else throw new Exception("Unknown new event "+s.Kind);
   Need(Program.State(g)==Program.State(ordinary),"Immediate whole ordinary shared-world replay");replayComparisons++;
   snapshots.Enqueue(new Snapshot{Kind=s.Kind,Delta=s.Delta,Time=s.Time,Distance=s.Distance,Cursor=s.Cursor,Accepted=s.Accepted,World=Program.State(g),Controller=ControllerState(bot)});
  };
  old.Observe(oldBot,s=>
  {
   Need(snapshots.Count>0,"Old emitted additional event");var expected=snapshots.Dequeue();string kind=(string)Read(s,"Kind");bool isAccepted=(bool)Read(s,"Accepted");bool oldFalse=kind=="press"&&!isAccepted;if(oldFalse)oldRejected++;
   Need(expected.Kind==(oldFalse?"suppressed":kind),"Event kind differs beyond suppression");Need(expected.Delta==(double)Read(s,"Delta")&&expected.Time==(double)Read(s,"Time")&&expected.Distance==(double)Read(s,"Distance")&&expected.Cursor==(int)Read(s,"Cursor")&&expected.Accepted==isAccepted,"Exact action/advance timing/cursor differs");
   Need(expected.World==Program.State(oldGame),"Exact old/new whole shared-world fields/metadata differ");worldComparisons++;Need(expected.Controller==ControllerState(oldBot),"Exact selectedplans/decisions/rolls/cursors/forecast counters differ");controllerComparisons++;
  });
  double[][] steps={new[]{1d/30},new[]{1d/60},new[]{.005,.027,.011,.073,.001,.049,.016,.211}};int frame=0;
  for(;g.Phase!="finished"&&frame<15000;frame++){double dt=steps[cadence][frame%steps[cadence].Length];bot.Advance(dt);old.Advance(oldBot,dt);Need(snapshots.Count==0,"New emitted additional event");Need(Program.State(g)==Program.State(oldGame)&&ControllerState(bot)==ControllerState(oldBot),"Postframe exact world/controller differs");}
  Need(g.Phase=="finished"&&rejected==0,"Full finish / zero actual rejected inputs");Need(suppressed==bot.SuppressedInputs&&suppressed==oldRejected,"Separate suppression telemetry differs from old false edges");Need(bot.ForecastObservations==bot.ForecastSteps&&bot.ForecastHorizonViolations==0&&bot.MaximumRequestedHorizon<=1000+2e-7&&bot.MaximumTrialHorizon<=1055+2e-7&&bot.MaximumTrialOvershoot<=55+2e-7,"Forecast bounds");Need(bot.PlanningSamples.All(p=>p.Candidates<=(level=="Master"?13:96)),"Candidate cap");
  var player=g.Players[1];if(level=="Master")Need(player.Collisions==0&&player.CompletedBoosts==9&&player.FinishBoostsEarned==1&&player.FinishBoostElapsed>=.25-1e-8&&Math.Abs(player.FinishTime.Value-StochasticTrackMap.Create(track,flight).MasterTime)<1e-6,"Master full clean");
  Console.WriteLine("PASS "+trackId+"/"+profile+"/"+level+"/"+seed+" cadence="+cadence+" camera="+camera+" accepted="+accepted+" suppressed="+suppressed+" oldRejected="+oldRejected+" compares="+worldComparisons);
  return new{trackId,profile,difficulty=level,seed,cadence,camera,time=player.FinishTime,accepted,rejected,suppressed,oldRejected,worldComparisons,controllerComparisons,replayComparisons,frames=frame,player.Collisions,player.CompletedBoosts,player.FinishBoostsEarned,bot.Cursor,bot.PlanningCalls,bot.CandidateSimulations,bot.ForecastSteps,bot.ForecastObservations,bot.ForecastHorizonViolations,bot.MaximumRequestedHorizon,bot.MaximumTrialHorizon,bot.MaximumTrialOvershoot,suppressedActions};
 }
 internal static int Run()
 {
  const string output="Tools/StochasticLocalProbe/suppression-equivalence-v2";if(File.Exists(output+".json"))throw new Exception("Preserve prior proof; output exists");const string baseline="Tools/StochasticLocalProbe/obj/suppression-baseline/bin/Debug/net8.0/StochasticSuppressionBaseline.dll";
  var sourceHashes=Directory.GetFiles("Assets/Scripts/Core","*.cs").OrderBy(x=>x,StringComparer.Ordinal).ToDictionary(x=>x,Hash);string baselineHash=Hash(baseline);Need(baselineHash=="53B79A72A7579ADC00E934D108679AFF84B1EB639FC079602102A60BAFBB7749","Unexpected EB0 baseline DLL");
  string currentText=File.ReadAllText("Assets/Scripts/Core/StochasticBotController.cs"),oldText=File.ReadAllText("Tools/StochasticLocalProbe/obj/suppression-baseline-src/StochasticBotController.cs");string Section(string text,string start,string end){int a=text.IndexOf(start,StringComparison.Ordinal),b=text.IndexOf(end,a,StringComparison.Ordinal);return text.Substring(a,b-a);}Need(Section(currentText,"        Evaluation Evaluate(","        bool Decide(")==Section(oldText,"        Evaluation Evaluate(","        bool Decide("),"Evaluate/scoring/planning/ranking source changed");
  var old=new OldCore(baseline);var rows=new List<object>();string status="PASS",error="";int queryFixtures=0;
  try
  {
   queryFixtures=QueryChecks(old);for(int cadence=0;cadence<3;cadence++)rows.Add(Case(old,TracksFull.LongId,"FastFlip","Novice",0,cadence,"Lagger"));
   foreach(var witness in new[]{(TracksFull.LongId,"Amateur",26u),(TracksFull.ShortId,"Novice",11u),(TracksFull.ShortId,"Strong",20u),(TracksRhythm.CascadeId,"Amateur",113u),(TracksMixed.Id,"Novice",13u),(TracksMixed.Id,"Confident",68u)})rows.Add(Case(old,witness.Item1,"FastFlip",witness.Item2,witness.Item3,1,"Lagger"));
   rows.Add(Case(old,TracksRhythm.CascadeId,"Chosen","Master",7,1,"Constant"));rows.Add(Case(old,TracksFull.ShortId,"FastFlip","Master",7,2,"Constant"));
  }
  catch(Exception ex){status="FAIL";error=ex.ToString();Console.WriteLine(error);}
  bool stable=sourceHashes.All(x=>Hash(x.Key)==x.Value);if(!stable){status="FAIL";error+="Core source changed";}
  File.WriteAllText(output+".json",JsonSerializer.Serialize(new{status,error,checks,queryFixtures,declaredCases=11,completedCases=rows.Count,sourceStable=stable,coreSourceHashes=sourceHashes,baselineAssemblyHash=baselineHash,baselineSourceManifest="Tools/StochasticLocalProbe/obj/suppression-baseline-source-hashes.json",evaluationPlanningScoreRankingSourceIdentical=true,comparison="Exact every-observer whole GameState/public physical fields and metadata; exact controller selected plans/private cursor/last-distance/decision-intent-rolls and forecast counters. Only old false Press=>suppressed plus separate newcounter allowed. CPU PlanningSamples.Milliseconds is observational and excluded.",cases=rows},new JsonSerializerOptions{WriteIndented=true}));
  File.WriteAllText(output+".txt",status+" cases="+rows.Count+"/11 queryFixtures="+queryFixtures+" checks="+checks+" sourceStable="+stable+"\n"+error+"\nSee JSON for exact old/new full-rest-of-race equivalence to preserved EB0 assembly and separate suppression telemetry. No probability/planning/input-limit changes; no calibration or held-out runs.\n");return status=="PASS"?0:1;
 }
}