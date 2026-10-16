using System;
using System.Collections.Generic;
using FlipRace.Core;

internal static partial class Program
{
    static void CleanApproachBound(Track track)
    {
        var finish = Array.Find(track.RiskZones, z => z.IsFinish);
        Check(finish != null, "CONTROL-03F has a distinct finish plane");
        Near(finish.X, track.Length - 44, "Finish contact plane is exactly 44 before physical tape");
        Near(finish.Y, 60, "Shared finish target height");
        Near(finish.FinishHeight, 16, "Exact center-Y span; no circular radius expansion");
        var earlier = new List<RiskZone>(); double latestContact = 0;
        foreach (var zone in track.RiskZones)
        {
            if (zone.IsFinish) continue;
            Check(zone.IsLanding || zone.IsNegative, "Bound explicitly covers only the authored earlier landing/negative kinds");
            double latest;
            if (zone.IsLanding)
            {
                var stone = Array.Find(track.Obstacles, o => o.Id == zone.ObstacleId);
                Check(stone != null, "Landing is attached to a real stone");
                latest = Math.Min(stone.X + stone.Width, zone.X + zone.LandingWidth / 2);
            }
            else latest = zone.X + zone.Radius + RaceConfig.PlayerRadius;
            latestContact = Math.Max(latestContact, latest);
            earlier.Add(zone);
        }
        Check(earlier.Count == 3, "Two one-off landing pulses and one one-off negative");
        foreach (var obstacle in track.Obstacles)
            Check(obstacle.X + obstacle.Width + RaceConfig.PlayerRadius < finish.X, "No obstacle in final 44 units");
        Near(latestContact, 3132, "Latest possible ordinary pickup, regardless of vertical history");
        double availableDistance = finish.X-latestContact, largestDuration = 0;
        for (int mask = 0; mask < 1 << earlier.Count; mask++)
        {
            double duration = 0;
            for (int bit = 0; bit < earlier.Count; bit++)
                if ((mask & (1 << bit)) != 0)
                    duration += earlier[bit].IsNegative ? RaceConfig.SlowDuration : RaceConfig.BoostDuration;
            largestDuration = Math.Max(largestDuration, duration);
            // All earned pulse remainders/queues are no longer than their full
            // one-off durations. Before finish activation speed never exceeds
            // 330. Recovery freezes both distance and service, so it cannot
            // consume this spatial clearance while retaining more effect time.
            Check(availableDistance > duration * RaceConfig.Speed * RaceConfig.BoostMultiplier,
                "Every attainable pickup subset clears its entire queue naturally before the finish plane");
        }
        Console.WriteLine("CLEAN APPROACH: 8 subsets; latest contact=" + latestContact + ", clearance="
            + availableDistance + ", at least " + (availableDistance/330).ToString("F6")
            + " moving seconds vs at most " + largestDuration.ToString("F1") + " effect seconds.");
    }
    static void FinalOnlyDuel(Track track, MotionMode mode, int hitter, double step)
    {
        var game = Start(track, mode, ControlProfile(mode));
        for (int i = 0; i < track.RiskRoute.Length-1; i++)
        { MoveTo(game,track.RiskRoute[i],step); Tap(game); }
        MoveTo(game,track.RiskRoute[track.RiskRoute.Length-1],step);
        Check(RaceSimulation.Press(game,hitter), "Only one player performs the final optional maneuver");
        Finish(game,step);
        var winner = game.Players[hitter]; var miss = game.Players[1-hitter];
        Near(winner.FinishTime.Value,20.3,"Final-only hit time"); Near(miss.FinishTime.Value,20.4,"Final-only miss time");
        Near(miss.FinishTime.Value-winner.FinishTime.Value,.1,"Real final-only advantage");
        Check(winner.Collisions==0 && miss.Collisions==0 && Equals(game.Winner,hitter), "Actual tape crossing chooses the right winner");
        Check(winner.FinishBoostsEarned==1 && miss.FinishBoostsEarned==0 && winner.BoostsEarned==2
            && miss.BoostsEarned==2, "Finish reward does not add an ordinary .3-second pulse");
    }
    static void FinishPlaneProof()
    {
        foreach (var mode in ControlModes)
        {
            var track=TracksControl.Challenge(mode); CleanApproachBound(track);
            var ideal=Route(track,mode,ControlProfile(mode),track.RiskRoute,.01,false);
            var finish=Array.Find(track.RiskZones,z=>z.IsFinish);
            var reward=ideal.Rewards.Find(r=>r.Id==finish.Id);
            Check(reward!=null,"Ideal route collected finish plane");
            var before=ReplayUntil(track,mode,ideal,reward.At-.000001);
            foreach(var p in before.Players)
                Check(p.Distance<finish.X && !p.FinishBoost && p.FinishBoostsEarned==0
                    && p.ActiveEffect==null && p.PendingEffects.Count==0 && Math.Abs(p.CurrentSpeed-220)<1e-8,
                    "Before exact plane: no early radius pickup, clean 220 approach");
            var contact=ReplayUntil(track,mode,ideal,reward.At);
            foreach(var p in contact.Players)
            {
                Near(p.Distance,finish.X,"Exact swept center-X finish contact",1e-7);
                Check(Math.Abs(RaceSimulation.GetPlayerCenterY(p)-finish.Y)<=finish.FinishHeight/2+1e-8,
                    "Actual center-Y is inside narrow finish span");
                Check(p.FinishBoost && p.FinishBoostsEarned==1 && p.BoostsEarned==2
                    && p.ActiveEffect==null && p.PendingEffects.Count==0,"Separate final state, no ordinary effect or queue");
                Near(p.CurrentSpeed,440,"Instant final speed");
            }
            AdvanceTime(contact,.05,.317);
            foreach(var p in contact.Players)
            { Near(p.Distance,finish.X+22,"Actual half-way motion after .05 seconds",1e-7); Check(!p.FinishTime.HasValue,"No teleport or premature result"); }
            AdvanceTime(contact,.05,.317);
            foreach(var p in contact.Players)
            {
                Near(p.FinishTime.Value,20.3,"Actual final tape time");
                Near(p.FinishBoostElapsed,.1,"Exactly .1 moving seconds of separate finish boost");
                Near(p.FinishBoostDistanceGained,22,"Separate ledger records actual extra movement");
                Check(!p.FinishBoost && p.FinishBoostsEarned==1 && p.CompletedBoosts==2,"No finish boost remains active or becomes an ordinary pulse");
            }
            foreach(double step in new[]{1d/30,1d/60,1d/144,.317,2.5,100d})
            { FinalOnlyDuel(track,mode,0,step); FinalOnlyDuel(track,mode,1,step); }
            Console.WriteLine("FINISH " + mode + ": exact plane4576, span52..68, 220->440, .1s travel/gain; paired20.3vs20.4 both sides PASS.");
        }
    }
}
