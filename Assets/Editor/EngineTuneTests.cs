using System;
using System.Collections.Generic;
using FlipRace.Core;

namespace FlipRace.Editor
{
    public static class EngineTuneTests
    {
        static int assertions;
        public static string RunAll()
        {
            assertions = 0;
            Action[] tests = { DepartureProfiles, ProfileLandings, ProfileAirReversal, WrongSurfaceHasNoImpulse,
                ProfileContinuousContacts, ProfilePauseSnapshotAndRematch, InstantPartialPreemption,
                InstantExtendsPositive, RepeatedPreemptionKeepsFifo, InstantCollisionFreeze,
                InstantFinishBoundaries, InstantAirborneAndIndependentPlayers, InstantSnapshotPauseAndReset };
            foreach (var test in tests)
                try { test(); }
                catch (Exception error) { throw new Exception("TUNE FAIL " + test.Method.Name + ": " + error.Message, error); }
            return "PASS: TUNE-01/02 " + tests.Length + " scenarios / " + assertions
                + " assertions; four profiles, instant boost, preserved effects and actual finishes.";
        }
        public static string RunInstantPreemptionRegression()
        { assertions = 0; InstantPartialPreemption(); return "PASS: instant preemption / " + assertions + " assertions."; }
        static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        static void Near(double actual, double expected, string message, double tolerance = 1e-8)
        { Check(Math.Abs(actual - expected) < tolerance, message + ": " + actual + " vs " + expected); }
        static GameState Start(MotionProfile profile, Track track = null, MotionMode mode = MotionMode.GravityAirFlip)
        {
            var game = RaceSimulation.CreateTrial(track ?? new Track(2200, Array.Empty<Obstacle>()), mode, profile);
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            return game;
        }
        static void Tap(GameState game)
        { Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "both real inputs accepted"); }
        static void AdvanceFor(GameState game, double time, double frame)
        {
            while (time > 1e-12 && game.Phase == "racing")
            { double step = Math.Min(time, frame); RaceSimulation.Advance(game, step); time -= step; }
        }
        static void Finish(GameState game, double frame)
        {
            int guard = 0;
            while (game.Phase == "racing")
            { RaceSimulation.Advance(game, frame); if (++guard > 100000) throw new Exception("finish stalled"); }
        }
        static double DepartureTime(MotionProfile profile)
        { return 424 / (profile.DepartureImpulse + Math.Sqrt(profile.DepartureImpulse * profile.DepartureImpulse + 424 * profile.GravityAcceleration)); }

        static void DepartureProfiles()
        {
            Check(RaceSimulation.CreateGame().Motion == MotionMode.Linear
                && ReferenceEquals(RaceSimulation.CreateGame().Profile, MotionProfiles.Baseline), "classic defaults preserved");
            Check(ReferenceEquals(RaceSimulation.CreateTrial(new Track(2200, Array.Empty<Obstacle>()), MotionMode.GravityAirFlip).Profile,
                MotionProfiles.Baseline), "old CreateTrial signature keeps baseline");
            double[] expectedY = { 222.6875, 221, 208.6875, 207 };
            for (int index = 0; index < MotionProfiles.All.Length; index++)
            {
                var profile = MotionProfiles.All[index];
                var game = Start(profile); Tap(game); var p = game.Players[0];
                Near(p.VerticalSpeed, -profile.DepartureImpulse, "surface departure applies only initial impulse");
                RaceSimulation.Advance(game, .1);
                Near(RaceSimulation.GetPlayerCenterY(p), expectedY[index], "profile position after .1 seconds");
                Near(p.VerticalSpeed, -profile.DepartureImpulse - profile.GravityAcceleration * .1, "profile acceleration changes velocity");
                Check(ReferenceEquals(game.Profile, MotionProfiles.Get(profile.Id)), "profile is a shared immutable choice");
            }
            var linear = Start(MotionProfiles.Combined, mode: MotionMode.Linear); Tap(linear); RaceSimulation.Advance(linear, .4);
            Near(RaceSimulation.GetPlayerCenterY(linear.Players[0]), 120, "linear motion ignores gravity profile");
        }

