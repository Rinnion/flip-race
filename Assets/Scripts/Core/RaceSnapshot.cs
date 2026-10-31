using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FlipRace.Core
{
    /// <summary>Independent mutable timeline for short ordinary Press/Advance forecasts.</summary>
    /// <remarks>
    /// Track geometry/routes/cues are shared read-only race input: neither the live race,
    /// a forecast, nor its caller may edit them after race creation. FlightSettings,
    /// MotionProfile, FlightMeasurement, BotPlan and StochasticBotSettings are immutable and also shared.
    /// Every mutable timeline object is copied; aliases inside that timeline are retained.
    /// Clone does not advance a race, activate a controller, or choose any bot actions.
    /// </remarks>
    public static class RaceSnapshot
    {
        public static GameState Clone(GameState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            // Winner is the engine's immutable union, not an arbitrary object graph.
            if (source.Winner != null && !(source.Winner is int) && !(source.Winner is string))
                throw new ArgumentException("Winner must be null, a player ID, or a tie string.", nameof(source));
            var copies = new Dictionary<object, object>(ReferenceComparer.Instance);
            var copy = source.SnapshotShell();
            copies.Add(source, copy);
            if (source.Players != null)
            {
                copy.Players = new PlayerState[source.Players.Length];
                copies.Add(source.Players, copy.Players);
                for (int i = 0; i < copy.Players.Length; i++) copy.Players[i] = Player(source.Players[i], copies);
            }
            return copy;
        }

        static PlayerState Player(PlayerState source, Dictionary<object, object> copies)
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out object known)) return (PlayerState)known;
            var copy = source.SnapshotShell();
            copies.Add(source, copy);
            copy.HitObstacleIds = Set(source.HitObstacleIds, copies);
            copy.CollectedRiskIds = Set(source.CollectedRiskIds, copies);
            copy.SuccessfulRiskIds = Set(source.SuccessfulRiskIds, copies);
            copy.FailedRiskIds = Set(source.FailedRiskIds, copies);
            copy.NegativeRiskIds = Set(source.NegativeRiskIds, copies);
            copy.LastHit = Hit(source.LastHit, copies);
            copy.LastCoin = Hit(source.LastCoin, copies);
            copy.LastSuccess = Hit(source.LastSuccess, copies);
            copy.LastInstant = Hit(source.LastInstant, copies);
            copy.LastSlow = Hit(source.LastSlow, copies);
            copy.LastFinishBoost = Hit(source.LastFinishBoost, copies);
            copy.ActiveEffect = Effect(source.ActiveEffect, copies);
            if (source.PendingEffects != null)
            {
                if (copies.TryGetValue(source.PendingEffects, out known)) copy.PendingEffects = (Queue<SpeedEffect>)known;
                else
                {
                    copy.PendingEffects = new Queue<SpeedEffect>(source.PendingEffects.Count);
                    copies.Add(source.PendingEffects, copy.PendingEffects);
                    foreach (var effect in source.PendingEffects) copy.PendingEffects.Enqueue(Effect(effect, copies));
                }
            }
            return copy;
        }

        static HashSet<string> Set(HashSet<string> source, Dictionary<object, object> copies)
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out object known)) return (HashSet<string>)known;
            var copy = new HashSet<string>(source, source.Comparer);
            copies.Add(source, copy);
            return copy;
        }

        static HitInfo Hit(HitInfo source, Dictionary<object, object> copies)
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out object known)) return (HitInfo)known;
            var copy = new HitInfo { Id = source.Id, Message = source.Message, At = source.At };
            copies.Add(source, copy);
            return copy;
        }

        static SpeedEffect Effect(SpeedEffect source, Dictionary<object, object> copies)
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out object known)) return (SpeedEffect)known;
            var copy = new SpeedEffect { RingId = source.RingId, IsNegative = source.IsNegative,
                Remaining = source.Remaining, Elapsed = source.Elapsed, PulseCount = source.PulseCount,
                CompletedPulses = source.CompletedPulses };
            copies.Add(source, copy);
            return copy;
        }

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
    }
}