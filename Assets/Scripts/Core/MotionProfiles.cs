using System;

namespace FlipRace.Core
{
    // Shared immutable settings: snapshot copies can retain this reference.
    public sealed class MotionProfile
    {
        public readonly string Id, Name;
        public readonly double GravityAcceleration, DepartureImpulse;

        public MotionProfile(string id, string name, double gravityAcceleration, double departureImpulse)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Profile id is required", nameof(id));
            if (double.IsNaN(gravityAcceleration) || double.IsInfinity(gravityAcceleration) || gravityAcceleration <= 0)
                throw new ArgumentOutOfRangeException(nameof(gravityAcceleration));
            if (double.IsNaN(departureImpulse) || double.IsInfinity(departureImpulse) || departureImpulse < 0)
                throw new ArgumentOutOfRangeException(nameof(departureImpulse));
            Id = id; Name = name; GravityAcceleration = gravityAcceleration; DepartureImpulse = departureImpulse;
        }
    }

    public static class MotionProfiles
    {
        public static readonly MotionProfile Baseline = new MotionProfile("base", "База", 662.5, 0);
        public static readonly MotionProfile StrongGravity = new MotionProfile("gravity", "Сильнее тяга", 1000, 0);
        public static readonly MotionProfile Impulse = new MotionProfile("impulse", "Толчок", 662.5, 140);
        public static readonly MotionProfile Combined = new MotionProfile("combined", "Тяга + толчок", 1000, 140);
        public static readonly MotionProfile[] All = { Baseline, StrongGravity, Impulse, Combined };

        public static MotionProfile Get(string id)
        {
            foreach (var profile in All) if (profile.Id == id) return profile;
            throw new ArgumentOutOfRangeException(nameof(id), "Unknown motion profile");
        }
    }
}
