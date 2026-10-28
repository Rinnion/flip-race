using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FlipRace.Core;

static class Program
{
    static int checks, snapshots, tapeActions;
    static string context = "setup";
    static readonly List<object> evidence = new List<object>();
    static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(context + ": " + message); }
    static void Near(double actual, double expected, string message, double tolerance = 1e-8)
    { Check(!double.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected); }
    static Track Empty(double length = 7920) { return new Track(length, Array.Empty<Obstacle>()); }
    static GameState Solo(PartyCamera camera = PartyCamera.Constant, double left = -288.8, Track track = null, FlightSettings flight = null)
    { return RaceSimulation.CreateSolo(track ?? Empty(), flight ?? FlightPresets.Chosen, camera, left, 1520); }
    static void Start(GameState game)
    {
        for (int i = 0; i < game.Players.Length; i++) Check(RaceSimulation.Press(game, i), "ready accepted");
        Check(game.Phase == "countdown", "countdown begins after own readiness");
        RaceSimulation.Advance(game, 3);
        Check(game.Phase == "racing", "countdown ends"); Near(game.Elapsed, 0, "no active countdown time");
    }
    // Enumerate every public personal field, recursively including event records,
    // served effects and FIFO queues. Sets compare by membership, never order.
    static void Same(object actual, object expected, string path)
    {
        if (actual == null || expected == null) { Check(actual == expected, path + " null"); return; }
        Type type = actual.GetType(); Check(type == expected.GetType(), path + " type");
        if (type == typeof(double)) { Near((double)actual, (double)expected, path); return; }
        if (type.IsPrimitive || type.IsEnum || actual is string) { Check(actual.Equals(expected), path); return; }
        if (actual is HashSet<string> set) { Check(set.SetEquals((HashSet<string>)expected), path + " membership"); return; }
        if (actual is IEnumerable list)
        {
            object[] a = list.Cast<object>().ToArray(), b = ((IEnumerable)expected).Cast<object>().ToArray();
            Check(a.Length == b.Length, path + " length");
            for (int i = 0; i < a.Length; i++) Same(a[i], b[i], path + "[" + i + "]");
            return;
        }
        if (!fields.TryGetValue(type, out FieldInfo[] members))
            fields[type] = members = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        Check(members.Length > 0, path + " has comparable fields");
        foreach (FieldInfo member in members) Same(member.GetValue(actual), member.GetValue(expected), path + "." + member.Name);
    }
    static void Invalid(Action action, Type error)
    {
        try { action(); } catch (Exception e) { Check(e.GetType() == error, "validation exception " + error.Name); return; }
        Check(false, "validation must reject " + error.Name);
    }
    static void LifecycleAndValidation()
    {
        context = "SOLO-00/03/06 lifecycle and API";
        var track = RaceTracks.All[0]; var g = Solo(track: track);
        Check(g.IsSolo && !g.IsParty && g.UsesSharedRaceCamera && g.Mode == "solo" && g.Players.Length == 1, "explicit one-player mode");
        Check(ReferenceEquals(g.Track, track) && ReferenceEquals(g.Flight, FlightPresets.Chosen), "same track and settings objects");
        foreach (string name in new[] { "IsParty", "IsSolo", "PartyCamera", "PartyCameraStartLeft", "PartyWindowWidth", "PartyCameraSpeed" })
            Check(typeof(GameState).GetField(name).IsInitOnly, name + " immutable");
        foreach (int slot in new[] { -1, 1, 2, 3, 99 })
            Check(!RaceSimulation.Press(g, slot) && !RaceSimulation.SetCharacter(g, slot, 0), "inactive slot rejected " + slot);
        Check(g.Phase == "ready" && !g.Players[0].Ready, "invalid slots do not start");
        RaceSimulation.Advance(g, 50); Near(g.PartyCameraLeft, -288.8, "ready camera holds");
        RaceSimulation.Press(g, 0); Check(g.Phase == "countdown", "single press starts countdown");
        Check(!RaceSimulation.Press(g, 0), "countdown input rejected");
        RaceSimulation.Advance(g, 2.5); Near(g.Elapsed, 0, "countdown inactive"); Near(g.PartyCameraLeft, -288.8, "countdown camera holds");
        RaceSimulation.Advance(g, .75); Near(g.Elapsed, .25, "countdown remainder"); Near(g.Players[0].Distance, 55, "remainder movement");
        g.Phase = "paused"; RaceSimulation.Advance(g, 100); Near(g.Elapsed, .25, "pause inactive"); Near(g.PartyCameraLeft, -233.8, "paused camera holds");
        Check(!RaceSimulation.Press(g, 0), "pause input rejected"); g.Phase = "racing";
        foreach (double dt in new[] { double.NaN, double.PositiveInfinity, -1, 0 }) RaceSimulation.Advance(g, dt);
        Near(g.Elapsed, .25, "invalid delta ignored");
        foreach (var old in new[] { RaceSimulation.CreateGame(), RaceSimulation.CreateTrial(track, FlightPresets.Chosen) })
            Check(old.Players.Length == 2 && !old.IsSolo && !old.IsParty && !old.UsesSharedRaceCamera, "legacy API remains two-player");
        foreach (int count in new[] { 2, 3, 4 })
        { var p = RaceSimulation.CreateParty(track, FlightPresets.Chosen, count, PartyCamera.Constant, -288.8, 1520); Check(p.IsParty && !p.IsSolo && p.UsesSharedRaceCamera && p.Players.Length == count, "company retains count " + count); }
        foreach (int count in new[] { 0, 1, 5 }) Invalid(() => RaceSimulation.CreateParty(track, FlightPresets.Chosen, count, PartyCamera.Constant, 0, 1520), typeof(ArgumentOutOfRangeException));
        Invalid(() => RaceSimulation.CreateSolo(null, FlightPresets.Chosen, PartyCamera.Constant, 0, 1520), typeof(ArgumentNullException));
        Invalid(() => RaceSimulation.CreateSolo(track, null, PartyCamera.Constant, 0, 1520), typeof(ArgumentNullException));
        Invalid(() => Solo((PartyCamera)99), typeof(ArgumentOutOfRangeException));
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Invalid(() => Solo(left: value), typeof(ArgumentOutOfRangeException));
        foreach (double value in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        { Invalid(() => RaceSimulation.CreateSolo(track, FlightPresets.Chosen, PartyCamera.Constant, 0, value), typeof(ArgumentOutOfRangeException)); Invalid(() => Solo(track: Empty(value)), typeof(ArgumentOutOfRangeException)); }
    }
    static void BoundariesAndTerminal()
    {
        context = "SOLO-02 exact boundaries and terminal priority";
        foreach (double distance in new[] { -1, 0, .001, 2000 })
        {
            var g = Solo(left: 14); Start(g); g.Players[0].Distance = distance; RaceSimulation.Advance(g, .1);
            Check(g.Players[0].Status == (distance <= 0 ? PlayerStatus.Eliminated : PlayerStatus.Running), "full body left inclusive, partial/right exit alive " + distance);
            if (distance <= 0) { Check(g.Phase == "finished" && g.Winner == null, "elimination immediate"); Near(g.Elapsed, 0, "initial exit time"); }
        }
        foreach (double delta in new[] { -.01, 0, .01 }) foreach (double step in new[] { 1.0, 1d / 30, 1d / 240 })
        {
            var g = Solo(left: 15796 + 14 - 220 * (.2 + delta), track: Empty(15840)); Start(g); g.Players[0].Distance = 15796;
            for (double t = 0; t < 1 && g.Phase == "racing"; t += step) RaceSimulation.Advance(g, step);
            var p = g.Players[0];
            Check(p.Status == (delta < 0 ? PlayerStatus.Eliminated : PlayerStatus.Finished), "finish before/at/after left exit " + delta);
            Near(p.TerminalTime.Value, delta < 0 ? .2 + delta : .2, "exact terminal timestamp"); Near(g.Elapsed, p.TerminalTime.Value, "no 3.5 second grace");
            Check(g.Phase == "finished", "terminal immediately resolves");
            double left = g.PartyCameraLeft, distance = p.Distance; RaceSimulation.Advance(g, 100);
            Near(g.PartyCameraLeft, left, "result camera freezes"); Near(p.Distance, distance, "terminal motion freezes");
        }
        var late = Solo(left: -1000); Start(late); late.Players[0].Recovering = 2;
        RaceSimulation.Advance(late, 37); Check(late.Phase == "racing", "36 seconds is no time limit"); Near(late.PartyCameraLeft, -1000 + 220 * 37, "camera continues at L/36 after36");
        RaceSimulation.Advance(late, 10); Check(late.Phase == "finished", "eventually finish"); Near(late.Elapsed, 38, "exact delayed finish time");
        var lagger = Solo(PartyCamera.Lagger, 14); Start(lagger); lagger.Players[0].Recovering = 100;
        RaceSimulation.Advance(lagger, 40); Check(lagger.Phase == "racing" && lagger.Players[0].Status == PlayerStatus.Running, "lagger has no elimination even outside body boundary"); Near(lagger.PartyCameraLeft, 14, "lagger follows sole runner");
        RaceSimulation.Advance(lagger, 100); Check(lagger.Phase == "finished", "lagger reaches finish after errors"); Near(lagger.Elapsed, 136, "lagger finish active time");
    }
    static double[] Route(Track track, FlightSettings flight, string kind)
    {
        if (kind == "idle") return Array.Empty<double>();
        if (track.Id == TracksMixed.Id) return kind == "safe" ? TracksMixed.SafeRoute(flight) : kind == "negative" ? TracksMixed.ContentTrapRoute(flight) : kind == "coin" ? TracksMixed.CoinRoute(flight) : TracksMixed.FullRoute(flight);
        if (TracksRhythm.IsFull(track.Id)) return kind == "safe" ? TracksRhythm.SafeRoute(track.Id, flight) : kind == "negative" ? TracksRhythm.NegativeRoute(track.Id, flight) : TracksRhythm.FullRoute(track.Id, flight);
        return kind == "safe" ? TracksFull.SafeRoute(track.Id, flight) : kind == "negative" ? TracksFull.NegativeRoute(track.Id, flight) : TracksFull.FullRoute(track.Id, flight);
    }
    static void Compare(GameState solo, GameState party)
    {
        Same(solo.Players[0], party.Players[0], "personal"); snapshots++;
        Check(solo.Phase == party.Phase, "matching terminal phase with identical companions");
        Near(solo.Elapsed, party.Elapsed, "active elapsed"); Near(solo.PartyCameraLeft, party.PartyCameraLeft, "same logical camera");
    }
    static void FullTrackParity()
    {
        Check(RaceTracks.All.Length == 5, "all five current full tracks covered"); int serial = 0;
        foreach (var track in RaceTracks.All) foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
        foreach (PartyCamera camera in new[] { PartyCamera.Constant, PartyCamera.Lagger })
        foreach (string kind in (track.Id == TracksMixed.Id ? new[] { "idle", "safe", "full", "negative", "coin" } : new[] { "idle", "safe", "full", "negative" }))
        {
            context = "SOLO-01/06 " + track.Id + " " + flight.FirstAction + " " + camera + " " + kind;
            var solo = Solo(camera, track: track, flight: flight);
            var party = RaceSimulation.CreateParty(track, flight, 2 + serial++ % 3, camera, -288.8, 1520);
            Start(solo); Start(party); Compare(solo, party); int actions = 0;
            Action<double> advance = dt => { RaceSimulation.Advance(solo, dt); RaceSimulation.Advance(party, dt); Compare(solo, party); };
            // The distance witness only generates the tape. Both modes receive
            // precisely the same dt and input edge sequence; neither reads the
            // other mode's state. 440 is the public maximum physical speed.
            foreach (double target in Route(track, flight, kind))
            {
                int guard = 0;
                while (solo.Phase == "racing" && target - solo.Players[0].Distance > 2e-7)
                {
                    double recovery = solo.Players[0].Recovering;
                    advance(recovery > 0 ? Math.Min(.137, recovery) : Math.Min(.137, Math.Max(1e-9, (target - solo.Players[0].Distance) / 440)));
                    Check(++guard < 10000, "distance tape guard");
                }
                if (solo.Phase != "racing") break;
                bool accepted = RaceSimulation.Press(solo, 0);
                for (int i = 0; i < party.Players.Length; i++) Check(RaceSimulation.Press(party, i) == accepted, "same input acceptance");
                Compare(solo, party); actions++; tapeActions++;
            }
            int endGuard = 0;
            while (solo.Phase == "racing") { advance(.137); Check(++endGuard < 3000, "finish tape guard"); }
            Check(solo.Players[0].Status != PlayerStatus.Running, "tape reaches terminal");
            if (camera == PartyCamera.Lagger) Check(solo.Players[0].Status == PlayerStatus.Finished, "no elimination alternative completes full track");
            var p = solo.Players[0];
            evidence.Add(new { track = track.Id, profile = flight.FirstAction.ToString(), camera = camera.ToString(), kind, partyCount = party.Players.Length, actions, status = p.Status.ToString(), time = p.TerminalTime, p.Collisions, p.BoostsEarned, p.SlowsEarned, p.CoinsEarned, p.FinishBoostsEarned, collected = p.CollectedRiskIds.OrderBy(x => x).ToArray(), hits = p.HitObstacleIds.OrderBy(x => x).ToArray() });
        }
    }
    static void PersonalTerminationAndRepeat()
    {
        context = "SOLO-03 personal termination, rewards and clean repeat";
        foreach (PartyCamera camera in new[] { PartyCamera.Constant, PartyCamera.Lagger }) foreach (bool eliminate in new[] { false, true })
        {
            if (eliminate && camera == PartyCamera.Lagger) continue;
            var solo = Solo(camera, left: eliminate ? -30 : -10000); Start(solo);
            var party = RaceSimulation.CreateParty(solo.Track, solo.Flight, 2, camera, solo.PartyCameraStartLeft, 1520); Start(party);
            foreach (var game in new[] { solo, party })
            { game.Players[0].Distance = eliminate ? 0 : 7700; game.Players[0].Recovering = eliminate ? .5 : 0; }
            party.Players[1].Recovering = 10; if (eliminate) party.Players[1].Distance = 6000;
            RaceSimulation.Advance(solo, 5); RaceSimulation.Advance(party, 5);
            Check(solo.Phase == "finished" && party.Phase == "racing", "solo immediate, unrelated companion cannot shorten/extend personal attempt");
            Same(solo.Players[0], party.Players[0], "personal terminal despite unequal game clocks");
            Check(party.Elapsed >= 5 && solo.Elapsed < 2, "shared race can continue past old grace");
            RaceSimulation.Advance(party, 4); Same(solo.Players[0], party.Players[0], "personal remains frozen while companion runs");
            Check(!RaceSimulation.Press(party, 0), "terminal racing participant cannot act");
        }
        var track = Empty();
        track.RiskZones = new[] { new RiskZone { Id = "coin", Kind = "coin", X = 60, Y = 226 }, new RiskZone { Id = "boost", Kind = "instant", X = 100, Y = 226 }, new RiskZone { Id = "slow", Kind = "negative", X = 200, Y = 226 }, new RiskZone { Id = "dash", Kind = "finish", X = 600, Y = 120 } };
        var dirty = Solo(PartyCamera.Lagger, track: track); RaceSimulation.SetCharacter(dirty, 0, 5); Start(dirty);
        RaceSimulation.Advance(dirty, 1); Check(dirty.Players[0].CoinsEarned == 1 && dirty.Players[0].BoostsEarned > 0 && dirty.Players[0].SlowsEarned > 0, "real personal rewards collected");
        RaceSimulation.Press(dirty, 0); RaceSimulation.Advance(dirty, 100); Check(dirty.Phase == "finished", "reward attempt finishes");
        var p0 = dirty.Players[0]; p0.HitObstacleIds.Add("old-hit"); p0.LastHit = new HitInfo { Id = "old-hit", At = .1 }; p0.Collisions++;
        p0.ActiveEffect = new SpeedEffect { RingId = "old-active", Remaining = .2 }; p0.PendingEffects.Enqueue(new SpeedEffect { RingId = "old-queue", Remaining = .6 });
        p0.SuccessfulRiskIds.Add("old-success"); p0.FailedRiskIds.Add("old-failure"); p0.NegativeRiskIds.Add("old-negative");
        p0.FinishBoostsEarned = 1; p0.LastFinishBoost = new HitInfo { Id = "old-dash" }; p0.LastSlow = new HitInfo { Id = "old-slow" };
        RaceSimulation.Press(dirty, 0); Check(dirty.Phase == "countdown" && dirty.Players.Length == 1, "single replay edge resets immediately");
        var fresh = Solo(PartyCamera.Lagger, track: track); RaceSimulation.SetCharacter(fresh, 0, 5); RaceSimulation.Press(fresh, 0);
        Same(dirty.Players[0], fresh.Players[0], "every field clean on repeat"); Near(dirty.Elapsed, 0, "repeat resets elapsed"); Near(dirty.PartyCameraLeft, -288.8, "repeat resets camera");
        Check(dirty.Winner == null && dirty.WinnerAt == null && ReferenceEquals(dirty.Track, track) && ReferenceEquals(dirty.Flight, fresh.Flight), "repeat preserves selections and clears result");
        Near(dirty.CountdownRemaining, 3, "repeat resets countdown");
    }
    static int Main(string[] args)
    {
        try
        {
            LifecycleAndValidation(); BoundariesAndTerminal(); FullTrackParity(); PersonalTerminationAndRepeat();
            var report = new { result = "PASS", checks, snapshots, tapeActions, cases = evidence.Count, coverage = "SOLO-00/01/02/03/06; VIEW/CONTROL/REWARD/FINISH/GATE preservation", evidence };
            if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("SoloCheck PASS: " + checks + " assertions; " + snapshots + " complete personal snapshots; " + tapeActions + " tape actions; " + evidence.Count + " full-track cases."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
