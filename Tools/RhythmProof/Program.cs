using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static readonly double[] Steps = { 1d/30, 1d/60, 1d/144, .317, 2.5, 100 };
    static readonly FlightSettings[] Profiles = { FlightPresets.Chosen, FlightPresets.FastFlip };
    static string ProfileId(FlightSettings f) { return f == FlightPresets.FastFlip ? "fast-flip" : "chosen"; }
    static GameState Begin(Track track, FlightSettings flight)
    {
        var game = RaceSimulation.CreateTrial(track, flight);
        Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "both ready");
        RaceSimulation.Advance(game, 3);
        return game;
    }
    static RouteResult Run(Track track, FlightSettings flight, double[] route, double step = .01, int omit = -1)
    {
        var r = new RouteResult { Game = Begin(track, flight) };
        for (int i = 0; i < route.Length; i++) {
            if (i == omit) continue;
            MoveTo(r.Game, route[i], step);
            if (omit < 0) Tap(r.Game, r);
            else { RaceSimulation.Press(r.Game, 0); RaceSimulation.Press(r.Game, 1); }
        }
        Finish(r.Game, step);
        return r;
    }
    static RouteResult Shift(Track track, FlightSettings flight, double[] route, RouteResult baseline, int changed, double seconds)
    {
        var r = new RouteResult { Game = Begin(track, flight) };
        for (int i = 0; i < route.Length; i++) {
            if (i == changed) AdvanceTime(r.Game, baseline.Inputs[i].Time + seconds - r.Game.Elapsed);
            else MoveTo(r.Game, route[i], .01);
            Tap(r.Game, r);
        }
        Finish(r.Game, .317);
        return r;
    }
    static void Clean(RouteResult r)
    {
        foreach (var p in r.Game.Players) {
            Check(p.Collisions == 0, "clean route: " + string.Join(",", p.HitObstacleIds));
            Check(p.BoostsEarned == 0 && p.SlowsEarned == 0, "no unplanned rewards");
            Near(p.FinishTime.Value, r.Game.Track.Length / 220, "exact safe finish");
        }
    }
    static void Stage()
    {
        var track = TracksRhythm.Practice(); var rows = new List<object>();
        Check(track.Obstacles.Length == 10, "two intro, six-chain, straight and low gate");
        foreach (var gate in track.Obstacles) {
            Check(ObstacleGeometry.TryGetBand(gate, 0, out var upper) && upper.Height > 0, "real upper part");
            Check(ObstacleGeometry.TryGetBand(gate, 1, out var lower) && lower.Height > 0, "real lower part");
        }
        foreach (var f in Profiles) foreach (bool quick in new[] { false, true }) {
            var route = quick ? TracksRhythm.PracticeQuickReturnRoute(f) : TracksRhythm.PracticeRoute(f);
            foreach (double step in Steps) { var r = Run(track, f, route, step); Clean(r); CheckSupport(r, f, quick); }
            var baseline = Run(track, f, route);
            var windows = new double[route.Length];
            for (int i = 0; i < route.Length; i++) {
                bool optionalInput = quick && i >= route.Length - (f == FlightPresets.FastFlip ? 2 : 3);
                windows[i] = optionalInput ? (f == FlightPresets.FastFlip ? .04 : .03) : .06;
                foreach (double shift in new[] { -windows[i], windows[i] })
                    Clean(Shift(track, f, route, baseline, i, shift));
            }
            // The alternative optional route is not a minimal-tap claim.
            // Check every required normal action and the six-action Fast chain.
            for (int i = 0; i < (quick ? (f == FlightPresets.FastFlip ? 8 : 16) : route.Length); i++)
                Check(Run(track, f, route, .01, i).Game.Players[0].Collisions > 0, "each stage action matters: " + i);
            rows.Add(new { id = track.Id, profile = ProfileId(f), preset = f == FlightPresets.FastFlip ? 11 : 10,
                variant = quick ? "quick-return" : "normal", route, inputToleranceSeconds = windows,
                inputs = baseline.Inputs, obstacles = track.Obstacles, finish = baseline.Game.Players[0].FinishTime,
                finalFlight = baseline.Game.Players[0].LastFlight });
            Console.WriteLine(ProfileId(f) + " " + (quick ? "quick-return" : "normal") + ": " + route.Length
                + " accepted inputs; six dt, real support, individual timing tolerance PASS");
        }
        File.WriteAllText("Verification/rhythm-stage2-routes.json", JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine("RHYTHM-01 STAGE2 PASS " + checks + " checks");
    }

    static void CheckSupport(RouteResult r, FlightSettings f, bool quick)
    {
        int compact = f == FlightPresets.FastFlip ? 8 : 16;
        for (int i = 0; i < compact; i++) {
            bool air = f != FlightPresets.FastFlip && i % 2 == 1;
            Check(r.Inputs[i].Airborne == air, "compact input uses real departure/repeat");
            if (!air) {
                Check(r.Inputs[i].Support == SupportKind.Wall, "compact actual wall recharge");
                Check(r.Inputs[i].SupportSide == ((f == FlightPresets.FastFlip ? i : i / 2) % 2 == 0 ? 1 : 0), "mirrored origin side");
            }
        }
        Check(!r.Inputs[compact].Airborne && r.Inputs[compact].SupportSide == 1, "straight starts on floor");
        int low = r.Inputs.Count - (quick ? (f == FlightPresets.FastFlip ? 2 : 3) : 1);
        Check(!r.Inputs[low].Airborne && r.Inputs[low].Support == SupportKind.Wall, "low begins on actual wall");
        Check(r.Inputs[low].SupportSide == (!quick && f == FlightPresets.FastFlip ? 0 : 1), "low normal versus same-wall approach");
        for (int i = low + 1; i < r.Inputs.Count; i++) Check(r.Inputs[i].Airborne && r.Inputs[i].AirFlipAvailable, "legal optional repeat");
        foreach (var p in r.Game.Players) {
            Check(p.Support == SupportKind.Wall && p.SupportSide == 1 && !p.Flipping, "low finishes on floor");
            Check(p.LastFlight != null && !p.LastFlight.Interrupted, "last flight actually lands cleanly");
            if (quick) Check(p.LastFlight.OriginSide == 1, "quick route returns to its original floor");
        }
    }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; captureCollision = false;
        try {
            if (args.Length > 0 && args[0] == "--recovery") StageRecovery();
            else if (args.Length > 0 && args[0] == "--straight-slow") SlowStraight();
            else if (args.Length > 0 && args[0] == "--verify") { Stage(); StageRecovery(); SlowStraight(); Console.WriteLine("STAGE2 ALL PASS " + checks + " checks"); }
            else Stage();
        }
        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
    }
}
