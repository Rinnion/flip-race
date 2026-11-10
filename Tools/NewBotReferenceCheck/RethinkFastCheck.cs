using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;

static class RethinkFastCheck
{
    internal static void Run(string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new ArgumentException("--rethink-fast <5.8-rethink.track.json> [report.json]");
        const string trackPath = "Assets/StreamingAssets/Tracks";
        string original = File.ReadAllText(args[0]);
        var files = Directory.GetFiles(trackPath, "*.track.json").ToDictionary(Path.GetFileName, File.ReadAllText);
        string current = files["rethink.track.json"];
        int checks = 0;
        var results = new List<object>();

        void Check(bool ok, string context)
        {
            if (!ok) throw new InvalidOperationException(context);
            checks++;
        }

        var chosen = FlightPresets.Chosen;
        var fast = FlightPresets.FastFlip;
        var fastEquivalent = new FlightSettings(fast.FirstAction, fast.Law, fast.AirRepeats, fast.GravityAcceleration,
            fast.FlipImpulse, fast.JumpImpulse, fast.LinearSpeed, fast.LinearJumpHeight);
        Check(TrackCatalog.SupportsProfile(TracksRethink.Id, fastEquivalent) && TrackCatalog.SupportsBot(TracksRethink.Id, fast), "Fallback profile/catalog");
        Check(!TrackCatalog.SupportsProfile(TracksRethink.Id, FlightPresets.Moderate), "Fallback opened arbitrary flight");
        Check(StochasticTrackMap.Create(TracksRethink.Get(), fast).ReferenceInputs.SequenceEqual(BotReferenceRoutes.Get(TracksRethink.Id, fast).Inputs),
            "Fallback Fast reference differs");

        Check(TrackJson.TryRead(original, out var baseline, out string error), error);
        Check(TrackJson.TryRead(current, out var parsed, out error), error);
        Check(baseline.ProfilePolicy == TrackProfilePolicy.ChosenOnly && !baseline.SupportsProfile(fast), "Historical chosen-only semantics changed");
        Check(parsed.ProfilePolicy == TrackProfilePolicy.ChosenOrFast && parsed.WarningMode == TrackWarningMode.Authored, "New profile policy or author warnings missing");
        Check(TrackJson.Write(parsed) == current, "Packaged JSON differs from canonical codec output");
        var oldTrack = JsonNode.Parse(original)["track"];
        var newTrack = JsonNode.Parse(current)["track"];
        oldTrack["focus"] = newTrack["focus"].GetValue<string>();
        Check(JsonNode.DeepEquals(oldTrack, newTrack), "A track field besides the focus text changed");
        var oldChosen = JsonNode.Parse(original)["botReferences"]![0];
        var newChosen = JsonNode.Parse(current)["botReferences"]![0];
        oldChosen["trackHash"] = newChosen["trackHash"].GetValue<string>();
        Check(JsonNode.DeepEquals(oldChosen, newChosen), "Chosen reference changed beyond the reissued hash");
        Check(TrackCatalog.TryInstallBuiltIns(File.ReadAllText(Path.Combine(trackPath, "index.json")), files, out error), error);
        Check(TrackCatalog.Entries.Length == 8 && TrackCatalog.Entries.Sum(entry => entry.Package.References.Length) == 16, "Shipped catalogue count");
        Check(TrackCatalog.TryGet(TracksRethink.Id, out var entry), "Missing Rethink");
        var package = entry.Package;
        var track = package.Track;
        Check(TrackCatalog.UsesAuthoredWarnings(track.Id) && track.Obstacles.Where(o => o.Hidden).All(o => o.HiddenWarningDuration == .35), "Authored warning changed");
        Check(TrackCatalog.SupportsProfile(track.Id, chosen) && TrackCatalog.SupportsProfile(track.Id, fastEquivalent)
            && !TrackCatalog.SupportsProfile(track.Id, FlightPresets.Moderate), "Installed exact profile policy");
        Check(!TrackCatalog.TryImportLocal(current, out _, out _), "Reserved ID could replace the built-in track");
        var probe = RaceSimulation.CreateSolo(track, fast, PartyCamera.Lagger, -288.8, 1520);
        HiddenObstacles.ConfigureWarning(probe, 1.2);
        Check(track.Obstacles.Where(o => o.Hidden).All(o => HiddenObstacles.WarningDurationFor(probe, o) == .35), "Global setting replaced author duration");

        foreach (var flight in new[] { chosen, fast })
        {
            string profile = flight == chosen ? "Chosen" : "FastFlip";
            Check(TrackCatalog.SupportsBot(track.Id, flight), "Catalog bot admission " + profile);
            Check(package.TryGetReference(flight, out var reference) && reference.Inputs.SequenceEqual(BotReferenceRoutes.Get(track.Id, flight).Inputs),
                "Shipped/fallback witness drift " + profile);
            var proof = new PreparedReferenceProof(package, flight);
            while (!proof.Complete) proof.Step(192);
            Check(proof.Success && proof.CompletedRuns == 2, profile + " authored proof " + proof.Failure);
            results.Add(new { kind = "reference-and-legacy-Master-proof", profile, proof.CompletedRuns, reference.BonusGain, inputs = reference.Inputs, goals = reference.RewardIds });
        }

        for (int count = 2; count <= 4; count++)
        foreach (var camera in new[] { PartyCamera.Constant, PartyCamera.Lagger })
        {
            var party = RaceSimulation.CreateParty(track, fastEquivalent, count, camera, -288.8, 1520);
            for (int slot = 0; slot < count; slot++) Check(RaceRunner.Press(party, slot), "Human party input");
            RaceRunner.Advance(party, 3.5);
            Check(party.Phase == "racing" && party.Players.All(p => p.Distance > 0)
                && party.Track == track && party.Flight == fastEquivalent, "Fast-equivalent human party did not start with its own profile");
        }

        foreach (bool surface in new[] { false, true })
        {
            var config = new PrestartBotConfig(package, fast, new BotDifficulty?[] { null, BotDifficulty.Master }, PartyCamera.Lagger,
                surface, SurfaceRunSettings.Default, .8, standaloneBot: true);
            var preparation = new PrestartBotPreparation(config, 12101);
            while (!preparation.Complete) preparation.Step(192);
            Check(preparation.Success, "Prestart preparation: " + preparation.Failure);
            var game = RaceSimulation.CreatePrestartRace(config);
            Check(RaceRunner.InstallPrestartPlans(game, preparation) && RaceRunner.Press(game, 0), "Prestart start gate");
            Play(game);
            var bot = game.Players[1];
            Check(bot.Status == PlayerStatus.Finished && bot.Collisions == 0 && bot.CompletedBoosts == 3
                && Math.Abs(bot.FinishBoostElapsed - .25) < 1e-8 && RaceRunner.PrestartCorrections(game, 1) == 0, "Fixed prestart Master replay");
            results.Add(new { kind = "prestart-Master", surface, preparation.ExpandedStates, preparation.ReplaySteps,
                finish = Result(bot), commands = RaceRunner.PrestartAcceptedCommands(game, 1) });
        }

        foreach (var difficulty in Enum.GetValues<BotDifficulty>())
        {
            bool surface = difficulty == BotDifficulty.Master;
            uint seed = surface ? 0u : 123u;
            var game = RaceSimulation.CreateStochasticBotRace(track, fast, difficulty, seed, PartyCamera.Lagger);
            RaceSimulation.ConfigureSurfaceRun(game, surface);
            Check(RaceRunner.Press(game, 0), "Legacy bot start");
            var bot = new StochasticBotController(game);
            Play(game, bot);
            var player = game.Players[1];
            Check(player.Status == PlayerStatus.Finished && bot.Cursor > 0, "Legacy bot runtime " + difficulty);
            results.Add(new { kind = "legacy", difficulty = difficulty.ToString(), surface, seed, finish = Result(player), commands = bot.Cursor });
        }

        if (args.Length == 2)
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { checks, package.ContentHash, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS Rethink Fast: {checks} checks, {results.Count} proof/runtime rows, hash {package.ContentHash}");
    }

    static void Play(GameState game, IRaceScheduledInput input = null)
    {
        RaceRunner.Advance(game, RaceConfig.CountdownDuration, input);
        for (int frame = 0; frame < 7200 && game.Phase == "racing"; frame++) RaceRunner.Advance(game, 1d / 60, input);
        if (game.Phase != "finished") throw new InvalidOperationException("Race did not reach a terminal phase");
    }

    static object Result(PlayerState player) => new { player.FinishTime, player.Collisions, player.SlowsEarned,
        player.CompletedBoosts, player.FinishBoostElapsed, rewards = player.SuccessfulRiskIds.Order().ToArray() };
}
