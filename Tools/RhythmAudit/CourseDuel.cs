using System;
using System.Collections.Generic;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static double CourseExpected(CourseWitness w)
    {return w.Purpose=="safe"?36:33.2+(w.Purpose=="trap"?.3*w.ExpectedNegative:0);}
    static void Duel(CourseWitness upper,CourseWitness lower,double step)
    {
        var witnesses=new[]{upper,lower};var g=Start(upper.Track,upper.Flight);var next=new[]{0,0};int guard=0;
        while(g.Phase=="racing")
        {
            bool acted=false;double dt=step;
            for(int id=0;id<2;id++)
            {
                var p=g.Players[id];var inputs=witnesses[id].Inputs;if(p.FinishTime.HasValue||next[id]>=inputs.Length)continue;
                double remaining=inputs[next[id]]-p.Distance;
                if(remaining<=1e-7){Need(RaceSimulation.Press(g,id),"mixed-player planned input accepted");next[id]++;acted=true;}
                else dt=Math.Min(dt,Math.Max(1e-8,remaining/440));
            }
            if(!acted)RaceSimulation.Advance(g,dt);
            if(++guard>500000||g.Elapsed>80)throw new Exception("Duel scheduler guard");
        }
        for(int id=0;id<2;id++)
        {
            var p=g.Players[id];var w=witnesses[id];int ordinary=w.Purpose=="safe"?0:9,negative=w.Purpose=="trap"?w.ExpectedNegative:0;
            Need(p.FinishTime.HasValue&&p.Collisions==0,"each mixed player physically finishes without impact");
            Near(p.FinishTime.Value,CourseExpected(w),"independent finish clock",2e-6);Near(p.Distance,7920,"independent finish distance");
            Need(p.BoostsEarned==ordinary&&p.CompletedBoosts==ordinary&&p.SlowsEarned==negative&&p.CompletedSlows==negative,"independent ordinary ledger");
            Need(p.FinishBoostsEarned==(w.Purpose=="safe"?0:1)&&!p.FinishBoost,"independent finish reward");
            Need(p.ActiveEffect==null&&p.PendingEffects.Count==0&&NoPendingRings(p),"independent empty effects and linked pending");
            Need(next[id]==w.Inputs.Length,"all and only that player's planned actions consumed");
        }
        double a=CourseExpected(upper),b=CourseExpected(lower);Near(g.Elapsed,Math.Max(a,b),"both finish before grace",2e-6);
        Need(a==b?(g.Winner is string&&(string)g.Winner=="tie"):(g.Winner is int&&(int)g.Winner==(a<b?0:1)),"winner follows actual independent clock");
    }
    static void CoursesDuels()
    {
        var routes=ReadCourses();int count=0;
        foreach(var a in routes)foreach(var b in routes)
        {
            if(a.Track.Id!=b.Track.Id||a.Flight!=b.Flight||a.Purpose==b.Purpose)continue;
            foreach(double step in new[]{1d/144,.317,100})
            {
                context=a.Track.Id+" "+Name(a.Flight)+" upper="+a.Purpose+" lower="+b.Purpose+" dt="+step;
                Duel(a,b,step);count++;
            }
        }
        Need(count>=36,"mixed purpose pairs present for all tracks/profiles");
        Console.WriteLine("PASS independent mixed-player course races: "+checks+" assertions / "+count+" actual games, both slot assignments, three dt; each player's distance, clock, input and reward/effect ledgers checked separately.");
    }
}
