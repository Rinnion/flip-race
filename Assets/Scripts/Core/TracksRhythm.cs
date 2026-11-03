using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    // The short course is kept separate from the full races so the new
    // passage can be tried without changing any of the existing geometry.
    public static partial class TracksRhythm
    {
        public const string PracticeId = "rhythm-practice";
        public const double GateWidth = 16, GateOffset = 112;
        public const double MainAirDelay = .2;
        public const double StraightX = 3800, LowX = 4300;
        public static readonly Track[] All = { Draft(CascadeId) };
        public static bool IsFull(string id) { foreach (var t in All) if (t.Id == id) return true; return false; }
        public static Track Get(string id) { id = RaceTracks.NormalizeId(id); foreach (var t in All) if (t.Id == id) return t; return Practice(); }

        public static Obstacle Passage(string id, double x, bool fromFloor)
        {
            return new Obstacle { Id = id, Kind = "gate", X = x, Width = GateWidth,
                GapTop = 110, GapBottom = 130,
                TopOffsetX = fromFloor ? 0 : GateOffset,
                BottomOffsetX = fromFloor ? GateOffset : 0 };
        }

        public static Track Practice()
        {
            double[] positions = { 300, 900, 1500, 1860, 2230, 2590, 2960, 3320 };
            var obstacles = new Obstacle[positions.Length + 2];
            for (int i = 0; i < positions.Length; i++)
                obstacles[i] = Passage("rhythm-practice-gate-" + i, positions[i], i % 2 == 0);
            obstacles[8] = new Obstacle { Id = "rhythm-practice-straight", Kind = "gate",
                X = StraightX, Width = 8, GapTop = 12, GapBottom = 228 };
            obstacles[9] = LowPassage("rhythm-practice-low", LowX, true);
            return new Track(4600, obstacles, new[] {
                new Cue(150, "С пола — вверх"), new Cue(730, "С потолка — вниз"),
                new Cue(1320, "Шесть переходов подряд"), new Cue(3620, "Прямой проход"),
                new Cue(4120, "Низкий порог и возврат") }) {
                Id = PracticeId, Name = "Проба ворот",
                Focus = "Два входа, связка из шести, прямой проход и низкий порог",
                SafeRoute = PracticeRoute(FlightPresets.Chosen),
                RiskRoute = PracticeRoute(FlightPresets.Chosen)
            };
        }

        public static Obstacle LowPassage(string id, double x, bool floor)
        {
            return new Obstacle { Id = id, Kind = "gate", X = x, Width = 16,
                GapTop = floor ? 114 : 16, GapBottom = floor ? 224 : 126,
                TopOffsetX = floor ? 80 : 0, BottomOffsetX = floor ? 0 : 80 };
        }

        // Authoring witness only. The playable course never reads these arrays.
        public static double[] PracticeRoute(FlightSettings flight)
        { return PracticeWitness(flight, false); }

        // A separate optional witness on exactly the same physical course.
        // Fast: one real wall setup, then departure + one airborne return.
        // Main: natural jump replaced by two independent air corrections.
        public static double[] PracticeQuickReturnRoute(FlightSettings flight)
        { return PracticeWitness(flight, true); }

        static double[] PracticeWitness(FlightSettings flight, bool quickReturn)
        {
            bool fast = flight.FirstAction == FirstAction.Flip;
            // Midpoints of the verified joint +/-60ms launch/repeat window.
            // Main's two inputs vary independently; this is not the midpoint
            // of the narrower fixed-repeat-time envelope.
            double relative = fast ? 5.1503213562917 : -19.1551226858691;
            var route = new List<double>();
            foreach (double x in new double[] { 300, 900, 1500, 1860, 2230, 2590, 2960, 3320 }) {
                route.Add(x + relative);
                if (!fast) route.Add(x + relative + 220 * MainAirDelay);
            }
            route.Add(fast ? StraightX + 4 - 110 * (FastTime(12) + FastTime(200))
                : StraightX + 4 - 220 * 350 / 662.5);
            if (!quickReturn) route.Add(LowX + (fast ? -23.0829351839127 : -108.2264150943396));
            else if (fast) {
                route.Add(4000); // Actual ceiling-to-floor landing before the low hop.
                route.Add(LowX - 50); route.Add(LowX - 17);
            }
            else {
                route.Add(LowX - 242); route.Add(LowX - 198); route.Add(LowX - 176);
            }
            return route.ToArray();
        }
        static double FastTime(double displacement)
        { return (Math.Sqrt(35 * 35 + 5400 * displacement) - 35) / 2700; }
        static double MainTime(double displacement)
        {
            double d = 350 * MainAirDelay - .5 * 662.5 * MainAirDelay * MainAirDelay;
            double v = 350 - 662.5 * MainAirDelay;
            if (displacement <= d) return (350 - Math.Sqrt(350 * 350 - 1325 * displacement)) / 662.5;
            return MainAirDelay + (Math.Sqrt(v * v + 1325 * (displacement - d)) - v) / 662.5;
        }
    }

    public static class RaceTracks
    {
        public static readonly Track[] All = Combined();
        static Track[] Combined()
        {
#if FLIP_MIX_STAGE
            return TracksMixedStage.All;
#else
            int originalCount = TracksFull.All.Length + TracksRhythm.All.Length + 1;
            var result = new Track[originalCount + TracksHidden.All.Length + 1];
            Array.Copy(TracksFull.All, result, TracksFull.All.Length);
            Array.Copy(TracksRhythm.All, 0, result, TracksFull.All.Length, TracksRhythm.All.Length);
            result[originalCount - 1] = TracksMixed.Get();
            Array.Copy(TracksHidden.All, 0, result, originalCount, TracksHidden.All.Length);
            result[result.Length - 1] = TracksRethink.Get();
            return result;
#endif
        }
        public static bool IsFull(string id)
        {
#if FLIP_MIX_STAGE
            foreach (var track in TracksMixedStage.All) if (track.Id == id) return true;
#endif
            return TracksFull.IsFull(id) || TracksRhythm.IsFull(id) || id == TracksMixed.Id || TracksHidden.IsNew(id) || id == TracksRethink.Id;
        }
        // Old selections may survive in an open game or an external caller.
        // Legacy index 4/5 must not become a newly appended UI course.
        public static string NormalizeId(string id)
        { return id == TracksRhythm.DenseId || id == TracksRhythm.WavesId ? TracksRhythm.CascadeId : id; }
        public static Track Get(int index)
        {
            return Get(index == 0 ? TracksFull.LongId : index == 1 ? TracksFull.ShortId
                : index == 2 ? TracksFull.FlowId : TracksRhythm.CascadeId);
        }
        // Only current menu positions use the active catalogue, including the
        // isolated FLIP_MIX_STAGE catalogue during its separate verification.
        public static Track GetAt(int index)
        { return index >= 0 && index < All.Length ? All[index] : Get(TracksRhythm.CascadeId); }
        public static Track Get(string id)
        {
            id = NormalizeId(id);
#if FLIP_MIX_STAGE
            foreach (var track in TracksMixedStage.All) if (track.Id == id) return track;
#endif
            if (id == TracksMixed.Id) return TracksMixed.Get();
            if (TracksHidden.IsNew(id)) return TracksHidden.Get(id);
            if (id == TracksRethink.Id) return TracksRethink.Get();
            return TracksRhythm.IsFull(id) || id == TracksRhythm.PracticeId ? TracksRhythm.Get(id) : TracksFull.Get(id);
        }
    }
}
