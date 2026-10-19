using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static int moduleRuns;
    static readonly double[] ModuleSteps={1d/30,1d/60,1d/144,.317,2.5,100};
    sealed class ModuleRun
    {
        public GameState Game;
        public bool Good, Accepted=true, SupportedFast=true;
        public double EntryTime, Duration;
        public List<double> Times=new List<double>();
        public List<InputSample> Inputs=new List<InputSample>();
        public string Error;
    }
    static Track ShiftModule(Track source,double dx,bool mirror)
    {
        var obstacles=new List<Obstacle>();var zones=new List<RiskZone>();
        foreach(var o in source.Obstacles){var c=new Obstacle();
            foreach(var f in typeof(Obstacle).GetFields(BindingFlags.Public|BindingFlags.Instance))f.SetValue(c,f.GetValue(o));
            c.X+=dx;if(mirror){
                if(c.Kind=="gate"){c.GapTop=240-o.GapBottom;c.GapBottom=240-o.GapTop;c.TopOffsetX=o.BottomOffsetX;c.BottomOffsetX=o.TopOffsetX;}
                else if(c.Kind=="floor")c.Kind="ceiling";else if(c.Kind=="ceiling")c.Kind="floor";}
            obstacles.Add(c);}
        foreach(var z in source.RiskZones){var c=new RiskZone();
            foreach(var f in typeof(RiskZone).GetFields(BindingFlags.Public|BindingFlags.Instance))f.SetValue(c,f.GetValue(z));
            c.X+=dx;if(c.ClearX!=0)c.ClearX+=dx;if(c.SafeFlipX!=0)c.SafeFlipX+=dx;if(c.RiskFlipX!=0)c.RiskFlipX+=dx;
            if(mirror)c.Y=240-c.Y;zones.Add(c);}
        return new Track(source.Length+dx,obstacles.ToArray()){Id=source.Id,RiskZones=zones.ToArray()};
    }
    static ModuleRun ModuleReplay(Track source,FlightSettings flight,List<double> inputs,int endSide,bool full,bool mirror,double step,
        int changed=-1,double shift=0,List<double> times=null,int? expectedBoosts=null)
    {
        moduleRuns++;const double padding=500;var r=new ModuleRun{Game=Begin(ShiftModule(source,padding,mirror),flight)};
        try{
            if(mirror){Tap(r.Game);if(flight==FlightPresets.FastFlip)AdvanceTime(r.Game,.5,step);
                else{AdvanceTime(r.Game,350/662.5,step);Tap(r.Game);AdvanceTime(r.Game,.7,step);}}
            MoveTo(r.Game,padding,step);r.EntryTime=r.Game.Elapsed;
            foreach(var p in r.Game.Players)Check(p.Support==SupportKind.Wall&&p.SupportSide==(mirror?0:1)&&p.Collisions==0,"real clean module entry");
            for(int i=0;i<inputs.Count;i++){
                if(i==changed)AdvanceTime(r.Game,r.EntryTime+times[i]+shift-r.Game.Elapsed,step);
                else MoveTo(r.Game,padding+inputs[i],step);
                var p=r.Game.Players[0];r.Times.Add(r.Game.Elapsed-r.EntryTime);
                r.Inputs.Add(new InputSample{X=p.Distance-padding,Time=r.Game.Elapsed-r.EntryTime,CenterY=RaceSimulation.GetPlayerCenterY(p),
                    VerticalSpeed=p.VerticalSpeed,Airborne=p.Flipping,AirFlipAvailable=p.AirFlipAvailable,Support=p.Support,SupportSide=p.SupportSide,SupportObstacleId=p.SupportObstacleId});
                if(flight==FlightPresets.FastFlip&&p.Support==SupportKind.None)r.SupportedFast=false;
                bool a=RaceSimulation.Press(r.Game,0),b=RaceSimulation.Press(r.Game,1);r.Accepted&=a&&b;
            }
            MoveTo(r.Game,padding+source.Length-20,step);r.Duration=r.Game.Elapsed-r.EntryTime;
            int expected=expectedBoosts??(full?source.RiskZones.Count(z=>!z.IsNegative&&!z.IsFinish):0);
            r.Good=r.Accepted&&r.SupportedFast&&r.Game.Players.All(p=>p.Collisions==0&&p.BoostsEarned==expected&&p.SlowsEarned==0
                &&p.Support==SupportKind.Wall&&p.SupportSide==(mirror?1-endSide:endSide)&&!p.Flipping
                &&p.ActiveEffect==null&&p.PendingEffects.Count==0&&p.CompletedBoosts==expected);
            if(r.Good)Near(r.Duration,(source.Length-20)/220-.3*expected,"module actual end time",1e-8);
        }catch(Exception ex){r.Error=ex.Message;r.Good=false;}
        return r;
    }
    static void ChainSubsets(bool r6=false)
    {
        var json=JsonDocument.Parse(File.ReadAllText(r6?"Verification/rhythm-modules-r6.json":"Verification/rhythm-modules-compact.json"));
        var chain=json.RootElement.EnumerateArray().First(x=>x.GetProperty("kind").GetString()=="chain");
        var track=RefinedModule("chain");track.Length=chain.GetProperty("endX").GetDouble()+20;
        if(r6){track.Obstacles.Single(o=>o.Id=="chain-gate-2").X+=30;track.RiskZones.Single(z=>z.Id=="chain-2").X+=30;}
        foreach(var f in new[]{FlightPresets.FastFlip,FlightPresets.Chosen}){
            string profile=f==FlightPresets.FastFlip?"fast-flip":"chosen";
            var routes=chain.GetProperty("routes").EnumerateArray().Where(x=>x.GetProperty("profile").GetString()==profile).ToArray();
            var safe=routes.First(x=>!x.GetProperty("full").GetBoolean()).GetProperty("inputs").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
            var full=routes.First(x=>x.GetProperty("full").GetBoolean()).GetProperty("inputs").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
            int half=full.Length/2;
            foreach(int first in new[]{0,1})foreach(int second in new[]{0,1}){
                var inputs=(first==1?full:safe).Take(half).Concat((second==1?full:safe).Skip(half)).ToList();
                foreach(bool mirror in new[]{false,true})foreach(double step in ModuleSteps){
                    var run=ModuleReplay(track,f,inputs,1,true,mirror,step,expectedBoosts:first+second);
                    Check(run.Good,"independent chain route "+profile+" "+first+second+" "+run.Error);
                    foreach(var p in run.Game.Players){
                        Check(p.SuccessfulRiskIds.Contains("chain-1")== (first==1),"first ring independent");
                        Check(p.SuccessfulRiskIds.Contains("chain-2")== (second==1),"second ring independent");}
                }
            }
        }
        Console.WriteLine($"PASS chain subsets: {checks} assertions / {moduleRuns} actual runs; both mirrors, two profiles, all4 reward subsets, six dt, complete effects.");
    }
    static double LatestRelease(Track t,RiskZone z)
    {
        double x=z.IsLanding?z.X+z.LandingWidth/2:z.X+z.Radius+14;
        foreach(var o in t.Obstacles)if(o.Id==z.ObstacleId)x=z.IsLanding?Math.Min(x,ObstacleGeometry.Right(o))
            :Math.Max(x,Math.Max(z.ClearX,ObstacleGeometry.Right(o)+14+1e-7));
        return x;
    }
    static void ExportModules(string[] args)
    {
        var old=JsonDocument.Parse(File.ReadAllText("Verification/rhythm-module-author-search.json"));
        bool r6=args.Contains("--r6-shifts"),recoveryBefore=args.Contains("--before400")||r6,beforeRevised=args.Contains("--before-refined")||recoveryBefore;
        var refined=JsonDocument.Parse(File.ReadAllText(recoveryBefore?"Verification/rhythm-module-before400-candidates.json":beforeRevised?"Verification/rhythm-module-refined-before-candidates.json":"Verification/rhythm-module-refined-candidates.json"));
        var output=new List<object>();moduleRuns=0;
        foreach(string kind in new[]{"gate","free","free-high","before","after","chain","pad","large"}){
            bool revised=(kind=="before"&&beforeRevised)||kind=="after"||kind=="chain";
            var track=revised?RefinedModule(kind,recoveryBefore):Module(kind);
            if(r6&&kind=="before")track.Obstacles.Single(o=>o.Id=="exit-gate").X+=20;
            if(r6&&kind=="chain"){track.Obstacles.Single(o=>o.Id=="chain-gate-2").X+=30;track.RiskZones.Single(z=>z.Id=="chain-2").X+=30;}
            if(args.Contains("--compact")){
                var compactEnds=new Dictionary<string,double>{{"gate",args.Contains("--gate250")?250:270},{"free",350},{"free-high",355},{"before",r6?610:recoveryBefore?590:beforeRevised?520:480},{"after",580},{"chain",r6?720:690},{"pad",460},{"large",args.Contains("--large580")?580:600}};
                track.Length=compactEnds[kind]+20;
            }
            int endSide=kind=="gate"||kind.StartsWith("free")||(kind=="before"&&!beforeRevised)?0:1;
            double endX=track.Length-20,workEnd=0;
            foreach(var z in track.RiskZones.OrderBy(z=>LatestRelease(track,z)))workEnd=Math.Max(workEnd,LatestRelease(track,z))+.6*330;
            Check(workEnd<endX,"universal all-late work ends before module boundary: "+kind);
            var routes=new List<object>();
            foreach(var f in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(bool full in new[]{false,true}){
                string profile=f==FlightPresets.FastFlip?"fast-flip":"chosen";
                var row=revised
                    ?refined.RootElement.EnumerateArray().First(e=>e.GetProperty("kind").GetString()==kind&&e.GetProperty("profile").GetString()==profile&&e.GetProperty("full").GetBoolean()==full)
                    :old.RootElement.EnumerateArray().First(e=>e.GetProperty("kind").GetString()==kind&&e.GetProperty("name").GetString()==(f==FlightPresets.FastFlip?"Fast":"Main")+(full?" full":" safe"));
                var inputs=row.GetProperty("route").EnumerateArray().Select(x=>x.GetDouble()).ToList();
                if(r6&&(kind=="before"||kind=="chain"))for(int i=inputs.Count/2;i<inputs.Count;i++)inputs[i]+=kind=="before"?20:30;
                var baseline=ModuleReplay(track,f,inputs,endSide,full,false,.01);Check(baseline.Good,"module baseline "+kind+"/"+profile+"/"+full+" "+baseline.Error);
                var windows=new List<double>();
                for(int i=0;i<inputs.Count;i++){
                    double radius=0;for(int n=1;n<=30;n++){double d=n*.005;
                        if(!ModuleReplay(track,f,inputs,endSide,full,false,.2,i,-d,baseline.Times).Good||!ModuleReplay(track,f,inputs,endSide,full,false,.2,i,d,baseline.Times).Good)break;radius=d;}
                    windows.Add(radius);
                    Check(radius+1e-10>=(full?.025:.06),"required actual clock window "+kind+"/"+profile+"/"+full+" input"+i+" radius"+radius);
                }
                foreach(bool mirror in new[]{false,true})foreach(double step in ModuleSteps){
                    var actual=ModuleReplay(track,f,inputs,endSide,full,mirror,step);Check(actual.Good,"mirrored six-step module "+kind+"/"+profile+"/"+full+" "+actual.Error);
                    for(int i=0;i<inputs.Count;i++)foreach(double sign in new[]{-1d,1})
                        Check(ModuleReplay(track,f,inputs,endSide,full,mirror,step,i,sign*(full?.025:.06),actual.Times).Good,"mirrored window "+kind+"/"+profile+"/"+full+" input"+i);
                }
                var omissions=new List<object>();
                for(int i=0;i<inputs.Count;i++){
                    var smaller=new List<double>(inputs);smaller.RemoveAt(i);var omitted=ModuleReplay(track,f,smaller,endSide,full,false,.317);
                    Check(!omitted.Good,"input needed for clean reward/end-support contract "+kind+"/"+profile+"/"+full+" input"+i);
                    var p=omitted.Game.Players[0];omissions.Add(new{index=i,accepted=omitted.Accepted,supportedFast=omitted.SupportedFast,hits=p.Collisions,rewards=p.BoostsEarned,
                        support=p.Support.ToString(),endSide=p.SupportSide,empty=p.ActiveEffect==null&&p.PendingEffects.Count==0});
                }
                routes.Add(new{profile,full,inputs,actualTimes=baseline.Times,windowsSeconds=windows,samples=baseline.Inputs,duration=baseline.Duration,emptyEffects=true,omissions});
                Console.WriteLine($"{kind}/{profile}/{(full?"full":"safe")} PASS; inputs={inputs.Count}; minimum window={windows.DefaultIfEmpty(.15).Min()*1000:F0}ms");
            }
            output.Add(new{kind,length=endX,endX,endSide,entrySide=1,entrySpeed=220,entryEffects="empty",latestAllWorkEndX=workEnd,
                obstacles=track.Obstacles,zones=track.RiskZones,routes});
        }
        string path=args.Length>1?args[1]:"Verification/rhythm-modules.json";
        File.WriteAllText(path,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine($"PASS module export: {checks} assertions / {moduleRuns} actual runs; {path}");
    }
}
