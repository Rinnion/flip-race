using System;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    // Immutable geometry witness, copied as data before any recovery fix.
    // Original TracksRhythm.cs SHA 84565BBC2DDB13C7F3FE39D453DB83CA9D9C429F649DA14A69883E5D6F21FD21.
    static Track InitialStageFixture()
    {
        double[] positions={300,900,1500,1800,2130,2410,2780,3100};
        var gates=new Obstacle[positions.Length];
        for(int i=0;i<gates.Length;i++)gates[i]=new Obstacle{Id="rhythm-practice-gate-"+i,Kind="gate",X=positions[i],Width=16,
            GapTop=140,GapBottom=100,TopOffsetX=i%2==0?0:192,BottomOffsetX=i%2==0?192:0};
        return new Track(3500,gates){Id="historical-rhythm-recovery-red"};
    }
    static void RecoveryRed()
    {
        context="historical actual Fast delayed input4";
        var track=InitialStageFixture();var plan=Oracle(track,FlightPresets.FastFlip);
        var game=PerturbedHit(track,FlightPresets.FastFlip,plan,4,.64);
        Need(game!=null,"first actual collision found");
        var p=game.Players[0];Need(p.LastHit.Id=="rhythm-practice-gate-4"&&p.Collisions==1,"first hit only");
        Near(p.Distance,2308,"first hit x");Near(RaceSimulation.GetPlayerCenterY(p),226,"floor centre");
        Console.WriteLine("HISTORICAL RED first hit elapsed="+game.Elapsed.ToString("R")+" x="+p.Distance.ToString("R")+" y="+RaceSimulation.GetPlayerCenterY(p));
        Advance(game,p.Recovering+.15,.001);
        Near(p.Distance,2341,"after remaining penalty and150ms");
        double nextFront=track.Obstacles[5].X-Radius;
        double available=(nextFront-p.Distance)/Speed,required=RiseTime(140,FlightPresets.FastFlip);
        Console.WriteLine("After reaction x="+p.Distance.ToString("R")+" next front="+nextFront+" available="+available.ToString("R")+" minimum rise140="+required.ToString("R"));
        Need(available<required,"analytic forced-next deficit");
        Need(TapBoth(game),"earliest legal departure after reaction");
        Advance(game,available+1e-7,1d/144);
        Need(p.Collisions==2&&p.LastHit.Id=="rhythm-practice-gate-5","earliest legal departure still hits next lower band");
        Near(p.Distance,nextFront,"second physical hit x");
        Console.WriteLine("Second actual hit x="+p.Distance.ToString("R")+" y="+RaceSimulation.GetPlayerCenterY(p).ToString("R")+" hits="+p.Collisions);
        Console.WriteLine("EXPECTED RED reproduced: initial stage needs more recovery spacing. Real Press/Advance only; no player/effect injection. One air reversal cannot improve the maximal upward acceleration.");
    }
    static double AcceleratedTime(double distance,double velocity,double gravity)
    {return distance<=0?0:(Math.Sqrt(velocity*velocity+2*gravity*distance)-velocity)/gravity;}
    static void RecoveryConstraints()
    {
        var track=InitialStageFixture();
        foreach(var f in Profiles)
        {
            var plan=Oracle(track,f);
            for(int i=0;i<plan.Count;i++)
            {
                if(f.FirstAction==FirstAction.Jump&&!plan[i].Air)continue;
                context=Name(f)+" historical late input="+i;
                var g=PerturbedHit(track,f,plan,i,.64);if(g==null)continue;
                int index=Array.FindIndex(track.Obstacles,o=>o.Id==g.Players[0].LastHit.Id);
                if(index+1==track.Obstacles.Length)continue;
                var next=track.Obstacles[index+1];var p=g.Players[0];double hitX=p.Distance;
                Advance(g,p.Recovering+.15,.001);
                double y=RaceSimulation.GetPlayerCenterY(p),vy=p.VerticalSpeed;
                bool lowerFirst=next.BottomOffsetX<next.TopOffsetX;
                double targetY=lowerFirst?next.GapBottom-Radius:next.GapTop+Radius;
                double sign=lowerFirst?-1:1;
                double firstFront=next.X+(lowerFirst?next.BottomOffsetX:next.TopOffsetX)-Radius;
                double available=(firstFront-p.Distance)/Speed;
                double direct=AcceleratedTime(sign*(targetY-y),sign*vy,f.GravityAcceleration);
                double oppositeWall=sign<0?Height-Radius:Radius;
                double toWall=AcceleratedTime(-sign*(oppositeWall-y),-sign*vy,f.GravityAcceleration);
                double depart=f.FirstAction==FirstAction.Jump?f.JumpImpulse:f.FlipImpulse;
                // Relaxed lower bound: permit immediate Main jump+gravity reversal
                // at the opposite wall. This is more permissive than a comfortable
                // recovery route, so an insufficient time is a genuine blocker.
                double viaWall=toWall+AcceleratedTime(sign*(targetY-oppositeWall),depart,f.GravityAcceleration);
                double lowerBound=Math.Min(direct,viaWall);
                double pitchLower=p.Distance-track.Obstacles[index].X+Speed*lowerBound+Radius;
                Console.WriteLine(Name(f)+" input="+i+" hitGate="+index+" hitX="+hitX.ToString("F6")+" reactionX="+p.Distance.ToString("F6")
                    +" Y="+y.ToString("F6")+" VY="+vy.ToString("F6")+" support="+p.Support+" targetY="+targetY
                    +" available="+available.ToString("F9")+" relaxedMin="+lowerBound.ToString("F9")
                    +" currentPitch="+(next.X-track.Obstacles[index].X)+" necessaryPitch>"+pitchLower.ToString("F6")
                    +" insufficient="+(available<lowerBound));
            }
        }
        Console.WriteLine("Historical necessary-bound map only, not a successful recovery or sufficient pitch proof.");
    }
    static void RecoveryPitch()
    {
        // Only the already demonstrated historical late-impact states. Move
        // the next gate and its suffix; never edit the production stage.
        foreach(var f in Profiles)foreach(int gate in new[]{3,4})
        {
            int changed=f.FirstAction==FirstAction.Jump?gate*2+1:gate;
            int firstFound=-1;
            for(int pitch=280;pitch<=460;pitch+=10)
            {
                var old=InitialStageFixture();double delta=pitch-(old.Obstacles[gate+1].X-old.Obstacles[gate].X);
                for(int i=gate+1;i<old.Obstacles.Length;i++)old.Obstacles[i].X+=delta;
                var track=new Track(old.Length+delta,old.Obstacles){Id="historical-pitch-fixture"};
                context=Name(f)+" gate="+gate+" pitch="+pitch;
                var hit=PerturbedHit(track,f,Oracle(track,f),changed,.64);
                Need(hit!=null&&hit.Players[0].LastHit.Id=="rhythm-practice-gate-"+gate,"same real first hit before shifted suffix");
                string witness;bool found=Escape(hit,out witness);
                Console.WriteLine(context+" found="+found+(found?" "+witness:""));
                if(found){firstFound=pitch;break;}
            }
            Need(firstFound>=0,"bounded pitch search produced a real escape");
        }
        Console.WriteLine("PASS historical pitch witnesses. First found on a 10-world-unit grid with a finite legal-control search; not a globally minimal sufficient pitch. Only actual Press/Advance, no player/effect injection. Nominal oracle Main repeat .18s; recovery may use a settling tap, true wall support and a later two-tap passage.");
    }
}
