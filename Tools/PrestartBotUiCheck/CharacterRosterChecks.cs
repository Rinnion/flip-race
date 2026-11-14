using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;
using FlipRace.SharedScreenCheck.Interop;
using UnityEngine;
using Display = FlipRace.SharedScreenCheck.Interop.Display;

static partial class Program
{
    static int characterLayoutCases;

    static int CharacterRosterSmoke(string builtIn)
    {
        context = "roster data";
        string[] names = { "Листик", "Капелька", "Травник", "Цветок", "Джек", "Тыква", "Нико", "Игри", "Шедоу", "Пушинка" };
        Check(CharacterRoster.Count == names.Length && RaceConfig.CharacterCount == names.Length, "complete ten-hero roster");
        foreach (int hero in Enumerable.Range(0, names.Length))
        {
            Check(CharacterRoster.Name(hero) == names[hero], "stable index and full name " + hero);
            Check(new[] { CharacterRoster.SuccessText(hero), CharacterRoster.HitText(hero), CharacterRoster.SlowText(hero),
                CharacterRoster.WinText(hero) }.All(text => !string.IsNullOrWhiteSpace(text)), "each hero has all reactions");
        }
        foreach (var size in new[] { (w: 568, h: 320), (w: 1280, h: 720) })
        {
            Prefs.Clear();
            for (int mode = 0; mode < 4; mode++)
            {
                context = $"legacy mode {mode} at {size}";
                var ui = NewUi(builtIn, 42, size.w, size.h);
                OpenModes(ui);
                Click(ui, ui.ViewOptionRect(0, Math.Min(mode, 2)));
                if (mode >= 2) Click(ui, ui.ViewOptionRect(1, mode - 2));
                Click(ui, ui.FlightApplyRect);
                Tick(ui);
                for (int slot = 0; slot < 2; slot++)
                    CheckCharacterPicker(ui, slot, paged: mode == 3 && size.w == 568);
                Check(ui.Game.Players.All(p => p.CharacterIndex == 9 && !p.Ready), "either seat can choose Пушинка");
                Check(!RaceSimulation.SetCharacter(ui.Game, 0, -1) && !RaceSimulation.SetCharacter(ui.Game, 0, 10),
                    "out-of-roster indices rejected");
                if (mode == 3 && size.w == 568) TestSelectionReopen(ui);
                Click(ui, ui.ReadyScreenRect(0));
                Check(ui.Game.Players[0].Ready && ui.Game.Phase == "ready", "first ready is personal");
                CheckSelectionStillPersonal(ui, 1);
                Click(ui, ui.ReadyScreenRect(1));
                Check(ui.Game.Phase == "countdown", "second ready starts countdown");
            }

            context = $"solo and standalone bot at {size}";
            Prefs.Clear();
            var solo = NewUi(builtIn, 43, size.w, size.h);
            OpenModes(solo);
            Click(solo, solo.SoloMenuButtonRect);
            Click(solo, solo.FlightApplyRect);
            Tick(solo);
            Check(solo.SoloEnabled, "solo entered through settings");
            CheckCharacterPicker(solo, 0, paged: false);
            Click(solo, solo.ReadyScreenRect(0));
            Check(solo.Game.Phase == "countdown" && solo.Game.Players[0].CharacterIndex == 9, "solo starts with chosen hero");

            var bot = NewUi(builtIn, 44, size.w, size.h);
            StandaloneMenu(bot, false);
            Click(bot, bot.FlightApplyRect);
            Tick(bot);
            Check(bot.BotEnabled && bot.Game.Players[1].Ready, "standalone bot has automatic readiness");
            CheckCharacterPicker(bot, 0, paged: false);
            Check(bot.CharacterScreenRect(1, 9).width == 0 && bot.CharacterPageScreenRect(1).width == 0
                && bot.Game.Players[1].Ready && bot.Game.Players[1].CharacterIndex == 0,
                "standalone rival has no selector and keeps distinct hero when advancing past index 9");
            Click(bot, bot.ReadyScreenRect(0));
            Check(bot.Game.Phase == "countdown" && bot.Game.Players[0].CharacterIndex == 9, "standalone starts with chosen hero");

            for (int count = 2; count <= 4; count++)
            {
                context = $"party {count} at {size}";
                foreach (bool allBots in new[] { false, true })
                {
                    Prefs.Clear();
                    var party = NewUi(builtIn, 45, size.w, size.h);
                    OpenModes(party);
                    Click(party, party.PartyMenuButtonRect);
                    Click(party, party.PartyCountRect(count - 2));
                    Click(party, party.FlightApplyRect);
                    int[] active = count == 2 ? new[] { 0, 3 } : count == 3 ? new[] { 0, 1, 3 } : new[] { 0, 1, 2, 3 };
                    foreach (int corner in active.Skip(allBots ? 0 : 1)) Click(party, party.PartyRosterRoleRect(corner));
                    Click(party, party.FlightApplyRect);
                    Tick(party);
                    Check(party.PartyEnabled && party.Game.Players.Length == count && party.Game.HumanCount == (allBots ? 0 : 1),
                        "party roles and physical seats applied");
                    for (int slot = 0; slot < count; slot++)
                        CheckCharacterPicker(party, slot, paged: size.w == 568, rotated: active[slot] < 2);
                    Check(party.Game.Players.All(p => p.CharacterIndex == 9), "same new hero selectable in every party role");
                    var action = allBots ? party.PartyAllBotActionRect : party.ReadyScreenRect(0);
                    Click(party, action);
                    Check(party.Game.Phase == "countdown", "party starts by existing readiness rule");
                }
            }
        }
        TestPageDuringPrestart();
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", checks, updates, pointers, characterLayoutCases,
            scope = "Production Measure/Pointer/Update/Core with managed input and native texture acquisition shim. Actual WebGL visuals and event timing separate." }));
        return 0;
    }

    static void CheckCharacterPicker(FlipRaceGame ui, int slot, bool paged, bool? rotated = null)
    {
        characterLayoutCases++;
        Rect area = ui.ControlAreaRect(slot), ready = ui.ReadyScreenRect(slot), pager = ui.CharacterPageScreenRect(slot);
        Check((pager.width > 0) == paged, "page control visible only where a five-column row would be too narrow");
        Rect common = ui.PartyAllBotActionRect;
        var otherPages = Enumerable.Range(0, ui.Game.Players.Length).Where(i => i != slot)
            .ToDictionary(i => i, i => ui.CharacterScreenRect(i, 0));
        if (paged)
        {
            CheckCharacterTarget(pager, area, ready, common, ui.SessionBarRect);
            Click(ui, ui.CharacterScreenRect(slot, 0));
            for (int hero = 5; hero < 10; hero++) Check(ui.CharacterScreenRect(slot, hero).width == 0, "hidden hero publishes an empty rectangle");
        }
        for (int page = 0; page < (paged ? 2 : 1); page++)
        {
            var cards = new List<Rect>();
            int start = page * 5, end = paged ? start + 5 : CharacterRoster.Count;
            for (int hero = start; hero < end; hero++)
            {
                Rect card = ui.CharacterScreenRect(slot, hero);
                CheckCharacterTarget(card, area, ready, common, ui.SessionBarRect);
                Check(!card.Overlaps(pager) && cards.All(previous => !card.Overlaps(previous)), "hero cards and pager use separate targets");
                cards.Add(card);
                Click(ui, card);
                Check(ui.Game.Players[slot].CharacterIndex == hero && ui.Game.Phase == "ready", "tapping each card immediately selects its own hero");
            }
            if (rotated.HasValue)
                Check((cards[0].center.x > cards[1].center.x) == rotated.Value, "upper-quarter grid and hits rotate together");
            if (paged && page == 0)
            {
                Click(ui, pager);
                Check(ui.Game.Players[slot].CharacterIndex == 4 && ui.Game.Phase == "ready", "pager tap does not select the hidden hero at that cell");
                Check(ui.CharacterScreenRect(slot, 0).width == 0 && ui.CharacterScreenRect(slot, 5).width > 0,
                    "page two replaces the first five accessible indices");
                Check(otherPages.All(other => ui.CharacterScreenRect(other.Key, 0) == other.Value), "each seat has independent pages");
            }
        }
    }

    static void CheckCharacterTarget(Rect rect, Rect area, Rect ready, Rect common, Rect toolbar)
    {
        Check(rect.width >= 80 && rect.height >= 44 && rect.xMin >= area.xMin - .01f && rect.xMax <= area.xMax + .01f
            && rect.yMin >= area.yMin - .01f && rect.yMax <= area.yMax + .01f, "full-size target inside its seat");
        Check(!rect.Overlaps(ready) && !rect.Overlaps(common) && !rect.Overlaps(toolbar),
            $"target clear of readiness {ready}, common start {common} and toolbar {toolbar}: {rect} in {area}");
    }

    static void CheckSelectionStillPersonal(FlipRaceGame ui, int slot)
    {
        Rect pager = ui.CharacterPageScreenRect(slot);
        if (pager.width > 0) Click(ui, pager);
        var card = ui.CharacterScreenRect(slot, pager.width > 0 ? 3 : 8);
        Click(ui, card);
        Check(ui.Game.Players[1 - slot].Ready && !ui.Game.Players[slot].Ready, "page and hero taps preserve the other seat's readiness");
    }

    static void TestSelectionReopen(FlipRaceGame ui)
    {
        Rect pager = ui.CharacterPageScreenRect(0);
        Click(ui, ui.ReadyScreenRect(0));
        Click(ui, pager);
        Check(ui.Game.Players[0].Ready && ui.Game.Players[0].CharacterIndex == 9 && ui.CharacterScreenRect(0, 9).width == 0,
            "ready seat can browse the other page without clearing readiness or selecting a hero");
        Click(ui, ui.TrackMenuButtonRect);
        Call(ui, "MeasureFlightMenu");
        Click(ui, Read<Rect>(ui, "flightBack"));
        Tick(ui);
        Check(ui.CharacterSelectionVisible && ui.CharacterScreenRect(0, 9).width > 0 && ui.Game.Players[0].Ready,
            "return from settings shows the selected hero's page and retains readiness");
        Click(ui, ui.CharacterScreenRect(0, 8));
        Check(!ui.Game.Players[0].Ready, "selecting a hero still clears personal readiness");
    }

    static void TestPageDuringPrestart()
    {
        context = "prestart pending request and role separation";
        Prefs.Clear();
        var ui = NewUi(LocalTrack, 61);
        Party(ui, 2, 3);
        Tick(ui);
        var firstReady = ui.ReadyScreenRect(0);
        var secondReady = ui.ReadyScreenRect(1);
        var firstPage = ui.CharacterPageScreenRect(0);
        var botPage = ui.CharacterPageScreenRect(2);
        uint seed = ui.PrestartAttemptSeed;
        Devices.Touches = new[] { TouchAt(1, firstReady), TouchAt(2, secondReady), TouchAt(3, firstPage), TouchAt(4, botPage) };
        Tick(ui);
        Devices.Clear();
        double queuedAt = Read<double>(ui, "prestartStartRequestedAt");
        Check(queuedAt >= 0 && ui.Game.Phase == "ready" && ui.Game.Players.Take(2).All(p => p.Ready)
            && ui.Game.Players[0].CharacterIndex == 0 && ui.Game.Players[2].CharacterIndex == 2
            && ui.CharacterScreenRect(0, 9).width > 0 && ui.CharacterScreenRect(2, 9).width > 0 && ui.PrestartAttemptSeed == seed,
            "simultaneous human and bot paging preserves readiness, requested start and preparation");
        Click(ui, ui.CharacterScreenRect(2, 8));
        Check(ui.Game.Players[2].CharacterIndex == 8 && Read<double>(ui, "prestartStartRequestedAt") == queuedAt
            && ui.PrestartAttemptSeed == seed, "bot selection keeps start request and route job");
        Click(ui, ui.CharacterScreenRect(0, 9));
        Check(ui.Game.Players[0].CharacterIndex == 9 && !ui.Game.Players[0].Ready && ui.Game.Players[1].Ready
            && Read<double>(ui, "prestartStartRequestedAt") < 0 && ui.PrestartAttemptSeed == seed,
            "human selection clears only own readiness and requested start while retaining route job");
    }
}
