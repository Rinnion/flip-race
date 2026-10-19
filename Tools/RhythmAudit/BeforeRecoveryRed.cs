using System;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    // Immutable R2 JSON witness. This historical failure must not silently
    // switch to a newer production Draft or a corrected module export.
    static void BeforeRecoveryRed()
    {
        Need(CourseFile=="rhythm-course-r2.json","historical R2 source selected");
        CourseWitness w=null;foreach(var candidate in ReadCourses())
            if(candidate.Track.Id=="rhythm-dense"&&candidate.Flight==FlightPresets.FastFlip&&candidate.Purpose=="safe")w=candidate;
        Need(w!=null,"historical Fast Dense safe witness exists");context="historical R2 before330 late hit";
        var baseline=ObserveCourse(w,1d/144);Need(CourseGoal(baseline,w),"unperturbed safe witness clean");
        var hit=CourseFirstHit(w,baseline.Actions.ConvertAll(a=>a.Time).ToArray(),8,-.64);
        Need(hit!=null&&hit.Players[0].LastHit.Id=="rhythm-dense-m6-stone","actual intended first impact");
        var reaction=Clone(hit);Advance(reaction,reaction.Players[0].Recovering+.15,.001);
        var p=reaction.Players[0];Need(p.Collisions==1&&p.Support==SupportKind.Wall&&p.SupportSide==0,"actual ceiling after reaction");
        Near(RaceSimulation.GetPlayerCenterY(p),14,"ceiling Y");Near(p.VerticalSpeed,0,"resting VY");Near(p.CurrentSpeed,220,"no effect");
        Need(p.AirFlipAvailable,"legal departure and one repeat available");
        Obstacle gate=Array.Find(w.Track.Obstacles,o=>o.Id=="rhythm-dense-m6-exit-gate");Need(gate!=null,"linked module exit gate exists");
        double front=gate.X+gate.TopOffsetX-Radius,requiredY=gate.GapTop+Radius;
        double required=requiredY-14;
        double minimum=(Math.Sqrt(35*35+2*2700*required)-35)/2700;
        double available=(front-p.Distance)/220;
        Need(available>0&&minimum>available,"analytic fastest monotone descent cannot clear first band");
        Console.WriteLine("ACTUAL hitX="+hit.Players[0].Distance.ToString("R")+" hitY="+RaceSimulation.GetPlayerCenterY(hit.Players[0]).ToString("R")
            +" reactionX="+p.Distance.ToString("R")+" firstBandFront="+front+" requiredY="+requiredY);
        Console.WriteLine("BOUND available="+available.ToString("R")+" fastest="+minimum.ToString("R")+" earliestSafeGateBase="+(p.Distance+Radius+220*minimum).ToString("R"));
        Need(TapBoth(reaction),"fastest possible immediate departure accepted");
        Advance(reaction,available+.000001,.0001);
        foreach(var player in reaction.Players)
            Need(player.Collisions==2&&player.LastHit.Id==gate.Id,"even fastest legal path actually hits next distinct gate");
        // Any later departure has less time. The sole airborne repeat reverses
        // acceleration and cannot beat this downward displacement envelope.
        Console.WriteLine("EXPECTED RED confirmed: both players received the second distinct-obstacle hit at X="+p.Distance.ToString("R")
            +" Y="+RaceSimulation.GetPlayerCenterY(p).ToString("R")+"; "+checks+" assertions / "+games+" real games. Historical R2, not a verdict on a replacement.");
    }
}
