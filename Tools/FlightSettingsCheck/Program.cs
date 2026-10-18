using System;
using System.Globalization;
using FlipRace.Core;
using FlipRace.Presentation;

static class Program
{
    static int assertions, roundTrips;
    static void Check(bool value, string why)
    { assertions++; if (!value) throw new Exception(why); }
    static void Same(FlightSettings a, FlightSettings b, string why)
    {
        Check(a.FirstAction == b.FirstAction && a.Law == b.Law && a.AirRepeats == b.AirRepeats,
            why + " semantics");
        Check(a.GravityAcceleration == b.GravityAcceleration && a.FlipImpulse == b.FlipImpulse
            && a.JumpImpulse == b.JumpImpulse && a.LinearSpeed == b.LinearSpeed
            && a.LinearJumpHeight == b.LinearJumpHeight, why + " exact physical values");
    }
    static int Main()
    {
        try
        {
            RoundTrips(); CorruptData(); SelectionIsolation(); ChosenAndActiveChoice(); CopyAndCulture();
            Console.WriteLine("PASS: FLIGHT settings " + assertions + " assertions / " + roundTrips + " physical round trips.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void RoundTrips()
    {
        double[] gravities = { 662.5, 800, 1000, 1250, 1500, 1800, 2200, 2700 };
        double[] flip = { 0, 35, 70, 140, 210, 280, 350, 420 };
        double[] jump = { 200, 250, 300, 350, 400, 450, 500, 550 };
        double[] duration = { 1.2, 1, .9, .8, .7, .6, .5, .4 };
        double[] heights = { 48, 64, 80, 96, 112, 128, 160, 192 };
        foreach (FirstAction first in Enum.GetValues(typeof(FirstAction)))
        foreach (MotionLaw law in Enum.GetValues(typeof(MotionLaw)))
        foreach (AirRepeatLimit repeats in Enum.GetValues(typeof(AirRepeatLimit)))
        for (int level = 0; level < 8; level++)
        {
            var expected = new FlightSettings(first, law, repeats, gravities[level], flip[7-level],
                jump[level], 212 / duration[level], heights[7-level]);
            var draft = FlightDraft.From(expected, level % 4);
            Check(FlightSettingsStore.TryDecode(FlightSettingsStore.Encode(draft), out var restored), "valid physical set decodes");
            Same(expected, restored.Settings(), "all scale positions round trip");
            Check(restored.ResetPreset == level % 4, "reset source survives");
            roundTrips++;
        }
        for (int preset = 0; preset <= FlightSettingsStore.ChosenPresetIndex; preset++)
        {
            var physical = FlightSettingsStore.Preset(preset);
            var d = FlightDraft.From(physical, preset);
            Check(FlightSettingsStore.TryDecode(FlightSettingsStore.Encode(d), out var copy), "legacy/current preset values decode");
            Same(physical, copy.Settings(), "preset exact physical values");
            roundTrips++;
        }
    }
    static void CorruptData()
    {
        string[] invalid = {
            null, "", "{}", "2|0|1|1|1000|70|400|265|112|0",
            "1|55|1|1|1000|70|400|265|112|0", "1|0|55|1|1000|70|400|265|112|0",
            "1|0|1|2|1000|70|400|265|112|0", "1|0|1|1|NaN|70|400|265|112|0",
            "1|0|1|1|Infinity|70|400|265|112|0", "1|0|1|1|0|70|400|265|112|0",
            "1|0|1|1|666.6|70|400|265|112|0", "1|0|1|1|1000|-70|400|265|112|0",
            "1|1|1|1|1000|70|0|265|112|0", "1|0|0|1|1000|70|400|0|112|0",
            "1|0|0|1|1000|70|400|266|112|0", "1|0|0|1|1000|70|400|265|999|0",
            "1|0|1|1|1000|70|400|265|112|11", "1|0|1|1|1000|70|400|265|112|-1",
            "1|0|1|1|1000|70|400|265|112|0|extra", new string('x', 513)
        };
        foreach (string text in invalid)
        {
            Check(!FlightSettingsStore.TryDecode(text, out var fallback), "corrupt data rejected: " + text);
            Same(fallback.Settings(), FlightPresets.Moderate, "whole safe fallback, never partially decoded");
            Check(fallback.ResetPreset == 0, "fallback reset target is valid");
        }
    }
    static void SelectionIsolation()
    {
        var custom = FlightDraft.From(new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.Unlimited,
            1800, 210, 500, 212 / .6, 160), 2);
        string encoded = FlightSettingsStore.Encode(custom);
        var selection = new FlightSelection(custom);
        custom.Flip = 0;
        Check(FlightSettingsStore.Encode(selection.Custom) == encoded, "selection owns its custom copy");
        for (int preset = 0; preset <= FlightSettingsStore.ChosenPresetIndex; preset++)
        {
            selection.SelectPreset(preset);
            Check(!selection.IsCustom && selection.PresetIndex == preset, "preset highlighted independently");
            Check(FlightSettingsStore.Encode(selection.Custom) == encoded, "preset selection never destroys remembered custom");
            Same(selection.Settings, FlightSettingsStore.Preset(preset), "exact selected preset");
        }
        selection.SelectCustom();
        Check(selection.IsCustom && FlightSettingsStore.Encode(selection.Custom) == encoded, "custom returns intact");
        var game = RaceSimulation.CreateTrial(TracksControl.Open(), selection.Settings);
        var runningSettings = game.Flight;
        RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
        RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, .1);
        double position = game.Players[0].Y;
        selection.Edit(d => { d.First = FirstAction.Flip; d.Law = MotionLaw.Gravity; d.Gravity = 7; });
        Check(ReferenceEquals(game.Flight, runningSettings) && game.Players[0].Y == position,
            "editing next set cannot change active flight");
        Check(selection.Custom.Flip == 4 && selection.Custom.Jump == 6,
            "changing first action preserves separate impulse levels");
        selection.Edit(d => { d.Law = MotionLaw.Linear; d.First = FirstAction.Jump; });
        Check(selection.Custom.Gravity == 7 && selection.Custom.Flip == 4 && selection.Custom.Jump == 6,
            "disabled fields retain values across law/action switches");
        selection.SelectPreset(2);
        selection.Edit(d => d.Gravity = 7);
        Check(selection.IsCustom && selection.Custom.ResetPreset == 2, "manual edit leaves preset and remembers its source");
        Check(FlightPresets.Jump.GravityAcceleration == 662.5 && FlightPresets.Jump.JumpImpulse == 400,
            "immutable preset never changed by manual edit");
        selection.ResetCustom();
        Same(selection.Settings, FlightPresets.Jump, "reset reloads exact source values");
    }
    static void ChosenAndActiveChoice()
    {
        Check(FlightSettingsStore.Key == "flip-race-custom-flight-v1", "existing custom storage key retained");
        Check(FlightSettingsStore.ActiveChoiceKey == "flip-race-flight-choice-v2", "new-version active choice has separate key");
        Check(FlightSettingsStore.ChosenPresetIndex == 10, "chosen appends without shifting old indexes");
        Same(FlightSettingsStore.Preset(10), new FlightSettings(FirstAction.Jump, MotionLaw.Gravity,
            AirRepeatLimit.Unlimited, 662.5, 0, 350, 265, 112), "chosen exact values including inactive flip");
        for (int index = -1; index <= 10; index++)
            Check(FlightSettingsStore.ValidActiveChoice(index) == index, "custom and all old/new choices restore unchanged");
        foreach (int invalid in new[] { int.MinValue, -2, 11, int.MaxValue })
            Check(FlightSettingsStore.ValidActiveChoice(invalid) == 10, "invalid active choice falls back to chosen");
        var custom = FlightDraft.From(new FlightSettings(FirstAction.Flip, MotionLaw.Gravity,
            AirRepeatLimit.One, 1250, 280, 450, 265, 96), 4);
        string saved = FlightSettingsStore.Encode(custom);
        Check(FlightSettingsStore.TryDecode(saved, out var restored), "old custom decodes independently of new choice");
        var selection = new FlightSelection(restored);
        selection.SelectPreset(FlightSettingsStore.ChosenPresetIndex);
        Check(FlightSettingsStore.Encode(selection.Custom) == saved, "first new-version chosen preserves old custom");
        selection.SelectCustom(); Same(selection.Settings, custom.Settings(), "old custom restores exact values after chosen");
        selection.SelectPreset(10); selection.Edit(d => d.Jump = 4);
        Check(selection.IsCustom && selection.Custom.ResetPreset == 10, "editing chosen creates separate custom with source");
        Check(FlightPresets.Chosen.JumpImpulse == 350 && FlightPresets.Chosen.FlipImpulse == 0, "chosen stays immutable after edit");
        selection.ResetCustom(); Same(selection.Settings, FlightPresets.Chosen, "reset chosen custom restores exact values");
    }
    static void CopyAndCulture()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "ru-RU", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var d = FlightDraft.From(new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.Unlimited,
                    662.5, 140, 450, 212 / .9, 128), 1);
                string encoded = FlightSettingsStore.Encode(d), text = FlightSettingsStore.CopyText(d);
                Check(encoded.Contains("662.5") && encoded.EndsWith("|128|1"), "storage invariant under culture");
                Check(text.Contains("Набор: " + encoded) && text.Contains("450") && text.Contains("140")
                    && text.Contains("∞") && text.Contains("Неактивные"), "copy has complete reproducible values including hidden fields");
                Check(FlightSettingsStore.TryDecode(encoded, out var restored), "localized display does not corrupt payload");
                Same(d.Settings(), restored.Settings(), "localized round trip");
                roundTrips++;
            }
        }
        finally { CultureInfo.CurrentCulture = savedCulture; }
    }
}
