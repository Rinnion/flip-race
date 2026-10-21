using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static void Bypass()
    {
        var rows=new List<object>();
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}) {
            var schedules=new List<(string name,double[] times)>{("stay",Array.Empty<double>()),("one-early-input",new[]{.5}),
                ("early-opposite-wall",flight.FirstAction==FirstAction.Flip?new[]{.5}:new[]{.5,.7})};
            foreach(double period in new[]{.15,.25,.4,.6,.8,1,1.4,2})foreach(double phase in new[]{0d,.25,.5,.75}) {
                var times=new List<double>();for(double t=.1+period*phase;t<45;t+=period)times.Add(t);
                schedules.Add(("period-"+period+"-phase-"+phase,times.ToArray()));
            }
            foreach(var schedule in schedules) {
                var g=RaceSimulation.CreateTrial(TracksMixed.Get(),flight);Tap(g);RaceSimulation.Advance(g,3);int accepted=0;
                foreach(double t in schedule.times){if(g.Phase!="racing")break;AdvanceTime(g,t-g.Elapsed,.05);if(g.Phase!="racing")break;if(Both(g))accepted++;}
                Finish(g,.05);var p=g.Players[0];Check(p.Collisions>0,"bounded simple controller has an actual collision "+schedule.name);
                rows.Add(new{profile=flight.FirstAction==FirstAction.Flip?"fast-flip":"chosen",controller=schedule.name,accepted,p.Collisions,p.FinishTime,p.BoostsEarned,p.SlowsEarned});
            }
        }
        File.WriteAllText("Verification/mixed-course-bypass-r2.json",JsonSerializer.Serialize(rows,Json));
        Console.WriteLine("BYPASS PASS "+rows.Count+" complete symmetric games; stay/early-opposite wall/32 periodic schedules, no clean route; finite catalogue only.");
    }
}
