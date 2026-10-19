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
                ReenteredRingOnce, AirFrameParityAndPlayers, AirPauseAndRematch, LinkedGravityReturn,
                UntouchedLinkedRingAfterHit, LinkedContactImpactOrder, PostHitPlayerIsolationAndReset,
                ChallengePostHitRing };
            foreach (var test in tests)
                try { test(); }
                catch (Exception error) { throw new Exception("V4 FAIL " + test.Method.Name + ": " + error.Message, error); }
            return "PASS: V4-01/02 " + tests.Length + " scenarios / " + assertions
                + " assertions; gravity, continuous contacts, one inertial air flip, free rings and legacy separation.";
        }
        static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        public static string RunPostHitRingRegression()
        {
            assertions = 0;
            UntouchedLinkedRingAfterHit(Console.WriteLine);
            return "PASS: V4 post-hit untouched linked ring / " + assertions + " assertions.";
        }
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
            Check(good.Players[0].CollectedRiskIds.Count == 1 && good.Players[0].BoostsEarned == 1 && good.Players[0].Flipping,
                "MODES: linked after reward confirms at geometric clearance before gravity landing");
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
            Check(air.Players[0].CollectedRiskIds.Count == 1 && air.Players[0].BoostsEarned == 1
                && air.Players[0].Flipping && air.Players[0].FlipElapsed > .8, "MODES: clearance confirms during long inertial return");
            Finish(air, 100);
            Check(air.Players[0].BoostsEarned == 1 && air.Players[0].Collisions == 0, "return to original surface can confirm maneuver");
            Check(air.Players[0].LastSuccess.At < .46 * (2 + Math.Sqrt(2)), "geometric confirmation precedes true inertial landing");
        }

        static void UntouchedLinkedRingAfterHit()
        { UntouchedLinkedRingAfterHit(null); }

        static void UntouchedLinkedRingAfterHit(Action<string> trace)
        {
            // Only real game actions: first hit the floor stone without jumping,
            // recover fully, then depart at x=212 and cross the after-ring at x=300.
            var game = Start(AfterStone(100, 64, 300, 173, 321));
            RaceSimulation.Advance(game, 86d / 220);
            var p = game.Players[0];
            Check(p.Collisions == 1 && p.Recovering > 0 && p.CollectedRiskIds.Count == 0,
                "first collision occurs before any ring contact");
            Near(p.Recovering, .5, "full collision penalty starts");
            RaceSimulation.Advance(game, .5);
            Check(p.Recovering == 0 && p.CompletedRecoveries == 1, "full penalty ends before later collection");
            RaceSimulation.Advance(game, 126d / 220);
            Near(p.Distance, 212, "later departure position");
            Tap(game);
            RaceSimulation.Advance(game, .4);
            Near(p.Distance, 300, "physical ring crossing x");
            Near(RaceSimulation.GetPlayerCenterY(p), 173, "physical ring crossing y");
            bool collectedAtContact = p.CollectedRiskIds.Contains("after");
            bool pendingAtContact = collectedAtContact && p.BoostsEarned == 0;
            if (trace != null) trace("After physical contact: collected=" + collectedAtContact + ", pending="
                + pendingAtContact + ", failed=" + p.FailedRiskIds.Contains("after") + ", hitHistory=" + p.HitObstacleIds.Contains("stone"));
            RaceSimulation.Advance(game, .1);
            bool awardedAfterClear = p.Distance > 321 && p.Flipping && p.BoostsEarned == 1;
            if (trace != null) trace("After clearance: x=" + p.Distance + ", airborne=" + p.Flipping + "; reward confirmed without landing.");
            RaceSimulation.Advance(game, .3);
            bool awardedAtLanding = !p.Flipping && p.BoostsEarned == 1;
            Finish(game, 100);
            Check(collectedAtContact && pendingAtContact && awardedAfterClear && awardedAtLanding
                && p.BoostsEarned == 1 && p.CompletedBoosts == 1 && !p.FailedRiskIds.Contains("after"),
                "hit -> .5s recovery -> untouched linked contact -> pending -> one reward; actual collected="
                + collectedAtContact + ", pending=" + pendingAtContact + ", landingAward=" + awardedAtLanding
                + ", failed=" + p.FailedRiskIds.Contains("after") + ", rewards=" + p.BoostsEarned);
            Near(p.FinishTime.Value, 10.2, "collision .5 and later full reward -.3 both affect physical finish");
            Near(game.Elapsed, 10.2, "actual clock matches post-hit reward result");
            Check(Equals(game.Winner, "tie"), "post-hit reward remains independent and exactly symmetric");
        }

        static void LinkedContactImpactOrder()
        {
            // Shift contact by +/-0.1ms around the same obstacle impact. The
            // pre-impact pending state, not obstacle history, decides cancellation.
            // This historical V4 contract covers the original three modes.
            // Later controls have their own trajectories in EngineControlTests.
            foreach (MotionMode mode in new[] { MotionMode.Linear, MotionMode.Gravity, MotionMode.GravityAirFlip })
            foreach (bool ceiling in new[] { false, true })
            foreach (string placement in new[] { "before", "after" })
            foreach (double offset in new[] { -.022, 0, .022 })
            foreach (double frame in new[] { 1d / 144, .317, 100d })
            {
                var track = new Track(2200, new[] { Stone(ceiling ? "ceiling" : "floor", 300, 64, 48) })
                { RiskZones = new[] { new RiskZone { Id = "order", ObstacleId = "stone", Placement = placement,
                    X = 306 + offset, Y = ceiling ? 14 : 226, Radius = 6, ClearX = 379 } } };
                var game = Start(track, mode);
                if (ceiling) Tap(game);
                Finish(game, frame);
                foreach (var p in game.Players)
                {
                    bool touchedFirst = offset < 0;
                    Check(p.Collisions == 1 && p.CollectedRiskIds.Contains("order"), "ordered route has one hit and one ring contact");
                    Check(p.FailedRiskIds.Contains("order") == touchedFirst, "only contact strictly preceding impact is cancelled");
                    Check(p.BoostsEarned == (touchedFirst ? 0 : 1) && p.CompletedBoosts == p.BoostsEarned,
                        "simultaneous or later untouched contact gives exactly one complete reward");
                    Near(p.FinishTime.Value, touchedFirst ? 10.5 : 10.2, "contact order controls real finish");
                    Check(p.ActiveEffect == null && p.PendingEffects.Count == 0, "ordered effects finish without zero-time stalling");
                }
                Check(Equals(game.Winner, "tie"), "contact order is symmetric across players");
            }

            // Stop exactly on a simultaneous hit/contact, then resume. Both
            // untouched rings remain eligible and preserve authored FIFO order.
            var simultaneous = new Track(2200, new[] { Stone("floor", 100, 64, 48) })
            { RiskZones = new[] {
                new RiskZone { Id = "first", ObstacleId = "stone", X = 106, Y = 226, Radius = 6, ClearX = 179 },
                new RiskZone { Id = "second", ObstacleId = "stone", Placement = "after", X = 106, Y = 226, Radius = 6, ClearX = 179 }
            } };
            var exact = Start(simultaneous);
            RaceSimulation.Advance(exact, 86d / 220);
            var first = exact.Players[0];
            Check(first.Collisions == 1 && first.CollectedRiskIds.Contains("first") && first.FailedRiskIds.Count == 0,
                "same-time impact does not cancel a previously untouched contact at the step endpoint");
            Near(first.Recovering, .5, "same-time ring leaves the full penalty intact");
            RaceSimulation.Advance(exact, .5 + 93d / 220 + .001);
            Check(first.ActiveEffect != null && first.ActiveEffect.RingId == "first"
                && first.PendingEffects.Count == 1 && first.PendingEffects.Peek().RingId == "second",
                "same-time post-hit confirmations preserve FIFO order");
            Finish(exact, 100);
            Check(first.BoostsEarned == 2 && first.CompletedBoosts == 2, "both untouched rings award once after the hit");
            Near(exact.Elapsed, 9.9, "one collision plus two complete rewards");
        }

        static void PostHitPlayerIsolationAndReset()
        {
            var game = Start(AfterStone(100, 64, 300, 173, 321));
            Check(RaceSimulation.Press(game, 1), "opponent departs early and avoids the stone");
            RaceSimulation.Advance(game, 212d / 220 + .5);
            var first = game.Players[0]; var second = game.Players[1];
            Check(first.Collisions == 1 && second.Collisions == 0, "one player's collision history is independent");
            Check(RaceSimulation.Press(game, 0), "hit player can depart after full recovery");
            Finish(game, .317);
            Check(first.BoostsEarned == 1 && first.SuccessfulRiskIds.Contains("after") && first.FailedRiskIds.Count == 0,
                "hit player earns the later untouched ring");
            Check(second.BoostsEarned == 0 && second.CollectedRiskIds.Count == 0 && second.FailedRiskIds.Count == 0,
                "opponent's missed ring remains untouched");
            Near(first.FinishTime.Value, 10.2, "hit player keeps both penalty and reward");
            Near(second.FinishTime.Value, 10, "opponent's actual finish is independent");
            Check(Equals(game.Winner, 1), "later ring does not erase the entire collision penalty");
            Tap(game);
            foreach (var p in game.Players)
                Check(p.HitObstacleIds.Count == 0 && p.CollectedRiskIds.Count == 0 && p.FailedRiskIds.Count == 0
                    && p.SuccessfulRiskIds.Count == 0 && p.BoostsEarned == 0 && p.ActiveEffect == null && p.PendingEffects.Count == 0,
                    "rematch clears all contact-order and effect state");
        }

        static void MoveToTrial(GameState game, double target, double frame)
        {
            int guard = 0;
            var p = game.Players[0];
            while (p.Distance < target - 1e-8 && game.Phase == "racing")
            {
                if (++guard > 100000) throw new Exception("trial input target stalled");
                // Bound with the maximum possible forward speed so a mid-step
                // reward cannot overshoot the actual Press position.
                double dt = p.Recovering > 0 ? p.Recovering : (target - p.Distance) / 330;
                RaceSimulation.Advance(game, Math.Min(frame, dt));
            }
            Near(p.Distance, target, "distance-driven trial action", 1e-7);
        }

        static void ChallengePostHitRing()
        {
            double[] route = { 1000, 1057.2, 1393, 1670, 1990, 2430, 2513.6, 3000, 3083.6, 3600, 3960 };
            foreach (double frame in new[] { 1d / 60, .317, 100d })
            {
                var game = Start(TracksV4.Challenge(), MotionMode.GravityAirFlip);
                bool inspected = false;
                foreach (double x in route)
                {
                    if (!inspected && x > 2680)
                    {
                        MoveToTrial(game, 2680, frame); var p = game.Players[0];
                        Check(p.Collisions == 1 && p.LastHit.Id == "v4-return-ceiling" && p.CompletedRecoveries == 1,
                            "authored challenge hits the linked stone and serves recovery before contact");
                        Check(p.CollectedRiskIds.Contains("v4-linked-after") && !p.SuccessfulRiskIds.Contains("v4-linked-after")
                            && !p.FailedRiskIds.Contains("v4-linked-after") && p.Flipping,
                            "authored post-hit ring is collected and waits for the actual return");
                        Near(p.LastHit.At, 2426d / 220 - .6, "browser route exact collision timestamp");
                        inspected = true;
                    }
                    MoveToTrial(game, x, frame); Tap(game);
                }
                Finish(game, frame);
                foreach (var p in game.Players)
                {
                    Check(p.BoostsEarned == 4 && p.CompletedBoosts == 4 && p.SlowsEarned == 0 && p.Collisions == 1,
                        "authored recovery route gets all four rings, no trap and only the intended hit");
                    Check(p.SuccessfulRiskIds.Contains("v4-linked-after") && p.FailedRiskIds.Count == 0
                        && p.ActiveEffect == null && p.PendingEffects.Count == 0, "authored route serves every reward once");
                    Near(p.FinishTime.Value, 19.3, "authored hit plus four rewards actual finish");
                }
                Near(game.Elapsed, 19.3, "authored actual race clock");
                Check(Equals(game.Winner, "tie"), "authored recovery route exact symmetric tie");
            }
        }
    }
}
