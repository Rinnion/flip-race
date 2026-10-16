using System;
using System.Collections.Generic;
using System.Globalization;
using FlipRace.Core;

// Independent route harness: production Press/Advance only, with exact
// distance-triggered edges even when a free ring changes speed mid-frame.
// Open and Challenge use the same frozen production physics and authored data.
internal static class Program
{
    static int checks;
    static List<GameState> firstHit;
    static void Check(bool value,string message)
    { checks++; if(!value)throw new Exception(message); }
    static void Near(double actual,double expected,string message,double tolerance=1e-8)
    { Check(Math.Abs(actual-expected)<=tolerance,message+": "+actual+" != "+expected); }

    internal sealed class InputSample
    {
        public double X,Time,Y,VerticalSpeed;
        public bool Airborne,AirFlipAvailable;
    }
    internal sealed class RouteResult
    {
        public GameState Game;
        public readonly List<InputSample> Inputs=new List<InputSample>();
        public int AirInputs;
    }

    static GameState Start(Track track,MotionMode motion)
    {
        var game=RaceSimulation.CreateTrial(track,motion);
        Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"Both players must ready");
        RaceSimulation.Advance(game,RaceConfig.CountdownDuration);
        Check(game.Phase=="racing","Countdown did not reach race");
        Near(game.Elapsed,0,"Route time excludes countdown");
        return game;
    }
    static SpeedEffect CopyEffect(SpeedEffect effect)
    {return effect==null?null:new SpeedEffect{RingId=effect.RingId,IsNegative=effect.IsNegative,Remaining=effect.Remaining,Elapsed=effect.Elapsed};}
    static PlayerState CopyPlayer(PlayerState p)
    {
        var q=new PlayerState(p.Id){CharacterIndex=p.CharacterIndex,Ready=p.Ready,Distance=p.Distance,Y=p.Y,
            Flipping=p.Flipping,FlipFrom=p.FlipFrom,FlipTo=p.FlipTo,FlipElapsed=p.FlipElapsed,
            VerticalSpeed=p.VerticalSpeed,Gravity=p.Gravity,AirFlipAvailable=p.AirFlipAvailable,
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
    {return new GameState{Mode=g.Mode,Motion=g.Motion,Track=g.Track,Phase=g.Phase,Elapsed=g.Elapsed,
        CountdownRemaining=g.CountdownRemaining,Winner=g.Winner,WinnerAt=g.WinnerAt,
        Players=new[]{CopyPlayer(g.Players[0]),CopyPlayer(g.Players[1])}};}
    static void Step(GameState game,double dt)
    {
        int previous=game.Players[0].Collisions;RaceSimulation.Advance(game,dt);
        if(firstHit!=null&&firstHit.Count==0&&game.Players[0].Collisions>previous)firstHit.Add(Clone(game));
    }

    static void MoveTo(GameState game,double target,double maxStep)
    {
        var p=game.Players[0];int guard=0;
        while(p.Distance<target-1e-8&&game.Phase=="racing")
        {
            if(++guard>100000)throw new Exception("Input target stalled at "+p.Distance+" -> "+target);
            if(p.Recovering>0)
            {Step(game,Math.Min(maxStep,p.Recovering));continue;}
            double remaining=target-p.Distance,dt=Math.Min(maxStep,remaining/p.CurrentSpeed);
            if(p.ActiveEffect!=null)dt=Math.Min(dt,p.ActiveEffect.Remaining);
            // Far from the input, even the maximum possible speed cannot pass it.
            if(remaining>dt*RaceConfig.Speed*RaceConfig.BoostMultiplier+1e-8)
            {Step(game,dt);continue;}
            var trial=Clone(game);RaceSimulation.Advance(trial,dt);
            if(trial.Players[0].Distance<=target+1e-8)
            {Step(game,dt);continue;}
            // A new free reward began inside this step. Solve the exact time
            // for the horizontal input edge without modifying actual state.
            double low=0,high=dt;
            for(int i=0;i<55;i++)
            {
                double middle=(low+high)/2;
                trial=Clone(game);RaceSimulation.Advance(trial,middle);
                if(trial.Players[0].Distance<target)low=middle;else high=middle;
            }
            Step(game,(low+high)/2);
        }
        Near(p.Distance,target,"Exact distance input",1e-7);
    }
    static void Finish(GameState game,double step)
    {
        int guard=0;
        while(game.Phase=="racing"&&guard++<100000)Step(game,step);
        Check(game.Phase=="finished","Race did not finish");
        foreach(var p in game.Players)
        {
            Check(p.FinishTime.HasValue,"Player never reached finish");
            Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"Prepared track truncated FIFO at finish");
            Check(p.CompletedBoosts==p.BoostsEarned&&p.CompletedSlows==p.SlowsEarned,"Earned effect did not finish");
        }
    }
    static RouteResult Route(Track track,MotionMode motion,double[] route,double step,bool groundOnly)
    {
        var result=new RouteResult{Game=Start(track,motion)};var game=result.Game;
        foreach(double x in route)
        {
            MoveTo(game,x,step);var p=game.Players[0];
            if(groundOnly)Check(!p.Flipping,"Ordinary route requires an air input at "+x);
            var sample=new InputSample{X=x,Time=game.Elapsed,Y=RaceSimulation.GetPlayerCenterY(p),
                VerticalSpeed=p.VerticalSpeed,Airborne=p.Flipping,AirFlipAvailable=p.AirFlipAvailable};
            result.Inputs.Add(sample);if(sample.Airborne)result.AirInputs++;
            Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"Route press rejected at "+x);
        }
        Finish(game,step);return result;
    }
    static void CheckClean(RouteResult result,int positives,int negatives,double finish)
    {
        foreach(var p in result.Game.Players)
        {
            Check(p.Collisions==0,"Route hit an obstacle: "+string.Join(",",p.HitObstacleIds));
            Check(p.BoostsEarned==positives&&p.SlowsEarned==negatives,"Wrong ring totals");
            Near(p.FinishTime.Value,finish,"Actual route finish");
        }
        Near(result.Game.Elapsed,finish,"Actual game clock");
        Check(Equals(result.Game.Winner,"tie"),"Symmetric route lost exact tie");
    }
    static void AdvanceTime(GameState game,double seconds,double step=.01)
    {
        while(seconds>1e-11&&game.Phase=="racing")
        {double dt=Math.Min(step,seconds);Step(game,dt);seconds-=dt;}
    }
    static bool ClearsNext(GameState game,double target,int hits)
    {
        if(game.Players[0].Collisions!=hits)return false;
        var stay=Clone(game);MoveTo(stay,target,.02);
        return stay.Players[0].Collisions==hits&&stay.Players[0].Distance>=target-1e-7;
    }
    static bool CanEscapeNext(GameState collision)
    {
        var ready=Clone(collision);var p=ready.Players[0];int hits=p.Collisions;
        int index=Array.FindIndex(ready.Track.Obstacles,o=>o.Id==p.LastHit.Id);
        Check(index>=0,"Unknown collision obstacle");
        if(index==ready.Track.Obstacles.Length-1)return true;
        var next=ready.Track.Obstacles[index+1];double target=next.X+next.Width+RaceConfig.PlayerRadius+1;
        AdvanceTime(ready,p.Recovering);AdvanceTime(ready,.15);
        if(p.Collisions!=hits)return false;
        if(ClearsNext(ready,target,hits))return true;
        for(double delay=0;delay<=2.5;delay+=.025)
        {
            var trial=Clone(ready);AdvanceTime(trial,delay,.02);
            if(trial.Players[0].Collisions!=hits)continue;
            if(!RaceSimulation.Press(trial,0))continue;
            RaceSimulation.Press(trial,1);
            if(ClearsNext(trial,target,hits))return true;
            // A surviving air charge, or an actual landing followed by another
            // departure, can also be part of a deliberate next-stone recovery.
            for(double again=.15;again<=1.8;again+=.05)
            {
                var second=Clone(trial);AdvanceTime(second,again,.02);
                if(second.Players[0].Collisions!=hits)continue;
                if(!RaceSimulation.Press(second,0))continue;
                RaceSimulation.Press(second,1);
                if(ClearsNext(second,target,hits))return true;
            }
        }
        return false;
    }
    static void RecoveryProof(Track track,MotionMode mode,double[] source)
    {
        int samples=0;
        for(int edge=0;edge<source.Length;edge++)for(int shift=-10;shift<=10;shift++)
        {
            if(shift==0)continue;
            var route=(double[])source.Clone();route[edge]+=shift*.08*RaceConfig.Speed;
            if((edge>0&&route[edge]<=route[edge-1])||(edge<route.Length-1&&route[edge]>=route[edge+1]))continue;
            var game=Start(track,mode);firstHit=new List<GameState>();
            foreach(double x in route)
            {MoveTo(game,x,.01);RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);}
            Finish(game,.02);var captured=firstHit;firstHit=null;
            if(captured.Count==0)continue;
            samples++;
            Check(CanEscapeNext(captured[0]),"Forced next collision: "+mode+" input="+edge+" shift="+shift*.08*RaceConfig.Speed+"units first="+captured[0].Players[0].LastHit.Id);
        }
        Check(samples>10,"Too few recovery collision samples");
        Console.WriteLine("Recovery "+mode+" route="+(source==track.RiskRoute?"ideal":"safe")+": "+samples+" collision states,150ms reaction,actual available controls PASS");
    }
    static RouteResult TimedShift(Track track,MotionMode motion,double[] route,List<InputSample> baseline,int changed,double delta,bool groundOnly)
    {
        var result=new RouteResult{Game=Start(track,motion)};var game=result.Game;
        for(int i=0;i<route.Length;i++)
        {
            if(i==changed)
            {
                double at=baseline[i].Time+delta;AdvanceTime(game,at-game.Elapsed);
                Near(game.Elapsed,at,"Actual-time perturbation");
            }
            else MoveTo(game,route[i],.01);
            var p=game.Players[0];
            if(groundOnly)Check(!p.Flipping,"Safe +/-40ms input became airborne at "+i);
            if(p.Flipping)result.AirInputs++;
            Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"Perturbed press rejected at "+i);
        }
        Finish(game,.03);return result;
    }
    static void OpenProof()
    {
        var track=TracksV4.Open();
        Check(TracksV4.IsOpen(track.Id)&&track.Obstacles.Length==0&&track.RiskZones.Length==0,"Open gate contains geometry");
        Near(track.Length/RaceConfig.Speed,10,"Open duration");
        foreach(MotionMode mode in Enum.GetValues(typeof(MotionMode)))
            foreach(double step in new[]{1d/30,1d/60,1d/144,.317,2.5})
            {
                CheckClean(Route(track,mode,Array.Empty<double>(),step,true),0,0,10);
                var normal=Route(track,mode,new double[]{220,440,660,880,1100,1320,1540,1760},step,true);
                CheckClean(normal,0,0,10);
                foreach(var input in normal.Inputs)Near(input.Time,input.X/RaceConfig.Speed,"Open ordinary input clock");
            }
        foreach(double step in new[]{1d/30,1d/60,1d/144,.317,2.5})
        {
            var air=Route(track,MotionMode.GravityAirFlip,new double[]{220,264,660,748,1210,1342},step,false);
            CheckClean(air,0,0,10);
            Check(air.AirInputs==3,"Early/high/late trials need exactly three air repeats");
            foreach(var input in air.Inputs)Near(input.Time,input.X/RaceConfig.Speed,"Open air input clock");
        }
        Console.WriteLine("Open: 3 controls, ordinary route + early/high/late air repeats, 30/60/144 Hz and .317/2.5s steps PASS.");
    }
    static void InputAcrossFreeEffect()
    {
        // Synthetic API fixture, not challenge geometry: reward at x=80 begins
        // before an x=100 input inside the same large outer simulation step.
        foreach(MotionMode mode in Enum.GetValues(typeof(MotionMode)))
            foreach(double step in new[]{1d/30,.317,2.5})
            {
                var track=TracksV4.Open();
                track.RiskZones=new[]{new RiskZone{Id="input-clock-fixture",Kind="positive",FreeReward=true,X=100,Y=226,Radius=6}};
                var result=Route(track,mode,new[]{100d},step,true);
                Near(result.Inputs[0].Time,80d/220+20d/330,"Free reward changed speed before input");
                CheckClean(result,1,0,9.7);
            }
        Console.WriteLine("Input positioning: actual-speed edge after a mid-step free reward PASS.");
    }
    static GameState Prefix(Track track,double target,double[] route)
    {
        var game=Start(track,MotionMode.GravityAirFlip);
        foreach(double x in route)
        {
            if(x>target)break;
            MoveTo(game,x,.01);
            Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"Prefix input rejected at "+x);
        }
        MoveTo(game,target,.01);return game;
    }
    static void ManeuverProof(Track track)
    {
        foreach(var ring in track.RiskZones)
        {
            if(ring.IsNegative)continue;
            var game=Prefix(track,ring.X,track.RiskRoute);var p=game.Players[0];
            Check(p.Flipping,"Ring should be reached in flight: "+ring.Id);
            Check(p.CollectedRiskIds.Contains(ring.Id),"Ring contact not recorded: "+ring.Id);
            if(ring.FreeReward)
                Check(p.SuccessfulRiskIds.Contains(ring.Id)&&p.BoostRemaining>0,"Free ring should award immediately while airborne");
            else
            {
                Check(!p.SuccessfulRiskIds.Contains(ring.Id),"Linked ring should still be pending at contact");
                MoveTo(game,ring.ClearX+.001,.01);
                Check(p.SuccessfulRiskIds.Contains(ring.Id)&&!p.HitObstacleIds.Contains(ring.ObstacleId),"Linked maneuver did not confirm after clean clearance");
            }
        }
        var late=(double[])track.RiskRoute.Clone();late[2]=1420;
        var failed=Prefix(track,1450,late);var q=failed.Players[0];
        Check(q.CollectedRiskIds.Contains("v4-linked-before")&&!q.SuccessfulRiskIds.Contains("v4-linked-before"),"Late linked attempt was not pending");
        MoveTo(failed,1580,.01);
        Check(q.HitObstacleIds.Contains("v4-linked-floor")&&q.FailedRiskIds.Contains("v4-linked-before")&&!q.SuccessfulRiskIds.Contains("v4-linked-before"),"Same-stone collision must cancel linked bonus");
        Check(q.SuccessfulRiskIds.Contains("v4-free-low"),"Later obstacle cannot revoke earlier free reward");
        foreach(int omitted in new[]{1,8})
        {
            var game=Start(track,MotionMode.GravityAirFlip);
            for(int i=0;i<track.RiskRoute.Length;i++)
            {
                if(i==omitted)continue;
                MoveTo(game,track.RiskRoute[i],.01);
                RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);
            }
            Finish(game,.03);
            Check(game.Players[0].Collisions>0||game.Players[0].BoostsEarned<track.PositiveRingCount,
                "An authored air-repeat input is redundant");
        }
        Console.WriteLine("Maneuvers: free contact immediate, linked pending/clear/cancel, both air returns purposeful PASS.");
    }
    static void ChallengeProof()
    {
        var track=TracksV4.Challenge();
        foreach(MotionMode mode in Enum.GetValues(typeof(MotionMode)))
        {
            var safe=Route(track,mode,track.SafeRoute,1d/120,true);
            CheckClean(safe,0,0,20);
            Console.WriteLine("Challenge safe "+mode+":20s,0 hits,0 effects,0 air repeats");
        }
        var ideal=Route(track,MotionMode.GravityAirFlip,track.RiskRoute,1d/120,false);
        foreach(var input in ideal.Inputs)
            Console.WriteLine("input x="+input.X+" time="+input.Time.ToString("F6")+" y="+input.Y.ToString("F3")+" air="+input.Airborne);
        var p=ideal.Game.Players[0];
        Console.WriteLine("Challenge ideal "+p.FinishTime+" hits="+p.Collisions+" positive="+p.BoostsEarned+" negative="+p.SlowsEarned);
        Console.WriteLine(" collected="+string.Join(",",p.SuccessfulRiskIds)+" hits="+string.Join(",",p.HitObstacleIds));
        CheckClean(ideal,track.PositiveRingCount,0,20-track.PositiveRingCount*.3);
        Check(ideal.AirInputs==2,"Ideal challenge must use two purposeful air returns");
        foreach(double step in new[]{1d/30,1d/60,1d/144,.317,2.5})
        {
            foreach(MotionMode mode in Enum.GetValues(typeof(MotionMode)))
                CheckClean(Route(track,mode,track.SafeRoute,step,true),0,0,20);
            CheckClean(Route(track,MotionMode.GravityAirFlip,track.RiskRoute,step,false),4,0,18.8);
        }
        for(int i=0;i<track.RiskRoute.Length;i++)foreach(double delta in new[]{-.04,.04})
        {
            try {CheckClean(TimedShift(track,MotionMode.GravityAirFlip,track.RiskRoute,ideal.Inputs,i,delta,false),4,0,18.8);}
            catch(Exception e){throw new Exception("Ideal input "+i+" shifted "+delta+"s",e);}
        }
        foreach(MotionMode mode in Enum.GetValues(typeof(MotionMode)))
        {
            var baseline=Route(track,mode,track.SafeRoute,.01,true);
            for(int i=0;i<track.SafeRoute.Length;i++)foreach(double delta in new[]{-.04,.04})
                CheckClean(TimedShift(track,mode,track.SafeRoute,baseline.Inputs,i,delta,true),0,0,20);
        }
        double[] early=(double[])track.RiskRoute.Clone();early[6]=2465;
        var hasty=Route(track,MotionMode.GravityAirFlip,early,.01,false);
        Console.WriteLine("Hasty return: "+hasty.Game.Players[0].FinishTime+" hits="+hasty.Game.Players[0].Collisions+" plus="+hasty.Game.Players[0].BoostsEarned+" minus="+hasty.Game.Players[0].SlowsEarned);
        CheckClean(hasty,3,1,19.4);
        Check(hasty.Game.Players[0].NegativeRiskIds.Contains("v4-return-trap")&&!hasty.Game.Players[0].SuccessfulRiskIds.Contains("v4-linked-after"),"Hasty return did not enter the intended trap");
        double[] late=(double[])track.RiskRoute.Clone();late[6]=2620;
        var delayed=Route(track,MotionMode.GravityAirFlip,late,.01,false);
        CheckClean(delayed,3,0,19.1);
        Console.WriteLine("Late return:19.1s,0 hits,3 positives,0 negative; correct return:18.8s/all4");
        ManeuverProof(track);
        foreach(MotionMode mode in Enum.GetValues(typeof(MotionMode)))RecoveryProof(track,mode,track.SafeRoute);
        RecoveryProof(track,MotionMode.GravityAirFlip,track.RiskRoute);
    }
    static int Main()
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        try {OpenProof();InputAcrossFreeEffect();ChallengeProof();Console.WriteLine("PASS "+checks+" V4 route-harness checks");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
