using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

static class Program
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    static readonly JsonSerializerOptions PropertiesJson = new JsonSerializerOptions { WriteIndented = false };
    static readonly Dictionary<Type, (Func<object, object, bool> Equal, FieldInfo[] Scalar, FieldInfo[] Nested)> Comparers = new();
    static readonly List<Result> Results = new();
    static long checks, fieldComparisons, immediateSnapshots;
    static string context = "prepare";
    static void Need(bool condition, string reason) { checks++; if (!condition) throw new InvalidOperationException(context + ": " + reason); }
    static bool Scalar(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || Nullable.GetUnderlyingType(t) != null;
    static (Func<object, object, bool> Equal, FieldInfo[] Scalar, FieldInfo[] Nested) Comparator(Type type)
    {
        if (Comparers.TryGetValue(type, out var comparator)) return comparator;
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var scalars = fields.Where(f => Scalar(f.FieldType)).ToArray(); var nested = fields.Where(f => !Scalar(f.FieldType)).ToArray();
        var a = Expression.Parameter(typeof(object)); var b = Expression.Parameter(typeof(object)); Expression equal = Expression.Constant(true);
        foreach (var field in scalars) equal = Expression.AndAlso(equal, Expression.Equal(Expression.Field(Expression.Convert(a, type), field), Expression.Field(Expression.Convert(b, type), field)));
        comparator = (Expression.Lambda<Func<object, object, bool>>(equal, a, b).Compile(), scalars, nested); Comparers.Add(type, comparator); return comparator;
    }
    static void Same(object actual, object replay, string path)
    {
        if (actual == null || replay == null) { Need(actual == null && replay == null, path + " null"); return; }
        Type type = actual.GetType(); Need(type == replay.GetType(), path + " type");
        if (Scalar(type)) { fieldComparisons++; Need(actual.Equals(replay), path + " scalar"); return; }
        if (actual is HashSet<string> set) { fieldComparisons += set.Count; Need(set.SetEquals((HashSet<string>)replay), path + " set"); return; }
        if (actual is IEnumerable values)
        {
            var a = values.GetEnumerator(); var b = ((IEnumerable)replay).GetEnumerator(); int index = 0;
            while (a.MoveNext()) { Need(b.MoveNext(), path + " shorter"); Same(a.Current, b.Current, path + "[" + index++ + "]"); }
            Need(!b.MoveNext(), path + " longer"); return;
        }
        var c = Comparator(type); fieldComparisons += c.Scalar.Length;
        if (!c.Equal(actual, replay)) foreach (var field in c.Scalar) Need(Equals(field.GetValue(actual), field.GetValue(replay)), path + "." + field.Name);
        foreach (var field in c.Nested) Same(field.GetValue(actual), field.GetValue(replay), path + "." + field.Name);
    }
    static void WorldSame(GameState actual, GameState replay)
    {
        foreach (var field in typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.Name == "Track" || field.Name == "Flight" || field.Name == "Profile")
                Need(ReferenceEquals(field.GetValue(actual), field.GetValue(replay)), "shared readonly world " + field.Name);
            else Same(field.GetValue(actual), field.GetValue(replay), "world." + field.Name);
        }
        Need(actual.PartyCameraLeft == replay.PartyCameraLeft, "world.PartyCameraLeft"); immediateSnapshots++;
    }
    sealed class Spec
    {
        public string Id, Track, Profile; public double Q; public uint Seed = 0; public PartyCamera Camera; public bool HumanScript;
    }
    sealed class Edge { public double Time, Distance; public bool Accepted; }
    sealed class Result
    {
        public Spec Case; public string Error; public string HumanStatus, BotStatus; public double? HumanTerminalTime, BotTerminalTime;
        public double HumanDistance, BotDistance, BotObservedTerminalWorldTime, WorldTime, Milliseconds;
        public int HumanCollisions, BotCollisions, BotBoosts, BotFinishBoosts, Cursor, AdvanceSlices, BotPressSlices, RejectedBotPresses, PostBotTerminalAdvanceSlices, PostBotTerminalHumanPresses, PauseChecks;
        public bool ReplayEqual, BotTerminalBeforeHuman, BotFrozenAfterTerminal, ControllerFrozenAfterTerminal, GlobalTerminalFrozen;
        public long Snapshots, FieldComparisons;
        public string[] BotSuccessIds, BotHitIds;
        public List<Edge> HumanInputs = new(), BotInputs = new(); public object[] Decisions;
        public List<BotSlice> RepresentativeActualObserverTape;
    }
    static string ControllerState(StochasticBotController controller) => JsonSerializer.Serialize(controller, PropertiesJson);
    static object[] DecisionRecords(StochasticBotController controller) => controller.Decisions.Select(d => (object)new { d.Id, kind = d.Kind.ToString(), d.Intent, d.Probability, d.Roll, d.AtDistance, d.AtTime, d.HorizonEndDistance, d.Outcome, d.UnreachableReason, d.SelectedInputs }).ToArray();
    static string IntentKey(StochasticBotController controller) => string.Join("|", controller.Decisions.Select(d => d.Id + ":" + d.Intent + ":" + d.Probability.ToString("R", CultureInfo.InvariantCulture) + ":" + d.Roll.ToString("R", CultureInfo.InvariantCulture)));
    static string IntentKey(Result result) => string.Join("|", result.Decisions.Select(d => JsonSerializer.Serialize(d, PropertiesJson)).Select(text => { using var j = JsonDocument.Parse(text); return j.RootElement.GetProperty("Id").GetString() + ":" + j.RootElement.GetProperty("Intent").GetString() + ":" + j.RootElement.GetProperty("Probability").GetDouble().ToString("R", CultureInfo.InvariantCulture) + ":" + j.RootElement.GetProperty("Roll").GetDouble().ToString("R", CultureInfo.InvariantCulture); }));
    static readonly double[] HumanTimes = Enumerable.Range(0, 135).Select(i => .07 + .317 * i).ToArray();
    static Result Run(Spec spec)
    {
        context = spec.Id; var result = new Result { Case = spec }; Results.Add(result); var stopwatch = Stopwatch.StartNew(); long startSnapshots = immediateSnapshots, startFields = fieldComparisons;
        Track track = RaceTracks.Get(spec.Track); FlightSettings flight = spec.Profile == "Chosen" ? FlightPresets.Chosen : FlightPresets.FastFlip;
        var game = RaceSimulation.CreateParty(track, flight, 2, spec.Camera, -288.8, 1520);
        var replay = RaceSimulation.CreateParty(track, flight, 2, spec.Camera, -288.8, 1520);
        Need(game.IsParty && !game.IsBot && game.Players.Length == 2, "ordinary party foundation, not future bot factory");
        var controller = new StochasticBotController(game, 1, spec.Q, spec.Seed);
        if (Results.Count == 1) result.RepresentativeActualObserverTape = new();
        bool terminalSeen = false, beforeBoundaryPaused = false, afterBoundaryPaused = false; string frozenBot = null, frozenController = null; int humanCursor = 0, frames = 0;
        void ObserveTerminal()
        {
            var bot = game.Players[1];
            if (!terminalSeen && bot.Status != PlayerStatus.Running)
            {
                terminalSeen = true; result.BotObservedTerminalWorldTime = game.Elapsed; result.BotTerminalBeforeHuman = game.Players[0].Status == PlayerStatus.Running;
                frozenBot = JsonSerializer.Serialize(bot, Json); frozenController = ControllerState(controller);
            }
            else if (terminalSeen)
            {
                Need(JsonSerializer.Serialize(bot, Json) == frozenBot, "bot public state changed after terminal");
                Need(ControllerState(controller) == frozenController, "controller cursor/decisions/planner state changed after terminal");
            }
        }
        void Pause(string when)
        {
            string actualState = JsonSerializer.Serialize(game, Json), replayState = JsonSerializer.Serialize(replay, Json), control = ControllerState(controller); long snapshots = immediateSnapshots;
            controller.Advance(.23, true); controller.Advance(0); controller.Advance(-1); controller.Advance(double.NaN); controller.Advance(double.PositiveInfinity);
            Need(actualState == JsonSerializer.Serialize(game, Json) && replayState == JsonSerializer.Serialize(replay, Json) && control == ControllerState(controller) && snapshots == immediateSnapshots, "pause/invalid delta changed world/controller/observer " + when); result.PauseChecks++;
        }
        controller.Observer = slice =>
        {
            Need(slice.Time == game.Elapsed && slice.Distance == game.Players[1].Distance, "observer reports actual world/bot coordinates");
            result.RepresentativeActualObserverTape?.Add(slice);
            if (slice.Kind == "advance")
            {
                Need(slice.Delta > 0 && double.IsFinite(slice.Delta), "invalid actual advance slice");
                bool activeBefore = replay.Phase == "racing"; double previousTime = replay.Elapsed;
                RaceSimulation.Advance(replay, slice.Delta); result.AdvanceSlices++;
                if (activeBefore && replay.Phase == "racing") Need(Math.Abs(game.Elapsed - previousTime - slice.Delta) < 1e-10, "shared racing world advanced more/less than one actual slice");
                else if (activeBefore && replay.Phase == "finished") Need(game.Elapsed == replay.Elapsed && game.Elapsed == replay.Players.Max(p => p.TerminalTime.Value), "global terminal clock must canonicalize to the actual last participant terminal time");
                if (terminalSeen && replay.Players[0].Status == PlayerStatus.Running) result.PostBotTerminalAdvanceSlices++;
            }
            else if (slice.Kind == "press")
            {
                Need(game.Players[1].Status == PlayerStatus.Running && !terminalSeen, "bot Press after terminal");
                Need(slice.Cursor == result.BotPressSlices, "one actual press per cursor");
                bool accepted = RaceSimulation.Press(replay, 1); Need(accepted == slice.Accepted, "slot1 Press acceptance replay");
                result.BotPressSlices++; if (!accepted) result.RejectedBotPresses++;
                result.BotInputs.Add(new Edge { Time = slice.Time, Distance = slice.Distance, Accepted = accepted });
            }
            else throw new InvalidOperationException("Unknown actual observer kind " + slice.Kind);
            WorldSame(game, replay); ObserveTerminal();
        };
        try
        {
            // Ready Press replaces both player objects once all are ready. All later
            // comparisons intentionally resolve game.Players afresh.
            var originalHuman = game.Players[0]; var originalBot = game.Players[1];
            Need(RaceSimulation.Press(game, 1) == RaceSimulation.Press(replay, 1), "bot readiness"); WorldSame(game, replay);
            Need(RaceSimulation.Press(game, 0) == RaceSimulation.Press(replay, 0), "human readiness/start"); WorldSame(game, replay);
            Need(!ReferenceEquals(originalHuman, game.Players[0]) && !ReferenceEquals(originalBot, game.Players[1]), "ready replacement must be respected");
            Pause("countdown");
            double[] deltas = { 1d / 30, .005, .027, .011, .073, .001, .049, .016 };
            while (game.Phase == "countdown" || game.Phase == "racing")
            {
                Need(frames < 30000 && game.Elapsed < 120, "finite shared-world fixture guard");
                if (game.Phase == "racing")
                {
                    if (!beforeBoundaryPaused && game.Elapsed >= StochasticTrackMap.Create(track, flight).MasterTime - .08 && game.Players[1].Status == PlayerStatus.Running)
                    { Pause("before earliest clean bot terminal boundary"); beforeBoundaryPaused = true; }
                    if (terminalSeen && !afterBoundaryPaused) { Pause("after bot terminal boundary"); afterBoundaryPaused = true; }
                    if (spec.HumanScript)
                    {
                        while (humanCursor < HumanTimes.Length && HumanTimes[humanCursor] - game.Elapsed <= 1e-9)
                        {
                            bool accepted = RaceSimulation.Press(game, 0); Need(accepted == RaceSimulation.Press(replay, 0), "fixed-time human Press acceptance");
                            result.HumanInputs.Add(new Edge { Time = game.Elapsed, Distance = game.Players[0].Distance, Accepted = accepted }); humanCursor++;
                            if (terminalSeen) result.PostBotTerminalHumanPresses++; WorldSame(game, replay); ObserveTerminal();
                        }
                    }
                }
                double dt = deltas[frames++ % deltas.Length];
                if (spec.HumanScript && game.Phase == "racing" && humanCursor < HumanTimes.Length)
                    dt = Math.Min(dt, Math.Max(1e-10, HumanTimes[humanCursor] - game.Elapsed));
                controller.Advance(dt); WorldSame(game, replay); ObserveTerminal();
                Need(controller.Cursor == result.BotPressSlices, "cursor equals actual emitted press count");
            }
            Need(game.Phase == "finished" && game.Players.All(p => p.Status != PlayerStatus.Running), "both actual participants terminal");
            Need(terminalSeen, "bot actual terminal observed");
            result.BotFrozenAfterTerminal = true; result.ControllerFrozenAfterTerminal = true;
            if (spec.Q == 1)
            {
                var bot = game.Players[1]; var map = StochasticTrackMap.Create(track, flight);
                Need(bot.Status == PlayerStatus.Finished && bot.Collisions == 0 && Math.Abs(bot.TerminalTime.Value - map.MasterTime) < 2e-7, "q1 clean master time");
                Need(track.RiskZones.Where(z => !z.IsNegative && !z.IsCoin).All(z => bot.SuccessfulRiskIds.Contains(z.Id)) && bot.CompletedBoosts == 9 && bot.FinishBoostElapsed >= .25 - 1e-7, "q1 actual all goals and complete effects");
                if (!spec.HumanScript) Need(result.BotTerminalBeforeHuman && result.PostBotTerminalAdvanceSlices > 0 && game.Players[0].TerminalTime > bot.TerminalTime, "idle human continues after q1 bot terminal");
            }
            if (spec.HumanScript) Need(result.HumanInputs.Any(e => e.Accepted), "fixed human script produced no actual accepted input");
            string terminalWorld = JsonSerializer.Serialize(game, Json), terminalControl = ControllerState(controller);
            controller.Advance(1); WorldSame(game, replay);
            Need(terminalWorld == JsonSerializer.Serialize(game, Json) && terminalControl == ControllerState(controller), "global terminal physical/controller freeze; no-op debug observer allowed");
            Pause("global terminal"); result.GlobalTerminalFrozen = true; result.ReplayEqual = true;
        }
        catch (Exception e) { result.Error = e.ToString(); throw; }
        finally
        {
            var human = game.Players[0]; var bot = game.Players[1]; result.HumanStatus = human.Status.ToString(); result.BotStatus = bot.Status.ToString();
            result.HumanTerminalTime = human.TerminalTime; result.BotTerminalTime = bot.TerminalTime; result.HumanDistance = human.Distance; result.BotDistance = bot.Distance;
            result.HumanCollisions = human.Collisions; result.BotCollisions = bot.Collisions; result.BotBoosts = bot.BoostsEarned; result.BotFinishBoosts = bot.FinishBoostsEarned;
            result.BotSuccessIds = bot.SuccessfulRiskIds.OrderBy(x => x).ToArray(); result.BotHitIds = bot.HitObstacleIds.OrderBy(x => x).ToArray();
            result.WorldTime = game.Elapsed; result.Cursor = controller.Cursor; result.Decisions = DecisionRecords(controller);
            result.Snapshots = immediateSnapshots - startSnapshots; result.FieldComparisons = fieldComparisons - startFields; result.Milliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }
        Console.WriteLine("PASS " + spec.Id + " bot=" + result.BotStatus + "/" + result.BotTerminalTime + " human=" + result.HumanStatus + "/" + result.HumanTerminalTime + " slices=" + result.AdvanceSlices + " postBot=" + result.PostBotTerminalAdvanceSlices); return result;
    }
    static List<Spec> Schedule() => new()
    {
        new Spec { Id="cascade-chosen-master-idle-lagger",Track=TracksRhythm.CascadeId,Profile="Chosen",Q=1,Camera=PartyCamera.Lagger },
        new Spec { Id="cascade-chosen-master-human-lagger",Track=TracksRhythm.CascadeId,Profile="Chosen",Q=1,Camera=PartyCamera.Lagger,HumanScript=true },
        new Spec { Id="cascade-chosen-novice-idle-lagger",Track=TracksRhythm.CascadeId,Profile="Chosen",Q=.5,Camera=PartyCamera.Lagger },
        new Spec { Id="cascade-chosen-novice-human-lagger",Track=TracksRhythm.CascadeId,Profile="Chosen",Q=.5,Camera=PartyCamera.Lagger,HumanScript=true },
        new Spec { Id="mixed-fast-master-idle-lagger",Track=TracksMixed.Id,Profile="FastFlip",Q=1,Camera=PartyCamera.Lagger },
        new Spec { Id="mixed-fast-master-human-lagger",Track=TracksMixed.Id,Profile="FastFlip",Q=1,Camera=PartyCamera.Lagger,HumanScript=true },
        new Spec { Id="mixed-fast-novice-idle-lagger",Track=TracksMixed.Id,Profile="FastFlip",Q=.5,Camera=PartyCamera.Lagger },
        new Spec { Id="mixed-fast-novice-idle-constant",Track=TracksMixed.Id,Profile="FastFlip",Q=.5,Camera=PartyCamera.Constant }
    };
    static Dictionary<string,string> Hashes() => Directory.GetFiles("Assets/Scripts/Core", "*.cs").Concat(Directory.GetFiles("Tools/StochasticWorldCheck", "*.cs")).Concat(Directory.GetFiles("Tools/StochasticWorldCheck", "*.csproj")).OrderBy(x => x).ToDictionary(f => f.Replace('\\','/'), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
    static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--masterfix" && !args[0].StartsWith("--output-stem=",StringComparison.Ordinal))) throw new ArgumentException("Choose optional --masterfix or a fresh scoped --output-stem selector.");
        string reportStem = args.Length == 1 && args[0]=="--masterfix" ? "stochastic-bots-world-masterfix" : "stochastic-bots-world";
        if(args.Length==1 && args[0].StartsWith("--output-stem=",StringComparison.Ordinal))
        {
            reportStem=args[0].Substring("--output-stem=".Length);
            if(!System.Text.RegularExpressions.Regex.IsMatch(reportStem,@"\Astochastic-bots-world-[a-z0-9-]+\z"))throw new ArgumentException("Output must be a named stochastic world proof within Verification");
        }
        if(File.Exists("Verification/"+reportStem+".json")||File.Exists("Verification/"+reportStem+".txt"))throw new IOException("Preserve existing world proof; choose a new --output-stem=stochastic-bots-world-name");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; var scheduled = Schedule(); var sources = Hashes(); Exception failure = null;
        try
        {
            foreach (var spec in scheduled) Run(spec);
            foreach (var group in Results.Where(r => r.Case.Camera == PartyCamera.Lagger).GroupBy(r => r.Case.Track + "/" + r.Case.Profile + "/" + r.Case.Q))
            {
                var idle = group.Single(r => !r.Case.HumanScript); var varied = group.SingleOrDefault(r => r.Case.HumanScript); if (varied == null) continue;
                context = "human independence " + group.Key; Need(IntentKey(idle) == IntentKey(varied), "human inputs changed bot saved intent/probability/roll");
                Need(idle.BotSuccessIds.SequenceEqual(varied.BotSuccessIds) && idle.BotHitIds.SequenceEqual(varied.BotHitIds) && idle.BotStatus == varied.BotStatus && Math.Abs(idle.BotTerminalTime.Value - varied.BotTerminalTime.Value) < 2e-7, "human inputs changed bot real Lagger outcome");
                Need(idle.BotInputs.Count == varied.BotInputs.Count, "human inputs changed bot input count");
                for (int i = 0; i < idle.BotInputs.Count; i++) Need(idle.BotInputs[i].Accepted == varied.BotInputs[i].Accepted && Math.Abs(idle.BotInputs[i].Distance - varied.BotInputs[i].Distance) < 5e-5 && Math.Abs(idle.BotInputs[i].Time - varied.BotInputs[i].Time) < 2e-7, "human inputs changed bot actual action");
            }
            Need(Results.Count == scheduled.Count, "all announced fixtures retained");
        }
        catch (Exception e) { failure = e; Console.WriteLine("FAIL " + e); }
        var after = Hashes(); bool stable = sources.Count == after.Count && sources.All(kv => after.TryGetValue(kv.Key, out var hash) && hash == kv.Value);
        if (!stable && failure == null) failure = new InvalidOperationException("Source changed during final world proof");
        var report = new { status = failure == null ? "PASS" : "FAIL", requirements = new[] { "BSTO00", "BSTO04" }, checks, immediateSnapshots, fieldComparisons,
            sourceStable = stable, sourceHashes = sources, scheduledFixtures = scheduled, ranFixtures = Results.Count, unrunFixtures = scheduled.Skip(Results.Count).Select(s => s.Id).ToArray(),
            failure = failure?.ToString(), maximumReplayNumericError = failure == null ? (double?)0 : null,
            method = "Actual ordinary CreateParty2, StochasticBotController slot1. Every emitted actual Advance/Press immediately replayed once on independent ordinary CreateParty2; all public player/world fields, queues, sets, event objects and camera compared. Human Press supplied separately and identically. Ready replacements resolved afresh. No physical state injection or stubs.",
            humanScriptPredefinedTimes = HumanTimes, limitations = "Eight seed0 foundation fixtures, not statistics, BOT factory integration, UI/browser/WebGL proof, or planner performance acceptance. One Constant fixture reports actual terminal statuses without filtering. No-op debug observer after globally finished is allowed only with unchanged physical/controller state.", results = Results };
        File.WriteAllText("Verification/" + reportStem + ".json", JsonSerializer.Serialize(report, Json));
        File.WriteAllText("Verification/" + reportStem + ".txt", (failure == null ? "PASS" : "FAIL") + " — BSTO00/BSTO04 ordinary shared-world foundation\n" + Results.Count + "/" + scheduled.Count + " predefined seed0 fixtures; " + checks + " checks; " + immediateSnapshots + " complete immediate snapshots; " + fieldComparisons + " field comparisons; sourceStable=" + stable + ".\n" +
            "Every actual emitted Advance/Press replayed immediately on independent ordinary CreateParty2. Current participants resolved after ready replacement. Human script supplied equally; q1 terminal bot freezes while idle human continues; pause freezes world/controller around boundary; Constant outcome retained.\n" +
            "This is not future BOT factory/UI/WebGL/statistical/performance acceptance. Exact terminal distances/times/sets/actions, source hashes and representative full observer tape are in JSON.\n" + (failure == null ? "" : failure + "\n"));
        return failure == null ? 0 : 1;
    }
}