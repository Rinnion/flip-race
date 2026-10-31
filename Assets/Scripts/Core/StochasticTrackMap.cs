using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    public enum BotOpportunityKind { Obstacle, Positive, Finish }

    // Immutable snapshot of one logical chance. Gate halves share one obstacle ID.
    public sealed class BotOpportunity
    {
        public readonly string Id, ObstacleId;
        public readonly BotOpportunityKind Kind;
        public readonly double Position, EndPosition;
        internal BotOpportunity(string id, BotOpportunityKind kind, double position, double endPosition, string obstacleId)
        { Id = id; Kind = kind; Position = position; EndPosition = endPosition; ObstacleId = obstacleId; }
    }

    // Lightweight orientation data only: no catalogue initialization and no simulation.
    // ReferenceInputs is one authored clean witness, never a library of outcome suffixes.
    public sealed class StochasticTrackMap
    {
        readonly BotOpportunity[] opportunities;
        readonly double[] referenceInputs;
        public readonly string ProfileId;
        public readonly double BaseTime, MasterTime, BonusGain, CollisionCostSum;
        public BotOpportunity[] Opportunities { get { return (BotOpportunity[])opportunities.Clone(); } }
        public double[] ReferenceInputs { get { return (double[])referenceInputs.Clone(); } }

        StochasticTrackMap(string profileId, BotOpportunity[] items, double[] inputs, double length, int ordinaryRewards, int obstacleCount)
        {
            ProfileId = profileId; opportunities = items; referenceInputs = inputs;
            BaseTime = length / RaceConfig.Speed;
            // All nine ordinary effects and the full finish effect are served in the
            // validated clean witnesses. These are actual time gains, not effect lengths.
            BonusGain = ordinaryRewards * RaceConfig.BoostTimeGain
                + RaceConfig.FinishBoostDuration * (RaceConfig.FinishBoostSpeed / RaceConfig.Speed - 1);
            MasterTime = BaseTime - BonusGain;
            CollisionCostSum = obstacleCount * RaceConfig.RecoveryDuration;
            if (BonusGain <= 0 || CollisionCostSum <= 0)
                throw new NotSupportedException("Stochastic probabilities require positive bonus gain and collision cost.");
            if (BonusGain / CollisionCostSum * .5 > 1)
                throw new NotSupportedException("Initial hit probability is outside [0,1] at the supported minimum q=.5; no clamping is permitted.");
        }

        public static StochasticTrackMap Create(Track track, FlightSettings flight)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (flight == null) throw new ArgumentNullException(nameof(flight));
            bool chosen = Same(flight, FlightPresets.Chosen), fast = Same(flight, FlightPresets.FastFlip);
            if (!chosen && !fast) throw new NotSupportedException("Only exact Chosen and FastFlip flight settings are supported.");
            bool full = track.Id == TracksFull.LongId || track.Id == TracksFull.ShortId || track.Id == TracksFull.FlowId;
            bool cascade = track.Id == TracksRhythm.CascadeId, mixed = track.Id == TracksMixed.Id;
            if (!full && !cascade && !mixed) throw new NotSupportedException("Only the five authored full tracks are supported.");
            if (double.IsNaN(track.Length) || double.IsInfinity(track.Length) || track.Length <= 0)
                throw new ArgumentException("A finite positive track length is required.", nameof(track));
            var list = new List<BotOpportunity>();
            var bounds = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var obstacle in track.Obstacles)
            {
                if (string.IsNullOrEmpty(obstacle.Id)) throw new ArgumentException("Obstacle ID is required.", nameof(track));
                double left = ObstacleGeometry.Left(obstacle), right = ObstacleGeometry.Right(obstacle);
                double[] value;
                if (!bounds.TryGetValue(obstacle.Id, out value)) bounds.Add(obstacle.Id, new[] { left, right });
                else { value[0] = Math.Min(value[0], left); value[1] = Math.Max(value[1], right); }
            }
            foreach (var pair in bounds)
                list.Add(new BotOpportunity("obstacle:" + pair.Key, BotOpportunityKind.Obstacle,
                    pair.Value[0], pair.Value[1] + RaceConfig.PlayerRadius + 1e-7, pair.Key));
            var zoneIds = new HashSet<string>(StringComparer.Ordinal); int ordinaryRewards = 0, finishCount = 0;
            foreach (var zone in track.RiskZones)
            {
                if (zone.IsNegative || zone.IsCoin) continue;
                if (string.IsNullOrEmpty(zone.Id) || !zoneIds.Add(zone.Id))
                    throw new ArgumentException("Unique reward IDs are required.", nameof(track));
                double end;
                if (zone.IsFinish)
                {
                    finishCount++;
                    // Latest geometrically possible stroke contact. Pulse completion is
                    // deliberately NOT represented by this spatial contact boundary.
                    end = zone.X + FinishTargetGeometry.HalfWidth + RaceConfig.PlayerRadius + FinishTargetGeometry.StrokeWidth / 2;
                }
                else if (zone.IsLanding)
                {
                    ordinaryRewards++; end = double.NegativeInfinity;
                    foreach (var obstacle in track.Obstacles) if (obstacle.Id == zone.ObstacleId)
                        end = Math.Max(end, Math.Min(obstacle.X + obstacle.Width, zone.X + zone.LandingWidth / 2));
                    if (double.IsInfinity(end)) throw new ArgumentException("Landing reward requires its physical obstacle.", nameof(track));
                }
                else
                {
                    ordinaryRewards++; end = zone.X + zone.Radius + RaceConfig.PlayerRadius;
                    if (!zone.IsInstant && !zone.FreeReward)
                    {
                        end = Math.Max(end, zone.ClearX);
                        double[] linked;
                        if (zone.ObstacleId != null && bounds.TryGetValue(zone.ObstacleId, out linked))
                            end = Math.Max(end, linked[1] + RaceConfig.PlayerRadius + 1e-7);
                    }
                }
                list.Add(new BotOpportunity((zone.IsFinish ? "finish:" : "positive:") + zone.Id,
                    zone.IsFinish ? BotOpportunityKind.Finish : BotOpportunityKind.Positive, zone.X, end, zone.ObstacleId));
            }
            if (finishCount != 1) throw new NotSupportedException("One finish opportunity is required.");
            list.Sort((a, b) => { int n = a.Position.CompareTo(b.Position); return n != 0 ? n : string.CompareOrdinal(a.Id, b.Id); });
            double[] route = mixed ? TracksMixed.FullRoute(flight) : cascade ? TracksRhythm.FullRoute(track.Id, flight) : TracksFull.FullRoute(track.Id, flight);
            var inputs = new List<double>(route);
            double aim = mixed ? (chosen ? 7639 : 7650) : cascade ? (chosen ? 7758 : 7724) : (chosen ? 7736 : 7706);
            if (mixed) inputs[inputs.Count - 1] = aim;
            else { if (full) inputs.RemoveAll(x => x >= 7500); inputs.Add(aim); }
            inputs.Sort();
            return new StochasticTrackMap(chosen ? "Chosen" : "FastFlip", list.ToArray(), inputs.ToArray(), track.Length, ordinaryRewards, bounds.Count);
        }

        static bool Same(FlightSettings a, FlightSettings b)
        {
            return a.FirstAction == b.FirstAction && a.Law == b.Law && a.AirRepeats == b.AirRepeats
                && a.GravityAcceleration == b.GravityAcceleration && a.FlipImpulse == b.FlipImpulse
                && a.JumpImpulse == b.JumpImpulse && a.LinearSpeed == b.LinearSpeed
                && a.LinearJumpHeight == b.LinearJumpHeight && a.LegacyLinear == b.LegacyLinear;
        }
    }
}