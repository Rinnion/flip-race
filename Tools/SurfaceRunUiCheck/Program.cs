using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;
using FlipRace.SharedScreenCheck.Interop;
using UnityEngine;
using Display = FlipRace.SharedScreenCheck.Interop.Display;
static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly int[] AirDecayChoices = { 0, 50, 100, 200, 400 };
    static readonly bool[] ResetChoices = { false, true };
    static readonly SurfaceRunCurve[] Curves = { SurfaceRunCurve.Linear, SurfaceRunCurve.Exponential };
    static readonly SurfaceRunSettings ExpectedDefault = new(5, SurfaceRunCurve.Linear, 30, 200, true);
    static readonly SurfaceRunSettings Edited = new(2, SurfaceRunCurve.Exponential, 40, 50, false);
    static int checks, cases, botSettingApplies;
    static object Call(object ui, string method, params object[] args) => ui.GetType().GetMethod(method, Private).Invoke(ui, args);
    static object Field(object ui, string name) => ui.GetType().GetField(name, Private).GetValue(ui);
    static void Set(object ui, string name, object value) => ui.GetType().GetField(name, Private).SetValue(ui, value);
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static void Click(FlipRaceGame ui, Rect rect) => Call(ui, "FlightPointer", rect.center);
    static void ClickMeasured(FlipRaceGame ui, string field)
    {
        Call(ui, "MeasureFlightMenu");
        Click(ui, (Rect)Field(ui, field));
    }
    static void Apply(FlipRaceGame ui)
    {
        ClickMeasured(ui, "flightApply");
        if (ui.FlightMenuPage == 12) ClickMeasured(ui, "flightApply");
    }
    static void Back(FlipRaceGame ui) => ClickMeasured(ui, "flightBack");
    static FlipRaceGame NewUi(int preset, int w, int h, float dpr)
    {
        Prefs.Clear(); Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey, preset); Devices.Clear();
        Clock.frameCount = 1; Clock.realtimeSinceStartupAsDouble = 1;
        Display.width = (int)(w * dpr); Display.height = (int)(h * dpr); Display.PixelRatio = dpr;
        Display.SafeAreaOverride = new Rect(0, 0, w * dpr, h * dpr);
        var ui = (FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        Set(ui, "botSessionSeed", 123u); Set(ui, "botSeedInitialized", true);
        Set(ui, "lanes", new Rect[2]); Call(ui, "InitializeFlightMenu");
        var selection = (FlightSelection)Field(ui, "flightSelection");
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui, RaceSimulation.CreateTrial(RaceTracks.GetAt(0), selection.Settings));
        Call(ui, "ConfigureSessionSurfaceRun"); Set(ui, "selectedTrackId", ui.Game.Track.Id); Call(ui, "Measure"); return ui;
    }
    static void Open(FlipRaceGame ui) { Call(ui, "OpenTracks"); Check(ui.FlightMenuPage == 0, "real root menu opened"); }
    static void Tick(FlipRaceGame ui, double dt = 0)
    {
        Clock.frameCount++;
        Clock.realtimeSinceStartupAsDouble += dt;
        Call(ui, "Update");
    }
    static void OpenSurfaceRun(FlipRaceGame ui)
    {
        Click(ui, ui.SurfaceRunMenuRect);
        Check(ui.FlightMenuPage == 11, "surface run page opens from the root menu");
    }
    static bool Matches(SurfaceRunSettings actual, SurfaceRunSettings expected) =>
        actual.RatePercent == expected.RatePercent && actual.Curve == expected.Curve && actual.CapPercent == expected.CapPercent
            && actual.AirDecayPercent == expected.AirDecayPercent && actual.ResetOnCollision == expected.ResetOnCollision;
    static bool Includes(Rect outer, Rect inner) =>
        inner.xMin >= outer.xMin - .01 && inner.xMax <= outer.xMax + .01 && inner.yMin >= outer.yMin - .01 && inner.yMax <= outer.yMax + .01;
    static void CheckSurfaceRunTargets(FlipRaceGame ui, Rect panel)
    {
        Rect[] targets = Enumerable.Range(1, 5).Select(ui.SurfaceRunRateRect)
            .Concat(Curves.Select(ui.SurfaceRunCurveRect))
            .Concat(new[] { ui.SurfaceRunCapRect(20), ui.SurfaceRunCapRect(30), ui.SurfaceRunCapRect(40) })
            .Concat(AirDecayChoices.Select(ui.SurfaceRunAirDecayRect))
            .Concat(ResetChoices.Select(ui.SurfaceRunResetRect))
            .Concat(new[] { ui.SurfaceRunToggleRect, (Rect)Field(ui, "flightApply"), (Rect)Field(ui, "flightBack") }).ToArray();
        Rect[] labels = Enumerable.Range(0, 5).Select(row => (Rect)Call(ui, "SurfaceRunLabelRect", row))
            .Append((Rect)Field(ui, "surfaceRunTitle")).ToArray();
        Check(labels.All(label => Includes(panel, label)), "all titles and row captions are inside panel");
        for (int i = 0; i < targets.Length; i++)
        {
            Check(targets[i].width >= 44 && targets[i].height >= 44 && Includes(panel, targets[i]), "each 44px target is inside panel");
            Check(!labels.Any(label => label.Overlaps(targets[i])), "settings targets don't overlap any title or row caption");
            for (int j = 0; j < i; j++) Check(!targets[i].Overlaps(targets[j]), "settings targets don't overlap");
        }
    }
    static void ChooseSurfaceRun(FlipRaceGame ui, SurfaceRunSettings target)
    {
        var start = ui.PendingSurfaceRunSettings;
        bool enabled = ui.PendingSurfaceRun;
        Click(ui, ui.SurfaceRunRateRect(target.RatePercent));
        Check(Matches(ui.PendingSurfaceRunSettings, new(target.RatePercent, start.Curve, start.CapPercent, start.AirDecayPercent, start.ResetOnCollision)), "rate click changes only rate");
        Click(ui, ui.SurfaceRunCurveRect(target.Curve));
        Check(Matches(ui.PendingSurfaceRunSettings, new(target.RatePercent, target.Curve, start.CapPercent, start.AirDecayPercent, start.ResetOnCollision)), "curve click changes only curve");
        Click(ui, ui.SurfaceRunCapRect(target.CapPercent));
        Check(Matches(ui.PendingSurfaceRunSettings, new(target.RatePercent, target.Curve, target.CapPercent, start.AirDecayPercent, start.ResetOnCollision)), "cap click changes only cap");
        Click(ui, ui.SurfaceRunAirDecayRect(target.AirDecayPercent));
        Check(Matches(ui.PendingSurfaceRunSettings, new(target.RatePercent, target.Curve, target.CapPercent, target.AirDecayPercent, start.ResetOnCollision)), "air click changes only air decay");
        Click(ui, ui.SurfaceRunResetRect(target.ResetOnCollision));
        Check(Matches(ui.PendingSurfaceRunSettings, target) && ui.PendingSurfaceRun == enabled, "reset click changes only collision reset");
    }
    static IEnumerable<SurfaceRunSettings> AllSettings()
    {
        foreach (int decay in AirDecayChoices) foreach (bool reset in ResetChoices) foreach (var curve in Curves)
            for (int rate = 1; rate <= 5; rate++) for (int cap = 20; cap <= 40; cap += 10)
                yield return new SurfaceRunSettings(rate, curve, cap, decay, reset);
    }
    static void CheckSurfaceRunHelp(FlipRaceGame ui)
    {
        string[] expectedAir = { "В воздухе запас сохраняется.", "В воздухе −2,5 п.п./с.", "В воздухе −5 п.п./с.", "В воздухе −10 п.п./с.", "В воздухе −20 п.п./с." };
        for (int i = 0; i < AirDecayChoices.Length; i++) foreach (bool reset in ResetChoices)
        {
            var expected = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30, AirDecayChoices[i], reset);
            Open(ui); OpenSurfaceRun(ui); ChooseSurfaceRun(ui, expected); Apply(ui);
            Check(ui.FlightMenuPage == -1 && ui.Game.SurfaceRunEnabled && Matches(ui.Game.SurfaceRunSettings, expected), "header apply commits every air and reset choice");
            string rule = (string)Call(ui, "SurfaceRunRule");
            string expectedCollision = reset ? "Удар обнуляет запас." : "Запас при ударе сохраняется.";
            Check(rule.Contains(expectedAir[i]) && rule.Contains(expectedCollision), "active rules describe the actual air rate and collision behavior");
        }
        Open(ui); OpenSurfaceRun(ui); ChooseSurfaceRun(ui, ExpectedDefault); Apply(ui);
    }
    static void HumanMode(FlipRaceGame ui, int mode)
    {
        Open(ui); Click(ui, ui.ViewMenuButtonRect);
        if (mode == 0) Click(ui, ui.SoloMenuButtonRect);
        else if (mode == 1) Click(ui, ui.PartyMenuButtonRect);
        else { Click(ui, (Rect)ui.GetType().GetProperty("ModeLegacyRect", Private).GetValue(ui)); Click(ui, ui.ViewOptionRect(0, mode - 2)); }
        Apply(ui);
        bool expectedMode = mode switch
        {
            0 => ui.SoloEnabled,
            1 => ui.PartyEnabled,
            2 => ui.FieldViewName == "split",
            3 => ui.FieldViewName == "split-ghost",
            4 => ui.FieldViewName == "shared",
            _ => false
        };
        Check(expectedMode && !ui.BotEnabled, "actual menu applied requested human mode");
    }
    static void BotMode(FlipRaceGame ui)
    {
        Open(ui); Click(ui, ui.ViewMenuButtonRect); Click(ui, ui.BotMenuButtonRect); Apply(ui);
    }
    static void CheckBotSettings(FlipRaceGame ui, bool enabled, SurfaceRunSettings settings)
    {
        Check(ui.BotEnabled && ui.Game.Players.Length == 2 && ui.SurfaceRunChoice == enabled
            && ui.Game.SurfaceRunEnabled == enabled && Matches(ui.Game.SurfaceRunSettings, settings)
            && ui.Game.Players.All(p => p.SurfaceRunEnabled == enabled && Matches(p.SurfaceRunSettings, settings)),
            "BOT and both players receive the exact chosen settings and ON/OFF state");
    }
    static void BotResultFixture(FlipRaceGame ui)
    {
        ui.Game.Phase = "finished";
        foreach (var p in ui.Game.Players) { p.Status = PlayerStatus.Finished; p.FinishTime = 1; }
        Tick(ui);
    }
    static void RetryBot(FlipRaceGame ui, bool enabled)
    {
        BotResultFixture(ui);
        uint seed = ui.BotAttemptSeed;
        long sequence = ui.BotAttemptSequenceIndex;
        Call(ui, "Pointer", ui.BotRetryRect.center);
        CheckBotSettings(ui, enabled, Edited);
        Check(ui.Game.Phase == "countdown" && ui.BotAttemptSequenceIndex == sequence + 1 && ui.BotAttemptSeed != seed
            && ui.Game.Players.All(p => p.SurfaceRunReserve == 0), "BOT retry starts with a new seed and zero reserves");
    }
    static void CheckBotSurfaceRun(FlipRaceGame ui)
    {
        BotMode(ui); CheckBotSettings(ui, true, Edited);
        Open(ui); OpenSurfaceRun(ui); CheckSurfaceRunTargets(ui, (Rect)Field(ui, "flightPanel"));
        Check(ui.PendingSurfaceRun && Matches(ui.PendingSurfaceRunSettings, Edited), "BOT menu opens with the previously chosen settings");
        ChooseSurfaceRun(ui, ExpectedDefault); Click(ui, ui.SurfaceRunToggleRect);
        Back(ui); Back(ui); CheckBotSettings(ui, true, Edited);

        foreach (var settings in AllSettings())
        {
            Open(ui); OpenSurfaceRun(ui); ChooseSurfaceRun(ui, settings); Apply(ui);
            CheckBotSettings(ui, true, settings);
            botSettingApplies++;
        }
        Open(ui); OpenSurfaceRun(ui); ChooseSurfaceRun(ui, Edited); Apply(ui);
        Tick(ui); Call(ui, "Pointer", ui.BotStartRect.center);
        Check(ui.Game.Phase == "countdown", "BOT start via the actual pointer starts countdown");
        Tick(ui);
        for (int frame = 0; frame < 40 && ui.Game.Phase == "countdown"; frame++) Tick(ui, .1);
        Check(ui.Game.Phase == "racing", "BOT countdown completes through Update");
        var observedSpeedGain = new bool[2];
        // A bot may take flight during this window. Check the gain for each
        // racer at a live frame, independently of their first planned jump.
        for (int frame = 0; frame < 40 && !observedSpeedGain.All(gain => gain); frame++)
        {
            Tick(ui, .05);
            for (int slot = 0; slot < observedSpeedGain.Length; slot++)
            {
                var p = ui.Game.Players[slot];
                observedSpeedGain[slot] |= p.SurfaceRunReserve > 0 && p.CurrentSpeed > RaceConfig.Speed;
            }
        }
        Check(observedSpeedGain.All(gain => gain), "actual Update gives the human and bot reserve and increased speed");
        var active = ui.Game;
        Call(ui, "OpenTracks"); Call(ui, "ApplyFlightChoice");
        Check(ui.FlightMenuPage == -1 && ReferenceEquals(active, ui.Game), "BOT cannot change settings mid-attempt");
        CheckBotSettings(ui, true, Edited);
        RetryBot(ui, true);

        BotResultFixture(ui);
        Open(ui); OpenSurfaceRun(ui); Click(ui, ui.SurfaceRunToggleRect); Apply(ui);
        CheckBotSettings(ui, false, Edited);
        RetryBot(ui, false);
        BotResultFixture(ui);
        HumanMode(ui, 0);
        Check(ui.SoloEnabled && !ui.Game.SurfaceRunEnabled && Matches(ui.Game.SurfaceRunSettings, Edited), "return from BOT keeps OFF and all options");
        Open(ui); OpenSurfaceRun(ui); Click(ui, ui.SurfaceRunToggleRect); Apply(ui);
        Check(ui.Game.SurfaceRunEnabled && Matches(ui.Game.SurfaceRunSettings, Edited), "re-enabling after BOT restores the same settings");
    }
    static void Run(int preset, int w, int h, float dpr)
    {
        cases++; var ui = NewUi(preset, w, h, dpr);
        Check(ui.SurfaceRunChoice && ui.Game.SurfaceRunEnabled && ui.Game.Players.All(p => p.SurfaceRunEnabled), "human default ON");
        Check(Matches(ui.Game.SurfaceRunSettings, ExpectedDefault), "human default is 5/linear/30/200/reset ON");
        BotMode(ui); CheckBotSettings(ui, true, ExpectedDefault); HumanMode(ui, 2);
        CheckSurfaceRunHelp(ui);
        Open(ui); Rect entry = ui.SurfaceRunMenuRect, panel = (Rect)Field(ui, "flightPanel"), apply = (Rect)Field(ui, "flightApply");
        Check(entry.height == 44 && Includes(panel, entry), "44px root entry inside panel");
        Check(!entry.Overlaps(apply) && !entry.Overlaps((Rect)Field(ui, "flightTracks")) && panel.yMax <= h, "entry doesn't overlap track/apply or clip viewport");
        OpenSurfaceRun(ui); CheckSurfaceRunTargets(ui, panel);
        foreach (var settings in AllSettings()) ChooseSurfaceRun(ui, settings);
        ChooseSurfaceRun(ui, Edited);
        Click(ui, ui.SurfaceRunToggleRect);
        Check(!ui.PendingSurfaceRun && Matches(ui.PendingSurfaceRunSettings, Edited), "global OFF does not change any option");
        Check(ui.SurfaceRunChoice && ui.Game.SurfaceRunEnabled && Matches(ui.Game.SurfaceRunSettings, ExpectedDefault), "pending OFF and settings edits don't mutate game");
        Back(ui);
        Check(ui.FlightMenuPage == 0 && !ui.PendingSurfaceRun && Matches(ui.PendingSurfaceRunSettings, Edited), "back keeps pending edits in menu");
        Back(ui); Open(ui);
        Check(ui.PendingSurfaceRun && ui.Game.SurfaceRunEnabled && Matches(ui.PendingSurfaceRunSettings, ExpectedDefault), "cancel discards pending OFF and setting edits");
        OpenSurfaceRun(ui); ChooseSurfaceRun(ui, Edited); Click(ui, ui.SurfaceRunToggleRect); Apply(ui);
        Check(!ui.SurfaceRunChoice && !ui.Game.SurfaceRunEnabled, "apply OFF from surface run page");
        for (int mode = 0; mode < 5; mode++)
        {
            HumanMode(ui, mode);
            Check(!ui.Game.SurfaceRunEnabled && !ui.SurfaceRunChoice && Matches(ui.Game.SurfaceRunSettings, Edited), "OFF and saved settings survive human mode change");
        }
        Open(ui); OpenSurfaceRun(ui);
        Check(Matches(ui.PendingSurfaceRunSettings, Edited), "settings remain available when OFF");
        Click(ui, ui.SurfaceRunToggleRect); Apply(ui); Check(ui.Game.SurfaceRunEnabled, "apply ON");
        for (int mode = 0; mode < 5; mode++)
        {
            HumanMode(ui, mode);
            Check(ui.Game.SurfaceRunEnabled && ui.Game.Players.All(p => p.SurfaceRunEnabled && Matches(p.SurfaceRunSettings, Edited)), "ON and active settings copied to all human modes");
        }
        CheckBotSurfaceRun(ui);
        RaceSimulation.Press(ui.Game, 0); RaceSimulation.Advance(ui.Game, 3.1); RaceSimulation.Advance(ui.Game, .4);
        Check(ui.Game.Players[0].SurfaceRunReserve > 0, "actual running gains reserve");
        var active = ui.Game; Call(ui, "OpenTracks"); Check(ui.FlightMenuPage == -1 && ReferenceEquals(active, ui.Game), "cannot open menu mid-race");
        Call(ui, "ApplyFlightChoice"); Check(ReferenceEquals(active, ui.Game) && ui.Game.SurfaceRunEnabled, "active attempt settings locked");
        ui.Game.Phase = "finished"; RaceSimulation.Press(ui.Game, 0);
        Check(ui.Game.SurfaceRunEnabled && Matches(ui.Game.SurfaceRunSettings, Edited)
            && ui.Game.Players.All(p => p.SurfaceRunReserve == 0 && Matches(p.SurfaceRunSettings, Edited)), "retry retains ON and settings, resets reserve");
    }
    static int Main()
    {
        try
        {
            foreach (int preset in new[] { FlightSettingsStore.ChosenPresetIndex, FlightSettingsStore.FastFlipPresetIndex })
                foreach (var size in new[] { (568,320), (800,360), (960,432), (1280,800) })
                    foreach (float dpr in new[] { 1f, 2.5f, 3f }) Run(preset, size.Item1, size.Item2, dpr);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", checks, cases, botSettingApplies, scope = "Production menu/apply/cancel/mode/retry and Update with native environment aliases. Retry fixtures force finished phase. No rendering or physical touchscreen claim." })); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
