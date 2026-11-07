using System;

namespace FlipRace.Core
{
    // One uninterrupted horizontal segment. The simulation splits it at support,
    // reward and recovery changes, and at linear reserve cap/zero boundaries.
    internal readonly struct SurfaceRunMotion
    {
        private readonly bool enabled;
        private readonly double position, speed, reserve, cap, rate, exponent;
        internal readonly double TransitionTime;
        internal bool IsExponential { get { return exponent > 0; } }
        internal double Acceleration { get { return RaceConfig.Speed * rate; } }

        internal SurfaceRunMotion(PlayerState player)
        {
            enabled = player.SurfaceRunEnabled;
            position = player.Distance;
            speed = player.CurrentSpeed;
            reserve = player.SurfaceRunReserve;
            cap = rate = exponent = 0;
            TransitionTime = double.PositiveInfinity;
            if (!enabled || player.Recovering > 0 || player.Status != PlayerStatus.Running) return;

            var settings = player.SurfaceRunSettings;
            cap = settings.Cap;
            if (!player.Flipping && player.Support != SupportKind.None)
            {
                if (reserve >= cap) return;
                if (settings.Curve == SurfaceRunCurve.Exponential) exponent = settings.Rate / cap;
                else
                {
                    rate = settings.Rate;
                    TransitionTime = (cap - reserve) / rate;
                }
            }
            else if (reserve > 0)
            {
                rate = -2 * settings.Rate;
                TransitionTime = -reserve / rate;
            }
        }

        private static double ExponentialGain(double z)
        {
            // 1 - exp(-z) loses relative precision near zero.
            return z < .001 ? z * (1 - z * (.5 - z * (1d / 6 - z * (1d / 24 - z / 120)))) : 1 - Math.Exp(-z);
        }

        private double ExponentialTravel(double time, double referenceSpeed)
        {
            double z = exponent * time;
            // Mean growth over the interval is 1 - (1 - exp(-z))/z.
            double mean = z < .001 ? z * (.5 - z * (1d / 6 - z * (1d / 24 - z * (1d / 120 - z / 720))))
                : 1 - ExponentialGain(z) / z;
            return (speed - referenceSpeed) * time + RaceConfig.Speed * (cap - reserve) * time * mean;
        }

        private double LinearTravel(double time)
        { return speed * time + .5 * Acceleration * time * time; }

        internal double Travel(double time)
        {
            if (IsExponential) return ExponentialTravel(time, 0);
            if (time <= TransitionTime) return LinearTravel(time);
            return LinearTravel(TransitionTime) + (time - TransitionTime) * (speed + Acceleration * TransitionTime);
        }

        internal double Position(double time) { return position + Travel(time); }

        internal double ReserveAfter(double time)
        {
            if (IsExponential) return Math.Min(cap, reserve + (cap - reserve) * ExponentialGain(exponent * time));
            if (time >= TransitionTime) return rate > 0 ? cap : 0;
            return Math.Max(0, reserve + rate * time);
        }

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
        internal double TimeToDistance(double distance, double horizon = double.PositiveInfinity)
        {
            double travel = distance - position;
            if (IsExponential && !double.IsInfinity(horizon) && travel > Travel(horizon + 1e-9))
                return double.PositiveInfinity;
            return TimeForTravel(travel);
        }

        internal double TimeForTravel(double distance)
        {
            if (distance <= 0 || double.IsPositiveInfinity(distance)) return Math.Max(0, distance);
            if (IsExponential)
            {
                // Positive speed increases monotonically from speed to topSpeed.
                double topSpeed = speed + RaceConfig.Speed * (cap - reserve);
                return RefineExponentialCrossing(distance / topSpeed, distance / speed, distance, 0, true);
            }
            if (!double.IsInfinity(TransitionTime))
            {
                double boundary = LinearTravel(TransitionTime);
                if (distance >= boundary)
                    return TransitionTime + (distance - boundary) / (speed + Acceleration * TransitionTime);
            }
            return TimeForDelta(distance, speed, Acceleration);
        }

        internal double EliminationTime(double gap, double cameraSpeed, double horizon)
        {
            if (!IsExponential) return FirstNonnegativeRoot(.5 * Acceleration, speed - cameraSpeed, gap);
            if (speed >= cameraSpeed) return double.PositiveInfinity;
            double topSpeed = speed + RaceConfig.Speed * (cap - reserve);
            double turningTime = topSpeed > cameraSpeed
                ? -Math.Log((topSpeed - cameraSpeed) / (topSpeed - speed)) / exponent : double.PositiveInfinity;
            double last = Math.Min(horizon, turningTime);
            double closest = gap + ExponentialTravel(last, cameraSpeed);
            // Gap is convex and reaches its minimum when runner and camera
            // speed match. Check this boundary even for a tangent contact.
            if (turningTime <= horizon && Math.Abs(closest) <= 1e-9) return turningTime;
            if (closest > 0) return double.PositiveInfinity;
            return RefineExponentialCrossing(0, last, -gap, cameraSpeed, false);
        }

        private double RefineExponentialCrossing(double lo, double hi, double distance, double referenceSpeed, bool increasing)
        {
            while (hi - lo > 1e-12)
            {
                double middle = lo + (hi - lo) / 2;
                if (middle == lo || middle == hi) break;
                double travel = ExponentialTravel(middle, referenceSpeed);
                if (increasing ? travel < distance : travel > distance) lo = middle;
                else hi = middle;
            }
            return hi;
        }

        internal bool RangeTimes(double minimum, double maximum,
            double horizon, out double enter, out double leave)
        {
            // OFF deliberately retains the original constant-speed operations.
            if (!enabled)
            {
                double first = (minimum - position) / speed;
                double last = (maximum - position) / speed;
                enter = Math.Max(0, Math.Min(first, last));
                leave = Math.Min(horizon, Math.Max(first, last));
                return enter <= leave + 1e-9;
            }
            enter = TimeToDistance(minimum, horizon);
            leave = position > maximum + 1e-9 ? -1
                : Position(horizon) <= maximum ? horizon : Math.Min(horizon, TimeToDistance(maximum));
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
