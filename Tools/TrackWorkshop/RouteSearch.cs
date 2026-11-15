using System.Diagnostics;
using FlipRace.Core;

namespace TrackWorkshop;

public sealed record RouteSearchRequest(string Json, string Profile, double WarningDuration, WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record AnalyzedRoute(double FinishTime, int Collisions, int Boosts, int Slows, int Coins,
    int FinishBoosts, int[] InputTicks);
public sealed record RouteFlightSettings(string FirstAction, string MotionLaw, string AirRepeatLimit,
    double GravityAcceleration, double FlipImpulse, double JumpImpulse, double LinearSpeed, double LinearJumpHeight)
{
    internal static RouteFlightSettings Capture(FlightSettings flight) => new(flight.FirstAction.ToString(),
        flight.Law.ToString(), flight.AirRepeats.ToString(), flight.GravityAcceleration, flight.FlipImpulse,
        flight.JumpImpulse, flight.LinearSpeed, flight.LinearJumpHeight);
}
public sealed record RouteSearchRun(string TrackFingerprint, int RulesVersion, string Profile, RouteFlightSettings FlightSettings,
    string WarningMode, double WarningDuration, bool SurfaceRun, double StartX, string Axis, string TimeOrigin,
    int StepMs, double SimulationStepSeconds, double MaxRaceSeconds, RouteSearchParameters SearchParameters,
    int ExpandedStates, long ElapsedMilliseconds, string StopReason, bool BeamPruned, int FoundCount, int VerifiedCount,
    int StoredCount, bool StorageLimited, AnalyzedRoute[] Routes, WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record RouteSearchParameters(string Algorithm, int BeamWidth, int PrefixesPerState,
    int MaximumStates, int MaximumMilliseconds, int VerificationMilliseconds);
public sealed record WorkshopRouteAnalysis(int Version, RouteSearchRun[] Runs);
public sealed record RouteAnalysisStatus(string Status, WorkshopRouteAnalysis Analysis = null);
public sealed record RouteSearchResponse(WorkshopRouteAnalysis Analysis, string SourceJson);

internal readonly record struct RouteSearchProgress(int Percent, string Phase);

internal sealed record RouteSearchLimits(int MaximumStates = 920_000, int BeamWidth = 768,
    int PrefixesPerState = 12, int MaximumMilliseconds = 11_000, int VerificationMilliseconds = 20_000);

internal sealed class RouteSearch
{
    public const int RulesVersion = 1, StepMs = 100, MaximumRoutes = 1000;
    public const int TicksPerSecond = 1000 / StepMs;
    public const int MaximumTicks = (int)WorkshopService.MaximumTime * TicksPerSecond;

    readonly record struct StateBucket(int X, int Y, int VerticalSpeed, int Gravity, bool AirRepeat,
        bool Flipping, SupportKind Support, int SupportSide, string ObstacleId, int Recovering, int Collisions,
        int Boosts, int Slows, bool FinishBoost, int EffectTime, int RewardSignature, int SurfaceReserve);

    sealed record Edge(int Tick, Edge Prior, int Count);
    sealed record Node(GameState Game, Edge Tape, int Sequence, double Priority);
    sealed record Candidate(double FinishTime, Edge Tape, int Sequence);

    readonly TrackPackage package;
    readonly FlightSettings flight;
    readonly string profile;
    readonly double warning;
    readonly RouteSearchLimits limits;
    readonly CancellationToken cancellation;
    readonly WorkshopSurfaceRunSettings surfaceRunSettings;
    readonly Stopwatch watch = Stopwatch.StartNew();
    int expanded;
    bool beamPruned;

    public RouteSearch(TrackPackage package, FlightSettings flight, string profile, double warning,
        RouteSearchLimits limits = null, CancellationToken cancellation = default, WorkshopSurfaceRunSettings surfaceRunSettings = null)
    {
        this.package = package;
        this.flight = flight;
        this.profile = profile;
        this.warning = warning;
        this.limits = limits ?? new();
        this.cancellation = cancellation;
        this.surfaceRunSettings = surfaceRunSettings ?? WorkshopSurfaceRunSettings.Legacy;
    }

    public RouteSearchRun Find(Action<RouteSearchProgress> progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        progress?.Invoke(new(0, "search"));
        cancellation.ThrowIfCancellationRequested();
        const int verificationCapacity = MaximumRoutes + 128;
        var best = new PriorityQueue<Candidate, (double NegativeTime, int NegativePresses, int NegativeSequence)>();
        var frontier = new List<Node> { new(Begin(), null, 0, 0) };
        var nextFrontier = new List<Node>(limits.BeamWidth);
        var candidates = new List<Node>(limits.BeamWidth * 2);
        var bins = new Dictionary<StateBucket, int>(limits.BeamWidth * 2);
        int tick = 0, found = 0, reportedPercent = 0;
        bool InBudget() => expanded < limits.MaximumStates && watch.ElapsedMilliseconds < limits.MaximumMilliseconds;

        for (; tick < MaximumTicks && frontier.Count > 0 && InBudget(); tick++)
        {
            if (progress != null)
            {
                double budgetUsed = Math.Max(expanded / (double)limits.MaximumStates,
                    Math.Max(watch.ElapsedMilliseconds / (double)limits.MaximumMilliseconds, tick / (double)MaximumTicks));
                Report(Math.Min(69, (int)(70 * budgetUsed)), "search");
            }
            foreach (var node in frontier)
            {
                if (!InBudget()) break;
                var idle = Expand(node, tick, false);
                Consider(idle);
                if (InBudget() && RaceSimulation.CanPress(node.Game, 0))
                    Consider(Expand(node, tick, true));
            }

            candidates.Sort((left, right) =>
            {
                int priority = right.Priority.CompareTo(left.Priority);
                return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
            });
            nextFrontier.Clear();
            bins.Clear();
            foreach (var child in candidates)
            {
                if (nextFrontier.Count == limits.BeamWidth) { beamPruned = true; break; }
                var bucket = Bucket(child.Game);
                bins.TryGetValue(bucket, out int count);
                if (count == limits.PrefixesPerState) { beamPruned = true; continue; }
                bins[bucket] = count + 1;
                nextFrontier.Add(child);
            }
            (frontier, nextFrontier) = (nextFrontier, frontier);
            candidates.Clear();
        }

        void Report(int percent, string phase)
        {
            if (progress == null || percent <= reportedPercent) return;
            reportedPercent = percent;
            progress(new(percent, phase));
        }

        void Consider(Node node)
        {
            if (node == null) return;
            var player = node.Game.Players[0];
            if (player.FinishTime is not double time) { candidates.Add(node); return; }
            if (time > WorkshopService.MaximumTime + 1e-8) return;
            found++;
            var candidate = new Candidate(time, node.Tape, node.Sequence);
            best.Enqueue(candidate, (-time, -(node.Tape?.Count ?? 0), -node.Sequence));
            if (best.Count > verificationCapacity) best.Dequeue();
        }

        string stop = expanded >= limits.MaximumStates ? "state-limit" : watch.ElapsedMilliseconds >= limits.MaximumMilliseconds
            ? "time-limit" : tick >= MaximumTicks && frontier.Count > 0 ? "race-time-limit" : "frontier-exhausted";
        cancellation.ThrowIfCancellationRequested();
        Report(70, "verify");
        var replayWatch = Stopwatch.StartNew();
        var verified = new List<AnalyzedRoute>(MaximumRoutes);
        int checkedCandidates = 0;
        foreach (var candidate in best.UnorderedItems.Select(entry => entry.Element)
            .OrderBy(item => item.FinishTime).ThenBy(item => item.Tape?.Count ?? 0).ThenBy(item => item.Sequence))
        {
            cancellation.ThrowIfCancellationRequested();
            if (replayWatch.ElapsedMilliseconds >= limits.VerificationMilliseconds) { stop = "verification-time-limit"; break; }
            var route = Verify(candidate.Tape, replayWatch);
            if (route != null) verified.Add(route);
            if (replayWatch.ElapsedMilliseconds >= limits.VerificationMilliseconds && route == null)
            { stop = "verification-time-limit"; break; }
            Report(70 + 28 * ++checkedCandidates / best.Count, "verify");
        }
        cancellation.ThrowIfCancellationRequested();
        var routes = verified.OrderBy(item => item.FinishTime).ThenBy(item => item.InputTicks.Length).Take(MaximumRoutes).ToArray();
        return new(package.ContentHash, RulesVersion, profile, RouteFlightSettings.Capture(flight),
            package.WarningMode == TrackWarningMode.Selected ? "selected" : "authored", warning,
            surfaceRunSettings.Enabled, 0, "time", "race-start", StepMs, WorkshopService.SampleStep, WorkshopService.MaximumTime,
            new("beam-v1", limits.BeamWidth, limits.PrefixesPerState, limits.MaximumStates,
                limits.MaximumMilliseconds, limits.VerificationMilliseconds),
            expanded, watch.ElapsedMilliseconds, stop, beamPruned, found, verified.Count, routes.Length, false, routes, surfaceRunSettings);
    }

    GameState Begin()
    {
        var game = WorkshopService.StartSolo(package.Track, flight, warning, surfaceRunSettings);
        RaceSimulation.Advance(game, RaceConfig.CountdownDuration);
        return game;
    }

    Node Expand(Node parent, int tick, bool press)
    {
        cancellation.ThrowIfCancellationRequested();
        var game = RaceSnapshot.Clone(parent.Game);
        Edge tape = parent.Tape;
        if (press)
        {
            if (!RaceSimulation.Press(game, 0)) return null;
            tape = new(tick, tape, (tape?.Count ?? 0) + 1);
        }
        // Decision points are integral ticks. Core sweeps contacts within the
        // branch; retained finishes go through the separate replay scheduler.
        double end = (tick + 1) / (double)TicksPerSecond;
        RaceSimulation.Advance(game, end - game.Elapsed);
        var player = game.Players[0];
        double score = player.Distance + (player.FinishBoost ? 22 : 0) +
            (player.ActiveEffect is { IsNegative: false } effect ? effect.Remaining * 70 : 0) -
            (tape?.Count ?? 0) * .02;
        return new(game, tape, ++expanded, score);
    }

    AnalyzedRoute Verify(Edge edge, Stopwatch replayWatch)
    {
        var ticks = new int[edge?.Count ?? 0];
        for (var input = edge; input != null; input = input.Prior) ticks[input.Count - 1] = input.Tick;
        double[] times = Array.ConvertAll(ticks, tick => tick / (double)TicksPerSecond);
        var game = Begin();
        int next = 0, steps = 0;
        while (WorkshopService.IsRacing(game))
        {
            if (++steps % 32 == 0 && replayWatch.ElapsedMilliseconds >= limits.VerificationMilliseconds) return null;
            var accepted = WorkshopService.ReplayStep(game, "time", times, ref next, cancellationToken: cancellation);
            if (accepted is { Status: not "accepted" }) return null;
        }
        var player = game.Players[0];
        return next == ticks.Length && player.FinishTime is double time && time <= WorkshopService.MaximumTime + 1e-8
            ? new(time, player.Collisions, player.BoostsEarned, player.SlowsEarned, player.CoinsEarned,
                player.FinishBoostsEarned, ticks) : null;
    }

    static StateBucket Bucket(GameState game)
    {
        var p = game.Players[0];
        int signature = 0;
        foreach (string reward in p.SuccessfulRiskIds) signature ^= StringComparer.Ordinal.GetHashCode(reward);
        foreach (var hidden in game.HiddenObstacles)
            signature ^= HashCode.Combine(hidden.Key, hidden.Value.Phase, Math.Round((hidden.Value.SolidAt ?? 0) * 20));
        return new((int)(p.Distance / 10), (int)(RaceSimulation.GetPlayerCenterY(p) / 8),
            (int)(p.VerticalSpeed / 60), p.Gravity, p.AirFlipAvailable, p.Flipping, p.Support, p.SupportSide,
            p.SupportObstacleId, (int)Math.Ceiling(p.Recovering * 20), p.Collisions, p.BoostsEarned, p.SlowsEarned,
            p.FinishBoost, (int)((p.ActiveEffect?.Remaining ?? 0) * 20) * (p.ActiveEffect?.IsNegative == true ? -1 : 1),
            signature, (int)(p.SurfaceRunReserve * 100));
    }
}
