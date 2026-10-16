using System;
using FlipRace.Core;

internal static partial class Program
{
    static void FinishAndBacklogProof()
    {
        foreach(var profile in MotionProfiles.All)
        {
            var track=TracksTune.Challenge(profile);
            var ideal=Route(track,MotionMode.GravityAirFlip,track.RiskRoute,.01,false,profile);
            var p=ideal.Game.Players[0];
            Check(p.LastInstant.Id=="tune-instant-finish","Final instant was missed");
            double remaining=p.FinishTime.Value-p.LastInstant.At;
            Check(remaining>RaceConfig.BoostDuration,"Final pulse did not have full real time");
            double maxGap=0;
            for(int i=1;i<ideal.Inputs.Count;i++)maxGap=Math.Max(maxGap,ideal.Inputs[i].Time-ideal.Inputs[i-1].Time);
            Console.WriteLine("Metrics "+profile.Id+" first="+ideal.Inputs[0].Time.ToString("F6")+
                " maxInterior="+maxGap.ToString("F6")+" lastToFinish="+(19.2-ideal.Inputs[ideal.Inputs.Count-1].Time).ToString("F6")+
                " finalContactToFinish="+remaining.ToString("F9"));
            var game=Start(track,MotionMode.GravityAirFlip,profile);
            for(int i=0;i<track.RiskRoute.Length-1;i++)
            {
                MoveTo(game,track.RiskRoute[i],.01);
                Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"Duel shared input failed");
            }
            MoveTo(game,track.RiskRoute[track.RiskRoute.Length-1],.01);
            Check(RaceSimulation.Press(game,0),"Final bonus departure failed");
            Finish(game,.01);
            Near(game.Players[0].FinishTime.Value,19.2,"Final reward winner time");
            Near(game.Players[1].FinishTime.Value,19.5,"Skipped final reward time");
            Check(Equals(game.Winner,0),"Final bonus did not produce a real earlier finish");
            Check(game.Players[0].Collisions==0&&game.Players[1].Collisions==0,"Final bonus duel collided");
        }
        // Deliberately synthetic long tail. This exercises slow preemption plus
        // a queued normal reward and a second instant, not new runtime geometry.
        foreach(var profile in MotionProfiles.All)
        {
            var track=new Track(880,Array.Empty<Obstacle>()) {NegativeEnabled=true,RiskZones=new[]{
                new RiskZone{Id="fixture-slow",Kind="negative",X=80,Y=226,Radius=6},
                new RiskZone{Id="fixture-queued",FreeReward=true,X=100,Y=226,Radius=6},
                new RiskZone{Id="fixture-instant-1",Kind="instant",X=130,Y=226,Radius=6},
                new RiskZone{Id="fixture-instant-2",Kind="instant",X=140,Y=226,Radius=6}
            }};
            var game=Start(track,MotionMode.GravityAirFlip,profile);
            MoveTo(game,121,.01);var p=game.Players[0];var waiting=p.PendingEffects.ToArray();
            Check(p.CurrentSpeed==330&&p.ActiveEffect.PulseCount==2,"Instants should interrupt slow then extend positive");
            Check(waiting.Length==2&&waiting[0].IsNegative&&!waiting[1].IsNegative,"Suspended slow must precede the existing queue");
            Check(waiting[0].Remaining>0&&waiting[0].Remaining<.6,"Slow remainder was lost or restarted");
            MoveTo(game,350,.01);
            Check(p.ActiveEffect.PulseCount==2&&p.ActiveEffect.CompletedPulses==1,"Extended pulse first credit missing");
            var copy=Clone(game);var q=copy.Players[0];
            Check(q.ActiveEffect.PulseCount==2&&q.ActiveEffect.CompletedPulses==1,"Snapshot lost extended pulse fields");
            Check(q.InstantBoostsEarned==p.InstantBoostsEarned&&q.LastInstant==p.LastInstant,"Snapshot lost instant metadata");
            Near(q.BoostDistanceGained,p.BoostDistanceGained,"Snapshot boost ledger");
            Near(q.SlowDistanceLost,p.SlowDistanceLost,"Snapshot suspended slow ledger");
            Finish(game,.317);CheckClean(new RouteResult{Game=game},3,1,3.4);
            Check(p.InstantBoostsEarned==2,"Fixture should earn both instant bonuses exactly once");
        }
        Console.WriteLine("Final bonus: real .3s win; synthetic slow + queued reward + extended instant tail fully served PASS.");
    }

    static void WindowReport()
    {
        // Search is diagnostic: failing trials locate limits and are excluded
        // from the PASS assertion counter. +/-40ms is asserted separately.
        foreach(var profile in MotionProfiles.All)
        {
            var track=TracksTune.Challenge(profile);
            foreach(bool safe in new[]{true,false})
            {
                var inputs=safe?track.SafeRoute:track.RiskRoute;
                var baseline=Route(track,MotionMode.GravityAirFlip,inputs,.01,safe,profile);
                double early=.12,late=.12;int earlyEdge=-1,lateEdge=-1;
                for(int edge=0;edge<inputs.Length;edge++)foreach(int sign in new[]{-1,1})
                {
                    double passed=0;
                    for(int n=1;n<=24;n++)
                    {
                        int saved=checks;bool clean=true;
                        try{CheckClean(TimedShift(track,MotionMode.GravityAirFlip,inputs,baseline.Inputs,edge,sign*n*.005,safe,profile),safe?0:6,0,safe?21:19.2);}
                        catch{clean=false;}
                        checks=saved;if(!clean)break;passed=n*.005;
                    }
                    if(sign<0&&passed<early){early=passed;earlyEdge=edge;}
                    if(sign>0&&passed<late){late=passed;lateEdge=edge;}
                }
                Console.WriteLine("Window scan "+profile.Id+" "+(safe?"safe":"ideal")+" early="+early.ToString("F3")+
                    " (input "+earlyEdge+") late="+late.ToString("F3")+" (input "+lateEdge+") seconds,5ms grid");
            }
        }
    }
}
