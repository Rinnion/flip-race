using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

// Offline authoring search. Every candidate uses production Press/Advance.
// A search witness is not a claim about timing windows or player accessibility.
internal static partial class Program
{
    sealed class Node
    {
        public GameState Game;
        public List<double> Inputs = new List<double>();
        public double LastTap = -100;
        public int AirInputs, AirChain;
    }
    static GameState Begin(Track track, FlightSettings flight)
    {
        var game = RaceSimulation.CreateTrial(track, flight);
        RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1);
        RaceSimulation.Advance(game, 3); return game;
    }
    static string Key(Node n)
    {
        var p = n.Game.Players[0];
        return Math.Round(RaceSimulation.GetPlayerCenterY(p) / 3) + ":" + Math.Round(p.VerticalSpeed / 30)
            + ":" + p.Gravity + ":" + p.AirFlipAvailable + ":" + p.Support + ":" + p.SupportSide
            + ":" + p.BoostsEarned + ":" + p.CollectedRiskIds.Count + ":" + p.SlowsEarned
            + ":" + Math.Round((p.ActiveEffect?.Remaining ?? 0) * 20)
            + ":" + Math.Min(3, (int)((n.Game.Elapsed - n.LastTap) / .08)) + ":" + n.AirChain;
    }
    static bool Viable(Node n, bool full)
    {
        var p = n.Game.Players[0];
        if (p.Collisions != 0 || p.SlowsEarned != 0) return false;
        if (!full) return p.BoostsEarned == 0 && p.CollectedRiskIds.Count == 0;
        foreach (var zone in n.Game.Track.RiskZones)
        {
            if (zone.IsNegative || zone.IsFinish) continue;
            double last = zone.X + (zone.IsLanding ? zone.LandingWidth / 2 : zone.Radius + 14);
            if (p.Distance > last + .01 && !p.CollectedRiskIds.Contains(zone.Id)
                && !p.SuccessfulRiskIds.Contains(zone.Id)) return false;
        }
        return true;
    }
    static Node Search(GameState start, double end, int endSide, bool full, int maxInputs = 5, double grid = 4)
    {
        var beam = new List<Node> { new Node { Game = start } };
        double first = start.Players[0].Distance;
        for (double x = first; x < end + grid / 2; x += grid)
        {
            double nextX = Math.Min(end, x + grid);
            if (nextX <= x) break;
            var buckets = new Dictionary<string, Node>();
            foreach (var source in beam)
            {
                for (int action = 0; action < 2; action++)
                {
                    if (action == 1 && (source.Inputs.Count >= maxInputs || source.Game.Elapsed - source.LastTap < .10)) continue;
                    var node = new Node { Game = Clone(source.Game), Inputs = new List<double>(source.Inputs), LastTap = source.LastTap,
                        AirInputs = source.AirInputs, AirChain = source.AirChain };
                    if (action == 1)
                    {
                        bool airborne = node.Game.Players[0].Flipping;
                        if (!full && airborne && (node.Game.Flight.FirstAction == FirstAction.Flip || node.AirChain >= 1)) continue;
                        if (!RaceSimulation.Press(node.Game, 0) || !RaceSimulation.Press(node.Game, 1)) continue;
                        if (airborne) { node.AirInputs++; node.AirChain++; } else node.AirChain = 0;
                        node.Inputs.Add(x); node.LastTap = node.Game.Elapsed;
                    }
                    MoveTo(node.Game, nextX, .2);
                    if (!Viable(node, full)) continue;
                    string key = Key(node);
                    if (!buckets.TryGetValue(key, out var prior) || prior.AirInputs > node.AirInputs
                        || (prior.AirInputs == node.AirInputs && prior.Inputs.Count > node.Inputs.Count))
                        buckets[key] = node;
                }
            }
            beam = buckets.Values.OrderBy(n => n.AirInputs).ThenBy(n => n.Inputs.Count).ThenBy(n => Math.Abs(
                RaceSimulation.GetPlayerCenterY(n.Game.Players[0]) - (endSide == 1 ? 226 : 14)))
                .Take(1600).ToList();
            if (beam.Count == 0) return null;
        }
        return beam.Where(n => n.Game.Players[0].Support == SupportKind.Wall
            && n.Game.Players[0].SupportSide == endSide && !n.Game.Players[0].Flipping
            && (!full || n.Game.Players[0].BoostsEarned == start.Track.RiskZones.Count(z => !z.IsNegative && !z.IsFinish)))
            .OrderBy(n => n.AirInputs).ThenBy(n => n.Inputs.Count).FirstOrDefault();
    }
    static Track Landing(double width, double height)
    {
        double x = 200, end = width > 100 ? 700 : 520;
        var stone = new Obstacle { Id = "pad-stone", Kind = "floor", X = x, Width = width, Height = height };
        return new Track(end + 20, new[] { stone }) { RiskZones = new[] {
            new RiskZone { Id = "pad", Kind = "landing", ObstacleId = stone.Id,
                X = x + width / 2, Y = 240 - height, LandingWidth = width > 100 ? 52 : 32 } } };
    }
    static Track Module(string kind)
    {
        if (kind == "pad") return Landing(64, 48);
        if (kind == "large") return Landing(200, 92);
        var obstacles = new List<Obstacle>(); var zones = new List<RiskZone>();
        double end = 340;
        if (kind == "gate" || kind == "free" || kind == "free-high")
        {
            obstacles.Add(new Obstacle { Id = "gate", Kind = "gate", X = 80, Width = 16,
                GapTop = 110, GapBottom = 130, BottomOffsetX = 112 });
            if (kind != "gate") zones.Add(new RiskZone { Id = kind, Kind = "instant",
                X = kind == "free" ? 114 : 130, Y = kind == "free" ? 120 : 70, Radius = 8 });
            end = kind == "gate" ? 300 : 380;
        }
        if (kind == "before")
        {
            obstacles.Add(new Obstacle { Id = "stone", Kind = "floor", X = 180, Width = 64, Height = 92 });
            zones.Add(new RiskZone { Id = "before", ObstacleId = "stone", X = 80, Y = 120, Radius = 8, ClearX = 259 });
            end = 500;
        }
        if (kind == "after")
        {
            obstacles.Add(new Obstacle { Id = "stone", Kind = "floor", X = 100, Width = 64, Height = 92 });
            zones.Add(new RiskZone { Id = "after", ObstacleId = "stone", Placement = "after", X = 260, Y = 40, Radius = 8, ClearX = 283 });
            end = 540;
        }
        if (kind == "chain")
        {
            obstacles.Add(new Obstacle { Id = "first", Kind = "floor", X = 100, Width = 64, Height = 48 });
            obstacles.Add(new Obstacle { Id = "second", Kind = "ceiling", X = 220, Width = 64, Height = 48 });
            zones.Add(new RiskZone { Id = "chain-1", Kind = "instant", X = 70, Y = 120, Radius = 8 });
            zones.Add(new RiskZone { Id = "chain-2", Kind = "instant", X = 190, Y = 120, Radius = 8 });
            end = 440;
        }
        return new Track(end + 20, obstacles.ToArray()) { Id = kind, RiskZones = zones.ToArray() };
    }
    static Node Replay(Track track, FlightSettings flight, List<double> inputs, int side, bool full,
        int shifted = -1, double seconds = 0, List<double> baselineTimes = null)
    {
        var node = new Node { Game = Begin(track, flight), Inputs = new List<double>(inputs) };
        try
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                if (i == shifted) AdvanceTime(node.Game, baselineTimes[i] + seconds - node.Game.Elapsed, .2);
                else MoveTo(node.Game, inputs[i], .2);
                if (!RaceSimulation.Press(node.Game, 0) || !RaceSimulation.Press(node.Game, 1)) return null;
                if (!Viable(node, full)) return null;
            }
            MoveTo(node.Game, track.Length - 20, .2);
            var p = node.Game.Players[0];
            if (!Viable(node, full) || p.Support != SupportKind.Wall || p.SupportSide != side || p.Flipping
                || (full && p.BoostsEarned != track.RiskZones.Count(z => !z.IsNegative && !z.IsFinish))) return null;
            return node;
        }
        catch (Exception) { return null; }
    }
    static List<double> Times(Track track, FlightSettings flight, List<double> inputs)
    {
        var game = Begin(track, flight); var times = new List<double>();
        foreach (double x in inputs)
        { MoveTo(game, x, .2); times.Add(game.Elapsed); RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); }
        return times;
    }
    static Node Center(Track track, FlightSettings flight, Node node, int side, bool full)
    {
        // Coordinate refinement of a found witness, then actual-time checks.
        // Kept offline; this never changes input acceptance or collision rules.
        for (int pass = 0; pass < 3; pass++) for (int i = 0; i < node.Inputs.Count; i++)
        {
            double original = node.Inputs[i], low = original, high = original;
            for (int sign = -1; sign <= 1; sign += 2)
            for (double delta = 1; delta <= 80; delta++)
            {
                double candidate = original + sign * delta;
                if (candidate < 0 || candidate >= track.Length - 20 || (i > 0 && candidate <= node.Inputs[i-1])
                    || (i + 1 < node.Inputs.Count && candidate >= node.Inputs[i+1])) break;
                var route = new List<double>(node.Inputs); route[i] = candidate;
                if (Replay(track, flight, route, side, full) == null) break;
                if (sign < 0) low = candidate; else high = candidate;
            }
            var centered = new List<double>(node.Inputs); centered[i] = (low + high) / 2;
            node = Replay(track, flight, centered, side, full) ?? node;
        }
        return node;
    }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        captureCollision = false;
        if(args.Length>0 && args[0]=="--refine-targets") { RefineTargets(args); return; }
        if(args.Length>0 && args[0]=="--export-modules") { ExportModules(args); return; }
        if(args.Length>0 && args[0]=="--chain-subsets") { ChainSubsets(args.Contains("--r6-shifts")); return; }
        var rows = new List<object>();
        foreach (string kind in (args.Length > 0 ? args : new[] { "gate", "free", "free-high", "before", "after", "chain", "pad", "large" }))
        foreach (var flight in new[] { FlightPresets.FastFlip, FlightPresets.Chosen })
        foreach (bool full in new[] { false, true })
        {
            var track = Module(kind);
            int side = kind == "gate" || kind.StartsWith("free") || kind == "before" ? 0 : 1;
            var found = Search(Begin(track, flight), track.Length - 20, side, full);
            if (kind == "pad" && full && flight.FirstAction == FirstAction.Jump)
                found = Replay(track, flight, new List<double> { 232 - 220 * (350 + Math.Sqrt(350*350 - 1325*48)) / 662.5 }, side, full) ?? found;
            if (found != null) found = Center(track, flight, found, side, full);
            var times = found == null ? null : Times(track, flight, found.Inputs);
            var windows = new List<double>();
            if (found != null) for (int i = 0; i < found.Inputs.Count; i++)
            {
                double radius = 0;
                for (double d = .005; d <= .1501; d += .005)
                {
                    if (Replay(track, flight, found.Inputs, side, full, i, -d, times) == null
                        || Replay(track, flight, found.Inputs, side, full, i, d, times) == null) break;
                    radius = d;
                }
                windows.Add(radius);
            }
            string name = (flight.FirstAction == FirstAction.Flip ? "Fast" : "Main") + (full ? " full" : " safe");
            Console.WriteLine(name + " " + kind + ": " + (found == null ? "no witness" : string.Join(",", found.Inputs))
                + "; windows=" + string.Join(",", windows));
            rows.Add(new { name, kind, route = found?.Inputs,
                times, windows, time = found?.Game.Elapsed, boosts = found?.Game.Players[0].BoostsEarned });
        }
        File.WriteAllText("Verification/rhythm-module-author-search.json", JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
}
