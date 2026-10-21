using System;
using System.Collections.Generic;
using System.Linq;
using FlipRace.Core;

namespace FlipRace.MixedAudit
{
    public static partial class StageAudit
    {
        static int checks, games;
        static readonly double[] Steps = { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100 };
        static void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException(message); }
        static void Near(double value, double expected, double tolerance, string message)
        { Check(Math.Abs(value - expected) <= tolerance, message + ": " + value.ToString("R") + " vs " + expected.ToString("R")); }
        static void Tap(GameState game)
        { Check(RaceSimulation.Press(game, 0), "upper input accepted"); Check(RaceSimulation.Press(game, 1), "lower input accepted"); }

        // Conservative speed bound avoids predicting past a ring whose reward
        // changes horizontal speed during Advance. No player state is injected.
        static void MoveTo(GameState game, double x, double step)
        {
            int guard = 0;
            while (game.Phase == "racing" && x - game.Players[0].Distance > 1e-7)
            {
                double gap = x - game.Players[0].Distance;
                RaceSimulation.Advance(game, Math.Min(step, gap / 440));
                if (++guard > 200000) throw new InvalidOperationException("Distance scheduler did not converge.");
            }
            Near(game.Players[0].Distance, x, 2e-7, "actual target X");
        }

        sealed class RouteResult
        {
            public GameState Game;
            public List<InputObservation> Inputs = new List<InputObservation>();
            public Dictionary<string, Observation> Clears = new Dictionary<string, Observation>();
        }

        static double[] Route(string id, FlightSettings flight, string variant)
        { return variant == "safe" ? TracksMixedStage.SafeRoute(id, flight)
            : variant == "full" ? TracksMixedStage.FullRoute(id, flight)
            : TracksMixedStage.QuickReturnRoute(id, flight); }

        static RouteResult Run(string id, FlightSettings flight, string variant, double step,
            int changed = -1, double shift = 0, RouteResult nominal = null)
        {
            games++;
            var track = TracksMixedStage.Get(id);
            var result = new RouteResult { Game = Adapter.Start(track, flight) };
            var route = Route(id, flight, variant);
            // Union endpoints are independently computed from explicit bands.
            // Checkpoints are only collected on nominal runs: perturbations
            // must not accidentally schedule past their changed clock event.
            var clears = track.Obstacles.Select(o => new { o.Id, X = IndependentRight(o) + 15 }).OrderBy(o => o.X).ToArray();
            int nextClear = 0;
            for (int i = 0; i < route.Length; i++)
            {
                if (changed < 0)
                    while (nextClear < clears.Length && clears[nextClear].X < route[i])
                    {
                        MoveTo(result.Game, clears[nextClear].X, step);
                        result.Clears.Add(clears[nextClear].Id, Observation.Read(result.Game, 0));
                        nextClear++;
                    }
                if (i == changed) Adapter.AdvanceTo(result.Game, nominal.Inputs[i].Before.Elapsed + shift, step);
                else MoveTo(result.Game, route[i], step);
                var upper = Adapter.Press(result.Game, 0, result.Game.Elapsed);
                var lower = Adapter.Press(result.Game, 1, result.Game.Elapsed);
                result.Inputs.Add(upper);
                Check(upper.Accepted && lower.Accepted, id + "/" + variant + " accepted input " + i);
            }
            if (changed < 0)
                while (nextClear < clears.Length)
                {
                    MoveTo(result.Game, clears[nextClear].X, step);
                    result.Clears.Add(clears[nextClear].Id, Observation.Read(result.Game, 0));
                    nextClear++;
                }
            Adapter.AdvanceTo(result.Game, 30, step);
            Check(result.Game.Phase == "finished", "actual finish");
            foreach (var p in result.Game.Players)
            {
                Check(p.Collisions == 0, id + "/" + flight.FirstAction + "/" + variant + " zero hits input=" + changed + " shift=" + shift);
                Check(p.BoostsEarned == (variant == "full" ? 2 : 0) && p.SlowsEarned == 0, "expected actual rewards");
                Near(p.FinishTime.Value, 2700d / 220 - (variant == "full" ? .6 : 0), 1e-8, "physical finish time");
                Check(p.ActiveEffect == null && p.PendingEffects.Count == 0, "empty effect tail");
            }
            foreach (var pair in result.Clears) Check(pair.Value.Collisions == 0, "whole union clear " + pair.Key);
            return result;
        }

