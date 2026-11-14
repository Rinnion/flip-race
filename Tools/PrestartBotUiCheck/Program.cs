using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;
using FlipRace.SharedScreenCheck.Interop;
using UnityEngine;
using Display = FlipRace.SharedScreenCheck.Interop.Display;

static partial class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string LocalTrack = "prestart-ui-no-reference", LongTrack = "prestart-ui-too-long";
    static int checks, updates, pointers;
    static string context;
    static void Check(bool ok, string why)
    {
        checks++;
        if (!ok) throw new InvalidOperationException(context + " — " + why);
    }
    static object Call(object ui, string name, params object[] args) => ui.GetType().GetMethod(name, Private).Invoke(ui, args);
    static FieldInfo Field(object ui, string name) => ui.GetType().GetField(name, Private);
    static void Set(object ui, string name, object value) => Field(ui, name).SetValue(ui, value);
    static T Read<T>(object ui, string name) => (T)Field(ui, name).GetValue(ui);

    static void Tick(FlipRaceGame ui, double dt = .016)
    {
        Clock.frameCount++;
        Clock.realtimeSinceStartupAsDouble += dt;
        Call(ui, "Update");
        updates++;
    }

    static void Click(FlipRaceGame ui, Rect target)
    {
        Devices.Clear();
        Tick(ui);
        Check(target.width > 0 && target.height > 0, "click target has area");
        Devices.MouseDown = Devices.MouseHeld = true;
        Devices.mousePosition = new Vector3(target.center.x * Display.PixelRatio,
            Display.height - target.center.y * Display.PixelRatio, 0);
        Tick(ui);
        Devices.Clear();
        pointers++;
    }

    static void Key(FlipRaceGame ui, KeyCode key)
    {
        Devices.Clear();
        Tick(ui);
        Devices.Keys.Add(key);
        Tick(ui);
        Devices.Clear();
    }

    static FlipRaceGame NewUi(string track, uint sessionSeed, int w = 568, int h = 320)
    {
        Display.PixelRatio = 2.5f;
        Display.width = (int)(w * Display.PixelRatio);
        Display.height = (int)(h * Display.PixelRatio);
        Display.SafeAreaOverride = new Rect(20, 15, Display.width - 54, Display.height - 48);
        Clock.frameCount = 1;
        Clock.realtimeSinceStartupAsDouble = 1;
        Devices.Clear();
        var ui = (FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        Set(ui, "lanes", new Rect[2]);
        Set(ui, "lastFrame", Clock.realtimeSinceStartupAsDouble);
        Set(ui, "botSessionSeed", sessionSeed);
        Set(ui, "botSeedInitialized", true);
        Call(ui, "InitializeFlightMenu");
        Call(ui, "ResetPrestartPreparation");
        var selection = Read<FlightSelection>(ui, "flightSelection");
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui, RaceSimulation.CreateTrial(RaceTracks.Get(track), selection.Settings));
        Set(ui, "selectedTrackId", track);
        Tick(ui);
        return ui;
    }

    static void OpenModes(FlipRaceGame ui)
    {
        Click(ui, ui.TrackMenuButtonRect);
        Check(ui.FlightMenuPage == 0, "open track settings through pointer");
        Click(ui, ui.ViewMenuButtonRect);
        Check(ui.FlightMenuPage == 5, "open play modes through pointer");
    }

    static void StandaloneMenu(FlipRaceGame ui, bool preliminary)
    {
        OpenModes(ui);
        Click(ui, ui.BotMenuButtonRect);
        Check(ui.FlightMenuPage == 8 && !ui.PendingPrestartBots, "standalone opens with previous bot by default");
        Check(!ui.BotAlgorithmRect(true).Overlaps(ui.BotDifficultyRect(4))
            && !ui.BotAlgorithmRect(true).Overlaps(ui.PartyCameraRect(1))
            && !ui.BotAlgorithmRect(true).Overlaps(ui.FlightApplyRect), "standalone selector is disjoint");
        if (preliminary) Click(ui, ui.BotAlgorithmRect(true));
        Check(ui.PendingPrestartBots == preliminary, "algorithm selected through the rendered target");
        Click(ui, ui.BotDifficultyRect(4));
    }

    static void Party(FlipRaceGame ui, params int[] botSlots)
    {
        OpenModes(ui);
        Click(ui, ui.PartyMenuButtonRect);
        Click(ui, ui.PartyCountRect(2));
        Click(ui, ui.PartyCameraRect(1));
        Click(ui, ui.FlightApplyRect);
        Check(ui.FlightMenuPage == 12, "party opens existing four-seat roster");
        foreach (int slot in botSlots) Click(ui, ui.PartyRosterRoleRect(slot));
        Check(!ui.BotAlgorithmRect(true).Overlaps(ui.PartyRosterDifficultyRect(3))
            && !ui.BotAlgorithmRect(true).Overlaps(ui.FlightApplyRect), "company selector is disjoint");
        Click(ui, ui.BotAlgorithmRect(true));
        Click(ui, ui.FlightApplyRect);
        Check(ui.PartyEnabled && ui.Game.UsesPrestartBots && ui.Game.HumanCount == 4 - botSlots.Length
            && ui.Game.PartyCamera == PartyCamera.Lagger, "company preserves party mode, roles and chosen camera");
        foreach (int slot in botSlots)
            Check(ui.Game.GetBotDifficulty(slot) == BotDifficulty.Amateur && ui.ReadyScreenRect(slot).width == 0
                && ui.PawScreenRect(slot).width == 0, "bots retain role difficulty and no manual actions");
    }

    static void Until(FlipRaceGame ui, Func<bool> done, int frames = 550, double dt = .016)
    {
        for (int i = 0; i < frames && !done(); i++) Tick(ui, dt);
        Check(done(), "expected completion, observed " + ui.Game.Phase + " / " + ui.PrestartPreparationState
            + " / " + ui.PrestartPreparationFailure);
    }

    static object ColdWarmAndMalformed(string builtIn)
    {
        context = "standalone cold, cache reload, malformed cache";
        Prefs.Clear();
        var previous = NewUi(builtIn, 10);
        StandaloneMenu(previous, false);
        Click(previous, previous.FlightApplyRect);
        Check(previous.BotEnabled && previous.Game.StochasticBot != null && !previous.Game.UsesPrestartBots, "old bot remains the default");

        var cold = NewUi(LocalTrack, 11);
        StandaloneMenu(cold, false);
        var starting = cold.Game;
        Click(cold, cold.FlightApplyRect);
        Check(ReferenceEquals(cold.Game, starting), "old bot rejects imported geometry without an authored reference");
        Click(cold, cold.BotAlgorithmRect(true));
        Click(cold, cold.FlightApplyRect);
        Check(cold.BotEnabled && cold.Game.UsesPrestartBots && cold.Game.BotDifficulty == BotDifficulty.Master
            && cold.Game.Track.Id == LocalTrack && cold.Game.Phase == "ready", "pre-start bot accepts the same local map");
        Key(cold, KeyCode.A);
        Until(cold, () => cold.Game.Phase == "countdown");
        Check(!cold.PrestartUsedCache && cold.Game.CountdownRemaining == RaceConfig.CountdownDuration, "cold plan gates complete three-second countdown");
        string stored = Prefs.GetString(PrestartBotCache.StorageKey);
        Check(stored.StartsWith("1\n", StringComparison.Ordinal), "bounded versioned route written to local preferences");
        var cacheKey = cold.Game.PrestartConfig.CacheKey;

        var warm = NewUi(LocalTrack, 12, 1280, 720);
        StandaloneMenu(warm, true);
        Click(warm, warm.FlightApplyRect);
        Until(warm, () => warm.PrestartPreparationState == "ready");
        Check(warm.PrestartUsedCache && warm.Game.PrestartReady && warm.Game.Phase == "ready", "new UI instance revalidates persisted routes without automatically starting");
        Check(warm.PrestartAttemptSeed != cold.PrestartAttemptSeed, "persisted bundle uses a new attempt seed");

        string malformed = Convert.ToBase64String(Encoding.UTF8.GetBytes("{bad route"));
        Prefs.SetString(PrestartBotCache.StorageKey, "1\n" + cacheKey + "\t" + malformed + "\n");
        var repaired = NewUi(LocalTrack, 13);
        StandaloneMenu(repaired, true);
        Click(repaired, repaired.FlightApplyRect);
        Until(repaired, () => repaired.PrestartPreparationState == "ready");
        Check(!repaired.PrestartUsedCache && repaired.Game.PrestartReady
            && !Prefs.GetString(PrestartBotCache.StorageKey).Contains(malformed), "malformed candidate is replaced by a fresh verified route");
        return new { cold = cold.PrestartPreparationSeconds, warm = warm.PrestartPreparationSeconds,
            malformedRecovered = repaired.PrestartPreparationSeconds };
    }

    static object FourAndRetry()
    {
        context = "four bots, hero change, no auto start, retry";
        var ui = NewUi(LocalTrack, 14);
        Party(ui, 0, 1, 2, 3);
        uint seed = ui.PrestartAttemptSeed;
        Click(ui, ui.CharacterScreenRect(2, 4));
        Check(ui.Game.Players[2].CharacterIndex == 4 && ui.PrestartAttemptSeed == seed, "bot hero change retains same route job");
        Key(ui, KeyCode.A);
        Until(ui, () => ui.PrestartPreparationState == "ready");
        Check(ui.Game.Phase == "ready" && !ui.PrestartUsedCache, "four bots wait for explicit start on verified cold lineup");
        double cold = ui.PrestartPreparationSeconds;
        Click(ui, ui.PartyAllBotActionRect);
        Check(ui.Game.Phase == "countdown", "start uses verified four-bot plan");
        Until(ui, () => ui.Game.Phase == "finished", 700, .1);
        Check(ui.Game.Players.All(player => player.Status == PlayerStatus.Finished), "all four bots physically finish");
        double[] finishes = ui.Game.Players.Select(p => p.FinishTime.Value).ToArray();
        for (int i = 0; i < 20; i++) Tick(ui);
        Check(ui.Game.Phase == "finished" && ui.PrestartAttemptSeed == seed, "result does not auto-retry");
        Click(ui, ui.PartyAllBotActionRect);
        Until(ui, () => ui.Game.Phase == "countdown");
        Check(ui.PrestartUsedCache && ui.PrestartAttemptSeed != seed && ui.Game.Players[2].CharacterIndex == 4,
            "explicit retry revalidates a compatible bundle with a fresh seed and preserves heroes");
        return new { cold, warm = ui.PrestartPreparationSeconds, finishes };
    }

    static void MixedHeroChange(int changed)
    {
        context = "mixed company queued readiness, human " + changed;
        Prefs.Clear();
        var ui = NewUi(LocalTrack, (uint)(18 + changed));
        Party(ui, 2, 3);
        // Two Ready and a hero selection in one Update exercise the pending
        // request before the next incremental preparation step can run.
        Rect a = ui.ReadyScreenRect(0), b = ui.ReadyScreenRect(1), hero = ui.CharacterScreenRect(changed, 4);
        Tick(ui);
        Check(ui.PrestartPreparationState == "searching", "fixture is still preparing");
        Devices.Touches = new[] { TouchAt(1, a), TouchAt(2, b), TouchAt(3, hero) };
        uint seed = ui.PrestartAttemptSeed;
        Tick(ui);
        Devices.Clear();
        Check(ui.Game.Phase == "ready" && !ui.Game.Players[changed].Ready && ui.Game.Players[1 - changed].Ready
            && ui.Game.Players[changed].CharacterIndex == 4 && ui.PrestartAttemptSeed == seed,
            "hero selection clears that human's queued readiness and keeps job and other human ready");
        Until(ui, () => ui.PrestartPreparationState == "ready");
        Check(ui.Game.Phase == "ready" && ui.PrestartAttemptSeed == seed, "verified route waits for changed human's second Ready");
        Click(ui, ui.ReadyScreenRect(changed));
        Check(ui.Game.Phase == "countdown", "second confirmation starts mixed race");
    }

    static Touch TouchAt(int id, Rect target) => new Touch { fingerId = id, phase = TouchPhase.Began,
        position = new Vector2(target.center.x * Display.PixelRatio, Display.height - target.center.y * Display.PixelRatio) };

    static void TimeoutAndSettings()
    {
        context = "bounded failure, retry and cancel on settings change";
        var ui = NewUi(LongTrack, 25);
        StandaloneMenu(ui, true);
        Click(ui, ui.FlightApplyRect);
        Key(ui, KeyCode.A);
        Check(ui.Game.Phase == "ready" && ui.PrestartPreparationState == "searching", "explicit start waits on long calculation");
        uint firstSeed = ui.PrestartAttemptSeed;
        Tick(ui, 10.01);
        Check(ui.Game.Phase == "ready" && ui.PrestartPreparationState == "failed" && !ui.Game.Players[0].Ready
            && ui.PrestartPreparationFailure.Contains("истекло"), "ten-second deadline clears intent with a recoverable failure");
        Key(ui, KeyCode.A);
        Check(ui.PrestartAttemptSeed != firstSeed && ui.PrestartPreparationState == "searching", "explicit retry requests a fresh seed");
        var previous = ui.Game;
        var oldJob = Read<PrestartBotPreparation>(ui, "prestartJob");
        Click(ui, ui.TrackMenuButtonRect);
        Check(!previous.Players[0].Ready && ui.FlightMenuPage == 0, "entering settings clears queued start");
        Click(ui, Read<Rect>(ui, "flightFastFlip"));
        Click(ui, ui.FlightApplyRect);
        Check(!ReferenceEquals(previous, ui.Game) && ui.Game.Flight == FlightPresets.FastFlip && oldJob.Complete
            && !oldJob.Success && ui.Game.PrestartConfig.CacheKey != previous.PrestartConfig.CacheKey,
            "applying changed physics cancels old work and creates a new compatibility key");
        Until(ui, () => ui.PrestartPreparationState == "failed", 1200);
        Check(ui.Game.UsesPrestartBots && ui.Game.Phase == "ready" && !ui.Game.PrestartReady, "search exhaustion stays in experimental mode and cannot start");
    }

    static int Main(string[] args)
    {
        string folder = Path.Combine(args[0], "Assets", "StreamingAssets", "Tracks");
        string manifest = File.ReadAllText(Path.Combine(folder, "index.json"));
        Check(TrackJson.TryReadManifest(manifest, out var parsed, out var error), error);
        var files = parsed.Files.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(folder, name)));
        Check(TrackCatalog.TryInstallBuiltIns(manifest, files, out error), error);
        string builtIn = RaceTracks.GetAt(0).Id;
        foreach (var fixture in new[] { (LocalTrack, 7400d), (LongTrack, 60000d) })
        {
            var obstacle = new Obstacle { Id = "floor-stone", Kind = "floor", X = 800, Width = 90, Height = 55 };
            var track = new Track(fixture.Item2, new[] { obstacle }) { Id = fixture.Item1, Name = fixture.Item1 };
            string json = TrackJson.Write(new TrackPackage(track, TrackProfilePolicy.Any, TrackWarningMode.Selected));
            Check(TrackCatalog.TryImportLocal(json, out var entry, out error) && entry.Package.References.Length == 0, error);
        }
        if (args.Skip(1).Contains("--character-roster")) return CharacterRosterSmoke(builtIn);
        var standalone = ColdWarmAndMalformed(builtIn);
        var party = FourAndRetry();
        MixedHeroChange(0);
        MixedHeroChange(1);
        TimeoutAndSettings();
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", checks, updates, pointers, standalone, party,
            timing = "Preparation seconds measured by the managed simulated-frame clock plus final work slice; native browser timings are separate." }));
        return 0;
    }
}
