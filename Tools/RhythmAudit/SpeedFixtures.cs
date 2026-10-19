using System;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    // Deliberate test-only injection. This is NOT an attainable reward history;
    // it isolates the complete fixed geometry at a constant horizontal speed.
    static void HeldSpeedFixture(GameState game,double speed)
    {
        Need(speed==110||speed==330,"only documented constant-speed fixtures");
        foreach(var p in game.Players)
            p.ActiveEffect=new SpeedEffect{RingId="isolated-held-speed",IsNegative=speed==110,Remaining=100,PulseCount=200};
    }
    static void StageSpeedFixtures()
    {
        var track=TracksRhythm.Practice();
        foreach(var f in Profiles)foreach(double speed in new[]{110d,330d})
        {
            // Requested .2s Main correction, with new launch X and actual X/v
            // schedule for each speed. Never rescale the vertical physics.
            var plan=Oracle(track,f,speed,.2);
            foreach(double step in Steps)
            {context=Name(f)+" ISOLATED constant speed="+speed+" dt="+step;Clean(Play(track,f,plan,step,nominal:true,heldFixtureSpeed:speed),track,plan.Count,speed);}
            for(int i=0;i<plan.Count;i++)foreach(double shift in new[]{-.06,.06})
            {context=Name(f)+" ISOLATED speed="+speed+" input="+i+" shift="+shift;Clean(Play(track,f,plan,1d/144,i,shift,heldFixtureSpeed:speed),track,plan.Count,speed);}
            Console.WriteLine(Name(f)+" held speed="+speed+" physical finish="+(track.Length/speed).ToString("F9")+" actions="+plan.Count);
        }
        Console.WriteLine("PASS ISOLATED RH stage constant-speed fixtures: "+checks+" assertions / "+games+" games; 110/330 injected held effect, recomputed geometric schedule, Main correction .2s. Not a normal playable reward route.");
    }
}
