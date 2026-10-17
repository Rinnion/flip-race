using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using FlipRace.Core;

namespace FlipRace.Editor
{
    // The original 13 JS tests, ported without changing their scenarios. This can
    // run either in Unity's editor or the independent Tools/ParityRunner project.
    public static class EngineParityTests
    {
        private static int assertions;
        private static int recoveryStates;
        private static readonly double[] RaceFlips = { 400, 980, 2040, 2500, 3318, 4950, 5410, 6118, 7110 };
        private static readonly double[] PracticeFlips = { 450, 1100, 2050, 2718 };

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Flip Race/Run engine parity tests")]
#endif
        public static void Run()
        {
            var report = RunAll() + "\n" + EngineV2Tests.RunAll() + "\n" + EngineV3Tests.RunAll() + "\n" + EngineV4Tests.RunAll()
                + "\n" + EngineTuneTests.RunAll() + "\n" + EngineControlTests.RunAll() + "\n" + EngineFinishTests.RunAll()
                + "\n" + EngineFlightTests.RunAll();
#if UNITY_EDITOR
            var project = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            var fixture = Path.Combine(project, "Tools", "engine-parity-fixtures.tsv.gz");
            if (File.Exists(fixture)) report += "\n" + CompareFixtures(fixture);
            Directory.CreateDirectory(Path.Combine(project, "Logs"));
            File.WriteAllText(Path.Combine(project, "Logs", "engine-tests.txt"), report);
            UnityEngine.Debug.Log(report);
#else
            Console.WriteLine(report);
#endif
        }

        public static string RunAll()
        {
            assertions = 0;
            recoveryStates = 0;
            var tests = new Action[]
            {
                ReadyAndCountdown, FlipAndDiscardedTaps, SurfacePenalty, AirbornePenalty,
                NarrowGate, ExactTie, WinnerBeforeOtherFinish, TinyFinishRemainder,
                NearlyCompletedFlight, GraceAndRematch, NoHitWitnesses,
                FrameRateIndependence, RecoverableMistakes
            };
            foreach (var test in tests)
            {
                try { test(); }
                catch (Exception error) { throw new Exception("FAIL " + test.Method.Name + ": " + error.Message, error); }
            }
            return "PASS: 13/13 original engine scenarios; " + assertions + " assertions; " + recoveryStates + " sampled recovery states.";
        }

        private static void Equal(object actual, object expected, string message = "")
        {
            assertions++;
            if (!Equals(actual, expected)) throw new Exception(message + " Expected " + (expected ?? "null") + "; got " + (actual ?? "null"));
        }

        private static void True(bool value, string message = "")
        {
            assertions++;
            if (!value) throw new Exception(message.Length > 0 ? message : "Expected true");
        }

        private static void Near(double actual, double expected, double tolerance = 1e-8)
        {
            assertions++;
            if (Math.Abs(actual - expected) >= tolerance) throw new Exception(actual + " should be close to " + expected);
        }

        private static GameState Start(string mode = "race")
        {
            var game = RaceSimulation.CreateGame(mode == "race" ? "legacy" : mode == "practice" ? "legacy-practice" : mode);
            RaceSimulation.Press(game, 0);
            RaceSimulation.Press(game, 1);
            RaceSimulation.Advance(game, 3);
            Equal(game.Phase, "racing");
            return game;
        }

        private static GameState WithTrack(Obstacle[] obstacles, double length = 2000)
        {
            var game = Start();
            game.Track = new Track(length, obstacles);
            return game;
        }

        private static Obstacle Rock(string id, double x, string kind = "floor")
        {
            return new Obstacle { Id = id, X = x, Width = 64, Kind = kind, Height = 68 };
        }

