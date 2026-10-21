using System;
using FlipRace.Core;
using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        // Session presentation choices: never included in FlightSettings or Core.
        bool sharedField = false, sharedAdjacent = false, sharedFullHeight = false;
        public bool SharedEnabled { get { return sharedField && Trial; } }
        public string FieldViewName { get { return SharedEnabled ? "shared" : "split"; } }
        public string SeatingName { get { return SharedEnabled && sharedAdjacent ? "adjacent" : "opposite"; } }
        public string FieldHeightName { get { return SharedEnabled && sharedFullHeight ? "full" : "half"; } }
        public Rect SharedFieldRect
        {
            get { return new Rect(arena.x, arena.center.y - (sharedFullHeight ? arena.height : lanes[0].height) / 2,
                arena.width, sharedFullHeight ? arena.height : lanes[0].height); }
        }
        public Rect ControlAreaRect(int slot)
        {
            if (!SharedEnabled) return lanes[slot];
            return sharedAdjacent ? new Rect(arena.x + slot * arena.width / 2, arena.y, arena.width / 2, arena.height)
                : new Rect(arena.x, arena.y + slot * arena.height / 2, arena.width, arena.height / 2);
        }
        bool SharedRotated(int slot) { return !sharedAdjacent && slot == 0; }
        Rect SharedScreenRect(int slot, Rect local)
        {
            Rect r = ControlAreaRect(slot);
            return SharedRotated(slot) ? new Rect(r.xMax - local.xMax, r.yMax - local.yMax, local.width, local.height)
                : new Rect(r.x + local.x, r.y + local.y, local.width, local.height);
        }
        Rect SharedCharacterRect(int slot, int hero)
        {
            Rect r = ControlAreaRect(slot);
            if (!sharedAdjacent) return CharacterRect(hero, r.width, r.height);
            float cw = Mathf.Min((r.width - 36) / 2, 176), ch = compact ? 44 : 64;
            float x = (r.width - cw * 2 - 8) / 2, y = Mathf.Max(36, (r.height - (ch * 2 + 60)) / 2);
            return new Rect(x + hero % 2 * (cw + 8), y + hero / 2 * (ch + 8), cw, ch);
        }
        Rect SharedReadyRect(int slot)
        {
            Rect r = ControlAreaRect(slot);
            if (!sharedAdjacent) return ReadyButtonRect(r.width, r.height);
            Rect last = SharedCharacterRect(slot, 3);
            float rw = Mathf.Min(r.width - 32, compact ? 190 : 220);
            return new Rect((r.width - rw) / 2, last.yMax + 8, rw, compact ? 32 : 38);
        }
        void SharedPointer(Vector2 point)
        {
            if (!arena.Contains(point)) return;
            int slot = sharedAdjacent ? (point.x < arena.center.x ? 0 : 1) : (point.y < arena.center.y ? 0 : 1);
            if (Game.Phase == "ready")
            {
                for (int hero = 0; hero < 4; hero++)
                    if (CharacterScreenRect(slot, hero).Contains(point)) { SelectCharacter(slot, hero); return; }
                if (ReadyScreenRect(slot).Contains(point)) Act(slot);
                return;
            }
            Act(slot);
        }
        void DrawSharedField()
        {
            Rect field = SharedFieldRect;
            Matrix4x4 canvas = GUI.matrix;
            GUI.matrix = canvas * Matrix4x4.Translate(new Vector3(field.x, field.y, 0));
            var shared = artwork.DrawSharedWorld(field.width, field.height, Game, animationTime);
            var a = shared.Player(0); var b = shared.Player(1);
            bool overlap = Mathf.Abs(a.X - b.X) < Mathf.Max(a.Radius, b.Radius) * 1.2f
                && Mathf.Abs(a.Y - b.Y) < Mathf.Max(a.Radius, b.Radius) * 1.2f;
            for (int slot = 0; slot < 2; slot++)
            {
                var g = shared.Player(slot);
                if (!shared.View.IsPlayerVisible(slot)) continue;
                // Both origins remain the exact physical projection. At a full
                // same-pose overlap, two texture halves expose both identities.
                bool samePose = Mathf.Abs(a.X - b.X) < .5f && Mathf.Abs(a.Y - b.Y) < .5f
                    && Mathf.Abs(Mathf.DeltaAngle(a.Rotation, b.Rotation)) < 1;
                DrawSharedMascot(slot, g, samePose, overlap && !samePose);
                artwork.DrawSharedPersonalForeground(field.width, field.height, Game, slot, animationTime, g);
            }
            GUI.matrix = canvas;
            if (Game.Phase == "racing")
                for (int slot = 0; slot < 2; slot++)
                    if (!shared.View.IsPlayerVisible(slot) && !Game.Players[slot].FinishTime.HasValue)
                    {
                        // This pointer belongs to the common world, so its left
                        // direction is not inverted with the upper player's UI.
                        Rect edge = new Rect(field.x + 4,
                            Mathf.Clamp(field.y + (float)shared.View.PlayerY(slot) - 11, field.y + 4, field.yMax - 26), 126, 22);
                        Round(edge, 5, new Color(.98f, .98f, .94f, .94f));
                        artwork.DrawPersonalBadge(edge.x + 21, edge.center.y, slot, 4);
                        Text(new Rect(edge.x + 4, edge.y, 13, edge.height), "←", 12, accents[slot], true);
                        Text(new Rect(edge.x + 31, edge.y, 91, edge.height), Number(shared.View.Gap, 0) + " ед позади", 9, accents[slot], true, TextAnchor.MiddleLeft);
                    }
            // Personal UI uses control regions, never the cropped world bounds.
            for (int slot = 0; slot < 2; slot++)
            {
                Rect region = ControlAreaRect(slot);
                GUI.matrix = canvas * (SharedRotated(slot)
                    ? Matrix4x4.TRS(new Vector3(region.xMax, region.yMax, 0), Quaternion.Euler(0, 0, 180), Vector3.one)
                    : Matrix4x4.Translate(new Vector3(region.x, region.y, 0)));
                if (Game.Phase != "racing")
                {
                    if (Game.Phase == "ready") DrawSharedSelection(slot, region.width, region.height);
                    else DrawOverlay(slot, region.width, region.height);
                }
                DrawSharedStatus(slot, region.width, region.height, shared.View.IsPlayerVisible(slot), shared.View.Gap);
            }
            GUI.matrix = canvas;
            if (sharedAdjacent) Fill(new Rect(arena.center.x - .5f, arena.y, 1, 12), Hex(0xb7beaa));
            else { Fill(new Rect(arena.x, arena.center.y - .5f, 10, 1), Hex(0xb7beaa)); Fill(new Rect(arena.xMax - 10, arena.center.y - .5f, 10, 1), Hex(0xb7beaa)); }
        }
        void DrawSharedMascot(int slot, CorridorArtwork.Geometry g, bool splitTexture, bool translucent)
        {
            PlayerState p = Game.Players[slot];
            Matrix4x4 field = GUI.matrix;
            GUI.matrix = field * Matrix4x4.TRS(new Vector3(g.X, g.Y, 0), Quaternion.Euler(0, 0, g.Rotation), Vector3.one);
            MascotReaction reaction = MascotReaction.None; float age = 0;
            if (p.Recovering > 0) reaction = MascotReaction.Hit;
            else if (Game.Phase == "finished" && Game.Winner is int && (int)Game.Winner == slot && animationTime - winnerReactionAt < 1.2f)
            { reaction = MascotReaction.Win; age = animationTime - winnerReactionAt; }
            else if (p.LastFinishBoost != null && Game.Elapsed - p.LastFinishBoost.At < .65)
            { reaction = MascotReaction.Success; age = (float)(Game.Elapsed - p.LastFinishBoost.At); }
            else if (p.LastSuccess != null && Game.Elapsed - p.LastSuccess.At < .65)
            { reaction = MascotReaction.Success; age = (float)(Game.Elapsed - p.LastSuccess.At); }
            var texture = MascotArtwork.GetMotionFrame(p.CharacterIndex, slot, animationTime, g.MotionTime, g.FlipProgress,
                g.Running, p.Flipping, g.Recovering, reaction, age);
            Rect unit = MascotArtwork.UnitBounds;
            Rect target = new Rect(unit.x * g.Radius, unit.y * g.Radius, unit.width * g.Radius, unit.height * g.Radius);
            GUI.color = translucent ? new Color(1, 1, 1, .72f) : Color.white;
            if (splitTexture)
            {
                target.x += slot * target.width / 2; target.width /= 2;
                GUI.DrawTextureWithTexCoords(target, texture, new Rect(slot * .5f, 0, .5f, 1));
            }
            else GUI.DrawTexture(target, texture);
            GUI.color = Color.white; GUI.matrix = field;
        }
        void DrawSharedSelection(int slot, float w, float h)
        {
            GUI.color = Color.white; GUI.DrawTexture(new Rect(0, 0, w, h), veil);
            for (int hero = 0; hero < 4; hero++)
            {
                Rect card = SharedCharacterRect(slot, hero); bool chosen = Game.Players[slot].CharacterIndex == hero;
                StrokeRound(card, 7, chosen ? accents[slot] : Hex(0xd9dece), chosen ? pales[slot] : Hex(0xf5f6ec));
                float s = card.height - 4;
                GUI.color = Color.white; GUI.DrawTexture(new Rect(card.x + 2, card.y + 2, s, s), MascotArtwork.Create(hero, slot));
                Text(new Rect(card.x + s + 3, card.y, card.width - s - 6, card.height), CharacterRoster.Name(hero), compact ? 9 : 11, ink, chosen, TextAnchor.MiddleCenter);
            }
            Rect ready = SharedReadyRect(slot);
            Round(ready, 7, Game.Players[slot].Ready ? Hex(0xdbe7ca) : pales[slot]);
            Text(ready, Game.Players[slot].Ready ? "Готов · ждём второго" : "Готов →", compact ? 10 : 12, accents[slot], true, TextAnchor.MiddleCenter);
        }
        void DrawSharedStatus(int slot, float w, float h, bool visible, double gap)
        {
            var p = Game.Players[slot];
            float statusHeight = compact || sharedFullHeight ? 22 : 27;
            float inset = sharedFullHeight ? 2 : 4;
            float statusY = !sharedAdjacent && Game.Phase == "racing" ? h - statusHeight - inset : inset;
            Rect status = new Rect(8, statusY, Mathf.Min(w - 16, 250), statusHeight);
            Round(status, 6, new Color(.98f, .98f, .94f, .94f));
            artwork.DrawPersonalBadge(18, status.center.y, slot, 5);
            string name = "Игрок " + (slot + 1) + " · " + (slot == 0 ? "A" : "L");
            string text = p.FinishTime.HasValue ? name + " · " + Number(p.FinishTime.Value, 2) + " с"
                : !visible && Game.Phase == "racing" ? name + " · вне поля · " + Number(gap, 0) + " ед"
                : Game.Phase == "ready" ? name + (sharedAdjacent ? (slot == 0 ? " · слева" : " · справа") : (slot == 0 ? " · сверху" : " · снизу"))
                : name + " · +" + Rewards(p) + " · −" + p.SlowsEarned + " · ударов " + p.Collisions;
            Text(new Rect(29, status.y, status.width - 25, status.height), text, compact ? 8 : 10, accents[slot], true, TextAnchor.MiddleLeft);
        }
    }
}
