using System;
using FlipRace.Core;

static class Program
{
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static void Near(double actual, double expected, string message, double tolerance = 1e-8)
    { Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected); }
    static Track Empty(double length = 7920) { return new Track(length, Array.Empty<Obstacle>()); }
    static GameState Party(int count = 4, PartyCamera camera = PartyCamera.Constant, double start = -100, Track track = null)
    { return RaceSimulation.CreateParty(track ?? Empty(), FlightPresets.Chosen, count, camera, start, 800); }
    static void Start(GameState game)
    {
        for (int i = 0; i < game.Players.Length; i++) Check(RaceSimulation.Press(game, i), "ready accepted");
        Check(game.Phase == "countdown", "all ready countdown");
        RaceSimulation.Advance(game, 3); Check(game.Phase == "racing", "countdown end"); Near(game.Elapsed, 0, "countdown inactive");
    }
    static void Run(GameState game, double duration, double step)
    { while (duration > 1e-12) { double dt = Math.Min(duration, step); RaceSimulation.Advance(game, dt); duration -= dt; } }
    static void ReadinessAndRematch()
    {
        for (int count = 2; count <= 4; count++)
        {
            var g = Party(count, PartyCamera.Lagger);
            Check(g.IsParty && g.Players.Length == count, "party count");
            for (int i = 0; i < count; i++) Check(RaceSimulation.SetCharacter(g, i, 5 - i), "select character");
            Check(!RaceSimulation.SetCharacter(g, count, 1) && !RaceSimulation.Press(g, count), "reject invalid slot");
            for (int i = 0; i < count - 1; i++) RaceSimulation.Press(g, i);
            Check(g.Phase == "ready", "wait for last ready");
            RaceSimulation.Press(g, count - 1); RaceSimulation.Advance(g, 3.25);
            Near(g.Elapsed, .25, "countdown remainder"); Near(g.Players[0].Distance, 55, "countdown remainder motion");
            RaceSimulation.Advance(g, 100); Check(g.Phase == "finished" && Equals(g.Winner, "tie"), "all-way tie");
            Near(g.Elapsed, 36, "stop clock at final finish");
            for (int i = 0; i < count; i++)
            {
                Check(g.Players[i].Status == PlayerStatus.Finished, "finished status");
                Near(g.Players[i].FinishTime.Value, 36, "finish timestamp");
                RaceSimulation.Press(g, i);
            }
            Check(g.Phase == "countdown" && g.Elapsed == 0 && g.Winner == null, "rematch reset");
            for (int i = 0; i < count; i++)
            {
                Check(g.Players[i].Status == PlayerStatus.Running && g.Players[i].TerminalTime == null, "terminal state reset");
                Check(g.Players[i].CharacterIndex == 5 - i && g.Players[i].CollectedRiskIds.Count == 0, "characters retained scores reset");
            }
        }
        Check(RaceSimulation.CreateGame().Players.Length == 2 && !RaceSimulation.CreateGame().IsParty, "old modes remain two");
        foreach (int invalid in new[] { 1, 5 }) { bool rejected = false; try { Party(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; } Check(rejected, "invalid count rejected"); }
    }
    static void CameraAndPause()
    {
        var g = Party(); Start(g); Near(g.PartyCameraLeft, -100, "initial camera");
        RaceSimulation.Advance(g, 1); Near(g.PartyCameraLeft, 120, "L/36 camera");
        g.Phase = "paused"; RaceSimulation.Advance(g, 10); Near(g.Elapsed, 1, "pause clock"); Near(g.PartyCameraLeft, 120, "pause camera");
        g.Phase = "racing"; RaceSimulation.Advance(g, double.NaN); RaceSimulation.Advance(g, -1); Near(g.Elapsed, 1, "invalid dt ignored");
        // A deliberately wide initial window keeps delayed runners alive past 36 seconds.
        g = Party(2, PartyCamera.Constant, -1000); Start(g);
        g.Players[0].Recovering = 2; g.Players[1].Recovering = 2;
        RaceSimulation.Advance(g, 37); Check(g.Phase == "racing", "after36 still active"); Near(g.PartyCameraLeft, -1000 + 220 * 37, "camera continues after36");
        RaceSimulation.Advance(g, 3); Near(g.Elapsed, 38, "freeze at allterminal"); double left = g.PartyCameraLeft;
        RaceSimulation.Advance(g, 10); Near(g.PartyCameraLeft, left, "finished camera freezes");
    }
    static void Boundaries()
    {
        var g = Party(4, PartyCamera.Constant, 14); Start(g);
        g.Players[0].Distance = 0; g.Players[1].Distance = .001; g.Players[2].Distance = -1; g.Players[3].Distance = 2000;
        RaceSimulation.Advance(g, .1);
        Check(g.Players[0].Status == PlayerStatus.Eliminated && g.Players[2].Status == PlayerStatus.Eliminated, "equality and fullbody outside eliminate");
        Near(g.Players[0].TerminalTime.Value, 0, "initial equality exact instant");
        Check(g.Players[1].Status == PlayerStatus.Running, "partial body remains");
        Check(g.Players[3].Status == PlayerStatus.Running && g.Players[3].Distance > g.PartyCameraLeft + g.PartyWindowWidth, "right exit never eliminates");
        double distance = g.Players[0].Distance; Check(!RaceSimulation.Press(g, 0), "eliminated input rejected");
        RaceSimulation.Advance(g, 1); Near(g.Players[0].Distance, distance, "eliminated motion frozen");
        g = Party(2, PartyCamera.Constant, -30); Start(g);
        foreach (var p in g.Players) p.Recovering = .5;
        RaceSimulation.Advance(g, 1); Check(g.Phase == "finished" && g.Winner == null, "all eliminated has no winner");
        foreach (var p in g.Players) { Near(p.TerminalTime.Value, .2, "recovery elimination exact"); Near(p.Distance, 0, "no post elimination motion"); }
        Near(g.Elapsed, .2, "all eliminated clock exact");
    }
    static GameState Priority(double start, bool reverse = false)
    {
        var g = Party(2, PartyCamera.Constant, start, Empty(15840)); Start(g);
        foreach (var p in g.Players) p.Distance = 15796;
        if (reverse) Array.Reverse(g.Players);
        return g;
    }
    static void EventPriorityAndDt()
    {
        // v_camera=440; finish in .2 seconds, body boundary gap arranged at .2 +/- .01.
        foreach (double delta in new[] { -.01, 0, .01 })
        {
            double start = 15796 + 14 - 220 * (.2 + delta);
            foreach (double step in new[] { 1.0, 1.0 / 30, 1.0 / 240 })
            {
                var g = Priority(start, step < .01); Run(g, 1, step);
                var expected = delta < 0 ? PlayerStatus.Eliminated : PlayerStatus.Finished;
                foreach (var p in g.Players)
                { Check(p.Status == expected, "finish/eliminate priority " + delta + " dt=" + step); Near(p.TerminalTime.Value, delta < 0 ? .2 + delta : .2, "priority timestamp"); }
            }
        }
        // Slow expiry at .1 changes catch-up time; reward/speed queue changes are swept.
        foreach (double step in new[] { 1.0, .01 })
        {
            var g = Party(2, PartyCamera.Constant, -8); Start(g);
            foreach (var p in g.Players) p.ActiveEffect = new SpeedEffect { RingId = "slow", IsNegative = true, Remaining = .1 };
            Run(g, .5, step); Check(g.Players[0].Status == PlayerStatus.Running, "survive slow before boundary"); Near(g.Players[0].Distance, 99, "piecewise speed integration");
        }
    }
    static void OutcomesAndLagger()
    {
        foreach (PartyCamera mode in new[] { PartyCamera.Constant, PartyCamera.Lagger })
        {
            var g = Party(4, mode, -10000); Start(g);
            g.Players[0].Distance = 7700; g.Players[1].Distance = 7000; g.Players[2].Recovering = 10; g.Players[3].Recovering = 5;
            RaceSimulation.Advance(g, 6); Check(g.Phase == "racing" && Equals(g.Winner, 0), "no old grace timeout " + mode);
            Check(g.Players[0].Status == PlayerStatus.Finished && !RaceSimulation.Press(g, 0), "finisher terminal and input protected");
            Near(g.Players[0].Distance, 7920, "finisher frozen");
            if (mode == PartyCamera.Lagger) Near(g.PartyCameraLeft, -10000, "lagger excludes finishers");
            RaceSimulation.Advance(g, 100); Check(g.Phase == "finished" && Equals(g.Winner, 0), "wait all outcomes and preserve winner");
        }
        var lone = Party(3, PartyCamera.Constant, 14); Start(lone);
        lone.Players[2].Distance = 1000; RaceSimulation.Advance(lone, .1);
        Check(lone.Phase == "racing" && lone.Winner == null && lone.Players[2].Status == PlayerStatus.Running, "lone runner gets no automatic win");
        var lag = Party(3, PartyCamera.Lagger, 14); Start(lag); foreach (var p in lag.Players) p.Recovering = 100;
        RaceSimulation.Advance(lag, 10); foreach (var p in lag.Players) Check(p.Status == PlayerStatus.Running, "lagger no elimination even full body left");
    }
    static void Rewards()
    {
        var track = Empty(); track.RiskZones = new[] { new RiskZone { Id = "coin", Kind = "coin", X = 100, Y = 226, Radius = 6 } };
        var g = Party(4, PartyCamera.Constant, -100, track); Start(g);
        g.Players[1].Y = 0; g.Players[2].Y = 0; g.Players[3].Y = 0;
        RaceSimulation.Advance(g, .5); Check(g.Players[0].CoinsEarned == 1, "first personal coin");
        for (int i = 1; i < 4; i++) Check(g.Players[i].CoinsEarned == 0 && !g.Players[i].CollectedRiskIds.Contains("coin"), "other rewards untouched");
        // Other players independently claim the same coin later.
        for (int i = 1; i < 4; i++) { g.Players[i].Distance = 0; g.Players[i].Y = 1; }
        RaceSimulation.Advance(g, .5); for (int i = 0; i < 4; i++) Check(g.Players[i].CoinsEarned == 1, "all four personal collection");
        // At t=.2, recovering runner is eliminated before the overlapping coin can ever act.
        g = Party(2, PartyCamera.Constant, -30, track); Start(g);
        g.Players[0].Recovering = .5; RaceSimulation.Advance(g, 1);
        Check(g.Players[0].Status == PlayerStatus.Eliminated && g.Players[0].CoinsEarned == 0, "terminal prevents subsequent rewards");
        Check(g.Players[1].CoinsEarned == 1 && g.Players[1].Status == PlayerStatus.Running, "elimination personal isolation");
        // Coin at the same exact instant as full-body exit must not be awarded.
        track = Empty(15840); track.RiskZones = new[] { new RiskZone { Id = "edge-coin", Kind = "coin", X = 64, Y = 226, Radius = 6 } };
        g = Party(2, PartyCamera.Constant, -30, track); Start(g); RaceSimulation.Advance(g, 1);
        foreach (var p in g.Players) Check(p.Status == PlayerStatus.Eliminated && p.CoinsEarned == 0, "no reward at terminal boundary");
    }
    static void CollisionsAndEffects()
    {
        foreach (double step in new[] { 2.0, .01, 1.0 / 240 })
        {
            var track = Empty();
            track.Obstacles = new[] { new Obstacle { Id = "rock", X = 100, Width = 64, Height = 68, Kind = "floor" } };
            track.RiskZones = new[] { new RiskZone { Id = "later", Kind = "coin", X = 300, Y = 226, Radius = 6 } };
            var g = Party(2, PartyCamera.Constant, -80, track); Start(g); g.Players[1].Y = 0;
            Run(g, 2, step);
            Check(g.Players[0].Status == PlayerStatus.Eliminated && g.Players[0].Collisions == 1, "collision then swept recovery elimination");
            Near(g.Players[0].TerminalTime.Value, 180.0 / 220, "collision elimination exact timestamp");
            Near(g.Players[0].Distance, 86, "stopped collision location");
            Check(g.Players[0].CoinsEarned == 0 && g.Players[1].Collisions == 0 && g.Players[1].Status == PlayerStatus.Running,
                "collision terminal and personal isolation");
            // A real slow starts before the obstacle; the moving camera catches
            // the runner before impact, so the future penalty must not be applied.
            track.RiskZones = new[] { new RiskZone { Id = "slow", Kind = "negative", X = 50, Y = 226, Radius = 6 } };
            g = Party(2, PartyCamera.Constant, -20, track); Start(g); Run(g, 2, step);
            foreach (var p in g.Players)
            {
                Check(p.Status == PlayerStatus.Eliminated && p.SlowsEarned == 1 && p.Collisions == 0,
                    "slow then elimination precedes collision");
                Near(p.TerminalTime.Value, 30.0 / 220 + 34.0 / 110, "speed change recomputes boundary time");
                Near(p.Distance, 64, "exact slow elimination distance");
                Near(p.ActiveEffect.Elapsed, 34.0 / 110, "effect not served after elimination");
            }
            track.Obstacles = Array.Empty<Obstacle>();
            track.RiskZones = new[] { new RiskZone { Id = "boost", Kind = "instant", X = 50, Y = 226, Radius = 6 } };
            g = Party(4, PartyCamera.Constant, -100, track); Start(g); Run(g, 1, step);
            foreach (var p in g.Players)
            { Check(p.Status == PlayerStatus.Running && p.InstantBoostsEarned == 1, "four independent physical boosts"); Near(p.Distance, 286, "actual boost motion"); }
        }
        foreach (var settings in new[] { FlightPresets.Chosen, FlightPresets.FastFlip,
            new FlightSettings(FirstAction.Flip, MotionLaw.Linear, AirRepeatLimit.Unlimited, 700, 30, 320, 300, 100) })
        {
            var g = RaceSimulation.CreateParty(Empty(), settings, 4, PartyCamera.Constant, -100, 800); Start(g);
            for (int i = 0; i < 4; i++)
            {
                Check(RaceSimulation.Press(g, i), "all four slots accept flight input");
                Check(g.Players[i].FlightActive && g.Players[i].Flipping, "flight starts personally");
            }
            RaceSimulation.Advance(g, .1);
            for (int i = 0; i < 4; i++) Check(RaceSimulation.Press(g, i), "air repeat in both presets and manual");
        }
    }

    static void MixedOrderAndTerminalTie()
    {
        GameState reference = null;
        foreach (double step in new[] { 1.0, 1.0 / 30, 1.0 / 240 })
        {
            var track = Empty();
            track.Obstacles = new[] { new Obstacle { Id = "rock", X = 100, Width = 64, Height = 68, Kind = "floor" } };
            track.RiskZones = new[] { new RiskZone { Id = "coin", Kind = "coin", X = 300, Y = 226, Radius = 6 } };
            var g = Party(4, PartyCamera.Constant, -80, track); Start(g);
            g.Players[1].Y = 0; g.Players[2].Recovering = .2; g.Players[3].Distance = 7700;
            if (step < 1) Array.Reverse(g.Players);
            Run(g, 2, step);
            if (reference == null) reference = g;
            foreach (var p in g.Players)
            {
                var prior = Array.Find(reference.Players, candidate => candidate.Id == p.Id);
                Check(prior.Status == p.Status && prior.Collisions == p.Collisions && prior.CoinsEarned == p.CoinsEarned,
                    "mixed terminal/effects order invariant");
                Near(p.Distance, prior.Distance, "mixed distance dt invariant");
                if (p.TerminalTime.HasValue) Near(p.TerminalTime.Value, prior.TerminalTime.Value, "mixed terminal timestamp dt invariant");
            }
            Check(Equals(g.Winner, reference.Winner), "mixed winner order invariant");
        }
        // Finish and elimination remain a finish even if obstacle contact is
        // simultaneous. No future collision or reward belongs to a finisher.
        var tieTrack = Empty(15840);
        tieTrack.Obstacles = new[] { new Obstacle { Id = "finish-rock", X = 15854, Width = 64, Height = 68, Kind = "floor" } };
        var tie = Party(2, PartyCamera.Constant, 15766, tieTrack); Start(tie);
        foreach (var p in tie.Players) p.Distance = 15796;
        RaceSimulation.Advance(tie, 1);
        foreach (var p in tie.Players)
            Check(p.Status == PlayerStatus.Finished && p.Collisions == 0, "finish wins simultaneous terminal and contact");
    }

    static int Main()
    {
        try { ReadinessAndRematch(); CameraAndPause(); Boundaries(); EventPriorityAndDt(); OutcomesAndLagger(); Rewards(); CollisionsAndEffects(); MixedOrderAndTerminalTie(); Console.WriteLine("PartyCheck PASS: " + checks + " deterministic assertions (PARTY-00/01/02/04/05/06)"); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
