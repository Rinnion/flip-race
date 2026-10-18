using System;
using System.Collections.Generic;
using System.Reflection;
using FlipRace.Core;

public static partial class TracksIndependentAudit
{
    static GameState CopyGame(GameState source)
    {
        var copy = new GameState();
        foreach (var field in typeof(GameState).GetFields(BindingFlags.Instance | BindingFlags.Public))
            field.SetValue(copy, field.GetValue(source));
        copy.Players = new PlayerState[source.Players.Length];
        for (int i = 0; i < copy.Players.Length; i++)
        {
            var p = source.Players[i]; var q = new PlayerState(p.Id);
            foreach (var field in typeof(PlayerState).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                object value = field.GetValue(p);
                if (value is HashSet<string>) value = new HashSet<string>((HashSet<string>)value);
                else if (value is SpeedEffect) value = CopyEffect((SpeedEffect)value);
                else if (value is Queue<SpeedEffect>)
                {
                    var queue = new Queue<SpeedEffect>();
                    foreach (var effect in (Queue<SpeedEffect>)value) queue.Enqueue(CopyEffect(effect));
                    value = queue;
                }
                field.SetValue(q, value);
            }
            copy.Players[i] = q;
        }
        return copy;
    }
    static SpeedEffect CopyEffect(SpeedEffect source)
    {
        if (source == null) return null;
        var copy = new SpeedEffect();
        foreach (var field in typeof(SpeedEffect).GetFields(BindingFlags.Instance | BindingFlags.Public))
            field.SetValue(copy, field.GetValue(source));
        return copy;
    }
    static double[] InputTimes(Track track, double[] route)
    {
        var g = Start(track); var times = new double[route.Length];
        for (int i = 0; i < route.Length; i++)
        { MoveTo(g, route[i], .01); times[i] = g.Elapsed; Tap(g); }
        return times;
    }
    static bool AdvanceUntilHit(GameState g, double targetTime)
    {
        while (g.Phase == "racing" && g.Elapsed < targetTime - 1e-12)
        {
            RaceSimulation.Advance(g, Math.Min(.005, targetTime - g.Elapsed));
            if (g.Players[0].Collisions > 0) return true;
        }
        return g.Players[0].Collisions > 0;
    }
    static GameState ShiftToFirstHit(Track track, double[] route, double[] times, int changed, double shift)
    {
        var g = Start(track);
        for (int i = 0; i < route.Length && g.Phase == "racing"; i++)
        {
            if (i == changed)
            {
                double when = times[i] + shift;
                if (when < g.Elapsed - 1e-10) return null;
                if (AdvanceUntilHit(g, when)) return g;
            }
            else
            {
                while (g.Phase == "racing" && route[i] - g.Players[0].Distance > 1e-8)
                {
                    double dt = Math.Min(.005, (route[i] - g.Players[0].Distance) / 440);
                    RaceSimulation.Advance(g, dt);
                    if (g.Players[0].Collisions > 0) return g;
                }
                if (route[i] < g.Players[0].Distance - 1e-6) continue;
            }
            if (g.Phase == "racing") Tap(g);
        }
        return AdvanceUntilHit(g, 100) ? g : null;
    }
    static bool TryClear(GameState original, double target, int hits)
    {
        var g = CopyGame(original);
        while (g.Phase == "racing" && g.Players[0].Distance < target - 1e-8)
        {
            RaceSimulation.Advance(g, Math.Min(.02, (target - g.Players[0].Distance) / 440));
            if (g.Players[0].Collisions != hits) return false;
        }
        return g.Players[0].Collisions == hits && g.Players[0].Distance >= target - 1e-8;
    }
    static bool RecoveryEscape(GameState collision, out string witness)
    {
        witness = ""; var g = CopyGame(collision); var p = g.Players[0]; int hits = p.Collisions;
        int hitIndex = Array.FindIndex(g.Track.Obstacles, o => o.Id == p.LastHit.Id);
        C(hitIndex >= 0, "actual hit has an authored obstacle");
        if (hitIndex + 1 == g.Track.Obstacles.Length) { witness = "last obstacle; no next obstacle"; return true; }
        var next = g.Track.Obstacles[hitIndex + 1]; double target = next.X + next.Width + Radius + 1;
        M(g, p.Recovering + .15, .01);
        if (p.Collisions != hits) return false;
        if (TryClear(g, target, hits)) { witness = "stay reaches " + target; return true; }
        for (int tick = 0; tick <= 40; tick++)
        {
            double delay = tick * .05; var one = CopyGame(g); M(one, delay, .02);
            if (one.Phase != "racing" || one.Players[0].Collisions != hits) continue;
            if (!RaceSimulation.Press(one, 0) || !RaceSimulation.Press(one, 1)) continue;
            if (TryClear(one, target, hits)) { witness = "tap +" + delay + " reaches " + target; return true; }
            for (int secondTick = 3; secondTick <= 36; secondTick++)
            {
                double second = secondTick * .05; var two = CopyGame(one); M(two, second, .02);
                if (two.Phase != "racing" || two.Players[0].Collisions != hits) continue;
                if (!RaceSimulation.Press(two, 0) || !RaceSimulation.Press(two, 1)) continue;
                if (!TryClear(two, target, hits)) continue;
                witness = "taps +" + delay + ", then +" + second + " reach " + target; return true;
            }
        }
        return false;
    }
    public static void RunFullRecovery()
    {
        int samples = 0, failures = 0;
        foreach (var track in TracksFull.All) foreach (bool full in new[] { false, true })
        {
            var route = full ? track.RiskRoute : track.SafeRoute;
            var times = InputTimes(track, route); int local = 0;
            for (int index = 0; index < route.Length; index++)
            foreach (double shift in new[] { -.5, -.25, -.1, .1, .25, .5 })
            {
                context = track.Id + (full ? " full" : " safe") + " input=" + index + " shift=" + shift;
                var hit = ShiftToFirstHit(track, route, times, index, shift);
                if (hit == null) continue;
                samples++; local++; string witness;
                bool escaped = RecoveryEscape(hit, out witness);
                if (!escaped)
                {
                    failures++;
                    Console.WriteLine("NO_ESCAPE " + context + " at=" + hit.Players[0].LastHit.At + " hit=" + hit.Players[0].LastHit.Id
                        + " " + StateLine(hit.Players[0]));
                }
                else if (local <= 2) Console.WriteLine("RECOVERY " + context + " " + witness);
            }
            Console.WriteLine("RECOVERY SAMPLES " + track.Id + (full ? " full " : " safe ") + local);
        }
        C(failures == 0, "sampled recovery has " + failures + " cases without found escape");
        Console.WriteLine("PASS independent full recovery: " + samples + " actual collision states; remaining recovery +150ms, then up to two real taps; explicit next trailing reach.");
    }
}
