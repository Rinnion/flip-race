using System;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    static void Analytics()
    {
        context="analytic constants";
        Near(FlipTime(212),.38352798957670875,"fast exact wall-to-wall time",2e-12);
        double oldHalf=FlipTime(98)-FlipTime(4);
        double oldMiddle=FlipTime(152)-FlipTime(60);
        Check(220*oldHalf<48,"old half passage impossible at base speed");
        Check(330*oldMiddle<48,"old middle passage impossible even on boost");
        double candidate=FlipTime(200)-FlipTime(12);
        Check((candidate-36/220d)/2>.06,"wide middle candidate analytic window exceeds60ms at220");
        Check(110*candidate<36,"same candidate not guaranteed at110 slow");
        Console.WriteLine("Fast T212="+FlipTime(212).ToString("R")+" T164="+FlipTime(164).ToString("R")
            +" T120="+FlipTime(120).ToString("R")+"; candidate window="+((candidate-36/220d)/2).ToString("R"));
        foreach(int side in new[]{0,1})foreach(double step in Steps)
        {
            context="fast free side="+side+" dt="+step;
            var game=Prepare(new Track(22000,Array.Empty<Obstacle>()),ExpectedFast,side,step);
            double began=game.Elapsed; Tap(game);
            foreach(var p in game.Players)
            { Near(p.VerticalSpeed,Away(side)*35,"departure35");Check(p.Gravity==Away(side),"gravity toward destination"); }
            double duration=FlipTime(Span);
            Advance(game,duration/2,step);
            double expectedV=Away(side)*(35+2700*duration/2);
            double expectedY=Wall(side)+Away(side)*(35*duration/2+.5*2700*duration*duration/4);
            foreach(var p in game.Players)
            { Near(p.VerticalSpeed,expectedV,"analytic midflight velocity");Near(RaceSimulation.GetPlayerCenterY(p),expectedY,"analytic midflight height"); }
            for(int repeat=0;repeat<16;repeat++)foreach(var p in game.Players)
            { Check(!RaceSimulation.Press(game,p.Id),"all air inputs rejected");Near(p.VerticalSpeed,expectedV,"rejected input preserves velocity"); }
            Advance(game,duration/2-1e-6,step);
            foreach(var p in game.Players)Check(!RaceSimulation.Press(game,p.Id),"input just before landing rejected");
            Advance(game,1e-6+1e-9,step);
            foreach(var p in game.Players)
            {
                Check(p.Support==SupportKind.Wall&&p.SupportSide==1-side&&!p.Flipping,"opposite real wall contact");
                Check(p.LastFlight!=null&&!p.LastFlight.Interrupted,"clean measured flight");
                Near(p.LastFlight.Duration,duration,"measured physical duration");Near(p.LastFlight.MaxDistance,212,"full center span");
                Near(p.LastFlight.EndedAt,began+duration,"analytic contact timestamp");
            }
            Tap(game);Advance(game,duration+1e-9,step);
            foreach(var p in game.Players)Check(p.SupportSide==side&&!p.Flipping&&p.Collisions==0,"new tap allowed only after true support");

            context="main jump side="+side+" dt="+step;
            game=Prepare(new Track(22000,Array.Empty<Obstacle>()),ExpectedMain,side,step);
            Tap(game);Advance(game,350/662.5,step);
            foreach(var p in game.Players)
            { Near(p.VerticalSpeed,0,"main apex");Near(Math.Abs(RaceSimulation.GetPlayerCenterY(p)-Wall(side)),350*350/(2*662.5),"main apex height"); }
            for(int repeat=0;repeat<32;repeat++)Tap(game);
            Advance(game,350/662.5+1e-9,step);
            foreach(var p in game.Players)
            { Check(p.SupportSide==side&&!p.Flipping,"main returns after even unlimited repeats");Near(p.LastFlight.Duration,700/662.5,"one continuous measured jump"); }

            context="inactive fast parameters side="+side+" dt="+step;
            var hidden=new FlightSettings(FirstAction.Flip,MotionLaw.Gravity,AirRepeatLimit.None,2700,35,950,1020,54);
            var a=Prepare(new Track(22000,Array.Empty<Obstacle>()),ExpectedFast,side,step);
            var b=Prepare(new Track(22000,Array.Empty<Obstacle>()),hidden,side,step);
            Tap(a);Tap(b);Advance(a,.217,step);Advance(b,.217,step);
            foreach(var p in a.Players)
            { Near(p.VerticalSpeed,b.Players[p.Id].VerticalSpeed,"hidden values do not change active gravity velocity");
              Near(p.Y,b.Players[p.Id].Y,"hidden values do not change active gravity position"); }
        }
        Console.WriteLine("PASS independent production-profile analytics: "+checks+" assertions / "+games+" games. Menu and new geometry are not certified by this scope.");
    }
}
