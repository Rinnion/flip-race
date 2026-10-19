using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    sealed class CourseRun
    {
        public RouteResult Result;
        public List<object> Boundaries=new List<object>();
    }
    static CourseRun Replay(Course c,FlightSettings flight,bool full,double step,int changed=-1,double shift=0,CourseRun baseline=null,int omit=-1,bool checkBoundaries=true)
    {
        var game=RaceSimulation.CreateTrial(c.Track,flight);
        Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"both ready");RaceSimulation.Advance(game,3);
        var r=new CourseRun{Result=new RouteResult{Game=game}};var route=c.Routes[RouteKey(flight,full)];
        int input=0,boundary=0,rewards=0;
        while(input<route.Count || boundary<c.Modules.Count) {
            double inputX=input<route.Count?route[input].X:double.PositiveInfinity;
            double boundaryX=boundary<c.Modules.Count?c.Modules[boundary].EndX:double.PositiveInfinity;
            if(boundaryX<inputX) {
                var m=c.Modules[boundary++];MoveTo(game,boundaryX,step);
                if(full)rewards+=m.Module.zones.Length;
                if(checkBoundaries)foreach(var p in game.Players) {
                    Check(p.Collisions==0,"clean module exit "+m.Id+" "+string.Join(',',p.HitObstacleIds));
                    Check(p.Support==SupportKind.Wall&&p.SupportSide==m.EndSide&&!p.Flipping,"real expected wall exit "+m.Id);
                    Near(p.VerticalSpeed,0,"wall vertical rest");
                    Check(p.AirFlipAvailable,"recharged at module exit "+m.Id);
                    Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"empty effects at module exit "+m.Id);
                    Check(!p.CollectedRiskIds.Except(p.SuccessfulRiskIds).Except(p.FailedRiskIds).Any(),"no pending linked reward at module exit");
                    Check(p.BoostsEarned==rewards&&p.CompletedBoosts==rewards&&p.SlowsEarned==0,"exact cumulative module rewards");
                    Near(p.CurrentSpeed,220,"base speed at module exit");
                }
                r.Boundaries.Add(new{module=m.Id,x=boundaryX,time=game.Elapsed,side=game.Players[0].SupportSide,
                    game.Players[0].Support,game.Players[0].Flipping,game.Players[0].BoostsEarned,empty=game.Players[0].ActiveEffect==null&&game.Players[0].PendingEffects.Count==0});
                continue;
            }
            if(input==omit){input++;continue;}
            if(input==changed)AdvanceTime(game,baseline.Result.Inputs[input].Time+shift-game.Elapsed,step);
            else MoveTo(game,inputX,step);
            if(omit<0)Tap(game,r.Result,flight==FlightPresets.FastFlip);
            else {RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);}
            input++;
        }
        Finish(game,step);return r;
    }
    static void Clean(CourseRun run,bool full)
    {
        foreach(var p in run.Result.Game.Players) {
            Check(p.Collisions==0,"whole course clean");Check(p.BoostsEarned==(full?9:0)&&p.SlowsEarned==0,"ordinary reward budget");
            Check(p.FinishBoostsEarned==(full?1:0),"finish budget");Near(p.FinishTime.Value,full?33.2:36,"actual whole course finish");
        }
        Check(Equals(run.Result.Game.Winner,"tie"),"symmetric independent runners tie");
    }
    static void Geometry(Course c)
    {
        int expected=c.Id=="rhythm-dense"?20:19;
        Check(c.Modules.Count==expected&&c.Modules.Count(m=>m.Kind=="gate")==expected-8,"authored module and bare-gate budget");
        Check(c.Track.RiskZones.Count(z=>!z.IsFinish&&!z.IsNegative)==9,"nine ordinary rewards");
        Check(c.Track.RiskZones.Count(z=>z.IsNegative)==0,"no unverified negative placeholders");
        Check(c.Track.Obstacles.Select(o=>o.Id).Distinct().Count()==c.Track.Obstacles.Length,"unique obstacle IDs");
        Check(c.Track.RiskZones.Select(z=>z.Id).Distinct().Count()==c.Track.RiskZones.Length,"unique zone IDs");
        foreach(var z in c.Track.RiskZones)if(z.ObstacleId!=null)Check(c.Track.Obstacles.Any(o=>o.Id==z.ObstacleId),"valid prefixed linked ID");
        foreach(var m in c.Modules) {
            Check(m.EffectBoundX<m.EndX,"universal late effect work fits module boundary");
            foreach(var o in c.Track.Obstacles.Where(o=>o.Id.StartsWith(m.Id+"-",StringComparison.Ordinal)))
                Check(ObstacleGeometry.Left(o)-14>m.X&&ObstacleGeometry.Right(o)+14<m.EndX,"both physical parts fit module boundaries");
        }
        var finish=c.Track.RiskZones.Single(z=>z.IsFinish);Near(finish.X,7876,"44-world finish-plane separation");Near(finish.FinishHeight,16,"unchanged exact finish height");
    }
    static object Rhythm(Course c,FlightSettings flight,bool full,CourseRun r)
    {
        var route=c.Routes[RouteKey(flight,full)];var inputs=r.Result.Inputs;var groups=new List<object>();
        int groupCount=c.Modules.Max(m=>m.Group)+1;
        for(int group=0;group<groupCount;group++) {
            var indices=Enumerable.Range(0,route.Count).Where(i=>!route[i].Finish&&route[i].Group==group).ToArray();
            int first=indices.First(),last=indices.Last();
            double? nextGap=group+1<groupCount?inputs[last+1].Time-inputs[last].Time:(double?)null;
            groups.Add(new{group=group+1,inputs=indices.Length,firstTime=inputs[first].Time,lastTime=inputs[last].Time,nextGroupGap=nextGap,
                maximumInteriorGap=indices.Skip(1).Select(i=>inputs[i].Time-inputs[i-1].Time).DefaultIfEmpty(0).Max()});
        }
        return new{firstTap=inputs.First().Time,lastTapToFinish=r.Result.Game.Players[0].FinishTime.Value-inputs.Last().Time,
            maxInputGap=inputs.Skip(1).Select((s,i)=>s.Time-inputs[i].Time).Max(),groups};
    }
    static void VerifyAndExport(Course[] courses)
    {
        var exports=new List<object>();int games=0;
        foreach(var c in courses) {
            Geometry(c);var runs=new List<object>();
            foreach(var f in Profiles)foreach(bool full in new[]{false,true}) {
                CourseRun baseline=null;
                foreach(double step in Steps){var run=Replay(c,f,full,step);Clean(run,full);if(baseline==null)baseline=run;games++;}
                if(WindowProof)for(int input=0;input<c.Routes[RouteKey(f,full)].Count;input++)foreach(double sign in new[]{-1d,1d}) {
                    Clean(Replay(c,f,full,.03,input,sign*(full?.025:.06),baseline),full);games++;
                }
                var rhythm=Rhythm(c,f,full,baseline);
                runs.Add(new{profile=ProfileId(f),preset=f==FlightPresets.FastFlip?11:10,variant=full?"full":"safe",
                    route=c.Routes[RouteKey(f,full)],inputs=baseline.Result.Inputs,finish=baseline.Result.Game.Players[0].FinishTime,
                    boundaries=baseline.Boundaries,rhythm});
                Console.WriteLine(c.Id+" "+ProfileId(f)+" "+(full?"full":"safe")+" taps="+baseline.Result.Inputs.Count+" finish="+baseline.Result.Game.Players[0].FinishTime);
                Console.WriteLine(JsonSerializer.Serialize(rhythm,Json));
            }
            exports.Add(new{id=c.Id,name=c.Name,track=c.Track,
                modules=c.Modules.Select(m=>new{id=m.Id,kind=m.Kind,group=m.Group+1,x=m.X,endX=m.EndX,m.EntrySide,m.EndSide,m.Mirrored,m.EffectBoundX}),routes=runs});
        }
        File.WriteAllText("Verification/rhythm-course-"+Revision+".json",JsonSerializer.Serialize(exports,Json));
        Console.WriteLine("COURSE DRAFT PASS "+checks+" checks / "+games+" complete games; windows="+WindowProof+"; no negative routes yet");
    }
}
