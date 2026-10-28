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
static class OutcomeGenerator
{
    const int Limit=6000;
    static readonly string[] Names={"Novice","Amateur","Confident","Strong","Master"};
    static readonly List<object> Evidence=new List<object>();static readonly List<string> Lines=new List<string>();static int simulations,timedReplayGames;
    sealed class Candidate { public string Name;public double[] Route;public ProbeResult R,Constant;public int[] Indices;public double Shift; }
    sealed class Event {public string Kind,Id;public double Time,Distance;}
    static JsonElement Get(JsonElement e,string name)=>e.EnumerateObject().First(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase)).Value;
    static string Key(IEnumerable<string> a)=>string.Join("|",a.OrderBy(x=>x,StringComparer.Ordinal));
    static string Positive(ProbeResult r)=>Key(r.Game.Players[0].SuccessfulRiskIds.Where(id=>r.Game.Track.RiskZones.Any(z=>z.Id==id&&!z.IsFinish&&!z.IsNegative&&!z.IsCoin)));
    static string Mask(ProbeResult r)=>Positive(r)+"/hits="+Key(r.Game.Players[0].HitObstacleIds)+"/finish="+r.Game.Players[0].FinishBoostsEarned;
    static string RouteKey(double[] route)=>string.Join(",",route.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)));
    static int Level(ProbeResult r){if(!r.Finished||r.Rejected!=0)return -1;if(r.Completed==r.Total)return 4;double s=r.Score;if(s>=85-1e-8&&s<=95+1e-8)return 3;if(s>=70-1e-8&&s<=80+1e-8)return 2;if(s>=60-1e-8&&s<=70+1e-8)return 1;if(s>=40-1e-8&&s<=60+1e-8)return 0;return -1;}
    static ProbeResult Run(Track t,FlightSettings f,double[] route,PartyCamera camera=PartyCamera.Lagger,double step=1d/60){simulations++;return Probe.Run(t,f,route,camera,step);}
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static object Summary(ProbeResult r,bool trace=false)=>new{status=r.Status.ToString(),total=r.Total,completed=r.Completed,score=r.Score,time=r.Time,terminalDistance=r.Game.Players[0].Distance,accepted=r.Accepted,rejected=r.Rejected,collisions=r.Collisions,recoveries=r.Game.Players[0].CompletedRecoveries,recoverySeconds=r.Game.Players[0].CompletedRecoveries*RaceConfig.RecoveryDuration+r.Game.Players[0].RecoveryElapsed,minMargin=r.MinMargin,minMarginLowerBound=r.MinMarginLowerBound,collectedRiskIds=r.Game.Players[0].CollectedRiskIds.OrderBy(x=>x).ToArray(),successfulRiskIds=r.Game.Players[0].SuccessfulRiskIds.OrderBy(x=>x).ToArray(),failedRiskIds=r.Game.Players[0].FailedRiskIds.OrderBy(x=>x).ToArray(),positiveIds=r.Game.Players[0].SuccessfulRiskIds.Where(id=>r.Game.Track.RiskZones.Any(z=>z.Id==id&&!z.IsFinish&&!z.IsNegative&&!z.IsCoin)).OrderBy(x=>x).ToArray(),hitObstacleIds=r.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(),slows=r.Game.Players[0].SlowsEarned,finishBoosts=r.Game.Players[0].FinishBoostsEarned,successfulGoalIds=r.CompletedIds,missedGoalIds=r.MissedIds,actions=r.Actions.Select(a=>new{distance=a.Distance,time=a.Time,accepted=a.Accepted}).ToArray(),eventChronology=trace?Trace(r.Game.Track,r.Game.Flight,r):null};
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
    static GameState Begin(Track t,FlightSettings f,PartyCamera camera=PartyCamera.Lagger){timedReplayGames++;var g=RaceSimulation.CreateSolo(t,f,camera,-288.8,1520);RaceSimulation.Press(g,0);RaceSimulation.Advance(g,3);return g;}
    static double AtEvent(Track t,FlightSettings f,Edge[] actions,double time,PartyCamera camera)
    {
        var g=Begin(t,f,camera);foreach(var edge in actions){if(edge.Time>=time-1e-9)break;RaceSimulation.Advance(g,Math.Max(0,edge.Time-g.Elapsed));RaceSimulation.Press(g,0);}RaceSimulation.Advance(g,Math.Max(0,time-g.Elapsed));return g.Players[0].Distance;
    }
    static object Trace(Track t,FlightSettings f,ProbeResult expected)
    {
        var g=Begin(t,f,expected.Game.PartyCamera);var p=g.Players[0];var success=new HashSet<string>();var hits=new HashSet<string>();var contact=new HashSet<string>();var events=new List<Event>();var contacts=new List<object>();bool ambiguous=false;int finish=0;
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
        var ordered=events.OrderBy(x=>x.Time).ToArray();foreach(var e in ordered)e.Distance=AtEvent(t,f,expected.Actions,e.Time,expected.Game.PartyCamera);
        return new{Events=ordered,FirstContactBounds=contacts,OrderingFromActualEventTimestamps=true,AmbiguousSameAdvance=ambiguous,Note="Positive-success/hit/finish timestamps come from real LastSuccess/LastHit/LastFinishBoost. Event distances replay ordinary timed inputs exactly to that timestamp. First-contact times unavailable from Core are honest observed intervals, never ID-sorted sequence."};
    }
    static readonly List<object> Libraries=new List<object>(),Diagnostics=new List<object>(),PairCalibration=new List<object>();
    static readonly Dictionary<string,string> Sources=Directory.GetFiles("Assets/Scripts/Core","*.cs").OrderBy(x=>x,StringComparer.Ordinal).ToDictionary(x=>x.Replace((char)92,(char)47),Hash);
    static string TextHash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    static string Signature(ProbeResult r)=>"successful="+Key(r.CompletedIds)+"\nmissed="+Key(r.MissedIds)+"\nhits="+Key(r.Game.Players[0].HitObstacleIds);
    static string Id(ProbeResult r)=>TextHash(Signature(r));
    static bool HasPositive(Candidate c,int third)=>c.R.Game.Track.RiskZones.Where(z=>!z.IsFinish&&!z.IsCoin&&!z.IsNegative&&(int)Math.Min(2,z.X/c.R.Game.Track.Length*3)==third).Any(z=>c.R.Game.Players[0].SuccessfulRiskIds.Contains(z.Id));
    static bool MissesPositive(Candidate c,int third)=>c.R.Game.Track.RiskZones.Where(z=>!z.IsFinish&&!z.IsCoin&&!z.IsNegative&&(int)Math.Min(2,z.X/c.R.Game.Track.Length*3)==third).Any(z=>!c.R.Game.Players[0].SuccessfulRiskIds.Contains(z.Id));
    static bool Nonconsecutive(Candidate c)
    {var z=c.R.Game.Track.RiskZones.Where(x=>!x.IsFinish&&!x.IsCoin&&!x.IsNegative).OrderBy(x=>x.X).ToArray();var a=Enumerable.Range(0,z.Length).Where(i=>c.R.Game.Players[0].SuccessfulRiskIds.Contains(z[i].Id)).ToArray();return a.Length>1&&a.Zip(a.Skip(1),(x,y)=>y-x).Any(d=>d>1);}
    static bool Forward(Candidate c)
    {var z=c.R.Game.Track.RiskZones.Where(x=>!x.IsFinish&&!x.IsCoin&&!x.IsNegative).OrderBy(x=>x.X).ToArray();return z.Length>0&&!c.R.Game.Players[0].SuccessfulRiskIds.Contains(z[0].Id)&&HasPositive(c,2);}
    static bool Reverse(Candidate c)=>HasPositive(c,0)&&MissesPositive(c,2);
    static bool Combined(Candidate c)=>c.R.Collisions>0&&c.R.Game.Players[0].BoostsEarned>0;
    static object Coverage(Track t,List<Candidate> selected)
    {
        int positive=selected.Select(c=>Positive(c.R)).Distinct().Count(),hit=selected.Select(c=>Key(c.R.Game.Players[0].HitObstacleIds)).Distinct().Count();var thirds=Enumerable.Range(0,3).Select(i=>new{third=i+1,goalIds=t.RiskZones.Where(z=>!z.IsFinish&&!z.IsCoin&&!z.IsNegative&&(int)Math.Min(2,z.X/t.Length*3)==i).Select(z=>z.Id).ToArray(),collectedPlans=selected.Count(c=>HasPositive(c,i)),missedPlans=selected.Count(c=>MissesPositive(c,i))}).ToArray();
        int constPositive=selected.Where(c=>c.Constant!=null).Select(c=>Positive(c.Constant)).Distinct().Count(),constHit=selected.Where(c=>c.Constant!=null).Select(c=>Key(c.Constant.Game.Players[0].HitObstacleIds)).Distinct().Count();
        return new{signatureCount=selected.Count,positiveMaskCount=positive,hitMaskCount=hit,thirds,nonconsecutivePlans=selected.Count(Nonconsecutive),earlyMissLateSuccessPlans=selected.Count(Forward),earlySuccessLateMissPlans=selected.Count(Reverse),positiveAndHitPlans=selected.Count(Combined),constantPositiveMaskCount=constPositive,constantHitMaskCount=constHit,constantCombinedPlans=selected.Count(c=>c.Constant!=null&&c.Constant.Collisions>0&&c.Constant.Game.Players[0].BoostsEarned>0),constantFinished=selected.Count(c=>c.Constant!=null&&c.Constant.Finished),constantEliminated=selected.Count(c=>c.Constant!=null&&!c.Constant.Finished),laggerMeanTime=selected.Count>0?selected.Average(c=>c.R.Time):0};
    }
    static bool Green(Track t,List<Candidate> selected)
    {
        if(selected.Count<12||selected.Select(c=>Positive(c.R)).Distinct().Count()<6||selected.Select(c=>Key(c.R.Game.Players[0].HitObstacleIds)).Distinct().Count()<3||!selected.Any(Nonconsecutive)||!selected.Any(Forward)||!selected.Any(Reverse)||!selected.Any(Combined))return false;
        for(int i=0;i<3;i++)if(t.RiskZones.Any(z=>!z.IsFinish&&!z.IsCoin&&!z.IsNegative&&(int)Math.Min(2,z.X/t.Length*3)==i)&&(!selected.Any(c=>HasPositive(c,i))||!selected.Any(c=>MissesPositive(c,i))))return false;
        if(t.Id==TracksRhythm.CascadeId&&Level(selected[0].R)<=1&&(selected.Select(c=>Positive(c.Constant)).Distinct().Count()<3||selected.Select(c=>Key(c.Constant.Game.Players[0].HitObstacleIds)).Distinct().Count()<3||!selected.Any(c=>c.Constant.Game.Players[0].BoostsEarned>0&&c.Constant.Collisions>0)))return false;
        return true;
    }
    static double GreedyValue(Track t,Candidate c,List<Candidate> selected,int level)
    {
        double v=0;int p=selected.Select(x=>Positive(x.R)).Distinct().Count(),h=selected.Select(x=>Key(x.R.Game.Players[0].HitObstacleIds)).Distinct().Count();if(!selected.Any(x=>Positive(x.R)==Positive(c.R)))v+=p<6?35:2;if(!selected.Any(x=>Key(x.R.Game.Players[0].HitObstacleIds)==Key(c.R.Game.Players[0].HitObstacleIds)))v+=h<3?40:1;
        for(int i=0;i<3;i++){if(HasPositive(c,i)&&!selected.Any(x=>HasPositive(x,i)))v+=22;if(MissesPositive(c,i)&&!selected.Any(x=>MissesPositive(x,i)))v+=22;}
        if(Nonconsecutive(c)&&!selected.Any(Nonconsecutive))v+=24;if(Forward(c)&&!selected.Any(Forward))v+=24;if(Reverse(c)&&!selected.Any(Reverse))v+=24;if(Combined(c)&&!selected.Any(Combined))v+=24;
        if(t.Id==TracksRhythm.CascadeId&&level<=1)
        {
            if(c.Constant==null)return -100000;
            if(c.Constant.Game.Players[0].BoostsEarned>0)v+=12;if(!selected.Any(x=>x.Constant!=null&&Positive(x.Constant)==Positive(c.Constant)))v+=selected.Where(x=>x.Constant!=null).Select(x=>Positive(x.Constant)).Distinct().Count()<3?32:1;
            if(!selected.Any(x=>x.Constant!=null&&Key(x.Constant.Game.Players[0].HitObstacleIds)==Key(c.Constant.Game.Players[0].HitObstacleIds)))v+=20;
        }
        v-=Math.Abs(c.R.Score-new[]{50d,65,75,90}[level])*.12+c.R.Game.Players[0].SlowsEarned*4;return v;
    }
    static double[] Bounds(Track t)
    {
        if(t.Id==TracksRhythm.CascadeId)return new[]{0d,830,1560,2300,2970,3760,4580,5830,6500,6900,7921};
        if(t.Id==TracksMixed.Id)return new[]{0d,850,1650,2400,2850,4200,4800,5600,6300,7100,7921};
        if(t.Id==TracksFull.LongId)return new[]{0d,1200,1750,2250,2850,3400,4300,4650,5550,6250,6800,7921};
        if(t.Id==TracksFull.ShortId)return new[]{0d,1100,1630,2300,3000,3500,4250,4750,5500,6100,6700,7921};
        return new[]{0d,850,1400,1800,2300,2750,3150,3650,4300,4750,5450,6100,6650,7050,7921};
    }
    static void Pair(Track t,FlightSettings f)
    {
        var watch=Stopwatch.StartNew();int beforeSim=simulations,beforeReplay=timedReplayGames,attempts=0;var seen=new HashSet<string>();var all=new List<Candidate>();var pool=Enumerable.Range(0,4).Select(i=>new Dictionary<string,Candidate>()).ToArray();var safe=Probe.Safe(t,f);var master=Probe.Master(t,f);
        Func<string,double[],Candidate> consider=(name,tape)=>
        {
            if(attempts>=Limit||tape.Length==0||tape[0]<0||!seen.Add(RouteKey(tape)))return null;attempts++;var r=Run(t,f,tape);var c=new Candidate{Name=name,Route=tape,R=r};all.Add(c);int level=Level(r);if(level>=0&&level<4){string id=Id(r);if(!pool[level].ContainsKey(id)||pool[level][id].R.Game.Players[0].SlowsEarned>r.Game.Players[0].SlowsEarned)pool[level][id]=c;}return c;
        };
        consider("safe",safe);var masterCandidate=consider("master",master);var singles=new Dictionary<string,Candidate>();
        // First directed Strong test: perturb each physical obstacle under the maximal-goal route.
        foreach(var obstacle in t.Obstacles)
        {
            int[] ids=Enumerable.Range(0,master.Length).Where(i=>master[i]>=obstacle.X-130&&master[i]<=obstacle.X+130).ToArray();if(ids.Length==0)ids=Enumerable.Range(0,master.Length).Where(i=>master[i]>=obstacle.X-300&&master[i]<=obstacle.X+100).ToArray();if(ids.Length==0)continue;
            foreach(double shift in new[]{-96d,-72d,-48d,-24d,-12d,12d,24d,48d,72d,96d})
            {
                var c=consider("strong-target/"+obstacle.Id+"/"+shift,Shift(master,ids,shift));if(c==null||!c.R.Finished||c.R.Rejected>0||c.R.Collisions!=1)continue;string hit=Key(c.R.Game.Players[0].HitObstacleIds);if(!singles.ContainsKey(hit)||singles[hit].R.Completed<c.R.Completed)singles[hit]=c;
            }
        }
        foreach(var oneHit in singles.Values.ToArray())
        {
            consider(oneHit.Name+"/skip-last",oneHit.Route.Take(oneHit.Route.Length-1).ToArray());
            foreach(var zone in t.RiskZones.Where(z=>!z.IsFinish&&!z.IsCoin&&!z.IsNegative))foreach(double radius in new[]{150d,250d})
            {
                double lo=zone.X-radius,hi=zone.X+radius;consider(oneHit.Name+"/safe-"+zone.Id+"/"+radius,oneHit.Route.Where(x=>x<lo||x>hi).Concat(safe.Where(x=>x>=lo&&x<=hi)).OrderBy(x=>x).ToArray());
            }
        }
        Diagnostics.Add(new{trackId=t.Id,profile=f.FirstAction==FirstAction.Jump?"Chosen":"FastFlip",kind="directed-max-goal-single-hit",attemptsSoFar=attempts,results=singles.Values.Select(c=>new{hitObstacleIds=c.R.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(),completed=c.R.Completed,total=c.R.Total,actualGoalLoss=c.R.Total-c.R.Completed,score=c.R.Score,time=c.R.Time,positiveIds=Positive(c.R),distances=c.Route}).ToArray(),strongSignaturesAfterTarget=pool[3].Count});
        Console.WriteLine(t.Id+"/"+f.FirstAction+" directedStrong oneHitObstacles="+singles.Count+" strongMasks="+pool[3].Count+" attempts="+attempts);Save("WORKING");
        var bounds=Bounds(t);int masks=1<<(bounds.Length-1);for(int mask=0;mask<masks&&attempts<3000;mask++)consider("hybrid/"+mask,Hybrid(safe,master,bounds,mask));
        if(t.Id==TracksRhythm.CascadeId)
        {
            var lowBases=all.Where(c=>c.R.Finished&&c.R.Rejected==0&&c.R.Collisions==0&&c.R.Game.Players[0].SlowsEarned==0&&c.R.Game.Players[0].BoostsEarned>=1&&c.R.Game.Players[0].BoostsEarned<=2&&c.R.Game.Players[0].FinishBoostsEarned==0).GroupBy(c=>Positive(c.R)).Select(g=>g.First()).OrderBy(c=>c.R.Game.Players[0].BoostsEarned).ThenBy(c=>c.R.Game.Players[0].SuccessfulRiskIds.Min(id=>t.RiskZones.Single(z=>z.Id==id).X)).Take(15).ToArray();
            for(int b=0;b<lowBases.Length&&attempts<Limit-500;b++)
            {
                var basis=lowBases[b];var local=new List<Candidate>();string positive=Positive(basis.R);
                foreach(var o in t.Obstacles)
                {
                    int[] ids=Enumerable.Range(0,basis.Route.Length).Where(i=>basis.Route[i]>=o.X-100&&basis.Route[i]<=o.X+100).ToArray();if(ids.Length==0)continue;
                    foreach(double shift in new[]{-48d,24d,72d})
                    {
                        var c=consider("low("+basis.Name+")/"+o.Id+"/"+shift,Shift(basis.Route,ids,shift));if(c!=null&&c.R.Finished&&c.R.Rejected==0&&c.R.Collisions==1&&Positive(c.R)==positive&&c.R.Game.Players[0].SlowsEarned==0&&!local.Any(x=>Key(x.R.Game.Players[0].HitObstacleIds)==Key(c.R.Game.Players[0].HitObstacleIds))){c.Indices=ids;c.Shift=shift;local.Add(c);}
                    }
                }
                int needed=basis.R.Game.Players[0].BoostsEarned+3;double firstBonus=t.RiskZones.Where(z=>basis.R.Game.Players[0].SuccessfulRiskIds.Contains(z.Id)).Min(z=>z.X);var random=new Random(771+b);int added=0;
                for(int sample=0;sample<450&&added<80&&attempts<Limit-500;sample++)
                {
                    var chosen=local.OrderBy(x=>random.Next()).Take(needed).ToArray();if(chosen.Length!=needed||chosen.SelectMany(x=>x.Indices).Distinct().Count()!=chosen.Sum(x=>x.Indices.Length))continue;
                    // Preserve a genuine bonus opportunity before the third unmitigated hit.
                    if(chosen.Count(x=>t.Obstacles.Single(o=>x.R.Game.Players[0].HitObstacleIds.Contains(o.Id)).X<firstBonus)>2)continue;
                    var tape=(double[])basis.Route.Clone();foreach(var c in chosen)foreach(int i in c.Indices)tape[i]+=c.Shift;Array.Sort(tape);consider("low-recovery("+basis.Name+")/"+sample,tape);added++;
                }
            }
        }
        // Directed local mutation across outcome-diverse bases supplies alternative hit locations at every level.
        var diverse=pool.SelectMany((p,i)=>p.Values.GroupBy(c=>Positive(c.R)).Select(g=>g.OrderBy(c=>c.R.Collisions).First()).Take(9)).ToArray();
        foreach(var basis in diverse)
        {
            foreach(var o in t.Obstacles)
            {
                int[] ids=Enumerable.Range(0,basis.Route.Length).Where(i=>basis.Route[i]>=o.X-130&&basis.Route[i]<=o.X+130).ToArray();if(ids.Length==0)ids=Enumerable.Range(0,basis.Route.Length).Where(i=>basis.Route[i]>=o.X-300&&basis.Route[i]<=o.X+90).ToArray();if(ids.Length==0)continue;
                foreach(double shift in new[]{-48d,24d,72d})consider("local("+basis.Name+")/"+o.Id+"/"+shift,Shift(basis.Route,ids,shift));
                if(attempts>=Limit)break;
            }
            if(attempts>=Limit)break;
        }
        var means=new List<double>();bool pairPass=true;
        for(int level=0;level<4;level++)
        {
            var values=pool[level].Values.ToList();bool lowCascade=t.Id==TracksRhythm.CascadeId&&level<=1;
            if(lowCascade)
            {
                // Measure actual pre-elimination outcomes for a bounded, mask-diverse shortlist.
                var shortlist=values.GroupBy(c=>Positive(c.R)).SelectMany(g=>g.OrderByDescending(c=>c.R.Game.Players[0].BoostsEarned).ThenByDescending(c=>c.R.Game.Players[0].HitObstacleIds.Select(id=>t.Obstacles.Single(o=>o.Id==id).X).DefaultIfEmpty(0).Min()).Take(14)).Take(210).ToList();
                foreach(var c in shortlist)c.Constant=Run(t,f,c.Route,PartyCamera.Constant);values=shortlist;
            }
            var selected=new List<Candidate>();var remaining=values.OrderBy(c=>c.R.Game.Players[0].SlowsEarned).ThenBy(c=>Math.Abs(c.R.Score-new[]{50d,65,75,90}[level])).ToList();
            while(selected.Count<12&&remaining.Count>0)
            {
                var best=remaining.OrderByDescending(c=>GreedyValue(t,c,selected,level)).First();remaining.Remove(best);var lagger=Run(t,f,best.Route,PartyCamera.Lagger,1d/120);var constant=Run(t,f,best.Route,PartyCamera.Constant,1d/120);if(Level(lagger)!=level||Id(lagger)!=Id(best.R))continue;best.R=lagger;best.Constant=constant;selected.Add(best);
            }
            bool pass=Green(t,selected);pairPass&=pass;double mean=selected.Count>0?selected.Average(c=>c.R.Time):0;means.Add(mean);var signatures=selected.Select(c=>new{id=Id(c.R),successfulGoalIds=c.R.CompletedIds,missedGoalIds=c.R.MissedIds,hitObstacleIds=c.R.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(),basis=new{distances=c.Route,lagger=Summary(c.R,true),constant=Summary(c.Constant,true)}}).ToArray();
            Libraries.Add(new{trackId=t.Id,profile=f.FirstAction==FirstAction.Jump?"Chosen":"FastFlip",difficulty=Names[level],catalogIds=Probe.Catalog(t),catalogHash=TextHash(string.Join("\n",Probe.Catalog(t))),status=pass?"PASS":"BOUNDED_SHORTFALL",signatures,coverage=Coverage(t,selected),rawPoolCounts=new{signatures=pool[level].Count,positiveMasks=pool[level].Values.Select(c=>Positive(c.R)).Distinct().Count(),hitMasks=pool[level].Values.Select(c=>Key(c.R.Game.Players[0].HitObstacleIds)).Distinct().Count()},alternatives=remaining.Take(24).Select(c=>new{id=Id(c.R),successfulGoalIds=c.R.CompletedIds,missedGoalIds=c.R.MissedIds,hitObstacleIds=c.R.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(),distances=c.Route,laggerTime=c.R.Time,score=c.R.Score}).ToArray()});
            Lines.Add(t.Id+"/"+f.FirstAction+"/"+Names[level]+" "+(pass?"PASS":"BOUNDED_SHORTFALL")+" selected="+selected.Count+" positives="+selected.Select(c=>Positive(c.R)).Distinct().Count()+" hits="+selected.Select(c=>Key(c.R.Game.Players[0].HitObstacleIds)).Distinct().Count()+" mean="+mean.ToString("F4",CultureInfo.InvariantCulture)+" raw="+pool[level].Count);Console.WriteLine(Lines.Last());Save("WORKING");
        }
        double masterTime=masterCandidate.R.Time;bool ordered=means.Count==4&&Enumerable.Range(0,3).All(i=>means[i]>means[i+1]+1e-8)&&means[3]>masterTime+1e-8;pairPass&=ordered;watch.Stop();PairCalibration.Add(new{trackId=t.Id,profile=f.FirstAction==FirstAction.Jump?"Chosen":"FastFlip",status=pairPass?"PASS":"BOUNDED_SHORTFALL",means=means.ToArray(),masterReferenceTime=masterTime,strictlyOrdered=ordered,coarseCandidates=attempts,probeRunCalls=simulations-beforeSim,timedReplayGames=timedReplayGames-beforeReplay,elapsedSeconds=watch.Elapsed.TotalSeconds});Lines.Add("PAIR "+t.Id+"/"+f.FirstAction+" candidates="+attempts+" ProbeRuns="+(simulations-beforeSim)+" timedReplayGames="+(timedReplayGames-beforeReplay)+" seconds="+watch.Elapsed.TotalSeconds.ToString("F2",CultureInfo.InvariantCulture));Console.WriteLine(Lines.Last());Save("WORKING");
    }
    static void Save(string status)
    {
        bool stable=Sources.All(x=>Hash(x.Key)==x.Value);if(!stable)throw new Exception("Production Core changed during offline generation");File.WriteAllText("Verification/bot-diversity-outcomes.json",JsonSerializer.Serialize(new{status,baseUnity="9a67f29",selectionStage="Outcome signatures only; timing variants/selection/UI/release remain separate. Failed coverage honestly marked. Each selected basis full1/120Lagger and actualConstant; no state injection.",catalogRule="unchanged clean obstacle + successful ordinary positive + full finish goal",probeRunCalls=simulations,timedReplayGames,sourceHashes=Sources,sourceStable=stable,libraries=Libraries,pairCalibration=PairCalibration,directedDiagnostics=Diagnostics},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));File.WriteAllLines("Verification/bot-diversity-outcomes.txt",Lines);
    }
    static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
            foreach(var t in new[]{TracksRhythm.Get(TracksRhythm.CascadeId),TracksMixed.Get(),TracksFull.Get(TracksFull.ShortId),TracksFull.Get(TracksFull.LongId),TracksFull.Get(TracksFull.FlowId)})foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})Pair(t,f);
            bool green=Libraries.Count==40&&PairCalibration.All(x=>JsonSerializer.SerializeToElement(x).GetProperty("status").GetString()=="PASS");Save(green?"PASS":"BOUNDED_SHORTFALL");return green?0:1;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
