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
    static void HumanMode(FlipRaceGame ui, int mode)
    {
        Open(ui); Click(ui, ui.ViewMenuButtonRect);
        if (mode == 0) Click(ui, ui.SoloMenuButtonRect);
        else if (mode == 1) Click(ui, ui.PartyMenuButtonRect);
        else { Call(ui, "ModeChoicePointer", ((Rect)ui.GetType().GetProperty("ModeLegacyRect", Private).GetValue(ui)).center); Click(ui, ui.ViewOptionRect(0, mode - 2)); }
        Apply(ui);
    }
    static void Run(int preset, int w, int h, float dpr)
    {
        cases++; var ui = NewUi(preset, w, h, dpr);
        Check(ui.SurfaceRunChoice && ui.Game.SurfaceRunEnabled && ui.Game.Players.All(p => p.SurfaceRunEnabled), "human default ON");
        Open(ui); Rect toggle = ui.SurfaceRunToggleRect, panel = (Rect)Field(ui, "flightPanel"), apply = (Rect)Field(ui, "flightApply");
        Check(toggle.height == 44 && toggle.xMin >= panel.xMin && toggle.xMax <= panel.xMax && toggle.yMax <= panel.yMax && toggle.yMin >= panel.yMin, "44px toggle inside panel");
        Check(!toggle.Overlaps(apply) && !toggle.Overlaps((Rect)Field(ui, "flightTracks")) && panel.yMax <= h, "toggle doesn't overlap track/apply or clip viewport");
        Click(ui, toggle); Check(!ui.PendingSurfaceRun && ui.SurfaceRunChoice && ui.Game.SurfaceRunEnabled, "pending OFF doesn't mutate game");
        Call(ui, "CloseRules"); Open(ui); Check(ui.PendingSurfaceRun && ui.Game.SurfaceRunEnabled, "cancel discards pending OFF");
        Click(ui, toggle); Apply(ui); Check(!ui.SurfaceRunChoice && !ui.Game.SurfaceRunEnabled, "apply OFF");
        for (int mode = 0; mode < 5; mode++) { HumanMode(ui, mode); Check(!ui.Game.SurfaceRunEnabled && !ui.SurfaceRunChoice, "OFF survives human mode change"); }
        Open(ui); Click(ui, ui.SurfaceRunToggleRect); Apply(ui); Check(ui.Game.SurfaceRunEnabled, "apply ON");
        for (int mode = 0; mode < 5; mode++) { HumanMode(ui, mode); Check(ui.Game.SurfaceRunEnabled && ui.Game.Players.All(p => p.SurfaceRunEnabled), "ON all human modes"); }
        Open(ui); Click(ui, ui.ViewMenuButtonRect); Click(ui, ui.BotMenuButtonRect); Apply(ui);
        Check(ui.BotEnabled && !ui.Game.SurfaceRunEnabled && ui.SurfaceRunChoice && ui.Game.Players.All(p => !p.SurfaceRunEnabled), "BOT forced OFF retains human ON");
        Open(ui); Click(ui, ui.SurfaceRunToggleRect); Check(ui.PendingSurfaceRun, "BOT cannot alter remembered human choice"); Call(ui, "CloseRules");
        Call(ui, "ResetBotRace"); Check(!ui.Game.SurfaceRunEnabled && ui.SurfaceRunChoice, "BOT retry stays OFF");
        HumanMode(ui, 0); Check(ui.SoloEnabled && ui.Game.SurfaceRunEnabled, "return from BOT restores ON");
        RaceSimulation.Press(ui.Game, 0); RaceSimulation.Advance(ui.Game, 3.1); RaceSimulation.Advance(ui.Game, .4);
        Check(ui.Game.Players[0].SurfaceRunReserve > 0, "actual running gains reserve");
        var active = ui.Game; Call(ui, "OpenTracks"); Check(ui.FlightMenuPage == -1 && ReferenceEquals(active, ui.Game), "cannot open menu mid-race");
        Call(ui, "ApplyFlightChoice"); Check(ReferenceEquals(active, ui.Game) && ui.Game.SurfaceRunEnabled, "active attempt settings locked");
        ui.Game.Phase = "finished"; RaceSimulation.Press(ui.Game, 0);
        Check(ui.Game.SurfaceRunEnabled && ui.Game.Players.All(p => p.SurfaceRunReserve == 0), "retry retains ON resets reserve");
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
