using System;

namespace FlipRace.Core
{
    /// <summary>A separate control-and-instant-bonus trial; original V4 fixtures stay intact.</summary>
    public static class TracksTune
    {
        public const string ChallengeId = "v4-tune";

        public static Track Get(string id, MotionProfile profile = null)
        { return id == ChallengeId ? Challenge(profile) : TracksV4.Get(id); }

        public static double[] NegativeRoute(MotionProfile profile = null)
        {
            profile = profile ?? MotionProfiles.Baseline;
            var route = Challenge(profile).RiskRoute;
            route[6] -= 55;
            switch(profile.Id)
            {
                case "base": route[7]=3215.1; break;
                case "gravity": route[7]=3254.1; break;
                case "impulse": route[7]=3251.5; break;
            }
            return route;
        }

        public static Track Challenge(MotionProfile profile = null)
        {
            profile = profile ?? MotionProfiles.Baseline;
            double[] safe, ideal;
            switch (profile.Id)
            {
                case "base":
                    safe = new double[] {1020,1300,1640,2000,2350,2650,3010,3220,3920};
                    ideal = new double[] {1025.2,1114.9,1713.3,1980,2350,2650,2900.1,3183.2,3319.8,3710,3920,4191.6};
                    break;
                case "gravity":
                    safe = new double[] {1020,1350,1640,2000,2350,2650,3010,3220,3920};
                    ideal = new double[] {1058.6,1131.5,1723.9,1980,2350,2650,2929.8,3241.6,3339.3,3710,3920,4213.6};
                    break;
                case "impulse":
                    safe = new double[] {1020,1350,1640,2000,2350,2650,3010,3220,3920};
                    ideal = new double[] {1060,1109,1743.2,1980,2350,2650,2940,3237.8,3314.8,3710,3920,4229.3};
                    break;
                case "combined":
                    safe = new double[] {1020,1370,1640,2000,2350,2650,3010,3220,3920};
                    ideal = new double[] {1083,1128.4,1745.3,1980,2350,2650,2957.1,3279.4,3336.6,3710,3920,4239.6};
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(profile),"Unknown TUNE profile");
            }
            return new Track(4620,new[] {
                Stone("tune-entry-ceiling",900,"ceiling",92),
                Stone("tune-low-floor",1180,"floor",48),
                Stone("tune-reset-ceiling",1500,"ceiling",92),
                Stone("tune-linked-floor",1820,"floor",48),
                Stone("tune-quick-ceiling",2170,"ceiling",92),
                Stone("tune-quick-floor",2500,"floor",48),
                Stone("tune-return-ceiling",2820,"ceiling",92),
                Stone("tune-return-floor",3170,"floor",48),
                Stone("tune-high-ceiling",3400,"ceiling",92),
                Stone("tune-final-ceiling",3860,"ceiling",92),
                Stone("tune-final-floor",4070,"floor",48)
            }) {
                Id=ChallengeId, Name="Рывки и развороты",
                Focus="Два обхода камней, видимая ловушка и рывок перед финишем.",
                NegativeEnabled=true, SafeRoute=safe, RiskRoute=ideal,
                RiskZones=new[] {
                    new RiskZone {Id="tune-instant-low",Kind="instant",X=1212,Y=116,Radius=22},
                    new RiskZone {Id="tune-free-return",FreeReward=true,X=1440,Y=220,Radius=8},
                    new RiskZone {Id="tune-linked-before",ObstacleId="tune-linked-floor",Placement="before",
                        X=1770,Y=204,Radius=6,ClearX=1899},
                    new RiskZone {Id="tune-return-trap",Kind="negative",FreeReward=true,X=2945,Y=170,Radius=6},
                    new RiskZone {Id="tune-linked-after",ObstacleId="tune-return-ceiling",Placement="after",
                        X=3060,Y=51,Radius=6,ClearX=3081},
                    new RiskZone {Id="tune-instant-high",Kind="instant",X=3432,Y=164,Radius=22},
                    new RiskZone {Id="tune-instant-finish",Kind="instant",X=4310,Y=110,Radius=9}
                }
            };
        }

        static Obstacle Stone(string id,double x,string side,double height)
        { return new Obstacle {Id=id,X=x,Width=64,Kind=side,Height=height}; }
    }
}
