using System.Text.Json;
using FlipRace.Core;

static partial class Program
{
    sealed class Result
    {
        public int Hits, Rejected, Earned, Completed, FinishEarned, StoneLandings;
        public double? Finish; public double Gain, FinishGain; public bool Clean;
        public List<object> Inputs=new(), Landings=new(), Events=new(); public string[] HitIds, RewardIds;
    }
    static Result Run(Track track, double[] tape, Dictionary<double,double> inputTimes=null, bool allTimes=false)
    {
        var game=RaceSimulation.CreateSolo(track, FlightPresets.Chosen, PartyCamera.Lagger, -10000, 20000);
        RaceSimulation.Press(game,0); RaceSimulation.Advance(game,3); var p=game.Players[0]; var result=new Result();
        var phases=new Dictionary<string,HiddenObstaclePhase>();
        void Tick(double dt)
        {
            bool air=p.Flipping; int hits=p.Collisions; string previousReward=p.LastInstant?.Id; RaceSimulation.Advance(game,dt);
            foreach(var o in track.Obstacles.Where(o=>o.Hidden)) {
                var phase=HiddenObstacles.GetPhase(game,o);
                if(!phases.TryGetValue(o.Id,out var previous)||phase!=previous) {
                    phases[o.Id]=phase;
                    result.Events.Add(new{id=o.Id,phase=phase.ToString(),x=p.Distance,time=game.Elapsed,y=RaceSimulation.GetPlayerCenterY(p),vy=p.VerticalSpeed,air=p.Flipping,
                        revealedAt=game.HiddenObstacles.TryGetValue(o.Id,out var state)?state.RevealedAt:null,solidAt=state?.SolidAt});
                }
            }
            if(p.Collisions>hits)result.Events.Add(new{phase="Hit",id=p.LastHit?.Id,x=p.Distance,time=game.Elapsed,y=RaceSimulation.GetPlayerCenterY(p),vy=p.VerticalSpeed});
            if(p.LastInstant?.Id!=previousReward)result.Events.Add(new{phase="Reward",id=p.LastInstant.Id,at=p.LastInstant.At,x=p.Distance,time=game.Elapsed,y=RaceSimulation.GetPlayerCenterY(p),vy=p.VerticalSpeed});
            if(air&&!p.Flipping&&p.Support!=SupportKind.None) {
                if(p.Support==SupportKind.Stone)result.StoneLandings++;
                result.Landings.Add(new{x=p.Distance,time=game.Elapsed,y=RaceSimulation.GetPlayerCenterY(p),support=p.Support.ToString(),p.SupportSide,p.SupportObstacleId});
            }
        }
        foreach(double x in tape) {
            double? targetTime=allTimes?x:inputTimes!=null&&inputTimes.TryGetValue(x,out var at)?at:null;
            int guard=0;
            while((targetTime.HasValue?game.Elapsed<targetTime.Value-1e-9:p.Distance<x-1e-8)&&game.Phase=="racing") {
                if(++guard>40000)throw new Exception("Distance scheduler stalled");
                double remaining=targetTime.HasValue?targetTime.Value-game.Elapsed:(x-p.Distance)/p.CurrentSpeed;
                if(p.Recovering>0){Tick(Math.Min(Math.Min(.005,remaining),p.Recovering));continue;}
                double dt=Math.Min(.005,remaining);
                if(p.ActiveEffect!=null)dt=Math.Min(dt,p.ActiveEffect.Remaining);
                var trial=RaceSnapshot.Clone(game);RaceSimulation.Advance(trial,dt);
                if(!targetTime.HasValue&&trial.Players[0].Distance>x+1e-9){double lo=0,hi=dt;for(int j=0;j<40;j++){double mid=(lo+hi)/2;trial=RaceSnapshot.Clone(game);RaceSimulation.Advance(trial,mid);if(trial.Players[0].Distance<x)lo=mid;else hi=mid;}dt=(lo+hi)/2;}
                Tick(dt);
            }
            result.Inputs.Add(new{requested=x,x=p.Distance,time=game.Elapsed,y=RaceSimulation.GetPlayerCenterY(p),vy=p.VerticalSpeed,speed=p.CurrentSpeed,effect=p.ActiveEffect?.RingId,effectRemaining=p.ActiveEffect?.Remaining,p.Flipping,support=p.Support.ToString(),p.SupportSide,
                phases=track.Obstacles.Where(o=>o.Hidden).Select(o=>new{id=o.Id,phase=HiddenObstacles.GetPhase(game,o).ToString()}).ToArray()});
            if(!RaceSimulation.Press(game,0))result.Rejected++;
        }
        int finishGuard=0;while(game.Phase=="racing"&&finishGuard++<60000)Tick(.005);
        result.Hits=p.Collisions;result.Finish=p.FinishTime;result.Earned=p.BoostsEarned;result.Completed=p.CompletedBoosts;
        result.Gain=p.BoostDistanceGained;result.FinishEarned=p.FinishBoostsEarned;result.FinishGain=p.FinishBoostDistanceGained;
        result.HitIds=p.HitObstacleIds.ToArray();result.RewardIds=p.SuccessfulRiskIds.ToArray();
        result.Clean=result.Hits==0&&result.Rejected==0&&p.Status==PlayerStatus.Finished;return result;
    }
    static void LegacyProbe(string[] args)
    {
        if(args.Contains("--full")) {
            var full=TracksRethink.Get();var safe=Run(full,TracksRethink.SafeRoute());var air=Run(full,TracksRethink.AirRoute());
            var pad=Run(full,TracksRethink.LandingRoute());var finish=Run(full,TracksRethink.FinishRoute());var bonus=Run(full,TracksRethink.BonusRoute());
            File.WriteAllText("Verification/v541-rethink-tracks-full.json",JsonSerializer.Serialize(new{safe,air,pad,finish,bonus},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
            foreach(var row in new[]{("safe",safe),("air",air),("pad",pad),("finish",finish),("bonus",bonus)})Console.WriteLine($"{row.Item1}: clean={row.Item2.Clean} hit={string.Join(',',row.Item2.HitIds)} reward={row.Item2.Earned} landing={row.Item2.StoneLandings} finish={row.Item2.FinishEarned} time={row.Item2.Finish}");return;
        }
        if(args.Contains("--three")) {
            var three=TracksRethink.FirstThree();var safe=Run(three,TracksRethink.FirstThreeRoute());var air=Run(three,TracksRethink.AirRoute().Where(x=>x<3960).ToArray());
            var responses=new List<object>();
            foreach(double entry in new[]{-11d,0,11}) {
                var cases=new List<object>();double first=double.NaN,last=double.NaN;
                for(double correction=1520;correction<=1740;correction+=5) {
                    var tape=TracksRethink.FirstThreeRoute();tape[5]+=entry;tape[6]=correction;
                    var result=Run(three,tape);if(result.Clean){if(double.IsNaN(first))first=correction;last=correction;}
                    cases.Add(new{correction,result});
                }
                responses.Add(new{entryMilliseconds=entry/220*1000,first,last,windowSeconds=(last-first)/220,lastSaveAfterSignal=(last-1520)/220,cases});
                Console.WriteLine($"Cancellation entry={entry/220*1000}: first={first} last={last} window={(last-first)/220:F3}s lastSave={(last-1520)/220:F3}s");
            }
            File.WriteAllText("Verification/v541-rethink-tracks-three.json",JsonSerializer.Serialize(new{safe,air,responses},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));
            Console.WriteLine($"First3 safe={safe.Clean} hits={string.Join(',',safe.HitIds)} rejected={safe.Rejected}; air={air.Clean} hits={string.Join(',',air.HitIds)} earned={air.Earned}");return;
        }
        var track=TracksRethink.ModuleOne();var route=TracksRethink.ModuleOneRoute();
        var nominal=Run(track,route);
        var auto=route.ToList();auto.Add(530);auto.Sort();var autopilot=Run(track,auto.ToArray());
        var scans=new List<object>();
        foreach(double entry in new[]{-11d,0,11}) {
            var rows=new List<object>();double first=double.NaN,last=double.NaN;
            for(double correction=620;correction<=1000;correction+=5) {
                var tape=(double[])route.Clone();tape[0]+=entry;tape[1]=correction;tape[2]=correction+44;
                var result=Run(track,tape);if(result.Clean){if(double.IsNaN(first))first=correction;last=correction;}
                rows.Add(new{correction,result});
            }
            var habit=route.ToList();habit[0]+=entry;habit.Add(530);habit.Sort();
            scans.Add(new{entryMilliseconds=entry/220*1000,first,last,windowSeconds=(last-first)/220,wrongTapAfterSignal=(530-440)/220d,habit=Run(track,habit.ToArray()),rows});
        }
        var path="Verification/v541-rethink-tracks-module1.json";
        File.WriteAllText(path,JsonSerializer.Serialize(new{nominal,autopilot,scans},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));
        Console.WriteLine($"Module1 safe={nominal.Clean} hits={nominal.Hits}; autopilot hits={autopilot.Hits} ids={string.Join(',',autopilot.HitIds)}");
        foreach(var s in scans)Console.WriteLine(JsonSerializer.Serialize(s).Split(",\"rows\"")[0]);
    }
}