        private static void ReadyAndCountdown()
        {
            var game = RaceSimulation.CreateGame();
            RaceSimulation.Press(game, 0);
            Equal(game.Phase, "ready");
            Equal(game.Players[0].Ready, true);
            RaceSimulation.Press(game, 1);
            Equal(game.Phase, "countdown");
            RaceSimulation.Press(game, 0);
            RaceSimulation.Advance(game, 2.5);
            Equal(game.Phase, "countdown");
            Equal(game.Players[0].Distance, 0d);
            RaceSimulation.Advance(game, .75);
            Equal(game.Phase, "racing");
            Equal(game.Elapsed, .25);
            Equal(game.Players[0].Distance, RaceConfig.Speed * .25);
            Equal(game.Players[0].Y, 1d);
        }

        private static void FlipAndDiscardedTaps()
        {
            var game = Start();
            RaceSimulation.Press(game, 0);
            RaceSimulation.Advance(game, RaceConfig.FlipDuration / 2);
            Equal(game.Players[0].Y, .5);
            Equal(game.Players[0].Distance, RaceConfig.Speed * RaceConfig.FlipDuration / 2);
            RaceSimulation.Press(game, 0);
            RaceSimulation.Advance(game, RaceConfig.FlipDuration / 2);
            Equal(game.Players[0].Flipping, false);
            Equal(game.Players[0].Y, 0d);
            RaceSimulation.Advance(game, .1);
            Equal(game.Players[0].Y, 0d);
            RaceSimulation.Press(game, 0);
            RaceSimulation.Advance(game, RaceConfig.FlipDuration);
            Equal(game.Players[0].Y, 1d);
        }

        private static void SurfacePenalty()
        {
            var obstacle = Rock("rock", 220);
            var game = WithTrack(new[] { obstacle });
            var contactTime = (obstacle.X - RaceConfig.PlayerRadius) / RaceConfig.Speed;
            RaceSimulation.Advance(game, contactTime + .1);
            var player = game.Players[0];
            Near(player.Distance, obstacle.X - RaceConfig.PlayerRadius);
            Near(player.Recovering, .4);
            Equal(player.Collisions, 1);
            Equal(player.Y, 1d);
            Equal(RaceSimulation.Press(game, 0), false);
            RaceSimulation.Advance(game, .2);
            Near(player.Distance, obstacle.X - RaceConfig.PlayerRadius);
            RaceSimulation.Advance(game, .3);
            Near(player.Distance, obstacle.X - RaceConfig.PlayerRadius + .1 * RaceConfig.Speed);
            Equal(player.Flipping, false);
            RaceSimulation.Advance(game, 1);
            Equal(player.Collisions, 1);
        }

        private static void AirbornePenalty()
        {
            var obstacle = Rock("rock", 40);
            var game = WithTrack(new[] { obstacle });
            RaceSimulation.Press(game, 0);
            var contactTime = (obstacle.X - RaceConfig.PlayerRadius) / RaceConfig.Speed;
            RaceSimulation.Advance(game, contactTime + .1);
            var player = game.Players[0];
            var frozenY = 1 - contactTime / RaceConfig.FlipDuration;
            Near(player.Y, frozenY);
            Near(player.FlipElapsed, contactTime);
            Equal(player.Flipping, true);
            Equal(RaceSimulation.Press(game, 0), false);
            RaceSimulation.Advance(game, .4);
            Near(player.Y, frozenY);
            RaceSimulation.Advance(game, RaceConfig.FlipDuration - contactTime);
            Equal(player.Y, 0d);
            Equal(player.Flipping, false);
            Near(player.Distance, RaceConfig.FlipDuration * RaceConfig.Speed);
            Equal(player.Collisions, 1);
        }

        private static void NarrowGate()
        {
            var game = WithTrack(new[] { new Obstacle { Id = "gate", X = 200, Width = 12, Kind = "gate", GapTop = 48, GapBottom = 192 } });
            RaceSimulation.Advance(game, 4);
            Equal(game.Players[0].Collisions, 1);
            Near(game.Players[0].Distance, (4 - RaceConfig.RecoveryDuration) * RaceConfig.Speed);
            Equal(game.Players[0].HitObstacleIds.Count, 1);
        }

