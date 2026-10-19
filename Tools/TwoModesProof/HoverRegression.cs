using System;
using FlipRace.Core;
internal static partial class Program
{
    static void HoverRegression()
    {
        foreach(double frame in new[]{1d/144,.317,100}) {
            var track=LegacyFullTracks.Get(LegacyFullTracks.LongId);var g=Begin(track,FlightPresets.Chosen);
            RiskZone after=null;foreach(var z in track.RiskZones)if(z.AfterObstacle)after=z;
            foreach(double x in track.RiskRoute)if(x<=after.X-20+1e-8){MoveTo(g,x,frame);Tap(g);}
            AdvanceTime(g,.55,frame);var p=g.Players[0];
            Check(p.SuccessfulRiskIds.Contains(after.Id)&&p.Flipping,"historical after already confirmed before unlimited hovering");
            int taps=0,guard=0;
            while(p.Distance<7600-1e-8){
                if((p.Gravity>0&&p.VerticalSpeed>=50-1e-8)||(p.Gravity<0&&p.VerticalSpeed<=-50+1e-8)){Tap(g);taps++;}
                double target=p.Gravity>0?50:-50;
                double untilTurn=(target-p.VerticalSpeed)/(p.Gravity*662.5);
                double dt=Math.Min(frame,Math.Min(untilTurn,(7600-p.Distance)/440));
                if(dt<=1e-12)dt=1e-10;
                AdvanceTime(g,dt,frame);if(++guard>100000)throw new Exception("historical hover controller stalled");
            }
            if(p.Gravity!=-1)Tap(g);
            while(p.Support==SupportKind.None)AdvanceTime(g,.001,frame);
            MoveTo(g,7876-220*(350-Math.Sqrt(350*350-2*662.5*46))/662.5,frame);Tap(g);
            MoveTo(g,7876,frame);
            Check(p.Collisions==0&&p.CompletedBoosts==9&&p.ActiveEffect==null&&p.PendingEffects.Count==0,
                "historical real hover route reaches finish plane with every ordinary effect fully served");
            Finish(g,frame);CheckClean(new RouteResult{Game=g},10,0,33.2);
            Console.WriteLine("Historical immutable D3 hover GREEN: "+taps+" real air taps, dt"+frame+", 9 ordinary fully served, finish33.2 / 0hits.");
        }
        Console.WriteLine("MODES hover regression PASS "+checks+" checks");
    }
}
