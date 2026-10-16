using System;
using System.Collections.Generic;
using FlipRace.Core;

public static class IndependentV3FullProbe
{
    static int checks, recoverySamples, recoveryActiveSlow, recoveryActiveBoost, recoveryQueued, recoveryPriorNegative;
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static void Near(double a, double b, string why) { Check(Math.Abs(a - b) < 1e-8, why + ": " + a + " / " + b); }
    static GameState Start(Track track)
    {
        var g = RaceSimulation.CreateGame("race", track.Id, track.NegativeEnabled);
        RaceSimulation.Press(g, 0); RaceSimulation.Press(g, 1); RaceSimulation.Advance(g, 3); return g;
    }
    static GameState MoveTo(GameState g, double target, double frame, bool stopOnHit = false)
    {
        int old = g.Players[0].Collisions, guard = 0;
        while (g.Phase == "racing" && g.Players[0].Distance < target - 1e-7)
        {
            RaceSimulation.Advance(g, Math.Min(frame, (target - g.Players[0].Distance) / 330));
            if (stopOnHit && g.Players[0].Collisions > old) return Copy(g);
            if (++guard > 1000000) throw new Exception("move stalled");
        }
        return null;
    }
    static GameState Route(Track track, double[] route, double frame, out List<double> times)
    {
        var g = Start(track); times = new List<double>();
        foreach (double x in route)
        {
            MoveTo(g, x, frame); times.Add(g.Elapsed);
            Check(RaceSimulation.Press(g, 0) && RaceSimulation.Press(g, 1), "authored input accepted");
        }
        int guard = 0;
        while (g.Phase == "racing") { RaceSimulation.Advance(g, frame); if (++guard > 1000000) throw new Exception("finish stalled"); }
        return g;
    }
    static SpeedEffect CopyEffect(SpeedEffect e)
    { return e == null ? null : new SpeedEffect { RingId=e.RingId, IsNegative=e.IsNegative, Remaining=e.Remaining, Elapsed=e.Elapsed }; }
    static PlayerState CopyPlayer(PlayerState p)
    {
        var q=new PlayerState(p.Id) { CharacterIndex=p.CharacterIndex, Ready=p.Ready, Distance=p.Distance, Y=p.Y,
            Flipping=p.Flipping, FlipFrom=p.FlipFrom, FlipTo=p.FlipTo, FlipElapsed=p.FlipElapsed, Recovering=p.Recovering,
            Collisions=p.Collisions, FinishTime=p.FinishTime, LastHit=p.LastHit, LastSuccess=p.LastSuccess, LastSlow=p.LastSlow,
            ActiveEffect=CopyEffect(p.ActiveEffect), BoostsEarned=p.BoostsEarned, SlowsEarned=p.SlowsEarned,
            CompletedBoosts=p.CompletedBoosts, CompletedSlows=p.CompletedSlows, CompletedRecoveries=p.CompletedRecoveries,
            RecoveryElapsed=p.RecoveryElapsed };
        q.HitObstacleIds.UnionWith(p.HitObstacleIds); q.CollectedRiskIds.UnionWith(p.CollectedRiskIds);
        q.SuccessfulRiskIds.UnionWith(p.SuccessfulRiskIds); q.FailedRiskIds.UnionWith(p.FailedRiskIds);
        q.NegativeRiskIds.UnionWith(p.NegativeRiskIds);
        foreach (var e in p.PendingEffects) q.PendingEffects.Enqueue(CopyEffect(e));
        return q;
    }
    static GameState Copy(GameState g)
    {
        return new GameState { Mode=g.Mode, Track=g.Track, Phase=g.Phase, Elapsed=g.Elapsed, CountdownRemaining=g.CountdownRemaining,
            Winner=g.Winner, WinnerAt=g.WinnerAt, Players=new[] {CopyPlayer(g.Players[0]),CopyPlayer(g.Players[1])} };
    }
    static void Wait(GameState g, double t)
    {
        while (t > 1e-12 && g.Phase == "racing") { double d=Math.Min(.01,t); RaceSimulation.Advance(g,d);t-=d; }
    }
    static bool Escape(GameState collision, Obstacle next)
    {
        var ready=Copy(collision); var p=ready.Players[0]; int hits=p.Collisions;
        Wait(ready,p.Recovering); Wait(ready,.15);
        if(p.Flipping)Wait(ready,RaceConfig.FlipDuration-p.FlipElapsed);
        if(p.Collisions!=hits)return false;
        double target=next.X+next.Width+RaceConfig.PlayerRadius+1;
        for(int step=0;step<=100;step++)
        {
            var delayed=Copy(ready);Wait(delayed,step*.025);
            if(delayed.Players[0].Collisions!=hits)continue;
            var stay=Copy(delayed);MoveTo(stay,target,.01);
            if(stay.Players[0].Collisions==hits&&stay.Players[0].Distance>=target-1e-7)return true;
            if(!RaceSimulation.Press(delayed,0))continue;
            RaceSimulation.Press(delayed,1);MoveTo(delayed,target,.01);
            if(delayed.Players[0].Collisions==hits&&delayed.Players[0].Distance>=target-1e-7)return true;
        }
        return false;
    }
    static void NegativeBranchRecovery(Track track)
    {
        var baseline=TracksV3.NegativeRoute(track.Id);
        int countBefore=recoverySamples, slowBefore=recoveryActiveSlow, boostBefore=recoveryActiveBoost;
        for(int index=0;index<baseline.Length;index++)
            foreach(double shift in new[]{-176d,-88,-44,44,88,176})
            {
                var route=(double[])baseline.Clone();route[index]+=shift;
                var g=Start(track);GameState captured=null;
                foreach(double x in route)
                {
                    captured=MoveTo(g,x,.01,true);
                    if(captured!=null)break;
                    RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);
                }
                if(captured==null)captured=MoveTo(g,track.Length,.01,true);
                if(captured==null)continue;
                double hitX=double.NegativeInfinity;foreach(var o in track.Obstacles)if(o.Id==captured.Players[0].LastHit.Id)hitX=o.X;
                Obstacle next=null;foreach(var o in track.Obstacles)if(o.X>hitX&&(next==null||o.X<next.X))next=o;
                if(next==null)continue;
                recoverySamples++;
                if(captured.Players[0].SlowRemaining>0)recoveryActiveSlow++;
                if(captured.Players[0].BoostRemaining>0)recoveryActiveBoost++;
                if(captured.Players[0].PendingEffects.Count>0)recoveryQueued++;
                if(captured.Players[0].SlowsEarned>0)recoveryPriorNegative++;
                Check(Escape(captured,next),track.Id+" negative branch forced next hit, action "+index+" offset "+shift+" first "+captured.Players[0].LastHit.Id);
            }
        Console.WriteLine(track.Id+" negative-branch recovery: "+(recoverySamples-countBefore)+" samples; active slow "+(recoveryActiveSlow-slowBefore)+", boost "+(recoveryActiveBoost-boostBefore));
    }
    static void VerifyGeometry(Track off,Track on)
    {
        Check(off.Length==7920&&on.Length==7920&&off.RiskRoute.Length==18&&on.RiskRoute.Length==18,"equal duration and 18 authored actions");
        Check(off.PositiveRingCount==6&&on.PositiveRingCount==6&&on.RiskZones.Length==8&&off.RiskZones.Length==6,"equal 6+/2- budgets");
        Check(off.Obstacles.Length==on.Obstacles.Length,"same obstacles count");
        for(int i=0;i<off.Obstacles.Length;i++){
            var a=off.Obstacles[i];var b=on.Obstacles[i];
            Check(a.Id==b.Id&&a.X==b.X&&a.Width==b.Width&&a.Height==b.Height&&a.Kind==b.Kind,"toggle exact obstacle");
        }
        foreach(var a in off.RiskZones){
            RiskZone b=null;foreach(var r in on.RiskZones)if(r.Id==a.Id)b=r;
            Check(b!=null&&!b.IsNegative&&a.X==b.X&&a.Y==b.Y&&a.Radius==b.Radius&&a.ClearX==b.ClearX
                &&a.Placement==b.Placement&&a.ObstacleId==b.ObstacleId,"toggle exact positive ring");
        }
    }
    public static string Run()
    {
        checks=recoverySamples=recoveryActiveSlow=recoveryActiveBoost=recoveryQueued=recoveryPriorNegative=0;
        foreach(var source in TracksV3.All)
        {
            var off=TracksV3.Get(source.Id,false);var on=TracksV3.Get(source.Id,true);VerifyGeometry(off,on);
            foreach(double dt in new[]{1d/30,1d/60,1d/144,.317,2.5,100})
            {
                foreach(bool negative in new[]{false,true})
                {
                    var track=negative?on:off;
                    var safe=Route(track,track.SafeRoute,dt,out _);
                    Near(safe.Elapsed,36,"physical safe baseline");
                    Check(safe.Players[0].Collisions==0&&safe.Players[0].BoostsEarned==0&&safe.Players[0].SlowsEarned==0,"safe declines every ring");
                    var ideal=Route(track,track.RiskRoute,dt,out _);
                    Near(ideal.Elapsed,34.2,"physical six-positive finish");
                    Check(ideal.Players[0].Collisions==0&&ideal.Players[0].BoostsEarned==6&&ideal.Players[0].SlowsEarned==0,"ideal counts");
                }
                var all=Route(on,TracksV3.NegativeRoute(on.Id),dt,out _);
                Near(all.Elapsed,34.8,"physical all-eight finish");
                foreach(var p in all.Players){
                    Near(p.FinishTime.Value,34.8,"canonical all-eight result");
                    Check(p.BoostsEarned==6&&p.SlowsEarned==2&&p.Collisions==0,"all-eight collection and no hits");
                    Check(p.CompletedBoosts==6&&p.CompletedSlows==2&&p.ActiveEffect==null&&p.PendingEffects.Count==0,"all-eight effect tail fully served");
                }
                Check(all.Players[0].FinishTime==all.Players[1].FinishTime&&Equals(all.Winner,"tie"),"all-eight exact tie");
            }
            var metrics=Route(on,on.RiskRoute,1d/144,out var times);double max=0;
            for(int i=1;i<times.Count;i++)max=Math.Max(max,times[i]-times[i-1]);
            Console.WriteLine(on.Id+" ideal: "+times.Count+" accepted actions; max interior "+max.ToString("F6")+"s; opening "+times[0].ToString("F6")+"s; tail "+(metrics.Elapsed-times[times.Count-1]).ToString("F6")+"s");
            NegativeBranchRecovery(on);
        }
        Check(recoverySamples>100,"independent negative-branch recovery sample coverage");
        Check(recoveryPriorNegative>0,"sampled recovery after actually earned negative effects");
        return "PASS: independent V3 full "+checks+" assertions; "+recoverySamples+" negative-branch recovery samples ("+recoveryPriorNegative+" after earned negatives; active slow "+recoveryActiveSlow+", boost "+recoveryActiveBoost+", queued "+recoveryQueued+"); all actual results/queues/toggles verified.";
    }
}
