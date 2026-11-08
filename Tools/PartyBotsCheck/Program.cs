using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

if (args.Length > 0 && args[0] == "--allocation")
{
    Environment.ExitCode = AllocationCheck.Run(args.Skip(1).ToArray());
    return;
}

const string trackFolder = "Assets/StreamingAssets/Tracks";
int assertions = 0;
var runs = new List<object>();
var levels = Enum.GetValues<BotDifficulty>();

void Check(bool condition, string context)
{
    if (!condition) throw new InvalidOperationException(context);
    assertions++;
}

void Close(double actual, double expected, string context)
{ Check(Math.Abs(actual - expected) < 1e-8, $"{context}: expected {expected:R}, got {actual:R}"); }

void Reject<T>(Action action, string context) where T : Exception
{
    try { action(); }
    catch (T) { assertions++; return; }
    throw new InvalidOperationException(context);
}

var files = Directory.GetFiles(trackFolder, "*.track.json").ToDictionary(Path.GetFileName, File.ReadAllText);
Check(TrackCatalog.TryInstallBuiltIns(File.ReadAllText(Path.Combine(trackFolder, "index.json")), files, out string error), error);

Track GetTrack(string id)
{ Check(TrackCatalog.TryGet(id, out var entry), "Track missing: " + id); return entry.Package.Track; }

Track shortTrack = GetTrack(TracksFull.ShortId);
Track hiddenTrack = GetTrack(TracksHidden.ManifestationId);
ForecastWorkspaceCheck.Run(hiddenTrack, Check);

void StartOrRetry(GameState game)
{
    if (game.HumanCount == 0) Check(RaceRunner.StartAllBotParty(game), "Explicit all-bot start/retry");
    else
    {
        Check(!RaceRunner.StartAllBotParty(game), "Mixed party could bypass human confirmation");
        for (int slot = game.Players.Length - 1; slot >= 0; slot--)
            if (!game.IsBotSlot(slot))
                Check((game.Phase is "ready" or "finished") && RaceRunner.Press(game, slot), "Party started before its last human");
    }
    Check(game.Phase == "countdown" && !RaceRunner.StartAllBotParty(game), "Missing or duplicate countdown");
}

// Cover every possible lineup, including all-bot parties of each size.
foreach (int count in new[] { 2, 3, 4 })
{
    for (int mask = 0; mask < (1 << count); mask++)
    {
        var lineup = Enumerable.Range(0, count).Select(slot => (mask & (1 << slot)) != 0
            ? (BotDifficulty?)levels[(slot + mask) % levels.Length] : null).ToArray();
        var game = RaceSimulation.CreateParty(shortTrack, FlightPresets.Chosen, lineup, 20);
        Check(game.IsParty && !game.IsBot && !game.IsSolo && game.HumanCount == lineup.Count(level => level == null), "Party identity or human count");
        var seeds = new HashSet<uint>();
        for (int slot = 0; slot < count; slot++)
        {
            Check(game.IsBotSlot(slot) == lineup[slot].HasValue, "Role outside its slot");
            var settings = game.GetPartyBotSettings(slot);
            Check(settings?.Difficulty == lineup[slot], "Difficulty outside its slot");
            if (settings == null) continue;
            Check(seeds.Add(settings.Seed), "Two bots received the same seed");
            Check(!RaceRunner.Press(game, slot) && !game.Players[slot].Ready, "Bot readiness was externally set");
            lineup[slot] = null;
            Check(game.IsBotSlot(slot), "Changing caller's lineup affected the race");
        }
        RaceRunner.Advance(game, 10);
        Check(game.Phase == "ready" && game.Elapsed == 0, "Idle party started by advancing time");
        StartOrRetry(game);
        RaceRunner.Advance(game, 2, paused: true);
        Close(game.CountdownRemaining, RaceConfig.CountdownDuration, "Paused countdown");
    }
}

