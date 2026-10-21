using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static int games;
    static GameState Replay(Track track, FlightSettings flight, double[] inputs)
    {
        games++;
        var game = RaceSimulation.CreateTrial(track, flight);
        Tap(game); RaceSimulation.Advance(game, 3);
        try {
            foreach (double x in inputs) { MoveTo(game, x, .317); Tap(game); }
            Finish(game, .317);
            return game;
        } catch (Exception) { return null; }
    }
    static bool Good(GameState game, bool full)
        => game != null && game.Players.All(p => p.Collisions == 0 && p.SlowsEarned == 0
            && p.BoostsEarned == (full ? 9 : 0) && p.FinishBoostsEarned == (full ? 1 : 0));
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        captureCollision = false;
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Assets/Scripts/Core/TracksMixed.cs")));
        var rows = new List<object>();
        foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
        foreach (bool full in new[] { false, true }) {
            var inputs = full ? TracksMixed.FullRoute(flight) : TracksMixed.SafeRoute(flight);
            var track = TracksMixed.Get();
            Check(Good(Replay(track, flight, inputs), full), "Original witness must pass before review.");
            var findings = new List<object>();
            for (int count = 1; count <= 2; count++)
            for (int first = 0; first <= inputs.Length - count; first++) {
                var shortened = inputs.Where((_, i) => i < first || i >= first + count).ToArray();
                var game = Replay(track, flight, shortened);
                if (!Good(game, full)) continue;
                var removed = inputs.Skip(first).Take(count).ToArray();
                findings.Add(new { first, count, removed, inputs = shortened, finish = game.Players[0].FinishTime });
                Console.WriteLine($"REVIEW {flight.FirstAction}/{(full ? "full" : "safe")} remove [{string.Join(",", removed)}]: clean with same rewards.");
            }
            rows.Add(new { profile = flight == FlightPresets.Chosen ? "chosen" : "fast-flip", variant = full ? "full" : "safe", original = inputs, findings });
        }
        string output = args.Length > 0 ? args[0] : "Verification/mixed-route-review.json";
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            scope = "Bounded removal review: each single input and adjacent input pair. Nominal replay only; not a minimality, semantic-equivalence or tolerance claim.",
            sourceHash = hash, games, rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Review completed: {games} games. Findings require author judgement before changing witnesses.");
    }
}
