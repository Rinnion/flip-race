using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlipRace.Core;
internal static partial class Program
{
 static int runs; static string outputTag=""; static bool replay;
 static readonly Stopwatch Budget = Stopwatch.StartNew();
 static readonly DateTime Deadline = new(2026,10,31,18,58,52,DateTimeKind.Utc);
 static void Guard(){if((!replay&&DateTime.UtcNow>=Deadline)||Budget.Elapsed.TotalSeconds>30)throw new Exception("Hard probe deadline reached");}
 sealed class Result {
  public string profile,error=""; public bool clean; public int collisions,rejected,earned,completed;
  public double? finish; public int wall; public string support; public double gain;
  public List<InputSample> inputs=new(); public List<double> speeds=new(); public List<object> landings=new(),hits=new(),trace=new();
  public string[] hitIds; public double? rewardAt; [JsonIgnore] public GameState game;
 }
 static GameState Begin(Track t,FlightSettings f){var g=RaceSimulation.CreateTrial(t,f);RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);RaceSimulation.Advance(g,3);return g;}
 static Obstacle Cross(double x,int side,string id)=>new(){Id=id,Kind="gate",X=x,Width=16,GapTop=110,GapBottom=130,TopOffsetX=side==1?0:112,BottomOffsetX=side==1?112:0};
 static Obstacle Low(double x,int side,string id)=>new(){Id=id,Kind="gate",X=x,Width=16,GapTop=side==1?114:16,GapBottom=side==1?224:126,TopOffsetX=side==1?80:0,BottomOffsetX=side==1?0:80};
 static Track Geometry()=>new(1380,new[]{Cross(200,1,"cross-a"),Low(520,0,"low-b"),Cross(840,0,"cross-c"),Low(1160,1,"low-d")}){Id="dense-probe-v1"};
 static Track Parallel()=>new(1420,Enumerable.Range(0,6).Select(i=>new Obstacle{Id="pair-"+i,Kind="gate",X=200+i*200,Width=16,GapTop=48,GapBottom=192}).ToArray()){Id="dense-probe-v2-parallel"};
 static double[] Inputs(FlightSettings f)=>f==FlightPresets.Chosen?new[]{180.8448773141309,224.8448773141309,411.77358490566036,820.8448773141309,864.8448773141309,1051.7735849056603}:new[]{205.1503213562917,460d,493d,845.1503213562917,1100d,1133d};
 static object Pose(GameState g)=>new{x=g.Players[0].Distance,time=g.Elapsed,y=RaceSimulation.GetPlayerCenterY(g.Players[0]),vy=g.Players[0].VerticalSpeed,side=g.Players[0].SupportSide,support=g.Players[0].Support.ToString()};
 static void Tick(GameState g,double dt,Result r,bool trace){var p=g.Players[0];bool air=p.Flipping;int hit=p.Collisions;Step(g,dt);if(air&&!p.Flipping&&p.Support!=SupportKind.None)r.landings.Add(Pose(g));if(p.Collisions>hit)r.hits.Add(new{pose=Pose(g),id=p.LastHit?.Id,recovering=p.Recovering});if(trace&&(r.trace.Count==0||r.trace.Count*.025<g.Elapsed))r.trace.Add(Pose(g));}
 static void GoX(GameState g,double x,Result r,bool trace){var p=g.Players[0];Check(x>=p.Distance-1e-7,"ordered distance");while(p.Distance<x-1e-8&&g.Phase=="racing"){if(p.Recovering>0){Tick(g,Math.Min(.005,p.Recovering),r,trace);continue;}if(x-p.Distance<=440*.005+1e-8){MoveTo(g,x,.005);break;}double dt=.005;if(p.ActiveEffect!=null)dt=Math.Min(dt,p.ActiveEffect.Remaining);if(dt<1e-10){MoveTo(g,x,.005);break;}Tick(g,dt,r,trace);}Near(p.Distance,x,"target",1e-6);}
 static void GoTime(GameState g,double t,Result r,bool trace){Check(t>=g.Elapsed-1e-8,"ordered time");while(g.Elapsed<t-1e-10&&g.Phase=="racing")Tick(g,Math.Min(.005,t-g.Elapsed),r,trace);}
 static Result Run(Track t,FlightSettings f,double[] inputs,int omit=-1,int shifted=-1,double shift=0,double[] times=null,bool trace=false,bool allTimes=false){
  Guard();runs++;var g=Begin(t,f);var r=new Result{profile=f==FlightPresets.Chosen?"Chosen":"FastFlip",game=g};
  try{for(int i=0;i<inputs.Length;i++){if(i==omit)continue;if(allTimes)GoTime(g,inputs[i],r,trace);else if(i==shifted)GoTime(g,times[i]+shift,r,trace);else GoX(g,inputs[i],r,trace);var p=g.Players[0];r.inputs.Add(new(){X=p.Distance,Time=g.Elapsed,CenterY=RaceSimulation.GetPlayerCenterY(p),VerticalSpeed=p.VerticalSpeed,Airborne=p.Flipping,AirFlipAvailable=p.AirFlipAvailable,Support=p.Support,SupportSide=p.SupportSide});r.speeds.Add(p.CurrentSpeed);bool a=RaceSimulation.Press(g,0),b=RaceSimulation.Press(g,1);if(!a||!b)r.rejected++;}while(g.Phase=="racing")Tick(g,.005,r,trace);}catch(Exception e){r.error=e.Message;}
  var q=g.Players[0];r.collisions=q.Collisions;r.finish=q.FinishTime;r.wall=q.SupportSide;r.support=q.Support.ToString();r.clean=r.error==""&&r.rejected==0&&q.Collisions==0&&q.Status==PlayerStatus.Finished;r.hitIds=q.HitObstacleIds.ToArray();r.earned=q.InstantBoostsEarned;r.completed=q.CompletedBoosts;r.gain=q.BoostDistanceGained;r.rewardAt=q.LastInstant?.At;return r;
 }
 static object GeometryData(Track t)=>new{t.Id,t.Length,obstacles=t.Obstacles.Select(o=>new{o.Id,o.Kind,o.X,o.Width,o.GapTop,o.GapBottom,o.TopOffsetX,o.BottomOffsetX})};
 static object[] Windows(Track t,FlightSettings f,double[] input,Result baseline){var times=baseline.inputs.Select(i=>i.Time).ToArray();return Enumerable.Range(0,input.Length).Select(i=>(object)new{edge=i+1,samples=new[]{-.06,-.04,-.02,.02,.04,.06}.Select(d=>new{seconds=d,result=Run(t,f,input,shifted:i,shift:d,times:times)}).ToArray()}).ToArray();}
 static void Save(string name,object data){var path="Verification/dense-chains-probe-"+name+outputTag+".json";if(File.Exists(path))throw new Exception("Preserve "+path);File.WriteAllText(path,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));}
  static void Main(string[] args){
  replay=args.Contains("--replay");outputTag=args.FirstOrDefault(x=>x.StartsWith("--tag="))?.Substring(6)??"";
  if(outputTag.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))throw new Exception("Tag must be alphanumeric/hyphen");
  if(args.Contains("--recovery")){
   var rows=new List<object>();
   foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}){
    var route=Inputs(f);var n=Run(Geometry(),f,route);int edge=f==FlightPresets.Chosen?1:2;double shift=f==FlightPresets.Chosen?.30:-.06;
    var bad=Run(Geometry(),f,route,shifted:edge,shift:shift,times:n.inputs.Select(x=>x.Time).ToArray(),trace:true);
    rows.Add(new{profile=n.profile,edge=edge+1,shift,meaning="Attempt continuation using remaining distance-triggered nominal tape after real recovery",result=bad});
   }
   Save("recovery",new{rows,runs,wallMilliseconds=Budget.Elapsed.TotalMilliseconds});
   Console.WriteLine($"Recovery witnesses: {rows.Count}; runs={runs}");return;
  }
  if(args.Contains("--entrances")){
   var rows=new List<object>();var t=Parallel();
   foreach(bool ceiling in new[]{false,true})foreach(int changeAfter in new[]{-1,0,2,4}){
    var tape=new List<double>();if(ceiling)tape.Add(0);for(int i=0;i<6;i++){
     double offset=i>changeAfter&&changeAfter>=0?-55:-70;
     tape.Add(200+i*200+offset);tape.Add(233+i*200+offset);
     if(i==changeAfter)tape.Add(250+i*200);
    }
    var result=Run(t,FlightPresets.FastFlip,tape.ToArray(),trace:true);
    rows.Add(new{entrance=ceiling?"ceiling, via legal flip at X0":"floor",changeAfter,meaning=changeAfter<0?"same-wall return tape":"one extra full flip between pairs, followed by same-wall tape",result});
    Console.WriteLine($"entrance={(ceiling?0:1)} switch={changeAfter}: clean={result.clean}, hits={result.collisions}, rejected={result.rejected}");
   }
   Save("entrances",new{geometry=GeometryData(t),rows,runs,wallMilliseconds=Budget.Elapsed.TotalMilliseconds});return;
  }
  if(args.Contains("--parallel")){var t=Parallel();double apex=40d/220+350/662.5;var route=new List<double>{40d/220,apex};for(double tm=apex+.2;tm<1420d/220;tm+=.4)route.Add(tm);var center=Run(t,FlightPresets.Chosen,route.ToArray(),trace:true,allTimes:true);var switches=new List<object>();foreach(double cut in new[]{.9,3.1,5.3}){var tape=route.Where(x=>x<cut).ToArray();switches.Add(new{cut,meaning="Stop center gravity oscillation at start/middle/end; unadapted coast is only one finite attempt",result=Run(t,FlightPresets.Chosen,tape,allTimes:true,trace:true)});}Save("parallel",new{geometry=GeometryData(t),center,scriptedSwitchAttempts=switches,runs,wallMilliseconds=Budget.Elapsed.TotalMilliseconds});Console.WriteLine($"parallel clean={center.clean} hits={center.collisions} inputs={center.inputs.Count}; runs={runs}");return;}
  var geometry=Geometry();var profiles=new[]{FlightPresets.Chosen,FlightPresets.FastFlip};var nominal=new List<object>();var omissions=new List<object>();var windows=new List<object>();var errors=new List<object>();var pulse=new List<object>();
  foreach(var f in profiles){var input=Inputs(f);var baseline=Run(geometry,f,input,trace:true);Check(baseline.clean,"nominal clean");nominal.Add(baseline);omissions.Add(new{profile=baseline.profile,rows=Enumerable.Range(0,6).Select(i=>new{omitted=i+1,result=Run(geometry,f,input,omit:i)}).ToArray()});windows.Add(new{profile=baseline.profile,rows=Windows(geometry,f,input,baseline)});
   foreach(double shift in new[]{-.15,.15})errors.Add(new{profile=baseline.profile,shift,result=Run(geometry,f,input,shifted:1,shift:shift,times:baseline.inputs.Select(i=>i.Time).ToArray(),trace:true)});
   var boosted=Geometry();boosted.RiskZones=new[]{new RiskZone{Id="real-pulse",Kind="instant",X=70,Y=226,Radius=6}};var original=Run(boosted,f,input);Result found=original;double[] chosen=input;int attempts=1;
   if(!found.clean){bool done=false;for(double launch=130;launch<=230&&!done;launch+=2){if(f==FlightPresets.FastFlip){var trial=(double[])input.Clone();trial[0]=launch;var r=Run(boosted,f,trial);attempts++;if(r.clean&&r.earned==1){found=r;chosen=trial;done=true;}}else for(double delta=24;delta<=100;delta+=4){var trial=(double[])input.Clone();trial[0]=launch;trial[1]=launch+delta;var r=Run(boosted,f,trial);attempts++;if(r.clean&&r.earned==1){found=r;chosen=trial;done=true;break;}}}}
   var pulseWindows=found.clean?Windows(boosted,f,chosen,found):Array.Empty<object>();pulse.Add(new{profile=baseline.profile,original,adapted=found,chosen,attempts,windows=pulseWindows});Console.WriteLine($"{baseline.profile}: clean={baseline.clean}; pulse={found.clean} earned={found.earned} search={attempts}");
  }
  Save("witnesses",new{geometry=GeometryData(geometry),nominal,omissions,windows,earlyLateErrorsAndContinuation=errors,realPulse=pulse,runs,checks,wallMilliseconds=Budget.Elapsed.TotalMilliseconds});Console.WriteLine($"runs={runs} costMs={Budget.Elapsed.TotalMilliseconds:F1}");
 }
}


