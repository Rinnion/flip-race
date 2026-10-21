using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    sealed class Observation
    {
        public GameState Game;public bool Accepted=true;
        public readonly List<InputSample> Samples=new List<InputSample>();
        public readonly List<double> Speeds=new List<double>();
    }
    static Observation Replay(FlightSettings flight,Planned[] inputs,double step=.317,int changed=-1,double shift=0,Observation baseline=null)
    {
        var r=new Observation{Game=RaceSimulation.CreateTrial(TracksMixed.Get(),flight)};Tap(r.Game);RaceSimulation.Advance(r.Game,3);
        for(int i=0;i<inputs.Length;i++){
            if(i==changed)AdvanceTime(r.Game,baseline.Samples[i].Time+shift-r.Game.Elapsed,step);else MoveTo(r.Game,inputs[i].X,step);
            var p=r.Game.Players[0];r.Samples.Add(new InputSample{X=p.Distance,Time=r.Game.Elapsed,CenterY=RaceSimulation.GetPlayerCenterY(p),
                VerticalSpeed=p.VerticalSpeed,Airborne=p.Flipping,AirFlipAvailable=p.AirFlipAvailable,Support=p.Support,SupportSide=p.SupportSide,SupportObstacleId=p.SupportObstacleId});
            r.Speeds.Add(p.CurrentSpeed);r.Accepted&=RaceSimulation.Press(r.Game,0)&&RaceSimulation.Press(r.Game,1);
        }
        Finish(r.Game,step);return r;
    }
    static bool Clean(Observation r,bool full,int negatives=0)=>r.Accepted&&r.Game.Players.All(p=>p.Collisions==0&&p.BoostsEarned==(full?9:0)&&p.SlowsEarned==negatives&&p.FinishBoostsEarned==(full?1:0));
    static void VerifyFinal()
    {
        quiet=true;var exports=new List<object>();int failures=0;
        foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(int variant in new[]{-1,0,1,2,3}){
            bool full=variant>=0;int mask=Math.Max(0,variant),negativeCount=((mask&1)!=0?1:0)+((mask&2)!=0?1:0);
            var plan=Plan(f,full,mask);var inputs=plan.Inputs.ToArray();var baseline=Replay(f,inputs);
            string name=full?(mask==0?"full":"trap-"+mask):"safe";
            Check(Clean(baseline,full,negativeCount),"nominal "+name);
            var published=full?(mask==0?TracksMixed.FullRoute(f):TracksMixed.NegativeRoute(f,mask)):TracksMixed.SafeRoute(f);
            Check(published.Length==inputs.Length&&published.Zip(inputs,(x,p)=>Math.Abs(x-p.X)<1e-7).All(x=>x),"runtime witnesses match "+name);
            double expected=full?33.2+.3*negativeCount:36;
            foreach(double dt in new[]{1d/30,1d/60,1d/144,.317,2.5,100}){var r=Replay(f,inputs,dt);Check(Clean(r,full,negativeCount),"six dt "+name);Check(Math.Abs(r.Game.Players[0].FinishTime.Value-expected)<1e-7,"exact finish "+name);}
            var windows=new List<object>();
            for(int i=0;i<inputs.Length;i++)foreach(double sign in new[]{-1d,1d}){
                double shift=sign*(full?.025:.06);var r=Replay(f,inputs,.317,i,shift,baseline);bool good=Clean(r,full,negativeCount);
                if(!good){failures++;Console.WriteLine($"RED {f.FirstAction}/{name} i{i} x{inputs[i].X} shift{shift}");}
                windows.Add(new{index=i,shift,good});
            }
            Console.WriteLine($"VERIFY {f.FirstAction}/{name} inputs{inputs.Length} finish{baseline.Game.Players[0].FinishTime:R}");
            exports.Add(new{profile=f==FlightPresets.Chosen?"chosen":"fast-flip",variant=name,track=baseline.Game.Track,inputs,samples=baseline.Samples,speeds=baseline.Speeds,windows,finish=baseline.Game.Players[0].FinishTime});
        }
        File.WriteAllText("Verification/mixed-course-r2-routes.json",JsonSerializer.Serialize(exports,Json));
        Check(failures==0,"all required individual windows");Console.WriteLine("VERIFY PASS "+checks+" checks / zero window failures");
    }
    static void Windows()
    {
        quiet=true;var exports=new List<object>();int failures=0;
        foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(bool full in new[]{false,true}){
            var plan=Plan(f,full);var inputs=plan.Inputs.ToArray();var baseline=Replay(f,inputs);
            Check(Clean(baseline,full),"nominal exact array replay");
            foreach(double dt in new[]{1d/30,1d/60,1d/144,.317,2.5,100})Check(Clean(Replay(f,inputs,dt),full),"six dt nominal");
            var results=new List<object>();
            for(int i=0;i<inputs.Length;i++)foreach(double sign in new[]{-1d,1d}){
                double shift=sign*(full?.025:.06);var r=Replay(f,inputs,.317,i,shift,baseline);bool good=Clean(r,full);
                if(!good){failures++;var p=r.Game.Players[0];Console.WriteLine($"RED {f.FirstAction}/{full} i{i} x{inputs[i].X:R} shift{shift} hit{p.Collisions} +{p.BoostsEarned} finish{p.FinishBoostsEarned} IDs{string.Join(',',p.HitObstacleIds)} {inputs[i].Decision}");}
                results.Add(new{index=i,shift,good});
            }
            Console.WriteLine($"NOMINAL {f.FirstAction}/{full} inputs{inputs.Length} finish{baseline.Game.Players[0].FinishTime:R}");
            exports.Add(new{profile=f==FlightPresets.Chosen?"chosen":"fast-flip",variant=full?"full":"safe",track=baseline.Game.Track,inputs,samples=baseline.Samples,speeds=baseline.Speeds,windows=results,finish=baseline.Game.Players[0].FinishTime});
        }
        File.WriteAllText("Verification/mixed-course-windows-r2.json",JsonSerializer.Serialize(exports,Json));
        Console.WriteLine("WINDOW DRAFT "+failures+" failures / "+checks+" harness checks");
    }
}
