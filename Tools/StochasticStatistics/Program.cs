using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlipRace.Core;

static partial class Program
{
    const int PerCohort = 200, MaximumWorkers = 3, MaximumAdvanceCalls = 30000;
    const double FrameSeconds = 1d / 60, MaximumVirtualSeconds = 150, TCritical99 = 2.601;
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly JsonSerializerOptions Compact = new() { IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly string[] TrackIds = { TracksFull.LongId, TracksFull.ShortId, TracksFull.FlowId, TracksRhythm.CascadeId, TracksMixed.Id };
    static readonly BotDifficulty[] Levels = { BotDifficulty.Novice, BotDifficulty.Amateur, BotDifficulty.Confident, BotDifficulty.Strong, BotDifficulty.Master };
    static readonly CancellationTokenSource Cancellation = new();
    sealed class RunProgress
    {
        internal Spec[] Schedule;
        internal ConcurrentDictionary<int,CaseResult> Results;
        internal string AssemblyHash, ManifestHash, ParameterHash, ScheduleHash;
        internal string CohortTag="TRAINING_INITIAL", RecoveryPrefix="initial-training";
        internal bool SyntheticFixture;
    }
    static RunProgress activeRun;
    static volatile bool cohortStarted;
    static (bool Stable, Exception Fatal) CheckAfterCohort(Func<bool> verify, Exception priorFatal)
    {
        try
        {
            bool stable = verify();
            return stable ? (true, priorFatal) : (false, priorFatal == null ? new IOException("Published files changed after cohort") : new AggregateException(priorFatal, new IOException("Published files changed after cohort")));
        }
        catch (Exception error)
        {
            var failure = new IOException("Post-run publication verification failed; accumulated outcomes are retained", error);
            return (false, priorFatal == null ? failure : new AggregateException(priorFatal, failure));
        }
    }
    static object RecoveryReport(RunProgress run, Exception error)
    {
        var observed = run.Results.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
        return new { status = "TRAINING_WITH_POSTRUN_ERROR", cohort = run.CohortTag, exploratoryOnly = true, validationAcceptance = false, heldOutSeedsRun = false,
            syntheticFixture = run.SyntheticFixture, publicationStable = false, assemblySha256 = run.AssemblyHash, manifestSha256 = run.ManifestHash, parameterTableSha256 = run.ParameterHash, scheduleSha256 = run.ScheduleHash,
            expectedCases = run.Schedule.Length, actualCases = observed.Length, unrunCaseIds = run.Schedule.Where(s => !run.Results.ContainsKey(s.Index)).Select(s => s.Id).ToArray(), fatalError = error.ToString(), cases = observed };
    }
    static string ShaFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static string ShaText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static string AssemblyPath => Assembly.GetExecutingAssembly().Location;
    static FlightSettings Flight(string profile) => profile == "Chosen" ? FlightPresets.Chosen : profile == "FastFlip" ? FlightPresets.FastFlip : throw new ArgumentException("Exact preset required");
    sealed class Parameters
    {
        public string CohortId, TrackId, ProfileId, Difficulty;
        public double Quality, CollectProbability, HitProbability, BaseTime, MasterTime, BonusGain, CollisionCostSum, TargetMeanTime;
    }
    static Parameters ParameterRow(StochasticBotSettings s) => new() { CohortId = s.TrackId + "/" + s.ProfileId + "/" + s.Difficulty, TrackId = s.TrackId, ProfileId = s.ProfileId, Difficulty = s.Difficulty.ToString(), Quality = s.Quality, CollectProbability = s.CollectProbability, HitProbability = s.HitProbability, BaseTime = s.BaseTime, MasterTime = s.MasterTime, BonusGain = s.BonusGain, CollisionCostSum = s.CollisionCostSum, TargetMeanTime = s.BaseTime - s.BonusGain * (2 * s.Quality - 1) };
    static Parameters[] InitialRows() => (from trackId in TrackIds from profile in new[] { "Chosen", "FastFlip" } from level in Levels select ParameterRow(StochasticBotSettings.CreateInitial(RaceTracks.Get(trackId), Flight(profile), level, 0u))).ToArray();
    sealed class Spec
    {
        public int Index; public string Id, TrackId, ProfileId, Difficulty, CohortId; public uint Seed;
    }
    static Spec[] Schedule() => (from ti in Enumerable.Range(0, TrackIds.Length) from pi in Enumerable.Range(0, 2) from li in Enumerable.Range(0, Levels.Length) from si in Enumerable.Range(0, PerCohort) let track = TrackIds[ti] let profile = pi == 0 ? "Chosen" : "FastFlip" let level = Levels[li] select new Spec { Index = ((ti * 2 + pi) * 5 + li) * PerCohort + si, TrackId = track, ProfileId = profile, Difficulty = level.ToString(), CohortId = track + "/" + profile + "/" + level, Seed = (uint)si, Id = track + "/" + profile + "/" + level + "/" + si.ToString(CultureInfo.InvariantCulture) }).ToArray();
    sealed class Intention
    {
        public string Id, Kind, Intent, ControllerOutcome, ActualOutcome, UnreachableReason;
        public double Probability, Roll, AtDistance, AtTime, HorizonEndDistance;
        public bool Resolved, Matched; public int InitiallySelectedInputCount;
        [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public double[] SelectedInputs;
    }
    sealed class CaseResult
    {
        public Spec Case; public Parameters Parameters; public string Status = "NotStarted", Error, Stage, PhysicalStatus;
        public double? TerminalTime, ActualElapsed, Distance, FinishBoostElapsed, FinishBoostDistanceGained, BoostElapsed, SlowElapsed, RecoveryElapsed;
        public int? Collisions, CompletedRecoveries, BoostsEarned, CompletedBoosts, CompletedSlows, SlowsEarned, FinishBoostsEarned, CoinsEarned, FullFinishCount, CompletedGoalCount, GoalCount, UnreachableCount;
        public bool Terminal, MasterInvariantSatisfied; public int AdvanceCalls, AcceptedInputs, RejectedInputs, SuppressedInputs, PlanningCalls, CandidateSimulations, ForecastSteps;
        public string[] SuccessfulIds, CollectedIds, FailedIds, HitIds, NegativeIds, CompletedGoalIds, MissedGoalIds;
        public object ActiveEffect; public object[] PendingEffects; public Intention[] Intentions;
    }
    static void ObserveInput(CaseResult row,string kind,bool accepted)
    {
        if(kind=="suppressed")row.SuppressedInputs++;
        else if(kind=="press"){if(accepted)row.AcceptedInputs++;else row.RejectedInputs++;}
    }
    static void CheckSuppressionTelemetry(int n)
    {
        var sample=new CaseResult{Terminal=true};
        ObserveInput(sample,"advance",false);
        ObserveInput(sample,"suppressed",false);ObserveInput(sample,"suppressed",false);
        ObserveInput(sample,"press",true);ObserveInput(sample,"press",false);
        if(sample.SuppressedInputs!=2||sample.AcceptedInputs!=1||sample.RejectedInputs!=1)throw new InvalidOperationException("Suppression observer accounting merged distinct event kinds");
        using(var raw=JsonDocument.Parse(JsonSerializer.Serialize(sample,Json)))
            if(raw.RootElement.GetProperty("suppressedInputs").GetInt32()!=2||raw.RootElement.GetProperty("acceptedInputs").GetInt32()!=1||raw.RootElement.GetProperty("rejectedInputs").GetInt32()!=1)throw new InvalidOperationException("Suppression raw export lost a separate counter");
        var rows=Enumerable.Range(0,n).Select(i=>new CaseResult{Terminal=true,SuppressedInputs=2,AcceptedInputs=1,RejectedInputs=1,Error=i==0?"synthetic terminal quality failure":null}).ToArray();
        var parameters=new Parameters{CohortId="synthetic-suppression",Difficulty="Novice"};
        using(var full=JsonDocument.Parse(JsonSerializer.Serialize(Cohort(parameters,rows,n),Json)))
        {
            var metrics=full.RootElement.GetProperty("metrics");
            if(metrics.GetProperty("suppressedInputs").GetProperty("mean").GetDouble()!=2||metrics.GetProperty("acceptedInputs").GetProperty("mean").GetDouble()!=1||metrics.GetProperty("rejectedInputs").GetProperty("mean").GetDouble()!=1)throw new InvalidOperationException("Suppression terminal aggregation or quality-failure retention failed");
        }
        rows[0].Terminal=false;
        using(var partial=JsonDocument.Parse(JsonSerializer.Serialize(Cohort(parameters,rows,n),Json)))
            foreach(string metric in new[]{"suppressedInputs","acceptedInputs","rejectedInputs"})
            {
                var m=partial.RootElement.GetProperty("metrics").GetProperty(metric);
                if(m.GetProperty("complete").GetBoolean()||m.GetProperty("mean").ValueKind!=JsonValueKind.Null||m.GetProperty("lower99").ValueKind!=JsonValueKind.Null)throw new InvalidOperationException("Partial input counts entered terminal cohort statistics");
            }
    }

    static object Effect(SpeedEffect effect) => effect == null ? null : new { effect.RingId, effect.IsNegative, effect.Remaining, effect.Elapsed, effect.PulseCount, effect.CompletedPulses };
    static CaseResult Run(Spec spec, Parameters expected, Func<Spec,StochasticBotSettings> settingsFactory=null, PartyCamera camera=PartyCamera.Lagger, bool recordSelectedInputs=false)
    {
        var r = new CaseResult { Case = spec }; GameState game = null; StochasticBotController controller = null;
        try
        {
            r.Stage = "settings"; Track track = RaceTracks.Get(spec.TrackId); FlightSettings flight = Flight(spec.ProfileId);
            var settings = settingsFactory == null ? StochasticBotSettings.CreateInitial(track, flight, Enum.Parse<BotDifficulty>(spec.Difficulty), spec.Seed) : settingsFactory(spec); r.Parameters = ParameterRow(settings);
            if (JsonSerializer.Serialize(r.Parameters, Compact) != JsonSerializer.Serialize(expected, Compact)) throw new InvalidOperationException("Initial settings differ from the table frozen before cohort");
            if (settings.Seed != spec.Seed) throw new InvalidOperationException("Settings seed mismatch");
            r.Stage = "factory"; game = RaceSimulation.CreateStochasticBotRace(track, flight, settings, camera);
            if (!game.IsBot || game.BotPlan != null || game.StochasticBot == null || game.Players.Length != 2 || !game.Players[1].Ready || game.Players[0].Ready) throw new InvalidOperationException("Actual bot factory classification/readiness mismatch");
            var original = game.Players[1]; r.Stage = "ready";
            if (!RaceSimulation.Press(game, 0) || ReferenceEquals(original, game.Players[1])) throw new InvalidOperationException("Ordinary human start must replace ready participants");
            controller = new StochasticBotController(game, 1);
            if (controller.Settings.Seed != spec.Seed || JsonSerializer.Serialize(ParameterRow(controller.Settings), Compact) != JsonSerializer.Serialize(expected, Compact)) throw new InvalidOperationException("Actual controller did not retain the published immutable settings");
            controller.Observer = slice => ObserveInput(r,slice.Kind,slice.Accepted);
            r.Stage = "running";
            while (game.Players[1].Status == PlayerStatus.Running)
            {
                Cancellation.Token.ThrowIfCancellationRequested();
                if (r.AdvanceCalls >= MaximumAdvanceCalls || game.Elapsed >= MaximumVirtualSeconds) throw new InvalidOperationException("Declared virtual/advance guard exceeded; no seed reroll");
                if (game.Phase != "countdown" && game.Phase != "racing") throw new InvalidOperationException("Bot remains Running in non-active world phase");
                controller.Advance(FrameSeconds); r.AdvanceCalls++;
            }
            r.Stage = "terminal"; r.Status = "Complete";
        }
        catch (Exception e) { r.Status = e is OperationCanceledException ? "Cancelled" : "Error"; r.Error = e.ToString(); }
        finally
        {
            if (game != null)
            {
                PlayerState p = game.Players[1]; Track track = game.Track; r.PhysicalStatus = p.Status.ToString(); r.Terminal = p.Status != PlayerStatus.Running;
                r.TerminalTime = p.TerminalTime; r.ActualElapsed = game.Elapsed; r.Distance = p.Distance;
                r.Collisions = p.Collisions; r.CompletedRecoveries = p.CompletedRecoveries; r.RecoveryElapsed = p.RecoveryElapsed;
                r.BoostsEarned = p.BoostsEarned; r.CompletedBoosts = p.CompletedBoosts; r.BoostElapsed = p.BoostElapsed; r.CompletedSlows = p.CompletedSlows; r.SlowsEarned = p.SlowsEarned; r.SlowElapsed = p.SlowElapsed;
                r.FinishBoostsEarned = p.FinishBoostsEarned; r.FinishBoostElapsed = p.FinishBoostElapsed; r.FinishBoostDistanceGained = p.FinishBoostDistanceGained; r.FullFinishCount = p.FinishBoostsEarned > 0 && p.FinishBoostElapsed >= RaceConfig.FinishBoostDuration - 1e-7 ? 1 : 0; r.CoinsEarned = p.CoinsEarned;
                r.SuccessfulIds = p.SuccessfulRiskIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(); r.CollectedIds = p.CollectedRiskIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(); r.FailedIds = p.FailedRiskIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(); r.HitIds = p.HitObstacleIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(); r.NegativeIds = p.NegativeRiskIds.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                r.ActiveEffect = Effect(p.ActiveEffect); r.PendingEffects = p.PendingEffects.Select(Effect).ToArray();
                var map = StochasticTrackMap.Create(track, game.Flight); var goals = map.Opportunities; var completed = new List<string>();
                foreach (var item in goals)
                {
                    string rawId = item.Id.Substring(item.Id.IndexOf(':') + 1);
                    if (item.Kind == BotOpportunityKind.Obstacle) { if (!p.HitObstacleIds.Contains(item.ObstacleId) && track.Obstacles.Where(o => o.Id == item.ObstacleId).All(o => p.Distance >= ObstacleGeometry.Right(o) + RaceConfig.PlayerRadius - 1e-7)) completed.Add(item.Id); }
                    else if (item.Kind == BotOpportunityKind.Finish) { if (r.FullFinishCount == 1) completed.Add(item.Id); }
                    else if (p.SuccessfulRiskIds.Contains(rawId)) completed.Add(item.Id);
                }
                r.CompletedGoalIds = completed.OrderBy(x => x, StringComparer.Ordinal).ToArray(); r.MissedGoalIds = goals.Select(o => o.Id).Except(completed).OrderBy(x => x, StringComparer.Ordinal).ToArray(); r.CompletedGoalCount = completed.Count; r.GoalCount = goals.Length;
                if (controller != null)
                {
                    r.PlanningCalls = controller.PlanningCalls; r.CandidateSimulations = controller.CandidateSimulations; r.ForecastSteps = controller.ForecastSteps;
                    r.Intentions = controller.Decisions.Select(d =>
                    {
                        var item = goals.Single(o => o.Id == d.Id); string raw = d.Id.Substring(d.Id.IndexOf(':') + 1); string actual;
                        if (item.Kind == BotOpportunityKind.Obstacle) actual = p.HitObstacleIds.Contains(item.ObstacleId) ? "hit" : completed.Contains(d.Id) ? "avoided" : "unreached";
                        else if (item.Kind == BotOpportunityKind.Finish) actual = r.FullFinishCount == 1 ? "collected" : p.FinishBoostsEarned > 0 ? "finish-contact-only" : p.Status == PlayerStatus.Finished || p.Distance > item.EndPosition ? "missed" : "unreached";
                        else actual = p.SuccessfulRiskIds.Contains(raw) ? "collected" : p.FailedRiskIds.Contains(raw) ? "failed" : p.Status == PlayerStatus.Finished || p.Distance > item.EndPosition ? "missed" : "unreached";
                        bool matched = d.Intent == "hit" ? actual == "hit" : d.Intent == "avoid" ? actual == "avoided" : d.Intent == "collect" ? actual == "collected" : actual == "missed" || actual == "failed";
                        return new Intention { Id = d.Id, Kind = d.Kind.ToString(), Intent = d.Intent, Probability = d.Probability, Roll = d.Roll, AtDistance = d.AtDistance, AtTime = d.AtTime, HorizonEndDistance = d.HorizonEndDistance, ControllerOutcome = d.Outcome, ActualOutcome = actual, UnreachableReason = d.UnreachableReason, Resolved = actual != "unreached", Matched = matched, InitiallySelectedInputCount = d.SelectedInputs.Length, SelectedInputs = recordSelectedInputs ? d.SelectedInputs : null };
                    }).ToArray();
                    r.UnreachableCount = r.Intentions.Count(i => !string.IsNullOrEmpty(i.UnreachableReason));
                }
                r.MasterInvariantSatisfied = spec.Difficulty != "Master" || (r.Terminal && p.Status == PlayerStatus.Finished && p.Collisions == 0 && completed.Count == goals.Length && p.CompletedBoosts == 9 && p.ActiveEffect == null && p.PendingEffects.Count == 0 && p.TerminalTime.HasValue && Math.Abs(p.TerminalTime.Value - map.MasterTime) < 2e-7);
                if (!r.MasterInvariantSatisfied && r.Error == null) { r.Status = "Error"; r.Error = "Actual Master invariant violated (terminal facts retained without filtering or reroll)"; }
            }
        }
        return r;
    }
    sealed class Summary
    {
        public int Expected, Observed; public bool Complete; public double? Mean, SampleStandardDeviation, Minimum, Maximum, Lower99, Upper99;
    }
    static Summary Stats(IEnumerable<double?> source, int expectedN=PerCohort)
    {
        if(expectedN<200) throw new ArgumentOutOfRangeException(nameof(expectedN),"At least200 observations must be declared");
        var values = source.ToArray(); var known = values.Where(v => v.HasValue && double.IsFinite(v.Value)).Select(v => v.Value).ToArray();
        var s = new Summary { Expected = expectedN, Observed = known.Length, Complete = values.Length == expectedN && known.Length == expectedN };
        if (!s.Complete) return s;
        double mean = known.Average(), variance = known.Sum(x => (x - mean) * (x - mean)) / (expectedN - 1), sd = Math.Sqrt(variance), half = TCritical99 * sd / Math.Sqrt(expectedN);
        s.Mean = mean; s.SampleStandardDeviation = sd; s.Minimum = known.Min(); s.Maximum = known.Max(); s.Lower99 = mean - half; s.Upper99 = mean + half; return s;
    }
    static double? EndCount(CaseResult row, int? value) => row.Terminal ? (double?)value : null;
    static object Cohort(Parameters parameters, CaseResult[] rows, int expectedN=PerCohort)
    {
        var intentions = rows.SelectMany(r => r.Intentions ?? Array.Empty<Intention>()).ToArray();
        return new { parameters, expectedCases = expectedN, observedCases = rows.Length, terminalCases = rows.Count(r => r.Terminal), terminalMetricsComplete = rows.Length == expectedN && rows.All(r => r.Terminal), errors = rows.Count(r => r.Error != null), terminalCounts = rows.GroupBy(r => r.PhysicalStatus ?? "NoPhysicalState").Select(g => new { status = g.Key, count = g.Count() }),
            metrics = new Dictionary<string, Summary> { ["actualTerminalTime"] = Stats(rows.Select(r => r.Terminal ? r.TerminalTime : null), expectedN), ["collisions"] = Stats(rows.Select(r => EndCount(r, r.Collisions)), expectedN), ["ordinaryBoostsEarned"] = Stats(rows.Select(r => EndCount(r, r.BoostsEarned)), expectedN), ["completedBoostPulses"] = Stats(rows.Select(r => EndCount(r, r.CompletedBoosts)), expectedN), ["fullFinishCount"] = Stats(rows.Select(r => EndCount(r, r.FullFinishCount)), expectedN), ["completedGoalCount"] = Stats(rows.Select(r => EndCount(r, r.CompletedGoalCount)), expectedN), ["unreachableCount"] = Stats(rows.Select(r => EndCount(r, r.UnreachableCount)), expectedN), ["acceptedInputs"] = Stats(rows.Select(r => EndCount(r, r.AcceptedInputs)), expectedN), ["rejectedInputs"] = Stats(rows.Select(r => EndCount(r, r.RejectedInputs)), expectedN), ["suppressedInputs"] = Stats(rows.Select(r => EndCount(r, r.SuppressedInputs)), expectedN) },
            intentionCountsScope = "All observed decisions, including partial error cases; completeness is declared by terminalMetricsComplete. Unresolved outcomes remain in denominator.",
            intentionVersusActual = intentions.GroupBy(i => i.Kind + "/" + i.Intent).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new { key = g.Key, decisions = g.Count(), resolved = g.Count(i => i.Resolved), unresolved = g.Count(i => !i.Resolved), matched = g.Count(i => i.Matched), matchRateAllDecisions = (double)g.Count(i => i.Matched) / g.Count(), unresolvedIncludedInDenominator = true, actualOutcomes = g.GroupBy(i => i.ActualOutcome).OrderBy(a => a.Key).Select(a => new { outcome = a.Key, count = a.Count() }) }),
            masterQuality = parameters.Difficulty != "Master" ? "NOT_APPLICABLE" : rows.Length != expectedN ? "INCOMPLETE" : rows.Any(r => !r.MasterInvariantSatisfied || r.Error != null) ? "FAIL" : "PASS", exploratoryOnly = true, validationAcceptance = false };
    }
    static Dictionary<string,string> VerifyPublication(JsonElement manifest, string baseDirectory)
    {
        var before = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var file in manifest.GetProperty("publishedFiles").EnumerateArray())
        {
            string relative = file.GetProperty("path").GetString(), expected = file.GetProperty("sha256").GetString(); string path = Path.GetFullPath(Path.Combine(baseDirectory, relative));
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Manifest path escapes immutable publication");
            string actual = ShaFile(path); if (actual != expected) throw new InvalidOperationException("Published file hash mismatch: " + relative); before.Add(relative, actual);
        }
        return before;
    }
    static int Execute(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if(args.Length>0 && (args[0]=="--study" || args[0]=="--describe-study" || args[0]=="--self-check-study")) return StudyEntry(args);
        var parameters = InitialRows(); string parameterJson = JsonSerializer.Serialize(parameters, Compact);
        if (args.SequenceEqual(new[] { "--describe-initial" })) { Console.WriteLine(JsonSerializer.Serialize(new { assemblySha256 = ShaFile(AssemblyPath), runtimeVersion = Environment.Version.ToString(), parameterTableSha256 = ShaText(parameterJson), parameterTableJson = parameterJson, scheduleSha256 = ShaText(JsonSerializer.Serialize(Schedule(), Compact)), parameters }, Json)); return 0; }
        if (args.SequenceEqual(new[] { "--self-check" }))
        {
            CheckSuppressionTelemetry(PerCohort);
            var constant = Stats(Enumerable.Repeat<double?>(7, PerCohort));
            var sequence = Stats(Enumerable.Range(0, PerCohort).Select(i => (double?)i));
            var incomplete = Stats(Enumerable.Repeat<double?>(7, PerCohort - 1).Concat(new double?[] { null }));
            if (constant.Mean != 7 || constant.SampleStandardDeviation != 0 || constant.Lower99 != 7 || constant.Upper99 != 7 || sequence.Mean != 99.5 || Math.Abs(sequence.SampleStandardDeviation.Value - Math.Sqrt(3350)) > 1e-10 || incomplete.Complete || incomplete.Mean.HasValue || incomplete.Lower99.HasValue) throw new InvalidOperationException("Statistics aggregation self-check failed");
            if (parameters.Length != 50 || parameters.Select(p => p.CohortId).Distinct().Count() != 50 || parameters.Any(p => !double.IsFinite(p.CollectProbability) || p.CollectProbability < 0 || p.CollectProbability > 1 || !double.IsFinite(p.HitProbability) || p.HitProbability < 0 || p.HitProbability > 1)) throw new InvalidOperationException("Initial parameter table self-check failed");
            using var parameterDocument = JsonDocument.Parse(parameterJson);
            foreach (var row in parameterDocument.RootElement.EnumerateArray()) foreach (string field in new[] { "cohortId", "trackId", "profileId", "difficulty", "quality", "collectProbability", "hitProbability", "baseTime", "masterTime", "bonusGain", "collisionCostSum", "targetMeanTime" }) if (!row.TryGetProperty(field, out _)) throw new InvalidOperationException("Missing initial parameter export field " + field);
            var partialCases = Enumerable.Range(0, PerCohort).Select(i => new CaseResult { Terminal = i != 0, Collisions = 3, AcceptedInputs = 4, ActualElapsed = 12, Error = i == 0 ? "partial running error" : null }).ToArray();
            var terminalFailures = Enumerable.Range(0, PerCohort).Select(i => new CaseResult { Terminal = true, Collisions = 3, Error = i == 0 ? "terminal quality failure" : null }).ToArray();
            var partialCount = Stats(partialCases.Select(r => EndCount(r, r.Collisions))); var partialInputs = Stats(partialCases.Select(r => EndCount(r, r.AcceptedInputs))); var failuresIncluded = Stats(terminalFailures.Select(r => EndCount(r, r.Collisions)));
            if (partialCount.Complete || partialCount.Mean.HasValue || partialCount.Lower99.HasValue || partialInputs.Mean.HasValue || !failuresIncluded.Complete || failuresIncluded.Observed != PerCohort || failuresIncluded.Mean != 3) throw new InvalidOperationException("Partial/terminal-quality-failure terminal-metric self-check failed");
            var schedule = Schedule();
            if (schedule.Length != 10000 || !schedule.Select((s,i) => s.Index == i).All(x => x) || schedule.Select(s => s.Id).Distinct().Count() != 10000 || schedule.GroupBy(s => s.CohortId).Count() != 50 || schedule.GroupBy(s => s.CohortId).Any(g => !g.Select(s => s.Seed).SequenceEqual(Enumerable.Range(0,PerCohort).Select(i => (uint)i))) || schedule.Any(s => s.Seed >= 1000000 && s.Seed <= 1000199)) throw new InvalidOperationException("Mandatory declared seed/output schedule self-check failed");
            var syntheticSchedule = Enumerable.Range(0,3).Select(i => new Spec { Index=i, Id="synthetic-"+i }).ToArray();
            var syntheticResults = new ConcurrentDictionary<int,CaseResult>();
            syntheticResults.TryAdd(0,new CaseResult { Case=syntheticSchedule[0],Terminal=true,TerminalTime=7,Status="Complete" });
            syntheticResults.TryAdd(1,new CaseResult { Case=syntheticSchedule[1],Terminal=true,TerminalTime=8,Status="Error",Error="synthetic terminal quality failure" });
            var injected = CheckAfterCohort(() => throw new FileNotFoundException("Synthetic post-run manifest unavailable"), null);
            var recovery = RecoveryReport(new RunProgress { Schedule=syntheticSchedule,Results=syntheticResults,AssemblyHash="synthetic",ManifestHash="synthetic",ParameterHash="synthetic",ScheduleHash="synthetic",SyntheticFixture=true },injected.Fatal);
            using var recoveryJson = JsonDocument.Parse(JsonSerializer.Serialize(recovery,Json));
            if(injected.Stable || injected.Fatal==null || recoveryJson.RootElement.GetProperty("actualCases").GetInt32()!=2 || recoveryJson.RootElement.GetProperty("unrunCaseIds").GetArrayLength()!=1 || recoveryJson.RootElement.GetProperty("cases").GetArrayLength()!=2 || recoveryJson.RootElement.GetProperty("cases")[1].GetProperty("terminalTime").GetDouble()!=8 || recoveryJson.RootElement.GetProperty("status").GetString()=="SETUP_FAILED_NO_COHORT") throw new InvalidOperationException("Post-run hash-failure retention self-check failed");
            Console.WriteLine("SELF-CHECK PASS: separate suppressed/accepted/rejected observer and raw/terminal/partial metrics; analytic/partial-terminal/terminal-quality-failure aggregation;50settings;10000seed schedule; synthetic missing-manifest failure retains2observed cases and1unrun ID. No race or cohort run."); return 0;
        }
        var options = new Dictionary<string,string>(StringComparer.Ordinal); bool training = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--training") { training = true; continue; }
            if (args[i] != "--start-seed" && args[i] != "--count" && args[i] != "--parallelism") throw new ArgumentException("Only initial --training --start-seed 0 --count 200 --parallelism 1..3 is supported; validation/held-out/fixed calibration runs are prohibited");
            if (++i >= args.Length || !options.TryAdd(args[i - 1], args[i])) throw new ArgumentException("Missing or repeated CLI value");
        }
        if (!training || !options.TryGetValue("--start-seed", out var seedText) || !uint.TryParse(seedText, out uint startSeed) || startSeed != 0 || !options.TryGetValue("--count", out var countText) || !int.TryParse(countText, out int count) || count != PerCohort || !options.TryGetValue("--parallelism", out var parallelText) || !int.TryParse(parallelText, out int workers) || workers < 1 || workers > MaximumWorkers)
            throw new ArgumentException("Required CLI: --training --start-seed 0 --count 200 --parallelism 1..3. Reserved validation seeds are never run.");
        string publication = AppContext.BaseDirectory, manifestPath = Path.Combine(publication, "training-manifest.json");
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath)); var manifest = manifestDocument.RootElement;
        string manifestHash = ShaFile(manifestPath);
        if (manifest.GetProperty("protocolId").GetString() != "BSTO-INITIAL-TRAINING-1" || manifest.GetProperty("assemblySha256").GetString() != ShaFile(AssemblyPath) || manifest.GetProperty("parameterTableSha256").GetString() != ShaText(parameterJson) || manifest.GetProperty("scheduleSha256").GetString() != ShaText(JsonSerializer.Serialize(Schedule(), Compact))) throw new InvalidOperationException("Published assembly/protocol/initial parameters do not match frozen manifest");
        var declared = manifest.GetProperty("training"); var heldOut = manifest.GetProperty("heldOut");
        if (declared.GetProperty("startSeed").GetUInt32() != 0 || declared.GetProperty("count").GetInt32() != PerCohort || declared.GetProperty("cohorts").GetInt32() != 50 || declared.GetProperty("expectedCases").GetInt32() != 10000 || declared.GetProperty("maximumParallelism").GetInt32() != MaximumWorkers || declared.GetProperty("camera").GetString() != "Lagger" || !declared.GetProperty("fixedBeforeStart").GetBoolean() || heldOut.GetProperty("startSeed").GetUInt32() != 1000000 || heldOut.GetProperty("count").GetInt32() != 200 || !heldOut.GetProperty("reserved").GetBoolean() || heldOut.GetProperty("executed").GetBoolean()) throw new InvalidOperationException("Frozen declared cohort/held-out reservation differs from this initial-only sampler");
        var publishedHashes = VerifyPublication(manifest, publication); var scheduled = Schedule(); var results = new ConcurrentDictionary<int, CaseResult>();
        string repository = manifest.GetProperty("repositoryRoot").GetString(), outputJson = Path.Combine(repository, "Verification/stochastic-bots-training-initial.json"), outputTxt = Path.Combine(repository, "Verification/stochastic-bots-training-initial.txt");
        if (File.Exists(outputJson) || File.Exists(outputTxt)) throw new IOException("Initial training outputs already exist; preserve them and request an explicitly named new cohort before rerunning");
        activeRun = new RunProgress { Schedule=scheduled,Results=results,AssemblyHash=ShaFile(AssemblyPath),ManifestHash=manifestHash,ParameterHash=ShaText(parameterJson),ScheduleHash=ShaText(JsonSerializer.Serialize(scheduled,Compact)) };
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Cancellation.Cancel(); }; int completed = 0, firstMasterFailure = 0; Exception fatal = null;
        Console.WriteLine("DECLARED TRAINING 50 cohorts x seeds0..199; workers=" + workers + "; assembly=" + ShaFile(AssemblyPath) + "; manifest=" + manifestHash + "; reserved1000000..1000199 NOT RUN");
        try
        {
            Parallel.ForEach(scheduled, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = Cancellation.Token }, spec =>
            {
                cohortStarted = true;
                var expected = parameters.Single(p => p.CohortId == spec.CohortId); CaseResult result;
                try { result = Run(spec, expected); } catch (Exception e) { result = new CaseResult { Case = spec, Status = "Error", Stage = "unexpected-sampler-error", Error = e.ToString() }; }
                results.TryAdd(spec.Index, result);
                if (spec.Difficulty == "Master" && result.Error != null && Interlocked.CompareExchange(ref firstMasterFailure, 1, 0) == 0) Console.WriteLine("FIRST_MASTER_CASE_FAILURE " + JsonSerializer.Serialize(new { caseId = spec.Id, result.Status, result.Error, result.PhysicalStatus, result.TerminalTime, result.Distance, result.Collisions, result.BoostsEarned, result.CompletedBoosts, result.FullFinishCount, result.CompletedGoalCount, result.GoalCount, result.HitIds, result.MissedGoalIds }, Compact));
                int done = Interlocked.Increment(ref completed); if (done % 1000 == 0) Console.WriteLine("TRAINING completed=" + done + "/" + scheduled.Length);
            });
        }
        catch (Exception e) { fatal = e; }
        var publicationCheck = CheckAfterCohort(() => ShaFile(manifestPath) == manifestHash && publishedHashes.All(kv => File.Exists(Path.Combine(publication, kv.Key)) && ShaFile(Path.Combine(publication, kv.Key)) == kv.Value), fatal);
        bool stable = publicationCheck.Stable; fatal = publicationCheck.Fatal;
        var ordered = results.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray(); var unrun = scheduled.Where(s => !results.ContainsKey(s.Index)).Select(s => s.Id).ToArray();
        var report = new { status = fatal == null && stable && unrun.Length == 0 && ordered.All(r => r.Error == null) ? "COMPLETE_INITIAL_TRAINING" : "TRAINING_WITH_ERRORS", cohort = "TRAINING_INITIAL", exploratoryOnly = true, validationAcceptance = false, heldOutSeedsRun = false, protocolId = "BSTO-INITIAL-TRAINING-1",
            assemblySha256 = ShaFile(AssemblyPath), manifestSha256 = manifestHash, parameterTableSha256 = ShaText(parameterJson), scheduleSha256 = ShaText(JsonSerializer.Serialize(scheduled, Compact)), publicationDirectory = publication, publicationStable = stable, publishedManifest = manifest.Clone(),
            startSeed, count, parallelism = workers, expectedCases = scheduled.Length, actualCases = ordered.Length, unrunCaseIds = unrun, fatalError = fatal?.ToString(),
            statisticalMethod = "Two-sided exploratory 99% Student-t mean CI, sample SD denominator199, n=200, conservative t critical2.601 (df199 rounded upward); no survivor filtering, no validation/familywise acceptance. Any missing observation yields null metric aggregates/CI.",
            sampler = "Actual settings factory and slot1 controller; human0 idle after normal ready Press. Stop at bot terminal even while human remains racing. Ordinary Press/Advance only, no fullsliceaudit/state injection/old catalogue selection. Case errors retained, no rerolls.",
            cohorts = parameters.Select(p => Cohort(p, ordered.Where(r => r.Case.CohortId == p.CohortId).ToArray())), cases = ordered };
        File.WriteAllText(outputJson, JsonSerializer.Serialize(report, Json));
        File.WriteAllText(outputTxt, "INITIAL TRAINING ONLY — NOT VALIDATION ACCEPTANCE\n" + ordered.Length + "/" + scheduled.Length + " announced cases; errors=" + ordered.Count(r => r.Error != null) + "; unrun=" + unrun.Length + "; publicationStable=" + stable + ".\nAssembly SHA256 " + ShaFile(AssemblyPath) + "\nManifest SHA256 " + manifestHash + "\nAll seeds0..199 retained; reserved1000000..1000199 not run. Settings fixed before each start from the published initial table. No calibration was selected or adjusted.\nSee JSON for every raw terminal/error case, actual effect/goal/hit facts, saved intentions, per-cohort means/sample SD/99% exploratory intervals and intent-versus-actual counts.\n");
        Console.WriteLine("TRAINING saved=" + outputJson + " cases=" + ordered.Length + " errors=" + ordered.Count(r => r.Error != null) + " stable=" + stable);
        return fatal == null && stable && unrun.Length == 0 && ordered.All(r => r.Error == null) ? 0 : 1;
    }
    static int Main(string[] args)
    {
        try { return Execute(args); }
        catch (Exception e)
        {
            if (cohortStarted && activeRun != null)
            {
                string recoveryDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tools/StochasticStatistics/Recovery");
                try
                {
                    Directory.CreateDirectory(recoveryDirectory);
                    string recoveryPath = Path.Combine(recoveryDirectory,activeRun.RecoveryPrefix+"-postrun-error-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".json");
                    File.WriteAllText(recoveryPath,JsonSerializer.Serialize(RecoveryReport(activeRun,e),Json));
                    Console.Error.WriteLine("POST-RUN FAILURE: retained "+activeRun.Results.Count+" accumulated cases; recovery report "+recoveryPath+"; "+e);
                }
                catch (Exception writeError) { Console.Error.WriteLine("POST-RUN FAILURE with "+activeRun.Results.Count+" accumulated cases; recovery-file write also failed: "+writeError+"; original: "+e); }
                return 1;
            }
            Console.Error.WriteLine("SETUP FAILED before cohort: " + e);
            if (args.Contains("--training"))
            {
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Tools/StochasticStatistics");
                if (Directory.Exists(folder))
                {
                    string file = Path.Combine(folder, "initial-training-setup-failure-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json");
                    File.WriteAllText(file, JsonSerializer.Serialize(new { status = "SETUP_FAILED_NO_COHORT", assemblySha256 = ShaFile(AssemblyPath), arguments = args, error = e.ToString(), actualCases = 0, reservedHeldOutSeedsRun = false, unrunCaseIds = Schedule().Select(s => s.Id).ToArray() }, Json));
                    Console.Error.WriteLine("Full unrun manifest: " + file);
                }
            }
            return 1;
        }
    }
}