using System;
using System.Linq;
using FlipRace.Core;
using FlipRace.Presentation;
using FlipRace.SharedScreenCheck.Interop;
using UnityEngine;

namespace FlipRace.PartyUiCheck
{
    static partial class Program
    {
        static int rosterCases;
        static readonly KeyCode[] PartyKeys = { KeyCode.A, KeyCode.L, KeyCode.S, KeyCode.K };

        static int Corner(int slot, int count) => count == 2 ? (slot == 0 ? 0 : 3) : count == 3 && slot == 2 ? 3 : slot;
        static void MenuClick(FlipRaceGame ui, string field)
        {
            Call(ui, "MeasureFlightMenu");
            Click(ui, ((Rect)Field(ui, field)).center);
        }

        static void OpenRoster(FlipRaceGame ui, int count)
        {
            Call(ui, "OpenTracks");
            Click(ui, ui.ViewMenuButtonRect.center);
            if (ui.FlightMenuPage != 6) Click(ui, ui.PartyMenuButtonRect.center);
            Check(ui.FlightMenuPage == 6, "existing company count/camera page");
            Click(ui, ui.PartyCountRect(count - 2).center);
            MenuClick(ui, "flightApply");
            Check(ui.FlightMenuPage == 12, "count/camera continue to roster");
        }

        static void ChooseLevel(FlipRaceGame ui, int corner, BotDifficulty level)
        {
            Click(ui, ui.PartyRosterDifficultyRect(corner).center);
            Check(ui.FlightMenuPage == 13, "bot opens the personal difficulty page");
            var levels = Enumerable.Range(0, 5).Select(ui.PartyRosterLevelRect).ToArray();
            var panel = (Rect)Field(ui, "flightPanel");
            Check(levels.All(r => Inside(r, panel) && r.width >= 44 && r.height >= 44)
                && levels.SelectMany((a, i) => levels.Skip(i + 1).Select(b => a.Overlaps(b))).All(overlap => !overlap),
                "five independent 44-unit difficulty buttons inside the panel");
            Click(ui, levels[(int)level].center);
            Check(ui.FlightMenuPage == 12, "difficulty choice returns to roster");
        }

        static void CheckRoster(FlipRaceGame ui, BotDifficulty?[] slots)
        {
            Check(ui.PartyEnabled && ui.Game.Players.Length == slots.Length && ui.Game.HumanCount == slots.Count(s => !s.HasValue),
                "company has the exact requested total and human count");
            for (int slot = 0; slot < slots.Length; slot++)
            {
                Check(ui.Game.IsBotSlot(slot) == slots[slot].HasValue && ui.Game.GetPartyBotSettings(slot)?.Difficulty == slots[slot],
                    "role and individual level stay with the physical corner");
                Check(ui.PawScreenRect(slot).width == (slots[slot].HasValue ? 0 : 56)
                    && ui.ReadyScreenRect(slot).height == (slots[slot].HasValue ? 0 : 44), "only humans have paws and readiness buttons");
            }
        }

