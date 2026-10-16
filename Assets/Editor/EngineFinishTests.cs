using System;
using FlipRace.Core;

namespace FlipRace.Editor
{
    public static class EngineFinishTests
    {
        static int assertions;
        static readonly MotionMode[] Modes = { MotionMode.GravityAirFlip, MotionMode.LinearAirFlip,
            MotionMode.JumpGravityFlip, MotionMode.GravityUnlimited };
        static readonly double[] Frames = { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100 };
        public static string RunAll()
        {
            assertions = 0;
            Action[] tests = { PairedHitMissAndTie, HeightBoundaries, ExactPlaneAndOnce,
                BeforeAtAndAfterFinish, DirtyEntryLedger, CollisionFreeze, PauseSnapshotAndRematch,
                LegacyLinearSweep, GraceWithFinishEffect, ExactGraceFinish };
            foreach (var test in tests)
                try { test(); }
                catch (Exception error) { throw new Exception("CONTROL-03F FAIL " + test.Method.Name + ": " + error.Message, error); }
            return "PASS: CONTROL-03F " + tests.Length + " scenarios / " + assertions + " assertions.";
        }
        static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception(message); }
        static void Near(double actual, double expected, string message, double tolerance = 2e-8)
        { Check(Math.Abs(actual - expected) < tolerance, message + ": " + actual + " vs " + expected); }
        static MotionProfile Profile(MotionMode mode)
        { return mode == MotionMode.JumpGravityFlip ? MotionProfiles.Jump : mode == MotionMode.LinearAirFlip
            || mode == MotionMode.Linear ? MotionProfiles.Baseline : MotionProfiles.Moderate; }
        static RiskZone Target(double x, double y = 150, double height = 16, string id = "finish")
        { return new RiskZone { Id = id, Kind = "finish", X = x, Y = y, FinishHeight = height, Radius = 10000 }; }
        static Track Empty(double length = 2200, double y = 150)
        { return new Track(length, Array.Empty<Obstacle>()) { RiskZones = new[] { Target(length - 44, y) } }; }
        static GameState Start(Track track, MotionMode mode = MotionMode.GravityAirFlip)
        {
            var game = RaceSimulation.CreateTrial(track, mode, Profile(mode));
            Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "ready edges accepted");
            RaceSimulation.Advance(game, 3); return game;
        }
        static void AdvanceFor(GameState game, double duration, double frame)
        {
            while (duration > 1e-12 && game.Phase == "racing")
            { double step = Math.Min(duration, frame); RaceSimulation.Advance(game, step); duration -= step; }
        }
        static double AscentTime(MotionMode mode, double center)
        {
            double distance = 226 - center;
            if (mode == MotionMode.LinearAirFlip || mode == MotionMode.Linear) return distance / 265;
            if (mode == MotionMode.JumpGravityFlip)
                return 2 * distance / (400 + Math.Sqrt(400 * 400 - 2 * 662.5 * distance));
            return 2 * distance / (70 + Math.Sqrt(70 * 70 + 2 * 1000 * distance));
        }
        static void DepartForPlane(GameState game, double center, bool both, double frame)
        {
            double departure = game.Track.RiskZones[0].X / 220 - AscentTime(game.Motion, center);
            AdvanceFor(game, departure, frame);
            Check(RaceSimulation.Press(game, 0), "plane approach begins with actual departure");
            if (both) Check(RaceSimulation.Press(game, 1), "matching opponent departure");
        }
        static void CheckOrdinaryUntouched(PlayerState p)
        {
            Check(p.BoostsEarned == 0 && p.InstantBoostsEarned == 0 && p.CompletedBoosts == 0
                && p.ActiveEffect == null && p.PendingEffects.Count == 0 && p.BoostElapsed == 0,
                "finish reward never creates or credits an ordinary pulse");
        }
        static void PairedHitMissAndTie()
        {
            foreach (var mode in Modes)
            foreach (double frame in Frames)
            {
                var game = Start(Empty(), mode); DepartForPlane(game, 150, false, frame);
                AdvanceFor(game, 100, frame);
                var p = game.Players[0]; var q = game.Players[1];
                Near(p.FinishTime.Value, 9.9, "hit actual finish"); Near(q.FinishTime.Value, 10, "miss actual finish");
                Near(game.Elapsed, 10, "both physical finish crossings");
                Check(Equals(game.Winner, 0) && p.FinishBoostsEarned == 1 && q.FinishBoostsEarned == 0, "independent winner and collection");
                Near(p.LastFinishBoost.At, 9.8, "real X plane contact timestamp");
                Near(p.FinishBoostElapsed, .1, "last44 physically take .1s");
                Near(p.FinishBoostDistanceGained, 22, "extra distance represents only .1 gain");
                Check(!p.FinishBoost && !q.FinishBoost && p.Distance == 2200 && q.Distance == 2200, "effect ends at physical finish");
                CheckOrdinaryUntouched(p); CheckOrdinaryUntouched(q);

                // Same starting state and actions, changed target only: a miss
                // has no cost. Circular Radius10000 must not collect early.
                var miss = Start(Empty(2200, 130), mode); DepartForPlane(miss, 150, false, frame);
                AdvanceFor(miss, 100, frame);
                Near(miss.Players[0].FinishTime.Value - p.FinishTime.Value, .1, "paired hit saves exactly .1");
                Check(miss.Players[0].FinishBoostsEarned == 0 && miss.Players[0].Collisions == 0, "miss is free despite enormous circle radius");

                var tied = Start(Empty(), mode); DepartForPlane(tied, 150, true, frame); AdvanceFor(tied, 100, frame);
                Check(Equals(tied.Winner, "tie") && tied.Players[0].FinishTime == tied.Players[1].FinishTime, "identical targets preserve exact tie");
                Near(tied.Elapsed, 9.9, "equal real finishes at9.9");
            }
        }
        static void HeightBoundaries()
        {
            foreach (var mode in Modes)
            foreach (double frame in Frames)
            foreach (double offset in new[] { -8 - 1e-6, -8d, -8 + 1e-6, 8 - 1e-6, 8d, 8 + 1e-6 })
            {
                var game = Start(Empty(), mode); DepartForPlane(game, 150 + offset, true, frame); AdvanceFor(game, 100, frame);
                bool hit = Math.Abs(offset) <= 8;
                Check(game.Players[0].FinishBoostsEarned == (hit ? 1 : 0), "inclusive center-Y boundary " + mode + " " + offset);
                Near(game.Elapsed, hit ? 9.9 : 10, "boundary actual finish");
            }
        }
        static void ExactPlaneAndOnce()
        {
            var track = Empty(); track.RiskZones = new[] { track.RiskZones[0], Target(2156, 150, 16, "duplicate"), Target(2167, 150, 240, "later") };
            var game = Start(track); DepartForPlane(game, 150, true, .317);
            AdvanceFor(game, 9.8 - 1e-5 - game.Elapsed, .317);
            var p = game.Players[0]; Check(p.FinishBoostsEarned == 0 && p.CurrentSpeed == 220, "no enlarged early contact");
            AdvanceFor(game, 1e-5, 100);
            Near(p.Distance, 2156, "contact exact world plane");
            Check(p.FinishBoost && p.CurrentSpeed == 440 && p.FinishBoostsEarned == 1, "immediate speed440 at plane");
            Near(p.LastFinishBoost.At, 9.8, "contact event time");
            double y = p.Y, vy = p.VerticalSpeed; int gravity = p.Gravity; bool charge = p.AirFlipAvailable;
            RaceSimulation.Advance(game, 0);
            Check(p.Y == y && p.VerticalSpeed == vy && p.Gravity == gravity && p.AirFlipAvailable == charge, "pickup preserves transverse state");
            AdvanceFor(game, 100, 100);
            Check(p.FinishBoostsEarned == 1 && p.SuccessfulRiskIds.Count == 1, "coincident and later finish targets cannot repeat award");
            Near(game.Elapsed, 9.9, "no duplicate fixed time credit");
        }
        static void BeforeAtAndAfterFinish()
        {
            foreach (double x in new[] { 0d, 22, 44, 45 })
            foreach (double frame in Frames)
            {
                var track = new Track(44, Array.Empty<Obstacle>()) { RiskZones = new[] { Target(x, 226) } };
                var game = Start(track); AdvanceFor(game, 100, frame);
                bool valid = x < 44;
                double expected = valid ? x / 220 + (44 - x) / 440 : .2;
                Near(game.Elapsed, expected, "served partial finish only, never retrospective fixed credit");
                Check(game.Players[0].FinishBoostsEarned == (valid ? 1 : 0), "at/after finish target cannot award");
                RaceSimulation.Advance(game, 100);
                Near(game.Elapsed, expected, "no reward or time after result");
            }
            // A real miss passes the plane; entering its Y band afterwards is
            // not a pickup from behind. No coordinates are manually assigned.
            var missed = Start(Empty()); DepartForPlane(missed, 160, true, 100);
            AdvanceFor(missed, 9.85 - missed.Elapsed, 100);
            Check(missed.Players[0].Distance > 2156 && missed.Players[0].FinishBoostsEarned == 0, "crossed miss remains missed when later Y enters band");
            AdvanceFor(missed, 100, 100); Near(missed.Elapsed, 10, "past-plane contact cannot recover a miss");
        }
        static RiskZone Ordinary(string id, string kind)
        { return new RiskZone { Id = id, Kind = kind, X = 20, Y = 226, Radius = 6, FreeReward = true }; }
        static void DirtyEntryLedger()
        {
            foreach (bool negative in new[] { false, true })
            foreach (double frame in Frames)
            {
                var track = new Track(55, Array.Empty<Obstacle>()) { RiskZones = new[] {
                    Ordinary("active", negative ? "negative" : "positive"), Ordinary("queued", "positive"), Target(11, 226) } };
                var game = Start(track); AdvanceFor(game, 100, frame); var p = game.Players[0];
                double speed = negative ? 110 : 330, expected = 11 / speed + .1;
                Near(game.Elapsed, expected, "dirty approach physical finish");
                Near(p.FinishTime.Value, expected, "differential ledger includes overridden ordinary partial");
                Near(p.FinishBoostDistanceGained, (440 - speed) * .1, "finish extra measured against preserved ordinary speed");
                Near(p.ActiveEffect.Elapsed, expected, "ordinary effect continues naturally");
                Near(p.ActiveEffect.Remaining, .6 - expected, "ordinary effect not erased at finish contact");
                Check(p.PendingEffects.Count == 1 && p.PendingEffects.Peek().RingId == "queued"
                    && p.PendingEffects.Peek().Remaining == .6, "earned queue preserved unserved");
                Check(p.CompletedBoosts == 0 && p.CompletedSlows == 0 && p.FinishBoostsEarned == 1, "no fabricated full credits");
            }
            foreach (double frame in Frames)
            {
                // The old positive ends halfway through the .1 finish tail;
                // its queued slow starts naturally while actual speed stays440.
                var track = new Track(225.5, Array.Empty<Obstacle>()) { RiskZones = new[] {
                    Ordinary("positive", "positive"), Ordinary("negative", "negative"), Target(181.5, 226) } };
                var game = Start(track); AdvanceFor(game, 100, frame); var p = game.Players[0];
                Near(game.Elapsed, .65, "ordinary completion inside finish tail preserves physical arrival");
                Near(p.FinishTime.Value, .65, "ledger spans ordinary speed transition");
                Near(p.FinishBoostDistanceGained, 22, "extra distance includes positive then slow background");
                Check(p.CompletedBoosts == 1 && p.CompletedSlows == 0 && p.ActiveEffect.IsNegative
                    && p.PendingEffects.Count == 0, "old queue advances normally during finish effect");
                Near(p.ActiveEffect.Elapsed, .05, "only actual slow slice served");
                Near(p.ActiveEffect.Remaining, .55, "unserved slow is retained at finish");
            }
        }
        static void CollisionFreeze()
        {
            var track = new Track(66, new[] { new Obstacle { Id = "stone", Kind = "floor", X = 36, Width = 64, Height = 48 } })
                { RiskZones = new[] { Target(22, 226) } };
            var game = Start(track); RaceSimulation.Advance(game, .1); var p = game.Players[0];
            Check(p.Collisions == 1 && p.FinishBoost && p.FinishBoostsEarned == 1, "same-time collision and finish pickup both apply");
            Near(p.LastHit.At, .1, "collision timestamp"); Near(p.LastFinishBoost.At, .1, "finish pickup timestamp");
            Check(!RaceSimulation.Press(game, 0), "finish effect gives no collision immunity");
            RaceSimulation.Advance(game, .25); Near(p.Distance, 22, "collision freezes distance");
            Near(p.FinishBoostElapsed, 0, "collision freezes finish ledger");
            AdvanceFor(game, 100, 100); Near(game.Elapsed, .7, "real collision delay plus finish tail");
            Near(p.FinishTime.Value, .7, "canonical collision finish");
        }
        static void PauseSnapshotAndRematch()
        {
            var game = Start(Empty(44, 226)); RaceSimulation.Advance(game, .04); var p = game.Players[0];
            Check(p.FinishBoost && p.FinishBoostsEarned == 1, "snapshot within active tail");
            double x = p.Distance, gain = p.FinishBoostDistanceGained, elapsed = game.Elapsed;
            RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, double.NaN);
            Check(p.Distance == x && p.FinishBoostDistanceGained == gain && game.Elapsed == elapsed, "pause does not consume finish movement");
            var clone = new GameState { Mode = game.Mode, Motion = game.Motion, Profile = game.Profile, Track = game.Track,
                Phase = game.Phase, Elapsed = game.Elapsed, Players = new[] { EngineTuneTests.CopyMotion(p), EngineTuneTests.CopyMotion(game.Players[1]) } };
            Check(!ReferenceEquals(clone.Players[0].LastFinishBoost, p.LastFinishBoost), "snapshot owns separate event record");
            AdvanceFor(game, 100, 100); AdvanceFor(clone, 100, 1d / 144);
            Near(game.Elapsed, .1, "paused original actual finish"); Near(clone.Elapsed, .1, "snapshot finish preserves ledger");
            Check(clone.Players[0].FinishTime == p.FinishTime, "snapshot exact final timestamp");
            Check(RaceSimulation.SetCharacter(game, 0, 3), "choose hero before rematch");
            Check(RaceSimulation.Press(game, 0), "one ready action");
            Check(game.Players[0].FinishBoostsEarned == 1, "one ready retains result until rematch begins");
            Check(RaceSimulation.Press(game, 1), "both ready start new race"); p = game.Players[0];
            Check(!p.FinishBoost && p.FinishBoostsEarned == 0 && p.FinishBoostElapsed == 0
                && p.FinishBoostDistanceGained == 0 && p.LastFinishBoost == null && p.SuccessfulRiskIds.Count == 0,
                "new race clears every finish field and once-only id");
            Check(p.CharacterIndex == 3 && ReferenceEquals(game.Profile, MotionProfiles.Moderate), "rematch keeps profile and hero");
            RaceSimulation.Advance(game, 100); Near(game.Elapsed, .1, "new race can independently collect again");
        }
        static void LegacyLinearSweep()
        {
            foreach (double frame in Frames)
            {
                var game = Start(Empty(), MotionMode.Linear); DepartForPlane(game, 150, true, frame);
                AdvanceFor(game, 100, frame); Near(game.Elapsed, 9.9, "legacy linear trajectory uses exact plane contact");
            }
        }
        static void ExactGraceFinish()
        {
            var obstacles = new Obstacle[7];
            for (int i = 0; i < obstacles.Length; i++) obstacles[i] = new Obstacle
                { Id = "deadline-" + i, Kind = "floor", X = 300 + i * 180, Width = 40, Height = 48 };
            // Both receive the same finish effect; seven real impacts delay
            // only the floor player by exactly the allowed3.5 seconds.
            var track = new Track(2200, obstacles) { RiskZones = new[] { Target(2156, 120, 240) } };
            foreach (double frame in Frames)
            {
                var game = Start(track); Check(RaceSimulation.Press(game, 0), "deadline winner leaves floor");
                AdvanceFor(game, 100, frame);
                Near(game.Elapsed, 13.4, "exact deadline physical time");
                Check(game.Players[1].FinishTime.HasValue, "runner arriving exactly at deadline must finish");
                Near(game.Players[0].FinishTime.Value, 9.9, "deadline winner actual finish");
                Near(game.Players[1].FinishTime.Value, 13.4, "deadline loser actual finish");
                Check(game.Players[1].Distance == 2200 && game.Players[1].Collisions == 7
                    && game.Players[1].FinishBoostsEarned == 1 && Equals(game.Winner, 0), "deadline keeps physical arrival and original winner");
            }
        }
        static void GraceWithFinishEffect()
        {
            // A synthetic long recovery chain is solely a grace-bound fixture.
            // The idle floor player hits every stone; the ceiling player earns
            // the finish target. All impacts remain real swept contacts.
            var obstacles = new Obstacle[9];
            for (int i = 0; i < obstacles.Length; i++) obstacles[i] = new Obstacle
                { Id = "floor-" + i, Kind = "floor", X = 300 + i * 180, Width = 40, Height = 48 };
            var track = new Track(2200, obstacles) { RiskZones = new[] { Target(2156, 14) } };
            foreach (double frame in Frames)
            {
                var game = Start(track); Check(RaceSimulation.Press(game, 0), "clean player leaves floor");
                AdvanceFor(game, 100, frame);
                Near(game.Elapsed, 13.4, "large finish effect respects winner grace");
                Near(game.Players[0].FinishTime.Value, 9.9, "clean ceiling winner actual finish");
                Check(Equals(game.Winner, 0) && game.Players[1].Collisions == 9 && !game.Players[1].FinishTime.HasValue,
                    "loser cannot finish during excess Advance time");
                Near(game.Players[1].Distance, (13.4 - 4.5) * 220, "loser stops at exact grace position");
            }
        }
    }
}
