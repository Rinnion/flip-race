using System;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    static Track GateFixture(double top,double bottom)
    {return new Track(2200,new[]{new Obstacle{Id="independent-gate",Kind="gate",X=1000,Width=8,GapTop=top,GapBottom=bottom}});}
    static void GateFinish(GameState game,double step,int hits)
    {
        Advance(game,100,step);
        foreach(var p in game.Players)
        {
            Check(p.Collisions==hits,"expected physical gate contact count actual="+p.Collisions+" expected="+hits);
            Near(p.FinishTime.Value,10+hits*.5,"gate penalty changes actual finish");
        }
        Near(game.Elapsed,10+hits*.5,"physical game clock at finish");
    }
    static void OpenGateAudit()
    {
        foreach(var flight in new[]{ExpectedMain,ExpectedFast})foreach(int side in new[]{0,1})foreach(double step in Steps)
        {
            context="zero-band wall pass "+flight.FirstAction+" side="+side+" dt="+step;
            var track=side==0?GateFixture(0,126):GateFixture(114,240);
            GateFinish(Prepare(track,flight,side,step),step,0);
            context="remaining opposite band "+flight.FirstAction+" side="+side+" dt="+step;
            GateFinish(Prepare(track,flight,1-side,step),step,1);
            context="nonzero thin band "+flight.FirstAction+" side="+side+" dt="+step;
            track=side==0?GateFixture(1e-6,126):GateFixture(114,240-1e-6);
            GateFinish(Prepare(track,flight,side,step),step,1);
            context="fully open wall and actual flight "+flight.FirstAction+" side="+side+" dt="+step;
            track=GateFixture(0,240);var game=Prepare(track,flight,side,step);
            MoveTo(game,950,step);Tap(game);GateFinish(game,step,0);
        }
        foreach(int side in new[]{0,1})foreach(double step in Steps)foreach(double shift in new[]{-1e-6,0d,1e-6})
        {
            context="remaining-band true apex tangent side="+side+" shift="+shift+" dt="+step;
            double apex=350*350/(2*662.5),edge=226-apex-14+shift;
            var track=side==1?GateFixture(edge,240):GateFixture(0,240-edge);
            var game=Prepare(track,ExpectedMain,side,step);
            MoveTo(game,1004-220*350/662.5,step);Tap(game);
            GateFinish(game,step,shift>=0?1:0);
        }
        foreach(int side in new[]{0,1})foreach(double step in Steps)foreach(double shift in new[]{-.001,0d,.001})
        {
            context="remaining-band rear corner fast side="+side+" shift="+shift+" dt="+step;
            var track=side==1?GateFixture(112,240):GateFixture(0,128);
            var game=Prepare(track,ExpectedFast,side,step);
            // Reach the expanded horizontal rear face exactly as the center
            // reaches the non-empty band's vertical edge, from the open gap.
            double launch=1022+shift-220*FlipTime(100);
            Advance(game,(launch-game.Players[0].Distance)/220,step);Tap(game);
            GateFinish(game,step,shift<=0?1:0);
        }
        Console.WriteLine("PASS independent open gates: "+checks+" assertions / "+games+" games; both walls/profiles/six dt, zero vs tiny bands, real apex tangent and rear-corner contact.");
    }
}
