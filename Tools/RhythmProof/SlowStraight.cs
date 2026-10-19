using System;
using FlipRace.Core;

internal static partial class Program
{
    // This is a held-speed isolation fixture, not an attainable reward history
    // in the reward-free stage. At 110 the straight gate needs a different
    // Fast path: departure + one air return, landing on its original wall.
    static void SlowStraight()
    {
        int runs = 0;
        foreach (var flight in Profiles) foreach (int side in new[] { 0, 1 })
        foreach (double step in Steps) {
            var gate = new Obstacle { Id = "isolated-straight", Kind = "gate", X = 1000,
                Width = 8, GapTop = 12, GapBottom = 228 };
            var game = Begin(new Track(1400, new[] { gate }), flight);
            if (side == 0) {
                Tap(game);
                if (flight == FlightPresets.Chosen) { AdvanceTime(game, .2, step); Tap(game); }
                AdvanceTime(game, 1.5, step);
            }
            foreach (var p in game.Players) {
                Check(p.Support == SupportKind.Wall && p.SupportSide == side, "real slow fixture origin wall");
                p.ActiveEffect = new SpeedEffect { RingId = "held-slow-fixture", IsNegative = true,
                    Remaining = 100, PulseCount = 200 };
            }
            double launch = flight == FlightPresets.FastFlip ? 950 : 1004 - 110 * 350 / 662.5;
            MoveTo(game, launch, step); Tap(game, null, true);
            if (flight == FlightPresets.FastFlip) {
                AdvanceTime(game, .2, step);
                Check(game.Players[0].Flipping && game.Players[0].AirFlipAvailable, "one legal air return");
                Tap(game);
                Check(!RaceSimulation.Press(game, 0) && !RaceSimulation.Press(game, 1), "second air repeat remains rejected");
            }
            Finish(game, step, false);
            foreach (var p in game.Players) {
                Check(p.Collisions == 0, "slow straight clean");
                Check(p.Support == SupportKind.Wall && p.SupportSide == side && !p.Flipping, "same-wall slow route actually lands");
                Check(p.LastFlight != null && !p.LastFlight.Interrupted && p.LastFlight.OriginSide == side, "same-wall flight measurement");
            }
            runs++;
        }
        Console.WriteLine("SLOW STRAIGHT PASS: " + runs + " isolated held-110 routes, both walls/six dt; Fast uses one air return. Normal one-flip path at 110 is not claimed.");
    }
}
