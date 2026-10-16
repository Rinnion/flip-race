using System;
using System.Collections.Generic;
using FlipRace.Core;

namespace FlipRace.Editor
{
    // Every reward/penalty is obtained through movement, contact and input edges.
    // No test injects a timer, collected ID or completed-effect counter.
    public static class EngineV3Tests
    {
        static int assertions;
        public static string RunAll()
        {
            assertions = 0;
            Action[] tests = { CloseEffectsAndControlTable, NegativeAllowsInput, RecoveryFreezesQueue,
                AfterRingConfirmation, PartialFinish, IndependentPlayersAndExactTie, GraceDeadline, PauseAndReset,
                OrderedOppositeEffects, AuthoredRoutes, IndependentReturnCancellation, FullNegativeRoutes };
            foreach (var test in tests)
                try { test(); }
                catch (Exception error) { throw new Exception("V3 FAIL " + test.Method.Name + ": " + error.Message, error); }
            return "PASS: V3 " + tests.Length + " scenarios / " + assertions
                + " assertions; FIFO effects, real finishes, after-ring confirmation, pause/reset and independence.";
        }
        static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        static void Near(double a, double b, string message, double tolerance = 1e-8)
        { Check(Math.Abs(a - b) < tolerance, message + ": " + a + " vs " + b); }
        static GameState Start(Track track)
        {
            var game = new GameState { Mode = "practice", Track = track };
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            return game;
        }
        static RiskZone Ring(string id, double x, bool negative, double y = 226)
        { return new RiskZone { Id = id, X = x, Y = y, ClearX = x + 1, Kind = negative ? "negative" : "positive" }; }
        static Track RingTrack(string signs, double length = 2200, bool collision = false)
        {
            var rings = new List<RiskZone>();
            for (int i = 0; i < signs.Length; i++) rings.Add(Ring("ring-" + i, 100 + i * 10, signs[i] == '-'));
            return new Track(length, collision ? new[] { new Obstacle { Id = "hit", X = 400, Width = 64, Height = 68, Kind = "floor" } }
                : Array.Empty<Obstacle>()) { RiskZones = rings.ToArray() };
        }
        static void MoveTo(GameState game, double target, double frame = 1d / 60)
        {
            int guard = 0;
            while (game.Players[0].Distance < target - 1e-7)
            {
                if (game.Phase != "racing") throw new Exception("route ended before target distance");
                RaceSimulation.Advance(game, Math.Min(frame, (target - game.Players[0].Distance) / 330));
                if (++guard > 100000) throw new Exception("MoveTo stalled");
            }
        }
        static void Finish(GameState game, double frame)
        {
            int guard = 0;
            while (game.Phase == "racing")
            {
                RaceSimulation.Advance(game, frame);
                if (++guard > 100000) throw new Exception("finish stalled");
            }
        }
        static void TapBoth(GameState game)
        { Check(RaceSimulation.Press(game, 0), "first tap accepted"); Check(RaceSimulation.Press(game, 1), "second tap accepted"); }
        static void CloseEffectsAndControlTable()
        {
            foreach (var signs in new[] { "+", "-", "+-", "-+", "++", "--", "++++++", "++++++--" })
                foreach (double frame in new[] { 1d / 30, 1d / 144, .317, 100d })
                {
                    var game = Start(RingTrack(signs)); Finish(game, frame);
                    int plus = 0, minus = 0;
                    foreach (char sign in signs) if (sign == '+') plus++; else minus++;
                    var player = game.Players[0];
                    Check(player.BoostsEarned == plus && player.SlowsEarned == minus, "each close ring triggers once " + signs);
                    Check(player.CompletedBoosts == plus && player.CompletedSlows == minus && player.ActiveEffect == null
                        && player.PendingEffects.Count == 0, "all close impulses fully served " + signs);
                    Near(player.FinishTime.Value, 10 + (minus - plus) * .3, "control table actual time " + signs);
                    Near(game.Elapsed, player.FinishTime.Value, "physical arrival agrees with reported finish " + signs);
                    Check(player.FinishTime == game.Players[1].FinishTime && Equals(game.Winner, "tie"), "exact same-action tie");
                }
            var error = Start(RingTrack("++", collision: true)); Finish(error, 100);
            Check(error.Players[0].Collisions == 1 && error.Players[0].BoostsEarned == 2, "two earned boosts survive unrelated hit");
            Near(error.Players[0].FinishTime.Value, 9.9, "two positive and collision minus .1");
            Near(error.Elapsed, 9.9, "collision plus FIFO physical arrival");

            // Simultaneous contacts use authored array order, with no overwritten impulse.
            var same = RingTrack("--"); same.RiskZones[1].X = same.RiskZones[0].X;
            var simultaneous = Start(same); MoveTo(simultaneous, 90);
            Check(simultaneous.Players[0].ActiveEffect.RingId == "ring-0"
                && simultaneous.Players[0].PendingEffects.Peek().RingId == "ring-1", "stable FIFO for same-time contacts");
        }
        static void NegativeAllowsInput()
        {
            var game = Start(RingTrack("-")); MoveTo(game, 100);
            var player = game.Players[0];
            Check(player.SlowRemaining > 0 && player.BoostRemaining == 0 && player.CurrentSpeed == 110, "negative changes real speed only");
            TapBoth(game); double start = player.Distance;
            RaceSimulation.Advance(game, .1);
            Near(player.Distance - start, 11, "slow movement distance");
            Check(player.Flipping && player.FlipElapsed > 0 && player.Recovering == 0, "slow preserves input and flight");
            Finish(game, .317); Near(player.FinishTime.Value, 10.3, "one full negative loses .3");
        }
        static void RecoveryFreezesQueue()
        {
            var track = RingTrack("++-");
            track.Obstacles = new[] { new Obstacle { Id = "freeze", X = 150, Width = 64, Height = 68, Kind = "floor" } };
            var game = Start(track); MoveTo(game, 130);
            while (game.Players[0].Recovering == 0) RaceSimulation.Advance(game, .001);
            var player = game.Players[0];
            Check(player.PendingEffects.Count == 2, "collision occurs with full pending queue");
            string active = player.ActiveEffect.RingId; double remaining = player.ActiveEffect.Remaining;
            double served = player.ActiveEffect.Elapsed, distance = player.Distance;
            RaceSimulation.Advance(game, .2);
            Check(player.PendingEffects.Count == 2 && player.ActiveEffect.RingId == active, "recovery does not dequeue");
            Check(player.ActiveEffect.Remaining == remaining && player.ActiveEffect.Elapsed == served && player.Distance == distance,
                "recovery freezes active elapsed, remaining and distance");
            Finish(game, 100);
            Near(player.FinishTime.Value, 10.2, "queue preserves complete credit through penalty");
            Near(game.Elapsed, 10.2, "recovery physical finish");
        }
        static Track AfterTrack()
        {
            return new Track(2200, new[] { new Obstacle { Id = "wide", Kind = "floor", X = 600, Width = 200, Height = 110 } })
            {
                RiskZones = new[] { new RiskZone { Id = "after", ObstacleId = "wide", X = 820, Y = 114, ClearX = 821, Placement = "after" } }
            };
        }
        static GameState ReturnAt(double distance)
        {
            var game = Start(AfterTrack()); MoveTo(game, 420); TapBoth(game); MoveTo(game, distance); TapBoth(game); return game;
        }
        static void AfterRingConfirmation()
        {
            var good = ReturnAt(750); MoveTo(good, 900);
            Check(good.Players[0].CollectedRiskIds.Contains("after") && good.Players[0].BoostsEarned == 0
                && good.Players[0].Flipping, "after ring remains pending past clear until landing");
            MoveTo(good, 930); Check(good.Players[0].BoostsEarned == 1 && !good.Players[0].Flipping, "safe completed return confirms");
            Finish(good, .317); Near(good.Players[0].FinishTime.Value, 9.7, "after reward full real credit");
            var early = ReturnAt(720); MoveTo(early, 801);
            Check(early.Players[0].CollectedRiskIds.Contains("after") && early.Players[0].BoostsEarned == 0, "early return contacts ring before trailing-edge hit");
            Finish(early, 100);
            Check(early.Players[0].Collisions == 1 && early.Players[0].FailedRiskIds.Contains("after")
                && early.Players[0].BoostsEarned == 0, "later trailing-edge contact cancels pending after ring");
            Near(early.Players[0].FinishTime.Value, 10.5, "failed return only collision penalty");
            var late = ReturnAt(900); Finish(late, .317);
            Check(late.Players[0].BoostsEarned == 0 && late.Players[0].Collisions == 0, "late return misses ring safely");
            var declined = Start(AfterTrack()); MoveTo(declined, 420); TapBoth(declined); Finish(declined, 100);
            Check(declined.Players[0].BoostsEarned == 0 && declined.Players[0].Collisions == 0, "remaining on safe side declines optional return");
        }
        static void PartialFinish()
        {
            foreach (double frame in new[] { 1d / 60, .317, 100d })
            {
                var boost = Start(RingTrack("++", 130)); Finish(boost, frame);
                double actual = 101d / 220 + 29d / 330;
                Near(boost.Players[0].FinishTime.Value, actual, "partial active boost contributes only served distance");
                Near(boost.Elapsed, actual, "partial boost physical finish");
                Check(boost.Players[0].PendingEffects.Count == 1 && boost.Players[0].CompletedBoosts == 0,
                    "unserved queue is not credited at short custom finish");
                var slow = Start(RingTrack("--", 120)); Finish(slow, frame);
                actual = 80d / 220 + 40d / 110;
                Near(slow.Players[0].FinishTime.Value, actual, "partial slow contributes only served distance");
                Near(slow.Elapsed, actual, "partial slow physical finish");
                Check(slow.Players[0].PendingEffects.Count == 1, "unserved negative not charged");
            }
        }
        static void IndependentPlayersAndExactTie()
        {
            var track = new Track(2200, Array.Empty<Obstacle>())
            { RiskZones = new[] { Ring("low-plus", 400, false), Ring("low-minus", 600, true),
                Ring("high-plus", 1000, false, 14), Ring("high-minus", 1200, true, 14) } };
            var game = Start(track); Check(RaceSimulation.Press(game, 1), "second takes other route"); Finish(game, 100);
            foreach (var player in game.Players)
            {
                Check(player.BoostsEarned == 1 && player.SlowsEarned == 1, "individual rings on independent routes");
                Near(player.FinishTime.Value, 10, "one positive and negative cancel after full service");
            }
            Check(game.Players[0].FinishTime == game.Players[1].FinishTime && Equals(game.Winner, "tie"),
                "same net effects at different positions tie exactly");
            Check(!game.Players[0].CollectedRiskIds.SetEquals(game.Players[1].CollectedRiskIds), "players do not share consumed IDs");
        }
        static GameState GraceGame()
        {
            var rings = new List<RiskZone>();
            for (int i = 0; i < 14; i++) rings.Add(Ring("slow-" + i, 400 + i * 50, true, 14));
            var game = Start(new Track(2200, Array.Empty<Obstacle>()) { RiskZones = rings.ToArray() });
            RaceSimulation.Press(game, 1); return game;
        }
        static void GraceDeadline()
        {
            var large = GraceGame(); Finish(large, 100);
            var small = GraceGame(); Finish(small, 1d / 60);
            Check(large.Phase == "finished" && Equals(large.Winner, 0) && !large.Players[1].FinishTime.HasValue,
                "slow queue may leave opponent unfinished at grace deadline");
            Near(large.Elapsed, 13.5, "hard grace deadline");
            Near(large.Players[1].Distance, small.Players[1].Distance, "loser position independent of long frame");
            Near(large.Players[1].SlowRemaining, small.Players[1].SlowRemaining, "loser active slow same at deadline");
            Check(large.Players[1].PendingEffects.Count == small.Players[1].PendingEffects.Count, "same queue at grace deadline");
        }
        static void PauseAndReset()
        {
            var game = Start(RingTrack("--+")); MoveTo(game, 130);
            var player = game.Players[0];
            Check(player.ActiveEffect != null && player.PendingEffects.Count > 0, "pause fixture has active and queued effects");
            double remaining = player.ActiveEffect.Remaining, elapsed = game.Elapsed, distance = player.Distance;
            int pending = player.PendingEffects.Count;
            RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, -1); RaceSimulation.Advance(game, double.NaN);
            Check(player.ActiveEffect.Remaining == remaining && game.Elapsed == elapsed && player.Distance == distance
                && player.PendingEffects.Count == pending, "caller pause and invalid time freeze effects");
            Finish(game, 100); RaceSimulation.SetCharacter(game, 0, 3); TapBoth(game);
            player = game.Players[0];
            Check(player.CharacterIndex == 3 && player.ActiveEffect == null && player.PendingEffects.Count == 0
                && player.BoostRemaining == 0 && player.SlowRemaining == 0 && player.BoostsEarned == 0 && player.SlowsEarned == 0
                && player.CompletedBoosts == 0 && player.CompletedSlows == 0 && player.NegativeRiskIds.Count == 0
                && player.CollectedRiskIds.Count == 0 && player.SuccessfulRiskIds.Count == 0 && player.FailedRiskIds.Count == 0
                && player.LastSuccess == null && player.LastSlow == null && player.LastHit == null, "rematch clears every effect and event");
        }
        static void OrderedOppositeEffects()
        {
            foreach (bool negativeFirst in new[] { false, true })
            {
                var track = new Track(2200, Array.Empty<Obstacle>())
                { RiskZones = new[] { Ring("first", 100, negativeFirst), Ring("second", 140, !negativeFirst) } };
                var game = Start(track); MoveTo(game, 142);
                var player = game.Players[0];
                Check(player.ActiveEffect.RingId == "first" && player.ActiveEffect.IsNegative == negativeFirst
                    && player.PendingEffects.Count == 1 && player.PendingEffects.Peek().RingId == "second", "opposite effects preserve earn order");
                Near(player.PendingEffects.Peek().Remaining, .6, "queued impulse keeps entire duration");
                RaceSimulation.Advance(game, player.ActiveEffect.Remaining);
                Check(player.ActiveEffect.RingId == "second" && player.PendingEffects.Count == 0, "next effect starts only after full first");
                Near(player.ActiveEffect.Remaining, .6, "transition does not shorten next effect");
                Near(player.CurrentSpeed, negativeFirst ? 330 : 110, "FIFO applies actual next velocity");
                Finish(game, 100); Near(game.Elapsed, 10, "opposite fully served pair nets zero physical time");
            }
        }
        static void IndependentReturnCancellation()
        {
            foreach (double frame in new[] { 1d / 30, 1d / 144, .317, 2.5 })
            {
                var game = EngineV2Tests.PlayRoutes(AfterTrack(), new[] { 420d, 750 }, new[] { 420d, 720 }, frame);
                Check(game.Players[0].SuccessfulRiskIds.Contains("after") && game.Players[0].BoostsEarned == 1
                    && game.Players[0].Collisions == 0, "successful return survives opponent's contact cancellation");
                Check(game.Players[1].CollectedRiskIds.Contains("after") && game.Players[1].FailedRiskIds.Contains("after")
                    && game.Players[1].BoostsEarned == 0 && game.Players[1].Collisions == 1,
                    "opponent collects same ring then cancels only own pending reward");
                Near(game.Players[1].FinishTime.Value - game.Players[0].FinishTime.Value, .8, "independent success/error physical gap");
                Check(Equals(game.Winner, 0), "completed return wins over failed return");
            }
        }
        static void AssertAuthored(Track track)
        {
            foreach (double frame in new[] { 1d / 30, 1d / 144, .317, 2.5 })
            {
                var safe = EngineV2Tests.Play(track, track.SafeRoute, frame);
                var ideal = EngineV2Tests.Play(track, track.RiskRoute, frame);
                var p = ideal.Players[0];
                Check(safe.Players[0].Collisions == 0 && safe.Players[0].BoostsEarned == 0 && safe.Players[0].SlowsEarned == 0,
                    "authored safe route declines both kinds " + track.Id);
                Check(p.Collisions == 0 && p.BoostsEarned == track.PositiveRingCount && p.SlowsEarned == 0,
                    "authored ideal reaches every positive and avoids negatives " + track.Id);
                Near(safe.Players[0].FinishTime.Value, track.Length / 220, "authored baseline");
                Near(p.FinishTime.Value, track.Length / 220 - track.PositiveRingCount * .3, "authored ideal complete credit");
                Near(ideal.Elapsed, p.FinishTime.Value, "authored physical arrival agrees with finish");
                Check(p.CompletedBoosts == track.PositiveRingCount && p.CompletedSlows == 0 && p.ActiveEffect == null
                    && p.PendingEffects.Count == 0, "authored finish never truncates an earned effect");
                Check(Equals(ideal.Winner, "tie") && p.FinishTime == ideal.Players[1].FinishTime, "authored exact tie");
            }
        }
        static void SameConfigurationGeometry(Track off, Track on)
        {
            Check(off.Length == on.Length && off.Obstacles.Length == on.Obstacles.Length && off.PositiveRingCount == on.PositiveRingCount,
                "negative toggle preserves track dimensions and positive count");
            for (int i = 0; i < off.Obstacles.Length; i++)
            {
                var a = off.Obstacles[i]; var b = on.Obstacles[i];
                Check(a.Id == b.Id && a.Kind == b.Kind && a.X == b.X && a.Width == b.Width && a.Height == b.Height
                    && a.GapTop == b.GapTop && a.GapBottom == b.GapBottom, "negative toggle keeps exact obstacle geometry");
            }
            foreach (var a in off.RiskZones)
            {
                RiskZone match = null;
                foreach (var b in on.RiskZones) if (b.Id == a.Id) match = b;
                Check(match != null && !match.IsNegative && a.X == match.X && a.Y == match.Y && a.Radius == match.Radius
                    && a.ClearX == match.ClearX && a.ObstacleId == match.ObstacleId && a.Placement == match.Placement,
                    "negative toggle keeps exact positive-ring geometry");
            }
            Check(off.SafeRoute.Length == on.SafeRoute.Length && off.RiskRoute.Length == on.RiskRoute.Length,
                "configuration keeps route choices");
            for (int i = 0; i < off.SafeRoute.Length; i++) Near(off.SafeRoute[i], on.SafeRoute[i], "same safe input position");
            for (int i = 0; i < off.RiskRoute.Length; i++) Near(off.RiskRoute[i], on.RiskRoute[i], "same ideal input position");
        }
        static void AuthoredRoutes()
        {
            SameConfigurationGeometry(TracksV3.StageOne(false), TracksV3.StageOne(true));
            foreach (bool negative in new[] { false, true })
            {
                var stage = TracksV3.StageOne(negative);
                AssertAuthored(stage);
                Check(stage.RiskRoute.Length == 6 && stage.PositiveRingCount == 2, "stage one has six actions and both positive maneuvers");
                var practice = RaceSimulation.CreateGame("practice", null, negative);
                Check(practice.Track.NegativeEnabled == negative && practice.Track.Id == stage.Id, "practice selects stage and configuration");
                foreach (var original in TracksV3.All)
                {
                    var track = TracksV3.Get(original.Id, negative);
                    if (track.Id != stage.Id)
                    {
                        AssertAuthored(track);
                        int negatives = 0; var ids = new HashSet<string>(); var heights = new HashSet<double>();
                        bool before = false, after = false;
                        foreach (var zone in track.RiskZones)
                        {
                            Check(ids.Add(zone.Id), "authored ring IDs unique within each track");
                            if (zone.IsNegative) negatives++;
                            else if (zone.AfterObstacle) after = true;
                            else before = true;
                        }
                        foreach (var obstacle in track.Obstacles) if (obstacle.Kind != "gate") heights.Add(obstacle.Height);
                        Check(track.Length == 7920 && track.PositiveRingCount == 6 && negatives == (negative ? 2 : 0),
                            "full race keeps 36 seconds, six positives and optional two negatives");
                        Check(before && after && heights.Count == 2, "full track has both ring placements and two stone heights");
                        if (!negative) SameConfigurationGeometry(track, TracksV3.Get(original.Id, true));
                    }
                    Check(RaceSimulation.CreateGame("race", track.Id, negative).Track.NegativeEnabled == negative,
                        "race preserves selected negative configuration");
                }
                if (negative)
                {
                    var route = TracksV3.NegativeRoute(stage.Id);
                    var game = EngineV2Tests.Play(stage, route, .317); var p = game.Players[0];
                    Check(p.Collisions == 0 && p.CompletedBoosts == 2 && p.CompletedSlows == 1 && p.ActiveEffect == null
                        && p.PendingEffects.Count == 0, "intentional stage negative remains recoverable and fully served");
                    Near(game.Elapsed, stage.Length / 220 - .3, "stage two positives and negative actual finish");
                    Track selected = game.Track; TapBoth(game);
                    Check(ReferenceEquals(game.Track, selected) && game.Track.NegativeEnabled, "rematch retains exact selected geometry/configuration");
                }
            }
        }
        static void FullNegativeRoutes()
        {
            Check(TracksV3.All.Length == 3, "exactly three full rhythms after stage-one approval");
            var ids = new HashSet<string>();
            foreach (var item in TracksV3.All)
            {
                Check(ids.Add(item.Id) && !string.IsNullOrEmpty(item.Name) && !string.IsNullOrEmpty(item.Focus), "distinct named rhythms");
                var track = TracksV3.Get(item.Id, true);
                foreach (double frame in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5 })
                {
                    var game = EngineV2Tests.Play(track, TracksV3.NegativeRoute(track.Id), frame);
                    foreach (var player in game.Players)
                    {
                        Check(player.BoostsEarned == 6 && player.SlowsEarned == 2 && player.Collisions == 0,
                            "full authored route reaches all eight rings without collision " + track.Id);
                        Check(player.CompletedBoosts == 6 && player.CompletedSlows == 2 && player.ActiveEffect == null
                            && player.PendingEffects.Count == 0, "all eight authored impulses end before finish " + track.Id);
                        Near(player.FinishTime.Value, 34.8, "six positive and two negative full authored time");
                    }
                    Near(game.Elapsed, 34.8, "all-eight physical arrival is 34.8");
                    Check(game.Players[0].FinishTime == game.Players[1].FinishTime && Equals(game.Winner, "tie"), "all-eight exact tie");
                }
            }
        }
    }
}
