using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static int runs;
    static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
    static readonly FlightSettings[] Profiles={FlightPresets.FastFlip,FlightPresets.Chosen};
    sealed class Result{public GameState Game;public List<double> Times=new List<double>();public bool Good;}
    static GameState Begin(FlightSettings f,bool mirror)
    {
        var track=new Track(1600,new[]{new Obstacle{Id="gate",Kind="gate",X=1000,Width=16,GapTop=110,GapBottom=130,
            TopOffsetX=mirror?112:0,BottomOffsetX=mirror?0:112}}){RiskZones=new[]{new RiskZone{Id="trap",Kind="negative",X=1080,Y=mirror?40:200,Radius=8}}};
        var game=RaceSimulation.CreateTrial(track,f);Tap(game);RaceSimulation.Advance(game,3);
        if(mirror){Tap(game);if(f==FlightPresets.FastFlip)AdvanceTime(game,.5);else{AdvanceTime(game,350/662.5);Tap(game);AdvanceTime(game,.7);}}
        return game;
    }
    static Result Run(FlightSettings f,bool mirror,double[] inputs,bool trap,double step=.317,int shifted=-1,double shift=0,List<double> times=null)
    {
        runs++;var r=new Result{Game=Begin(f,mirror)};bool accepted=true,support=true;
        try{
            for(int i=0;i<inputs.Length;i++){
                if(i==shifted)AdvanceTime(r.Game,times[i]+shift-r.Game.Elapsed,step);else MoveTo(r.Game,inputs[i],step);
                r.Times.Add(r.Game.Elapsed);var p=r.Game.Players[0];
                if(i==0||f==FlightPresets.FastFlip)support&=p.Support==SupportKind.Wall;
                accepted&=RaceSimulation.Press(r.Game,0)&&RaceSimulation.Press(r.Game,1);
            }
            MoveTo(r.Game,1190,step);
            r.Good=accepted&&support&&r.Game.Players.All(p=>p.Collisions==0&&p.SlowsEarned==(trap?1:0)&&p.BoostsEarned==0
                &&p.Support==SupportKind.Wall&&p.SupportSide==(mirror?1:0)&&p.ActiveEffect==null&&p.PendingEffects.Count==0&&p.CompletedSlows==(trap?1:0));
        }catch(Exception){r.Good=false;}return r;
    }
    static double Window(FlightSettings f,double[] inputs,int index)
    {
        var baseRun=Run(f,false,inputs,true);if(!baseRun.Good)return -1;
        double last=0;for(int n=1;n<=20;n++){double d=n*.005;
            if(!Run(f,false,inputs,true,shifted:index,shift:-d,times:baseRun.Times).Good||!Run(f,false,inputs,true,shifted:index,shift:d,times:baseRun.Times).Good)break;last=d;}
        return last;
    }
    static void Local()
    {
        var rows=new List<object>();
        foreach(var f in Profiles){
            double[] normal=f==FlightPresets.FastFlip?new[]{1005d}:new[]{970.5,1021.5};
            foreach(bool mirror in new[]{false,true})foreach(double step in Steps){var nominal=Run(f,mirror,normal,false,step);Check(nominal.Good,"ordinary bare gate misses trap");
                for(int i=0;i<normal.Length;i++)foreach(double sign in new[]{-1d,1}){var shifted=Run(f,mirror,normal,false,step,i,sign*.06,nominal.Times);var p=shifted.Game.Players[0];
                    Check(shifted.Good,$"normal ±60ms avoids negative {f.FirstAction}/{mirror}/{step}/{i}/{sign}: hits={p.Collisions},slow={p.SlowsEarned},support={p.Support}/{p.SupportSide},remaining={p.SlowRemaining}");}}
            double[] best=null;double radius=-1;
            for(double x=1018;x<=1050;x+=2)for(double y=1048;y<=1090;y+=2){
                var inputs=f==FlightPresets.FastFlip?new[]{x}:new[]{x,y};
                if(!Run(f,false,inputs,true).Good)continue;double win=Enumerable.Range(0,inputs.Length).Min(i=>Window(f,inputs,i));
                if(win>radius){radius=win;best=inputs;}if(f==FlightPresets.FastFlip)break;
            }
            Check(best!=null,"negative route exists");
            foreach(bool mirror in new[]{false,true})foreach(double step in Steps){var nominal=Run(f,mirror,best,true,step);Check(nominal.Good,"trap mirrored six-step");
                for(int i=0;i<best.Length;i++)foreach(double sign in new[]{-1d,1})Check(Run(f,mirror,best,true,step,i,sign*radius,nominal.Times).Good,"trap measured window");}
            rows.Add(new{profile=f==FlightPresets.FastFlip?"fast-flip":"chosen",normal,trap=best,eachInputWindowSeconds=radius});
            Console.WriteLine($"{f.FirstAction} trap={string.Join(',',best)}; individual ±{radius*1000}ms; normal ±60ms avoids");
        }
        File.WriteAllText("Verification/rhythm-trap-local.json",JsonSerializer.Serialize(new{shape=new{firstX=1000,offset=112,top=110,bottom=130,width=16},trap=new{X=1080,Y=200,Radius=8},endX=1190,rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS local trap: {checks} assertions / {runs} actual games.");
    }
    static void Alternative()
    {
        double[] inputs=null;var f=FlightPresets.Chosen;Result nominal=null;double radius=-1;
        for(double x=1030;x<=1054;x+=2)for(double y=1058;y<=1092;y+=2){var candidate=new[]{x,y};var r=Run(f,false,candidate,true);
            if(!r.Good||r.Times[1]-r.Times[0]<.15)continue;double w=Enumerable.Range(0,2).Min(i=>Window(f,candidate,i));
            if(w>radius){inputs=candidate;radius=w;nominal=r;}}
        Check(inputs!=null,"alternative Main trap exists");
        foreach(bool mirror in new[]{false,true})foreach(double step in Steps){var baseline=Run(f,mirror,inputs,true,step);Check(baseline.Good,"alternative mirrored nominal");
            for(int i=0;i<2;i++)foreach(double sign in new[]{-1d,1})Check(Run(f,mirror,inputs,true,step,i,sign*radius,baseline.Times).Good,"alternative individual window");}
        Console.WriteLine($"PASS Main alternative [{string.Join(',',inputs)}]: actual input interval={nominal.Times[1]-nominal.Times[0]:R}s; each input ±{radius*1000}ms; {checks} assertions / {runs} actual games.");
    }
    static void Main(string[] args){CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;captureCollision=false;try{
        if(args.Length>0&&args[0]=="--courses")CourseTraps(args);else if(args.Contains("--dense-browser"))DenseBrowserTiming();else if(args.Contains("--recovery-r7")){RecoveryR6("rhythm-r7-recovery-mapped78");RecoveryR7Six();}else if(args.Contains("--recovery-r6-before-six"))RecoveryR6BeforeSix();else if(args.Contains("--recovery-r6-before"))RecoveryR6Before();else if(args.Contains("--recovery-r6"))RecoveryR6();else if(args.Contains("--recovery-catalogue"))RecoveryCatalogue();else if(args.Contains("--recovery-bare20"))RecoveryBare20();else if(args.Contains("--recovery-classes"))RecoveryClasses();else if(args.Contains("--recovery-shift"))RecoveryShift();else if(args.Contains("--recovery"))RecoveryCases();else if(args.Contains("--alternative"))Alternative();else Local();
    }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
}
