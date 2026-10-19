using System;

namespace FlipRace.Core
{
    // Physical, unexpanded world rectangle. Player-radius expansion belongs to
    // collision detection, not artwork or the authored obstacle dimensions.
    public readonly struct ObstacleBand
    {
        public readonly double X, Y, Width, Height;
        public double Left { get { return X; } }
        public double Right { get { return X + Width; } }
        public double Top { get { return Y; } }
        public double Bottom { get { return Y + Height; } }
        public ObstacleBand(double x, double y, double width, double height)
        { X = x; Y = y; Width = width; Height = height; }
    }

    public static class ObstacleGeometry
    {
        // Gate index 0 is the ceiling half; index 1 is the floor half.
        // Stones have only index 0. An absent gate half has no bounds/collider.
        public static bool TryGetBand(Obstacle obstacle, int index, out ObstacleBand band)
        {
            band = default(ObstacleBand);
            if (obstacle == null) return false;
            if (obstacle.Kind == "gate")
            {
                if (index == 0 && obstacle.GapTop > 0)
                    band = new ObstacleBand(obstacle.X + obstacle.TopOffsetX, 0, obstacle.Width, obstacle.GapTop);
                else if (index == 1 && obstacle.GapBottom < RaceConfig.CorridorHeight)
                    band = new ObstacleBand(obstacle.X + obstacle.BottomOffsetX, obstacle.GapBottom,
                        obstacle.Width, RaceConfig.CorridorHeight - obstacle.GapBottom);
                else return false;
                return true;
            }
            if (index != 0) return false;
            band = new ObstacleBand(obstacle.X, obstacle.Kind == "ceiling" ? 0 : RaceConfig.CorridorHeight - obstacle.Height,
                obstacle.Width, obstacle.Height);
            return true;
        }

        // Empty gates reduce to X. Their absent halves never enlarge culling
        // bounds or postpone a linked reward; there is no solid body to clear.
        public static double Left(Obstacle obstacle)
        {
            bool first = TryGetBand(obstacle, 0, out var top);
            bool second = TryGetBand(obstacle, 1, out var bottom);
            return first ? (second ? Math.Min(top.Left, bottom.Left) : top.Left)
                : second ? bottom.Left : obstacle.X;
        }
        public static double Right(Obstacle obstacle)
        {
            bool first = TryGetBand(obstacle, 0, out var top);
            bool second = TryGetBand(obstacle, 1, out var bottom);
            return first ? (second ? Math.Max(top.Right, bottom.Right) : top.Right)
                : second ? bottom.Right : obstacle.X;
        }
    }
}
