using FlipRace.Core;
using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        const int PartyRosterPage = 12, PartyLevelPage = 13;
        const int PartySeatCount = 4;

        struct PartySeatChoice
        {
            public bool IsBot;
            public BotDifficulty? SelectedLevel;
            public BotDifficulty Level => SelectedLevel ?? BotDifficulty.Amateur;
        }

        PartySeatChoice[] partySeats, pendingPartySeats;
        int partyRosterReturnPage, partyLevelCorner;

        void BeginPartyRosterMenu()
        {
            partySeats ??= new PartySeatChoice[PartySeatCount];
            pendingPartySeats = (PartySeatChoice[])partySeats.Clone();
        }

        bool PendingPartyHasBots
        {
            get
            {
                if (!pendingParty || pendingPartySeats == null) return false;
                for (int slot = 0; slot < pendingPartyCount; slot++)
                    if (pendingPartySeats[PartyCorner(slot, pendingPartyCount)].IsBot) return true;
                return false;
            }
        }

        bool ApplyOpensPartyRoster => pendingParty && flightPage != PartyRosterPage
            && (flightPage == 6 || !PartyEnabled);

        void OpenPartyRoster()
        {
            if (PartyEnabled)
                for (int slot = 0; slot < Game.Players.Length; slot++)
                    if (!Game.IsBotSlot(slot)) Game.Players[slot].Ready = false;
            partyRosterReturnPage = flightPage;
            flightPage = PartyRosterPage;
            BlockGameplayInput();
        }

        GameState CreateSelectedParty(Track track)
        {
            if (!PendingPartyHasBots)
                return RaceSimulation.CreateParty(track, flightSelection.Settings, pendingPartyCount,
                    pendingPartyCamera, PartyStartLeft, PartyWindowWidth);

            var slots = new BotDifficulty?[pendingPartyCount];
            for (int slot = 0; slot < slots.Length; slot++)
            {
                var seat = pendingPartySeats[PartyCorner(slot, slots.Length)];
                if (seat.IsBot) slots[slot] = seat.Level;
            }
            return RaceSimulation.CreateParty(track, flightSelection.Settings, slots, NextBotSeed(),
                pendingPartyCamera, PartyStartLeft, PartyWindowWidth);
        }

        Rect PartySeatRect(int corner)
        {
            float w = (flightPanel.width - 44) / 2;
            return new Rect(flightPanel.x + 16 + corner % 2 * (w + 12),
                flightPanel.y + 54 + corner / 2 * 77, w, 69);
        }

        public Rect PartyRosterRoleRect(int corner)
        {
            MeasureFlightMenu();
            Rect seat = PartySeatRect(corner);
            return new Rect(seat.x + 6, seat.y + 20, (seat.width - 18) / 2, 44);
        }

        public Rect PartyRosterDifficultyRect(int corner)
        {
            Rect role = PartyRosterRoleRect(corner);
            return new Rect(role.xMax + 6, role.y, role.width, role.height);
        }

        public Rect PartyRosterLevelRect(int level)
        {
            MeasureFlightMenu();
            float w = (flightPanel.width - 40) / 2;
            return new Rect(flightPanel.x + 16 + level % 2 * (w + 8),
                flightPanel.y + 58 + level / 2 * 52, w, 44);
        }

        void PartyRosterPointer(Vector2 point)
        {
            for (int slot = 0; slot < pendingPartyCount; slot++)
            {
                int corner = PartyCorner(slot, pendingPartyCount);
                if (PartyRosterRoleRect(corner).Contains(point))
                {
                    pendingPartySeats[corner].IsBot = !pendingPartySeats[corner].IsBot;
                    return;
                }
                if (pendingPartySeats[corner].IsBot && PartyRosterDifficultyRect(corner).Contains(point))
                {
                    partyLevelCorner = corner;
                    flightPage = PartyLevelPage;
                    BlockGameplayInput();
                    return;
                }
            }
        }

        void PartyLevelPointer(Vector2 point)
        {
            for (int level = 0; level < BotLevelNames.Length; level++)
                if (PartyRosterLevelRect(level).Contains(point))
                {
                    pendingPartySeats[partyLevelCorner].SelectedLevel = (BotDifficulty)level;
                    flightPage = PartyRosterPage;
                    BlockGameplayInput();
                    return;
                }
        }

        void DrawPartyRoster()
        {
            for (int corner = 0; corner < PartySeatCount; corner++)
            {
                int slot = 0;
                while (slot < pendingPartyCount && PartyCorner(slot, pendingPartyCount) != corner) slot++;
                Rect card = PartySeatRect(corner);
                if (slot == pendingPartyCount)
                {
                    Round(card, 6, Hex(0xf5f6ec));
                    Text(card, "Свободное место", 10, muted, false, TextAnchor.MiddleCenter);
                    continue;
                }

                var seat = pendingPartySeats[corner];
                Round(card, 6, pales[slot]);
                Text(new Rect(card.x + 8, card.y + 1, card.width - 16, 18), "Место " + (slot + 1), 10, accents[slot], true);
                FlightButton(PartyRosterRoleRect(corner), seat.IsBot ? "Бот" : "Человек", seat.IsBot, true, 12);
                if (seat.IsBot)
                    FlightButton(PartyRosterDifficultyRect(corner), BotLevelNames[(int)seat.Level] + " ›", false, true, 11);
            }
            DrawFlightApplyFooter();
        }

        void DrawPartyLevels()
        {
            var selected = pendingPartySeats[partyLevelCorner].Level;
            for (int level = 0; level < BotLevelNames.Length; level++)
                FlightButton(PartyRosterLevelRect(level), BotLevelNames[level], level == (int)selected, true, 12);
        }
    }
}