        static void ProfileLandings()
        {
            foreach (var profile in MotionProfiles.All)
            foreach (double frame in new[] { 1d / 30, 1d / 144, .317, 100d })
            {
                var game = Start(profile); Tap(game); var p = game.Players[0];
                double flight = DepartureTime(profile);
                AdvanceFor(game, flight - 1e-5, frame);
                Check(p.Flipping && p.Y > 0 && p.VerticalSpeed < 0, "flight persists until actual profile landing");
                AdvanceFor(game, 1e-5, frame);
                Check(!p.Flipping && p.Y == 0 && p.VerticalSpeed == 0 && p.AirFlipAvailable, "exact ceiling landing and recharge");
                Tap(game); Near(p.VerticalSpeed, profile.DepartureImpulse, "ceiling departure impulse is mirrored");
                AdvanceFor(game, flight, frame);
                Check(!p.Flipping && p.Y == 1 && p.VerticalSpeed == 0 && p.AirFlipAvailable, "floor landing uses same acceleration and recharge");
                Finish(game, frame);
                Near(game.Elapsed, 10, "profiles do not change base forward speed");
                Check(Equals(game.Winner, "tie") && game.Players[0].FinishTime == game.Players[1].FinishTime,
                    "same profile gives exact player tie across frame sizes");
            }
        }

        static void ProfileAirReversal()
        {
            foreach (var profile in MotionProfiles.All)
            foreach (double frame in new[] { 1d / 144, .317, 100d })
            {
                var game = Start(profile); Tap(game); AdvanceFor(game, .1, frame);
                var p = game.Players[0]; double incoming = p.VerticalSpeed;
                Tap(game);
                Check(p.VerticalSpeed == incoming && p.Gravity == 1 && !p.AirFlipAvailable,
                    "air repeat retains all incoming momentum without reapplying impulse");
                Check(!RaceSimulation.Press(game, 0), "third press rejected until a true landing");
                double a = profile.GravityAcceleration, u = profile.DepartureImpulse;
                double rise = u * .1 + a * .005 + incoming * incoming / (2 * a);
                double untilTurn = -incoming / a;
                AdvanceFor(game, untilTurn, frame);
                Near(p.VerticalSpeed, 0, "profile-specific inertial turn");
                Near(RaceSimulation.GetPlayerCenterY(p), 226 - rise, "profile-specific hop height");
                Check(p.Flipping && !p.AirFlipAvailable, "turn does not recharge");
                AdvanceFor(game, Math.Sqrt(2 * rise / a), frame);
                Check(p.Y == 1 && p.VerticalSpeed == 0 && !p.Flipping && p.AirFlipAvailable,
                    "return to origin restores charge without an automatic impulse");
            }
        }

        static void WrongSurfaceHasNoImpulse()
        {
            foreach (var profile in MotionProfiles.All)
            {
                var game = Start(profile); Tap(game);
                double departure = .9 * DepartureTime(profile), a = profile.GravityAcceleration;
                AdvanceFor(game, departure, .017);
                var p = game.Players[0]; double remainingRise = RaceSimulation.GetPlayerCenterY(p) - 14;
                double incoming = -p.VerticalSpeed;
                Tap(game);
                double wrongContact = 2 * remainingRise / (incoming + Math.Sqrt(incoming * incoming - 2 * a * remainingRise));
                AdvanceFor(game, wrongContact, .017);
                Check(p.Y == 0 && p.VerticalSpeed == 0 && p.Flipping && p.Gravity == 1 && !p.AirFlipAvailable,
                    "wrong surface clamps speed but neither lands nor gives a new impulse or charge");
                Check(!RaceSimulation.Press(game, 0), "wrong surface cannot be used as another departure");
                AdvanceFor(game, .1, .017);
                Near(RaceSimulation.GetPlayerCenterY(p), 14 + .005 * a, "wrong surface leaves from rest under gravity alone");
                Near(p.VerticalSpeed, .1 * a, "no kick added after wrong contact");
                AdvanceFor(game, Math.Sqrt(424 / a) - .1, .017);
                Check(p.Y == 1 && !p.Flipping && p.AirFlipAvailable && p.VerticalSpeed == 0, "right-side landing still recharges");
            }
        }

