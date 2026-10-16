using System;
using System.Globalization;
using FlipRace.Core;

internal static partial class Program
{
    static double FlightDuration(MotionProfile profile)
    {
        double a=profile.GravityAcceleration,v=profile.DepartureImpulse;
        return (Math.Sqrt(v*v+2*a*(RaceConfig.CorridorHeight-2*RaceConfig.PlayerRadius))-v)/a;
    }
    static double ReturnDelay(MotionProfile profile,double height)
    {
        double a=profile.GravityAcceleration,v=profile.DepartureImpulse;
        return (-v+Math.Sqrt(v*v/2+a*height))/a;
    }
    static void OpenProfilesProof()
    {
        foreach(var profile in MotionProfiles.All)
        {
            double duration=FlightDuration(profile);
            foreach(double step in new[]{1d/30,1d/60,1d/144,.317,2.5})
            {
                var noInput=Route(TracksV4.Open(),MotionMode.GravityAirFlip,Array.Empty<double>(),step,true,profile);
                CheckClean(noInput,0,0,10);
                var normal=Route(TracksV4.Open(),MotionMode.GravityAirFlip,new double[]{220,440,660,880,1100,1320,1540,1760},step,true,profile);
                CheckClean(normal,0,0,10);
                var air=Route(TracksV4.Open(),MotionMode.GravityAirFlip,new double[]{220,253,880,880+220*ReturnDelay(profile,80),1540,1540+220*duration*.9},step,false,profile);
                CheckClean(air,0,0,10);Check(air.AirInputs==3,"Open trial must use three air repeats");
                Check(ReferenceEquals(air.Game.Profile,profile),"Harness lost selected profile");
            }
            Console.WriteLine("Open profile="+profile.Id+" full-flight="+duration.ToString("F9")+" low-repeat="+ReturnDelay(profile,80).ToString("F9")+" high-repeat="+ReturnDelay(profile,135).ToString("F9")+" PASS");
        }
    }
    static int Main()
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        try{OpenProfilesProof();InitialChallengeProof();RouteMatrixProof();TrapProof();AirReturnProof();FinishAndBacklogProof();WindowReport();RecoveryMatrixProof();Console.WriteLine("PASS "+checks+" TUNE harness checks");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void InitialChallengeProof()
    {
        foreach(var profile in MotionProfiles.All)
        {
            var track=TracksTune.Challenge(profile);
            foreach(bool safe in new[]{true,false})
            {
                var result=Route(track,MotionMode.GravityAirFlip,safe?track.SafeRoute:track.RiskRoute,.01,safe,profile);
                var p=result.Game.Players[0];
                Console.WriteLine(profile.Id+" "+(safe?"safe":"ideal")+" finish="+p.FinishTime+" positives="+p.BoostsEarned+" instant="+p.InstantBoostsEarned+" negative="+p.SlowsEarned+" hits="+string.Join(",",p.HitObstacleIds)+" collected="+string.Join(",",p.SuccessfulRiskIds));
                CheckClean(result,safe?0:6,0,safe?21:19.2);
            }
        }
    }
    static void RouteMatrixProof()
    {
        int failures=0;
        foreach(var profile in MotionProfiles.All)
        {
            var track=TracksTune.Challenge(profile);
            foreach(bool safe in new[]{true,false})
            {
                var inputs=safe?track.SafeRoute:track.RiskRoute;
                var baseline=Route(track,MotionMode.GravityAirFlip,inputs,.01,safe,profile);
                foreach(double step in new[]{1d/30,1d/60,1d/144,.317,2.5})
                    CheckClean(Route(track,MotionMode.GravityAirFlip,inputs,step,safe,profile),safe?0:6,0,safe?21:19.2);
                for(int i=0;i<inputs.Length;i++)foreach(double delta in new[]{-.04,.04})
                {
                    try{CheckClean(TimedShift(track,MotionMode.GravityAirFlip,inputs,baseline.Inputs,i,delta,safe,profile),safe?0:6,0,safe?21:19.2);}
                    catch(Exception error){failures++;Console.WriteLine("WINDOW FAIL "+profile.Id+" "+(safe?"safe":"ideal")+" input="+i+" delta="+delta+" "+error.Message);}
                }
                if(!safe)
                {
                    Check(baseline.AirInputs==2,"Ideal must contain two air repeats");
                    Check(baseline.Game.Players[0].InstantBoostsEarned==3,"Ideal needs all three instant rewards");
                    Console.WriteLine("Repeat intervals "+profile.Id+": "+(baseline.Inputs[1].Time-baseline.Inputs[0].Time).ToString("F6")+", "+(baseline.Inputs[8].Time-baseline.Inputs[7].Time).ToString("F6"));
                }
            }
        }
        Check(failures==0,"Individual +/-40ms route failures: "+failures);
    }
    static void TrapProof()
    {
        foreach(var profile in MotionProfiles.All)
        {
            var track=TracksTune.Challenge(profile);
            foreach(double step in new[]{1d/30,1d/60,1d/144,.317,2.5})
            {
                var result=Route(track,MotionMode.GravityAirFlip,TracksTune.NegativeRoute(profile),step,false,profile);
                var p=result.Game.Players[0];
                if(step==1d/60)Console.WriteLine("Trap "+profile.Id+" finish="+p.FinishTime+" plus="+p.BoostsEarned+" minus="+p.SlowsEarned+" hits="+string.Join(",",p.HitObstacleIds));
                CheckClean(result,5,1,19.8);
                Check(p.InstantBoostsEarned==3&&p.NegativeRiskIds.Contains("tune-return-trap")
                    &&!p.SuccessfulRiskIds.Contains("tune-linked-after"),"Hasty route must retain instants and lose the after-ring");
            }
        }
    }
    static void RecoveryMatrixProof()
    {
        foreach(var profile in MotionProfiles.All)
        {
            Console.WriteLine("Recovery profile="+profile.Id);
            var track=TracksTune.Challenge(profile);
            RecoveryProof(track,MotionMode.GravityAirFlip,track.SafeRoute,profile);
            RecoveryProof(track,MotionMode.GravityAirFlip,track.RiskRoute,profile);
        }
    }
    static void AirReturnProof()
    {
        foreach(var profile in MotionProfiles.All)foreach(int omitted in new[]{1,8})
        {
            var track=TracksTune.Challenge(profile);var game=Start(track,MotionMode.GravityAirFlip,profile);
            for(int i=0;i<track.RiskRoute.Length;i++)
            {
                if(i==omitted)continue;
                MoveTo(game,track.RiskRoute[i],.01);
                RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);
            }
            Finish(game,.02);
            Check(game.Players[0].Collisions>0||game.Players[0].InstantBoostsEarned<3,
                "An ideal air return is redundant: "+profile.Id+" input="+omitted);
        }
        Console.WriteLine("Both authored air returns are purposeful in all profiles PASS.");
    }
}
