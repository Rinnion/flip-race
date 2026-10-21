using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlipRace.Core;

namespace FlipRace.MixedAudit
{
    // Independent replay scheduler. Route discovery and geometry authoring are
    // deliberately not claimed: frozen production X arrays are the witnesses.
    public static class FullAudit
    {
        static int checks;
        static readonly double[] Steps = { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100 };
        public sealed class PlayerResult
        {
            public int Slot;
            public string Route;
            public double Finish, ObservedFinishAt, Distance;
            public int Accepted, Ordinary, Instant, FinishBonus, Negative, Hits;
            public double TailX, TailTime;
        }
        public sealed class GameResult
        {
            public string Profile;
            public double Step;
            public object Winner;
            public PlayerResult[] Players;
        }
        public sealed class Report
        {
            public string Status, Scope;
            public int Checks;
            public readonly List<GameResult> Games = new List<GameResult>();
        }
        static void Check(bool condition, string description)
        { checks++; if (!condition) throw new InvalidOperationException(description); }
        static void Near(double actual, double expected, double tolerance, string description)
        { Check(Math.Abs(actual - expected) <= tolerance, description + ": " + actual.ToString("R") + " vs " + expected.ToString("R")); }
        static Type MixedType()
        { return typeof(Track).Assembly.GetType("FlipRace.Core.TracksMixed") ?? throw new InvalidOperationException("TracksMixed is not ready."); }
        static Track Course()
        { return (Track)MixedType().GetMethod("Get", Type.EmptyTypes).Invoke(null, null); }
        static double[] Route(string name, FlightSettings settings)
        {
            string method = name == "safe" ? "SafeRoute" : name == "full" ? "FullRoute" : "NegativeRoute";
            var found = MixedType().GetMethod(method, new[] { typeof(FlightSettings) });
            if (found == null && name == "trap")
            {
                found = MixedType().GetMethod(method, new[] { typeof(FlightSettings), typeof(int) });
                if (found != null) return (double[])found.Invoke(null, new object[] { settings, 3 });
            }
            if (found == null) throw new InvalidOperationException("Await frozen API: TracksMixed." + method);
            return (double[])found.Invoke(null, new object[] { settings });
        }
        public static string Preparation()
        { return "PREPARED: independent two-player distance scheduler compiled. Full-course route discovery, frozen geometry and acceptance are not claimed."; }

