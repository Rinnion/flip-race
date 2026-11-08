using System.Globalization;
using FlipRace.Core;

static class AllocationCheck
{
    const long FrameBudget = 32L * 1024 * 1024;

    internal static int Run(string[] args)
    {
        const string trackFolder = "Assets/StreamingAssets/Tracks";
        var files = Directory.GetFiles(trackFolder, "*.track.json").ToDictionary(Path.GetFileName, File.ReadAllText);
        if (!TrackCatalog.TryInstallBuiltIns(File.ReadAllText(Path.Combine(trackFolder, "index.json")), files, out var error))
            throw new InvalidOperationException(error);
        double delta = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : .3;
        int botCount = args.Length > 1 ? int.Parse(args[1]) : 4;
        string track = args.Length > 2 ? args[2] : TracksHidden.ManifestationId;
        bool surfaceRun = args.Length <= 3 || bool.Parse(args[3]);
        uint seed = args.Length > 4 ? uint.Parse(args[4]) : 0x9b66cda5u;
        var lineup = new BotDifficulty?[] { BotDifficulty.Amateur, BotDifficulty.Novice, BotDifficulty.Strong, BotDifficulty.Master };
        for (int i = botCount; i < lineup.Length; i++) lineup[i] = null;
        if (!TrackCatalog.TryGet(track, out var entry)) throw new ArgumentException("Unknown track", nameof(args));
        var game = RaceSimulation.CreateParty(entry.Package.Track, FlightPresets.Chosen, lineup, seed, PartyCamera.Lagger);
        RaceSimulation.ConfigureSurfaceRun(game, surfaceRun);
        if (botCount == 4) RaceRunner.StartAllBotParty(game);
        else for (int i = botCount; i < lineup.Length; i++) RaceRunner.Press(game, i);
        RaceRunner.Advance(game, RaceConfig.CountdownDuration);
        var controllers = Enumerable.Range(0, botCount).Select(slot => RaceRunner.GetPartyBotController(game, slot)).ToArray();
        var frames = new List<(double At, long Bytes, int Plans, int Forecasts, int Checkpoints, int Rollbacks)>();
        long retainedBefore = GC.GetTotalMemory(true);
        int plans = 0, forecasts = 0, checkpoints = 0, rollbacks = 0;
        while (game.Phase == "racing" && game.Elapsed < 60)
        {
            double started = game.Elapsed;
            long before = GC.GetAllocatedBytesForCurrentThread();
            RaceRunner.Advance(game, delta);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            int nextPlans = controllers.Sum(bot => bot.PlanningCalls);
            int nextForecasts = controllers.Sum(bot => bot.ForecastSteps);
            int nextCheckpoints = controllers.Sum(bot => bot.ForecastCheckpoints);
            int nextRollbacks = controllers.Sum(bot => bot.ForecastTrialRollbacks);
            frames.Add((started, bytes, nextPlans - plans, nextForecasts - forecasts, nextCheckpoints - checkpoints, nextRollbacks - rollbacks));
            plans = nextPlans;
            forecasts = nextForecasts;
            checkpoints = nextCheckpoints;
            rollbacks = nextRollbacks;
        }
        long retained = GC.GetTotalMemory(true) - retainedBefore;
        var top = frames.OrderByDescending(frame => frame.Bytes).Take(3).ToArray();
        Console.WriteLine($"{game.Phase} in {frames.Count} frames; {track}; {botCount} bots; delta={delta:R}; surface={surfaceRun}; seed={seed}");
        Console.WriteLine($"Total {MiB(frames.Sum(frame => frame.Bytes)):F2} MiB; retained {MiB(retained):F2} MiB; worst {MiB(top[0].Bytes):F2} MiB; budget {MiB(FrameBudget):F0} MiB");
        foreach (var frame in top)
            Console.WriteLine($"  {frame.At:F3}s: {MiB(frame.Bytes):F2} MiB, plans={frame.Plans}, forecast steps={frame.Forecasts}, checkpoints={frame.Checkpoints}, rollbacks={frame.Rollbacks}");
        return game.Phase == "finished" && top[0].Bytes <= FrameBudget ? 0 : 1;
    }

    static double MiB(long bytes) => bytes / (1024d * 1024d);
}
