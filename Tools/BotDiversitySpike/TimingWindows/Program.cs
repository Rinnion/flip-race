using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.BotPrototype;

internal static class Program
{
    const int Budget=6000;
    static int probes,orderRejected;
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
    static readonly List<string> Lines=new List<string>();
    sealed class Sample
    {
        public double Shift,Target,ActualDistance,ActualTime,TimeDeltaMs;
        public bool Valid,Accepted;
        public double Score,FinishTime;
        public int Completed,Collisions,Rejected;
        public string Status,Reason;
        public string[] MissingComparedToBase,ExtraComparedToBase,Hits;
    }
    sealed class Window
    {
        public int Index;
        public string Hint;
        public List<Sample> Samples=new List<Sample>();
        public Sample[] Valid=>Samples.Where(s=>s.Valid).OrderBy(s=>s.Shift).ToArray();
        public double SpanMs=>Valid.Length==0?0:1000*(Valid.Max(s=>s.ActualTime)-Valid.Min(s=>s.ActualTime));
    }
    static void Say(string s){Console.WriteLine(s);Lines.Add(s);}
    static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static ProbeResult Run(Track t,FlightSettings f,double[] route,PartyCamera camera=PartyCamera.Lagger,double step=1d/60)
    {if(++probes>Budget)throw new Exception("Bounded full-probe budget exceeded");return Probe.Run(t,f,route,camera,step);}
    static bool Ordered(double[] a)=>a.All(x=>x>=0&&!double.IsNaN(x)&&!double.IsInfinity(x))&&a.Zip(a.Skip(1),(x,y)=>x<=y).All(x=>x);
    static bool Same(ProbeResult r,ProbeResult b)=>r.Finished&&r.Rejected==0&&r.CompletedIds.SequenceEqual(b.CompletedIds)&&r.Game.Players[0].HitObstacleIds.SetEquals(b.Game.Players[0].HitObstacleIds)&&r.Game.Players[0].SlowsEarned==b.Game.Players[0].SlowsEarned;
    static object Detail(ProbeResult r)=>new{r.CompletedIds,r.MissedIds,r.Completed,r.Total,r.Score,r.Time,status=r.Status.ToString(),terminalDistance=r.Game.Players[0].Distance,r.Collisions,r.Accepted,r.Rejected,r.MinMarginLowerBound,hits=r.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray(),collectedRiskIds=r.Game.Players[0].CollectedRiskIds.OrderBy(x=>x).ToArray(),successfulRiskIds=r.Game.Players[0].SuccessfulRiskIds.OrderBy(x=>x).ToArray(),boosts=r.Game.Players[0].BoostsEarned,slows=r.Game.Players[0].SlowsEarned,finishBoosts=r.Game.Players[0].FinishBoostsEarned,recoverySeconds=r.Game.Players[0].CompletedRecoveries*RaceConfig.RecoveryDuration+r.Game.Players[0].RecoveryElapsed,actions=r.Actions};
    static Sample ProbeOne(Track t,FlightSettings f,double[] source,ProbeResult baseline,int edge,double shift)
    {
        var route=(double[])source.Clone();route[edge]+=shift;
        if(!Ordered(route)){orderRejected++;return new Sample{Shift=shift,Target=route[edge],Reason="action-order constraint",Status="not simulated"};}
        var r=Run(t,f,route);var a=r.Actions[edge];
        return new Sample{Shift=shift,Target=route[edge],ActualDistance=a.Distance,ActualTime=a.Time,TimeDeltaMs=1000*(a.Time-baseline.Actions[edge].Time),Accepted=a.Accepted,Valid=Same(r,baseline),Score=r.Score,Completed=r.Completed,Collisions=r.Collisions,Rejected=r.Rejected,FinishTime=r.Time,Status=r.Status.ToString(),Reason=Same(r,baseline)?"same full-route actual outcome mask":"actual outcome/acceptance changed",MissingComparedToBase=baseline.CompletedIds.Except(r.CompletedIds).ToArray(),ExtraComparedToBase=r.CompletedIds.Except(baseline.CompletedIds).ToArray(),Hits=r.Game.Players[0].HitObstacleIds.OrderBy(x=>x).ToArray()};
    }
    static object[] Intervals(Window w)
    {
        var result=new List<object>();var group=new List<Sample>();
        Action flush=()=>{if(group.Count==0)return;result.Add(new{sampleCount=group.Count,minShift=group.First().Shift,maxShift=group.Last().Shift,minTarget=group.First().Target,maxTarget=group.Last().Target,actualTimeSpanMs=1000*(group.Max(s=>s.ActualTime)-group.Min(s=>s.ActualTime)),boundaries="sampled endpoints only; no unsampled continuum guarantee"});group.Clear();};
        foreach(var s in w.Samples.OrderBy(s=>s.Shift)){if(!s.Valid){flush();continue;}if(group.Count>0&&s.Shift-group.Last().Shift>10.000001)flush();group.Add(s);}flush();return result.ToArray();
    }
    static uint Next(ref uint x){x^=x<<13;x^=x>>17;x^=x<<5;return x;}
    static double[] Joint(double[] source,List<Window> windows,int seed)
    {
        uint state=(uint)(seed+1)*0x9E3779B9u;var route=(double[])source.Clone();
        foreach(var w in windows){var choices=w.Valid;if(choices.Length>0)route[w.Index]=choices[Next(ref state)%(uint)choices.Length].Target;}
        return route;
    }
    static List<Sample[]> Connected(Window w)
    {
        var groups=new List<Sample[]>();var current=new List<Sample>();
        Action flush=()=>{if(current.Count>0){groups.Add(current.ToArray());current.Clear();}};
        foreach(var s in w.Samples.OrderBy(s=>s.Shift))
        {if(!s.Valid){flush();continue;}if(current.Count>0&&s.Shift-current.Last().Shift>10.000001)flush();current.Add(s);}flush();return groups;
    }
    static object Bvar03(Track t,FlightSettings f,double[] route,ProbeResult baseline,List<Window> windows)
    {
        var choices=windows.Select(w=>new{Window=w,Group=Connected(w).OrderByDescending(g=>g.Last().Target-g.First().Target).FirstOrDefault()})
            .Where(x=>x.Group!=null&&x.Group.Length>=3&&x.Group.Last().Target-x.Group.First().Target>=20).ToArray();
        var early=choices.Where(x=>route[x.Window.Index]<t.Length/2).OrderByDescending(x=>x.Group.Last().Target-x.Group.First().Target).Take(3).ToArray();
        var late=choices.Where(x=>route[x.Window.Index]>=t.Length/2).OrderByDescending(x=>x.Group.Last().Target-x.Group.First().Target).Take(3).ToArray();
        var trials=new List<object>();int pairAttempts=0;
        foreach(var a in early)foreach(var b in late)
        {
            if(Math.Abs(route[a.Window.Index]-route[b.Window.Index])<t.Length/4)continue;
            pairAttempts++;
            Func<Sample[],Sample[]> points=g=>new[]{g.First(),g.OrderBy(s=>Math.Abs(s.Target-(g.First().Target+g.Last().Target)/2)).First(),g.Last()};
            var ap=points(a.Group);var bp=points(b.Group);var variants=new List<object>();bool all=true;
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)
            {
                var candidate=(double[])route.Clone();candidate[a.Window.Index]=ap[i].Target;candidate[b.Window.Index]=bp[j].Target;
                var l=Run(t,f,candidate,PartyCamera.Lagger,1d/120);var c=Run(t,f,candidate,PartyCamera.Constant,1d/120);
                bool valid=Same(l,baseline)&&Same(c,baseline);all&=valid;
                variants.Add(new{firstPosition=new[]{"early","middle","late"}[i],secondPosition=new[]{"early","middle","late"}[j],valid,distances=candidate,lagger=Detail(l),constant=Detail(c)});
            }
            var ao=t.Obstacles.OrderBy(o=>Math.Abs(o.X-route[a.Window.Index])).First();var bo=t.Obstacles.OrderBy(o=>Math.Abs(o.X-route[b.Window.Index])).First();
            var trial=new{firstEdge=a.Window.Index,secondEdge=b.Window.Index,firstObstacle=ao.Id,secondObstacle=bo.Id,firstMeasuredTargetInterval=new[]{a.Group.First().Target,a.Group.Last().Target},secondMeasuredTargetInterval=new[]{b.Group.First().Target,b.Group.Last().Target},firstSampledWidthMs=1000*(a.Group.Max(s=>s.ActualTime)-a.Group.Min(s=>s.ActualTime)),secondSampledWidthMs=1000*(b.Group.Max(s=>s.ActualTime)-b.Group.Min(s=>s.ActualTime)),firstPositionsRelativeObstacleX=ap.Select(s=>s.ActualDistance-ao.X).ToArray(),secondPositionsRelativeObstacleX=bp.Select(s=>s.ActualDistance-bo.X).ToArray(),coverageOfMeasuredDistanceInterval=new[]{1d,1d},allNineWholePlansValid=all,variants};
            trials.Add(trial);
            if(all)return new{status="PROVED",pairAttempts,interpretation="Two separated maneuvers. Early/middle/late points span100% of each sampled connected single-action interval in position relative to its obstacle. Every one of nine Cartesian whole routes verified at1/120 with both cameras. Sampled intervals do not certify unsampled continuum.",trials};
        }
        return new{status="NOT PROVED",pairAttempts,interpretation="Bounded candidate-pair search did not establish all nine Cartesian combinations; failures retained for review.",trials};
    }
    static int Main(string[] args)
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
        var watch=Stopwatch.StartNew();var evidence=new List<object>();
        var sourceFiles=Directory.GetFiles("Assets/Scripts/Core","*.cs").Concat(new[]{"Tools/BotPrototype/SharedProbe.cs","Tools/BotDiversitySpike/TimingWindows/Program.cs"}).ToArray();
        var hashes=sourceFiles.ToDictionary(p=>p.Replace((char)92,(char)47),Hash);
        try
        {
            bool risk=args.Contains("--risk");
            foreach(var t in (risk?new[]{RaceTracks.All.Single(t=>t.Id==TracksMixed.Id),RaceTracks.All.Single(t=>t.Id==TracksRhythm.CascadeId)}:new[]{RaceTracks.All.Single(t=>t.Id==TracksFull.LongId),RaceTracks.All.Single(t=>t.Id==TracksRhythm.CascadeId)}))
            foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
            foreach(var difficulty in (risk?new[]{BotDifficulty.Master}:new[]{BotDifficulty.Master,BotDifficulty.Novice}))
            {
                int startProbes=probes;var plan=BotPlanCatalog.Select(t.Id,f,difficulty,0);double[] route=plan.Distances;
                var baseline=Run(t,f,route);var baselineConstant=Run(t,f,route,PartyCamera.Constant,1d/120);
                if(!baseline.Finished||baseline.Rejected!=0)throw new Exception("Base is not a legal full route: "+plan.Id);
                var windows=new List<Window>();
                for(int i=0;i<route.Length;i++)
                {
                    string hint=i==route.Length-1&&difficulty==BotDifficulty.Master?"finish preparation":"ordinary jump/flip";
                    var landing=t.RiskZones.Where(z=>z.IsLanding).OrderBy(z=>Math.Abs(z.X-route[i])).FirstOrDefault();
                    if(landing!=null&&Math.Abs(landing.X-route[i])<260)hint+=" near linked landing "+landing.Id;
                    var w=new Window{Index=i,Hint=hint};
                    for(double shift=-110;shift<=110;shift+=10)w.Samples.Add(ProbeOne(t,f,route,baseline,i,shift));
                    windows.Add(w);
                }
                // Refine the finish edge and the narrowest nonzero sampled window.
                var refine=windows.OrderBy(w=>w.SpanMs).Where(w=>w.Valid.Length>1).Take(1).ToList();
                if(difficulty==BotDifficulty.Master&&!refine.Contains(windows.Last()))refine.Add(windows.Last());
                foreach(var w in refine)
                {
                    if(w.Valid.Length==0)continue;double lo=Math.Max(-110,w.Valid.First().Shift-10),hi=Math.Min(110,w.Valid.Last().Shift+10);
                    for(double shift=lo;shift<=hi+1e-7;shift+=2)if(!w.Samples.Any(s=>Math.Abs(s.Shift-shift)<1e-7))w.Samples.Add(ProbeOne(t,f,route,baseline,w.Index,shift));
                }
                var extrema=new List<object>();
                var selected=windows.OrderByDescending(w=>w.SpanMs).Take(1).Concat(refine).Distinct().ToArray();
                foreach(var w in selected)foreach(var s in new[]{w.Valid.First(),w.Valid.Last()}.Distinct())
                {
                    var candidate=(double[])route.Clone();candidate[w.Index]=s.Target;
                    var lagger=Run(t,f,candidate,PartyCamera.Lagger,1d/120);var constant=Run(t,f,candidate,PartyCamera.Constant,1d/120);
                    extrema.Add(new{edge=w.Index,s.Shift,distances=candidate,laggerSameMask=Same(lagger,baseline),masterConstantCorrect=difficulty!=BotDifficulty.Master||Same(constant,baseline),lagger=Detail(lagger),constant=Detail(constant)});
                }
                object explicitCoverage=difficulty==BotDifficulty.Master?Bvar03(t,f,route,baseline,windows):null;
                var joints=new List<object>();int rawValid=0,finalValid=0;var unique=new HashSet<string>();
                for(int seed=0;seed<20;seed++)
                {
                    var requested=Joint(route,windows,seed);double[] final=null;var attempts=new List<object>();
                    Func<double[],string,bool> attempt=(candidate,name)=>
                    {
                        if(!Ordered(candidate)){orderRejected++;attempts.Add(new{name,distances=candidate,ordered=false,valid=false});return false;}
                        var r=Run(t,f,candidate);bool valid=Same(r,baseline);attempts.Add(new{name,distances=candidate,ordered=true,valid,lagger=Detail(r)});if(valid)final=candidate;return valid;
                    };
                    bool raw=attempt(requested,"independent sampled-window points");if(raw)rawValid++;
                    if(!raw)
                    {
                        var shrunk=(double[])route.Clone();foreach(var w in windows){double desired=(requested[w.Index]-route[w.Index])*.5;shrunk[w.Index]=w.Valid.OrderBy(s=>Math.Abs(s.Shift-desired)).ThenBy(s=>Math.Abs(s.Shift)).First().Target;}
                        if(!attempt(shrunk,"repair1 nearest measured point at half offset"))
                        {
                            var spaced=(double[])route.Clone();var chosen=new List<int>();
                            foreach(var w in windows.Where(w=>Math.Abs(requested[w.Index]-route[w.Index])>1e-8).OrderByDescending(w=>w.SpanMs))
                            {if(chosen.All(i=>Math.Abs(route[i]-route[w.Index])>=700)){chosen.Add(w.Index);spaced[w.Index]=requested[w.Index];if(chosen.Count>=3)break;}}
                            attempt(spaced,"repair2 at most three separated measured single-edge points");
                        }
                    }
                    object verified=null;bool finalOk=false;
                    if(final!=null)
                    {
                        var l=Run(t,f,final,PartyCamera.Lagger,1d/120);var c=Run(t,f,final,PartyCamera.Constant,1d/120);
                        finalOk=Same(l,baseline)&&(difficulty!=BotDifficulty.Master||Same(c,baseline));
                        if(finalOk){finalValid++;unique.Add(string.Join(",",final.Select(x=>x.ToString("R"))));}
                        verified=new{distances=final,lagger=Detail(l),constant=Detail(c),finalOk};
                    }
                    joints.Add(new{seed,rawValid=raw,finalValid=finalOk,attempts,verified});
                }
                var validSpans=windows.Select(w=>w.SpanMs).OrderBy(x=>x).ToArray();
                int disconnected=windows.Count(w=>Intervals(w).Length>1);
                Say(plan.Id+" edges="+route.Length+" sampled spans(ms) min/median/max="+validSpans.First().ToString("F1")+"/"+validSpans[validSpans.Length/2].ToString("F1")+"/"+validSpans.Last().ToString("F1")+" disconnected="+disconnected+" joint raw/final/unique="+rawValid+"/"+finalValid+"/"+unique.Count+" probes="+(probes-startProbes));
                evidence.Add(new{plan.Id,track=t.Id,profile=f.FirstAction.ToString(),difficulty=difficulty.ToString(),seed=0,baseDistances=route,catalog=Probe.Catalog(t),baseline=Detail(baseline),baselineConstant=Detail(baselineConstant),singleWindows=windows.Select(w=>new{edge=w.Index,w.Hint,baseTarget=route[w.Index],baseActualTime=baseline.Actions[w.Index].Time,sampledTimeSpanMs=w.SpanMs,intervals=Intervals(w),samples=w.Samples.OrderBy(s=>s.Shift).ToArray()}).ToArray(),selectedExtremaVerification=extrema,explicitTwoManeuverEarlyMidLate=explicitCoverage,jointSeedCount=20,rawJointValid=rawValid,finalJointValid=finalValid,uniqueJointVariants=unique.Count,joints,simulationRuns=probes-startProbes});
            }
            bool stable=hashes.All(p=>Hash(p.Key)==p.Value);if(!stable)throw new Exception("Sources changed during spike");
            watch.Stop();Say("Measured requested bases; full simulations="+probes+"; order-invalid candidates="+orderRejected+"; elapsed="+watch.Elapsed.TotalSeconds.ToString("F2")+"s; sources stable="+stable);
            File.WriteAllText("Verification/bot-diversity-timing-spike.json",JsonSerializer.Serialize(new{status="MEASURED",scope="BOT-DIVERSITY5.2.1 bounded technical timing-window spike; same outcome masks. Mixed/Cascade Master risk mode adds explicit two-maneuver early/mid/late Cartesian whole-plan proofs. Initial Long/Cascade eight-base scan retained under owned TimingWindows/initial-eight-bases.json. Outcome-mask diversity is separate.",baseRevision="9a67f29",budget=Budget,simulationRuns=probes,orderRejected,wallSeconds=watch.Elapsed.TotalSeconds,explorationStep=1d/60,selectedVerificationStep=1d/120,scanDistanceRadius=110,coarseDistanceStep=10,refinementDistanceStep=2,method="Each single edge is shifted with every other input fixed. Valid requires same completed and hit masks, full Lagger finish, no rejected input, same negative effects. Windows describe sampled feasible points, not a certified continuous interval. Joint seeds0..19 sample the measured valid points; failed seeds are reported, with at most two explicit measured-point repairs. Master must remain100% with Constant; lower-level Constant actual outcomes are reported.",sourceHashes=hashes,sourceStable=stable,evidence},Json));
            File.WriteAllLines("Verification/bot-diversity-timing-spike.txt",Lines);return 0;
        }
        catch(Exception e){watch.Stop();Say("SPIKE ERROR: "+e);File.WriteAllText("Verification/bot-diversity-timing-spike.json",JsonSerializer.Serialize(new{status="ERROR",simulationRuns=probes,error=e.ToString(),evidence,sourceHashes=hashes},Json));File.WriteAllLines("Verification/bot-diversity-timing-spike.txt",Lines);return 1;}
    }
}