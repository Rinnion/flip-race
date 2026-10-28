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
static class CalibrationAudit
{
    const string Input="Verification/bot-diversity-plans.json",ExpectedHash="5BCDD9F6D597FE041D71A6791795D2903B6D09AC3B9818EA5953B3D78185AA46";
    static readonly string[] Levels={"Novice","Amateur","Confident","Strong","Master"};
    static readonly JsonSerializerOptions Options=new JsonSerializerOptions{WriteIndented=true};
    static readonly List<string> Failures=new List<string>();static int checks,failureCount,planCount,signatureCount,metricCount,traceCount,broadProofs;
    static readonly List<object> Libraries=new List<object>(),Means=new List<object>(),CurrentSourceDifferences=new List<object>();
    const string DataPrefixSha256="83D897FDDE9FF8C8144E3237DD26E3EEB7253D4218D07C1896FFC699666592D7";static string context="root";
    static JsonElement G(JsonElement e,string n)=>e.GetProperty(n);
    static string S(JsonElement e,string n)=>G(e,n).GetString();static double N(JsonElement e,string n)=>G(e,n).GetDouble();static int I(JsonElement e,string n)=>G(e,n).GetInt32();
    static string[] Strings(JsonElement e)=>e.EnumerateArray().Select(x=>x.GetString()).ToArray();static double[] Numbers(JsonElement e)=>e.EnumerateArray().Select(x=>x.GetDouble()).ToArray();
    static string Key(IEnumerable<string> values)=>string.Join("|",values.OrderBy(x=>x,StringComparer.Ordinal));static string HashText(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));static string HashFile(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static void Check(bool ok,string why){checks++;if(!ok){failureCount++;if(Failures.Count<150)Failures.Add(context+": "+why);}}
    static bool Same(IEnumerable<string>a,IEnumerable<string>b)=>a.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(b.OrderBy(x=>x,StringComparer.Ordinal));
    static bool Unique(string[] a)=>a.Length==a.Distinct(StringComparer.Ordinal).Count();
    static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
    static bool Near(double a,double b,double tolerance=2e-6)=>Finite(a)&&Finite(b)&&Math.Abs(a-b)<=tolerance;
    static string SigKey(JsonElement signature)=>"successful="+Key(Strings(G(signature,"successfulGoalIds")))+"\nmissed="+Key(Strings(G(signature,"missedGoalIds")))+"\nhits="+Key(Strings(G(signature,"hitObstacleIds")));
    static string MetricSignature(JsonElement m)=>Key(Strings(G(m,"successfulGoalIds")))+"/missed="+Key(Strings(G(m,"missedGoalIds")))+"/hits="+Key(Strings(G(m,"hitObstacleIds")));
    static string PositiveKey(JsonElement m)=>Key(Strings(G(m,"positiveIds")));
    static string[] Catalog(Track t)=>t.Obstacles.Select(o=>"obstacle:"+o.Id).Concat(t.RiskZones.Where(z=>!z.IsNegative&&!z.IsCoin).Select(z=>(z.IsFinish?"finish:":"positive:")+z.Id)).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    static void Metrics(JsonElement m,JsonElement plan,JsonElement signature,Track track,bool constant,bool master,int level)
    {
        metricCount++;string status=S(m,"status");double distance=N(m,"terminalDistance"),time=N(m,"time");string[] completed=Strings(G(m,"successfulGoalIds")),missed=Strings(G(m,"missedGoalIds")),hits=Strings(G(m,"hitObstacleIds")),positive=Strings(G(m,"positiveIds")),successful=Strings(G(m,"successfulRiskIds")),collected=Strings(G(m,"collectedRiskIds")),failed=Strings(G(m,"failedRiskIds"));string[] catalog=Catalog(track);
        Check(status=="Finished"||(constant&&status=="Eliminated"),"legal terminal status");Check(Finite(time)&&time>0&&Finite(distance)&&distance>=0&&distance<=track.Length+1e-6,"finite terminal time/distance");Check(status!="Finished"||Near(distance,track.Length),"finish crosses full track");
        Check(Unique(completed)&&Unique(missed)&&Unique(hits)&&Unique(positive)&&Unique(successful)&&Unique(collected)&&Unique(failed),"sets contain unique IDs");Check(Same(completed.Concat(missed),catalog)&&!completed.Intersect(missed).Any(),"goals partition fixed catalog");Check(Same(completed,Strings(G(m,"completedIds"))),"completedIds matches successful goals");Check(I(m,"total")==catalog.Length&&I(m,"completed")==completed.Length&&Near(N(m,"score"),100d*completed.Length/catalog.Length),"counts/score arithmetic");
        Check(hits.All(id=>track.Obstacles.Any(o=>o.Id==id))&&hits.Length==I(m,"collisions"),"unique real hit IDs/collision count");Check(positive.All(id=>track.RiskZones.Any(z=>z.Id==id&&!z.IsFinish&&!z.IsNegative&&!z.IsCoin))&&Same(positive,completed.Where(id=>id.StartsWith("positive:",StringComparison.Ordinal)).Select(id=>id.Substring(9))),"positive success IDs match goal IDs");Check(positive.All(successful.Contains)&&successful.All(collected.Contains)&&failed.All(collected.Contains),"success/failed are actual collected attempts");Check(I(m,"boosts")==positive.Length,"boost count equals actual ordinary positive successes");
        var expectedGoals=new List<string>();foreach(var o in track.Obstacles)if(!hits.Contains(o.Id)&&distance>=ObstacleGeometry.Right(o)+RaceConfig.PlayerRadius-1e-6)expectedGoals.Add("obstacle:"+o.Id);expectedGoals.AddRange(positive.Select(id=>"positive:"+id));foreach(var z in track.RiskZones.Where(z=>z.IsFinish))if(completed.Contains("finish:"+z.Id))expectedGoals.Add("finish:"+z.Id);Check(Same(expectedGoals,completed),"clean obstacle+positive goals independently reconstructed");
        Check(N(m,"recoverySeconds")>=-1e-7&&N(m,"recoverySeconds")<=hits.Length*.5+1e-6,"actual recovery bound");if(status=="Finished")Check(Near(N(m,"recoverySeconds"),hits.Length*.5),"all finish recoveries completed");
        if(!constant)
        {
            Check(Same(completed,Strings(G(signature,"successfulGoalIds")))&&Same(missed,Strings(G(signature,"missedGoalIds")))&&Same(hits,Strings(G(signature,"hitObstacleIds"))),"whole Lagger plan retains signature");
            double lo=new[]{40d,60,70,85,100}[level],hi=new[]{60d,70,80,95,100}[level];Check(N(m,"score")>=lo-1e-7&&N(m,"score")<=hi+1e-7,"actual level range");double expectedTime=track.Length/220d+.5*hits.Length+.3*I(m,"slows")-.3*positive.Length-.25*I(m,"finishBoosts");Check(Near(time,expectedTime,1e-5),"full-route time independently reconstructs served normal effects/recoveries/finish");
        }
        else
        {
            if(status=="Eliminated")Check(Near(distance+14,-288.8+track.Length/36*time,1e-5),"Constant exact full-body left-only boundary");if(status=="Finished")Check(Near(time,N(G(plan,"lagger"),"time"))&&Same(completed,Strings(G(G(plan,"lagger"),"successfulGoalIds"))),"surviving Constant shares full physical outcome/time");
            var full=G(plan,"lagger");Check(completed.All(Strings(G(full,"successfulGoalIds")).Contains)&&hits.All(Strings(G(full,"hitObstacleIds")).Contains)&&positive.All(Strings(G(full,"positiveIds")).Contains),"Constant is actual prefix, no invented full-route successes");
        }
        if(master)Check(status=="Finished"&&completed.Length==catalog.Length&&hits.Length==0&&I(m,"finishBoosts")==1,"Master100 and finish both cameras");
        var actions=G(m,"actions").EnumerateArray().ToArray();double[] targets=Numbers(G(plan,"distances"));Check(I(m,"accepted")==actions.Count(a=>G(a,"Accepted").GetBoolean())&&I(m,"rejected")==actions.Count(a=>!G(a,"Accepted").GetBoolean())&&I(m,"rejected")==0,"actual action acceptance counts");Check(actions.Length<=targets.Length&&(constant||actions.Length==targets.Length),"whole/prefix action counts");double lastTime=-1,lastDistance=-1;var fullActions=G(G(plan,"lagger"),"actions").EnumerateArray().ToArray();
        for(int a=0;a<actions.Length;a++){double at=N(actions[a],"Time"),x=N(actions[a],"Distance");Check(Finite(at)&&Finite(x)&&at>=lastTime-1e-7&&x>=lastDistance-1e-7&&at<=time+1e-6,"chronological finite action");Check(Near(x,targets[a],1e-6),"action reaches target from real distance");if(constant)Check(a<fullActions.Length&&Near(at,N(fullActions[a],"Time"))&&Near(x,N(fullActions[a],"Distance")),"Constant input prefix coincides before terminal");lastTime=at;lastDistance=x;}
        var trace=G(m,"actualEventTrace");var events=G(trace,"events").EnumerateArray().ToArray();traceCount++;lastTime=-1;lastDistance=-1;foreach(var e in events){double at=N(e,"time"),x=N(e,"distance");Check(Finite(at)&&Finite(x)&&at>=lastTime-1e-7&&x>=lastDistance-2e-6&&at<=time+1e-6&&x<=distance+1e-5,"actual event chronology bounded by terminal");lastTime=at;lastDistance=x;}
        Check(Same(events.Where(e=>S(e,"kind")=="positive-success").Select(e=>S(e,"id")),positive),"positive events match successes");Check(Same(events.Where(e=>S(e,"kind")=="hit").Select(e=>S(e,"id")),hits),"hit events match hit set");var finishEvents=events.Where(e=>S(e,"kind")=="finish-contact").ToArray();Check(finishEvents.Length==I(m,"finishBoosts"),"finish contact event count");foreach(var e in finishEvents){Check(track.RiskZones.Any(z=>z.Id==S(e,"id")&&z.IsFinish),"real finish target event");if(completed.Contains("finish:"+S(e,"id")))Check(status=="Finished"&&N(e,"distance")+110<=track.Length+1e-5&&N(e,"time")+.25<=time+1e-5,"completed finish goal has full motion runway/time");}
        var contacts=G(trace,"firstContactBounds").EnumerateArray().ToArray();Check(Same(contacts.Select(e=>S(e,"id")),collected),"contact observations cover actual collected IDs");foreach(var e in contacts)Check(N(e,"observedAfter")<=N(e,"observedBy")+1e-9&&N(e,"observedBy")-N(e,"observedAfter")<=1d/120+1e-6&&N(e,"distanceFrom")<=N(e,"distanceTo")+1e-6,"honest bounded contact interval");
    }
    static object Coverage(JsonElement library,Track t)
    {
        var plans=G(library,"plans").EnumerateArray().ToArray();var signatures=G(library,"signatures").EnumerateArray().ToArray();var bases=signatures.Select(s=>plans.First(p=>S(p,"signatureId")==S(s,"id"))).Select(p=>G(p,"lagger")).ToArray();bool master=S(library,"difficulty")=="Master";
        int positives=bases.Select(PositiveKey).Distinct().Count(),hit=bases.Select(m=>Key(Strings(G(m,"hitObstacleIds")))).Distinct().Count();var goals=t.RiskZones.Where(z=>!z.IsFinish&&!z.IsCoin&&!z.IsNegative).OrderBy(z=>z.X).ToArray();
        var thirds=Enumerable.Range(0,3).Select(i=>new{third=i+1,goalCount=goals.Count(z=>(int)Math.Min(2,z.X/t.Length*3)==i),collectedPlans=bases.Count(m=>goals.Where(z=>(int)Math.Min(2,z.X/t.Length*3)==i).Any(z=>Strings(G(m,"positiveIds")).Contains(z.Id))),missedPlans=bases.Count(m=>goals.Where(z=>(int)Math.Min(2,z.X/t.Length*3)==i).Any(z=>!Strings(G(m,"positiveIds")).Contains(z.Id)))}).ToArray();
        int nonconsecutive=bases.Count(m=>{var a=Enumerable.Range(0,goals.Length).Where(i=>Strings(G(m,"positiveIds")).Contains(goals[i].Id)).ToArray();return a.Length>1&&a.Zip(a.Skip(1),(x,y)=>y-x).Any(d=>d>1);});int forward=bases.Count(m=>!Strings(G(m,"positiveIds")).Contains(goals[0].Id)&&goals.Where(z=>z.X>=t.Length*2/3).Any(z=>Strings(G(m,"positiveIds")).Contains(z.Id)));int reverse=bases.Count(m=>goals.Where(z=>z.X<t.Length/3).Any(z=>Strings(G(m,"positiveIds")).Contains(z.Id))&&goals.Where(z=>z.X>=t.Length*2/3).Any(z=>!Strings(G(m,"positiveIds")).Contains(z.Id)));int combined=bases.Count(m=>I(m,"boosts")>0&&I(m,"collisions")>0);
        int constantSignatures=plans.Select(p=>MetricSignature(G(p,"constant"))).Distinct().Count(),constantPositive=plans.Select(p=>PositiveKey(G(p,"constant"))).Distinct().Count(),constantHit=plans.Select(p=>Key(Strings(G(G(p,"constant"),"hitObstacleIds")))).Distinct().Count(),constantCombined=plans.Count(p=>I(G(p,"constant"),"boosts")>0&&I(G(p,"constant"),"collisions")>0);
        if(!master){Check(signatures.Length>=12&&positives>=6&&hit>=3,"BVAR01 actual signature/positive/hit cardinalities");Check(thirds.All(x=>x.goalCount==0||(x.collectedPlans>0&&x.missedPlans>0))&&nonconsecutive>0&&forward>0&&reverse>0&&combined>0,"BVAR02 actual thirds/nonconsecutive/bidirectional/mixed coverage");Check(constantSignatures>=2&&constantPositive>=2,"actual pre-terminal Constant outcome diversity");if(t.Id==TracksRhythm.CascadeId&&(S(library,"difficulty")=="Novice"||S(library,"difficulty")=="Amateur"))Check(constantPositive>=3&&constantHit>=3&&constantCombined>0,"junior Cascade actual visible positive+hit diversity");}
        return new{signatureCount=signatures.Length,positiveMaskCount=positives,hitMaskCount=hit,thirds,nonconsecutive,earlyMissLateSuccess=forward,earlySuccessLateMiss=reverse,positiveAndHit=combined,constantActualSignatures=constantSignatures,constantPositiveMasks=constantPositive,constantHitMasks=constantHit,constantCombinedPlans=constantCombined};
    }
    static object Timing(JsonElement library,Track t,Dictionary<string,JsonElement> byId)
    {
        var proof=G(library,"timingCoverage");string status=S(proof,"status");var signatures=G(library,"signatures").EnumerateArray().ToArray();var plans=G(library,"plans").EnumerateArray().ToArray();double minimumSeparation=double.PositiveInfinity;
        foreach(var signature in signatures)
        {
            var variants=plans.Where(p=>S(p,"signatureId")==S(signature,"id")).ToArray();Check(variants.Length>=3,">=3 whole timing plans per signature");var tapes=variants.Select(p=>Numbers(G(p,"distances"))).ToArray();Check(tapes.Select(a=>string.Join(",",a.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))).Distinct().Count()==tapes.Length,"no duplicate whole timing tapes");
            for(int a=0;a<tapes.Length;a++)for(int b=a+1;b<tapes.Length;b++){Check(tapes[a].Length==tapes[b].Length,"same-signature timing retains edge count");if(tapes[a].Length!=tapes[b].Length)continue;double separation=tapes[a].Zip(tapes[b],(x,y)=>Math.Abs(x-y)).Max();minimumSeparation=Math.Min(minimumSeparation,separation);Check(separation>=5-1e-6,"meaningful local action-position difference>=5units");}
        }
        if(status=="PROVED")
        {
            broadProofs++;int first=I(proof,"firstEdge"),second=I(proof,"secondEdge");double[] a=Numbers(G(proof,"firstMeasuredInterval")),b=Numbers(G(proof,"secondMeasuredInterval")),ar=Numbers(G(proof,"firstPositionsRelativeObstacleX")),br=Numbers(G(proof,"secondPositionsRelativeObstacleX"));Check(first!=second&&a.Length==2&&b.Length==2&&a[1]>a[0]&&b[1]>b[0]&&a[1]-a[0]>=20-1e-6&&b[1]-b[0]>=20-1e-6,"two distinct nontrivial measured action windows");Check(Math.Abs(a.Average()-b.Average())>=t.Length/4-220,"separated track parts, not a common clock shift");Check(ar.Length==3&&br.Length==3&&ar.Distinct().Count()==3&&br.Distinct().Count()==3,"early/middle/late local positions");Check((ar.Max()-ar.Min())/(a[1]-a[0])>=.5-1e-6&&(br.Max()-br.Min())/(b[1]-b[0])>=.5-1e-6,">=half sampled feasible interval covered");
            var firstObstacle=t.Obstacles.Single(o=>o.Id==S(proof,"firstObstacle"));var secondObstacle=t.Obstacles.Single(o=>o.Id==S(proof,"secondObstacle"));Check(firstObstacle.Id!=secondObstacle.Id,"different local obstacle anchors");string[] ids=Strings(G(proof,"planIds"));var points=G(proof,"points").EnumerateArray().ToArray();Check(ids.Length==9&&ids.Distinct().Count()==9&&points.Length==9&&points.Select(p=>S(p,"first")+"/"+S(p,"second")).Distinct().Count()==9,"complete distinct3x3 whole-route Cartesian grid");Check(G(proof,"allNineWholeRoutesValid").GetBoolean(),"producer nine-route validation flag");string signatureId=S(proof,"signatureId");var labels=new[]{"early","middle","late"};
            for(int i=0;i<Math.Min(ids.Length,points.Length);i++)
            {
                Check(byId.ContainsKey(ids[i]),"proof references published plan");if(!byId.ContainsKey(ids[i]))continue;var plan=byId[ids[i]];Check(S(plan,"signatureId")==signatureId,"Cartesian grid stays in one signature");var tape=Numbers(G(plan,"distances"));Check(first>=0&&second>=0&&first<tape.Length&&second<tape.Length,"proof edge indexes valid");var actions=G(G(plan,"lagger"),"actions").EnumerateArray().ToArray();int ai=Array.IndexOf(labels,S(points[i],"first")),bi=Array.IndexOf(labels,S(points[i],"second"));Check(ai>=0&&bi>=0,"early/middle/late labels valid");if(ai<0||bi<0||first>=tape.Length||second>=tape.Length)continue;Check(Near(tape[first]-firstObstacle.X,ar[ai])&&Near(tape[second]-secondObstacle.X,br[bi]),"proof positions anchored to obstacles");Check(Near(N(points[i],"firstActualDistance"),N(actions[first],"Distance"))&&Near(N(points[i],"secondActualDistance"),N(actions[second],"Distance"))&&Near(N(points[i],"firstActualTime"),N(actions[first],"Time"))&&Near(N(points[i],"secondActualTime"),N(actions[second],"Time")),"Cartesian evidence equals actual published action edges");
            }
        }
        else Check(status=="TIMING_VARIANTS_VERIFIED"&&S(proof,"pairProofRef")==S(library,"trackId")+"/"+S(library,"profile")+"/Confident","minor library references same pair proved Confident windows");
        return new{status,minimumPairwiseLocalActionSeparation=minimumSeparation};
    }
    static int Main()
    {
        var watch=Stopwatch.StartNew();try
        {
            string inputHash=HashFile(Input);Check(inputHash==ExpectedHash,"immutable input SHA256");using var doc=JsonDocument.Parse(File.ReadAllText(Input));var root=doc.RootElement;Check(S(root,"status")=="COMPLETE"&&G(root,"sourceStable").GetBoolean()&&G(root,"outcomesFileUnchangedAtSave").GetBoolean()&&G(root,"blockers").GetArrayLength()==0,"complete producer provenance/no blockers");foreach(var source in G(root,"sourceHashes").EnumerateObject())
            {
                bool matches=File.Exists(source.Name)&&HashFile(source.Name)==source.Value.GetString();
                if(!matches)CurrentSourceDifferences.Add(new{path=source.Name,producerHash=source.Value.GetString(),currentHash=File.Exists(source.Name)?HashFile(source.Name):"MISSING"});
                if(source.Name.Contains("/Tracks")||source.Name.EndsWith("/FlightSettings.cs")||source.Name.EndsWith("/MotionProfiles.cs")||source.Name.EndsWith("/ObstacleGeometry.cs")||source.Name.EndsWith("/ContinuousMotion.cs")||source.Name.EndsWith("/FinishTargetGeometry.cs")||source.Name.StartsWith("Tools/",StringComparison.Ordinal))Check(matches,"immutable physical-data/producer source hash "+source.Name);
            }
            string currentData=File.ReadAllText("Assets/Scripts/Core/RaceSimulation.cs");currentData=currentData.Substring(0,currentData.IndexOf("    public sealed class SpeedEffect",StringComparison.Ordinal));Check(HashText(currentData)==DataPrefixSha256,"compiled verbatim data-type/constants prefix hash");Check(HashFile("Verification/bot-diversity-outcomes.json")==S(root,"outcomesSnapshotSha256"),"immutable outcome snapshot hash");var libraries=G(root,"libraries").EnumerateArray().ToArray();Check(libraries.Length==50&&I(root,"libraryCount")==50,"all50 libraries");Check(libraries.Select(l=>S(l,"trackId")+"/"+S(l,"profile")+"/"+S(l,"difficulty")).Distinct().Count()==50,"unique track/profile/level keys");var allPlanIds=new HashSet<string>();
            foreach(var library in libraries)
            {
                context=S(library,"trackId")+"/"+S(library,"profile")+"/"+S(library,"difficulty");Check(RaceTracks.All.Any(t=>t.Id==S(library,"trackId"))&&(S(library,"profile")=="Chosen"||S(library,"profile")=="FastFlip")&&Levels.Contains(S(library,"difficulty")),"supported library key");var track=RaceTracks.Get(S(library,"trackId"));bool master=S(library,"difficulty")=="Master";int level=Array.IndexOf(Levels,S(library,"difficulty"));string[] catalog=Strings(G(library,"catalogIds"));Check(Same(catalog,Catalog(track))&&HashText(string.Join("\n",catalog))==S(library,"catalogHash")&&Unique(catalog),"unchanged fixed catalog IDs/hash");var signatures=G(library,"signatures").EnumerateArray().ToArray();var plans=G(library,"plans").EnumerateArray().ToArray();signatureCount+=signatures.Length;planCount+=plans.Length;Check(signatures.Select(s=>S(s,"id")).Distinct().Count()==signatures.Length,"unique signatures");var byId=plans.ToDictionary(p=>S(p,"id"));
                foreach(var signature in signatures)
                {
                    string expected=master?HashText(Key(Strings(G(signature,"successfulGoalIds")))+"/missed="+Key(Strings(G(signature,"missedGoalIds")))+"/hits="+Key(Strings(G(signature,"hitObstacleIds")))):HashText(SigKey(signature));Check(S(signature,"id")==expected,"signature identity hashes actual goal/hit sets");var members=plans.Where(p=>S(p,"signatureId")==S(signature,"id")).ToArray();Check(Same(Strings(G(signature,"planIds")),members.Select(p=>S(p,"id"))),"signature references exact group membership");Check(Unique(Strings(G(signature,"successfulGoalIds")))&&Unique(Strings(G(signature,"missedGoalIds")))&&Unique(Strings(G(signature,"hitObstacleIds"))),"signature unique sets");
                    foreach(var plan in members){context=S(library,"trackId")+"/"+S(library,"profile")+"/"+S(library,"difficulty")+"/"+S(plan,"id");Check(allPlanIds.Add(S(plan,"id")),"globally unique plan ID");double[] tape=Numbers(G(plan,"distances"));Check(tape.Length>0&&tape.All(x=>Finite(x)&&x>=0&&x<track.Length)&&tape.Zip(tape.Skip(1),(a,b)=>a<=b).All(x=>x),"ordered finite whole plan");Metrics(G(plan,"lagger"),plan,signature,track,false,master,level);Metrics(G(plan,"constant"),plan,signature,track,true,master,level);}
                }
                Check(plans.All(p=>signatures.Any(s=>S(s,"id")==S(p,"signatureId"))),"no orphan plans");context=S(library,"trackId")+"/"+S(library,"profile")+"/"+S(library,"difficulty");var coverage=Coverage(library,track);var timing=Timing(library,track,byId);double weighted=signatures.Average(s=>plans.Where(p=>S(p,"signatureId")==S(s,"id")).Average(p=>N(G(p,"lagger"),"time"))),flat=plans.Average(p=>N(G(p,"lagger"),"time"));Libraries.Add(new{trackId=S(library,"trackId"),profile=S(library,"profile"),difficulty=S(library,"difficulty"),signatures=signatures.Length,plans=plans.Length,signatureUniformMeanTime=weighted,flatTapeMeanTime=flat,coverage,timing});
            }
            foreach(var pair in libraries.GroupBy(l=>S(l,"trackId")+"/"+S(l,"profile")))
            {
                context=pair.Key;var ordered=pair.OrderBy(l=>Array.IndexOf(Levels,S(l,"difficulty"))).ToArray();Check(ordered.Length==5,"five levels per pair");double[] means=ordered.Select(l=>G(l,"signatures").EnumerateArray().Average(s=>G(l,"plans").EnumerateArray().Where(p=>S(p,"signatureId")==S(s,"id")).Average(p=>N(G(p,"lagger"),"time")))).ToArray();bool strict=Enumerable.Range(0,4).All(i=>means[i]>means[i+1]+1e-7);Check(strict,"strict signature-uniform mean level order");Means.Add(new{trackId=S(ordered[0],"trackId"),profile=S(ordered[0],"profile"),levels=Levels,signatureUniformMeans=means,strictlyOrdered=strict,minimumAdjacentGap=Enumerable.Range(0,4).Min(i=>means[i]-means[i+1])});
            }
            context="totals";Check(planCount==1799&&I(root,"planCount")==planCount&&signatureCount==490&&Means.Count==10&&broadProofs==20,"expected1799plans/490signatures/10pairs/20broadproofs");Check(HashFile(Input)==inputHash,"producer artifact unchanged through audit");watch.Stop();var result=new{status=failureCount==0?"PASS":"FAIL",inputPath=Input,inputSha256=inputHash,producerDeclaredSourceStable=G(root,"sourceStable").GetBoolean(),currentSourceDifferences=CurrentSourceDifferences,compiledDataPrefixSha256=DataPrefixSha256,releaseStatus="BLOCKED_AS_REPORTED_BY_ROOT: producer COMPLETE-to-PASS metadata update was denied by auto-review; dependent export/runtime remain blocked pending exact human approval. This read-only mathematical audit does not change that gate.",checks,failureCount,findings=Failures,libraryCount=libraries.Length,planCount,signatureCount,actualMetricsAudited=metricCount,eventTracesAudited=traceCount,broadCartesianProofs=broadProofs,elapsedSeconds=watch.Elapsed.TotalSeconds,meanRule="Uniform probability across signatures, then uniform within that signature: mean_s(mean_t(time[s,t])). Flat tape mean reported only for contrast and never used for acceptance.",limitations="Read-only immutable-metrics/static-geometry audit; no physical simulations rerun and no current RaceSimulation methods/catalog/factory compiled. Only verbatim data definitions/constants and unchanged real track geometry are compiled; current integration-source differences are reported separately. Trace timestamps/distances and sampled connected window boundaries retain producer provenance; this audit verifies consistency, published Cartesian shapes/actual edges, geometry/runway, arithmetic and coverage, not continuous-window feasibility or full-state observer replay. Those are separate runtime/release checks.",pairs=Means,libraries=Libraries};File.WriteAllText("Verification/bot-diversity-calibration.json",JsonSerializer.Serialize(result,Options));var lines=new List<string>{result.status+": "+checks+" checks;1799 plans/490 signatures/50 libraries; "+failureCount+" failures."};lines.AddRange(Means.Select(x=>JsonSerializer.Serialize(x)));lines.AddRange(Failures);lines.Add(result.limitations);File.WriteAllLines("Verification/bot-diversity-calibration.txt",lines);Console.WriteLine(lines[0]);foreach(var p in Means)Console.WriteLine(JsonSerializer.Serialize(p));return failureCount==0?0:1;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}

// Verbatim read-only data schema/constants from RaceSimulation prefix; no simulation or factory methods.
namespace FlipRace.Core
{
    public enum MotionMode { Linear, Gravity, GravityAirFlip, LinearAirFlip, JumpGravityFlip, GravityUnlimited }
    public enum SupportKind { None, Wall, Stone }
    public enum PlayerStatus { Running, Finished, Eliminated }
    public enum PartyCamera { Constant, Lagger }

    public static class RaceConfig
    {
        public const int CharacterCount = 6;
        public const double Speed = 220;
        public const double FlipDuration = .8;
        public const double RecoveryDuration = .5;
        public const double PlayerRadius = 14;
        public const double CorridorHeight = 240;
        public const double CountdownDuration = 3;
        public const double FinishGrace = 3.5;
        public const double BoostMultiplier = 1.5;
        public const double BoostDuration = .6;
        public const double BoostTimeGain = .3;
        public const double SlowMultiplier = .5;
        public const double SlowDuration = .6;
        public const double SlowTimeLoss = .3;
        public const double GravityAcceleration = 662.5;
        public const double LinearVerticalSpeed = 265;
        public const double FinishBoostSpeed = 440;
        public const double FinishBoostDuration = .25;
        public const double FinishBoostDistance = FinishBoostSpeed * FinishBoostDuration;
    }

    public sealed class Obstacle
    {
        public string Id;
        public double X, Width, Height, GapTop, GapBottom;
        // Gate halves retain their vertical bands and share one logical ID.
        // Each horizontal offset is relative to X; zero preserves straight gates.
        public double TopOffsetX, BottomOffsetX;
        public string Kind;
    }

    public sealed class Cue
    {
        public double X;
        public string Text;
        public Cue(double x, string text) { X = x; Text = text; }
    }

    public sealed class Track
    {
        public string Id = "custom", Name = "Тестовая тропа", Focus = "";
        public double Length;
        public Obstacle[] Obstacles;
        public Cue[] Cues;
        public RiskZone[] RiskZones = Array.Empty<RiskZone>();
        public double[] SafeRoute = Array.Empty<double>();
        public double[] RiskRoute = Array.Empty<double>();
        public bool NegativeEnabled;
        public int PositiveRingCount
        {
            get { int count = 0; foreach (var zone in RiskZones) if (!zone.IsNegative && !zone.IsCoin) count++; return count; }
        }

        public Track(double length, Obstacle[] obstacles, Cue[] cues = null)
        {
            Length = length;
            Obstacles = obstacles;
            Cues = cues ?? Array.Empty<Cue>();
        }
    }

    public sealed class RiskZone
    {
        public string Id, ObstacleId;
        public string Kind = "positive", Placement = "before";
        public bool IsNegative { get { return Kind == "negative"; } }
        public bool IsInstant { get { return Kind == "instant" || IsLanding; } }
        public bool IsLanding { get { return Kind == "landing"; } }
        public bool IsFinish { get { return Kind == "finish"; } }
        public bool IsCoin { get { return Kind == "coin"; } }
        public bool AfterObstacle { get { return Placement == "after"; } }
        public bool FreeReward;
        // Ring center and radius use world units, just like obstacle geometry.
        public double X, Y, Radius = 6, ClearX, SafeFlipX, RiskFlipX;
        // Landing uses a narrow center-X interval on the linked stone's actual
        // corridor-facing plane. Y and Radius do not define its contact shape.
        public double LandingWidth;
        // Legacy serialized dimensions retained for old fixtures/data only.
        // Finish rendering/contact use FinishTargetGeometry at X/Y instead.
        public double FinishWidth = 32, FinishHeight = 8;
    }

}
