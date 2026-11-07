#if !BASELINE
using System.Text.Json;
using FlipRace.Core;

static partial class Program
{
    static IEnumerable<SurfaceRunSettings> SurfaceSettings()
    {
        foreach (int rate in Enumerable.Range(1, 5))
        foreach (var curve in Enum.GetValues<SurfaceRunCurve>())
        foreach (int cap in new[] { 20, 30, 40 })
        foreach (int decay in new[] { 0, 50, 100, 200, 400 })
        foreach (bool reset in new[] { false, true })
            yield return new SurfaceRunSettings(rate, curve, cap, decay, reset);
    }

    static void EqualPhysicalPlayers(GameState bot, GameState party, string label)
    {
        byte[] actual = JsonSerializer.SerializeToUtf8Bytes(bot.Players, Json);
        byte[] expected = JsonSerializer.SerializeToUtf8Bytes(party.Players, Json);
        if (!actual.AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException(label + ": human and bot physical player states differ");
    }

    static void SurfaceMatrixCase(Track track, FlightSettings flight, StochasticBotSettings botSettings,
        SurfaceRunSettings surface)
    {
        var bot = RaceSimulation.CreateStochasticBotRace(track, flight, botSettings, PartyCamera.Lagger);
        var party = RaceSimulation.CreateParty(track, flight, 2, PartyCamera.Lagger, -288.8, 1520);
        RaceSimulation.ConfigureSurfaceRun(bot, true, surface);
        RaceSimulation.ConfigureSurfaceRun(party, true, surface);
        Require(RaceRunner.Press(bot, 0) && RaceRunner.Press(party, 1) && RaceRunner.Press(party, 0), "Readiness failed");
        RaceRunner.Advance(bot, 3);
        RaceRunner.Advance(party, 3);
        EqualPhysicalPlayers(bot, party, "countdown");
        bool airborne = false, heldReserve = false, sawCollision = false;
        for (int i = 1; i <= 160; i++)
        {
            // Leave the first floor rock unavoided, then exercise airborne input.
            if (i is 70 or 71 or 95 or 115 or 137)
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    bool actual = RaceRunner.Press(bot, slot);
                    bool expected = RaceRunner.Press(party, slot);
                    Require(actual == expected, "Identical action legality changed between BOT and human-only modes");
                    EqualPhysicalPlayers(bot, party, "press");
                }
            }
            RaceRunner.Advance(bot, .05);
            RaceRunner.Advance(party, .05);
            EqualPhysicalPlayers(bot, party, "advance");
            airborne |= bot.Players.Any(p => p.Flipping);
            heldReserve |= bot.Players.All(p => p.SurfaceRunEnabled && p.SurfaceRunSettings.Equals(surface) && p.SurfaceRunReserve > 0);
            if (!sawCollision && bot.Players.Any(p => p.Collisions > 0))
            {
                Require(bot.Players.All(p => p.Recovering > 0
                    && (surface.ResetOnCollision ? p.SurfaceRunReserve == 0 : p.SurfaceRunReserve > 0)),
                    "Collision did not apply the selected reserve reset to both players");
                sawCollision = true;
            }
        }
        Require(airborne && heldReserve && sawCollision, "Expected surface, air and collision witness in each combination");
    }

    static void HiddenForecast(Track track, FlightSettings flight, SurfaceRunSettings surface)
    {
        var live = RaceSimulation.CreateStochasticBotRace(track, flight, BotDifficulty.Master, 0, cameraStartLeft: -5000);
        RaceSimulation.ConfigureSurfaceRun(live, true, surface);
        HiddenObstacles.ConfigureWarning(live, .6);
        Require(RaceRunner.Press(live, 0), "Hidden fixture could not start");
        RaceRunner.Advance(live, 3);
        bool Revealed() => live.HiddenObstacles.Values.Any(s => s.Phase != HiddenObstaclePhase.Hidden);
        while (live.Phase == "racing" && live.Elapsed < 25 && !Revealed())
            RaceRunner.Advance(live, 1d / 60);
        Require(Revealed() && live.Players.All(p => p.SurfaceRunReserve > 0),
            "Expected real shared reveal and live reserve");
        byte[] before = World(live);
        var projected = RaceSnapshot.ForBotForecast(live, 1);
        Require(projected.PartyCamera == PartyCamera.Lagger && projected.SurfaceRunEnabled
            && projected.SurfaceRunSettings.Equals(surface) && projected.Players.Length == 2,
            "Forecast lost camera policy, settings or active hidden-world participants");
        Require(projected.Elapsed == live.Elapsed && projected.HiddenWarningDuration == live.HiddenWarningDuration,
            "Forecast lost race clocks");
        foreach (var player in live.Players)
        {
            var copy = projected.Players.Single(p => p.Id == player.Id);
            Require(!ReferenceEquals(copy, player) && JsonSerializer.Serialize(copy, Json) == JsonSerializer.Serialize(player, Json),
                "Forecast lost player input legality, physics or reward state");
        }
        Require(JsonSerializer.Serialize(projected.HiddenObstacles, Json) == JsonSerializer.Serialize(live.HiddenObstacles, Json)
            && live.HiddenObstacles.All(pair => !ReferenceEquals(pair.Value, projected.HiddenObstacles[pair.Key])),
            "Forecast lost or shared hidden timeline");
        RaceRunner.Press(projected, 0);
        RaceRunner.Advance(projected, .5);
        Require(before.AsSpan().SequenceEqual(World(live)), "Forecast changed the live world");
    }

    static TrackCatalogEntry PrepareImport(TrackPackage original, FlightSettings[] flights)
    {
        Require(TrackJson.TryRead(TrackJson.Write(original), out var copy, out var error), error);
        copy.Track.Id = "v56-regression-import";
        copy.Track.Name = "V56 regression import";
        copy.Track.Length += 220;
        copy.Track.Obstacles[0].X += .1;
        string hash = TrackFingerprint.Compute(copy.Track, copy.ProfilePolicy, copy.WarningMode);
        var references = original.References.Select(reference => new TrackBotReference(reference.ProfileId, hash,
            reference.Inputs, reference.RewardIds, reference.BonusGain)).ToArray();
        var local = new TrackPackage(copy.Track, copy.ProfilePolicy, copy.WarningMode, references);
        Require(TrackCatalog.TryImportLocal(TrackJson.Write(local), out var entry, out error), error);
        foreach (var flight in flights)
        {
            Require(!TrackCatalog.SupportsBot(local.Track.Id, flight), "Imported bot enabled before ordinary reference/Master proof");
            var proof = entry.GetOrCreateProof(flight);
            for (int step = 0; step < PreparedReferenceProof.MaximumTotalSteps && !proof.Complete; step += 32)
                proof.Step(32);
            Require(proof.Success && proof.CompletedRuns == proof.RequiredRuns && TrackCatalog.SupportsBot(local.Track.Id, flight),
                $"Imported reference proof failed for {ProfileName(flight)}: {proof.Failure}");
        }
        return entry;
    }

    static int StageB(TrackCatalogEntry[] entries, string output, string importOutput)
    {
        var tracks = entries.ToDictionary(e => e.Package.Track.Id);
        var flights = new[] { FlightPresets.Chosen, FlightPresets.FastFlip };
        var ordinary = tracks[TracksMixed.Id].Package;
        var hidden = tracks[TracksHidden.InterceptsId].Package.Track;
        var settings = new[]
        {
            SurfaceRunSettings.Default,
            new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 40, 0, false),
            new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 20, 400, true)
        };
        int matrix = 0;
        foreach (var flight in flights)
        {
            var botSettings = StochasticBotSettings.CreateInitial(ordinary.Track, flight, BotDifficulty.Master, 0);
            foreach (var surface in SurfaceSettings())
            {
                SurfaceMatrixCase(ordinary.Track, flight, botSettings, surface);
                matrix++;
            }
            HiddenForecast(hidden, flight, settings[1]);
        }
        Require(matrix == 600, "Expected 300 common-physics cases per profile");
        Console.WriteLine($"PASS {matrix} common-physics cases and both hidden forecasts");
        var imported = PrepareImport(ordinary, flights);
        if (importOutput != null) File.WriteAllText(importOutput, TrackJson.Write(imported.Package));
        Console.WriteLine("PASS original imported reference and Master admission for both profiles");
        var cases = new List<CaseResult>();
        foreach (var flight in flights)
        {
            foreach (var surface in settings)
            foreach (var track in new[] { ordinary.Track, hidden })
            {
                var run = BotCase(track, flight, BotDifficulty.Master, 0, PartyCamera.Lagger, false, surface);
                cases.Add(run);
                Console.WriteLine($"{run.Name}: {run.Players[1].FinishTime:R}, {run.Players[1].Collisions} hits, {run.BotAccepted} bot inputs");
            }
            var importTrack = imported.Package.Track;
            cases.Add(BotCase(importTrack, flight, BotDifficulty.Master, 0, PartyCamera.Lagger, false, settings[0]));
            var game = RaceSimulation.CreateStochasticBotRace(importTrack, flight, BotDifficulty.Master, 0);
            RaceSimulation.ConfigureSurfaceRun(game, true, settings[0]);
            RaceRunner.Press(game, 0);
            RaceRunner.Advance(game, 3.2);
            var projection = RaceSnapshot.ForBotForecast(game, 1);
            Require(projection.Players[0].SurfaceRunSettings.Equals(settings[0])
                && projection.Players[0].SurfaceRunReserve == game.Players[1].SurfaceRunReserve
                && projection.Players[0].SurfaceRunReserve > 0, "Imported forecast dropped live configured acceleration");
        }
        imported.Package.Track.Obstacles[0].Width += 1;
        Require(flights.All(f => !TrackCatalog.SupportsBot(imported.Package.Track.Id, f)),
            "Changed imported geometry kept an old OFF proof");
        cases.Add(BotCase(hidden, FlightPresets.Chosen, BotDifficulty.Novice, 2944602636, PartyCamera.Lagger, true, settings[0]));
        cases.Add(BotCase(ordinary.Track, FlightPresets.FastFlip, BotDifficulty.Confident, 456, PartyCamera.Lagger, true, settings[1]));
        var report = new { Status = "PASS", CommonPhysicsCases = matrix, HiddenForecasts = 2, PreparedImportProfiles = 2, Cases = cases };
        File.WriteAllText(output, JsonSerializer.Serialize(report, ReportJson));
        Console.WriteLine($"PASS {cases.Count} accelerated live controller / ordinary-replay cases");
        return 0;
    }
}
#endif
