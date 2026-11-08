using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

if (args.Length != 3) throw new ArgumentException("Usage: MasterTimingTrial A|B|C track-directory output.json");
string variant = args[0];
string directory = args[1];
var files = Directory.GetFiles(directory, "*.track.json").ToDictionary(Path.GetFileName, File.ReadAllText);
Require(TrackCatalog.TryInstallBuiltIns(File.ReadAllText(Path.Combine(directory, "index.json")), files, out var error), error);
var json = new JsonSerializerOptions { WriteIndented = true };
using var process = Process.GetCurrentProcess();
var rows = new List<object>();
var warm = Config(TracksFull.LongId, FlightPresets.Chosen, true, BotDifficulty.Master);
var warmup = Prepare(warm, 12101, null);
if (warmup.Job.Success) Play(warm, warmup.Job, strict: variant != "C");

foreach (string track in new[] { TracksFull.LongId, TracksFull.ShortId })
foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
foreach (bool acceleration in new[] { false, true })
{
    var master = Config(track, flight, acceleration, BotDifficulty.Master);
    string coldBank = null;
    foreach (uint seed in new uint[] { 12101, 12102, 12103 })
    {
        string bank = Run(master, BotDifficulty.Master, seed, "cold", null);
        if (seed == 12101) coldBank = bank;
    }
    Run(master, BotDifficulty.Master, 12102, "warm", coldBank);
    foreach (var difficulty in new[] { BotDifficulty.Novice, BotDifficulty.Amateur, BotDifficulty.Confident, BotDifficulty.Strong })
        Run(Config(track, flight, acceleration, difficulty), difficulty, 12101, "cold", null);
}

File.WriteAllText(args[2], JsonSerializer.Serialize(new { variant, rows }, json));
Console.WriteLine($"{variant} ran {rows.Count} configurations; {args[2]}");

string Run(PrestartBotConfig config, BotDifficulty difficulty, uint seed, string stage, string bank)
{
    bool strict = variant != "C" || difficulty != BotDifficulty.Master;
    var measured = stage == "warm" && bank == null && !strict ? default : Prepare(config, seed, bank);
    var job = measured.Job;
    Require(!strict || job.Success, "Preparation failed: " + job?.Failure);
    if (job?.Success == true)
    {
        Require(job.UsedCache == (stage == "warm"), "Unexpected cache use");
        Require(stage != "warm" || job.ExpandedStates == 0, "Warm path expanded search states");
    }
    object actual = job?.Success == true ? Play(config, job, strict)
        : new { status = job == null ? "NoColdCache" : "PreparationFailed", failure = job?.Failure };
    string exported = job?.ExportCache();
    string trackId = config.Package.Track.Id;
    string profile = config.Flight == FlightPresets.Chosen ? "Chosen" : "FastFlip";
    var candidates = difficulty == BotDifficulty.Master && stage == "cold" ? InspectSearch(config, seed, job.ExpandedStates) : null;
    var verifiedBank = exported != null ? ReadBank(config, seed, exported) : null;
    rows.Add(new { trackId, trackHash = config.Package.ContentHash, profile, surface = config.SurfaceRunEnabled,
        difficulty = difficulty.ToString(), seed, stage, actual, candidates, verifiedBank,
        preparation = job == null ? null : new { job.UsedCache, job.ExpandedStates, job.ReplaySteps, job.JointRejectedCommands, measured.Slices,
            measured.CpuMilliseconds, job.ElapsedMilliseconds },
        cacheSha256 = exported != null ? Sha(Convert.FromBase64String(exported)) : null });
    return exported;
}

PrestartBotConfig Config(string id, FlightSettings flight, bool surface, BotDifficulty difficulty)
{
    Require(TrackCatalog.TryGet(id, out var entry), "Missing track " + id);
    return new PrestartBotConfig(entry.Package, flight, new BotDifficulty?[] { null, difficulty }, PartyCamera.Lagger, surface,
        new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, 200, true), .8, standaloneBot: true);
}

(PrestartBotPreparation Job, int Slices, double CpuMilliseconds) Prepare(PrestartBotConfig config, uint seed, string bank)
{
    TimeSpan cpu = process.TotalProcessorTime;
    var job = new PrestartBotPreparation(config, seed, bank);
    int slices = 0;
    while (!job.Complete && slices < 4096)
    {
        job.Step(192);
        slices++;
    }
    double cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
    Require(job.Complete, "Preparation exceeded harness slice limit");
    return (job, slices, cpuMilliseconds);
}

