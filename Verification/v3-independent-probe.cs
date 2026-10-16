using System;
using System.Collections.Generic;
using FlipRace.Core;

// Independent, read-only verification harness. Compile together with the actual
// Core and TracksV3 sources; no Unity API and no changes to runtime.
public static class IndependentV3Probe
{
    static int assertions;
    static void Check(bool value, string why)
    {
        assertions++;
        if (!value) throw new Exception(why);
    }
    static void Near(double actual, double expected, string why)
    {
        Check(Math.Abs(actual - expected) < 1e-8, why + ": " + actual + " != " + expected);
    }
    static GameState Start(Track track)
    {
        var game = RaceSimulation.CreateGame();
        game.Track = track;
        RaceSimulation.Press(game, 0);
        RaceSimulation.Press(game, 1);
        RaceSimulation.Advance(game, 3);
        return game;
    }
    static Track Cluster(int count, int mask, double length = 2200, bool colocated = false)
    {
        var rings = new List<RiskZone>();
        for (int i = 0; i < count; i++)
        {
            double x = 100 + (colocated ? 0 : i * 8);
            rings.Add(new RiskZone { Id = "ring-" + i, Kind = (mask & (1 << i)) != 0 ? "negative" : "positive",
                X = x, Y = 226, Radius = 6, ClearX = x - 20 });
        }
        return new Track(length, Array.Empty<Obstacle>()) { RiskZones = rings.ToArray(), NegativeEnabled = true };
    }
    static void Finish(GameState game, double dt)
    {
        int guard = 0;
        while (game.Phase == "racing")
        {
            RaceSimulation.Advance(game, dt);
            if (++guard > 1000000) throw new Exception("finish stalled");
        }
    }
    static void MoveTo(GameState game, double target, double dt = 1d / 144)
    {
        int guard = 0;
        while (game.Players[0].Distance < target - 1e-7 && game.Phase == "racing")
        {
            // An upper speed bound prevents crossing the next input position,
            // including a queued boost beginning within this step.
            RaceSimulation.Advance(game, Math.Min(dt, (target - game.Players[0].Distance) / 330));
            if (++guard > 1000000) throw new Exception("route stalled");
        }
    }
    static void TapBoth(GameState game)
    {
        Check(RaceSimulation.Press(game, 0), "route accepts player 0 action");
        Check(RaceSimulation.Press(game, 1), "route accepts player 1 action");
    }
    static void ExhaustiveSignedClusters()
    {
        foreach (double dt in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100 })
            for (int count = 1; count <= 5; count++)
                for (int mask = 0; mask < (1 << count); mask++)
                {
                    int negative = 0;
                    for (int i = 0; i < count; i++) if ((mask & (1 << i)) != 0) negative++;
                    var game = Start(Cluster(count, mask));
                    Finish(game, dt);
                    double expected = 10 + (2 * negative - count) * .3;
                    foreach (var p in game.Players)
                    {
                        Near(p.FinishTime.Value, expected, "signed cluster result");
                        Check(p.BoostsEarned == count - negative && p.SlowsEarned == negative, "every impulse earned once");
                        Check(p.CompletedBoosts == count - negative && p.CompletedSlows == negative, "all impulses served");
                        Check(p.ActiveEffect == null && p.PendingEffects.Count == 0, "empty effect queue");
                        Check(p.Collisions == 0 && p.Distance == 2200, "physical clean finish");
                    }
                    Near(game.Elapsed, expected, "actual time agrees with canonical finish");
                    Check(game.Players[0].FinishTime == game.Players[1].FinishTime && Equals(game.Winner, "tie"), "exact symmetric tie");
                }
    }
    static double ExplicitArrival(double length, int count, int mask)
    {
        double seconds = 80d / 220, remaining = length - 80;
        for (int i = 0; i < count && remaining > 0; i++)
        {
            double speed = (mask & (1 << i)) != 0 ? 110 : 330;
            double moved = Math.Min(remaining, speed * .6);
            seconds += moved / speed;
            remaining -= moved;
        }
        return seconds + remaining / 220;
    }
    static void QueueOrderAndPartialFinish()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            var order = Start(Cluster(4, mask, 2200, true));
            RaceSimulation.Advance(order, 80d / 220 + .001);
            var player = order.Players[0];
            Check(player.ActiveEffect.RingId == "ring-0", "same-time first effect stable");
            var queued = player.PendingEffects.ToArray();
            Check(queued.Length == 3, "same-time queue contains every remaining effect");
            for (int i = 0; i < 3; i++) Check(queued[i].RingId == "ring-" + (i + 1), "same-time FIFO order");
            double edge = 80;
            var lengths = new List<double> { 80.001, 100, 200 };
            for (int i = 0; i < 4; i++)
            {
                edge += ((mask & (1 << i)) != 0 ? 110 : 330) * .6;
                lengths.Add(edge - .000001);
                lengths.Add(edge);
                lengths.Add(edge + .000001);
            }
            foreach (double length in lengths)
                foreach (double dt in new[] { 1d / 60, .317, 100 })
                {
                    var game = Start(Cluster(4, mask, length, true));
                    Finish(game, dt);
                    double expected = ExplicitArrival(length, 4, mask);
                    Near(game.Players[0].FinishTime.Value, expected, "partial effect/queue finish timestamp");
                    Near(game.Elapsed, expected, "partial physical arrival");
                    Check(game.Players[0].Distance == length && Equals(game.Winner, "tie"), "partial finish exact distance and tie");
                    Check(game.Players[0].BoostsEarned + game.Players[0].SlowsEarned == 4, "same-time events all collected before short finish");
                }
        }
    }
    static void RecoveryAndSlowControl()
    {
        var slow = Start(Cluster(1, 1));
        RaceSimulation.Advance(slow, .4);
        Check(slow.Players[0].SlowRemaining > 0 && slow.Players[0].CurrentSpeed == 110, "slow actual speed positive");
        TapBoth(slow);
        RaceSimulation.Advance(slow, .8);
        Check(slow.Players[0].Y == 0 && !slow.Players[0].Flipping, "slow does not block full flip");
        var track = Cluster(3, 2);
        track.Obstacles = new[] { new Obstacle { Id = "hit", X = 170, Width = 64, Height = 48, Kind = "floor" } };
        var game = Start(track);
        while (game.Players[0].Recovering == 0) RaceSimulation.Advance(game, .001);
        var p = game.Players[0];
        double distance = p.Distance, activeRemaining = p.ActiveEffect.Remaining, activeElapsed = p.ActiveEffect.Elapsed;
        int waiting = p.PendingEffects.Count;
        Check(waiting == 2, "collision occurs with queued opposite effects");
        RaceSimulation.Advance(game, p.Recovering * .5);
        Check(p.Distance == distance && p.ActiveEffect.Remaining == activeRemaining && p.ActiveEffect.Elapsed == activeElapsed
            && p.PendingEffects.Count == waiting, "recovery freezes active and queued effects");
        RaceSimulation.Advance(game, 0);
        Check(p.ActiveEffect.Remaining == activeRemaining && p.PendingEffects.Count == waiting, "pause freezes queue");
        Finish(game, 100);
        Near(game.Players[0].FinishTime.Value, 10.2, "recovery plus two boosts and one slow");
        Near(game.Elapsed, game.Players[0].FinishTime.Value, "recovery physical time");
        RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1);
        Check(game.Players[0].ActiveEffect == null && game.Players[0].PendingEffects.Count == 0
            && game.Players[0].NegativeRiskIds.Count == 0 && game.Players[0].SlowsEarned == 0, "rematch clears new state");
    }
    static void AfterPendingAndGeometry()
    {
        foreach (bool negative in new[] { false, true })
        {
            var track = TracksV3.StageOne(negative);
            var game = Start(track);
            int next = 0; bool sawPending = false;
            while (game.Phase == "racing")
            {
                if (next < track.RiskRoute.Length && game.Players[0].Distance >= track.RiskRoute[next] - 1e-7)
                { TapBoth(game); next++; }
                var p = game.Players[0];
                if (p.CollectedRiskIds.Contains("series-after-ring") && !p.SuccessfulRiskIds.Contains("series-after-ring"))
                {
                    sawPending = true;
                    Check(p.Flipping && p.BoostsEarned == 1, "after ring remains pending during return");
                }
                double step = 1d / 144;
                if (next < track.RiskRoute.Length) step = Math.Min(step, (track.RiskRoute[next] - p.Distance) / 330);
                RaceSimulation.Advance(game, step);
            }
            Check(next == 6 && sawPending, "six-action series includes pending after ring");
            Check(game.Players[0].BoostsEarned == 2 && game.Players[0].SlowsEarned == 0 && game.Players[0].Collisions == 0, "prototype all positives avoids negative");
            Near(game.Players[0].FinishTime.Value, track.Length / 220 - .6, "prototype actual ideal result");
            Near(game.Elapsed, game.Players[0].FinishTime.Value, "prototype physical arrival");
        }
        var off = TracksV3.StageOne(false); var on = TracksV3.StageOne(true);
        Check(off.Length == on.Length && off.Obstacles.Length == on.Obstacles.Length, "toggle preserves track size");
        for (int i = 0; i < off.Obstacles.Length; i++)
        {
            var a = off.Obstacles[i]; var b = on.Obstacles[i];
            Check(a.Id == b.Id && a.X == b.X && a.Width == b.Width && a.Height == b.Height && a.Kind == b.Kind, "toggle preserves obstacle geometry");
        }
        Check(on.RiskZones.Length == off.RiskZones.Length + 1, "prototype toggle adds one negative");
        for (int i = 0; i < off.RiskZones.Length; i++)
        {
            var a = off.RiskZones[i]; var b = on.RiskZones[i];
            Check(a.Id == b.Id && a.X == b.X && a.Y == b.Y && a.Radius == b.Radius && a.ClearX == b.ClearX, "toggle preserves positive geometry");
        }
    }
    public static string Run()
    {
        assertions = 0;
        ExhaustiveSignedClusters();
        QueueOrderAndPartialFinish();
        RecoveryAndSlowControl();
        AfterPendingAndGeometry();
        return "PASS: independent V3 " + assertions + " assertions; signed FIFO clusters, simultaneous events, physical/partial finish, recovery, slow control, after-ring pending and unchanged toggle geometry.";
    }
}
