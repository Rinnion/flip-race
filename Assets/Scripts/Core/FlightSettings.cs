using System;

namespace FlipRace.Core
{
    public enum FirstAction { Flip, Jump }
    public enum MotionLaw { Linear, Gravity }
    public enum AirRepeatLimit { Unlimited = -1, None = 0, One = 1 }

    // Immutable physical values, independent of menu levels and persistence.
    // Hidden parameters stay in the snapshot but do not affect the chosen law.
    public sealed class FlightSettings
    {
        public readonly FirstAction FirstAction;
        public readonly MotionLaw Law;
        public readonly AirRepeatLimit AirRepeats;
        public readonly double GravityAcceleration, FlipImpulse, JumpImpulse, LinearSpeed, LinearJumpHeight;
        internal readonly bool LegacyLinear;

        public FlightSettings(FirstAction firstAction, MotionLaw law, AirRepeatLimit airRepeats,
            double gravityAcceleration = 1000, double flipImpulse = 70, double jumpImpulse = 400,
            double linearSpeed = 265, double linearJumpHeight = 112)
            : this(firstAction, law, airRepeats, gravityAcceleration, flipImpulse, jumpImpulse, linearSpeed, linearJumpHeight, false)
        { Positive(jumpImpulse, nameof(jumpImpulse)); }

        internal FlightSettings(FirstAction firstAction, MotionLaw law, AirRepeatLimit airRepeats,
            double gravityAcceleration, double flipImpulse, double jumpImpulse, double linearSpeed,
            double linearJumpHeight, bool legacyLinear)
        {
            if (!Enum.IsDefined(typeof(FirstAction), firstAction)) throw new ArgumentOutOfRangeException(nameof(firstAction));
            if (!Enum.IsDefined(typeof(MotionLaw), law)) throw new ArgumentOutOfRangeException(nameof(law));
            if (!Enum.IsDefined(typeof(AirRepeatLimit), airRepeats)) throw new ArgumentOutOfRangeException(nameof(airRepeats));
            Positive(gravityAcceleration, nameof(gravityAcceleration)); Positive(linearSpeed, nameof(linearSpeed));
            Positive(linearJumpHeight, nameof(linearJumpHeight)); Nonnegative(flipImpulse, nameof(flipImpulse));
            Nonnegative(jumpImpulse, nameof(jumpImpulse));
            FirstAction = firstAction; Law = law; AirRepeats = airRepeats;
            GravityAcceleration = gravityAcceleration; FlipImpulse = flipImpulse; JumpImpulse = jumpImpulse;
            LinearSpeed = linearSpeed; LinearJumpHeight = linearJumpHeight; LegacyLinear = legacyLinear;
        }
        static void Positive(double value, string name)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(name); }
        static void Nonnegative(double value, string name)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
    }

    public static class FlightPresets
    {
        public static readonly FlightSettings Chosen = new FlightSettings(FirstAction.Jump, MotionLaw.Gravity, AirRepeatLimit.Unlimited, 662.5, 0, 350);
        public static readonly FlightSettings Moderate = new FlightSettings(FirstAction.Flip, MotionLaw.Gravity, AirRepeatLimit.One);
        public static readonly FlightSettings Linear = new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.One, 662.5, 0);
        public static readonly FlightSettings Jump = new FlightSettings(FirstAction.Jump, MotionLaw.Gravity, AirRepeatLimit.One, 662.5);
        public static readonly FlightSettings Unlimited = new FlightSettings(FirstAction.Flip, MotionLaw.Gravity, AirRepeatLimit.Unlimited);

        public static FlightSettings FromLegacy(MotionMode mode, MotionProfile profile = null)
        {
            if (!Enum.IsDefined(typeof(MotionMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            profile = profile ?? (mode == MotionMode.JumpGravityFlip ? MotionProfiles.Jump
                : mode == MotionMode.GravityUnlimited ? MotionProfiles.Moderate : MotionProfiles.Baseline);
            bool jump = mode == MotionMode.JumpGravityFlip;
            bool linear = mode == MotionMode.Linear || mode == MotionMode.LinearAirFlip;
            var repeats = mode == MotionMode.Linear || mode == MotionMode.Gravity ? AirRepeatLimit.None
                : mode == MotionMode.GravityUnlimited ? AirRepeatLimit.Unlimited : AirRepeatLimit.One;
            return new FlightSettings(jump ? FirstAction.Jump : FirstAction.Flip, linear ? MotionLaw.Linear : MotionLaw.Gravity,
                repeats, profile.GravityAcceleration, jump ? 70 : profile.DepartureImpulse,
                jump ? profile.DepartureImpulse : 400, RaceConfig.LinearVerticalSpeed, 112, mode == MotionMode.Linear);
        }
    }

    public sealed class FlightMeasurement
    {
        public readonly double Duration, MaxDistance, EndedAt;
        public readonly bool Interrupted;
        public readonly int OriginSide;
        public readonly string OriginObstacleId;
        public FlightMeasurement(double duration, double maxDistance, bool interrupted, double endedAt,
            int originSide, string originObstacleId)
        { Duration = duration; MaxDistance = maxDistance; Interrupted = interrupted; EndedAt = endedAt;
            OriginSide = originSide; OriginObstacleId = originObstacleId; }
    }
}
