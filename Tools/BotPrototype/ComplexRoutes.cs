using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using FlipRace.Core;
using FlipRace.BotPrototype;
static class ComplexRoutes
{
    sealed class Candidate { public string Name; public double[] Route; public ProbeResult Result; public int[] Indices; public double Shift; }
    static int runs; static readonly List<object> blockers=new List<object>();
    static ProbeResult Run(Track t,FlightSettings f,double[] d,PartyCamera c=PartyCamera.Constant,double step=1d/60) { runs++; return Probe.Run(t,f,d,c,step); }
    static bool Novice(ProbeResult r) => r.Finished && r.Score>=40-1e-8 && r.Score<=60+1e-8;
    static string Pattern(ProbeResult r)=>string.Join("|",r.MissedIds);
    static object Detail(ProbeResult r)=>new { r.Total,r.Completed,r.Score,r.Time,Status=r.Status.ToString(),r.Collisions,r.Accepted,r.Rejected,r.MinMargin,r.MinMarginLowerBound,
        p=r.Game.Players[0].Distance,boosts=r.Game.Players[0].BoostsEarned,slows=r.Game.Players[0].SlowsEarned,finishBoosts=r.Game.Players[0].FinishBoostsEarned,
        hits=r.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(),collected=r.Game.Players[0].CollectedRiskIds.OrderBy(x=>x).ToArray(),r.CompletedIds,r.MissedIds,r.Actions };
    static double[] Shift(double[] basis,int[] ids,double amount) { var a=(double[])basis.Clone();foreach(int i in ids)a[i]+=amount;return a.OrderBy(x=>x).ToArray(); }
    static List<Candidate> Find(Track t,FlightSettings f)
    {
        var basis=Probe.Safe(t,f);var safe=Run(t,f,basis);var found=new List<Candidate>();var patterns=new HashSet<string>();
        Action<Candidate> add=c=>{if(Novice(c.Result)&&patterns.Add(Pattern(c.Result))) { found.Add(c); Console.WriteLine(t.Id+" "+f.FirstAction+" novice "+c.Name+" "+c.Result.Completed+"/"+c.Result.Total+" hits="+c.Result.Collisions+" time="+c.Result.Time); }};
        add(new Candidate{Name="safe",Route=basis,Result=safe});
        var singles=new List<Candidate>();
        foreach(var obstacle in t.Obstacles)
        {
            int[] ids=Enumerable.Range(0,basis.Length).Where(i=>basis[i]>=obstacle.X-100 && basis[i]<=obstacle.X+100).ToArray();
            if(ids.Length==0)continue;
            foreach(double shift in new[]{-48d,-24d,-12d,12d,24d,48d,72d})
            {
                var d=Shift(basis,ids,shift);if(d[0]<0)continue;var r=Run(t,f,d);
                var c=new Candidate{Name=obstacle.Id+"/shift"+shift,Route=d,Result=r,Indices=ids,Shift=shift};
                if(r.Finished&&r.Collisions==1&&r.Game.Players[0].BoostsEarned==0&&r.Game.Players[0].SlowsEarned==0 && !singles.Any(z=>Pattern(z.Result)==Pattern(r)))singles.Add(c);
                add(c);
            }
            if(found.Count>=4)break;
        }
        if(found.Count<3)
        {
            for(int i=0;i<singles.Count&&found.Count<4;i++)for(int j=i+1;j<singles.Count&&found.Count<4;j++)
            {
                var a=singles[i];var b=singles[j];if(a.Indices.Intersect(b.Indices).Any())continue;
                var d=(double[])basis.Clone();foreach(int k in a.Indices)d[k]+=a.Shift;foreach(int k in b.Indices)d[k]+=b.Shift;Array.Sort(d);
                var r=Run(t,f,d);add(new Candidate{Name=a.Name+" + "+b.Name,Route=d,Result=r});
            }
        }
        if(t.Id==TracksRhythm.CascadeId && found.Count==0)
        {
            int count=0;var distinct=new HashSet<string>();
            for(int i=0;i<singles.Count&&count<3;i++)for(int j=i+1;j<singles.Count&&count<3;j++)for(int k=j+1;k<singles.Count&&count<3;k++)
            {
                var chosen=new[]{singles[i],singles[j],singles[k]};if(chosen.SelectMany(x=>x.Indices).Distinct().Count()!=chosen.Sum(x=>x.Indices.Length))continue;
                var d=(double[])basis.Clone();foreach(var one in chosen)foreach(int m in one.Indices)d[m]+=one.Shift;Array.Sort(d);
                var lagger=Run(t,f,d,PartyCamera.Lagger);
                if(!Novice(lagger)||lagger.Collisions!=3||lagger.Game.Players[0].BoostsEarned!=0||lagger.Game.Players[0].SlowsEarned!=0||!distinct.Add(Pattern(lagger)))continue;
                var constant=Run(t,f,d,PartyCamera.Constant,1d/120);lagger=Run(t,f,d,PartyCamera.Lagger,1d/120);
                blockers.Add(new { Track=t.Id,Profile=f.FirstAction.ToString(),Name=string.Join(" + ",chosen.Select(x=>x.Name)),Distances=d,Constant=Detail(constant),Lagger=Detail(lagger),
                    Bound=new { Obstacles=t.Obstacles.Length,PositiveGoals=t.RiskZones.Count(x=>!x.IsNegative&&!x.IsCoin&&!x.IsFinish),Catalog=lagger.Total,MaxNoviceCompleted=(int)Math.Floor(lagger.Total*.6+1e-8),
                        BaselineSeconds=t.Length/220,CameraAllowance=(288.8+14)/220,MinimumNoviceDelay=1.5,MinimumNoviceFinish=t.Length/220+1.5,LatestPossibleConstantFinish=(t.Length+288.8+14)/220,
                        Algebra="Full finish objectives=21-hits+bonuses+finish. <=18 implies hits-bonuses-finish>=3. Delay=.5hits-.3bonuses-.25finish+.3slows>=1.5+.2bonuses+.25finish+.3slows>=1.5. Camera budget302.8/220=1.37636 seconds. No legal Novice Constant finish." } });
                Console.WriteLine("BLOCKER "+t.Id+" "+f.FirstAction+" constant="+constant.Status+" x="+constant.Game.Players[0].Distance+" t="+constant.Time+" lagger="+lagger.Completed+"/"+lagger.Total+" t="+lagger.Time);count++;
            }
        }
        Console.WriteLine("searched "+t.Id+" "+f.FirstAction+" singles="+singles.Count+" variants="+found.Count+" runs="+runs);
        return found.Take(3).ToList();
    }
    static int Main(string[] args)
    {
        if(args.Contains("--levels"))return Levels();
        var evidence=new List<object>();var text=new List<string>();bool pass=true;
        foreach(var t in RaceTracks.All.Where(t=>t.Id==TracksRhythm.CascadeId||t.Id==TracksMixed.Id))foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        {
            var master=Probe.Master(t,f);var mc=Run(t,f,master,PartyCamera.Constant,1d/120);var ml=Run(t,f,master,PartyCamera.Lagger,1d/120);
            bool masterPass=mc.Finished&&ml.Finished&&mc.Completed==mc.Total&&ml.Completed==ml.Total;pass &=masterPass;
            text.Add(t.Id+" "+f.FirstAction+" Master "+mc.Completed+"/"+mc.Total+" "+mc.Status+" t="+mc.Time+" marginLB="+mc.MinMarginLowerBound+" accepted="+mc.Accepted+" rejected="+mc.Rejected);
            Console.WriteLine(text.Last());
            var candidates=Find(t,f);pass &= candidates.Count>=3;var variants=new List<object>();
            foreach(var c in candidates)
            {
                var constant=Run(t,f,c.Route,PartyCamera.Constant,1d/120);var lagger=Run(t,f,c.Route,PartyCamera.Lagger,1d/120);
                bool valid=Novice(constant)&&Novice(lagger)&&constant.CompletedIds.SequenceEqual(lagger.CompletedIds);pass&=valid;
                variants.Add(new { c.Name,Distances=c.Route,Valid=valid,Constant=Detail(constant),Lagger=Detail(lagger) });
                text.Add(t.Id+" "+f.FirstAction+" Novice "+c.Name+" "+constant.Completed+"/"+constant.Total+" "+constant.Score+"% t="+constant.Time+" marginLB="+constant.MinMarginLowerBound+" hits="+constant.Collisions+" rejected="+constant.Rejected+" bothCameras="+valid);
            }
            evidence.Add(new { Track=t.Id,Profile=f.FirstAction.ToString(),Catalog=mc.GoalIds,Master=new { Distances=master,Valid=masterPass,Constant=Detail(mc),Lagger=Detail(ml) },Novices=variants });
        }
        var report=new { Result=pass?"PASS":"BLOCKED",BaseRevision="92b61a6",Scope="BOT-01/02/03 risk prototype; Cascade+Mixed x Chosen/FastFlip only; no production changes",Metric="one clean objective per logical obstacle ID, one per nonnegative noncoin positive zone, one finish goal; shared catalog all levels",SearchRuns=runs,Notes="Finite catalog of 3 Novice plans per pair selected by distinct missed-objective pattern. Distance-triggered real Press/Advance; no injected player state, human input or opponent lag. Minimum conservative bound subtracts220/120 from sampled margin.",Evidence=evidence,Blockers=blockers };
        File.WriteAllText("Verification/bots-complex-prototype.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        if(blockers.Count>0)text.Add("CASCADE BLOCKER: 21 obstacles+9positive+finish=31 goals; Novice<=18. Full objectives21-hits+bonuses+finish<=18 forces hits-bonuses-finish>=3; minimum delay .5hits-.3bonuses-.25finish+.3slows>=1.5sec. Constant budget302.8/220=1.376363636sec. Earliest qualifying finish>=37.5sec exceeds latest camera finish37.376363636sec. Three actual distinct Lagger58.06% plans per profile fail Constant; see JSON Blockers.");text.Add("RESULT "+report.Result+" runs="+runs);File.WriteAllLines("Verification/bots-complex-prototype.txt",text);Console.WriteLine(string.Join(Environment.NewLine,text));return pass?0:1;
    }
    sealed class LevelPlan
    {
        public string Id,Name,Track,Profile,Level;
        public double[] Distances;
        public ProbeResult Constant,Lagger;
    }
    static readonly string[] LevelNames={"Novice","Amateur","Confident","Strong","Master"};
    static int Classify(ProbeResult r)
    {
        if(!r.Finished||r.Rejected>0)return -1;
        if(r.Completed==r.Total)return 4;
        double s=r.Score;
        if(s>=85-1e-8&&s<=95+1e-8)return 3;
        if(s>=70-1e-8&&s<=80+1e-8)return 2;
        if(s>=60-1e-8&&s<=70+1e-8)return 1;
        if(s>=40-1e-8&&s<=60+1e-8)return 0;
        return -1;
    }
    static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static double[] Hybrid(double[] safe,double[] full,double[] bounds,int mask)
    {
        var result=new List<double>();for(int block=0;block<bounds.Length-1;block++)result.AddRange(((mask&(1<<block))!=0?full:safe).Where(x=>x>=bounds[block]&&x<bounds[block+1]));return result.OrderBy(x=>x).ToArray();
    }
    static int Levels()
    {
        bool pass=true;var allPlans=new List<LevelPlan>();var pairs=new List<object>();var lines=new List<string>();
        foreach(var track in RaceTracks.All.Where(x=>x.Id==TracksRhythm.CascadeId||x.Id==TracksMixed.Id))foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        {
            bool cascade=track.Id==TracksRhythm.CascadeId;var safe=Probe.Safe(track,flight);var master=Probe.Master(track,flight);
            var pools=Enumerable.Range(0,5).Select(i=>new List<Candidate>()).ToArray();var identities=Enumerable.Range(0,5).Select(i=>new HashSet<string>()).ToArray();
            int searched=0,eliminated=0,rejected=0,unclassified=0;var uniqueTapes=new HashSet<string>();
            Action<string,double[]> consider=(name,tape)=>
            {
                string identity=string.Join(",",tape.Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));if(!uniqueTapes.Add(identity))return;
                searched++;var r=Run(track,flight,tape,PartyCamera.Lagger);if(!r.Finished)eliminated++;if(r.Rejected>0)rejected++;int level=Classify(r);
                if(level<0){unclassified++;return;}
                string key=level==4?identity:Pattern(r);if(identities[level].Add(key))pools[level].Add(new Candidate{Name=name,Route=tape,Result=r});
            };
            consider("safe",safe);consider("master",master);
            foreach(double amount in new[]{-1d,1d}){var tape=(double[])master.Clone();tape[tape.Length-1]+=amount;consider("master-finish-retime"+amount,tape);}
            double[] bounds=cascade?new[]{0d,830,1560,2300,2970,3760,4580,5830,6500,6900,7921}:new[]{0d,850,1650,2400,2850,4200,4800,5600,6300,7100,7921};
            for(int mask=0;mask<(1<<(bounds.Length-1));mask++)consider("hybrid-mask"+mask,Hybrid(safe,master,bounds,mask));
            var recoverySingles=new List<Candidate>();
            // Actual misses/recovery enrich the low levels beyond bonus-only masks.
            foreach(var obstacle in track.Obstacles)
            {
                int[] indices=Enumerable.Range(0,safe.Length).Where(i=>safe[i]>=obstacle.X-100&&safe[i]<=obstacle.X+100).ToArray();if(indices.Length==0)continue;
                foreach(double amount in new[]{-48d,-24d,24d,48d,72d})
                {
                    var tape=Shift(safe,indices,amount);consider("safe-"+obstacle.Id+"-shift"+amount,tape);
                    if(cascade)
                    {
                        var witness=Run(track,flight,tape,PartyCamera.Lagger);
                        if(witness.Finished&&witness.Collisions==1&&witness.Rejected==0&&witness.Game.Players[0].BoostsEarned==0&&witness.Game.Players[0].SlowsEarned==0&&!recoverySingles.Any(x=>Pattern(x.Result)==Pattern(witness)))recoverySingles.Add(new Candidate{Name=obstacle.Id,Route=tape,Indices=indices,Shift=amount,Result=witness});
                    }
                }
            }
            if(cascade)
            {
                var triples=new List<(Candidate[] C,double First,double Middle,double Last)>();
                for(int i=0;i<recoverySingles.Count;i++)for(int j=i+1;j<recoverySingles.Count;j++)for(int k=j+1;k<recoverySingles.Count;k++)
                {
                    var trio=new[]{recoverySingles[i],recoverySingles[j],recoverySingles[k]};if(trio.SelectMany(x=>x.Indices).Distinct().Count()!=trio.Sum(x=>x.Indices.Length))continue;
                    var positions=trio.Select(x=>track.Obstacles.Single(o=>o.Id==x.Result.Game.Players[0].HitObstacleIds.Single()).X).OrderBy(x=>x).ToArray();triples.Add((trio,positions[0],positions[1],positions[2]));
                }
                foreach(double target in new[]{4500d,5500,6200,6900,7400,7700})
                {
                    int before=pools[0].Count;
                    foreach(var triple in triples.OrderBy(x=>Math.Abs(x.Last-target)+.1*Math.Abs(x.First-.4*target)+.1*Math.Abs(x.Middle-.7*target)).Take(30))
                    {
                        var tape=(double[])safe.Clone();foreach(var c in triple.C)foreach(int i in c.Indices)tape[i]+=c.Shift;Array.Sort(tape);
                        consider("cascade-recovery-target"+target+"-"+string.Join("+",triple.C.Select(x=>x.Name)),tape);if(pools[0].Count>before)break;
                    }
                }
            }
            var pairLevels=new List<object>();var lastLevel=-1;double lastMean=double.PositiveInfinity;
            for(int level=0;level<5;level++)
            {

                int desired=level==4?2:6;var selected=pools[level].OrderBy(c=>cascade&&level==0?(c.Name.StartsWith("cascade-recovery-target",StringComparison.Ordinal)?0:1):c.Result.Collisions).ThenBy(c=>Math.Abs(c.Result.Score-new[]{50d,65,75,90,100}[level])).ThenBy(c=>c.Name,StringComparer.Ordinal).Take(desired).ToList();
                if(level<4 && !(cascade&&level==0) && selected.All(c=>c.Result.Collisions==0))
                {
                    var recovery=pools[level].Where(c=>c.Result.Collisions>0&&!selected.Contains(c)).OrderBy(c=>Math.Abs(c.Result.Score-new[]{50d,65,75,90,100}[level])).ThenBy(c=>c.Result.Collisions).FirstOrDefault();
                    if(recovery!=null&&selected.Count>0)selected[selected.Count-1]=recovery;
                }
                bool valid=selected.Count>=(level==4?2:4);var validated=new List<LevelPlan>();
                for(int i=0;i<selected.Count;i++)
                {
                    var candidate=selected[i];var constant=Run(track,flight,candidate.Route,PartyCamera.Constant,1d/120);var lagger=Run(track,flight,candidate.Route,PartyCamera.Lagger,1d/120);
                    bool thisValid=Classify(lagger)==level&&(level!=4||(Classify(constant)==4&&constant.Finished));valid&=thisValid;
                    var plan=new LevelPlan{Id=track.Id+"/"+flight.FirstAction+"/"+LevelNames[level]+"/"+i,Name=candidate.Name,Track=track.Id,Profile=flight.FirstAction.ToString(),Level=LevelNames[level],Distances=candidate.Route,Constant=constant,Lagger=lagger};validated.Add(plan);allPlans.Add(plan);
                }
                var seeds=new List<object>();double totalTime=0,totalScore=0;var use=new Dictionary<string,int>();
                for(int seed=0;seed<20;seed++)
                {
                    if(validated.Count==0){valid=false;break;}var plan=validated[seed%validated.Count];var repeat=validated[(seed+1)%validated.Count];
                    // Every predefined seed is evaluated without retry/filtering; selection is finite modulo.
                    var constant=Run(track,flight,plan.Distances,PartyCamera.Constant,1d/120);var lagger=Run(track,flight,plan.Distances,PartyCamera.Lagger,1d/120);
                    bool seedValid=Classify(lagger)==level&&(level!=4||Classify(constant)==4)&&lagger.CompletedIds.SequenceEqual(plan.Lagger.CompletedIds)&&Math.Abs(lagger.Time-plan.Lagger.Time)<1e-8&&lagger.Actions.Select(x=>x.Time).SequenceEqual(plan.Lagger.Actions.Select(x=>x.Time))&&constant.CompletedIds.SequenceEqual(plan.Constant.CompletedIds)&&Math.Abs(constant.Time-plan.Constant.Time)<1e-8;valid&=seedValid;
                    totalTime+=lagger.Time;totalScore+=lagger.Score;if(!use.ContainsKey(plan.Id))use[plan.Id]=0;use[plan.Id]++;
                    seeds.Add(new{Seed=seed,PlanId=plan.Id,RepeatPlanId=repeat.Id,Valid=seedValid,lagger.Completed,lagger.Total,lagger.Score,lagger.Time,lagger.Collisions,lagger.Rejected,LaggerMinMarginLowerBound=lagger.MinMarginLowerBound,Constant=new{Status=constant.Status.ToString(),Distance=constant.Game.Players[0].Distance,constant.Time,constant.Completed,constant.Total,constant.Score,constant.Collisions,constant.Accepted,constant.Rejected,constant.MinMarginLowerBound,constant.CompletedIds,constant.MissedIds}});
                }
                double mean=totalTime/20,score=totalScore/20;bool ordered=lastLevel<0||mean<lastMean-1e-8;valid&=ordered;pass&=valid;
                pairLevels.Add(new {Level=LevelNames[level],Status=valid?"PASS":"FAIL",AvailableDistinctPatterns=pools[level].Count,LibrarySize=validated.Count,CalibrationCamera="Lagger",MeanTime20Seeds=mean,MeanScore20Seeds=score,StrictlyFasterThanPreviousAvailableLevel=ordered,SeedDistribution=use,Seeds=seeds,
                    Plans=validated.Select(p=>new {p.Id,p.Name,p.Distances,RouteHash=Hash(JsonSerializer.Serialize(p.Distances)),Constant=Detail(p.Constant),Lagger=Detail(p.Lagger)}).ToArray() });
                lastLevel=level;lastMean=mean;lines.Add(track.Id+" "+flight.FirstAction+" "+LevelNames[level]+" "+(valid?"PASS":"FAIL")+" plans="+validated.Count+" candidates="+pools[level].Count+" meanTime="+mean+" meanScore="+score+" minMarginLB="+(validated.Count>0?validated.Min(p=>p.Constant.MinMarginLowerBound):double.NaN));Console.WriteLine(lines.Last());
            }
            string[] catalog=Probe.Catalog(track);pairs.Add(new{Track=track.Id,Profile=flight.FirstAction.ToString(),Catalog=catalog,CatalogHash=Hash(string.Join("\n",catalog)),Search=new{Attempts=searched,Eliminated=eliminated,RejectedInputRuns=rejected,Unclassified=unclassified,HybridSegments=bounds,MaskCount=1<<(bounds.Length-1)},Levels=pairLevels});
        }
        var report=new{Result=pass?"PASS":"FAIL",BaseRevision="92b61a6",SearchRuns=runs,Selection="Fixed seed0..19 maps seed modulo finite library count; repeat uses next index modulo count. No failed seed filtering. Every selected plan individually verified under Constant+Lagger at1/120. Full-route accuracy/time calibrated only atLagger; Constant records actual finish/elimination/distance/time/objectives without survivor filtering. Master finishes100%both. Same seed action times reproduced exactly. Catalog unchanged across levels.",Scope="Mixed+Cascade5levels x2profiles under clarified2026-10-28 contract. Old BLOCKED risk prototype outputs preserved as historical evidence of superseded Constant finish guarantee.",Evidence=pairs};
        File.WriteAllText("Verification/bots-complex-levels.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));lines.Add("RESULT "+report.Result+" runs="+runs+" finitePlans="+allPlans.Count);File.WriteAllLines("Verification/bots-complex-levels.txt",lines);Console.WriteLine(lines.Last());return pass?0:1;
    }
}
