using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static void RecoveryR7Six()
    {
        int oldChecks=checks;string id=TracksRhythm.CascadeId;var track=TracksRhythm.Draft(id);
        var before=track.Obstacles.Single(o=>o.Id=="rhythm-cascade-m4-exit-gate");
        var next=track.Obstacles.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(before)).OrderBy(ObstacleGeometry.Left).First();
        double target=ObstacleGeometry.Right(next)+15;var rows=new List<object>();
        foreach(string variant in new[]{"full","trap"}) {
            var route=variant=="full"?TracksRhythm.FullRoute(id,FlightPresets.Chosen):TracksRhythm.NegativeRoute(id,FlightPresets.Chosen);
            var baseline=RunCourse(track,FlightPresets.Chosen,route,1d/144);
            foreach(double error in new[]{.4,.64,.8}) {
                const int index=10;var hit=RecoveryPrefix(track,route,baseline.Times,index,error);Check(hit.Players[0].LastHit.Id==before.Id,"new six R7 actual same obstacle hit");
                var reaction=Clone(hit);AdvanceTime(reaction,reaction.Players[0].Recovering+.15,.001);var witnesses=new List<object>();
                foreach(double extra in new[]{0d,.005,.01,.015,.02}) {
                    var offsets=new List<double>();Check(AdaptiveTwoWalls(reaction,extra,.09,0,.10,target,offsets),"R7 adaptive two actual walls");
                    GameState end;List<InputSample> samples;foreach(double step in Steps)Check(RecoveryTry(reaction,offsets.ToArray(),target,out end,out samples,step),"R7 six cases actual six dt whole union");
                    RecoveryTry(reaction,offsets.ToArray(),target,out end,out samples);witnesses.Add(new{extraFirstDelay=extra,offsetsSeconds=offsets,inputs=samples,clearedX=end.Players[0].Distance,hits=end.Players[0].Collisions});
                }
                var p=reaction.Players[0];rows.Add(new{track=id,profile="chosen",route=variant,changedInput=index,shiftSeconds=error,
                    baselineInputX=route[index],baselineInputTime=baseline.Times[index],hitId=before.Id,nextId=next.Id,targetX=target,
                    reactionX=p.Distance,reactionY=RaceSimulation.GetPlayerCenterY(p),reactionVY=p.VerticalSpeed,repeat1=.09,repeat2=.10,witnesses});
                Console.WriteLine($"PASS R7 new before {variant} input{index}+{error}: actual runtime, firstextra0/5/10/15/20ms x6dt");
            }
        }
        File.WriteAllText("Verification/rhythm-r7-recovery-new-six.json",JsonSerializer.Serialize(rows,CourseJson));
        Console.WriteLine($"PASS R7 new recovery: {checks-oldChecks} checks /6 actual prefixes; total targeted checks {checks}; no state injection or geometry override.");
    }
}