        static GameResult Run(FlightSettings settings, string first, string second, double step)
        {
            var track = Course();
            Check(track.Id == "mixed-tricks" && track.Length == 7920, "expected mixed course and length");
            var finish = track.RiskZones.Single(z => z.IsFinish);
            Near(finish.X, 7876, 0, "finish plane unchanged");
            Check(track.RiskZones.Count(z => !z.IsNegative && !z.IsFinish) == 9 && track.RiskZones.Count(z => z.IsNegative) == 2,
                "nine ordinary and two negative zones");
            var plans = new[] { Route(first, settings), Route(second, settings) };
            foreach (var plan in plans)
            {
                Check(plan.Length != 0, "nonempty author witness");
                for (int i = 0; i < plan.Length; i++)
                    Check(double.IsFinite(plan[i]) && plan[i] >= 0 && plan[i] < track.Length && (i == 0 || plan[i] >= plan[i - 1]),
                        "sorted finite route X");
            }
            var game = Adapter.Start(track, settings);
            var result = new GameResult { Profile = settings.FirstAction == FirstAction.Flip ? "fast" : "main", Step = step,
                Players = new[] { new PlayerResult { Slot = 0, Route = first }, new PlayerResult { Slot = 1, Route = second } } };
            var indices = new int[2]; var tail = new bool[2]; var observed = new bool[2];
            double tailX = finish.X - 1e-5;
            int guard = 0;
            while (game.Phase == "racing")
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    var p = game.Players[slot];
                    if (!tail[slot] && p.Distance >= tailX - 1e-7)
                    {
                        Check(!p.FinishBoost && p.FinishBoostsEarned == 0, "tail checked before finish trigger");
                        Check(p.ActiveEffect == null && p.PendingEffects.Count == 0, "ordinary effects empty before 7876 for " + result.Players[slot].Route);
                        Check(!p.CollectedRiskIds.Any(id => !p.SuccessfulRiskIds.Contains(id) && !p.FailedRiskIds.Contains(id)
                            && !p.NegativeRiskIds.Contains(id)), "no unconfirmed linked reward at finish entry");
                        result.Players[slot].TailX = p.Distance; result.Players[slot].TailTime = game.Elapsed; tail[slot] = true;
                    }
                    if (p.FinishTime.HasValue)
                    {
                        if (!observed[slot]) { result.Players[slot].ObservedFinishAt = game.Elapsed; observed[slot] = true; }
                        continue;
                    }
                    while (indices[slot] < plans[slot].Length && plans[slot][indices[slot]] - p.Distance <= 1e-7)
                    {
                        Near(p.Distance, plans[slot][indices[slot]], 2e-7, "independently scheduled player input X");
                        Check(RaceSimulation.Press(game, slot), "accepted " + result.Profile + "/" + result.Players[slot].Route + " input " + indices[slot]);
                        indices[slot]++; result.Players[slot].Accepted++;
                    }
                }
                double dt = step;
                for (int slot = 0; slot < 2; slot++)
                {
                    var p = game.Players[slot]; if (p.FinishTime.HasValue) continue;
                    double nextX = track.Length;
                    if (indices[slot] < plans[slot].Length) nextX = Math.Min(nextX, plans[slot][indices[slot]]);
                    if (!tail[slot]) nextX = Math.Min(nextX, tailX);
                    // 440 bounds every actual speed, including a newly earned
                    // instant mid-Advance. Each player's next event constrains
                    // the shared clock separately; no averaged X or input pair.
                    dt = Math.Min(dt, Math.Max(1e-12, (nextX - p.Distance) / 440));
                }
                double before = game.Elapsed;
                RaceSimulation.Advance(game, dt);
                Check(game.Elapsed > before || game.Phase != "racing", "clock progresses");
                Check(++guard < 500000 && game.Elapsed <= 45, "bounded actual replay");
            }
            for (int slot = 0; slot < 2; slot++)
            {
                var p = game.Players[slot]; var r = result.Players[slot];
                if (!observed[slot]) r.ObservedFinishAt = game.Elapsed;
                Check(p.FinishTime.HasValue && tail[slot] && indices[slot] == plans[slot].Length, "actual complete route and finish, not caption");
                Near(p.Distance, track.Length, 0, "physical finish distance");
                Check(p.Collisions == 0 && p.ActiveEffect == null && p.PendingEffects.Count == 0, "clean complete finish and FIFO");
                Check(p.CompletedBoosts == p.BoostsEarned && p.CompletedSlows == p.SlowsEarned, "all earned ordinary pulses actually served");
                if (r.Route == "safe") Check(p.BoostsEarned == 0 && p.SlowsEarned == 0 && p.FinishBoostsEarned == 0, "safe zero rewards");
                else if (r.Route == "full") Check(p.BoostsEarned == 9 && p.SlowsEarned == 0 && p.FinishBoostsEarned == 1, "full nine ordinary plus finish");
                else Check(p.SlowsEarned >= 1, "trap actually collects a negative; ordinary count is measured");
                double expected = track.Length / 220 + .3 * (p.SlowsEarned - p.BoostsEarned) - .1 * p.FinishBoostsEarned;
                Near(p.FinishTime.Value, expected, 1e-8, "actual-count finish budget");
                Near(r.ObservedFinishAt, p.FinishTime.Value, 1e-7, "observed physical crossing clock matches ledger");
                r.Finish = p.FinishTime.Value; r.Distance = p.Distance; r.Ordinary = p.BoostsEarned; r.Instant = p.InstantBoostsEarned;
                r.Negative = p.SlowsEarned; r.FinishBonus = p.FinishBoostsEarned; r.Hits = p.Collisions;
            }
            result.Winner = game.Winner;
            double a = result.Players[0].Finish, b = result.Players[1].Finish;
            Check(Math.Abs(a - b) < 1e-8 ? Equals(game.Winner, "tie") : Equals(game.Winner, a < b ? 0 : 1), "winner follows actual individual finishes");
            return result;
        }

        public static Report RunAll()
        {
            checks = 0;
            var report = new Report { Scope = "Frozen author route replay using an independent two-player X scheduler; no route discovery, injection, large recovery search or global tail theorem." };
            foreach (var settings in new[] { FlightPresets.Chosen, FlightPresets.FastFlip }) foreach (double step in Steps)
            {
                foreach (string route in new[] { "safe", "full", "trap" }) report.Games.Add(Run(settings, route, route, step));
                report.Games.Add(Run(settings, "safe", "full", step));
                report.Games.Add(Run(settings, "full", "safe", step));
                report.Games.Add(Run(settings, "full", "trap", step));
                report.Games.Add(Run(settings, "trap", "full", step));
            }
            report.Checks = checks; report.Status = "PASS"; return report;
        }
    }
}
