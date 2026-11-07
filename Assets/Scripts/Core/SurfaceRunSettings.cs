using System;

namespace FlipRace.Core
{
    public enum SurfaceRunCurve { Linear, Exponential }

    public readonly struct SurfaceRunSettings
    {
        internal const int MaximumCapPercent = 40;
        public static readonly SurfaceRunSettings Default = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30);

        public int RatePercent { get; }
        public SurfaceRunCurve Curve { get; }
        public int CapPercent { get; }
        public int AirDecayPercent { get; }
        public bool ResetOnCollision { get; }
        public double AirDecayRatePercent { get { return RatePercent * AirDecayPercent / 100d; } }

        public SurfaceRunSettings(int ratePercent, SurfaceRunCurve curve, int capPercent,
            int airDecayPercent = 200, bool resetOnCollision = true)
        {
            if (ratePercent < 1 || ratePercent > 5) throw new ArgumentOutOfRangeException(nameof(ratePercent));
            if (curve != SurfaceRunCurve.Linear && curve != SurfaceRunCurve.Exponential)
                throw new ArgumentOutOfRangeException(nameof(curve));
            if (capPercent != 20 && capPercent != 30 && capPercent != MaximumCapPercent)
                throw new ArgumentOutOfRangeException(nameof(capPercent));
            if (airDecayPercent != 0 && airDecayPercent != 50 && airDecayPercent != 100
                && airDecayPercent != 200 && airDecayPercent != 400)
                throw new ArgumentOutOfRangeException(nameof(airDecayPercent));
            RatePercent = ratePercent;
            Curve = curve;
            CapPercent = capPercent;
            AirDecayPercent = airDecayPercent;
            ResetOnCollision = resetOnCollision;
        }

        internal double Rate { get { return RatePercent / 100d; } }
        internal double Cap { get { return CapPercent / 100d; } }
    }
}
