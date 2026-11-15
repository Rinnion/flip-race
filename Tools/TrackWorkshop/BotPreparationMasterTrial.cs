using System.Diagnostics;
using FlipRace.Core;

namespace TrackWorkshop;

public sealed record MasterTrialResult(string Status, double[] InputTimes, bool ReplayVerified, double? FinishTime,
    int Collisions, int Boosts, int Slows, int Coins, int FinishBoosts, bool Clean, bool GoalsMet, string[] MissedRewardIds,
    uint Seed, string Profile, double WarningDuration, WorkshopSurfaceRunSettings SurfaceRunSettings, string Camera,
    int Participants, string[] SuccessfulRewardIds, string[] FailedRewardIds, string[] HitIds);

internal static class BotPreparationMasterTrial
{
    internal static MasterTrialResult Run(TrackPackage package, FlightSettings flight, TrackBotReference reference,
        double warning, WorkshopSurfaceRunSettings settings, CancellationToken cancellation)
    {
        const int budgetMilliseconds = 8000, maximumInputs = 4096;
        var watch = Stopwatch.StartNew();
        var game = WorkshopService.StartSolo(package.Track, flight, warning, settings);
        var config = StochasticBotSettings.CreateProofMaster(package, flight, reference);
        var controller = new StochasticBotController(game, 0, config);
        var times = new List<double>();
        bool validTape = true;
        controller.Observer = slice =>
        {
            if (slice.Kind != "press") return;
            if (times.Count == maximumInputs || (times.Count > 0 && slice.Time <= times[^1])) validTape = false;
            else times.Add(slice.Time);
        };
        RaceRunner.Advance(game, RaceConfig.CountdownDuration, controller);
        while (WorkshopService.IsRacing(game) && watch.ElapsedMilliseconds < budgetMilliseconds)
        {
            cancellation.ThrowIfCancellationRequested();
            RaceRunner.Advance(game, Math.Min(WorkshopService.SampleStep, WorkshopService.MaximumTime - game.Elapsed), controller);
        }
        cancellation.ThrowIfCancellationRequested();
        var player = game.Players[0];
        string status = player.Status == PlayerStatus.Finished ? "finished" :
            watch.ElapsedMilliseconds >= budgetMilliseconds ? "budget-limit" : "timed-out";
        var successful = new HashSet<string>(player.SuccessfulRiskIds, StringComparer.Ordinal);
        foreach (var zone in package.Track.RiskZones)
            if (zone.IsFinish && player.CollectedRiskIds.Contains(zone.Id)) successful.Add(zone.Id);
        string[] missed = reference.RewardIds.Where(id => !successful.Contains(id)).Order(StringComparer.Ordinal).ToArray();
        bool clean = status == "finished" && player.Collisions == 0 && player.SlowsEarned == 0 && player.FailedRiskIds.Count == 0;
        double[] tape = validTape ? times.ToArray() : Array.Empty<double>();
        bool verified = status == "finished" && validTape && CheckReplay(tape, player);
        return new(status, tape, verified, player.FinishTime, player.Collisions, player.BoostsEarned, player.SlowsEarned,
            player.CoinsEarned, player.FinishBoostsEarned, clean, clean && missed.Length == 0, missed, config.Seed,
            reference.ProfileId, warning, settings, "lagger", 1,
            successful.Order(StringComparer.Ordinal).ToArray(), player.FailedRiskIds.Order(StringComparer.Ordinal).ToArray(),
            player.HitObstacleIds.Order(StringComparer.Ordinal).ToArray());

        bool CheckReplay(double[] inputs, PlayerState original)
        {
            var replay = WorkshopService.StartSolo(package.Track, flight, warning, settings);
            RaceSimulation.Advance(replay, RaceConfig.CountdownDuration);
            int next = 0;
            while (WorkshopService.IsRacing(replay))
            {
                var input = WorkshopService.ReplayStep(replay, "time", inputs, ref next, cancellationToken: cancellation);
                if (input?.Status == "ignored") return false;
            }
            var result = replay.Players[0];
            return next == inputs.Length && result.FinishTime is double finish &&
                Math.Abs(finish - original.FinishTime.Value) < 1e-6 && result.Collisions == original.Collisions &&
                result.BoostsEarned == original.BoostsEarned && result.SlowsEarned == original.SlowsEarned &&
                result.CoinsEarned == original.CoinsEarned && result.FinishBoostsEarned == original.FinishBoostsEarned &&
                original.SuccessfulRiskIds.SetEquals(result.SuccessfulRiskIds) &&
                original.FailedRiskIds.SetEquals(result.FailedRiskIds) && original.HitObstacleIds.SetEquals(result.HitObstacleIds);
        }
    }
}
