using System;
using FlipRace.Core;

public static partial class TracksIndependentAudit
{
    static Track Section(int side)
    {
        var t = TracksFull.Practice();
        if (side == 1) return t;
        foreach (var o in t.Obstacles) o.Kind = o.Kind == "floor" ? "ceiling" : "floor";
        foreach (var z in t.RiskZones) z.Y = 240 - z.Y;
        return t;
    }
    static void Route(GameState g, double[] inputs, double step)
    {
        foreach (double x in inputs) { MoveTo(g, x, step); Tap(g); }
        M(g, 100, step);
        C(g.Phase == "finished", "route reaches actual finish");
    }
    static void Finished(GameState g, double expected, int rewards, string[] ids)
    {
        foreach (var p in g.Players)
        {
            C(p.FinishTime.HasValue, "actual finish recorded"); N(p.FinishTime.Value, expected, "physical finish time");
            C(p.Collisions == 0 && p.SlowsEarned == 0, "no collision or negative");
            C(p.BoostsEarned == rewards && p.InstantBoostsEarned == rewards, "exact independent earned rewards");
            C(p.CompletedBoosts == rewards && p.ActiveEffect == null && p.PendingEffects.Count == 0, "full real effect time and empty FIFO");
            C(p.SuccessfulRiskIds.Count == ids.Length, "no other reward");
            foreach (string id in ids) C(p.SuccessfulRiskIds.Contains(id), "expected individual reward " + id);
            N(p.Distance, g.Track.Length, "actual distance at finish");
        }
        C(g.Winner is string, "both players have matching exact route result");
    }
    static void StageOneRoutes()
    {
        context = "stage metadata";
        var t = TracksFull.Practice();
        C(t.Obstacles.Length == 1 && t.Obstacles[0].Width == 64 && t.Obstacles[0].Height == 48, "real narrow stone geometry");
        var pad = t.RiskZones[0]; N(pad.X, t.Obstacles[0].X + 32, "centered narrow marker");
        C(pad.IsLanding && pad.LandingWidth >= 24 && pad.LandingWidth <= 32, "narrow marker interval");
        C(pad.X - pad.LandingWidth / 2 >= t.Obstacles[0].X && pad.X + pad.LandingWidth / 2 <= t.Obstacles[0].X + 64,
            "entire marker is inside actual stone");
        double start = t.RiskRoute[0];
        foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        foreach (int route in new[] { 0, 1, 2, 3 })
        {
            context = "section side=" + side + " dt=" + step + " route=" + route;
            var track = Section(side); var g = Prepare(side, step, track);
            if (route == 0) { Route(g, track.SafeRoute, step); Finished(g, 10, 0, Array.Empty<string>()); }
            else if (route == 1) { Route(g, track.RiskRoute, step); Finished(g, 9.1, 3, new[] { "practice-pad", "practice-chain-1", "practice-chain-2" }); }
            else if (route == 2) { Route(g, new[] { start, 1100d }, step); Finished(g, 9.4, 2, new[] { "practice-pad", "practice-chain-1" }); }
            else { Route(g, new[] { start, 1170d, 1225d }, step); Finished(g, 9.4, 2, new[] { "practice-pad", "practice-chain-2" }); }
        }
    }
    static GameState Land(int side, double step)
    {
        var t = Section(side); var g = Prepare(side, step, t);
        MoveTo(g, t.RiskRoute[0], step); Tap(g); MoveTo(g, 732, step); M(g, 1e-9, step);
        foreach (var p in g.Players)
        {
            C(p.Support == SupportKind.Stone && p.SupportObstacleId == "practice-pad-stone" && p.SupportSide == side,
                "real authored landing"); C(p.Collisions == 0 && p.BoostsEarned == 1, "landing is legal and rewards once");
            N(p.VerticalSpeed, 0, "supported velocity"); N(p.CurrentSpeed, 330, "landing instant speed");
        }
        return g;
    }
    static void NarrowEdges()
    {
        foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        foreach (double delta in new[] { -1e-4, 1e-4 })
        {
            context = "narrow edge side=" + side + " dt=" + step + " offset=" + delta;
            var g = Land(side, step); double landedAt = g.Elapsed;
            MoveTo(g, 764 + delta, step);
            double v = g.Players[0].VerticalSpeed; int gravity = g.Players[0].Gravity;
            if (delta < 0)
            {
                foreach (var p in g.Players) C(p.Support == SupportKind.Stone, "still on stone just before edge");
                Tap(g); foreach (var p in g.Players) N(p.VerticalSpeed, Sign(side) * U, "before edge starts normal jump");
            }
            else
            {
                foreach (var p in g.Players)
                {
                    C(p.Support == SupportKind.None && p.Flipping && p.FlightOriginObstacleId == "practice-pad-stone", "real walk-off flight");
                    N(p.VerticalSpeed, -Sign(side) * G * delta / 330, "walk-off adds no jump impulse", 2e-6);
                    C(p.Collisions == 0 && p.BoostsEarned == 1, "walk-off has no phantom impact or repeated award");
                }
                N(g.Elapsed - landedAt, (32 + delta) / 330, "short supported time", 2e-8);
                Tap(g); foreach (var p in g.Players) { N(p.VerticalSpeed, v, "after edge is inertial air repeat"); C(p.Gravity == -gravity, "after edge changes gravity only"); }
            }
            M(g, .02, step); foreach (var p in g.Players) C(p.Collisions == 0, "no zero-time reimpact departing old face");
        }
        // The authored full route itself stays idle through the edge. Check
        // that a whole 150 ms reaction delay after its landing is harmless.
        foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "natural narrow exit side=" + side + " dt=" + step;
            var g = Land(side, step); M(g, .15, step);
            foreach (var p in g.Players)
                C(p.Collisions == 0 && p.Support == SupportKind.None && p.Flipping, "natural exit needs no sub-100ms response");
            Route(g, new[] { 1100d, 1180d }, step);
            Finished(g, 9.1, 3, new[] { "practice-pad", "practice-chain-1", "practice-chain-2" });
        }
    }
    static void MarkerEdgesAndReuse()
    {
        double landingTime = (U + Math.Sqrt(U * U - 2 * G * 48)) / G;
        foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        foreach (double target in new[] { 716 - 1e-4, 716, 716 + 1e-4, 748 - 1e-4, 748, 748 + 1e-4 })
        {
            context = "marker endpoint side=" + side + " dt=" + step + " x=" + target;
            var g = Prepare(side, step, Section(side));
            MoveTo(g, target - 220 * landingTime, step); Tap(g); M(g, landingTime + 2e-9, step);
            bool legal = target >= 716 && target <= 748;
            foreach (var p in g.Players)
            {
                C(p.Collisions == (legal ? 0 : 1), "real face contact obeys marker center interval");
                C(p.BoostsEarned == (legal ? 1 : 0), "only legal marker contact awards");
                C(legal ? p.Support == SupportKind.Stone : p.Support == SupportKind.None, "support agrees with true contact");
            }
        }
        foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "early side impact side=" + side + " dt=" + step;
            var g = Prepare(side, step, Section(side)); MoveTo(g, 680, step); Tap(g); M(g, .04, step);
            foreach (var p in g.Players)
            { C(p.Collisions == 1 && p.LastHit.Id == "practice-pad-stone", "marked stone still has blocking side");
              C(p.BoostsEarned == 0 && p.Support == SupportKind.None && p.LastFlight.Interrupted, "early side hit is not landing"); }

            context = "synthetic wide revisit side=" + side + " dt=" + step;
            // Revisit the same marked face after a real jump. Width700/window400
            // deliberately isolate the once-only contract: u350 cannot complete
            // such a return before leaving an authored width64 platform.
            var t = new Track(6000, new[] { new Obstacle { Id = "wide-revisit", X = 700, Width = 700,
                Height = 48, Kind = side == 1 ? "floor" : "ceiling" } });
            t.RiskZones = new[] { new RiskZone { Id = "wide-pad", Kind = "landing", ObstacleId = "wide-revisit",
                X = 900, LandingWidth = 400, Y = side == 1 ? 192 : 48 } };
            g = Prepare(side, step, t); MoveTo(g, 732 - 220 * landingTime, step); Tap(g); M(g, landingTime + 2e-9, step);
            foreach (var p in g.Players) C(p.Support == SupportKind.Stone && p.BoostsEarned == 1, "first real landing awards once");
            Tap(g); M(g, Cycle + 2e-9, step);
            foreach (var p in g.Players)
            { C(p.Support == SupportKind.Stone && p.SupportObstacleId == "wide-revisit" && p.Collisions == 0, "collected face remains legal");
              C(p.BoostsEarned == 1 && p.CompletedBoosts == 1 && p.ActiveEffect == null && p.PendingEffects.Count == 0, "revisit neither duplicates nor restarts pulse");
              N(p.LastFlight.Duration, Cycle, "real stone-origin normal jump duration"); }
        }
    }
    static void LocalSafeWindow()
    {
        foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        foreach (double delta in new[] { -.06, .06 })
        {
            context = "safe individual clock shift side=" + side + " dt=" + step + " seconds=" + delta;
            var t = Section(side); var g = Prepare(side, step, t);
            // No effects precede this action: x/220 is its absolute race time.
            double actionTime = t.SafeRoute[0] / 220 + delta;
            M(g, actionTime - g.Elapsed, step); Tap(g); M(g, 100, step);
            Finished(g, 10, 0, Array.Empty<string>());
        }
    }
}