        static void ProfileContinuousContacts()
        {
            foreach (var profile in MotionProfiles.All)
            foreach (double frame in new[] { 1d / 30, .317, 100d })
            {
                double a = profile.GravityAcceleration, u = profile.DepartureImpulse;
                var obstacle = new Obstacle { Id = "ceiling", Kind = "ceiling", X = 0, Width = 400, Height = 68 };
                var hit = Start(profile, new Track(2200, new[] { obstacle })); Tap(hit);
                double hitAt = 288 / (u + Math.Sqrt(u * u + 288 * a));
                AdvanceFor(hit, hitAt + .1, frame); var p = hit.Players[0];
                Check(p.Collisions == 1 && p.Recovering > 0, "new trajectory catches actual vertical stone edge");
                Near(p.LastHit.At, hitAt, "continuous collision time uses profile acceleration and impulse");
                Near(RaceSimulation.GetPlayerCenterY(p), 82, "profile collision freezes exact expanded edge");
                Near(p.VerticalSpeed, -u - a * hitAt, "profile collision preserves incoming speed through freeze");
                double y = p.Y, vy = p.VerticalSpeed, distance = p.Distance;
                AdvanceFor(hit, .2, frame);
                Check(p.Y == y && p.VerticalSpeed == vy && p.Distance == distance, "recovery freezes every profile");
                Finish(hit, frame); Near(hit.Elapsed, 10.5, "profile collision still costs exactly .5");

                // The circle is tangent at t=.2 to the independently specified
                // parabola. Its double root must survive every chosen profile.
                double speed = u + .2 * a, norm = Math.Sqrt(speed * speed + 220d * 220);
                var ring = new RiskZone { Id = "tangent", FreeReward = true, Radius = 6,
                    X = 44 + 20 * speed / norm, Y = 226 - .2 * u - .02 * a + 20 * 220 / norm };
                var collection = Start(profile, new Track(2200, Array.Empty<Obstacle>()) { RiskZones = new[] { ring } });
                Tap(collection); Finish(collection, frame);
                Check(collection.Players[0].BoostsEarned == 1 && collection.Players[0].CompletedBoosts == 1,
                    "profile quartic tangency collects once and serves full reward");
                Near(collection.Players[0].LastSuccess.At, .2, "profile tangent contact timestamp", 1e-7);
                Near(collection.Elapsed, 9.7, "profile ring changes real finish by .3");
            }
        }

        internal static PlayerState CopyMotion(PlayerState p)
        {
            var q = new PlayerState(p.Id) { Ready = p.Ready, CharacterIndex = p.CharacterIndex, Distance = p.Distance,
                Y = p.Y, Flipping = p.Flipping, FlipFrom = p.FlipFrom, FlipTo = p.FlipTo, FlipElapsed = p.FlipElapsed,
                VerticalSpeed = p.VerticalSpeed, Gravity = p.Gravity, AirFlipAvailable = p.AirFlipAvailable,
                Support = p.Support, SupportSide = p.SupportSide, SupportObstacleId = p.SupportObstacleId,
                EdgeClearObstacleId = p.EdgeClearObstacleId,
                Recovering = p.Recovering, Collisions = p.Collisions, FinishTime = p.FinishTime, LastHit = p.LastHit,
                ActiveEffect = CopyEffect(p.ActiveEffect), BoostsEarned = p.BoostsEarned, SlowsEarned = p.SlowsEarned,
                InstantBoostsEarned = p.InstantBoostsEarned, LastSuccess = p.LastSuccess, LastInstant = p.LastInstant, LastSlow = p.LastSlow,
                FinishBoost = p.FinishBoost, FinishBoostsEarned = p.FinishBoostsEarned,
                FinishBoostElapsed = p.FinishBoostElapsed, FinishBoostDistanceGained = p.FinishBoostDistanceGained,
                LastFinishBoost = p.LastFinishBoost == null ? null : new HitInfo
                    { Id = p.LastFinishBoost.Id, Message = p.LastFinishBoost.Message, At = p.LastFinishBoost.At },
                CompletedBoosts = p.CompletedBoosts, CompletedSlows = p.CompletedSlows,
                CompletedRecoveries = p.CompletedRecoveries, RecoveryElapsed = p.RecoveryElapsed,
                HitObstacleIds = new HashSet<string>(p.HitObstacleIds), CollectedRiskIds = new HashSet<string>(p.CollectedRiskIds),
                SuccessfulRiskIds = new HashSet<string>(p.SuccessfulRiskIds), FailedRiskIds = new HashSet<string>(p.FailedRiskIds),
                NegativeRiskIds = new HashSet<string>(p.NegativeRiskIds) };
            foreach (var effect in p.PendingEffects) q.PendingEffects.Enqueue(CopyEffect(effect));
            return q;
        }
        static SpeedEffect CopyEffect(SpeedEffect effect)
        {
            return effect == null ? null : new SpeedEffect { RingId = effect.RingId, IsNegative = effect.IsNegative,
                Remaining = effect.Remaining, Elapsed = effect.Elapsed, PulseCount = effect.PulseCount, CompletedPulses = effect.CompletedPulses };
        }
        static void ProfilePauseSnapshotAndRematch()
        {
            foreach (var profile in MotionProfiles.All)
            {
                var game = Start(profile); Tap(game); RaceSimulation.Advance(game, .1); Tap(game);
                var p = game.Players[0]; double vy = p.VerticalSpeed, y = p.Y, age = p.FlipElapsed;
                RaceSimulation.Advance(game, 0);
                Check(p.VerticalSpeed == vy && p.Y == y && p.FlipElapsed == age && !p.AirFlipAvailable,
                    "pause freezes profile momentum and spent charge");
                var clone = new GameState { Mode = game.Mode, Motion = game.Motion, Profile = game.Profile, Track = game.Track,
                    Phase = game.Phase, Elapsed = game.Elapsed, Players = new[] { CopyMotion(game.Players[0]), CopyMotion(game.Players[1]) } };
                RaceSimulation.Advance(game, .05); RaceSimulation.Advance(clone, .05);
                Check(ReferenceEquals(clone.Profile, game.Profile) && clone.Players[0].Y == p.Y
                    && clone.Players[0].VerticalSpeed == p.VerticalSpeed, "snapshot keeps immutable profile and exact trajectory");
                Finish(game, 100); RaceSimulation.SetCharacter(game, 0, 3); Tap(game);
                p = game.Players[0];
                Check(ReferenceEquals(game.Profile, profile) && p.CharacterIndex == 3 && p.Y == 1
                    && p.VerticalSpeed == 0 && p.Gravity == 1 && p.AirFlipAvailable && !p.Flipping,
                    "rematch preserves profile and hero but resets flight before applying any kick");
            }
        }