void RunParty(int botCount, Track track, FlightSettings flight, PartyCamera camera, bool acceleration)
{
    var lineup = Enumerable.Range(0, 4).Select(i => i < 4 - botCount ? null : (BotDifficulty?)levels[(i + botCount) % levels.Length]).ToArray();
    var game = RaceSimulation.CreateParty(track, flight, lineup, 0x9b66cda5u, camera);
    var accelerationSettings = new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 30, 100, true);
    RaceSimulation.ConfigureSurfaceRun(game, acceleration, accelerationSettings);
    HiddenObstacles.ConfigureWarning(game, .6);
    var events = lineup.Select(_ => new List<BotSlice>()).ToArray();
    var controllers = lineup.Select((_, i) => RaceRunner.GetPartyBotController(game, i)).ToArray();
    for (int slot = 0; slot < lineup.Length; slot++)
    {
        Check(RaceSimulation.SetCharacter(game, slot, (slot + 3) % RaceConfig.CharacterCount), "Character choice");
        var controller = controllers[slot];
        if (controller == null) continue;
        controller.Observer = events[slot].Add;
        Reject<ArgumentException>(() => controller.Advance(.1), "Owned bot advanced the whole race by itself");
    }
    StartOrRetry(game);
    RaceRunner.Advance(game, 3.1);
    Close(game.Elapsed, .1, "Countdown remainder");
    Check(game.Phase == "racing" && !RaceRunner.StartAllBotParty(game), "Explicit start interrupted active race");
    for (int slot = 0; slot < lineup.Length; slot++)
        if (lineup[slot].HasValue) Check(!RaceRunner.Press(game, slot), "Human ingress accepted a bot slot in an active race");

    var replay = RaceSnapshot.Clone(game);
    var direct = RaceSnapshot.Clone(game);
    RaceRunner.Advance(replay, 1.1);
    RaceSimulation.Advance(direct, 1.1);
    Close(replay.Elapsed, direct.Elapsed, "Snapshot clock");
    Close(game.Elapsed, .1, "Snapshot advanced live clock");
    for (int slot = 0; slot < lineup.Length; slot++)
    {
        Check(RaceRunner.GetPartyBotController(replay, slot) == null, "Snapshot inherited a live bot");
        Close(replay.Players[slot].Distance, direct.Players[slot].Distance, "Snapshot replay acquired bot input");
    }

    if (botCount == 3 && camera == PartyCamera.Constant) game.Players[0].Recovering = 100;
    bool continuedAfterHuman = false;
    int frames = 0;
    for (; frames < 1200 && game.Phase == "racing"; frames++)
    {
        if (frames % 11 == 0)
            for (int slot = 0; slot < game.HumanCount; slot++) RaceRunner.Press(game, slot);
        RaceRunner.Advance(game, .05);
        if (botCount == 3 && game.Players[0].Status == PlayerStatus.Eliminated && game.Phase == "racing")
            continuedAfterHuman = true;
    }
    Check(game.Phase == "finished" && game.Players.All(player => player.TerminalTime.HasValue), "Expected an outcome for every participant");
    Close(game.Elapsed, game.Players.Max(player => player.TerminalTime.Value), "Final shared clock");
    if (camera == PartyCamera.Lagger) Check(game.Players.All(player => player.Status == PlayerStatus.Finished), "Lagger camera eliminated a player");
    if (botCount == 3 && camera == PartyCamera.Constant) Check(continuedAfterHuman, "Last human elimination ended the bots' race");
    if (ReferenceEquals(track, hiddenTrack))
        Check(game.HiddenObstacles.Count > 0 && game.HiddenObstacles.Values.All(state => state.RevealedAt.HasValue
            && state.TriggerPlayerId is >= 0 and < 4 && (!state.SolidAt.HasValue || state.SolidAt >= state.RevealedAt)), "Invalid shared hidden timeline");

    var eventCounts = events.Select(list => list.Count).ToArray();
    RaceRunner.Advance(game, 3);
    Check(game.Phase == "finished" && eventCounts.SequenceEqual(events.Select(list => list.Count)), "Idle finished race emitted callbacks or restarted");
    for (int slot = 0; slot < lineup.Length; slot++)
    {
        var controller = controllers[slot];
        if (controller == null) continue;
        double end = game.Players[slot].TerminalTime.Value;
        Check(controller.Cursor > 0 && controller.Decisions.Count > 0, "A configured bot never controlled its participant");
        Check(events[slot].All(edge => edge.Time <= end + 1e-8) && controller.Decisions.All(d => d.Outcome != "pending"), "Post-terminal event or stale decision");
        Close(events[slot].Where(edge => edge.Kind == "advance").Sum(edge => edge.Delta), end, "Source active span");
    }
    runs.Add(new { botCount, track = track.Id, flight = flight == FlightPresets.Chosen ? "Chosen" : "FastFlip",
        camera = camera.ToString(), acceleration, frames, game.Elapsed, game.Winner, hidden = game.HiddenObstacles.Count,
        players = game.Players.Select((player, i) => new { player.Id, status = player.Status.ToString(), player.TerminalTime,
            player.Collisions, player.BoostsEarned, difficulty = lineup[i]?.ToString(), seed = game.GetPartyBotSettings(i)?.Seed,
            presses = controllers[i]?.Cursor }).ToArray() });

    StartOrRetry(game);
    Check(game.HiddenObstacles.Count == 0 && game.Elapsed == 0, "Retry retained live timeline");
    var seeds = controllers.Where(controller => controller != null).Select(controller => controller.Settings.Seed).ToHashSet();
    for (int slot = 0; slot < lineup.Length; slot++)
    {
        Check(game.Players[slot].CharacterIndex == (slot + 3) % RaceConfig.CharacterCount && game.Players[slot].SurfaceRunEnabled == acceleration,
            "Retry lost heroes or surface acceleration");
        if (lineup[slot] == null) continue;
        var fresh = RaceRunner.GetPartyBotController(game, slot);
        Check(!ReferenceEquals(fresh, controllers[slot]) && fresh.Cursor == 0 && fresh.Decisions.Count == 0, "Retry carried planner state");
        Check(fresh.Settings.Difficulty == lineup[slot] && seeds.Add(fresh.Settings.Seed), "Retry lost difficulty or reused a seed");
    }
    Check(game.HiddenWarningDuration == .6 && game.SurfaceRunSettings.Equals(accelerationSettings), "Retry changed common options");
}

