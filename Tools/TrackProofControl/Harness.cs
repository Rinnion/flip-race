using System;
using System.Collections.Generic;
using System.Reflection;
using FlipRace.Core;

// Tool-only scheduler. All motion, contacts, rewards and finishes are performed
// by production Press/Advance; no alternate physics or test-only runtime hooks.
internal static partial class Program
{
    static int checks;
    static GameState firstCollision;
    static bool captureCollision;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static void Near(double actual, double expected, string message, double epsilon = 1e-8)
    { Check(Math.Abs(actual - expected) <= epsilon, message + ": " + actual + " != " + expected); }

    internal sealed class InputSample
    {
        public double X, Time, CenterY, VerticalSpeed;
        public bool Airborne, AirFlipAvailable;
        public SupportKind Support;
        public int SupportSide;
        public string SupportObstacleId;
    }
    internal sealed class RouteResult
    {
        public GameState Game;
        public readonly List<InputSample> Inputs = new List<InputSample>();
        public readonly List<HitInfo> Rewards = new List<HitInfo>();
        public int AirInputs;
    }

    // Track and immutable profile are shared. Every mutable simulation object
    // is copied, including support, effect pulse-ledger and future scalar fields.
    // Unknown reference types fail explicitly rather than alias silently.
    static object CopyValue(object source)
    {
        if (source == null) return null;
        Type type = source.GetType();
        if (type.IsValueType || source is string || source is Track || source is MotionProfile
            || source is FlightSettings || source is FlightMeasurement) return source;
        if (source is HashSet<string>) return new HashSet<string>((HashSet<string>)source);
        if (source is Queue<SpeedEffect>)
        {
            var copy = new Queue<SpeedEffect>();
            foreach (var effect in (Queue<SpeedEffect>)source) copy.Enqueue((SpeedEffect)CopyValue(effect));
            return copy;
        }
        if (source is PlayerState[])
        {
            var values = (PlayerState[])source;
            var copy = new PlayerState[values.Length];
            for (int i = 0; i < values.Length; i++) copy[i] = (PlayerState)CopyValue(values[i]);
            return copy;
        }
        object target;
        if (source is GameState) target = new GameState();
        else if (source is PlayerState) target = new PlayerState(((PlayerState)source).Id);
        else if (source is SpeedEffect) target = new SpeedEffect();
        else if (source is HitInfo) target = new HitInfo();
        else throw new InvalidOperationException("Snapshot must explicitly support " + type.FullName);
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            field.SetValue(target, CopyValue(field.GetValue(source)));
        return target;
    }
    static GameState Clone(GameState game) { return (GameState)CopyValue(game); }
    static void Step(GameState game, double dt)
    {
        int before = game.Players[0].Collisions;
        RaceSimulation.Advance(game, dt);
        if (captureCollision && firstCollision == null && game.Players[0].Collisions > before)
            firstCollision = Clone(game);
    }
    static void CaptureReward(RouteResult result)
    {
        if (result == null) return;
        var player = result.Game.Players[0];
        foreach (var hit in new[] { player.LastInstant, player.LastFinishBoost })
            if (hit != null && !result.Rewards.Exists(r => r.Id == hit.Id))
                result.Rewards.Add((HitInfo)CopyValue(hit));
    }

