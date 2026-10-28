using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.BotPrototype;

static class TimingGenerator
{
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
    static readonly List<Library> Libraries=new List<Library>();
    static readonly List<string> Lines=new List<string>();
    static readonly List<object> Blockers=new List<object>();
    static readonly Dictionary<string,string> Sources=new Dictionary<string,string>();
    static readonly Stopwatch Clock=Stopwatch.StartNew();
    static int probes,traceRuns,plansPublished;
    public sealed class Library
    {
        public string trackId,profile,difficulty,catalogHash;
        public string[] catalogIds;
        public List<Signature> signatures=new List<Signature>();
        public List<Published> plans=new List<Published>();
        public object timingCoverage;
    }
    public sealed class Signature
    {
        public string id;
        public string[] successfulGoalIds,missedGoalIds,hitObstacleIds;
        public List<string> planIds=new List<string>();
    }
    public sealed class Published
    {
        public string id,signatureId;
        public double[] distances;
        public object lagger,constant,timing;
        internal double FullTime;
    }
    sealed class Sample {public double shift,target,time;public bool valid;}
    sealed class Window {public int index;public List<Sample> samples=new List<Sample>();}
    sealed class Coverage
    {
        public int firstEdge,secondEdge;
        public string firstObstacle,secondObstacle;
        public double[] firstInterval,secondInterval;
        public double[] firstOffsets,secondOffsets;
        public List<double[]> routes=new List<double[]>();
        public List<object> points=new List<object>();
    }
    sealed class Event {public string kind,id;public double time,distance;}
    static JsonElement Get(JsonElement e,string n)=>e.EnumerateObject().First(p=>p.Name.Equals(n,StringComparison.OrdinalIgnoreCase)).Value;
    static bool Try(JsonElement e,string n,out JsonElement v){foreach(var p in e.EnumerateObject())if(p.Name.Equals(n,StringComparison.OrdinalIgnoreCase)){v=p.Value;return true;}v=default;return false;}
    static string HashFile(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static string HashText(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    static string RouteKey(double[] a)=>string.Join(",",a.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)));
    static FlightSettings Flight(string p)=>p=="Chosen"?FlightPresets.Chosen:FlightPresets.FastFlip;
    static string Profile(FlightSettings f)=>f.FirstAction==FirstAction.Jump?"Chosen":"FastFlip";
    static string[] Strings(JsonElement e)=>e.EnumerateArray().Select(x=>x.GetString()).ToArray();
    static double[] Numbers(JsonElement e)=>e.EnumerateArray().Select(x=>x.GetDouble()).ToArray();
    static void Say(string s){Console.WriteLine(s);Lines.Add(s);}
    static ProbeResult Run(Track t,FlightSettings f,double[] a,PartyCamera c=PartyCamera.Lagger,double step=1d/60){probes++;return Probe.Run(t,f,a,c,step);}
    static bool Ordered(double[] a)=>a.All(x=>x>=0&&double.IsFinite(x))&&a.Zip(a.Skip(1),(x,y)=>x<=y).All(x=>x);
    static string[] Hits(ProbeResult r)=>r.Game.Players[0].HitObstacleIds.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    static bool Same(ProbeResult r,ProbeResult b)=>r.Finished&&r.Rejected==0&&r.CompletedIds.SequenceEqual(b.CompletedIds)&&Hits(r).SequenceEqual(Hits(b))&&r.Game.Players[0].SlowsEarned==b.Game.Players[0].SlowsEarned;
    static string SignatureKey(ProbeResult r)=>string.Join("|",r.CompletedIds)+"/missed="+string.Join("|",r.MissedIds)+"/hits="+string.Join("|",Hits(r));
    static int Classify(ProbeResult r)
    {if(!r.Finished||r.Rejected!=0)return -1;if(r.Completed==r.Total)return 4;double s=r.Score;if(s>=85-1e-8&&s<=95+1e-8)return 3;if(s>=70-1e-8&&s<=80+1e-8)return 2;if(s>=60-1e-8&&s<=70+1e-8)return 1;if(s>=40-1e-8&&s<=60+1e-8)return 0;return -1;}
    static GameState Begin(Track t,FlightSettings f,PartyCamera c){var g=RaceSimulation.CreateSolo(t,f,c,-288.8,1520);RaceSimulation.Press(g,0);RaceSimulation.Advance(g,3);return g;}
    static double AtEvent(Track t,FlightSettings f,PartyCamera c,Edge[] actions,double time)
    {var g=Begin(t,f,c);foreach(var a in actions){if(a.Time>=time-1e-9)break;RaceSimulation.Advance(g,Math.Max(0,a.Time-g.Elapsed));RaceSimulation.Press(g,0);}RaceSimulation.Advance(g,Math.Max(0,time-g.Elapsed));return g.Players[0].Distance;}
    static object Trace(Track t,FlightSettings f,PartyCamera c,ProbeResult expected)
    {
        traceRuns++;var g=Begin(t,f,c);var p=g.Players[0];var success=new HashSet<string>();var hits=new HashSet<string>();var contact=new HashSet<string>();var events=new List<Event>();var bounds=new List<object>();int finish=0;
        Action<double> advance=dt=>
        {
            double bt=g.Elapsed,bx=p.Distance;RaceSimulation.Advance(g,dt);
            var newly=p.SuccessfulRiskIds.Where(id=>!success.Contains(id)&&t.RiskZones.Any(z=>z.Id==id&&!z.IsFinish&&!z.IsNegative&&!z.IsCoin)).ToArray();
            if(newly.Length>1)throw new Exception("Multiple successes in trace step; reduce step: "+t.Id);
            foreach(string id in newly){if(p.LastSuccess==null||p.LastSuccess.Id!=id)throw new Exception("Success timestamp missing");events.Add(new Event{kind="positive-success",id=id,time=p.LastSuccess.At});success.Add(id);}
            foreach(string id in p.HitObstacleIds.Where(id=>!hits.Contains(id)).ToArray()){if(p.LastHit==null||p.LastHit.Id!=id)throw new Exception("Hit timestamp missing");events.Add(new Event{kind="hit",id=id,time=p.LastHit.At});hits.Add(id);}
            if(p.FinishBoostsEarned>finish){events.Add(new Event{kind="finish-contact",id=p.LastFinishBoost.Id,time=p.LastFinishBoost.At});finish=p.FinishBoostsEarned;}
            foreach(string id in p.CollectedRiskIds.Where(id=>!contact.Contains(id)).ToArray()){bounds.Add(new{id,observedAfter=bt,observedBy=g.Elapsed,distanceFrom=bx,distanceTo=p.Distance});contact.Add(id);}
        };
        foreach(var a in expected.Actions){while(g.Elapsed<a.Time-1e-10&&g.Phase=="racing")advance(Math.Min(1d/120,a.Time-g.Elapsed));if(g.Phase!="racing")break;bool accepted=RaceSimulation.Press(g,0);if(accepted!=a.Accepted)throw new Exception("Trace input acceptance differs");}
        while(g.Phase=="racing")advance(1d/120);
        if(!p.SuccessfulRiskIds.SetEquals(expected.Game.Players[0].SuccessfulRiskIds)||!p.HitObstacleIds.SetEquals(expected.Game.Players[0].HitObstacleIds)||p.Status!=expected.Status||Math.Abs(p.TerminalTime.Value-expected.Time)>1e-7)throw new Exception("Actual event trace differs from validated distance tape");
        var ordered=events.OrderBy(e=>e.time).ToArray();foreach(var e in ordered)e.distance=AtEvent(t,f,c,expected.Actions,e.time);
        return new{events=ordered,firstContactBounds=bounds,ordering="Actual LastSuccess/LastHit/LastFinishBoost timestamps; distances replayed to each timestamp using ordinary Press/Advance. First contact is an observed interval, not an invented exact timestamp."};
    }
    static object Detail(Track t,FlightSettings f,PartyCamera c,ProbeResult r)
    {
        var p=r.Game.Players[0];return new{status=r.Status.ToString(),terminalDistance=p.Distance,time=r.Time,total=r.Total,completed=r.Completed,score=r.Score,successfulGoalIds=r.CompletedIds,completedIds=r.CompletedIds,missedGoalIds=r.MissedIds,hitObstacleIds=Hits(r),collectedRiskIds=p.CollectedRiskIds.OrderBy(x=>x).ToArray(),successfulRiskIds=p.SuccessfulRiskIds.OrderBy(x=>x).ToArray(),failedRiskIds=p.FailedRiskIds.OrderBy(x=>x).ToArray(),positiveIds=p.SuccessfulRiskIds.Where(id=>t.RiskZones.Any(z=>z.Id==id&&!z.IsFinish&&!z.IsNegative&&!z.IsCoin)).OrderBy(x=>x).ToArray(),collisions=r.Collisions,boosts=p.BoostsEarned,slows=p.SlowsEarned,finishBoosts=p.FinishBoostsEarned,recoverySeconds=p.CompletedRecoveries*RaceConfig.RecoveryDuration+p.RecoveryElapsed,accepted=r.Accepted,rejected=r.Rejected,minMarginLowerBound=r.MinMarginLowerBound,actions=r.Actions,actualEventTrace=Trace(t,f,c,r)};
    }
    static Signature MakeSignature(string id,ProbeResult r)=>new Signature{id=id,successfulGoalIds=r.CompletedIds,missedGoalIds=r.MissedIds,hitObstacleIds=Hits(r)};
    static bool Publish(Library library,Signature signature,Track t,FlightSettings f,double[] route,ProbeResult reference,object timing)
    {
        if(library.plans.Any(p=>p.signatureId==signature.id&&RouteKey(p.distances)==RouteKey(route)))return false;
        var l=Run(t,f,route,PartyCamera.Lagger,1d/120);var c=Run(t,f,route,PartyCamera.Constant,1d/120);
        if(!Same(l,reference)||Classify(l)!=(int)Enum.Parse<BotDifficulty>(library.difficulty)||(library.difficulty=="Master"&&!Same(c,reference)))return false;
        string id=library.trackId+"/"+library.profile+"/"+library.difficulty+"/"+signature.id.Substring(0,Math.Min(12,signature.id.Length))+"/t"+signature.planIds.Count;
        var published=new Published{id=id,signatureId=signature.id,distances=(double[])route.Clone(),lagger=Detail(t,f,PartyCamera.Lagger,l),constant=Detail(t,f,PartyCamera.Constant,c),timing=timing,FullTime=l.Time};
        library.plans.Add(published);signature.planIds.Add(id);plansPublished++;return true;
    }
    static List<Sample[]> Groups(Window w)
    {
        var result=new List<Sample[]>();var current=new List<Sample>();Action flush=()=>{if(current.Count>0){result.Add(current.ToArray());current.Clear();}};
        foreach(var s in w.samples.OrderBy(s=>s.shift)){if(!s.valid){flush();continue;}if(current.Count>0&&s.shift-current.Last().shift>10.000001)flush();current.Add(s);}flush();return result;
    }
    static Window Scan(Track t,FlightSettings f,double[] source,ProbeResult reference,int index)
    {
        var w=new Window{index=index};for(double shift=-110;shift<=110;shift+=10)
        {
            var a=(double[])source.Clone();a[index]+=shift;if(!Ordered(a)){w.samples.Add(new Sample{shift=shift,target=a[index]});continue;}
            var r=Run(t,f,a);w.samples.Add(new Sample{shift=shift,target=a[index],time=r.Actions[index].Time,valid=Same(r,reference)});
        }return w;
    }
    static List<Window> ReuseWindows(string id,double[] source)
    {
        foreach(string path in new[]{"Verification/bot-diversity-timing-spike.json","Tools/BotDiversitySpike/TimingWindows/initial-eight-bases.json"})
        {
            if(!File.Exists(path))continue;using var d=JsonDocument.Parse(File.ReadAllText(path));
            foreach(var e in Get(d.RootElement,"evidence").EnumerateArray())if(Get(e,"Id").GetString()==id&&Numbers(Get(e,"baseDistances")).SequenceEqual(source))
                return Get(e,"singleWindows").EnumerateArray().Select(w=>new Window{index=Get(w,"edge").GetInt32(),samples=Get(w,"samples").EnumerateArray().Select(s=>new Sample{shift=Get(s,"Shift").GetDouble(),target=Get(s,"Target").GetDouble(),time=Get(s,"ActualTime").GetDouble(),valid=Get(s,"Valid").GetBoolean()}).ToList()}).ToList();
        }return null;
    }
    static Coverage FindCoverage(Track t,FlightSettings f,double[] source,ProbeResult reference,List<Window> windows)
    {
        var choices=windows.Select(w=>new{w,g=Groups(w).OrderByDescending(g=>g.Last().target-g.First().target).FirstOrDefault()}).Where(x=>x.g!=null&&x.g.Length>=3&&x.g.Last().target-x.g.First().target>=20).ToArray();
        var first=choices.Where(x=>source[x.w.index]<t.Length/2).OrderByDescending(x=>x.g.Last().target-x.g.First().target).Take(3);
        var second=choices.Where(x=>source[x.w.index]>=t.Length/2).OrderByDescending(x=>x.g.Last().target-x.g.First().target).Take(3);
        foreach(var a in first)foreach(var b in second)
        {
            if(Math.Abs(source[a.w.index]-source[b.w.index])<t.Length/4)continue;
            Func<Sample[],Sample[]> points=g=>new[]{g.First(),g.OrderBy(s=>Math.Abs(s.target-(g.First().target+g.Last().target)/2)).First(),g.Last()};var ap=points(a.g);var bp=points(b.g);
            var proof=new Coverage{firstEdge=a.w.index,secondEdge=b.w.index,firstInterval=new[]{a.g.First().target,a.g.Last().target},secondInterval=new[]{b.g.First().target,b.g.Last().target}};
            var ao=t.Obstacles.OrderBy(o=>Math.Abs(o.X-source[a.w.index])).First();var bo=t.Obstacles.OrderBy(o=>Math.Abs(o.X-source[b.w.index])).First();proof.firstObstacle=ao.Id;proof.secondObstacle=bo.Id;proof.firstOffsets=ap.Select(s=>s.target-ao.X).ToArray();proof.secondOffsets=bp.Select(s=>s.target-bo.X).ToArray();bool all=true;
            for(int i=0;i<3&&all;i++)for(int j=0;j<3;j++)
            {
                var route=(double[])source.Clone();route[a.w.index]=ap[i].target;route[b.w.index]=bp[j].target;
                var l=Run(t,f,route,PartyCamera.Lagger,1d/120);var c=Run(t,f,route,PartyCamera.Constant,1d/120);
                if(!Same(l,reference)||(reference.Completed==reference.Total&&!Same(c,reference))){all=false;break;}
                proof.routes.Add(route);proof.points.Add(new{first=new[]{"early","middle","late"}[i],second=new[]{"early","middle","late"}[j],firstActualDistance=l.Actions[a.w.index].Distance,secondActualDistance=l.Actions[b.w.index].Distance,firstActualTime=l.Actions[a.w.index].Time,secondActualTime=l.Actions[b.w.index].Time});
            }
            if(all)return proof;
        }return null;
    }
    static uint Next(ref uint x){x^=x<<13;x^=x>>17;x^=x<<5;return x;}
    static double[] Joint(Track t,FlightSettings f,double[] source,ProbeResult reference,List<Window> windows,int seed)
    {
        uint state=(uint)(seed+1)*0x9E3779B9u;var requested=(double[])source.Clone();
        foreach(var w in windows){var good=w.samples.Where(s=>s.valid).ToArray();if(good.Length>0)requested[w.index]=good[Next(ref state)%(uint)good.Length].target;}
        Func<double[],bool> valid=a=>Ordered(a)&&Same(Run(t,f,a),reference);
        if(valid(requested))return requested;
        var half=(double[])source.Clone();foreach(var w in windows){double desired=(requested[w.index]-source[w.index])*.5;half[w.index]=w.samples.Where(s=>s.valid).OrderBy(s=>Math.Abs(s.shift-desired)).ThenBy(s=>Math.Abs(s.shift)).First().target;}if(valid(half))return half;
        var spaced=(double[])source.Clone();var chosen=new List<int>();
        foreach(var w in windows.Where(w=>Math.Abs(requested[w.index]-source[w.index])>1e-8).OrderByDescending(w=>w.samples.Where(s=>s.valid).Max(s=>s.target)-w.samples.Where(s=>s.valid).Min(s=>s.target)))
            if(chosen.All(i=>Math.Abs(source[i]-source[w.index])>=700)){chosen.Add(w.index);spaced[w.index]=requested[w.index];if(chosen.Count>=3)break;}
        return valid(spaced)?spaced:null;
    }
    static Library NewLibrary(Track t,FlightSettings f,string difficulty)=>new Library{trackId=t.Id,profile=Profile(f),difficulty=difficulty,catalogIds=Probe.Catalog(t),catalogHash=HashText(string.Join("\n",Probe.Catalog(t)))};
    static object CoverageDetail(Coverage c,IEnumerable<Published> plans,string signatureId)
    {
        return new{status="PROVED",signatureId,c.firstEdge,c.secondEdge,c.firstObstacle,c.secondObstacle,firstMeasuredInterval=c.firstInterval,secondMeasuredInterval=c.secondInterval,firstPositionsRelativeObstacleX=c.firstOffsets,secondPositionsRelativeObstacleX=c.secondOffsets,coverageOfSampledInterval=new[]{1d,1d},allNineWholeRoutesValid=true,points=c.points,planIds=c.routes.Select(a=>plans.Single(p=>RouteKey(p.distances)==RouteKey(a)).id).ToArray(),note="Sampled connected feasible intervals, not a continuous guarantee. Each of nine Cartesian whole routes verified at1/120; local positions differ relative to obstacle X, not a common clock shift."};
    }
    static void Masters()
    {
        foreach(var t in RaceTracks.All)foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        {
            var library=NewLibrary(t,f,"Master");var basePlan=BotPlanCatalog.Select(t.Id,f,BotDifficulty.Master,0);var source=basePlan.Distances;var reference=Run(t,f,source,PartyCamera.Lagger,1d/120);
            var signature=MakeSignature(HashText(SignatureKey(reference)),reference);library.signatures.Add(signature);
            var windows=ReuseWindows(basePlan.Id,source);bool reused=windows!=null;
            if(windows==null)windows=Enumerable.Range(0,source.Length).Select(i=>Scan(t,f,source,reference,i)).ToList();
            var coverage=FindCoverage(t,f,source,reference,windows);if(coverage==null)throw new Exception("Master timing coverage not found: "+basePlan.Id);
            foreach(var a in coverage.routes)if(!Publish(library,signature,t,f,a,reference,new{method="two-maneuver early/middle/late Cartesian coverage",basisPlan=basePlan.Id}))throw new Exception("Fine Master Cartesian publication failed");
            Publish(library,signature,t,f,source,reference,new{method="preserved baseline Master",basisPlan=basePlan.Id});
            for(int seed=0;seed<80&&library.plans.Count<29;seed++)
            {
                var a=Joint(t,f,source,reference,windows,seed);if(a!=null)Publish(library,signature,t,f,a,reference,new{method="measured single-window point selection plus at most two whole-route repairs",seed,basisPlan=basePlan.Id});
            }
            if(library.plans.Count<20)throw new Exception("Fewer than20 meaningful Master whole plans: "+basePlan.Id);
            library.timingCoverage=CoverageDetail(coverage,library.plans,signature.id);Libraries.Add(library);
            Say("MASTER "+t.Id+"/"+Profile(f)+" plans="+library.plans.Count+" Cartesian9=PASS windows="+(reused?"reused accepted scan":"measured")+" simulations="+probes+" traces="+traceRuns);Save("MASTERS_WORKING");
        }
        Save("MASTERS_READY");
    }
    static List<Window> TargetedWindows(Track t,FlightSettings f,double[] source,ProbeResult reference)
    {
        var masterLibrary=Libraries.Single(l=>l.trackId==t.Id&&l.profile==Profile(f)&&l.difficulty=="Master");
        using var coverage=JsonDocument.Parse(JsonSerializer.Serialize(masterLibrary.timingCoverage,Json));
        var firstInterval=Numbers(Get(coverage.RootElement,"firstMeasuredInterval"));var secondInterval=Numbers(Get(coverage.RootElement,"secondMeasuredInterval"));
        double[] anchors={(firstInterval[0]+firstInterval[1])/2,(secondInterval[0]+secondInterval[1])/2};
        var indices=anchors.SelectMany(x=>Enumerable.Range(0,source.Length).Where(i=>source[i]<7500).OrderBy(i=>Math.Abs(source[i]-x)).Take(3)).Distinct().OrderBy(i=>i).ToArray();
        return indices.Select(i=>Scan(t,f,source,reference,i)).ToList();
    }
    static object Changed(double[] basis,double[] a)=>Enumerable.Range(0,basis.Length).Where(i=>Math.Abs(basis[i]-a[i])>1e-8).Select(i=>new{index=i,before=basis[i],after=a[i],delta=a[i]-basis[i]}).ToArray();
    static void EnsureThree(Library library,Signature signature,Track t,FlightSettings f,double[] source,ProbeResult reference)
    {
        int attempts=0;uint random=Convert.ToUInt32(signature.id.Substring(0,8),16);if(random==0)random=1;
        var ordinary=Enumerable.Range(0,source.Length).Where(i=>source[i]<7500&&i<source.Length-1).ToArray();
        for(int sample=0;sample<12&&signature.planIds.Count<3;sample++)
        {
            var a=(double[])source.Clone();var chosen=new List<int>();
            foreach(int i in ordinary.OrderBy(i=>Next(ref random)))
                if(chosen.All(j=>Math.Abs(source[i]-source[j])>=600)){chosen.Add(i);a[i]+=new[]{-30d,-20,-10,10,20,30}[Next(ref random)%6];if(chosen.Count==3)break;}
            if(chosen.Count==0||!Ordered(a))continue;attempts++;var coarse=Run(t,f,a);if(!Same(coarse,reference))continue;
            Publish(library,signature,t,f,a,reference,new{method="bounded actual-state validated multi-action retime",sample,changedEdges=Changed(source,a),maxTargetShift=a.Zip(source,(x,y)=>Math.Abs(x-y)).Max()});
        }
        foreach(double magnitude in new[]{40d,25,15,10,5})
        {
            if(signature.planIds.Count>=3)break;
            foreach(int index in ordinary.OrderBy(i=>Math.Abs(source[i]-t.Length*.35)))
            {
                if(signature.planIds.Count>=3)break;
                foreach(double sign in new[]{-1d,1})
                {
                    if(signature.planIds.Count>=3)break;var a=(double[])source.Clone();a[index]+=sign*magnitude;if(!Ordered(a))continue;attempts++;
                    var coarse=Run(t,f,a);if(!Same(coarse,reference))continue;
                    Publish(library,signature,t,f,a,reference,new{method="measured complete-route single ordinary-maneuver retime fallback",index,shift=sign*magnitude,changedEdges=Changed(source,a),maxTargetShift=magnitude});
                }
            }
        }
        if(signature.planIds.Count<3)throw new Exception("Fewer than3 meaningful whole timing plans for "+library.trackId+"/"+library.profile+"/"+library.difficulty+" signature "+signature.id+" after "+attempts+" measured candidates (ordinary shifts5..40, no subunit fake shifts)");
    }
    static string outcomesHash;
    static void Outcomes()
    {
        string inputPath="Verification/bot-diversity-outcomes.json";
        if(!File.Exists(inputPath))throw new Exception("Outcome artifact not ready; Master stage already complete");
        string snapshot=File.ReadAllText(inputPath);outcomesHash=HashText(snapshot);using var doc=JsonDocument.Parse(snapshot);
        var all=Get(doc.RootElement,"libraries").EnumerateArray().ToArray();
        foreach(var item in all.OrderBy(l=>Get(l,"difficulty").GetString()=="Confident"?0:1).ThenBy(l=>Get(l,"trackId").GetString(),StringComparer.Ordinal).ThenBy(l=>Get(l,"profile").GetString(),StringComparer.Ordinal).ThenBy(l=>Get(l,"difficulty").GetString(),StringComparer.Ordinal))
        {
            string trackId=Get(item,"trackId").GetString(),profile=Get(item,"profile").GetString(),difficulty=Get(item,"difficulty").GetString();var t=RaceTracks.All.Single(t=>t.Id==trackId);var f=Flight(profile);var library=NewLibrary(t,f,difficulty);
            if(!Strings(Get(item,"catalogIds")).SequenceEqual(library.catalogIds)||Get(item,"catalogHash").GetString()!=library.catalogHash)throw new Exception("Fixed catalog differs from production "+trackId);
            var inputs=Get(item,"signatures").EnumerateArray().ToArray();if(inputs.Length<12)throw new Exception("Fewer than12 outcome signatures: "+trackId+"/"+profile+"/"+difficulty);
            string proofSignature=null;Coverage proof=null;double[] proofBasis=null;ProbeResult proofReference=null;
            if(difficulty=="Confident")
            {
                foreach(var input in inputs.Take(3))
                {
                    var a=Numbers(Get(Get(input,"basis"),"distances"));var r=Run(t,f,a,PartyCamera.Lagger,1d/120);var windows=TargetedWindows(t,f,a,r);proof=FindCoverage(t,f,a,r,windows);
                    if(proof==null)
                    {
                        var known=windows.Select(w=>w.index).ToHashSet();windows.AddRange(Enumerable.Range(0,a.Length).Where(i=>!known.Contains(i)).Select(i=>Scan(t,f,a,r,i)));proof=FindCoverage(t,f,a,r,windows);
                    }
                    if(proof!=null){proofSignature=Get(input,"id").GetString();proofBasis=a;proofReference=r;break;}
                }
                if(proof==null)throw new Exception("Explicit two-separated-maneuver Confident timing proof not found after3 signatures/full window scans: "+trackId+"/"+profile);
            }
            foreach(var input in inputs)
            {
                string id=Get(input,"id").GetString();var basis=Numbers(Get(Get(input,"basis"),"distances"));var reference=id==proofSignature?proofReference:Run(t,f,basis,PartyCamera.Lagger,1d/120);
                if(Classify(reference)!=(int)Enum.Parse<BotDifficulty>(difficulty)||!reference.CompletedIds.SequenceEqual(Strings(Get(input,"successfulGoalIds")))||!reference.MissedIds.SequenceEqual(Strings(Get(input,"missedGoalIds")))||!Hits(reference).SequenceEqual(Strings(Get(input,"hitObstacleIds"))))throw new Exception("Fine outcome basis/signature differs: "+id);
                var signature=MakeSignature(id,reference);library.signatures.Add(signature);
                if(!Publish(library,signature,t,f,basis,reference,new{method="validated outcome basis"}))throw new Exception("Outcome basis publication failed: "+id);
                if(id==proofSignature)foreach(var a in proof.routes)Publish(library,signature,t,f,a,reference,new{method="two-separated-maneuver early/middle/late Cartesian coverage",changedEdges=Changed(basis,a)});
                EnsureThree(library,signature,t,f,basis,reference);
            }
            library.timingCoverage=proof!=null?CoverageDetail(proof,library.plans,proofSignature):new{status="TIMING_VARIANTS_VERIFIED",pairProofRef=trackId+"/"+profile+"/Confident",minimumVariantsPerSignature=library.signatures.Min(s=>s.planIds.Count),meaningfulOrdinaryInputShifts="At least5 units; multi-action10..30 preferred, full-mask validated. No0.001-only variants."};
            Libraries.Add(library);
            Say("OUTCOMES "+trackId+"/"+profile+"/"+difficulty+" signatures="+library.signatures.Count+" timingPlans="+library.plans.Count+" minimumTiming="+library.signatures.Min(s=>s.planIds.Count)+" broadProof="+(proof!=null)+" simulations="+probes+" traces="+traceRuns);Save("OUTCOMES_WORKING");
        }
        bool complete=all.Length==40&&Libraries.Count==50;
        if(complete)
        {
            foreach(var group in Libraries.GroupBy(l=>l.trackId+"/"+l.profile))
            {
                var ordered=group.OrderBy(l=>(int)Enum.Parse<BotDifficulty>(l.difficulty)).ToArray();
                double[] means=ordered.Select(l=>l.signatures.Average(s=>l.plans.Where(p=>p.signatureId==s.id).Average(p=>p.FullTime))).ToArray();
                if(!Enumerable.Range(0,4).All(i=>means[i]>means[i+1]+1e-7))throw new Exception("Signature-uniform mean finish times not strictly ordered: "+group.Key+" "+string.Join(",",means));
            }
        }
        Save(complete?"PASS":"OUTCOMES_PARTIAL");
    }
    static void Save(string status)
    {
        bool stable=Sources.All(p=>File.Exists(p.Key)&&HashFile(p.Key)==p.Value);
        File.WriteAllText("Verification/bot-diversity-plans.json",JsonSerializer.Serialize(new{schemaVersion=1,status,baseRevision="9a67f29",selection="Offline validated whole plans. Runtime must shuffle outcome signatures first, then timing variants within that signature.",libraryCount=Libraries.Count,planCount=plansPublished,simulationRuns=probes,traceRuns,wallSeconds=Clock.Elapsed.TotalSeconds,sourceHashes=Sources,sourceStable=stable,outcomesSnapshotSha256=outcomesHash,outcomesFileUnchangedAtSave=outcomesHash==null||!File.Exists("Verification/bot-diversity-outcomes.json")?true:HashFile("Verification/bot-diversity-outcomes.json")==outcomesHash,blockers=Blockers,libraries=Libraries},Json));
        File.WriteAllLines("Verification/bot-diversity-plans.txt",Lines.Concat(new[]{"STATUS "+status+" libraries="+Libraries.Count+" plans="+plansPublished+" simulations="+probes+" traceRuns="+traceRuns+" sourceStable="+stable}));
    }
    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        foreach(string path in Directory.GetFiles("Assets/Scripts/Core","*.cs").Concat(new[]{"Tools/BotPrototype/SharedProbe.cs","Tools/BotDiversity/TimingGenerator.cs"}))Sources[path.Replace((char)92,(char)47)]=HashFile(path);
        try{Masters();Say("MASTERS READY: all10 pairs with two-separated-maneuver early/middle/late whole-route proofs.");if(args.Contains("--complete"))Outcomes();else Save("MASTERS_READY");return 0;}
        catch(Exception e){Say("BLOCKED: "+e);Blockers.Add(new{error=e.ToString()});Save("BLOCKED");return 1;}
    }
}