        static double IndependentRight(Obstacle o)
        { return o.Kind == "gate" ? o.X + Math.Max(o.TopOffsetX, o.BottomOffsetX) + o.Width : o.X + o.Width; }

        static void Geometry(Track track)
        {
            bool mirror = track.Id.EndsWith(TracksMixedStage.MirrorSuffix, StringComparison.Ordinal);
            var small = track.Obstacles.Single(o => o.Id == "mixed-small");
            var large = track.Obstacles.Single(o => o.Id == "mixed-large");
            var low = track.Obstacles.Single(o => o.Id == "mixed-low");
            Check(small.Width == 64 && small.Height == 48 && large.Width == 200 && large.Height == 92, "different stone bodies");
            Check(small.Kind == (mirror ? "ceiling" : "floor") && large.Kind == small.Kind, "same-side stone orientation");
            Check(ObstacleGeometry.TryGetBand(low, 0, out var top) && ObstacleGeometry.TryGetBand(low, 1, out var bottom), "both low parts exist");
            ObstacleGeometry.TryGetBand(low, 1, out bottom);
            Near(top.Height, mirror ? 16 : 114, 0, "physical upper height");
            Near(bottom.Height, mirror ? 114 : 16, 0, "physical lower height");
            Near(mirror ? top.Left : bottom.Left, low.X, 0, "small threshold first");
            Near(mirror ? bottom.Left : top.Left, low.X + 80, 0, "long opposite part later");
            Near(ObstacleGeometry.Right(low), low.X + 96, 0, "complete offset union");
            Near(IndependentRight(low), ObstacleGeometry.Right(low), 0, "independent union matches helper");
        }

        static void Semantics(string id, FlightSettings flight, string variant, RouteResult run)
        {
            int origin = id.EndsWith(TracksMixedStage.MirrorSuffix, StringComparison.Ordinal) ? 0 : 1;
            var small = run.Inputs.Where(p => p.Before.Distance > 300 && p.Before.Distance < 800).ToArray();
            var later = run.Inputs.First(p => p.Before.Distance > 800);
            Check(small[0].Before.Support == SupportKind.Wall && small[0].Before.Side == origin, "small departs real original wall");
            if (variant != "full")
            {
                Check(small.Length == (flight.FirstAction == FirstAction.Flip ? 2 : 1), "small task action count");
                if (flight.FirstAction == FirstAction.Flip)
                {
                    var repeat = small[1];
                    Check(repeat.Before.Support == SupportKind.None && repeat.Before.Charge, "Fast small repeat is actually airborne with available One");
                    Check(repeat.After.Support == SupportKind.None && !repeat.After.Charge, "Fast small repeat consumes One");
                    Near(repeat.After.VerticalSpeed, repeat.Before.VerticalSpeed, 0, "repeat preserves momentum");
                }
                Check(later.Before.Support == SupportKind.Wall && later.Before.Side == origin && later.Before.Charge,
                    "small returns to real same-side wall and recharges before the next maneuver");
            }
            var largeClear = run.Clears["mixed-large"];
            Check(largeClear.Support == SupportKind.Wall && largeClear.Side != origin, "large uses opposite wall, unlike small same-wall jump");
            Check(run.Clears["mixed-low"].Distance > IndependentRight(TracksMixedStage.Get(id).Obstacles.Single(o => o.Id == "mixed-low")) + 14,
                "low clear includes both bands and body radius");
        }

