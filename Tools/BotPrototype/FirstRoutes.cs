using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.BotPrototype;

internal static class FirstRoutes
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    static readonly List<object> Evidence = new List<object>();
    static readonly List<string> Lines = new List<string>();
    static void Say(string s) { Console.WriteLine(s); Lines.Add(s); }
    static string Pattern(ProbeResult r) => string.Join("|", r.MissedIds) + "/" + string.Join("|",r.Game.Players[0].HitObstacleIds.OrderBy(x=>x));
    static bool Novice(ProbeResult r) => r.Finished && r.Score >= 40-1e-7 && r.Score <= 60+1e-7 && r.MinMarginLowerBound > 0;
    static object Result(ProbeResult r) => new {r.GoalIds,r.CompletedIds,r.MissedIds,r.Total,r.Completed,r.Score,r.Time,Status=r.Status.ToString(),r.Collisions,r.Accepted,r.Rejected,r.MinMargin,r.MinMarginLowerBound,r.Distances,r.Actions,
        hits=r.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(), boosts=r.Game.Players[0].BoostsEarned, slows=r.Game.Players[0].SlowsEarned, finishBoosts=r.Game.Players[0].FinishBoostsEarned,recoveries=r.Game.Players[0].CompletedRecoveries,recoverySeconds=r.Game.Players[0].CompletedRecoveries*RaceConfig.RecoveryDuration+r.Game.Players[0].RecoveryElapsed,recoveryRemaining=r.Game.Players[0].Recovering,completedOrdinaryBoosts=r.Game.Players[0].CompletedBoosts,ordinaryBoostSeconds=r.Game.Players[0].CompletedBoosts*RaceConfig.BoostDuration+r.Game.Players[0].BoostElapsed,finishBoostSeconds=r.Game.Players[0].FinishBoostElapsed};
    static IEnumerable<(string name,double[] route)> Candidates(Track t,FlightSettings f)
    {
        var safe=Probe.Safe(t,f);var full=Probe.Master(t,f);
        yield return ("safe",safe);
        // A local splice is only a candidate. Physics decides whether the real
        // arrival state makes the author's full block valid after safe blocks.
        foreach(var z in t.RiskZones.Where(z=>!z.IsNegative&&!z.IsCoin))
        foreach(double radius in new[]{150d,250,400,650})
        {
            double lo=Math.Max(0,z.X-radius),hi=Math.Min(t.Length,z.X+radius);
            yield return ("splice-"+z.Id+"-"+radius,safe.Where(x=>x<lo||x>hi).Concat(full.Where(x=>x>=lo&&x<=hi)).OrderBy(x=>x).ToArray());
        }
        for(int i=0;i<safe.Length;i++)
        {
            yield return ("suppress-"+i,safe.Where((x,k)=>k!=i).ToArray());
            foreach(double delta in new[]{-120d,-60,-30,30,60,120})
                yield return ("retime-"+i+"-"+delta,safe.Select((x,k)=>k==i?Math.Max(0,x+delta):x).OrderBy(x=>x).ToArray());
        }
        foreach(var z in t.RiskZones.Where(z=>!z.IsNegative&&!z.IsCoin))
        foreach(double delta in new[]{-180d,-100,-60,-30,0,30,60,100})
            yield return ("extra-"+z.Id+"-"+delta,safe.Concat(new[]{z.X+delta}).OrderBy(x=>x).ToArray());
    }
    sealed class LibraryPlan
    {
        public string Id, Name;
        public double[] Distances;
        public ProbeResult Constant, Lagger;
    }
    static string RouteKey(double[] a) => string.Join(",",a.Select(x=>x.ToString("R")));
    static string Hash(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));
    static bool Middle(ProbeResult r,int level)
    {
        int[] low={9,14,16,19,22}, high={13,15,17,20,22};
        return r.Finished&&r.Completed>=low[level]&&r.Completed<=high[level]&&r.Collisions==0&&r.Rejected==0&&r.Game.Players[0].SlowsEarned==0&&r.MinMarginLowerBound>0;
    }
    static IEnumerable<(string name,double[] route)> MiddleCandidates(Track t,FlightSettings f)
    {
        var safe=Probe.Safe(t,f);var full=Probe.Master(t,f);
        for(double cut=400;cut<=7400;cut+=100)
        {
            yield return ("prefix-full-"+cut,full.Where(x=>x<cut).Concat(safe.Where(x=>x>=cut)).OrderBy(x=>x).ToArray());
            yield return ("suffix-full-"+cut,safe.Where(x=>x<cut).Concat(full.Where(x=>x>=cut)).OrderBy(x=>x).ToArray());
        }
        var zones=t.RiskZones.Where(z=>!z.IsNegative&&!z.IsCoin&&!z.IsFinish).ToArray();
        foreach(double radius in new[]{250d,400,150,650})
        for(int mask=1;mask<(1<<zones.Length);mask++)
        {
            int bits=System.Numerics.BitOperations.PopCount((uint)mask);
            if(bits<2||bits>8)continue;
            Func<double,bool> selected=x=>zones.Where((z,i)=>(mask&(1<<i))!=0).Any(z=>Math.Abs(x-z.X)<=radius);
            yield return ("mask-"+mask+"-radius-"+radius,safe.Where(x=>!selected(x)).Concat(full.Where(selected)).OrderBy(x=>x).ToArray());
        }
    }
    static object Summary(ProbeResult r) => new { r.Completed,r.Total,r.Score,r.Time,r.Collisions,r.Accepted,r.Rejected,r.MinMarginLowerBound,Status=r.Status.ToString(),boosts=r.Game.Players[0].BoostsEarned,slows=r.Game.Players[0].SlowsEarned,finishBoosts=r.Game.Players[0].FinishBoostsEarned,missed=r.MissedIds,hits=r.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(),recoveries=r.Game.Players[0].CompletedRecoveries,recoverySeconds=r.Game.Players[0].CompletedRecoveries*RaceConfig.RecoveryDuration+r.Game.Players[0].RecoveryElapsed,recoveryRemaining=r.Game.Players[0].Recovering,completedOrdinaryBoosts=r.Game.Players[0].CompletedBoosts,ordinaryBoostSeconds=r.Game.Players[0].CompletedBoosts*RaceConfig.BoostDuration+r.Game.Players[0].BoostElapsed,finishBoostSeconds=r.Game.Players[0].FinishBoostElapsed };
    static string Fingerprint(ProbeResult r) => JsonSerializer.Serialize(new{player=r.Game.Players[0],r.GoalIds,r.CompletedIds,r.MissedIds,r.Actions,r.MinMargin,r.MinMarginLowerBound},Json);
    static int Levels()
    {
        var initialHash=Hash(File.ReadAllText("Verification/bots-first-prototype.json"));
        var sourceHashes=Directory.GetFiles("Assets/Scripts/Core","*.cs").OrderBy(p=>p,StringComparer.Ordinal).ToDictionary(p=>p.Replace((char)92,(char)47),p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));
        using var initial=JsonDocument.Parse(File.ReadAllText("Verification/bots-first-prototype.json"));
        string[] names={"Novice","Amateur","Confident","Strong","Master"};
        var evidence=new List<object>();bool pass=true;int simulationRuns=0,seedCount=0;
        foreach(var t in TracksFull.All)
        foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        {
            string[] fixedCatalog=Probe.Catalog(t);string fixedCatalogHash=Hash(string.Join("\n",fixedCatalog));
            var libraries=names.Select(x=>new List<LibraryPlan>()).ToArray();
            var routeKeys=names.Select(x=>new HashSet<string>()).ToArray();
            var patterns=names.Select(x=>new HashSet<string>()).ToArray();
            int attempts=0;
            Action<int,string,double[]> add=(level,name,route)=>
            {
                string key=RouteKey(route);if(routeKeys[level].Contains(key))return;
                var r=Probe.Run(t,f,route);var l=Probe.Run(t,f,route,PartyCamera.Lagger);simulationRuns+=2;
                bool ok=level==0?Novice(r):Middle(r,level);
                if(!ok||!l.Finished||FingerprintPersonal(r)!=FingerprintPersonal(l))return;
                routeKeys[level].Add(key);patterns[level].Add(Pattern(r));
                libraries[level].Add(new LibraryPlan{Id=t.Id+"/"+f.FirstAction+"/"+names[level]+"/"+libraries[level].Count,Name=name,Distances=route,Constant=r,Lagger=l});
            };
            foreach(var old in initial.RootElement.GetProperty("evidence").EnumerateArray().Where(e=>e.GetProperty("kind").GetString()=="novice"&&e.GetProperty("track").GetString()==t.Id&&e.GetProperty("profile").GetString()==f.FirstAction.ToString()))
                add(0,old.GetProperty("name").GetString(),old.GetProperty("constant").GetProperty("Distances").EnumerateArray().Select(x=>x.GetDouble()).ToArray());
            var master=Probe.Master(t,f);add(4,"trusted-FIX491",master);
            foreach(double delta in new[]{-1d,1,-2,2,-3,3,-4,4,-5,5,-6,6})
            {
                add(4,"first-edge-retime-"+delta,master.Select((x,i)=>i==0?x+delta:x).ToArray());
                if(libraries[4].Count>=8)break;
            }
            var searched=new HashSet<string>();
            foreach(var candidate in MiddleCandidates(t,f))
            {
                if(!searched.Add(RouteKey(candidate.route)))continue;
                attempts++;
                var r=Probe.Run(t,f,candidate.route,PartyCamera.Constant,.05);
                for(int level=1;level<=3;level++)
                {
                    if(libraries[level].Count>=8||!Middle(r,level)||patterns[level].Contains(Pattern(r)))continue;
                    add(level,candidate.name,candidate.route);
                }
                if(libraries.Skip(1).Take(3).All(a=>a.Count>=8))break;
            }
            var means=new double[5];var seedEvidence=new List<object>();
            for(int level=0;level<5;level++)
            {
                var library=libraries[level];pass&=library.Count>=4;
                if(library.Count==0){means[level]=double.NaN;continue;}
                var times=new List<double>();
                for(int seed=0;seed<20;seed++)
                {
                    int selected=seed%library.Count;
                    var plan=library[selected];
                    var a=Probe.Run(t,f,plan.Distances);var b=Probe.Run(t,f,plan.Distances,PartyCamera.Lagger);var same=Probe.Run(t,f,plan.Distances);simulationRuns+=3;seedCount++;
                    bool correct=(level==0?Novice(a):Middle(a,level))&&b.Finished&&Fingerprint(a)==Fingerprint(same)&&FingerprintPersonal(a)==FingerprintPersonal(b);
                    pass&=correct;times.Add(a.Time);
                    seedEvidence.Add(new{level=names[level],seed,planIndex=selected,planId=plan.Id,repeatPlanIndex=(selected+1)%library.Count,sameSeedExact=Fingerprint(a)==Fingerprint(same),bothCamerasPersonalExact=FingerprintPersonal(a)==FingerprintPersonal(b),verified=correct,constant=Summary(a),lagger=Summary(b)});
                }
                means[level]=times.Average();
                Say(t.Id+" "+f.FirstAction+" "+names[level]+" plans="+library.Count+" patterns="+patterns[level].Count+" mean20="+means[level].ToString("F4")+" score="+library.Min(p=>p.Constant.Score).ToString("F2")+".."+library.Max(p=>p.Constant.Score).ToString("F2"));
            }
            bool ordered=Enumerable.Range(0,4).All(i=>means[i]>means[i+1]+1e-7);pass&=ordered;
            string[] catalog=fixedCatalog;string catalogHash=fixedCatalogHash;pass&=fixedCatalog.SequenceEqual(Probe.Catalog(t));
            Say("  attempts="+attempts+" ordered="+ordered+" counts="+string.Join(",",libraries.Select(l=>l.Count)));
            evidence.Add(new{track=t.Id,profile=f.FirstAction.ToString(),catalog,catalogHash,attempts,strictlyOrdered=ordered,means,libraries=names.Select((name,i)=>new{level=name,planCount=libraries[i].Count,distinctMissHitPatterns=patterns[i].Count,plans=libraries[i].Select(p=>new{p.Id,p.Name,p.Distances,constant=Result(p.Constant),lagger=Result(p.Lagger)})}).ToArray(),seeds=seedEvidence});
        }
        pass&=initialHash==Hash(File.ReadAllText("Verification/bots-first-prototype.json"));
        bool sourceStable=sourceHashes.All(p=>File.Exists(p.Key)&&p.Value==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p.Key))));pass&=sourceStable;
        Say((pass?"PASS":"FAIL")+": first6 finite libraries, all5levels, all plans both cameras, predefined20seeds, same-seed exact and ordered mean finish times; simulationRuns="+simulationRuns);
        File.WriteAllText("Verification/bots-first-levels.json",JsonSerializer.Serialize(new{status=pass?"PASS":"FAIL",scope="offline BOT-01/02/03/04 finite plan proof for first3 tracks x2 presets; production integration not included",selection="Plan is fixed before race: seed modulo finite library size; repeat increments plan index modulo size. No human state/input is consulted. Seeds0..19 are predefined and none filtered.",masterVariation="Master variants are distinct legal edge positions; all have identical full accuracy and ideal effect time, with no error patterns. Finite library does not imply unlimited generated routes.",seedCount,simulationRuns,initialPrototypeHash=initialHash,sourceHashes,sourceStable,catalogTiming="Catalog IDs/hash captured before candidate search and calibration; unchanged after all plans/seeds.",evidence},Json));
        File.WriteAllLines("Verification/bots-first-levels.txt",Lines);
        return pass?0:1;
    }
    static string FingerprintPersonal(ProbeResult r) => JsonSerializer.Serialize(new{player=r.Game.Players[0],r.CompletedIds,r.MissedIds,r.Actions},Json);
    static int Main(string[] args)
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
        if(args.Length>0 && args[0]=="--levels")return Levels();
        bool pass=true;
        foreach(var t in TracksFull.All)
        foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        {
            var master=Probe.Run(t,f,Probe.Master(t,f));
            var masterLagger=Probe.Run(t,f,Probe.Master(t,f),PartyCamera.Lagger);
            bool masterOk=master.Finished&&master.Completed==master.Total&&master.Collisions==0&&masterLagger.Finished&&masterLagger.Completed==master.Total;
            Say(t.Id+" "+f.FirstAction+" catalog="+master.Total+" Master="+master.Completed+" time="+master.Time+" margin="+master.MinMarginLowerBound+" accepted="+master.Accepted+" rejected="+master.Rejected);
            Evidence.Add(new{track=t.Id,profile=f.FirstAction.ToString(),kind="master",verified=masterOk,constant=Result(master),lagger=Result(masterLagger)});
            pass&=masterOk;
            var seenRoutes=new HashSet<string>();var seenPatterns=new HashSet<string>();int attempts=0,count=0,collisionCount=0;
            foreach(var c in Candidates(t,f))
            {
                string key=string.Join(",",c.route.Select(x=>x.ToString("R")));
                if(!seenRoutes.Add(key))continue;
                attempts++;var initial=Probe.Run(t,f,c.route,PartyCamera.Constant,.05);
                if(!Novice(initial)||!seenPatterns.Add(Pattern(initial)))continue;
                var r=Probe.Run(t,f,c.route);var l=Probe.Run(t,f,c.route,PartyCamera.Lagger);
                if(!Novice(r)||!l.Finished||l.Completed!=r.Completed||l.Collisions!=r.Collisions)continue;
                count++;if(r.Collisions>0)collisionCount++;
                Say("  Novice "+count+" "+c.name+" "+r.Completed+"/"+r.Total+"="+r.Score.ToString("F2")+" time="+r.Time.ToString("F4")+" hits="+r.Collisions+" margin="+r.MinMarginLowerBound.ToString("F3")+" edges="+r.Accepted+"/"+r.Actions.Length);
                Evidence.Add(new{track=t.Id,profile=f.FirstAction.ToString(),kind="novice",variant=count,name=c.name,constant=Result(r),lagger=Result(l)});
                // Bounded finite prototype: aim for eight different missed/hit
                // patterns, with a real collision/recovery case if feasible.
                if(count>=8&&collisionCount>=2)break;
            }
            Say("  searched="+attempts+" distinctNovicePatterns="+count+" collisionVariants="+collisionCount);
            pass&=count>=3;
            Evidence.Add(new{track=t.Id,profile=f.FirstAction.ToString(),kind="search-summary",attempts,variants=count,collisionVariants=collisionCount});
        }
        Say(pass?"PASS: first3 x2 Master100 and >=3 diverse Novice40-60 full-finish plans, both cameras.":"FAIL: bounded search did not prove all first3 pairs.");
        File.WriteAllText("Verification/bots-first-prototype.json",JsonSerializer.Serialize(new{status=pass?"PASS":"FAIL",scope="BOT-01/02/03 risk-first offline prototype; no production edits",catalog="one clean objective per logical obstacle ID (both gate parts share ID), every ordinary positive zone ID, one complete finish boost; same22 objectives across all levels",step=1d/120,searchStep=.05,source="FIX491 TakeRoute/Aim and current authored safe/full routes",evidence=Evidence},Json));
        File.WriteAllLines("Verification/bots-first-prototype.txt",Lines);
        return pass?0:1;
    }
}