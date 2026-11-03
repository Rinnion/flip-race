using UnityEngine;
using FlipRace.Core;

namespace FlipRace.Presentation
{
    public sealed partial class CorridorArtwork
    {
        // Outline the unexpanded physical band, never the decorative texture padding.
        // The translucent texture and absent landing marks distinguish a warning
        // from solid geometry, including postponed activation at full progress.
        void HiddenObstacleWarning(GameState game, Obstacle obstacle, float x, float y, float width, float height)
        {
            Color previous = GUI.color;
            float progress = Mathf.Clamp01((float)HiddenObstacles.WarningProgress(game, obstacle));
            float opacity = .12f + .56f * (progress * progress * (3 - 2 * progress));
            bool ceiling = obstacle.Kind == "ceiling";
            Rock(x, width, height, ceiling ? y : y + height, ceiling, false, false, obstacle.Width >= 160, opacity);
            HiddenObstacleOutline(x, y, width, height);
            GUI.color = previous;
        }

        void HiddenObstacleOutline(float x, float y, float width, float height)
        {
            Color ink = Hex(0x898591);
            Line(x, y, x + width, y, ink, 2);
            Line(x + width, y, x + width, y + height, ink, 2);
            Line(x + width, y + height, x, y + height, ink, 2);
            Line(x, y + height, x, y, ink, 2);
        }
    }
}
