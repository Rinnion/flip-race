using System;

namespace FlipRace.Core
{
    // Every caller splits at support/effect/recovery changes and reserve zero.
    // Within one such segment horizontal acceleration is constant.
    internal static class SurfaceRunMotion
    {
        internal const double GrowthPerSecond = .05, DecayPerSecond = .10;

        internal static double ReserveRate(PlayerState player)
        {
            if (!player.SurfaceRunEnabled || player.Recovering > 0 || player.Status != PlayerStatus.Running) return 0;
            if (!player.Flipping && player.Support != SupportKind.None) return GrowthPerSecond;
            return player.SurfaceRunReserve > 0 ? -DecayPerSecond : 0;
        }
        internal static double Acceleration(PlayerState player) { return RaceConfig.Speed * ReserveRate(player); }
        internal static double Position(PlayerState player, double time)
        { return player.Distance + player.CurrentSpeed * time + .5 * Acceleration(player) * time * time; }

        internal static double FirstNonnegativeRoot(double a, double b, double c)
        {
            int count = ContinuousMotion.QuadraticRoots(a, b, c, out double first, out double second);
            if (count > 0 && first >= -1e-12) return Math.Max(0, first);
            return count > 1 && second >= -1e-12 ? Math.Max(0, second) : double.PositiveInfinity;
        }
        internal static double TimeForDelta(double distance, double speed, double acceleration)
        {
            if (distance <= 0) return 0;
            return acceleration == 0 ? distance / speed : FirstNonnegativeRoot(.5 * acceleration, speed, -distance);
        }
        internal static double TimeToDistance(PlayerState player, double distance)
        { return TimeForDelta(Math.Max(0, distance - player.Distance), player.CurrentSpeed, Acceleration(player)); }

        internal static bool RangeTimes(PlayerState player, double minimum, double maximum,
            double horizon, out double enter, out double leave)
        {
            // OFF deliberately retains the original constant-speed operations.
            if (!player.SurfaceRunEnabled)
            {
                double first = (minimum - player.Distance) / player.CurrentSpeed;
                double last = (maximum - player.Distance) / player.CurrentSpeed;
                enter = Math.Max(0, Math.Min(first, last));
                leave = Math.Min(horizon, Math.Max(first, last));
                return enter <= leave + 1e-9;
            }
            enter = TimeToDistance(player, minimum);
            leave = player.Distance > maximum + 1e-9 ? -1 : Math.Min(horizon, TimeToDistance(player, maximum));
            return enter <= leave + 1e-9;
        }

        internal static double FirstFinishContact(double x, double y, double vx, double vy,
            double ax, double ay, double bodyRadius, double duration)
        {
            double radius = bodyRadius + FinishTargetGeometry.StrokeWidth / 2;
            double first = double.PositiveInfinity;
            for (int i = 0; i < FinishTargetGeometry.SegmentCount; i++)
            {
                var segment = FinishTargetGeometry.GetSegment(i);
                first = Math.Min(first, ContinuousMotion.FirstCircleContact(x - segment.X1, y - segment.Y1,
                    vx, vy, ax, ay, radius, duration));
                first = Math.Min(first, ContinuousMotion.FirstCircleContact(x - segment.X2, y - segment.Y2,
                    vx, vy, ax, ay, radius, duration));
                double dx = segment.X2 - segment.X1, dy = segment.Y2 - segment.Y1;
                double length = Math.Sqrt(dx * dx + dy * dy), ux = dx / length, uy = dy / length;
                double tangent = (x - segment.X1) * ux + (y - segment.Y1) * uy;
                double normal = -(x - segment.X1) * uy + (y - segment.Y1) * ux;
                double tv = vx * ux + vy * uy, nv = -vx * uy + vy * ux;
                double ta = ax * ux + ay * uy, na = -ax * uy + ay * ux;
                if (Inside(0, tangent, tv, ta, normal, nv, na, length, radius)) first = 0;
                for (int boundary = 0; boundary < 4; boundary++)
                {
                    bool along = boundary < 2;
                    double p = along ? tangent : normal, v = along ? tv : nv, a = along ? ta : na;
                    double limit = boundary == 0 ? 0 : boundary == 1 ? length : boundary == 2 ? -radius : radius;
                    int count = ContinuousMotion.QuadraticRoots(.5 * a, v, p - limit, out double t1, out double t2);
                    for (int root = 0; root < count; root++)
                    {
                        double time = root == 0 ? t1 : t2;
                        if (time < -1e-12 || time > duration + 1e-12) continue;
                        time = Math.Max(0, Math.Min(duration, time));
                        if (Inside(time, tangent, tv, ta, normal, nv, na, length, radius)) first = Math.Min(first, time);
                    }
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