    static GameState Start(Track track, MotionMode mode, MotionProfile profile = null)
    {
        var game = RaceSimulation.CreateTrial(track, mode, profile);
        Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "Both players ready");
        RaceSimulation.Advance(game, RaceConfig.CountdownDuration);
        Check(game.Phase == "racing", "Countdown reaches race");
        Near(game.Elapsed, 0, "Input clock excludes countdown");
        return game;
    }
    static void AdvanceTime(GameState game, double duration, double maxStep = .01)
    {
        Check(duration >= -1e-8, "Timed input must not precede previous input");
        while (duration > 1e-11 && game.Phase == "racing")
        {
            double dt = Math.Min(maxStep, duration);
            Step(game, dt);
            duration -= dt;
        }
    }
    static void MoveTo(GameState game, double target, double maxStep)
    {
        var player = game.Players[0];
        Check(target >= player.Distance - 1e-8, "Distance input must not precede current position");
        int guard = 0;
        while (player.Distance < target - 1e-8 && game.Phase == "racing")
        {
            if (++guard > 100000) throw new Exception("Input target stalled: " + player.Distance + " -> " + target);
            if (player.Recovering > 0)
            { Step(game, Math.Min(maxStep, player.Recovering)); continue; }
            double remaining = target - player.Distance;
            double dt = Math.Min(maxStep, remaining / player.CurrentSpeed);
            if (player.ActiveEffect != null) dt = Math.Min(dt, player.ActiveEffect.Remaining);
            // An event inside this interval can change horizontal speed. Only
            // intervals that can cross the target need a snapshot/bisection.
            if (remaining > dt * 440 + 1e-8)
            { Step(game, dt); continue; }
            var trial = Clone(game); RaceSimulation.Advance(trial, dt);
            if (trial.Players[0].Distance <= target + 1e-8)
            { Step(game, dt); continue; }
            double low = 0, high = dt;
            for (int i = 0; i < 55; i++)
            {
                double middle = (low + high) / 2;
                trial = Clone(game); RaceSimulation.Advance(trial, middle);
                if (trial.Players[0].Distance < target) low = middle; else high = middle;
            }
            Step(game, (low + high) / 2);
        }
        Near(player.Distance, target, "Exact input distance", 1e-7);
    }
    static void Tap(GameState game, RouteResult result = null, bool requireSupport = false)
    {
        var p = game.Players[0];
        if (requireSupport) Check(p.Support != SupportKind.None && !p.Flipping, "Safe input must use actual support");
        if (result != null)
        {
            CaptureReward(result);
            result.Inputs.Add(new InputSample { X = p.Distance, Time = game.Elapsed,
                CenterY = RaceSimulation.GetPlayerCenterY(p), VerticalSpeed = p.VerticalSpeed,
                Airborne = p.Flipping, AirFlipAvailable = p.AirFlipAvailable,
                Support = p.Support, SupportSide = p.SupportSide, SupportObstacleId = p.SupportObstacleId });
            if (p.Flipping) result.AirInputs++;
        }
        Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "Both action edges accepted at " + p.Distance);
    }
    static void Finish(GameState game, double maxStep, bool requireEmptyEffects = true)
    {
        int guard = 0;
        while (game.Phase == "racing" && guard++ < 100000) Step(game, maxStep);
        Check(game.Phase == "finished", "Race finishes");
        foreach (var p in game.Players)
        {
            Check(p.FinishTime.HasValue, "Both players reach finish");
            if (!requireEmptyEffects) continue;
            Check(p.ActiveEffect == null && p.PendingEffects.Count == 0, "Full earned effects finish before tape");
            Check(p.CompletedBoosts == p.BoostsEarned && p.CompletedSlows == p.SlowsEarned,
                "Completed pulse-ledger equals earned rewards");
        }
    }
    static RouteResult Route(Track track, MotionMode mode, MotionProfile profile, double[] route,
        double maxStep, bool requireSupport)
    {
        var result = new RouteResult { Game = Start(track, mode, profile) };
        foreach (double x in route)
        { MoveTo(result.Game, x, maxStep); Tap(result.Game, result, requireSupport); }
        Finish(result.Game, maxStep);
        CaptureReward(result);
        return result;
    }
    static RouteResult TimedShift(Track track, MotionMode mode, MotionProfile profile, double[] route,
        RouteResult baseline, int changed, double seconds, bool requireSupport)
    {
        var result = new RouteResult { Game = Start(track, mode, profile) };
        for (int i = 0; i < route.Length; i++)
        {
            if (i == changed) AdvanceTime(result.Game, baseline.Inputs[i].Time + seconds - result.Game.Elapsed);
            else MoveTo(result.Game, route[i], .01);
            Tap(result.Game, result, requireSupport);
        }
        Finish(result.Game, .03);
        CaptureReward(result);
        return result;
    }
    static void CheckClean(RouteResult result, int positives, int negatives, double expectedFinish)
    {
        foreach (var p in result.Game.Players)
        {
            Check(p.Collisions == 0, "Clean route: " + string.Join(",", p.HitObstacleIds));
            Check(p.BoostsEarned + p.FinishBoostsEarned == positives && p.SlowsEarned == negatives, "Exact reward counts");
            Near(p.FinishTime.Value, expectedFinish, "Actual finish time");
        }
        Check(Equals(result.Game.Winner, "tie"), "Symmetric inputs retain exact tie");
    }
}
