using System.IO;
using FlipRace.Presentation;
using UnityEditor;
using UnityEngine;

namespace FlipRace.Editor
{
    public static class RockArtworkExport
    {
        [MenuItem("Flip Race/Export rock comparison")]
        public static void Export()
        {
            const int width = 752, height = 536;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            Color paper = new Color(250 / 255f, 247 / 255f, 237 / 255f);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = paper;
            for (int y = 236; y < 300; y++)
                for (int x = 0; x < width; x++)
                    pixels[(height - y - 1) * width + x] = new Color(217 / 255f, 226 / 255f, 199 / 255f);
            Blit(pixels, width, height, false, false, 64, 236);
            Blit(pixels, width, height, true, false, 280, 236);
            Blit(pixels, width, height, false, true, 64, 300);
            Blit(pixels, width, height, true, true, 280, 300);
            result.SetPixels(pixels);
            result.Apply();
            Directory.CreateDirectory("Verification");
            File.WriteAllBytes("Verification/tracks-rock-comparison.png", result.EncodeToPNG());
            Object.DestroyImmediate(result);
            Debug.Log("FLIP_ARTWORK_OK standard64x48 and slab200x92, floor/ceiling, same 2px/unit scale");
        }

        static void Blit(Color[] pixels, int width, int height, bool large, bool ceiling, float x, float surface)
        {
            var source = CorridorArtwork.CreateRockPreview(large, ceiling);
            float w = large ? 400 : 128, h = large ? 184 : 96;
            float dx = large ? 16 : w / 6, dy = large ? 16 : h / 6;
            var rect = new Rect(x - dx, (ceiling ? surface : surface - h) - dy, w + 2 * dx, h + 2 * dy);
            for (int py = Mathf.FloorToInt(rect.y); py < rect.yMax; py++)
                for (int px = Mathf.FloorToInt(rect.x); px < rect.xMax; px++)
                {
                    if (px < 0 || py < 0 || px >= width || py >= height) continue;
                    float u = (px + .5f - rect.x) / rect.width;
                    float v = (py + .5f - rect.y) / rect.height;
                    Color ink = source.GetPixelBilinear(u, ceiling ? v : 1 - v);
                    int index = (height - py - 1) * width + px;
                    pixels[index] = Color.Lerp(pixels[index], new Color(ink.r, ink.g, ink.b, 1), ink.a);
                }
            Object.DestroyImmediate(source);
        }
    }
}
