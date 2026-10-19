using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    public static class TracksFull
    {
        public const string LongId = "full-long", ShortId = "full-short", FlowId = "full-flow", PracticeId = "full-practice";
        public static readonly Track[] All = { BuildLong(), BuildShort(), BuildFlow() };
        public static bool IsFull(string id) { return id == LongId || id == ShortId || id == FlowId; }
        public static Track Get(string id) { foreach (var track in All) if (track.Id == id) return track; return Practice(); }
        // Both named controls and custom settings use exactly the same geometry.
        public static Track Get(string id, FlightSettings flight) { return Get(id); }
        public static Track Practice(FlightSettings flight) { return Practice(); }
        static bool Fast(FlightSettings flight)
        { return flight != null && flight.FirstAction == FirstAction.Flip && flight.Law == MotionLaw.Gravity
            && flight.AirRepeats == AirRepeatLimit.One && flight.GravityAcceleration == 2700 && flight.FlipImpulse == 35; }
        static double FastTime(double distance) { return (Math.Sqrt(35*35+5400*distance)-35)/2700; }
        public static double[] SafeRoute(string id, FlightSettings flight)
        {
            if(Fast(flight) && id==LongId)return new[]{150d,2600};
            if(Fast(flight) && id==ShortId)return new[]{150d,730,3700,7270};
            if(Fast(flight) && id==FlowId)return new[]{1400d,1915,2904-110*(FastTime(12)+FastTime(200)),6705,7120};
            if(Fast(flight) && !IsFull(id)) return new[]{304-110*(FastTime(12)+FastTime(200)),2600.0};
            return Get(id).SafeRoute;
        }
        public static double[] FullRoute(string id, FlightSettings flight)
        {
            if(Fast(flight) && id==LongId)return new[]{150d,682-220*FastTime(164),900,1150,1400-220*FastTime(106),
                1700,1950-220*FastTime(186),2550,3018,3142,4032-220*FastTime(164),4350,
                4900-220*FastTime(120),4966,5970,6600-220*FastTime(106),7700,FastFinish()};
            if(Fast(flight) && id==ShortId)return new[]{150d,730,1482-220*FastTime(164),1740,2400,2680-220*FastTime(106),
                3150,3292,3650,3950-220*FastTime(120),4016,5032-220*FastTime(164),5600,5870,
                6450-220*FastTime(106),7270,7720,FastFinish()};
            if(Fast(flight) && id==FlowId)return new[]{700-220*FastTime(106),850,1182-220*FastTime(164),1900-220*FastTime(106),
                2280,2500-220*FastTime(186),2904-110*(FastTime(12)+FastTime(200)),3060,3318,3450,3670,
                4000-220*FastTime(120),4420,5032-220*FastTime(164),5550,5775,6150,6705,7120,7700,FastFinish()};
            if(Fast(flight) && !IsFull(id)) return new[]{304-110*(FastTime(12)+FastTime(200)),
                732-220*FastTime(164),1118.0,1242.0,2100-220*FastTime(106),2720.0,2880.0};
            return Get(id).RiskRoute;
        }
        public static double[] NegativeRoute(string id, FlightSettings flight) {
            if(!Fast(flight)||!IsFull(id))return NegativeRoute(id);
            var route=new List<double>(FullRoute(id,flight));route.RemoveRange(route.Count-2,2);
            double trap=id==ShortId?7640:7600;
            route.Add(trap-220*FastTime(186));
            route.Add(FastFinish());
            return route.ToArray();
        }
        static double FastFinish(){return 7876-110*(FastTime(10)+FastTime(26));}
        public static double[] NegativeRoute(string id)
        { var route=new List<double>(Get(id).RiskRoute); if(IsFull(id))route.Add(id==ShortId?7610:7570);route.Sort();return route.ToArray(); }
        static Track BuildLong()
        {
            var b=new Builder(LongId,"Длинные серии","Три длинные серии: посадки, воздушная цепочка и возврат");
            b.Low(300,"floor"); b.Landing(650,"floor"); b.Low(1050,"floor");
            b.Before(1500,"floor");b.LowRing(1950,"ceiling");
            b.High(2750,"ceiling");b.Chain(3000,"floor");b.Low(3600,"ceiling");
            b.Landing(4000,"ceiling");b.High(4500,"ceiling");b.Large(4800,"ceiling");
            b.After(5900,"ceiling");b.MiddleRing(6500,"floor");b.Low(7000,"ceiling");b.Low(7300,"ceiling");
            b.Zones.Add(new RiskZone{Id="long-hasty-trap",Kind="negative",X=2000,Y=120,Radius=8});
            b.Zones.Add(new RiskZone{Id="long-habit-trap",Kind="negative",X=7600,Y=40,Radius=8});
            return b.Finish(32,"ceiling");
        }
        static Track BuildShort()
        {
            var b=new Builder(ShortId,"Быстрые связки","Короткие пары, возвраты и крупная площадка между сериями");
            b.Low(300,"floor");b.High(700,"floor");b.Low(880,"ceiling");
            b.Landing(1450,"ceiling");b.LowRing(1750,"ceiling");b.Low(2160,"ceiling");
            b.Before(2780,"ceiling");b.Chain(3150,"floor",true);
            b.Large(3850,"floor");b.Low(4500,"floor");b.Landing(5000,"floor");
            b.After(5800,"floor");b.MiddleRing(6350,"ceiling");b.High(7000,"floor");
            b.Low(7420,"ceiling");
            b.Zones.Add(new RiskZone{Id="short-hasty-trap",Kind="negative",X=1790,Y=120,Radius=8});
            b.Zones.Add(new RiskZone{Id="short-habit-trap",Kind="negative",X=7640,Y=40,Radius=8});
            return b.Finish(32,"ceiling");
        }
        static Track BuildFlow()
        {
            var b=new Builder(FlowId,"Высотный поток","Проходы по высоте, кольца в воздухе и посадки на обеих сторонах");
            b.Passage(300,"floor",114,240);b.MiddleRing(600,"floor");b.Landing(1150,"ceiling");
            b.Passage(1550,"ceiling",0,126);b.Before(2000,"ceiling");b.LowRing(2500,"floor");
            b.Passage(2900,"floor",12,228,8);b.Chain(3300,"floor");b.Large(3900,"floor");
            b.Low(4500,"floor");b.Landing(5000,"floor");b.After(5700,"floor");
            b.Passage(6400,"ceiling",0,126);b.High(6800,"ceiling");b.Low(7200,"floor");
            b.Zones.Add(new RiskZone{Id="flow-hasty-trap",Kind="negative",X=2550,Y=120,Radius=8});
            b.Zones.Add(new RiskZone{Id="flow-habit-trap",Kind="negative",X=7600,Y=200,Radius=8});
            return b.Finish(208,"floor");
        }
        public static Track Practice()
        {
            double landingTime = (350 + Math.Sqrt(350 * 350 - 2 * 662.5 * 48)) / 662.5;
            return new Track(3300, new[] {
                new Obstacle{Id="practice-passage",Kind="gate",X=300,Width=8,GapTop=12,GapBottom=228},
                Stone("practice-pad-stone", "floor", 700, 64, 48),
                Stone("practice-before-stone","floor",2200,64,92),
                Stone("practice-after-stone","ceiling",2800,64,92) })
            {
                Id = PracticeId, Name = "Два управления", Focus = "Проход, узкая посадка, цепочка и два связанных кольца",
                SafeRoute = new[] {304-220*350/662.5,616.0,1920,1975,2600,2655},
                RiskRoute = new[] {304-220*350/662.5,732-220*landingTime,1100.0,1180,1700,1755,2000,2055,2600,2655,2880},
                RiskZones = new[] {
                    Pad("practice-pad", "practice-pad-stone", 732, 192, 32),
                    Instant("practice-chain-1", 1155, 180), Instant("practice-chain-2", 1300, 60),
                    new RiskZone{Id="practice-before",ObstacleId="practice-before-stone",X=2100,Y=120,Radius=8,ClearX=2279},
                    new RiskZone{Id="practice-after",ObstacleId="practice-after-stone",Placement="after",X=2900,Y=200,Radius=8,ClearX=2923} }
            };
        }
        static Obstacle Stone(string id, string side, double x, double w, double h)
        { return new Obstacle { Id=id, Kind=side, X=x, Width=w, Height=h }; }
        static RiskZone Pad(string id, string obstacle, double x, double y, double width)
        { return new RiskZone { Id=id, Kind="landing", ObstacleId=obstacle, X=x, Y=y, LandingWidth=width }; }
        static RiskZone Instant(string id, double x, double y)
        { return new RiskZone { Id=id, Kind="instant", X=x, Y=y, Radius=8 }; }
        sealed class Builder
        {
            public readonly List<Obstacle> Obstacles=new List<Obstacle>();
            public readonly List<RiskZone> Zones=new List<RiskZone>();
            readonly List<double> safe=new List<double>(),full=new List<double>();
            readonly string id,name,focus;
            int serial;
            public Builder(string id,string name,string focus){this.id=id;this.name=name;this.focus=focus;}
            string Id(string suffix){return id+"-"+(++serial)+"-"+suffix;}
            static double Y(string side,double floorY){return side=="floor"?floorY:240-floorY;}
            void Both(params double[] values){safe.AddRange(values);full.AddRange(values);}
            public void Low(double x,string side)
            {Obstacles.Add(Stone(Id("low"),side,x,64,48));Both(x+32-220*350/662.5);}
            public void High(double x,string side)
            {Obstacles.Add(Stone(Id("high"),side,x,64,92));Both(x-200,x-145);}
            public void Passage(double x,string side,double top,double bottom,double width=20)
            { Obstacles.Add(new Obstacle{Id=Id("passage"),Kind="gate",X=x,Width=width,GapTop=top,GapBottom=bottom});Both(x+width/2-220*350/662.5); }
            public void Landing(double x,string side)
            {
                string stone=Id("standard-pad");Obstacles.Add(Stone(stone,side,x,64,48));
                Zones.Add(Pad(stone+"-landing",stone,x+32,Y(side,192),32));
                safe.Add(x+32-220*350/662.5);
                full.Add(x+32-220*(350+Math.Sqrt(350*350-2*662.5*48))/662.5);
            }
            public void Large(double x,string side)
            {
                string stone=Id("large-pad");Obstacles.Add(Stone(stone,side,x,200,92));
                Zones.Add(Pad(stone+"-landing",stone,x+100,Y(side,148),52));
                double tau=.25,d=350*tau-.5*662.5*tau*tau,v=350-662.5*tau;
                double t=tau+(-v+Math.Sqrt(v*v+2*662.5*(120-d)))/662.5;
                full.Add(x+100-220*t);full.Add(x+155-220*t);full.Add(x+166);
                safe.Add(x+280);safe.Add(x+335);
            }
            public void Before(double x,string side)
            {
                string stone=Id("before-stone");Obstacles.Add(Stone(stone,side,x,64,92));
                Zones.Add(new RiskZone{Id=stone+"-ring",ObstacleId=stone,X=x-100,Y=Y(side,120),Radius=8,ClearX=x+79});
                full.Add(x-200);full.Add(x-145);safe.Add(x-280);safe.Add(x-225);
            }
            public void After(double x,string side)
            {
                string stone=Id("after-stone");Obstacles.Add(Stone(stone,side,x,64,92));Both(x-200,x-145);
                Zones.Add(new RiskZone{Id=stone+"-ring",ObstacleId=stone,Placement="after",X=x+100,Y=Y(side,40),Radius=8,ClearX=x+123});
                full.Add(x+80);
            }
            public void LowRing(double x,string side)
            {Zones.Add(Instant(Id("free-low"),x,Y(side,200)));full.Add(x-30);}
            public void MiddleRing(double x,string side)
            {
                Zones.Add(Instant(Id("free-mid"),x+100,Y(side,120)));
                full.Add(x);full.Add(x+55);safe.Add(x+160);safe.Add(x+215);
            }
            public void Chain(double x,string side,bool lowFirst=false)
            {
                string key=Id("chain");Zones.Add(Instant(key+"-1",x+(lowFirst?30:55),Y(side,lowFirst?200:180)));Zones.Add(Instant(key+"-2",x+200,Y(side,60)));
                full.Add(x);full.Add(x+80);safe.Add(x+250);safe.Add(x+305);
            }
            public Track Finish(double y,string side)
            {
                Zones.Add(new RiskZone{Id=id+"-finish",Kind="finish",X=7876,Y=y,FinishHeight=16});
                double displacement=side=="floor"?226-y:y-14;
                // Center the input in time across the 16-unit vertical window.
                double first=(350-Math.Sqrt(350*350-2*662.5*(displacement-8)))/662.5;
                double last=(350-Math.Sqrt(350*350-2*662.5*(displacement+8)))/662.5;
                full.Add(7876-110*(first+last));
                safe.Sort();full.Sort();Zones.Sort((a,b)=>a.X.CompareTo(b.X));Obstacles.Sort((a,b)=>a.X.CompareTo(b.X));
                return new Track(7920,Obstacles.ToArray()){Id=id,Name=name,Focus=focus,NegativeEnabled=true,RiskZones=Zones.ToArray(),SafeRoute=safe.ToArray(),RiskRoute=full.ToArray()};
            }
        }
    }
}
