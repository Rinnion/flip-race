using System;
namespace FlipRace.Core
{
    // Frozen before start. Initial probabilities are an explicit uncalibrated model;
    // fixed probabilities come from a published offline measurement, never runtime feedback.
    public sealed class StochasticBotSettings
    {
        public readonly string TrackId,ProfileId;
        public readonly BotDifficulty Difficulty;
        public readonly uint Seed;
        public readonly double Quality,CollectProbability,HitProbability;
        public readonly double BaseTime,MasterTime,BonusGain,CollisionCostSum;
        StochasticBotSettings(Track track,StochasticTrackMap map,BotDifficulty difficulty,uint seed,double collect,double hit)
        {
            TrackId=track.Id;ProfileId=map.ProfileId;Difficulty=difficulty;Seed=seed;Quality=QualityFor(difficulty);
            CollectProbability=collect;HitProbability=hit;BaseTime=map.BaseTime;MasterTime=map.MasterTime;BonusGain=map.BonusGain;CollisionCostSum=map.CollisionCostSum;
        }
        static double QualityFor(BotDifficulty difficulty)
        {
            switch(difficulty){case BotDifficulty.Novice:return .5;case BotDifficulty.Amateur:return .65;case BotDifficulty.Confident:return .75;case BotDifficulty.Strong:return .9;case BotDifficulty.Master:return 1;default:throw new ArgumentOutOfRangeException(nameof(difficulty));}
        }
        static void Probability(double value,string name)
        {if(double.IsNaN(value)||double.IsInfinity(value)||value<0||value>1)throw new ArgumentOutOfRangeException(name,"A finite probability in [0,1] is required; no clamping.");}
        public static StochasticBotSettings CreateInitial(Track track,FlightSettings flight,BotDifficulty difficulty,uint seed)
        {
            double quality=QualityFor(difficulty);var map=StochasticTrackMap.Create(track,flight);
            double hit=difficulty==BotDifficulty.Master?0:map.BonusGain/map.CollisionCostSum*(1-quality);
            if(double.IsNaN(hit)||double.IsInfinity(hit)||hit<0||hit>1)throw new NotSupportedException("Initial hit probability is outside [0,1]; configuration is unsupported.");
            return new StochasticBotSettings(track,map,difficulty,seed,quality,hit);
        }
        public static StochasticBotSettings CreateFixed(Track track,FlightSettings flight,BotDifficulty difficulty,uint seed,double collectProbability,double hitProbability)
        {
            QualityFor(difficulty);Probability(collectProbability,nameof(collectProbability));Probability(hitProbability,nameof(hitProbability));
            if(difficulty==BotDifficulty.Master&&(collectProbability!=1||hitProbability!=0))throw new ArgumentException("Master requires collection probability1 and hit probability0.");
            var map=StochasticTrackMap.Create(track,flight);return new StochasticBotSettings(track,map,difficulty,seed,collectProbability,hitProbability);
        }
        internal StochasticTrackMap RequireCompatible(Track track,FlightSettings flight)
        {
            var map=StochasticTrackMap.Create(track,flight);
            if(TrackId!=track.Id||ProfileId!=map.ProfileId||BaseTime!=map.BaseTime||MasterTime!=map.MasterTime||BonusGain!=map.BonusGain||CollisionCostSum!=map.CollisionCostSum)
                throw new ArgumentException("Stochastic settings do not match this exact track/profile map.");
            return map;
        }
        internal bool SameAs(StochasticBotSettings other)
        {return other!=null&&TrackId==other.TrackId&&ProfileId==other.ProfileId&&Difficulty==other.Difficulty&&Seed==other.Seed&&Quality==other.Quality&&CollectProbability==other.CollectProbability&&HitProbability==other.HitProbability&&BaseTime==other.BaseTime&&MasterTime==other.MasterTime&&BonusGain==other.BonusGain&&CollisionCostSum==other.CollisionCostSum;}
    }
}