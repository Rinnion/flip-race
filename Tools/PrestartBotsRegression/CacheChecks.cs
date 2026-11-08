using System.Diagnostics;
using System.Text;
using FlipRace.Core;

static partial class Program
{
    static PrestartBotConfig Config(TrackPackage package, BotDifficulty?[] slots = null,
        FlightSettings flight = null, PartyCamera camera = PartyCamera.Lagger, bool surface = false,
        SurfaceRunSettings? surfaceSettings = null, double warning = .8, bool standalone = false,
        double left = -288.8, double width = 1520)
    {
        return new PrestartBotConfig(package, flight ?? FlightPresets.Chosen,
            slots ?? new BotDifficulty?[] { null, BotDifficulty.Master }, camera, surface,
            surfaceSettings ?? SurfaceRunSettings.Default, warning, standalone, left, width);
    }

    static string[] CheckCacheKeys(TrackPackage novel, TrackPackage moved)
    {
        var baseline = Config(novel);
        Need(baseline.CacheKey == Config(Novel()).CacheKey, "Equivalent round-trip geometry lost its cache key");
        var slots = new BotDifficulty?[] { null, BotDifficulty.Master };
        var copied = Config(novel, slots);
        slots[1] = BotDifficulty.Amateur;
        Need(copied.GetDifficulty(1) == BotDifficulty.Master && copied.CacheKey == baseline.CacheKey,
            "Cache identity shares mutable lineup input");

        var changes = new (string Name, PrestartBotConfig Config)[]
        {
            ("same-id geometry", Config(moved)),
            ("flight", Config(novel, flight: FlightPresets.FastFlip)),
            ("camera", Config(novel, camera: PartyCamera.Constant)),
            ("start boundary", Config(novel, left: -290)),
            ("camera window", Config(novel, width: 1500)),
            ("standalone mode", Config(novel, standalone: true)),
            ("roles", Config(novel, slots: new BotDifficulty?[] { BotDifficulty.Master, null })),
            ("difficulty", Config(novel, slots)),
            ("hidden warning", Config(novel, warning: .4)),
            ("surface flag", Config(novel, surface: true)),
            ("surface rate", Config(novel, surfaceSettings: new SurfaceRunSettings(3, SurfaceRunCurve.Linear, 30))),
            ("surface curve", Config(novel, surfaceSettings: new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 30))),
            ("surface cap", Config(novel, surfaceSettings: new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 40))),
            ("surface decay", Config(novel, surfaceSettings: new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, airDecayPercent: 50))),
            ("surface reset", Config(novel, surfaceSettings: new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, resetOnCollision: false)))
        };
        Need(changes.Select(change => change.Config.CacheKey).Append(baseline.CacheKey).Distinct().Count() == changes.Length + 1,
            "Two incompatible configurations share a cache key");

        var stale = Novel();
        var bound = Config(stale);
        stale.Track.Obstacles[0].Width += 1;
        Need(!bound.GeometryMatches(), "Live geometry mutation failed to invalidate bound cache config");
        bool rejected = false;
        try { Config(stale); }
        catch (ArgumentException) { rejected = true; }
        Need(rejected, "Stale package could create a new cache config");
        return changes.Select(change => change.Name).Append("stale bound geometry").ToArray();
    }

    record PreparationMetrics(string Case, double Milliseconds, double MaxSliceMilliseconds, long AllocatedBytes,
        int Expanded, int ReplaySteps, bool UsedCache, int JointRejected, int CacheCharacters);

    static (PrestartBotPreparation Job, PreparationMetrics Metrics) Prepare(string name, PrestartBotConfig config,
        uint seed, string cache = null, bool expectCache = false)
    {
        long bytes = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
        var job = new PrestartBotPreparation(config, seed, cache);
        double maxSlice = 0, progress = 0;
        int slices = 0;
        while (!job.Complete && slices++ < 5000)
        {
            long slice = Stopwatch.GetTimestamp();
            job.Step(64);
            maxSlice = Math.Max(maxSlice, Stopwatch.GetElapsedTime(slice).TotalMilliseconds);
            Need(job.Progress >= progress && job.Progress <= 1, name + ": invalid progress");
            progress = job.Progress;
        }
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Need(job.Complete && job.Success, $"{name}: failed preparation: {job.Failure}");
        Need(job.UsedCache == expectCache && (expectCache ? job.ExpandedStates == 0 : job.ExpandedStates > 0)
            && job.ReplaySteps > 0, name + ": wrong cache/search/replay path");
        string exported = job.ExportCache();
        Need(!string.IsNullOrEmpty(exported) && exported.Length <= PrestartBotPreparation.MaximumCacheCharacters,
            name + ": unbounded or missing cache payload");
        return (job, new PreparationMetrics(name, elapsed, maxSlice, allocated, job.ExpandedStates,
            job.ReplaySteps, job.UsedCache, job.JointRejectedCommands, exported.Length));
    }

    static string CacheWithOneTape(string key, params double[] tape)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(key);
        writer.Write((byte)0); // human
        writer.Write((byte)1); // one bot variant
        writer.Write((ushort)tape.Length);
        foreach (double time in tape) writer.Write(time);
        writer.Flush();
        return Convert.ToBase64String(stream.ToArray());
    }

    static PreparationMetrics[] CheckCachePayloads(TrackPackage novel, TrackPackage moved)
    {
        var baseline = Config(novel);
        var (cold, coldMetrics) = Prepare("cold", baseline, Seed);
        string cache = cold.ExportCache();
        var (warm, warmMetrics) = Prepare("warm replay, new seed", baseline, Seed + 1, cache, expectCache: true);
        Need(warm.Plans.Seed != cold.Plans.Seed, "Cache overwrote the new attempt seed");
        var liveWarm = Replay(novel.Track, warm.Plans.GetTape(1).Select(edge => edge.Time).ToArray());
        Need(liveWarm.Players[0].Status == PlayerStatus.Finished, "Warm tape failed independent replay");

        var fast = Config(novel, flight: FlightPresets.FastFlip);
        var cases = new (string Name, PrestartBotConfig Config, string Payload)[]
        {
            ("stale version", baseline, CacheWithOneTape("obsolete-planner" + baseline.CacheKey[baseline.CacheKey.IndexOf('/')..], .25)),
            ("same-ID moved geometry", Config(moved), cache),
            ("changed settings", Config(novel, surface: true), cache),
            ("broken base64", baseline, "@@@@"),
            ("truncated payload", baseline, cache[..^4]),
            ("extra payload bytes", baseline, Convert.ToBase64String(Convert.FromBase64String(cache).Append((byte)1).ToArray())),
            ("NaN time", baseline, CacheWithOneTape(baseline.CacheKey, double.NaN)),
            ("duplicate time", baseline, CacheWithOneTape(baseline.CacheKey, .25, .25)),
            ("parseable but illegal play", fast, CacheWithOneTape(fast.CacheKey, .125, .126, .127)),
        };
        var measurements = new List<PreparationMetrics> { coldMetrics, warmMetrics };
        foreach (var item in cases) measurements.Add(Prepare(item.Name, item.Config, Seed + 2, item.Payload).Metrics);

        var cancelled = new PrestartBotPreparation(baseline, Seed);
        cancelled.Step(3);
        cancelled.Cancel();
        cancelled.Step(64);
        Need(cancelled.Complete && !cancelled.Success && cancelled.ExportCache() == null,
            "Cancelled preparation returned or cached a result");
        var stale = Novel();
        var staleConfig = Config(stale);
        stale.Track.Obstacles[0].X += 17;
        var staleJob = new PrestartBotPreparation(staleConfig, Seed, cache);
        Need(staleJob.Complete && !staleJob.Success && staleJob.ExpandedStates == 0,
            "Stale in-place geometry was searched or started");

        var changedAfterInstall = Novel();
        var installedConfig = Config(changedAfterInstall);
        var (prepared, _) = Prepare("ready before geometry mutation", installedConfig, Seed + 1, cache, expectCache: true);
        var race = RaceSimulation.CreatePrestartRace(installedConfig);
        Need(RaceRunner.InstallPrestartPlans(race, prepared) && race.PrestartReady, "Cannot install valid plan before mutation");
        changedAfterInstall.Track.Obstacles[0].X += 17;
        Need(RaceRunner.Press(race, 0) && race.Phase == "ready" && !race.PrestartReady,
            "Changed geometry launched previously installed plans");
        return measurements.ToArray();
    }
}
