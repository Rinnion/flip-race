using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    // Continuous roots of the actual trajectory, never time/frame sampling.
    // Rectangle boundaries are quadratic. Circle contact along a parabola is
    // quartic; derivative roots partition it into monotone intervals, including
    // tangencies that do not change sign. Bisection only refines isolated roots.
    internal static class ContinuousMotion
    {
        const double TimeTolerance = 1e-12;

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
            foreach (double boundary in new[] { minimum, maximum })
            {
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
            var coefficients = new[] { dx * dx + dy * dy - radius * radius,
                2 * (dx * vx + dy * vy), vx * vx + vy * vy + acceleration * dy,
                acceleration * vy, .25 * acceleration * acceleration };
            if (coefficients[0] <= Tolerance(coefficients, 0)) return 0;
            var roots = new List<double>();
            Roots(coefficients, 0, duration, roots);
            return roots.Count > 0 ? roots[0] : double.PositiveInfinity;
        }

        static double Value(double[] coefficients, double x)
        {
            double value = coefficients[coefficients.Length - 1];
            for (int i = coefficients.Length - 2; i >= 0; i--) value = value * x + coefficients[i];
            return value;
        }
        static double Tolerance(double[] coefficients, double x)
        {
            double scale = Math.Abs(coefficients[coefficients.Length - 1]);
            for (int i = coefficients.Length - 2; i >= 0; i--) scale = scale * Math.Abs(x) + Math.Abs(coefficients[i]);
            return Math.Max(1, scale) * 1e-12;
        }
        static void Add(List<double> roots, double value)
        {
            foreach (double previous in roots) if (Math.Abs(previous - value) <= TimeTolerance) return;
            roots.Add(value);
        }
        static void Roots(double[] coefficients, double lo, double hi, List<double> result)
        {
            int degree = coefficients.Length - 1;
            while (degree > 0 && coefficients[degree] == 0) degree--;
            if (degree == 0) return;
            if (degree == 1)
            {
                double root = -coefficients[0] / coefficients[1];
                if (root >= lo - TimeTolerance && root <= hi + TimeTolerance) Add(result, Math.Max(lo, Math.Min(hi, root)));
                return;
            }
            var derivative = new double[degree];
            for (int i = 1; i <= degree; i++) derivative[i - 1] = coefficients[i] * i;
            var cuts = new List<double> { lo };
            Roots(derivative, lo, hi, cuts); cuts.Add(hi); cuts.Sort();
            for (int i = 0; i < cuts.Count; i++)
            {
                double point = cuts[i], value = Value(coefficients, point);
                if (Math.Abs(value) <= Tolerance(coefficients, point)) Add(result, point);
                if (i == 0 || point - cuts[i - 1] <= TimeTolerance) continue;
                double left = cuts[i - 1], right = point, leftValue = Value(coefficients, left);
                if ((leftValue < 0) == (value < 0) || leftValue == 0 || value == 0) continue;
                for (int iteration = 0; iteration < 80 && right - left > TimeTolerance; iteration++)
                {
                    double middle = (left + right) / 2, middleValue = Value(coefficients, middle);
                    if ((middleValue < 0) == (leftValue < 0)) { left = middle; leftValue = middleValue; }
                    else right = middle;
                }
                Add(result, (left + right) / 2);
            }
            result.Sort();
        }
    }
}
