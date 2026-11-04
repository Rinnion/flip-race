using System;

namespace FlipRace.Core
{
    public enum TrackProfilePolicy { Any, ChosenOnly }
    public enum TrackWarningMode { Selected, Authored }

    public sealed class TrackBotReference
    {
        readonly double[] inputs;
        readonly string[] rewardIds;
        public string ProfileId { get; private set; }
        public string TrackHash { get; private set; }
        public double[] Inputs { get { return (double[])inputs.Clone(); } }
        public string[] RewardIds { get { return (string[])rewardIds.Clone(); } }
        public double BonusGain { get; private set; }
        public TrackBotReference(string profileId,string trackHash,double[] inputs,string[] rewardIds,double bonusGain)
        {
            ProfileId=profileId;TrackHash=trackHash;this.inputs=(double[])(inputs??Array.Empty<double>()).Clone();
            this.rewardIds=(string[])(rewardIds??Array.Empty<string>()).Clone();BonusGain=bonusGain;
        }
    }

    // Track is the existing read-only-after-creation physics input. Reference
    // trust/origin belongs to the catalogue, never to a flag in an imported file.
    public sealed class TrackPackage
    {
        readonly TrackBotReference[] references;
        public Track Track { get; private set; }
        public string ContentHash { get; private set; }
        public TrackProfilePolicy ProfilePolicy { get; private set; }
        public TrackWarningMode WarningMode { get; private set; }
        public TrackBotReference[] References { get { return (TrackBotReference[])references.Clone(); } }
        public TrackPackage(Track track,TrackProfilePolicy profilePolicy=TrackProfilePolicy.Any,
            TrackWarningMode warningMode=TrackWarningMode.Selected,TrackBotReference[] references=null)
        {
            if(track==null)throw new ArgumentNullException(nameof(track));
            Track=track;ProfilePolicy=profilePolicy;WarningMode=warningMode;
            this.references=(TrackBotReference[])(references??Array.Empty<TrackBotReference>()).Clone();
            ContentHash=TrackFingerprint.Compute(track,profilePolicy,warningMode);
        }
        public bool SupportsProfile(FlightSettings flight)
        { return flight!=null&&(ProfilePolicy==TrackProfilePolicy.Any||MatchesProfile("Chosen",flight)); }
        public bool TryGetReference(FlightSettings flight,out TrackBotReference reference)
        {
            reference=null;if(!SupportsProfile(flight))return false;
            foreach(var candidate in references)if(candidate!=null&&MatchesProfile(candidate.ProfileId,flight)){reference=candidate;return true;}
            return false;
        }
        public static bool MatchesProfile(string id,FlightSettings flight)
        {
            var expected=id=="Chosen"?FlightPresets.Chosen:id=="FastFlip"?FlightPresets.FastFlip:null;
            return expected!=null&&flight!=null&&flight.FirstAction==expected.FirstAction&&flight.Law==expected.Law
                &&flight.AirRepeats==expected.AirRepeats&&flight.GravityAcceleration==expected.GravityAcceleration
                &&flight.FlipImpulse==expected.FlipImpulse&&flight.JumpImpulse==expected.JumpImpulse
                &&flight.LinearSpeed==expected.LinearSpeed&&flight.LinearJumpHeight==expected.LinearJumpHeight
                &&flight.LegacyLinear==expected.LegacyLinear;
        }
    }

    public sealed class TrackManifest
    {
        readonly string[] files;
        public string[] Files { get { return (string[])files.Clone(); } }
        public TrackManifest(string[] files){this.files=(string[])(files??Array.Empty<string>()).Clone();}
    }
}
