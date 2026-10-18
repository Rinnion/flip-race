using System;
using System.Collections.Generic;
using FlipRace.Core;
internal static partial class Program
{
    static readonly double[] Steps = { 1.0/30, 1.0/60, 1.0/144, .317, 2.5, 100 };
    static T Fields<T>(T source) where T:new()
    { var target=new T(); foreach(var f in typeof(T).GetFields()) f.SetValue(target,f.GetValue(source)); return target; }
    static Track Mirror(Track source)
    {
        var obstacles=new List<Obstacle>(); foreach(var o in source.Obstacles) { var p=Fields(o); p.Kind=o.Kind=="floor"?"ceiling":o.Kind=="ceiling"?"floor":o.Kind; p.GapTop=240-o.GapBottom;p.GapBottom=240-o.GapTop;obstacles.Add(p); }
        var zones=new List<RiskZone>();foreach(var z in source.RiskZones){var p=Fields(z);p.Y=240-z.Y;zones.Add(p);}
        return new Track(source.Length,obstacles.ToArray()){Id=source.Id+"-mirror",RiskZones=zones.ToArray(),SafeRoute=PrepareMirror(source.SafeRoute),RiskRoute=PrepareMirror(source.RiskRoute)};
    }
    static double[] PrepareMirror(double[] route)
    { var r=new double[route.Length+2]; r[0]=50;r[1]=105;Array.Copy(route,0,r,2,route.Length);return r; }
    static void StageOne()
    {
        Check(FlightPresets.Chosen.FirstAction==FirstAction.Jump && FlightPresets.Chosen.Law==MotionLaw.Gravity && FlightPresets.Chosen.AirRepeats==AirRepeatLimit.Unlimited,"Chosen independent semantics");
        Near(FlightPresets.Chosen.JumpImpulse,350,"Chosen jump");Near(FlightPresets.Chosen.GravityAcceleration,662.5,"Chosen gravity");Near(FlightPresets.Chosen.FlipImpulse,0,"Inactive flip impulse");
        foreach(bool mirror in new[]{false,true}) foreach(double step in Steps)
        {
            var track=mirror?Mirror(TracksFull.Practice()):TracksFull.Practice();
            CheckClean(Run(track,track.SafeRoute,step),0,0,10);
            var full=Run(track,track.RiskRoute,step);CheckClean(full,3,0,9.1);
            Check(full.Game.Players[0].InstantBoostsEarned==3,"Landing and two independent instant rewards");
            var firstOnly=new[]{track.RiskRoute[mirror?2:0],1100.0};
            var secondOnly=new[]{track.RiskRoute[mirror?2:0],1170.0,1225.0};
            if(mirror){firstOnly=PrepareMirror(firstOnly);secondOnly=PrepareMirror(secondOnly);}
            var first=Run(track,firstOnly,step);CheckClean(first,2,0,9.4);
            Check(first.Game.Players[0].SuccessfulRiskIds.Contains("practice-chain-1")&&!first.Game.Players[0].SuccessfulRiskIds.Contains("practice-chain-2"),"First retained when second missed");
            var second=Run(track,secondOnly,step);CheckClean(second,2,0,9.4);
            Check(!second.Game.Players[0].SuccessfulRiskIds.Contains("practice-chain-1")&&second.Game.Players[0].SuccessfulRiskIds.Contains("practice-chain-2"),"Second remains available without first");
            var game=Begin(track);if(mirror){MoveTo(game,50,step);Tap(game);MoveTo(game,105,step);Tap(game);}
            MoveTo(game,track.RiskRoute[mirror?2:0],step);Tap(game);MoveTo(game,740,step);
            Check(game.Players[0].Support==SupportKind.Stone,"Actual narrow landing support");
            var before=Clone(game);MoveTo(before,763.99,step);Tap(before);Near(Math.Abs(before.Players[0].VerticalSpeed),350,"Before edge is fresh jump");
            MoveTo(game,764.01,step);var p=game.Players[0];double vy=p.VerticalSpeed;
            Check(p.Support==SupportKind.None&&p.Flipping,"Natural walkoff, no immediate reaction");Tap(game);Near(p.VerticalSpeed,vy,"After edge changes gravity without impulse");
            var walk=Begin(track);if(mirror){MoveTo(walk,50,step);Tap(walk);MoveTo(walk,105,step);Tap(walk);}
            MoveTo(walk,track.RiskRoute[mirror?2:0],step);Tap(walk);MoveTo(walk,900,step);
            Check(walk.Players[0].Support==SupportKind.Wall&&walk.Players[0].Collisions==0,"Unassisted walkoff reaches wall safely");
            foreach(double offset in new[]{-16.0,16.0,-16.000001,16.000001})
            {
                var edge=Begin(track);if(mirror){MoveTo(edge,50,step);Tap(edge);MoveTo(edge,105,step);Tap(edge);}
                MoveTo(edge,track.RiskRoute[mirror?2:0]+offset,step);Tap(edge);MoveTo(edge,800,step);
                Check((edge.Players[0].BoostsEarned==1)==(Math.Abs(offset)<=16),"Marker endpoints use center X, outside hits");
            }
        }
        JumpRule();
        foreach(int input in new[]{0,1,2})
        {
            var t=TracksFull.Practice();var b=Run(t,t.RiskRoute);double early=0,late=0;
            for(int sign=-1;sign<=1;sign+=2) for(int ms=1;ms<=150;ms++) {
                var r=Shift(t,t.RiskRoute,b,input,sign*ms/1000.0);var p=r.Game.Players[0];
                if(p.Collisions>0||p.BoostsEarned!=3)break;if(sign<0)early=ms;else late=ms;
            }
            Console.WriteLine($"Practice input {input} X={t.RiskRoute[input]:R}, time={b.Inputs[input].Time:R}, window -{early}/+{late} ms");
        }
    }
    static RouteResult Shift(Track track,double[] route,RouteResult baseline,int index,double seconds)
    {
        var r=new RouteResult{Game=Begin(track)};
        for(int i=0;i<route.Length;i++){
            if(i==index)AdvanceTime(r.Game,baseline.Inputs[i].Time+seconds-r.Game.Elapsed,.01);else MoveTo(r.Game,route[i],.01);
            Tap(r.Game,r);
        }Finish(r.Game,.317);CaptureReward(r);return r;
    }
    static void JumpRule()
    {
        foreach(bool mirror in new[]{false,true}) foreach(int speed in new[]{220,330}) foreach(double height in new[]{48.0,92.0}) foreach(double width in height==48?new[]{64.0}:new[]{64.0,200.0})
        {
            double window=2*Math.Sqrt(350*350-2*662.5*height)/662.5;
            if(height==92)Check(window*speed<width+28,"Analytic clearance window shorter than expanded stone");
            int clears=0;
            for(double press=470;press<686;press+=1)
            {
                var track=new Track(1400,new[]{new Obstacle{Id="stone",Kind="floor",X=700,Width=width,Height=height}});
                if(speed==330)track.RiskZones=new[]{new RiskZone{Id="speed1",Kind="instant",X=420,Y=226},new RiskZone{Id="speed2",Kind="instant",X=420,Y=226},new RiskZone{Id="speed3",Kind="instant",X=420,Y=226}};
                if(mirror)track=Mirror(track);
                var game=Begin(track);if(mirror){MoveTo(game,50,100);Tap(game);MoveTo(game,105,100);Tap(game);}
                MoveTo(game,press,100);Near(game.Players[0].CurrentSpeed,speed,"Rule test uses actual horizontal speed");Tap(game);MoveTo(game,960,100);
                if(game.Players[0].Collisions==0)clears++;
            }
            Check(height==48?clears>0:clears==0,$"Single jump clearance {width}x{height} speed{speed}");
            Console.WriteLine($"Jump rule {width}x{height} speed={speed} clear phases={clears}");
        }
    }
}