        private static void ExactTie()
        {
            var game = WithTrack(new[] { Rock("early-floor", 220), Rock("late-ceiling", 600, "ceiling") }, 1000);
            RaceSimulation.Press(game, 1);
            RaceSimulation.Advance(game, 20);
            Equal(game.Players[0].Collisions, 1);
            Equal(game.Players[1].Collisions, 1);
            Equal(game.Players[0].FinishTime, game.Players[1].FinishTime);
            Equal(game.Players[0].FinishTime, 1000 / RaceConfig.Speed + RaceConfig.RecoveryDuration);
            Equal(game.Winner, "tie");
            Equal(game.Phase, "finished");
        }

        private static void WinnerBeforeOtherFinish()
        {
            var game = WithTrack(new[] { Rock("rock", 220) }, 1000);
            RaceSimulation.Press(game, 0);
            RaceSimulation.Advance(game, 1000 / RaceConfig.Speed);
            Equal(game.Winner, 0);
            Equal(game.Phase, "racing");
            Equal(game.Players[0].Distance, 1000d);
            Equal(game.Players[1].FinishTime, null);
            RaceSimulation.Advance(game, RaceConfig.RecoveryDuration);
            Equal(game.Phase, "finished");
            Equal(game.Winner, 0);
            Equal(game.Players[1].FinishTime, 1000 / RaceConfig.Speed + RaceConfig.RecoveryDuration);
        }

        private static void TinyFinishRemainder()
        {
            var game = WithTrack(Array.Empty<Obstacle>(), 1000);
            RaceSimulation.Advance(game, 1000 / RaceConfig.Speed - 5e-10);
            RaceSimulation.Advance(game, 1d / 60);
            Equal(game.Phase, "finished");
            Equal(game.Winner, "tie");
            foreach (var player in game.Players)
            {
                Equal(player.Distance, 1000d);
                Equal(player.FinishTime, 1000 / RaceConfig.Speed);
            }
        }

        private static void NearlyCompletedFlight()
        {
            var game = WithTrack(Array.Empty<Obstacle>());
            RaceSimulation.Press(game, 0);
            RaceSimulation.Advance(game, RaceConfig.FlipDuration - 5e-10);
            RaceSimulation.Advance(game, 1d / 60);
            Equal(game.Players[0].Flipping, false);
            Equal(game.Players[0].Y, 0d);
            Equal(RaceSimulation.Press(game, 0), true);
            RaceSimulation.Advance(game, RaceConfig.FlipDuration);
            Equal(game.Players[0].Y, 1d);
        }

        private static void GraceAndRematch()
        {
            var obstacles = new Obstacle[9];
            for (var index = 0; index < obstacles.Length; index++) obstacles[index] = Rock("rock-" + index, 220 + index * 180);
            var game = WithTrack(obstacles, 2000);
            RaceSimulation.Press(game, 0);
            RaceSimulation.Advance(game, 100);
            Equal(game.Phase, "finished");
            Equal(game.Winner, 0);
            Equal(game.Players[1].FinishTime, null);
            Near(game.Elapsed, 2000 / RaceConfig.Speed + RaceConfig.FinishGrace);
            Equal(game.Players[0].Ready, false);
            Equal(game.Players[1].Ready, false);
            RaceSimulation.Press(game, 1);
            Equal(game.Phase, "finished");
            Equal(game.Players[1].Ready, true);
            RaceSimulation.Press(game, 0);
            Equal(game.Phase, "countdown");
            Equal(game.Winner, null);
            Equal(game.Elapsed, 0d);
            Equal(game.Players[1].Collisions, 0);
            Equal(game.Players[1].HitObstacleIds.Count, 0);
            Equal(game.Players[1].Distance, 0d);
        }

