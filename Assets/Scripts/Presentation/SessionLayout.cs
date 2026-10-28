using System;
using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        public bool SessionCentered { get { return !Portrait && !(SharedEnabled && sharedAdjacent && !PartyEnabled && !SoloEnabled); } }
        public Rect SessionBarRect { get { return session; } }
        void PlaceSession(float sh, float dh, float lh)
        {
            if (!SessionCentered) return;
            // Reuse the old edge strip between the two sides; world height is unchanged.
            arena = new Rect(shell.x + 1, shell.y + 1, shell.width - 2, sh + lh * 2 + dh);
            lanes[0] = new Rect(arena.x, arena.y, arena.width, lh);
            session = new Rect(arena.x, arena.y + lh, arena.width, sh);
            divider = new Rect(arena.x, session.yMax, arena.width, dh);
            lanes[1] = new Rect(arena.x, divider.yMax, arena.width, lh);
            if (SharedEnabled)
            {
                session.y = arena.center.y - sh / 2;
                // A toolbar through the playable world would hide flight and obstacles.
                if (SoloEnabled || Game.Phase == "racing" || Game.Phase == "countdown") session.y = SharedFieldRect.y - sh;
            }
        }
        float DesktopBrandLeft { get { return Mathf.Max(shell.x, SafeUiArea().xMin + 80); } }
        float DesktopBrandRight { get { return Mathf.Min(shell.xMax, SafeUiArea().xMax - 80); } }
        float CommonFieldTop(float fieldHeight)
        {
            float centered = arena.center.y - fieldHeight / 2;
            if (!SessionCentered) return centered;
            Rect safe = SafeUiArea();
            float minimum = safe.yMin + 8 + 56 + session.height;
            float maximum = safe.yMax - 8 - 56 - fieldHeight;
            return minimum <= maximum ? Mathf.Clamp(centered, minimum, maximum) : centered;
        }
        float SharedCountdownCenter(int slot)
        {
            Rect region = ControlAreaRect(slot);
            bool upper = PartyEnabled ? PartyRotated(slot) : SharedRotated(slot);
            float physical = upper ? (region.yMin + session.yMin) / 2 : (SharedFieldRect.yMax + region.yMax) / 2;
            return upper ? region.yMax - physical : physical - region.yMin;
        }
        float SharedCountdownBandHeight(int slot)
        {
            Rect region = ControlAreaRect(slot);
            bool upper = PartyEnabled ? PartyRotated(slot) : SharedRotated(slot);
            return upper ? session.yMin - region.yMin : region.yMax - SharedFieldRect.yMax;
        }
        float PauseTitleY { get { return SessionCentered && !SharedEnabled ? session.y - 38 : arena.center.y - 30; } }
        float PauseSubtitleY { get { return SessionCentered && !SharedEnabled ? divider.yMax + 8 : arena.center.y + 5; } }
        float PauseIconSize
        {
            get
            {
                if (!SessionCentered || !SharedEnabled) return 40;
                float gap = PauseTitleY - session.yMax - 8;
                return gap < 24 ? 0 : Mathf.Min(40, gap);
            }
        }
        Rect SharedSideArea(int slot)
        {
            float half = (arena.height - session.height) / 2;
            return new Rect(arena.x, slot == 0 ? arena.y : arena.yMax - half, arena.width, half);
        }
        int PawCorner(int slot)
        {
            if (slot < 0 || slot >= Game.Players.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            return SoloEnabled ? 3 : PartyEnabled ? PartyCorner(slot) : SharedEnabled && sharedAdjacent ? slot + 2 : slot == 0 ? 0 : 3;
        }
        bool PointerPaw(Vector2 point)
        {
            if (!PawsVisible) return false;
            for (int slot = 0; slot < Game.Players.Length; slot++)
                if (PawScreenRect(slot).Contains(point)) { LocalRacePress(slot, point); return true; }
            return false;
        }
    }
}
