using System.Diagnostics;
using System.Text.Json;
using FlipRace.Core;

static partial class Program
{
    sealed record SlotResult(int Slot, BotDifficulty? Difficulty, uint? Seed, uint? RetrySeed,
        PlayerStatus Status, double? Finish, int Collisions, int Rewards, int Accepted, int Suppressed, int Decisions);

    sealed record LiveResult(string Name, string TrackId, string Profile, PartyCamera Camera, uint BaseSeed,
        SurfaceRunSettings? Surface, int Frames, int SharedTimestamps, int ComparedWorlds,
        bool HumanTerminalBeforeBots, bool BotsTerminalBeforeHuman,
        double P95Milliseconds, double MaximumMilliseconds, int FramesOver16Ms, int FramesOver300Ms, SlotResult[] Slots);

    readonly record struct ReplayEvent(double Time, int Slot, bool Accepted);

    static void NearWorld(GameState live, GameState ordinary, string label)
    {
        using var expected = JsonDocument.Parse(World(ordinary));
        using var actual = JsonDocument.Parse(World(live));
        Compare(actual.RootElement, expected.RootElement, "$");

        void Compare(JsonElement a, JsonElement b, string path)
        {
            if (a.ValueKind != b.ValueKind) throw new InvalidOperationException($"{label} {path}: {a} != {b}");
            switch (a.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in a.EnumerateObject()) Compare(property.Value, b.GetProperty(property.Name), path + "." + property.Name);
                    break;
                case JsonValueKind.Array:
                    if (a.GetArrayLength() != b.GetArrayLength()) throw new InvalidOperationException($"{label} {path}: array length");
                    for (int i = 0; i < a.GetArrayLength(); i++) Compare(a[i], b[i], path + "[" + i + "]");
                    break;
                case JsonValueKind.Number:
                    if (Math.Abs(a.GetDouble() - b.GetDouble()) > 2e-6)
                        throw new InvalidOperationException($"{label} {path}: {a} != {b}");
                    break;
                default:
                    if (a.GetRawText() != b.GetRawText()) throw new InvalidOperationException($"{label} {path}: {a} != {b}");
                    break;
            }
        }
        checks++;
    }

    static void Snapshot(GameState live, int target)
    {
        byte[] before = World(live);
        var snapshot = RaceSnapshot.Clone(live);
        var raw = RaceSnapshot.Clone(live);
        for (int slot = 0; slot < live.Players.Length; slot++)
            Need(RaceRunner.GetPartyBotController(snapshot, slot) == null, "Snapshot registered a live controller");
        RaceRunner.Advance(snapshot, 1.4);
        RaceSimulation.Advance(raw, 1.4);
        Same(snapshot, raw, "Snapshot acquired automatic bot inputs");
        var forecast = RaceSnapshot.ForBotForecast(live, target);
        Need(forecast.Players.Length == live.Players.Length && forecast.Players[0].Id == live.Players[target].Id
            && forecast.Players.Select(player => player.Id).Order().SequenceEqual(live.Players.Select(player => player.Id)),
            "Hidden forecast lost other bodies or target identity");
        Need(forecast.PartyCamera == PartyCamera.Lagger && forecast.SurfaceRunEnabled && forecast.SurfaceRunSettings.Equals(live.SurfaceRunSettings),
            "Hidden forecast lost camera policy or acceleration settings");
        foreach (var player in live.Players)
        {
            var copy = forecast.Players.Single(projected => projected.Id == player.Id);
            Need(!ReferenceEquals(copy, player) && JsonSerializer.Serialize(copy, Json) == JsonSerializer.Serialize(player, Json),
                "Forecast lost a current participant's physical state");
        }
        Need(JsonSerializer.Serialize(forecast.HiddenObstacles, Json) == JsonSerializer.Serialize(live.HiddenObstacles, Json)
            && live.HiddenObstacles.All(item => !ReferenceEquals(item.Value, forecast.HiddenObstacles[item.Key])),
            "Hidden world was omitted or shared by the forecast");
        RaceRunner.Advance(forecast, .5);
        Need(before.AsSpan().SequenceEqual(World(live)), "Forecast or cloned world mutated the original");
    }

    static LiveResult Live(string name, Track track, FlightSettings flight, BotDifficulty?[] slots,
        PartyCamera camera, SurfaceRunSettings? surface, bool terminalHumanWitness = false)
    {
        const uint baseSeed = 0x17f08c21;
        var live = RaceSimulation.CreateParty(track, flight, slots, baseSeed, camera,
            cameraStartLeft: terminalHumanWitness ? -14 : -288.8);
        if (surface.HasValue) RaceSimulation.ConfigureSurfaceRun(live, true, surface.Value);
        if (HiddenObstacles.HasHidden(track)) HiddenObstacles.ConfigureWarning(live, .6);
        for (int slot = 0; slot < slots.Length; slot++)
            RaceSimulation.SetCharacter(live, slot, (slot + 1) % RaceConfig.CharacterCount);
        Ready(live, slots);
        RaceRunner.Advance(live, 3);
        var ordinary = RaceSnapshot.Clone(live);
        var controllers = slots.Select((difficulty, slot) => RaceRunner.GetPartyBotController(live, slot)).ToArray();
        int target = Array.FindLastIndex(controllers, controller => controller != null);
        Need(target >= 0, name + ": no registered controller");
        Throws<ArgumentException>(() => RaceRunner.Advance(live, .1, controllers[target]), "Live registered party accepted a second explicit controller");
        Throws<ArgumentException>(() => RaceRunner.AdvanceScheduled(live, .1, controllers), "Live registered party accepted a second controller array");
        Same(live, ordinary, "An explicit duplicate source changed the live race");

        var tape = new List<ReplayEvent>();
        var accepted = new int[slots.Length];
        var suppressed = new int[slots.Length];
        var milliseconds = new List<double>();
        double lastObserved = live.Elapsed;
        int sharedTimestamps = 0, comparedWorlds = 0;
        bool humanTerminalBeforeBots = false, botsTerminalBeforeHuman = false, hiddenChecked = false;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            var controller = controllers[slot];
            Need((controller != null) == slots[slot].HasValue, "Controller registered at wrong slot");
            if (controller == null) continue;
            Need(controller.Settings.SameAs(live.GetPartyBotSettings(slot)), "Controller does not use its slot's settings");
            int index = slot;
            controller.Observer = slice =>
            {
                if (slice.Kind == "advance")
                {
                    // Group observers report each active bot's span, truncated at
                    // its terminal time. The world clock gives the shared boundary.
                    if (live.Elapsed == lastObserved) return;
                    lastObserved = live.Elapsed;
                    tape.Add(new ReplayEvent(lastObserved, -1, false));
                    sharedTimestamps++;
                    return;
                }
                tape.Add(new ReplayEvent(live.Elapsed, index, slice.Accepted));
                if (slice.Accepted) accepted[index]++;
                else suppressed[index]++;
            };
        }

        for (int frame = 0; live.Phase == "racing" && frame < 6000; frame++)
        {
            if (!terminalHumanWitness && frame % 71 == 4)
            {
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    if (live.IsBotSlot(slot))
                        Need(!RaceRunner.Press(live, slot), "Bot slot accepted human race input");
                    else
                        Need(RaceRunner.Press(live, slot) == RaceSimulation.Press(ordinary, slot), "Human input replay legality diverged");
                }
            }

            if (frame == 9)
            {
                var cursors = controllers.Select(controller => controller?.Cursor).ToArray();
                var decisions = controllers.Select(controller => controller?.Decisions.Count).ToArray();
                byte[] before = World(live);
                RaceRunner.Advance(live, 3, paused: true);
                Need(before.AsSpan().SequenceEqual(World(live))
                    && cursors.SequenceEqual(controllers.Select(controller => controller?.Cursor))
                    && decisions.SequenceEqual(controllers.Select(controller => controller?.Decisions.Count)),
                    "Pause mutated the shared world, decisions or a bot cursor");
            }

            double previous = live.Elapsed;
            double frameDelta = frame % 5 == 0 ? 1d / 30 : 1d / 60;
            long started = Stopwatch.GetTimestamp();
            RaceRunner.Advance(live, frameDelta);
            milliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            double elapsed = live.Elapsed - previous;
            Need(live.Phase == "racing" ? Math.Abs(elapsed - frameDelta) < 1e-10 : elapsed >= 0 && elapsed <= frameDelta + 1e-10,
                $"{name}: live clock advanced {elapsed:R} in frame of {frameDelta:R}");
            foreach (var entry in tape)
            {
                if (entry.Time > ordinary.Elapsed) RaceSimulation.Advance(ordinary, entry.Time - ordinary.Elapsed);
                if (entry.Slot < 0) continue;
                bool ordinaryAccepted = RaceSimulation.Press(ordinary, entry.Slot);
                Need(ordinaryAccepted == entry.Accepted, $"{name}: ordinary bot press differed at {entry.Time:R}, slot {entry.Slot}");
            }
            tape.Clear();
            if (live.Elapsed > ordinary.Elapsed) RaceSimulation.Advance(ordinary, live.Elapsed - ordinary.Elapsed);
            if (frame % 60 == 0 || live.Phase == "finished")
            {
                NearWorld(live, ordinary, $"{name} frame {frame}");
                comparedWorlds++;
            }

            if (terminalHumanWitness && live.Players[0].Status != PlayerStatus.Running
                && live.Players.Skip(1).Any(player => player.Status == PlayerStatus.Running))
            {
                Need(live.Phase == "racing", "Last human terminal stopped running bots");
                humanTerminalBeforeBots = true;
            }
            if (live.HumanCount > 0 && Enumerable.Range(0, slots.Length).All(slot =>
                !slots[slot].HasValue || live.Players[slot].Status != PlayerStatus.Running)
                && Enumerable.Range(0, slots.Length).Any(slot => !slots[slot].HasValue && live.Players[slot].Status == PlayerStatus.Running))
            {
                Need(live.Phase == "racing", "Last bot terminal stopped a running human");
                botsTerminalBeforeHuman = true;
            }
            if (!hiddenChecked && live.HiddenObstacles.Values.Any(item => item.Phase != HiddenObstaclePhase.Hidden))
            {
                Snapshot(live, target);
                hiddenChecked = true;
            }
        }

        Need(live.Phase == "finished" && live.Players.All(player => player.Status != PlayerStatus.Running), name + ": shared finish did not await every slot");
        Need(!terminalHumanWitness || humanTerminalBeforeBots, "Fixture did not witness bots continuing after human elimination");
        Need(!HiddenObstacles.HasHidden(track) || hiddenChecked, "Fixture did not reach a real shared reveal");
        for (int slot = 0; slot < slots.Length; slot++)
        {
            var controller = controllers[slot];
            if (controller == null) continue;
            Need(accepted[slot] > 0 && controller.Decisions.Count > 0 && controller.ForecastHorizonViolations == 0,
                "Bot did not drive its own slot or breached forecast bounds");
            foreach (var decision in controller.Decisions)
            {
                double expected = decision.Kind == BotOpportunityKind.Obstacle
                    ? controller.Settings.HitProbability : controller.Settings.CollectProbability;
                Need(decision.Probability == expected, "Bot used a different slot's stochastic difficulty");
            }
        }
        if (AllBot(slots))
        {
            bool divergentRolls = controllers.SelectMany(controller => controller.Decisions)
                .GroupBy(decision => decision.Id).Any(group => group.Select(decision => decision.Roll).Distinct().Count() == slots.Length);
            Need(divergentRolls, "Four bots shared an RNG stream");
        }

        var results = live.Players.Select((player, slot) => new SlotResult(slot, slots[slot], live.GetPartyBotSettings(slot)?.Seed,
            null, player.Status, player.FinishTime, player.Collisions, player.BoostsEarned,
            accepted[slot], suppressed[slot], controllers[slot]?.Decisions.Count ?? 0)).ToArray();
        byte[] finished = World(live);
        RaceRunner.Advance(live, 4);
        Need(finished.AsSpan().SequenceEqual(World(live)), "Finished party automatically restarted");
        Ready(live, slots);
        Roles(live, slots);
        var previousSeeds = results.Where(result => result.Seed.HasValue).Select(result => result.Seed.Value).ToHashSet();
        foreach (var result in results)
        {
            int slot = result.Slot;
            var replacement = RaceRunner.GetPartyBotController(live, slot);
            if (result.Seed.HasValue)
                Need(!ReferenceEquals(controllers[slot], replacement) && replacement.Cursor == 0 && replacement.Decisions.Count == 0
                    && !previousSeeds.Contains(replacement.Settings.Seed), "Retry reused a bot's old random stream/controller");
            Need(live.Players[slot].CharacterIndex == (slot + 1) % RaceConfig.CharacterCount
                && live.Players[slot].SurfaceRunEnabled == surface.HasValue && live.Players[slot].SurfaceRunReserve == 0,
                "Retry lost character or acceleration mode, or carried reserve");
            results[slot] = result with { RetrySeed = replacement?.Settings.Seed };
        }
        var ordered = milliseconds.Order().ToArray();
        var measured = new LiveResult(name, track.Id, flight == FlightPresets.Chosen ? "Chosen" : "FastFlip", camera, baseSeed,
            surface, ordered.Length, sharedTimestamps, comparedWorlds, humanTerminalBeforeBots, botsTerminalBeforeHuman,
            ordered[(int)Math.Ceiling(ordered.Length * .95) - 1], ordered[^1], ordered.Count(ms => ms > 16), ordered.Count(ms => ms > 300), results);
        Console.WriteLine($"{name}: {measured.Frames} frames, p95 {measured.P95Milliseconds:F2} ms, max {measured.MaximumMilliseconds:F2} ms");
        return measured;
    }

    static void Imported(TrackPackage original)
    {
        Need(TrackJson.TryRead(TrackJson.Write(original), out var copy, out var error), error);
        copy.Track.Id = "v57-regression-import";
        copy.Track.Name = "V57 regression import";
        copy.Track.Length += 220;
        string hash = TrackFingerprint.Compute(copy.Track, copy.ProfilePolicy, copy.WarningMode);
        var references = original.References.Select(reference => new TrackBotReference(reference.ProfileId, hash,
            reference.Inputs, reference.RewardIds, reference.BonusGain)).ToArray();
        var local = new TrackPackage(copy.Track, copy.ProfilePolicy, copy.WarningMode, references);
        Need(TrackCatalog.TryImportLocal(TrackJson.Write(local), out var entry, out error), error);
        var track = entry.Package.Track;
        var slots = new BotDifficulty?[] { BotDifficulty.Novice, null, BotDifficulty.Master };
        var flight = FlightPresets.Chosen;
        Throws<NotSupportedException>(() => RaceSimulation.CreateParty(track, flight, slots, 7), "Unproven import admitted party bots");
        var proof = entry.GetOrCreateProof(flight);
        while (!proof.Complete) proof.Step(32);
        Need(proof.Success, "Imported reference proof failed: " + proof.Failure);
        var live = RaceSimulation.CreateParty(track, flight, slots, 7);
        RaceSimulation.ConfigureSurfaceRun(live, true);
        Ready(live, slots);
        RaceRunner.Advance(live, 3.4);
        Need(live.Players.All(player => player.SurfaceRunEnabled && player.SurfaceRunReserve > 0),
            "Prepared import did not use selected acceleration in every slot");
        track.Obstacles[0].Width += 1;
        Throws<NotSupportedException>(() => RaceSimulation.CreateParty(track, flight, slots, 7), "Stale imported geometry admitted party bots");
    }

    static LiveResult[] RuntimeCases(TrackPackage mixed, TrackPackage hidden)
    {
        Imported(mixed);
        var results = new[]
        {
            Live("mixed two, last human eliminated", mixed.Track, FlightPresets.Chosen,
                new BotDifficulty?[] { null, BotDifficulty.Master }, PartyCamera.Constant, null, terminalHumanWitness: true),
            Live("mixed three, acceleration", mixed.Track, FlightPresets.FastFlip,
                new BotDifficulty?[] { BotDifficulty.Strong, null, BotDifficulty.Amateur }, PartyCamera.Lagger, SurfaceRunSettings.Default),
            Live("hidden four bots, acceleration", hidden.Track, FlightPresets.Chosen,
                new BotDifficulty?[] { BotDifficulty.Novice, BotDifficulty.Amateur, BotDifficulty.Confident, BotDifficulty.Strong },
                PartyCamera.Lagger, new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 40, 0, false))
        };
        Need(results[1].BotsTerminalBeforeHuman, "Fixture did not witness the remaining human after all bots finished");
        return results;
    }
}
