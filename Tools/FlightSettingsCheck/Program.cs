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
            RoundTrips(); CorruptData(); SelectionIsolation(); ChosenAndActiveChoice(); LegacyMigration(); CopyAndCulture(); FastFlip451();
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
            Check(restored.ResetPreset == 10, "retired reset source migrates without changing physical values");
            roundTrips++;
        }
        for (int preset = 10; preset <= 11; preset++)
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
            "1|0|1|1|1000|70|400|265|112|12", "1|0|1|1|1000|70|400|265|112|-1",
            "1|0|1|1|1000|70|400|265|112|0|extra", new string('x', 513)
        };
        foreach (string text in invalid)
        {
            Check(!FlightSettingsStore.TryDecode(text, out var fallback), "corrupt data rejected: " + text);
            Same(fallback.Settings(), FlightPresets.Chosen, "whole safe fallback, never partially decoded");
            Check(fallback.ResetPreset == 10, "fallback reset target is main");
        }
    }
    static void SelectionIsolation()
    {
        var custom = FlightDraft.From(new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.Unlimited,
            1800, 210, 500, 212 / .6, 160), 10);
        string encoded = FlightSettingsStore.Encode(custom);
        var selection = new FlightSelection(custom);
        custom.Flip = 0;
        Check(FlightSettingsStore.Encode(selection.Custom) == encoded, "selection owns its custom copy");
        for (int preset = 10; preset <= 11; preset++)
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
        selection.SelectPreset(11);
        selection.Edit(d => d.Gravity = 7);
        Check(selection.IsCustom && selection.Custom.ResetPreset == 11, "manual edit leaves preset and remembers its source");
        Check(FlightPresets.FastFlip.GravityAcceleration == 2700 && FlightPresets.FastFlip.JumpImpulse == 300,
            "immutable preset never changed by manual edit");
        Check(selection.Custom.Flip == 1 && selection.Custom.Jump == 2 && selection.Custom.Speed == 7,
            "fast preset retains inactive jump and linear speed in custom");
        selection.ResetCustom();
        Same(selection.Settings, FlightPresets.FastFlip, "reset reloads exact source values");
    }
    static void ChosenAndActiveChoice()
    {
        Check(FlightSettingsStore.Key == "flip-race-custom-flight-v1", "existing custom storage key retained");
        Check(FlightSettingsStore.ActiveChoiceKey == "flip-race-flight-choice-v2", "new-version active choice has separate key");
        Check(FlightSettingsStore.ChosenPresetIndex == 10 && FlightSettingsStore.FastFlipPresetIndex == 11,
            "stable IDs retain main and append fast alternative");
        Same(FlightSettingsStore.Preset(10), new FlightSettings(FirstAction.Jump, MotionLaw.Gravity,
            AirRepeatLimit.Unlimited, 662.5, 0, 350, 265, 112), "chosen exact values including inactive flip");
        Same(FlightSettingsStore.Preset(11), new FlightSettings(FirstAction.Flip, MotionLaw.Gravity,
            AirRepeatLimit.One, 2700, 35, 300, 530, 112), "fast alternative exact active and hidden values");
        foreach(int index in new[]{-1,10,11})
            Check(FlightSettingsStore.ValidActiveChoice(index) == index, "custom/main/fast choice restores unchanged");
        foreach (int invalid in new[] { int.MinValue, -2, 12, int.MaxValue })
            Check(FlightSettingsStore.ValidActiveChoice(invalid) == 10, "invalid active choice falls back to chosen");
        var custom = FlightDraft.From(new FlightSettings(FirstAction.Flip, MotionLaw.Gravity,
            AirRepeatLimit.One, 1250, 280, 450, 265, 96), 10);
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
    static FlightSettings RetiredPreset(int index)
    {
        switch(index)
        {
            case 0:return FlightPresets.Moderate;
            case 1:return FlightPresets.Linear;
            case 2:return FlightPresets.Jump;
            case 3:return FlightPresets.Unlimited;
            case 8:return FlightPresets.FromLegacy(MotionMode.Linear);
            case 9:return FlightPresets.FromLegacy(MotionMode.Gravity);
            default:return FlightPresets.FromLegacy(MotionMode.GravityAirFlip,MotionProfiles.All[index-4]);
        }
    }
    static void LegacyMigration()
    {
        for(int index=0;index<10;index++)
        {
            var old=RetiredPreset(index);string encoded=FlightSettingsStore.Encode(FlightDraft.From(old,index));
            Check(FlightSettingsStore.ValidActiveChoice(index)==10,"retired active choice maps to main");
            Check(FlightSettingsStore.TryDecode(encoded,out var restored),"old custom payload remains valid");
            Same(restored.Settings(),old,"migration preserves every old custom physical parameter");
            Check(restored.ResetPreset==10,"only reset reference migrates to main");
            string migrated=FlightSettingsStore.Encode(restored);
            Check(migrated.Substring(0,migrated.LastIndexOf('|'))==encoded.Substring(0,encoded.LastIndexOf('|')),
                "migration changes no serialized physical field");
            Check(FlightSettingsStore.TryDecode(migrated,out var again)&&FlightSettingsStore.Encode(again)==migrated,
                "migration is idempotent after reload");
            var selection=new FlightSelection(restored);selection.SelectCustom();Same(selection.Settings,old,"active custom still uses original physics");
            selection.SelectPreset(11);selection.SelectPreset(10);selection.SelectCustom();
            Same(selection.Settings,old,"both preset switches preserve migrated custom");
            selection.ResetCustom();Same(selection.Settings,FlightPresets.Chosen,"explicit reset now loads main");
            bool rejected=false;try{selection.SelectPreset(index);}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"retired ID cannot silently select a different preset");
            roundTrips++;
        }
        const string userSet="1|0|1|0|2700|35|300|530|112|10";
        Check(FlightSettingsStore.TryDecode(userSet,out var supplied),"user's original zero-repeat payload decodes");
        Same(supplied.Settings(),new FlightSettings(FirstAction.Flip,MotionLaw.Gravity,AirRepeatLimit.None,
            2700,35,300,530,112),"original custom zero-repeat physics is not migrated to the new preset");
        Check(supplied.ResetPreset==10,"user payload reset10 is metadata, not mode ID or repeats");
        var own=FlightDraft.From(FlightPresets.FastFlip,11);
        Check(FlightSettingsStore.TryDecode(FlightSettingsStore.Encode(own),out var fast)&&fast.ResetPreset==11,
            "new fast reset source survives round trip");
    }
    static void FastFlip451()
    {
        const string newPayload="1|0|1|1|2700|35|300|530|112|10";
        const string oldPayload="1|0|1|0|2700|35|300|530|112|10";
        var oldPhysics=new FlightSettings(FirstAction.Flip,MotionLaw.Gravity,AirRepeatLimit.None,2700,35,300,530,112);
        Check(FlightSettingsStore.TryDecode(newPayload,out var upgraded),"4.5.1 one-repeat payload decodes");
        Same(upgraded.Settings(),FlightPresets.FastFlip,"new user payload equals the one-repeat preset including hidden fields");
        Check(upgraded.ResetPreset==10&&FlightSettingsStore.Encode(upgraded)==newPayload,
            "new payload keeps reset10 metadata and exact serialization");
        Check(FlightSettingsStore.Summary(upgraded.Settings()).Contains("повтор 1"),"menu summary displays one repeat");
        Check(FlightSettingsStore.CopyText(upgraded).Contains("Воздушные повторы: 1"),"copy text displays one repeat");
        roundTrips++;

        foreach(int source in new[]{10,11})
        {
            string saved=source==10?oldPayload:oldPayload.Substring(0,oldPayload.LastIndexOf('|')+1)+"11";
            Check(FlightSettingsStore.TryDecode(saved,out var remembered),"saved zero-repeat custom decodes for either reset source");
            Same(remembered.Settings(),oldPhysics,"saved custom keeps repeat zero and all exact values");
            Check(FlightSettingsStore.Encode(remembered)==saved,"no implicit custom rewrite on decode");
            var directReset=new FlightSelection(remembered);directReset.SelectCustom();directReset.ResetCustom();
            Same(directReset.Settings,source==11?FlightPresets.FastFlip:FlightPresets.Chosen,
                "explicit reset of a loaded custom follows its unchanged source metadata");
            var selection=new FlightSelection(remembered);
            selection.SelectPreset(FlightSettingsStore.ValidActiveChoice(11));
            Same(selection.Settings,FlightPresets.FastFlip,"active11 resolves the updated one-repeat preset");
            Check(FlightSettingsStore.Encode(selection.Custom)==saved,"active11 preserves separately remembered custom0");
            selection.SelectCustom();Same(selection.Settings,oldPhysics,"return to remembered custom restores zero repeats");
            Check(FlightSettingsStore.Summary(selection.Settings).Contains("повтор 0"),"custom summary honestly displays zero");
            Check(FlightSettingsStore.CopyText(selection.Custom).Contains("Воздушные повторы: 0"),"custom copy retains zero");

            // Recreate the Awake boundary from a serialized custom and active choice.
            Check(FlightSettingsStore.TryDecode(FlightSettingsStore.Encode(selection.Custom),out var reloaded),"custom reload decodes");
            var restored=new FlightSelection(reloaded);restored.SelectPreset(FlightSettingsStore.ValidActiveChoice(11));
            Same(restored.Settings,FlightPresets.FastFlip,"reload active11 loads one repeat");
            Check(FlightSettingsStore.Encode(restored.Custom)==saved,"reload never rewrites the old custom0");
            restored.SelectCustom();Same(restored.Settings,oldPhysics,"reload then custom restores zero repeats");
            var unchangedCustom=restored.Custom;
            restored.SelectPreset(11);restored.Edit(d=>{});
            Same(restored.Settings,FlightPresets.FastFlip,"explicit Edit from Fast copies one repeat");
            Check(restored.IsCustom&&restored.Custom.ResetPreset==11,"edited Fast draft remembers source11");
            Check(FlightSettingsStore.Encode(unchangedCustom)==saved&&FlightSettingsStore.Encode(remembered)==saved,
                "editing a new Fast draft does not mutate the prior custom object or serialized record");
            restored.Edit(d=>d.Repeats=AirRepeatLimit.None);
            Check(restored.Settings.AirRepeats==AirRepeatLimit.None,"manual zero repeat remains available");
            restored.ResetCustom();Same(restored.Settings,FlightPresets.FastFlip,"explicit Reset to Fast now restores one repeat");
            Check(FlightSettingsStore.Encode(unchangedCustom)==saved,"reset uses a new draft without mutating the prior custom");
            Check(FlightSettingsStore.Encode(remembered)==saved,"original saved custom0 remains unchanged throughout");
            roundTrips++;
        }
        Check(FlightSettingsStore.TryDecode(oldPayload,out var original),"old reset10 custom can be restored again");
        var own=new FlightSelection(original);own.SelectCustom();own.ResetCustom();
        Same(own.Settings,FlightPresets.Chosen,"explicit reset of old source10 still targets main, not Fast");
        Check(FlightPresets.FastFlip.AirRepeats==AirRepeatLimit.One,"no custom operation mutates the preset");
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
                    662.5, 140, 450, 212 / .9, 128), 10);
                string encoded = FlightSettingsStore.Encode(d), text = FlightSettingsStore.CopyText(d);
                Check(encoded.Contains("662.5") && encoded.EndsWith("|128|10"), "storage invariant under culture");
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
