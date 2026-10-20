using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;
using UnityEngine;

static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static int checks;
    static readonly string[] Ids = { TracksFull.LongId, TracksFull.ShortId, TracksFull.FlowId, TracksRhythm.CascadeId };
    static void Check(bool condition, string description) { checks++; if (!condition) throw new Exception(description); }
    static object Call(object game, string name, params object[] args) => typeof(FlipRaceGame).GetMethod(name, Private).Invoke(game, args);
    static object StaticCall(string name, params object[] args) => typeof(FlipRaceGame).GetMethod(name, Static).Invoke(null, args);
    static object Field(object game, string name) => typeof(FlipRaceGame).GetField(name, Private).GetValue(game);
    static void Set(object game, string name, object value) => typeof(FlipRaceGame).GetField(name, Private).SetValue(game, value);
    static object Ui(GameState state)
    {
        var ui = RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui, state);
        Set(ui, "width", 568f); Set(ui, "height", 320f);
        return ui;
    }
    static int Main()
    {
        try { Verify(); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Verify()
    {
        Check(RaceTracks.All.Select(t => t.Id).SequenceEqual(Ids), "exact active four-track catalogue");
        Check(TracksRhythm.All.Length == 1 && TracksFull.All.Length == 3, "one rhythm track plus three originals");
        Check(((Track)StaticCall("InitialFlightTrack")).Id == Ids[0], "real startup selects first original track");
        foreach (int index in new[] { int.MinValue, -1, 4, 5, 6, 999, int.MaxValue })
            Check(RaceTracks.Get(index).Id == TracksRhythm.CascadeId, "obsolete or invalid index resolves to Cascade");
        for (int i = 0; i < Ids.Length; i++)
            Check(ReferenceEquals(RaceTracks.Get(i), RaceTracks.Get(Ids[i])), "current index/id preserve exact selection");
        foreach (string retired in new[] { TracksRhythm.DenseId, TracksRhythm.WavesId })
        {
            Check(!RaceTracks.IsFull(retired) && !TracksRhythm.IsFull(retired), "retired course absent from active membership");
            Check(RaceTracks.Get(retired).Id == TracksRhythm.CascadeId && TracksRhythm.Get(retired).Id == TracksRhythm.CascadeId,
                "both public id lookup paths migrate retired references");
        }
        var oldIds = (string[])typeof(FlipRaceGame).GetField("OldTrackIds", Static).GetValue(null);
        Check(oldIds.Length == 7 && !oldIds.Any(id => id == TracksRhythm.DenseId || id == TracksRhythm.WavesId), "previous sections do not expose retired full courses");
        var layouts = new List<object>();
        foreach (var viewport in new[] { (w: 568f, h: 320f), (w: 844f, h: 390f), (w: 1280f, h: 720f) })
        {
            var state = RaceSimulation.CreateTrial(RaceTracks.Get(0), FlightPresets.Chosen);
            state.Players[0].CharacterIndex = 2; state.Players[1].CharacterIndex = 3;
            var ui = Ui(state); Set(ui, "width", viewport.w); Set(ui, "height", viewport.h);
            Set(ui, "flightPage", 4); Call(ui, "MeasureFlightMenu");
            Rect panel = (Rect)Field(ui, "flightPanel"), apply = (Rect)Field(ui, "flightApply");
            var cards = new List<object>();
            for (int i = 0; i < Ids.Length; i++)
            {
                Rect r = (Rect)Call(ui, "FullTrackRect", i);
                Check(r.height >= 44 && r.xMin >= panel.xMin && r.xMax <= panel.xMax && r.yMin >= 28 && r.yMax < apply.yMin,
                    "visible card has full touch height and clears footer/version");
                cards.Add(new { id = Ids[i], x = r.x, y = r.y, width = r.width, height = r.height });
            }
            layouts.Add(new { viewport, cards });
            foreach (int choice in new[] { 10, 11, -1 })
            {
                var custom = FlightDraft.From(FlightPresets.FastFlip); custom.Repeats = AirRepeatLimit.None;
                var selection = new FlightSelection(custom);
                if (choice < 0) selection.SelectCustom(); else selection.SelectPreset(choice);
                Set(ui, "flightSelection", selection);
                string saved = FlightSettingsStore.Encode(selection.Custom);
                for (int i = 0; i < Ids.Length; i++)
                {
                    Rect r = (Rect)Call(ui, "FullTrackRect", i);
                    foreach (var point in new[] { r.center, new Vector2(r.xMin + .1f, r.yMin + .1f), new Vector2(r.xMax - .1f, r.yMax - .1f) })
                    {
                        Call(ui, "FlightPointer", point);
                        Check((string)Field(ui, "pendingFlightTrack") == Ids[i], "whole production card selects matching id");
                        Check(selection.PresetIndex == choice && FlightSettingsStore.Encode(selection.Custom) == saved, "card retains exact preset and saved custom");
                        Check(ReferenceEquals(((FlipRaceGame)ui).Game, state) && state.Phase == "ready" && !state.Players.Any(p => p.Ready), "card neither starts nor recreates game");
                        Check(state.Players[0].CharacterIndex == 2 && state.Players[1].CharacterIndex == 3, "card retains both heroes");
                    }
                }
                string pending = (string)Field(ui, "pendingFlightTrack");
                foreach (int retiredSlot in new[] { 4, 5 })
                {
                    Rect r = (Rect)Call(ui, "FullTrackRect", retiredSlot);
                    foreach (var point in new[] { r.center, new Vector2(r.xMin + 1, r.yMin + 1), new Vector2(r.xMax - 1, r.yMax - 1) })
                    {
                        Call(ui, "FlightPointer", point);
                        Check((string)Field(ui, "pendingFlightTrack") == pending && selection.PresetIndex == choice && state.Phase == "ready",
                            "removed bottom-card hitbox is inactive");
                    }
                }
            }
        }
        foreach (string id in Ids.Concat(new[] { TracksRhythm.DenseId, TracksRhythm.WavesId }).Concat(oldIds))
        foreach (int choice in new[] { 10, 11, -1 })
        {
            var custom = FlightDraft.From(FlightPresets.FastFlip); custom.Repeats = AirRepeatLimit.None;
            var selection = new FlightSelection(custom);
            if (choice < 0) selection.SelectCustom(); else selection.SelectPreset(choice);
            var settings = selection.Settings;
            var previous = RaceSimulation.CreateTrial(RaceTracks.Get(0), settings);
            previous.Players[0].CharacterIndex = 3; previous.Players[1].CharacterIndex = 3;
            string encoded = FlightSettingsStore.Encode(selection.Custom);
            var next = (GameState)StaticCall("CreateSelectedFlight", previous, id, settings);
            Check(next.Track.Id == RaceTracks.NormalizeId(id), "real apply factory resolves full, previous and retired selections");
            Check(ReferenceEquals(next.Flight, settings) && next.Players.All(p => p.CharacterIndex == 3), "apply preserves exact physical settings and same-character pair");
            Check(next.Phase == "ready" && !next.Players.Any(p => p.Ready), "apply waits for two explicit readiness inputs");
            Check(selection.PresetIndex == choice && FlightSettingsStore.Encode(selection.Custom) == encoded, "apply does not mutate saved custom or active choice");
        }
        foreach (string retired in new[] { TracksRhythm.DenseId, TracksRhythm.WavesId })
        foreach (int choice in new[] { 10, 11, -1 })
        foreach (string phase in new[] { "ready", "finished" })
        for (int first = 0; first < 4; first++) for (int second = 0; second < 4; second++)
        {
            var custom = FlightDraft.From(FlightPresets.FastFlip); custom.Repeats = AirRepeatLimit.None;
            var selection = new FlightSelection(custom);
            if (choice < 0) selection.SelectCustom(); else selection.SelectPreset(choice);
            var settings = selection.Settings;
            var state = RaceSimulation.CreateTrial(TracksRhythm.Draft(retired), settings);
            state.Phase = phase; state.Players[0].CharacterIndex = first; state.Players[1].CharacterIndex = second;
            var ui = Ui(state); Set(ui, "flightSelection", selection); Set(ui, "activeFlightPreset", choice); Set(ui, "activeCustomFlight", choice < 0);
            Call(ui, "BeginFlightMenu");
            Check((string)Field(ui, "pendingFlightTrack") == TracksRhythm.CascadeId && state.Track.Id == retired,
                "opening menu canonicalizes draft without changing current round");
            Call(ui, "Act", 0);
            Check(state.Track.Id == TracksRhythm.CascadeId && state.Phase == phase && state.Players[0].Ready && !state.Players[1].Ready,
                "first real ready/rematch action migrates track but still waits for opponent");
            Call(ui, "Act", 1);
            Check(state.Phase == "countdown" && ReferenceEquals(state.Flight, settings), "second real action starts migrated track with exact settings");
            Check(state.Players[0].CharacterIndex == first && state.Players[1].CharacterIndex == second, "ready/rematch retains every hero pair");
            Check(selection.PresetIndex == choice && selection.Custom.Repeats == AirRepeatLimit.None, "rematch retains selection and independent custom");
            RaceSimulation.Advance(state, RaceConfig.CountdownDuration);
            Check(state.Phase == "racing" && state.Track.Id == TracksRhythm.CascadeId, "actual countdown enters Cascade");
        }
        Directory.CreateDirectory("Verification");
        File.WriteAllText("Verification/post-playtest-461-catalog-layout.json", JsonSerializer.Serialize(new { catalogue = RaceTracks.All.Select(t => new { t.Id, t.Name }), layouts }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        Console.WriteLine("POST-PLAYTEST 4.6.1 CATALOG PASS " + checks + " checks: four active courses, ID/index migration, real menu handler, apply factory, ready/rematch, three viewports, exact controls/custom and all hero pairs.");
    }
}
