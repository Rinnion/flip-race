using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

const string tracks = "Assets/StreamingAssets/Tracks";
const uint baseSeed = 12101;
int assertions = 0;
var results = new List<object>();
var files = Directory.GetFiles(tracks, "*.track.json").ToDictionary(Path.GetFileName, File.ReadAllText);
Check(TrackCatalog.TryInstallBuiltIns(File.ReadAllText(Path.Combine(tracks, "index.json")), files, out string failure), failure);

void Check(bool ok, string description)
{
    if (!ok) throw new InvalidOperationException(description);
    assertions++;
}

TrackPackage NoReferences(string id)
{
    Check(TrackCatalog.TryGet(id, out var entry), "Missing track " + id);
    var package = entry.Package;
    return new TrackPackage(package.Track, package.ProfilePolicy, package.WarningMode);
}

PrestartBotConfig Config(TrackPackage package, FlightSettings flight, BotDifficulty?[] slots, bool acceleration = false,
    PartyCamera camera = PartyCamera.Lagger, bool standalone = false, double warning = .8)
{ return new PrestartBotConfig(package, flight, slots, camera, acceleration, SurfaceRunSettings.Default, warning, standalone); }

(PrestartBotPreparation Prep, object Metrics) Prepare(PrestartBotConfig config, uint seed, string cache = null, bool expected = true)
{
    var job = new PrestartBotPreparation(config, seed, cache);
    int slices = 0;
    long peakBytes = 0, peakTicks = 0;
    while (!job.Complete && slices < 4096)
    {
        double progress = job.Progress;
        long bytes = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
        job.Step(192);
        peakTicks = Math.Max(peakTicks, Stopwatch.GetTimestamp() - started);
        peakBytes = Math.Max(peakBytes, GC.GetAllocatedBytesForCurrentThread() - bytes);
        Check(job.Progress >= progress && job.Progress <= 1, "Progress moved backward or exceeded 100%");
        slices++;
    }
    Check(job.Complete && job.Success == expected, $"{config.Package.Track.Id}: {job.Failure}");
    Check(peakBytes < 4 * 1024 * 1024, "Managed allocation budget exceeded in a preparation slice");
    var metrics = new { job.Seed, job.UsedCache, job.ElapsedMilliseconds, job.ExpandedStates, job.ReplaySteps, job.JointRejectedCommands,
        peakBytes, maxSliceMilliseconds = 1000d * peakTicks / Stopwatch.Frequency, slices };
    Console.WriteLine($"{config.Package.Track.Id}/{(config.Flight == FlightPresets.Chosen ? "Chosen" : "Fast")}, {config.SlotCount - config.HumanCount} bots, cache={job.UsedCache}: "
        + $"{job.ElapsedMilliseconds:F1}ms, {job.ExpandedStates} expansions, {job.ReplaySteps} replay steps, max {peakBytes / 1024d:F1}KiB per slice");
    return (job, metrics);
}

object Play(GameState game, bool perturb = false)
{
    Check(game.Phase == "countdown" && !game.PrestartReady, "A prepared plan was not consumed at countdown");
    RaceRunner.Advance(game, .5, paused: true);
    Check(game.CountdownRemaining == RaceConfig.CountdownDuration && game.Elapsed == 0, "Countdown pause changed physics");
    RaceRunner.Advance(game, RaceConfig.CountdownDuration);
    if (perturb)
    {
        int slot = Enumerable.Range(0, game.Players.Length).First(game.IsBotSlot);
        game.Players[slot].Recovering = .5;
    }
    int frame = 0;
    while (game.Phase == "racing" && frame < 7200)
    {
        if (perturb && frame % 19 == 0)
            for (int slot = 0; slot < game.Players.Length; slot++) if (!game.IsBotSlot(slot)) RaceRunner.Press(game, slot);
        RaceRunner.Advance(game, 1d / 60);
        frame++;
    }
    Check(game.Phase == "finished", "Live scheduled race never finished");
    int corrections = 0;
    for (int slot = 0; slot < game.Players.Length; slot++)
    {
        int count = RaceRunner.PrestartCorrections(game, slot);
        Check(count <= PrestartBotController.MaximumCorrections, "Correction budget exceeded");
        corrections += count;
    }
    if (perturb) Check(corrections > 0, "Actual divergence did not trigger repair");
    var positiveIds = game.Track.RiskZones.Where(zone => !zone.IsCoin && !zone.IsNegative).Select(zone => zone.Id).ToHashSet();
    return new { game.Elapsed, game.Winner, frame, corrections, hidden = game.HiddenObstacles.Count,
        players = game.Players.Select(p => new { p.Id, status = p.Status.ToString(), p.FinishTime, p.Collisions, p.BoostsEarned,
            hitIds = p.HitObstacleIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            positiveIds = p.SuccessfulRiskIds.Where(positiveIds.Contains).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            commands = RaceRunner.PrestartAcceptedCommands(game, p.Id), corrections = RaceRunner.PrestartCorrections(game, p.Id) }).ToArray() };
}

