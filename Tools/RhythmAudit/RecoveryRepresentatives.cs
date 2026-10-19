using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    sealed class RecoveryRow
    {
        public string track {get;set;} public string profile {get;set;} public string route {get;set;} public string hit {get;set;} public string support {get;set;}
        public int input {get;set;} public int gravity {get;set;}
        public double shift {get;set;} public double hitX {get;set;} public double reactionX {get;set;} public double Y {get;set;} public double VY {get;set;}
    }
    static bool SelectedRecovery(RecoveryRow row)
    {
        return row.hit.Contains("-chain-gate-1") || row.hit.EndsWith("-stone")
            || row.hit=="rhythm-cascade-m14-gate" || row.hit=="rhythm-waves-m15-gate";
    }
    static Obstacle NextObstacle(GameState g)
    {
        var ordered=new List<Obstacle>(g.Track.Obstacles);
        ordered.Sort((a,b)=>ObstacleGeometry.Left(a).CompareTo(ObstacleGeometry.Left(b)));
        int i=ordered.FindIndex(o=>o.Id==g.Players[0].LastHit.Id);
        Need(i>=0&&i+1<ordered.Count,"next actual obstacle exists");return ordered[i+1];
    }
    static double HorizontalArrival(PlayerState p,double distance)
    {
        double t=0;
        var effects=new List<SpeedEffect>();
        if(p.ActiveEffect!=null)effects.Add(p.ActiveEffect);
        effects.AddRange(p.PendingEffects);
        foreach(var e in effects)
        {
            double v=220*e.Multiplier,served=Math.Min(e.Remaining,distance/v);
            t+=served;distance-=served*v;if(distance<=1e-9)return t;
        }
        return t+distance/220;
    }
    static object EffectSnapshot(PlayerState p)
    {
        var effects=new List<object>();
        if(p.ActiveEffect!=null)effects.Add(new {active=true,id=p.ActiveEffect.RingId,negative=p.ActiveEffect.IsNegative,remaining=p.ActiveEffect.Remaining});
        foreach(var e in p.PendingEffects)effects.Add(new {active=false,id=e.RingId,negative=e.IsNegative,remaining=e.Remaining});
        return effects;
    }
    static void RecoveryRepresentatives()
    {
        var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        var rows=JsonSerializer.Deserialize<RecoveryRow[]>(File.ReadAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-recovery-no-found.json")),options);
        var courses=ReadCourses();var clocks=new Dictionary<string,double[]>();var output=new List<object>();
        foreach(var row in rows)
        {
            if(!SelectedRecovery(row))continue;
            var w=courses.Find(c=>c.Track.Id==row.track&&Name(c.Flight)==row.profile&&c.Purpose==row.route);
            context=row.track+" "+row.route+" input="+row.input+" shift="+row.shift;
            string key=row.track+"/"+row.route;
            if(!clocks.ContainsKey(key))clocks[key]=ObserveCourse(w,1d/144).Actions.ConvertAll(a=>a.Time).ToArray();
            var hit=CourseFirstHit(w,clocks[key],row.input,row.shift);Need(hit!=null&&hit.Players[0].LastHit.Id==row.hit,"same actual first impact");
            var g=Clone(hit);Advance(g,g.Players[0].Recovering+.15,.001);var p=g.Players[0];
            Near(p.Distance,row.reactionX,"preserved reaction X",1e-5);Near(RaceSimulation.GetPlayerCenterY(p),row.Y,"preserved reaction Y",1e-5);
            var next=NextObstacle(g);ObstacleBand a,b;bool hasA=ObstacleGeometry.TryGetBand(next,0,out a),hasB=ObstacleGeometry.TryGetBand(next,1,out b);
            bool top=hasA&&(!hasB||a.Left<=b.Left);var band=top?a:b;
            double gap=band.Left-Radius-p.Distance,arrival=HorizontalArrival(p,gap);
            double y=top?RaceSimulation.GetPlayerCenterY(p):240-RaceSimulation.GetPlayerCenterY(p),v=top?p.VerticalSpeed:-p.VerticalSpeed;
            double targetY=top?band.Bottom+Radius:240-band.Top+Radius;
            double directY=y+v*arrival+.5*w.Flight.GravityAcceleration*arrival*arrival;
            var targets=new List<object>();
            foreach(var z in w.Track.RiskZones)
                if(!z.IsFinish&&!p.CollectedRiskIds.Contains(z.Id)&&z.X+z.Radius+Radius>p.Distance&&z.X-z.Radius-Radius<=band.Left-Radius)
                    targets.Add(new {z.Id,z.Kind,z.X,z.Y,z.Radius});
            var result=new {row.track,row.profile,row.route,changedInput=row.input,shiftSeconds=row.shift,row.hit,row.hitX,
                reactionX=p.Distance,Y=RaceSimulation.GetPlayerCenterY(p),VY=p.VerticalSpeed,p.Gravity,support=p.Support.ToString(),p.AirFlipAvailable,
                speed=p.CurrentSpeed,effects=EffectSnapshot(p),next=next.Id,frontX=band.Left-Radius,gap,arrivalWithoutNewRewards=arrival,
                normalizedY=y,normalizedVY=v,targetY,maxDirectAccelerationY=directY,preFrontUncollectedRewards=targets,
                targetX=ObstacleGeometry.Right(next)+Radius+1};
            output.Add(result);Console.WriteLine(JsonSerializer.Serialize(result));
        }
        Need(output.Count==30,"all thirty assigned rows replayed");
        File.WriteAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-recovery-representatives.json"),JsonSerializer.Serialize(output,options)+"\n");
        Console.WriteLine("PASS representative state replay: "+checks+" assertions / "+games+" actual games / "+output.Count+" rows. Direct acceleration is a bound only until any real wall/stone contact; it is not a global controllability claim.");
    }
    static bool ReplayRecovery(GameState reaction,double[] taps,double target,double step,out List<object> samples)
    {
        samples=new List<object>();var g=Clone(reaction);var p=g.Players[0];int hits=p.Collisions;double start=g.Elapsed;
        foreach(double at in taps)
        {
            if(at<g.Elapsed-start-1e-9)return false;
            Until(g,start+at,step);
            if(p.Collisions!=hits||g.Phase!="racing"||p.Distance>=target)return false;
            samples.Add(new {offset=at,X=p.Distance,Y=RaceSimulation.GetPlayerCenterY(p),VY=p.VerticalSpeed,support=p.Support.ToString(),speed=p.CurrentSpeed});
            if(!TapBoth(g))return false;
        }
        return Reach(g,target,hits,step);
    }
    static CourseWitness ShiftChainSecond(CourseWitness w,double amount=20,bool stone=false)
    {
        var options=new JsonSerializerOptions{IncludeFields=true};
        var t=JsonSerializer.Deserialize<Track>(JsonSerializer.Serialize(w.Track,options),options);
        // Geometry-only candidate. Every actual prefix is replayed from ready;
        // no playable player/effect/velocity state is injected or reset.
        foreach(var o in t.Obstacles)if(o.Id.EndsWith(stone?"-exit-gate":"-chain-gate-2"))o.X+=amount;
        if(!stone)foreach(var z in t.RiskZones)if(z.Id.EndsWith("-chain-2"))z.X+=amount;
        return new CourseWitness{Track=t,Flight=w.Flight,Purpose=w.Purpose,Inputs=w.Inputs,Groups=w.Groups};
    }
    static double[] FindChainRecovery(GameState reaction,double target)
    {
        int hits=reaction.Players[0].Collisions;
        foreach(double first in new[]{.02,.025,.03,.01,0d})
        {
            var g=Clone(reaction);double start=g.Elapsed;Until(g,start+first,.001);if(g.Players[0].Collisions!=hits||!TapBoth(g))continue;
            while(g.Phase=="racing"&&g.Players[0].Collisions==hits&&g.Players[0].Distance<target&&g.Players[0].Support!=SupportKind.Wall)RaceSimulation.Advance(g,.001);
            if(g.Players[0].Support!=SupportKind.Wall||g.Players[0].Collisions!=hits)continue;
            double wall=g.Elapsed-start;
            foreach(double wait in new[]{.02,0d,.01,.03,.04,.05})foreach(double repeat in new[]{.18,.2,.16,.22,.14,.24,.12,.26,.1})
            {
                double[] trial={first,wall+wait,wall+wait+repeat};List<object> samples;
                if(!ReplayRecovery(reaction,trial,target,.01,out samples))continue;
                var delayed=(double[])trial.Clone();delayed[0]+=.02;
                if(ReplayRecovery(reaction,delayed,target,.01,out samples))return trial;
            }
        }
        return null;
    }
    sealed class RecoveryBranch
    {public GameState Game;public double[] Taps;public double Last=-100;}
    static double[] BoundedRecoveryBranches(GameState reaction,double target,double spacing)
    {
        const double dt=.02;int hits=reaction.Players[0].Collisions;
        var current=new List<RecoveryBranch>{new RecoveryBranch{Game=Clone(reaction),Taps=Array.Empty<double>()}};
        for(int tick=0;tick<150&&current.Count>0;tick++)
        {
            double at=tick*dt;var next=new Dictionary<string,RecoveryBranch>();
            foreach(var node in current)for(int press=0;press<2;press++)
            {
                if(press==1&&(node.Taps.Length>=4||at-node.Last<spacing-1e-9))continue;
                var g=Clone(node.Game);var taps=node.Taps;
                if(press==1){if(!TapBoth(g))continue;var list=new List<double>(taps);list.Add(at);taps=list.ToArray();}
                RaceSimulation.Advance(g,dt);var p=g.Players[0];if(p.Collisions!=hits||g.Phase!="racing")continue;
                if(p.Distance>=target)
                {
                    bool margin=taps.Length>0;
                    if(margin){var delayed=(double[])taps.Clone();delayed[0]+=.02;margin=ReplayRecovery(reaction,delayed,target,.01,out var ignored);}
                    if(margin)return taps;
                    continue;
                }
                var branch=new RecoveryBranch{Game=g,Taps=taps,Last=press==1?at:node.Last};
                string key=string.Join("/",Math.Round(p.Distance),Math.Round(RaceSimulation.GetPlayerCenterY(p)/2),Math.Round(p.VerticalSpeed/10),p.Gravity,p.Support,taps.Length,Math.Round(Math.Min(.5,at+dt-branch.Last)/.02),p.ActiveEffect?.RingId,Math.Round((p.ActiveEffect?.Remaining??0)/.02));
                if(!next.ContainsKey(key))next.Add(key,branch);
            }
            current=new List<RecoveryBranch>(next.Values);
            // This is a bounded counterexample/witness search, not a proof of
            // impossibility when grid, state merging or this cap excludes a path.
            if(current.Count>18000)current.RemoveRange(18000,current.Count-18000);
        }
        return null;
    }
    static void RecoveryCandidate()
    {
        var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        var rows=JsonSerializer.Deserialize<RecoveryRow[]>(File.ReadAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-recovery-no-found.json")),options);
        var courses=ReadCourses();var clocks=new Dictionary<string,double[]>();var output=new List<object>();var witnesses=new Dictionary<string,double[]>();int failed=0;
        foreach(var row in rows)
        {
            if(!SelectedRecovery(row))continue;
            bool chain=row.hit.Contains("chain-gate-1");
            var w=courses.Find(c=>c.Track.Id==row.track&&Name(c.Flight)==row.profile&&c.Purpose==row.route);
            context=row.track+" "+row.route+" input="+row.input+" shift="+row.shift;
            string key=row.track+"/"+row.route;
            if(!clocks.ContainsKey(key))clocks[key]=ObserveCourse(w,1d/144).Actions.ConvertAll(a=>a.Time).ToArray();
            var fixture=chain?ShiftChainSecond(w):w;
            var hit=CourseFirstHit(fixture,clocks[key],row.input,row.shift);Need(hit!=null&&hit.Players[0].LastHit.Id==row.hit,"same first collision on candidate");
            var g=Clone(hit);Advance(g,g.Players[0].Recovering+.15,.001);var p=g.Players[0];Near(p.Distance,row.reactionX,"same actual reaction",1e-5);
            var next=NextObstacle(g);double target=ObstacleGeometry.Right(next)+15;
            string cls=chain?(Math.Round(Math.Abs(p.VerticalSpeed),1).ToString()):row.hit.EndsWith("stone")?"stone":"bare-chain";
            double[] taps;
            if(!witnesses.TryGetValue(cls,out taps))
            {
                taps=chain?FindChainRecovery(g,target):cls=="stone"?new[]{.075,.225,.675,.825}:new[]{.05,.2,.65,.8};
                if(chain&&taps==null)foreach(double spacing in new[]{.15,.1,.06}){taps=BoundedRecoveryBranches(g,target,spacing);if(taps!=null)break;}
                witnesses[cls]=taps;
            }
            bool ok=taps!=null;List<object> samples=null;
            if(ok)foreach(double step in Steps)ok&=ReplayRecovery(g,taps,target,step,out samples);
            if(!ok){failed++;Console.WriteLine("NO_FOUND_CANDIDATE "+context+" class="+cls);output.Add(new{row.track,row.route,row.input,row.shift,chainShift=chain?20:0,found=false});continue;}
            var margins=new List<double>();
            for(int i=0;i<taps.Length;i++)
            {
                double max=0;
                for(int ms=1;ms<=100;ms++)
                {
                    double d=ms*.001;bool all=true;
                    foreach(int sign in new[]{-1,1})
                    {
                        var changed=(double[])taps.Clone();changed[i]+=sign*d;
                        if(changed[i]<0||(i>0&&changed[i]<=changed[i-1])||(i<taps.Length-1&&changed[i]>=changed[i+1])||!ReplayRecovery(g,changed,target,.01,out samples)){all=false;break;}
                    }
                    if(!all)break;max=d;
                }
                margins.Add(max);
            }
            ReplayRecovery(g,taps,target,.01,out samples);
            var delayed=(double[])taps.Clone();delayed[0]+=.02;bool late20=ReplayRecovery(g,delayed,target,.01,out var ignored);
            output.Add(new{row.track,row.profile,row.route,changedInput=row.input,shiftSeconds=row.shift,chainShift=chain?20:0,targetX=target,found=true,offsetsSeconds=taps,individualSymmetricWindows=margins,firstInputPlus20ms=late20,samples});
            Console.WriteLine("WITNESS "+context+" chainShift="+(chain?20:0)+" taps="+string.Join(",",taps)+" windows="+string.Join(",",margins)+" firstLate20="+late20);
        }
        File.WriteAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-chain20-candidate.json"),JsonSerializer.Serialize(output,options)+"\n");
        Console.WriteLine((failed==0?"PASS":"REVIEW REQUIRED")+" targeted legal recovery witnesses: "+checks+" assertions / "+games+" actual-prefix games / "+output.Count+" cases; noFound="+failed+". Only chain second gate/ring shifted20 in tool-only candidate; stone/bare-chain use unchanged R5. Not whole-course acceptance.");
    }
    static bool UntilRealWall(GameState g,int hits,double target)
    {
        while(g.Phase=="racing"&&g.Players[0].Collisions==hits&&g.Players[0].Distance<target)
        {
            if(g.Players[0].Support==SupportKind.Wall&&!g.Players[0].Flipping)return true;
            RaceSimulation.Advance(g,.001);
        }
        return false;
    }
    static bool RecoveryMargins(GameState g,double[] taps,double target,double tolerance)
    {
        if(!ReplayRecovery(g,taps,target,.02,out var ignored))return false;
        for(int i=0;i<taps.Length;i++)foreach(int sign in new[]{-1,1})
        {
            var changed=(double[])taps.Clone();changed[i]+=sign*tolerance;
            if(changed[i]<0||(i>0&&changed[i]<=changed[i-1])||(i<taps.Length-1&&changed[i]>=changed[i+1])||!ReplayRecovery(g,changed,target,.02,out ignored))return false;
        }
        return true;
    }
    static double[] AdaptiveFour(GameState reaction,double target)
    {
        int hits=reaction.Players[0].Collisions;double start=reaction.Elapsed;
        var wall=Clone(reaction);if(!UntilRealWall(wall,hits,target))return null;
        double wallTime=wall.Elapsed-start;
        foreach(double repeat in new[]{.18,.16,.20,.14,.22,.12,.24,.10})
        foreach(double delay in new[]{.02,.04,.06,.08,.10,.12,.14,.16,.18,.20})
        {
            var g=Clone(wall);Advance(g,delay,.01);if(g.Players[0].Collisions!=hits||!TapBoth(g))continue;
            Advance(g,repeat,.01);if(g.Players[0].Collisions!=hits||!TapBoth(g)||!UntilRealWall(g,hits,target))continue;
            double secondWall=g.Elapsed-start;
            foreach(double repeat2 in new[]{.18,.16,.20,.14,.22,.12,.24,.10})
            foreach(double delay2 in new[]{.02,.04,.06,.08,.10,.12,.14})
            {
                double[] taps={wallTime+delay,wallTime+delay+repeat,secondWall+delay2,secondWall+delay2+repeat2};
                if(RecoveryMargins(reaction,taps,target,.02))return taps;
            }
        }
        return null;
    }
    static void RecoverySpacing()
    {
        var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        var rows=JsonSerializer.Deserialize<RecoveryRow[]>(File.ReadAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-recovery-no-found.json")),options);
        var courses=ReadCourses();var output=new List<object>();var seen=new HashSet<string>();
        foreach(var row in rows)
        {
            bool chain=row.hit.Contains("chain-gate-1"),stone=row.hit.EndsWith("stone");if(!chain&&!stone)continue;
            string cls=stone?"stone":Math.Round(Math.Abs(row.VY),1).ToString();if(!seen.Add(cls))continue;
            var w=courses.Find(c=>c.Track.Id==row.track&&Name(c.Flight)==row.profile&&c.Purpose==row.route);
            var clocks=ObserveCourse(w,1d/144).Actions.ConvertAll(a=>a.Time).ToArray();
            foreach(double shift in stone?new[]{20d}:new[]{30d,40d,60d})
            {
                context="recovery geometry spacing "+cls+" +"+shift;
                var fixture=ShiftChainSecond(w,shift,stone);var hit=CourseFirstHit(fixture,clocks,row.input,row.shift);
                Need(hit!=null&&hit.Players[0].LastHit.Id==row.hit,"candidate has same real first hit");
                var g=Clone(hit);Advance(g,g.Players[0].Recovering+.15,.001);Near(g.Players[0].Distance,row.reactionX,"candidate preserves reaction",1e-5);
                double target=ObstacleGeometry.Right(NextObstacle(g))+15;var taps=AdaptiveFour(g,target);
                Console.WriteLine("RECOVERY SPACING "+cls+" +"+shift+" found="+(taps!=null));
                if(taps==null){output.Add(new{classification=cls,shiftWorld=shift,found=false});continue;}
                List<object> samples=null;foreach(double dt in Steps)Need(ReplayRecovery(g,taps,target,dt,out samples),"adaptive actual-wall witness six dt");
                var windows=new List<double>();
                for(int i=0;i<taps.Length;i++)
                {
                    double max=0;for(int ms=1;ms<=100;ms++)
                    {
                        bool ok=true;foreach(int sign in new[]{-1,1}){var c=(double[])taps.Clone();c[i]+=sign*ms*.001;if(c[i]<0||(i>0&&c[i]<=c[i-1])||(i<taps.Length-1&&c[i]>=c[i+1])||!ReplayRecovery(g,c,target,.01,out samples)){ok=false;break;}}
                        if(!ok)break;max=ms*.001;
                    }
                    windows.Add(max);
                }
                ReplayRecovery(g,taps,target,.01,out samples);
                output.Add(new{row.track,row.profile,row.route,changedInput=row.input,shiftSeconds=row.shift,classification=cls,shiftWorld=shift,found=true,targetX=target,offsetsSeconds=taps,individualSymmetricWindows=windows,samples});
                Console.WriteLine("WITNESS SPACING "+cls+" +"+shift+" taps="+string.Join(",",taps)+" windows="+string.Join(",",windows));
                break;
            }
        }
        File.WriteAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-recovery-spacing-candidates.json"),JsonSerializer.Serialize(output,options)+"\n");
        Console.WriteLine("REVIEW REQUIRED spacing candidate study: "+checks+" assertions / "+games+" actual-prefix games; finite catalogue with actual wall contacts, four taps, minimum100ms spacing and individual20ms acceptance criterion. No runtime changes or whole-course acceptance.");
    }
    static void RecoverySpacingReplay()
    {
        var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        var rows=JsonSerializer.Deserialize<RecoveryRow[]>(File.ReadAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-recovery-no-found.json")),options);
        var plans=new Dictionary<string,double[]>();var shifts=new Dictionary<string,double>();
        using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-recovery-spacing-candidates.json"))))
            foreach(var entry in doc.RootElement.EnumerateArray())if(entry.GetProperty("found").GetBoolean())
            {string cls=entry.GetProperty("classification").GetString();plans[cls]=JsonSerializer.Deserialize<double[]>(entry.GetProperty("offsetsSeconds").GetRawText());shifts[cls]=entry.GetProperty("shiftWorld").GetDouble();}
        var courses=ReadCourses();var clocks=new Dictionary<string,double[]>();var output=new List<object>();
        foreach(var row in rows)
        {
            if(!SelectedRecovery(row))continue;
            bool chain=row.hit.Contains("chain-gate-1"),stone=row.hit.EndsWith("stone");
            string cls=chain?Math.Round(Math.Abs(row.VY),1).ToString():stone?"stone":"bare-chain";
            double[] taps=cls=="bare-chain"?new[]{.05,.2,.65,.8}:plans[cls];double shift=cls=="bare-chain"?0:shifts[cls];
            var w=courses.Find(c=>c.Track.Id==row.track&&Name(c.Flight)==row.profile&&c.Purpose==row.route);
            string key=row.track+"/"+row.route;if(!clocks.ContainsKey(key))clocks[key]=ObserveCourse(w,1d/144).Actions.ConvertAll(a=>a.Time).ToArray();
            context=row.track+" "+row.route+" changed="+row.input+" shift="+row.shift;
            var fixture=shift==0?w:ShiftChainSecond(w,shift,stone);var hit=CourseFirstHit(fixture,clocks[key],row.input,row.shift);
            Need(hit!=null&&hit.Players[0].LastHit.Id==row.hit,"same actual impact after geometry change");
            var g=Clone(hit);Advance(g,g.Players[0].Recovering+.15,.001);Near(g.Players[0].Distance,row.reactionX,"same candidate reaction",1e-5);
            double target=ObstacleGeometry.Right(NextObstacle(g))+15;List<object> samples=null;
            foreach(double step in Steps)
            {
                Need(ReplayRecovery(g,taps,target,step,out samples),"nominal whole union six dt");
                for(int i=0;i<taps.Length;i++)foreach(int sign in new[]{-1,1})
                {var moved=(double[])taps.Clone();moved[i]+=sign*.02;Need(ReplayRecovery(g,moved,target,step,out samples),"individual actual-time20ms whole union six dt");}
            }
            ReplayRecovery(g,taps,target,.01,out samples);
            output.Add(new{row.track,row.profile,row.route,changedInput=row.input,shiftSeconds=row.shift,classification=cls,geometryShift=shift,offsetsSeconds=taps,targetX=target,steps=Steps,individualWindowSeconds=.02,samples});
        }
        Need(output.Count==30,"all assigned first-hit states validated");
        File.WriteAllText(Path.Combine(ProjectRoot,"Verification/rhythm-r5-recovery-spacing-replay.json"),JsonSerializer.Serialize(output,options)+"\n");
        Console.WriteLine("PASS assigned recovery candidate replay: "+checks+" assertions / "+games+" actual-prefix games / "+output.Count+" real first-hit states; nominal and everyinput +/-20ms in each of six dt; complete next unionRight+15, no additional hit. Geometry-only candidates: before exitgate+20, chain second gate and its ring+30; bare-to-chain unchanged. Not whole-course acceptance.");
    }
}