        private static GameState PlayScript(string mode, double[] distances, double frameDuration = double.PositiveInfinity, double timingOffset = 0)
        {
            var game = Start(mode);
            double scriptTime = 0;
            foreach (var distance in distances)
            {
                var nextTime = distance / RaceConfig.Speed + timingOffset;
                while (nextTime - scriptTime > 1e-8)
                {
                    var step = Math.Min(frameDuration, nextTime - scriptTime);
                    RaceSimulation.Advance(game, step);
                    scriptTime += step;
                }
                RaceSimulation.Press(game, 0);
                RaceSimulation.Press(game, 1);
            }
            var guard = 0;
            while (game.Phase == "racing")
            {
                RaceSimulation.Advance(game, Math.Min(frameDuration, 1));
                if (++guard > 100000) throw new Exception("Simulation stalled");
            }
            return game;
        }

        private static void NoHitWitnesses()
        {
            True(Tracks.LegacyRace.Length / RaceConfig.Speed >= 30 && Tracks.LegacyRace.Length / RaceConfig.Speed <= 45);
            True(Tracks.LegacyPractice.Length < Tracks.LegacyRace.Length);
            foreach (var mode in new[] { "race", "practice" })
                foreach (var jitter in new[] { -.08, 0, .08 })
                {
                    var game = PlayScript(mode, mode == "race" ? RaceFlips : PracticeFlips, double.PositiveInfinity, jitter);
                    foreach (var player in game.Players)
                    {
                        Equal(player.Collisions, 0, mode + ", jitter " + jitter + ": " + player.LastHit?.Id);
                        Equal(player.FinishTime, game.Track.Length / RaceConfig.Speed);
                    }
                    Equal(game.Winner, "tie");
                }
        }

        private static void FrameRateIndependence()
        {
            var flips = new double[RaceFlips.Length - 2];
            Array.Copy(RaceFlips, 2, flips, 0, flips.Length);
            foreach (var frameDuration in new[] { 1d / 30, 1d / 60, 1d / 144, .317, double.PositiveInfinity })
            {
                var game = PlayScript("race", flips, frameDuration, RaceConfig.RecoveryDuration);
                Equal(game.Winner, "tie");
                Equal(game.Players[0].FinishTime, game.Players[1].FinishTime);
                Equal(game.Players[0].FinishTime, Tracks.LegacyRace.Length / RaceConfig.Speed + game.Players[0].Collisions * RaceConfig.RecoveryDuration);
                Equal(game.Players[0].Collisions, 1);
            }
        }

        private static void RecoverableMistakes()
        {
            var offsets = new[] { -210d, -175, -140, -105, -70, -35, 0, 35, 70, double.PositiveInfinity };
            foreach (var track in new[] { Tracks.LegacyRace, Tracks.LegacyPractice })
                for (var index = 0; index < track.Obstacles.Length - 1; index++)
                {
                    var obstacle = track.Obstacles[index];
                    var next = track.Obstacles[index + 1];
                    foreach (var initialSide in new[] { 0, 1 })
                        foreach (var offset in offsets)
                        {
                            var game = WithTrack(new[] { obstacle, next }, next.X + next.Width + 700);
                            if (initialSide == 0) RaceSimulation.Press(game, 0);
                            RaceSimulation.Advance(game, (obstacle.X - 400) / RaceConfig.Speed);
                            var tapped = false;
                            while (game.Players[0].LastHit == null && game.Players[0].Distance < obstacle.X + obstacle.Width + 16)
                            {
                                if (!tapped && game.Players[0].Distance >= obstacle.X + offset)
                                {
                                    RaceSimulation.Press(game, 0);
                                    tapped = true;
                                }
                                RaceSimulation.Advance(game, .01);
                            }
                            var player = game.Players[0];
                            if (player.LastHit?.Id != obstacle.Id) continue;
                            recoveryStates++;
                            RaceSimulation.Advance(game, player.Recovering + (player.Flipping ? RaceConfig.FlipDuration - player.FlipElapsed : 0) + .15);
                            Equal(player.Collisions, 1, obstacle.Id + " already forced a second hit");
                            if (next.Kind == "gate")
                            {
                                var idealStart = next.X + next.Width / 2 - RaceConfig.Speed * RaceConfig.FlipDuration / 2;
                                True(player.Distance < idealStart, obstacle.Id + ": gate decision is still ahead");
                                RaceSimulation.Advance(game, (idealStart - player.Distance) / RaceConfig.Speed);
                                Equal(RaceSimulation.Press(game, 0), true);
                            }
                            else if (player.Y == (next.Kind == "floor" ? 1 : 0)) Equal(RaceSimulation.Press(game, 0), true);
                            RaceSimulation.Advance(game, (next.X + next.Width + 30 - player.Distance) / RaceConfig.Speed);
                            Equal(player.Collisions, 1, obstacle.Id + " -> " + next.Id + ", side " + initialSide + ", tap " + offset + ": " + player.LastHit?.Message);
                        }
                }
            True(recoveryStates >= 100, "Only " + recoveryStates + " recovery states checked");
        }

