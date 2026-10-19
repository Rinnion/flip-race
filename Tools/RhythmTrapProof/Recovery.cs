using System;
using System.Collections.Generic;
using System.Linq;
using FlipRace.Core;

internal static partial class Program
{
    static GameState RecoveryPrefix(Track track, double[] route, List<double> clocks, int index, double shift)
    {
        var g=RaceSimulation.CreateTrial(track,FlightPresets.Chosen);Tap(g);RaceSimulation.Advance(g,3);
        captureCollision=true;firstCollision=null;
        try {
            for(int i=0;i<route.Length;i++) {
                if(i==index)AdvanceTime(g,clocks[i]+shift-g.Elapsed,.025);else MoveTo(g,route[i],.025);
                if(firstCollision!=null)return Clone(firstCollision);
                Tap(g);
            }
            while(firstCollision==null&&g.Phase=="racing")Step(g,.025);
            return firstCollision==null?null:Clone(firstCollision);
        } finally {captureCollision=false;}
    }
    static bool RecoveryReach(GameState g, double target, int hits, double maxStep=.025)
    {
        while(g.Players[0].Distance<target-1e-8&&g.Phase=="racing") {
            RaceSimulation.Advance(g,Math.Min(maxStep,(target-g.Players[0].Distance)/440));
            if(g.Players[0].Collisions!=hits)return false;
        }
        return g.Players.All(p=>p.Collisions==hits&&p.Distance>=target-1e-8);
    }
    static bool RecoveryTry(GameState reaction,double[] times,double target,out GameState end,out List<InputSample> samples,double maxStep=.025)
    {
        end=Clone(reaction);samples=new List<InputSample>();int hits=end.Players[0].Collisions;double start=end.Elapsed;
        foreach(double at in times) {
            AdvanceTime(end,start+at-end.Elapsed,maxStep);
            var p=end.Players[0];if(p.Collisions!=hits||p.Distance>=target)return false;
            samples.Add(new InputSample{X=p.Distance,Time=end.Elapsed,CenterY=RaceSimulation.GetPlayerCenterY(p),VerticalSpeed=p.VerticalSpeed,Support=p.Support,SupportSide=p.SupportSide,Airborne=p.Flipping});
            if(!RaceSimulation.Press(end,0)||!RaceSimulation.Press(end,1))return false;
        }
        return RecoveryReach(end,target,hits,maxStep);
    }
    static void RecoveryCases(double offset=0)
    {
        var track=ShiftFinalGate(R5Track(TracksRhythm.WavesId),offset);var route=ShiftFinalInputs(R5Route(track.Id,FlightPresets.Chosen,"safe"),false,FlightPresets.Chosen,offset);
        var baseline=RunCourse(track,FlightPresets.Chosen,route,1d/144);
        foreach(var pair in new[]{(40,.8),(41,.4),(41,.64),(41,.8)}) {
            var hit=RecoveryPrefix(track,route,baseline.Times,pair.Item1,pair.Item2);Check(hit!=null,"real modified route hits");
            var reaction=Clone(hit);AdvanceTime(reaction,reaction.Players[0].Recovering+.15,.001);var p=reaction.Players[0];
            Console.WriteLine($"STATE input={pair.Item1} shift={pair.Item2:R} hit={p.LastHit.Id} hitX={hit.Players[0].Distance:R} reactionX={p.Distance:R} y={RaceSimulation.GetPlayerCenterY(p):R} vy={p.VerticalSpeed:R} gravity={p.Gravity} support={p.Support}");
            double target=track.Obstacles.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(track.Obstacles.Single(o=>o.Id==p.LastHit.Id))).OrderBy(ObstacleGeometry.Left).Select(o=>ObstacleGeometry.Right(o)+15).First();
            double[] best=null;double bestSpacing=-1;GameState end;List<InputSample> samples;
            foreach(double first in new[]{0d,.02,.04,.06,.08,.10,.12,.14,.16,.18,.20})
            for(double second=first+.02;second<=1.1+1e-8;second+=.02)
            for(double third=second+.02;third<=1.6+1e-8;third+=.02) {
                double spacing=Math.Min(second-first,third-second);if(spacing<bestSpacing-1e-8)continue;
                double[] candidate={first,second,third};if(!RecoveryTry(reaction,candidate,target,out end,out samples))continue;
                best=candidate;bestSpacing=spacing;
            }
            Check(best!=null,"three accepted real actions escape full next gate");
            // Refine only the late wall-jump pair; a broad search is unnecessary
            // for three identical airborne states from the reported inputs.
            if(p.Support==SupportKind.None&&offset==0) {
                for(double second=.85;second<=.91+1e-9;second+=.001)
                for(double third=.98;third<=1.10+1e-9;third+=.001) {
                    double spacing=third-second;if(spacing<bestSpacing-1e-9)continue;
                    double[] candidate={0,second,third};if(!RecoveryTry(reaction,candidate,target,out end,out samples))continue;
                    best=candidate;bestSpacing=spacing;
                }
            }
            foreach(double step in Steps) {
                Check(RecoveryTry(reaction,best,target,out end,out samples,step),"three-action escape frame independent");
                if(step==Steps[0])Console.WriteLine($"ESCAPE target={target:R} offsets=[{string.Join(',',best.Select(x=>x.ToString("R")))}] minSpacing={bestSpacing:R} atX=[{string.Join(',',samples.Select(x=>x.X.ToString("R")))}] inputY=[{string.Join(',',samples.Select(x=>x.CenterY.ToString("R")))}] endY={RaceSimulation.GetPlayerCenterY(end.Players[0]):R} hits={end.Players[0].Collisions} slows={end.Players[0].SlowsEarned}");
            }
            double low=0,high=.03;
            for(int n=0;n<40;n++) {
                double middle=(low+high)/2;var shifted=(double[])best.Clone();shifted[0]+=middle;
                if(RecoveryTry(reaction,shifted,target,out end,out samples))low=middle;else high=middle;
            }
            Console.WriteLine($"FIRST_INPUT_LATE_MARGIN seconds={low:R}; measured against fixed remaining witness; comfort is not inferred from existence.");
        }
        Console.WriteLine($"PASS targeted R5 recovery offset={offset}: {checks} checks; 4 actual modified prefixes, 3 accepted recovery inputs, entire next union cleared; no state injection.");
    }
}
