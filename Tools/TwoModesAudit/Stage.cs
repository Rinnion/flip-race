using System;
using System.Collections.Generic;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    static GameState StageRoute(FlightSettings flight,bool full,double step,out double[] times,int shiftIndex=-1,double shift=0,double[] baselineTimes=null)
    {
        var track=TracksFull.Practice(flight);
        var route=full?TracksFull.FullRoute(track.Id,flight):TracksFull.SafeRoute(track.Id,flight);
        var game=Start(track,flight);times=new double[route.Length];
        for(int i=0;i<route.Length;i++)
        {
            if(i==shiftIndex)Advance(game,baselineTimes[i]+shift-game.Elapsed,step);
            else MoveTo(game,route[i],step);
            times[i]=game.Elapsed;
            foreach(var p in game.Players)
                if(IsFastProfile(flight))Check(p.Support!=SupportKind.None&&!p.Flipping,"Fast stage input requires real support i="+i);
            Tap(game);
        }
        Advance(game,100,step);
        foreach(var p in game.Players)
        {
            Check(p.FinishTime.HasValue&&p.Collisions==0&&p.SlowsEarned==0,"clean stage finish");
            Near(p.FinishTime.Value,full?13.5:15,"stage physical finish");
            Check(p.BoostsEarned==(full?5:0)&&p.CompletedBoosts==p.BoostsEarned,"all intended stage rewards served");
            Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"stage FIFO empty");
            if(full)Check(p.SuccessfulRiskIds.Contains("practice-before")&&p.SuccessfulRiskIds.Contains("practice-after")&&p.SuccessfulRiskIds.Contains("practice-pad"),"authored pad and both linked rewards");
        }
        return game;
    }
    static void StageAudit()
    {
        foreach(var flight in new[]{ExpectedMain,ExpectedFast})
        {
            foreach(double step in Steps)foreach(bool full in new[]{false,true})
            {
                context="stage route "+flight.FirstAction+" full="+full+" dt="+step;
                double[] ignored;StageRoute(flight,full,step,out ignored);
            }
            context="stage safe clock windows "+flight.FirstAction;
            double[] baseline;StageRoute(flight,false,1d/144,out baseline);
            for(int i=0;i<baseline.Length;i++)foreach(double offset in new[]{-.06,.06})
            {
                context="stage safe actual-time window "+flight.FirstAction+" input="+i+" offset="+offset;
                double[] shifted;StageRoute(flight,false,1d/144,out shifted,i,offset,baseline);
                Near(shifted[i],baseline[i]+offset,"actual clock shift applied",2e-8);
            }
            foreach(double step in Steps)
            {
                context="stage natural walkoff "+flight.FirstAction+" dt="+step;
                var track=TracksFull.Practice(flight);var route=TracksFull.FullRoute(track.Id,flight);
                var game=Start(track,flight);MoveTo(game,route[0],step);Tap(game);MoveTo(game,route[1],step);Tap(game);
                MoveTo(game,732,step);Advance(game,1e-8,step);
                foreach(var p in game.Players)Check(p.Support==SupportKind.Stone&&p.SupportObstacleId=="practice-pad-stone"&&p.BoostsEarned==1,"true narrow centered landing");
                MoveTo(game,763.9999,step);
                foreach(var p in game.Players)Check(p.Support==SupportKind.Stone,"support to trailing edge");
                MoveTo(game,764.0001,step);Advance(game,.15,step);
                foreach(var p in game.Players)Check(p.Support==SupportKind.None&&p.Collisions==0,"natural fall still safe after150ms reaction");
                if(IsFastProfile(flight)){
                    // Preserve the untouched natural-walkoff witness; exercise the optional new repeat on a deep snapshot.
                    var branch=CopyGame(game);
                    foreach(var p in branch.Players){
                        double velocity=p.VerticalSpeed;
                        Check(RaceSimulation.Press(branch,p.Id),"one real air repeat available after narrow walkoff");
                        Near(p.VerticalSpeed,velocity,"walkoff repeat preserves falling velocity");
                        Check(!RaceSimulation.Press(branch,p.Id),"second walkoff air repeat rejected");
                    }
                }
                MoveTo(game,1000,step);
                foreach(var p in game.Players)Check(p.Support==SupportKind.Wall&&p.SupportSide==1&&p.Collisions==0,"safe true wall landing without timed rescue input");
            }
            foreach(int which in new[]{0,1,2})foreach(double step in Steps)
            {
                context="stage independent chain "+flight.FirstAction+" subset="+which+" dt="+step;
                var track=TracksFull.Practice(flight);var full=TracksFull.FullRoute(track.Id,flight);var game=Start(track,flight);
                MoveTo(game,full[0],step);Tap(game);MoveTo(game,full[1],step);Tap(game);
                var taps=flight.FirstAction==FirstAction.Jump
                    ? (which==0?new[]{1100d,1180}:which==1?new[]{1100d}:new[]{1170d,1225})
                    : (which==0?new[]{1118d,1242}:which==1?new[]{1118d}:new[]{1230d});
                foreach(double x in taps)
                {
                    MoveTo(game,x,step);
                    if(IsFastProfile(flight))foreach(var p in game.Players)Check(p.Support!=SupportKind.None,"independent-chain Fast tap only from actual support");
                    Tap(game);
                }
                MoveTo(game,1500,step);
                foreach(var p in game.Players)
                {
                    Check(p.SuccessfulRiskIds.Contains("practice-chain-1")== (which!=2),"first chain reward independent");
                    Check(p.SuccessfulRiskIds.Contains("practice-chain-2")== (which!=1),"second chain reward independent");
                    Check(p.Collisions==0&&p.SlowsEarned==0,"clean independent chain segment");
                }
            }
        }
        Console.WriteLine("PASS independent stage: "+checks+" assertions / "+games+" games; six dt, individual safe±60ms real clock, narrow natural walkoff and independent chain subsets.");
    }
}
