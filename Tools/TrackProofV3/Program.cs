using System;
using System.Collections.Generic;
using FlipRace.Core;

// Separate geometry proof runner. All inputs go through Press and all travel,
// collisions and effects go through the production Advance implementation.
internal static class Program
{
    static int checks;
    static List<GameState> capturedHits;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception(message);}
    static void Near(double a,double b,string message) {Check(Math.Abs(a-b)<1e-7,message+" expected "+b+" got "+a);}
    static GameState Start(Track track) {return new GameState {Mode="race",Track=track,Phase="racing"};}
    static void MoveTo(GameState game,double distance,double maxStep=.01)
    {
        PlayerState p=game.Players[0];
        int guard=0;
        while(p.Distance<distance-1e-8 && game.Phase=="racing")
        {
            if(++guard>200000)throw new Exception("MoveTo stalled at "+p.Distance+" target "+distance);
            double dt=p.Recovering>0?p.Recovering:(distance-p.Distance)/p.CurrentSpeed;
            if(p.BoostRemaining>0)dt=Math.Min(dt,p.BoostRemaining);
            if(p.SlowRemaining>0)dt=Math.Min(dt,p.SlowRemaining);
            if(p.Flipping)dt=Math.Min(dt,RaceConfig.FlipDuration-p.FlipElapsed);
            AdvanceCapture(game,Math.Max(1e-10,Math.Min(maxStep,dt)));
        }
    }
    static void Finish(GameState game,double step=.03)
    {
        int guard=0;
        while(game.Phase=="racing"&&guard++<100000)AdvanceCapture(game,step);
        Check(game.Players[0].FinishTime.HasValue,"Did not reach finish");
        foreach(var p in game.Players)
        {
            Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"Finish left an incomplete speed effect");
            Check(p.CompletedBoosts==p.BoostsEarned&&p.CompletedSlows==p.SlowsEarned,"Finish truncated an earned effect");
        }
    }
    static GameState Route(Track track,double[] route,double step,out List<double> times)
    {
        var game=Start(track);times=new List<double>();
        foreach(double x in route)
        {
            MoveTo(game,x,step);times.Add(game.Elapsed);
            bool a=RaceSimulation.Press(game,0),b=RaceSimulation.Press(game,1);
            Check(a&&b,"Rejected route press at "+x+" distance="+game.Players[0].Distance);
        }
        Finish(game,step);return game;
    }
    static GameState TimedShift(Track track,int input,double at)
    {
        var game=Start(track);
        for(int i=0;i<track.RiskRoute.Length;i++)
        {
            if(i==input) {Wait(game,at-game.Elapsed);Near(game.Elapsed,at,"Timed input did not occur at requested clock");}
            else MoveTo(game,track.RiskRoute[i]);
            bool a=RaceSimulation.Press(game,0),b=RaceSimulation.Press(game,1);
            Check(a&&b,"Rejected timed input at "+i);
        }
        Finish(game);return game;
    }
    static void InputProof(Track track,List<double> times)
    {
        for(int input=0;input<track.RiskRoute.Length;input++)
        {
            foreach(double delta in new[]{-.04,.04})
            {
                var p=TimedShift(track,input,times[input]+delta).Players[0];
                Check(p.Collisions==0&&p.BoostsEarned==track.PositiveRingCount&&p.SlowsEarned==0,
                    "Actual clock +/-40ms tolerance failed at "+input+" delta "+delta);
            }
            var missed=Start(track);
            for(int i=0;i<track.RiskRoute.Length;i++)
            {
                if(i==input)continue;
                MoveTo(missed,track.RiskRoute[i]);RaceSimulation.Press(missed,0);RaceSimulation.Press(missed,1);
            }
            Finish(missed);
            var q=missed.Players[0];
            Check(q.Collisions>0||q.BoostsEarned<track.PositiveRingCount||q.SlowsEarned>0,
                "Input "+input+" is redundant on the active route");
        }
        Console.WriteLine(" all "+track.RiskRoute.Length+" actions meaningful; each tolerates +/-40ms actual clock");
    }
    static void VerifyTrack(Track track)
    {
        var safe=Route(track,track.SafeRoute,.01,out var safeTimes);
        var ideal=Route(track,track.RiskRoute,.01,out var times);
        var p=safe.Players[0];var q=ideal.Players[0];
        Console.WriteLine(track.Id+" negative="+track.NegativeEnabled+" safe="+p.FinishTime+" hits="+p.Collisions+" plus="+p.BoostsEarned);
        Console.WriteLine(" ideal="+q.FinishTime+" hits="+q.Collisions+" plus="+q.BoostsEarned+" minus="+q.NegativeRiskIds.Count);
        Console.WriteLine(" action times: "+string.Join(", ",times.ConvertAll(x=>x.ToString("F3"))));
        Check(p.Collisions==0&&p.BoostsEarned==0&&p.NegativeRiskIds.Count==0,"Safe route is not optional/no-effect");
        Check(q.Collisions==0&&q.BoostsEarned==track.PositiveRingCount&&q.NegativeRiskIds.Count==0,"Ideal route did not collect every positive safely");
        Near(p.FinishTime.Value,track.Length/RaceConfig.Speed,"Safe finish");
        Near(q.FinishTime.Value,track.Length/RaceConfig.Speed-track.PositiveRingCount*.3,"Ideal finish");
        Check(ideal.Winner is string&&(string)ideal.Winner=="tie","Identical route should tie");
        double maxGap=0;for(int i=1;i<times.Count;i++)maxGap=Math.Max(maxGap,times[i]-times[i-1]);
        Console.WriteLine(" flips="+times.Count+" max interior gap="+maxGap.ToString("F3")+" s");
        Console.WriteLine(" first input="+times[0].ToString("F6")+" last input to finish="+(q.FinishTime.Value-times[times.Count-1]).ToString("F6"));
        InputProof(track,times);
        foreach(double step in new[]{1d/30,1d/60,1d/144,.25})
        {
            var other=Route(track,track.RiskRoute,step,out _).Players[0];
            Check(other.Collisions==0&&other.BoostsEarned==track.PositiveRingCount&&other.NegativeRiskIds.Count==0,"Different timestep changed route");
            Near(other.FinishTime.Value,q.FinishTime.Value,"Different timestep changed finish");
        }
        var ringInputs = new List<int>();
        foreach(var ring in track.RiskZones)
            if(!ring.IsNegative)ringInputs.Add(Array.IndexOf(track.RiskRoute,ring.RiskFlipX));
        foreach(int routeIndex in ringInputs)
            foreach(double shift in new[]{-.04*RaceConfig.Speed,.04*RaceConfig.Speed})
            {
                double[] jitter=(double[])track.RiskRoute.Clone();jitter[routeIndex]+=shift;
                var trial=Route(track,jitter,.01,out _).Players[0];
                Check(trial.Collisions==0&&trial.BoostsEarned==track.PositiveRingCount,"Ring window lacks +/-40 ms at "+routeIndex);
            }
        if(track.NegativeEnabled)
        {
            double[] slow=TracksV3.NegativeRoute(track.Id);
            var trial=Route(track,slow,.01,out _).Players[0];
            int negatives=0;foreach(var ring in track.RiskZones)if(ring.IsNegative)negatives++;
            Check(trial.Collisions==0&&trial.BoostsEarned==track.PositiveRingCount&&trial.NegativeRiskIds.Count==negatives,"Intentional negative contact forced an error");
            Near(trial.FinishTime.Value,track.Length/RaceConfig.Speed-track.PositiveRingCount*.3+negatives*.3,"Full negative effect");
            Console.WriteLine(" intentional negatives="+negatives+" finish="+trial.FinishTime+" hits="+trial.Collisions);
        }
        if(track.Id=="v3-series")
        {
        double[] early=(double[])track.RiskRoute.Clone();early[1]=775;
        var tooEarly=Route(track,early,.01,out _).Players[0];
        Check(tooEarly.HitObstacleIds.Contains("series-return")&&tooEarly.FailedRiskIds.Contains("series-after-ring"),"Early return should strike the rear edge and lose its ring");
        double[] late=(double[])track.RiskRoute.Clone();late[1]=1000;
        var tooLate=Route(track,late,.01,out _).Players[0];
        Check(tooLate.Collisions==0&&tooLate.BoostsEarned==1,"Late optional return should miss only the after ring");
        }
        RecoveryProof(track);
    }
    static void AdvanceCapture(GameState game,double dt)
    {
        int old=game.Players[0].Collisions;RaceSimulation.Advance(game,dt);
        if(capturedHits!=null&&capturedHits.Count==0&&game.Players[0].Collisions>old)capturedHits.Add(Clone(game));
    }
    static SpeedEffect CopyEffect(SpeedEffect effect)
    {return effect==null?null:new SpeedEffect{RingId=effect.RingId,IsNegative=effect.IsNegative,Remaining=effect.Remaining,Elapsed=effect.Elapsed};}
    static PlayerState CopyPlayer(PlayerState p)
    {
        var q=new PlayerState(p.Id){CharacterIndex=p.CharacterIndex,Ready=p.Ready,Distance=p.Distance,Y=p.Y,
            Flipping=p.Flipping,FlipFrom=p.FlipFrom,FlipTo=p.FlipTo,FlipElapsed=p.FlipElapsed,
            Recovering=p.Recovering,Collisions=p.Collisions,FinishTime=p.FinishTime,LastHit=p.LastHit,
            ActiveEffect=CopyEffect(p.ActiveEffect),BoostsEarned=p.BoostsEarned,SlowsEarned=p.SlowsEarned,
            LastSuccess=p.LastSuccess,LastSlow=p.LastSlow,CompletedBoosts=p.CompletedBoosts,
            CompletedSlows=p.CompletedSlows,CompletedRecoveries=p.CompletedRecoveries,RecoveryElapsed=p.RecoveryElapsed};
        q.HitObstacleIds=new HashSet<string>(p.HitObstacleIds);
        q.CollectedRiskIds=new HashSet<string>(p.CollectedRiskIds);
        q.SuccessfulRiskIds=new HashSet<string>(p.SuccessfulRiskIds);
        q.FailedRiskIds=new HashSet<string>(p.FailedRiskIds);
        q.NegativeRiskIds=new HashSet<string>(p.NegativeRiskIds);
        foreach(var effect in p.PendingEffects)q.PendingEffects.Enqueue(CopyEffect(effect));
        return q;
    }
    static GameState Clone(GameState g)
    {
        return new GameState{Mode=g.Mode,Track=g.Track,Phase=g.Phase,Elapsed=g.Elapsed,
            CountdownRemaining=g.CountdownRemaining,Winner=g.Winner,WinnerAt=g.WinnerAt,
            Players=new[]{CopyPlayer(g.Players[0]),CopyPlayer(g.Players[1])}};
    }
    static void Wait(GameState g,double seconds)
    {
        while(seconds>1e-10&&g.Phase=="racing"){double step=Math.Min(.01,seconds);RaceSimulation.Advance(g,step);seconds-=step;}
    }
    static bool CanEscapeNext(GameState collision)
    {
        GameState ready=Clone(collision);PlayerState p=ready.Players[0];
        int hits=p.Collisions;double previousX=double.NegativeInfinity;
        foreach(var o in ready.Track.Obstacles)if(o.Id==p.LastHit.Id)previousX=o.X;
        Obstacle next=null;foreach(var o in ready.Track.Obstacles)if(o.X>previousX){next=o;break;}
        if(next==null)return true;
        double target=next.X+next.Width+RaceConfig.PlayerRadius+1;
        Wait(ready,p.Recovering);Wait(ready,.15);
        if(p.Flipping)Wait(ready,RaceConfig.FlipDuration-p.FlipElapsed);
        if(p.Collisions!=hits)return false;
        for(double delay=0;delay<=2.5;delay+=.025)
        {
            var trial=Clone(ready);Wait(trial,delay);
            if(trial.Players[0].Collisions!=hits)continue;
            // A stay on the current surface is also an available decision.
            var stay=Clone(trial);MoveTo(stay,target);
            if(stay.Players[0].Collisions==hits&&stay.Players[0].Distance>=target-1e-8)return true;
            if(!RaceSimulation.Press(trial,0))continue;
            RaceSimulation.Press(trial,1);MoveTo(trial,target);
            if(trial.Players[0].Collisions==hits&&trial.Players[0].Distance>=target-1e-8)return true;
        }
        return false;
    }
    static void RecoveryProof(Track track)
    {
        int samples=0;
        for(int edge=0;edge<track.RiskRoute.Length;edge++)
            for(int shift=-10;shift<=10;shift++)
            {
                if(shift==0)continue;
                double[] route=(double[])track.RiskRoute.Clone();
                route[edge]+=shift*.08*RaceConfig.Speed;
                var game=Start(track);capturedHits=new List<GameState>();
                foreach(double x in route)
                {
                    MoveTo(game,x);RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);
                }
                Finish(game);
                var captured=capturedHits;capturedHits=null;
                if(captured.Count==0)continue;
                samples++;
                Check(CanEscapeNext(captured[0]),"Forced next collision after input "+edge+" shifted "+shift*.08*RaceConfig.Speed+" distance units, first hit "+captured[0].Players[0].LastHit.Id);
            }
        Check(samples>=40,"Recovery sampling did not produce enough collision states");
        Console.WriteLine(" recovery samples="+samples+"; 150ms reaction and actual-speed escape passed");
    }
    static int Main()
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
        try {
            VerifyTrack(TracksV3.StageOne(false));VerifyTrack(TracksV3.StageOne(true));
            foreach(var t in TracksV3.All){VerifyTrack(TracksV3.Get(t.Id,false));VerifyTrack(TracksV3.Get(t.Id,true));}
            Console.WriteLine("PASS "+checks+" track checks");return 0;}
        catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
    }
}