        static void CheckRosterCase(int w, int h, float dpr, int count, PartyCamera camera, int choice, int mask)
        {
            var ui = Ui(w, h, dpr, choice, count, camera, openRosterOnly: true);
            var slots = new BotDifficulty?[count];
            for (int slot = 0; slot < count; slot++)
            {
                int corner = Corner(slot, count);
                Rect role = ui.PartyRosterRoleRect(corner), difficulty = ui.PartyRosterDifficultyRect(corner);
                var panel = (Rect)Field(ui, "flightPanel");
                Check(Inside(role, panel) && Inside(difficulty, panel) && role.width >= 44 && role.height == 44
                    && difficulty.width >= 44 && difficulty.height == 44 && !role.Overlaps(difficulty), "composition buttons remain full targets");
                if ((mask & (1 << slot)) == 0) continue;
                slots[slot] = (BotDifficulty)((mask + slot + count) % 5);
                Click(ui, role.center);
                ChooseLevel(ui, corner, slots[slot].Value);
            }
            foreach (int corner in Enumerable.Range(0, 4).Except(Enumerable.Range(0, count).Select(i => Corner(i, count))))
            {
                Click(ui, ui.PartyRosterRoleRect(corner).center);
                Check(ui.FlightMenuPage == 12, "inactive quadrant is inert");
            }
            MenuClick(ui, "flightApply");
            CheckRoster(ui, slots);
            Check(ui.Game.PartyCamera == camera && ui.Game.SurfaceRunEnabled
                && ui.Game.Players.All(p => p.SurfaceRunEnabled), "camera and default acceleration reach every participant");
            Check(Enumerable.Range(0, count).Where(i => !slots[i].HasValue).All(i => !ui.Game.Players[i].Ready),
                "human readiness starts clear; bots do not require it");
            Rect commonAction = ui.PartyAllBotActionRect;
            Check((commonAction.height == 44) == (ui.Game.HumanCount == 0), "one common action only in all-bot composition");
            if (ui.Game.HumanCount == 0)
                Check(Inside(commonAction, (Rect)Field(ui, "arena")) && !commonAction.Overlaps(ui.PartyFieldRect)
                    && !commonAction.Overlaps(ui.SessionBarRect), "common action outside the world and toolbar");

            for (int slot = 0; slot < count; slot++)
            {
                for (int hero = 0; hero < 6; hero++)
                {
                    Rect card = ui.CharacterScreenRect(slot, hero);
                    Check(Inside(card, ui.ControlAreaRect(slot)) && card.width >= 44 && card.height == 44
                        && !card.Overlaps(commonAction), "same large hero cards for human and bot, clear of common action");
                }
                int heroChoice = (slot + 3) % 6;
                Click(ui, ui.CharacterScreenRect(slot, heroChoice).center);
                Check(ui.Game.Players[slot].CharacterIndex == heroChoice && ui.Game.Phase == "ready", "hero click works independently for each role");
            }

            var humans = Enumerable.Range(0, count).Where(i => !slots[i].HasValue).ToArray();
            var bots = Enumerable.Range(0, count).Where(i => slots[i].HasValue).ToArray();
            foreach (int slot in bots) Devices.Keys.Add(PartyKeys[slot]);
            Tick(ui); Devices.Clear();
            Check(ui.Game.Phase == "ready" && humans.All(i => !ui.Game.Players[i].Ready), "bot keys cannot start or ready a human");
            if (humans.Length == 0) Click(ui, commonAction.center);
            else for (int i = 0; i < humans.Length; i++)
            {
                Click(ui, ui.ReadyScreenRect(humans[i]).center);
                Check(ui.Game.Phase == (i + 1 == humans.Length ? "countdown" : "ready"), "only human readiness controls the countdown");
            }
            Check(ui.Game.Phase == "countdown" && ui.PartyAllBotActionRect.width == 0, "explicit start begins exactly one countdown");
            Tick(ui);
            for (int frame = 0; frame < 50 && ui.Game.Phase == "countdown"; frame++) Tick(ui, .1);
            Check(ui.Game.Phase == "racing" && ui.PawsVisible == (humans.Length > 0), "live countdown finishes; spectator mode has no paws");
            Call(ui, "OpenTracks");
            Check(ui.FlightMenuPage == -1, "roster stays locked during the attempt");
            Tick(ui, .1);
            Check(ui.Game.Players.All(p => p.SurfaceRunReserve > 0), "live Update advances all racers with the same acceleration choice");

            string beforeBotKeys = State(ui.Game);
            foreach (int slot in bots) Devices.Keys.Add(PartyKeys[slot]);
            Tick(ui); Devices.Clear();
            Check(State(ui.Game) == beforeBotKeys, "keyboard on bot slots cannot change the live race");
            TouchAll(ui, slot => slots[slot].HasValue ? ui.ControlAreaRect(slot).center : ui.PawScreenRect(slot).center, mouse: true);
            Check(ui.Game.Players.Select(p => p.Flipping).SequenceEqual(slots.Select(s => !s.HasValue))
                && ui.Game.Players.All(p => p.AirFlipAvailable), "simultaneous touch uses only human paws, without a duplicate mouse edge");

            uint[] oldSeeds = bots.Select(i => ui.Game.GetPartyBotSettings(i).Seed).ToArray();
            Check(oldSeeds.Distinct().Count() == bots.Length, "bot seeds are distinct within the attempt");
            int[] oldHeroes = ui.Game.Players.Select(p => p.CharacterIndex).ToArray();
            if (count == 4 && mask == 15 && w == 568 && dpr == 1 && camera == PartyCamera.Lagger && choice == 10)
            {
                for (int frame = 0; frame < 1200 && ui.Game.Phase != "finished"; frame++) Tick(ui, .1);
                Check(ui.Game.Phase == "finished" && ui.Game.Players.All(p => p.Status != PlayerStatus.Running), "actual Update reaches the result of all four bots");
                double elapsed = ui.Game.Elapsed;
                Tick(ui, .1);
                Check(ui.Game.Phase == "finished" && ui.Game.Elapsed == elapsed, "the result does not start a loop");
                Evidence.Add(new { Kind = "actual-four-bot-update-to-result", elapsed, Distances = ui.Game.Players.Select(p => p.Distance).ToArray() });
            }
            else
            {
                ui.Game.Phase = "finished";
                foreach (var player in ui.Game.Players) { player.Status = PlayerStatus.Eliminated; player.Ready = false; }
            }
            Call(ui, "Measure");
            if (humans.Length == 0) Click(ui, ui.PartyAllBotActionRect.center);
            else for (int i = 0; i < humans.Length; i++)
            {
                Click(ui, ui.ReadyScreenRect(humans[i]).center);
                Check(ui.Game.Phase == (i + 1 == humans.Length ? "countdown" : "finished"), "only human readiness controls the mixed retry");
            }
            Check(ui.Game.Phase == "countdown" && ui.Game.Players.Select(p => p.CharacterIndex).SequenceEqual(oldHeroes),
                "actual retry retains all heroes and starts one countdown");
            CheckRoster(ui, slots);
            var newSeeds = bots.Select(i => ui.Game.GetPartyBotSettings(i).Seed).ToArray();
            Check(newSeeds.Distinct().Count() == bots.Length && !newSeeds.Intersect(oldSeeds).Any()
                && ui.Game.Players.All(p => p.SurfaceRunEnabled && p.SurfaceRunReserve == 0), "retry gives every bot a fresh seed and keeps acceleration");
            rosterCases++;
        }

