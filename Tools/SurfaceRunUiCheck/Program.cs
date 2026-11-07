using System;
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
    static int checks, cases;
    static object Call(object ui, string method, params object[] args) => ui.GetType().GetMethod(method, Private).Invoke(ui, args);
    static object Field(object ui, string name) => ui.GetType().GetField(name, Private).GetValue(ui);
    static void Set(object ui, string name, object value) => ui.GetType().GetField(name, Private).SetValue(ui, value);
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static void Click(FlipRaceGame ui, Rect rect) => Call(ui, "FlightPointer", rect.center);
    static void Apply(FlipRaceGame ui) => Click(ui, (Rect)Field(ui, "flightApply"));
    static FlipRaceGame NewUi(int preset, int w, int h, float dpr)
    {
        Prefs.Clear(); Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey, preset); Devices.Clear();
        Clock.frameCount = 1; Clock.realtimeSinceStartupAsDouble = 1;
        Display.width = (int)(w * dpr); Display.height = (int)(h * dpr); Display.PixelRatio = dpr;
        Display.SafeAreaOverride = new Rect(0, 0, w * dpr, h * dpr);
        var ui = (FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        Set(ui, "lanes", new Rect[2]); Call(ui, "InitializeFlightMenu");
        var selection = (FlightSelection)Field(ui, "flightSelection");
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui, RaceSimulation.CreateTrial(RaceTracks.GetAt(0), selection.Settings));
        Call(ui, "ConfigureSessionSurfaceRun"); Set(ui, "selectedTrackId", ui.Game.Track.Id); Call(ui, "Measure"); return ui;
    }
    static void Open(FlipRaceGame ui) { Call(ui, "OpenTracks"); Check(ui.FlightMenuPage == 0, "real root menu opened"); }
    static void OpenSurfaceRun(FlipRaceGame ui)
    {
        Click(ui, ui.SurfaceRunMenuRect);
        Check(ui.FlightMenuPage == 11, "surface run page opens from the root menu");
    }
    static bool Matches(SurfaceRunSettings settings, int rate, SurfaceRunCurve curve, int cap) =>
        settings.RatePercent == rate && settings.Curve == curve && settings.CapPercent == cap;
    static bool Includes(Rect outer, Rect inner) =>
        inner.xMin >= outer.xMin - .01 && inner.xMax <= outer.xMax + .01 && inner.yMin >= outer.yMin - .01 && inner.yMax <= outer.yMax + .01;
    static void CheckSurfaceRunTargets(FlipRaceGame ui, Rect panel)
    {
        Rect[] targets = Enumerable.Range(1, 5).Select(ui.SurfaceRunRateRect)
            .Concat(new[] { ui.SurfaceRunCurveRect(SurfaceRunCurve.Linear), ui.SurfaceRunCurveRect(SurfaceRunCurve.Exponential) })
            .Concat(new[] { ui.SurfaceRunCapRect(20), ui.SurfaceRunCapRect(30), ui.SurfaceRunCapRect(40) })
            .Concat(new[] { ui.SurfaceRunToggleRect, (Rect)Field(ui, "flightApply"), (Rect)Field(ui, "flightBack") }).ToArray();
        for (int i = 0; i < targets.Length; i++)
        {
            Check(targets[i].width >= 44 && targets[i].height >= 44 && Includes(panel, targets[i]), "each 44px target is inside panel");
            for (int j = 0; j < i; j++) Check(!targets[i].Overlaps(targets[j]), "settings targets don't overlap");
        }
    }
    static void ChooseSurfaceRun(FlipRaceGame ui, int rate, SurfaceRunCurve curve, int cap)
    {
        Click(ui, ui.SurfaceRunRateRect(rate));
        Click(ui, ui.SurfaceRunCurveRect(curve));
        Click(ui, ui.SurfaceRunCapRect(cap));
        Check(Matches(ui.PendingSurfaceRunSettings, rate, curve, cap), "rate, curve and cap can be selected independently");
    }
    static void HumanMode(FlipRaceGame ui, int mode)
    {
        Open(ui); Click(ui, ui.ViewMenuButtonRect);
        if (mode == 0) Click(ui, ui.SoloMenuButtonRect);
        else if (mode == 1) Click(ui, ui.PartyMenuButtonRect);
        else { Call(ui, "ModeChoicePointer", ((Rect)ui.GetType().GetProperty("ModeLegacyRect", Private).GetValue(ui)).center); Click(ui, ui.ViewOptionRect(0, mode - 2)); }
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
    static void Run(int preset, int w, int h, float dpr)
    {
        cases++; var ui = NewUi(preset, w, h, dpr);
        Check(ui.SurfaceRunChoice && ui.Game.SurfaceRunEnabled && ui.Game.Players.All(p => p.SurfaceRunEnabled), "human default ON");
        Check(Matches(ui.Game.SurfaceRunSettings, 5, SurfaceRunCurve.Linear, 30), "human default is 5/linear/30");
        Open(ui); Rect entry = ui.SurfaceRunMenuRect, panel = (Rect)Field(ui, "flightPanel"), apply = (Rect)Field(ui, "flightApply");
        Check(entry.height == 44 && Includes(panel, entry), "44px root entry inside panel");
        Check(!entry.Overlaps(apply) && !entry.Overlaps((Rect)Field(ui, "flightTracks")) && panel.yMax <= h, "entry doesn't overlap track/apply or clip viewport");
        OpenSurfaceRun(ui); CheckSurfaceRunTargets(ui, panel);
        foreach (var curve in new[] { SurfaceRunCurve.Linear, SurfaceRunCurve.Exponential })
            for (int rate = 1; rate <= 5; rate++) for (int cap = 20; cap <= 40; cap += 10) ChooseSurfaceRun(ui, rate, curve, cap);
        ChooseSurfaceRun(ui, 2, SurfaceRunCurve.Exponential, 40);
        Click(ui, ui.SurfaceRunToggleRect);
        Check(!ui.PendingSurfaceRun && ui.SurfaceRunChoice && ui.Game.SurfaceRunEnabled && Matches(ui.Game.SurfaceRunSettings, 5, SurfaceRunCurve.Linear, 30), "pending OFF and settings edits don't mutate game");
        Click(ui, (Rect)Field(ui, "flightBack"));
        Check(ui.FlightMenuPage == 0 && Matches(ui.PendingSurfaceRunSettings, 2, SurfaceRunCurve.Exponential, 40), "back keeps pending edits in menu");
        Call(ui, "CloseRules"); Open(ui);
        Check(ui.PendingSurfaceRun && ui.Game.SurfaceRunEnabled && Matches(ui.PendingSurfaceRunSettings, 5, SurfaceRunCurve.Linear, 30), "cancel discards pending OFF and setting edits");
        OpenSurfaceRun(ui); ChooseSurfaceRun(ui, 2, SurfaceRunCurve.Exponential, 40); Click(ui, ui.SurfaceRunToggleRect); Apply(ui);
        Check(!ui.SurfaceRunChoice && !ui.Game.SurfaceRunEnabled, "apply OFF from surface run page");
        for (int mode = 0; mode < 5; mode++)
        {
            HumanMode(ui, mode);
            Check(!ui.Game.SurfaceRunEnabled && !ui.SurfaceRunChoice && Matches(ui.Game.SurfaceRunSettings, 2, SurfaceRunCurve.Exponential, 40), "OFF and saved settings survive human mode change");
        }
        Open(ui); OpenSurfaceRun(ui);
        Check(Matches(ui.PendingSurfaceRunSettings, 2, SurfaceRunCurve.Exponential, 40), "settings remain available when OFF");
        Click(ui, ui.SurfaceRunToggleRect); Apply(ui); Check(ui.Game.SurfaceRunEnabled, "apply ON");
        for (int mode = 0; mode < 5; mode++)
        {
            HumanMode(ui, mode);
            Check(ui.Game.SurfaceRunEnabled && ui.Game.Players.All(p => p.SurfaceRunEnabled && Matches(p.SurfaceRunSettings, 2, SurfaceRunCurve.Exponential, 40)), "ON and active settings copied to all human modes");
        }
        Open(ui); Click(ui, ui.ViewMenuButtonRect); Click(ui, ui.BotMenuButtonRect); Apply(ui);
        Check(ui.BotEnabled && !ui.Game.SurfaceRunEnabled && ui.SurfaceRunChoice && ui.Game.Players.All(p => !p.SurfaceRunEnabled), "BOT forced OFF retains human ON");
        Open(ui); Click(ui, ui.SurfaceRunMenuRect);
        Check(ui.FlightMenuPage == 0 && ui.PendingSurfaceRun && Matches(ui.PendingSurfaceRunSettings, 2, SurfaceRunCurve.Exponential, 40), "BOT cannot edit remembered human choice");
        Call(ui, "CloseRules");
        Call(ui, "ResetBotRace"); Check(!ui.Game.SurfaceRunEnabled && ui.SurfaceRunChoice, "BOT retry stays OFF");
        HumanMode(ui, 0);
        Check(ui.SoloEnabled && ui.Game.SurfaceRunEnabled && Matches(ui.Game.SurfaceRunSettings, 2, SurfaceRunCurve.Exponential, 40), "return from BOT restores ON and settings");
        RaceSimulation.Press(ui.Game, 0); RaceSimulation.Advance(ui.Game, 3.1); RaceSimulation.Advance(ui.Game, .4);
        Check(ui.Game.Players[0].SurfaceRunReserve > 0, "actual running gains reserve");
        var active = ui.Game; Call(ui, "OpenTracks"); Check(ui.FlightMenuPage == -1 && ReferenceEquals(active, ui.Game), "cannot open menu mid-race");
        Call(ui, "ApplyFlightChoice"); Check(ReferenceEquals(active, ui.Game) && ui.Game.SurfaceRunEnabled, "active attempt settings locked");
        ui.Game.Phase = "finished"; RaceSimulation.Press(ui.Game, 0);
        Check(ui.Game.SurfaceRunEnabled && Matches(ui.Game.SurfaceRunSettings, 2, SurfaceRunCurve.Exponential, 40)
            && ui.Game.Players.All(p => p.SurfaceRunReserve == 0 && Matches(p.SurfaceRunSettings, 2, SurfaceRunCurve.Exponential, 40)), "retry retains ON and settings, resets reserve");
    }
    static int Main()
    {
        try
        {
            foreach (int preset in new[] { FlightSettingsStore.ChosenPresetIndex, FlightSettingsStore.FastFlipPresetIndex })
                foreach (var size in new[] { (568,320), (800,360), (1280,800) }) foreach (float dpr in new[] { 1f, 2.5f }) Run(preset, size.Item1, size.Item2, dpr);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", checks, cases, scope = "Production menu/apply/cancel/mode/retry methods with native environment aliases. Retry fixture forces finished phase. No rendering or physical touchscreen claim." })); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
