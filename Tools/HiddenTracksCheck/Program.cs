using System.Text.Json;
using FlipRace.Core;

static class Program
{
    sealed class Result
    {
        public string Track, Profile; public int Hits, Rejected, Earned, Completed, FinishEarned, WallLandings, StoneLandings;
        public double? Finish; public double Gain, FinishGain; public bool Clean;
        public List<object> Inputs = new(), Landings = new(); public string[] HitIds, RewardIds;
    }
    static Result Run(string id, FlightSettings flight, double[] tape, int omit = -1, double warning = .8)
    {
        var game = RaceSimulation.CreateTrial(TracksHidden.Get(id), flight);
        HiddenObstacles.ConfigureWarning(game, warning);
        RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
        var result = new Result { Track = id, Profile = flight.FirstAction == FirstAction.Flip ? "FastFlip" : "Chosen" };
        var p = game.Players[0];
        void Tick(double dt)
        {
            bool air = p.Flipping; RaceSimulation.Advance(game, dt);
            if (air && !p.Flipping && p.Support != SupportKind.None)
            {
                if (p.Support == SupportKind.Stone) result.StoneLandings++; else result.WallLandings++;
                result.Landings.Add(new { x = p.Distance, y = RaceSimulation.GetPlayerCenterY(p), support = p.Support.ToString(), p.SupportSide, p.SupportObstacleId });
            }
        }
        foreach (var edge in tape.Select((x, i) => (x, i)))
        {
            if (edge.i == omit) continue;
            int inputGuard = 0;
            while (p.Distance < edge.x - 1e-8 && game.Phase == "racing")
            {
                if (++inputGuard > 30000) throw new Exception("Input scheduler stalled");
                if (p.Recovering > 0) { Tick(Math.Min(.005, p.Recovering)); continue; }
                double dt = Math.Min(.005, (edge.x - p.Distance) / p.CurrentSpeed);
                if (p.ActiveEffect != null) dt = Math.Min(dt, p.ActiveEffect.Remaining);
                var trial = RaceSnapshot.Clone(game); RaceSimulation.Advance(trial, dt);
                if (trial.Players[0].Distance > edge.x + 1e-9)
                {
                    double lo = 0, hi = dt;
                    for (int j = 0; j < 40; j++) { double mid = (lo + hi) / 2; trial = RaceSnapshot.Clone(game); RaceSimulation.Advance(trial, mid); if (trial.Players[0].Distance < edge.x) lo = mid; else hi = mid; }
                    dt = (lo + hi) / 2;
                }
                Tick(dt);
            }
            result.Inputs.Add(new { x = p.Distance, time = game.Elapsed, y = RaceSimulation.GetPlayerCenterY(p), p.Flipping, support = p.Support.ToString(), p.SupportSide, p.AirFlipAvailable });
            bool a = RaceSimulation.Press(game, 0), b = RaceSimulation.Press(game, 1); if (!a || !b) result.Rejected++;
        }
        int guard = 0; while (game.Phase == "racing" && guard++ < 50000) Tick(.005);
        result.Hits = p.Collisions; result.Finish = p.FinishTime; result.Earned = p.BoostsEarned; result.Completed = p.CompletedBoosts;
        result.Gain = p.BoostDistanceGained; result.FinishEarned = p.FinishBoostsEarned; result.FinishGain = p.FinishBoostDistanceGained;
        result.HitIds = p.HitObstacleIds.ToArray(); result.RewardIds = p.SuccessfulRiskIds.ToArray();
        result.Clean = result.Hits == 0 && result.Rejected == 0 && p.Status == PlayerStatus.Finished; return result;
    }
    static int Main(string[] args)
    {
        int checks = 0; void Require(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        var rows = new List<object>();
        foreach (var track in TracksHidden.All) foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
        {
            var tape = TracksHidden.SafeRoute(track.Id, flight);
            var safe = Run(track.Id, flight, tape);
            var reward = Run(track.Id, flight, TracksHidden.RewardRoute(track.Id, flight));
            var idle = Run(track.Id, flight, Array.Empty<double>());
            var air = Run(track.Id, flight, TracksHidden.AirRoute(track.Id, flight));
            var finish = Run(track.Id, flight, TracksHidden.FinishRoute(track.Id, flight));
            var omissions = new[] { 0, 1, 2 }.Select(edge => new { edge, result = Run(track.Id, flight, tape, edge) }).ToArray();
            double firstStone = track.Obstacles.First(o => o.Hidden && o.Id.Contains("-hidden-")).X;
            var recovery = Run(track.Id, flight, tape.Where(x => x < firstStone - 85 || x > firstStone).ToArray());
            var windows = new[] { -.02, .02 }.Select(seconds => { var shifted = (double[])tape.Clone(); shifted[0] += seconds * 220; return new { seconds, result = Run(track.Id, flight, shifted) }; }).ToArray();
            var warnings = new[] { .4, .6, 1.0, 1.2 }.Select(seconds => new { seconds, result = Run(track.Id, flight, tape, warning: seconds) }).ToArray();
            var wallTape = new List<double>();
            foreach (var obstacle in track.Obstacles.OrderBy(o => o.X))
            {
                wallTape.Add(obstacle.X - (flight.FirstAction == FirstAction.Flip ? 70 : 84.2264150943396));
                if (flight.FirstAction == FirstAction.Flip) wallTape.Add(obstacle.X - 37);
            }
            wallTape.Sort();
            var floorOnly = Run(track.Id, flight, wallTape.ToArray());
            var ceilingTape = new List<double>(wallTape) { 0 }; if (flight.FirstAction != FirstAction.Flip) ceilingTape.Add(44); ceilingTape.Sort();
            var ceilingOnly = Run(track.Id, flight, ceilingTape.ToArray());
            Require(safe.Clean && safe.Earned == 0 && safe.FinishEarned == 0, "Safe is clean and rewards optional");
            Require(reward.Clean && reward.Earned == 1 && reward.StoneLandings > 0, "Actual marked-stone landing");
            Require(reward.Completed == 1 && Math.Abs(reward.Gain - 66) < 1e-6 && Math.Abs(safe.Finish.Value - reward.Finish.Value - .3) < 1e-6, "Full real pad effect .3 seconds");
            Require(air.Clean && air.Earned > 0 && air.Completed == air.Earned, "Real air collection and completion");
            Require(Math.Abs(safe.Finish.Value - air.Finish.Value - air.Earned * .3) < 1e-6, "Full real air effect");
            Require(finish.Clean && finish.FinishEarned == 1 && Math.Abs(finish.FinishGain - 55) < 1e-6 && Math.Abs(safe.Finish.Value - finish.Finish.Value - .25) < 1e-6, "Optional real finish .25 seconds");
            Require(!idle.Clean && idle.Hits > 0, "No input cannot clear series");
            Require(!floorOnly.Clean && !ceilingOnly.Clean, "Selected same-wall return attempts fail");
            Require(omissions.All(o => !o.result.Clean), "Selected omitted inputs are useful");
            Require(recovery.Hits == 1 && recovery.Finish.HasValue, "Finish after one real hit");
            Require(warnings.All(w => w.result.Clean), "Safe at all warning settings");
            Require(windows.All(w => w.result.Clean), "First action tolerates independent plus/minus20ms");
            Require(track.Obstacles.Where(o => o.Kind == "gate").All(o => o.GapTop > 0 && o.GapBottom < 240), "Two physical gate parts");
            rows.Add(new { safe, reward, air, finish, idle, floorOnly, ceilingOnly, omissions, recovery, windows, warnings });
            Console.WriteLine($"{safe.Track} {safe.Profile}: safe={safe.Clean} hits={safe.Hits} rejected={safe.Rejected} earned={safe.Earned}; pad={reward.Clean} hits={reward.Hits} earned={reward.Earned} stone={reward.StoneLandings}");
            Console.WriteLine($"air={air.Clean} earned={air.Earned} hits={air.Hits} rejected={air.Rejected}; finish={finish.Clean} finishEarned={finish.FinishEarned} gain={finish.FinishGain}");
        }
        File.WriteAllText("Verification/v54-hidden-tracks-witnesses.json", JsonSerializer.Serialize(new { checks, rows }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"PASS {checks} assertions; simulation witnesses only, not human-playability proof.");
        return 0;
    }
}
