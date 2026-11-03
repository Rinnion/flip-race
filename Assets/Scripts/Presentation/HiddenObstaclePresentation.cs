using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class CorridorArtwork
    {
        // Outline the unexpanded physical band, never the decorative texture padding.
        // An empty interior distinguishes the warning from a solid landing surface.
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
