using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class CorridorArtwork
    {
        // Wide layered sandstone, authored at its own aspect ratio. Its broad
        // horizontal ledge and three facets remain readable at mobile scale.
        // This family is generated once per renderer, alongside the small rock.
        static Texture2D MakeLargeRock(bool ceiling, bool readable = false)
        {
            var p = new Painter(216, 108);
            Color baseColor = ceiling ? Hex(0x99aaa7) : Hex(0xa2aaa5);
            Color outline = ceiling ? Hex(0x788e88) : Hex(0x838f82);
            float[] silhouette = { 8, 100, 8, 22, 15, 8, 199, 8, 208, 22, 208, 100 };
            p.Polygon(baseColor, silhouette);
            p.Polygon(Alpha(Color.white, .19f), 16, 10, 86, 10, 70, 40, 10, 30);
            p.Polygon(Alpha(Color.white, .11f), 88, 10, 155, 10, 174, 43, 127, 62, 72, 40);
            p.Polygon(Alpha(Hex(0x667f78), .15f), 157, 10, 198, 10, 206, 23, 206, 97, 183, 76, 174, 43);
            p.Polygon(Alpha(Hex(0x778b7b), .12f), 10, 64, 69, 52, 126, 68, 182, 62, 206, 78, 206, 97, 10, 97);
            Color crack = Alpha(Hex(0x718477), .48f);
            p.Line(88, 12, 72, 40, crack, 2.2f);
            p.Line(72, 40, 83, 59, crack, 2.2f);
            p.Line(83, 59, 67, 75, crack, 1.8f);
            p.Line(156, 12, 173, 43, crack, 2.2f);
            p.Line(173, 43, 151, 58, crack, 2.2f);
            p.Line(173, 43, 186, 72, crack, 1.8f);
            p.Line(19, 65, 47, 59, Alpha(Color.white, .23f), 2);
            p.Line(103, 78, 134, 80, Alpha(Color.white, .22f), 2);
            for (int i = 0; i < silhouette.Length; i += 2)
            {
                int next = (i + 2) % silhouette.Length;
                p.Line(silhouette[i], silhouette[i + 1], silhouette[next], silhouette[next + 1], outline, 2);
            }
            // Flat usable ledge; no painted peak changes the apparent height.
            p.Line(16, 10, 198, 10, Alpha(Hex(0xe4ebd6), .72f), 2);
            p.Polygon(Hex(0xb3c38f), 10, 92, 49, 94, 93, 91, 133, 95, 175, 92, 206, 94, 206, 100, 10, 100);
            return p.Finish(ceiling ? "Ceiling wide slab" : "Floor wide slab", readable);
        }

        // Editor exports use the same source as the cached runtime textures.
        public static Texture2D CreateRockPreview(bool large, bool ceiling)
        {
            return large ? MakeLargeRock(ceiling, true) : MakeRock(ceiling, true);
        }
    }
}
