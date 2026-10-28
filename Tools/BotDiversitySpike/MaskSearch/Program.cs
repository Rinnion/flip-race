using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;
using FlipRace.BotPrototype;
static class Program
{
    const int Limit=6000;
    static readonly string[] Names={"Novice","Amateur","Confident","Strong","Master"};
    static readonly List<object> Evidence=new List<object>();static readonly List<string> Lines=new List<string>();static int simulations;
    sealed class Candidate { public string Name;public double[] Route;public ProbeResult R;public int[] Indices;public double Shift; }
    sealed class Event {public string Kind,Id;public double Time,Distance;}
    static JsonElement Get(JsonElement e,string name)=>e.EnumerateObject().First(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase)).Value;
    static string Key(IEnumerable<string> a)=>string.Join("|",a.OrderBy(x=>x,StringComparer.Ordinal));
    static string Positive(ProbeResult r)=>Key(r.Game.Players[0].SuccessfulRiskIds.Where(id=>r.Game.Track.RiskZones.Any(z=>z.Id==id&&!z.IsFinish&&!z.IsNegative&&!z.IsCoin)));
    static string Mask(ProbeResult r)=>Positive(r)+"/hits="+Key(r.Game.Players[0].HitObstacleIds)+"/finish="+r.Game.Players[0].FinishBoostsEarned;
    static string RouteKey(double[] route)=>string.Join(",",route.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)));
    static int Level(ProbeResult r){if(!r.Finished||r.Rejected!=0)return -1;if(r.Completed==r.Total)return 4;double s=r.Score;if(s>=85-1e-8&&s<=95+1e-8)return 3;if(s>=70-1e-8&&s<=80+1e-8)return 2;if(s>=60-1e-8&&s<=70+1e-8)return 1;if(s>=40-1e-8&&s<=60+1e-8)return 0;return -1;}
    static ProbeResult Run(Track t,FlightSettings f,double[] route,PartyCamera camera=PartyCamera.Lagger,double step=1d/60){simulations++;return Probe.Run(t,f,route,camera,step);}
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static object Summary(ProbeResult r)=>new{Status=r.Status.ToString(),r.Total,r.Completed,r.Score,r.Time,TerminalDistance=r.Game.Players[0].Distance,r.Accepted,r.Rejected,r.Collisions,r.MinMarginLowerBound,CollectedRiskIds=r.Game.Players[0].CollectedRiskIds.OrderBy(x=>x).ToArray(),SuccessfulRiskIds=r.Game.Players[0].SuccessfulRiskIds.OrderBy(x=>x).ToArray(),FailedRiskIds=r.Game.Players[0].FailedRiskIds.OrderBy(x=>x).ToArray(),PositiveIds=r.Game.Players[0].SuccessfulRiskIds.Where(id=>r.Game.Track.RiskZones.Any(z=>z.Id==id&&!z.IsFinish&&!z.IsNegative&&!z.IsCoin)).OrderBy(x=>x).ToArray(),HitIds=r.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(),Slows=r.Game.Players[0].SlowsEarned,FinishBoosts=r.Game.Players[0].FinishBoostsEarned,r.CompletedIds,r.MissedIds,r.Actions};
    static double[] Shift(double[] basis,int[] ids,double amount){var r=(double[])basis.Clone();foreach(int i in ids)r[i]+=amount;Array.Sort(r);return r;}
    static double[] Hybrid(double[] safe,double[] full,double[] bounds,int mask){var r=new List<double>();for(int i=0;i<bounds.Length-1;i++)r.AddRange(((mask&(1<<i))!=0?full:safe).Where(x=>x>=bounds[i]&&x<bounds[i+1]));return r.OrderBy(x=>x).ToArray();}
    static HashSet<string>[] Existing(Track t,FlightSettings f)
    {
        var result=Names.Select(x=>new HashSet<string>()).ToArray();string path=t.Id==TracksRhythm.CascadeId||t.Id==TracksMixed.Id?"Verification/bots-complex-levels.json":"Verification/bots-first-levels.json";
        using var doc=JsonDocument.Parse(File.ReadAllText(path));var pair=Get(doc.RootElement,"evidence").EnumerateArray().Single(x=>Get(x,"track").GetString()==t.Id&&Get(x,"profile").GetString()==f.FirstAction.ToString());
        var levels=pair.EnumerateObject().First(x=>x.Name=="Levels"||x.Name=="libraries").Value;
        foreach(var level in levels.EnumerateArray())
        {
            int index=Array.IndexOf(Names,Get(level,"level").GetString());foreach(var plan in Get(level,"plans").EnumerateArray())
            {
                var actual=Get(plan,"lagger");var positive=Get(actual,"CompletedIds").EnumerateArray().Select(x=>x.GetString()).Where(x=>x.StartsWith("positive:",StringComparison.Ordinal)).Select(x=>x.Substring(9));var hits=Get(actual,"hits").EnumerateArray().Select(x=>x.GetString());int finish=Get(actual,"finishBoosts").GetInt32();result[index].Add(Key(positive)+"/hits="+Key(hits)+"/finish="+finish);
            }
        }
        return result;
    }
    static GameState Begin(Track t,FlightSettings f){var g=RaceSimulation.CreateSolo(t,f,PartyCamera.Lagger,-288.8,1520);RaceSimulation.Press(g,0);RaceSimulation.Advance(g,3);return g;}
    static double AtEvent(Track t,FlightSettings f,Edge[] actions,double time)
    {
        var g=Begin(t,f);foreach(var edge in actions){if(edge.Time>=time-1e-9)break;RaceSimulation.Advance(g,Math.Max(0,edge.Time-g.Elapsed));RaceSimulation.Press(g,0);}RaceSimulation.Advance(g,Math.Max(0,time-g.Elapsed));return g.Players[0].Distance;
    }
    static object Trace(Track t,FlightSettings f,ProbeResult expected)
    {
        var g=Begin(t,f);var p=g.Players[0];var success=new HashSet<string>();var hits=new HashSet<string>();var contact=new HashSet<string>();var events=new List<Event>();var contacts=new List<object>();bool ambiguous=false;int finish=0;
        Action<double> advance=dt=>
        {
            double beforeTime=g.Elapsed,beforeX=p.Distance;RaceSimulation.Advance(g,dt);var newly=p.SuccessfulRiskIds.Where(x=>!success.Contains(x)&&t.RiskZones.Any(z=>z.Id==x&&!z.IsFinish&&!z.IsCoin&&!z.IsNegative)).ToArray();if(newly.Length>1)ambiguous=true;
            foreach(string id in newly){if(p.LastSuccess==null||p.LastSuccess.Id!=id)throw new Exception("Success timestamp unavailable; reduce trace step");events.Add(new Event{Kind="positive-success",Id=id,Time=p.LastSuccess.At});success.Add(id);}
            foreach(string id in p.HitObstacleIds.Where(x=>!hits.Contains(x)).ToArray()){if(p.LastHit==null||p.LastHit.Id!=id)throw new Exception("Hit timestamp unavailable");events.Add(new Event{Kind="hit",Id=id,Time=p.LastHit.At});hits.Add(id);}
            if(p.FinishBoostsEarned>finish){events.Add(new Event{Kind="finish-contact",Id=p.LastFinishBoost.Id,Time=p.LastFinishBoost.At});finish=p.FinishBoostsEarned;}
            foreach(string id in p.CollectedRiskIds.Where(x=>!contact.Contains(x)).ToArray()){contacts.Add(new{Id=id,ObservedAfter=beforeTime,ObservedBy=g.Elapsed,DistanceFrom=beforeX,DistanceTo=p.Distance});contact.Add(id);}
        };
        foreach(var action in expected.Actions){while(g.Elapsed<action.Time-1e-10&&g.Phase=="racing")advance(Math.Min(1d/120,action.Time-g.Elapsed));if(g.Phase!="racing")break;RaceSimulation.Press(g,0);}
        while(g.Phase=="racing")advance(1d/120);
        if(!p.SuccessfulRiskIds.SetEquals(expected.Game.Players[0].SuccessfulRiskIds)||!p.HitObstacleIds.SetEquals(expected.Game.Players[0].HitObstacleIds)||Math.Abs((p.TerminalTime??g.Elapsed)-expected.Time)>1e-7)throw new Exception("Selected timed tape differs from distance trace");
        var ordered=events.OrderBy(x=>x.Time).ToArray();foreach(var e in ordered)e.Distance=AtEvent(t,f,expected.Actions,e.Time);
        return new{Events=ordered,FirstContactBounds=contacts,OrderingFromActualEventTimestamps=true,AmbiguousSameAdvance=ambiguous,Note="Positive-success/hit/finish timestamps come from real LastSuccess/LastHit/LastFinishBoost. Event distances replay ordinary timed inputs exactly to that timestamp. First-contact times unavailable from Core are honest observed intervals, never ID-sorted sequence."};
    }
    static void Pair(Track t,FlightSettings f)
    {
        var watch=Stopwatch.StartNew();int started=simulations,attempts=0,finished=0,rejected=0;var seen=new HashSet<string>();var pool=new List<Candidate>();var novel=Names.Select(x=>new Dictionary<string,Candidate>()).ToArray();var existing=Existing(t,f);var safe=Probe.Safe(t,f);var master=Probe.Master(t,f);var baselineHashes=new[]{"Verification/bots-first-levels.json","Verification/bots-complex-levels.json"}.ToDictionary(x=>x,Hash);
        Func<string,double[],Candidate> consider=(name,route)=>
        {
            if(attempts>=Limit||route.Length==0||route[0]<0||!seen.Add(RouteKey(route)))return null;attempts++;var r=Run(t,f,route);if(r.Finished)finished++;if(r.Rejected>0)rejected++;var c=new Candidate{Name=name,Route=route,R=r};pool.Add(c);int level=Level(r);if(level>=0&&!existing[level].Contains(Mask(r))&&!novel[level].ContainsKey(Mask(r)))novel[level].Add(Mask(r),c);return c;
        };
        consider("safe",safe);consider("master",master);
        double[] bounds=t.Id==TracksRhythm.CascadeId?new[]{0d,830,1560,2300,2970,3760,4580,5830,6500,6900,7921}:t.Id==TracksMixed.Id?new[]{0d,850,1650,2400,2850,4200,4800,5600,6300,7100,7921}:new[]{0d,1100,1630,2300,3000,3500,4250,4750,5500,6100,6700,7921};
        for(int mask=0;mask<(1<<(bounds.Length-1))&&attempts<2200;mask++)consider("hybrid-"+mask,Hybrid(safe,master,bounds,mask));
        if(t.Id==TracksRhythm.CascadeId)
        {
            var bases=pool.Where(c=>c.R.Finished&&c.R.Rejected==0&&c.R.Collisions==0&&c.R.Game.Players[0].SlowsEarned==0&&c.R.Game.Players[0].BoostsEarned>=1&&c.R.Game.Players[0].BoostsEarned<=2&&c.R.Game.Players[0].FinishBoostsEarned==0).GroupBy(c=>Positive(c.R)).Select(g=>g.First()).OrderBy(c=>c.R.Game.Players[0].BoostsEarned).ThenByDescending(c=>c.R.Game.Players[0].SuccessfulRiskIds.Max(id=>t.RiskZones.Single(z=>z.Id==id).X)).Take(15).ToArray();
            foreach(var basis in bases)
            {
                if(attempts>=Limit)break;var singles=new List<Candidate>();string positive=Positive(basis.R);
                foreach(var o in t.Obstacles)
                {
                    int[] ids=Enumerable.Range(0,basis.Route.Length).Where(i=>basis.Route[i]>=o.X-100&&basis.Route[i]<=o.X+100).ToArray();if(ids.Length==0)continue;
                    foreach(double shift in new[]{-48d,24d,72d})
                    {
                        var c=consider("base("+basis.Name+")-"+o.Id+"-shift"+shift,Shift(basis.Route,ids,shift));if(c==null)continue;
                        if(c.R.Finished&&c.R.Rejected==0&&c.R.Collisions==1&&Positive(c.R)==positive&&c.R.Game.Players[0].SlowsEarned==0&&!singles.Any(x=>Key(x.R.Game.Players[0].HitObstacleIds)==Key(c.R.Game.Players[0].HitObstacleIds))){c.Indices=ids;c.Shift=shift;singles.Add(c);}
                    }
                }
                int needed=basis.R.Game.Players[0].BoostsEarned+3;var rng=new Random(1207+bases.ToList().IndexOf(basis));int legalCombos=0;
                for(int sample=0;sample<180&&legalCombos<70&&attempts<Limit;sample++)
                {
                    var selected=singles.OrderBy(x=>rng.Next()).Take(needed).ToArray();if(selected.Length!=needed||selected.SelectMany(x=>x.Indices).Distinct().Count()!=selected.Sum(x=>x.Indices.Length))continue;
                    var route=(double[])basis.Route.Clone();foreach(var c in selected)foreach(int i in c.Indices)route[i]+=c.Shift;Array.Sort(route);consider("recover("+basis.Name+")-"+string.Join("+",selected.Select(c=>c.Name.Split('-').Last())),route);legalCombos++;
                }
            }
        }
        else
        {
            // Retiming and local recovery candidates operate on actual outcome-diverse bases.
            var bases=pool.Where(c=>Level(c.R)>=0).GroupBy(c=>Level(c.R)+"/"+Positive(c.R)).Select(g=>g.First()).GroupBy(c=>Level(c.R)).SelectMany(g=>g.Take(8)).ToArray();
            foreach(var basis in bases)
            {
                foreach(int index in Enumerable.Range(0,Math.Min(3,basis.Route.Length)))foreach(double shift in new[]{-20d,-5d,5d,20d})consider("prefix-retime("+basis.Name+")-"+index+"/"+shift,Shift(basis.Route,new[]{index},shift));
                foreach(var o in t.Obstacles.Where((x,i)=>i%2==0))
                {
                    int[] ids=Enumerable.Range(0,basis.Route.Length).Where(i=>basis.Route[i]>=o.X-130&&basis.Route[i]<=o.X+120).ToArray();if(ids.Length==0)continue;foreach(double shift in new[]{-48d,48d})consider("recovery("+basis.Name+")-"+o.Id+"/"+shift,Shift(basis.Route,ids,shift));
                }
                if(attempts>=Limit)break;
            }
        }
        var levels=new List<object>();int verifiedPlans=0;
        for(int level=0;level<5;level++)
        {
            var values=novel[level].Values.ToArray();var selected=new List<Candidate>();
            foreach(var candidate in values.OrderByDescending(c=>c.R.Game.Players[0].SuccessfulRiskIds.Any(id=>t.RiskZones.Any(z=>z.Id==id&&z.X>t.Length*.6))).ThenByDescending(c=>c.R.Collisions>0&&c.R.Game.Players[0].BoostsEarned>0).ThenBy(c=>Math.Abs(c.R.Score-new[]{50d,65,75,90,100}[level])))if(!selected.Any(c=>Positive(c.R)==Positive(candidate.R))){selected.Add(candidate);if(selected.Count==(level==2?16:8))break;}
            foreach(var candidate in values)if(selected.Count<(level==2?16:8)&&!selected.Contains(candidate))selected.Add(candidate);
            if(level==2)
            {
                // Ensure the spike demonstrates both positive-set and hit-set variation,
                // rather than selecting many signatures sharing one collision location.
                foreach(var hitGroup in values.GroupBy(c=>Key(c.R.Game.Players[0].HitObstacleIds)).OrderByDescending(g=>g.Key.Length))
                {
                    if(selected.Select(c=>Key(c.R.Game.Players[0].HitObstacleIds)).Distinct().Count()>=3)break;
                    if(selected.Any(c=>Key(c.R.Game.Players[0].HitObstacleIds)==hitGroup.Key))continue;
                    var extra=hitGroup.First();if(selected.Count>=16)selected.RemoveAt(selected.Count-1);selected.Add(extra);
                }
            }
            var plans=new List<object>();int fineFailures=0;
            foreach(var candidate in selected)
            {
                var lagger=Run(t,f,candidate.Route,PartyCamera.Lagger,1d/120);var constant=Run(t,f,candidate.Route,PartyCamera.Constant,1d/120);bool valid=Level(lagger)==level&&Mask(lagger)==Mask(candidate.R);if(!valid){fineFailures++;continue;}verifiedPlans++;
                plans.Add(new{Name=candidate.Name,Distances=candidate.Route,Valid=true,NovelActualMask=!existing[level].Contains(Mask(lagger)),ActualMask=Mask(lagger),ActualSignature=Key(lagger.CompletedIds)+"/missed="+Key(lagger.MissedIds)+"/hits="+Key(lagger.Game.Players[0].HitObstacleIds),Lagger=Summary(lagger),Constant=Summary(constant),ActualEventTrace=Trace(t,f,lagger)});
            }
            levels.Add(new{Level=Names[level],NewActualMaskCount=values.Length,NewPositiveSetCount=values.Select(c=>Positive(c.R)).Distinct().Count(),NewHitSetCount=values.Select(c=>Key(c.R.Game.Players[0].HitObstacleIds)).Distinct().Count(),SelectedPositiveSetCount=selected.Select(c=>Positive(c.R)).Distinct().Count(),SelectedHitSetCount=selected.Select(c=>Key(c.R.Game.Players[0].HitObstacleIds)).Distinct().Count(),PositiveAndHitMaskCount=values.Count(c=>c.R.Game.Players[0].BoostsEarned>0&&c.R.Collisions>0),LatePositiveMaskCount=values.Count(c=>c.R.Game.Players[0].SuccessfulRiskIds.Any(id=>t.RiskZones.Any(z=>z.Id==id&&z.X>t.Length*.6))),VerifiedSelected=plans.Count,FineFailures=fineFailures,Plans=plans});
            Lines.Add(t.Id+" "+f.FirstAction+" "+Names[level]+":newMasks="+values.Length+" positiveSets="+values.Select(c=>Positive(c.R)).Distinct().Count()+" hitSets="+values.Select(c=>Key(c.R.Game.Players[0].HitObstacleIds)).Distinct().Count()+" boost+hit="+values.Count(c=>c.R.Game.Players[0].BoostsEarned>0&&c.R.Collisions>0)+" selectedVerified="+plans.Count);Console.WriteLine(Lines.Last());
        }
        bool stable=baselineHashes.All(x=>Hash(x.Key)==x.Value);if(!stable)throw new Exception("Prior calibration report modified during spike");watch.Stop();Evidence.Add(new{Track=t.Id,Profile=f.FirstAction.ToString(),Catalog=Probe.Catalog(t),Attempts=attempts,CandidateLimit=Limit,FinishedCandidates=finished,RejectedInputCandidates=rejected,IndependentSimulationRuns=simulations-started,ElapsedSeconds=watch.Elapsed.TotalSeconds,VerifiedSelectedPlans=verifiedPlans,PriorReportsUnchanged=stable,PriorReportHashes=baselineHashes,HybridBounds=bounds,Levels=levels});Lines.Add("PAIR "+t.Id+"/"+f.FirstAction+" attempts="+attempts+" simulations="+(simulations-started)+" seconds="+watch.Elapsed.TotalSeconds.ToString("F2",CultureInfo.InvariantCulture));Console.WriteLine(Lines.Last());Save();
    }
    static void Save(){File.WriteAllText("Verification/bot-diversity-mask-spike.json",JsonSerializer.Serialize(new{Status="FEASIBILITY_SPIKE",BaseUnity="9a67f29",Scope="New actual positive/hit masks for initialrisk4pairs Mixed+Cascade bothprofiles only. No final diversity acceptance/all40libraries claim; no production changes. Coarse1/60; selected1/120 both cameras. Lagger calibration; Constant honest outcomes. Actual event order from runtime timestamps, not sortedIDs.",CompletedPairs=Evidence.Count,IndependentSimulationRuns=simulations,Evidence},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));File.WriteAllLines("Verification/bot-diversity-mask-spike.txt",Lines);}
    static int Main(string[] args){try{CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;if(args.Contains("--enrich")){Enrich();return 0;}foreach(var pair in new[]{(TracksRhythm.Get(TracksRhythm.CascadeId),FlightPresets.Chosen),(TracksRhythm.Get(TracksRhythm.CascadeId),FlightPresets.FastFlip),(TracksMixed.Get(),FlightPresets.Chosen),(TracksMixed.Get(),FlightPresets.FastFlip)})Pair(pair.Item1,pair.Item2);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    static void Enrich()
    {
        var watch=Stopwatch.StartNew();var sources=Directory.GetFiles("Assets/Scripts/Core","*.cs").OrderBy(x=>x,StringComparer.Ordinal).ToDictionary(x=>x.Replace((char)92,(char)47),Hash);var node=JsonNode.Parse(File.ReadAllText("Verification/bot-diversity-mask-spike.json"));int plans=0;var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        foreach(var pairNode in node["Evidence"].AsArray())
        {
            var track=RaceTracks.Get(pairNode["Track"].GetValue<string>());var flight=pairNode["Profile"].GetValue<string>()=="Jump"?FlightPresets.Chosen:FlightPresets.FastFlip;
            foreach(var levelNode in pairNode["Levels"].AsArray())
            {
                var verified=new List<ProbeResult>();foreach(var planNode in levelNode["Plans"].AsArray())
                {
                    double[] route=planNode["Distances"].AsArray().Select(x=>x.GetValue<double>()).ToArray();var lagger=Run(track,flight,route,PartyCamera.Lagger,1d/120);var constant=Run(track,flight,route,PartyCamera.Constant,1d/120);
                    if(Mask(lagger)!=planNode["ActualMask"].GetValue<string>()||Names[Level(lagger)]!=levelNode["Level"].GetValue<string>())throw new Exception("Fine-only enrichment mismatch");
                    planNode["Lagger"]=JsonSerializer.SerializeToNode(Summary(lagger),options);planNode["Constant"]=JsonSerializer.SerializeToNode(Summary(constant),options);verified.Add(lagger);plans++;
                }
                var goals=track.RiskZones.Where(z=>!z.IsNegative&&!z.IsCoin&&!z.IsFinish).OrderBy(z=>z.X).ToArray();
                levelNode["SelectedThirdCoverage"]=JsonSerializer.SerializeToNode(Enumerable.Range(0,3).Select(third=>new{Third=third+1,Goals=goals.Where(z=>(int)Math.Min(2,z.X/track.Length*3)==third).Select(z=>new{z.Id,z.X,CollectedPlans=verified.Count(r=>r.Game.Players[0].SuccessfulRiskIds.Contains(z.Id)),MissedPlans=verified.Count(r=>!r.Game.Players[0].SuccessfulRiskIds.Contains(z.Id))}).ToArray()}).ToArray(),options);
                levelNode["SelectedNonconsecutivePositivePlans"]=verified.Count(r=>{int[] indices=Enumerable.Range(0,goals.Length).Where(i=>r.Game.Players[0].SuccessfulRiskIds.Contains(goals[i].Id)).ToArray();return indices.Length>=2&&indices.Zip(indices.Skip(1),(a,b)=>b-a).Any(gap=>gap>1);});
                if(goals.Length>0)
                {
                    levelNode["SelectedEarliestMissLateSuccessPlans"]=verified.Count(r=>!r.Game.Players[0].SuccessfulRiskIds.Contains(goals[0].Id)&&goals.Where(z=>z.X>=track.Length*2/3).Any(z=>r.Game.Players[0].SuccessfulRiskIds.Contains(z.Id)));
                    levelNode["SelectedEarlySuccessLateMissPlans"]=verified.Count(r=>goals.Where(z=>z.X<track.Length/3).Any(z=>r.Game.Players[0].SuccessfulRiskIds.Contains(z.Id))&&goals.Where(z=>z.X>=track.Length*2/3).Any(z=>!r.Game.Players[0].SuccessfulRiskIds.Contains(z.Id)));
                }
                levelNode["SelectedConstantPositiveSetCount"]=verified.Count==0?0:levelNode["Plans"].AsArray().Select(p=>string.Join("|",p["Constant"]["PositiveIds"].AsArray().Select(x=>x.GetValue<string>()))).Distinct().Count();
            }
        }
        bool stable=sources.All(x=>Hash(x.Key)==x.Value);if(!stable)throw new Exception("Core changed during selected fine revalidation");node["SelectedFineRevalidation"]=JsonSerializer.SerializeToNode(new{Plans=plans,IndependentSimulationRuns=simulations,ElapsedSeconds=watch.Elapsed.TotalSeconds,CoreSourceHashes=sources,CoreSourceStable=true,PriorReportHashes=new[]{"Verification/bots-first-levels.json","Verification/bots-complex-levels.json"}.ToDictionary(x=>x,Hash)},options);File.WriteAllText("Verification/bot-diversity-mask-spike.json",node.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));File.AppendAllText("Verification/bot-diversity-mask-spike.txt",Environment.NewLine+"SELECTED_FINE_REVALIDATION plans="+plans+" simulations="+simulations+" CoreSourceStable="+stable+" seconds="+watch.Elapsed.TotalSeconds.ToString("F2",CultureInfo.InvariantCulture)+Environment.NewLine);Console.WriteLine("Selected fine revalidation PASS plans="+plans+" simulations="+simulations+" coreStable="+stable);
    }
}
