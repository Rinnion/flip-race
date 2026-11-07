using System;

namespace FlipRace.Core
{
    public enum SurfaceRunCurve { Linear, Exponential }

    public readonly struct SurfaceRunSettings
    {
        public static readonly SurfaceRunSettings Default = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 30);

        public int RatePercent { get; }
        public SurfaceRunCurve Curve { get; }
        public int CapPercent { get; }

        public SurfaceRunSettings(int ratePercent, SurfaceRunCurve curve, int capPercent)
        {
            if (ratePercent < 1 || ratePercent > 5) throw new ArgumentOutOfRangeException(nameof(ratePercent));
            if (curve != SurfaceRunCurve.Linear && curve != SurfaceRunCurve.Exponential)
                throw new ArgumentOutOfRangeException(nameof(curve));
            if (capPercent != 20 && capPercent != 30 && capPercent != 40)
                throw new ArgumentOutOfRangeException(nameof(capPercent));
            RatePercent = ratePercent;
            Curve = curve;
            CapPercent = capPercent;
        }

        internal double Rate { get { return RatePercent / 100d; } }
        internal double Cap { get { return CapPercent / 100d; } }
    }
}