        private static string Value(object value)
        {
            if (value == null) return "null";
            if (value is bool) return (bool)value ? "true" : "false";
            if (value is double) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static List<string> Snapshot(GameState game, object accepted)
        {
            var result = new List<string>
            {
                Value(accepted), game.Phase, Value(game.Elapsed), Value(game.CountdownRemaining), Value(game.Winner), Value(game.WinnerAt)
            };
            foreach (var player in game.Players)
            {
                var hits = new List<string>(player.HitObstacleIds);
                hits.Sort(StringComparer.Ordinal);
                result.AddRange(new[]
                {
                    Value(player.Id), Value(player.Ready), Value(player.Distance), Value(player.Y), Value(player.Flipping),
                    Value(player.FlipFrom), Value(player.FlipTo), Value(player.FlipElapsed), Value(player.Recovering),
                    Value(player.Collisions), Value(player.FinishTime), string.Join(",", hits),
                    Value(player.LastHit?.Id), Value(player.LastHit?.Message), Value(player.LastHit?.At)
                });
            }
            return result;
        }

        public static string CompareFixtures(string fixturePath)
        {
            GameState game = null;
            var lines = 0;
            var states = 0;
            var comparedFields = 0;
            foreach (var line in ReadFixtureLines(fixturePath))
            {
                lines++;
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                var columns = line.Split('\t');
                object accepted = null;
                if (columns[0] == "new") game = RaceSimulation.CreateGame(columns[1] == "race" ? "legacy" : "legacy-practice");
                else if (columns[0] == "press") accepted = RaceSimulation.Press(game, int.Parse(columns[1], CultureInfo.InvariantCulture));
                else if (columns[0] == "advance") RaceSimulation.Advance(game, double.Parse(columns[1], CultureInfo.InvariantCulture));
                else throw new Exception("Unknown fixture operation on line " + lines);
                var actual = Snapshot(game, accepted);
                if (columns.Length != actual.Count + 2) throw new Exception("Malformed fixture on line " + lines);
                for (var index = 0; index < actual.Count; index++)
                {
                    var expected = columns[index + 2];
                    var observed = actual[index];
                    comparedFields++;
                    if (observed == expected) continue;
                    if (double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
                        && double.TryParse(observed, NumberStyles.Float, CultureInfo.InvariantCulture, out var observedNumber)
                        && Math.Abs(expectedNumber - observedNumber) < 1e-8) continue;
                    throw new Exception("JS/C# mismatch at fixture line " + lines + ", field " + index + ": expected " + expected + ", got " + observed);
                }
                states++;
            }
            return "PASS: JS/C# differential comparison, " + states + " snapshots / " + comparedFields + " state fields (numeric tolerance < 1e-8).";
        }

        private static IEnumerable<string> ReadFixtureLines(string path)
        {
            using (var file = File.OpenRead(path))
            using (Stream input = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? (Stream)new GZipStream(file, CompressionMode.Decompress) : file)
            using (var reader = new StreamReader(input))
            {
                string line;
                while ((line = reader.ReadLine()) != null) yield return line;
            }
        }
    }
}

