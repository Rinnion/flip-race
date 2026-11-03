using System;

namespace FlipRace.Core
{
    public enum HiddenObstaclePhase { Hidden, Warning, Solid }

    // Shared per-race timeline; never attach mutable state to track geometry.
    public sealed class HiddenObstacleState
    {
        public HiddenObstaclePhase Phase;
        public double? RevealedAt, SolidAt;
        public int? TriggerPlayerId;
        public bool ActivationPostponed;
        internal HiddenObstacleState Copy() { return (HiddenObstacleState)MemberwiseClone(); }
    }

    public static class HiddenObstacles
    {
        public const double RevealDistance = 440;
        // Only hidden tracks use this shared-world sampling bound. Analytic
        // player motion stays unchanged; body-clear lag is <= 4.167 ms.
        internal const double MaximumStep = 1d / 240;
        private const double ContactTolerance = 1e-8;

        private static bool IsHiddenStone(Obstacle obstacle)
        { return obstacle.Hidden && (obstacle.Kind == "floor" || obstacle.Kind == "ceiling"); }

        public static HiddenObstaclePhase GetPhase(GameState game, Obstacle obstacle)
        {
            if (!IsHiddenStone(obstacle)) return HiddenObstaclePhase.Solid;
            return game != null && game.HiddenObstacles.TryGetValue(obstacle.Id, out var state)
                ? state.Phase : HiddenObstaclePhase.Hidden;
        }

        public static double RevealDistanceFor(Obstacle obstacle)
        {
            if (obstacle == null) throw new ArgumentNullException(nameof(obstacle));
            double value = obstacle.HiddenRevealDistance ?? RevealDistance;
            RequireFiniteAbove(value, 0, nameof(obstacle.HiddenRevealDistance));
            return value;
        }

        public static double WarningDurationFor(GameState game, Obstacle obstacle)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (obstacle == null) throw new ArgumentNullException(nameof(obstacle));
            double value = obstacle.HiddenWarningDuration ?? game.HiddenWarningDuration;
            RequireFiniteAbove(value, MaximumStep, nameof(obstacle.HiddenWarningDuration));
            return value;
        }

