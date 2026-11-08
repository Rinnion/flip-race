using System;

namespace FlipRace.Core
{
    // Continuous roots of the actual trajectory, never time/frame sampling.
    // Rectangle boundaries are quadratic. Circle contact along a parabola is
    // quartic; derivative roots partition it into monotone intervals, including
    // tangencies that do not change sign. Bisection only refines isolated roots.
    internal static class ContinuousMotion
    {
        const double TimeTolerance = 1e-12;
        // Tolerance can report both a cut point and a root in each adjacent
        // interval. Starting with one linear root, degree n can therefore emit
        // at most 2*(previous bound + 2)-1 entries (1, 5, 13, 29 for degrees 1–4).
        const int RootScratchSize = 29;

        internal static int QuadraticRoots(double a, double b, double c, out double first, out double second)
        {
            first = second = double.PositiveInfinity;
            if (a == 0)
            {
                if (b == 0) return 0;
                first = -c / b; return 1;
            }
            double discriminant = b * b - 4 * a * c;
            double scale = Math.Abs(b * b) + Math.Abs(4 * a * c);
            if (discriminant < 0 && discriminant >= -1e-13 * Math.Max(1, scale)) discriminant = 0;
            if (discriminant < 0) return 0;
            double root = Math.Sqrt(discriminant);
            if (root == 0) { first = -b / (2 * a); return 1; }
            double q = -.5 * (b + (b >= 0 ? root : -root));
            first = q / a; second = c / q;
            if (first > second) { double swap = first; first = second; second = swap; }
            return 2;
        }

        internal static double FirstBandContact(double p, double v, double acceleration,
            double minimum, double maximum, double start, double end)
        {
            double y = p + v * start + .5 * acceleration * start * start;
            if (y >= minimum - 1e-9 && y <= maximum + 1e-9) return start;
            double result = double.PositiveInfinity;
            for (int i = 0; i < 2; i++)
            {
                double boundary = i == 0 ? minimum : maximum;
                if (double.IsInfinity(boundary)) continue;
                int count = QuadraticRoots(.5 * acceleration, v, p - boundary, out double first, out double second);
                if (count > 0 && first >= start - TimeTolerance && first <= end + TimeTolerance) result = Math.Min(result, Math.Max(start, first));
                if (count > 1 && second >= start - TimeTolerance && second <= end + TimeTolerance) result = Math.Min(result, Math.Max(start, second));
            }
            return result;
        }

        internal static double FirstCircleContact(double dx, double dy, double vx, double vy,
            double acceleration, double radius, double duration)
        {
            Span<double> coefficients = stackalloc double[] { dx * dx + dy * dy - radius * radius,
                2 * (dx * vx + dy * vy), vx * vx + vy * vy + acceleration * dy,
                acceleration * vy, .25 * acceleration * acceleration };
            return FirstRoot(coefficients, duration);
        }

        internal static double FirstCircleContact(double dx, double dy, double vx, double vy,
            double ax, double ay, double radius, double duration)
        {
            if (ax == 0) return FirstCircleContact(dx, dy, vx, vy, ay, radius, duration);
            Span<double> coefficients = stackalloc double[] { dx * dx + dy * dy - radius * radius,
                2 * (dx * vx + dy * vy), vx * vx + vy * vy + ax * dx + ay * dy,
                ax * vx + ay * vy, .25 * (ax * ax + ay * ay) };
            return FirstRoot(coefficients, duration);
        }

        static double FirstRoot(ReadOnlySpan<double> coefficients, double duration)
        {
            if (coefficients[0] <= Tolerance(coefficients, 0)) return 0;
            Span<double> roots = stackalloc double[RootScratchSize];
            int count = Roots(coefficients, 0, duration, roots, 0);
            return count > 0 ? roots[0] : double.PositiveInfinity;
        }

        static double Value(ReadOnlySpan<double> coefficients, double x)
        {
            double value = coefficients[coefficients.Length - 1];
            for (int i = coefficients.Length - 2; i >= 0; i--) value = value * x + coefficients[i];
            return value;
        }
        static double Tolerance(ReadOnlySpan<double> coefficients, double x)
        {
            double scale = Math.Abs(coefficients[coefficients.Length - 1]);
            for (int i = coefficients.Length - 2; i >= 0; i--) scale = scale * Math.Abs(x) + Math.Abs(coefficients[i]);
            return Math.Max(1, scale) * 1e-12;
        }
        static void Add(Span<double> roots, ref int count, double value)
        {
            for (int i = 0; i < count; i++) if (Math.Abs(roots[i] - value) <= TimeTolerance) return;
            roots[count++] = value;
        }

        static void Sort(Span<double> roots, int count)
        {
            for (int i = 1; i < count; i++)
            {
                double value = roots[i];
                int position = i;
                while (position > 0 && roots[position - 1].CompareTo(value) > 0)
                {
                    roots[position] = roots[position - 1];
                    position--;
                }
                roots[position] = value;
            }
        }

        static int Roots(ReadOnlySpan<double> coefficients, double lo, double hi, Span<double> result, int count)
        {
            int degree = coefficients.Length - 1;
            while (degree > 0 && coefficients[degree] == 0) degree--;
            if (degree == 0) return count;
            if (degree == 1)
            {
                double root = -coefficients[0] / coefficients[1];
                if (root >= lo - TimeTolerance && root <= hi + TimeTolerance) Add(result, ref count, Math.Max(lo, Math.Min(hi, root)));
                return count;
            }
            Span<double> derivative = stackalloc double[degree];
            for (int i = 1; i <= degree; i++) derivative[i - 1] = coefficients[i] * i;
            Span<double> cuts = stackalloc double[RootScratchSize];
            cuts[0] = lo;
            int cutCount = Roots(derivative, lo, hi, cuts, 1);
            cuts[cutCount++] = hi;
            Sort(cuts, cutCount);
            for (int i = 0; i < cutCount; i++)
            {
                double point = cuts[i], value = Value(coefficients, point);
                if (Math.Abs(value) <= Tolerance(coefficients, point)) Add(result, ref count, point);
                if (i == 0 || point - cuts[i - 1] <= TimeTolerance) continue;
                double left = cuts[i - 1], right = point, leftValue = Value(coefficients, left);
                if ((leftValue < 0) == (value < 0) || leftValue == 0 || value == 0) continue;
                for (int iteration = 0; iteration < 80 && right - left > TimeTolerance; iteration++)
                {
                    double middle = (left + right) / 2, middleValue = Value(coefficients, middle);
                    if ((middleValue < 0) == (leftValue < 0)) { left = middle; leftValue = middleValue; }
                    else right = middle;
                }
                Add(result, ref count, (left + right) / 2);
            }
            Sort(result, count);
            return count;
        }
    }
}
