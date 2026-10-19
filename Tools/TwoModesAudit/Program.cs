using System;
using System.Globalization;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    static int checks, games;
    static string context;
    static readonly double[] Steps = { 1d/30, 1d/60, 1d/144, .317, 2.5, 100 };
    static readonly FlightSettings ExpectedFast = FlightPresets.FastFlip;
    static readonly FlightSettings ExpectedMain = FlightPresets.Chosen;
    const double R = 14, H = 240, Span = 212;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(context + ": " + message); }
    static void Near(double actual, double expected, string message, double tolerance=5e-7)
    { Check(!double.IsNaN(actual) && Math.Abs(actual-expected)<=tolerance,
        message+" actual="+actual.ToString("R")+" expected="+expected.ToString("R")); }
    static double Wall(int side) { return side == 1 ? 226 : 14; }
    static double Away(int side) { return side == 1 ? -1 : 1; }
    static double FlipTime(double distance) { return (Math.Sqrt(35*35+2*2700*distance)-35)/2700; }
    static GameState Start(Track track, FlightSettings flight)
    {
        var game=RaceSimulation.CreateTrial(track,flight);
        Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"two ready presses accepted");
        RaceSimulation.Advance(game,3); Check(game.Phase=="racing","actual countdown ends"); games++;
        return game;
    }
    static void Advance(GameState game,double seconds,double step)
    {
        Check(seconds>=-1e-9,"nonnegative advance request"); int guard=0;
        while(seconds>1e-13&&game.Phase=="racing")
        { double dt=Math.Min(seconds,step); RaceSimulation.Advance(game,dt);seconds-=dt;
          if(++guard>100000)throw new Exception("advance guard"); }
    }
    static void Tap(GameState game)
    { Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"both real presses accepted"); }
    static void MoveTo(GameState game,double target,double step)
    {
        int guard=0;
        while(game.Phase=="racing"&&target-game.Players[0].Distance>2e-7)
        {
            // Public maximum horizontal speed, independent of production event predictors.
            double recovery=game.Players[0].Recovering;
            RaceSimulation.Advance(game,recovery>0 ? Math.Min(step,recovery)
                : Math.Min(step,Math.Max(1e-9,(target-game.Players[0].Distance)/440)));
            if(++guard>100000)throw new Exception(context+": distance guard x="+game.Players[0].Distance+" target="+target);
        }
        foreach(var player in game.Players)Near(player.Distance,target,"actual distance target",2e-6);
    }
    static GameState Prepare(Track track,FlightSettings flight,int side,double step)
    {
        var game=Start(track,flight);
        if(side==0)
        {
            Tap(game);
            if(flight.FirstAction==FirstAction.Flip) Advance(game,FlipTime(Span)+1e-9,step);
            else
            {
                double apex=350/662.5,height=350*350/(2*662.5);
                Advance(game,apex,step);Tap(game);
                Advance(game,Math.Sqrt(2*(Span-height)/662.5)+1e-9,step);
            }
        }
        foreach(var p in game.Players)Check(p.Support==SupportKind.Wall&&p.SupportSide==side&&!p.Flipping,"real initial support");
        return game;
    }
    public static void Main(string[] args) { Run(args.Length==0?"analytics":args[0]); }
    public static void Run(string scope)
    {
        checks=0;games=0;
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        context="production presets";
        Check(ExpectedFast.FirstAction==FirstAction.Flip&&ExpectedFast.Law==MotionLaw.Gravity&&ExpectedFast.AirRepeats==AirRepeatLimit.None,"FastFlip action/law/repeats");
        Near(ExpectedFast.GravityAcceleration,2700,"FastFlip gravity",0);Near(ExpectedFast.FlipImpulse,35,"FastFlip impulse",0);
        Near(ExpectedFast.JumpImpulse,300,"FastFlip inactive jump",0);Near(ExpectedFast.LinearSpeed,530,"FastFlip inactive speed",0);Near(ExpectedFast.LinearJumpHeight,112,"FastFlip inactive height",0);
        Check(ExpectedMain.FirstAction==FirstAction.Jump&&ExpectedMain.Law==MotionLaw.Gravity&&ExpectedMain.AirRepeats==AirRepeatLimit.Unlimited,"Chosen action/law/repeats");
        Near(ExpectedMain.GravityAcceleration,662.5,"Chosen gravity",0);Near(ExpectedMain.FlipImpulse,0,"Chosen flip impulse",0);Near(ExpectedMain.JumpImpulse,350,"Chosen jump",0);
        if(scope=="analytics") Analytics();
        else if(scope=="after") AfterSemantics();
        else if(scope=="late-tail") LateTailRegression();
        else if(scope=="bound") ConditionalTailBounds();
        else if(scope=="stage") StageAudit();
        else if(scope=="gates") OpenGateAudit();
        else if(scope=="full") FullAudit();
        else if(scope=="recovery") FullRecoveryAudit();
        else if(scope=="windows") FullRewardWindows();
        else throw new ArgumentException("Unknown audit scope");
    }
}
