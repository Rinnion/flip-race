using System.Diagnostics;
using FlipRace.Core;

static partial class Program
{
    record RaceMetrics(string Case, PreparationMetrics Preparation, int Frames, double LiveP95Milliseconds,
        double LiveMaxMilliseconds, long LiveAllocatedBytes, double WorldSeconds, int HiddenStates,
        string[] Outcomes, int[] Accepted, int[] Corrections, uint RetrySeed, PreparationMetrics RetryPreparation);

    static RaceMetrics CheckLiveRace(string name, TrackPackage package, BotDifficulty?[] slots)
    {
        var config = Config(package, slots, camera: PartyCamera.Constant, surface: true, warning: .6);
        var game = RaceSimulation.CreatePrestartRace(config);
        var humans = Enumerable.Range(0, slots.Length).Where(i => !slots[i].HasValue).ToArray();
        var bots = Enumerable.Range(0, slots.Length).Where(i => slots[i].HasValue).ToArray();
        Need(game.HumanCount == humans.Length && game.UsesPrestartBots && !game.PrestartReady,
            name + ": wrong start state or lineup");
        foreach (int bot in bots) Need(!RaceRunner.Press(game, bot), name + ": bot readied by external input");
        foreach (int human in humans) Need(RaceRunner.Press(game, human), name + ": human could not ready");
        Need(!RaceRunner.StartAllBotParty(game), name + ": could start before preparation");
        RaceRunner.Advance(game, .5);
        Need(game.Phase == "ready" && game.Elapsed == 0, name + ": preparation advances the live clock");

        var (job, measured) = Prepare(name, config, Seed);
        Need(RaceRunner.InstallPrestartPlans(game, job) && game.PrestartReady, name + ": plan did not install");
        var clone = RaceSnapshot.Clone(game);
        Need(!RaceRunner.InstallPrestartPlans(clone, job), name + ": detached copy accepted live plan installation");
        RaceRunner.Advance(game, .5);
        Need(game.Phase == "ready" && game.Elapsed == 0, name + ": installation bypassed explicit start");
        bool launched = humans.Length == 0 ? RaceRunner.StartAllBotParty(game) : RaceRunner.Press(game, humans[0]);
        Need(launched && game.Phase == "countdown" && game.PrestartSeed == Seed, name + ": did not enter countdown");
        Need(!RaceRunner.InstallPrestartPlans(game, job), name + ": active plan can be swapped");
        RaceRunner.Advance(game, RaceConfig.CountdownDuration, paused: true);
        Need(game.Phase == "countdown" && game.CountdownRemaining == RaceConfig.CountdownDuration, name + ": pause moved countdown");
        RaceRunner.Advance(game, RaceConfig.CountdownDuration);
        Need(game.Phase == "racing" && game.Elapsed == 0, name + ": wrong countdown handoff");
        foreach (int bot in bots) Need(!RaceRunner.Press(game, bot), name + ": bot accepted external race input");
        foreach (int human in humans) Need(RaceRunner.Press(game, human), name + ": human input rejected in racing");

        double[] deltas = { .016, .034, .011, .058 };
        var frames = new List<double>();
        long liveAllocated = 0;
        bool humanGoneBeforeFinish = false;
        while (game.Phase == "racing" && game.Elapsed < PrestartBotSearch.MaximumRaceTime)
        {
            double before = game.Elapsed, dt = deltas[frames.Count % deltas.Length];
            long started = Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread();
            RaceRunner.Advance(game, dt);
            liveAllocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
            frames.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (game.Phase == "racing")
            {
                Need(Math.Abs(game.Elapsed - before - dt) < Tolerance, name + ": multiple/incorrect world advances");
                humanGoneBeforeFinish |= humans.Length != 0 && humans.All(i => game.Players[i].Status != PlayerStatus.Running);
            }
        }
        Need(game.Phase == "finished" && game.HiddenObstacles.Count > 0
            && game.Players.All(p => p.TerminalTime.HasValue)
            && Math.Abs(game.Players.Max(p => p.TerminalTime.Value) - game.Elapsed) < Tolerance,
            name + ": incomplete terminal or missing shared hidden state");
        if (humans.Length == 0)
            Need(bots.Any(i => game.Players[i].Status == PlayerStatus.Finished), name + ": no bot finished the real joint race");
        else
            Need(humanGoneBeforeFinish, name + ": test missed last human elimination before bots end");
        var outcomes = game.Players.Select(p => p.Status.ToString()).ToArray();
        var accepted = bots.Select(i => RaceRunner.PrestartAcceptedCommands(game, i)).ToArray();
        var corrections = bots.Select(i => RaceRunner.PrestartCorrections(game, i)).ToArray();
        Need(accepted.All(count => count > 0) && corrections.All(count => count <= PrestartBotController.MaximumCorrections),
            name + ": bot took no commands or recovery exceeded the bound");

        double elapsed = game.Elapsed;
        int hiddenCount = game.HiddenObstacles.Count;
        Need(!RaceRunner.InstallPrestartPlans(game, job), name + ": completed seed accepted for retry");
        RaceRunner.Advance(game, 5);
        Need(game.Phase == "finished" && game.Elapsed == elapsed, name + ": automatically repeated");
        var (retry, retryMeasured) = Prepare(name + " retry", config, Seed + 1, job.ExportCache(), expectCache: true);
        Need(RaceRunner.InstallPrestartPlans(game, retry) && game.Phase == "finished", name + ": retry did not stay explicit");
        foreach (int human in humans) Need(RaceRunner.Press(game, human), name + ": human retry not accepted");
        if (humans.Length == 0) Need(RaceRunner.StartAllBotParty(game), name + ": all-bot retry not accepted");
        Need(game.Phase == "countdown" && game.PrestartSeed == Seed + 1 && game.HiddenObstacles.Count == 0,
            name + ": retry reused seed or hidden state");

        frames.Sort();
        return new RaceMetrics(name, measured, frames.Count, frames[(int)Math.Ceiling(frames.Count * .95) - 1],
            frames[^1], liveAllocated, elapsed, hiddenCount, outcomes, accepted, corrections, retry.Seed, retryMeasured);
    }

    static void CheckAllLost(TrackPackage package)
    {
        // All bodies start at the left elimination threshold. Each personal
        // Lagger replay can finish, but the joint constant-camera replay cannot.
        var slots = new BotDifficulty?[] { BotDifficulty.Master, BotDifficulty.Amateur };
        var config = Config(package, slots, camera: PartyCamera.Constant, left: RaceConfig.PlayerRadius);
        var job = new PrestartBotPreparation(config, Seed);
        for (int slice = 0; slice < 5000 && !job.Complete; slice++) job.Step(64);
        Need(job.Complete && job.ExpandedStates > 0 && job.ReplaySteps > 0 && !job.Success && job.ExportCache() == null,
            "All-eliminated joint replay was admitted as a complete route");
        var game = RaceSimulation.CreatePrestartRace(config);
        Need(!RaceRunner.InstallPrestartPlans(game, job) && !RaceRunner.StartAllBotParty(game) && game.Phase == "ready",
            "Failed joint replay started anyway");
    }
}
