using System;
using FlipRace.Core;

internal static partial class Program
{
    static readonly MotionMode[] ControlModes = { MotionMode.GravityAirFlip, MotionMode.LinearAirFlip,
        MotionMode.JumpGravityFlip, MotionMode.GravityUnlimited };
    static MotionProfile ControlProfile(MotionMode mode)
    { return mode == MotionMode.JumpGravityFlip ? MotionProfiles.Jump : mode == MotionMode.LinearAirFlip
        ? MotionProfiles.Baseline : MotionProfiles.Moderate; }

    static void DraftRoutes()
    {
        foreach (var mode in ControlModes)
        foreach (bool ideal in new[] { false, true })
        {
            var track = TracksControl.Challenge(mode);
            var route = ideal ? track.RiskRoute : track.SafeRoute;
            var game = Start(track, mode, ControlProfile(mode));
            var result = new RouteResult { Game = game };
            foreach (double x in route)
            {
                MoveTo(game, x, .01);
                var p = game.Players[0];
                Console.WriteLine(mode + " " + (ideal ? "ideal" : "safe") + " input " + x.ToString("F3")
                    + " t=" + game.Elapsed.ToString("F6") + " y=" + RaceSimulation.GetPlayerCenterY(p).ToString("F3")
                    + " support=" + p.Support + "/" + p.SupportObstacleId + " vy=" + p.VerticalSpeed.ToString("F3")
                    + " hits=" + p.Collisions + " rewards=" + p.BoostsEarned);
                Tap(game, result, !ideal && mode != MotionMode.JumpGravityFlip);
            }
            Finish(game, .03);
            var player = game.Players[0];
            Console.WriteLine("RESULT " + mode + " " + (ideal ? "ideal" : "safe") + " t=" + player.FinishTime
                + " positive=" + player.BoostsEarned + " negative=" + player.SlowsEarned + " hits=" + player.Collisions
                + " hitIDs=" + string.Join(",", player.HitObstacleIds) + " rewards=" + string.Join(",", player.SuccessfulRiskIds));
        }
    }

    static bool HasOutcome(RouteResult result, int positives, int negatives)
    {
        foreach (var p in result.Game.Players)
            if (p.Collisions != 0 || p.BoostsEarned + p.FinishBoostsEarned != positives || p.SlowsEarned != negatives
                || !p.FinishTime.HasValue || p.ActiveEffect != null || p.PendingEffects.Count != 0) return false;
        return true;
    }
    static void RouteWindows()
    {
        foreach (var mode in ControlModes)
        foreach (bool ideal in new[] { false, true })
        {
            var track = TracksControl.Challenge(mode); var profile = ControlProfile(mode);
            double[] route = ideal ? track.RiskRoute : track.SafeRoute;
            bool supported = !ideal && mode != MotionMode.JumpGravityFlip;
            var baseline = Route(track, mode, profile, route, .01, supported);
            CheckClean(baseline, ideal ? 3 : 0, 0, ideal ? 20.3 : 21);
            double minEarly = 1, minLate = 1;
            for (int edge = 0; edge < route.Length; edge++)
            {
                double[] windows = { 0, 0 };
                bool finishInput = ideal && edge == route.Length - 1;
                int grid = finishInput ? 1 : 5;
                for (int direction = 0; direction < 2; direction++)
                for (int ms = grid; ms <= 150; ms += grid)
                {
                    int savedChecks = checks;
                    bool passed;
                    try
                    {
                        var shifted = TimedShift(track, mode, profile, route, baseline, edge,
                            ms / 1000d * (direction == 0 ? -1 : 1), supported);
                        passed = HasOutcome(shifted, ideal ? 3 : 0, 0);
                    }
                    catch { passed = false; }
                    checks = savedChecks;
                    if (!passed) break;
                    windows[direction] = ms;
                }
                Console.WriteLine(mode + " " + (ideal ? "ideal" : "safe") + " input" + edge
                    + " x=" + route[edge] + " actual windows -" + windows[0] + "/+" + windows[1] + "ms");
                minEarly = Math.Min(minEarly, windows[0] / 1000); minLate = Math.Min(minLate, windows[1] / 1000);
                Check(windows[0] >= (finishInput ? 20 : 40) && windows[1] >= (finishInput ? 20 : 40),
                    "Finish target is deliberately precise; preceding route keeps its previous tolerance");
            }
            Console.WriteLine("MIN " + mode + " " + (ideal ? "ideal" : "safe") + " -" + minEarly + "/+" + minLate);
            Check(minEarly >= (ideal ? .02 : .04) && minLate >= (ideal ? .02 : .04), "Comparison timing windows satisfy the measured trial targets");
        }
    }

    static void TrapSearch()
    {
        foreach (var mode in ControlModes)
        {
            var track = TracksControl.Challenge(mode); double first = 0, last = 0;
            for (int x = 2960; x <= 3130; x++)
            {
                var route = (double[])track.RiskRoute.Clone();
                route[6] = x;
                if (mode == MotionMode.JumpGravityFlip) route[7] = x + 55;
                try
                {
                    var result = Route(track, mode, ControlProfile(mode), route, .02, false);
                    if (!HasOutcome(result, 3, 1)) continue;
                    if (first == 0) first = x;
                    last = x;
                }
                catch { }
            }
            Console.WriteLine(mode + " intentional trap input interval " + first + ".." + last);
        }
    }
}
