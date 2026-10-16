using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    /// <summary>
    /// Authored V3 geometry. Routes are distance-triggered input edges, not timing
    /// shortcuts: the proof runner executes them against the actual simulation.
    /// Stage one remains as a short practice, while the three full tracks have
    /// equal length and ring budgets with deliberately different action rhythms.
    /// </summary>
    public static class TracksV3
    {
        public const double LowStoneHeight = 48;
        public const double HighStoneHeight = 92;
        public static readonly Track Series = StageOne(false);
        public static readonly Track[] All = { Long(false), Short(false), Flow(false) };
        public static Track Practice { get { return StageOne(false); } }

        public static Track Get(string id, bool negativeEnabled = false)
        {
            if (id == "v3-series" || id == "v3-practice") return StageOne(negativeEnabled);
            if (id == "v3-long") return Long(negativeEnabled);
            if (id == "v3-short") return Short(negativeEnabled);
            if (id == "v3-flow") return Flow(negativeEnabled);
            throw new ArgumentOutOfRangeException(nameof(id), "Unknown V3 track");
        }

        // Deliberate contact starts 40 units after the ideal departure. Waiting
        // another 150 ms at the resulting 110 units/sec still clears the stone.
        public static double[] NegativeRoute(string id)
        {
            Track track = Get(id, true);
            double[] route = (double[])track.RiskRoute.Clone();
            foreach (RiskZone ring in track.RiskZones)
                if (ring.IsNegative)
                    for (int i = 0; i < route.Length; i++)
                        if (Math.Abs(route[i] - ring.RiskFlipX) < .001) route[i] += 56.5;
            return route;
        }

        static Track Long(bool negativeEnabled)
        {
            // Three sustained six-action phrases; a short breathing space
            // separates them. The middle phrase mirrors the ring manoeuvres.
            return Authored("v3-long", "Длинные серии", "Три серии по шесть действий с короткими передышками.",
                new double[] {392,810,1220,1600,1970,2330,2892,3310,3720,4100,4470,4830,5392,5810,6220,6600,6970,7330},
                new[] {0,7,12}, new[] {1,8,13}, new[] {3,15},
                new[] {0,6,12}, negativeEnabled);
        }

        static Track Short(bool negativeEnabled)
        {
            // Six compact triples. The 680-unit between-group gaps are longer
            // than the 310-unit within-group gaps, without lengthening the race.
            return Authored("v3-short", "Короткие связки", "Шесть коротких троек: действие, действие, действие — передышка.",
                new double[] {392,702,1012,1692,2002,2312,2992,3302,3612,4292,4602,4912,5592,5902,6212,6892,7202,7512},
                new[] {0,7,12}, new[] {3,10,15}, new[] {5,14},
                new[] {0,3,6,9,12,15}, negativeEnabled);
        }

        static Track Flow(bool negativeEnabled)
        {
            // An even cadence has no group rests. Ring speed effects shorten
            // some elapsed gaps, but the underlying input spacing stays even.
            return Authored("v3-flow", "Ровный поток", "Восемнадцать действий в почти равномерном потоке.",
                new double[] {392,802,1212,1622,2032,2442,2852,3262,3672,4082,4492,4902,5312,5722,6132,6542,6952,7362},
                new[] {0,7,12}, new[] {3,10,15}, new[] {5,14},
                new[] {0,6,12}, negativeEnabled);
        }

        // This is an authored-data builder, not a random track generator. Each
        // route above is fixed and independently verified with real speed/FIFO.
        static Track Authored(string id, string name, string focus, double[] route,
            int[] before, int[] after, int[] negative, int[] phraseStarts, bool negativeEnabled)
        {
            var obstacles = new List<Obstacle>();
            var rings = new List<RiskZone>();
            var safe = new List<double>();
            for (int i = 0; i < route.Length; i++)
            {
                bool isBefore = Array.IndexOf(before, i) >= 0;
                bool isAfter = Array.IndexOf(after, i) >= 0;
                bool fromFloor = (i & 1) == 0;
                string stoneId = id + "-stone-" + i;
                string side = (isAfter ? !fromFloor : fromFloor) ? "floor" : "ceiling";
                double x = route[i] + (isBefore ? 108 : isAfter ? 40 : 140);
                double height = isBefore ? HighStoneHeight : isAfter ? LowStoneHeight :
                    ((i % 3) == 1 ? HighStoneHeight : LowStoneHeight);
                obstacles.Add(Stone(stoneId, x, side, height));
                if (isBefore || isAfter)
                {
                    double y = isBefore ? (fromFloor ? 149 : 91) : (fromFloor ? 45 : 195);
                    rings.Add(new RiskZone {
                        Id = id + "-ring-" + i, ObstacleId = stoneId,
                        Kind = "positive", Placement = isAfter ? "after" : "before",
                        X = route[i] + (isBefore ? 64 : 150), Y = y, Radius = 6,
                        ClearX = route[i] + (isBefore ? 187 : 171),
                        SafeFlipX = isBefore ? route[i] - 92 : route[i] + 210,
                        RiskFlipX = route[i]
                    });
                }
                // Staying on the safe side replaces an optional after-ring
                // return and the following departure as a pair.
                if (!isAfter && Array.IndexOf(after, i - 1) < 0)
                    safe.Add(route[i] - (isBefore ? 92 : 0));
                if (negativeEnabled && Array.IndexOf(negative, i) >= 0)
                    rings.Add(new RiskZone {
                        Id = id + "-negative-" + i, Kind = "negative", Placement = "before",
                        X = route[i] + 60, Y = fromFloor ? 226 : 14, Radius = 6,
                        SafeFlipX = route[i], RiskFlipX = route[i]
                    });
            }
            var cues = new List<Cue>();
            for (int i = 0; i < phraseStarts.Length; i++)
                cues.Add(new Cue(Math.Max(100, route[phraseStarts[i]] - 200),
                    id == "v3-flow" ? "Держи ритм" : "Серия " + (i + 1)));
            return new Track(7920, obstacles.ToArray(), cues.ToArray()) {
                Id = id, Name = name, Focus = focus, NegativeEnabled = negativeEnabled,
                RiskZones = rings.ToArray(), RiskRoute = route, SafeRoute = safe.ToArray()
            };
        }

        public static Track StageOne(bool negativeEnabled = false)
        {
            var obstacles = new[]
            {
                Stone("series-before", 500, "floor", HighStoneHeight),
                Stone("series-return", 850, "floor", LowStoneHeight),
                Stone("series-floor", 1360, "floor", LowStoneHeight),
                Stone("series-ceiling", 1740, "ceiling", HighStoneHeight),
                Stone("series-tall-floor", 2110, "floor", HighStoneHeight),
                Stone("series-low-ceiling", 2470, "ceiling", LowStoneHeight)
            };
            var rings = new List<RiskZone>
            {
                new RiskZone { Id = "series-before-ring", ObstacleId = "series-before",
                    Kind = "positive", Placement = "before", X = 456, Y = 149, Radius = 6,
                    ClearX = 579, SafeFlipX = 300, RiskFlipX = 392 },
                new RiskZone { Id = "series-after-ring", ObstacleId = "series-return",
                    Kind = "positive", Placement = "after", X = 960, Y = 195, Radius = 6,
                    ClearX = 981, SafeFlipX = 1020, RiskFlipX = 810 }
            };
            if (negativeEnabled) rings.Add(new RiskZone
            {
                Id = "series-negative", Kind = "negative", Placement = "before",
                ObstacleId = null, X = 1660, Y = 14, Radius = 6,
                SafeFlipX = 1600, RiskFlipX = 1600
            });
            return new Track(2800, obstacles, new[]
            {
                new Cue(240, "Поздний уход — полезное кольцо"),
                new Cue(720, "Ранний возврат после камня"),
                new Cue(1220, "Теперь меняй стороны"),
                new Cue(1540, "Ромб с минусом можно обойти")
            })
            {
                Id = "v3-series", Name = "Одна длинная серия",
                Focus = "Шесть действий: поздний уход, ранний возврат и смена сторон.",
                NegativeEnabled = negativeEnabled,
                RiskZones = rings.ToArray(),
                RiskRoute = new[] { 392d, 810, 1220, 1600, 1970, 2330 },
                SafeRoute = new[] { 300d, 1580, 1950, 2310 }
            };
        }

        static Obstacle Stone(string id, double x, string side, double height)
        {
            return new Obstacle { Id = id, X = x, Width = 64, Kind = side, Height = height };
        }
    }
}
