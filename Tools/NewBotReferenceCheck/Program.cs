using System.Text.Json;
using FlipRace.Core;

static class Program
{
    static int checks;
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    sealed class Result {
        public string Track,Profile;public bool Clean;public int Hits,Rejected,Earned,Completed,FinishEarned;public double? Finish;
        public double Gain,FinishGain;public string[] HitIds,RewardIds;public List<object> Inputs=new(),Events=new();
    }
    static Result Run(Track track,FlightSettings flight,double[] tape,double warning=.8,bool initialSolid=false)
    {
        var g=RaceSimulation.CreateSolo(track,flight,PartyCamera.Lagger,-10000,20000);HiddenObstacles.ConfigureWarning(g,warning);
        RaceSimulation.Press(g,0);RaceSimulation.Advance(g,3);
        // Structural conservative fixture only. Actual human-ahead shared-world
        // reveal/postponement is covered by the Core integration checks.
        if(initialSolid)foreach(var obstacle in track.Obstacles.Where(o=>o.Hidden))g.HiddenObstacles[obstacle.Id]=new HiddenObstacleState{Phase=HiddenObstaclePhase.Solid,RevealedAt=0,SolidAt=0};
        var p=g.Players[0];var r=new Result{Track=track.Id,Profile=flight.FirstAction==FirstAction.Flip?"FastFlip":"Chosen"};
        void Tick(double dt){bool air=p.Flipping;int hits=p.Collisions;string reward=p.LastInstant?.Id;RaceSimulation.Advance(g,dt);
            if(hits!=p.Collisions)r.Events.Add(new{kind="hit",id=p.LastHit?.Id,x=p.Distance,time=g.Elapsed,y=RaceSimulation.GetPlayerCenterY(p)});
            if(reward!=p.LastInstant?.Id)r.Events.Add(new{kind="reward",id=p.LastInstant?.Id,x=p.Distance,time=g.Elapsed,y=RaceSimulation.GetPlayerCenterY(p)});
            if(air&&!p.Flipping&&p.Support!=SupportKind.None)r.Events.Add(new{kind="landing",support=p.Support.ToString(),id=p.SupportObstacleId,x=p.Distance,time=g.Elapsed,y=RaceSimulation.GetPlayerCenterY(p)});}
        foreach(double x in tape){int guard=0;
            while(p.Distance<x-1e-8&&g.Phase=="racing"){
                if(++guard>40000)throw new Exception("Distance scheduler stalled");
                if(p.Recovering>0){Tick(Math.Min(.005,p.Recovering));continue;}
                double dt=Math.Min(.005,(x-p.Distance)/p.CurrentSpeed);if(p.ActiveEffect!=null)dt=Math.Min(dt,p.ActiveEffect.Remaining);
                var trial=RaceSnapshot.Clone(g);RaceSimulation.Advance(trial,dt);
                if(trial.Players[0].Distance>x+1e-9){double lo=0,hi=dt;for(int i=0;i<40;i++){double mid=(lo+hi)/2;trial=RaceSnapshot.Clone(g);RaceSimulation.Advance(trial,mid);if(trial.Players[0].Distance<x)lo=mid;else hi=mid;}dt=(lo+hi)/2;}
                Tick(dt);
            }
            r.Inputs.Add(new{x=p.Distance,time=g.Elapsed,y=RaceSimulation.GetPlayerCenterY(p),vy=p.VerticalSpeed,p.Flipping,support=p.Support.ToString(),p.SupportSide,p.CurrentSpeed});
            if(!RaceSimulation.Press(g,0))r.Rejected++;
        }
        int done=0;while(g.Phase=="racing"&&done++<60000)Tick(.005);
        r.Hits=p.Collisions;r.Earned=p.BoostsEarned;r.Completed=p.CompletedBoosts;r.FinishEarned=p.FinishBoostsEarned;r.Finish=p.FinishTime;r.Gain=p.BoostDistanceGained;r.FinishGain=p.FinishBoostDistanceGained;
        r.HitIds=p.HitObstacleIds.ToArray();r.RewardIds=p.SuccessfulRiskIds.ToArray();r.Clean=r.Hits==0&&r.Rejected==0&&p.Status==PlayerStatus.Finished;return r;
    }
    static void Main(string[] args)
    {
        if(args.Contains("--compatibility")) {
            var track=TracksHidden.Get(TracksHidden.InterceptsId);var compatRows=new List<object>();int more=0;
            foreach(double launch in new[]{2440d,2470,2500,2530,2560})foreach(double response in new[]{2720d,2750,2780,2810}) {
                var tape=BotReferenceRoutes.Get(track.Id,FlightPresets.Chosen).Inputs.ToList();tape.Add(launch);
                tape.RemoveAll(x=>Math.Abs(x-2780.844877314131)<1e-6||Math.Abs(x-2824.844877314131)<1e-6);tape.Add(response);tape.Sort();
                var r=Run(track,FlightPresets.Chosen,tape.ToArray());bool improved=r.Clean&&r.Earned>8;if(improved)more++;
                compatRows.Add(new{profile="Chosen",launch,response,improved,r.Clean,r.Earned,r.Hits,r.HitIds,r.RewardIds});
                if(improved)Console.WriteLine($"BETTER Chosen launch={launch} response={response} earned={r.Earned}");
            }
            foreach(double launch in new[]{2160d,2180,2200})foreach(double returning in new[]{2250d,2290,2330}) {
                // Reconstruct the original eight-reward baseline, so the saved
                // historical local experiment remains reproducible after the
                // resulting improved reference has replaced it.
                var tape=BotReferenceRoutes.Get(track.Id,FlightPresets.FastFlip).Inputs.Select(x=>Math.Abs(x-2180)<1e-6?launch:Math.Abs(x-2290)<1e-6?returning:Math.Abs(x-4690)<1e-6?4760:x).OrderBy(x=>x).ToArray();
                var r=Run(track,FlightPresets.FastFlip,tape);bool improved=r.Clean&&r.Earned>8;if(improved)more++;
                compatRows.Add(new{profile="FastFlip",launch,returning,improved,r.Clean,r.Earned,r.Hits,r.HitIds,r.RewardIds});
                if(improved)Console.WriteLine($"BETTER Fast launch={launch} returning={returning} earned={r.Earned}");
            }
            File.WriteAllText("Verification/v542-new-bot-reference-compatibility.json",JsonSerializer.Serialize(new{attempts=compatRows.Count,improvements=more,rows=compatRows},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
            Console.WriteLine($"Bounded compatibility attempts={compatRows.Count}, improvements={more}");return;
        }
        var rows=new List<object>();foreach(var id in new[]{TracksHidden.InterceptsId,TracksHidden.ManifestationId,TracksRethink.Id})foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}){
            if(!BotReferenceRoutes.Supports(id,f))continue;var track=id==TracksRethink.Id?TracksRethink.Get():TracksHidden.Get(id);var reference=BotReferenceRoutes.Get(id,f);
            var ids=reference.RewardIds;var inputs=reference.Inputs;string firstId=ids[0];double firstInput=inputs[0];ids[0]="mutated";inputs[0]=double.NaN;
            Check(reference.RewardIds[0]==firstId&&reference.Inputs[0]==firstInput,"Defensive reference copies");
            var scenarios=(id==TracksRethink.Id?new[]{.8}:new[]{.4,.6,.8,1d,1.2}).Select(delay=>(delay,initialSolid:false)).ToList();
            scenarios.Add((.8,true));
            foreach(var scenario in scenarios){
                double delay=scenario.delay;var result=Run(track,f,reference.Inputs,delay,scenario.initialSolid);var expected=new HashSet<string>(reference.RewardIds,StringComparer.Ordinal);
                Check(result.Clean,"Clean reference "+id+"/"+result.Profile+" warning"+delay);
                Check(expected.SetEquals(result.RewardIds),"Exact selected rewards; no omitted reachable collection");
                Check(result.Earned==reference.RewardIds.Length-1&&result.Completed==result.Earned,"Every ordinary effect fully served");
                Check(Math.Abs(result.Gain-result.Earned*66)<1e-6,"Full actual ordinary distance");
                Check(result.FinishEarned==1&&Math.Abs(result.FinishGain-55)<1e-6,"Full actual finish effect");
                Check(Math.Abs(result.Finish.Value-(36-reference.BonusGain))<1e-6,"Actual finish agrees with selected B");
                rows.Add(new{delay,scenario.initialSolid,selected=reference.RewardIds,reference.BonusGain,result});
            }
            Console.WriteLine($"PASS {id}/{(f.FirstAction==FirstAction.Flip?"FastFlip":"Chosen")}: {reference.RewardIds.Length-1} ordinary + finish, B={reference.BonusGain:F2}");
        }
        Check(!BotReferenceRoutes.Supports(TracksRethink.Id,FlightPresets.FastFlip),"Rethink Fast unsupported");
        Check(!BotReferenceRoutes.Supports(TracksFull.LongId,FlightPresets.Chosen),"Old references retain existing path");
        Check(BotReferenceRoutes.Supports(TracksHidden.InterceptsId,new FlightSettings(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.5,0,350)),"Equivalent exact settings supported");
        Check(!BotReferenceRoutes.Supports(TracksHidden.InterceptsId,new FlightSettings(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.6,0,350)),"Modified physical profile unsupported");
        File.WriteAllText("Verification/v542-new-bot-reference-proof.json",JsonSerializer.Serialize(new{checks,rows},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine($"PASS {checks} assertions / {rows.Count} full replay cases.");
    }
}
