using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    // MIX-02 mixed course; author witnesses below are offline only.
    public static class TracksMixed
    {
        public const string Id = "mixed-tricks";
        public static Track Get(FlightSettings flight) { return Get(); }
        public static Track Get()
        {
            return new Track(7920, new[] {
                Stone("small-pad", "floor", 600, 64, 48), Stone("small-second", "floor", 1050, 64, 48),
                Stone("large-before", "floor", 2050, 200, 92),
                new Obstacle { Id=Id+"-straight",Kind="gate",X=3000,Width=8,GapTop=48,GapBottom=228 },
                Low("low-floor",3550,true), Offset("middle-offset",4050,true),
                Stone("large-pad", "floor", 5200, 200, 92), Stone("after-stone", "floor", 5900, 64, 92),
                Stone("small-ceiling", "ceiling", 6800, 64, 48),
                Offset("final-offset",7260,false), Low("low-ceiling",7700,false)
            }, new[] {new Cue(350,"Малые камни: вернись на свою сторону"),new Cue(1600,"Большой камень: выбери другую сторону"),
                new Cue(2600,"Прямое окно и низкий порог"),new Cue(3900,"Приманка и две высоты"),
                new Cue(4900,"Точная посадка и возвращение"),new Cue(6500,"Знакомые приёмы вместе")}) {
                Id=Id,Name="Полоса уловок",Focus="Малые прыжки, разные окна, приманки и точные посадки",
                NegativeEnabled=true, SafeRoute=SafeRoute(FlightPresets.Chosen), RiskRoute=FullRoute(FlightPresets.Chosen),
                RiskZones=new[] {
                    Pad("standard-pad","small-pad",632,192,32), Instant("low-reward",1350,200),
                    new RiskZone{Id=Id+"-before",ObstacleId=Id+"-large-before",X=1950,Y=120,Radius=8,ClearX=2265},
                    Instant("high-reward",2700,60), Instant("chain-center",4400,120),Instant("chain-low",4570,180),
                    Pad("wide-pad","large-pad",5300,148,52),
                    new RiskZone{Id=Id+"-after",ObstacleId=Id+"-after-stone",Placement="after",X=6080,Y=120,Radius=8,ClearX=5979},
                    Instant("ceiling-low-reward",6900,40),
                    new RiskZone{Id=Id+"-negative-middle",Kind="negative",X=3820,Y=180,Radius=8},
                    new RiskZone{Id=Id+"-negative-final",Kind="negative",X=6500,Y=180,Radius=8},
                    new RiskZone{Id=Id+"-coin-first",Kind="coin",X=3250,Y=150,Radius=8},
                    new RiskZone{Id=Id+"-coin-second",Kind="coin",X=5500,Y=100,Radius=8},
                    new RiskZone{Id=Id+"-negative-first-choice",Kind="negative",X=3300,Y=180,Radius=8},
                    new RiskZone{Id=Id+"-negative-second-choice",Kind="negative",X=5720,Y=180,Radius=8},
                    new RiskZone{Id=Id+"-finish",Kind="finish",X=7780,Y=32,FinishWidth=28,FinishHeight=8}
                }
            };
        }
        // Offline author witnesses. Simulation never reads these inputs.
        public static double[] SafeRoute(FlightSettings flight)
        {
            return flight.FirstAction==FirstAction.Flip ? new double[] {
                530,596,980,1046,1940,2560,2952.3,2988.6,3300,3526.917064816087,4055.150321356292,
                6080,7030,7265.150321356292,7676.917064816087
            } : new double[] {
                515.7735849056604,965.7735849056604,1910,1954,2560,2604,2887.7735849056603,3441.7735849056603,
                4030.844877314131,4074.844877314132,
                6730.773584905661,7240.844877314131,7284.844877314127,7420,7464,7591.773584905661
            };
        }
        public static double[] FullRoute(FlightSettings flight)
        {
            return flight.FirstAction==FirstAction.Flip ? new double[] {
                430,558.1196111528561,980,1046,1320,1480,1891.1393785965915,2662.141678133602,2952.3,2988.6,3300,
                3526.917064816087,4055.150321356292,4341.139378596592,4513.2125172004025,5237.5,
                5785.5,6030.5,6400,6730,6796,7265.150321356292,7676.917064816087
            } : new double[] {
                435.1811187353192,965.7735849056604,1320,1866.8139808283297,1910.8139808283306,2666.1590934524424,
                2722.20159047915,2887.7735849056603,3441.7735849056603,4030.844877314131,4074.844877314132,
                4316.8139808283295,4360.813980828326,4519.238640178664,5026,5064,5092,5765,5809,6004,6055,6400,6444,
                6693,7240.844877314131,7284.844877314127,7420,7464,7591.773584905661
            };
        }
        public static double[] NegativeRoute(FlightSettings flight, int mask=3)
        {
            bool fast=flight.FirstAction==FirstAction.Flip;
            var route=new List<double>(FullRoute(flight));
            if((mask&1)!=0) { if(fast){route.Add(3702.5);route.Add(3739.9);}else route.Add(3762.5); }
            if((mask&2)!=0) {
                route.RemoveAll(x=>Math.Abs(x-6400)<1e-6||Math.Abs(x-6444)<1e-6);
                if(fast)route.Add(6445);else{route.Add(6442.5);route.Add(6475.5);}
            }
            route.Sort();return route.ToArray();
        }
        // Optional local collection witnesses; no runtime control reads them.
        // These preserve every original positive reward and avoid all traps.
        public static double[] CoinRoute(FlightSettings flight, int mask=3)
        {
            bool fast=flight.FirstAction==FirstAction.Flip;
            var route=new List<double>(FullRoute(flight));
            if((mask&1)!=0){route.Add(fast?3150:3180);if(fast)route.Add(3185);}
            if((mask&2)!=0){
                route.Add(fast?5415:5400);
                route.RemoveAll(x=>Math.Abs(x-(fast?5785.5:5765))<1e-6||(!fast&&Math.Abs(x-5809)<1e-6));
            }
            route.Sort();return route.ToArray();
        }
        // Optional error branches for the two new ordinary slow zones. Existing
        // negatives and the physical obstacle catalogue remain unchanged.
        public static double[] ContentTrapRoute(FlightSettings flight, int mask=3)
        {
            bool fast=flight.FirstAction==FirstAction.Flip;
            var route=new List<double>(FullRoute(flight));
            if((mask&1)!=0){
                if(fast){route.Remove(3300);route.Add(3260);}
                else{route.Add(3250);route.Remove(3441.7735849056603);route.Add(3485);}
            }
            if((mask&2)!=0){
                if(fast){route.Remove(5785.5);route.Add(5680);}
                else{route.Add(5680);route.Remove(5809);}
            }
            route.Sort();return route.ToArray();
        }
        static Obstacle Stone(string id,string side,double x,double width,double height)
        {return new Obstacle{Id=Id+"-"+id,Kind=side,X=x,Width=width,Height=height};}
        static Obstacle Low(string id,double x,bool floor)
        {return new Obstacle{Id=Id+"-"+id,Kind="gate",X=x,Width=16,GapTop=floor?114:16,GapBottom=floor?224:126,TopOffsetX=floor?80:0,BottomOffsetX=floor?0:80};}
        static Obstacle Offset(string id,double x,bool floor)
        {return new Obstacle{Id=Id+"-"+id,Kind="gate",X=x,Width=16,GapTop=110,GapBottom=130,TopOffsetX=floor?0:112,BottomOffsetX=floor?112:0};}
        static RiskZone Pad(string id,string stone,double x,double y,double width)
        {return new RiskZone{Id=Id+"-"+id,Kind="landing",ObstacleId=Id+"-"+stone,X=x,Y=y,LandingWidth=width};}
        static RiskZone Instant(string id,double x,double y)
        {return new RiskZone{Id=Id+"-"+id,Kind="instant",X=x,Y=y,Radius=8};}
    }
}
