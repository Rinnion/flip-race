using UnityEngine;
using FlipRace.Core;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        const int CharactersPerPage = 5;
        const float CharacterCardGap = 4, MinWideCardWidth = 80;
        int[] characterPages;
        GameState characterPageGame;
        bool characterPagesWereVisible;

        readonly struct CharacterGrid
        {
            readonly Rect area;
            readonly int columns;
            readonly float cardWidth;

            public CharacterGrid(Rect area, float maxCardWidth = 176)
            {
                this.area = area;
                columns = (area.width - 4 * CharacterCardGap) / 5 >= MinWideCardWidth ? 5 : 3;
                cardWidth = Mathf.Min(maxCardWidth, (area.width - (columns - 1) * CharacterCardGap) / columns);
            }

            public bool Paged => columns == 3;
            public int First(int page) => Paged ? page * CharactersPerPage : 0;
            public int Count => Paged ? CharactersPerPage : CharacterRoster.Count;

            public Rect Cell(int index)
            {
                float totalWidth = columns * cardWidth + (columns - 1) * CharacterCardGap;
                return new Rect(area.center.x - totalWidth / 2 + index % columns * (cardWidth + CharacterCardGap),
                    area.y + index / columns * (area.height + CharacterCardGap), cardWidth, area.height);
            }
        }

        void SyncCharacterPages()
        {
            bool visible = CharacterSelectionVisible;
            if (characterPages == null) characterPages = new int[4];
            if (visible && (!characterPagesWereVisible || characterPageGame != Game))
                for (int slot = 0; slot < Game.Players.Length; slot++)
                    characterPages[slot] = Game.Players[slot].CharacterIndex / CharactersPerPage;
            characterPageGame = Game;
            characterPagesWereVisible = visible;
        }

        CharacterGrid LaneCharacterGrid(float w, float h)
        {
            float cardHeight = compact ? 44 : 56;
            float listBottom = ReadyButtonRect(w, h).y - 4;
            float y = Mathf.Max(4, (listBottom - (2 * cardHeight + CharacterCardGap)) / 2);
            return new CharacterGrid(new Rect(8, y, w - 16, cardHeight));
        }

        CharacterGrid PartyCharacterGrid(int slot)
        {
            Rect r = PartyControlArea(slot);
            float y = Mathf.Max(3, (r.height - 44 - 4 - (2 * 44 + CharacterCardGap)) / 2);
            return new CharacterGrid(new Rect(3, y, r.width - 6, 44), float.PositiveInfinity);
        }

        CharacterGrid SoloCharacterGrid()
        {
            Rect r = SoloOverlayRect;
            float w = Mathf.Min(r.width - 16, 560);
            return new CharacterGrid(new Rect(r.center.x - w / 2, SoloContentTop(170) + 22, w, 44));
        }

        CharacterGrid BotCharacterGrid()
        {
            Rect r = BotPersonalArea(0);
            float w = Mathf.Min(r.width - 16, 560);
            return new CharacterGrid(new Rect(r.center.x - w / 2, r.y + Mathf.Max(0, (r.height - 140) / 2), w, 44));
        }

        CharacterGrid CharacterGridFor(int slot)
        {
            if (BotEnabled) return BotCharacterGrid();
            if (SoloEnabled) return SoloCharacterGrid();
            if (PartyEnabled) return PartyCharacterGrid(slot);
            Rect area = ControlAreaRect(slot);
            return LaneCharacterGrid(area.width, area.height);
        }

        Rect CharacterCardRect(int slot, int hero, CharacterGrid grid)
        {
            int index = hero - grid.First(characterPages[slot]);
            return index >= 0 && index < grid.Count ? grid.Cell(index) : default;
        }

        Rect CharacterSelectionScreenRect(int slot, Rect rect)
        {
            if (rect.width <= 0 || BotEnabled || SoloEnabled) return rect;
            if (PartyEnabled) return PartyScreenRect(slot, rect);
            return SharedEnabled ? SharedScreenRect(slot, rect) : LaneScreenRect(slot, rect);
        }

        public Rect CharacterPageScreenRect(int slot)
        {
            if (!CharacterSelectionVisible || slot < 0 || slot >= Game.Players.Length || (BotEnabled && slot != 0)) return default;
            CharacterGrid grid = CharacterGridFor(slot);
            return grid.Paged ? CharacterSelectionScreenRect(slot, grid.Cell(CharactersPerPage)) : default;
        }

        bool CharacterPickerPointer(int slot, Vector2 point)
        {
            CharacterGrid grid = CharacterGridFor(slot);
            if (grid.Paged && CharacterSelectionScreenRect(slot, grid.Cell(CharactersPerPage)).Contains(point))
            {
                characterPages[slot] = 1 - characterPages[slot];
                Beep(tapClip);
                return true;
            }
            int first = grid.First(characterPages[slot]);
            for (int index = 0; index < grid.Count; index++)
            {
                if (!CharacterSelectionScreenRect(slot, grid.Cell(index)).Contains(point)) continue;
                SelectCharacter(slot, first + index);
                return true;
            }
            return false;
        }

        void DrawCharacterPicker(int slot, CharacterGrid grid, int fontSize, bool showCheck = false)
        {
            int page = characterPages[slot], first = grid.First(page);
            for (int index = 0; index < grid.Count; index++)
            {
                int hero = first + index;
                Rect card = grid.Cell(index);
                bool chosen = Game.Players[slot].CharacterIndex == hero;
                StrokeRound(card, 7, chosen ? accents[slot] : Hex(0xd9dece), chosen ? pales[slot] : Hex(0xf5f6ec));
                float size = Mathf.Min(card.height - 4, card.width * .40f);
                if (card.width >= MinWideCardWidth) size = Mathf.Min(size, card.width - 50);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(card.x + 2, card.center.y - size / 2, size, size), MascotArtwork.Create(hero, slot));
                string name = CharacterRoster.Name(hero);
                Rect label = new Rect(card.x + size + 4, card.y, card.width - size - 6, card.height);
                int nameSize = fontSize;
                while (nameSize > 9 && TextWidth(name, nameSize, chosen) > label.width) nameSize--;
                Text(label, name, nameSize,
                    chosen ? accents[slot] : ink, chosen, TextAnchor.MiddleCenter);
                if (chosen && showCheck)
                    Text(new Rect(card.xMax - 15, card.y + 2, 13, 13), "✓", 10, accents[slot], true, TextAnchor.MiddleCenter);
            }
            if (!grid.Paged) return;
            Rect pager = grid.Cell(CharactersPerPage);
            StrokeRound(pager, 7, accents[slot], paper);
            Text(pager, page == 0 ? "Ещё ›  1/2" : "‹ Назад  2/2", 11, accents[slot], true, TextAnchor.MiddleCenter);
        }
    }
}
