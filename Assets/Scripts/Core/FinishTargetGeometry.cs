using System;

namespace FlipRace.Core
{
    // The four visible strokes are also the complete finish contact geometry.
    public static class FinishTargetGeometry
    {
        public const int SegmentCount = 4;
        public const double StrokeWidth = 3;
        public const double HalfWidth = 10.5;
        public const double HalfHeight = 8.5;

        public readonly struct Segment
        {
            public readonly double X1, Y1, X2, Y2;
            public Segment(double x1, double y1, double x2, double y2)
            { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
        }

        public static Segment GetSegment(int index)
        {
            switch (index)
            {
                case 0: return new Segment(-10.5, -8.5, -2.1, 0);
                case 1: return new Segment(-2.1, 0, -10.5, 8.5);
                case 2: return new Segment(2.1, -8.5, 10.5, 0);
                case 3: return new Segment(10.5, 0, 2.1, 8.5);
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static double DistanceSquaredToStrokes(double x, double y)
        {
            double minimum = double.PositiveInfinity;
            for (int i = 0; i < SegmentCount; i++)
            {
                Segment s = GetSegment(i);
                double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
                double projection = Math.Max(0, Math.Min(1, ((x - s.X1) * dx + (y - s.Y1) * dy) / (dx * dx + dy * dy)));
                double gapX = x - s.X1 - projection * dx, gapY = y - s.Y1 - projection * dy;
                minimum = Math.Min(minimum, gapX * gapX + gapY * gapY);
            }
            return minimum;
        }

        // x/y are relative to the target center. Motion is x+vx*t,
        // y+vy*t+acceleration*t*t/2. No frame or trajectory sampling is used.
        public static double FirstContact(double x, double y, double vx, double vy,
            double acceleration, double bodyRadius, double duration)
        {
            if (duration < 0 || bodyRadius < 0) return double.PositiveInfinity;
            double radius = bodyRadius + StrokeWidth / 2;
            double enter = 0, leave = duration, extent = HalfWidth + radius;
            if (vx == 0)
            {
                if (x < -extent || x > extent) return double.PositiveInfinity;
            }
            else
            {
                double a = (-extent - x) / vx, b = (extent - x) / vx;
                enter = Math.Max(0, Math.Min(a, b));
                leave = Math.Min(duration, Math.Max(a, b));
                if (enter > leave) return double.PositiveInfinity;
            }
            x += vx * enter;
            y += vy * enter + .5 * acceleration * enter * enter;
            vy += acceleration * enter;
            double horizon = Math.Max(0, leave - enter), first = double.PositiveInfinity;
            for (int i = 0; i < SegmentCount; i++)
            {
                Segment segment = GetSegment(i);
                first = Math.Min(first, ContinuousMotion.FirstCircleContact(x - segment.X1, y - segment.Y1,
                    vx, vy, acceleration, radius, horizon));
                first = Math.Min(first, ContinuousMotion.FirstCircleContact(x - segment.X2, y - segment.Y2,
                    vx, vy, acceleration, radius, horizon));
                first = Math.Min(first, FirstStripContact(segment, x, y, vx, vy, acceleration, radius, horizon));
            }
            return double.IsInfinity(first) ? first : enter + first;
        }

        static double FirstStripContact(Segment segment, double x, double y, double vx, double vy,
            double acceleration, double radius, double duration)
        {
            double dx = segment.X2 - segment.X1, dy = segment.Y2 - segment.Y1;
            double length = Math.Sqrt(dx * dx + dy * dy), ux = dx / length, uy = dy / length;
            double tangent = (x - segment.X1) * ux + (y - segment.Y1) * uy;
            double normal = -(x - segment.X1) * uy + (y - segment.Y1) * ux;
            double tangentV = vx * ux + vy * uy, normalV = -vx * uy + vy * ux;
            double tangentA = acceleration * uy, normalA = acceleration * ux;
            if (Inside(0, tangent, tangentV, tangentA, normal, normalV, normalA, length, radius)) return 0;
            double first = double.PositiveInfinity;
            // An intersection of the two quadratic bands can begin only at
            // t=0 or one of their four boundaries, including double roots.
            for (int boundary = 0; boundary < 4; boundary++)
            {
                bool along = boundary < 2;
                double p = along ? tangent : normal, v = along ? tangentV : normalV;
                double a = along ? tangentA : normalA;
                double limit = boundary == 0 ? 0 : boundary == 1 ? length : boundary == 2 ? -radius : radius;
                int count = ContinuousMotion.QuadraticRoots(.5 * a, v, p - limit, out double t1, out double t2);
                for (int root = 0; root < count; root++)
                {
                    double time = root == 0 ? t1 : t2;
                    if (time < -1e-12 || time > duration + 1e-12) continue;
                    time = Math.Max(0, Math.Min(duration, time));
                    if (Inside(time, tangent, tangentV, tangentA, normal, normalV, normalA, length, radius))
                        first = Math.Min(first, time);
                }
            }
            return first;
        }

        static bool Inside(double time, double p, double v, double a, double n, double nv, double na,
            double length, double radius)
        {
            double tangent = p + v * time + .5 * a * time * time;
            double normal = n + nv * time + .5 * na * time * time;
            return tangent >= -1e-9 && tangent <= length + 1e-9 && Math.Abs(normal) <= radius + 1e-9;
        }
    }
}
