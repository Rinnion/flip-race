using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlipRace.Core;

static partial class Program
{
    const string StudyProtocol="BSTO-NAMED-TRAINING-1";
    sealed class StudyRow { public double K; public Parameters Parameters; }
    // Parsed once; collections never exposed outside this private implementation.
    sealed class StudyDefinition
    {
        internal readonly string Id,ConfigHash,TableJson,TableHash,ScheduleHash;
        internal readonly PartyCamera Camera;
        internal readonly bool RecordSelectedInputs;
        internal readonly int ExpectedN;
        internal readonly StudyRow[] Rows;
        internal readonly Spec[] Cases;
        internal StudyDefinition(string id,string hash,PartyCamera camera,bool inputs,int n,StudyRow[] rows,Spec[] cases)
        {
            Id=id;ConfigHash=hash;Camera=camera;RecordSelectedInputs=inputs;ExpectedN=n;Rows=rows;Cases=cases;
            TableJson=JsonSerializer.Serialize(rows,Compact);TableHash=ShaText(TableJson);ScheduleHash=ShaText(JsonSerializer.Serialize(cases,Compact));
        }
        internal StochasticBotSettings Settings(Spec s)
        {
            var p=Rows.Single(r=>r.Parameters.CohortId==s.CohortId).Parameters;
            return StochasticBotSettings.CreateFixed(RaceTracks.Get(s.TrackId),Flight(s.ProfileId),Enum.Parse<BotDifficulty>(s.Difficulty),s.Seed,p.CollectProbability,p.HitProbability);
        }
    }
    static void StudyObject(JsonElement obj,string[] required,params string[] optional)
    {
        if(obj.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected object");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in obj.EnumerateObject())
            if(!seen.Add(p.Name)||(!required.Contains(p.Name)&&!optional.Contains(p.Name)))throw new ArgumentException("Unknown/duplicate field: "+p.Name);
        if(required.Any(x=>!seen.Contains(x)))throw new ArgumentException("Missing required fields");
    }
    static string[] StudySubset(JsonElement obj,string name,string[] supported)
    {
        var v=obj.GetProperty(name);if(v.ValueKind!=JsonValueKind.Array)throw new ArgumentException(name+" must be array");
        var a=v.EnumerateArray().Select(e=>e.GetString()).ToArray();
        if(a.Length==0||a.Distinct(StringComparer.Ordinal).Count()!=a.Length||a.Any(x=>!supported.Contains(x)))throw new ArgumentException("Invalid/duplicate "+name);
        return supported.Where(x=>a.Contains(x)).ToArray();
    }
    static StudyDefinition ParseStudy(byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
        StudyObject(root,new[]{"studyId","phase","tracks","profiles","levels","seedRanges","kByPair"},"camera","humanInput","recordSelectedInputs");
        string id=root.GetProperty("studyId").GetString();
        if(id==null||!Regex.IsMatch(id,@"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z")||id.Length>64||id=="initial")throw new ArgumentException("Safe unique lowercase studyId required (not initial)");
        if(root.GetProperty("phase").GetString()!="Training")throw new ArgumentException("Training-only v1: Validation/held-out execution unsupported");
        var tracks=StudySubset(root,"tracks",TrackIds);var profiles=StudySubset(root,"profiles",new[]{"Chosen","FastFlip"});
        var levels=StudySubset(root,"levels",Levels.Select(x=>x.ToString()).ToArray());
        string camera=root.TryGetProperty("camera",out var c)?c.GetString():"Lagger";
        if(camera!="Lagger"&&camera!="Constant")throw new ArgumentException("Declared exact camera required");
        if(root.TryGetProperty("humanInput",out var h)&&h.GetString()!="Idle")throw new ArgumentException("Only human Idle supported");
        bool inputs=root.TryGetProperty("recordSelectedInputs",out var inp)&&inp.GetBoolean();
        var ranges=root.GetProperty("seedRanges");
        if(ranges.ValueKind!=JsonValueKind.Array||ranges.GetArrayLength()==0)throw new ArgumentException("Nonempty seedRanges required");
        var seeds=new SortedSet<uint>();
        foreach(var range in ranges.EnumerateArray())
        {
            StudyObject(range,new[]{"start","count"});uint start=range.GetProperty("start").GetUInt32();int count=range.GetProperty("count").GetInt32();
            if(count<1||start>=1000000||(ulong)start+(ulong)count>1000000UL)throw new ArgumentException("Positive ranges entirely below1000000 required");
            for(uint i=0;i<(uint)count;i++)if(!seeds.Add(start+i))throw new ArgumentException("Overlapping/duplicate seed ranges prohibited");
        }
        if(seeds.Count<200)throw new ArgumentException("N>=200 required per cohort");
        var factors=new Dictionary<string,double>(StringComparer.Ordinal);
        var ks=root.GetProperty("kByPair");if(ks.ValueKind!=JsonValueKind.Array)throw new ArgumentException("kByPair array required");
        foreach(var k in ks.EnumerateArray())
        {
            StudyObject(k,new[]{"trackId","profileId","k"});
            string track=k.GetProperty("trackId").GetString(),profile=k.GetProperty("profileId").GetString();double factor=k.GetProperty("k").GetDouble();
            if(!tracks.Contains(track)||!profiles.Contains(profile)||!double.IsFinite(factor)||factor<=0||!factors.TryAdd(track+"/"+profile,factor))throw new ArgumentException("Explicit unique positive finite k required per selected pair");
        }
        if(factors.Count!=tracks.Length*profiles.Length)throw new ArgumentException("Exactly one k per selected pair required");
        var rows=new List<StudyRow>();var cases=new List<Spec>();
        foreach(string track in tracks)foreach(string profile in profiles)foreach(string difficulty in levels)
        {
            var level=Enum.Parse<BotDifficulty>(difficulty);var initial=StochasticBotSettings.CreateInitial(RaceTracks.Get(track),Flight(profile),level,0u);
            if(initial.BonusGain<=0||initial.CollisionCostSum<=0)throw new NotSupportedException("Invalid B/C model; no clamp");
            double factor=factors[track+"/"+profile],hit=level==BotDifficulty.Master?0:factor*(initial.BonusGain/initial.CollisionCostSum)*(1-initial.Quality);
            var p=ParameterRow(StochasticBotSettings.CreateFixed(RaceTracks.Get(track),Flight(profile),level,0u,initial.Quality,hit));
            rows.Add(new StudyRow{K=factor,Parameters=p});
            foreach(uint seed in seeds)cases.Add(new Spec{Index=cases.Count,Id=p.CohortId+"/"+seed.ToString(CultureInfo.InvariantCulture),TrackId=track,ProfileId=profile,Difficulty=difficulty,CohortId=p.CohortId,Seed=seed});
        }
        return new StudyDefinition(id,Convert.ToHexString(SHA256.HashData(bytes)),Enum.Parse<PartyCamera>(camera),inputs,seeds.Count,rows.ToArray(),cases.ToArray());
    }
    static object DescribeStudy(StudyDefinition d)=>new{
        protocolId=StudyProtocol,studyId=d.Id,phase="Training",exploratoryOnly=true,validationAcceptance=false,heldOutSeedsRun=false,
        assemblySha256=ShaFile(AssemblyPath),runtimeVersion=Environment.Version.ToString(),configSha256=d.ConfigHash,
        parameterTableSha256=d.TableHash,parameterTableJson=d.TableJson,parameters=d.Rows,
        scheduleSha256=d.ScheduleHash,scheduleJson=JsonSerializer.Serialize(d.Cases,Compact),schedule=d.Cases,expectedN=d.ExpectedN,cohorts=d.Rows.Length,expectedCases=d.Cases.Length,
        camera=d.Camera.ToString(),humanInput="Idle",maximumParallelism=MaximumWorkers,fixedBeforeStart=true,
        probabilityRule="pCollect=q; pHit=k*(B/C)*(1-q); Master1/0; positive finite k, no clamp",
        selectedInputsScope=d.RecordSelectedInputs?"Initial saved decision combo, not applied Press tape":"Not recorded"};
    static string StudyDescription(string id="synthetic-study",string extra="",string ranges="[{\"start\":0,\"count\":200}]",string k="1",string phase="Training")
        =>"{\"studyId\":\""+id+"\",\"phase\":\""+phase+"\",\"tracks\":[\"full-long\"],\"profiles\":[\"Chosen\"],\"levels\":[\"Novice\",\"Master\"],\"seedRanges\":"+ranges+",\"kByPair\":[{\"trackId\":\"full-long\",\"profileId\":\"Chosen\",\"k\":"+k+"}]"+extra+"}";
    static StudyDefinition StudyText(string text)=>ParseStudy(Encoding.UTF8.GetBytes(text));
    static int StudySelfCheck()
    {
        int checks=0;void Check(bool ok){checks++;if(!ok)throw new InvalidOperationException("Study self-check"+checks+" failed");}
        void Reject(string s){bool rejected=false;try{StudyText(s);}catch(ArgumentException){rejected=true;}catch(JsonException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected);}
        CheckSuppressionTelemetry(400);checks++;
        var d=StudyText(StudyDescription());Check(d.Rows.Length==2&&d.Cases.Length==400&&d.ExpectedN==200);
        Check(d.Rows.Single(x=>x.Parameters.Difficulty=="Master").Parameters.CollectProbability==1&&d.Rows.Single(x=>x.Parameters.Difficulty=="Master").Parameters.HitProbability==0);
        Reject(StudyDescription(k:"0"));Reject(StudyDescription(k:"-1"));Reject(StudyDescription(k:"1e308"));Reject(StudyDescription(k:"\"NaN\""));
        Reject(StudyDescription(extra:",\"hitProbability\":0"));Reject(StudyDescription(extra:",\"phase\":\"Training\""));Reject(StudyDescription(phase:"Validation"));
        Reject(StudyDescription(id:"../unsafe"));Reject(StudyDescription(id:"initial"));
        Reject(StudyDescription(ranges:"[{\"start\":1000000,\"count\":200}]"));Reject(StudyDescription(ranges:"[{\"start\":999999,\"count\":200}]"));
        Reject(StudyDescription(ranges:"[{\"start\":0,\"count\":199}]"));Reject(StudyDescription(ranges:"[{\"start\":0,\"count\":200},{\"start\":199,\"count\":200}]"));
        Reject(StudyDescription().Replace("\"Chosen\"],","\"Chosen\",\"Chosen\"],"));
        Reject(StudyDescription().Replace("\"Master\"","\"Unknown\""));Reject(StudyDescription().Replace("\"k\":1","\"k\":1,\"collectProbability\":0"));
        string allText=StudyDescription().Replace("[\"full-long\"]",JsonSerializer.Serialize(TrackIds)).Replace("[\"Chosen\"]","[\"Chosen\",\"FastFlip\"]").Replace("\"Novice\",\"Master\"","\"Novice\",\"Amateur\",\"Confident\",\"Strong\",\"Master\"");
        allText=allText.Replace("\"kByPair\":[{\"trackId\":\"full-long\",\"profileId\":\"Chosen\",\"k\":1}]","\"kByPair\":"+JsonSerializer.Serialize((from t in TrackIds from p in new[]{"Chosen","FastFlip"} select new{trackId=t,profileId=p,k=1}).ToArray(),Compact));
        var all=StudyText(allText);Check(all.Rows.Length==50&&all.Cases.Length==10000&&all.Cases.All(s=>s.Seed<1000000));
        Check(all.Cases.Select((s,i)=>s.Index==i).All(x=>x)&&all.Cases.Select(s=>s.Id).Distinct().Count()==10000);
        Check(all.Rows.All(row=>JsonSerializer.Serialize(row.Parameters,Compact)==JsonSerializer.Serialize(InitialRows().Single(p=>p.CohortId==row.Parameters.CohortId),Compact)));
        var reversed=StudyText(StudyDescription(ranges:"[{\"start\":100,\"count\":100},{\"start\":0,\"count\":100}]"));Check(reversed.ScheduleHash==d.ScheduleHash&&reversed.TableHash==d.TableHash);
        var extended=StudyText(StudyDescription(ranges:"[{\"start\":0,\"count\":400}]"));Check(extended.ExpectedN==400&&extended.Cases.Length==800);
        Check(Stats(Enumerable.Range(0,400).Select(i=>(double?)i),400).Mean==199.5);Check(!Stats(Enumerable.Repeat((double?)1,399),400).Mean.HasValue);
        var synthetic=Enumerable.Range(0,400).Select(i=>new CaseResult{Terminal=true,Collisions=2,Status="Error",Error="synthetic quality failure"}).ToArray();
        Check(Stats(synthetic.Select(r=>EndCount(r,r.Collisions)),400).Mean==2);synthetic[0].Terminal=false;Check(!Stats(synthetic.Select(r=>EndCount(r,r.Collisions)),400).Complete);
        // Metadata/clone isolation only: no controller/Advance or physical cohort.
        foreach(var row in all.Rows)
        {
            var spec=all.Cases.First(s=>s.CohortId==row.Parameters.CohortId);var settings=all.Settings(spec);
            var game=RaceSimulation.CreateStochasticBotRace(RaceTracks.Get(spec.TrackId),Flight(spec.ProfileId),settings);
            Check(game.IsBot&&game.BotPlan==null&&ReferenceEquals(game.StochasticBot,settings)&&game.Players[1].Ready&&!game.Players[0].Ready);
            var clone=RaceSnapshot.Clone(game);Check(ReferenceEquals(clone.StochasticBot,settings)&&!ReferenceEquals(clone.Players[1],game.Players[1]));
            clone.Players[1].HitObstacleIds.Add("synthetic-alias-check");
            Check(!game.Players[1].HitObstacleIds.Contains("synthetic-alias-check")&&!ReferenceEquals(clone.Players[1].PendingEffects,game.Players[1].PendingEffects));
        }
        var progress=new RunProgress{Schedule=d.Cases,Results=new ConcurrentDictionary<int,CaseResult>(),SyntheticFixture=true,CohortTag="NAMED_TRAINING",RecoveryPrefix=d.Id};
        progress.Results.TryAdd(0,new CaseResult{Case=d.Cases[0],Terminal=true,TerminalTime=33,Status="Error",Error="synthetic quality failure"});
        var fault=CheckAfterCohort(()=>throw new FileNotFoundException("synthetic manifest vanished"),null);
        using(var j=JsonDocument.Parse(JsonSerializer.Serialize(RecoveryReport(progress,fault.Fatal),Json)))
            Check(!fault.Stable&&j.RootElement.GetProperty("actualCases").GetInt32()==1&&j.RootElement.GetProperty("unrunCaseIds").GetArrayLength()==399&&j.RootElement.GetProperty("cases")[0].GetProperty("terminalTime").GetDouble()==33);
        Console.WriteLine("STUDY SELF-CHECK PASS "+checks+" checks; no Advance, race, publication, cohort, or held-out seed executed.");return 0;
    }
    static void CreateStudyOutput(string path,string content)
    {
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        using var writer=new StreamWriter(stream,new UTF8Encoding(false));writer.Write(content);
    }
    static int StudyEntry(string[] args)
    {
        if(args.Length==1&&args[0]=="--self-check-study")return StudySelfCheck();
        if(args.Length==2&&args[0]=="--describe-study"){Console.WriteLine(JsonSerializer.Serialize(DescribeStudy(ParseStudy(File.ReadAllBytes(args[1]))),Json));return 0;}
        StudyDefinition study=null;ConcurrentDictionary<int,CaseResult> results=null;string repository=null;
        try{return RunStudy(args,out study,out results,out repository);}
        catch(Exception error)
        {
            if(cohortStarted)throw;
            string folder=Path.Combine(repository??Directory.GetCurrentDirectory(),"Tools/StochasticStatistics/Recovery");Directory.CreateDirectory(folder);
            string failure=Path.Combine(folder,"study-setup-failure-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".json");
            CreateStudyOutput(failure,JsonSerializer.Serialize(new{status="SETUP_FAILED_NO_COHORT",studyId=study?.Id,actualCases=0,heldOutSeedsRun=false,knownSchedule=study!=null,unrunCaseIds=study?.Cases.Select(s=>s.Id).ToArray(),arguments=args,error=error.ToString()},Json));
            Console.Error.WriteLine("Study setup failed; no cases run. "+failure+"\n"+error);return 1;
        }
    }
    static int RunStudy(string[] args,out StudyDefinition study,out ConcurrentDictionary<int,CaseResult> results,out string repository)
    {
        study=null;results=null;repository=null;
        if(args.Length!=5||args[0]!="--study"||args[1]!="--study-id"||args[3]!="--parallelism"||!int.TryParse(args[4],out int workers)||workers<1||workers>MaximumWorkers)
            throw new ArgumentException("Required: --study --study-id <frozen-id> --parallelism 1..3. No seed/config/parameter overrides.");
        string publication=AppContext.BaseDirectory,manifestPath=Path.Combine(publication,"study-manifest.json");
        string manifestHash=ShaFile(manifestPath);using var doc=JsonDocument.Parse(File.ReadAllBytes(manifestPath));var manifest=doc.RootElement;
        if(manifest.GetProperty("protocolId").GetString()!=StudyProtocol||manifest.GetProperty("studyId").GetString()!=args[2]||manifest.GetProperty("phase").GetString()!="Training")throw new ArgumentException("Frozen Training manifest/ID required");
        repository=manifest.GetProperty("repositoryRoot").GetString();var hashes=VerifyPublication(manifest,publication);
        string configRelative=manifest.GetProperty("configPath").GetString();if(!hashes.ContainsKey(configRelative))throw new InvalidOperationException("Config must be a hashed publication file");
        study=ParseStudy(File.ReadAllBytes(Path.Combine(publication,configRelative)));var d=study;
        if(d.Id!=args[2]||ShaFile(AssemblyPath)!=manifest.GetProperty("assemblySha256").GetString()||d.ConfigHash!=manifest.GetProperty("configSha256").GetString()||d.TableHash!=manifest.GetProperty("parameterTableSha256").GetString()||d.ScheduleHash!=manifest.GetProperty("scheduleSha256").GetString()||d.Cases.Length!=manifest.GetProperty("expectedCases").GetInt32()||d.ExpectedN!=manifest.GetProperty("expectedN").GetInt32()||d.Rows.Length!=manifest.GetProperty("cohorts").GetInt32()||d.Camera.ToString()!=manifest.GetProperty("camera").GetString()||manifest.GetProperty("maximumParallelism").GetInt32()!=MaximumWorkers)throw new InvalidOperationException("Published assembly/config/table/schedule differs from declared manifest");
        string output=Path.Combine(repository,"Verification/stochastic-bots-study-"+d.Id);
        if(File.Exists(output+".json")||File.Exists(output+".txt"))throw new IOException("Output exists: never overwrite/retry a cohort");
        string recovery=Path.Combine(repository,"Tools/StochasticStatistics/Recovery");Directory.CreateDirectory(recovery);
        CreateStudyOutput(Path.Combine(recovery,d.Id+".claim"),JsonSerializer.Serialize(new{studyId=d.Id,manifestSha256=manifestHash,assemblySha256=ShaFile(AssemblyPath),scheduleSha256=d.ScheduleHash},Json));
        results=new ConcurrentDictionary<int,CaseResult>();var observed=results;
        activeRun=new RunProgress{Schedule=d.Cases,Results=observed,CohortTag="NAMED_TRAINING/"+d.Id,RecoveryPrefix=d.Id,AssemblyHash=ShaFile(AssemblyPath),ManifestHash=manifestHash,ParameterHash=d.TableHash,ScheduleHash=d.ScheduleHash};
        Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;Cancellation.Cancel();};int completed=0,firstMaster=0;Exception fatal=null;
        Console.WriteLine("DECLARED TRAINING "+d.Id+" cases="+d.Cases.Length+" N="+d.ExpectedN+" workers="+workers+" assembly="+activeRun.AssemblyHash+" manifest="+manifestHash+" table="+d.TableHash+" schedule="+d.ScheduleHash+"; NO held-out seeds");
        try
        {
            Parallel.ForEach(d.Cases,new ParallelOptions{MaxDegreeOfParallelism=workers,CancellationToken=Cancellation.Token},spec=>
            {
                cohortStarted=true;CaseResult row;
                try{row=Run(spec,d.Rows.Single(r=>r.Parameters.CohortId==spec.CohortId).Parameters,d.Settings,d.Camera,d.RecordSelectedInputs);}
                catch(Exception error){row=new CaseResult{Case=spec,Status="Error",Stage="unexpected-sampler-error",Error=error.ToString()};}
                observed.TryAdd(spec.Index,row);
                if(spec.Difficulty=="Master"&&row.Error!=null&&Interlocked.CompareExchange(ref firstMaster,1,0)==0)Console.WriteLine("FIRST_MASTER_CASE_FAILURE "+JsonSerializer.Serialize(new{caseId=spec.Id,row.Error,row.TerminalTime,row.PhysicalStatus,row.Collisions,row.CompletedBoosts,row.CompletedGoalCount,row.GoalCount,row.HitIds,row.MissedGoalIds},Compact));
                int done=Interlocked.Increment(ref completed);if(done%1000==0)Console.WriteLine("STUDY "+d.Id+" completed="+done+"/"+d.Cases.Length);
            });
        }
        catch(Exception error){fatal=error;}
        var verification=CheckAfterCohort(()=>ShaFile(manifestPath)==manifestHash&&hashes.All(kv=>ShaFile(Path.Combine(publication,kv.Key))==kv.Value),fatal);
        var ordered=observed.OrderBy(kv=>kv.Key).Select(kv=>kv.Value).ToArray();var unrun=d.Cases.Where(s=>!observed.ContainsKey(s.Index)).Select(s=>s.Id).ToArray();
        bool complete=verification.Stable&&verification.Fatal==null&&unrun.Length==0&&ordered.All(r=>r.Error==null);
        var report=new{status=complete?"COMPLETE_NAMED_TRAINING":"TRAINING_WITH_ERRORS",protocolId=StudyProtocol,studyId=d.Id,phase="Training",
            exploratoryOnly=true,validationAcceptance=false,heldOutSeedsRun=false,publicationStable=verification.Stable,
            assemblySha256=activeRun.AssemblyHash,manifestSha256=manifestHash,configSha256=d.ConfigHash,parameterTableSha256=d.TableHash,scheduleSha256=d.ScheduleHash,
            publishedManifest=manifest.Clone(),expectedN=d.ExpectedN,expectedCases=d.Cases.Length,actualCases=ordered.Length,unrunCaseIds=unrun,fatalError=verification.Fatal?.ToString(),
            camera=d.Camera.ToString(),humanInput="Idle",parallelism=workers,parameters=d.Rows,
            statisticalMethod="Exploratory two-sided99% Student-t mean CI, sample SD n-1, conservative t=2.601 for declared n>=200; no validation/familywise acceptance. Missing terminal observation makes metric mean/CI null. Terminal quality failures remain included.",
            selectedInputsScope=d.RecordSelectedInputs?"Initial saved decision combo only; not applied Press tape":"Not recorded",
            sampler="Same initial sampler with explicit CreateFixed settings, actual bot factory/slot1 controller, normal human readiness, human idle; stop bot terminal; no filtering/rerolls or physical-state injection.",
            cohorts=d.Rows.Select(row=>Cohort(row.Parameters,ordered.Where(r=>r.Case.CohortId==row.Parameters.CohortId).ToArray(),d.ExpectedN)),cases=ordered};
        CreateStudyOutput(output+".json",JsonSerializer.Serialize(report,Json));
        CreateStudyOutput(output+".txt","NAMED TRAINING ONLY — NOT VALIDATION ACCEPTANCE\nStudy "+d.Id+"; "+ordered.Length+"/"+d.Cases.Length+" cases; errors="+ordered.Count(r=>r.Error!=null)+"; unrun="+unrun.Length+"; publicationStable="+verification.Stable+".\nAssembly "+activeRun.AssemblyHash+"\nManifest "+manifestHash+"\nTable "+d.TableHash+"\nSchedule "+d.ScheduleHash+"\nEvery announced outcome retained. No held-out seeds, rerolls, runtime parameter changes, or automatic calibration.\n");
        Console.WriteLine("STUDY saved="+output+".json cases="+ordered.Length+" errors="+ordered.Count(r=>r.Error!=null)+" stable="+verification.Stable);return complete?0:1;
    }
}
