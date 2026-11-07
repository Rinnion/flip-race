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

    static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }

    static bool Press(GameState game, int slot)
    {
#if BASELINE
        return RaceSimulation.Press(game, slot);
#else
        return RaceRunner.Press(game, slot);
#endif
    }

    static void Advance(GameState game, double delta, StochasticBotController bot, bool paused = false)
    {
#if BASELINE
        if (bot != null) bot.Advance(delta, paused);
        else if (!paused) RaceSimulation.Advance(game, delta);
#else
        RaceRunner.Advance(game, delta, bot, paused);
#endif
    }

    static TrackCatalogEntry[] Install(string tracksPath)
    {
        var files = Directory.GetFiles(tracksPath, "*.track.json")
            .ToDictionary(Path.GetFileName, File.ReadAllText);
        string manifest = File.ReadAllText(Path.Combine(tracksPath, "index.json"));
        Require(TrackCatalog.TryInstallBuiltIns(manifest, files, out string error), error);
        return TrackCatalog.Entries;
    }

    static string ProfileName(FlightSettings flight) => flight == FlightPresets.Chosen ? "Chosen" : "FastFlip";

    sealed record Input(string Source, int Slot, int Frame, double Time, double Distance, bool Accepted, int Cursor);
    sealed record CaseResult(string Name, string StateTraceSha256, string DecisionTraceSha256, int Advances,
        int HumanAccepted, int HumanRejected, int BotAccepted, int BotSuppressed, int Pauses, Input[] Inputs,
        string Phase, double Elapsed, object Winner, double? WinnerAt, PlayerResult[] Players);
    sealed record PlayerResult(PlayerStatus Status, double Distance, double? FinishTime, int Collisions,
        int BoostsEarned, int SlowsEarned, int FinishBoostsEarned, double SurfaceRunReserve);

    // The immutable geometry/profile graphs are shared. The serialized world
    // includes player ledgers, status, hidden timers, reserve and camera state.
    static byte[] World(GameState game) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        game.Mode, game.Motion, game.Phase, game.Elapsed, game.CountdownRemaining, game.Winner, game.WinnerAt,
        game.PartyCamera, game.PartyCameraLeft, game.PartyLastCameraLeft, game.SurfaceRunEnabled, game.SurfaceRunSettings,
        game.HiddenWarningDuration, game.HiddenObstacles, game.Players
    }, Json);

    sealed class Trace : IDisposable
    {
        readonly string name;
        readonly GameState live, replay;
        readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        readonly List<Input> inputs = new();
        int frame, advances, pauses;

        public Trace(string name, GameState live, GameState replay)
        {
            this.name = name;
            this.live = live;
            this.replay = replay;
            Check("initial");
        }

        void Append<T>(T value)
        {
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(value, Json));
            hash.AppendData("\n"u8);
        }

        void Check(string eventName)
        {
            byte[] actual = World(live);
            byte[] expected = World(replay);
            if (!actual.AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException($"{name} {eventName} frame {frame} at {live.Elapsed:R}: ordinary replay diverged");
            hash.AppendData(actual);
            hash.AppendData("\n"u8);
        }

        void LogInput(string source, int slot, double time, double distance, bool accepted, int cursor)
        {
            var edge = new Input(source, slot, frame, time, distance, accepted, cursor);
            inputs.Add(edge);
            Append(edge);
            Check(source);
        }

        public void Human(int slot)
        {
            var player = live.Players[slot];
            double time = live.Elapsed;
            bool actual = Press(live, slot);
            bool expected = RaceSimulation.Press(replay, slot);
            Require(actual == expected, $"{name}: human acceptance changed for slot {slot} at frame {frame}");
            LogInput("human", slot, time, player.Distance, actual, -1);
        }

        public void Bot(BotSlice slice)
        {
            if (slice.Kind == "advance")
            {
                RaceSimulation.Advance(replay, slice.Delta);
                advances++;
                Append(slice);
                Check(slice.Kind);
                return;
            }
            Require(slice.Kind == "press" || slice.Kind == "suppressed", $"{name}: unknown event {slice.Kind}");
            bool legal = RaceSimulation.CanPress(replay, 1);
            Require(legal == (slice.Kind == "press"), $"{name}: bot legality changed at {slice.Time:R}");
            bool expected = legal && RaceSimulation.Press(replay, 1);
            Require(slice.Accepted == expected, $"{name}: bot acceptance changed at {slice.Time:R}");
            LogInput(slice.Kind, 1, slice.Time, slice.Distance, slice.Accepted, slice.Cursor);
        }

        public void Frame(double delta, StochasticBotController bot)
        {
            frame++;
            if (bot == null)
            {
                RaceSimulation.Advance(replay, delta);
                advances++;
            }
            Advance(live, delta, bot);
            Append(new { frame, delta });
            Check("frame");
        }

        public void Pause(StochasticBotController bot)
        {
            pauses++;
            int cursor = bot?.Cursor ?? -1;
            int decisions = bot?.Decisions.Count ?? 0;
            Advance(live, 5, bot, paused: true);
            Require(cursor == (bot?.Cursor ?? -1) && decisions == (bot?.Decisions.Count ?? 0),
                $"{name}: pause mutated controller progress");
            Append(new { frame, paused = true, cursor, decisions });
            Check("pause");
        }

        public CaseResult Finish(StochasticBotController bot)
        {
            object decisionState = bot == null ? null : new
            {
                bot.Decisions, bot.Cursor, bot.SuppressedInputs, bot.LastPlanningFailure, bot.ForecastObservations,
                bot.ForecastHorizonViolations, bot.MaximumRequestedHorizon, bot.MaximumTrialHorizon, bot.MaximumTrialOvershoot,
                bot.ForecastCheckpoints, bot.ForecastTrialRollbacks,
                Samples = bot.PlanningSamples.Select(s => new
                {
                    s.Number, s.AtDistance, s.HorizonEndDistance, s.Candidates, s.ForecastSteps, s.Rollbacks
                })
            };
            string decisionHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(decisionState, Json)));
            var players = live.Players.Select(p => new PlayerResult(p.Status, p.Distance, p.FinishTime,
                p.Collisions, p.BoostsEarned, p.SlowsEarned, p.FinishBoostsEarned, p.SurfaceRunReserve)).ToArray();
            return new CaseResult(name, Convert.ToHexString(hash.GetHashAndReset()), decisionHash, advances,
                inputs.Count(i => i.Source == "human" && i.Accepted), inputs.Count(i => i.Source == "human" && !i.Accepted),
                inputs.Count(i => i.Source == "press" && i.Accepted), inputs.Count(i => i.Source == "suppressed"),
                pauses, inputs.ToArray(), live.Phase, live.Elapsed, live.Winner, live.WinnerAt, players);
        }

        public void Dispose() => hash.Dispose();
    }

    static void ScriptHuman(Trace trace, int index, int players)
    {
        for (int slot = 0; slot < players; slot++)
        {
            if (index != 1 + slot && (index + 17 * slot) % 71 != 0) continue;
            trace.Human(slot);
            trace.Human(slot);
            trace.Human(slot);
        }
    }

    static CaseResult BotCase(Track track, FlightSettings flight, BotDifficulty difficulty, uint seed,
        PartyCamera camera, bool scriptedHuman, SurfaceRunSettings? surface = null)
    {
        string name = $"{track.Id}/{ProfileName(flight)}/{difficulty}/{seed}/{camera}/{(scriptedHuman ? "human" : "idle")}";
        var live = RaceSimulation.CreateStochasticBotRace(track, flight, difficulty, seed, camera);
        var replay = RaceSimulation.CreateStochasticBotRace(track, flight, difficulty, seed, camera);
        if (surface.HasValue)
        {
            name += $"/{surface.Value.RatePercent}-{surface.Value.Curve}-{surface.Value.CapPercent}-{surface.Value.AirDecayPercent}-{surface.Value.ResetOnCollision}";
            RaceSimulation.ConfigureSurfaceRun(live, true, surface.Value);
            RaceSimulation.ConfigureSurfaceRun(replay, true, surface.Value);
        }
        using var trace = new Trace(name, live, replay);
        var bot = new StochasticBotController(live) { Observer = trace.Bot };
        trace.Human(0);
        trace.Human(0);
        trace.Frame(2.99, bot);
        trace.Frame(.02, bot);
        for (int i = 1; live.Phase == "racing" && live.Elapsed < 100; i++)
        {
            if (scriptedHuman) ScriptHuman(trace, i, 1);
            if (i == 29) trace.Pause(bot);
            trace.Frame(scriptedHuman && i % 3 == 0 ? 1d / 30 : 1d / 60, bot);
        }
        var result = trace.Finish(bot);
        Require(live.Phase == "finished" && result.BotAccepted > 0 && result.Pauses == 1, name + ": incomplete bot race");
        if (scriptedHuman)
            Require(result.HumanAccepted > 1 && result.HumanRejected > 1,
                $"{name}: incomplete human inputs ({result.HumanAccepted} accepted, {result.HumanRejected} rejected)");
        if (surface.HasValue)
        {
            Require(live.Players.All(p => p.SurfaceRunEnabled && p.SurfaceRunSettings.Equals(surface.Value)),
                name + ": selected settings changed");
            double overrun = StochasticBotController.MaxForecastSlice
                * (RaceConfig.FinishBoostSpeed + RaceConfig.Speed * surface.Value.CapPercent / 100d);
            double limit = StochasticBotController.MaxHorizonDistance;
            const double tolerance = 2e-7;
            Require(bot.ForecastHorizonViolations == 0 && bot.ForecastObservations == bot.ForecastSteps
                && bot.MaximumRequestedHorizon <= limit + tolerance
                && bot.MaximumTrialHorizon <= limit + overrun + tolerance
                && bot.MaximumTrialOvershoot <= overrun + tolerance, name + ": forecast exceeded bounded horizon");
            Require(live.Players[1].Status == PlayerStatus.Finished, name + ": accelerated bot did not finish");
            foreach (var decision in bot.Decisions)
            {
                bool obstacle = decision.Kind == BotOpportunityKind.Obstacle;
                double probability = obstacle ? bot.Settings.HitProbability : bot.Settings.CollectProbability;
                string intent = obstacle ? (decision.Roll < probability ? "hit" : "avoid")
                    : (decision.Roll < probability ? "collect" : "skip");
                Require(decision.Probability == probability && decision.Intent == intent,
                    name + ": acceleration changed stochastic intention rule");
            }
        }
        return result;
    }

    static CaseResult HumanCase(string name, Func<GameState> create, bool surfaceRun)
    {
        var live = create();
        var replay = create();
        if (surfaceRun)
        {
            var settings = new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 40, 0, false);
            RaceSimulation.ConfigureSurfaceRun(live, true, settings);
            RaceSimulation.ConfigureSurfaceRun(replay, true, settings);
        }
        using var trace = new Trace(name, live, replay);
        int players = live.Players.Length;
        for (int slot = players - 1; slot >= 0; slot--) trace.Human(slot);
        trace.Human(0);
        trace.Frame(2.99, null);
        trace.Frame(.02, null);
        for (int i = 1; live.Phase == "racing" && live.Elapsed < 100; i++)
        {
            ScriptHuman(trace, i, players);
            if (i == 29) trace.Pause(null);
            trace.Frame(i % 3 == 0 ? 1d / 30 : 1d / 60, null);
        }
        Require(live.Phase == "finished", name + ": did not reach terminal state");
        var result = trace.Finish(null);
        Require(result.HumanAccepted > players && result.HumanRejected > 1 && result.Pauses == 1,
            $"{name}: incomplete human race ({result.HumanAccepted} accepted, {result.HumanRejected} rejected, {result.Pauses} pauses)");
        return result;
    }

    static IEnumerable<CaseResult> StageA(TrackCatalogEntry[] entries)
    {
        var flights = new[] { FlightPresets.Chosen, FlightPresets.FastFlip };
        var masters = entries.SelectMany(e => flights
            .Where(f => TrackCatalog.SupportsBot(e.Package.Track.Id, f))
            .Select(f => (Track: e.Package.Track, Flight: f))).ToArray();
        Require(masters.Length == 15, "Expected all 15 shipped track/profile combinations");
        foreach (var (track, flight) in masters)
            yield return BotCase(track, flight, BotDifficulty.Master, 0, PartyCamera.Constant, false);

        var tracks = entries.ToDictionary(e => e.Package.Track.Id, e => e.Package.Track);
        yield return BotCase(tracks[TracksHidden.InterceptsId], FlightPresets.Chosen, BotDifficulty.Novice,
            2944602636, PartyCamera.Constant, true);
        yield return BotCase(tracks[TracksHidden.ManifestationId], FlightPresets.FastFlip, BotDifficulty.Confident,
            456, PartyCamera.Lagger, true);
        yield return BotCase(tracks[TracksRethink.Id], FlightPresets.Chosen, BotDifficulty.Strong,
            123, PartyCamera.Lagger, true);
        yield return BotCase(tracks[TracksFull.FlowId], FlightPresets.FastFlip, BotDifficulty.Amateur,
            123, PartyCamera.Constant, true);
        yield return HumanCase("solo/Intercepts/Chosen/Lagger/OFF", () => RaceSimulation.CreateSolo(
            tracks[TracksHidden.InterceptsId], FlightPresets.Chosen, PartyCamera.Lagger, -288.8, 1520), false);
        yield return HumanCase("solo/Manifestation/FastFlip/Lagger/ON", () => RaceSimulation.CreateSolo(
            tracks[TracksHidden.ManifestationId], FlightPresets.FastFlip, PartyCamera.Lagger, -288.8, 1520), true);
        yield return HumanCase("party4/Mixed/Chosen/Lagger/ON", () => RaceSimulation.CreateParty(
            tracks[TracksMixed.Id], FlightPresets.Chosen, 4, PartyCamera.Lagger, -288.8, 1520), true);
        yield return HumanCase("legacy/split", () => RaceSimulation.CreateGame("legacy", "legacy"), false);
    }

    static int Main(string[] args)
    {
        bool surfaceMode = args.Length >= 3 && args[2] == "--surface";
        if (args.Length < 2 || args.Length > (surfaceMode ? 4 : 3))
        {
            Console.Error.WriteLine("Usage: UnifiedRaceRegression <track-directory> <report.json> [baseline.json | --surface [import.json]]");
            return 2;
        }
        try
        {
            var entries = Install(args[0]);
#if !BASELINE
            if (surfaceMode)
                return StageB(entries, args[1], args.Length == 4 ? args[3] : null);
#endif
            using var baseline = args.Length == 3 ? JsonDocument.Parse(File.ReadAllText(args[2])) : null;
            var expected = baseline?.RootElement.GetProperty("Cases").EnumerateArray()
                .ToDictionary(c => c.GetProperty("Name").GetString());
            var cases = new List<CaseResult>();
            foreach (var result in StageA(entries))
            {
                if (expected != null)
                {
                    Require(expected.Remove(result.Name, out JsonElement old), $"{result.Name}: missing baseline");
                    Require(old.GetProperty("StateTraceSha256").GetString() == result.StateTraceSha256,
                        $"{result.Name}: state/slice/input trace differs from baseline");
                    Require(old.GetProperty("DecisionTraceSha256").GetString() == result.DecisionTraceSha256,
                        $"{result.Name}: planning trace differs from baseline");
                }
                cases.Add(result);
                Console.WriteLine($"{result.Name} {result.Advances} steps {result.BotAccepted} bot inputs {result.HumanAccepted} human inputs");
            }
            Require(expected == null || expected.Count == 0, "Cases missing from the comparison");
            var report = new { Status = "PASS", ComparedWithBaseline = baseline != null, Cases = cases };
            File.WriteAllText(args[1], JsonSerializer.Serialize(report, ReportJson));
            Console.WriteLine($"PASS {cases.Count} exact live-world / ordinary-replay cases");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
