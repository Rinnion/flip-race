using System;

namespace FlipRace.Core
{
    // Short authored control trials. Legacy V4/TUNE geometry remains unchanged.
    public static class TracksControl
    {
        public const string ChallengeId = "control-comparison";
        public const string UnlimitedId = "control-passages";

        public static Track Open() { return TracksV4.Open(); }
        public static Track Get(string id, MotionMode mode)
        {
            if (id == ChallengeId) return Challenge(mode);
            if (id == UnlimitedId) return Unlimited();
            return TracksV4.Get(id);
        }

        public static Track Challenge(MotionMode mode)
        {
            double[] safe, ideal;
            switch (mode)
            {
                case MotionMode.GravityAirFlip:
                case MotionMode.GravityUnlimited:
                    safe = new double[] { 850, 1740, 2420, 2740, 3130 };
                    ideal = new double[] { 803.545515, 917.945515, 1160, 1450, 1906.527781, 2740, 3130, 4523.152127406574 };
                    break;
                case MotionMode.LinearAirFlip:
                    safe = new double[] { 850, 1740, 2420, 2740, 3130 };
                    ideal = new double[] { 915.049057, 1007.449057, 1160, 1450, 1900.377358, 2740, 3130, 4537.811320754717 };
                    break;
                case MotionMode.JumpGravityFlip:
                    safe = new double[] { 800, 855, 1740, 1795, 2420, 2475, 2740, 2795, 3130, 3185 };
                    ideal = new double[] { 824.065859, 1160, 1240, 1802.351265, 2740, 2795, 3130, 3185, 4547.531146724642 };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), "Use one of the four CONTROL modes");
            }
            return new Track(4620, new[]
            {
                Stone("control-floor-pad", "floor", 1000, 200, 48),
                Stone("control-ceiling-pad", "ceiling", 1940, 200, 92),
                Stone("control-quick-floor", "floor", 2600, 64, 48),
                Stone("control-quick-ceiling", "ceiling", 2940, 64, 92),
                Stone("control-quick-exit", "floor", 3260, 64, 48)
            })
            {
                Id = ChallengeId, Name = "Точные посадки", Focus = "Две площадки, связка и точный финишный рывок",
                NegativeEnabled = true, SafeRoute = safe, RiskRoute = ideal,
                RiskZones = new[]
                {
                    Pad("control-landing-floor", "control-floor-pad", 1060, 192),
                    Pad("control-landing-ceiling", "control-ceiling-pad", 2000, 92),
                    new RiskZone { Id = "control-visible-trap", Kind = "negative", X = 3110, Y = 110, Radius = 8 },
                    new RiskZone { Id = "control-finish-boost", Kind = "finish", X = 4576, Y = 60, FinishHeight = 16 }
                }
            };
        }

        public static Track Unlimited()
        {
            double[] route = { 750.254574710043, 822.427287355021, 910,
                1050.254574710043, 1122.427287355022, 1210,
                1350.254574710043, 1422.427287355022, 1510,
                1650.254574710043, 1722.427287355022, 1810 };
            return new Track(2860, new[]
            {
                Gate("control-passage-upper", 900, 4, 126),
                Gate("control-passage-lower", 1200, 114, 236),
                Gate("control-passage-high", 1500, 4, 126),
                Gate("control-passage-low", 1800, 114, 236)
            })
            {
                Id = UnlimitedId, Name = "Проходы по высоте", Focus = "Два повтора в воздухе, затем посадка",
                SafeRoute = route, RiskRoute = (double[])route.Clone()
            };
        }

        public static double[] NegativeRoute(MotionMode mode)
        {
            double[] route = Challenge(mode).RiskRoute;
            route[6] = mode == MotionMode.LinearAirFlip ? 3014 : mode == MotionMode.JumpGravityFlip ? 3026.5 : 3018.5;
            if (mode == MotionMode.JumpGravityFlip) route[7] = 3081.5;
            return route;
        }

        static Obstacle Stone(string id, string kind, double x, double width, double height)
        { return new Obstacle { Id = id, Kind = kind, X = x, Width = width, Height = height }; }
        static Obstacle Gate(string id, double x, double top, double bottom)
        { return new Obstacle { Id = id, Kind = "gate", X = x, Width = 20, GapTop = top, GapBottom = bottom }; }
        static RiskZone Pad(string id, string obstacle, double x, double y)
        { return new RiskZone { Id = id, Kind = "landing", ObstacleId = obstacle, X = x, Y = y, LandingWidth = 52 }; }
    }
}
