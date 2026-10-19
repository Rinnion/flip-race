using System;
using System.Collections.Generic;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static GameState CourseFirstHit(CourseWitness w,double[] clocks,int changed,double shift)
    {
        var g=Start(w.Track,w.Flight);
        for(int i=0;i<w.Inputs.Length&&g.Players[0].Collisions==0;i++)
        {
            if(i==changed)
            {
                double at=clocks[i]+shift;if(at<g.Elapsed-1e-8)return null;
                UntilHit(g,at,.025);
            }
            else FlowTo(g,w.Inputs[i],.025,true);
            if(g.Players[0].Collisions>0)break;
            TapBoth(g);
        }
        if(g.Players[0].Collisions==0)UntilHit(g,100,.025);
        return g.Players[0].Collisions>0?g:null;
    }
    static void CourseRecovery(bool onlyBefore)
    {
        int cases=0,failed=0;Console.WriteLine("COURSE RECOVERY "+CourseFile+" beforeOnly="+onlyBefore);
        foreach(var w in ReadCourses())
        {
            var beforeIds=new HashSet<string>();
            foreach(var z in w.Track.RiskZones)if(!z.IsNegative&&!z.IsInstant&&!z.IsLanding&&!z.IsFinish&&!z.FreeReward&&!z.AfterObstacle)beforeIds.Add(z.ObstacleId);
            context=w.Track.Id+" "+Name(w.Flight)+" "+w.Purpose;
            var baseline=ObserveCourse(w,1d/144);Need(CourseGoal(baseline,w),"recovery nominal route clean");
            var clocks=baseline.Actions.ConvertAll(a=>a.Time).ToArray();int local=0;
            for(int i=0;i<w.Inputs.Length;i++)
            {
                if(onlyBefore)
                {
                    bool near=false;foreach(var o in w.Track.Obstacles)if(beforeIds.Contains(o.Id)&&w.Inputs[i]>=ObstacleGeometry.Left(o)-250&&w.Inputs[i]<=ObstacleGeometry.Right(o)+150)near=true;
                    if(!near)continue;
                }
                foreach(double shift in new[]{-.8,-.64,-.4,-.24,-.16,-.08,.08,.16,.24,.4,.64,.8})
                {
                    var hit=CourseFirstHit(w,clocks,i,shift);if(hit==null)continue;
                    if(onlyBefore&&!beforeIds.Contains(hit.Players[0].LastHit.Id))continue;
                    context=w.Track.Id+" "+Name(w.Flight)+" "+w.Purpose+" input="+i+" shift="+shift;
                    cases++;local++;string witness;bool escaped=Escape(hit,out witness);if(!escaped)failed++;
                    var reaction=Clone(hit);Advance(reaction,reaction.Players[0].Recovering+.15,.001);var p=reaction.Players[0];
                    if(!escaped||onlyBefore)Console.WriteLine((escaped?"ESCAPE ":"NO_FOUND_ESCAPE ")+context+" hit="+hit.Players[0].LastHit.Id
                        +" hitX="+hit.Players[0].Distance.ToString("R")+" hitY="+RaceSimulation.GetPlayerCenterY(hit.Players[0]).ToString("R")
                        +" reactionX="+p.Distance.ToString("R")+" Y="+RaceSimulation.GetPlayerCenterY(p).ToString("R")+" VY="+p.VerticalSpeed.ToString("R")
                        +" gravity="+p.Gravity+" support="+p.Support+" charge="+p.AirFlipAvailable+" hitsAfterReaction="+p.Collisions+" witness="+witness);
                }
            }
            Console.WriteLine("RECOVERY SAMPLE "+w.Track.Id+" "+Name(w.Flight)+" "+w.Purpose+" actualHits="+local);
        }
        Need(cases>0,"nonempty actual impact sample");
        Console.WriteLine((failed==0?"PASS":"REVIEW REQUIRED")+" independent course recovery: "+checks+" assertions / "+games+" games / "+cases+" actual first-hit states; no-found-escape="+failed+". A finite failed search is not proof of inevitability.");
    }
}