object Play(PrestartBotConfig config, PrestartBotPreparation job, bool strict)
{
    var game = RaceSimulation.CreatePrestartRace(config);
    Require(RaceRunner.InstallPrestartPlans(game, job) && RaceRunner.Press(game, 0) && game.Phase == "countdown", "Verified start failed");
    Require(game.CountdownRemaining == RaceConfig.CountdownDuration, "Changed countdown");
    RaceRunner.Advance(game, RaceConfig.CountdownDuration);
    var bot = RaceRunner.GetPrestartBotController(game, 1);
    var tape = job.Plans.GetTape(1).Select(edge => edge.Time).ToArray();
    var actualCommandFrames = new List<int>(tape.Length);
    using var trace = new MemoryStream();
    using var writer = new BinaryWriter(trace);
    int frame = 0;
    bool matchedFrames = true;
    while (game.Phase == "racing" && frame < 7200)
    {
        double started = game.Elapsed;
        int prior = bot.Accepted;
        RaceRunner.Advance(game, 1d / 60);
        if (bot.Accepted != prior)
        {
            matchedFrames &= bot.Accepted == prior + 1 && prior < tape.Length
                && tape[prior] >= started - 1e-8 && tape[prior] <= game.Elapsed + 1e-8;
            actualCommandFrames.Add(frame);
        }
        frame++;
        WriteFrame(writer, game, bot);
    }
    var player = game.Players[1];
    bool matchedTape = matchedFrames && bot.Accepted == tape.Length && bot.Skipped == 0 && bot.Corrections == 0;
    Require(!strict || (game.Phase == "finished" && player.Status == PlayerStatus.Finished), "Live race did not finish");
    Require(!strict || matchedTape, "Verified tape diverged in the live race");
    return new { status = player.Status.ToString(), player.FinishTime, player.Collisions, player.SlowsEarned,
        player.BoostsEarned, player.FinishBoostsEarned, hitIds = Sort(player.HitObstacleIds),
        slowIds = Sort(player.NegativeRiskIds), earnedRewardIds = Earned(config.Package.Track, player),
        commands = bot.Accepted, tape, tapeSha256 = TapeHash(tape), actualCommandFrames, traceSha256 = Sha(trace.ToArray()),
        bot.Corrections, bot.Skipped, matchedTape, frame };
}

static void WriteFrame(BinaryWriter writer, GameState game, PrestartBotController bot)
{
    writer.Write(game.Elapsed);
    writer.Write(game.Phase);
    writer.Write(game.PartyCameraLeft);
    writer.Write(game.Winner?.ToString() ?? "");
    writer.Write(bot.Accepted);
    foreach (var p in game.Players)
    {
        writer.Write((int)p.Status);
        writer.Write(p.TerminalTime ?? -1);
        writer.Write(p.FinishTime ?? -1);
        writer.Write(p.Distance);
        writer.Write(p.Y);
        writer.Write(p.VerticalSpeed);
        writer.Write(p.Gravity);
        writer.Write(p.Flipping);
        writer.Write(p.FlipElapsed);
        writer.Write(p.AirFlipAvailable);
        writer.Write((int)p.Support);
        writer.Write(p.SupportSide);
        writer.Write(p.SupportObstacleId ?? "");
        writer.Write(p.SurfaceRunReserve);
        writer.Write(p.Recovering);
        writer.Write(p.Collisions);
        writer.Write(p.BoostsEarned);
        writer.Write(p.SlowsEarned);
        writer.Write(p.CoinsEarned);
        writer.Write(p.FinishBoostsEarned);
        writer.Write(p.FinishBoostElapsed);
        writer.Write(p.ActiveEffect?.RingId ?? "");
        writer.Write(p.ActiveEffect?.Remaining ?? 0);
    }
}

