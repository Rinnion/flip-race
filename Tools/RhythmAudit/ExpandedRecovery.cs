using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static Obstacle OptionalNextObstacle(GameState g)
    {
        var ordered=new List<Obstacle>(g.Track.Obstacles);ordered.Sort((a,b)=>ObstacleGeometry.Left(a).CompareTo(ObstacleGeometry.Left(b)));
        int i=ordered.FindIndex(o=>o.Id==g.Players[0].LastHit.Id);Need(i>=0,"first hit is a real track obstacle");
        return i+1<ordered.Count?ordered[i+1]:null;
    }
    // General legal-action catalogue. It does not recognize R5 input indices,
    // obstacle IDs or injected states. R6 enumerates its own actual prefixes.
    static bool AdaptiveOneWall(GameState reaction,double first,double wait,double repeat,double target,out double[] taps)
    {
        taps=null;var g=Clone(reaction);int hits=g.Players[0].Collisions;double start=g.Elapsed;
        Until(g,start+first,.005);if(g.Players[0].Collisions!=hits||!TapBoth(g))return false;
        if(!UntilRealWall(g,hits,target))return false;
        double wall=g.Elapsed-start;
        taps=new[]{first,wall+wait,wall+wait+repeat};
        return ReplayRecovery(reaction,taps,target,.025,out var ignored);
    }
    static bool AdaptiveTwoWalls(GameState reaction,double delay,double repeat1,double repeat2,double target,out double[] taps)
    {
        taps=null;var g=Clone(reaction);var p=g.Players[0];int hits=p.Collisions;double start=g.Elapsed;var times=new List<double>();
        if(!UntilRealWall(g,hits,target))return false;Advance(g,delay,.001);
        if(p.Collisions!=hits||!TapBoth(g))return false;times.Add(g.Elapsed-start);
        Advance(g,repeat1,.005);if(p.Collisions!=hits||!TapBoth(g))return false;times.Add(g.Elapsed-start);
        if(!UntilRealWall(g,hits,target)||!TapBoth(g))return false;times.Add(g.Elapsed-start);
        Advance(g,repeat2,.005);if(p.Collisions!=hits||!TapBoth(g))return false;times.Add(g.Elapsed-start);
        taps=times.ToArray();return ReplayRecovery(reaction,taps,target,.025,out var ignored);
    }
    static bool ExpandedEscape(GameState hit,out string policy,out double[] taps,out bool firstLate20,RecoverySeed seed=null)
    {
        policy="";taps=null;firstLate20=false;
        var reaction=Clone(hit);Advance(reaction,reaction.Players[0].Recovering+.15,.005);int hits=hit.Players[0].Collisions;
        if(reaction.Players[0].Collisions!=hits){policy="additional impact during 150ms reaction";return false;}
        var next=OptionalNextObstacle(hit);double target=next==null?hit.Track.Length:ObstacleGeometry.Right(next)+15;
        if(Reach(reaction,target,hits)){policy="stay";taps=Array.Empty<double>();firstLate20=true;return true;}
        if(TryRecoverySeed(reaction,seed,target,out taps,out firstLate20))
        {policy="previous policy independently replayed on current actual prefix/geometry";return true;}
        var fixedCatalogue=new[]{
            new[]{.05,.20,.65,.80},new[]{.069,.249,.718,.898},new[]{.329,.449,.914,1.014},new[]{.342,.442,.887,1.027},
            new[]{.10,.25,.70,.875},new[]{0d,.15,.60,.75},new[]{.02,.24,.66},new[]{.15,.30,.75,.925},
            new[]{.275,.325,.775,.825},new[]{.275,.325,.775,.90}};
        if(reaction.Flight.FirstAction==FirstAction.Jump)
        {
            foreach(var trial in fixedCatalogue)
                if(ReplayRecovery(reaction,trial,target,.025,out var ignored))
                {taps=trial;policy="four/three accepted taps, finite catalogue";break;}
            if(taps==null)
                foreach(double delay in new[]{.01,.02,0d})
                    if(AdaptiveTwoWalls(reaction,delay,.09,.10,target,out var pair))
                    {taps=pair;policy="two actual Wall contacts, jump/repeat90ms then jump/repeat100ms";break;}
            if(taps==null)
            {
                foreach(double first in new[]{.02,0d,.01,.04,.06,.08,.10,.12,.16,.20})
                {
                    foreach(double wait in new[]{0d,.02,.04})foreach(double repeat in new[]{.18,.15,.10,.05,.20})
                        if(AdaptiveOneWall(reaction,first,wait,repeat,target,out var adaptive))
                        {taps=adaptive;policy="first air input, actual Wall, jump and repeat (50..200ms catalogue)";break;}
                    if(taps!=null)break;
                }
            }
        }
        if(taps!=null)
        {
            var late=(double[])taps.Clone();late[0]+=.02;
            firstLate20=ReplayRecovery(reaction,late,target,.025,out var ignored);
            return true;
        }
        // Keep all old stay/one/two-input and settling/oracle branches. Their
        // descriptive witness is preserved, but no unmeasured window is claimed.
        if(Escape(hit,out var legacy)){policy="legacy legal catalogue: "+legacy;return true;}
        if(reaction.Flight.FirstAction==FirstAction.Jump)
        {
            taps=AdaptiveFour(reaction,target);
            if(taps!=null){policy="two actual Wall contacts, four taps with individually20ms window";firstLate20=true;return true;}
        }
        policy="no witness in finite expanded catalogue";return false;
    }
    static void CourseRecoveryExpanded()
    {
        // A unique timestamp prevents accidentally replacing any historical run.
        string generation=CourseFile.Contains("r7")?"r7":"r6";
        string artifact="rhythm-"+generation+"-recovery-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".jsonl";
        string path=Path.Combine(ProjectRoot,"Verification",artifact);
        int candidates=0,cases=0,failed=0,late20=0,descriptive=0;
        var counts=new Dictionary<string,int>();var options=new JsonSerializerOptions{IncludeFields=true};var seeds=LoadRecoverySeeds();
        Console.WriteLine("RECOVERY PROGRESS artifact=Verification/"+artifact+" source="+CourseFile);
        using(var stream=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read)))
        {
            stream.AutoFlush=true;
            foreach(var w in ReadCourses())
            {
                context=w.Track.Id+" "+Name(w.Flight)+" "+w.Purpose;
                var nominal=ObserveCourse(w,1d/144);Need(CourseGoal(nominal,w),"new export nominal route before recovery sweep");
                var clocks=nominal.Actions.ConvertAll(a=>a.Time).ToArray();int local=0;
                for(int i=0;i<w.Inputs.Length;i++)foreach(double shift in new[]{-.8,-.64,-.4,-.24,-.16,-.08,.08,.16,.24,.4,.64,.8})
                {
                    candidates++;var hit=CourseFirstHit(w,clocks,i,shift);if(hit==null)continue;
                    context=w.Track.Id+" "+Name(w.Flight)+" "+w.Purpose+" input="+i+" shift="+shift;
                    cases++;local++;seeds.TryGetValue(RecoverySeedKey(w.Track.Id,Name(w.Flight),w.Purpose,i,shift),out var seed);
                    bool escaped=ExpandedEscape(hit,out string policy,out double[] taps,out bool delay20,seed);
                    if(!escaped)failed++;if(delay20)late20++;if(taps==null&&escaped)descriptive++;
                    if(!counts.ContainsKey(policy))counts[policy]=0;counts[policy]++;
                    var g=Clone(hit);Advance(g,g.Players[0].Recovering+.15,.005);var p=g.Players[0];var next=OptionalNextObstacle(hit);
                    var row=new{track=w.Track.Id,profile=Name(w.Flight),route=w.Purpose,changedInput=i,shiftSeconds=shift,
                        firstHit=hit.Players[0].LastHit.Id,hitX=hit.Players[0].Distance,reactionX=p.Distance,Y=RaceSimulation.GetPlayerCenterY(p),VY=p.VerticalSpeed,
                        p.Gravity,support=p.Support.ToString(),p.AirFlipAvailable,speed=p.CurrentSpeed,effects=EffectSnapshot(p),next=next?.Id,
                        targetX=next==null?hit.Track.Length:ObstacleGeometry.Right(next)+15,escaped,policy,offsetsSeconds=taps,firstInputPlus20msMeasured=delay20};
                    stream.WriteLine(JsonSerializer.Serialize(row,options));
                    if(!escaped)Console.WriteLine("NO_FOUND_ESCAPE "+context+" hit="+row.firstHit+" reactionX="+p.Distance.ToString("R")+" next="+next?.Id);
                    if(cases%100==0)Console.WriteLine("RECOVERY PROGRESS candidates="+candidates+" actualHits="+cases+" noFound="+failed+" at="+context);
                }
                Console.WriteLine("RECOVERY SAMPLE "+w.Track.Id+" "+Name(w.Flight)+" "+w.Purpose+" actualHits="+local+" cumulative="+cases+" noFound="+failed);
            }
        }
        Need(cases>0,"nonempty new-export first-hit sample");
        Console.WriteLine((failed==0?"PASS":"REVIEW REQUIRED")+" expanded actual-prefix recovery: "+checks+" assertions / "+games+" games / "+candidates+" perturbation candidates / "+cases+" actual first-hit states; noFound="+failed+". Explicit fixed-first+20ms witnesses="+late20+"; descriptive legacy witnesses="+descriptive+". Individually measured representative windows are separate; no global comfort or arbitrary-error guarantee.");
        foreach(var item in counts)Console.WriteLine("RECOVERY POLICY "+item.Value+" "+item.Key);
    }
}