RunParty(0, shortTrack, FlightPresets.Chosen, PartyCamera.Lagger, false);
RunParty(1, shortTrack, FlightPresets.FastFlip, PartyCamera.Constant, true);
RunParty(2, shortTrack, FlightPresets.Chosen, PartyCamera.Lagger, false);
RunParty(3, shortTrack, FlightPresets.FastFlip, PartyCamera.Constant, true);
RunParty(4, hiddenTrack, FlightPresets.Chosen, PartyCamera.Lagger, true);

var bots = new BotDifficulty?[] { BotDifficulty.Amateur, null };
var customLikePreset = new FlightSettings(FirstAction.Jump, MotionLaw.Gravity, AirRepeatLimit.Unlimited, 662.5, 0, 350);
Check(TrackPackage.MatchesProfile("Chosen", customLikePreset), "Custom fixture does not match Chosen values");
Reject<NotSupportedException>(() => RaceSimulation.CreateParty(shortTrack, customLikePreset, bots, 1), "Numerically identical custom admitted bots");
Check(RaceSimulation.CreateParty(shortTrack, customLikePreset, new BotDifficulty?[2], 1).HumanCount == 2, "Custom broke a human party");
Reject<NotSupportedException>(() => RaceSimulation.CreateParty(GetTrack(TracksRethink.Id), FlightPresets.FastFlip, bots, 1), "Rethink admitted FastFlip bot");
Check(TrackJson.TryRead(files["full-short.track.json"], out var localPackage, out error), error);
localPackage.Track.Id = "party-bots-prepared-import";
string localHash = TrackFingerprint.Compute(localPackage.Track, localPackage.ProfilePolicy, localPackage.WarningMode);
var imported = new TrackPackage(localPackage.Track, localPackage.ProfilePolicy, localPackage.WarningMode,
    localPackage.References.Select(reference => new TrackBotReference(reference.ProfileId, localHash, reference.Inputs,
        reference.RewardIds, reference.BonusGain)).ToArray());
Check(TrackCatalog.TryImportLocal(TrackJson.Write(imported), out var local, out error), error);
Reject<NotSupportedException>(() => RaceSimulation.CreateParty(local.Package.Track, FlightPresets.Chosen, bots, 1), "Unproved import admitted bots");
var proof = local.GetOrCreateProof(FlightPresets.Chosen);
for (int i = 0; i < 200000 && !proof.Complete; i++) proof.Step(32);
Check(proof.Success, "Local fixture proof failed: " + proof.Failure);
Check(RaceSimulation.CreateParty(local.Package.Track, FlightPresets.Chosen, bots, 1).IsBotSlot(0), "Prepared import rejected bots");
local.Package.Track.Obstacles[0].Width++;
Reject<NotSupportedException>(() => RaceSimulation.CreateParty(local.Package.Track, FlightPresets.Chosen, bots, 1), "Mutated import admitted bots");

var coreHashes = new[] { "ContinuousMotion.cs", "RaceSimulation.cs", "RaceRunner.cs", "RaceSnapshot.cs", "StochasticBotController.cs" }
    .ToDictionary(file => file, file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine("Assets/Scripts/Core", file)))));
if (args.Length > 0)
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { assertions, lineups = 28, runs, coreHashes }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS party bots: {assertions} assertions, 28 lineups, {runs.Count} full races");
