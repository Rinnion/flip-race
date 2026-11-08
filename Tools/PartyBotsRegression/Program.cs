using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlipRace.Core;

static partial class Program
{
    static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    static readonly JsonSerializerOptions ReportJson = new(Json) { WriteIndented = true };
    static int checks;

    static void Need(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { checks++; return; }
        throw new InvalidOperationException(message);
    }

    static byte[] World(GameState game) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        game.Mode, game.IsParty, game.IsSolo, game.IsBot, game.Phase, game.Elapsed, game.CountdownRemaining,
        game.Winner, game.WinnerAt, game.PartyCamera, game.PartyCameraLeft, game.PartyLastCameraLeft,
        game.HiddenWarningDuration, game.HiddenObstacles, game.SurfaceRunEnabled, game.SurfaceRunSettings, game.Players
    }, Json);

    static void Same(GameState first, GameState second, string message) =>
        Need(World(first).AsSpan().SequenceEqual(World(second)), message);

    static TrackCatalogEntry[] Install(string tracksPath)
    {
        string manifest = File.ReadAllText(Path.Combine(tracksPath, "index.json"));
        var files = Directory.GetFiles(tracksPath, "*.track.json").ToDictionary(Path.GetFileName, File.ReadAllText);
        Need(TrackCatalog.TryInstallBuiltIns(manifest, files, out string error), error);
        return TrackCatalog.Entries;
    }

    static Dictionary<string, string> SourceHashes(string corePath) => Directory.GetFiles(corePath, "*.cs")
        .OrderBy(Path.GetFileName, StringComparer.Ordinal)
        .ToDictionary(Path.GetFileName, file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));

    static bool AllBot(BotDifficulty?[] slots) => slots.All(difficulty => difficulty.HasValue);

    static void Ready(GameState race, BotDifficulty?[] slots)
    {
        Need(race.Phase is "ready" or "finished", "Fixture cannot start from current phase");
        string waiting = race.Phase;
        if (AllBot(slots))
        {
            Need(RaceRunner.StartAllBotParty(race), "Explicit all-bot start or retry was rejected");
            Need(race.Phase == "countdown", "All-bot explicit start did not enter countdown");
            return;
        }

        int[] humans = Enumerable.Range(0, slots.Length).Where(slot => !slots[slot].HasValue).Reverse().ToArray();
        for (int index = 0; index < humans.Length; index++)
        {
            Need(RaceRunner.Press(race, humans[index]), "Human readiness was rejected");
            Need(race.Phase == (index == humans.Length - 1 ? "countdown" : waiting),
                "Race started before every human was ready");
        }
    }

    static void Roles(GameState race, BotDifficulty?[] slots)
    {
        Need(race.IsParty && race.Players.Length == slots.Length, "Party mode or slot count changed");
        Need(race.HumanCount == slots.Count(difficulty => !difficulty.HasValue), "Human count did not match composition");
        var seeds = new HashSet<uint>();
        for (int slot = 0; slot < slots.Length; slot++)
        {
            Need(race.IsBotSlot(slot) == slots[slot].HasValue, "Role assigned to wrong slot");
            var settings = race.GetPartyBotSettings(slot);
            Need(settings?.Difficulty == slots[slot], "Difficulty assigned to wrong slot");
            if (settings != null)
                Need(seeds.Add(settings.Seed), "Bots in the same attempt received a duplicate seed");
        }
    }

    static int Compositions(Track track)
    {
        var difficulties = Enum.GetValues<BotDifficulty>();
        int cases = 0;
        for (int count = 2; count <= 4; count++)
        {
            for (int mask = 0; mask < 1 << count; mask++)
            {
                var slots = Enumerable.Range(0, count).Select(slot => (mask & (1 << slot)) == 0
                    ? (BotDifficulty?)null : difficulties[(slot + mask) % difficulties.Length]).ToArray();
                const uint seed = 0xf12862a1;
                var race = RaceSimulation.CreateParty(track, FlightPresets.Chosen, slots, seed, PartyCamera.Lagger);
                var sameSeed = RaceSimulation.CreateParty(track, FlightPresets.Chosen, slots, seed, PartyCamera.Lagger);
                Roles(race, slots);
                for (int slot = 0; slot < count; slot++)
                {
                    Need(race.GetPartyBotSettings(slot)?.Seed == sameSeed.GetPartyBotSettings(slot)?.Seed,
                        "Same base seed and slot were not reproducible");
                    Need(RaceSimulation.SetCharacter(race, slot, (slot + 1) % RaceConfig.CharacterCount),
                        "A party character could not be selected in ready state");
                    if (race.IsBotSlot(slot)) Need(!RaceRunner.Press(race, slot), "Bot accepted a human readiness input");
                }
                RaceRunner.Advance(race, 4);
                Need(race.Phase == "ready" && race.Elapsed == 0, "Waiting on ready screen started a race");
                if (!AllBot(slots)) Need(!RaceRunner.StartAllBotParty(race), "A mixed or all-human party bypassed readiness");
                Ready(race, slots);
                for (int slot = 0; slot < count; slot++)
                {
                    Need(race.Players[slot].CharacterIndex == (slot + 1) % RaceConfig.CharacterCount, "Ready transition lost a character");
                    Need(!RaceRunner.Press(race, slot), "A press was accepted during countdown");
                }
                Need(!RaceRunner.StartAllBotParty(race), "All-bot start restarted countdown");
                RaceRunner.Advance(race, 3.075);
                Need(race.Phase == "racing" && Math.Abs(race.Elapsed - .075) < 1e-12,
                    "Common elapsed time was multiplied by number of sources");
                cases++;
            }
        }
        Need(cases == 28, "Missing a role arrangement for 2, 3, or 4 slots");
        return cases;
    }

    static void HumanOnly(Track track)
    {
        var legacy = RaceSimulation.CreateParty(track, FlightPresets.FastFlip, 4, PartyCamera.Lagger, -288.8, 1520);
        var slots = new BotDifficulty?[4];
        var candidate = RaceSimulation.CreateParty(track, FlightPresets.FastFlip, slots, 7654, PartyCamera.Lagger);
        var surface = new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 40, 0, false);
        RaceSimulation.ConfigureSurfaceRun(legacy, true, surface);
        RaceSimulation.ConfigureSurfaceRun(candidate, true, surface);
        Ready(legacy, slots);
        Ready(candidate, slots);
        RaceRunner.Advance(legacy, 3);
        RaceRunner.Advance(candidate, 3);
        Same(legacy, candidate, "New all-human factory differs after countdown");
        for (int frame = 0; frame < 80; frame++)
        {
            if (frame % 9 == 0)
                for (int slot = 0; slot < 4; slot++)
                    Need(RaceRunner.Press(legacy, slot) == RaceRunner.Press(candidate, slot), "All-human press legality changed");
            RaceRunner.Advance(legacy, .04);
            RaceRunner.Advance(candidate, .04);
            Same(legacy, candidate, "New all-human factory changed the physical timeline");
        }
    }

    static void Admission(Track track, Track restricted)
    {
        var slots = new BotDifficulty?[] { null, BotDifficulty.Novice, BotDifficulty.Master };
        Throws<ArgumentException>(() => RaceSimulation.CreateParty(track, FlightPresets.Chosen,
            new BotDifficulty?[] { null }, 1), "A one-slot party was accepted");
        Throws<ArgumentException>(() => RaceSimulation.CreateParty(track, FlightPresets.Chosen,
            new BotDifficulty?[5], 1), "A five-slot party was accepted");
        Throws<ArgumentException>(() => RaceSimulation.CreateParty(track, FlightPresets.Chosen,
            new BotDifficulty?[] { (BotDifficulty)255, null }, 1), "Invalid bot difficulty was accepted");
        Throws<NotSupportedException>(() => RaceSimulation.CreateParty(restricted, FlightPresets.FastFlip, slots, 1),
            "An incompatible authored profile received a bot");
        Throws<NotSupportedException>(() => RaceSimulation.CreateParty(track, FlightPresets.Linear, slots, 1),
            "An unsupported profile received a bot");
        var input = new BotDifficulty?[] { BotDifficulty.Amateur, null };
        var immutable = RaceSimulation.CreateParty(track, FlightPresets.Chosen, input, 111);
        input[0] = null;
        input[1] = BotDifficulty.Master;
        Roles(immutable, new BotDifficulty?[] { BotDifficulty.Amateur, null });
    }

    static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: PartyBotsRegression <track-directory> <core-source-directory> <report.json>");
            return 2;
        }
        try
        {
            var sources = SourceHashes(args[1]);
            var entries = Install(args[0]);
            var tracks = entries.ToDictionary(entry => entry.Package.Track.Id, entry => entry.Package);
            var mixed = tracks[TracksMixed.Id];
            var hidden = tracks[TracksHidden.InterceptsId];
            Admission(mixed.Track, tracks[TracksRethink.Id].Track);
            int compositions = Compositions(mixed.Track);
            HumanOnly(mixed.Track);
            ScheduledContract();
            Console.WriteLine($"PASS {compositions} compositions, admission and human-only equality");
            var runtime = RuntimeCases(mixed, hidden);
            Need(sources.SequenceEqual(SourceHashes(args[1])), "Core files changed during the run");
            var report = new { Status = "PASS", checks, Compositions = compositions, SourceSha256 = sources, Runtime = runtime };
            File.WriteAllText(args[2], JsonSerializer.Serialize(report, ReportJson));
            Console.WriteLine($"PASS {checks} party-bot assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
