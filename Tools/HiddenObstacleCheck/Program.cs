using System;
using System.Linq;
using FlipRace.Core;

static class Program
{
    const double Tick = 1d / 240;
    static int assertions;
    static readonly double[] Frames = { 1d / 30, 1d / 60, 1d / 144, .317, 10 };
    static void Main()
    {
        Action[] tests = { DelaysAndRealCollision, WarningHasNoLanding, SolidHasRealLanding,
            DepartingBodyThenReturn, PauseCountdownRetrySnapshot, SharedWorldPermutations,
            TerminalBodiesDoNotProtect, AirborneDoesNotProtect, BoostCrossing, GatesStaySolid,
            FirstParticipantAndObstacleOrder, NonPartyHiddenTrack, OldTracksHaveNoHidden };
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception error) { throw new Exception(test.Method.Name + ": " + error.Message, error); }
        }
        Console.WriteLine("PASS: HIDDEN " + tests.Length + " scenarios / " + assertions + " assertions.");
        Console.WriteLine("Exact swept reveal and warning deadline; hidden-only body-clear step <= 1/240 s (lag <= 4.167 ms).");
    }
    static void Check(bool ok, string message)
    { assertions++; if (!ok) throw new Exception(message); }
    static void Near(double actual, double expected, string message, double tolerance = 2e-8)
    { Check(Math.Abs(actual - expected) < tolerance, message + " " + actual + " vs " + expected); }
    static Obstacle Stone(double x = 600, double width = 64, string kind = "floor")
    { return new Obstacle { Id = "hidden", X = x, Width = width, Height = 68, Kind = kind, Hidden = true }; }
    static Track Track(Obstacle obstacle, bool landing = false)
    {
        var track = new Track(10000, new[] { obstacle });
        if (landing) track.RiskZones = new[] { new RiskZone { Id = "landing", ObstacleId = obstacle.Id,
            Kind = "landing", X = obstacle.X + obstacle.Width / 2, LandingWidth = obstacle.Width } };
        return track;
    }
    static GameState Ready(Track track, int count = 1, PartyCamera camera = PartyCamera.Lagger, FlightSettings flight = null)
    { return count == 1 ? RaceSimulation.CreateSolo(track, flight ?? FlightPresets.Chosen, camera, -10000, 20000)
        : RaceSimulation.CreateParty(track, flight ?? FlightPresets.Chosen, count, camera, -10000, 20000); }
    static void Start(GameState game)
    {
        for (int i = 0; i < game.Players.Length; i++) Check(RaceSimulation.Press(game, i), "ready");
        RaceSimulation.Advance(game, 3);
        Check(game.Phase == "racing", "countdown completes");
    }
    static void Run(GameState game, double seconds, double frame)
    {
        while (seconds > 1e-12 && game.Phase == "racing")
        { double step = Math.Min(frame, seconds); RaceSimulation.Advance(game, step); seconds -= step; }
    }
    static HiddenObstacleState State(GameState game) { return game.HiddenObstacles["hidden"]; }
    static HiddenObstaclePhase Phase(GameState game) { return HiddenObstacles.GetPhase(game, game.Track.Obstacles[0]); }
    static void DelaysAndRealCollision()
    {
        foreach (double delay in new[] { .4, .6, .8, 1, 1.2 })
        foreach (double frame in Frames)
        {
            var game = Ready(Track(Stone())); HiddenObstacles.ConfigureWarning(game, delay); Start(game);
            Near(game.HiddenWarningDuration, delay, "selected delay survives start");
            Run(game, 160d / 220 - 1e-6, frame);
            Check(Phase(game) == HiddenObstaclePhase.Hidden, "hidden before trigger");
            Run(game, Tick + 2e-6, frame);
            var state = State(game);
            Check(Phase(game) == HiddenObstaclePhase.Warning, "warning after crossing");
            Check(state.RevealedAt.Value >= 160d / 220 - 1e-9 && state.RevealedAt.Value <= 160d / 220 + Tick + 1e-9,
                "bounded trigger lag");
            Near(state.RevealedAt.Value, 160d / 220, "analytic reveal crossing");
            double deadline = state.RevealedAt.Value + delay;
            Run(game, deadline - game.Elapsed - 1e-6, frame);
            Check(Phase(game) == HiddenObstaclePhase.Warning && game.Players[0].Collisions == 0, "warning excludes collision");
            Run(game, 2e-6, frame);
            Check(Phase(game) == HiddenObstaclePhase.Solid, "solid at deadline");
            Near(state.SolidAt.Value, deadline, "exact deadline");
            Run(game, 3 - game.Elapsed, frame);
            Check(game.Players[0].Collisions == 1 && game.Players[0].LastHit.Id == "hidden", "solid produces actual hit");
            Near(game.Players[0].LastHit.At, (600 - 14d) / 220, "analytic collision retained");
            Check(Phase(game) == HiddenObstaclePhase.Solid, "permanent solid");
        }
    }
    static void WarningHasNoLanding()
    {
        foreach (string kind in new[] { "floor", "ceiling" })
        foreach (double frame in Frames)
        {
            var game = Ready(Track(Stone(0, 1200, kind), true)); HiddenObstacles.ConfigureWarning(game, 1.2); Start(game);
            if (kind == "ceiling") { game.Players[0].Y = 0; game.Players[0].SupportSide = 0; }
            Check(RaceSimulation.Press(game, 0), "ordinary chosen jump"); Run(game, .85, frame);
            Check(Phase(game) == HiddenObstaclePhase.Warning, "long warning");
            var p = game.Players[0];
            Check(p.Collisions == 0 && p.Support != SupportKind.Stone && p.BoostsEarned == 0
                && p.SuccessfulRiskIds.Count == 0, "warning neither collides nor awards/supports landing");
            Check(p.Flipping && (kind == "floor" ? RaceSimulation.GetPlayerCenterY(p) > 158
                : RaceSimulation.GetPlayerCenterY(p) < 82), "body passed back through future surface");
        }
    }
    static void SolidHasRealLanding()
    {
        foreach (string kind in new[] { "floor", "ceiling" })
        foreach (double frame in Frames)
        {
            var game = Ready(Track(Stone(0, 1200, kind), true)); HiddenObstacles.ConfigureWarning(game, .4); Start(game);
            if (kind == "ceiling") { game.Players[0].Y = 0; game.Players[0].SupportSide = 0; }
            RaceSimulation.Press(game, 0); Run(game, .9, frame);
            Check(Phase(game) == HiddenObstaclePhase.Solid, "solid after in-flight clear deadline");
            var p = game.Players[0];
            Check(p.Support == SupportKind.Stone && p.SupportObstacleId == "hidden" && p.Collisions == 0,
                "actual marked solid top supports body");
            Check(p.BoostsEarned == 1 && p.SuccessfulRiskIds.Contains("landing"), "actual solid landing awards reward");
            Check(p.CurrentSpeed == 330, "landing gives real speed effect");
        }
    }
    static void DepartingBodyThenReturn()
    {
        foreach (string kind in new[] { "floor", "ceiling" })
        foreach (double frame in Frames)
        {
            var game = Ready(Track(Stone(0, 1200, kind))); HiddenObstacles.ConfigureWarning(game, .4); Start(game);
            if (kind == "ceiling") { game.Players[0].Y = 0; game.Players[0].SupportSide = 0; }
            Run(game, .25, frame); RaceSimulation.Press(game, 0); Run(game, .16, frame);
            Check(Phase(game) == HiddenObstaclePhase.Warning && State(game).ActivationPostponed,
                "actual rising body postpones global activation");
            Check(game.Players[0].Collisions == 0, "no spawn hit inside body");
            Run(game, .2, frame);
            Check(Phase(game) == HiddenObstaclePhase.Solid && game.Players[0].Flipping,
                "becomes solid on body clearance while flight continues");
            double clearTime = .25 + (350 - Math.Sqrt(350 * 350 - 2 * 662.5 * 68)) / 662.5;
            Check(State(game).SolidAt >= clearTime - 1e-9 && State(game).SolidAt <= clearTime + Tick + 1e-8,
                "bounded body-clear activation lag");
            Check(game.Players[0].Collisions == 0, "departing body is not falsely hit");
            Run(game, .6, frame);
            Check(game.Players[0].Collisions == 1, "returning to newly solid body is a real collision");
        }
    }
    static void PauseCountdownRetrySnapshot()
    {
        var game = Ready(Track(Stone(0, 100))); HiddenObstacles.ConfigureWarning(game, .6);
        RaceSimulation.Press(game, 0); RaceSimulation.Advance(game, 2.5);
        Check(game.HiddenObstacles.Count == 0 && game.Elapsed == 0, "no reveal/countdown world advance");
        RaceSimulation.Advance(game, .6);
        Near(game.Elapsed, .1, "countdown leftover goes to world");
        Near(State(game).RevealedAt.Value, 0, "reveal starts after countdown");
        var copy = RaceSnapshot.Clone(game);
        Check(!ReferenceEquals(copy.HiddenObstacles, game.HiddenObstacles)
            && !ReferenceEquals(State(copy), State(game)), "deep independent reveal map and entries");
        game.Phase = "paused"; RaceSimulation.Advance(game, 20);
        Near(game.Elapsed, .1, "pause stops world"); Check(State(game).SolidAt == null, "pause stops warning");
        Run(copy, .7, .317);
        Check(State(game).Phase == HiddenObstaclePhase.Warning && State(copy).Phase == HiddenObstaclePhase.Solid,
            "forecast advances without mutating live timeline");
        copy.HiddenObstacles.Clear(); Check(game.HiddenObstacles.Count == 1, "forecast clear independent");
        game.Phase = "finished"; RaceSimulation.Press(game, 0);
        Check(game.Phase == "countdown" && game.HiddenObstacles.Count == 0 && game.Elapsed == 0, "retry clears world state");
        Near(game.HiddenWarningDuration, .6, "retry preserves selected delay");
        RaceSimulation.Advance(game, 3.1); Near(State(game).RevealedAt.Value, 0, "retry fresh reveal");
        bool rejected = false;
        try { HiddenObstacles.ConfigureWarning(game, .4); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "cannot change attempt delay midway");
    }
    static void SharedWorldPermutations()
    {
        foreach (int count in new[] { 2, 3, 4 })
        foreach (var camera in new[] { PartyCamera.Constant, PartyCamera.Lagger })
        foreach (double frame in new[] { 1d / 60, .317 })
        {
            var game = Ready(Track(Stone(0, 1200)), count, camera); HiddenObstacles.ConfigureWarning(game, .4); Start(game);
            // Represent players separated earlier: participant 1 remains inside,
            // while the others run on the ceiling. Then drive real Press/Advance.
            for (int i = 0; i < count; i++) if (i != 1) { game.Players[i].Y = 0; game.Players[i].SupportSide = 0; }
            var reversed = RaceSnapshot.Clone(game); Array.Reverse(reversed.Players);
            Run(game, .5, frame); Run(reversed, .5, frame);
            Check(Phase(game) == HiddenObstaclePhase.Warning && State(game).ActivationPostponed,
                "one participant body postpones for all " + count);
            Near(State(game).RevealedAt.Value, State(reversed).RevealedAt.Value, "array order does not change reveal");
            Check(State(game).TriggerPlayerId == State(reversed).TriggerPlayerId, "tie uses stable player ID");
            RaceSimulation.Press(game, 1);
            int reversedIndex = Array.FindIndex(reversed.Players, p => p.Id == 1); RaceSimulation.Press(reversed, reversedIndex);
            Run(game, .4, frame); Run(reversed, .4, frame);
            Check(Phase(game) == HiddenObstaclePhase.Solid && Phase(reversed) == HiddenObstaclePhase.Solid, "clear releases everyone");
            Near(State(game).SolidAt.Value, State(reversed).SolidAt.Value, "array order does not change solid time");
            foreach (var p in game.Players)
            {
                var q = reversed.Players.Single(candidate => candidate.Id == p.Id);
                Near(p.Distance, q.Distance, "array order actual motion"); Near(p.Y, q.Y, "array order actual flight");
                Check(p.Collisions == q.Collisions && p.Collisions == 0, "no participant activation hit");
            }
        }
    }
    static void TerminalBodiesDoNotProtect()
    {
        foreach (var status in new[] { PlayerStatus.Finished, PlayerStatus.Eliminated })
        {
            var game = Ready(Track(Stone(0, 1000)), 2); HiddenObstacles.ConfigureWarning(game, .4); Start(game);
            game.Players[0].Status = status;
            game.Players[1].Y = 0; game.Players[1].SupportSide = 0;
            Run(game, .5, .317);
            Check(Phase(game) == HiddenObstaclePhase.Solid && State(game).TriggerPlayerId == 1,
                "terminal player neither reveals nor protects");
        }
    }
    static void AirborneDoesNotProtect()
    {
        foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
        {
            var game = Ready(Track(Stone(600)), flight: flight); HiddenObstacles.ConfigureWarning(game, .4); Start(game);
            Run(game, .8, .317); RaceSimulation.Press(game, 0); Run(game, .34, .317);
            Check(Phase(game) == HiddenObstaclePhase.Solid && !State(game).ActivationPostponed,
                "airborne outside body does not postpone for " + flight.FirstAction);
        }
    }
    static void BoostCrossing()
    {
        foreach (double frame in Frames)
        {
            var stone = Stone(600); var track = Track(stone);
            track.RiskZones = new[] { new RiskZone { Id = "boost", Kind = "instant", FreeReward = true, X = 135, Y = 226, Radius = 6 } };
            var game = Ready(track); Start(game); Run(game, .85, frame);
            Check(game.Players[0].BoostsEarned == 1 && game.Players[0].CurrentSpeed == 330, "actual swept ring speeds runner");
            double contactAt = (135d - 20) / 220;
            double revealExpected = contactAt + (160 - 115d) / 330;
            double actual = State(game).RevealedAt.Value;
            Check(actual >= revealExpected - 1e-9 && actual <= revealExpected + Tick + 1e-9,
                "boost crossing has bounded trigger lag");
            Near(actual, revealExpected, "exact reveal after swept reward acceleration");
            Check(State(game).TriggerPlayerId == 0 && game.Players[0].Collisions == 0, "boost reveal is physical and harmless");
        }
    }
    static void GatesStaySolid()
    {
        var gate = new Obstacle { Id = "hidden", Kind = "gate", Hidden = true, X = 100, Width = 12, GapTop = 48, GapBottom = 192 };
        var game = Ready(Track(gate)); Start(game); Run(game, .5, .317);
        Check(Phase(game) == HiddenObstaclePhase.Solid && game.HiddenObstacles.Count == 0 && game.Players[0].Collisions == 1,
            "gate metadata cannot hide either physical band");
    }
    static void NonPartyHiddenTrack()
    {
        var game = RaceSimulation.CreateTrial(Track(Stone()), FlightPresets.Chosen); Start(game);
        Run(game, 3, .317);
        Check(game.Players.All(p => p.Collisions == 1) && Phase(game) == HiddenObstaclePhase.Solid,
            "trial filters world collision without enabling party semantics");
    }
    static void FirstParticipantAndObstacleOrder()
    {
        foreach (int count in new[] { 2, 3, 4 })
        foreach (double frame in Frames)
        {
            var floor = Stone(600); var ceiling = Stone(600, kind: "ceiling"); ceiling.Id = "upper";
            var a = Ready(new Track(10000, new[] { floor, ceiling }), count); Start(a);
            // Two players cross less than 1/240 s apart. Earliest is ID 1,
            // despite ID 0 being first in the source array.
            a.Players[1].Distance = .2;
            var b = RaceSnapshot.Clone(a); Array.Reverse(b.Players);
            b.Track = new Track(10000, new[] { ceiling, floor });
            Run(a, 2, frame); Run(b, 2, frame);
            foreach (var id in new[] { "hidden", "upper" })
            {
                Check(a.HiddenObstacles[id].TriggerPlayerId == 1 && b.HiddenObstacles[id].TriggerPlayerId == 1,
                    "first actual crossing wins within one global slice");
                Near(a.HiddenObstacles[id].RevealedAt.Value, (160 - .2) / 220, "first participant exact crossing");
                Near(a.HiddenObstacles[id].RevealedAt.Value, b.HiddenObstacles[id].RevealedAt.Value, "source order reveal");
                Near(a.HiddenObstacles[id].SolidAt.Value, b.HiddenObstacles[id].SolidAt.Value, "source order activation");
            }
        }
    }
    static void OldTracksHaveNoHidden()
    {
        var original = TracksFull.All.Concat(TracksRhythm.All).Concat(new[] { TracksMixed.Get() }).ToArray();
        Check(original.Length == 5, "five original playable full tracks");
        foreach (var track in original)
        {
            Check(track.Obstacles.All(o => !o.Hidden), "old track has no hidden data " + track.Id);
            var game = Ready(track); Start(game); Run(game, .1, .317);
            Check(game.HiddenObstacles.Count == 0, "old world has no hidden timeline " + track.Id);
        }
    }
}
