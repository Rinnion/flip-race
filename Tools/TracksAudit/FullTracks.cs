using System;
using FlipRace.Core;

public static partial class TracksIndependentAudit
{
    public static void RunFullProof()
    {
        int windowCases = 0;
        foreach (var t in TracksFull.All)
        {
            context = t.Id + " authored metadata";
            C(t.Length == 7920 && t.PositiveRingCount == 10, "length and total positive budget");
            int landing = 0, negative = 0, finish = 0, instant = 0, before = 0, after = 0;
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var o in t.Obstacles)
            { C(ids.Add(o.Id), "unique obstacle ID"); C(o.X + o.Width < 7876, "no obstacle in finish tail"); }
            foreach (var z in t.RiskZones)
            {
                C(ids.Add(z.Id), "unique reward ID");
                if (z.IsNegative) negative++;
                else if (z.IsFinish) { finish++; N(z.X, 7876, "finish plane"); }
                else if (z.IsLanding)
                {
                    landing++; var o = Array.Find(t.Obstacles, item => item.Id == z.ObstacleId);
                    C(o != null && z.X - z.LandingWidth / 2 >= o.X && z.X + z.LandingWidth / 2 <= o.X + o.Width,
                        "landing interval lies within physical face");
                }
                else if (z.IsInstant) instant++;
                else if (z.AfterObstacle) after++;
                else before++;
                if (!z.IsFinish) C(z.X + z.Radius < 7876, "no ordinary contact in final 44 units");
                if (!z.IsLanding && !z.IsFinish && (z.IsInstant || z.IsNegative))
                    C(z.Y - Radius - z.Radius > 14 && z.Y + Radius + z.Radius < 226,
                        "air-only circle cannot collect from either stable wall");
            }
            C(landing == 3 && negative == 2 && finish == 1 && instant == 4 && before == 1 && after == 1,
                "exact nine ordinary/2 negative/finish composition");
            foreach (double step in Steps) foreach (int mode in new[] { 0, 1, 2 })
            {
                context = t.Id + " route=" + mode + " dt=" + step;
                var g = Start(t);
                Route(g, mode == 0 ? t.SafeRoute : mode == 1 ? t.RiskRoute : TracksFull.NegativeRoute(t.Id), step);
                foreach (var p in g.Players)
                {
                    C(p.FinishTime.HasValue, "physical finish exists");
                    N(p.FinishTime.Value, mode == 0 ? 36 : mode == 1 ? 33.2 : 33.5, "real complete track finish");
                    C(p.Collisions == 0 && p.BoostsEarned == (mode == 0 ? 0 : 9)
                        && p.FinishBoostsEarned == (mode == 0 ? 0 : 1) && p.SlowsEarned == (mode == 2 ? 1 : 0), "full route budget");
                    C(p.ActiveEffect == null && p.PendingEffects.Count == 0 && p.CompletedBoosts == p.BoostsEarned
                        && p.CompletedSlows == p.SlowsEarned, "all awarded ordinary effects fully served");
                    N(p.Distance, 7920, "actual full distance");
                }
            }
            var times = InputTimes(t, t.SafeRoute);
            for (int changed = 0; changed < times.Length; changed++) foreach (double shift in new[] { -.06, .06 })
            {
                context = t.Id + " safe clock window input=" + changed + " shift=" + shift;
                var g = Start(t);
                for (int i = 0; i < t.SafeRoute.Length; i++)
                {
                    if (i == changed) M(g, times[i] + shift - g.Elapsed, .013);
                    else MoveTo(g, t.SafeRoute[i], .013);
                    Tap(g);
                }
                M(g, 100, .013);
                foreach (var p in g.Players)
                {
                    C(p.FinishTime.HasValue && p.Collisions == 0 && p.BoostsEarned == 0 && p.SlowsEarned == 0
                        && p.FinishBoostsEarned == 0, "individual safe shift remains safe and reward-free");
                    N(p.FinishTime.Value, 36, "safe shift real finish");
                }
                windowCases++;
            }
            foreach (int winner in new[] { 0, 1 }) foreach (double step in Steps)
            {
                context = t.Id + " finish-only duel winner=" + winner + " dt=" + step;
                var g = Start(t);
                for (int i = 0; i < t.RiskRoute.Length; i++)
                {
                    MoveTo(g, t.RiskRoute[i], step);
                    if (i + 1 == t.RiskRoute.Length) C(RaceSimulation.Press(g, winner), "only winner takes final target jump");
                    else Tap(g);
                }
                M(g, 100, step);
                C(g.Winner is int && (int)g.Winner == winner, "actual winner correct");
                foreach (var p in g.Players)
                {
                    C(p.Collisions == 0 && p.BoostsEarned == 9 && p.SlowsEarned == 0, "identical clean prior collection");
                    N(p.FinishTime.Value, p.Id == winner ? 33.2 : 33.3, "finish-only physical difference");
                }
            }
            Console.WriteLine("PASS " + t.Id + " six dt safe36/full33.2/trap33.5; both finish-only winner assignments33.2/33.3; safe individual ±60ms");
        }
        Console.WriteLine("PASS full routes independent: " + checks + " assertions / " + games + " games / " + windowCases + " safe local window cases. Arbitrary pending reward tail is a separate contract check.");
    }
    public static void RunFullChains()
    {
        int cases = 0;
        foreach (var t in TracksFull.All) foreach (double step in Steps) foreach (int branch in new[] { 1, 2 })
        {
            RiskZone first = null, second = null;
            foreach (var zone in t.RiskZones)
            {
                if (zone.Id.Contains("-chain-") && zone.Id.EndsWith("-1")) first = zone;
                if (zone.Id.Contains("-chain-") && zone.Id.EndsWith("-2")) second = zone;
            }
            C(first != null && second != null, "authored chain has two distinct rewards");
            double beginning = second.X - 200;
            context = t.Id + " chain only=" + branch + " dt=" + step;
            var g = Start(t);
            foreach (double x in t.RiskRoute) if (x < beginning - 1e-8) { MoveTo(g, x, step); Tap(g); }
            foreach (var p in g.Players) C(p.Collisions == 0, "actual full prefix reaches chain clean");
            if (branch == 1) { MoveTo(g, beginning, step); Tap(g); }
            else { MoveTo(g, beginning + 70, step); Tap(g); MoveTo(g, beginning + 125, step); Tap(g); }
            MoveTo(g, second.X + 23, step);
            foreach (var p in g.Players)
            {
                C(p.Collisions == 0, "independent branch clears both circular contact bands without hit");
                C(p.SuccessfulRiskIds.Contains(branch == 1 ? first.Id : second.Id), "selected individual chain reward earned");
                C(!p.CollectedRiskIds.Contains(branch == 1 ? second.Id : first.Id), "other chain reward actually untouched");
            }
            cases++;
        }
        Console.WriteLine("PASS full chain independent: " + cases + " actual branches, first-only/second-only, all3tracks/sixdt; stops after both contact bands, not a full rerouted finish claim.");
    }
    public static void RunLongProof()
    {
        foreach (double step in Steps) foreach (int mode in new[] { 0, 1, 2 })
        {
            context = "Long independent route=" + mode + " dt=" + step;
            var t = TracksFull.Get(TracksFull.LongId); var g = Start(t);
            Route(g, mode == 0 ? t.SafeRoute : mode == 1 ? t.RiskRoute : TracksFull.NegativeRoute(t.Id), step);
            foreach (var p in g.Players)
            {
                N(p.FinishTime.Value, mode == 0 ? 36 : mode == 1 ? 33.2 : 33.5, "real complete track finish");
                C(p.Collisions == 0 && p.BoostsEarned == (mode == 0 ? 0 : 9)
                    && p.FinishBoostsEarned == (mode == 0 ? 0 : 1) && p.SlowsEarned == (mode == 2 ? 1 : 0), "full route budget");
                C(p.ActiveEffect == null && p.PendingEffects.Count == 0, "no effect tail");
                N(p.Distance, 7920, "actual full distance");
            }
        }
        Console.WriteLine("PASS preliminary Long replay: " + checks + " assertions / " + games + " games; full branch bounds/recovery still separate.");
    }
    static string StateLine(PlayerState p)
    {
        return "x=" + p.Distance.ToString("R") + " y=" + RaceSimulation.GetPlayerCenterY(p).ToString("R")
            + " vy=" + p.VerticalSpeed.ToString("R") + " gravity=" + p.Gravity + " earned=" + p.BoostsEarned
            + " collected=" + p.CollectedRiskIds.Count + " pending=" + (p.CollectedRiskIds.Count - p.SuccessfulRiskIds.Count)
            + " boost=" + p.BoostRemaining.ToString("R") + " hits=" + p.Collisions;
    }
    public static void RunAfterPendingProbe() { AfterPendingProbe(false); }
    public static void RunAfterPendingTargetProbe() { AfterPendingProbe(true); }
    static void AfterPendingProbe(bool hitTarget)
    {
        const double step = 1d / 144;
        context = "actual Long delayed after confirmation";
        var t = TracksFull.Get(TracksFull.LongId); var g = Start(t);
        string afterId = null;
        double hop = 0;
        foreach (var z in t.RiskZones) if (!z.IsInstant && !z.IsNegative && z.AfterObstacle)
        { afterId = z.Id; hop = z.X - 20; }
        foreach (double x in t.RiskRoute) if (x <= hop + 1e-8) { MoveTo(g, x, step); Tap(g); }
        M(g, .55, step);
        Console.WriteLine("After collection " + StateLine(g.Players[0]) + " afterId=" + afterId);
        C(g.Players[0].CollectedRiskIds.Contains(afterId) && !g.Players[0].SuccessfulRiskIds.Contains(afterId), "after reward actually pending");
        // Real unlimited taps stabilize a narrow band near the natural apex.
        // No position/velocity/reward fields are changed by this controller.
        int guard = 0, hoverPresses = 0;
        double hoverEnd = hitTarget ? 7600 : 7740;
        while (g.Players[0].Distance < hoverEnd - 1e-8 && g.Phase == "racing")
        {
            var p = g.Players[0];
            if ((p.Gravity > 0 && p.VerticalSpeed >= 50 - 1e-8)
                || (p.Gravity < 0 && p.VerticalSpeed <= -50 + 1e-8)) { Tap(g); hoverPresses++; }
            p = g.Players[0];
            double targetVelocity = p.Gravity > 0 ? 50 : -50;
            double untilTurn = (targetVelocity - p.VerticalSpeed) / (p.Gravity * G);
            double untilX = (hoverEnd - p.Distance) / 440;
            double dt = Math.Min(step, Math.Min(untilTurn, untilX));
            if (dt <= 1e-12) dt = 1e-10;
            M(g, dt, step);
            if (++guard > 100000) throw new Exception("hover guard");
        }
        Console.WriteLine("Late pending " + StateLine(g.Players[0]) + " presses=" + hoverPresses);
        C(g.Players[0].Collisions == 0 && !g.Players[0].SuccessfulRiskIds.Contains(afterId), "pending carried by attainable clean airborne path");
        if (g.Players[0].Gravity != (hitTarget ? -1 : 1)) Tap(g);
        // Reach a real wall; the old after reward confirms only now.
        while (g.Players[0].Support == SupportKind.None && g.Phase == "racing") M(g, .001, step);
        Console.WriteLine("Late landing " + StateLine(g.Players[0]));
        if (hitTarget)
        {
            double timeToTarget = (U - Math.Sqrt(U * U - 2 * G * 46)) / G;
            MoveTo(g, 7876 - 330 * timeToTarget, step); Tap(g);
        }
        MoveTo(g, 7876, step);
        Console.WriteLine("At finish plane " + StateLine(g.Players[0]));
        M(g, .000001, step);
        Console.WriteLine("Finish target awarded=" + g.Players[0].FinishBoostsEarned + " boost=" + g.Players[0].BoostRemaining.ToString("R"));
    }
}
