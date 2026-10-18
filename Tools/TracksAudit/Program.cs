using System;
using FlipRace.Core;

public static partial class TracksIndependentAudit
{
    static int checks, games;
    static string context;
    static readonly double[] Steps = { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100 };
    const double U = 350, G = 662.5, Radius = 14, Span = 212;
    static readonly double Apex = U / G, Height = U * U / (2 * G), Cycle = 2 * U / G;
    public static void Main() { Run(); }
    static void C(bool value, string message)
    { checks++; if (!value) throw new Exception(context + ": " + message); }
    static void N(double actual, double expected, string message, double tolerance = 4e-7)
    { C(!double.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance,
        message + " actual=" + actual.ToString("R") + " expected=" + expected.ToString("R")); }
    static double Sign(int side) { return side == 1 ? -1 : 1; }
    static double Wall(int side) { return side == 1 ? 226 : 14; }
    static GameState Start(Track track)
    {
        var g = RaceSimulation.CreateTrial(track, FlightPresets.Chosen);
        C(RaceSimulation.Press(g, 0) && RaceSimulation.Press(g, 1), "both ready accepted");
        RaceSimulation.Advance(g, 3); C(g.Phase == "racing", "countdown completed"); games++; return g;
    }
    static void M(GameState g, double seconds, double step)
    {
        C(seconds >= -1e-9, "nonnegative elapsed request"); int guard = 0;
        while (seconds > 1e-13 && g.Phase == "racing")
        { double dt = Math.Min(seconds, step); RaceSimulation.Advance(g, dt); seconds -= dt;
          if (++guard > 100000) throw new Exception("advance guard"); }
    }
    static void Tap(GameState g)
    { C(RaceSimulation.Press(g, 0) && RaceSimulation.Press(g, 1), "both actual Press accepted"); }
    static GameState Prepare(int side, double step, Track track)
    {
        var g = Start(track);
        if (side == 0)
        {
            // Reach the ceiling with the chosen profile itself: normal jump,
            // then a real air repeat at its analytic apex. No state injection.
            Tap(g); M(g, Apex, step); Tap(g);
            M(g, Math.Sqrt(2 * (Span - Height) / G) + 1e-9, step);
        }
        foreach (var p in g.Players)
            C(p.Support == SupportKind.Wall && p.SupportSide == side && !p.Flipping, "real initial wall support");
        return g;
    }
    static void MoveTo(GameState g, double x, double step)
    {
        int guard = 0;
        while (x - g.Players[0].Distance > 1e-9 && g.Phase == "racing")
        {
            var p = g.Players[0];
            // A public speed upper bound avoids predicting any production
            // contact event. Even an unseen instant/finish cannot overshoot x.
            double dt = Math.Min((x - p.Distance) / 440, step);
            M(g, dt, step);
            if (++guard > 10000) throw new Exception("distance guard");
        }
        foreach (var p in g.Players) N(p.Distance, x, "actual distance target", 2e-6);
    }
    static void ChosenProfile()
    {
        context = "Chosen immutable fields";
        var f = FlightPresets.Chosen;
        C(f.FirstAction == FirstAction.Jump && f.Law == MotionLaw.Gravity
            && f.AirRepeats == AirRepeatLimit.Unlimited, "chosen actions/law/repeats");
        N(f.GravityAcceleration, G, "chosen gravity"); N(f.JumpImpulse, U, "chosen jump"); N(f.FlipImpulse, 0, "inactive flip impulse");
        N(FlightPresets.Jump.JumpImpulse, 400, "old Jump preset retained");
        C(FlightPresets.Jump.AirRepeats == AirRepeatLimit.One && FlightPresets.Unlimited.FirstAction == FirstAction.Flip,
            "old presets retain semantics");
        foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "chosen free side=" + side + " dt=" + step;
            var g = Prepare(side, step, new Track(22000, Array.Empty<Obstacle>())); double began = g.Elapsed;
            Tap(g); foreach (var p in g.Players) N(p.VerticalSpeed, Sign(side) * U, "initial impulse");
            M(g, Apex, step);
            foreach (var p in g.Players)
            { N(RaceSimulation.GetPlayerCenterY(p), Wall(side) + Sign(side) * Height, "analytic apex"); N(p.VerticalSpeed, 0, "apex velocity"); }
            M(g, Cycle - Apex + 1e-9, step);
            foreach (var p in g.Players)
            {
                C(p.Support == SupportKind.Wall && p.SupportSide == side && !p.Flipping, "normal jump returns");
                C(p.LastFlight != null && !p.LastFlight.Interrupted, "completed clean measurement");
                N(p.LastFlight.Duration, Cycle, "analytic duration"); N(p.LastFlight.MaxDistance, Height, "analytic height");
                N(p.LastFlight.EndedAt, began + Cycle, "measurement time");
            }
            Tap(g); M(g, .125, step);
            double v = g.Players[0].VerticalSpeed, y = RaceSimulation.GetPlayerCenterY(g.Players[0]), elapsed = g.Elapsed;
            int initialGravity = g.Players[0].Gravity;
            for (int i = 0; i < 32; i++)
            {
                Tap(g); foreach (var p in g.Players)
                { N(p.VerticalSpeed, v, "repeat retains velocity"); N(RaceSimulation.GetPlayerCenterY(p), y, "repeat retains position");
                  C(p.Gravity == (((i & 1) == 0) ? -initialGravity : initialGravity), "repeat toggles gravity"); }
            }
            N(g.Elapsed, elapsed, "input edges do not advance time");
            M(g, Cycle - .125 + 1e-9, step);
            foreach (var p in g.Players) { C(p.Collisions == 0 && p.SupportSide == side, "even repeat cycle returns clean"); N(p.LastFlight.Duration, Cycle, "repeat remains same measured flight"); }
        }
    }
    static Track RockFixture(int side, double width, double height, double speed, double launch)
    {
        var t = new Track(6000, new[] { new Obstacle { Id = "test-rock", X = 1500, Width = width,
            Height = height, Kind = side == 1 ? "floor" : "ceiling" } });
        if (speed == 330)
        {
            // Three real, co-located instant contacts cover the entire test
            // flight at 330. This is a synthetic fixture, not authored geometry.
            t.RiskZones = new RiskZone[3];
            for (int i = 0; i < 3; i++) t.RiskZones[i] = new RiskZone {
                Id = "setup-instant-" + i, Kind = "instant", X = launch - 1 + 20, Y = Wall(side), Radius = 6 };
        }
        return t;
    }
    static void SingleJumpRocks()
    {
        context = "rock analytic clearance intervals";
        double low = 2 * Math.Sqrt(U * U - 2 * G * 48) / G;
        double high = 2 * Math.Sqrt(U * U - 2 * G * 92) / G;
        C(Height > 92, "high rock impossibility is width/time, not apex height alone");
        foreach (double speed in new[] { 220d, 330d })
        {
            C(low * speed > 64 + 2 * Radius, "low stone has complete horizontal clearance window");
            C(high * speed < 64 + 2 * Radius && high * speed < 200 + 2 * Radius, "high stone cannot fit for any phase at constant speed");
            foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
            foreach (int size in new[] { 0, 1, 2 })
            {
                double width = size == 2 ? 200 : 64, height = size == 0 ? 48 : 92;
                context = "one jump rock=" + width + "x" + height + " speed=" + speed + " side=" + side + " dt=" + step;
                double launch = 1500 + width / 2 - speed * Apex;
                var g = Prepare(side, step, RockFixture(side, width, height, speed, launch));
                if (speed == 330) { MoveTo(g, launch - 1, step); M(g, 1e-9, step); }
                MoveTo(g, launch, step);
                foreach (var p in g.Players) { N(p.CurrentSpeed, speed, "real reward sets requested speed"); C(p.Collisions == 0, "clean approach"); }
                Tap(g); M(g, Cycle + 1e-9, step);
                foreach (var p in g.Players)
                {
                    if (size == 0)
                    { C(p.Collisions == 0 && p.Support == SupportKind.Wall && p.SupportSide == side, "small stone cleared with one ordinary jump");
                      N(p.LastFlight.Duration, Cycle, "small jump time unaffected by horizontal boost"); }
                    else C(p.Collisions == 1 && p.LastHit.Id == "test-rock", "high stone actually collides on one jump");
                }
            }
        }
    }
    public static void Run()
    {
        ChosenProfile(); SingleJumpRocks(); StageOneRoutes(); NarrowEdges(); MarkerEdgesAndReuse(); LocalSafeWindow();
        Console.WriteLine("PASS TRACKS-01 independent: " + checks + " assertions / " + games + " games.");
        Console.WriteLine("Scope: chosen profile, single-jump rocks, authored narrow landing/edges and independent chain. Full tracks not yet covered.");
    }
}
