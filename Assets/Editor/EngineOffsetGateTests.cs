using System;
using FlipRace.Core;

namespace FlipRace.Editor
{
    public static class EngineOffsetGateTests
    {
        static int assertions;
        static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
        static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
        static void Near(double a,double b,string message){Check(Math.Abs(a-b)<1e-7,message+": "+a+" / "+b);}
        static double FastTime(double d){return (Math.Sqrt(1225+5400*d)-35)/2700;}
        static double RiseTime(FlightSettings f,double d){return f==FlightPresets.FastFlip?FastTime(d):
            d<=56.75?(350-Math.Sqrt(122500-1325*d))/662.5:.2+(-217.5+Math.Sqrt(217.5*217.5+1325*(d-56.75)))/662.5;}
        static void Advance(GameState g,double time,double step){while(time>1e-12){double dt=Math.Min(time,step);RaceSimulation.Advance(g,dt);time-=dt;}}
        static void Tap(GameState g){Check(RaceSimulation.Press(g,0)&&RaceSimulation.Press(g,1),"actual accepted inputs");}
        static GameState Prepare(Track t,FlightSettings f,int side,double step){var g=RaceSimulation.CreateTrial(t,f);Tap(g);RaceSimulation.Advance(g,3);
            if(side==0){Tap(g);if(f==FlightPresets.FastFlip)Advance(g,FastTime(212)+1e-8,step);else{Advance(g,350/662.5,step);Tap(g);Advance(g,Math.Sqrt(2*(212-350*350/1325d)/662.5)+1e-8,step);}}
            foreach(var p in g.Players)Check(p.Support==SupportKind.Wall&&p.SupportSide==side,"real starting wall "+f.FirstAction+" side="+side+" dt="+step+" y="+RaceSimulation.GetPlayerCenterY(p)+" v="+p.VerticalSpeed+" support="+p.Support);return g;}
        static Obstacle Gate(int side,double shift=0){return new Obstacle{Id="offset",Kind="gate",X=1000,Width=16,GapTop=140,GapBottom=100,
            TopOffsetX=shift+(side==1?0:192),BottomOffsetX=shift+(side==1?192:0)};}
        static void Launch(GameState g,FlightSettings f,double x,double step){Advance(g,(x-g.Players[0].Distance)/220,step);Tap(g);
            if(f==FlightPresets.Chosen){Advance(g,.2,step);Tap(g);}}
        public static string RunAll(){assertions=0;Bounds();Corners();OneLogicalPenalty();LinkedClear();
            return "PASS: RHYTHM offset gates 4 scenarios / "+assertions+" assertions; real mirrors, signed offsets, corners, one logical penalty and linked union clearance.";}
        static void Bounds(){
            var o=Gate(1,-192);Near(ObstacleGeometry.Left(o),808,"negative left");Near(ObstacleGeometry.Right(o),1016,"union right");
            Check(ObstacleGeometry.TryGetBand(o,0,out var top)&&ObstacleGeometry.TryGetBand(o,1,out _),"both real halves exist");
            Near(top.X,808,"top offset");Near(top.Bottom,140,"physical top extent");
            o.GapTop=0;o.TopOffsetX=-10000;Near(ObstacleGeometry.Left(o),1000,"absent top cannot enlarge left");
            Near(ObstacleGeometry.Right(o),1016,"only bottom width");Check(!ObstacleGeometry.TryGetBand(o,0,out _),"absent top has no collider");
            o.GapTop=140;o.TopOffsetX=-192;o.GapBottom=240;o.BottomOffsetX=10000;
            Near(ObstacleGeometry.Right(o),824,"absent bottom cannot delay clear");
            o.GapTop=0;Near(ObstacleGeometry.Left(o),1000,"fully empty origin");Near(ObstacleGeometry.Right(o),1000,"fully empty bounds");
            var stone=new Obstacle{Kind="floor",X=200,Width=64,Height=48,TopOffsetX=-10000,BottomOffsetX=10000};
            Check(ObstacleGeometry.TryGetBand(stone,0,out var body)&&!ObstacleGeometry.TryGetBand(stone,1,out _),"stone is one unshifted body");
            Near(body.Y,192,"stone physical plane");Near(ObstacleGeometry.Left(stone),200,"gate fields do not move stone");
        }
        static void Corners(){foreach(var f in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(int side in new[]{0,1})
            foreach(double shift in new[]{0d,-192d})foreach(double step in Steps){
                double low=1030+shift-220*RiseTime(f,72),high=1178+shift-220*RiseTime(f,140);
                foreach(double edge in new[]{low,high})foreach(double delta in new[]{-1e-4,0,1e-4}){
                    double x=edge+delta;var g=Prepare(new Track(4400,new[]{Gate(side,shift)}),f,side,step);Launch(g,f,x,step);Advance(g,6,step);
                    bool clear=x>low+1e-9&&x<high-1e-9;
                    foreach(var p in g.Players)Check(p.Collisions==(clear?0:1),"separate X bands retain exact corner and ±epsilon control");
                }
            }
        }
        static void OneLogicalPenalty(){foreach(var f in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(int side in new[]{0,1})
            foreach(double step in Steps)foreach(bool separate in new[]{false,true}){
                var gate=new Obstacle{Id="gate",Kind="gate",X=1000,Width=16,GapTop=72,GapBottom=168,TopOffsetX=side==0?0:80,BottomOffsetX=side==0?80:0};
                var obstacles=separate?new[]{
                    new Obstacle{Id="first",Kind="gate",X=1000,Width=16,GapTop=side==0?72:0,GapBottom=side==0?240:168},
                    new Obstacle{Id="second",Kind="gate",X=1080,Width=16,GapTop=side==0?0:72,GapBottom=side==0?168:240},
                    new Obstacle{Id="neighbor",Kind="gate",X=1600,Width=16,GapTop=240,GapBottom=240}}
                    :new[]{gate,new Obstacle{Id="neighbor",Kind="gate",X=1600,Width=16,GapTop=240,GapBottom=240}};
                var g=Prepare(new Track(4400,obstacles),f,side,step);Advance(g,(986-g.Players[0].Distance)/220+.50000001,step);
                foreach(var p in g.Players){Check(p.Collisions==1,"first actual half hit");Near(p.LastHit.At,986d/220,"earliest leading half timestamp");}
                Tap(g);if(f==FlightPresets.Chosen){Advance(g,.1,step);Tap(g);}Advance(g,5,step);
                foreach(var p in g.Players)Check(p.Collisions==(separate?3:2),"same ID charges once for both halves, next ID remains live");
            }
        }
        static void LinkedClear(){foreach(var f in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(int side in new[]{0,1})foreach(double step in Steps){
            var track=new Track(4400,new[]{Gate(side)}){RiskZones=new[]{new RiskZone{Id="linked",ObstacleId="offset",Placement="after",X=1180,Y=side==1?14:226,ClearX=1050}}};
            var g=Prepare(track,f,side,step);Launch(g,f,f==FlightPresets.FastFlip?1045.9563987106758:1023.0939510614623,step);
            Advance(g,(1190-g.Players[0].Distance)/220,step);
            foreach(var p in g.Players)Check(p.CollectedRiskIds.Contains("linked")&&p.BoostsEarned==0,"collected after first half stays pending for farther half");
            Advance(g,(1222-1e-4-g.Players[0].Distance)/220,step);
            foreach(var p in g.Players)Check(p.BoostsEarned==0,"full body must clear farthest rectangle");
            Advance(g,2e-4/220,step);
            foreach(var p in g.Players){Check(p.BoostsEarned==1&&p.Collisions==0,"union clear awards once without a new wall condition");Near(p.LastSuccess.At,(1222+1e-7)/220,"exact far-half confirmation timestamp");}
        }}
    }
}
