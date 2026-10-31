using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlipRace.Core;
static class Program
{
 static int checks;static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 internal static string State(object value)
 {
  var b=new StringBuilder();void Visit(object v){if(v==null){b.Append("null;");return;}Type t=v.GetType();if(t.IsPrimitive||t.IsEnum||v is string){b.Append(Convert.ToString(v,CultureInfo.InvariantCulture)).Append(';');return;}if(v is HashSet<string> set){foreach(string item in set.OrderBy(x=>x,StringComparer.Ordinal))Visit(item);return;}if(v is IEnumerable seq){foreach(object item in seq)Visit(item);return;}foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.Instance).OrderBy(f=>f.Name,StringComparer.Ordinal)){b.Append(f.Name).Append('=');Visit(f.GetValue(v));}}Visit(value);return b.ToString();
 }
 sealed class Result
 {
  public string Track{get;set;}public string Profile{get;set;}public double Q{get;set;}public uint Seed{get;set;}public int Cadence{get;set;}
  public double Time{get;set;}public int Collisions{get;set;}public int Boosts{get;set;}public int FinishBoosts{get;set;}
  public double[] Inputs{get;set;}public string Signature{get;set;}public int ForecastSteps{get;set;}public int Rollbacks{get;set;}
  public double Requested{get;set;}public double Trial{get;set;}public double Overrun{get;set;}public int SourceComparisons{get;set;}
  public IReadOnlyList<BotPlanningSample> Plans{get;set;}
 }
 static Result Run(Track track,FlightSettings flight,double q,uint seed,int cadence)
 {
  string context=track.Id+"/"+(ReferenceEquals(flight,FlightPresets.Chosen)?"Chosen":"FastFlip")+"/"+q+"/"+seed+"/"+cadence;
  var g=RaceSimulation.CreateSolo(track,flight,PartyCamera.Lagger,-288.8,1520);RaceSimulation.Press(g,0);var ordinary=RaceSnapshot.Clone(g);string trackBefore=State(track);
  var controller=new StochasticBotController(g,0,q,seed);var inputs=new List<double>();int compared=0,lastPlan=0;
  controller.Observer=s=>
  {
   if(s.Kind=="advance")RaceSimulation.Advance(ordinary,s.Delta);
   else if(s.Kind=="press"){Need(RaceSimulation.Press(ordinary,0)==s.Accepted,context+" Press replay");Need(s.Accepted,context+" rejected input");inputs.Add(s.Distance);}
   if(lastPlan!=controller.PlanningCalls){Need(State(g.Players[0])==State(ordinary.Players[0])&&g.Elapsed==ordinary.Elapsed&&g.Phase==ordinary.Phase,context+" forecast mutated source");compared++;lastPlan=controller.PlanningCalls;}
  };
  double[][] cadences={new[]{1d/30},new[]{1d/60},new[]{.005,.027,.011,.073,.001,.049,.016,.211}};int frame=0;
  while(g.Phase!="finished"&&frame<12000)controller.Advance(cadences[cadence][frame++%cadences[cadence].Length]);
  var p=g.Players[0];Need(g.Phase=="finished"&&p.Status==PlayerStatus.Finished,context+" Lagger completion");
  Need(controller.MaximumRequestedHorizon<=1000+2e-7&&controller.MaximumTrialHorizon<=1055+2e-7&&controller.MaximumTrialOvershoot<=55+2e-7,context+" horizon bounds");
  Need(controller.ForecastHorizonViolations==0&&controller.ForecastObservations==controller.ForecastSteps,context+" every trial observed");
  Need(controller.PlanningSamples.All(s=>s.Candidates<=(q==1?13:96)),context+" per-plan candidate cap");Need(State(track)==trackBefore,context+" shared geometry mutated");
  if(q==1)Need(p.Collisions==0&&p.SlowsEarned==0&&p.BoostsEarned==9&&p.CompletedBoosts==9&&p.FinishBoostsEarned==1&&p.FinishBoostElapsed>=.25-1e-8&&Math.Abs(p.FinishTime.Value-StochasticTrackMap.Create(track,flight).MasterTime)<1e-6,context+" Master reference");
  foreach(var d in controller.Decisions){bool met=d.Intent=="hit"?d.Outcome=="hit":d.Intent=="avoid"?d.Outcome=="avoided":d.Intent=="collect"?d.Outcome=="collected":d.Outcome=="missed"||d.Outcome=="failed";Need(!met||d.UnreachableReason.Length==0,context+" met intention marked unreachable");Need(met||d.Outcome=="pending"||d.UnreachableReason.Length>0,context+" actual mismatch lacks reason");}
  var r=new Result{Track=track.Id,Profile=ReferenceEquals(flight,FlightPresets.Chosen)?"Chosen":"FastFlip",Q=q,Seed=seed,Cadence=cadence,Time=p.FinishTime.Value,Collisions=p.Collisions,Boosts=p.BoostsEarned,FinishBoosts=p.FinishBoostsEarned,Inputs=inputs.ToArray(),Signature=string.Join("|",inputs.Select(x=>x.ToString("F5",CultureInfo.InvariantCulture))),ForecastSteps=controller.ForecastSteps,Rollbacks=controller.ForecastTrialRollbacks,Requested=controller.MaximumRequestedHorizon,Trial=controller.MaximumTrialHorizon,Overrun=controller.MaximumTrialOvershoot,SourceComparisons=compared,Plans=controller.PlanningSamples};
  Console.WriteLine(context+" t="+r.Time+" inputs="+r.Inputs.Length+" steps="+r.ForecastSteps+" rollback="+r.Rollbacks+" requested="+r.Requested+" actual="+r.Trial+" overrun="+r.Overrun+" maxPlanMs="+r.Plans.Max(x=>x.Milliseconds));return r;
 }
 static void Invalid(Action action,string why)
 {bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(NotSupportedException){rejected=true;}Need(rejected,why);}
 static int Factory()
 {
  const string reportPath="Tools/StochasticLocalProbe/factory-base-formula-check.json";
  if(File.Exists(reportPath))throw new IOException("Preserve existing base formula factory evidence; choose a new explicitly named run instead of overwriting");
  string[] sourcePaths={"Assets/Scripts/Core/StochasticBotSettings.cs","Assets/Scripts/Core/RaceSimulation.cs","Assets/Scripts/Core/StochasticBotController.cs","Assets/Scripts/Core/StochasticTrackMap.cs"};
  var sourceBefore=sourcePaths.ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),StringComparer.Ordinal);
  // Common formula independently checked from physical B/C metadata; no pair coefficient table.
  var seenPairs=new HashSet<string>(StringComparer.Ordinal);
  var rows=new List<object>();int cases=0;
  foreach(var field in typeof(StochasticBotSettings).GetFields(BindingFlags.Instance|BindingFlags.Public))Need(field.IsInitOnly,"settings field must be readonly: "+field.Name);
  Need(typeof(GameState).GetField("StochasticBot").IsInitOnly,"race settings reference readonly");
  foreach(var track in RaceTracks.All)foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(BotDifficulty difficulty in Enum.GetValues(typeof(BotDifficulty)))
  {
   uint seed=unchecked(0x80000000u+(uint)cases);var initial=StochasticBotSettings.CreateInitial(track,flight,difficulty,seed);var settings=StochasticBotSettings.CreateInitial(track,flight,difficulty,seed);var map=StochasticTrackMap.Create(track,flight);
   Need(initial.TrackId==track.Id&&initial.ProfileId==map.ProfileId&&initial.Seed==seed&&initial.Difficulty==difficulty,"initial metadata");
   Need(settings.TrackId==track.Id&&settings.ProfileId==map.ProfileId&&settings.Seed==seed&&settings.Difficulty==difficulty,"base formula metadata");
   double expectedInitial=difficulty==BotDifficulty.Master?0:map.BonusGain/map.CollisionCostSum*(1-initial.Quality);Need(initial.CollectProbability==initial.Quality&&initial.HitProbability==expectedInitial,"separate unchanged initial model");
   seenPairs.Add(track.Id+"/"+map.ProfileId);
   double expected=difficulty==BotDifficulty.Master?0:(map.BonusGain/map.CollisionCostSum)*(1-settings.Quality);
   Need(settings.CollectProbability==settings.Quality&&settings.HitProbability==expected,"independent exact base probability formula");
   var zero=StochasticBotSettings.CreateInitial(track,flight,difficulty,0u);
   Need(zero.Seed==0&&zero.Quality==settings.Quality&&zero.CollectProbability==settings.CollectProbability&&zero.HitProbability==settings.HitProbability,"seed does not change frozen probabilities");
   var initialRace=RaceSimulation.CreateStochasticBotRace(track,flight,initial);
   Need(ReferenceEquals(initialRace.StochasticBot,initial),"explicit initial settings factory unchanged");
   foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
   {
    var active=RaceSimulation.CreateStochasticBotRace(track,flight,difficulty,seed,camera);Need(active.StochasticBot.SameAs(settings)&&active.PartyCamera==camera,"active difficulty factory calibrated both cameras");
    var g=RaceSimulation.CreateStochasticBotRace(track,flight,settings,camera);Need(g.IsBot&&!g.IsParty&&!g.IsSolo&&g.UsesSharedRaceCamera&&g.Mode=="bot"&&g.BotPlan==null&&ReferenceEquals(g.StochasticBot,settings),"new factory union");
    Need(g.BotDifficulty==difficulty&&g.BotSeed==unchecked((int)seed)&&g.Players.Length==2&&!g.Players[0].Ready&&g.Players[1].Ready,"new factory readiness/uint seed");
    var controller=new StochasticBotController(g);Need(ReferenceEquals(controller.Settings,settings),"controller binds race metadata");Need(RaceSimulation.Press(g,0)&&g.Phase=="countdown","human readiness starts ordinary countdown");controller.Advance(3);Need(g.Phase=="racing"&&g.Elapsed==0,"ordinary countdown");
    Need(ReferenceEquals(RaceSnapshot.Clone(g).StochasticBot,settings),"snapshot shares immutable settings");cases++;
   }
   var fromDifficulty=RaceSimulation.CreateStochasticBotRace(track,flight,difficulty,seed);Need(fromDifficulty.StochasticBot.SameAs(settings)&&fromDifficulty.BotPlan==null,"difficulty factory overload");
   double collect=difficulty==BotDifficulty.Master?1:.37,hit=difficulty==BotDifficulty.Master?0:.13;var frozen=StochasticBotSettings.CreateFixed(track,flight,difficulty,seed,collect,hit);Need(frozen.Quality==settings.Quality&&frozen.CollectProbability==collect&&frozen.HitProbability==hit,"explicit fixed probabilities independent of nominal quality");
   rows.Add(new{track=track.Id,profile=settings.ProfileId,difficulty=difficulty.ToString(),seed,initialHitProbability=initial.HitProbability,settings.Quality,settings.CollectProbability,settings.HitProbability,settings.BaseTime,settings.MasterTime,settings.BonusGain,settings.CollisionCostSum});
  }
  Need(seenPairs.Count==10,"all ten supported pairs use common formula");
  var cascade=RaceTracks.Get(TracksRhythm.CascadeId);var fixedSettings=StochasticBotSettings.CreateFixed(cascade,FlightPresets.Chosen,BotDifficulty.Amateur,uint.MaxValue,.37,.13);
  var training=RaceSimulation.CreateSolo(cascade,FlightPresets.Chosen,PartyCamera.Lagger,-288.8,1520);Need(training.StochasticBot==null,"ordinary training has no bot metadata");var fixedController=new StochasticBotController(training,0,fixedSettings);RaceSimulation.Press(training,0);
  int guard=0;while(!fixedController.Decisions.Any(d=>d.Kind!=BotOpportunityKind.Obstacle)&&guard++<600)fixedController.Advance(1d/30);
  Need(fixedController.Decisions.Any(d=>d.Kind!=BotOpportunityKind.Obstacle),"fixed probability collection decision observed");foreach(var d in fixedController.Decisions)Need(d.Probability==(d.Kind==BotOpportunityKind.Obstacle?.13:.37),"runtime uses frozen probability");
  Invalid(()=>StochasticBotSettings.CreateFixed(cascade,FlightPresets.Chosen,BotDifficulty.Master,0,.99,0),"Master fixed probabilities strict");
  foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-.01,1.01}){Invalid(()=>StochasticBotSettings.CreateFixed(cascade,FlightPresets.Chosen,BotDifficulty.Novice,0,bad,0),"invalid collect probability");Invalid(()=>StochasticBotSettings.CreateFixed(cascade,FlightPresets.Chosen,BotDifficulty.Novice,0,.5,bad),"invalid hit probability");}
  Invalid(()=>StochasticBotSettings.CreateInitial(cascade,FlightPresets.Moderate,BotDifficulty.Novice,0),"custom profile unsupported");Invalid(()=>StochasticBotSettings.CreateInitial(new Track(7920,Array.Empty<Obstacle>()),FlightPresets.Chosen,BotDifficulty.Novice,0),"unknown track unsupported");Invalid(()=>StochasticBotSettings.CreateInitial(cascade,FlightPresets.Chosen,(BotDifficulty)99,0),"unknown level");
  Invalid(()=>StochasticBotSettings.CreateInitial(cascade,FlightPresets.Moderate,BotDifficulty.Novice,0),"base formula custom profile unsupported");
  Invalid(()=>StochasticBotSettings.CreateInitial(new Track(7920,Array.Empty<Obstacle>()),FlightPresets.Chosen,BotDifficulty.Novice,0),"base formula unknown track unsupported");
  Invalid(()=>StochasticBotSettings.CreateInitial(cascade,FlightPresets.Chosen,(BotDifficulty)99,0),"base formula unknown level unsupported");
  Invalid(()=>RaceSimulation.CreateStochasticBotRace(cascade,FlightPresets.FastFlip,fixedSettings),"profile mismatch rejected");Invalid(()=>RaceSimulation.CreateStochasticBotRace(RaceTracks.Get(TracksMixed.Id),FlightPresets.Chosen,fixedSettings),"track mismatch rejected");
  var g2=RaceSimulation.CreateStochasticBotRace(cascade,FlightPresets.Chosen,BotDifficulty.Amateur,7);Invalid(()=>new StochasticBotController(g2,1,fixedSettings),"controller metadata mismatch rejected");Invalid(()=>new StochasticBotController(training),"metadata ctor requires metadata");
  var legacyPlan=new BotPlan("metadata-fixture",cascade.Id,"Chosen",BotDifficulty.Strong,-17,0,"fixture",Array.Empty<double>());var legacy=new GameState(cascade,PartyCamera.Lagger,-288.8,1520,false,legacyPlan);Need(legacy.StochasticBot==null&&legacy.BotDifficulty==BotDifficulty.Strong&&legacy.BotSeed==-17,"legacy metadata fallback preserved without catalog initialization");
  var guardedSettings=StochasticBotSettings.CreateFixed(cascade,FlightPresets.Chosen,BotDifficulty.Amateur,7,.2,.4);
  var guardedRace=RaceSimulation.CreateStochasticBotRace(cascade,FlightPresets.Chosen,guardedSettings,PartyCamera.Lagger);
  string guardedBefore=State(guardedRace.Players);double guardedElapsed=guardedRace.Elapsed;string guardedPhase=guardedRace.Phase;
  Invalid(()=>new StochasticBotController(guardedRace,1,.65,999),"raw q/seed cannot override fixed race probabilities or seed");
  Need(State(guardedRace.Players)==guardedBefore&&guardedRace.Elapsed==guardedElapsed&&guardedRace.Phase==guardedPhase,"rejected raw metadata override leaves actual race untouched");
  Need(ReferenceEquals(new StochasticBotController(guardedRace).Settings,guardedSettings)&&ReferenceEquals(new StochasticBotController(guardedRace,1,guardedSettings).Settings,guardedSettings),"both metadata constructors remain available");
  Invalid(()=>new StochasticBotController(legacy,0,.65,999),"raw q/seed cannot override legacy tape metadata");
  Invalid(()=>new StochasticBotController(legacy,0,fixedSettings),"explicit settings cannot override legacy tape metadata");
  var rawTrainingRows=new List<object>();
  foreach(bool party in new[]{false,true})
  {
   var rawGame=party?RaceSimulation.CreateParty(cascade,FlightPresets.Chosen,2,PartyCamera.Lagger,-288.8,1520):RaceSimulation.CreateSolo(cascade,FlightPresets.Chosen,PartyCamera.Lagger,-288.8,1520);
   int rawSlot=party?1:0;var rawController=new StochasticBotController(rawGame,rawSlot,.65,999);
   Need(rawGame.StochasticBot==null&&rawGame.BotPlan==null&&rawController.Settings==null,"ordinary raw training remains metadata-free");
   for(int slot=0;slot<rawGame.Players.Length;slot++)Need(RaceSimulation.Press(rawGame,slot),"ordinary raw training readiness");
   int rawFrames=0;while(!rawController.Decisions.Any(d=>d.Kind!=BotOpportunityKind.Obstacle)&&rawFrames++<600)rawController.Advance(1d/30);
   Need(rawController.Decisions.Any(d=>d.Kind==BotOpportunityKind.Obstacle)&&rawController.Decisions.Any(d=>d.Kind!=BotOpportunityKind.Obstacle),"raw training observes obstacle and collection decisions");
   var rawMap=StochasticTrackMap.Create(cascade,FlightPresets.Chosen);double rawHit=rawMap.BonusGain/rawMap.CollisionCostSum*(1-.65);
   foreach(var d in rawController.Decisions)Need(d.Probability==(d.Kind==BotOpportunityKind.Obstacle?rawHit:.65),"raw training uses initial q probabilities");
   var twinGame=party?RaceSimulation.CreateParty(cascade,FlightPresets.Chosen,2,PartyCamera.Lagger,-288.8,1520):RaceSimulation.CreateSolo(cascade,FlightPresets.Chosen,PartyCamera.Lagger,-288.8,1520);
   var twinController=new StochasticBotController(twinGame,rawSlot,.65,999);for(int slot=0;slot<twinGame.Players.Length;slot++)RaceSimulation.Press(twinGame,slot);
   for(int frame=0;frame<rawFrames;frame++)twinController.Advance(1d/30);
   Need(rawController.Decisions.Count==twinController.Decisions.Count,"raw training seed replay count");
   for(int i=0;i<rawController.Decisions.Count;i++){var a=rawController.Decisions[i];var b=twinController.Decisions[i];Need(a.Id==b.Id&&a.Intent==b.Intent&&a.Roll==b.Roll&&a.Probability==b.Probability,"raw training preserves q/seed choices");}
   rawTrainingRows.Add(new{mode=party?"party":"solo",q=.65,seed=999u,decisions=rawController.Decisions.Count,frames=rawFrames});
  }
  var sourceAfter=sourcePaths.ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),StringComparer.Ordinal);Need(sourceBefore.All(kv=>sourceAfter[kv.Key]==kv.Value),"factory-check source stable");
  using var reportStream=new FileStream(reportPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read);using var reportWriter=new StreamWriter(reportStream,new UTF8Encoding(false));reportWriter.Write(JsonSerializer.Serialize(new{status="PASS",checks,metadataCombinations=rows.Count,cameraCases=cases,usesAdditionalTrackProfileCoefficients=false,activeDifficultyFactoryUsesBaseFormula=true,sourceStable=true,sourceBefore,sourceAfter,probabilityModel="pCollect=q; pHit=(B/C)*(1-q); Master1/0",rawMetadataOverrideRejected=true,legacyMetadataOverrideRejected=true,rawTrainingRows,settingsSourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Assets/Scripts/Core/StochasticBotSettings.cs"))),raceSourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Assets/Scripts/Core/RaceSimulation.cs"))),controllerSourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Assets/Scripts/Core/StochasticBotController.cs"))),rows},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS factory checks="+checks+" combinations="+rows.Count+" cameraCases="+cases);return 0;
 }
 static int Main(string[] args)
 {
  if(args.Contains("--suppression-equivalence"))return SuppressionEquivalence.Run();
  if(args.Contains("--rejected-input-audit"))return RejectedInputCheck.Run(true);
  if(args.Contains("--rejected-input-repro"))return RejectedInputCheck.Run();
  if(args.Contains("--intent-witnesses")){int input=Array.IndexOf(args,"--intent-witnesses")+1;if(input>=args.Length)throw new ArgumentException("Witness JSON path required");return IntentWitnessCheck.Run(args[input]);}
  if(args.Contains("--master-all-pairs-clippingfix"))return MasterCoverage.RunAll();
  if(args.Contains("--master-clipping-target"))return MasterCoverage.RunAll(true);
  if(args.Contains("--master-short-fixed"))return ShortMasterCheck.Run(true);
  if(args.Contains("--master-short-repro"))return ShortMasterCheck.Run();
  if(args.Contains("--factory"))return Factory();
  bool variation=args.Contains("--master-variation");var results=new List<Result>();string path="Assets/Scripts/Core/StochasticBotController.cs",hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));var watch=Stopwatch.StartNew();
  foreach(var track in new[]{RaceTracks.Get(TracksRhythm.CascadeId),RaceTracks.Get(TracksMixed.Id)})foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
  {
   if(variation)
   {
    for(uint seed=0;seed<12;seed++)
    {
     Result baseline=null;for(int frame=0;frame<3;frame++){var r=Run(track,flight,1,seed,frame);results.Add(r);if(baseline==null)baseline=r;else{Need(r.Inputs.Length==baseline.Inputs.Length,"cadence input count");for(int i=0;i<r.Inputs.Length;i++)Need(Math.Abs(r.Inputs[i]-baseline.Inputs[i])<5e-5,"cadence input distance");}}
    }
    var pool=results.Where(r=>r.Track==track.Id&&r.Profile==(ReferenceEquals(flight,FlightPresets.Chosen)?"Chosen":"FastFlip")&&r.Cadence==0).ToArray();Need(pool.Select(r=>r.Signature).Distinct().Count()>=2,"No actual Master input diversity: "+track.Id+"/"+pool[0].Profile);
   }
   else foreach(double q in new[]{1d,.5})results.Add(Run(track,flight,q,0,0));
  }
  Need(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))==hash,"source changed");var times=results.SelectMany(r=>r.Plans.Select(p=>p.Milliseconds)).OrderBy(x=>x).ToArray();
  var report=new{status="PASS",sourceHash=hash,sourceStable=true,checks,elapsedMilliseconds=watch.Elapsed.TotalMilliseconds,declaredSeeds=variation?Enumerable.Range(0,12).ToArray():new[]{0},declaredCadences=variation?new[]{0,1,2}:new[]{0},environment="Managed desktop; all Plan durations retained, no WebGL latency claim",planCount=times.Length,planP95Milliseconds=times[(int)Math.Ceiling(times.Length*.95)-1],planMaximumMilliseconds=times.Last(),variation=results.Where(r=>r.Q==1&&r.Cadence==0).GroupBy(r=>r.Track+"/"+r.Profile).Select(g=>new{pair=g.Key,actualInputSignatures=g.Select(r=>r.Signature).Distinct().Count()}),cases=results};
  File.WriteAllText(variation?"Tools/StochasticLocalProbe/master-variation.json":"Tools/StochasticLocalProbe/performance-preflight.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS checks="+checks+" cases="+results.Count+" plans="+times.Length+" PlanP95="+report.planP95Milliseconds+" PlanMax="+report.planMaximumMilliseconds);return 0;
 }
}