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
            assertions=0;OpenGateEdges();NonemptyGateCorner();LinkedWholeBodyBoundary();FastSingleAirRepeat();
            return "PASS: MODES 4 scenarios / "+assertions+" assertions; open edges, real corners, geometric linked confirmation and Fast single air repeat.";
        }
        public static string RunOpenGateRegression()
        { assertions=0;OpenGateEdges();return "PASS: MODES open gate / "+assertions+" assertions."; }
        static GameState FastSupport(Track track,int side,double frame)
        {
            var g=Start(track,FlightPresets.FastFlip);
            if(side==0){Tap(g);Advance(g,(Math.Sqrt(35*35+2*2700*212)-35)/2700+1e-8,frame);}
            foreach(var p in g.Players)Check(p.Support==SupportKind.Wall&&p.SupportSide==side,"real initial Fast wall");
            return g;
        }
        static void FastSingleAirRepeat()
        {
            Check(FlightPresets.FastFlip.AirRepeats==AirRepeatLimit.One,"Fast preset has exactly one repeat");
            Check(FlightPresets.Chosen.AirRepeats==AirRepeatLimit.Unlimited,"Chosen remains unlimited");
            foreach(int side in new[]{0,1})foreach(double frame in Steps){
                var g=FastSupport(new Track(22000,Array.Empty<Obstacle>()),side,frame);
                Tap(g);Advance(g,.3,frame);
                var p=g.Players[0];var other=g.Players[1];double vy=p.VerticalSpeed,y=p.Y;
                double otherV=other.VerticalSpeed,otherY=other.Y;int gravity=p.Gravity,otherGravity=other.Gravity;
                Check(RaceSimulation.Press(g,0),"first Fast air repeat accepted");
                Near(p.VerticalSpeed,vy,"repeat retains inertia");Near(p.Y,y,"repeat retains position");
                Check(p.Gravity==-gravity&&!p.AirFlipAvailable,"repeat changes acceleration and consumes own charge");
                Check(!RaceSimulation.Press(g,0),"second Fast air repeat rejected");
                Near(p.VerticalSpeed,vy,"rejected repeat retains inertia");Check(p.Gravity==-gravity,"rejected repeat retains acceleration");
                Near(other.VerticalSpeed,otherV,"other velocity unaffected");Near(other.Y,otherY,"other position unaffected");
                Check(other.Gravity==otherGravity&&other.AirFlipAvailable,"other player keeps independent repeat");
                // This late reversal still touches the opposite wall by inertia. That is not a landing.
                double wrongWall=(845-Math.Sqrt(845*845-2*2700*80))/2700;
                Advance(g,wrongWall+1e-8,frame);
                Check(p.Support==SupportKind.None&&p.Flipping&&!p.AirFlipAvailable,"wrong-wall touch does not recharge");
                Check(!RaceSimulation.Press(g,0),"wrong-wall contact cannot supply another repeat");
                Advance(g,.5,frame);
                Check(p.Support==SupportKind.Wall&&p.SupportSide==side&&p.AirFlipAvailable,"true wall landing restores one repeat");
                Check(p.Collisions==0&&other.Collisions==0,"repeat route has no artificial impact");
                Check(RaceSimulation.Press(g,0),"new departure from true wall");Advance(g,.02,frame);
                Check(RaceSimulation.Press(g,0)&&!RaceSimulation.Press(g,0),"wall-recharged repeat is still exactly one");

                var track=new Track(22000,new[]{new Obstacle{Id="pad-stone",Kind=side==1?"floor":"ceiling",X=600,Width=64,Height=48}})
                {RiskZones=new[]{new RiskZone{Id="pad",Kind="landing",ObstacleId="pad-stone",X=632,Y=side==1?192:48,LandingWidth=32}}};
                g=FastSupport(track,side,frame);
                double arrival=.15+(440+Math.Sqrt(440*440-2*2700*(48-35.625)))/2700;
                double launchX=632-220*arrival;
                Advance(g,(launchX-g.Players[0].Distance)/220,frame);Tap(g);Advance(g,.15,frame);Tap(g);
                foreach(var player in g.Players)Check(!player.AirFlipAvailable,"pad approach consumes one real airborne repeat");
                Advance(g,arrival-.15+1e-8,frame);
                foreach(var player in g.Players){
                    Check(player.Support==SupportKind.Stone&&player.SupportObstacleId=="pad-stone"&&player.SupportSide==side,"real mirrored pad landing");
                    Check(player.AirFlipAvailable&&player.BoostsEarned==1&&player.Collisions==0,"legal pad restores charge and awards once");
                }
                Tap(g);Advance(g,.02,frame);Tap(g);
                foreach(var player in g.Players)Check(!RaceSimulation.Press(g,player.Id),"pad-recharged repeat is exactly one");
            }
            foreach(double frame in Steps){
                var none=new FlightSettings(FirstAction.Flip,MotionLaw.Gravity,AirRepeatLimit.None,2700,35,300,530,112);
                var g=Start(new Track(22000,Array.Empty<Obstacle>()),none);Tap(g);Advance(g,.1,frame);
                foreach(var p in g.Players)Check(!RaceSimulation.Press(g,p.Id),"manual None still rejects airborne input");
                g=Start(new Track(22000,Array.Empty<Obstacle>()),FlightPresets.Chosen);Tap(g);Advance(g,.1,frame);
                double vy=g.Players[0].VerticalSpeed;
                for(int i=0;i<8;i++){Tap(g);Near(g.Players[0].VerticalSpeed,vy,"Chosen unlimited repeats preserve velocity");}
                foreach(var p in g.Players)Check(p.AirFlipAvailable,"Chosen charge remains unlimited");
            }
        }
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