GameState Ready(PrestartBotConfig config)
{
    var game = RaceSimulation.CreatePrestartRace(config);
    Check(game.IsBot == config.StandaloneBot && game.IsParty != config.StandaloneBot && game.UsesPrestartBots && game.HumanCount == config.HumanCount,
        "Wrong mode, algorithm or human count");
    for (int i = 0; i < config.SlotCount; i++)
    {
        Check(game.GetBotDifficulty(i) == config.GetDifficulty(i), "Wrong bot difficulty");
        Check(RaceSimulation.SetCharacter(game, i, i + 1), "Character selection");
        if (game.IsBotSlot(i)) Check(!RaceRunner.Press(game, i), "Human press reached bot slot");
        else Check(RaceRunner.Press(game, i) && game.Players[i].Ready, "Human readiness not remembered during preparation");
    }
    Check(!RaceRunner.StartAllBotParty(game), "Unprepared race accepted common start");
    RaceRunner.Advance(game, 5);
    Check(game.Phase == "ready" && game.Elapsed == 0, "Physics started before preparation");
    return game;
}

void Start(GameState game, PrestartBotPreparation job)
{
    Check(RaceRunner.InstallPrestartPlans(game, job) && game.PrestartReady && game.PrestartSeed == job.Seed, "Verified plans did not attach");
    if (game.HumanCount == 0) Check(RaceRunner.StartAllBotParty(game), "All-bot common start");
    else for (int i = 0; i < game.Players.Length && game.Phase != "countdown"; i++)
        if (!game.IsBotSlot(i)) Check(RaceRunner.Press(game, i), "Human start with verified plan");
    Check(!RaceRunner.InstallPrestartPlans(game, job), "Replaced a running attempt");
}

var one = new BotDifficulty?[] { null, BotDifficulty.Master };
var four = new BotDifficulty?[] { BotDifficulty.Amateur, BotDifficulty.Novice, BotDifficulty.Strong, BotDifficulty.Master };
var hashes = new HashSet<string>();
foreach (string name in new[] { TracksFull.LongId, TracksMixed.Id, TracksHidden.ManifestationId })
{
    var package = NoReferences(name);
    Check(package.References.Length == 0 && hashes.Add(package.ContentHash), "Geometry unexpectedly equal or had an authored route");
    foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
    {
        var config = Config(package, flight, one, name == TracksHidden.ManifestationId, standalone: true);
        var game = Ready(config);
        var cold = Prepare(config, baseSeed);
        Start(game, cold.Prep);
        var outcome = Play(game);
        Check(game.Players[1].Status == PlayerStatus.Finished && RaceRunner.PrestartAcceptedCommands(game, 1) > 0, "Standalone bot did not execute full route");
        results.Add(new { name, config.Package.ContentHash, config.CacheKey, cold = cold.Metrics, outcome });
    }
}

var groupConfig = Config(NoReferences(TracksHidden.ManifestationId), FlightPresets.Chosen, four, true);
var group = Ready(groupConfig);
var groupCold = Prepare(groupConfig, baseSeed);
string groupCache = groupCold.Prep.ExportCache();
Check(!string.IsNullOrEmpty(groupCache) && groupCache.Length <= PrestartBotPreparation.MaximumCacheCharacters, "Export exceeded bounds");
Start(group, groupCold.Prep);
var groupOutcome = Play(group);
string groupLedger = Goals(group);
Check(group.Players.All(p => p.Status == PlayerStatus.Finished) && group.HiddenObstacles.Count > 0, "Hidden four-bot proof did not finish everyone");
Check(!RaceRunner.StartAllBotParty(group) && !RaceRunner.InstallPrestartPlans(group, groupCold.Prep), "Consumed attempt was reused");

var groupWarm = Prepare(groupConfig, baseSeed + 1, groupCache);
bool changedTape = false;
for (uint attempt = 1; attempt <= 8; attempt++)
{
    if (attempt > 1) groupWarm = Prepare(groupConfig, baseSeed + attempt, groupCache);
    Check(groupWarm.Prep.UsedCache && groupWarm.Prep.ExpandedStates == 0 && groupWarm.Prep.ReplaySteps > 0, "Warm attempt did not verify cached routes");
    changedTape = Enumerable.Range(0, four.Length).Any(slot => !groupCold.Prep.Plans.GetTape(slot).Select(edge => edge.Time)
        .SequenceEqual(groupWarm.Prep.Plans.GetTape(slot).Select(edge => edge.Time)));
    if (changedTape) break;
}
Check(changedTape, "Different seed did not select a different cached tape in the fixture");
Start(group, groupWarm.Prep);
Check(group.Players.Select(p => p.CharacterIndex).SequenceEqual(new[] { 1, 2, 3, 4 }), "Retry lost heroes");
var groupWarmOutcome = Play(group);
bool changedGoals = groupLedger != Goals(group);
results.Add(new { name = "hidden four-bot cold + warm retry", groupConfig.CacheKey, cacheCharacters = groupCache.Length,
    cold = groupCold.Metrics, warm = groupWarm.Metrics, groupOutcome, groupWarmOutcome, changedTape, changedGoals });

