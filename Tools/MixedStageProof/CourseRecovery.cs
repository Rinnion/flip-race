using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static GameState CourseCollision(CourseCase c,CourseResult baseline,int changed,double shift,double step=.01)
    {
        var g=RaceSimulation.CreateTrial(c.Track,c.Flight);Tap(g);RaceSimulation.Advance(g,3);captureCollision=true;firstCollision=null;
        try {
            for(int i=0;i<c.Inputs.Length&&firstCollision==null;i++){
                if(i==changed){double at=baseline.Inputs[i].Time+shift;if(at<g.Elapsed)return null;AdvanceTime(g,at-g.Elapsed,step);}
                else if(c.Inputs[i]>=g.Players[0].Distance-1e-8)MoveTo(g,c.Inputs[i],step);else continue;
                if(firstCollision!=null)break;RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);
            }
            while(firstCollision==null&&g.Phase=="racing")Step(g,step);
            return firstCollision;
        }finally{captureCollision=false;firstCollision=null;}
    }
    static bool LegalTap(GameState g){bool a=RaceSimulation.Press(g,0),b=RaceSimulation.Press(g,1);return a&&b;}
    static bool ClearUnion(GameState g,double target,int hits,double step)
    {
        while(g.Phase=="racing"&&g.Players[0].Distance<target-1e-7){RaceSimulation.Advance(g,Math.Min(step,(target-g.Players[0].Distance)/440));if(g.Players.Any(p=>p.Collisions!=hits))return false;}
        return g.Players.All(p=>p.Distance>=target-1e-7&&p.Collisions==hits);
    }
    static bool WaitRealWall(GameState g,int side,int hits,double step)
    {
        for(double time=0;time<3;time+=Math.Min(step,.005)){
            var p=g.Players[0];if(p.Collisions!=hits)return false;
            if(p.Support==SupportKind.Wall&&p.SupportSide==side&&!p.Flipping)return true;
            RaceSimulation.Advance(g,Math.Min(step,.005));
        }
        return false;
    }
    static bool AdaptiveLowRecovery(GameState hit,double firstExtra,double step,out List<InputSample> inputs,out InputSample readySample,out InputSample cleared)
    {
        var g=Clone(hit);inputs=new List<InputSample>();var p=g.Players[0];int hits=p.Collisions;AdvanceTime(g,p.Recovering+.15,step);readySample=Sample(g);cleared=null;
        if(!WaitRealWall(g,0,hits,step))return false;
        AdvanceTime(g,firstExtra,step);inputs.Add(Sample(g));if(!LegalTap(g))return false;
        AdvanceTime(g,.2,step);inputs.Add(Sample(g));if(!LegalTap(g))return false;
        if(!WaitRealWall(g,1,hits,step)||p.Distance>4030.844877314131)return false;
        MoveTo(g,4030.844877314131,step);inputs.Add(Sample(g));if(!LegalTap(g))return false;
        AdvanceTime(g,.2,step);inputs.Add(Sample(g));if(!LegalTap(g))return false;
        bool good=ClearUnion(g,4193,hits,step);cleared=Sample(g);return good;
    }
    static void CourseRecovery()
    {
        var courses=ReadCourses("Verification/mixed-course-r2-routes.json");
        using(var doc=JsonDocument.Parse(File.ReadAllText("Verification/mixed-course-recovery-r2.json"))){
            var rows=new List<object>();var nofound=doc.RootElement.EnumerateArray().Where(r=>!r.GetProperty("found").GetBoolean()).ToArray();
            foreach(var row in nofound){var c=courses.Single(c=>c.Profile==row.GetProperty("profile").GetString()&&c.Variant==row.GetProperty("variant").GetString());var baseline=CourseRun(c,c.Inputs,Array.Empty<RiskZone>());
                int index=row.GetProperty("input").GetInt32();double shift=row.GetProperty("shift").GetDouble();var hit=CourseCollision(c,baseline,index,shift);Check(hit!=null&&hit.Players[0].LastHit.Id==row.GetProperty("hit").GetString(),"actual recovery prefix matches recorded hit");
                foreach(double step in Steps)foreach(double extra in new[]{0d,.01,.02}){
                    bool good=AdaptiveLowRecovery(hit,extra,step,out var inputs,out var ready,out var cleared);
                    Console.WriteLine($"ADAPT {c.Profile}/{c.Variant} input{index}/{shift} dt{step} +{extra} good{good} readyX{ready.X} y{ready.CenterY} support{ready.Support} taps{string.Join(',',inputs.Select(i=>i.X))}");
                    Check(good,"four-action continuation via two actual walls");rows.Add(new{profile=c.Profile,variant=c.Variant,input=index,shift,step,firstExtra=extra,ready,inputs,target=4193,cleared});
                }
            }
            var simple=new List<object>();
            foreach(string profile in new[]{"chosen","fast-flip"})foreach(string suffix in new[]{"straight","low-floor","large-pad","final-offset"}) {
                if(profile=="chosen"&&suffix=="low-floor")continue;
                var row=doc.RootElement.EnumerateArray().First(r=>r.GetProperty("profile").GetString()==profile&&r.GetProperty("hit").GetString()=="mixed-tricks-"+suffix&&r.GetProperty("found").GetBoolean());
                var c=courses.Single(c=>c.Profile==profile&&c.Variant==row.GetProperty("variant").GetString());var baseline=CourseRun(c,c.Inputs,Array.Empty<RiskZone>());
                int index=row.GetProperty("input").GetInt32();double shift=row.GetProperty("shift").GetDouble();var hit=CourseCollision(c,baseline,index,shift);
                Check(hit!=null&&hit.Players[0].LastHit.Id=="mixed-tricks-"+suffix,"independent actual boundary prefix");
                var ordered=c.Track.Obstacles.OrderBy(ObstacleGeometry.Left).ToArray();int obstacleIndex=Array.FindIndex(ordered,o=>o.Id==hit.Players[0].LastHit.Id);
                double target=ObstacleGeometry.Right(ordered[obstacleIndex+1])+15;Near(target,row.GetProperty("target").GetDouble(),"target is entire next physical union+15");
                double[] offsets=row.GetProperty("witness").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
                foreach(double step in Steps){var g=Clone(hit);int hits=g.Players[0].Collisions;AdvanceTime(g,g.Players[0].Recovering+.15,step);var ready=Sample(g);var samples=new List<InputSample>();double previous=0;
                    foreach(double time in offsets){AdvanceTime(g,time-previous,step);samples.Add(Sample(g));Check(LegalTap(g),"actual recovery edge accepted by both players");previous=time;}
                    Check(ClearUnion(g,target,hits,step),"independent witness clears entire next obstacle without another hit");
                    simple.Add(new{profile,variant=c.Variant,input=index,shift,step,ready,offsets,inputs=samples,target,cleared=Sample(g)});
                }
            }
            File.WriteAllText("Verification/mixed-course-recovery-targeted.json",JsonSerializer.Serialize(new{scope="actual exported r2 prefixes; no state injection; adaptive real-wall continuation plus independently replayed UI boundary witnesses; first extra0/10/20ms after first real wall only in adaptive class",checks,runs,adaptive=rows,simple},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            Console.WriteLine($"PASS targeted recovery {checks} checks/{runs} baseline games/{rows.Count+simple.Count} continuation replays");
        }
    }
    static void CourseNative()
    {
        var c=ReadCourses("Verification/mixed-course-r2-routes.json").Single(c=>c.Profile=="chosen"&&c.Variant=="full");
        var baseline=CourseRun(c,c.Inputs,Array.Empty<RiskZone>());int first=Array.FindIndex(c.Inputs,x=>Math.Abs(x-5026)<1e-6);
        Check(first>=0,"current large-pad launch found");var rows=new List<object>();
        foreach(double frame in new[]{1d/32,1d/30}){
            var joint=new Dictionary<int,double>{{first+1,6*frame-(baseline.Inputs[first+1].Time-baseline.Inputs[first].Time)},{first+2,10*frame-(baseline.Inputs[first+2].Time-baseline.Inputs[first].Time)}};
            var r=CourseRun(c,c.Inputs,Array.Empty<RiskZone>(),step:frame,nominal:baseline,joint:joint);Check(CourseGood(r,true,0),"Main large-pad native6th/10th frame witness");
            Console.WriteLine($"PASS Main full large pad frame{frame:R}, inputs {string.Join(',',r.Inputs.Skip(first).Take(3).Select(x=>x.X))}, finish{r.Game.Players[0].FinishTime}");
            rows.Add(new{frame,offsetSeconds=new[]{0d,6*frame,10*frame},samples=r.Inputs.Skip(first).Take(3),finish=r.Game.Players[0].FinishTime});
        }
        File.WriteAllText("Verification/mixed-course-native-pad.json",JsonSerializer.Serialize(new{scope="focused actual full r2 replay; Main large-pad repeat6th/10th frame after actual5026 launch; no claim about entire-route raster or jointbox",checks,runs,rows},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        Console.WriteLine($"PASS native pad {checks}/{runs}");
    }
}