        static void CheckRosterPersistence()
        {
            var ui = Ui(568, 320, 3, 10, 4, PartyCamera.Lagger, openRosterOnly: true);
            Click(ui, ui.PartyRosterRoleRect(1).center);
            Click(ui, ui.PartyRosterRoleRect(3).center);
            ChooseLevel(ui, 3, BotDifficulty.Master);
            MenuClick(ui, "flightApply");
            var expected = new BotDifficulty?[] { null, BotDifficulty.Amateur, null, BotDifficulty.Master };
            CheckRoster(ui, expected);
            Click(ui, ui.ReadyScreenRect(0).center);
            Check(ui.Game.Players[0].Ready, "first person can ready independently");
            OpenRoster(ui, 4);
            Check(!ui.Game.Players[0].Ready, "returning to composition clears human readiness");
            Click(ui, ui.PartyRosterDifficultyRect(1).center);
            MenuClick(ui, "flightBack");
            Check(ui.FlightMenuPage == 12, "difficulty Back returns to composition");
            Click(ui, ui.PartyRosterRoleRect(1).center);
            MenuClick(ui, "flightBack"); MenuClick(ui, "flightBack"); MenuClick(ui, "flightBack");
            Check(ui.FlightMenuPage == -1, "Back closes the root and cancels pending roster edits");
            CheckRoster(ui, expected);

            OpenRoster(ui, 2); MenuClick(ui, "flightApply");
            CheckRoster(ui, new BotDifficulty?[] { null, BotDifficulty.Master });
            OpenRoster(ui, 4); MenuClick(ui, "flightApply");
            CheckRoster(ui, expected);
            OpenRoster(ui, 4);
            Click(ui, ui.PartyRosterRoleRect(0).center); Click(ui, ui.PartyRosterRoleRect(2).center);
            MenuClick(ui, "flightApply");
            var allBots = new BotDifficulty?[] { BotDifficulty.Amateur, BotDifficulty.Amateur, BotDifficulty.Amateur, BotDifficulty.Master };
            CheckRoster(ui, allBots);

            Call(ui, "OpenTracks"); Click(ui, ui.ViewMenuButtonRect.center); Click(ui, ui.BotMenuButtonRect.center);
            MenuClick(ui, "flightApply");
            Check(ui.BotEnabled && !ui.PartyEnabled && ui.Game.HumanCount == 1, "all-bot Company returns to ordinary BOT");
            Call(ui, "OpenTracks"); Click(ui, ui.ViewMenuButtonRect.center); Click(ui, ui.SoloMenuButtonRect.center);
            MenuClick(ui, "flightApply");
            Check(ui.SoloEnabled && !ui.PartyEnabled && ui.Game.HumanCount == 1, "all-bot Company can leave via BOT to SOLO");
            OpenRoster(ui, 4); MenuClick(ui, "flightApply");
            CheckRoster(ui, allBots);
        }