string Goals(GameState game)
{
    var positive = game.Track.RiskZones.Where(zone => !zone.IsCoin && !zone.IsNegative).Select(zone => zone.Id).ToHashSet();
    return string.Join("|", game.Players.Select(player => string.Join(",", player.HitObstacleIds.Order())
        + "/" + string.Join(",", player.SuccessfulRiskIds.Where(positive.Contains).Order())));
}

var mixed = Config(NoReferences(TracksHidden.ManifestationId), FlightPresets.FastFlip,
    new BotDifficulty?[] { null, BotDifficulty.Confident, null }, true, PartyCamera.Constant);
var mixedGame = Ready(mixed);
var mixedCold = Prepare(mixed, baseSeed);
Start(mixedGame, mixedCold.Prep);
results.Add(new { name = "mixed Constant with live divergence", mixed.CacheKey, cold = mixedCold.Metrics, outcome = Play(mixedGame, perturb: true) });

var mixedLagger = Config(mixed.Package, FlightPresets.FastFlip, new BotDifficulty?[] { null, BotDifficulty.Confident, null }, true);
var laggerGame = Ready(mixedLagger);
var laggerCold = Prepare(mixedLagger, baseSeed, mixedCold.Prep.ExportCache());
Check(!laggerCold.Prep.UsedCache, "Verification crossed camera configurations");
Start(laggerGame, laggerCold.Prep);
results.Add(new { name = "mixed Lagger with live divergence", mixedLagger.CacheKey, cold = laggerCold.Metrics, outcome = Play(laggerGame, perturb: true) });
Check(laggerGame.Players.All(p => p.Status == PlayerStatus.Finished), "Lagger stopped or eliminated a participant after divergence");

var template = NoReferences(TracksMixed.Id);
Check(TrackJson.TryRead(files["mixed-tricks.track.json"], out var importedSource, out failure), failure);
importedSource.Track.Id = "prestart-no-reference-fixture";
var imported = new TrackPackage(importedSource.Track, template.ProfilePolicy, template.WarningMode);
Check(TrackCatalog.TryImportLocal(TrackJson.Write(imported), out var local, out failure), failure);
Check(!TrackCatalog.SupportsBot(imported.Track.Id, FlightPresets.Chosen) && local.Package.References.Length == 0, "Local fixture unexpectedly admitted legacy bot");
var localConfig = Config(local.Package, FlightPresets.Chosen, one, standalone: true);
var localGame = Ready(localConfig);
var localCold = Prepare(localConfig, baseSeed, groupCache);
Check(!localCold.Prep.UsedCache && localCold.Prep.ExpandedStates > 0, "Cache crossed content/roster boundary");
Start(localGame, localCold.Prep);
results.Add(new { name = "unprepared local import", localConfig.CacheKey, cold = localCold.Metrics, outcome = Play(localGame) });

var guarded = new PrestartBotConfig(NoReferences(TracksFull.LongId), FlightPresets.Chosen, four,
    PartyCamera.Constant, false, SurfaceRunSettings.Default, .8, cameraStartLeft: 500);
var allEliminated = Prepare(guarded, baseSeed, expected: false);
Check(allEliminated.Prep.Failure.Contains("ни один"), "All eliminated accepted as a successful group proof");
Check(!RaceRunner.InstallPrestartPlans(RaceSimulation.CreatePrestartRace(guarded), allEliminated.Prep), "Failed group could attach plan");

var cancelled = new PrestartBotPreparation(groupConfig, 9, "malformed");
cancelled.Step(1);
cancelled.Cancel();
Check(cancelled.Complete && !cancelled.Success && cancelled.ExportCache() == null, "Cancelled route remained eligible");

var replayCacheConfig = Config(NoReferences(TracksFull.LongId), FlightPresets.FastFlip, one, standalone: true);
using var forgedCache = new MemoryStream();
using (var writer = new BinaryWriter(forgedCache, System.Text.Encoding.UTF8, true))
{
    writer.Write(replayCacheConfig.CacheKey);
    writer.Write((byte)0);
    writer.Write((byte)1);
    writer.Write((ushort)3);
    foreach (double time in new[] { 0d, .01, .02 }) writer.Write(time);
}
var repairedCache = Prepare(replayCacheConfig, baseSeed, Convert.ToBase64String(forgedCache.ToArray()));
Check(!repairedCache.Prep.UsedCache && repairedCache.Prep.ExpandedStates > 0, "Plausible but physically illegal cache did not fall back to bounded search");

var coreHashes = new[] { "PrestartBotConfig.cs", "PrestartBotPreparation.cs", "PrestartBotSearch.cs", "PrestartBotTape.cs", "RaceSimulation.cs", "RaceRunner.cs" }
    .ToDictionary(file => file, file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine("Assets/Scripts/Core", file)))));
if (args.Length > 0)
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { assertions, results, coreHashes }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS prestart bots: {assertions} assertions");
