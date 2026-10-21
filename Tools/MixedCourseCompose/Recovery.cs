using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static GameState Collision(FlightSettings flight,Planned[] route,Observation baseline,int changed,double shift)
    {
        var g=RaceSimulation.CreateTrial(TracksMixed.Get(),flight);Tap(g);RaceSimulation.Advance(g,3);firstCollision=null;captureCollision=true;
        try{
            for(int i=0;i<route.Length&&firstCollision==null;i++){
                if(i==changed){double at=baseline.Samples[i].Time+shift;if(at<g.Elapsed)return null;AdvanceTime(g,at-g.Elapsed,.01);}
                else if(route[i].X>=g.Players[0].Distance-1e-8)MoveTo(g,route[i].X,.01);else continue;
                if(firstCollision!=null)break;RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);
            }
            while(firstCollision==null&&g.Phase=="racing")Step(g,.01);
            return firstCollision;
        }finally{captureCollision=false;firstCollision=null;}
    }
    static bool Both(GameState g)=>RaceSimulation.Press(g,0)&RaceSimulation.Press(g,1);
    static bool Clears(GameState g,double target,int hits)
    {
        if(g.Players.Any(p=>p.Collisions!=hits))return false;
        var trial=Clone(g);
        while(trial.Phase=="racing"&&trial.Players[0].Distance<target-1e-7){RaceSimulation.Advance(trial,Math.Min(.05,(target-trial.Players[0].Distance)/440));if(trial.Players.Any(p=>p.Collisions!=hits))return false;}
        return trial.Players.All(p=>p.Collisions==hits&&p.Distance>=target-1e-7);
    }
    static bool Rescue(GameState collision,out double target,out double[] witness)
    {
        var ready=Clone(collision);var p=ready.Players[0];int hits=p.Collisions;
        var obs=ready.Track.Obstacles.OrderBy(ObstacleGeometry.Left).ToArray();int hit=Array.FindIndex(obs,o=>o.Id==p.LastHit.Id);
        target=hit+1<obs.Length?ObstacleGeometry.Right(obs[hit+1])+15:ready.Track.Length;witness=null;
        AdvanceTime(ready,p.Recovering+.15,.01);
        if(ready.Players.Any(v=>v.Collisions!=hits))return false;
        if(Clears(ready,target,hits)){witness=Array.Empty<double>();return true;}
        double max=Math.Min(6,(target-p.Distance)/110);
        for(double delay=0;delay<max;delay+=.05){
            var one=Clone(ready);AdvanceTime(one,delay,.03);if(one.Players.Any(v=>v.Collisions!=hits)||!Both(one))continue;
            if(Clears(one,target,hits)){witness=new[]{delay};return true;}
            for(double second=.1;second<=1.5;second+=.05){
                var two=Clone(one);AdvanceTime(two,second,.03);if(two.Players.Any(v=>v.Collisions!=hits)||!Both(two))continue;
                if(Clears(two,target,hits)){witness=new[]{delay,delay+second};return true;}
            }
        }
        // The observed low-gate collision can first settle on the ceiling, then
        // return to the floor and approach the next offset gate from real support.
        // This is an offline recovery witness, never a gameplay controller.
        if(ready.Flight.FirstAction==FirstAction.Jump&&p.LastHit.Id==TracksMixed.Id+"-low-floor"){
            var four=Clone(ready);var times=new List<double>();double origin=four.Elapsed;
            bool wall=WaitWall(four,hits);
            if(wall&&Both(four)){
                times.Add(four.Elapsed-origin);AdvanceTime(four,.2,.01);
                if(four.Players.All(v=>v.Collisions==hits)&&Both(four)){
                    times.Add(four.Elapsed-origin);
                    if(WaitWall(four,hits)){
                        double launch=obs[hit+1].X-19.1551226858691;
                        if(four.Players[0].Distance<=launch){
                            MoveTo(four,launch,.01);
                            if(four.Players.All(v=>v.Collisions==hits)&&Both(four)){
                                times.Add(four.Elapsed-origin);AdvanceTime(four,.2,.01);
                                if(four.Players.All(v=>v.Collisions==hits)&&Both(four)){
                                    times.Add(four.Elapsed-origin);
                                    if(Clears(four,target,hits)){witness=times.ToArray();return true;}
                                }
                            }
                        }
                    }
                }
            }
        }
        return false;
    }
    static bool WaitWall(GameState g,int hits)
    {
        double end=g.Elapsed+4;
        while(g.Phase=="racing"&&g.Elapsed<end&&g.Players[0].Support!=SupportKind.Wall){AdvanceTime(g,.002,.002);if(g.Players.Any(p=>p.Collisions!=hits))return false;}
        return g.Players.All(p=>p.Collisions==hits&&p.Support==SupportKind.Wall&&!p.Flipping&&p.AirFlipAvailable);
    }
    static void Recovery()
    {
        quiet=true;var rows=new List<object>();int failed=0;
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(bool full in new[]{false,true}){
            var plan=Plan(flight,full);var route=plan.Inputs.ToArray();var baseline=Replay(flight,route);int start=rows.Count;
            for(int i=0;i<route.Length;i++)foreach(double shift in new[]{-.64,-.4,-.16,.16,.4,.64}){
                var g=Collision(flight,route,baseline,i,shift);if(g==null)continue;
                bool found=Rescue(g,out double target,out double[] witness);var p=g.Players[0];if(!found){failed++;Console.WriteLine($"NOFOUND {flight.FirstAction}/{full} i{i} shift{shift} hit{p.LastHit.Id} X{p.Distance} y{RaceSimulation.GetPlayerCenterY(p)} v{p.VerticalSpeed} target{target}");}
                rows.Add(new{profile=flight==FlightPresets.Chosen?"chosen":"fast-flip",variant=full?"full":"safe",input=i,shift,hit=p.LastHit.Id,x=p.Distance,y=RaceSimulation.GetPlayerCenterY(p),p.VerticalSpeed,p.Support,p.SupportSide,p.AirFlipAvailable,remainingRecovery=p.Recovering,target,found,witness});
            }
            Console.WriteLine($"RECOVERY {flight.FirstAction}/{full} actualstates{rows.Count-start} cumulativeNoFound{failed}");
        }
        File.WriteAllText("Verification/mixed-course-recovery-r2.json",JsonSerializer.Serialize(rows,Json));
        Check(failed==0,"bounded recovery witnesses found");
        Console.WriteLine($"RECOVERY PASS states{rows.Count} noFound{failed}; first actual hit, remaining penalty+.15, next whole union or tape reached; stay/two-input catalogue plus actual-wall four-input recovery.");
    }
}
