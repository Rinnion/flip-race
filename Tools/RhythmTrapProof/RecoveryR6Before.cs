using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static bool WaitRecoveryWall(GameState g,int hits,double target)
    {
        while(g.Players[0].Support!=SupportKind.Wall&&g.Players[0].Distance<target&&g.Players[0].Collisions==hits){
            var next=Clone(g);RaceSimulation.Advance(next,.025);
            if(next.Players[0].Support==SupportKind.Wall){double low=0,high=.025;for(int k=0;k<40;k++){double mid=(low+high)/2;var test=Clone(g);RaceSimulation.Advance(test,mid);if(test.Players[0].Support==SupportKind.Wall)high=mid;else low=mid;}RaceSimulation.Advance(g,high+1e-10);}
            else RaceSimulation.Advance(g,.025);
        }
        return g.Players[0].Collisions==hits&&(g.Players[0].Support==SupportKind.Wall||g.Players[0].Distance>=target);
    }
    static bool AdaptiveTwoWalls(GameState reaction,double firstPause,double q,double secondPause,double r,double target,List<double> offsets)
    {
        var g=Clone(reaction);int hits=g.Players[0].Collisions;double start=g.Elapsed;
        foreach(var leg in new[]{(firstPause,q),(secondPause,r)}){
            if(!WaitRecoveryWall(g,hits,target))return false;
            if(g.Players[0].Distance>=target)return true;
            AdvanceTime(g,leg.Item1,.025);if(g.Players[0].Collisions!=hits||!RaceSimulation.Press(g,0)||!RaceSimulation.Press(g,1))return false;offsets?.Add(g.Elapsed-start);
            AdvanceTime(g,leg.Item2,.025);if(g.Players[0].Collisions!=hits||!RaceSimulation.Press(g,0)||!RaceSimulation.Press(g,1))return false;offsets?.Add(g.Elapsed-start);
        }
        return RecoveryReach(g,target,hits);
    }
    static void RecoveryR6Before()
    {
        string id=TracksRhythm.CascadeId;var source=CheckpointTrack("rhythm-r6-routes.json",id);var route=CheckpointRoute("rhythm-r6-routes.json",id,FlightPresets.Chosen,"full");
        var baseline=RunCourse(source,FlightPresets.Chosen,route,1d/144);const int index=10;const double error=.4;
        var before=source.Obstacles.Single(o=>o.Id=="rhythm-cascade-m4-exit-gate");var next=source.Obstacles.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(before)).OrderBy(ObstacleGeometry.Left).First();
        var rows=new List<object>();
        foreach(double dx in new[]{0d,10,15,20}){
            var track=ShiftFuture(source,ObstacleGeometry.Left(next),dx);var hit=RecoveryPrefix(track,route,baseline.Times,index,error);Check(hit.Players[0].LastHit.Id==before.Id,"new R6 before actual hit");
            var reaction=Clone(hit);AdvanceTime(reaction,reaction.Players[0].Recovering+.15,.001);var p=reaction.Players[0];double target=ObstacleGeometry.Right(next)+dx+15;
            Console.WriteLine($"NEW BEFORE delta{dx} reactionX{p.Distance:R} y{RaceSimulation.GetPlayerCenterY(p):R} vy{p.VerticalSpeed:R} target{target}");
            double bestQ=-1,bestR=-1,best=-1;
            for(double q=.01;q<=.200001;q+=.01)for(double r=.01;r<=.200001;r+=.01){
                double score=Math.Min(q,r);if(score<best-1e-9)continue;bool good=true;
                foreach(double extra in new[]{0d,.005,.01,.015,.02})if(!AdaptiveTwoWalls(reaction,extra,q,0,r,target,null)){good=false;break;}
                if(good){best=score;bestQ=q;bestR=r;}
            }
            var witnesses=new List<object>();
            if(bestQ<0)Console.WriteLine("NO_FOUND adaptive20ms delta"+dx);
            else {Console.WriteLine($"ADAPTIVE delta{dx} firstwall+extra0..20ms; jump/repeat{bestQ:R}; nextwall jump/repeat{bestR:R}");
                foreach(double extra in new[]{0d,.005,.01,.015,.02}){var offsets=new List<double>();Check(AdaptiveTwoWalls(reaction,extra,bestQ,0,bestR,target,offsets),"new before adaptive found");GameState end;List<InputSample> samples;
                    foreach(double step in Steps)Check(RecoveryTry(reaction,offsets.ToArray(),target,out end,out samples,step),"new before actual six dt");
                    witnesses.Add(new{extraFirstDelay=extra,offsetsSeconds=offsets});}}
            rows.Add(new{track=id,profile="chosen",route="full",changedInput=index,shiftSeconds=error,geometrySuffixFrom=ObstacleGeometry.Left(next),geometrySuffixDelta=dx,targetX=target,repeat1=bestQ,repeat2=bestR,witnesses});
        }
        File.WriteAllText("Verification/rhythm-r6-before-newclass-candidates.json",JsonSerializer.Serialize(rows,CourseJson));
        Console.WriteLine($"New R6 before candidate checks {checks}; no runtime changes.");
    }
    static void RecoveryR6BeforeSix()
    {
        string id=TracksRhythm.CascadeId;var source=CheckpointTrack("rhythm-r6-routes.json",id);var before=source.Obstacles.Single(o=>o.Id=="rhythm-cascade-m4-exit-gate");
        var next=source.Obstacles.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(before)).OrderBy(ObstacleGeometry.Left).First();
        const double dx=20,q=.09,r=.10;const int index=10;var track=ShiftFuture(source,ObstacleGeometry.Left(next),dx);double target=ObstacleGeometry.Right(next)+dx+15;var rows=new List<object>();
        foreach(string variant in new[]{"full","trap"}) {
            var route=CheckpointRoute("rhythm-r6-routes.json",id,FlightPresets.Chosen,variant);
            var baseline=RunCourse(source,FlightPresets.Chosen,route,1d/144);
            foreach(double error in new[]{.4,.64,.8}) {
                var hit=RecoveryPrefix(track,route,baseline.Times,index,error);Check(hit.Players[0].LastHit.Id==before.Id,"six-case actual same before hit");
                var reaction=Clone(hit);AdvanceTime(reaction,reaction.Players[0].Recovering+.15,.001);var witnesses=new List<object>();
                foreach(double extra in new[]{0d,.005,.01,.015,.02}) {
                    var offsets=new List<double>();Check(AdaptiveTwoWalls(reaction,extra,q,0,r,target,offsets),"six-case adaptive two walls");
                    GameState end;List<InputSample> samples;foreach(double step in Steps)Check(RecoveryTry(reaction,offsets.ToArray(),target,out end,out samples,step),"six-case actual six dt clear union");
                    RecoveryTry(reaction,offsets.ToArray(),target,out end,out samples);witnesses.Add(new{extraFirstDelay=extra,offsetsSeconds=offsets,inputs=samples,clearedX=end.Players[0].Distance,hits=end.Players[0].Collisions});
                }
                var p=reaction.Players[0];rows.Add(new{track=id,profile="chosen",route=variant,changedInput=index,shiftSeconds=error,geometrySuffixFrom=ObstacleGeometry.Left(next),geometrySuffixDelta=dx,targetX=target,reactionX=p.Distance,reactionY=RaceSimulation.GetPlayerCenterY(p),reactionVY=p.VerticalSpeed,repeat1=q,repeat2=r,witnesses});
                Console.WriteLine($"PASS before+20 {variant} input{index}+{error}: real walls, repeat90/100ms, firstextra0/5/10/15/20ms x6dt");
            }
        }
        File.WriteAllText("Verification/rhythm-r6-before-six-candidate.json",JsonSerializer.Serialize(rows,CourseJson));
        Console.WriteLine($"PASS six actual R6 before-error prefixes: {checks} checks; tool-only +20 next boundary, Core/runtime unchanged.");
    }
}
