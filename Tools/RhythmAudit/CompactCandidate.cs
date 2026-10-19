using System;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static Track CompactCandidateFixture(int gate,int pitch)
    {
        var old=InitialStageFixture();double delta=pitch-(old.Obstacles[gate+1].X-old.Obstacles[gate].X);
        for(int i=0;i<old.Obstacles.Length;i++)
        {
            var o=old.Obstacles[i];o.GapTop=110;o.GapBottom=130;
            o.TopOffsetX=i%2==0?0:112;o.BottomOffsetX=i%2==0?112:0;
            if(i>gate)o.X+=delta;
        }
        return new Track(old.Length+delta,old.Obstacles){Id="isolated-compact112-candidate"};
    }
    static void CompactRecovery()
    {
        foreach(var f in Profiles)foreach(int gate in new[]{3,4})
        {
            int changed=f.FirstAction==FirstAction.Jump?gate*2+1:gate;
            int firstFound=-1;
            for(int pitch=230;pitch<=440;pitch+=10)
            {
                var track=CompactCandidateFixture(gate,pitch);context=Name(f)+" compact112 gate="+gate+" pitch="+pitch;
                var hit=PerturbedHit(track,f,Oracle(track,f),changed,.64);
                Need(hit!=null,"actual delayed action produces hit");
                Need(hit.Players[0].LastHit.Id=="rhythm-practice-gate-"+gate,"same selected gate hit before shifted suffix");
                var reaction=Clone(hit);Advance(reaction,reaction.Players[0].Recovering+.15,.001);
                var p=reaction.Players[0];
                if(pitch==230)Console.WriteLine(context+" hitX="+hit.Players[0].Distance.ToString("R")+" reactionX="+p.Distance.ToString("R")
                    +" Y="+RaceSimulation.GetPlayerCenterY(p).ToString("R")+" VY="+p.VerticalSpeed.ToString("R")+" support="+p.Support);
                string witness;bool found=Escape(hit,out witness);
                Console.WriteLine(context+" found="+found+(found?" "+witness:""));
                if(found){firstFound=pitch;break;}
            }
            Need(firstFound>=0,"bounded compact pitch search found legal recovery");
        }
        Console.WriteLine("PASS compact112 candidate recovery witnesses. Geometry fixture only; no production edits. Actual late-action states; remaining penalty+150ms before controls. First found on10-unit pitch grid, finite search, not global minimum. Main nominal repeat.18s; no compulsory80ms route introduced.");
    }
    static void CompactProposed()
    {
        var track=CompactCandidateFixture(4,280);
        double[] positions={300,900,1500,1860,2230,2590,2960,3320};
        for(int i=0;i<positions.Length;i++)track.Obstacles[i].X=positions[i];
        track.Length=3720;track.Id="isolated-compact112-proposed-series";
        Console.WriteLine("Proposed positions="+string.Join(",",positions)+"; isolated geometry fixture, not production stage2 freeze.");
        foreach(var f in Profiles)
        {
            var plan=Oracle(track,f);
            foreach(double step in Steps)Clean(Play(track,f,plan,step,nominal:true),track,plan.Count);
        }
        Recovery(track);
    }
}