static object InspectSearch(PrestartBotConfig config, uint seed, int expectedExpansions)
{
    var game = config.BeginReplay(true);
    uint slotSeed = PrestartBotConfig.SlotSeed(seed, 1);
    var search = new PrestartBotSearch(game, BotDifficulty.Master, slotSeed);
    while (!search.Complete) search.Step();
    Require(search.Expansions == expectedExpansions, "Candidate census used different search work");
    var intent = new PrestartBotIntent(game.Track, BotDifficulty.Master, slotSeed);
    const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
    var finishes = (IEnumerable)typeof(PrestartBotSearch).GetField("finishes", fields).GetValue(search);
    var complete = new List<Candidate>();
    foreach (object node in finishes)
    {
        Type type = node.GetType();
        var finish = (GameState)type.GetField("Game", fields).GetValue(node);
        object edge = type.GetField("Tape", fields).GetValue(node);
        var times = new List<double>();
        while (edge != null)
        {
            Type edgeType = edge.GetType();
            times.Add((double)edgeType.GetField("Time", fields).GetValue(edge));
            edge = edgeType.GetField("Prior", fields).GetValue(edge);
        }
        times.Reverse();
        var player = finish.Players[0];
        complete.Add(new(TapeHash(times), finish.Elapsed, intent.UndesiredContacts(player), intent.Preference(player),
            Sort(player.HitObstacleIds), Sort(player.NegativeRiskIds), Earned(game.Track, player)));
    }
    if (complete.Count == 0) return new { fullCandidates = 0, distinctTapes = 0, distinctOutcomes = 0 };
    var bestContacts = complete.Min(candidate => candidate.Contacts);
    var eligible = complete.Where(candidate => candidate.Contacts == bestContacts).ToArray();
    var bestGoals = eligible.OrderByDescending(candidate => candidate.Goals).ThenBy(candidate => candidate.Time).First();
    var bestTime = eligible.OrderBy(candidate => candidate.Time).ThenByDescending(candidate => candidate.Goals).First();
    return new { fullCandidates = complete.Count, distinctTapes = complete.Select(candidate => candidate.TapeSha256).Distinct().Count(),
        distinctOutcomes = complete.Select(candidate => Ledger(candidate)).Distinct().Count(), bestContacts,
        atBestContacts = eligible.Length, bestGoals, bestTime,
        censusSha256 = Sha(JsonSerializer.SerializeToUtf8Bytes(complete)) };
}

static object ReadBank(PrestartBotConfig config, uint seed, string text)
{
    using var buffer = new MemoryStream(Convert.FromBase64String(text));
    using var reader = new BinaryReader(buffer);
    Require(reader.ReadString() == config.CacheKey && reader.ReadByte() == 0, "Unexpected cache format");
    int count = reader.ReadByte();
    Require(count > 0, "No cached full route");
    var intent = new PrestartBotIntent(config.Package.Track, config.GetDifficulty(1).Value, PrestartBotConfig.SlotSeed(seed, 1));
    var verified = new List<Candidate>();
    for (int index = 0; index < count; index++)
    {
        var tape = new double[reader.ReadUInt16()];
        for (int i = 0; i < tape.Length; i++) tape[i] = reader.ReadDouble();
        var world = config.BeginReplay(true);
        var input = new PrestartReplayInput(world, 0, tape);
        var sources = new IRaceScheduledInput[] { input };
        while (world.Phase == "racing" && world.Elapsed < PrestartBotSearch.MaximumRaceTime)
            RaceRunner.AdvanceScheduled(world, PrestartBotSearch.Quantum, sources);
        var player = world.Players[0];
        Require(player.Status == PlayerStatus.Finished && input.AllRead && input.Rejected == 0, "Cached full route was not reproducible");
        verified.Add(new(TapeHash(tape), world.Elapsed, intent.UndesiredContacts(player), intent.Preference(player),
            Sort(player.HitObstacleIds), Sort(player.NegativeRiskIds), Earned(world.Track, player)));
    }
    Require(buffer.Position == buffer.Length, "Unexpected data after cache bank");
    return new { fullTapes = count, distinctOutcomes = verified.Select(Ledger).Distinct().Count(), routes = verified };
}

static string Ledger(Candidate candidate) => string.Join("|", string.Join(",", candidate.Hits),
    string.Join(",", candidate.Slows), string.Join(",", candidate.Rewards));
static string[] Earned(Track track, PlayerState player) => Sort(track.RiskZones
    .Where(zone => !zone.IsNegative && !zone.IsCoin && player.SuccessfulRiskIds.Contains(zone.Id)).Select(zone => zone.Id));
static string[] Sort(IEnumerable<string> ids) => ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
static string TapeHash(IEnumerable<double> times)
{
    using var bytes = new MemoryStream();
    using var writer = new BinaryWriter(bytes);
    foreach (double time in times) writer.Write(time);
    return Sha(bytes.ToArray());
}
static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record Candidate(string TapeSha256, double Time, int Contacts, double Goals, string[] Hits, string[] Slows, string[] Rewards);