        static void CheckRosterAdmission()
        {
            var ui = Ui(568, 320, 1, -1, 2, PartyCamera.Lagger, openRosterOnly: true);
            var previous = ui.Game;
            Click(ui, ui.PartyRosterRoleRect(3).center);
            MenuClick(ui, "flightApply");
            Check(ReferenceEquals(previous, ui.Game) && ui.FlightMenuPage == 12
                && ((string)Call(ui, "FlightApplyRestriction")).Contains("готовый профиль"), "custom plus bot shows reason and changes no active game");
            MenuClick(ui, "flightBack"); MenuClick(ui, "flightBack");
            MenuClick(ui, "flightChosen"); MenuClick(ui, "flightApply");
            Check(ui.FlightMenuPage == 12, "correcting the profile retains the pending composition");
            MenuClick(ui, "flightApply");
            CheckRoster(ui, new BotDifficulty?[] { null, BotDifficulty.Amateur });

            ui = Ui(568, 320, 1, -1, 2, PartyCamera.Lagger, openRosterOnly: true);
            Click(ui, ui.PartyRosterRoleRect(3).center);
            Click(ui, ui.PartyRosterRoleRect(3).center);
            MenuClick(ui, "flightApply");
            CheckRoster(ui, new BotDifficulty?[2]);
            Check(ui.IsCustomFlight, "all-human Company preserves the manual profile");
        }

        static void CheckRosterRunChoice()
        {
            var ui = Ui(568, 320, 1, 11, 3, PartyCamera.Lagger, openRosterOnly: true);
            Click(ui, ui.PartyRosterRoleRect(0).center);
            Click(ui, ui.PartyRosterRoleRect(3).center);
            MenuClick(ui, "flightBack"); MenuClick(ui, "flightBack");
            Click(ui, ui.SurfaceRunMenuRect.center);
            Click(ui, ui.SurfaceRunRateRect(2).center);
            Click(ui, ui.SurfaceRunCurveRect(SurfaceRunCurve.Exponential).center);
            Click(ui, ui.SurfaceRunCapRect(40).center);
            Click(ui, ui.SurfaceRunAirDecayRect(50).center);
            Click(ui, ui.SurfaceRunResetRect(false).center);
            MenuClick(ui, "flightApply");
            Check(ui.FlightMenuPage == 12, "apply from acceleration settings opens the pending roster");
            MenuClick(ui, "flightApply");
            var roster = new BotDifficulty?[] { BotDifficulty.Amateur, null, BotDifficulty.Amateur };
            CheckRoster(ui, roster);
            var expected = new SurfaceRunSettings(2, SurfaceRunCurve.Exponential, 40, 50, false);
            Check(ui.Game.SurfaceRunEnabled && ui.Game.SurfaceRunSettings.Equals(expected)
                && ui.Game.Players.All(p => p.SurfaceRunEnabled && p.SurfaceRunSettings.Equals(expected)),
                "exact chosen five acceleration values reach humans and bots");

            Call(ui, "OpenTracks"); Click(ui, ui.SurfaceRunMenuRect.center);
            Click(ui, ui.SurfaceRunToggleRect.center); MenuClick(ui, "flightApply");
            CheckRoster(ui, roster);
            Check(!ui.Game.SurfaceRunEnabled && ui.Game.SurfaceRunSettings.Equals(expected)
                && ui.Game.Players.All(p => !p.SurfaceRunEnabled && p.SurfaceRunSettings.Equals(expected)),
                "changing settings in an existing Company retains the exact roster and inactive settings");
        }

        static void CheckRosters()
        {
            foreach (var size in new[] { (w: 568, h: 320), (w: 1280, h: 720) })
            foreach (float dpr in new[] { 1f, 2.5f, 3f })
            foreach (int count in new[] { 2, 3, 4 })
            foreach (var camera in new[] { PartyCamera.Constant, PartyCamera.Lagger })
            foreach (int choice in new[] { 10, 11 })
            for (int mask = 0; mask < 1 << count; mask++)
            {
                try { CheckRosterCase(size.w, size.h, dpr, count, camera, choice, mask); }
                catch (Exception ex) { throw new Exception($"Roster {size.w}x{size.h} DPR {dpr}, count {count}, {camera}, preset {choice}, mask {mask}", ex); }
            }
            CheckRosterPersistence();
            CheckRosterAdmission();
            CheckRosterRunChoice();
        }
    }
}
