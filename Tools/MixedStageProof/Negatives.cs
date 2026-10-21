using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    sealed class CourseCase { public string Profile,Variant;public FlightSettings Flight;public Track Track;public double[] Inputs; }
    sealed class CourseResult { public GameState Game;public bool Accepted=true;public List<InputSample> Inputs=new List<InputSample>(); }
    static CourseCase[] ReadCourses(string path)
    {
        using(var doc=JsonDocument.Parse(File.ReadAllText(path)))return doc.RootElement.EnumerateArray().Select(row=>{
            var t=row.GetProperty("track");var fields=new JsonSerializerOptions{IncludeFields=true};
            string profile=row.GetProperty("profile").GetString();
            return new CourseCase{Profile=profile,Variant=row.GetProperty("variant").GetString(),Flight=profile=="chosen"?FlightPresets.Chosen:FlightPresets.FastFlip,
                Track=new Track(t.GetProperty("Length").GetDouble(),JsonSerializer.Deserialize<Obstacle[]>(t.GetProperty("Obstacles").GetRawText(),fields),Array.Empty<Cue>()){
                    Id=t.GetProperty("Id").GetString(),RiskZones=JsonSerializer.Deserialize<RiskZone[]>(t.GetProperty("RiskZones").GetRawText(),fields)},
                Inputs=row.GetProperty("inputs").EnumerateArray().Select(x=>x.GetProperty("X").GetDouble()).ToArray()};
        }).ToArray();
    }
    static CourseResult CourseRun(CourseCase c,double[] inputs,RiskZone[] extra,double step=.317,int changed=-1,double shift=0,CourseResult nominal=null,Dictionary<int,double> joint=null)
    {
        var t=new Track(c.Track.Length,c.Track.Obstacles,Array.Empty<Cue>()){Id=c.Track.Id,RiskZones=c.Track.RiskZones.Concat(extra).ToArray()};
        var r=new CourseResult{Game=RaceSimulation.CreateTrial(t,c.Flight)};Tap(r.Game);RaceSimulation.Advance(r.Game,3);
        for(int i=0;i<inputs.Length;i++){if(joint!=null&&joint.ContainsKey(i))AdvanceTime(r.Game,nominal.Inputs[i].Time+joint[i]-r.Game.Elapsed,step);else if(i==changed)AdvanceTime(r.Game,nominal.Inputs[i].Time+shift-r.Game.Elapsed,step);else MoveTo(r.Game,inputs[i],step);r.Inputs.Add(Sample(r.Game));r.Accepted&=RaceSimulation.Press(r.Game,0)&&RaceSimulation.Press(r.Game,1);}
        Finish(r.Game,step);runs++;return r;
    }
    static bool CourseGood(CourseResult r,bool full,int negative)
    {return r.Accepted&&r.Game.Players.All(p=>p.Collisions==0&&p.BoostsEarned==(full?9:0)&&p.FinishBoostsEarned==(full?1:0)&&p.SlowsEarned==negative);}
    static void NegativeSearch()
    {
        var courses=ReadCourses("Verification/mixed-course-windows-r1.json");
        foreach(var c in courses){var r=CourseRun(c,c.Inputs,Array.Empty<RiskZone>());Check(CourseGood(r,c.Variant=="full",0),"immutable positive source baseline");}
        var candidates=new List<object>();
        foreach(double nx in new[]{3820d,6500})foreach(double ny in new[]{180d}){
            var z=new RiskZone{Id="probe-negative",Kind="negative",X=nx,Y=ny,Radius=8};bool baselineClean=courses.All(c=>CourseGood(CourseRun(c,c.Inputs,new[]{z}),c.Variant=="full",0));
            if(!baselineClean)continue;Console.WriteLine($"AVOIDED {nx}/{ny}");
            var results=new List<object>();
            foreach(var c in courses.Where(c=>c.Variant=="full")){
                double[] found=null;
                if(nx<4000){
                    for(double launch=nx-100;launch<=nx-5&&found==null;launch+=5)for(double q=c.Flight==FlightPresets.Chosen?0:.1;q<=(c.Flight==FlightPresets.Chosen?0:.25)&&found==null;q+=.025){
                        var extras=c.Flight==FlightPresets.Chosen?new[]{launch}:new[]{launch,launch+220*q};
                        var inputs=c.Inputs.Concat(extras).OrderBy(x=>x).ToArray();
                        try{if(CourseGood(CourseRun(c,inputs,new[]{z}),true,1))found=inputs;}catch(Exception){}
                    }
                }else if(nx<5000){
                    for(double launch=nx-150;launch<=nx-5&&found==null;launch+=5){
                        double[] inputs;
                        if(c.Flight==FlightPresets.FastFlip)inputs=c.Inputs.Select(x=>x==4800?launch:x).OrderBy(x=>x).ToArray();
                        else inputs=c.Inputs.Concat(new[]{launch}).OrderBy(x=>x).ToArray();
                        try{if(CourseGood(CourseRun(c,inputs,new[]{z}),true,1))found=inputs;}catch(Exception){}
                    }
                }else{
                    for(double launch=6420;launch<=6500&&found==null;launch+=5)for(double q=.1;q<=.301&&found==null;q+=.025){
                        var inputs=c.Inputs.Select(x=>Math.Abs(x-6400)<1e-6?launch:Math.Abs(x-6444)<1e-6?launch+220*q:x).ToArray();
                        try{if(CourseGood(CourseRun(c,inputs,new[]{z}),true,1))found=inputs;}catch(Exception){}
                    }
                }
                Console.WriteLine($"  {c.Profile} {(found==null?"NONE":string.Join(',',found.Where(x=>x>4600&&x<6750)))}");
                results.Add(new{profile=c.Profile,found=found!=null,inputs=found});
            }
            candidates.Add(new{x=nx,y=ny,results});
        }
        File.WriteAllText("Verification/mixed-negative-search.json",JsonSerializer.Serialize(new{scope="isolated immutable positive r1 JSON plus trial negative; no runtime edits",checks,runs,candidates},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        Console.WriteLine($"Negative search {checks}/{runs}");
    }
    static void NegativeProof(string path)
    {
        var courses=ReadCourses(path);var negatives=new[]{new RiskZone{Id="mixed-tricks-negative-middle",Kind="negative",X=3820,Y=180,Radius=8},new RiskZone{Id="mixed-tricks-negative-final",Kind="negative",X=6500,Y=180,Radius=8}};
        var rows=new List<object>();
        foreach(var c in courses){bool full=c.Variant=="full";var r=CourseRun(c,c.Inputs,negatives);Check(CourseGood(r,full,0),"two negatives preserve baseline");
            for(int i=0;i<c.Inputs.Length;i++)foreach(double shift in new[]{full?-.025:-.06,full?.025:.06}){var s=CourseRun(c,c.Inputs,negatives,changed:i,shift:shift,nominal:r);Check(CourseGood(s,full,0),$"baseline windows preserved {c.Profile}/{c.Variant}/{i}/{shift}");}
        }
        foreach(var c in courses.Where(c=>c.Variant=="full")){
            var foundBoth=new List<double[]>();
            foreach(int which in new[]{0,1}){
                double[] found=null;CourseResult baseline=null;double[] edits=null;
                for(double launch=which==0?3700:6425;launch<=(which==0?3815:6480)&&found==null;launch+=2.5)
                for(double q=c.Flight==FlightPresets.Chosen&&which==0?0:.1;q<=(c.Flight==FlightPresets.Chosen&&which==0?0:.241)&&found==null;q+=.01){
                    double[] candidate,changed;
                    if(which==0){changed=c.Flight==FlightPresets.Chosen?new[]{launch}:new[]{launch,launch+220*q};candidate=c.Inputs.Concat(changed).OrderBy(x=>x).ToArray();}
                    else {changed=c.Flight==FlightPresets.FastFlip?new[]{launch}:new[]{launch,launch+220*q};candidate=c.Inputs.Select(x=>Math.Abs(x-6400)<1e-6?launch:Math.Abs(x-6444)<1e-6?launch+220*q:x).ToArray();}
                    try{var r=CourseRun(c,candidate,negatives);if(!CourseGood(r,true,1))continue;bool good=true;
                        foreach(double edit in changed){int index=Array.FindIndex(candidate,x=>Math.Abs(x-edit)<1e-6);foreach(double delta in new[]{-.025,.025}){var shifted=CourseRun(c,candidate,negatives,changed:index,shift:delta,nominal:r);if(!CourseGood(shifted,true,1)){good=false;break;}}if(!good)break;}
                        if(good){found=candidate;baseline=r;edits=changed;}
                    }catch(Exception){}
                }
                Check(found!=null,$"optional trap has individual25 {c.Profile}/{which}");
                foreach(double step in Steps){var r=CourseRun(c,found,negatives,step);Check(CourseGood(r,true,1),"trap six dt");Near(r.Game.Players[0].FinishTime.Value,33.5,"actual single negative finish");}
                Console.WriteLine($"TRAP {c.Profile}/{which}: edits {string.Join(',',edits)}");
                rows.Add(new{profile=c.Profile,negative=negatives[which].Id,inputs=found,edits,samples=baseline.Inputs,finish=baseline.Game.Players[0].FinishTime});foundBoth.Add(found);
            }
            var firstExtra=foundBoth[0].Where(x=>!c.Inputs.Any(original=>Math.Abs(original-x)<1e-6));
            var combined=foundBoth[1].Concat(firstExtra).OrderBy(x=>x).ToArray();
            foreach(double step in Steps){var r=CourseRun(c,combined,negatives,step);Check(CourseGood(r,true,2),"both traps six dt");Near(r.Game.Players[0].FinishTime.Value,33.8,"actual two negative finish");}
            rows.Add(new{profile=c.Profile,negative="both",inputs=combined,finish=33.8});
        }
        File.WriteAllText("Verification/mixed-negative-proposal.json",JsonSerializer.Serialize(new{scope="isolated copy of authored source JSON; two added negatives, unchanged positive geometry/physics",source=path,checks,runs,negatives,rows},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        Console.WriteLine($"PASS negative proposal {checks}/{runs}");
    }
}
