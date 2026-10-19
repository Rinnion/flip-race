using System;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    static GameState FullRouteGame(Track track,FlightSettings flight,int kind,double step,out double[] times,
        int shiftIndex=-1,double shift=0,double[] baseline=null)
    {
        var route=kind==0?TracksFull.SafeRoute(track.Id,flight):kind==1?TracksFull.FullRoute(track.Id,flight):TracksFull.NegativeRoute(track.Id,flight);
        var game=Start(track,flight);times=new double[route.Length];
        for(int i=0;i<route.Length;i++)
        {
            if(i==shiftIndex)Advance(game,baseline[i]+shift-game.Elapsed,step);else MoveTo(game,route[i],step);
            times[i]=game.Elapsed;
            if(flight.AirRepeats==AirRepeatLimit.None)foreach(var p in game.Players)Check(p.Support!=SupportKind.None&&!p.Flipping,"Fast input uses real support input="+i);
            Tap(game);
        }
        Advance(game,100,step);
        double expected=kind==0?36:kind==1?33.2:33.5;
        foreach(var p in game.Players)
        {
            Check(p.FinishTime.HasValue&&p.Collisions==0,"real clean finish "+State(p));
            Near(p.Distance,7920,"physical finish line");Near(p.FinishTime.Value,expected,"canonical route result");
            Check(p.BoostsEarned==(kind==0?0:9)&&p.CompletedBoosts==p.BoostsEarned,"ordinary rewards fully served");
            Check(p.FinishBoostsEarned==(kind==0?0:1),"separate final reward");
            Check(p.SlowsEarned==(kind==2?1:0)&&p.CompletedSlows==p.SlowsEarned,"trap ledger fully served");
            Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"empty actual effect state");
            foreach(var zone in track.RiskZones)
                Check(!p.CollectedRiskIds.Contains(zone.Id)||p.SuccessfulRiskIds.Contains(zone.Id)||p.NegativeRiskIds.Contains(zone.Id)||p.FailedRiskIds.Contains(zone.Id),"no retained pending at finish "+zone.Id);
        }
        Near(game.Elapsed,expected,"actual game clock equals result, not only caption");
        return game;
    }
    static void FullAudit()
    {
        int windows=0;
        foreach(var flight in new[]{ExpectedMain,ExpectedFast})foreach(var original in TracksFull.All)
        {
            var track=TracksFull.Get(original.Id,flight);
            Check(track.Length==7920,"fixed baseline length");
            foreach(double step in Steps)foreach(int kind in new[]{0,1,2})
            {
                context="full "+track.Id+" profile="+flight.FirstAction+" kind="+kind+" dt="+step;
                double[] ignored;FullRouteGame(track,flight,kind,step,out ignored);
            }
            context="full safe baseline "+track.Id+" "+flight.FirstAction;
            double[] times;FullRouteGame(track,flight,0,1d/144,out times);
            for(int index=0;index<times.Length;index++)foreach(double shift in new[]{-.06,.06})
            {
                context="full safe clock window "+track.Id+" "+flight.FirstAction+" i="+index+" shift="+shift;
                double[] shifted;FullRouteGame(track,flight,0,1d/144,out shifted,index,shift,times);windows++;
                Near(shifted[index],times[index]+shift,"actual clock shift",2e-8);
            }
            foreach(double step in new[]{1d/144,.317,100})foreach(int winner in new[]{0,1})
                FinishOnlyDuel(track,flight,winner,step);
        }
        Console.WriteLine("PASS independent full routes: "+checks+" assertions / "+games+" games; safe/full/trap both profiles/three tracks/six dt, "+windows+" individual safe±60ms shifts and final-only paired duels.");
    }
    static void FinishOnlyDuel(Track track,FlightSettings flight,int winner,double step)
    {
        context="final-only duel "+track.Id+" "+flight.FirstAction+" winner="+winner+" dt="+step;
        var route=TracksFull.FullRoute(track.Id,flight);var game=Start(track,flight);
        for(int i=0;i<route.Length-1;i++){MoveTo(game,route[i],step);Tap(game);}
        MoveTo(game,route[route.Length-1],step);
        Check(RaceSimulation.Press(game,winner),"one real final tap");
        Advance(game,100,step);
        foreach(var p in game.Players)
        {
            Near(p.FinishTime.Value,p.Id==winner?33.2:33.3,"actual hit/miss final gain");
            Check(p.Collisions==0&&p.BoostsEarned==9&&p.CompletedBoosts==9,"same clean ordinary route");
            Check(p.FinishBoostsEarned==(p.Id==winner?1:0),"only final bonus differs");
            Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"duel ordinary tail empty");
        }
        Near(game.Elapsed,33.3,"loser reaches finish physically");
    }
}
