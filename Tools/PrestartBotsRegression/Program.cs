using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

static partial class Program
{
    const uint Seed = 12101;
    const double Tolerance = 1e-9;

    record SearchResult(string Id, string ContentHash, double Length, int Obstacles, int Rewards, int References,
        double SearchMilliseconds, double LargestExpansionMilliseconds, long SearchAllocatedBytes,
        double ReplayMilliseconds, int Expansions, int Variants, int Presses, string TapeSha256,
        double FinishSeconds, int Hits, int Boosts, int IdleHits);

    static void Need(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static TrackPackage BuiltIn(string id)
    {
        Need(TrackCatalog.TryGet(id, out var entry), "Missing built-in " + id);
        return entry.Package;
    }

    static TrackPackage Novel(double lastStoneOffset = 0)
    {
        var track = new Track(1800, new[]
        {
            new Obstacle { Id = "quartz-a9", Kind = "floor", X = 370, Width = 54, Height = 43 },
            new Obstacle { Id = "brass-u2", Kind = "ceiling", X = 820, Width = 61, Height = 62 },
            new Obstacle { Id = "mica-f3", Kind = "floor", X = 1210 + lastStoneOffset, Width = 54, Height = 55 },
        })
        {
            Id = "v58-regression-independent",
            Name = "Independent geometry without routes",
            RiskZones = new[] { new RiskZone { Id = "iris-v6", X = 457, Y = 190, Radius = 10, FreeReward = true } }
        };
        return new TrackPackage(track);
    }

    static GameState StartSolo(Track track)
    {
        var game = RaceSimulation.CreateSolo(track, FlightPresets.Chosen, PartyCamera.Lagger, -288.8, 1520);
        Need(RaceRunner.Press(game, 0), "Could not start independent replay");
        RaceRunner.Advance(game, RaceConfig.CountdownDuration);
        Need(game.Phase == "racing" && game.Elapsed == 0, "Bad replay start");
        return game;
    }

    // Split a different frame schedule from the search at each actual saved input.
    static GameState Replay(Track track, double[] tape)
    {
        var game = StartSolo(track);
        double[] frameTimes = { .014, .037, .012, .083 };
        int edge = 0, frame = 0;
        while (game.Phase == "racing" && game.Elapsed < PrestartBotSearch.MaximumRaceTime)
        {
            double next = edge < tape.Length ? tape[edge] : double.PositiveInfinity;
            if (next <= game.Elapsed + Tolerance)
            {
                Need(Math.Abs(next - game.Elapsed) < Tolerance && RaceRunner.Press(game, 0),
                    $"{track.Id}: illegal edge {edge} at {game.Elapsed:R}");
                edge++;
            }
            else RaceRunner.Advance(game, Math.Min(frameTimes[frame++ % frameTimes.Length], next - game.Elapsed));
        }
        Need(game.Players[0].Status == PlayerStatus.Finished && edge == tape.Length,
            $"{track.Id}: incomplete independent replay, {edge}/{tape.Length} presses");
        return game;
    }

    static SearchResult Search(TrackPackage package)
    {
        Track track = package.Track;
        var origin = StartSolo(track);
        long bytesBefore = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
        var search = new PrestartBotSearch(origin, BotDifficulty.Master, Seed);
        double maxStep = 0;
        int steps = 0;
        while (!search.Complete && steps < 50000)
        {
            long slice = Stopwatch.GetTimestamp();
            search.Step();
            maxStep = Math.Max(maxStep, Stopwatch.GetElapsedTime(slice).TotalMilliseconds);
            steps++;
        }
        double searchMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long searchAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
        Need(search.Complete && origin.Elapsed == 0 && origin.Players[0].Distance == 0,
            $"{track.Id}: search did not complete or mutated its input");
        var tapes = search.Tapes();
        Need(tapes.Length is >= 1 and <= PrestartBotSearch.VariantCount, $"{track.Id}: no bounded complete variants");
        Need(tapes.All(tape => tape.Length <= PrestartBotSearch.MaximumInputs
            && tape.All(t => double.IsFinite(t) && t >= 0)
            && tape.Zip(tape.Skip(1)).All(pair => pair.First < pair.Second)), $"{track.Id}: malformed output tape");
        long replayStarted = Stopwatch.GetTimestamp();
        var replay = Replay(track, tapes[0]);
        double replayMilliseconds = Stopwatch.GetElapsedTime(replayStarted).TotalMilliseconds;
        var idle = Replay(track, Array.Empty<double>());
        return new SearchResult(track.Id, package.ContentHash, track.Length, track.Obstacles.Length, track.RiskZones.Length,
            package.References.Length, searchMilliseconds, maxStep, searchAllocatedBytes, replayMilliseconds, search.Expansions,
            tapes.Length, tapes[0].Length, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(tapes[0]))),
            replay.Elapsed, replay.Players[0].Collisions, replay.Players[0].BoostsEarned, idle.Players[0].Collisions);
    }

    static void Install(string path)
    {
        var files = Directory.GetFiles(path, "*.track.json").ToDictionary(Path.GetFileName, File.ReadAllText);
        Need(TrackCatalog.TryInstallBuiltIns(File.ReadAllText(Path.Combine(path, "index.json")), files, out string error), error);
    }

    static Dictionary<string, string> SourceHashes(string trackDirectory)
    {
        string core = Path.GetFullPath(Path.Combine(trackDirectory, "..", "..", "Scripts", "Core"));
        string[] names = { "PrestartBotConfig.cs", "PrestartBotPreparation.cs", "PrestartBotSearch.cs", "PrestartBotTape.cs",
            "RaceRunner.cs", "RaceSimulation.cs", "RaceSnapshot.cs", "StochasticBotController.cs" };
        return names.ToDictionary(name => name, name => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(core, name)))));
    }

    static void Main(string[] args)
    {
        Need(args.Length == 2, "Usage: PrestartBotsRegression <track-directory> <report.json>");
        Install(args[0]);
        Need(TrackCatalog.TryImportLocal(TrackJson.Write(Novel()), out var entry, out string error), error);
        var novel = entry.Package;
        var mutated = Novel(33);
        Need(novel.References.Length == 0 && novel.Track.SafeRoute.Length == 0 && novel.Track.RiskRoute.Length == 0,
            "Novel import unexpectedly has route data");
        Need(!TrackCatalog.SupportsBot(novel.Track.Id, FlightPresets.Chosen), "Novel import admitted to the old bot");
        Need(novel.Track.Id == mutated.Track.Id && novel.ContentHash != mutated.ContentHash,
            "Same-ID moved geometry failed to produce a different fingerprint");
        var cases = new[] { Search(BuiltIn(TracksFull.LongId)), Search(BuiltIn(TracksMixed.Id)), Search(novel), Search(mutated) };
        Need(cases[0].ContentHash != cases[1].ContentHash && cases[0].TapeSha256 != cases[1].TapeSha256,
            "Long and Mixed should demonstrate distinct geometry and input tapes");
        Need(cases[2..].All(item => item.Presses > 0 && item.Hits < item.IdleHits),
            "Novel route did not improve on doing nothing");
        var keys = CheckCacheKeys(novel, mutated);
        var cache = CheckCachePayloads(novel, mutated);
        var hidden = BuiltIn(TracksHidden.ManifestationId);
        var races = new[]
        {
            CheckLiveRace("four bots, shared hidden state", hidden,
                new BotDifficulty?[] { BotDifficulty.Novice, BotDifficulty.Amateur, BotDifficulty.Confident, BotDifficulty.Master }),
            CheckLiveRace("one human plus three bots, shared hidden state", hidden,
                new BotDifficulty?[] { BotDifficulty.Novice, null, BotDifficulty.Confident, BotDifficulty.Master })
        };
        CheckAllLost(novel);
        var report = new { Status = "PASS", Runtime = RuntimeInformation.FrameworkDescription,
            Measurement = "Release .NET process; elapsed wall time while computing in one process, no WebGL or frame scheduling; allocation totals are not retained heap",
            CoreSha256 = SourceHashes(args[0]), Cases = cases, CacheKeyGuards = keys, Cache = cache, Races = races,
            RejectedAllEliminatedReplay = true, RejectedGeometryChangeAfterInstall = true };
        File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {cases.Length} routes replayed; wrote {args[1]}");
    }
}
