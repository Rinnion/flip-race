using System;

namespace FlipRace.Core
{
    /// <summary>
    /// Two short authored experiments. The open control comparison is kept
    /// separate from the obstacle/ring challenge; legacy full tracks are untouched.
    /// </summary>
    public static class TracksV4
    {
        public const string OpenId = "v4-open";
        public const string ChallengeId = "v4-challenge";

        public static bool IsOpen(string id) { return id == OpenId; }

        public static Track Get(string id)
        {
            if(id==OpenId)return Open();
            if(id==ChallengeId)return Challenge();
            throw new ArgumentOutOfRangeException(nameof(id),"Unknown V4 trial segment");
        }

        public static Track Open()
        {
            return new Track(2200, Array.Empty<Obstacle>())
            {
                Id = OpenId,
                Name = "Свободный полёт",
                Focus = "Сравни три управления: первый флип, ранний возврат и поздний разворот."
            };
        }

        public static Track Challenge()
        {
            return new Track(4400,new[] {
                Stone("v4-entry-ceiling",900,"ceiling",92),
                Stone("v4-shallow-ceiling",1230,"ceiling",48),
                Stone("v4-linked-floor",1500,"floor",48),
                Stone("v4-tall-ceiling",1850,"ceiling",92),
                Stone("v4-quick-floor",2120,"floor",48),
                Stone("v4-return-ceiling",2440,"ceiling",92),
                Stone("v4-return-floor",2850,"floor",48),
                Stone("v4-high-hop-floor",3230,"floor",48),
                Stone("v4-final-ceiling",3760,"ceiling",92),
                Stone("v4-final-floor",4100,"floor",48)
            },new[] {
                new Cue(180,"Сначала почувствуй управление"),
                new Cue(920,"Низкий подскок — и обратно"),
                new Cue(1580,"Три быстрых смены стороны"),
                new Cue(2320,"Не спеши возвращаться: впереди ловушка"),
                new Cue(2890,"Теперь высокий подскок с возвратом")
            }) {
                Id=ChallengeId,Name="Кольца и развороты",
                Focus="Короткая проба: подскоки, быстрая связка и ловушка раннего возврата.",
                NegativeEnabled=true,
                RiskZones=new[] {
                    new RiskZone{Id="v4-free-low",Kind="positive",FreeReward=true,
                        X=1115,Y=181,Radius=6,SafeFlipX=1320,RiskFlipX=1000},
                    new RiskZone{Id="v4-linked-before",Kind="positive",ObstacleId="v4-linked-floor",
                        Placement="before",X=1450,Y=204,Radius=6,ClearX=1579,SafeFlipX=1320,RiskFlipX=1393},
                    new RiskZone{Id="v4-return-trap",Kind="negative",FreeReward=true,
                        X=2565,Y=170,Radius=6,SafeFlipX=2640,RiskFlipX=2520},
                    new RiskZone{Id="v4-linked-after",Kind="positive",ObstacleId="v4-return-ceiling",
                        Placement="after",X=2680,Y=51,Radius=6,ClearX=2701,SafeFlipX=2640,RiskFlipX=2520},
                    new RiskZone{Id="v4-free-high",Kind="positive",FreeReward=true,
                        X=3160,Y=110,Radius=8,SafeFlipX=3600,RiskFlipX=3000}
                },
                SafeRoute=new double[]{1320,1690,1990,2280,2640,3600,3960},
                RiskRoute=new double[]{1000,1057.2,1393,1670,1990,2280,2520,3000,3083.6,3600,3960}
            };
        }

        static Obstacle Stone(string id,double x,string side,double height)
        {return new Obstacle{Id=id,X=x,Width=64,Kind=side,Height=height};}
    }
}
