using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

// A distance-domain workload bound, independent of the author's input routes.
// No simulation state is injected and no published course is modified.
internal static class Program
{
    sealed record Release(string Id, string Kind, double LatestContact, double LatestRelease, double Work);
    static int checks;
    static void Require(bool condition, string reason)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(reason);
    }
    static void Near(double value, double expected, string reason)
        => Require(Math.Abs(value - expected) < 1e-8, reason);
    static string Hash(string relativePath)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(relativePath)));

    static int Main()
    {
        try
        {
            // Reflection lets the checker compile before the new authoring file
            // exists; invoking it still fails unless the real production API exists.
            var type = typeof(Track).Assembly.GetType("FlipRace.Core.TracksMixed")
                ?? throw new InvalidOperationException("TracksMixed is not available yet; no tail verdict.");
            var get = type.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)
                ?? throw new InvalidOperationException("TracksMixed.Get() is missing.");
            var track = (Track)(get.Invoke(null, null) ?? throw new InvalidOperationException("Null course."));
            var coreHash = Hash("Assets/Scripts/Core/RaceSimulation.cs");
            var geometryHash = Hash("Assets/Scripts/Core/ObstacleGeometry.cs");
            Require(coreHash == "AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24", "Effect/queue/collision physics changed from 4.6.1; re-audit the proof assumptions.");
            Require(geometryHash == "BB1A5F4F79981CCDF3DABC4E9DB0511C3F9D9247B9E712ACC3FABB717653B92B", "Obstacle union implementation changed; re-audit release bounds.");
            Near(track.Length, 7920, "Course must retain 36 base seconds.");
            Near(RaceConfig.Speed, 220, "Base speed.");
            Near(RaceConfig.BoostDuration, .6, "Positive pulse duration.");
            Near(RaceConfig.SlowDuration, .6, "Negative pulse duration.");
            Near(RaceConfig.BoostMultiplier, 1.5, "Positive speed multiplier.");
            Near(RaceConfig.SlowMultiplier, .5, "Negative speed multiplier.");
            Near(RaceConfig.FinishBoostDistance, 44, "Finish-only distance.");
            double finishX = track.Length - RaceConfig.FinishBoostDistance;
            var finishes = track.RiskZones.Where(z => z.IsFinish).ToArray();
            Require(finishes.Length == 1, "Exactly one finish target.");
            Near(finishes[0].X, finishX, "Finish plane is X7876.");
            Near(finishes[0].FinishHeight, 16, "Retain the physical finish target height.");
            Require(finishes[0].Y == 32 || finishes[0].Y == 208, "Use the existing floor/ceiling finish target height.");
            Require(track.Obstacles.Select(o => o.Id).Distinct().Count() == track.Obstacles.Length, "Unique obstacle IDs.");
            Require(track.RiskZones.All(z => !string.IsNullOrEmpty(z.Id)), "Nonempty reward IDs.");
            Require(track.RiskZones.Select(z => z.Id).Distinct().Count() == track.RiskZones.Length, "Each ordinary effect can be awarded at most once.");
            Require(track.RiskZones.Count(z => !z.IsFinish && !z.IsNegative) == 9, "Nine ordinary positive rewards, without constraining their distribution by type.");
            Require(track.RiskZones.Count(z => z.IsNegative) == 2, "Two negative rewards.");
            foreach (var obstacle in track.Obstacles)
                Require(ObstacleGeometry.Right(obstacle) + RaceConfig.PlayerRadius < finishX,
                    "The entire expanded obstacle union must end before the final 44 units: " + obstacle.Id);

            var releases = new List<Release>();
            foreach (var zone in track.RiskZones.Where(z => !z.IsFinish))
            {
                // The landing interval is clipped further by the actual stone
                // face; retaining its full nominal width is a conservative bound.
                double contact = zone.X + (zone.IsLanding ? zone.LandingWidth / 2 : zone.Radius + RaceConfig.PlayerRadius);
                Require(double.IsFinite(contact) && contact < finishX, "Ordinary contact lies before the finish target: " + zone.Id);
                double release = contact;
                if (!zone.IsInstant && !zone.IsNegative && !zone.FreeReward)
                {
                    var linked = track.Obstacles.SingleOrDefault(o => o.Id == zone.ObstacleId);
                    Require(linked != null, "A delayed reward must refer to a real obstacle: " + zone.Id);
                    release = Math.Max(contact, Math.Max(zone.ClearX,
                        ObstacleGeometry.Right(linked!) + RaceConfig.PlayerRadius + 1e-7));
                }
                if (zone.IsLanding)
                    Require(track.Obstacles.Any(o => o.Id == zone.ObstacleId && o.Kind != "gate"), "Landing refers to a stone face: " + zone.Id);
                double work = zone.IsNegative
                    ? RaceConfig.Speed * RaceConfig.SlowMultiplier * RaceConfig.SlowDuration
                    : RaceConfig.Speed * RaceConfig.BoostMultiplier * RaceConfig.BoostDuration;
                releases.Add(new Release(zone.Id, zone.Kind, contact, release, work));
            }
            releases.Sort((a, b) => a.LatestRelease.CompareTo(b.LatestRelease));
            Require(releases.Count == 11, "Eleven once-only ordinary pulses.");
            int subsetCount = 1 << releases.Count, worstMask = 0;
            double worstEnd = 0;
            for (int mask = 0; mask < subsetCount; mask++)
            {
                double end = 0;
                for (int i = 0; i < releases.Count; i++)
                    if ((mask & (1 << i)) != 0)
                        end = Math.Max(end, releases[i].LatestRelease) + releases[i].Work;
                Require(end < finishX - 1e-7, "Ordinary effects might reach the finish target; subset " + mask);
                if (end > worstEnd) { worstEnd = end; worstMask = mask; }
            }
            var report = new {
                scope = "Conservative bound over all ordinary-effect subsets, including unreachable subsets; not a route replay or human playtest.",
                track = track.Id, sourceHash = Hash("Assets/Scripts/Core/TracksMixed.cs"), coreHash, geometryHash,
                checks, subsetCount, finishX, worstEnd, worstMask, clearMargin = finishX - worstEnd, releases,
                reasoning = "In distance coordinates each served .6s positive pulse consumes 198 units, each negative 66. Instant preemption preserves unfinished work; impacts freeze both X and effect service. Delaying each once-only release to its latest contact/clearance bound can only postpone the final workload completion. Failed linked rewards and skipped rings remove work. Sorting these upper release bounds and serving all selected work conservatively bounds every actual collection order; no queue is cleared at the finish."
            };
            File.WriteAllText("Verification/mixed-tail-bound.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS mixed tail: {checks} checks, {subsetCount} subsets, latest ordinary end X{worstEnd:R}, margin {finishX-worstEnd:R}.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("NO TAIL PASS: " + error.Message);
            return 1;
        }
    }
}
