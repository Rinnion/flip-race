using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    sealed class BrowserTimingResult
    {
        public string Name;
        public double Step, FirstX, SecondTime, ThirdTime;
        public bool Good, Accepted;
        public double? Finish;
        public int Hits, Earned;
        public string[] HitIds, SuccessIds;
        public List<InputSample> Inputs = new List<InputSample>();
    }
    static BrowserTimingResult DenseTiming(string name,double firstX,double second,double third,double step)
    {
        runs++;
        var inputs=TracksRhythm.FullRoute(TracksRhythm.DenseId,FlightPresets.Chosen);
        var game=RaceSimulation.CreateTrial(TracksRhythm.Draft(TracksRhythm.DenseId),FlightPresets.Chosen);
        Tap(game);RaceSimulation.Advance(game,3);
        var r=new BrowserTimingResult{Name=name,Step=step,FirstX=firstX,SecondTime=second,ThirdTime=third,Accepted=true};
        double launch=0;
        for(int i=0;i<inputs.Length;i++)
        {
            if(i==30||i==31)AdvanceTime(game,launch+(i==30?second:third)-game.Elapsed,step);
            else MoveTo(game,i==29?firstX:inputs[i],step);
            var p=game.Players[0];
            if(i==29)launch=game.Elapsed;
            if(i>=29&&i<=31)r.Inputs.Add(new InputSample{X=p.Distance,Time=game.Elapsed,CenterY=RaceSimulation.GetPlayerCenterY(p),VerticalSpeed=p.VerticalSpeed,
                Airborne=p.Flipping,AirFlipAvailable=p.AirFlipAvailable,Support=p.Support,SupportSide=p.SupportSide,SupportObstacleId=p.SupportObstacleId});
            r.Accepted&=RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1);
        }
        Finish(game,step);
        var end=game.Players[0];r.Finish=end.FinishTime;r.Hits=end.Collisions;r.Earned=end.BoostsEarned;
        r.HitIds=end.HitObstacleIds.ToArray();r.SuccessIds=end.SuccessfulRiskIds.ToArray();
        r.Good=r.Accepted&&r.Hits==0&&r.Earned==9&&end.FinishBoostsEarned==1&&end.SlowsEarned==0&&Math.Abs(r.Finish.Value-33.2)<1e-8;
        return r;
    }
    static void DenseBrowserTiming()
    {
        var cases=new List<BrowserTimingResult>();
        foreach(double step in Steps)
        {
            foreach(var sample in new[]{
                ("authored",5029d,38d/220,.3),
                ("32hz-6-10",5029d,.1875,.3125),
                ("32hz-actual-first",5029.2,.1875,.3125),
                ("observed-estimate",5029.2,(5063.3-5029.2)/220,(5097.84-5029.2)/220)})
            {
                var r=DenseTiming(sample.Item1,sample.Item2,sample.Item3,sample.Item4,step);cases.Add(r);
                if(r.Name=="observed-estimate")Check(!r.Good&&r.Hits==1&&r.Earned==8&&r.HitIds.SequenceEqual(new[]{"rhythm-dense-m13-pad-stone"}),"browser joint-error reconstruction reproduces actual missed pad");
                else Check(r.Good,"authored and six/ten-frame candidates complete actual course cleanly");
                Console.WriteLine($"{r.Name} dt={step:R} good={r.Good} finish={r.Finish:R} hit={r.Hits} earned={r.Earned} ids={string.Join(',',r.HitIds)} X={string.Join(',',r.Inputs.Select(x=>x.X.ToString("R")))}");
            }
        }
        var grid=new List<object>();
        for(int secondFrames=4;secondFrames<=8;secondFrames++)for(int thirdFrames=8;thirdFrames<=13;thirdFrames++)
        {
            var r=DenseTiming("frame-grid",5029.2,secondFrames/32d,thirdFrames/32d,.317);
            grid.Add(new{secondFrames,thirdFrames,r.Good,r.Finish,r.Hits,r.Earned});
            if(r.Good)Console.WriteLine($"32Hz PASS {secondFrames}/{thirdFrames} frames = {secondFrames/32d:R}/{thirdFrames/32d:R}s");
        }
        var joint=new List<object>();
        foreach(var center in new[]{(.1875,.3125),(.1875,.34375),(.21875,.34375),(.20,.33)})
        {
            double last=0;
            for(int n=1;n<=6;n++)
            {
                double delta=n*.005;bool good=true;int failed=0;
                foreach(double first in new[]{-delta,0,delta})foreach(double second in new[]{-delta,0,delta})foreach(double third in new[]{-delta,0,delta})
                {
                    var r=DenseTiming("joint-error",5029.2+220*first,center.Item1+second,center.Item2+third,.317);
                    if(!r.Good){good=false;failed++;}
                }
                joint.Add(new{second=center.Item1,third=center.Item2,delta,good,failed,total=27});
                if(center.Item1==.1875&&center.Item2==.3125&&n<=2)Check(good,"six/ten-frame candidate tolerates simultaneous ±10ms input deviations");
                if(!good)break;last=delta;
            }
            Console.WriteLine($"JOINT {center.Item1:R}/{center.Item2:R}: simultaneous 3-input cube ±{last*1000}ms PASS (grid corners+centers)");
        }
        var framePeriods=new List<object>();
        foreach(int thirdFrame in new[]{10,11})foreach(double period in new[]{.029,.030,.031,.03125,.032,.033,.034})
        {
            var r=DenseTiming("frame-period",5029.2,6*period,thirdFrame*period,.317);
            framePeriods.Add(new{thirdFrame,period,r.Good,r.Finish,r.Hits,r.Earned});
            if(thirdFrame==10)Check(r.Good,"six/ten-frame candidate passes measured 29–34ms frame-period samples");
            Console.WriteLine($"PERIOD {period:R}s, frames6/{thirdFrame}: {r.Good}");
        }
        File.WriteAllText("Verification/flip460-dense-browser-timing.json",JsonSerializer.Serialize(new{cases,grid,joint,framePeriods},CourseJson));
        Console.WriteLine($"Completed Dense browser timing probe: {checks} assertions / {runs} actual games.");
    }
}
