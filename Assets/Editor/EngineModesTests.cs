using System;
using FlipRace.Core;

namespace FlipRace.Editor
{
    public static class EngineModesTests
    {
        static int assertions;
        static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
        static void Check(bool condition,string message)
        { assertions++;if(!condition)throw new Exception(message); }
        static void Near(double actual,double expected,string message)
        { Check(Math.Abs(actual-expected)<1e-8,message+": "+actual+" vs "+expected); }
        static GameState Start(Track track,FlightSettings flight)
        {
            var g=RaceSimulation.CreateTrial(track,flight);Tap(g);RaceSimulation.Advance(g,3);return g;
        }
        static void Tap(GameState g)
        { Check(RaceSimulation.Press(g,0)&&RaceSimulation.Press(g,1),"real actions accepted"); }
        static void Advance(GameState g,double duration,double frame)
        { while(duration>1e-12&&g.Phase=="racing"){double dt=Math.Min(duration,frame);RaceSimulation.Advance(g,dt);duration-=dt;} }
        public static string RunAll()
        {
            assertions=0;OpenGateEdges();NonemptyGateCorner();LinkedWholeBodyBoundary();
            return "PASS: MODES 3 scenarios / "+assertions+" assertions; open edges, real corners and geometric linked confirmation.";
        }
        public static string RunOpenGateRegression()
        { assertions=0;OpenGateEdges();return "PASS: MODES open gate / "+assertions+" assertions."; }
        static void OpenGateEdges()
        {
            foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
            foreach(bool ceiling in new[]{false,true})foreach(double thickness in new[]{0d,1e-6})foreach(double frame in Steps){
                var track=new Track(2200,new[]{new Obstacle{Id="gate",Kind="gate",X=1000,Width=20,
                    GapTop=ceiling?thickness:114,GapBottom=ceiling?126:240-thickness}});
                var g=Start(track,flight);
                if(ceiling){Tap(g);if(flight==FlightPresets.Chosen){Advance(g,.25,frame);Tap(g);Advance(g,1.25,frame);}else Advance(g,1.5,frame);}
                else Advance(g,1.5,frame);
                Check(g.Players[0].Support==SupportKind.Wall && g.Players[0].SupportSide==(ceiling?0:1),"real intended wall reached");
                Advance(g,100,frame);
                foreach(var p in g.Players){
                    Check(p.Collisions==(thickness==0?0:1),thickness==0?"open gate wall-run has no absent-half collision":"nonempty gate half still collides");
                    Near(p.FinishTime.Value,thickness==0?10:10.5,"actual gate finish");
                    if(thickness!=0)Near(p.LastHit.At,986d/220,"nonempty half retains exact expanded front corner");
                }
            }
        }
        static void NonemptyGateCorner()
        {
            foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(double shift in new[]{-.001,0,.001})foreach(double frame in Steps){
                var track=new Track(2200,new[]{new Obstacle{Id="corner",Kind="gate",X=1000,Width=8,GapTop=12,GapBottom=228}});
                var g=Start(track,f);
                double t=f==FlightPresets.Chosen?(350-Math.Sqrt(350*350-2*662.5*12))/662.5:(Math.Sqrt(35*35+2*2700*12)-35)/2700;
                Advance(g,986d/220-t+shift,frame);Tap(g);Advance(g,100,frame);
                foreach(var p in g.Players)Check(p.Collisions==(shift<0?0:1),"finite corner keeps inclusive contact and real early-clear alternative");
            }
        }
        static void LinkedWholeBodyBoundary()
        {
            foreach(double frame in Steps)foreach(bool early in new[]{false,true}){
                var t=new Track(2200,new[]{new Obstacle{Id="wide",Kind="floor",X=600,Width=200,Height=110}})
                {RiskZones=new[]{new RiskZone{Id="after",ObstacleId="wide",Placement="after",X=820,Y=114,ClearX=100}}};
                var g=RaceSimulation.CreateTrial(t,MotionMode.Linear);Tap(g);RaceSimulation.Advance(g,3);
                Advance(g,420d/220,frame);Tap(g);Advance(g,((early?720:750)-420d)/220,frame);Tap(g);
                if(early){Advance(g,81d/220,frame);Check(g.Players[0].CollectedRiskIds.Contains("after")&&g.Players[0].BoostsEarned==0,
                    "early ClearX cannot override whole stone plus player radius");}
                else {Advance(g,150d/220,frame);Check(g.Players[0].SuccessfulRiskIds.Contains("after")&&g.Players[0].Flipping,
                    "late airborne collection confirms without waiting for a wall");}
                Advance(g,100,frame);
                foreach(var p in g.Players){Check(p.BoostsEarned==(early?0:1)&&p.Collisions==(early?1:0),"only pre-clear collision cancels pending reward");
                    Near(p.FinishTime.Value,early?10.5:9.7,"linked route actual physical result");}
            }
        }
    }
}
