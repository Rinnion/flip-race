using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    public sealed class DraftInput{public double X{get;set;}public int Module{get;set;}public int Group{get;set;}public bool Finish{get;set;}}
    public sealed class DraftRoute{public string profile{get;set;}public string variant{get;set;}public DraftInput[] route{get;set;}}
    public sealed class DraftModule{public string id{get;set;}public string kind{get;set;}public double x{get;set;}public double endX{get;set;}public int EntrySide{get;set;}public int EndSide{get;set;}}
    public sealed class DraftCourse{public string id{get;set;}public string name{get;set;}public Track track{get;set;}public DraftModule[] modules{get;set;}public DraftRoute[] routes{get;set;}}
    static readonly JsonSerializerOptions CourseJson=new JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true,WriteIndented=true};
    sealed class CourseResult{public GameState Game;public List<double> Times=new List<double>();public List<InputSample> Samples=new List<InputSample>();public bool Accepted=true;}
    static CourseResult RunCourse(Track track,FlightSettings f,double[] inputs,double step,int changed=-1,double shift=0,List<double> times=null)
    {
        runs++;var r=new CourseResult{Game=RaceSimulation.CreateTrial(track,f)};Tap(r.Game);RaceSimulation.Advance(r.Game,3);
        for(int i=0;i<inputs.Length;i++){
            if(i==changed)AdvanceTime(r.Game,times[i]+shift-r.Game.Elapsed,step);else MoveTo(r.Game,inputs[i],step);
            r.Times.Add(r.Game.Elapsed);var p=r.Game.Players[0];r.Samples.Add(new InputSample{X=p.Distance,Time=r.Game.Elapsed,CenterY=RaceSimulation.GetPlayerCenterY(p),
                VerticalSpeed=p.VerticalSpeed,Airborne=p.Flipping,AirFlipAvailable=p.AirFlipAvailable,Support=p.Support,SupportSide=p.SupportSide,SupportObstacleId=p.SupportObstacleId});
            if(f==FlightPresets.FastFlip)Check(p.Support!=SupportKind.None,"authored Fast input supported");
            r.Accepted&=RaceSimulation.Press(r.Game,0)&&RaceSimulation.Press(r.Game,1);
        }
        Finish(r.Game,step);return r;
    }
    static void CourseGood(CourseResult r,bool full,bool negative,string label)
    {
        Check(r.Accepted,label+" accepted edges");
        foreach(var p in r.Game.Players){Check(p.Collisions==0,label+" clean "+string.Join(',',p.HitObstacleIds));
            Check(p.BoostsEarned==(full?9:0)&&p.FinishBoostsEarned==(full?1:0)&&p.SlowsEarned==(negative?1:0),label+" exact earned budget");
            Near(p.FinishTime.Value,full?(negative?33.5:33.2):36,label+" actual finish");}
        Check(Equals(r.Game.Winner,"tie"),label+" exact symmetric tie");
    }
    static void CourseTraps(string[] args)
    {
        string source=args.Length>1?args[1]:"Verification/rhythm-course-draft.json";
        bool runtime=args.Contains("--runtime");
        var courses=JsonSerializer.Deserialize<DraftCourse[]>(File.ReadAllText(source),CourseJson);var exports=new List<object>();var browser=new List<object>();
        foreach(var c in courses){
            if(runtime){var actual=TracksRhythm.Draft(c.id);
                Check(JsonSerializer.Serialize(actual.Obstacles,CourseJson)==JsonSerializer.Serialize(c.track.Obstacles,CourseJson),"runtime obstacle geometry matches frozen course export");
                Check(JsonSerializer.Serialize(actual.RiskZones.Where(z=>!z.IsNegative),CourseJson)==JsonSerializer.Serialize(c.track.RiskZones.Where(z=>!z.IsNegative),CourseJson),"runtime positive zones unchanged");
                c.track=actual;}
            int last=Array.FindLastIndex(c.modules,m=>m.kind=="gate");
            int middle=Enumerable.Range(0,c.modules.Length).Where(i=>i!=last&&c.modules[i].kind=="gate")
                .OrderBy(i=>Math.Abs(c.modules[i].x-3700)).First();
            var zones=new List<RiskZone>(c.track.RiskZones);var added=new List<RiskZone>();
            foreach(int index in new[]{middle,last}){
                var m=c.modules[index];var o=c.track.Obstacles.Single(o=>o.Id.StartsWith(m.id+"-",StringComparison.Ordinal));
                Check(o.Kind=="gate"&&o.Width==16&&o.GapTop==110&&o.GapBottom==130,"matching proven compact negative host");
                double first=ObstacleGeometry.Left(o);var z=new RiskZone{Id=c.id+(index==last?"-hasty-final":"-hasty-middle"),Kind="negative",X=first+80,Y=m.EntrySide==1?200:40,Radius=8};
                Check(z.X+z.Radius+14+66<m.endX,"last negative pulse ends before module boundary");
                if(runtime){var actual=c.track.RiskZones.Single(v=>v.Id==z.Id);Check(actual.IsNegative&&actual.X==z.X&&actual.Y==z.Y&&actual.Radius==z.Radius,"runtime negative exact fields");}
                else zones.Add(z);added.Add(z);
            }
            c.track.RiskZones=zones.ToArray();c.track.NegativeEnabled=true;var routes=new List<object>();
            Check(c.track.RiskZones.Count(z=>!z.IsNegative&&!z.IsFinish)==9,"exact nine ordinary positive rewards");
            Check(c.track.RiskZones.Count(z=>z.IsNegative)==2&&c.track.RiskZones.Count(z=>z.IsFinish)==1,"two negative and one finish reward");
            Check(c.track.RiskZones.Select(z=>z.Id).Distinct().Count()==c.track.RiskZones.Length,"unique final reward IDs");
            Check(c.track.RiskZones.Single(z=>z.IsFinish).X==7876,"exact finish plane");
            foreach(var f in Profiles){string profile=f==FlightPresets.FastFlip?"fast-flip":"chosen";
                var safe=c.routes.Single(r=>r.profile==profile&&r.variant=="safe").route.Select(x=>x.X).ToArray();
                var full=c.routes.Single(r=>r.profile==profile&&r.variant=="full").route;
                var host=c.track.Obstacles.Single(o=>o.Id.StartsWith(c.modules[last].id+"-",StringComparison.Ordinal));double first=ObstacleGeometry.Left(host);
                var negative=new List<double>();bool replaced=false;
                foreach(var input in full){if(input.Module==last){if(!replaced){negative.Add(first+(f==FlightPresets.FastFlip?44:46));if(f==FlightPresets.Chosen)negative.Add(first+70);replaced=true;}continue;}negative.Add(input.X);}
                Check(replaced,"last gate inputs replaced");var fullInputs=full.Select(x=>x.X).ToArray();
                if(runtime){Check(TracksRhythm.SafeRoute(c.id,f).SequenceEqual(safe),"runtime safe route matches frozen export");
                    Check(TracksRhythm.FullRoute(c.id,f).SequenceEqual(fullInputs),"runtime full route matches frozen export");
                    Check(TracksRhythm.NegativeRoute(c.id,f).SequenceEqual(negative),"runtime negative route matches verified witness");}
                foreach(double step in Steps){CourseGood(RunCourse(c.track,f,safe,step),false,false,c.id+" safe");
                    CourseGood(RunCourse(c.track,f,fullInputs,step),true,false,c.id+" full");CourseGood(RunCourse(c.track,f,negative.ToArray(),step),true,true,c.id+" negative");}
                var baselines=new Dictionary<string,CourseResult>();
                foreach(var variant in new[]{"safe","full","negative"}){
                    var inputs=variant=="safe"?safe:variant=="full"?fullInputs:negative.ToArray();var baseline=RunCourse(c.track,f,inputs,.317);
                    baselines[variant]=baseline;
                    for(int i=0;i<inputs.Length;i++)foreach(double sign in new[]{-1d,1})CourseGood(RunCourse(c.track,f,inputs,.317,i,sign*(variant=="safe"?.06:.025),baseline.Times),variant!="safe",variant=="negative",c.id+" "+variant+" shifted"+i);
                    routes.Add(new{profile,variant,inputs,actualTimes=baseline.Times,finish=baseline.Game.Players[0].FinishTime});
                }
                browser.Add(new{id=c.id,name=c.name,profile,preset=f==FlightPresets.FastFlip?11:10,flight=f,length=c.track.Length,
                    safe,full=fullInputs,negative=negative.ToArray(),safeFinish=baselines["safe"].Game.Players[0].FinishTime,
                    fullFinish=baselines["full"].Game.Players[0].FinishTime,negativeFinish=baselines["negative"].Game.Players[0].FinishTime,
                    inputs=baselines["full"].Samples,safeInputs=baselines["safe"].Samples,negativeInputs=baselines["negative"].Samples,
                    obstacles=c.track.Obstacles,zones=c.track.RiskZones});
                Console.WriteLine(c.id+"/"+profile+" safe36/full33.2/trap33.5 six dt; every safe±60ms/full+trap±25ms PASS");
            }
            exports.Add(new{id=c.id,negativeZones=added,negativeHosts=new[]{middle,last},routes});
        }
        string path=args.Length>2?args[2]:"Verification/rhythm-course-traps.json";
        File.WriteAllText(path,JsonSerializer.Serialize(exports,CourseJson));
        int browserArg=Array.IndexOf(args,"--browser");if(browserArg>=0){Check(runtime,"browser export requires runtime verification");
            File.WriteAllText(args[browserArg+1],JsonSerializer.Serialize(browser,CourseJson));}
        Console.WriteLine($"PASS course negative proof: {checks} assertions / {runs} actual games; {path}");
    }
}
