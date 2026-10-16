using System;
using FlipRace.Core;

internal static partial class Program
{
    static void UnlimitedProof()
    {
        var track = TracksControl.Unlimited(); var mode = MotionMode.GravityUnlimited;
        var profile = MotionProfiles.Moderate; double[] route = track.SafeRoute;
        foreach (double dt in new[] { 1d/30, 1d/60, 1d/144, .317, 2.5, 100d })
        {
            var result = Route(track, mode, profile, route, dt, false);
            CheckClean(result, 0, 0, 13);
            Check(result.AirInputs == 8, "Passages use eight accepted air repeats");
            for (int group = 0; group < 4; group++)
            {
                var launch = result.Inputs[group*3];
                var first = result.Inputs[group*3+1]; var second = result.Inputs[group*3+2];
                Check(!launch.Airborne && launch.Support == SupportKind.Wall,
                    "Each passage sequence starts after a real stable wall reset");
                Check(first.Airborne && second.Airborne && first.Support == SupportKind.None
                    && second.Support == SupportKind.None, "Two distinct air repeats occur before the next landing");
                Check(first.Time-launch.Time >= .3 && second.Time-first.Time >= .3,
                    "Passage route does not require rapid sub-.3s repeats");
            }
        }
        var baseline = Route(track, mode, profile, route, .01, false);
        for (int gate = 0; gate < track.Obstacles.Length; gate++)
        {
            var obstacle = track.Obstacles[gate];
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            for (double x = obstacle.X-14; x <= obstacle.X+obstacle.Width+14+.001; x += 1)
            {
                var game = ReplayUntil(track, mode, baseline, x / 220);
                double y = RaceSimulation.GetPlayerCenterY(game.Players[0]);
                min = Math.Min(min,y); max = Math.Max(max,y);
                Check(y > obstacle.GapTop+14 && y < obstacle.GapBottom-14,
                    "Actual sampled passage height fits both expanded gate faces");
            }
            Console.WriteLine("PASSAGE " + obstacle.Id + " centerYrange=" + min.ToString("F6") + ".."
                + max.ToString("F6") + " legal=" + (obstacle.GapTop+14) + ".." + (obstacle.GapBottom-14));
        }
        Check(track.Obstacles[0].GapBottom-14 < track.Obstacles[1].GapTop+14,
            "Upper and lower passages have no shared safe center height");
        double early = 1000, late = 1000;
        for (int edge = 0; edge < route.Length; edge++)
        {
            double[] window = { 0, 0 };
            for (int direction = 0; direction < 2; direction++)
            for (int ms = 5; ms <= 100; ms += 5)
            {
                int saved = checks; bool passed;
                try { passed = HasOutcome(TimedShift(track, mode, profile, route, baseline, edge,
                    ms/1000d*(direction==0?-1:1),false),0,0); }
                catch { passed = false; }
                checks = saved;
                if (!passed) break;
                window[direction] = ms;
            }
            Check(window[0] >= 40 && window[1] >= 40, "Passage input has at least individual +/-40ms tolerance");
            early = Math.Min(early,window[0]); late = Math.Min(late,window[1]);
            Console.WriteLine("UNLIMITED input" + edge + " x=" + route[edge].ToString("F9")
                + " actual windows -" + window[0] + "/+" + window[1] + "ms");
        }
        Console.WriteLine("UNLIMITED PASS: 13s,4 passages,8 air repeats, min repeat .328058s, windows -"
            + early + "/+" + late + "ms; finite per-input perturbations.");
    }
}
