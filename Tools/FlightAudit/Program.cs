using System;
using FlipRace.Core;

public static partial class FlightIndependentAudit
{
    static int checks, games;
    static string context;
    static readonly double[] Steps = { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100 };
    static readonly AirRepeatLimit[] Repeats = { AirRepeatLimit.None, AirRepeatLimit.One, AirRepeatLimit.Unlimited };
    static void C(bool yes, string message) { checks++; if (!yes) throw new Exception(context + ": " + message); }
    static void N(double actual, double expected, string message, double tolerance = 3e-7)
    { C(!double.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance, message + ": " + actual.ToString("R") + " != " + expected.ToString("R")); }
    static double Away(int side) { return side == 1 ? -1 : 1; }
    static double WallY(int side) { return side == 1 ? 226 : 14; }
    static double Travel(double distance, double speed, double acceleration)
    { return 2 * distance / (Math.Sqrt(speed * speed + 2 * acceleration * distance) + speed); }
    static GameState Start(FlightSettings f, Track track = null)
    {
        var g = RaceSimulation.CreateTrial(track ?? new Track(220000, Array.Empty<Obstacle>()), f);
        C(RaceSimulation.Press(g, 0) && RaceSimulation.Press(g, 1), "both ready");
        RaceSimulation.Advance(g, 3); C(g.Phase == "racing", "countdown completed"); games++; return g;
    }
    static void M(GameState g, double duration, double step)
    {
        C(duration >= -1e-9, "nonnegative requested elapsed"); int guard = 0;
        while (duration > 1e-13 && g.Phase == "racing")
        { double dt = Math.Min(duration, step); RaceSimulation.Advance(g, dt); duration -= dt; if (++guard > 100000) throw new Exception("advance guard"); }
    }
    static void Tap(GameState g) { C(RaceSimulation.Press(g, 0) && RaceSimulation.Press(g, 1), "both actual input edges accepted"); }
    // Mirror fixture: establish the ceiling by an actual no-repeat flip first.
    // Only the immutable tested settings reference changes at stationary support;
    // no position, velocity, support, charge, clock or result is fabricated.
    static GameState Prepare(FlightSettings f, int side, double step, Track track = null)
    {
        if (side == 1) return Start(f, track);
        var prep = new FlightSettings(FirstAction.Flip, f.Law, f.AirRepeats, f.GravityAcceleration,
            f.FlipImpulse, f.JumpImpulse, f.LinearSpeed, f.LinearJumpHeight);
        var g = Start(prep, track); Tap(g);
        M(g, f.Law == MotionLaw.Linear ? 212 / f.LinearSpeed : Travel(212, f.FlipImpulse, f.GravityAcceleration), step);
        foreach (var p in g.Players) C(p.Support == SupportKind.Wall && p.SupportSide == 0 && !p.Flipping, "real ceiling preparation");
        g.Flight = f; return g;
    }
    static void Support(GameState g, int side, AirRepeatLimit repeats)
    {
        foreach (var p in g.Players)
        {
            N(RaceSimulation.GetPlayerCenterY(p), WallY(side), "wall endpoint"); N(p.VerticalSpeed, 0, "wall velocity");
            C(!p.Flipping && !p.FlightActive && p.Support == SupportKind.Wall && p.SupportSide == side, "stable completed wall flight");
            C(p.AirFlipAvailable == (repeats != AirRepeatLimit.None), "recharge obeys independent limit");
        }
    }
    static void Measurement(PlayerState p, double duration, double height, double endedAt, int side, bool interrupted = false, string origin = null)
    {
        C(p.LastFlight != null, "measurement exists"); N(p.LastFlight.Duration, duration, "measured active flight time");
        N(p.LastFlight.MaxDistance, height, "analytic maximum distance"); N(p.LastFlight.EndedAt, endedAt, "physical end timestamp");
        C(p.LastFlight.Interrupted == interrupted && p.LastFlight.OriginSide == side && p.LastFlight.OriginObstacleId == origin, "measurement flags and origin");
    }
    static void FreeCycles()
    {
        foreach (var action in new[] { FirstAction.Flip, FirstAction.Jump })
        foreach (double gravity in new[] { 662.5, 2700 })
        foreach (double impulse in action == FirstAction.Flip ? new[] { 0d, 420 } : new[] { 200d, 550 })
        foreach (var repeat in Repeats) foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "gravity " + action + " g" + gravity + " u" + impulse + " r" + repeat + " side" + side + " dt" + step;
            var f = new FlightSettings(action, MotionLaw.Gravity, repeat, gravity,
                action == FirstAction.Flip ? impulse : 70, action == FirstAction.Jump ? impulse : 400);
            var g = Prepare(f, side, step); double start = g.Elapsed; Tap(g); double sign = Away(side);
            double expectedTime, maximum; int endSide;
            if (action == FirstAction.Flip) { expectedTime = Travel(212, impulse, gravity); maximum = 212; endSide = 1 - side; }
            else
            {
                maximum = impulse * impulse / (2 * gravity); expectedTime = 2 * impulse / gravity; endSide = side;
                if (maximum > 212)
                {
                    double wall = (impulse - Math.Sqrt(impulse * impulse - 424 * gravity)) / gravity;
                    M(g, wall, step); foreach (var p in g.Players)
                    {
                        N(RaceSimulation.GetPlayerCenterY(p), WallY(1 - side), "opposite wall before apex");
                        C(p.Flipping && p.FlightActive && p.Support == SupportKind.None, "wrong wall neither ends flight nor supports");
                        N(p.FlightMaxDistance, 212, "actual maximum clipped by wall");
                    }
                    expectedTime = wall + Math.Sqrt(424 / gravity); maximum = 212;
                }
            }
            M(g, start + expectedTime - g.Elapsed + 1e-9, step); Support(g, endSide, repeat);
            foreach (var p in g.Players) Measurement(p, expectedTime, maximum, start + expectedTime, side);
        }
        foreach (var action in new[] { FirstAction.Flip, FirstAction.Jump })
        foreach (double fullTime in new[] { 1.2, .8, .4 })
        foreach (double height in action == FirstAction.Jump ? new[] { 48d, 112, 192 } : new[] { 112d })
        foreach (var repeat in Repeats) foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "linear " + action + " T" + fullTime + " H" + height + " r" + repeat + " side" + side + " dt" + step;
            double speed = 212 / fullTime, duration = action == FirstAction.Flip ? fullTime : 2 * height / speed;
            var f = new FlightSettings(action, MotionLaw.Linear, repeat, 2700, 420, 550, speed, height);
            var g = Prepare(f, side, step); double start = g.Elapsed; Tap(g);
            if (action == FirstAction.Jump)
            {
                M(g, height / speed, step); foreach (var p in g.Players)
                {
                    N(RaceSimulation.GetPlayerCenterY(p), WallY(side) + Away(side) * height, "exact linear apex");
                    C(p.Flipping && p.FlightActive && p.Support == SupportKind.None, "apex is not support");
                    N(p.VerticalSpeed, -Away(side) * speed, "apex reverses without kick");
                }
            }
            M(g, start + duration - g.Elapsed + 1e-9, step); Support(g, action == FirstAction.Flip ? 1 - side : side, repeat);
            foreach (var p in g.Players) Measurement(p, duration, action == FirstAction.Flip ? 212 : height, start + duration, side);
        }
    }
    public static void Run()
    {
        checks = games = 0; FreeCycles(); RepeatRules(); StoneCases(); InterruptAndReset();
        Console.WriteLine("PASS " + checks + " independent FLIGHT assertions / " + games + " games:12 combinations, extreme mirrored cycles, analytic measurements, air repeats, stone contacts/walkoff, collision interruption/reset.");
    }
    public static void Main() { Run(); }
}