        // Independent analytic oracle: no route arrays are used here.
        static void SmallOracle()
        {
            double g = 2700, u = 35, tau = .3;
            double traveled = u * tau + .5 * g * tau * tau;
            double v = u + g * tau;
            double wrongWall = (v - Math.Sqrt(v * v - 2 * g * (212 - traveled))) / g;
            double home = Math.Sqrt(424 / g);
            foreach (bool mirror in new[] { false, true }) foreach (double step in Steps)
            {
                games++;
                var track = TracksMixedStage.Get(TracksMixedStage.StageId, mirror);
                var game = Adapter.Start(track, FlightPresets.FastFlip);
                if (mirror) { Tap(game); Adapter.AdvanceTo(game, 1, step); }
                MoveTo(game, 530, step); double start = game.Elapsed; Tap(game);
                Adapter.AdvanceTo(game, start + tau, step);
                foreach (var p in game.Players)
                { Near(p.VerticalSpeed, mirror ? v : -v, 1e-7, "analytic pre-repeat VY"); Check(p.Support == SupportKind.None && p.AirFlipAvailable, "actual airborne repeat"); }
                Tap(game);
                Adapter.AdvanceTo(game, start + tau + wrongWall + 1e-8, step);
                foreach (var p in game.Players)
                {
                    Near(RaceSimulation.GetPlayerCenterY(p), mirror ? 226 : 14, 1e-6, "analytic inertial opposite-wall contact");
                    Check(p.Support == SupportKind.None && !p.AirFlipAvailable, "wrong wall is no support and no recharge");
                    Check(!RaceSimulation.Press(game, p.Id), "spent repeat rejected at wrong wall");
                }
                Adapter.AdvanceTo(game, start + tau + wrongWall + home + 1e-7, step);
                foreach (var p in game.Players)
                {
                    Check(p.Support == SupportKind.Wall && p.SupportSide == (mirror ? 0 : 1) && p.AirFlipAvailable,
                        "analytic actual origin-wall landing recharges");
                    Check(p.Collisions == 0, "small oracle clean");
                }
                Near(game.Players[0].Distance, 530 + 220 * (tau + wrongWall + home + 1e-7), 1e-7, "independent horizontal clock");
            }
            // With Jump350, the useful horizontal span above a 92-high body is
            // much smaller than expanded width 228, even at speed 330. A single
            // unmodified jump therefore cannot substitute for the large task.
            double largeWindow = 2 * Math.Sqrt(350 * 350 - 2 * 662.5 * 92) / 662.5;
            foreach (double speed in new[] { 110d, 220d, 330d })
                Check(speed * largeWindow < 228, "natural jump cannot clear large expanded body at " + speed);
        }

        public static string RunAll()
        {
            checks = games = 0;
            SmallOracle();
            foreach (var track in TracksMixedStage.All)
            {
                Geometry(track);
                foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
                    foreach (string variant in new[] { "safe", "full", "quickreturn" })
                    {
                        var nominal = Run(track.Id, flight, variant, .317);
                        Semantics(track.Id, flight, variant, nominal);
                        foreach (double step in Steps) Run(track.Id, flight, variant, step);
                        double window = variant == "safe" ? .060 : .025;
                        for (int i = 0; i < nominal.Inputs.Count; i++) foreach (double shift in new[] { -window, window })
                        {
                            if (nominal.Inputs[i].Before.Elapsed + shift < 0) continue;
                            Run(track.Id, flight, variant, .317, i, shift, nominal);
                        }
                    }
            }
            return "PASS " + checks + " checks / " + games + " actual games. Scope: independent small-flight oracle, actual support/recharge semantics, explicit physical bands/whole union, both orders/mirrors/profiles, six dt and individual route windows. Held-speed/pulse fixtures and human comfort are separate scopes.";
        }
    }
}
