using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FlipRace.Core
{
    /// <summary>Independent mutable timeline for short ordinary Press/Advance forecasts.</summary>
    /// <remarks>
    /// Track geometry/routes/cues are shared read-only race input: neither the live race,
    /// a forecast, nor its caller may edit them after race creation. FlightSettings,
    /// MotionProfile, FlightMeasurement, BotPlan, party lineup and bot settings are immutable and also shared.
    /// Every mutable timeline object is copied; aliases inside that timeline are retained.
    /// Clones have no registered live controllers, so their runner advances physics only.
    /// </remarks>
    public static class RaceSnapshot
    {
        public static GameState Clone(GameState source)
        { return Clone(source, false); }

        // Planning ignores Constant-camera elimination. It keeps all current
        // bodies when they can affect the shared hidden world, with no future
        // inputs from the other participants. Player IDs remain unchanged.
        internal static GameState ForBotForecast(GameState source, int slot)
        {
            var copy = Clone(source, true);
            var bot = copy.Players[slot];
            if (HiddenObstacles.HasHidden(copy.Track))
            {
                copy.Players[slot] = copy.Players[0];
                copy.Players[0] = bot;
            }
            else
            {
                copy.Players = new[] { bot };
                copy.Winner = null;
                copy.WinnerAt = null;
            }
            return copy;
        }

        // One planning call owns two private timelines. Neither their player
        // array nor the sets or queues are replaced during active play.
        internal sealed class ForecastWorkspace
        {
            readonly GameState origin;
            ForecastBuffer current, checkpoint;

            internal ForecastWorkspace(GameState origin)
            {
                this.origin = origin;
                current = new ForecastBuffer(origin);
                checkpoint = new ForecastBuffer(origin);
            }

            internal GameState BeginCandidate()
            {
                current.RestoreFrom(origin);
                return current.Race;
            }

            internal void SaveCheckpoint() { checkpoint.RestoreFrom(current.Race); }

            internal GameState Rollback()
            {
                var discarded = current;
                current = checkpoint;
                checkpoint = discarded;
                return current.Race;
            }
        }

        sealed class ForecastBuffer
        {
            internal readonly GameState Race;
            readonly RecordCopies<HitInfo> hits = new RecordCopies<HitInfo>((source, target) =>
            { target.Id = source.Id; target.Message = source.Message; target.At = source.At; });
            readonly RecordCopies<SpeedEffect> effects = new RecordCopies<SpeedEffect>((source, target) =>
            {
                target.RingId = source.RingId; target.IsNegative = source.IsNegative;
                target.Remaining = source.Remaining; target.Elapsed = source.Elapsed;
                target.PulseCount = source.PulseCount; target.CompletedPulses = source.CompletedPulses;
            });
            readonly RecordCopies<HiddenObstacleState> hidden = new RecordCopies<HiddenObstacleState>((source, target) =>
            {
                target.Phase = source.Phase; target.RevealedAt = source.RevealedAt; target.SolidAt = source.SolidAt;
                target.TriggerPlayerId = source.TriggerPlayerId; target.ActivationPostponed = source.ActivationPostponed;
            });

            internal ForecastBuffer(GameState origin) { Race = Clone(origin); }

            internal void RestoreFrom(GameState source)
            {
                Race.Elapsed = source.Elapsed;
                Race.Phase = source.Phase;
                Race.CountdownRemaining = source.CountdownRemaining;
                Race.Winner = source.Winner;
                Race.WinnerAt = source.WinnerAt;
                Race.PartyLastCameraLeft = source.PartyLastCameraLeft;
                Race.Flight = source.Flight;
                hits.Reset();
                effects.Reset();
                hidden.Reset();
                if (source.HiddenObstacles != null)
                {
                    Race.HiddenObstacles.Clear();
                    foreach (var pair in source.HiddenObstacles) Race.HiddenObstacles.Add(pair.Key, hidden.Copy(pair.Value));
                }
                for (int i = 0; i < Race.Players.Length; i++) RestorePlayer(source.Players[i], Race.Players[i]);
            }

            void RestorePlayer(PlayerState source, PlayerState target)
            {
                target.CopyForecastScalarsFrom(source);
                RestoreSet(source.HitObstacleIds, target.HitObstacleIds);
                RestoreSet(source.CollectedRiskIds, target.CollectedRiskIds);
                RestoreSet(source.SuccessfulRiskIds, target.SuccessfulRiskIds);
                RestoreSet(source.FailedRiskIds, target.FailedRiskIds);
                RestoreSet(source.NegativeRiskIds, target.NegativeRiskIds);
                target.LastHit = hits.Copy(source.LastHit);
                target.LastCoin = hits.Copy(source.LastCoin);
                target.LastSuccess = hits.Copy(source.LastSuccess);
                target.LastInstant = hits.Copy(source.LastInstant);
                target.LastSlow = hits.Copy(source.LastSlow);
                target.LastFinishBoost = hits.Copy(source.LastFinishBoost);
                target.ActiveEffect = effects.Copy(source.ActiveEffect);
                if (source.PendingEffects == null) return;
                target.PendingEffects.Clear();
                foreach (var effect in source.PendingEffects) target.PendingEffects.Enqueue(effects.Copy(effect));
            }

            static void RestoreSet(HashSet<string> source, HashSet<string> target)
            {
                if (source == null) return;
                target.Clear();
                foreach (string item in source) target.Add(item);
            }
        }

        // Reuse records within one buffer while retaining aliases between, for
        // example, LastSuccess and LastInstant or an active and queued effect.
        sealed class RecordCopies<T> where T : class, new()
        {
            readonly Dictionary<T, T> copies = new Dictionary<T, T>();
            readonly List<T> records = new List<T>();
            readonly Action<T, T> copy;
            int used;

            internal RecordCopies(Action<T, T> copy) { this.copy = copy; }

            internal void Reset() { copies.Clear(); used = 0; }

            internal T Copy(T source)
            {
                if (source == null) return null;
                if (copies.TryGetValue(source, out T existing)) return existing;
                if (used == records.Count) records.Add(new T());
                T target = records[used++];
                copy(source, target);
                copies.Add(source, target);
                return target;
            }
        }

        private static GameState Clone(GameState source, bool useBotForecastCamera)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            // Winner is the engine's immutable union, not an arbitrary object graph.
            if (source.Winner != null && !(source.Winner is int) && !(source.Winner is string))
                throw new ArgumentException("Winner must be null, a player ID, or a tie string.", nameof(source));
            var copies = new Dictionary<object, object>(ReferenceComparer.Instance);
            var copy = source.SnapshotShell(useBotForecastCamera);
            copies.Add(source, copy);
            if (source.HiddenObstacles != null)
            {
                copy.HiddenObstacles = new Dictionary<string, HiddenObstacleState>(source.HiddenObstacles.Comparer);
                foreach (var pair in source.HiddenObstacles)
                    copy.HiddenObstacles.Add(pair.Key, pair.Value.Copy());
            }
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
