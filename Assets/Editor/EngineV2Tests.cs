using System;
using System.Collections.Generic;
using FlipRace.Core;

namespace FlipRace.Editor
{
    public static class EngineV2Tests
    {
        static int assertions;
        static int recoveryStates;
        public static string RunAll()
        {
            assertions = 0;
            recoveryStates = 0;
            Action[] tests = { SingleSafeAndSuccess, RingThenHitCancels, FrameRateParity, RematchAndCharacters, PartialBoostArrival,
                ThreeTracksAndMargins, FullRiskResults, DifferentRoutesTie, RecoverableMistakes, CharacterParity,
                AsymmetricRiskAndError, BoostedGraceDeadline };
            foreach (var test in tests)
                try { test(); }
                catch (Exception error) { throw new Exception("V2 FAIL " + test.Method.Name + ": " + error.Message, error); }
            return "PASS: V2 " + tests.Length + " scenarios / " + assertions + " assertions / " + recoveryStates
                + " sampled recovery states; single risk, 3 tracks, real boosts, exact ties, grace and characters.";
        }
        static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        static void Near(double a, double b, string message, double tolerance = 1e-8)
        { Check(Math.Abs(a - b) < tolerance, message + ": " + a + " vs " + b); }
        static GameState Start(Track track, int firstCharacter = 0, int secondCharacter = 1)
        {
            var game = RaceSimulation.CreateGame("practice"); game.Track = track;
            RaceSimulation.SetCharacter(game, 0, firstCharacter); RaceSimulation.SetCharacter(game, 1, secondCharacter);
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            return game;
        }
        static void Tap(GameState game) { Check(RaceSimulation.Press(game, 0), "player 0 tap accepted"); Check(RaceSimulation.Press(game, 1), "player 1 tap accepted"); }
        static void MoveTo(GameState game, double target, double frame = 1d / 60)
        {
            int guard = 0;
            while (game.Players[0].Distance < target - 1e-7 && game.Phase == "racing")
            {
                double dt = Math.Min(frame, (target - game.Players[0].Distance) / (RaceConfig.Speed * RaceConfig.BoostMultiplier));
                RaceSimulation.Advance(game, dt);
                if (++guard > 100000) throw new Exception("MoveTo stalled");
            }
        }
        public static GameState Play(Track track, double[] route, double frame = 1d / 60)
        {
            var game = Start(track);
            foreach (double distance in route) { MoveTo(game, distance, frame); Tap(game); }
            int guard = 0;
            while (game.Phase == "racing")
            {
                RaceSimulation.Advance(game, Math.Min(frame, 1));
                if (++guard > 100000) throw new Exception("finish stalled");
            }
            return game;
        }
        static void SingleSafeAndSuccess()
        {
            var track = Tracks.RiskPractice;
            var safe = Play(track, track.SafeRoute);
            var risky = Play(track, track.RiskRoute);
            Check(safe.Players[0].Collisions == 0 && safe.Players[0].BoostsEarned == 0, "early route safe and ring optional");
            Check(risky.Players[0].Collisions == 0 && risky.Players[0].BoostsEarned == 1, "later route gets reward without collision");
            Near(safe.Players[0].FinishTime.Value - risky.Players[0].FinishTime.Value, .3, "full effect real gain");
            Near(risky.Elapsed, risky.Players[0].FinishTime.Value, "game ends at actual finish, not adjusted display time");
            Check(risky.Players[0].FinishTime == risky.Players[1].FinishTime && Equals(risky.Winner, "tie"), "identical risk actions exact tie");
            var game = Start(track); MoveTo(game, 505); Tap(game); MoveTo(game, 575);
            Check(game.Players[0].CollectedRiskIds.Count == 1 && game.Players[0].BoostsEarned == 0, "ring pending before clearing linked rock");
            MoveTo(game, 678); Check(game.Players[0].BoostsEarned == 0, "no premature reward at trailing edge");
            MoveTo(game, 680); Check(game.Players[0].BoostRemaining > 0 && game.Players[0].BoostsEarned == 1, "clear starts real speed boost");
            double before = game.Players[0].Distance;
            RaceSimulation.Advance(game, .1);
            Near(game.Players[0].Distance - before, 33, "boost changes actual movement speed");
            MoveTo(game, 900); Check(game.Players[0].BoostRemaining == 0, "boost fully ends before next obstacle");
        }
        static void RingThenHitCancels()
        {
            var track = Tracks.RiskPractice;
            var game = Start(track); MoveTo(game, 540); Tap(game); MoveTo(game, 575);
            Check(game.Players[0].CollectedRiskIds.Count == 1, "late action touches ring before collision");
            RaceSimulation.Advance(game, 0);
            Check(game.Players[0].CollectedRiskIds.Count == 1 && game.Players[0].BoostsEarned == 0, "pause preserves pending ring");
            RaceSimulation.Advance(game, .1);
            var recovering = game.Players[0];
            Check(recovering.Recovering > 0, "late action is recovering");
            double recovery = recovering.Recovering, frozenY = recovering.Y, frozenX = recovering.Distance;
            RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, -1); RaceSimulation.Advance(game, double.PositiveInfinity);
            Check(recovering.Recovering == recovery && recovering.Y == frozenY && recovering.Distance == frozenX, "pause and invalid steps preserve recovery");
            MoveTo(game, 900);
            Check(game.Players[0].Collisions == 1 && game.Players[0].FailedRiskIds.Count == 1, "linked hit cancels pending ring");
            Check(game.Players[0].BoostsEarned == 0 && game.Players[0].BoostRemaining == 0, "failed maneuver no reward");
            MoveTo(game, 1320); Tap(game); RaceSimulation.Advance(game, 100);
            Near(game.Players[0].FinishTime.Value, track.Length / RaceConfig.Speed + .5, "one error costs .5");
            Check(game.Players[0].Collisions == 1, "recovery does not force next collision");
        }
        static void FrameRateParity()
        {
            foreach (double dt in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5 })
            {
                var safe = Play(Tracks.RiskPractice, Tracks.RiskPractice.SafeRoute, dt);
                var risk = Play(Tracks.RiskPractice, Tracks.RiskPractice.RiskRoute, dt);
                var failed = Play(Tracks.RiskPractice, new[] { 540d, 1320 }, dt);
                Near(safe.Players[0].FinishTime.Value, Tracks.RiskPractice.Length / RaceConfig.Speed, "safe fps " + dt);
                Near(risk.Players[0].FinishTime.Value, Tracks.RiskPractice.Length / RaceConfig.Speed - .3, "risk fps " + dt);
                Near(failed.Players[0].FinishTime.Value, Tracks.RiskPractice.Length / RaceConfig.Speed + .5, "error fps " + dt);
                Check(risk.Players[0].SuccessfulRiskIds.Count == 1 && risk.Players[1].SuccessfulRiskIds.Count == 1, "rings independent, once each");
            }
        }
        static void RematchAndCharacters()
        {
            var game = RaceSimulation.CreateGame("practice", "risk-practice");
            Check(RaceSimulation.SetCharacter(game, 0, 3), "select character");
            RaceSimulation.Press(game, 0);
            Check(RaceSimulation.SetCharacter(game, 0, 2) && !game.Players[0].Ready, "selection resets own ready");
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            Check(game.Players[0].CharacterIndex == 2 && !RaceSimulation.SetCharacter(game, 0, 1), "character preserved and locked during race");
            MoveTo(game, 505); Tap(game); MoveTo(game, 700);
            double distance = game.Players[0].Distance, boost = game.Players[0].BoostRemaining;
            // Pause is caller-owned: not advancing cannot change effect timers.
            RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, double.NaN);
            Check(game.Players[0].Distance == distance && game.Players[0].BoostRemaining == boost, "pause preserves boost");
            MoveTo(game, 1320); Tap(game); RaceSimulation.Advance(game, 100);
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1);
            Check(game.Players[0].CharacterIndex == 2, "rematch keeps character");
            Check(game.Players[0].BoostRemaining == 0 && game.Players[0].BoostsEarned == 0
                && game.Players[0].CollectedRiskIds.Count == 0 && game.Players[0].SuccessfulRiskIds.Count == 0
                && game.Players[0].FailedRiskIds.Count == 0 && game.Players[0].LastSuccess == null, "rematch clears all risk state");
        }
        static void PartialBoostArrival()
        {
            var source = Tracks.RiskPractice;
            var track = new Track(730, new[] { source.Obstacles[0] }) { RiskZones = source.RiskZones };
            var game = Play(track, new[] { 505d }, .317);
            double partialDuration = (730 - source.RiskZones[0].ClearX) / (RaceConfig.Speed * RaceConfig.BoostMultiplier);
            Near(game.Players[0].FinishTime.Value, source.RiskZones[0].ClearX / RaceConfig.Speed + partialDuration, "actual crossing during partial boost");
            Check(game.Players[0].FinishTime.Value > track.Length / RaceConfig.Speed - .3, "unserved reward is not subtracted on results");
        }

        public static GameState PlayRoutes(Track track, double[] first, double[] second, double frame = 1d / 60,
            int firstCharacter = 0, int secondCharacter = 1)
        {
            var game = Start(track, firstCharacter, secondCharacter);
            double[][] routes = { first, second };
            int[] next = { 0, 0 };
            int guard = 0;
            while (game.Phase == "racing")
            {
                for (int i = 0; i < 2; i++)
                    if (next[i] < routes[i].Length && game.Players[i].Distance >= routes[i][next[i]] - 1e-7)
                    {
                        Check(RaceSimulation.Press(game, i), "route action accepted for player " + i);
                        next[i]++;
                    }
                double dt = Math.Min(frame, 1);
                for (int i = 0; i < 2; i++)
                    if (next[i] < routes[i].Length)
                        dt = Math.Min(dt, (routes[i][next[i]] - game.Players[i].Distance) / (RaceConfig.Speed * RaceConfig.BoostMultiplier));
                RaceSimulation.Advance(game, dt);
                if (++guard > 200000) throw new Exception("two-route playback stalled");
            }
            return game;
        }

        static double[] Shift(double[] route, double offset)
        {
            var result = new double[route.Length];
            for (int i = 0; i < result.Length; i++) result[i] = route[i] + offset;
            return result;
        }
        static double[] RiskErrors(Track track, int failedZone)
        {
            var route = (double[])track.RiskRoute.Clone();
            var zone = track.RiskZones[failedZone];
            for (int i = 0; i < route.Length; i++) if (route[i] == zone.RiskFlipX) route[i] += 35;
            return route;
        }
        static void ThreeTracksAndMargins()
        {
            Check(Tracks.All.Length == 3, "exactly three race tracks");
            var ids = new HashSet<string>();
            foreach (var track in Tracks.All)
            {
                Check(ids.Add(track.Id) && !string.IsNullOrEmpty(track.Name) && !string.IsNullOrEmpty(track.Focus), "distinct named track");
                Check(track.RiskZones.Length == 3, "exactly three optional risks on " + track.Id);
                Check(ReferenceEquals(RaceSimulation.CreateGame("race", track.Id).Track, track), "select track by id");
                foreach (double jitter in new[] { -.08, 0, .08 })
                {
                    var safe = Play(track, Shift(track.SafeRoute, jitter * RaceConfig.Speed));
                    Check(safe.Players[0].Collisions == 0 && safe.Players[0].BoostsEarned == 0, "safe ±80ms " + track.Id + " jitter " + jitter);
                    Near(safe.Players[0].FinishTime.Value, track.Length / RaceConfig.Speed, "baseline safe duration");
                }
                foreach (double jitter in new[] { -.04, 0, .04 })
                {
                    var precise = Play(track, Shift(track.RiskRoute, jitter * RaceConfig.Speed));
                    Check(precise.Players[0].Collisions == 0 && precise.Players[0].BoostsEarned == 3,
                        "centered risk guide ±40ms " + track.Id + " jitter " + jitter);
                }
                foreach (var zone in track.RiskZones)
                {
                    Obstacle linked = null;
                    foreach (var obstacle in track.Obstacles) if (obstacle.Id == zone.ObstacleId) linked = obstacle;
                    Check(linked != null && zone.ClearX == linked.X + linked.Width + RaceConfig.PlayerRadius + 1,
                        "clear boundary matches linked obstacle geometry " + zone.Id);
                    double end = zone.ClearX + RaceConfig.Speed * RaceConfig.BoostMultiplier * RaceConfig.BoostDuration;
                    Check(end < track.Length - 200, "full boost before finish " + zone.Id);
                    foreach (var obstacle in track.Obstacles)
                        if (obstacle.X > zone.ClearX)
                        {
                            Check(end + RaceConfig.Speed * .15 < obstacle.X - 180, "boost ends with reaction margin before next combination " + zone.Id);
                            break;
                        }
                }
            }
        }
        static void FullRiskResults()
        {
            foreach (var track in Tracks.All)
                foreach (double dt in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5 })
                {
                    var all = Play(track, track.RiskRoute, dt);
                    Check(all.Players[0].BoostsEarned == 3 && all.Players[0].Collisions == 0, "three successes " + track.Id);
                    Near(all.Players[0].FinishTime.Value, track.Length / RaceConfig.Speed - .9, "three successes save .9");
                    Check(all.Players[0].FinishTime == all.Players[1].FinishTime && Equals(all.Winner, "tie"), "full exact tie " + dt);
                    Near(all.Elapsed, all.Players[0].FinishTime.Value, "actual finish after all three boosts");
                    for (int failed = 0; failed < 3; failed++)
                    {
                        var mixed = Play(track, RiskErrors(track, failed), dt);
                        Check(mixed.Players[0].BoostsEarned == 2 && mixed.Players[0].Collisions == 1, "two successes and one error " + track.Id);
                        Near(mixed.Players[0].FinishTime.Value, track.Length / RaceConfig.Speed - .1, "two successes plus error save .1");
                        Check(mixed.Players[0].SuccessfulRiskIds.Count == 2 && mixed.Players[0].FailedRiskIds.Count == 1, "earlier rewards survive later error");
                    }
                }
            var oneSuccess = (double[])Tracks.Hold.SafeRoute.Clone();
            for (int i = 0; i < oneSuccess.Length; i++)
                if (oneSuccess[i] == Tracks.Hold.RiskZones[0].SafeFlipX) oneSuccess[i] = Tracks.Hold.RiskZones[0].RiskFlipX;
                else if (oneSuccess[i] == Tracks.Hold.RiskZones[1].SafeFlipX) oneSuccess[i] = Tracks.Hold.RiskZones[1].RiskFlipX + 35;
            var plus = Play(Tracks.Hold, oneSuccess);
            Near(plus.Players[0].FinishTime.Value, Tracks.Hold.Length / RaceConfig.Speed + .2, "one success and error costs .2");
        }
        static void DifferentRoutesTie()
        {
            foreach (var track in Tracks.All)
            {
                var both = PlayRoutes(track, RiskErrors(track, 0), RiskErrors(track, 2), .317);
                Check(both.Players[0].FinishTime == both.Players[1].FinishTime && Equals(both.Winner, "tie"), "same outcomes at different locations tie exactly");
                var duel = PlayRoutes(track, track.SafeRoute, track.RiskRoute, .317);
                Check(Equals(duel.Winner, 1), "risky player really wins over safe route");
                Near(duel.Players[0].FinishTime.Value - duel.Players[1].FinishTime.Value, .9, "asymmetric risk advantage");
                for (int i = 0; i < 2; i++) Check(both.Players[i].SuccessfulRiskIds.Count == 2, "each player owns independent rings");
            }
        }
        static void RecoverableMistakes()
        {
            int checkedStates = 0;
            double[] offsets = { -210, -175, -140, -105, -70, -35, 0, 35, 70, double.PositiveInfinity };
            foreach (var track in Tracks.All)
                for (int index = 0; index < track.Obstacles.Length - 1; index++)
                {
                    var obstacle = track.Obstacles[index]; var next = track.Obstacles[index + 1];
                    var zones = new List<RiskZone>();
                    foreach (var zone in track.RiskZones) if (zone.ObstacleId == obstacle.Id || zone.ObstacleId == next.Id) zones.Add(zone);
                    foreach (int side in new[] { 0, 1 })
                        foreach (double offset in offsets)
                        {
                            var pair = new Track(next.X + next.Width + 700, new[] { obstacle, next }) { RiskZones = zones.ToArray() };
                            var game = Start(pair);
                            if (side == 0) RaceSimulation.Press(game, 0);
                            RaceSimulation.Advance(game, (obstacle.X - 400) / RaceConfig.Speed);
                            bool tapped = false;
                            while (game.Players[0].LastHit == null && game.Players[0].Distance < obstacle.X + obstacle.Width + 16)
                            {
                                if (!tapped && game.Players[0].Distance >= obstacle.X + offset)
                                { RaceSimulation.Press(game, 0); tapped = true; }
                                RaceSimulation.Advance(game, .01);
                            }
                            var player = game.Players[0];
                            if (player.LastHit?.Id != obstacle.Id) continue;
                            checkedStates++;
                            RaceSimulation.Advance(game, player.Recovering + (player.Flipping ? RaceConfig.FlipDuration - player.FlipElapsed : 0) + .15);
                            Check(player.Collisions == 1, "error does not force immediate second hit " + obstacle.Id);
                            if (next.Kind == "gate")
                            {
                                double idealStart = next.X + next.Width / 2 - RaceConfig.Speed * RaceConfig.FlipDuration / 2;
                                Check(player.Distance < idealStart, "gate choice remains ahead " + obstacle.Id);
                                RaceSimulation.Advance(game, (idealStart - player.Distance) / RaceConfig.Speed);
                                Check(RaceSimulation.Press(game, 0), "gate recovery action accepted");
                            }
                            else if (player.Y == (next.Kind == "floor" ? 1 : 0)) Check(RaceSimulation.Press(game, 0), "recovery action accepted");
                            RaceSimulation.Advance(game, (next.X + next.Width + 30 - player.Distance) / RaceConfig.Speed);
                            Check(player.Collisions == 1, "no forced chain " + obstacle.Id + " -> " + next.Id + " side " + side + " offset " + offset);
                        }
                }
            Check(checkedStates >= 300, "at least 300 v2 recovery states sampled; actual " + checkedStates);
            recoveryStates = checkedStates;
        }
        static void CharacterParity()
        {
            foreach (int character in new[] { 0, 1, 2, 3 })
            {
                var game = PlayRoutes(Tracks.RiskPractice, Tracks.RiskPractice.RiskRoute, Tracks.RiskPractice.RiskRoute,
                    .317, character, character);
                Check(game.Players[0].CharacterIndex == character && game.Players[1].CharacterIndex == character, "same character allowed");
                Near(game.Players[0].FinishTime.Value, Tracks.RiskPractice.Length / RaceConfig.Speed - .3, "all characters same rules");
                Check(Equals(game.Winner, "tie"), "same character exact tie");
            }
        }
        static void AsymmetricRiskAndError()
        {
            var game = PlayRoutes(Tracks.RiskPractice, Tracks.RiskPractice.RiskRoute, new[] { 540d, 1320 }, .317);
            Check(Equals(game.Winner, 0), "success beats error");
            Near(game.Players[1].FinishTime.Value - game.Players[0].FinishTime.Value, .8, "success versus error difference .8");
            Check(game.Players[0].SuccessfulRiskIds.Count == 1 && game.Players[1].SuccessfulRiskIds.Count == 0,
                "other player's failure does not cancel successful reward");
        }
        static GameState LeadingPlayerBeforeFinish()
        {
            var game = Start(Tracks.Hold);
            foreach (double target in Tracks.Hold.RiskRoute)
            {
                MoveTo(game, target);
                Check(RaceSimulation.Press(game, 0), "leading player's route accepted");
            }
            return game;
        }
        static void BoostedGraceDeadline()
        {
            var large = LeadingPlayerBeforeFinish();
            RaceSimulation.Advance(large, 100);
            var fine = LeadingPlayerBeforeFinish();
            while (fine.Phase == "racing") RaceSimulation.Advance(fine, 1d / 60);
            Check(large.Phase == "finished" && Equals(large.Winner, 0) && !large.Players[1].FinishTime.HasValue,
                "boost winner stops unfinished opponent after grace");
            Near(large.Elapsed, large.WinnerAt.Value + RaceConfig.FinishGrace, "large step respects exact grace deadline");
            Near(large.Elapsed, fine.Elapsed, "grace independent of frame size");
            Near(large.Players[1].Distance, fine.Players[1].Distance, "large step does not advance loser past deadline");
            Near(large.Players[1].Recovering, fine.Players[1].Recovering, "same remaining penalty at deadline");
        }
    }
}
