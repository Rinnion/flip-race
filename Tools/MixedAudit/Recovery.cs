using System;
using System.Collections.Generic;
using System.Linq;
using FlipRace.Core;

namespace FlipRace.MixedAudit
{
    public static partial class StageAudit
    {
        static bool UntilTimeOrHit(GameState game, double at)
        {
            while (game.Phase == "racing" && game.Elapsed < at - 1e-9 && game.Players[0].Collisions == 0)
                RaceSimulation.Advance(game, Math.Min(.025, at - game.Elapsed));
            return game.Players[0].Collisions != 0;
        }

        static GameState FirstHit(string id, FlightSettings flight, double[] route, int changed, double shift)
        {
            var game = Adapter.Start(TracksMixedStage.Get(id), flight);
            for (int i = 0; i < route.Length; i++)
            {
                double time = route[i] / 220 + (i == changed ? shift : 0);
                if (time < game.Elapsed - 1e-9) return null;
                if (UntilTimeOrHit(game, time)) return game;
                // A rejected mistimed input is retained as part of the real
                // mistake; no state is repaired and no remaining route is used
                // once the first physical collision is observed.
                RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1);
            }
            return UntilTimeOrHit(game, 14) ? game : null;
        }

        static void WaitActualWall(GameState game, double deadlineX)
        {
            int guard = 0;
            while (game.Phase == "racing" && game.Players[0].Support != SupportKind.Wall)
            {
                Check(game.Players[0].Distance < deadlineX, "real wall arrives before next obstacle front");
                RaceSimulation.Advance(game, .005);
                if (++guard > 2000) throw new InvalidOperationException("No real wall reached during recovery policy.");
            }
        }

        static void CrossFromWall(GameState game, FlightSettings flight)
        {
            Check(game.Players[0].Support == SupportKind.Wall && game.Players[0].AirFlipAvailable, "recovery departure has actual wall/recharge");
            double start = game.Elapsed; Tap(game);
            if (flight.FirstAction == FirstAction.Jump)
            {
                Adapter.AdvanceTo(game, start + .2, .025);
                Check(game.Players[0].Support == SupportKind.None, "recovery Main repeat airborne"); Tap(game);
            }
        }

        public static string Recovery()
        {
            checks = games = 0; int candidates = 0, witnesses = 0;
            var rows = new List<string>();
            foreach (var track in TracksMixedStage.All) foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
            {
                var route = TracksMixedStage.SafeRoute(track.Id, flight);
                var seen = new HashSet<string>();
                // Bounded witness selection: first ordinary one-input mistake
                // for each nonterminal obstacle, not an exhaustive error matrix.
                foreach (int changed in Enumerable.Range(0, route.Length))
                    foreach (double shift in new[] { .4, -.4, .64, -.64, .8, -.8 })
                    {
                        if (seen.Count == 2) break;
                        if (route[changed] < 300 || route[changed] / 220 + shift < 0) continue;
                        candidates++;
                        var game = FirstHit(track.Id, flight, route, changed, shift);
                        if (game == null) continue;
                        var player = game.Players[0];
                        string hitId = player.LastHit.Id;
                        var hit = track.Obstacles.Single(o => o.Id == hitId);
                        var next = track.Obstacles.Where(o => o.X > hit.X).OrderBy(o => o.X).FirstOrDefault();
                        if (next == null || seen.Contains(hitId)) continue;
                        seen.Add(hitId); games++; witnesses++;
                        double hitX = player.Distance;
                        // Remaining penalty, then the requested human reaction
                        // allowance. The current prefix state is kept intact.
                        double remainingPenalty = player.Recovering;
                        RaceSimulation.Advance(game, remainingPenalty + .15);
                        double reactionX = player.Distance, reactionY = RaceSimulation.GetPlayerCenterY(player);
                        Check(player.Collisions == 1 && player.Recovering == 0, "single collision before recovery action");
                        double front = ObstacleGeometry.Left(next) - 14, target = IndependentRight(next) + 15;
                        WaitActualWall(game, front);
                        if (next.Kind != "gate")
                        {
                            int blockedSide = next.Kind == "floor" ? 1 : 0;
                            if (player.SupportSide == blockedSide) CrossFromWall(game, flight);
                        }
                        else
                        {
                            int origin = next.BottomOffsetX == 0 ? 1 : 0;
                            if (player.SupportSide != origin)
                            {
                                CrossFromWall(game, flight); WaitActualWall(game, front);
                            }
                            double launch = next.X - (flight.FirstAction == FirstAction.Flip ? 50 : 108.2264150943396);
                            Check(player.Distance < launch, "recovery can prepare low maneuver before its input");
                            MoveTo(game, launch, .025); Tap(game);
                            if (flight.FirstAction == FirstAction.Flip)
                            {
                                MoveTo(game, next.X - 17, .025);
                                Check(player.Support == SupportKind.None && player.AirFlipAvailable, "low recovery repeat has actual One"); Tap(game);
                            }
                        }
                        MoveTo(game, target, .025);
                        foreach (var p in game.Players) Check(p.Collisions == 1 && p.Distance >= target - 1e-6,
                            "recovery clears next complete body with no second impact");
                        rows.Add(track.Id + "/" + flight.FirstAction + " input=" + changed + " shift=" + shift
                            + " hit=" + hitId + " hitX=" + hitX.ToString("R")
                            + " remainingPenalty=" + remainingPenalty.ToString("R")
                            + " reactionX=" + reactionX.ToString("R") + " reactionY=" + reactionY.ToString("R")
                            + " next=" + next.Id + " fullClear=" + target);
                    }
                Check(seen.Count == 2, "two nonterminal actual first-hit witnesses per orientation/order/profile");
            }
            return "PASS " + checks + " checks / " + witnesses + " actual first-hit witnesses selected from " + candidates
                + " bounded candidates. Real remaining recovery + .15 s, actual wall/adaptive next maneuver, both players clear entire next union. No state injection; not exhaustive recovery.\n"
                + string.Join("\n", rows);
        }
    }
}