        static RiskZone Ring(string id, string kind, double x)
        { return new RiskZone { Id = id, Kind = kind, X = x, Y = 226, Radius = 6, FreeReward = kind == "positive" }; }

        static void InstantPartialPreemption()
        {
            // An independent worked example: .2s at110 reaches x22; the pickup
            // immediately changes speed to330 and .1s later reaches finish x55.
            var track = new Track(55, Array.Empty<Obstacle>())
            { RiskZones = new[] { Ring("slow", "negative", 0), Ring("instant", "instant", 42) } };
            foreach (var profile in MotionProfiles.All)
            foreach (double frame in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 100d })
            {
                var game = Start(profile, track); Finish(game, frame);
                foreach (var p in game.Players)
                {
                    Near(p.Distance, 55, "short track physical finish");
                    Near(p.FinishTime.Value, .3, "canonical finish includes already served suspended slow");
                    Check(p.CurrentSpeed == 330 && p.ActiveEffect.RingId == "instant" && p.PendingEffects.Count == 1,
                        "instant is active even with an unserved slow remaining");
                    Near(p.BoostElapsed, .1, "only served instant time counts");
                    Near(p.SlowElapsed, .2, "suspended served time is not lost");
                    Near(p.PendingEffects.Peek().Remaining, .4, "suspended slow retains its future remainder");
                    Near(p.LastInstant.At, .2, "instant starts exactly on continuous contact");
                    Check(p.CompletedBoosts == 0 && p.CompletedSlows == 0 && p.BoostsEarned == 1
                        && p.InstantBoostsEarned == 1 && p.SlowsEarned == 1, "partial finish never credits unserved full pulses");
                }
                Near(game.Elapsed, .3, "actual game clock matches physical finish");
                Check(Equals(game.Winner, "tie"), "partial preemption remains an exact tie");
            }
        }

        static void InstantExtendsPositive()
        {
            var track = new Track(2200, Array.Empty<Obstacle>()) { RiskZones = new[] {
                Ring("ordinary", "positive", 0), Ring("waiting-slow", "negative", 86), Ring("instant", "instant", 152) } };
            var game = Start(MotionProfiles.Baseline, track);
            RaceSimulation.Advance(game, .4); var p = game.Players[0];
            Check(p.CurrentSpeed == 330 && p.ActiveEffect.RingId == "ordinary" && p.ActiveEffect.PulseCount == 2,
                "instant extends the current positive interval without stacking its multiplier");
            Near(p.BoostRemaining, .8, "remaining .2 plus instant .6 stays uninterrupted");
            Near(p.ActiveEffect.Elapsed, .4, "extension preserves already served positive time");
            Near(p.ActiveEffect.Duration, 1.2, "extended duration is two full pulses");
            Check(p.PendingEffects.Count == 1 && p.PendingEffects.Peek().RingId == "waiting-slow", "old FIFO stays behind the extended positive");
            RaceSimulation.Advance(game, .2);
            Check(p.CompletedBoosts == 1 && p.CurrentSpeed == 330 && p.ActiveEffect.CompletedPulses == 1,
                "first full pulse is credited while extension remains active");
            Near(p.BoostElapsed, 0, "completed credit is not counted again as partial time");
            RaceSimulation.Advance(game, .6);
            Check(p.CompletedBoosts == 2 && p.ActiveEffect.RingId == "waiting-slow", "queued slow begins after both positive pulses");
            Near(p.SlowRemaining, .6, "waiting slow keeps its full duration");
            Finish(game, 100);
            Near(game.Elapsed, 9.7, "two positive pulses and one slow give real net -.3");
            Check(p.CompletedSlows == 1 && p.ActiveEffect == null && p.PendingEffects.Count == 0, "extension leaves no unfinished tail");
        }

        static Track PreemptionTrack()
        {
            return new Track(2200, Array.Empty<Obstacle>()) { RiskZones = new[] {
                Ring("slow-a", "negative", 0), Ring("ordinary", "positive", 31), Ring("slow-b", "negative", 36.5),
                Ring("instant-a", "instant", 42), Ring("instant-b", "instant", 75), Ring("instant-c", "instant", 449) } };
        }
        static void RepeatedPreemptionKeepsFifo()
        {
            var game = Start(MotionProfiles.Baseline, PreemptionTrack());
            RaceSimulation.Advance(game, .2); var p = game.Players[0];
            var queued = p.PendingEffects.ToArray();
            Check(queued.Length == 3 && queued[0].RingId == "slow-a" && queued[1].RingId == "ordinary"
                && queued[2].RingId == "slow-b", "paused slow is placed ahead of every previously waiting reward");
            Near(queued[0].Elapsed, .2, "paused slow keeps its served time");
            Near(queued[0].Remaining, .4, "paused slow keeps its remaining time");
            RaceSimulation.Advance(game, .1);
            Check(p.CurrentSpeed == 330 && p.ActiveEffect.PulseCount == 2 && p.PendingEffects.Count == 3,
                "second instant extends active positive without duplicating suspended slow");
            Near(p.BoostRemaining, 1.1, "two instant durations are preserved");
            RaceSimulation.Advance(game, 1.1);
            Check(p.CompletedBoosts == 2 && p.ActiveEffect.RingId == "slow-a", "original slow resumes first after instant chain");
            Near(p.SlowRemaining, .4, "resumption never restarts the slow");
            RaceSimulation.Advance(game, .1);
            Check(p.ActiveEffect.RingId == "instant-c" && p.PendingEffects.Peek().RingId == "slow-a",
                "third instant can interrupt the same slow again");
            Near(p.SlowElapsed, .3, "repeated suspension retains total served partial slow");
            Near(p.PendingEffects.Peek().Remaining, .3, "repeated suspension retains only its remaining budget");
            RaceSimulation.Advance(game, .9);
            Check(p.CompletedBoosts == 3 && p.CompletedSlows == 1 && p.ActiveEffect.RingId == "ordinary",
                "completed resumed slow receives one credit before the old ordinary reward");
            Finish(game, 100);
            Check(p.BoostsEarned == 4 && p.InstantBoostsEarned == 3 && p.CompletedBoosts == 4
                && p.SlowsEarned == 2 && p.CompletedSlows == 2 && p.PendingEffects.Count == 0 && p.ActiveEffect == null,
                "every earned pulse finishes once after repeated preemption");
            Near(game.Elapsed, 9.4, "four boosts and two slows keep exact total real effect");
            foreach (double frame in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 100d })
            {
                var replay = Start(MotionProfiles.Baseline, PreemptionTrack()); Finish(replay, frame);
                Near(replay.Elapsed, 9.4, "FIFO and preemption are independent of frame size");
                Check(replay.Players[0].CompletedBoosts == 4 && replay.Players[0].CompletedSlows == 2
                    && Equals(replay.Winner, "tie"), "large frames cannot skip an instant or suspended remainder");
            }
        }

        static void InstantCollisionFreeze()
        {
            var track = new Track(2200, new[] { new Obstacle { Id = "stone", Kind = "floor", X = 25, Width = 64, Height = 48 } })
            { RiskZones = new[] { Ring("slow", "negative", 0), Ring("ordinary", "positive", 25), Ring("instant", "instant", 31) } };
            var game = Start(MotionProfiles.Combined, track); RaceSimulation.Advance(game, .1); var p = game.Players[0];
            Check(p.Collisions == 1 && p.Recovering > 0 && p.InstantBoostsEarned == 1 && p.ActiveEffect.RingId == "instant",
                "same-time instant contact gives no collision immunity");
            Near(p.LastHit.At, .1, "same-time impact clock"); Near(p.LastInstant.At, .1, "same-time instant clock");
            Near(p.BoostRemaining, .6, "instant duration has not elapsed during impact");
            Check(p.PendingEffects.Peek().RingId == "slow" && p.PendingEffects.Count == 2, "collision retains suspended slow and old FIFO");
            double x = p.Distance, vy = p.VerticalSpeed, active = p.BoostRemaining;
            double waiting = p.PendingEffects.Peek().Remaining, served = p.SlowElapsed;
            Check(!RaceSimulation.Press(game, 0), "instant never unlocks collision input");
            RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, .25);
            Check(p.Distance == x && p.VerticalSpeed == vy && p.BoostRemaining == active
                && p.PendingEffects.Peek().Remaining == waiting && p.SlowElapsed == served,
                "recovery freezes active, suspended, queued and already served accounting");
            Finish(game, 100);
            Near(game.Elapsed, 10.2, "collision .5 plus two boosts and one slow physically totals +.2");
            Check(p.CompletedBoosts == 2 && p.CompletedSlows == 1 && p.Collisions == 1, "collision cannot erase earned effects");
        }

        static void InstantFinishBoundaries()
        {
            foreach (double time in new[] { .6 - 1e-7, .6, .6 + 1e-7, 1.2 - 1e-7, 1.2, 1.2 + 1e-7 })
            foreach (double frame in new[] { 1d / 30, .317, 100d })
            {
                // Two uninterrupted positive pulses: x=330t until1.2, then220.
                double length = time <= 1.2 ? 330 * time : 396 + 220 * (time - 1.2);
                var track = new Track(length, Array.Empty<Obstacle>())
                { RiskZones = new[] { Ring("ordinary", "positive", 0), Ring("instant", "instant", 86) } };
                var game = Start(MotionProfiles.Impulse, track); Finish(game, frame);
                foreach (var p in game.Players)
                {
                    int expected = time < .6 ? 0 : time < 1.2 ? 1 : 2;
                    Check(p.CompletedBoosts == expected && p.BoostsEarned == 2 && p.InstantBoostsEarned == 1,
                        "extended pulses receive one credit at each actual .6 boundary");
                    Near(p.FinishTime.Value, time, "extended partial finish canonical clock");
                    Near(p.BoostDistanceGained, 110 * Math.Min(time, 1.2), "served distance counts completed and partial pulse once");
                    if (time >= 1.2) Check(p.ActiveEffect == null, "completed extension leaves no phantom active credit");
                }
                Near(game.Elapsed, time, "physical finish agrees around each pulse boundary");
                Check(Equals(game.Winner, "tie"), "extension boundary exact tie");
            }
        }

        static void InstantAirborneAndIndependentPlayers()
        {
            var instant = new RiskZone { Id = "air-instant", Kind = "instant", Placement = "after", ObstacleId = "ceiling",
                X = 88, Y = 173, Radius = 6, ClearX = 579 };
            var track = new Track(2200, new[] { new Obstacle { Id = "ceiling", Kind = "ceiling", X = 500, Width = 64, Height = 68 } })
            { RiskZones = new[] { instant } };
            var game = Start(MotionProfiles.Baseline, track);
            Check(RaceSimulation.Press(game, 0), "first player takes the airborne instant route");
            RaceSimulation.Advance(game, .4); var p = game.Players[0];
            Check(p.BoostsEarned == 1 && p.InstantBoostsEarned == 1 && p.BoostRemaining > 0 && p.Flipping,
                "instant confirms at contact without linked clear or landing even when FreeReward is false");
            Check(p.AirFlipAvailable && p.Gravity == -1, "instant does not spend or reset the air charge");
            Near(p.VerticalSpeed, -265, "instant preserves transverse trajectory");
            Check(game.Players[1].InstantBoostsEarned == 0 && game.Players[1].CollectedRiskIds.Count == 0,
                "same ring can be missed independently by the other player");
            Finish(game, 100);
            Check(p.Collisions == 1 && p.InstantBoostsEarned == 1 && p.CompletedBoosts == 1
                && p.SuccessfulRiskIds.Contains("air-instant") && p.FailedRiskIds.Count == 0,
                "instant is earned once and gives no later stone immunity or cancellation");
            Near(p.FinishTime.Value, 10.2, "instant and later collision both affect actual finish");
            Near(game.Players[1].FinishTime.Value, 10, "opponent keeps its own no-effect finish");
            Check(Equals(game.Winner, 1), "instant does not erase the collision penalty");

            var cancellation = new Track(2200, Array.Empty<Obstacle>())
            { RiskZones = new[] { Ring("slow", "negative", 200), Ring("instant", "instant", 222) } };
            var tied = Start(MotionProfiles.Baseline, cancellation); RaceSimulation.Press(tied, 1); Finish(tied, 100);
            Check(tied.Players[0].CompletedBoosts == 1 && tied.Players[0].CompletedSlows == 1
                && tied.Players[1].BoostsEarned == 0 && tied.Players[1].SlowsEarned == 0,
                "one player preempts a slow while the other avoids both objects");
            Check(tied.Players[0].FinishTime == tied.Players[1].FinishTime && Equals(tied.Winner, "tie"),
                "different routes with net-zero served effects still produce an exact tie");
        }

        static void InstantSnapshotPauseAndReset()
        {
            var game = Start(MotionProfiles.Combined, PreemptionTrack()); RaceSimulation.Advance(game, .85);
            var p = game.Players[0];
            Check(p.ActiveEffect.PulseCount == 2 && p.ActiveEffect.CompletedPulses == 1 && p.CompletedBoosts == 1,
                "snapshot fixture contains a partially credited extension");
            double elapsed = game.Elapsed, distance = p.Distance, served = p.BoostElapsed, slow = p.SlowElapsed;
            double remaining = p.BoostRemaining, waiting = p.PendingEffects.Peek().Remaining;
            RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, double.NaN);
            Check(game.Elapsed == elapsed && p.Distance == distance && p.BoostElapsed == served && p.SlowElapsed == slow
                && p.BoostRemaining == remaining && p.PendingEffects.Peek().Remaining == waiting,
                "pause freezes active extension and suspended partial credit");
            var clone = new GameState { Mode = game.Mode, Motion = game.Motion, Profile = game.Profile, Track = game.Track,
                Phase = game.Phase, Elapsed = game.Elapsed, CountdownRemaining = game.CountdownRemaining,
                Winner = game.Winner, WinnerAt = game.WinnerAt, Players = new[] { CopyMotion(game.Players[0]), CopyMotion(game.Players[1]) } };
            Finish(game, 100); Finish(clone, 1d / 144);
            Near(game.Elapsed, 9.4, "original preemption route finishes"); Near(clone.Elapsed, 9.4, "snapshot keeps full served accounting");
            Check(clone.Players[0].FinishTime == p.FinishTime && clone.Players[0].CompletedBoosts == 4
                && clone.Players[0].CompletedSlows == 2 && clone.Players[0].InstantBoostsEarned == 3,
                "snapshot carries both pulse fields, all earned effects and exact finish");
            RaceSimulation.SetCharacter(game, 0, 3); Tap(game); p = game.Players[0];
            Check(ReferenceEquals(game.Profile, MotionProfiles.Combined) && p.CharacterIndex == 3
                && p.InstantBoostsEarned == 0 && p.LastInstant == null && p.ActiveEffect == null && p.PendingEffects.Count == 0
                && p.CompletedBoosts == 0 && p.CompletedSlows == 0 && p.BoostElapsed == 0 && p.SlowElapsed == 0,
                "rematch resets instant/partial accounting and keeps profile and hero");
        }
    }
}
