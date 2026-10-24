using System;
using FlipRace.Core;

namespace FlipRace.Editor
{
    public static class EngineFlightTests
    {
        static int assertions;
        static readonly double[] Frames = { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100 };
        public static string RunAll()
        {
            assertions = 0;
            Action[] tests = { SettingsAndPresets, IndependentRepeats, LinearJumpCycle, GravityJumpMeasurement,
                InvalidSettings, LinearJumpFirstRepeat, LinearSpeedsAndHeights, GravityExtremesAndWallContact,
                OppositeStoneAndLinearBounce, InterruptedMeasurement, PauseSnapshotAndReset, ExactPresetRoutes };
            foreach (var test in tests)
                try { test(); }
                catch (Exception error) { throw new Exception("FLIGHT FAIL " + test.Method.Name + ": " + error.Message, error); }
            return "PASS: FLIGHT " + tests.Length + " scenarios / " + assertions + " assertions.";
        }
        static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception(message); }
        static void Near(double actual, double expected, string message, double tolerance = 2e-7)
        { Check(Math.Abs(actual - expected) < tolerance, message + ": " + actual + " vs " + expected); }
        static GameState Start(FlightSettings flight, Track track = null)
        {
            var game = RaceSimulation.CreateTrial(track ?? new Track(22000, Array.Empty<Obstacle>()), flight);
            Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "ready edges");
            RaceSimulation.Advance(game, 3); return game;
        }
        static void AdvanceFor(GameState game, double duration, double frame)
        {
            while (duration > 1e-12 && game.Phase == "racing")
            { double step = Math.Min(duration, frame); RaceSimulation.Advance(game, step); duration -= step; }
        }
        static void Tap(GameState game)
        { Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "both taps accepted"); }
        static void SettingsAndPresets()
        {
            var modes = new[] { MotionMode.GravityAirFlip, MotionMode.LinearAirFlip, MotionMode.JumpGravityFlip, MotionMode.GravityUnlimited };
            var profiles = new[] { MotionProfiles.Moderate, MotionProfiles.Baseline, MotionProfiles.Jump, MotionProfiles.Moderate };
            var presets = new[] { FlightPresets.Moderate, FlightPresets.Linear, FlightPresets.Jump, FlightPresets.Unlimited };
            for (int i = 0; i < modes.Length; i++)
            {
                var old = FlightPresets.FromLegacy(modes[i], profiles[i]); var current = presets[i];
                Check(old.FirstAction == current.FirstAction && old.Law == current.Law && old.AirRepeats == current.AirRepeats
                    && old.GravityAcceleration == current.GravityAcceleration && old.FlipImpulse == current.FlipImpulse
                    && old.JumpImpulse == current.JumpImpulse && old.LinearSpeed == current.LinearSpeed
                    && old.LinearJumpHeight == current.LinearJumpHeight, "preset has exact physical and inactive values");
                var game = Start(current); Check(ReferenceEquals(game.Flight, current), "immutable settings shared across players and start");
            }
        }
        static void IndependentRepeats()
        {
            foreach (var law in new[] { MotionLaw.Linear, MotionLaw.Gravity })
            foreach (var action in new[] { FirstAction.Flip, FirstAction.Jump })
            foreach (var count in new[] { AirRepeatLimit.None, AirRepeatLimit.One, AirRepeatLimit.Unlimited })
            {
                var game = Start(new FlightSettings(action, law, count)); Tap(game); RaceSimulation.Advance(game, .1);
                var p = game.Players[0]; double y = p.Y, velocity = p.VerticalSpeed;
                bool accepted = RaceSimulation.Press(game, 0);
                Check(accepted == (count != AirRepeatLimit.None), "first air edge follows independent repeat count");
                Near(p.Y, y, "air edge never teleports");
                if (law == MotionLaw.Gravity || count == AirRepeatLimit.None) Near(p.VerticalSpeed, velocity, "gravity edge preserves momentum");
                else Near(p.VerticalSpeed, action == FirstAction.Jump ? velocity : -velocity, "first linear jump edge preserves ascent; flip reverses");
                Check(RaceSimulation.Press(game, 0) == (count == AirRepeatLimit.Unlimited), "next separate edge follows count without cooldown");
                if (count == AirRepeatLimit.Unlimited)
                    for (int i = 0; i < 64; i++) Check(RaceSimulation.Press(game, 0), "unlimited has no arbitrary cap");
            }
        }
        static void LinearJumpCycle()
        {
            foreach (double frame in Frames)
            {
                var game = Start(new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.None)); Tap(game);
                AdvanceFor(game, 2 * 112d / 265, frame); var p = game.Players[0];
                Check(!p.Flipping && p.Support == SupportKind.Wall && p.Y == 1 && !p.AirFlipAvailable,
                    "zero-repeat linear jump returns to starting wall");
                Check(p.LastFlight != null && !p.LastFlight.Interrupted, "complete clean linear flight measured");
                Near(p.LastFlight.Duration, 224d / 265, "linear jump actual duration");
                Near(p.LastFlight.MaxDistance, 112, "linear apex actual distance");
            }
        }
        static void GravityJumpMeasurement()
        {
            foreach (double frame in Frames)
            {
                var game = Start(new FlightSettings(FirstAction.Jump, MotionLaw.Gravity, AirRepeatLimit.None, 2700, 0, 200)); Tap(game);
                AdvanceFor(game, .317, frame); var p = game.Players[0];
                Check(!p.Flipping && p.Y == 1 && !p.LastFlight.Interrupted, "weak fast jump completes inside one .317 frame");
                Near(p.LastFlight.Duration, 400d / 2700, "short jump duration");
                Near(p.LastFlight.MaxDistance, 200d * 200 / (2 * 2700), "analytic vertex maximum between frame endpoints");
            }
        }
        static void Reject(Action create)
        {
            bool rejected = false;
            try { create(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid physical settings rejected before play");
        }
        static void InvalidSettings()
        {
            Reject(() => new FlightSettings((FirstAction)7, MotionLaw.Linear, AirRepeatLimit.None));
            Reject(() => new FlightSettings(FirstAction.Flip, (MotionLaw)7, AirRepeatLimit.One));
            Reject(() => new FlightSettings(FirstAction.Flip, MotionLaw.Gravity, (AirRepeatLimit)2));
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
            {
                Reject(() => new FlightSettings(FirstAction.Flip, MotionLaw.Gravity, AirRepeatLimit.One, gravityAcceleration: invalid));
                Reject(() => new FlightSettings(FirstAction.Flip, MotionLaw.Gravity, AirRepeatLimit.One, flipImpulse: invalid));
                Reject(() => new FlightSettings(FirstAction.Flip, MotionLaw.Gravity, AirRepeatLimit.One, jumpImpulse: invalid));
                Reject(() => new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.One, linearSpeed: invalid));
                Reject(() => new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.One, linearJumpHeight: invalid));
            }
            Reject(() => new FlightSettings(FirstAction.Jump, MotionLaw.Gravity, AirRepeatLimit.None, jumpImpulse: 0));
            Reject(() => new FlightSettings(FirstAction.Flip, MotionLaw.Gravity, AirRepeatLimit.None, gravityAcceleration: 0));
            Reject(() => new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.None, linearSpeed: 0));
            Reject(() => new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.None, linearJumpHeight: 0));
        }
        static void LinearJumpFirstRepeat()
        {
            foreach (double frame in Frames)
            foreach (double when in new[] { .1, .6 })
            foreach (var repeats in new[] { AirRepeatLimit.One, AirRepeatLimit.Unlimited })
            {
                var game = Start(new FlightSettings(FirstAction.Jump, MotionLaw.Linear, repeats)); Tap(game);
                AdvanceFor(game, when, frame); var p = game.Players[0]; double y = p.Y;
                double remaining = (RaceSimulation.GetPlayerCenterY(p) - 14) / 265;
                Tap(game); Check(!p.LinearJumpActive && p.Y == y && p.VerticalSpeed == -265 && p.Gravity == -1,
                    "first linear jump air edge always targets opposite origin, both ascent and descent");
                if (repeats == AirRepeatLimit.One) Check(!RaceSimulation.Press(game, 0), "one repeat exhausted before real landing");
                AdvanceFor(game, remaining, frame);
                Check(p.Support == SupportKind.Wall && p.Y == 0 && !p.Flipping && p.AirFlipAvailable,
                    "cancelled automatic return lands on opposite wall and recharges");
                Near(p.LastFlight.Duration, when + remaining, "repeat keeps one continuous flight clock");
                Near(p.LastFlight.MaxDistance, 212, "repeat does not reset origin or max");
            }
        }
        static void LinearSpeedsAndHeights()
        {
            foreach (double duration in new[] { 1.2, 1d, .9, .8, .7, .6, .5, .4 })
            foreach (double frame in new[] { 1d / 144, 100d })
            {
                double speed = 212 / duration;
                var game = Start(new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.None,
                    gravityAcceleration: 2700, flipImpulse: 420, jumpImpulse: 550, linearSpeed: speed)); Tap(game);
                AdvanceFor(game, duration, frame); var p = game.Players[0];
                Check(p.Y == 0 && !p.Flipping && !p.AirFlipAvailable, "linear speed and zero repeats independent of hidden impulses");
                Near(p.LastFlight.Duration, duration, "full free-corridor linear duration");
                Near(p.LastFlight.MaxDistance, 212, "full linear excursion");
            }
            foreach (double height in new[] { 48d, 64, 80, 96, 112, 128, 160, 192 })
            foreach (double frame in new[] { 1d / 144, 100d })
            {
                var game = Start(new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.One,
                    linearSpeed: 530, linearJumpHeight: height)); Tap(game);
                AdvanceFor(game, 2 * height / 530, frame); var p = game.Players[0];
                Near(p.LastFlight.Duration, 2 * height / 530, "linear jump height sets flight duration");
                Near(p.LastFlight.MaxDistance, height, "linear height has an exact unsampled apex");
                Check(p.Y == 1 && p.AirFlipAvailable && !p.LinearJumpActive, "automatic apex was not a landing or recharge");
            }
        }
        static void GravityExtremesAndWallContact()
        {
            foreach (double gravity in new[] { 662.5, 2700 })
            foreach (double impulse in new[] { 0d, 420 })
            foreach (double frame in Frames)
            {
                var game = Start(new FlightSettings(FirstAction.Flip, MotionLaw.Gravity, AirRepeatLimit.None, gravity, impulse)); Tap(game);
                double duration = 424 / (impulse + Math.Sqrt(impulse * impulse + 424 * gravity));
                AdvanceFor(game, duration, frame); var p = game.Players[0];
                Check(p.Y == 0 && !p.Flipping, "gravity flip reaches opposite wall with no required repeat");
                Near(p.LastFlight.Duration, duration, "analytic extreme gravity transfer");
                Near(p.LastFlight.MaxDistance, 212, "extreme transfer physical excursion");
            }
            foreach (double frame in Frames)
            {
                var game = Start(new FlightSettings(FirstAction.Jump, MotionLaw.Gravity, AirRepeatLimit.One, 662.5, 0, 550)); Tap(game);
                double contact = 424 / (550 + Math.Sqrt(550 * 550 - 424 * 662.5));
                AdvanceFor(game, contact, frame); var p = game.Players[0];
                Check(p.Flipping && p.Support == SupportKind.None && p.LastFlight == null,
                    "opposite wall under inward gravity is not the end of measured jump");
                Near(p.VerticalSpeed, 0, "wrong-wall contact clears incoming velocity without new impulse");
                AdvanceFor(game, .8, frame);
                Near(p.LastFlight.Duration, contact + .8, "wrong-wall continuation retains original flight clock");
                Near(p.LastFlight.MaxDistance, 212, "actual wall clips theoretical228.3 maximum");
                Check(p.Y == 1 && !p.LastFlight.Interrupted, "ordinary gravity jump eventually returns cleanly");
            }
        }
        static Track Pad(bool ceiling, double height = 48, double x = 300, double width = 500, double marker = 350, double markerWidth = 12)
        {
            return new Track(5000, new[] { new Obstacle { Id = "pad-stone", Kind = ceiling ? "ceiling" : "floor", X = x, Width = width, Height = height } })
                { RiskZones = new[] { new RiskZone { Id = "pad", Kind = "landing", ObstacleId = "pad-stone", X = marker, LandingWidth = markerWidth } } };
        }
        static void OppositeStoneAndLinearBounce()
        {
            foreach (double frame in Frames)
            {
                // An ordinary jump lands on a ceiling stone before its planned
                // apex. This becomes the true origin for the next edge fall.
                var game = Start(new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.One,
                    linearJumpHeight: 192), Pad(true, 92, width: 200));
                AdvanceFor(game, 350d / 220 - 120d / 265, frame); Tap(game); AdvanceFor(game, 120d / 265, frame);
                var p = game.Players[0]; Check(p.Support == SupportKind.Stone && p.SupportSide == 0 && p.Gravity == -1,
                    "early opposite stone is stable support with normalized linear direction");
                Near(p.LastFlight.MaxDistance, 120, "stone clips planned linear apex");
                // Reward330 runs to the edge in150/330, then linear fall reaches
                // the wall: no automatic jump impulse or hidden acceleration.
                AdvanceFor(game, 150d / 330 + .02, frame);
                Check(p.Support == SupportKind.None && p.VerticalSpeed == -265 && p.FlipTo == 0 && p.AirFlipAvailable,
                    "opposite-stone walkoff targets its actual wall");
                Tap(game); Check(p.VerticalSpeed == 265 && p.Gravity == 1 && p.FlipTo == 1,
                    "walkoff air reversal keeps direction and target coherent");

                // A tall linear jump from a raised floor reaches the ceiling
                // before its desired192 apex. Wide legal pad permits its return.
                var bounce = Start(new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.One,
                    linearJumpHeight: 192), Pad(false, marker: 550, markerWidth: 500));
                Tap(bounce); AdvanceFor(bounce, .1, frame); Tap(bounce); AdvanceFor(bounce, .7, frame);
                AdvanceFor(bounce, (350 - 164d / 265 * 220) / 220 - .8, frame); Tap(bounce);
                AdvanceFor(bounce, 164d / 265, frame); var q = bounce.Players[0];
                Check(q.Support == SupportKind.Stone && q.SupportSide == 1, "real raised origin reached");
                Tap(bounce); AdvanceFor(bounce, 164d / 265, frame);
                Check(q.LinearJumpActive && q.LinearJumpReturning && q.Flipping && q.Support == SupportKind.None
                    && q.VerticalSpeed == 265 && q.LastFlight.OriginObstacleId == null,
                    "pre-apex opposite wall reflects without creating a completed flight");
                Check(q.AirFlipAvailable, "unspent charge preserved but not regenerated by reflected wall");
                AdvanceFor(bounce, 164d / 265, frame);
                Check(q.Support == SupportKind.Stone && !q.Flipping && !q.LastFlight.Interrupted, "return lands on legal original stone");
                Near(q.LastFlight.Duration, 328d / 265, "clipped linear jump duration");
                Near(q.LastFlight.MaxDistance, 164, "clipped linear jump height from raised support");
                Check(q.LastFlight.OriginObstacleId == "pad-stone", "measurement records concrete origin support");
            }
        }
        static void InterruptedMeasurement()
        {
            foreach (double frame in Frames)
            {
                var track = new Track(2200, new[] { new Obstacle { Id = "impact", Kind = "floor", X = 36, Width = 64, Height = 48 } });
                var game = Start(FlightPresets.Moderate, track); Tap(game); AdvanceFor(game, .2, frame); var p = game.Players[0];
                Check(p.Collisions == 1 && p.LastFlight != null && p.LastFlight.Interrupted && !p.FlightActive,
                    "real impact immediately closes an interrupted measurement");
                Near(p.LastFlight.Duration, .1, "measurement stops at impact, not end of recovery");
                var sample = p.LastFlight; AdvanceFor(game, 2, frame);
                Check(ReferenceEquals(sample, p.LastFlight) && !p.Flipping, "later landing cannot relabel interrupted flight as clean");
                Tap(game); AdvanceFor(game, 1, frame);
                Check(!p.LastFlight.Interrupted && p.LastFlight.OriginSide == 0, "new deliberate flight replaces old interrupted sample");
            }
        }
        static FlightSettings Copy(FlightSettings p)
        { return new FlightSettings(p.FirstAction, p.Law, p.AirRepeats, p.GravityAcceleration, p.FlipImpulse, p.JumpImpulse, p.LinearSpeed, p.LinearJumpHeight); }
        static void PauseSnapshotAndReset()
        {
            var settings = new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.Unlimited, linearSpeed: 424, linearJumpHeight: 160);
            var game = Start(settings); Tap(game); RaceSimulation.Advance(game, .2); var p = game.Players[0];
            var clone = new GameState { Mode = game.Mode, Motion = game.Motion, Profile = game.Profile, Flight = game.Flight, Track = game.Track,
                Phase = game.Phase, Elapsed = game.Elapsed, Players = new[] { EngineTuneTests.CopyMotion(p), EngineTuneTests.CopyMotion(game.Players[1]) } };
            double y = p.Y, duration = p.FlightDuration; RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, double.NaN);
            Check(p.Y == y && p.FlightDuration == duration && p.LastFlight == null, "pause leaves live flight measurement unchanged");
            AdvanceFor(game, 1, 100); AdvanceFor(clone, 1, 1d / 144);
            Near(clone.Players[0].LastFlight.Duration, p.LastFlight.Duration, "snapshot keeps linear cycle and clock");
            Near(clone.Players[0].LastFlight.MaxDistance, 160, "snapshot keeps origin and max");
            Check(ReferenceEquals(clone.Flight, settings), "snapshot shares immutable settings");
            RaceSimulation.Advance(game, 100); Check(game.Phase == "finished", "actual finish before rematch");
            Tap(game); Check(ReferenceEquals(game.Flight, settings) && game.Players[0].LastFlight == null
                && !game.Players[0].FlightActive && !game.Players[0].LinearJumpActive, "rematch keeps settings and resets measurements/cycles");
        }
        static GameState Route(Track track, FlightSettings settings, double[] route, double frame, MotionMode? legacy = null)
        {
            GameState game;
            if (legacy.HasValue)
            {
                game = RaceSimulation.CreateTrial(track, legacy.Value, legacy.Value == MotionMode.LinearAirFlip ? MotionProfiles.Baseline
                    : legacy.Value == MotionMode.JumpGravityFlip ? MotionProfiles.Jump : MotionProfiles.Moderate);
                Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "old factory ready edges");
                RaceSimulation.Advance(game, 3);
            }
            else game = Start(settings, track);
            int next = 0, guard = 0;
            while (game.Phase == "racing")
            {
                if (++guard > 100000) throw new Exception("route stalled");
                if (next < route.Length && game.Players[0].Distance >= route[next] - 1e-8) { Tap(game); next++; }
                double dt = frame;
                if (next < route.Length) dt = Math.Min(dt, Math.Max(1e-10, (route[next] - game.Players[0].Distance) / 440));
                RaceSimulation.Advance(game, dt);
            }
            return game;
        }
        static void ExactPresetRoutes()
        {
            var modes = new[] { MotionMode.GravityAirFlip, MotionMode.LinearAirFlip, MotionMode.JumpGravityFlip, MotionMode.GravityUnlimited };
            var presets = new[] { FlightPresets.Moderate, FlightPresets.Linear, FlightPresets.Jump, FlightPresets.Unlimited };
            for (int i = 0; i < modes.Length; i++)
            foreach (double frame in new[] { 1d / 144, 100d })
            {
                var track = TracksControl.Challenge(modes[i]); var p = presets[i];
                foreach (bool safe in new[] { true, false })
                {
                    var game = Route(track, p, safe ? track.SafeRoute : track.RiskRoute, frame);
                    var copy = Route(track, Copy(p), safe ? track.SafeRoute : track.RiskRoute, frame);
                    var old = Route(track, p, safe ? track.SafeRoute : track.RiskRoute, frame, modes[i]);
                    // The retired4.2.1 trial keeps its authored geometry, but
                    // ITER49 globally replaces the finish trigger/timer. Its
                    // late pad may serve a partial pulse. Verify real motion
                    // conservation against the same input with no finish pad,
                    // rather than retain the obsolete automatic0.1s discount.
                    var ordinary = new System.Collections.Generic.List<RiskZone>();
                    foreach (var zone in track.RiskZones) if (!zone.IsFinish) ordinary.Add(zone);
                    var withoutPad = new Track(track.Length, track.Obstacles, track.Cues) { RiskZones = ordinary.ToArray() };
                    var counterfactual = Route(withoutPad, p, safe ? track.SafeRoute : track.RiskRoute, frame);
                    Near(counterfactual.Elapsed, safe ? 21 : 20.4, "unchanged training geometry/ordinary rewards without finish");
                    var runner = game.Players[0];
                    Near(game.Elapsed, counterfactual.Elapsed - runner.FinishBoostElapsed, "arrival equals actual served x2 moving duration");
                    Near(runner.FinishBoostDistanceGained, RaceConfig.Speed * runner.FinishBoostElapsed, "real extra travel agrees with served pulse");
                    Check(safe ? runner.FinishBoostElapsed == 0 : runner.FinishBoostElapsed > 0 && runner.FinishBoostElapsed < .25,
                        "legacy late trial honestly credits only its served partial pulse");
                    Check(game.Players[0].Collisions == 0 && game.Players[0].BoostsEarned == (safe ? 0 : 2)
                        && game.Players[0].FinishBoostsEarned == (safe ? 0 : 1), "preset retains support and finish semantics");
                    Check(game.Players[0].FinishTime == copy.Players[0].FinishTime && Equals(copy.Winner, "tie"),
                        "customized exact copy of preset has identical physics");
                    Near(game.Players[0].Y, copy.Players[0].Y, "preset copy actual finish trajectory");
                    Check(old.Players[0].FinishTime == game.Players[0].FinishTime && old.Players[0].Collisions == 0,
                        "old creation and new settings factory give identical finish");
                    Near(old.Players[0].Y, game.Players[0].Y, "old factory and new settings preserve actual trajectory");
                }
            }
        }
    }
}
