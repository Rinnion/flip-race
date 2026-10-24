using System;

namespace FlipRace.Core
{
    // Authored from verified module geometry. Route arrays are proof fixtures;
    // gameplay never reads them as controls. Catalogue activation is separate.
    public static partial class TracksRhythm
    {
        public const string CascadeId = "rhythm-cascade";
        public const string DenseId = "rhythm-dense";
        public const string WavesId = "rhythm-waves";
        public struct RhythmGroup { public int Number; public double StartX, EndX; public string Cue; }
        static bool IsFastWitness(FlightSettings flight)
        { return flight != null && flight.FirstAction == FirstAction.Flip && flight.Law == MotionLaw.Gravity
            && flight.AirRepeats == AirRepeatLimit.One && flight.GravityAcceleration == 2700 && flight.FlipImpulse == 35; }
        public static Track Draft(string id)
        {
            if (id == CascadeId) return WithNegatives(BuildCascade());
            if (id == DenseId) return WithNegatives(BuildDense());
            if (id == WavesId) return WithNegatives(BuildWaves());
            throw new ArgumentException("Unknown rhythm draft: " + id);
        }
        public static double[] SafeRoute(string id, FlightSettings flight)
        {
            if (id == CascadeId) return IsFastWitness(flight)
                ? new double[] { 265, 523.5, 725, 995, 1308, 1774, 2121, 2411, 2669.5, 2871, 3141, 3561, 3902, 4152.5, 4509, 4752, 5052, 5420.5, 5715, 6040, 6343, 6693, 7030, 7330, 7630 }
                : new double[] { 230.5, 281.5, 566, 960.5, 1011.5, 1287.5, 1324.5, 1756.5, 1807, 2072.8, 2139.8, 2376.5, 2427.5, 2712, 3106.5, 3157.5, 3518.5, 3575.5, 3867.5, 3918.5, 4152, 4213, 4420, 4518, 4717.5, 4768.5, 5017.5, 5068.5, 5420.5, 5466, 5701.5, 5766, 6005.5, 6056.5, 6322.5, 6359.5, 6672.5, 6709.5, 6995.5, 7046.5, 7295.5, 7346.5, 7595.5, 7646.5 };
            if (id == DenseId) return IsFastWitness(flight)
                ? new double[] { 135, 442, 803, 1085.5, 1287, 1567, 1861, 2148, 2495, 2789, 3097, 3462, 3756, 4083, 4433, 4794, 5068.5, 5425, 5678, 5960.5, 6162, 6442, 6696.5, 6991, 7340, 7634 }
                : new double[] { 100.5, 151.5, 421.5, 458.5, 768.5, 819.5, 1128, 1532.5, 1583.5, 1826.5, 1877.5, 2130.5, 2181, 2446.8, 2513.8, 2754.5, 2805.5, 3054.5, 3111.5, 3427.5, 3478.5, 3721.5, 3772.5, 4062.5, 4099.5, 4412.5, 4449.5, 4759.5, 4810.5, 5068, 5129, 5336, 5434, 5643.5, 5694.5, 6003, 6407.5, 6458.5, 6696.5, 6742, 6977.5, 7042, 7305.5, 7356.5, 7599.5, 7650.5 };
            if (id == WavesId) return IsFastWitness(flight)
                ? new double[] { 298, 635, 893.5, 1095, 1365, 1850, 2113, 2460, 2710.5, 3067, 3310, 3624, 3953.5, 4155, 4425, 4725, 5025, 5510, 5740.5, 6035, 6360, 6663, 7013, 7350, 7650 }
                : new double[] { 277.5, 314.5, 600.5, 651.5, 936, 1330.5, 1381.5, 1815.5, 1866.5, 2095.5, 2146, 2411.8, 2478.8, 2710, 2771, 2978, 3076, 3275.5, 3326.5, 3581.5, 3638.5, 3996, 4390.5, 4441.5, 4690.5, 4741.5, 4990.5, 5041.5, 5475.5, 5526.5, 5740.5, 5786, 6021.5, 6086, 6325.5, 6376.5, 6642.5, 6679.5, 6992.5, 7029.5, 7315.5, 7366.5, 7615.5, 7666.5 };
            throw new ArgumentException("Unknown rhythm route: " + id);
        }
        public static double[] FullRoute(string id, FlightSettings flight)
        {
            if (id == CascadeId) return IsFastWitness(flight)
                ? new double[] { 265, 492.5, 608.5, 995, 1274, 1718, 2102, 2411, 2638.5, 2754.5, 3141, 3528.5, 3902, 4152.5, 4324.5, 4752, 5052, 5420.5, 5656.5, 6040, 6309, 6659, 7030, 7330, 7630 }
                : new double[] { 230.5, 281.5, 485.1811187353192, 960.5, 1011.5, 1248.5, 1301, 1718.5, 1741.5, 2047.8, 2128.3, 2376.5, 2427.5, 2631.181118735319, 3106.5, 3157.5, 3513, 3536, 3867.5, 3918.5, 4113, 4151, 4179, 4717.5, 4768.5, 5017.5, 5068.5, 5419.5, 5461.5, 5603, 5709.5, 6005.5, 6056.5, 6267.5, 6341, 6614, 6691.5, 6995.5, 7046.5, 7295.5, 7346.5, 7595.5, 7646.5 };
            if (id == DenseId) return IsFastWitness(flight)
                ? new double[] { 135, 408, 803, 1054.5, 1170.5, 1567, 1861, 2092, 2476, 2789, 3064.5, 3462, 3756, 4049, 4399, 4794, 5068.5, 5240.5, 5678, 5929.5, 6045.5, 6442, 6696.5, 6932.5, 7340, 7634, 7853.945775241146 }
                : new double[] { 100.5, 151.5, 382.5, 435, 768.5, 819.5, 1047.1811187353192, 1532.5, 1583.5, 1826.5, 1877.5, 2092.5, 2115.5, 2421.8, 2502.3, 2754.5, 2805.5, 3049, 3072, 3427.5, 3478.5, 3721.5, 3772.5, 4007.5, 4081, 4354, 4431.5, 4759.5, 4810.5, 5029, 5067, 5095, 5643.5, 5694.5, 5922.18111873532, 6407.5, 6458.5, 6695.5, 6737.5, 6879, 6985.5, 7305.5, 7356.5, 7599.5, 7650.5, 7863.922760472742 };
            if (id == WavesId) return IsFastWitness(flight)
                ? new double[] { 264, 635, 862.5, 978.5, 1365, 1850, 2057, 2441, 2710.5, 2882.5, 3310, 3591.5, 3922.5, 4038.5, 4425, 4725, 5025, 5510, 5740.5, 5976.5, 6360, 6629, 6979, 7350, 7650, 7853.945775241146 }
                : new double[] { 238.5, 291, 600.5, 651.5, 855.1811187353192, 1330.5, 1381.5, 1815.5, 1866.5, 2057.5, 2080.5, 2386.8, 2467.3, 2671, 2709, 2737, 3275.5, 3326.5, 3576, 3599, 3915.181118735319, 4390.5, 4441.5, 4690.5, 4741.5, 4990.5, 5041.5, 5475.5, 5526.5, 5739.5, 5781.5, 5923, 6029.5, 6325.5, 6376.5, 6587.5, 6661, 6934, 7011.5, 7315.5, 7366.5, 7615.5, 7666.5, 7863.922760472742 };
            throw new ArgumentException("Unknown rhythm route: " + id);
        }
        public static RhythmGroup[] Groups(string id)
        {
            if (id == CascadeId) return new[] {
                new RhythmGroup { Number = 1, StartX = 180, EndX = 1560, Cue = "Связка 1" },
                new RhythmGroup { Number = 2, StartX = 1696, EndX = 3326, Cue = "Связка 2" },
                new RhythmGroup { Number = 3, StartX = 3462, EndX = 5237, Cue = "Связка 3" },
                new RhythmGroup { Number = 4, StartX = 5375, EndX = 7815, Cue = "Связка 4" },
            };
            if (id == DenseId) return new[] {
                new RhythmGroup { Number = 1, StartX = 50, EndX = 7819, Cue = "Плотный поток" },
            };
            if (id == WavesId) return new[] {
                new RhythmGroup { Number = 1, StartX = 200, EndX = 1550, Cue = "Связка 1" },
                new RhythmGroup { Number = 2, StartX = 1765, EndX = 5210, Cue = "Связка 2" },
                new RhythmGroup { Number = 3, StartX = 5425, EndX = 7835, Cue = "Связка 3" },
            };
            throw new ArgumentException("Unknown rhythm groups: " + id);
        }
        static Track BuildCascade()
        {
            return new Track(7920, new[] {
                new Obstacle { Id = "rhythm-cascade-m0-gate", Kind = "gate", X = 260, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-cascade-m1-pad-stone", Kind = "ceiling", X = 650, Width = 64, Height = 48, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m2-gate", Kind = "gate", X = 990, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m3-gate", Kind = "gate", X = 1290, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-cascade-m4-stone", Kind = "ceiling", X = 1876, Width = 64, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m4-exit-gate", Kind = "gate", X = 2116, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-cascade-m5-gate", Kind = "gate", X = 2406, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m6-pad-stone", Kind = "floor", X = 2796, Width = 64, Height = 48, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m7-gate", Kind = "gate", X = 3136, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-cascade-m8-gate", Kind = "gate", X = 3542, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m9-gate", Kind = "gate", X = 3897, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-cascade-m10-pad-stone", Kind = "ceiling", X = 4287, Width = 200, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m11-gate", Kind = "gate", X = 4747, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m12-gate", Kind = "gate", X = 5047, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-cascade-m13-after-stone", Kind = "ceiling", X = 5535, Width = 64, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m14-gate", Kind = "gate", X = 6035, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m15-chain-gate-1", Kind = "gate", X = 6325, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-cascade-m15-chain-gate-2", Kind = "gate", X = 6675, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m16-gate", Kind = "gate", X = 7025, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-cascade-m17-gate", Kind = "gate", X = 7325, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-cascade-m18-gate", Kind = "gate", X = 7625, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
            }, new[] {
                new Cue(120, "Связка 1"),
                new Cue(1636, "Связка 2"),
                new Cue(3402, "Связка 3"),
                new Cue(5315, "Связка 4"),
            }) {
                Id = CascadeId, Name = "Каскад", Focus = "Четыре связки с короткими передышками", NegativeEnabled = false,
                SafeRoute = SafeRoute(CascadeId, FlightPresets.Chosen),
                RiskRoute = FullRoute(CascadeId, FlightPresets.Chosen),
                RiskZones = new[] {
                    new RiskZone { Id = "rhythm-cascade-m1-pad", ObstacleId = "rhythm-cascade-m1-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 682, Y = 48, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 32, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-m3-free", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 1324, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-m4-before", ObstacleId = "rhythm-cascade-m4-stone", Kind = "positive", Placement = "before", FreeReward = false, X = 1776, Y = 120, Radius = 8, ClearX = 1955, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-m6-pad", ObstacleId = "rhythm-cascade-m6-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 2828, Y = 192, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 32, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-m8-free-high", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 3592, Y = 170, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-m10-pad", ObstacleId = "rhythm-cascade-m10-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 4387, Y = 92, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 52, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-m13-after-ring", ObstacleId = "rhythm-cascade-m13-after-stone", Kind = "positive", Placement = "after", FreeReward = false, X = 5715, Y = 120, Radius = 8, ClearX = 5614, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-m15-chain-1", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 6359, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-m15-chain-2", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 6709, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-cascade-finish", ObstacleId = null, Kind = "finish", Placement = "before", FreeReward = false, X = 7780, Y = 32, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishWidth = 28, FinishHeight = 8 },
                }
            };
        }
        static Track BuildDense()
        {
            return new Track(7920, new[] {
                new Obstacle { Id = "rhythm-dense-m0-gate", Kind = "gate", X = 130, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m1-gate", Kind = "gate", X = 424, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m2-gate", Kind = "gate", X = 798, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m3-pad-stone", Kind = "ceiling", X = 1212, Width = 64, Height = 48, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m4-gate", Kind = "gate", X = 1562, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m5-gate", Kind = "gate", X = 1856, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m6-stone", Kind = "ceiling", X = 2250, Width = 64, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m6-exit-gate", Kind = "gate", X = 2490, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m7-gate", Kind = "gate", X = 2784, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m8-gate", Kind = "gate", X = 3078, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m9-gate", Kind = "gate", X = 3457, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m10-gate", Kind = "gate", X = 3751, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m11-chain-gate-1", Kind = "gate", X = 4065, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m11-chain-gate-2", Kind = "gate", X = 4415, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m12-gate", Kind = "gate", X = 4789, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m13-pad-stone", Kind = "floor", X = 5203, Width = 200, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m14-gate", Kind = "gate", X = 5673, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m15-pad-stone", Kind = "ceiling", X = 6087, Width = 64, Height = 48, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m16-gate", Kind = "gate", X = 6437, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m17-after-stone", Kind = "floor", X = 6811, Width = 64, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-dense-m18-gate", Kind = "gate", X = 7335, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-dense-m19-gate", Kind = "gate", X = 7629, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
            }, new[] {
                new Cue(0, "Плотный поток"),
            }) {
                Id = DenseId, Name = "Плотный поток", Focus = "Непрерывное чередование проходов и наград", NegativeEnabled = false,
                SafeRoute = SafeRoute(DenseId, FlightPresets.Chosen),
                RiskRoute = FullRoute(DenseId, FlightPresets.Chosen),
                RiskZones = new[] {
                    new RiskZone { Id = "rhythm-dense-m1-free", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 458, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-m3-pad", ObstacleId = "rhythm-dense-m3-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 1244, Y = 48, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 32, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-m6-before", ObstacleId = "rhythm-dense-m6-stone", Kind = "positive", Placement = "before", FreeReward = false, X = 2150, Y = 120, Radius = 8, ClearX = 2329, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-m8-free-high", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 3128, Y = 70, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-m11-chain-1", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 4099, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-m11-chain-2", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 4449, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-m13-pad", ObstacleId = "rhythm-dense-m13-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 5303, Y = 148, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 52, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-m15-pad", ObstacleId = "rhythm-dense-m15-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 6119, Y = 48, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 32, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-m17-after-ring", ObstacleId = "rhythm-dense-m17-after-stone", Kind = "positive", Placement = "after", FreeReward = false, X = 6991, Y = 120, Radius = 8, ClearX = 6890, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-dense-finish", ObstacleId = null, Kind = "finish", Placement = "before", FreeReward = false, X = 7876, Y = 208, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                }
            };
        }
        static Track BuildWaves()
        {
            return new Track(7920, new[] {
                new Obstacle { Id = "rhythm-waves-m0-gate", Kind = "gate", X = 280, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-waves-m1-gate", Kind = "gate", X = 630, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m2-pad-stone", Kind = "floor", X = 1020, Width = 64, Height = 48, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m3-gate", Kind = "gate", X = 1360, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-waves-m4-gate", Kind = "gate", X = 1845, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m5-stone", Kind = "floor", X = 2215, Width = 64, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m5-exit-gate", Kind = "gate", X = 2455, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m6-pad-stone", Kind = "floor", X = 2845, Width = 200, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m7-gate", Kind = "gate", X = 3305, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-waves-m8-gate", Kind = "gate", X = 3605, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m9-pad-stone", Kind = "floor", X = 4080, Width = 64, Height = 48, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m10-gate", Kind = "gate", X = 4420, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-waves-m11-gate", Kind = "gate", X = 4720, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m12-gate", Kind = "gate", X = 5020, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-waves-m13-gate", Kind = "gate", X = 5505, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m14-after-stone", Kind = "floor", X = 5855, Width = 64, Height = 92, GapTop = 0, GapBottom = 0, TopOffsetX = 0, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m15-gate", Kind = "gate", X = 6355, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-waves-m16-chain-gate-1", Kind = "gate", X = 6645, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m16-chain-gate-2", Kind = "gate", X = 6995, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
                new Obstacle { Id = "rhythm-waves-m17-gate", Kind = "gate", X = 7345, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 112, BottomOffsetX = 0 },
                new Obstacle { Id = "rhythm-waves-m18-gate", Kind = "gate", X = 7645, Width = 16, Height = 0, GapTop = 110, GapBottom = 130, TopOffsetX = 0, BottomOffsetX = 112 },
            }, new[] {
                new Cue(140, "Связка 1"),
                new Cue(1705, "Связка 2"),
                new Cue(5365, "Связка 3"),
            }) {
                Id = WavesId, Name = "Волны", Focus = "Короткая связка, длинная серия и финальная волна", NegativeEnabled = false,
                SafeRoute = SafeRoute(WavesId, FlightPresets.Chosen),
                RiskRoute = FullRoute(WavesId, FlightPresets.Chosen),
                RiskZones = new[] {
                    new RiskZone { Id = "rhythm-waves-m0-free", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 314, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-m2-pad", ObstacleId = "rhythm-waves-m2-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 1052, Y = 192, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 32, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-m5-before", ObstacleId = "rhythm-waves-m5-stone", Kind = "positive", Placement = "before", FreeReward = false, X = 2115, Y = 120, Radius = 8, ClearX = 2294, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-m6-pad", ObstacleId = "rhythm-waves-m6-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 2945, Y = 148, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 52, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-m8-free-high", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 3655, Y = 170, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-m9-pad", ObstacleId = "rhythm-waves-m9-pad-stone", Kind = "landing", Placement = "before", FreeReward = false, X = 4112, Y = 192, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 32, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-m14-after-ring", ObstacleId = "rhythm-waves-m14-after-stone", Kind = "positive", Placement = "after", FreeReward = false, X = 6035, Y = 120, Radius = 8, ClearX = 5934, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-m16-chain-1", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 6679, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-m16-chain-2", ObstacleId = null, Kind = "instant", Placement = "before", FreeReward = false, X = 7029, Y = 120, Radius = 8, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                    new RiskZone { Id = "rhythm-waves-finish", ObstacleId = null, Kind = "finish", Placement = "before", FreeReward = false, X = 7876, Y = 32, Radius = 6, ClearX = 0, SafeFlipX = 0, RiskFlipX = 0, LandingWidth = 0, FinishHeight = 16 },
                }
            };
        }
    }
}