        // Rendering progress only. Reaching 1 never makes an occupied warning
        // solid; consumers must continue using GetPhase for its physical state.
        public static double WarningProgress(GameState game, Obstacle obstacle)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (obstacle == null) throw new ArgumentNullException(nameof(obstacle));
            var phase = GetPhase(game, obstacle);
            if (phase == HiddenObstaclePhase.Solid) return 1;
            if (phase == HiddenObstaclePhase.Hidden
                || !game.HiddenObstacles.TryGetValue(obstacle.Id, out var state) || !state.RevealedAt.HasValue) return 0;
            return Math.Max(0, Math.Min(1, (game.Elapsed - state.RevealedAt.Value) / WarningDurationFor(game, obstacle)));
        }

        private static void RequireFiniteAbove(double value, double minimum, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= minimum)
                throw new ArgumentOutOfRangeException(parameter, value, "Value must be finite and greater than " + minimum + ".");
        }

        internal static void ValidateTrack(Track track)
        {
            foreach (var obstacle in track.Obstacles)
            {
                if (obstacle.HiddenRevealDistance.HasValue)
                    RequireFiniteAbove(obstacle.HiddenRevealDistance.Value, 0, nameof(obstacle.HiddenRevealDistance));
                if (obstacle.HiddenWarningDuration.HasValue)
                    RequireFiniteAbove(obstacle.HiddenWarningDuration.Value, MaximumStep, nameof(obstacle.HiddenWarningDuration));
            }
        }

        public static void ConfigureWarning(GameState game, double seconds)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (game.Elapsed > 0) throw new InvalidOperationException("Warning duration is fixed for an attempt.");
            if (seconds != .4 && seconds != .6 && seconds != .8 && seconds != 1 && seconds != 1.2)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            game.HiddenWarningDuration = seconds;
        }

        internal static bool HasHidden(Track track)
        {
            foreach (var obstacle in track.Obstacles) if (IsHiddenStone(obstacle)) return true;
            return false;
        }

        internal static double StepLimit(GameState game)
        {
            double step = MaximumStep;
            foreach (var obstacle in game.Track.Obstacles)
                if (game.HiddenObstacles.TryGetValue(obstacle.Id, out var state)
                    && state.Phase == HiddenObstaclePhase.Warning && state.RevealedAt.HasValue)
                {
                    double remaining = state.RevealedAt.Value + WarningDurationFor(game, obstacle) - game.Elapsed;
                    if (remaining > 1e-12) step = Math.Min(step, remaining);
                }
            return step;
        }

        // AdvancePlayer reports each existing analytic constant-speed segment.
        // New warnings have no physical effect in this slice (duration > MaximumStep),
        // so candidates can be reduced to the earliest time after any player
        // order, without changing another participant's collision state.
        internal static void ObserveMotion(GameState game, PlayerState player, double duration, double at)
        {
            if (game == null || !Active(player)) return;
            foreach (var obstacle in game.Track.Obstacles)
            {
                if (!IsHiddenStone(obstacle)) continue;
                double boundary = ObstacleGeometry.Left(obstacle) - RevealDistanceFor(obstacle);
                double crossing = Math.Max(0, (boundary - player.Distance) / player.CurrentSpeed);
                if (crossing > duration + 1e-12) continue;
                double revealedAt = at + Math.Min(crossing, duration);
                if (!game.HiddenObstacles.TryGetValue(obstacle.Id, out var state))
                {
                    state = new HiddenObstacleState { Phase = HiddenObstaclePhase.Warning };
                    game.HiddenObstacles.Add(obstacle.Id, state);
                }
                if (state.Phase == HiddenObstaclePhase.Solid) continue;
                if (!state.RevealedAt.HasValue || revealedAt < state.RevealedAt.Value - 1e-12
                    || (Math.Abs(revealedAt - state.RevealedAt.Value) <= 1e-12 && player.Id < state.TriggerPlayerId))
                { state.RevealedAt = revealedAt; state.TriggerPlayerId = player.Id; }
            }
        }

        internal static void Update(GameState game)
        {
            foreach (var obstacle in game.Track.Obstacles)
            {
                if (!IsHiddenStone(obstacle)) continue;
                if (!game.HiddenObstacles.TryGetValue(obstacle.Id, out var state))
                {
                    PlayerState first = null;
                    foreach (var player in game.Players)
                        if (Active(player) && player.Distance >= ObstacleGeometry.Left(obstacle) - RevealDistanceFor(obstacle) - 1e-9
                            && (first == null || player.Id < first.Id)) first = player;
                    if (first == null) continue;
                    state = new HiddenObstacleState { Phase = HiddenObstaclePhase.Warning,
                        RevealedAt = game.Elapsed, TriggerPlayerId = first.Id };
                    game.HiddenObstacles.Add(obstacle.Id, state);
                }
                if (state.Phase != HiddenObstaclePhase.Warning || !state.RevealedAt.HasValue
                    || game.Elapsed + 1e-12 < state.RevealedAt.Value + WarningDurationFor(game, obstacle)) continue;
                bool occupied = false;
                foreach (var player in game.Players)
                    if (Active(player) && Overlaps(player, obstacle)) { occupied = true; break; }
                if (occupied) { state.ActivationPostponed = true; continue; }
                state.Phase = HiddenObstaclePhase.Solid;
                state.SolidAt = game.Elapsed;
            }
        }

        private static bool Active(PlayerState player)
        { return player.Status == PlayerStatus.Running && !player.FinishTime.HasValue; }

        private static bool Overlaps(PlayerState player, Obstacle obstacle)
        {
            // Match the body's swept rectangular collision envelope, including
            // contact: activating at a departing inclusive plane would false-hit.
            if (!ObstacleGeometry.TryGetBand(obstacle, 0, out var band)) return false;
            double radius = RaceConfig.PlayerRadius, y = RaceSimulation.GetPlayerCenterY(player);
            return player.Distance + radius >= band.Left - ContactTolerance
                && player.Distance - radius <= band.Right + ContactTolerance
                && y + radius >= band.Top - ContactTolerance
                && y - radius <= band.Bottom + ContactTolerance;
        }
    }
}
