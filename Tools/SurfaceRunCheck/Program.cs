using System;
using FlipRace.Core;

static class Program
{
    static int assertions;
    static readonly double[] Frames = { .0007, 1d / 30, .317, 20 };
    static void Main()
    {
        Action[] tests = { GroundAirLandingAndFlip, FreezeResetAndSnapshot, AdditiveEffects,
            AcceleratedGatesAndRings, MarkedLandingAndEdge, FinishStrokesAndTime,
            HiddenRevealAndPostponement, ConstantCameraBoundary, NonsharedFinishAndGrace, GuardsAndOff };
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception e) { throw new Exception(test.Method.Name + ": " + e.Message, e); }
        }
        Console.WriteLine("PASS: SURFACE RUN " + tests.Length + " scenarios / " + assertions + " assertions.");
    }
    static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    static void Near(double actual, double expected, string message, double tolerance = 2e-8)
    { Check(Math.Abs(actual - expected) < tolerance, message + ": " + actual + " vs " + expected); }
    static Track Empty(double length = 100000) { return new Track(length, Array.Empty<Obstacle>()); }
    static GameState Ready(Track track = null, FlightSettings flight = null)
    {
        var game = RaceSimulation.CreateSolo(track ?? Empty(), flight ?? FlightPresets.Linear, PartyCamera.Lagger, -10000, 20000);
        RaceSimulation.ConfigureSurfaceRun(game, true);
        return game;
    }
    static GameState Start(Track track = null, FlightSettings flight = null)
    { var game = Ready(track, flight); RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 3); return game; }
    static void Run(GameState game, double duration, double frame)
    {
        while (duration > 1e-12 && game.Phase == "racing")
        { double step = Math.Min(frame, duration); RaceSimulation.Advance(game, step); duration -= step; }
    }
    static double Time(double distance, double speed, double acceleration)
    { return 2 * distance / (speed + Math.Sqrt(speed * speed + 2 * acceleration * distance)); }
    static void GroundAirLandingAndFlip()
    {
        foreach (double frame in Frames)
        {
            var game = Start(flight: new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.Unlimited, linearSpeed: 106));
            Run(game, 4, frame); var p = game.Players[0];
            Near(p.SurfaceRunReserve, .20, "four seconds reserve"); Near(p.Distance, 968, "integrated four seconds distance");
            RaceSimulation.Press(game, 0); Run(game, 1, frame);
            Near(p.SurfaceRunReserve, .10, "one second airborne decay"); Near(p.Distance, 1221, "air integral");
            Run(game, 1, frame); Check(p.Support == SupportKind.Wall && !p.Flipping, "ceiling landing");
            Near(p.SurfaceRunReserve, 0, "zero at landing"); Run(game, 2, frame);
            Near(p.SurfaceRunReserve, .10, "immediate ceiling regrowth");
            RaceSimulation.Press(game, 0); Run(game, .1, frame); double before = p.SurfaceRunReserve;
            RaceSimulation.Press(game, 0); Near(p.SurfaceRunReserve, before, "air reversal preserves reserve");
            Run(game, .1, frame); Near(p.SurfaceRunReserve, .08, "decay through reversal");
        }
        var longRun = Start(); Run(longRun, 6, 6); Near(longRun.Players[0].Distance, 1518, "six seconds exact integral");
        Run(longRun, 100, 100); Near(longRun.Players[0].SurfaceRunReserve, 5.3, "unlimited reserve");
        var zero = Start(flight: new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.Unlimited, linearSpeed: 1));
        zero.Players[0].SurfaceRunReserve = .01; RaceSimulation.Press(zero, 0); Run(zero, 10, 10);
        Near(zero.Players[0].SurfaceRunReserve, 0, "cross reserve zero without negative or zero-step loop");
        Near(zero.Players[0].Distance, 2200.11, "air zero crossing exact integral");
    }
    static void FreezeResetAndSnapshot()
    {
        var game = Ready(); RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 2);
        Near(game.Players[0].SurfaceRunReserve, 0, "countdown freeze"); RaceSimulation.Advance(game, 2);
        Near(game.Players[0].SurfaceRunReserve, .05, "countdown leftover");
        game.Phase = "paused"; RaceSimulation.Advance(game, 100); Near(game.Players[0].SurfaceRunReserve, .05, "pause freeze"); game.Phase = "racing";
        var clone = RaceSnapshot.Clone(game); Run(clone, 2, 2);
        Check(clone.SurfaceRunEnabled && clone.Players[0].SurfaceRunEnabled, "snapshot flags");
        Near(game.Players[0].SurfaceRunReserve, .05, "independent snapshot reserve"); Near(clone.Players[0].SurfaceRunReserve, .15, "snapshot progression");
        game.Players[0].Recovering = .5; Run(game, .25, .25); Near(game.Players[0].SurfaceRunReserve, .05, "recovery freeze");
        Run(game, .5, .5); Near(game.Players[0].SurfaceRunReserve, .0625, "post recovery growth");
        game.Phase = "finished"; RaceSimulation.Advance(game, 50); Near(game.Players[0].SurfaceRunReserve, .0625, "finished freeze");
        RaceSimulation.Press(game, 0); Check(game.Players[0].SurfaceRunEnabled, "retry retains flag"); Near(game.Players[0].SurfaceRunReserve, 0, "retry clears reserve");
    }
    static void AdditiveEffects()
    {
        var game = Start(); var p = game.Players[0]; p.SurfaceRunReserve = .2;
        p.ActiveEffect = new SpeedEffect { RingId = "positive", Remaining = .6 };
        Near(p.CurrentSpeed, 374, "positive adds 110"); Run(game, .6, .6); Near(p.Distance, 226.38, "positive integrated distance"); Near(p.SurfaceRunReserve, .23, "positive growth");
        p.ActiveEffect = new SpeedEffect { RingId = "slow", IsNegative = true, Remaining = .6 };
        Near(p.CurrentSpeed, 160.6, "negative subtracts 110"); Run(game, .2, .2);
        p.FinishBoost = true; double slowBefore = p.ActiveEffect.Remaining, distanceBefore = p.Distance;
        Near(p.CurrentSpeed, 492.8, "finish adds 220 and replaces slow delta"); Run(game, .25, .25);
        Near(p.ActiveEffect.Remaining, slowBefore, "finish suspends ordinary service");
        Near(p.Distance - distanceBefore, 123.54375, "finish integrated reserve motion");
        Near(p.SurfaceRunReserve, .2525, "finish growth"); Check(!p.FinishBoost, "finish duration remains .25");
    }
    static void AcceleratedGatesAndRings()
    {
        foreach (double frame in Frames)
        {
            var gate = new Obstacle { Id = "gate", Kind = "gate", X = 500, Width = 12, GapTop = 48, GapBottom = 192, TopOffsetX = 30, BottomOffsetX = -25 };
            var game = Start(new Track(100000, new[] { gate })); var p = game.Players[0]; p.SurfaceRunReserve = 4;
            Run(game, .6, frame); Near(p.LastHit.At, Time(461, 1100, 11), "accelerated offset gate time");
            Check(p.Collisions == 1, "gate deduplicated"); Near(p.SurfaceRunReserve, 0, "impact clears reserve");
            Check(p.Recovering > 0, "existing recovery"); Near(p.Distance, 461, "stopped impact x"); Run(game, 1, frame); Check(p.Collisions == 1, "gate remains one hit");
            var track = Empty(); track.RiskZones = new[] { new RiskZone { Id = "ring", Kind = "positive", FreeReward = true, X = 500, Y = 226, Radius = 6 } };
            game = Start(track); p = game.Players[0]; p.SurfaceRunReserve = 4; Run(game, 1, frame);
            Check(p.BoostsEarned == 1, "accelerated swept ring"); Near(p.LastSuccess.At, Time(480, 1100, 11), "quartic ring time");
            Near(p.SurfaceRunReserve, 4.05, "ring does not reset reserve"); Near(p.Distance, 1105.5 + 110 * (1 - Time(480, 1100, 11)), "ring additive distance");
            game = Start(Empty(), FlightPresets.Chosen); p = game.Players[0]; p.SurfaceRunReserve = .4;
            RaceSimulation.Press(game, 0); double t = .25, x = 308 * t - 11 * t * t;
            double y = 226 - 350 * t + .5 * 662.5 * t * t;
            game.Track.RiskZones = new[] { new RiskZone { Id = "air", Kind = "coin", X = x, Y = y, Radius = 6 } };
            Run(game, .5, frame); Check(p.CoinsEarned == 1, "quadratic x/y quartic air contact");
        }
    }
    static void MarkedLandingAndEdge()
    {
        foreach (double frame in Frames)
        {
            var stone = new Obstacle { Id = "stone", Kind = "floor", X = 500, Width = 400, Height = 68 };
            var track = new Track(100000, new[] { stone }) { RiskZones = new[] { new RiskZone { Id = "landing", Kind = "landing", ObstacleId = "stone", X = 700, LandingWidth = 400 } } };
            var game = Start(track, new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.One, linearSpeed: 100));
            var p = game.Players[0]; p.SurfaceRunReserve = 4; p.Y = (100 - 14d) / 212; p.Support = SupportKind.None; p.Flipping = true; p.VerticalSpeed = 100; p.Gravity = 1;
            Run(game, .7, frame); Check(p.Support == SupportKind.Stone && !p.Flipping, "marked stone support");
            Near(p.LastInstant.At, .58, "landing exact time"); Near(p.SurfaceRunReserve, 3.948, "landing starts growth immediately"); Check(p.Collisions == 0, "legal accelerated landing");
            Run(game, .5, frame); Check(p.Flipping && p.Support == SupportKind.None, "accelerated stone edge walkoff"); Check(p.Collisions == 0, "walkoff no slab hit");
        }
    }
    static void FinishStrokesAndTime()
    {
        double baseline = double.NaN;
        foreach (double frame in Frames)
        {
            var game = Start(Empty(1500)); Run(game, 20, frame);
            Near(game.Players[0].FinishTime.Value, Time(1500, 220, 11), "actual accelerated finish time");
            Near(game.Elapsed, game.Players[0].FinishTime.Value, "shared terminal elapsed");
            var track = Empty(2000); track.RiskZones = new[] { new RiskZone { Id = "finish", Kind = "finish", X = 500, Y = 120 } };
            game = Start(track, new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.Unlimited, linearSpeed: 106)); var p = game.Players[0]; p.SurfaceRunReserve = 4;
            p.Y = .5; p.Support = SupportKind.None; p.Flipping = true; p.VerticalSpeed = 0; p.Gravity = 1;
            Run(game, .8, frame); Check(p.FinishBoostsEarned == 1, "accelerated chevron contact");
            Near(p.FinishBoostElapsed, .25, "finish full service");
            if (double.IsNaN(baseline)) baseline = p.LastFinishBoost.At; else Near(p.LastFinishBoost.At, baseline, "chevron partition time");
            game = Start(track); game.Players[0].SurfaceRunReserve = 4; Run(game, .8, frame); Check(game.Players[0].FinishBoostsEarned == 0, "nearby floor misses strokes");
        }
    }
    static void HiddenRevealAndPostponement()
    {
        foreach (double frame in Frames)
        {
            var stone = new Obstacle { Id = "hidden", Kind = "floor", Hidden = true, X = 600, Width = 2000, Height = 68 };
            var game = Start(new Track(100000, new[] { stone })); var p = game.Players[0]; p.SurfaceRunReserve = 4;
            Run(game, 1.2, frame); var state = game.HiddenObstacles["hidden"];
            Near(state.RevealedAt.Value, Time(160, 1100, 11), "accelerated hidden trigger exact");
            Check(state.Phase == HiddenObstaclePhase.Warning && state.ActivationPostponed, "occupied activation postponed"); Check(p.Collisions == 0, "warning harmless");
            Run(game, 1.5, frame); Check(state.Phase == HiddenObstaclePhase.Solid, "body clearance solid"); Check(p.Collisions == 0, "clearance no surprise hit");
        }
    }
    static void ConstantCameraBoundary()
    {
        foreach (double frame in Frames)
        {
            var game = RaceSimulation.CreateSolo(Empty(10800), FlightPresets.Linear, PartyCamera.Constant, 0, 1520);
            RaceSimulation.ConfigureSurfaceRun(game, true); RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 3);
            var p = game.Players[0]; p.SurfaceRunReserve = .2; Run(game, 2, frame);
            double expected = (36 - Math.Sqrt(36 * 36 - 4 * 5.5 * 14)) / 11;
            Check(p.Status == PlayerStatus.Eliminated, "quadratic left boundary elimination"); Near(p.TerminalTime.Value, expected, "left boundary exact time");
            game = RaceSimulation.CreateSolo(Empty(10800), FlightPresets.Linear, PartyCamera.Constant, -100, 1520);
            RaceSimulation.ConfigureSurfaceRun(game, true); RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 3);
            p = game.Players[0]; p.SurfaceRunReserve = .2; Run(game, 8, frame);
            Check(p.Status == PlayerStatus.Running, "acceleration closes speed deficit before left bound");
        }
    }
    static void NonsharedFinishAndGrace()
    {
        foreach (double frame in new[] { .317, 200d })
        {
            var track = Empty(100000);
            // A future uninterrupted reward queue makes the winner finish more
            // than one grace period before the optimistic unboosted arrival.
            var rings = new RiskZone[300];
            for (int i = 0; i < rings.Length; i++) rings[i] = new RiskZone
            { Id = "future-" + i, Kind = "positive", FreeReward = true, X = 200, Y = 226, Radius = 6 };
            track.RiskZones = rings;
            var game = RaceSimulation.CreateTrial(track, FlightPresets.Linear);
            RaceSimulation.ConfigureSurfaceRun(game, true);
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            var winner = game.Players[0]; var lagger = game.Players[1];
            winner.SurfaceRunReserve = lagger.SurfaceRunReserve = 1;
            lagger.Y = 0; lagger.SupportSide = 0;
            double contact = Time(180, 440, 11);
            double expectedFinish = Time(track.Length + 110 * contact, 550, 11);
            Run(game, 200, frame);
            Near(winner.FinishTime.Value, expectedFinish, "nonshared accelerated actual finish timestamp");
            Near(game.Elapsed, expectedFinish + RaceConfig.FinishGrace, "nonshared grace deadline exact");
            Check(game.Phase == "finished" && !lagger.FinishTime.HasValue, "large advance stops lagger at grace");
            double expectedDistance = 440 * game.Elapsed + 5.5 * game.Elapsed * game.Elapsed;
            Near(lagger.Distance, expectedDistance, "lagger receives only time before grace", 2e-6);
            Check(winner.BoostsEarned == rings.Length, "future reward queue collected");
        }
    }
    static void GuardsAndOff()
    {
        var off = RaceSimulation.CreateSolo(Empty(), FlightPresets.Linear, PartyCamera.Lagger, 0, 1000);
        Check(!off.SurfaceRunEnabled && !off.Players[0].SurfaceRunEnabled, "factory default off"); RaceSimulation.Press(off, 0); RaceSimulation.Advance(off, 7);
        Near(off.Players[0].Distance, 880, "off original 220"); Near(off.Players[0].SurfaceRunReserve, 0, "off no accumulation");
        bool rejected = false; try { RaceSimulation.ConfigureSurfaceRun(off, true); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "attempt immutable");
        var bot = RaceSimulation.CreateStochasticBotRace(RaceTracks.Get(TracksRhythm.CascadeId), FlightPresets.Chosen, BotDifficulty.Master, 1u);
        rejected = false; try { RaceSimulation.ConfigureSurfaceRun(bot, true); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "bot on rejected");
    }
}
