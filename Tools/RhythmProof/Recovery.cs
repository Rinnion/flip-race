using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static GameState PerturbedCollision(Track track, FlightSettings flight,
        RouteResult baseline, int changed, double offset)
    {
        var game = Begin(track, flight);
        firstCollision = null; captureCollision = true;
        try {
            for (int i = 0; i < baseline.Inputs.Count && firstCollision == null; i++) {
                double time = baseline.Inputs[i].Time + (i == changed ? offset : 0);
                if (time < game.Elapsed - 1e-9) return null;
                AdvanceTime(game, time - game.Elapsed, .002);
                if (firstCollision != null) break;
                RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1);
            }
            while (firstCollision == null && game.Phase == "racing") Step(game, .002);
            return firstCollision;
        }
        finally { captureCollision = false; firstCollision = null; }
    }

    static bool BothPress(GameState game)
    { bool a = RaceSimulation.Press(game, 0), b = RaceSimulation.Press(game, 1); return a && b; }

    static bool ReachesWithoutHit(GameState game, double target, int hits)
    {
        var copy = Clone(game);
        while (copy.Phase == "racing" && copy.Players[0].Distance < target - 1e-8) {
            RaceSimulation.Advance(copy, Math.Min(.02, (target - copy.Players[0].Distance) / 440));
            if (copy.Players[0].Collisions != hits || copy.Players[1].Collisions != hits) return false;
        }
        return copy.Players[0].Distance >= target - 1e-7
            && copy.Players[0].Collisions == hits && copy.Players[1].Collisions == hits;
    }

    static bool WallThenNext(GameState state, Obstacle next, double target, int hits, out double[] inputs)
    {
        inputs = null;
        // This continuation is only for the verified compact alternating gate.
        if (next.GapTop != 110 || next.GapBottom != 130) return false;
        var game = Clone(state); var p = game.Players[0];
        while (p.Support != SupportKind.Wall || p.Flipping) {
            if (p.Collisions != hits || p.Distance >= target || game.Phase != "racing") return false;
            RaceSimulation.Advance(game, .005);
        }
        int side = next.TopOffsetX < next.BottomOffsetX ? 1 : 0;
        double launch = next.X + (game.Flight.FirstAction == FirstAction.Flip ? 5.1503213562917 : -19.1551226858691);
        if (p.SupportSide != side || p.Distance > launch) return false;
        MoveTo(game, launch, .01);
        double first = game.Elapsed - state.Elapsed;
        if (p.Collisions != hits || !BothPress(game)) return false;
        if (game.Flight.FirstAction == FirstAction.Jump) {
            AdvanceTime(game, .2);
            if (p.Collisions != hits || !BothPress(game)) return false;
            inputs = new[] { first, first + .2 };
        }
        else inputs = new[] { first };
        return ReachesWithoutHit(game, target, hits);
    }

    static bool Rescue(GameState collision, out double target, out double[] taps)
    {
        taps = null; var ready = Clone(collision); var p = ready.Players[0]; int hits = p.Collisions;
        var obstacles = new List<Obstacle>(ready.Track.Obstacles);
        obstacles.Sort((a,b) => ObstacleGeometry.Left(a).CompareTo(ObstacleGeometry.Left(b)));
        int index = obstacles.FindIndex(o => o.Id == p.LastHit.Id);
        Check(index >= 0, "Collision belongs to authored course");
        target = index + 1 < obstacles.Count ? ObstacleGeometry.Right(obstacles[index+1]) + 15 : ready.Track.Length;
        AdvanceTime(ready, p.Recovering + .15, .002);
        if (p.Collisions != hits) return false;
        if (ReachesWithoutHit(ready, target, hits)) { taps = Array.Empty<double>(); return true; }
        Obstacle next = index + 1 < obstacles.Count ? obstacles[index+1] : null;
        if (next != null && WallThenNext(ready, next, target, hits, out taps)) return true;
        double maxDelay = Math.Min(3, (target-p.Distance)/220);
        for (double delay = 0; delay <= maxDelay + 1e-9; delay += .04) {
            var one = Clone(ready); AdvanceTime(one, delay, .02);
            if (one.Players[0].Collisions != hits || !BothPress(one)) continue;
            if (ReachesWithoutHit(one, target, hits)) { taps = new[] { delay }; return true; }
            if (next != null && WallThenNext(one, next, target, hits, out double[] afterOne)) {
                var all = new List<double> { delay }; foreach (double t in afterOne) all.Add(delay+t);
                taps = all.ToArray(); return true;
            }
            for (double second = .08; second <= 1.2 + 1e-9; second += .04) {
                var two = Clone(one); AdvanceTime(two, second, .02);
                if (two.Players[0].Collisions != hits || !BothPress(two)) continue;
                if (ReachesWithoutHit(two, target, hits)) { taps = new[] { delay, delay+second }; return true; }
                if (next != null && WallThenNext(two, next, target, hits, out double[] afterTwo)) {
                    var all = new List<double> { delay, delay+second }; foreach (double t in afterTwo) all.Add(delay+second+t);
                    taps = all.ToArray(); return true;
                }
            }
        }
        return false;
    }

    static void StageRecovery()
    {
        var track = TracksRhythm.Practice(); var rows = new List<object>(); int failed = 0;
        foreach (var flight in Profiles) {
            var baseline = Run(track, flight, TracksRhythm.PracticeRoute(flight));
            for (int i = 0; i < baseline.Inputs.Count; i++)
            foreach (double offset in new[] { -.64, -.4, -.16, .16, .4, .64 }) {
                var collision = PerturbedCollision(track, flight, baseline, i, offset);
                if (collision == null) continue;
                bool found = Rescue(collision, out double target, out double[] taps);
                var p = collision.Players[0];
                rows.Add(new { profile = ProfileId(flight), input = i, offset,
                    hit = p.LastHit, collisionX = p.Distance, centerY = RaceSimulation.GetPlayerCenterY(p),
                    p.VerticalSpeed, p.Support, p.SupportSide, p.AirFlipAvailable,
                    remainingRecovery = p.Recovering, reaction = .15, target, found, tapsAfterReaction = taps });
                if (!found) { failed++; Console.WriteLine("NO RESCUE " + ProfileId(flight) + " input=" + i + " shift=" + offset + " hit=" + p.LastHit.Id + " x=" + p.Distance); }
            }
        }
        File.WriteAllText("Verification/rhythm-stage2-recovery.json", JsonSerializer.Serialize(rows,
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Check(rows.Count > 0 && failed == 0, "Finite recovery catalogue: " + failed + " cases without found escape");
        Console.WriteLine("STAGE2 RECOVERY PASS: " + rows.Count + " real collision snapshots; penalty +150ms; physical next unionRight+15 reached. Finite error sample, not all histories.");
    }
}
