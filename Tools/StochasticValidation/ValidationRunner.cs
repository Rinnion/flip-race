using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlipRace.Core;

public static class ValidationEntry
{
    public static int Main(string[] args)=>Program.ValidationMain(args);
}
static partial class Program
{
    const string ValidationProtocol="BSTO-FINAL-VALIDATION-1";
    const string PreplanHash="2233FBCDF0B7E65D5AD1EFD864A74988B00BFC032B4CCDF7AF146F197FE7319D";
    const double AcceptanceTolerance=1e-9,TargetAllowance=.2;
    sealed class ValidationRow { public Parameters Parameters; public double K; public int ExpectedN; }
    sealed class ValidationDefinition
    {
        internal readonly string Id,Phase,ConfigHash,PreplanSha,TableJson,TableHash,ScheduleJson,ScheduleHash;
        internal readonly PartyCamera Camera;
        internal readonly bool RecordInputs;
        internal readonly ValidationRow[] Rows;
        internal readonly Spec[] Cases;
        internal ValidationDefinition(string id,string phase,string configHash,bool record,ValidationRow[] rows,Spec[] cases)
        {
            Id=id;Phase=phase;ConfigHash=configHash;PreplanSha=PreplanHash;RecordInputs=record;Rows=rows;Cases=cases;
            Camera=phase=="Validation"?PartyCamera.Lagger:PartyCamera.Constant;
            TableJson=JsonSerializer.Serialize(rows,Compact);TableHash=ShaText(TableJson);
            ScheduleJson=JsonSerializer.Serialize(cases,Compact);ScheduleHash=ShaText(ScheduleJson);
        }
        internal StochasticBotSettings ActualSettings(Spec spec)=>
            RaceSimulation.CreateStochasticBotRace(RaceTracks.Get(spec.TrackId),Flight(spec.ProfileId),
                Enum.Parse<BotDifficulty>(spec.Difficulty),spec.Seed,Camera).StochasticBot;
    }
    static int HoldoutN(string track,string level)=>level=="Master"?200:track==TracksRhythm.CascadeId?(level=="Novice"||level=="Amateur"?800:400):level=="Strong"?200:400;
    static ValidationDefinition ParseValidation(byte[] config,byte[] preplan)
    {
        string preplanSha=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(preplan));
        if(preplanSha!=PreplanHash)throw new ArgumentException("Predeclared plan bytes/hash changed; no held-out execution");
        using var planDoc=JsonDocument.Parse(preplan);var plan=planDoc.RootElement;
        if(plan.GetProperty("protocolId").GetString()!="BSTO-HOLDOUT-PREPLAN-2026-10-31-V2"||plan.GetProperty("heldOutExecuted").GetBoolean()||plan.GetProperty("startSeed").GetUInt32()!=1000000||plan.GetProperty("expectedCases").GetInt32()!=18000||plan.GetProperty("firstScheduledBlockPerCohort").GetInt32()!=200||plan.GetProperty("sequentialPeeking").GetBoolean()||plan.GetProperty("automaticPostInspectionExtension").GetBoolean())throw new ArgumentException("Preplan constants mismatch");
        var planned=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var row in plan.GetProperty("rows").EnumerateArray())
        {
            StudyObject(row,new[]{"cohortId","track","profile","level","count","seedFirst","seedLast"});
            string track=row.GetProperty("track").GetString(),profile=row.GetProperty("profile").GetString(),level=row.GetProperty("level").GetString(),key=track+"/"+profile+"/"+level;
            int n=HoldoutN(track,level);
            if(!TrackIds.Contains(track)||!new[]{"Chosen","FastFlip"}.Contains(profile)||!Levels.Select(x=>x.ToString()).Contains(level)||row.GetProperty("cohortId").GetString()!=key||row.GetProperty("count").GetInt32()!=n||row.GetProperty("seedFirst").GetUInt32()!=1000000||row.GetProperty("seedLast").GetUInt32()!=1000000u+n-1||!planned.TryAdd(key,n))throw new ArgumentException("Preplan row mismatch/duplicate");
        }
        if(planned.Count!=50||planned.Values.Sum()!=18000)throw new ArgumentException("Exactly50/18000 preplan required");
        using var cfgDoc=JsonDocument.Parse(config);var cfg=cfgDoc.RootElement;
        StudyObject(cfg,new[]{"studyId","phase","camera","preplanSha256"},"recordSelectedInputs");
        string id=cfg.GetProperty("studyId").GetString(),phase=cfg.GetProperty("phase").GetString(),camera=cfg.GetProperty("camera").GetString();
        if(id==null||id.Length>64||!Regex.IsMatch(id,@"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z")||id=="initial")throw new ArgumentException("Safe unique validation study ID required");
        if((phase!="Validation"&&phase!="ConstantAudit")||camera!=(phase=="Validation"?"Lagger":"Constant")||cfg.GetProperty("preplanSha256").GetString()!=PreplanHash)throw new ArgumentException("Exact phase/camera/preplan required; Training and seed/parameter overrides unsupported");
        bool record=cfg.TryGetProperty("recordSelectedInputs",out var ri)&&ri.GetBoolean();
        var rows=new List<ValidationRow>();
        foreach(string track in TrackIds)foreach(string profile in new[]{"Chosen","FastFlip"})
        {
            double? pairK=null;
            foreach(var level in Levels)
            {
                // Query actual difficulty factory, exactly as UI; metadata seed0 never advances.
                var settings=RaceSimulation.CreateStochasticBotRace(RaceTracks.Get(track),Flight(profile),level,0u,Enum.Parse<PartyCamera>(camera)).StochasticBot;
                if(settings==null||settings.Seed!=0||settings.TrackId!=track||settings.ProfileId!=profile||settings.Difficulty!=level||settings.CollectProbability!=settings.Quality||settings.BonusGain<=0||settings.CollisionCostSum<=0)throw new InvalidOperationException("Actual difficulty factory metadata/model mismatch");
                double k;
                if(level==BotDifficulty.Master)
                {
                    if(settings.CollectProbability!=1||settings.HitProbability!=0)throw new InvalidOperationException("Actual Master must be1/0");
                    k=pairK??throw new InvalidOperationException("Junior rows must precede Master");
                }
                else
                {
                    double denominator=settings.BonusGain/settings.CollisionCostSum*(1-settings.Quality);
                    k=settings.HitProbability/denominator;
                    if(!double.IsFinite(k)||k<=0||settings.HitProbability<0||settings.HitProbability>1||(pairK.HasValue&&Math.Abs(k-pairK.Value)>1e-12))throw new InvalidOperationException("Expected one positive finite shared junior k per pair; no clamp");
                    pairK??=k;
                }
                var p=ParameterRow(settings);
                rows.Add(new ValidationRow{Parameters=p,K=k,ExpectedN=phase=="Validation"?planned[p.CohortId]:200});
            }
        }
        var cases=new List<Spec>();
        foreach(var block in new[]{(Offset:0,Count:200),(Offset:200,Count:200),(Offset:400,Count:400)})
            foreach(var row in rows.Where(r=>r.ExpectedN>block.Offset))
                for(int si=0;si<block.Count;si++)
                {
                    uint seed=1000000u+(uint)(block.Offset+si);var p=row.Parameters;
                    cases.Add(new Spec{Index=cases.Count,Id=p.CohortId+"/"+seed.ToString(CultureInfo.InvariantCulture),TrackId=p.TrackId,ProfileId=p.ProfileId,Difficulty=p.Difficulty,CohortId=p.CohortId,Seed=seed});
                }
        if(cases.Count!=(phase=="Validation"?18000:10000)||cases.Select(s=>s.Id).Distinct().Count()!=cases.Count)throw new InvalidOperationException("Fixed block schedule mismatch");
        return new ValidationDefinition(id,phase,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(config)),record,rows.ToArray(),cases.ToArray());
    }
    static object ValidationDescription(ValidationDefinition d)=>new{
        protocolId=ValidationProtocol,studyId=d.Id,phase=d.Phase,camera=d.Camera.ToString(),humanInput="Idle",
        assemblySha256=ShaFile(AssemblyPath),runtimeVersion=Environment.Version.ToString(),configSha256=d.ConfigHash,preplanSha256=d.PreplanSha,
        parameterTableSha256=d.TableHash,parameterTableJson=d.TableJson,parameters=d.Rows,
        scheduleSha256=d.ScheduleHash,scheduleJson=d.ScheduleJson,expectedCases=d.Cases.Length,cohorts=d.Rows.Length,
        maximumParallelism=MaximumWorkers,fixedBeforeStart=true,automaticExtension=false,inspectBetweenBlocks=false,
        confidenceScope="Pointwise99%, not familywise",selectedInputsScope=d.RecordInputs?"Saved initial combos, not applied Press tape":"Not recorded"};
    static string TimeBand(Summary s,double target)
    {
        if(!s.Complete||!s.Mean.HasValue||!s.Lower99.HasValue||!s.Upper99.HasValue)return "INCOMPLETE";
        double half=(s.Upper99.Value-s.Lower99.Value)/2;
        if(Math.Abs(s.Mean.Value-target)+half<=TargetAllowance+AcceptanceTolerance)return "PASS";
        if(s.Upper99.Value<target-TargetAllowance-AcceptanceTolerance||s.Lower99.Value>target+TargetAllowance+AcceptanceTolerance)return "FAIL";
        return "INCONCLUSIVE";
    }
    static string ActualPattern(CaseResult r)=>JsonSerializer.Serialize(new{successful=(r.SuccessfulIds??Array.Empty<string>()).OrderBy(x=>x,StringComparer.Ordinal),hits=(r.HitIds??Array.Empty<string>()).OrderBy(x=>x,StringComparer.Ordinal),r.FullFinishCount,r.PhysicalStatus,r.Terminal},Compact);
    static string IntentPattern(CaseResult r)=>JsonSerializer.Serialize((r.Intentions??Array.Empty<Intention>()).OrderBy(i=>i.Id,StringComparer.Ordinal).Select(i=>new{i.Id,i.Intent}),Compact);
    sealed class ValidationSummary
    {
        public string CohortId,Phase,Acceptance,TimeCriterion,MasterQuality,Diversity;
        public Parameters Parameters;
        public int ExpectedCases,ActualCases,TerminalCases,EliminatedCases,ErrorCases,PartialCases,ActualPatternCount,IntentionPatternCount;
        public bool Complete;
        public Dictionary<string,Summary> Metrics;
        public object[] ActualPatterns,IntentionPatterns,IntentionVersusActual;
    }
    static ValidationSummary SummarizeValidation(ValidationDefinition d,ValidationRow declared,CaseResult[] all)
    {
        var rows=all.Where(r=>r.Case.CohortId==declared.Parameters.CohortId).ToArray();int n=declared.ExpectedN;
        var intents=rows.SelectMany(r=>r.Intentions??Array.Empty<Intention>()).ToArray();
        var actualPatterns=rows.GroupBy(ActualPattern,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal).ToArray();
        var intentionPatterns=rows.GroupBy(IntentPattern,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal).ToArray();
        var metrics=new Dictionary<string,Summary>{
            ["actualTerminalTime"]=Stats(rows.Select(r=>r.Terminal?r.TerminalTime:null),n),
            ["collisions"]=Stats(rows.Select(r=>EndCount(r,r.Collisions)),n),
            ["ordinaryBoostsEarned"]=Stats(rows.Select(r=>EndCount(r,r.BoostsEarned)),n),
            ["completedBoostPulses"]=Stats(rows.Select(r=>EndCount(r,r.CompletedBoosts)),n),
            ["fullFinishCount"]=Stats(rows.Select(r=>EndCount(r,r.FullFinishCount)),n),
            ["completedGoalCount"]=Stats(rows.Select(r=>EndCount(r,r.CompletedGoalCount)),n),
            ["unreachableCount"]=Stats(rows.Select(r=>EndCount(r,r.UnreachableCount)),n),
            ["acceptedInputs"]=Stats(rows.Select(r=>EndCount(r,r.AcceptedInputs)),n),
            ["rejectedInputs"]=Stats(rows.Select(r=>EndCount(r,r.RejectedInputs)),n),
            ["suppressedInputs"]=Stats(rows.Select(r=>EndCount(r,r.SuppressedInputs)),n)};
        bool complete=rows.Length==n&&rows.All(r=>r.Terminal);
        string quality=declared.Parameters.Difficulty!="Master"?"NOT_APPLICABLE":!complete?"INCOMPLETE":rows.Any(r=>!r.MasterInvariantSatisfied||r.Error!=null)?"FAIL":"PASS";
        string band=d.Phase=="Validation"?TimeBand(metrics["actualTerminalTime"],declared.Parameters.TargetMeanTime):"NOT_APPLICABLE_CONSTANT_AUDIT";
        string diversity=declared.Parameters.Difficulty=="Master"?"NOT_APPLICABLE":actualPatterns.Length<2?"NEEDS_REVIEW":"PASS";
        string acceptance=!complete?"INCOMPLETE":rows.Any(r=>r.Error!=null||r.RejectedInputs>0)||quality=="FAIL"||band=="FAIL"?"FAIL":band=="INCOMPLETE"||band=="INCONCLUSIVE"?band:diversity=="NEEDS_REVIEW"?"NEEDS_REVIEW":"PASS";
        return new ValidationSummary{CohortId=declared.Parameters.CohortId,Phase=d.Phase,Parameters=declared.Parameters,ExpectedCases=n,ActualCases=rows.Length,TerminalCases=rows.Count(r=>r.Terminal),EliminatedCases=rows.Count(r=>r.PhysicalStatus=="Eliminated"),ErrorCases=rows.Count(r=>r.Error!=null),PartialCases=rows.Count(r=>!r.Terminal),Complete=complete,TimeCriterion=band,MasterQuality=quality,Diversity=diversity,Acceptance=acceptance,Metrics=metrics,
            ActualPatternCount=actualPatterns.Length,IntentionPatternCount=intentionPatterns.Length,
            ActualPatterns=actualPatterns.Select(g=>(object)new{pattern=JsonDocument.Parse(g.Key).RootElement.Clone(),count=g.Count()}).ToArray(),
            IntentionPatterns=intentionPatterns.Select(g=>(object)new{pattern=JsonDocument.Parse(g.Key).RootElement.Clone(),count=g.Count()}).ToArray(),
            IntentionVersusActual=intents.GroupBy(i=>i.Kind+"/"+i.Intent).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>(object)new{key=g.Key,decisions=g.Count(),matched=g.Count(i=>i.Matched),unresolved=g.Count(i=>!i.Resolved),unmetRate=(double)g.Count(i=>!i.Matched)/g.Count(),unresolvedIncluded=true,actual=g.GroupBy(i=>i.ActualOutcome).OrderBy(a=>a.Key,StringComparer.Ordinal).Select(a=>new{outcome=a.Key,count=a.Count()})}).ToArray()};
    }
    static object[] ValidateOrder(ValidationDefinition d,ValidationSummary[] summaries)
    {
        return (from t in TrackIds from p in new[]{"Chosen","FastFlip"}
            let cohortRows=Levels.Select(l=>summaries.Single(s=>s.CohortId==t+"/"+p+"/"+l)).ToArray()
            let values=cohortRows.Select(s=>s.Metrics["actualTerminalTime"].Mean).ToArray()
            let complete=cohortRows.All(s=>s.Complete)&&values.All(v=>v.HasValue)
            select (object)new{track=t,profile=p,phase=d.Phase,means=values,
                status=d.Phase!="Validation"?"NOT_APPLICABLE_CONSTANT_AUDIT":!complete?"INCOMPLETE":Enumerable.Range(0,4).All(i=>values[i].Value>values[i+1].Value)?"PASS":"FAIL"}).ToArray();
    }
    sealed class ValidationProgress
    {
        internal ValidationDefinition Definition;
        internal ConcurrentDictionary<int,CaseResult> Results=new();
        internal string AssemblyHash,ManifestHash;
        internal bool Started;
    }
    static ValidationProgress validationProgress;
    static object ValidationRecovery(ValidationProgress p,Exception error,bool synthetic=false)
    {
        var rows=p.Results.OrderBy(kv=>kv.Key).Select(kv=>kv.Value).ToArray();var d=p.Definition;
        return new{status=p.Started?"VALIDATION_POSTRUN_ERROR":"SETUP_FAILED_NO_COHORT",protocolId=ValidationProtocol,studyId=d?.Id,phase=d?.Phase,
            syntheticFixture=synthetic,publicationStable=false,validationAcceptance=false,assemblySha256=p.AssemblyHash,manifestSha256=p.ManifestHash,
            expectedCases=d?.Cases.Length,actualCases=rows.Length,knownSchedule=d!=null,unrunCaseIds=d?.Cases.Where(s=>!p.Results.ContainsKey(s.Index)).Select(s=>s.Id).ToArray(),fatalError=error.ToString(),cases=rows};
    }
    static string ValidationClaimName(string phase)=>PreplanHash+"-"+phase+".claim";
    static string ValidationConfig(string id,string phase,string camera)=>JsonSerializer.Serialize(new{studyId=id,phase,camera,preplanSha256=PreplanHash},Compact);
    static int ValidationSelfCheck(byte[] preplan)
    {
        int checks=0;void Check(bool ok){checks++;if(!ok)throw new InvalidOperationException("Validation self-check"+checks+" failed");}
        ValidationDefinition Parse(string cfg,byte[] pp=null)=>ParseValidation(Encoding.UTF8.GetBytes(cfg),pp??preplan);
        void Reject(string cfg,byte[] pp=null){bool rejected=false;try{Parse(cfg,pp);}catch(ArgumentException){rejected=true;}catch(JsonException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected);}
        var v=Parse(ValidationConfig("synthetic-validation","Validation","Lagger"));var c=Parse(ValidationConfig("synthetic-constant","ConstantAudit","Constant"));
        Check(v.Rows.Length==50&&v.Cases.Length==18000&&c.Rows.Length==50&&c.Cases.Length==10000);
        Check(v.Rows.Count(r=>r.ExpectedN==800)==4&&v.Rows.Count(r=>r.ExpectedN==400)==28&&v.Rows.Count(r=>r.ExpectedN==200)==18);
        Check(v.Cases.Take(10000).Select(s=>s.Id).SequenceEqual(c.Cases.Select(s=>s.Id)));
        Check(v.Cases.All(s=>s.Seed>=1000000&&s.Seed<=1000799)&&v.Cases.Select(s=>s.Id).Distinct().Count()==18000);
        Check(v.Cases.Take(10000).All(s=>s.Seed<1000200)&&v.Cases.Skip(10000).Take(6400).All(s=>s.Seed>=1000200&&s.Seed<1000400)&&v.Cases.Skip(16400).All(s=>s.Seed>=1000400));
        Check(v.Cases.Select((s,i)=>s.Index==i).All(x=>x));
        Reject(ValidationConfig("bad","Training","Lagger"));Reject(ValidationConfig("../bad","Validation","Lagger"));
        Reject(ValidationConfig("bad","Validation","Constant"));Reject(ValidationConfig("bad","ConstantAudit","Lagger"));
        Reject(ValidationConfig("bad","Validation","Lagger").Replace(PreplanHash,"BAD"));
        Reject(ValidationConfig("bad","Validation","Lagger").Replace("\"camera\":\"Lagger\"","\"camera\":\"Lagger\",\"camera\":\"Lagger\""));
        Reject(ValidationConfig("bad","Validation","Lagger").Replace("}",",\"startSeed\":0}"));
        Reject(ValidationConfig("bad","Validation","Lagger").Replace("}",",\"count\":200}"));
        var changed=(byte[])preplan.Clone();changed[0]=32;Reject(ValidationConfig("bad","Validation","Lagger"),changed);
        foreach(var n in new[]{200,400,800})
        {
            CheckSuppressionTelemetry(n);checks++;
            var time=Stats(Enumerable.Repeat((double?)33,n),n);Check(time.Complete&&time.Mean==33&&TimeBand(time,33)=="PASS");
            Check(!Stats(Enumerable.Repeat((double?)33,n-1),n).Mean.HasValue);
            var qualityRows=Enumerable.Range(0,n).Select(i=>new CaseResult{Terminal=true,TerminalTime=33,Collisions=2,Error="synthetic terminal quality failure"}).ToArray();
            Check(Stats(qualityRows.Select(r=>EndCount(r,r.Collisions)),n).Mean==2);
            qualityRows[0].Terminal=false;Check(!Stats(qualityRows.Select(r=>EndCount(r,r.Collisions)),n).Complete);
        }
        Check(TimeBand(new Summary{Complete=true,Mean=33.1,Lower99=33.0,Upper99=33.2},33)=="PASS");
        Check(TimeBand(new Summary{Complete=true,Mean=33.1,Lower99=32.9,Upper99=33.3},33)=="INCONCLUSIVE");
        Check(TimeBand(new Summary{Complete=true,Mean=33.5,Lower99=33.4,Upper99=33.6},33)=="FAIL");
        // Rejected inputs are an acceptance failure in both phases, independently of time/quality.
        foreach(var definition in new[]{v,c})
        {
            var row=definition.Rows.First(r=>r.Parameters.Difficulty=="Master");
            var specs=definition.Cases.Where(s=>s.CohortId==row.Parameters.CohortId).ToArray();
            var clean=specs.Select(s=>new CaseResult{Case=s,Terminal=true,TerminalTime=row.Parameters.TargetMeanTime,PhysicalStatus="Finished",MasterInvariantSatisfied=true,FullFinishCount=1}).ToArray();
            foreach(var result in clean)result.SuppressedInputs=3;
            var suppressionSummary=SummarizeValidation(definition,row,clean);
            Check(suppressionSummary.Acceptance=="PASS"&&suppressionSummary.Metrics["suppressedInputs"].Mean==3&&suppressionSummary.Metrics["rejectedInputs"].Mean==0&&suppressionSummary.Metrics["acceptedInputs"].Mean==0);
            clean[0].Terminal=false;
            var partialSuppression=SummarizeValidation(definition,row,clean).Metrics["suppressedInputs"];
            Check(!partialSuppression.Complete&&!partialSuppression.Mean.HasValue&&!partialSuppression.Lower99.HasValue);
            clean[0].Terminal=true;
            clean[0].RejectedInputs=1;
            Check(SummarizeValidation(definition,row,clean).Acceptance=="FAIL");
        }
        Check(ValidationClaimName(v.Phase)==ValidationClaimName(Parse(ValidationConfig("different-id","Validation","Lagger")).Phase));
        Check(ValidationClaimName(v.Phase)!=ValidationClaimName(c.Phase));
        // Only seed0 metadata factory/clone fixtures. No Press, Advance, or controller.
        foreach(var row in v.Rows)
        {
            var p=row.Parameters;var game=RaceSimulation.CreateStochasticBotRace(RaceTracks.Get(p.TrackId),Flight(p.ProfileId),Enum.Parse<BotDifficulty>(p.Difficulty),0u,PartyCamera.Lagger);
            Check(game.IsBot&&game.StochasticBot.Seed==0&&JsonSerializer.Serialize(ParameterRow(game.StochasticBot),Compact)==JsonSerializer.Serialize(p,Compact));
            var clone=RaceSnapshot.Clone(game);Check(ReferenceEquals(game.StochasticBot,clone.StochasticBot)&&!ReferenceEquals(game.Players[1],clone.Players[1]));
            clone.Players[1].HitObstacleIds.Add("synthetic-no-physics");Check(!game.Players[1].HitObstacleIds.Contains("synthetic-no-physics")&&!ReferenceEquals(game.Players[1].PendingEffects,clone.Players[1].PendingEffects));
        }
        var recovery=new ValidationProgress{Definition=v,Started=true,AssemblyHash="synthetic",ManifestHash="synthetic"};
        recovery.Results.TryAdd(0,new CaseResult{Case=v.Cases[0],Terminal=true,TerminalTime=33,Error="synthetic quality failure"});
        recovery.Results.TryAdd(1,new CaseResult{Case=v.Cases[1],Terminal=false,ActualElapsed=7,Error="synthetic partial"});
        var fault=CheckAfterCohort(()=>throw new FileNotFoundException("synthetic postrun manifest loss"),null);
        using(var j=JsonDocument.Parse(JsonSerializer.Serialize(ValidationRecovery(recovery,fault.Fatal,true),Json)))
            Check(!fault.Stable&&j.RootElement.GetProperty("actualCases").GetInt32()==2&&j.RootElement.GetProperty("unrunCaseIds").GetArrayLength()==17998&&j.RootElement.GetProperty("status").GetString()=="VALIDATION_POSTRUN_ERROR");
        var physical=new CaseResult{Terminal=true,PhysicalStatus="Finished",FullFinishCount=1,SuccessfulIds=new[]{"b","a"},HitIds=Array.Empty<string>()};
        var physical2=new CaseResult{Terminal=true,PhysicalStatus="Finished",FullFinishCount=1,SuccessfulIds=new[]{"a","b"},HitIds=Array.Empty<string>()};
        Check(ActualPattern(physical)==ActualPattern(physical2));physical2.HitIds=new[]{"hit"};Check(ActualPattern(physical)!=ActualPattern(physical2));
        Console.WriteLine("VALIDATION SELF-CHECK PASS "+checks+" metadata/synthetic checks. No Press/Advance/controller/publication/physical race/held-out seed executed.");
        Console.WriteLine("Validation schedule "+v.ScheduleHash+" cases18000; Constant schedule "+c.ScheduleHash+" cases10000.");return 0;
    }
    public static int ValidationMain(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;CultureInfo.CurrentUICulture=CultureInfo.InvariantCulture;
        try
        {
            if(args.Length==2&&args[0]=="--self-check-validation")return ValidationSelfCheck(File.ReadAllBytes(args[1]));
            if(args.Length==3&&args[0]=="--describe-validation")
            {
                Console.WriteLine(JsonSerializer.Serialize(ValidationDescription(ParseValidation(File.ReadAllBytes(args[1]),File.ReadAllBytes(args[2]))),Json));return 0;
            }
            return ExecuteValidation(args);
        }
        catch(Exception error)
        {
            var p=validationProgress??new ValidationProgress();string directory=Path.Combine(Directory.GetCurrentDirectory(),"Tools/StochasticValidation/Recovery");
            try{Directory.CreateDirectory(directory);string path=Path.Combine(directory,"failure-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".json");CreateStudyOutput(path,JsonSerializer.Serialize(ValidationRecovery(p,error),Json));Console.Error.WriteLine("Failure report "+path+"; actualCases="+p.Results.Count);}
            catch(Exception writeError){Console.Error.WriteLine("Recovery write failed; retained in memory="+p.Results.Count+"; "+writeError);}
            Console.Error.WriteLine(error);return 1;
        }
    }
    static int ExecuteValidation(string[] args)
    {
        if(args.Length!=5||args[0]!="--validation"||args[1]!="--study-id"||args[3]!="--parallelism"||!int.TryParse(args[4],out int workers)||workers<1||workers>MaximumWorkers)throw new ArgumentException("Only --validation --study-id <frozen-id> --parallelism1..3; no Training/seed/count/parameter overrides");
        string publication=AppContext.BaseDirectory,manifestPath=Path.Combine(publication,"validation-manifest.json"),manifestHash=ShaFile(manifestPath),assemblyHash=ShaFile(AssemblyPath);
        using var doc=JsonDocument.Parse(File.ReadAllBytes(manifestPath));var m=doc.RootElement;
        if(m.GetProperty("protocolId").GetString()!=ValidationProtocol||m.GetProperty("studyId").GetString()!=args[2]||m.GetProperty("assemblySha256").GetString()!=assemblyHash)throw new InvalidOperationException("Frozen validation publication required");
        var hashes=VerifyPublication(m,publication);string cfg=m.GetProperty("configPath").GetString(),plan=m.GetProperty("preplanPath").GetString();
        if(!hashes.ContainsKey(cfg)||!hashes.ContainsKey(plan))throw new InvalidOperationException("Config/preplan must be hashed published files");
        var d=ParseValidation(File.ReadAllBytes(Path.Combine(publication,cfg)),File.ReadAllBytes(Path.Combine(publication,plan)));
        var progress=new ValidationProgress{Definition=d,AssemblyHash=assemblyHash,ManifestHash=manifestHash};validationProgress=progress;
        if(d.Id!=args[2]||d.Phase!=m.GetProperty("phase").GetString()||d.ConfigHash!=m.GetProperty("configSha256").GetString()||d.PreplanSha!=m.GetProperty("preplanSha256").GetString()||d.TableHash!=m.GetProperty("parameterTableSha256").GetString()||d.ScheduleHash!=m.GetProperty("scheduleSha256").GetString()||d.Cases.Length!=m.GetProperty("expectedCases").GetInt32()||d.Camera.ToString()!=m.GetProperty("camera").GetString()||m.GetProperty("maximumParallelism").GetInt32()!=3)throw new InvalidOperationException("Frozen config/table/preplan/schedule mismatch");
        string repository=m.GetProperty("repositoryRoot").GetString(),output=Path.Combine(repository,"Verification/stochastic-bots-validation-"+d.Id);
        if(File.Exists(output+".json")||File.Exists(output+".txt"))throw new IOException("Validation outputs already exist; no overwrite/retry");
        string claims=Path.Combine(repository,"Tools/StochasticValidation/Recovery");Directory.CreateDirectory(claims);
        CreateStudyOutput(Path.Combine(claims,ValidationClaimName(d.Phase)),JsonSerializer.Serialize(new{preplanSha256=PreplanHash,phase=d.Phase,studyId=d.Id,manifestSha256=manifestHash,scheduleSha256=d.ScheduleHash},Json));
        CreateStudyOutput(Path.Combine(claims,d.Id+".claim"),JsonSerializer.Serialize(new{studyId=d.Id,manifestSha256=manifestHash,scheduleSha256=d.ScheduleHash},Json));
        Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;Cancellation.Cancel();};int completed=0,firstMaster=0;Exception fatal=null;
        Console.WriteLine("PREDECLARED "+d.Phase+" "+d.Id+" cases="+d.Cases.Length+" assembly="+assemblyHash+" manifest="+manifestHash+" table="+d.TableHash+" schedule="+d.ScheduleHash);
        try
        {
            // Complete each predefined block without examining results or changing eligibility.
            foreach(var block in new[]{(Start:1000000u,End:1000200u),(Start:1000200u,End:1000400u),(Start:1000400u,End:1000800u)})
                Parallel.ForEach(d.Cases.Where(s=>s.Seed>=block.Start&&s.Seed<block.End),new ParallelOptions{MaxDegreeOfParallelism=workers,CancellationToken=Cancellation.Token},s=>
                {
                    progress.Started=true;CaseResult r;
                    try{r=Run(s,d.Rows.Single(row=>row.Parameters.CohortId==s.CohortId).Parameters,d.ActualSettings,d.Camera,d.RecordInputs);}
                    catch(Exception error){r=new CaseResult{Case=s,Status="Error",Stage="unexpected-sampler-error",Error=error.ToString()};}
                    progress.Results.TryAdd(s.Index,r);
                    if(s.Difficulty=="Master"&&r.Error!=null&&Interlocked.CompareExchange(ref firstMaster,1,0)==0)Console.WriteLine("FIRST_MASTER_CASE_FAILURE "+JsonSerializer.Serialize(new{caseId=s.Id,r.Error,r.TerminalTime,r.PhysicalStatus,r.Collisions,r.CompletedBoosts,r.CompletedGoalCount,r.GoalCount},Compact));
                    int done=Interlocked.Increment(ref completed);if(done%1000==0)Console.WriteLine(d.Phase+" completed="+done+"/"+d.Cases.Length);
                });
        }
        catch(Exception error){fatal=error;}
        var verification=CheckAfterCohort(()=>ShaFile(manifestPath)==manifestHash&&hashes.All(kv=>ShaFile(Path.Combine(publication,kv.Key))==kv.Value),fatal);
        var ordered=progress.Results.OrderBy(kv=>kv.Key).Select(kv=>kv.Value).ToArray();var unrun=d.Cases.Where(s=>!progress.Results.ContainsKey(s.Index)).Select(s=>s.Id).ToArray();
        var summaries=d.Rows.Select(row=>SummarizeValidation(d,row,ordered)).ToArray();var order=ValidateOrder(d,summaries);
        bool orderedPass=d.Phase!="Validation"||order.All(o=>JsonSerializer.SerializeToElement(o,Compact).GetProperty("status").GetString()=="PASS");
        string acceptance=!verification.Stable||verification.Fatal!=null||unrun.Length>0?"INCOMPLETE":summaries.Any(s=>s.Acceptance=="FAIL")||!orderedPass?"FAIL":summaries.Any(s=>s.Acceptance!="PASS")?"NEEDS_REVIEW":"PASS";
        var report=new{status=acceptance,protocolId=ValidationProtocol,studyId=d.Id,phase=d.Phase,camera=d.Camera.ToString(),validationAcceptance=d.Phase=="Validation"&&acceptance=="PASS",constantAuditAcceptance=d.Phase=="ConstantAudit"&&acceptance=="PASS",
            confidenceScope="Pointwise99% per cohort; no familywise claim; fixed predeclared N, no extension or refit",
            pairedCameraScope="Constant shares first held-out seeds; no independence claim across cameras; no Lagger time-target criterion for Constant",
            toleranceSeconds=AcceptanceTolerance,targetAllowanceSeconds=TargetAllowance,tCritical99=TCritical99,
            assemblySha256=assemblyHash,manifestSha256=manifestHash,configSha256=d.ConfigHash,preplanSha256=d.PreplanSha,parameterTableSha256=d.TableHash,scheduleSha256=d.ScheduleHash,
            publicationStable=verification.Stable,publishedManifest=m.Clone(),expectedCases=d.Cases.Length,actualCases=ordered.Length,unrunCaseIds=unrun,fatalError=verification.Fatal?.ToString(),parallelism=workers,humanInput="Idle",
            selectedInputsScope=d.RecordInputs?"Saved initial decision combos, not applied Press tapes":"Not recorded",cohorts=summaries,adjacentMeanOrder=order,cases=ordered};
        CreateStudyOutput(output+".json",JsonSerializer.Serialize(report,Json));
        CreateStudyOutput(output+".txt",d.Phase+" "+d.Id+" "+acceptance+"\n"+ordered.Length+"/"+d.Cases.Length+" cases; errors="+ordered.Count(r=>r.Error!=null)+"; unrun="+unrun.Length+"; publicationStable="+verification.Stable+"\nAssembly "+assemblyHash+"\nManifest "+manifestHash+"\nAll outcomes/errors/eliminations retained. Fixed predeclared schedule, no filtering/rerolls/extensions/refit. See JSON for pointwise CI, quality/order/diversity and all raw facts.\n");
        Console.WriteLine("SAVED "+output+".json "+acceptance);return acceptance=="PASS"?0:1;
    }
}
