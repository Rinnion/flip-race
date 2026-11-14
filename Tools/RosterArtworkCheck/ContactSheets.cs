using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

// PNG writer follows the CPU-only Iter49Heroes verifier. PNG rows are top-first.
internal sealed class ContactSheets
{
    const int Size = 128, Cell = 138;
    readonly int width, height;
    readonly Color32[] pixels;

    ContactSheets(int columns, int rows)
    {
        width = columns * Cell;
        height = rows * Cell;
        pixels = new Color32[width * height];
        Array.Fill(pixels, new Color32(220, 215, 207, 255));
    }

    public static void Previews(string path, Texture2D[,] frames, Func<Texture2D, Color32[]> source)
    {
        var sheet = new ContactSheets(frames.GetLength(0), frames.GetLength(1));
        for (int hero = 0; hero < frames.GetLength(0); hero++)
        for (int slot = 0; slot < frames.GetLength(1); slot++)
            sheet.Tile(hero, slot, source(frames[hero, slot]), false);
        sheet.Write(path);
    }

    public static void Poses(string path, Texture2D[,] frames, Func<Texture2D, Color32[]> source)
    {
        var sheet = new ContactSheets(frames.GetLength(1) * 2, frames.GetLength(0));
        for (int slot = 0; slot < frames.GetLength(0); slot++)
        for (int pose = 0; pose < frames.GetLength(1); pose++)
        {
            var image = source(frames[slot, pose]);
            sheet.Tile(pose * 2, slot, image, false);
            sheet.Tile(pose * 2 + 1, slot, image, true);
        }
        sheet.Write(path);
    }

    void Tile(int column, int row, Color32[] image, bool upper)
    {
        // Shared field uses 180° at rest on the ceiling and no horizontal reflection.
        // Slightly darker upper tiles make paired orientations visible when zoomed out.
        var backdrop = upper ? new Color32(237, 230, 239, 255) : new Color32(249, 245, 233, 255);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int fromX = upper ? Size - 1 - x : x;
            int fromY = upper ? y : Size - 1 - y;
            var c = image[fromY * Size + fromX];
            int a = c.a, b = 255 - a;
            pixels[(row * Cell + 5 + y) * width + column * Cell + 5 + x] = new Color32(
                (byte)((c.r * a + backdrop.r * b + 127) / 255),
                (byte)((c.g * a + backdrop.g * b + 127) / 255),
                (byte)((c.b * a + backdrop.b * b + 127) / 255), 255);
        }
    }

    void Write(string path)
    {
        using var output = File.Create(path);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        Big(header, 0, (uint)width);
        Big(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR", header);
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, true))
        {
            var row = new byte[1 + width * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var c = pixels[y * width + x];
                    int at = 1 + x * 4;
                    row[at] = c.r;
                    row[at + 1] = c.g;
                    row[at + 2] = c.b;
                    row[at + 3] = c.a;
                }
                z.Write(row);
            }
        }
        Chunk(output, "IDAT", packed.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
    }

    static void Big(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    static void Chunk(Stream stream, string kind, byte[] data)
    {
        byte[] tag = Encoding.ASCII.GetBytes(kind), size = new byte[4];
        Big(size, 0, (uint)data.Length);
        stream.Write(size);
        stream.Write(tag);
        stream.Write(data);
        uint crc = 0xffffffff;
        foreach (byte b in tag.Concat(data))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
        }
        Big(size, 0, crc ^ 0xffffffff);
        stream.Write(size);
    }
}
