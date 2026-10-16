using System;
using FlipRace.Core;

internal static partial class Program
{
    static GameState ReplayUntil(Track track, MotionMode mode, RouteResult baseline, double time)
    {
        var game = Start(track, mode, ControlProfile(mode));
        foreach (var edge in baseline.Inputs)
        {
            if (edge.Time >= time - 1e-9) break;
            AdvanceTime(game, edge.Time - game.Elapsed, .317); Tap(game);
        }
        AdvanceTime(game, time - game.Elapsed, .317);
        return game;
    }
    static void LandingRouteProof(Track track, MotionMode mode, RouteResult ideal)
    {
        Check(ideal.Rewards.Count == 3, "Route records both actual landing events and final plane boost");
        double[] supportDurations = new double[2]; int number = 0;
        foreach (var reward in ideal.Rewards)
        {
            var zone = Array.Find(track.RiskZones, z => z.Id == reward.Id);
            if (!zone.IsLanding) continue;
            var stone = Array.Find(track.Obstacles, o => o.Id == zone.ObstacleId);
            var contact = ReplayUntil(track, mode, ideal, reward.At);
            var p = contact.Players[0];
            double plane = stone.Kind == "floor" ? 240 - stone.Height - 14 : stone.Height + 14;
            Check(Math.Abs(p.Distance - zone.X) <= zone.LandingWidth / 2 + 1e-7, "Actual landing center inside marker");
            Near(RaceSimulation.GetPlayerCenterY(p), plane, "Actual contact matches stone plane");
            Check(p.Support == SupportKind.Stone && p.SupportObstacleId == stone.Id && !p.Flipping
                && p.AirFlipAvailable, "Ideal landing is stable support and restores charge");
            Check(p.Collisions == 0 && p.SuccessfulRiskIds.Contains(zone.Id), "Contact actually earns legal reward");
            var run = Clone(contact); AdvanceTime(run, .3, .317);
            Check(run.Players[0].Support == SupportKind.Stone && run.Players[0].SupportObstacleId == stone.Id,
                "At least .3 seconds of boosted supported run after each central landing");
            Near(run.Players[0].Distance - p.Distance, 99, "Immediate reward changes supported speed");
            Check(run.Players[0].BoostsEarned == p.BoostsEarned && run.Players[0].Collisions == 0,
                "Run outside narrow marker keeps support without repeated reward");
            Tap(run); AdvanceTime(run, .08, .317);
            Check(run.Players[0].Support == SupportKind.None && run.Players[0].Flipping
                && run.Players[0].Collisions == 0, "Every mode can deliberately depart either stone after a visible run");
            Check(stone.Kind == "floor" ? RaceSimulation.GetPlayerCenterY(run.Players[0]) < plane
                : RaceSimulation.GetPlayerCenterY(run.Players[0]) > plane, "Departure uses physical stone face");
            var edge = Clone(contact); MoveTo(edge, stone.X + stone.Width + 1, .317);
            Check(edge.Players[0].Support == SupportKind.None && edge.Players[0].Flipping
                && edge.Players[0].Collisions == 0, "Boosted walkoff removes concrete support without a new hit");
            supportDurations[number++] = (stone.X + stone.Width - p.Distance) / 330;
            Console.WriteLine("LANDING " + mode + " " + zone.Id + " actual=" + reward.At.ToString("F9")
                + " x=" + p.Distance.ToString("F6") + " stable=true boostedRunToEdge=" + supportDurations[number-1].ToString("F6"));
        }
        Check(number == 2, "Both landing sides proved");
        double finalAt = ideal.Rewards[2].At;
        Near(ideal.Game.Players[0].FinishTime.Value - finalAt, .1, "Actual final 44 units take .1 seconds");
        Console.WriteLine("METRICS " + mode + " first=" + ideal.Inputs[0].Time.ToString("F6")
            + " lastToFinish=" + (ideal.Game.Players[0].FinishTime.Value-ideal.Inputs[ideal.Inputs.Count-1].Time).ToString("F6")
            + " finalContactToFinish=" + (ideal.Game.Players[0].FinishTime.Value-finalAt).ToString("F6"));
    }
    static void ComparisonProof()
    {
        SnapshotContract();
        foreach (var mode in ControlModes)
        {
            var track = TracksControl.Challenge(mode); var profile = ControlProfile(mode);
            foreach (double dt in new[] { 1d/30, 1d/60, 1d/144, .317, 2.5, 100d })
            {
                CheckClean(Route(track, mode, profile, track.SafeRoute, dt, mode != MotionMode.JumpGravityFlip), 0, 0, 21);
                CheckClean(Route(track, mode, profile, track.RiskRoute, dt, false), 3, 0, 20.3);
                CheckClean(Route(track, mode, profile, TracksControl.NegativeRoute(mode), dt, false), 3, 1, 20.6);
            }
            var ideal = Route(track, mode, profile, track.RiskRoute, .01, false);
            LandingRouteProof(track, mode, ideal);
            Console.WriteLine("COMPARE " + mode + " safe21.0 ideal20.3 trap20.6; zero hits, ordinary pulses served, separate finish boost.");
        }
        Console.WriteLine("COMPARISON PASS " + checks + " checks.");
    }
}
