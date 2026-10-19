using System;
using FlipRace.Core;
using TwoModesAuditFixtures;

public static partial class TwoModesIndependentAudit
{
    static string State(PlayerState p)
    { return "x="+p.Distance.ToString("R")+" y="+RaceSimulation.GetPlayerCenterY(p).ToString("R")
        +" vy="+p.VerticalSpeed.ToString("R")+" earned="+p.BoostsEarned+" boost="+p.BoostRemaining.ToString("R")
        +" hits="+p.Collisions+" finishBoosts="+p.FinishBoostsEarned; }
    static void LateTailRegression()
    {
        context="historical D3 review late-after regression";
        var track=ReviewTracksFixture.Get(ReviewTracksFixture.LongId);var game=Start(track,ExpectedMain);
        RiskZone after=null;
        foreach(var zone in track.RiskZones)if(!zone.IsNegative&&!zone.IsInstant&&zone.AfterObstacle)after=zone;
        foreach(double x in track.RiskRoute)if(x<=after.X-20+1e-8){MoveTo(game,x,1d/144);Tap(game);}
        Advance(game,.55,1d/144);
        bool awardedBeforeHover=game.Players[0].SuccessfulRiskIds.Contains(after.Id);
        Console.WriteLine("After old collection segment "+State(game.Players[0])+" confirmed="+awardedBeforeHover);
        int taps=0,guard=0;
        while(game.Players[0].Distance<7600-1e-8&&game.Phase=="racing")
        {
            var p=game.Players[0];
            if((p.Gravity>0&&p.VerticalSpeed>=50-1e-8)||(p.Gravity<0&&p.VerticalSpeed<=-50+1e-8)){Tap(game);taps++;}
            p=game.Players[0];double nextV=p.Gravity>0?50:-50;
            double turn=(nextV-p.VerticalSpeed)/(p.Gravity*662.5);
            double dt=Math.Min(1d/144,Math.Min(turn,(7600-p.Distance)/440));
            Advance(game,dt<=1e-12?1e-10:dt,1d/144);
            if(++guard>100000)throw new Exception("hover guard");
        }
        Console.WriteLine("Late hover "+State(game.Players[0])+" realAirTaps="+taps);
        if(game.Players[0].Gravity!=-1)Tap(game);
        while(game.Players[0].Support==SupportKind.None&&game.Phase=="racing")Advance(game,.001,1d/144);
        Console.WriteLine("Late wall "+State(game.Players[0]));
        double time=(350-Math.Sqrt(350*350-2*662.5*46))/662.5;
        MoveTo(game,7876-game.Players[0].CurrentSpeed*time,1d/144);Tap(game);MoveTo(game,7876,1d/144);
        Advance(game,1e-6,1d/144);
        Console.WriteLine("Finish target "+State(game.Players[0]));
        // Assertions are deliberately after the trace so the unchanged old Core
        // produces a useful RED without altering its historical evidence files.
        foreach(var p in game.Players)
        {
            Check(awardedBeforeHover,"after confirmed at geometry, before long airborne hover");
            Check(p.Collisions==0&&p.BoostsEarned==9&&p.FinishBoostsEarned==1,"old attainable path remains clean with all rewards");
            Check(p.ActiveEffect==null&&p.PendingEffects.Count==0&&p.CompletedBoosts==9,"all ordinary work naturally ends before target");
        }
        Advance(game,100,.317);
        foreach(var p in game.Players)Near(p.FinishTime.Value,33.2,"real complete elapsed after clean-entry fix");
        Console.WriteLine("PASS historical late-tail regression: "+checks+" assertions / "+games+" games; no time limit, no erased effects, no flight-state injection.");
    }
}
