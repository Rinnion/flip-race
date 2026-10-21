using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    // MIX-01 only. These courses are not part of the released race catalogue.
    public static class TracksMixedStage
    {
        public const string StageId = "mixed-stage";
        public const string ReorderedId = "mixed-stage-reordered";
        public const string MirrorSuffix = "-mirrored";
        public const double SmallX = 600, Length = 2700;
        public static readonly Track[] All = { Get(StageId), Get(ReorderedId), Get(StageId, true), Get(ReorderedId, true) };

        public static Track Get(string id, bool mirrored = false)
        {
            if (id != null && id.EndsWith(MirrorSuffix, StringComparison.Ordinal))
            { mirrored = true; id = id.Substring(0, id.Length - MirrorSuffix.Length); }
            if (id != StageId && id != ReorderedId) throw new ArgumentException("Unknown mixed stage", nameof(id));
            bool reordered = id == ReorderedId;
            double large = reordered ? 2200 : 1300, low = reordered ? 1300 : 2200;
            var obstacles = new[] {
                new Obstacle { Id = "mixed-small", Kind = "floor", X = SmallX, Width = 64, Height = 48 },
                new Obstacle { Id = "mixed-large", Kind = "floor", X = large, Width = 200, Height = 92 },
                new Obstacle { Id = "mixed-low", Kind = "gate", X = low, Width = 16,
                    GapTop = 114, GapBottom = 224, TopOffsetX = 80 }
            };
            Array.Sort(obstacles, (a, b) => a.X.CompareTo(b.X));
            var zones = new[] {
                new RiskZone { Id = "mixed-small-pad", Kind = "landing", ObstacleId = "mixed-small",
                    X = SmallX + 32, Y = 192, LandingWidth = 32 },
                new RiskZone { Id = "mixed-large-before", Kind = "positive", ObstacleId = "mixed-large",
                    X = large - 100, Y = 120, Radius = 8, ClearX = large + 215 }
            };
            if (mirrored) {
                foreach (var o in obstacles) {
                    if (o.Kind == "floor") o.Kind = "ceiling";
                    else { double top = o.GapTop; o.GapTop = 240 - o.GapBottom; o.GapBottom = 240 - top;
                        double offset = o.TopOffsetX; o.TopOffsetX = o.BottomOffsetX; o.BottomOffsetX = offset; }
                }
                foreach (var z in zones) z.Y = 240 - z.Y;
            }
            string resolvedId = id + (mirrored ? MirrorSuffix : "");
            return new Track(Length, obstacles, new[] {
                new Cue(350, "Малый камень: остаться на своей стороне"),
                new Cue(Math.Min(large, low) - 240, reordered ? "Низкий порог" : "Большой камень: выбрать другую сторону"),
                new Cue(Math.Max(large, low) - 240, reordered ? "Теперь большой камень" : "Низкий порог и возврат")
            }) { Id = resolvedId, Name = (reordered ? "Низкий раньше" : "Большой раньше") + (mirrored ? " · зеркало" : ""),
                Focus = "Разные манёвры, точная посадка и быстрый возврат", RiskZones = zones,
                SafeRoute = SafeRoute(resolvedId, FlightPresets.Chosen), RiskRoute = FullRoute(resolvedId, FlightPresets.Chosen) };
        }
        public static double[] SafeRoute(string id, FlightSettings flight) { return Route(id, flight, false, false); }
        public static double[] FullRoute(string id, FlightSettings flight) { return Route(id, flight, true, false); }
        public static double[] QuickReturnRoute(string id, FlightSettings flight) { return Route(id, flight, false, true); }
        static double[] Route(string id, FlightSettings flight, bool full, bool quick)
        {
            bool mirrored = id.EndsWith(MirrorSuffix, StringComparison.Ordinal);
            bool reordered = id.StartsWith(ReorderedId, StringComparison.Ordinal);
            bool fast = flight.FirstAction == FirstAction.Flip;
            double large = reordered ? 2200 : 1300, low = reordered ? 1300 : 2200;
            var route = new List<double>();
            if (mirrored) { route.Add(0); if (!fast) route.Add(44); }
            if (fast) { route.Add(full ? 430 : 530); route.Add(full ? 632 - 220 * FastTime(164) : 596); }
            else route.Add(full ? 632 - 220 * (350 + Math.Sqrt(350 * 350 - 2 * 662.5 * 48)) / 662.5 : 632 - 220 * 350 / 662.5);
            Action addLarge = () => {
                route.Add(full ? large - 100 - 220 * (fast ? FastTime(106) : MainFlipTime(106)) : large - (fast ? 110 : 140));
                if (!fast) route.Add(route[route.Count - 1] + 44);
            };
            Action addLow = () => {
                if (fast) {
                    if (quick) { route.Add(low - 50); route.Add(low - 17); }
                    else { route.Add(low - 300); route.Add(low - 23.0829351839127); }
                } else if (quick) { route.Add(low - 242); route.Add(low - 198); route.Add(low - 176); }
                else route.Add(low - 108.2264150943396);
            };
            if (reordered) { addLow(); addLarge(); }
            else {
                addLarge();
                if (!fast) { route.Add(large + 500); route.Add(large + 544); addLow(); }
                else if (quick) { route.Add(low - 300); addLow(); }
                else route.Add(low - 23.0829351839127);
            }
            return route.ToArray();
        }
        static double FastTime(double d) { return (Math.Sqrt(1225 + 5400 * d) - 35) / 2700; }
        static double MainFlipTime(double d)
        { return .2 + (Math.Sqrt(217.5 * 217.5 + 1325 * (d - 56.75)) - 217.5) / 662.5; }
    }
}
