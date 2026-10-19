using System;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    static bool CompleteShiftedFull(Track track,FlightSettings flight,double[] route,double[] times,int changed,double shift)
    {
        var game=Start(track,flight);
        for(int i=0;i<route.Length&&game.Phase=="racing";i++)
        {
            if(i==changed)
            {double dt=times[i]+shift-game.Elapsed;if(dt<0)return false;Advance(game,dt,1d/144);}
            else
            {if(route[i]<game.Players[0].Distance-2e-6)return false;MoveTo(game,route[i],1d/144);}
            if(!RaceSimulation.Press(game,0)||!RaceSimulation.Press(game,1))return false;
        }
        Advance(game,100,1d/144);
        foreach(var p in game.Players)
            if(!p.FinishTime.HasValue||Math.Abs(p.FinishTime.Value-33.2)>5e-7||p.Collisions!=0||p.SlowsEarned!=0
                ||p.BoostsEarned!=9||p.CompletedBoosts!=9||p.FinishBoostsEarned!=1||p.ActiveEffect!=null||p.PendingEffects.Count!=0)return false;
        return Math.Abs(game.Elapsed-33.2)<5e-7;
    }
    static void FullRewardWindows()
    {
        int shiftedRuns=0;
        foreach(var flight in new[]{ExpectedMain,ExpectedFast})foreach(var source in TracksFull.All)
        {
            context="full window baseline "+source.Id+" "+flight.FirstAction;
            var track=TracksFull.Get(source.Id,flight);var route=TracksFull.FullRoute(source.Id,flight);
            double[] times;FullRouteGame(track,flight,1,1d/144,out times);
            double minEarly=1,minLate=1;
            for(int index=0;index<route.Length;index++)
            {
                double early=0,late=0;
                foreach(int direction in new[]{-1,1})
                {
                    for(int tick=1;tick<=60;tick++)
                    {
                        double shift=direction*tick*.005;
                        context="full window "+source.Id+" "+flight.FirstAction+" input="+index+" shift="+shift;
                        shiftedRuns++;
                        if(!CompleteShiftedFull(track,flight,route,times,index,shift))break;
                        if(direction<0)early=tick*.005;else late=tick*.005;
                    }
                }
                Console.WriteLine("WINDOW "+source.Id+" "+flight.FirstAction+" input="+index+" X="+route[index].ToString("R")+" actualTime="+times[index].ToString("R")+" early="+early+" late="+late+(index==route.Length-1?" final":""));
                if(index<route.Length-1){minEarly=Math.Min(minEarly,early);minLate=Math.Min(minLate,late);}
                Check(early>0&&late>0,"every full input has a measured nonzero neighborhood");
            }
            Console.WriteLine("ORDINARY WINDOW MIN "+source.Id+" "+flight.FirstAction+" early="+minEarly+" late="+minLate);
        }
        Console.WriteLine("PASS measured full-input windows: "+shiftedRuns+" shifted full-route simulations; 5ms grid, one input at a time, stop at first failed sample, cap300ms per side. Final target reported separately.");
    }
}
