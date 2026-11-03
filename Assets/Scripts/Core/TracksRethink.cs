using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    // Human-only course and offline author witnesses. No simulation reads tapes.
    public static class TracksRethink
    {
        public const string Id = "rethink";
        public static bool IsSupported(FlightSettings flight)
        {
            var chosen = FlightPresets.Chosen;
            return flight != null && !flight.LegacyLinear && flight.FirstAction == chosen.FirstAction
                && flight.Law == chosen.Law && flight.AirRepeats == chosen.AirRepeats
                && flight.GravityAcceleration == chosen.GravityAcceleration && flight.FlipImpulse == chosen.FlipImpulse
                && flight.JumpImpulse == chosen.JumpImpulse && flight.LinearSpeed == chosen.LinearSpeed
                && flight.LinearJumpHeight == chosen.LinearJumpHeight;
        }
        public static Track Get()
        {
            var first=FirstThree();var obstacles=new List<Obstacle>(first.Obstacles) {
                new Obstacle { Id=Id+"-m4-entry",Kind="gate",X=4420,Width=16,GapTop=18,GapBottom=222,BottomOffsetX=80 },
                Stone("m4-no-return","floor",4620,160,100,true),
                Stone("m4-large","ceiling",5000,120,108),
                new Obstacle { Id=Id+"-m4-exit",Kind="gate",X=5240,Width=16,GapTop=18,GapBottom=222 },
                Stone("m5-cancel-landing","ceiling",5740,180,72,true),
                Stone("m5-exit","floor",6060,100,70),
                Stone("m6-pad","ceiling",6860,200,48),
                Stone("m6-again","ceiling",7280,64,48,true)
            };
            obstacles.Find(o=>o.Id==Id+"-m5-cancel-landing").HiddenRevealDistance=260;
            var rewards=new List<RiskZone>(first.RiskZones) {
                new RiskZone {Id=Id+"-m6-landing",Kind="landing",ObstacleId=Id+"-m6-pad",X=6892,Y=48,LandingWidth=32},
                new RiskZone {Id=Id+"-finish",Kind="finish",X=7790,Y=120}
            };
            return new Track(7920,obstacles.ToArray(),new[]{new Cue(170,"Передумай"),new Cue(1350,"Эпизод 2"),new Cue(2650,"Эпизод 3"),
                new Cue(4110,"Эпизод 4"),new Cue(5310,"Эпизод 5"),new Cue(6650,"Эпизод 6")}) {
                Id=Id,Name="Передумай",Focus="Неожиданные продолжения для Основного управления",
                SafeRoute=SafeRoute(),RiskRoute=LandingRoute(),RiskZones=rewards.ToArray()
            };
        }
        public static Track ModuleOne()
        {
            return new Track(1400, new[] {
                new Obstacle { Id=Id+"-m1-entry", Kind="gate", X=460, Width=16,
                    GapTop=18, GapBottom=222, TopOffsetX=80 },
                Stone("m1-no-return", "ceiling", 660, 160, 100, true),
                Stone("m1-exit", "floor", 1040, 120, 108)
            }, new[] { new Cue(170,"Передумай") }) {
                Id=Id, Name="Передумай", Focus="Поздний сигнал меняет уже выбранное действие",
                SafeRoute=ModuleOneRoute(), RiskRoute=ModuleOneRoute()
            };
        }
        public static double[] ModuleOneRoute() { return new double[] { 360, 900, 944, 1190, 1234 }; }
        public static Track FirstThree()
        {
            var obstacles=new List<Obstacle>(ModuleOne().Obstacles) {
                Stone("m2-cancel-landing","floor",1780,180,72,true),
                Stone("m2-exit","ceiling",2100,100,70),
                Stone("m3-price","floor",3020,64,48,true),
                Stone("m3-large","floor",3500,120,108),
                TracksRhythm.LowPassage(Id+"-m3-exit",3820,false)
            };
            // Reveal after the already-started jump, with room to reverse momentum.
            obstacles.Find(o=>o.Id==Id+"-m2-cancel-landing").HiddenRevealDistance=260;
            return new Track(3960,obstacles.ToArray(),new[]{new Cue(170,"Передумай"),new Cue(1350,"Эпизод 2"),new Cue(2650,"Эпизод 3")}) {
                Id=Id,Name="Передумай",Focus="Отказ от тапа, отмена посадки и цена ускорения",
                SafeRoute=FirstThreeRoute(),RiskRoute=AirRoute(),
                RiskZones=new[]{new RiskZone{Id=Id+"-m2-air",Kind="instant",X=2250,Y=120,Radius=8},
                    new RiskZone{Id=Id+"-m3-price-bonus",Kind="instant",X=2800,Y=208,Radius=8}}
            };
        }
        public static double[] FirstThreeRoute()
        { return new double[]{360,900,944,1190,1234,1460,1600,1960,2004,2684,2935.7735849056603,3400,3444,3711.7735849056603}; }
        public static double[] AirRoute()
        { var route=new List<double>(SafeRoute()){2140};route.Sort();return route.ToArray(); }
        public static double[] BonusRoute()
        {
            var route=new List<double>(SafeRoute());route.Remove(2684);route.Remove(2935.7735849056603);route.Add(2920);route.Sort();return route.ToArray();
        }
        public static double[] SafeRoute()
        {
            var route=new List<double>(FirstThreeRoute()) {4320,4860,4904,5150,5194,5420,5560,5920,5964,6740,6784,7050,7094,7200};
            return route.ToArray();
        }
        public static double[] LandingRoute()
        {
            var route=new List<double>(SafeRoute());route.RemoveAll(x=>x==6740||x==6784||x==7050||x==7094);
            route.Add(6695.1811187353192);route.Sort();return route.ToArray();
        }
        public static double[] FinishRoute()
        { var route=new List<double>(SafeRoute()){7674};return route.ToArray(); }
        static Obstacle Stone(string key, string kind, double x, double width, double height, bool hidden=false)
        { return new Obstacle { Id=Id+"-"+key, Kind=kind, X=x, Width=width, Height=height, Hidden=hidden,
            HiddenRevealDistance=hidden?(double?)220:null, HiddenWarningDuration=hidden?(double?).35:null }; }
    }
}
