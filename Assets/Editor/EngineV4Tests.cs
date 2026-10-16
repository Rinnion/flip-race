using System;
using FlipRace.Core;

namespace FlipRace.Editor
{
    public static class EngineV4Tests
    {
        static int assertions;
        public static string RunAll()
        {
            assertions = 0;
            Action[] tests = { GravityAccelerationAndSurfaces, ContinuousStoneContacts, ParabolicRingTangency,
                FreeAndLinkedRewards, GravityFrameParity, PartialGravityFinish, GravityPauseAndRematch,
                AirReversalAndLanding, OppositeSurfaceDoesNotRecharge, LateCorrectionAndRecovery,
                ReenteredRingOnce, AirFrameParityAndPlayers, AirPauseAndRematch, LinkedGravityReturn };
            foreach (var test in tests)
                try { test(); }
                catch (Exception error) { throw new Exception("V4 FAIL " + test.Method.Name + ": " + error.Message, error); }
            return "PASS: V4-01/02 " + tests.Length + " scenarios / " + assertions
                + " assertions; gravity, continuous contacts, one inertial air flip, free rings and legacy separation.";
        }
        static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        static void Near(double actual, double expected, string message, double tolerance = 1e-8)
        { Check(Math.Abs(actual - expected) < tolerance, message + ": " + actual + " vs " + expected); }
        static Track Open(double length = 2200) { return new Track(length, Array.Empty<Obstacle>()); }
        static GameState Start(Track track, MotionMode motion = MotionMode.Gravity)
        {
            var game = RaceSimulation.CreateTrial(track, motion);
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            return game;
        }
        static void Tap(GameState game)
        { Check(RaceSimulation.Press(game, 0), "first tap accepted"); Check(RaceSimulation.Press(game, 1), "second tap accepted"); }
        static void Finish(GameState game, double step)
        {
            int guard = 0;
            while (game.Phase == "racing")
            { RaceSimulation.Advance(game, step); if (++guard > 100000) throw new Exception("finish stalled"); }
        }
        static Obstacle Stone(string kind, double x, double width = 64, double height = 68)
        { return new Obstacle { Id = "stone", Kind = kind, X = x, Width = width, Height = height }; }
        static RiskZone Free(string id, double x, double y, bool negative = false)
        { return new RiskZone { Id = id, X = x, Y = y, Radius = 6, FreeReward = true, Kind = negative ? "negative" : "positive" }; }

