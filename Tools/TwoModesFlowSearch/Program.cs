using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
 static double T(double d) => (Math.Sqrt(1225+5400*d)-35)/2700;
 static GameState Begin(Track track)
 {var g=RaceSimulation.CreateTrial(track,FlightPresets.FastFlip);RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);RaceSimulation.Advance(g,3);return g;}
 static Track Track() => TracksFull.Get(TracksFull.FlowId);
 static readonly double[] Full={700-220*T(106),850,1182-220*T(164),1900-220*T(106),2280,2500-220*T(186),2904-110*(T(12)+T(200)),3060,3318,3450,3670,4000-220*T(120),4420,5032-220*T(164),5550,5775,6150,6705,7120,7876-220*T(166)};
 static readonly double[] Safe={1400,1915,2904-110*(T(12)+T(200)),6705,7120};
 static double[] Negative(){var list=new List<double>(Full);list.RemoveAt(list.Count-1);list.Add(7600-220*T(186));list.Add(7876-220*T(46));return list.ToArray();}
 static RouteResult Run(Track track,double[] route,double step,bool verbose=false)
 {var r=new RouteResult{Game=Begin(track)};
  foreach(double x in route){MoveTo(r.Game,x,step);var p=r.Game.Players[0];if(verbose)Console.WriteLine($"input x={x:R} t={r.Game.Elapsed:R} y={RaceSimulation.GetPlayerCenterY(p):F3} support={p.Support}/{p.SupportSide} v={p.VerticalSpeed:F3} +{p.BoostsEarned} -{p.SlowsEarned} hits={string.Join(',',p.HitObstacleIds)}");Tap(r.Game,r,true);}
  Finish(r.Game,step);return r;
 }
 static void Show(RouteResult r)
 {var p=r.Game.Players[0];Console.WriteLine($"finish={p.FinishTime:R} +{p.BoostsEarned} finish+{p.FinishBoostsEarned} -{p.SlowsEarned} hits={string.Join(',',p.HitObstacleIds)} collected={string.Join(',',p.SuccessfulRiskIds)}");}
 static bool ShiftClean(Track t,double[] route,RouteResult basis,int index,double seconds,int positives)
 {
  try{var r=new RouteResult{Game=Begin(t)};
   for(int i=0;i<route.Length;i++) {
    if(i==index)AdvanceTime(r.Game,basis.Inputs[i].Time+seconds-r.Game.Elapsed,.03);else MoveTo(r.Game,route[i],.03);
    Tap(r.Game,r,true);
   }
   Finish(r.Game,.1);var p=r.Game.Players[0];return p.Collisions==0&&p.SlowsEarned==0&&p.BoostsEarned+p.FinishBoostsEarned==positives;
  }catch(Exception){return false;}
 }
 static double Window(Track t,double[] route,RouteResult basis,int index,int sign,int positives)
 {double lo=0,hi=.15;for(int j=0;j<17;j++){double mid=(lo+hi)/2;if(ShiftClean(t,route,basis,index,sign*mid,positives))lo=mid;else hi=mid;}return sign*lo;}
 static void Export(Track t)
 {
  var full=Run(t,Full,.01);var safe=Run(t,Safe,.01);var rows=new List<object>();
  foreach(var data in new[]{(name:"safe",route:Safe,result:safe,n:0),(name:"full",route:Full,result:full,n:10)})
   for(int i=0;i<data.route.Length;i++) {double early=Window(t,data.route,data.result,i,-1,data.n),late=Window(t,data.route,data.result,i,1,data.n);
    Console.WriteLine($"{data.name} {i+1}: x={data.route[i]:R} time={data.result.Inputs[i].Time:R} window=[{early*1000:F3},{late*1000:F3}]ms");
    rows.Add(new{route=data.name,index=i,x=data.route[i],time=data.result.Inputs[i].Time,early,late});}
  Directory.CreateDirectory("Tools/TwoModesFlowSearch/Artifacts");
  File.WriteAllText("Tools/TwoModesFlowSearch/Artifacts/flow-routes.json",JsonSerializer.Serialize(new{safe=Safe,full=Full,negative=Negative(),windows=rows,inputs=full.Inputs},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
 }
 static void Main(string[] args)
 {CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;captureCollision=false;var t=Track();
  if(args.Length>0 && args[0]=="--verify") {
   foreach(double dt in new[]{1d/30,1d/60,1d/144,.317,2.5,100}) {CheckClean(Run(t,Full,dt),10,0,33.2);CheckClean(Run(t,Safe,dt),0,0,36);CheckClean(Run(t,Negative(),dt),10,1,33.5);}
   var basis=Run(t,Safe,.01);for(int i=0;i<Safe.Length;i++)foreach(double shift in new[]{-.06,.06})Check(ShiftClean(t,Safe,basis,i,shift,0),"safe individual ±60ms");
   basis=Run(t,Full,.01);for(int i=0;i<Full.Length;i++)foreach(double sign in new[]{-1d,1d})Check(ShiftClean(t,Full,basis,i,sign*(i==Full.Length-1?.008:.05),10),"full ordinary ±50ms / finish ±8ms");
   Console.WriteLine("FLOW PASS "+checks+" checks; 6dt safe36/full33.2/negative33.5; safe ±60ms, ordinary full ±50ms, finish ±8ms.");return;
  }
  if(args.Length>0&&args[0]=="--export"){Export(t);return;}
  Show(Run(t,args.Length>0&&args[0]=="safe"?Safe:args.Length>0&&args[0]=="negative"?Negative():Full,.01,true));
 }
}
