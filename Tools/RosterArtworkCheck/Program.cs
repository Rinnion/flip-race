using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Presentation;
using UnityEngine;

internal static class Program
{
    const int Size = 128, Slots = 4, Legacy = 6, Heroes = 10, Repeats = 10_000;
    const string Output = "Verification/v584-art";
    static readonly string[] Groups = { "Idle", "IdleBlink", "Run", "RunBlink", "RunSuccess", "Air", "AirBlink", "Recover", "Success", "Win" };
    static readonly string[] Names = { "Нико", "Игри", "Шедоу", "Пушинка" };
    static readonly string[] Slugs = { "69-niko", "68-igri", "48-shadow", "38-pushinka" };
    static readonly FieldInfo PixelStorage = typeof(Texture2D).GetField("pixels", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly List<object> Results = new();
    static readonly Texture2D[,] Previews = new Texture2D[Heroes, Slots];
    static int legacyPoses, newPoses, previews, legacyPreviews;
    static double maxColdPreviewMs, maxColdSlotMs;
    static int minMargin = Size, minFacePixels = Size * Size, minBlinkChangedPixels = Size * Size;

    static void Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--baseline") throw new ArgumentException("Use Tools/RosterArtworkCheck/Run.ps1.");
        var inputs = new[] { "Assets/Scripts/Presentation/MascotArtwork.cs", "Assets/Scripts/Presentation/CharacterRoster.cs",
            "Assets/Scripts/Core/RaceSimulation.cs", "Tools/RosterArtworkCheck/obj/Baseline.g.cs", "Tools/Iter49Heroes/UnityShim.cs" };
        var hashes = inputs.ToDictionary(p => p, FileHash);
        Texture2D.CapturePixels = true;
        Require(CharacterRoster.Count == Heroes, "Expected ten production entries.");
        Directory.CreateDirectory(Output);
        foreach (int hero in Enumerable.Range(Legacy, Heroes - Legacy).Concat(Enumerable.Range(0, Legacy)))
        {
            if (hero < Legacy)
                Require(CharacterRoster.Name(hero) == BaselineRoster.Name(hero), $"Legacy roster changed at {hero}.");
            else
                Require(CharacterRoster.Name(hero) == Names[hero - Legacy], $"Incorrect name at {hero}.");
            CheckHero(hero);
            Console.WriteLine($"Checked #{hero} {CharacterRoster.Name(hero)}.");
        }
        for (int slot = 0; slot < Slots; slot++)
            Require(Enumerable.Range(0, Heroes).Select(hero => Previews[hero, slot].GetHash()).Distinct().Count() == Heroes,
                $"Different heroes need distinct previews in slot {slot}.");
        for (int hero = Legacy; hero < Heroes; hero++)
            Require(Enumerable.Range(0, Slots).Select(slot => Previews[hero, slot].GetHash()).Distinct().Count() == Slots,
                $"Hero {hero} should retain each slot's color.");
        ContactSheets.Previews(Output + "/all-previews.png", Previews, Pixels);
        Require(hashes.All(entry => FileHash(entry.Key) == entry.Value), "An input changed during verification.");
        File.WriteAllText(Output + "/result.json", JsonSerializer.Serialize(new {
            Status = "PASS", BaselineRevision = args[1], Runtime = RuntimeInformation.FrameworkDescription,
            LegacyPosesPixelIdentical = legacyPoses, LegacyPreviewsPixelIdentical = legacyPreviews,
            NewPoses = newPoses, Previews = previews, Slots, Groups,
            NewMinTransparentMarginPixels = minMargin, NewMinOpaquePixelsInFaceWindow = minFacePixels, NewMinChangedPixelsInBlinkWindow = minBlinkChangedPixels,
            FirstPreviewMaxMs = maxColdPreviewMs, LazyFirstMotionPoseMaxMs = maxColdSlotMs,
            WarmCallsPerNewHeroSlot = Repeats, WarmBytesPerSample = 0, WarmNewTexturesPerSample = 0,
            Scope = "Production rasterizer under desktop CPU Unity shim; no browser, WebGL, GPU or phone timing. Image sheets show 180° at a shared-field ceiling.",
            InputSha256 = hashes, Results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {legacyPoses} old poses unchanged; {newPoses} new poses; {previews} previews in four slots. " +
            $"New transparent margin >= {minMargin}px, central face alpha >= {minFacePixels}px; desktop cold preview/motion max {maxColdPreviewMs:F2}/{maxColdSlotMs:F2} ms.");
    }

    static void CheckHero(int hero)
    {
        bool isNew = hero >= Legacy;
        var contact = new Texture2D[Slots, 7];
        for (int slot = 0; slot < Slots; slot++)
        {
            string tag = $"hero {hero} slot {slot}";
            int created = Texture2D.Created;
            long start = Stopwatch.GetTimestamp();
            var preview = MascotArtwork.Create(hero, slot);
            if (isNew) maxColdPreviewMs = Math.Max(maxColdPreviewMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Require(Texture2D.Created == created + 1 && ReferenceEquals(preview, MascotArtwork.Create(hero, slot)) &&
                Texture2D.Created == created + 1, tag + " preview must create and reuse exactly one texture.");
            Previews[hero, slot] = preview;
            previews++;
            if (isNew) CheckBounds(preview, tag + " Preview");
            else
            {
                Require(preview.GetHash() == BaselineMascotArtwork.Create(hero, slot).GetHash(), tag + " legacy preview changed.");
                legacyPreviews++;
            }

            Texture2D first = null;
            if (isNew)
            {
                created = Texture2D.Created;
                MascotArtwork.WarmUp(hero, slot);
                Require(Texture2D.Created == created, tag + " warm up must leave animation textures empty.");
                start = Stopwatch.GetTimestamp();
                first = Motion(hero, slot, 0.11f, true, false, false);
                maxColdSlotMs = Math.Max(maxColdSlotMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                Require(Texture2D.Created == created + 1, tag + " first motion call must rasterize only one texture.");
                WarmMotion(hero, slot, first, tag);
            }
            var current = FrameSet.Load(typeof(MascotArtwork), hero, slot);
            var baseline = isNew ? null : FrameSet.Load(typeof(BaselineMascotArtwork), hero, slot);
            var sampled = new Dictionary<string, (Texture2D Frame, string Hash)>();
            var summaries = new List<object>();
            foreach (string group in Groups)
            {
                Texture2D[] array = current.Group(group);
                if (!isNew) Require(baseline.Group(group).Length == array.Length, tag + " changed phase count for " + group);
                var seen = new HashSet<string>();
                var hashes = new List<string>();
                for (int i = 0; i < array.Length; i++)
                {
                    var frame = current.Pose(array, i);
                    string hash = frame.GetHash();
                    if (isNew)
                    {
                        CheckBounds(frame, $"{tag} {group}[{i}]");
                        newPoses++;
                    }
                    else
                    {
                        Require(hash == baseline.Pose(baseline.Group(group), i).GetHash(), $"{tag} {group}[{i}] pixels changed.");
                        legacyPoses++;
                    }
                    seen.Add(hash);
                    hashes.Add(hash);
                    int representative = group == "Run" ? 12 : group == "Air" ? 6 : 0;
                    if (i == representative) sampled[group] = (frame, hash);
                }
                if (isNew) Require(seen.Count >= 3, tag + " static " + group);
                summaries.Add(new { Group = group, Frames = hashes.Count, Unique = seen.Count });
            }
            CheckPublicSelectors(hero, slot, current, isNew, tag);
            if (isNew)
            {
                string[] sheetGroups = { "Idle", "IdleBlink", "Run", "Air", "Recover", "RunSuccess", "Win" };
                Require(sheetGroups.Select(g => sampled[g].Hash).Distinct().Count() == sheetGroups.Length, tag + " repeated pose across groups.");
                for (int col = 0; col < sheetGroups.Length; col++) contact[slot, col] = sampled[sheetGroups[col]].Frame;
                CheckBlink(contact[slot, 0], contact[slot, 1], tag);
            }
            Results.Add(new { Hero = hero, Name = CharacterRoster.Name(hero), Slot = slot + 1,
                PreviewSha256 = preview.GetHash(), Poses = summaries });
        }
        if (isNew) ContactSheets.Poses($"{Output}/{Slugs[hero - Legacy]}-poses.png", contact, Pixels);
    }

    static Texture2D Motion(int hero, int slot, float motion, bool run, bool air, bool hit)
        => MascotArtwork.GetMotionFrame(hero, slot, 0, motion, 0, run, air, hit);

    static void WarmMotion(int hero, int slot, Texture2D expected, string tag)
    {
        for (int i = 0; i < 128; i++) Motion(hero, slot, 0.11f, true, false, false);
        int count = Texture2D.Created;
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool same = true;
        for (int i = 0; i < Repeats; i++)
            same &= ReferenceEquals(expected, Motion(hero, slot, 0.11f, true, false, false));
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(same && Texture2D.Created == count && bytes == 0, $"{tag} warm retrieval: allocated {bytes} bytes or changed texture.");
    }

    static void CheckPublicSelectors(int hero, int slot, FrameSet set, bool isNew, string tag)
    {
        if (!isNew)
        {
            for (int i = 0; i < 64; i++)
            {
                float time = i * .137f, motion = i * .071f, progress = (i % 23) / 22f, age = i * .053f;
                bool run = (i & 1) != 0, air = i % 3 == 0, hit = i % 11 == 0;
                MascotReaction reaction = (MascotReaction)(i % 4);
                Require(MascotArtwork.GetMotionFrame(hero, slot, time, motion, progress, run, air, hit, reaction, age).GetHash() ==
                    BaselineMascotArtwork.GetMotionFrame(hero, slot, time, motion, progress, run, air, hit, reaction, age).GetHash(),
                    tag + " changed legacy public motion pose.");
            }
            return;
        }
        float blink = (MathF.PI / 2 + MathF.PI * 2 - hero * .71f - slot * .29f) / 1.71f;
        float open = (MathF.PI * 2 - hero * .71f - slot * .29f) / 1.71f;
        foreach (var entry in new[] {
            ("Idle",open,false,false,false,MascotReaction.None), ("IdleBlink",blink,false,false,false,MascotReaction.None),
            ("Run",open,true,false,false,MascotReaction.None), ("RunBlink",blink,true,false,false,MascotReaction.None),
            ("Air",open,true,true,false,MascotReaction.None), ("AirBlink",blink,true,true,false,MascotReaction.None),
            ("Recover",open,true,false,true,MascotReaction.Hit), ("RunSuccess",open,true,false,false,MascotReaction.Success),
            ("Win",open,false,false,false,MascotReaction.Win) })
        {
            var (group,time,run,air,hit,reaction) = entry;
            var frame = MascotArtwork.GetMotionFrame(hero, slot, time, .34f, .5f, run, air, hit, reaction, .24f);
            Require(set.Group(group).Contains(frame), tag + " route for " + group);
        }
        Require(set.Group("Success").Contains(MascotArtwork.GetReactionFrame(hero, slot, 0, MascotReaction.Success)),
            tag + " route for standing success.");
    }

    static void CheckBounds(Texture2D frame, string tag)
    {
        var pixels = Pixels(frame);
        int alpha = 0, face = 0, margin = Size;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            byte a = pixels[y * Size + x].a;
            if (a == 0) continue;
            margin = Math.Min(margin, Math.Min(Math.Min(x, Size - 1 - x), Math.Min(y, Size - 1 - y)));
            if (a == 255)
            {
                alpha++;
                // Fixed central-face window in texture space: x ±0.56, y -0.58..-0.04 in GUI radius units.
                if (x >= 40 && x < 88 && y >= 54 && y < 78) face++;
            }
        }
        Require(margin >= 1 && alpha >= 1500 && face >= 650, $"{tag}: margin={margin}, alpha={alpha}, face={face}.");
        minMargin = Math.Min(minMargin, margin);
        minFacePixels = Math.Min(minFacePixels, face);
    }

    static void CheckBlink(Texture2D idle, Texture2D closed, string tag)
    {
        var a = Pixels(idle);
        var b = Pixels(closed);
        int changed = 0;
        // Left eye (including on Igri's mask-free side), around x=-.47 and y=-.36..-.09.
        for (int y = 56; y <= 72; y++)
        for (int x = 35; x <= 53; x++)
        {
            var p = a[y * Size + x];
            var q = b[y * Size + x];
            if (Math.Abs(p.r - q.r) + Math.Abs(p.g - q.g) + Math.Abs(p.b - q.b) >= 50) changed++;
        }
        Require(changed >= 10, tag + " left eye blink too small: " + changed);
        minBlinkChangedPixels = Math.Min(minBlinkChangedPixels, changed);
    }

    static Color32[] Pixels(Texture2D texture) => (Color32[])PixelStorage.GetValue(texture);
    static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    sealed class FrameSet
    {
        readonly object data;
        readonly MethodInfo pose;
        FrameSet(object data, MethodInfo pose) { this.data = data; this.pose = pose; }
        public static FrameSet Load(Type type, int hero, int slot)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            return new FrameSet(type.GetMethod("Ensure", flags).Invoke(null, new object[] { hero, slot }), type.GetMethod("Pose", flags));
        }
        public Texture2D[] Group(string name) => (Texture2D[])data.GetType().GetField(name).GetValue(data);
        public Texture2D Pose(Texture2D[] group, int i) => (Texture2D)pose.Invoke(null, new object[] { data, group, i });
    }
}