        static void GravityAccelerationAndSurfaces()
        {
            Check(RaceSimulation.CreateGame().Motion == MotionMode.Linear, "default V3 control remains linear");
            var linear = Start(Open(), MotionMode.Linear); Tap(linear); RaceSimulation.Advance(linear, .4);
            Near(RaceSimulation.GetPlayerCenterY(linear.Players[0]), 120, "linear reference midpoint unchanged");
            var game = Start(Open()); Tap(game); RaceSimulation.Advance(game, .2);
            var p = game.Players[0];
            Near(RaceSimulation.GetPlayerCenterY(p), 212.75, "quadratic position at .2");
            Near(p.VerticalSpeed, -132.5, "linear velocity increase at .2");
            Check(p.Gravity == -1 && p.Flipping && !p.AirFlipAvailable, "gravity-only flight state");
            Check(!RaceSimulation.Press(game, 0), "gravity-only rejects air input");
            RaceSimulation.Advance(game, .2);
            Near(RaceSimulation.GetPlayerCenterY(p), 173, "quadratic midpoint differs from linear");
            Near(p.VerticalSpeed, -265, "velocity at .4");
            RaceSimulation.Advance(game, .4);
            Check(!p.Flipping && p.Y == 0 && p.VerticalSpeed == 0 && p.Gravity == -1, "ceiling landing is an exact boundary event");
            Tap(game); RaceSimulation.Advance(game, 2);
            Check(!p.Flipping && p.Y == 1 && p.VerticalSpeed == 0 && p.Gravity == 1, "large step lands and remains on floor");
            Near(p.Distance, 2.8 * 220, "surface contacts keep forward movement");
        }
        static void ContinuousStoneContacts()
        {
            var game = Start(new Track(2200, new[] { Stone("ceiling", 100) })); Tap(game);
            double hitTime = Math.Sqrt(2 * (226d - 82) / RaceConfig.GravityAcceleration);
            RaceSimulation.Advance(game, hitTime + .1); var p = game.Players[0];
            Check(p.Collisions == 1 && p.Recovering > 0, "vertical parabolic edge catches ceiling stone");
            Near(p.LastHit.At, hitTime, "exact vertical contact timestamp");
            Near(RaceSimulation.GetPlayerCenterY(p), 82, "position freezes exactly at expanded stone edge");
            Near(p.VerticalSpeed, -RaceConfig.GravityAcceleration * hitTime, "collision freezes nonzero velocity");
            double speed = p.VerticalSpeed, y = p.Y, x = p.Distance; int gravity = p.Gravity;
            RaceSimulation.Advance(game, .2);
            Check(p.VerticalSpeed == speed && p.Y == y && p.Distance == x && p.Gravity == gravity, "recovery freezes entire trajectory");
            Finish(game, 100); Near(game.Elapsed, 10.5, "gravity collision still costs .5 physically");

            var horizontal = Start(new Track(2200, new[] { Stone("floor", 100) })); Tap(horizontal);
            RaceSimulation.Advance(horizontal, 1);
            Near(horizontal.Players[0].LastHit.At, 86d / 220, "exact horizontal leading-edge contact");
            var safe = Start(new Track(2200, new[] { Stone("floor", 150) })); Tap(safe); Finish(safe, 100);
            Check(safe.Players[0].Collisions == 0, "early gravity departure clears floor stone");
            var thin = Start(new Track(2200, new[] { Stone("ceiling", 300, 1) })); Tap(thin); Finish(thin, 100);
            Check(thin.Players[0].Collisions == 1, "large frame cannot skip thin obstacle after landing");
            Near(thin.Players[0].LastHit.At, 286d / 220, "thin horizontal collision time");
        }
        static Track TangentTrack(double offset = 0)
        {
            // At t=.4, p=(88,173), velocity=(220,-265). Put a circle center
            // exactly one combined radius along the normal to that trajectory.
            double norm = Math.Sqrt(265d * 265 + 220d * 220), radius = 20 + offset;
            return new Track(2200, Array.Empty<Obstacle>())
            { RiskZones = new[] { Free("tangent", 88 + radius * 265 / norm, 173 + radius * 220 / norm) } };
        }
        static void ParabolicRingTangency()
        {
            foreach (double frame in new[] { .017, .317, 2.5, 100d })
            {
                var game = Start(TangentTrack()); Tap(game); Finish(game, frame);
                Check(game.Players[0].BoostsEarned == 1 && game.Players[0].CompletedBoosts == 1, "quartic double-root ring contact is detected");
                Near(game.Players[0].LastSuccess.At, .4, "tangent has correct continuous timestamp", 1e-7);
                Near(game.Elapsed, 9.7, "tangent ring changes actual finish");
                var miss = Start(TangentTrack(.001)); Tap(miss); Finish(miss, frame);
                Check(miss.Players[0].BoostsEarned == 0, "nearby noncontact does not become tangent hit");
            }
            var horizon = Start(TangentTrack()); Tap(horizon); RaceSimulation.Advance(horizon, .4);
            Check(horizon.Players[0].BoostsEarned == 1, "contact at exact step endpoint is included");
        }
        static void FreeAndLinkedRewards()
        {
            var free = Free("free", 88, 173); free.ObstacleId = "stone";
            var game = Start(new Track(2200, new[] { Stone("ceiling", 500) }) { RiskZones = new[] { free } });
            Tap(game); RaceSimulation.Advance(game, .4);
            Check(game.Players[0].BoostsEarned == 1 && game.Players[0].BoostRemaining > 0, "free reward confirms at contact while airborne");
            Finish(game, 100);
            Check(game.Players[0].Collisions == 1 && game.Players[0].SuccessfulRiskIds.Contains("free")
                && !game.Players[0].FailedRiskIds.Contains("free"), "later unrelated hit cannot revoke free reward");
            Near(game.Elapsed, 10.2, "free reward plus collision actual .2 loss");
            var linked = Free("linked", 88, 173); linked.FreeReward = false; linked.ObstacleId = "stone"; linked.ClearX = 579;
            var pending = Start(new Track(2200, new[] { Stone("ceiling", 500) }) { RiskZones = new[] { linked } });
            Tap(pending); RaceSimulation.Advance(pending, .4);
            Check(pending.Players[0].CollectedRiskIds.Contains("linked") && pending.Players[0].BoostsEarned == 0, "linked reward remains pending");
            Finish(pending, 100);
            Check(pending.Players[0].BoostsEarned == 0 && pending.Players[0].FailedRiskIds.Contains("linked"), "linked later impact cancels pending reward");
        }
        static void GravityFrameParity()
        {
            var track = new Track(2200, new[] { Stone("ceiling", 500) })
            { RiskZones = new[] { Free("flight", 88, 173), Free("ceiling-slow", 300, 14, true) } };
            foreach (double step in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100d })
            {
                var game = Start(track); Tap(game); Finish(game, step);
                foreach (var p in game.Players)
                {
                    Check(p.CompletedBoosts == 1 && p.CompletedSlows == 1 && p.Collisions == 1,
                        "quadratic route and effects independent of frame size");
                    Check(p.ActiveEffect == null && p.PendingEffects.Count == 0, "gravity route serves full FIFO");
                    Near(p.FinishTime.Value, 10.5, "signed effects cancel, collision remains");
                }
                Near(game.Elapsed, 10.5, "actual finish with changing horizontal speed");
                Check(game.Players[0].FinishTime == game.Players[1].FinishTime && Equals(game.Winner, "tie"), "gravity exact symmetric tie");
            }
        }
        static void PartialGravityFinish()
        {
            var game = Start(Open(100)); Tap(game); RaceSimulation.Advance(game, 100);
            double t = 100d / 220;
            Near(game.Elapsed, t, "finish can interrupt gravity flight");
            Near(RaceSimulation.GetPlayerCenterY(game.Players[0]), 226 - .5 * RaceConfig.GravityAcceleration * t * t, "finish freezes actual airborne position");
            Check(game.Players[0].Flipping, "no artificial landing at finish");
        }
        static void GravityPauseAndRematch()
        {
            var game = Start(TangentTrack()); Tap(game); RaceSimulation.Advance(game, .45);
            var p = game.Players[0]; double y = p.Y, vy = p.VerticalSpeed, remaining = p.BoostRemaining;
            RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, double.NaN);
            Check(p.Y == y && p.VerticalSpeed == vy && p.BoostRemaining == remaining && p.Gravity == -1, "pause freezes gravity and effect");
            Finish(game, 100); RaceSimulation.SetCharacter(game, 0, 3); Track track = game.Track; Tap(game);
            p = game.Players[0];
            Check(game.Motion == MotionMode.Gravity && ReferenceEquals(game.Track, track) && p.CharacterIndex == 3,
                "rematch retains experiment and hero");
            Check(p.Y == 1 && p.Gravity == 1 && p.VerticalSpeed == 0 && !p.Flipping && !p.AirFlipAvailable
                && p.ActiveEffect == null && p.PendingEffects.Count == 0 && p.SuccessfulRiskIds.Count == 0, "rematch resets complete gravity state");
        }
        static GameState ReverseAt(double time, Track track = null)
        {
            var game = Start(track ?? Open(), MotionMode.GravityAirFlip); Tap(game); RaceSimulation.Advance(game, time);
            double first = game.Players[0].VerticalSpeed, second = game.Players[1].VerticalSpeed;
            Tap(game);
            Check(game.Players[0].VerticalSpeed == first && game.Players[1].VerticalSpeed == second, "air reverse preserves incoming momentum exactly");
            return game;
        }
        static void AirReversalAndLanding()
        {
            foreach (double reverse in new[] { .2, .4 })
            {
                var game = ReverseAt(reverse); var p = game.Players[0];
                Check(!p.AirFlipAvailable && p.Gravity == 1 && p.Flipping, "one air charge spent and acceleration reversed");
                Check(!RaceSimulation.Press(game, 0), "third input rejected in same flight");
                double priorY = p.Y; RaceSimulation.Advance(game, reverse * .5);
                Check(p.Y < priorY && p.VerticalSpeed < 0, "inertia continues toward old surface after reversal");
                RaceSimulation.Advance(game, reverse * .5);
                Near(p.VerticalSpeed, 0, "vertical turn event at twice departure time");
                Near(RaceSimulation.GetPlayerCenterY(p), 226 - RaceConfig.GravityAcceleration * reverse * reverse, "selectable apex height");
                double finishTime = reverse * (2 + Math.Sqrt(2));
                if (reverse == .4) Check(p.Flipping && p.FlipElapsed >= .8 - 1e-9, "old .8 timer never forces reversal landing");
                RaceSimulation.Advance(game, finishTime - 2 * reverse);
                Check(!p.Flipping && p.Y == 1 && p.VerticalSpeed == 0 && p.AirFlipAvailable, "return to gravity-side floor restores charge");
                Near(p.FlipElapsed, finishTime, "flight clock records entire inertial return");
                Tap(game); Check(p.Flipping && p.Gravity == -1 && p.AirFlipAvailable, "new surface departure has one available reversal");
            }
        }
        static void OppositeSurfaceDoesNotRecharge()
        {
            var game = ReverseAt(.6); var p = game.Players[0];
            double oppositeTime = 1.2 - Math.Sqrt(.08);
            RaceSimulation.Advance(game, oppositeTime - .6);
            Check(p.Y == 0 && p.VerticalSpeed == 0 && p.Gravity == 1 && p.Flipping && !p.AirFlipAvailable,
                "opposite ceiling contact clamps momentum without landing or recharge");
            Check(!RaceSimulation.Press(game, 0), "opposite surface does not permit another input");
            RaceSimulation.Advance(game, .4);
            Near(RaceSimulation.GetPlayerCenterY(p), 67, "gravity leaves opposite boundary from rest");
            Check(!p.AirFlipAvailable, "charge stays spent while leaving opposite boundary");
            RaceSimulation.Advance(game, .4);
            Check(p.Y == 1 && !p.Flipping && p.AirFlipAvailable, "only actual gravity-side landing recharges");
            Near(p.FlipElapsed, oppositeTime + .8, "opposite contact does not reset flight clock");
        }
        static void LateCorrectionAndRecovery()
        {
            var track = new Track(2200, new[] { Stone("ceiling", 196, 64, 48) });
            var game = ReverseAt(.6, track); var p = game.Players[0];
            RaceSimulation.Advance(game, .25);
            Check(p.Collisions == 1 && p.Recovering > 0 && !p.AirFlipAvailable, "late air correction cannot evade incoming stone");
            double vy = p.VerticalSpeed, y = p.Y, elapsed = p.FlipElapsed;
            Check(!RaceSimulation.Press(game, 0), "recovery blocks input without granting charge");
            RaceSimulation.Advance(game, .2);
            Check(p.VerticalSpeed == vy && p.Y == y && p.FlipElapsed == elapsed && p.Gravity == 1 && !p.AirFlipAvailable,
                "recovery freezes momentum, gravity, flight clock and spent charge");
            RaceSimulation.Advance(game, p.Recovering);
            Check(!p.AirFlipAvailable && p.Flipping, "end of penalty itself does not recharge");
            Finish(game, 100); Near(game.Elapsed, 10.5, "late correction collision costs exactly .5");
        }
        static void ReenteredRingOnce()
        {
            // A slow impulse makes the horizontal speed 110 around the apex.
            // This circle is entered, exited near the apex, then entered again:
            // its quartic has four roots inside the same parabolic flight.
            var track = Open(); track.RiskZones = new[] { Free("slow", 0, 226, true), Free("return", 44, 219.51) };
            var game = ReverseAt(.2, track); var p = game.Players[0];
            RaceSimulation.Advance(game, .1);
            Check(p.BoostsEarned == 1 && p.SlowsEarned == 1 && p.PendingEffects.Count == 1, "first curved entry queues free reward behind slow");
            RaceSimulation.Advance(game, .1);
            double dx = p.Distance - 44, dy = RaceSimulation.GetPlayerCenterY(p) - 219.51;
            Check(dx * dx + dy * dy > 400, "trajectory exits circle near its turn");
            RaceSimulation.Advance(game, .1);
            dx = p.Distance - 44; dy = RaceSimulation.GetPlayerCenterY(p) - 219.51;
            Check(dx * dx + dy * dy < 400 && p.BoostsEarned == 1 && p.PendingEffects.Count == 1,
                "return enters same free ring without a second reward");
            Check(!p.AirFlipAvailable, "ring and boost queue never restore air charge");
            Finish(game, 100);
            Check(p.CompletedBoosts == 1 && p.CompletedSlows == 1 && p.ActiveEffect == null && p.PendingEffects.Count == 0,
                "complete opposing impulses survive curved flight");
            Near(game.Elapsed, 10, "opposing impulses cancel actual time");
        }
        static void AirFrameParityAndPlayers()
        {
            foreach (double frame in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100d })
            {
                var game = ReverseAt(.4); Finish(game, frame);
                Near(game.Elapsed, 10, "air return keeps base horizontal finish across frames");
                Check(Equals(game.Winner, "tie") && game.Players[0].FinishTime == game.Players[1].FinishTime, "air return exact symmetric tie");
            }
            foreach (int character in new[] { 0, 1, 2, 3 })
            {
                var game = RaceSimulation.CreateTrial(Open(), MotionMode.GravityAirFlip);
                RaceSimulation.SetCharacter(game, 0, character); RaceSimulation.SetCharacter(game, 1, character);
                RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3); Tap(game);
                RaceSimulation.Advance(game, .4); Check(RaceSimulation.Press(game, 0), "only first player spends air charge");
                Check(!game.Players[0].AirFlipAvailable && game.Players[1].AirFlipAvailable && game.Players[1].Gravity == -1,
                    "gravity direction and charge belong to each player independently");
                RaceSimulation.Advance(game, .4);
                Check(game.Players[0].Flipping && !game.Players[1].Flipping && game.Players[1].Y == 0,
                    "one player returns while the other lands normally");
                Finish(game, 100);
                Check(Equals(game.Winner, "tie") && game.Players[0].CharacterIndex == character, "four equal heroes and different vertical routes tie");
            }
        }
        static void AirPauseAndRematch()
        {
            var game = ReverseAt(.4); var p = game.Players[0];
            double vy = p.VerticalSpeed, y = p.Y, flight = p.FlipElapsed;
            RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, double.PositiveInfinity);
            Check(p.VerticalSpeed == vy && p.Y == y && p.FlipElapsed == flight && p.Gravity == 1 && !p.AirFlipAvailable,
                "pause freezes the reversed inertial state");
            Finish(game, 100); RaceSimulation.SetCharacter(game, 0, 2); Tap(game); p = game.Players[0];
            Check(game.Motion == MotionMode.GravityAirFlip && p.Gravity == 1 && p.VerticalSpeed == 0 && p.Y == 1
                && !p.Flipping && p.AirFlipAvailable && p.CharacterIndex == 2, "air experiment rematch fully resets and retains selection");
        }
        static Track AfterStone(double x, double width, double ringX, double ringY, double clearX)
        {
            return new Track(2200, new[] { Stone("floor", x, width, 48) })
            { RiskZones = new[] { new RiskZone { Id = "after", ObstacleId = "stone", Placement = "after", X = ringX, Y = ringY, ClearX = clearX } } };
        }
        static void LinkedGravityReturn()
        {
            var good = Start(AfterStone(100, 300, 420, 170, 441)); Tap(good);
            RaceSimulation.Advance(good, 280d / 220); Tap(good); RaceSimulation.Advance(good, (445d - 280) / 220);
            Check(good.Players[0].CollectedRiskIds.Count == 1 && good.Players[0].BoostsEarned == 0 && good.Players[0].Flipping,
                "linked after reward waits for actual gravity landing past ClearX");
            Finish(good, 100);
            Check(good.Players[0].BoostsEarned == 1 && good.Players[0].Collisions == 0, "completed clean gravity return confirms reward");
            Near(good.Elapsed, 9.7, "gravity after reward real finish");

            var early = Start(AfterStone(100, 300, 420, 170, 441)); Tap(early);
            RaceSimulation.Advance(early, 250d / 220); Tap(early); Finish(early, 100);
            Check(early.Players[0].CollectedRiskIds.Count == 1 && early.Players[0].BoostsEarned == 0
                && early.Players[0].Collisions == 1 && early.Players[0].FailedRiskIds.Contains("after"),
                "parabolic ring contact followed by rear-edge hit cancels only pending reward");
            Near(early.Elapsed, 10.5, "failed gravity return real penalty");

            var air = ReverseAt(.46, AfterStone(200, 100, 330, 180, 315));
            RaceSimulation.Advance(air, 1.5 - .46);
            Check(air.Players[0].CollectedRiskIds.Count == 1 && air.Players[0].BoostsEarned == 0
                && air.Players[0].Flipping && air.Players[0].FlipElapsed > .8, "air-return linked ring stays pending beyond old flight timer");
            Finish(air, 100);
            Check(air.Players[0].BoostsEarned == 1 && air.Players[0].Collisions == 0, "return to original surface can confirm maneuver");
            Near(air.Players[0].LastSuccess.At, .46 * (2 + Math.Sqrt(2)), "linked reward timestamp follows true inertial landing");
        }
    }
}
