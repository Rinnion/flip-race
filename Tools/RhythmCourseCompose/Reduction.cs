using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    sealed class ReducedRun
    {
        public GameState Game;
        public int Accepted, Rejected;
        public bool Good;
    }
    static ReducedRun ReducedReplay(Course course,FlightSettings flight,bool full,IEnumerable<PlannedInput> route)
    {
        var game=RaceSimulation.CreateTrial(course.Track,flight);RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);RaceSimulation.Advance(game,3);
        var r=new ReducedRun{Game=game};
        foreach(var input in route) {
            MoveTo(game,input.X,.025);
            bool a=RaceSimulation.Press(game,0),b=RaceSimulation.Press(game,1);
            if(a&&b)r.Accepted++;else r.Rejected++;
        }
        Finish(game,.1);
        r.Good=r.Rejected==0&&game.Players.All(p=>p.Collisions==0&&p.BoostsEarned==(full?9:0)
            &&p.SlowsEarned==0&&p.FinishBoostsEarned==(full?1:0)&&Math.Abs(p.FinishTime.Value-(full?33.2:36))<1e-7);
        return r;
    }
    static void Reduction(Course[] courses)
    {
        var rows=new List<object>();int runs=0;
        foreach(var c in courses)foreach(var f in Profiles)foreach(bool full in new[]{false,true}) {
            var original=c.Routes[RouteKey(f,full)];var single=new List<object>();
            for(int i=0;i<original.Count;i++) {
                var route=original.Where((_,j)=>j!=i).ToList();var result=ReducedReplay(c,f,full,route);runs++;
                var p=result.Game.Players[0];single.Add(new{input=i,removedX=original[i].X,result.Good,result.Accepted,result.Rejected,
                    p.Collisions,p.BoostsEarned,p.FinishBoostsEarned,p.FinishTime});
            }
            var reduced=new List<PlannedInput>(original);bool changed=true;
            while(changed) {
                changed=false;
                for(int count=1;count<=4&&!changed;count++)for(int at=0;at+count<=reduced.Count&&!changed;at++) {
                    var trial=new List<PlannedInput>(reduced);trial.RemoveRange(at,count);
                    var result=ReducedReplay(c,f,full,trial);runs++;
                    if(result.Good){reduced=trial;changed=true;}
                }
            }
            rows.Add(new{id=c.Id,profile=ProfileId(f),variant=full?"full":"safe",original=original.Count,reduced=reduced.Count,
                reducedRoute=reduced.Select(i=>i.X),singleOmissions=single,
                scope="finite greedy contiguous deletion of1..4 inputs; actual Press/Advance; not a global minimum proof"});
            Console.WriteLine(c.Id+" "+ProfileId(f)+" "+(full?"full":"safe")+" reduction "+original.Count+" -> "+reduced.Count+"; single clean deletions="+single.Count(o=>(bool)o.GetType().GetProperty("Good").GetValue(o)));
        }
        File.WriteAllText("Verification/rhythm-course-"+Revision+"-reduction.json",JsonSerializer.Serialize(rows,Json));
        Console.WriteLine("REDUCTION COMPLETE "+runs+" production runs / "+checks+" scheduler checks. Results are finite probes, not minimum guarantees.");
    }
}
