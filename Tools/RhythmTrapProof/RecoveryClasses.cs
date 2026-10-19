using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    sealed class RecoveryRequest {public string track{get;set;}public string profile{get;set;}public string route{get;set;}public string hit{get;set;}public string support{get;set;}public int input{get;set;}public double shift{get;set;}public double hitX{get;set;}public double reactionX{get;set;}public double Y{get;set;}public double VY{get;set;}public int gravity{get;set;}}
    sealed class RecoveryNode {public GameState Game;public double[] Inputs;public double Last=-100;}
    static string RecoveryKey(RecoveryNode n,double at)
    {
        var p=n.Game.Players[0];return string.Join("/",Math.Round(p.Distance),Math.Round(RaceSimulation.GetPlayerCenterY(p)/3),Math.Round(p.VerticalSpeed/15),p.Gravity,p.Support,n.Inputs.Length,Math.Round(Math.Min(.4,at-n.Last)/.05),p.ActiveEffect?.IsNegative,Math.Round((p.ActiveEffect?.Remaining??0)/.05));
    }
    static double[] BeamRecovery(GameState reaction,double target,double spacing,double firstLateMargin=0,double forcedFirst=-1)
    {
        double dt=forcedFirst>=0?.02:.025;double start=reaction.Elapsed;int hits=reaction.Players[0].Collisions;
        var current=new List<RecoveryNode>{new RecoveryNode{Game=Clone(reaction),Inputs=Array.Empty<double>()}};
        for(int tick=0;tick<220&&current.Count>0;tick++) {
            double at=tick*dt;var next=new Dictionary<string,RecoveryNode>();
            foreach(var node in current)for(int press=0;press<2;press++) {
                if(forcedFirst>=0&&node.Inputs.Length==0&&(at>forcedFirst+1e-8||(press==1&&Math.Abs(at-forcedFirst)>1e-8)))continue;
                if(press==1&&(node.Inputs.Length>=4||at-node.Last<spacing-1e-8))continue;
                var g=Clone(node.Game);double[] inputs=node.Inputs;
                if(press==1){if(!RaceSimulation.Press(g,0)||!RaceSimulation.Press(g,1))continue;inputs=inputs.Concat(new[]{at}).ToArray();}
                RaceSimulation.Advance(g,dt);
                if(g.Players[0].Collisions!=hits)continue;
                if(g.Players[0].Distance>=target){
                    if(firstLateMargin==0||inputs.Length==0)return inputs;
                    var shifted=(double[])inputs.Clone();shifted[0]+=firstLateMargin;
                    GameState end;List<InputSample> samples;
                    if((shifted.Length<2||shifted[0]<shifted[1])&&RecoveryTry(reaction,shifted,target,out end,out samples))return inputs;
                    continue;
                }
                var candidate=new RecoveryNode{Game=g,Inputs=inputs,Last=press==1?at:node.Last};
                string key=RecoveryKey(candidate,at+dt);if(!next.ContainsKey(key))next.Add(key,candidate);
            }
            current=next.Values.ToList();
            if(current.Count>12000)current=current.OrderBy(n=>n.Inputs.Length).ThenBy(n=>n.Last).Take(12000).ToList();
        }
        return null;
    }
    static void RecoveryBare20()
    {
        var requests=JsonSerializer.Deserialize<RecoveryRequest[]>(File.ReadAllText("Verification/rhythm-r5-recovery-no-found.json"),CourseJson);
        var r=requests.First(x=>x.track==TracksRhythm.CascadeId&&x.input==4&&x.route=="safe"&&x.shift==.4);
        var source=R5Track(r.track);var hitObstacle=source.Obstacles.Single(o=>o.Id==r.hit);
        var next=source.Obstacles.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(hitObstacle)).OrderBy(ObstacleGeometry.Left).First();
        var originalRoute=R5Route(r.track,FlightPresets.Chosen,"safe");var baseline=RunCourse(source,FlightPresets.Chosen,originalRoute,1d/144);
        var results=new List<object>();
        foreach(double dx in new[]{20d,25,30}) {
            double from=ObstacleGeometry.Left(next);
            var track=new Track(source.Length+dx,source.Obstacles.Select(o=>{var c=new Obstacle();foreach(var f in typeof(Obstacle).GetFields())f.SetValue(c,f.GetValue(o));if(ObstacleGeometry.Left(o)>=from)c.X+=dx;return c;}).ToArray()) {Id=source.Id,RiskZones=source.RiskZones.Select(z=>{var c=new RiskZone();foreach(var f in typeof(RiskZone).GetFields())f.SetValue(c,f.GetValue(z));if(c.X>=from)c.X+=dx;if(c.ClearX>=from)c.ClearX+=dx;return c;}).ToArray()};
            var hit=RecoveryPrefix(track,originalRoute,baseline.Times,r.input,r.shift);var reaction=Clone(hit);AdvanceTime(reaction,reaction.Players[0].Recovering+.15,.001);
            double target=ObstacleGeometry.Right(next)+dx+15;
            bool vertical=false;
            for(double level=95;level<=170&&!vertical;level+=.5) {
                bool good=true;foreach(double first in new[]{0d,.005,.01,.015,.02})if(!AdaptiveHeightRecovery(reaction,first,level,target,null)){good=false;break;}
                if(good){vertical=true;Console.WriteLine($"ADAPTIVE HEIGHT shift={dx}: first0..20ms then second flip descending at Y={level}");
                    foreach(double first in new[]{0d,.005,.01,.015,.02}){var offsets=new List<double>();Check(AdaptiveHeightRecovery(reaction,first,level,target,offsets),"adaptive height sampled window");Console.WriteLine($"HEIGHT first{first}: [{string.Join(',',offsets)}]");}}
            }
            if(!vertical)Console.WriteLine($"ADAPTIVE HEIGHT NO_FOUND shift={dx}");
            bool adaptive=false;
            for(double pause=0;pause<=.06001&&!adaptive;pause+=.01)
            for(double repeat=.24;repeat>=.01999&&!adaptive;repeat-=.01) {
                bool good=true;
                foreach(double first in new[]{0d,.005,.01,.015,.02})if(!AdaptiveWallRecovery(reaction,first,pause,repeat,target,null)){good=false;break;}
                if(good){adaptive=true;Console.WriteLine($"ADAPTIVE shift={dx}: first0..20ms, land actual wall, pause{pause:R}, jump, repeat+{repeat:R}");
                    foreach(double first in new[]{0d,.005,.01,.015,.02}){var offsets=new List<double>();Check(AdaptiveWallRecovery(reaction,first,pause,repeat,target,offsets),"adaptive sampled window production");Console.WriteLine($"ADAPTIVE first{first}: [{string.Join(',',offsets)}]");}}
            }
            if(!adaptive)Console.WriteLine($"ADAPTIVE WALL NO_FOUND shift={dx}");
            foreach(double first in new[]{0d,.02}) {
                double[] witness=null;double chosenSpacing=0;
                foreach(double spacing in new[]{.15,.1,.05}){witness=BeamRecovery(reaction,target,spacing,forcedFirst:first);if(witness!=null){chosenSpacing=spacing;break;}}
                Console.WriteLine($"BARE AIR shift={dx}, first={first}: {(witness==null?"NO_FOUND":string.Join(',',witness))}");
                if(witness!=null){GameState end;List<InputSample> samples;foreach(double step in Steps)Check(RecoveryTry(reaction,witness,target,out end,out samples,step),"shifted bare actual witness six dt");}
                results.Add(new{track=r.track,profile=r.profile,route=r.route,changedInput=r.input,shiftSeconds=r.shift,geometrySuffixFrom=from,geometrySuffixDelta=dx,targetX=target,firstInputSeconds=first,offsetsSeconds=witness,minSpacing=chosenSpacing});
            }
            var strict=BeamRecovery(reaction,target,.1,.02);Console.WriteLine($"BARE strict first+20ms same remainder shift={dx}: {(strict==null?"NO_FOUND":string.Join(',',strict))}");
        }
        File.WriteAllText("Verification/rhythm-bare-air-spacing-candidates.json",JsonSerializer.Serialize(results,CourseJson));
        Console.WriteLine($"PASS actual found bare-air witnesses: {checks} checks; finite NO_FOUND is not impossibility.");
    }
    static bool AdaptiveWallRecovery(GameState reaction,double first,double pause,double repeat,double target,List<double> offsets)
    {
        var g=Clone(reaction);int hits=g.Players[0].Collisions;double start=g.Elapsed;AdvanceTime(g,first,.025);
        if(!RaceSimulation.Press(g,0)||!RaceSimulation.Press(g,1))return false;offsets?.Add(g.Elapsed-start);
        while(g.Players[0].Support!=SupportKind.Wall&&g.Players[0].Distance<target&&g.Players[0].Collisions==hits) {
            var trial=Clone(g);RaceSimulation.Advance(trial,.025);
            if(trial.Players[0].Support==SupportKind.Wall) {
                double low=0,high=.025;for(int i=0;i<40;i++){double mid=(low+high)/2;var probe=Clone(g);RaceSimulation.Advance(probe,mid);if(probe.Players[0].Support==SupportKind.Wall)high=mid;else low=mid;}
                RaceSimulation.Advance(g,high+1e-10);
            } else RaceSimulation.Advance(g,.025);
        }
        if(g.Players[0].Collisions!=hits||g.Players[0].Support!=SupportKind.Wall)return false;
        AdvanceTime(g,pause,.025);if(!RaceSimulation.Press(g,0)||!RaceSimulation.Press(g,1))return false;offsets?.Add(g.Elapsed-start);
        AdvanceTime(g,repeat,.025);if(!RaceSimulation.Press(g,0)||!RaceSimulation.Press(g,1))return false;offsets?.Add(g.Elapsed-start);
        return RecoveryReach(g,target,hits);
    }
    static bool AdaptiveHeightRecovery(GameState reaction,double first,double level,double target,List<double> offsets)
    {
        var g=Clone(reaction);int hits=g.Players[0].Collisions;double start=g.Elapsed;AdvanceTime(g,first,.025);
        if(!RaceSimulation.Press(g,0)||!RaceSimulation.Press(g,1))return false;offsets?.Add(g.Elapsed-start);
        Func<GameState,bool> reached=x=>x.Players[0].VerticalSpeed>0&&RaceSimulation.GetPlayerCenterY(x.Players[0])>=level;
        while(!reached(g)&&g.Players[0].Distance<target&&g.Players[0].Collisions==hits) {
            var trial=Clone(g);RaceSimulation.Advance(trial,.025);
            if(reached(trial)) {double low=0,high=.025;for(int i=0;i<40;i++){double mid=(low+high)/2;var probe=Clone(g);RaceSimulation.Advance(probe,mid);if(reached(probe))high=mid;else low=mid;}RaceSimulation.Advance(g,high+1e-10);}
            else RaceSimulation.Advance(g,.025);
        }
        if(g.Players[0].Collisions!=hits||!reached(g))return false;
        if(!RaceSimulation.Press(g,0)||!RaceSimulation.Press(g,1))return false;offsets?.Add(g.Elapsed-start);
        return RecoveryReach(g,target,hits);
    }
    static void RecoveryClasses()
    {
        var requests=JsonSerializer.Deserialize<RecoveryRequest[]>(File.ReadAllText("Verification/rhythm-r5-recovery-no-found.json"),CourseJson);
        var baselines=new Dictionary<string,CourseResult>();var seen=new HashSet<string>();var rows=new List<object>();
        foreach(var r in requests) {
            var track=R5Track(r.track);var hitObstacle=track.Obstacles.Single(o=>o.Id==r.hit);
            var next=track.Obstacles.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(hitObstacle)).OrderBy(ObstacleGeometry.Left).First();
            bool topFirst=next.TopOffsetX<next.BottomOffsetX;double y=topFirst?r.Y:240-r.Y,v=topFirst?r.VY:-r.VY;
            string cls=string.Join("/",r.support,Math.Round(ObstacleGeometry.Left(next)-14-r.reactionX,2),Math.Round(y,2),Math.Abs(v)<.005?0:Math.Round(v,2));if(!seen.Add(cls))continue;
            var route=R5Route(r.track,FlightPresets.Chosen,r.route);
            string baselineKey=r.track+"/"+r.route;if(!baselines.ContainsKey(baselineKey))baselines[baselineKey]=RunCourse(track,FlightPresets.Chosen,route,1d/144);
            var hit=RecoveryPrefix(track,route,baselines[baselineKey].Times,r.input,r.shift);Check(hit!=null,"reported representative actual hit");
            var reaction=Clone(hit);AdvanceTime(reaction,reaction.Players[0].Recovering+.15,.001);
            Near(reaction.Players[0].Distance,r.reactionX,"reported reaction X",5e-5);Near(RaceSimulation.GetPlayerCenterY(reaction.Players[0]),r.Y,"reported reaction Y",5e-5);
            double target=ObstacleGeometry.Right(next)+15;
            Console.WriteLine($"CLASS {cls} {r.track}/{r.route} input{r.input} shift{r.shift}: searching actual target{target}");
            double[] inputs=null;double spacing=.15;
            foreach(double trialSpacing in new[]{.15,.1,.05}) {inputs=BeamRecovery(reaction,target,trialSpacing,.02);if(inputs!=null){spacing=trialSpacing;break;}}
            if(inputs==null){Console.WriteLine("NO_FOUND_EXTENDED "+cls);rows.Add(new{r.track,r.profile,r.route,changedInput=r.input,shiftSeconds=r.shift,targetX=target,classification=cls,found=false});}
            else {
                GameState end;List<InputSample> samples;foreach(double step in Steps)Check(RecoveryTry(reaction,inputs,target,out end,out samples,step),"representative actual four-input witness six dt");
                var windows=new List<double>();for(int i=0;i<inputs.Length;i++){double window=0;for(int k=1;k<=12;k++){double d=k*.005;bool good=true;foreach(double sign in new[]{-1d,1}){var shifted=(double[])inputs.Clone();shifted[i]+=sign*d;if(shifted[i]<0||(i>0&&shifted[i]<=shifted[i-1])||(i<inputs.Length-1&&shifted[i]>=shifted[i+1])||!RecoveryTry(reaction,shifted,target,out end,out samples)){good=false;break;}}if(!good)break;window=d;}windows.Add(window);}
                Console.WriteLine($"FOUND {cls} offsets=[{string.Join(',',inputs)}] spacing>={spacing} individualSymmetricWindows=[{string.Join(',',windows)}]");
                rows.Add(new{r.track,r.profile,r.route,changedInput=r.input,shiftSeconds=r.shift,targetX=target,classification=cls,found=true,offsetsSeconds=inputs,minSpacing=spacing,individualSymmetricWindows=windows});
            }
            File.WriteAllText("Verification/rhythm-r5-recovery-class-witnesses.json",JsonSerializer.Serialize(rows,CourseJson));
        }
        Console.WriteLine($"Representative classification done: {seen.Count} classes, {checks} checks. Failed finite search is not impossibility proof.");
    }
}
