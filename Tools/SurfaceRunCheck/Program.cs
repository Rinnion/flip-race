using System;
using FlipRace.Core;

static class Program
{
    static int assertions;
    static readonly double[] Frames = { .0007, 1d / 30, .317, 20 };
    static readonly SurfaceRunSettings Cap40 = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 40);
    static void Main()
    {
        Action[] tests = { SettingsMatrix, GroundAirLandingAndFlip, ZeroDecayThroughAirFlip, CollisionResetOptions,
            FreezeResetAndSnapshot, AdditiveEffects,
            AcceleratedGatesAndRings, MarkedLandingAndEdge, FinishStrokesAndTime,
            HiddenRevealAndPostponement, ConstantCameraBoundary, ExponentialSweeps, ExponentialCamera,
            NonsharedFinishAndGrace, GuardsAndOff };
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
    static GameState Ready(Track track = null, FlightSettings flight = null, SurfaceRunSettings? settings = null)
    {
        var game = RaceSimulation.CreateSolo(track ?? Empty(), flight ?? FlightPresets.Linear, PartyCamera.Lagger, -10000, 20000);
        RaceSimulation.ConfigureSurfaceRun(game, true, settings ?? SurfaceRunSettings.Default);
        return game;
    }
    static GameState Start(Track track = null, FlightSettings flight = null, SurfaceRunSettings? settings = null)
    { var game = Ready(track, flight, settings); RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 3); return game; }
    static void Run(GameState game, double duration, double frame)
    {
        while (duration > 1e-12 && game.Phase == "racing")
        { double step = Math.Min(frame, duration); RaceSimulation.Advance(game, step); duration -= step; }
    }
    static double Time(double distance, double speed, double acceleration)
    { return 2 * distance / (speed + Math.Sqrt(speed * speed + 2 * acceleration * distance)); }
    static double GroundReserve(SurfaceRunSettings settings, double start, double time)
    {
        double cap = settings.Cap, rate = settings.Rate;
        return settings.Curve == SurfaceRunCurve.Linear ? Math.Min(cap, start + rate * time)
            : cap - (cap - start) * Math.Exp(-rate * time / cap);
    }
    static double GroundDistance(SurfaceRunSettings settings, double start, double time)
    {
        double cap = settings.Cap, rate = settings.Rate;
        if (settings.Curve == SurfaceRunCurve.Exponential)
            return 220 * ((1 + cap) * time - (cap - start) * cap / rate * (1 - Math.Exp(-rate * time / cap)));
        double ramp = Math.Min(time, (cap - start) / rate);
        return 220 * ((1 + start) * ramp + rate * ramp * ramp / 2 + (1 + cap) * (time - ramp));
    }
    static double AirDistance(double start, double decay, double time)
    {
        double ramp = decay == 0 ? time : Math.Min(time, start / decay);
        return 220 * (time + start * ramp - decay * ramp * ramp / 2);
    }
    static void SettingsMatrix()
    {
        foreach (int rate in new[] { 1, 2, 3, 4, 5 })
        foreach (int cap in new[] { 20, 30, 40 })
        foreach (var curve in new[] { SurfaceRunCurve.Linear, SurfaceRunCurve.Exponential })
        foreach (int airDecay in new[] { 0, 50, 100, 200, 400 })
        foreach (bool resetOnCollision in new[] { false, true })
        foreach (double frame in new[] { .013, 50d })
        {
            var settings = new SurfaceRunSettings(rate, curve, cap, airDecay, resetOnCollision);
            var flight = new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.One, linearSpeed: 106);
            var game = Start(flight: flight, settings: settings);
            var p = game.Players[0];
            double decay = rate * airDecay / 10000d;
            Near(p.SurfaceRunSettings.AirDecayRatePercent, 100 * decay, "air rate in percentage points per second");
            Check(p.SurfaceRunSettings.AirDecayPercent == airDecay
                && p.SurfaceRunSettings.ResetOnCollision == resetOnCollision, "attempt retains both configured options");
            Run(game, 3.75, frame);
            double before = GroundReserve(settings, 0, 3.75);
            Near(p.SurfaceRunReserve, before, "configured ground reserve");
            Near(p.Distance, GroundDistance(settings, 0, 3.75), "configured ground integral");
            double startX = p.Distance;
            RaceSimulation.Press(game, 0);
            Run(game, .75, frame);
            Near(p.SurfaceRunReserve, Math.Max(0, before - decay * .75), "air decay scales with nominal rate");
            Near(p.Distance - startX, AirDistance(before, decay, .75), "configured air integral");
            Run(game, 1.25, frame);
            Check(p.Support == SupportKind.Wall && p.SupportSide == 0, "configured ceiling landing");
            double landed = Math.Max(0, before - decay * 2);
            Near(p.SurfaceRunReserve, landed, "landing retains the remaining reserve");
            Near(p.Distance - startX, AirDistance(before, decay, 2), "air integral through landing");
            startX = p.Distance;
            Run(game, 50, frame);
            Near(p.SurfaceRunReserve, GroundReserve(settings, landed, 50), "cap or asymptote after sustained run");
            Near(p.Distance - startX, GroundDistance(settings, landed, 50), "integral through cap or asymptote", 2e-7);
            Check(p.SurfaceRunReserve <= settings.Cap, "cap is respected");
        }
    }
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
        Run(longRun, 100, 100); Near(longRun.Players[0].SurfaceRunReserve, .30, "default linear cap");
        Near(longRun.Players[0].Distance, 30118, "constant speed after cap");
        var zero = Start(flight: new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.Unlimited, linearSpeed: 1));
        zero.Players[0].SurfaceRunReserve = .01; RaceSimulation.Press(zero, 0); Run(zero, 10, 10);
        Near(zero.Players[0].SurfaceRunReserve, 0, "cross reserve zero without negative or zero-step loop");
        Near(zero.Players[0].Distance, 2200.11, "air zero crossing exact integral");
    }
    static void ZeroDecayThroughAirFlip()
    {
        var settings = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, 0);
        var flight = new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.One, linearSpeed: 1);
        foreach (double frame in new[] { .317, 60d })
        {
            var game = Start(flight: flight, settings: settings);
            var p = game.Players[0];
            Run(game, 4, frame);
            double startX = p.Distance;
            Check(RaceSimulation.Press(game, 0), "initial zero-decay takeoff");
            Run(game, 60, frame);
            Near(p.SurfaceRunReserve, .2, "long zero-decay flight holds reserve");
            Check(p.Flipping && RaceSimulation.Press(game, 0), "legal air reversal");
            Near(p.SurfaceRunReserve, .2, "air reversal holds reserve");
            Run(game, 60, frame);
            Check(p.Support == SupportKind.Wall && p.SupportSide == 1, "zero-decay return to original wall");
            Near(p.SurfaceRunReserve, .2, "zero-decay landing holds reserve");
            Near(p.Distance - startX, 120 * 264, "constant horizontal speed through the whole flight");
            Run(game, .5, frame);
            Near(p.SurfaceRunReserve, .225, "ground growth resumes after zero-decay landing");
        }
    }
    static void CollisionResetOptions()
    {
        const double before = .4, decay = .05, at = .4, contact = .12;
        double hitX = AirDistance(before, decay, at);
        var flight = new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.One, linearSpeed: 106);
        foreach (bool reset in new[] { false, true })
        foreach (double frame in Frames)
        {
            var settings = new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 40, 100, reset);
            var gate = new Obstacle { Id = "gate", Kind = "gate", X = hitX + 39, Width = 12,
                GapTop = 48, GapBottom = 192, TopOffsetX = 30, BottomOffsetX = -25 };
            var track = new Track(100000, new[] { gate }) { RiskZones = new[] {
                new RiskZone { Id = "pending", Kind = "positive", ObstacleId = gate.Id,
                    X = AirDistance(before, decay, contact), Y = 226 - 106 * contact, Radius = 6 },
                new RiskZone { Id = "untouched", Kind = "positive", ObstacleId = gate.Id, X = 400, Y = 180, Radius = 6 }
            } };
            var game = Start(track, flight, settings);
            var p = game.Players[0];
            p.SurfaceRunReserve = before;
            RaceSimulation.Press(game, 0);
            Run(game, .2, frame);
            Check(p.CollectedRiskIds.Contains("pending") && p.BoostsEarned == 0, "linked reward pending before hit");
            Run(game, .4, frame);
            Check(p.Collisions == 1, "one hit with either reset setting");
            Near(p.LastHit.At, at, "reset setting preserves impact time");
            Near(p.Distance, hitX, "recovery freezes position after impact");
            double retained = reset ? 0 : before - decay * at;
            Near(p.SurfaceRunReserve, retained, "recovery holds the impact reserve or zero");
            Near(p.Recovering, .3, "usual impact penalty");
            Check(!RaceSimulation.CanPress(game, 0) && !RaceSimulation.Press(game, 0), "input rejected during recovery");
            Check(p.FailedRiskIds.Contains("pending") && !p.FailedRiskIds.Contains("untouched") && p.BoostsEarned == 0,
                "hit cancels only the linked pending reward");
            Run(game, .4, frame);
            Check(p.CompletedRecoveries == 1 && p.Collisions == 1, "ordinary recovery completes without another hit");
            Near(p.SurfaceRunReserve, Math.Max(0, retained - decay * .1), "decay resumes when airborne after recovery");
            Near(p.Distance - hitX, AirDistance(retained, decay, .1), "motion resumes after the usual stop");
        }
    }
    static void FreezeResetAndSnapshot()
    {
        var settings = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, 50, false);
        var game = Ready(settings: settings); RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 2);
        Near(game.Players[0].SurfaceRunReserve, 0, "countdown freeze"); RaceSimulation.Advance(game, 2);
        Near(game.Players[0].SurfaceRunReserve, .05, "countdown leftover");
        game.Phase = "paused"; RaceSimulation.Advance(game, 100); Near(game.Players[0].SurfaceRunReserve, .05, "pause freeze"); game.Phase = "racing";
        var clone = RaceSnapshot.Clone(game); Run(clone, 2, 2);
        Check(clone.SurfaceRunEnabled && clone.Players[0].SurfaceRunEnabled, "snapshot flags");
        Check(clone.SurfaceRunSettings.AirDecayPercent == 50 && !clone.SurfaceRunSettings.ResetOnCollision
            && clone.Players[0].SurfaceRunSettings.AirDecayPercent == 50 && !clone.Players[0].SurfaceRunSettings.ResetOnCollision,
            "snapshot retains both options on game and player");
        Near(game.Players[0].SurfaceRunReserve, .05, "independent snapshot reserve"); Near(clone.Players[0].SurfaceRunReserve, .15, "snapshot progression");
        RaceSimulation.Press(clone, 0); Run(clone, .2, .2);
        Near(clone.Players[0].SurfaceRunReserve, .145, "snapshot uses the stored decay");
        game.Players[0].Recovering = .5; Run(game, .25, .25); Near(game.Players[0].SurfaceRunReserve, .05, "recovery freeze");
        Run(game, .5, .5); Near(game.Players[0].SurfaceRunReserve, .0625, "post recovery growth");
        game.Phase = "finished"; RaceSimulation.Advance(game, 50); Near(game.Players[0].SurfaceRunReserve, .0625, "finished freeze");
        RaceSimulation.Press(game, 0); Check(game.Players[0].SurfaceRunEnabled, "retry retains flag"); Near(game.Players[0].SurfaceRunReserve, 0, "retry clears reserve");
        Check(game.Players[0].SurfaceRunSettings.AirDecayPercent == 50 && !game.Players[0].SurfaceRunSettings.ResetOnCollision,
            "retry retains both options");
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
            var gate = new Obstacle { Id = "gate", Kind = "gate", X = 150, Width = 12, GapTop = 48, GapBottom = 192, TopOffsetX = 30, BottomOffsetX = -25 };
            var game = Start(new Track(100000, new[] { gate }), settings: Cap40); var p = game.Players[0]; p.SurfaceRunReserve = .35;
            Run(game, .6, frame); Near(p.LastHit.At, Time(111, 297, 11), "accelerated offset gate time");
            Check(p.Collisions == 1, "gate deduplicated"); Near(p.SurfaceRunReserve, 0, "impact clears reserve");
            Check(p.Recovering > 0, "existing recovery"); Near(p.Distance, 111, "stopped impact x"); Run(game, 1, frame); Check(p.Collisions == 1, "gate remains one hit");
            var track = Empty(); track.RiskZones = new[] { new RiskZone { Id = "ring", Kind = "positive", FreeReward = true, X = 150, Y = 226, Radius = 6 } };
            game = Start(track, settings: Cap40); p = game.Players[0]; p.SurfaceRunReserve = .35; Run(game, 1, frame);
            Check(p.BoostsEarned == 1, "accelerated swept ring"); Near(p.LastSuccess.At, Time(130, 297, 11), "quartic ring time");
            Near(p.SurfaceRunReserve, .40, "ring does not reset reserve"); Near(p.Distance, 302.5 + 110 * (1 - Time(130, 297, 11)), "ring additive distance");
            game = Start(Empty(), FlightPresets.Chosen, Cap40); p = game.Players[0]; p.SurfaceRunReserve = .4;
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
            var stone = new Obstacle { Id = "stone", Kind = "floor", X = 120, Width = 130, Height = 68 };
            var track = new Track(100000, new[] { stone }) { RiskZones = new[] { new RiskZone { Id = "landing", Kind = "landing", ObstacleId = "stone", X = 185, LandingWidth = 130 } } };
            var game = Start(track, new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.One, linearSpeed: 100), Cap40);
            var p = game.Players[0]; p.SurfaceRunReserve = .4; p.Y = (100 - 14d) / 212; p.Support = SupportKind.None; p.Flipping = true; p.VerticalSpeed = 100; p.Gravity = 1;
            Run(game, .7, frame); Check(p.Support == SupportKind.Stone && !p.Flipping, "marked stone support");
            Near(p.LastInstant.At, .58, "landing exact time"); Near(p.SurfaceRunReserve, .348, "landing starts growth immediately"); Check(p.Collisions == 0, "legal accelerated landing");
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
            var track = Empty(2000); track.RiskZones = new[] { new RiskZone { Id = "finish", Kind = "finish", X = 150, Y = 120 } };
            game = Start(track, new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.Unlimited, linearSpeed: 106), Cap40); var p = game.Players[0]; p.SurfaceRunReserve = .4;
            p.Y = .5; p.Support = SupportKind.None; p.Flipping = true; p.VerticalSpeed = 0; p.Gravity = 1;
            Run(game, .8, frame); Check(p.FinishBoostsEarned == 1, "accelerated chevron contact");
            Near(p.FinishBoostElapsed, .25, "finish full service");
            if (double.IsNaN(baseline)) baseline = p.LastFinishBoost.At; else Near(p.LastFinishBoost.At, baseline, "chevron partition time");
            game = Start(track, settings: Cap40); game.Players[0].SurfaceRunReserve = .4; Run(game, .8, frame); Check(game.Players[0].FinishBoostsEarned == 0, "nearby floor misses strokes");
        }
    }
    static void HiddenRevealAndPostponement()
    {
        foreach (double frame in Frames)
        {
            var stone = new Obstacle { Id = "hidden", Kind = "floor", Hidden = true, X = 260, Width = 350, Height = 68, HiddenRevealDistance = 180 };
            var game = Start(new Track(100000, new[] { stone }), settings: Cap40); var p = game.Players[0]; p.SurfaceRunReserve = .35;
            Run(game, 1.2, frame); var state = game.HiddenObstacles["hidden"];
            Near(state.RevealedAt.Value, Time(80, 297, 11), "accelerated hidden trigger exact");
            Check(state.Phase == HiddenObstaclePhase.Warning && state.ActivationPostponed, "occupied activation postponed"); Check(p.Collisions == 0, "warning harmless");
            Run(game, 1.5, frame); Check(state.Phase == HiddenObstaclePhase.Solid, "body clearance solid"); Check(p.Collisions == 0, "clearance no surprise hit");
        }
    }
    static void ConstantCameraBoundary()
    {
        foreach (double frame in Frames)
        {
            var game = RaceSimulation.CreateSolo(Empty(10800), FlightPresets.Linear, PartyCamera.Constant, 0, 1520);
            RaceSimulation.ConfigureSurfaceRun(game, true, Cap40); RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 3);
            var p = game.Players[0]; p.SurfaceRunReserve = .2; Run(game, 2, frame);
            double expected = (36 - Math.Sqrt(36 * 36 - 4 * 5.5 * 14)) / 11;
            Check(p.Status == PlayerStatus.Eliminated, "quadratic left boundary elimination"); Near(p.TerminalTime.Value, expected, "left boundary exact time");
            game = RaceSimulation.CreateSolo(Empty(10800), FlightPresets.Linear, PartyCamera.Constant, -100, 1520);
            RaceSimulation.ConfigureSurfaceRun(game, true, Cap40); RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 3);
            p = game.Players[0]; p.SurfaceRunReserve = .2; Run(game, 8, frame);
            Check(p.Status == PlayerStatus.Running, "acceleration closes speed deficit before left bound");
        }
    }
    static void ExponentialSweeps()
    {
        var settings = new SurfaceRunSettings(4, SurfaceRunCurve.Exponential, 30);
        const double at = .55;
        double crossingX = GroundDistance(settings, 0, at);
        foreach (double frame in Frames)
        {
            var gate = new Obstacle { Id = "exp-gate", Kind = "gate", X = crossingX + 39,
                Width = 12, GapTop = 48, GapBottom = 192, TopOffsetX = 30, BottomOffsetX = -25 };
            var game = Start(new Track(10000, new[] { gate }), settings: settings);
            Run(game, .8, frame); var p = game.Players[0];
            Check(p.Collisions == 1, "exponential gate swept hit");
            Near(p.LastHit.At, at, "exponential offset gate contact");
            Near(p.Distance, crossingX, "exponential collision X");
            Near(p.SurfaceRunReserve, 0, "exponential hit clears reserve");

            var track = Empty();
            track.RiskZones = new[] {
                new RiskZone { Id = "tangent", Kind = "coin", X = crossingX, Y = 206, Radius = 6 },
                new RiskZone { Id = "near-miss", Kind = "coin", X = crossingX, Y = 205.999, Radius = 6 }
            };
            game = Start(track, settings: settings); Run(game, .8, frame); p = game.Players[0];
            Check(p.CoinsEarned == 1 && p.LastCoin.Id == "tangent", "exponential ring tangent and nearby miss");
            Near(p.LastCoin.At, at, "exponential ring tangent time");

            // From this horizontal line the first chevron contact is at the
            // leading endpoints' expanded circles, 8.5 units off center.
            track = Empty(GroundDistance(settings, 0, 2) + 55);
            track.RiskZones = new[] { new RiskZone { Id = "finish", Kind = "finish", Y = 226,
                X = crossingX + 10.5 + Math.Sqrt(15.5 * 15.5 - 8.5 * 8.5) } };
            game = Start(track, settings: settings); Run(game, 3, frame); p = game.Players[0];
            Check(p.FinishBoostsEarned == 1, "exponential chevron contact");
            Near(p.LastFinishBoost.At, at, "exponential chevron time");
            Near(p.FinishBoostDistanceGained, 55, "exponential finish bonus stays additive");
            Near(p.FinishTime.Value, 2, "exponential finish crossing time");

            var stone = new Obstacle { Id = "exp-hidden", Kind = "floor", Hidden = true, X = crossingX + 120,
                Width = 250, Height = 68, HiddenRevealDistance = 120 };
            game = Start(new Track(10000, new[] { stone }), settings: settings);
            Run(game, 1.6, frame);
            var hidden = game.HiddenObstacles[stone.Id];
            Near(hidden.RevealedAt.Value, at, "exponential exact hidden reveal");
            Check(hidden.ActivationPostponed && hidden.Phase == HiddenObstaclePhase.Warning, "exponential overlap delays solidity");
            Run(game, 1.4, frame);
            Check(hidden.Phase == HiddenObstaclePhase.Solid && game.Players[0].Collisions == 0, "exponential hidden stone activates after clearance");
        }

        var live = Start(settings: settings); Run(live, 2, 2);
        var clone = RaceSnapshot.Clone(live); Run(clone, 2, 2);
        Check(clone.SurfaceRunSettings.Curve == SurfaceRunCurve.Exponential && clone.Players[0].SurfaceRunSettings.RatePercent == 4
            && clone.Players[0].SurfaceRunSettings.CapPercent == 30, "snapshot retains nondefault settings");
        Near(live.Players[0].SurfaceRunReserve, GroundReserve(settings, 0, 2), "exponential snapshot source independent");
        Near(clone.Players[0].SurfaceRunReserve, GroundReserve(settings, 0, 4), "exponential snapshot continues selected curve");
        clone.Phase = "finished"; RaceSimulation.Press(clone, 0); RaceSimulation.Advance(clone, 5);
        Near(clone.Players[0].SurfaceRunReserve, GroundReserve(settings, 0, 2), "retry resets reserve and retains exponential settings");
    }

    static GameState ExponentialCameraRun(SurfaceRunSettings settings, double scroll, double gap)
    {
        var game = RaceSimulation.CreateSolo(Empty(scroll * 36), FlightPresets.Linear, PartyCamera.Constant, 14 - gap, 1520);
        RaceSimulation.ConfigureSurfaceRun(game, true, settings);
        RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 3);
        return game;
    }
    static void ExponentialCamera()
    {
        var settings = new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 40);
        foreach (double frame in Frames)
        {
            double scroll = 260, crossing = 1.2;
            var game = ExponentialCameraRun(settings, scroll, scroll * crossing - GroundDistance(settings, 0, crossing));
            Run(game, 8, frame);
            Check(game.Players[0].Status == PlayerStatus.Eliminated, "exponential camera first crossing");
            Near(game.Players[0].TerminalTime.Value, crossing, "exponential camera first root time");

            double turning = -Math.Log((308d - scroll) / (308 - 220)) / (settings.Rate / settings.Cap);
            double tangentGap = scroll * turning - GroundDistance(settings, 0, turning);
            game = ExponentialCameraRun(settings, scroll, tangentGap);
            Run(game, 8, frame);
            Check(game.Players[0].Status == PlayerStatus.Eliminated, "exponential camera tangent is contact");
            Near(game.Players[0].TerminalTime.Value, turning, "exponential camera tangent time");
            game = ExponentialCameraRun(settings, scroll, tangentGap + 1);
            Run(game, 8, frame);
            Check(game.Players[0].Status == PlayerStatus.Running, "exponential camera turnaround prevents elimination");

            scroll = 308;
            game = ExponentialCameraRun(settings, scroll, scroll * crossing - GroundDistance(settings, 0, crossing));
            Run(game, 8, frame);
            Check(game.Players[0].Status == PlayerStatus.Eliminated, "camera speed equals asymptote and still crosses");
            Near(game.Players[0].TerminalTime.Value, crossing, "asymptotic camera first root time");
        }
    }
    static void NonsharedFinishAndGrace()
    {
        foreach (double frame in new[] { .317, 200d })
        {
            var track = Empty(10000);
            // A future uninterrupted reward queue makes the winner finish more
            // than one grace period before the optimistic unboosted arrival.
            var rings = new RiskZone[300];
            for (int i = 0; i < rings.Length; i++) rings[i] = new RiskZone
            { Id = "future-" + i, Kind = "positive", FreeReward = true, X = 200, Y = 226, Radius = 6 };
            track.RiskZones = rings;
            var game = RaceSimulation.CreateTrial(track, FlightPresets.Linear);
            RaceSimulation.ConfigureSurfaceRun(game, true, Cap40);
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            var winner = game.Players[0]; var lagger = game.Players[1];
            winner.SurfaceRunReserve = lagger.SurfaceRunReserve = .4;
            lagger.Y = 0; lagger.SupportSide = 0;
            double contact = 180 / 308d;
            double expectedFinish = contact + (track.Length - 180) / 418;
            Run(game, 200, frame);
            Near(winner.FinishTime.Value, expectedFinish, "nonshared accelerated actual finish timestamp");
            Near(game.Elapsed, expectedFinish + RaceConfig.FinishGrace, "nonshared grace deadline exact");
            Check(game.Phase == "finished" && !lagger.FinishTime.HasValue, "large advance stops lagger at grace");
            double expectedDistance = 308 * game.Elapsed;
            Near(lagger.Distance, expectedDistance, "lagger receives only time before grace", 2e-6);
            Check(winner.BoostsEarned == rings.Length, "future reward queue collected");
        }
    }
    static void GuardsAndOff()
    {
        var explicitDefaults = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, 200, true);
        Check(SurfaceRunSettings.Default.Equals(explicitDefaults)
            && new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30).Equals(explicitDefaults),
            "omitting new options preserves 5.5.1 defaults");
        var custom = new SurfaceRunSettings(3, SurfaceRunCurve.Exponential, 40, 0, false);
        var off = RaceSimulation.CreateSolo(Empty(), FlightPresets.Linear, PartyCamera.Lagger, 0, 1000);
        Check(!off.SurfaceRunEnabled && !off.Players[0].SurfaceRunEnabled, "factory default off");
        RaceSimulation.ConfigureSurfaceRun(off, false, custom);
        RaceSimulation.Press(off, 0); RaceSimulation.Advance(off, 7);
        Check(off.SurfaceRunSettings.Equals(custom) && off.Players[0].SurfaceRunSettings.Equals(custom), "off retains custom settings");
        Near(off.Players[0].Distance, 880, "off original 220"); Near(off.Players[0].SurfaceRunReserve, 0, "off no accumulation");
        RaceSimulation.Press(off, 0); RaceSimulation.Advance(off, .4);
        Near(off.Players[0].Distance, 968, "off preserves ordinary airborne speed");
        Near(off.Players[0].SurfaceRunReserve, 0, "off has no airborne reserve");
        bool rejected = false; try { RaceSimulation.ConfigureSurfaceRun(off, true); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "attempt immutable");
        var bot = RaceSimulation.CreateStochasticBotRace(RaceTracks.Get(TracksRhythm.CascadeId), FlightPresets.Chosen, BotDifficulty.Master, 1u);
        RaceSimulation.ConfigureSurfaceRun(bot, true, custom);
        Check(bot.SurfaceRunEnabled && bot.SurfaceRunSettings.Equals(custom)
            && Array.TrueForAll(bot.Players, p => p.SurfaceRunEnabled && p.SurfaceRunSettings.Equals(custom)), "bot and human receive all selected settings");
        RaceRunner.Press(bot, 0); RaceRunner.Advance(bot, 4);
        Near(bot.Players[0].SurfaceRunReserve, GroundReserve(custom, 0, 1), "human builds configured reserve");
        Near(bot.Players[1].SurfaceRunReserve, bot.Players[0].SurfaceRunReserve, "bot builds the same reserve from the same input");
        Near(bot.Players[1].Distance, bot.Players[0].Distance, "bot and human move identically from the same input");
        var game = RaceSimulation.CreateSolo(Empty(), FlightPresets.Linear, PartyCamera.Lagger, 0, 1000);
        Action[] invalid = {
            () => RaceSimulation.ConfigureSurfaceRun(game, true, default),
            () => new SurfaceRunSettings(0, SurfaceRunCurve.Linear, 30),
            () => new SurfaceRunSettings(6, SurfaceRunCurve.Linear, 30),
            () => new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 25),
            () => new SurfaceRunSettings(5, (SurfaceRunCurve)8, 30),
            () => new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, -1),
            () => new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, 25),
            () => new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, 300),
            () => new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, 401)
        };
        foreach (Action action in invalid)
        {
            rejected = false; try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && !game.SurfaceRunEnabled, "invalid configuration rejected before mutating race");
        }
    }
}
