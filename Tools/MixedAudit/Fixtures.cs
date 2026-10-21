using System;
using System.Collections.Generic;
using FlipRace.Core;

namespace FlipRace.MixedAudit
{
    public static partial class StageAudit
    {
        static Track SmallFixture(bool mirror, string pulse, double contactX)
        {
            return new Track(1100, new[] {
                new Obstacle { Id = "small", X = 600, Width = 64, Height = 48, Kind = mirror ? "ceiling" : "floor" }
            }) { RiskZones = new[] {
                new RiskZone { Id = "real-pulse", Kind = pulse, FreeReward = true, X = contactX + 22,
                    Y = mirror ? 14 : 226, Radius = 8 }
            } };
        }

        static void PrepMirror(GameState game, FlightSettings flight, bool mirror, double step)
        {
            if (!mirror) return;
            Tap(game);
            if (flight.FirstAction == FirstAction.Jump) { Adapter.AdvanceTo(game, .2, step); Tap(game); }
            Adapter.AdvanceTo(game, 1, step);
            Check(game.Players[0].Support == SupportKind.Wall && game.Players[0].SupportSide == 0, "real mirror preparation");
        }

        public static string HeldSmall()
        {
            checks = games = 0;
            foreach (bool mirror in new[] { false, true }) foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
                foreach (double speed in new[] { 110d, 220d, 330d }) foreach (double step in Steps)
                {
                    var track = new Track(1400, new[] { new Obstacle {
                        Id = "small", X = 600, Width = 64, Height = 48, Kind = mirror ? "ceiling" : "floor"
                    }});
                    var game = Adapter.Start(track, flight); games++;
                    // Explicit held horizontal-speed isolation. This is the only
                    // injected field: no position, vertical motion, support or
                    // charge is supplied. Do not interpret its finish ledger.
                    if (speed != 220) foreach (var p in game.Players)
                        p.ActiveEffect = new SpeedEffect { RingId = "held-isolation", IsNegative = speed == 110,
                            Remaining = 100, PulseCount = 167 };
                    PrepMirror(game, flight, mirror, step);
                    bool fast = flight.FirstAction == FirstAction.Flip;
                    if (speed == 110)
                    {
                        // Service alternative: a real opposite wall between
                        // maneuvers, never presented as the baseline small One.
                        MoveTo(game, 586 - speed * .7, step); double start = game.Elapsed; Tap(game);
                        if (!fast) { Adapter.AdvanceTo(game, start + .2, step); Tap(game); }
                        MoveTo(game, 679, step);
                        foreach (var p in game.Players)
                            Check(p.Support == SupportKind.Wall && p.SupportSide != (mirror ? 0 : 1) && p.AirFlipAvailable,
                                "110 service uses actual opposite wall/recharge");
                        start = game.Elapsed; Tap(game);
                        if (!fast) { Adapter.AdvanceTo(game, start + .2, step); Tap(game); }
                        Adapter.AdvanceTo(game, start + .8, step);
                    }
                    else if (fast)
                    {
                        double g = 2700, tau = .3, v = 35 + g * tau;
                        double enter = (Math.Sqrt(35 * 35 + 2 * g * 48) - 35) / g;
                        double wrong = (v - Math.Sqrt(v * v - 2 * g * (212 - 35 * tau - .5 * g * tau * tau))) / g;
                        double leave = tau + wrong + Math.Sqrt(2 * (212 - 48) / g);
                        double departure = 632 - speed * (enter + leave) / 2;
                        MoveTo(game, departure, step); double start = game.Elapsed; Tap(game);
                        Adapter.AdvanceTo(game, start + tau, step);
                        Check(game.Players[0].Support == SupportKind.None && game.Players[0].AirFlipAvailable, "held Fast actual One");
                        Tap(game); Adapter.AdvanceTo(game, start + 1, step);
                    }
                    else
                    {
                        MoveTo(game, 632 - speed * 350 / 662.5, step);
                        double start = game.Elapsed; Tap(game); Adapter.AdvanceTo(game, start + 1.2, step);
                    }
                    foreach (var p in game.Players)
                    {
                        Check(p.Distance > 678 && p.Collisions == 0, "held small complete clearance " + speed);
                        Check(p.Support == SupportKind.Wall && p.SupportSide == (mirror ? 0 : 1) && p.AirFlipAvailable,
                            "held small actual same-side return/recharge");
                        Near(p.CurrentSpeed, speed, 0, "held horizontal service speed");
                    }
                }
            return "PASS " + checks + " checks / " + games + " held-speed small fixtures. Only ActiveEffect supplied; vertical trajectories/support/charge are actual. At 110, explicit opposite-wall service alternative; not a baseline One claim or earned pulse/finish accounting.";
        }

        // Actual earned .6 s pulses, not a held-effect or vertical-state fixture.
        // The schedules are independently derived from a center-at-apex Main
        // jump and a .3 s Fast reversal. The reward's expiry lies in that flight.
        public static string RunPulses()
        {
            checks = games = 0;
            var evidence = new List<string>();
            foreach (bool mirror in new[] { false, true })
                foreach (var flight in new[] { FlightPresets.Chosen, FlightPresets.FastFlip })
                    foreach (string kind in new[] { "instant", "negative" }) foreach (double step in Steps)
                    {
                        bool fast = flight.FirstAction == FirstAction.Flip, slow = kind == "negative";
                        // At boost: contact448, end646. At slow: contact500, end566.
                        // Departure is after real collection but before expiry.
                        double contact = slow ? 500 : 448;
                        double departure = slow ? (fast ? 550 : 555) : (fast ? 525 : 632 - 330 * 350 / 662.5);
                        var game = Adapter.Start(SmallFixture(mirror, kind, contact), flight); games++;
                        PrepMirror(game, flight, mirror, step);
                        MoveTo(game, departure, step);
                        var p = game.Players[0];
                        Check(p.Support == SupportKind.Wall && p.SupportSide == (mirror ? 0 : 1), "pulse departure real wall");
                        Check(p.ActiveEffect != null && p.ActiveEffect.IsNegative == slow, "real pulse collected before departure");
                        Near(p.CurrentSpeed, slow ? 110 : 330, 0, "actual speed before departure");
                        double remaining = p.ActiveEffect.Remaining, start = game.Elapsed, actualDeparture = p.Distance;
                        Tap(game);
                        if (fast)
                        {
                            Adapter.AdvanceTo(game, start + .3, step);
                            p = game.Players[0];
                            Check(p.Support == SupportKind.None && p.AirFlipAvailable, "pulse Fast repeat is airborne");
                            double vy = p.VerticalSpeed; Tap(game);
                            Near(p.VerticalSpeed, vy, 0, "pulse repeat retains VY");
                            Check(!p.AirFlipAvailable, "pulse spends One");
                        }
                        Adapter.AdvanceTo(game, start + 1.2, step);
                        p = game.Players[0];
                        Check(p.Collisions == 0, "actual pulse small clean " + flight.FirstAction + "/" + kind);
                        Check(p.Support == SupportKind.Wall && p.SupportSide == (mirror ? 0 : 1) && p.AirFlipAvailable, "pulse same-side real landing/recharge");
                        Check(p.ActiveEffect == null && p.PendingEffects.Count == 0, "pulse completed naturally");
                        Check(p.BoostsEarned == (slow ? 0 : 1) && p.SlowsEarned == (slow ? 1 : 0), "actual pulse ledger");
                        Near(p.Distance, actualDeparture + (slow ? 110 : 330) * remaining + 220 * (1.2 - remaining), 1e-7, "piecewise actual horizontal clock");
                        if (step == .317) evidence.Add((mirror ? "ceiling" : "floor") + "/" + flight.FirstAction + "/" + kind
                            + " departure=" + departure.ToString("R") + " remaining=" + remaining.ToString("R")
                            + " returnX=" + p.Distance.ToString("R"));
                    }
            return "PASS " + checks + " checks / " + games + " actual pulse fixture games. No held state or player injection.\n" + string.Join("\n", evidence);
        }

        // A diagnostic of the stated independent timing windows, not a new
        // acceptance requirement for their Cartesian product.
        public static string JointSmall()
        {
            checks = games = 0;
            var rows = new List<string>();
            foreach (bool mirror in new[] { false, true })
                foreach (double departureShift in new[] { -.06, 0, .06 })
                    foreach (double repeatShift in new[] { -.06, 0, .06 })
                    {
                        var game = Adapter.Start(TracksMixedStage.Get(TracksMixedStage.StageId, mirror), FlightPresets.FastFlip); games++;
                        PrepMirror(game, FlightPresets.FastFlip, mirror, .001);
                        double departure = 530d / 220 + departureShift, repeat = 596d / 220 + repeatShift;
                        Adapter.AdvanceTo(game, departure, .001); Tap(game);
                        Adapter.AdvanceTo(game, repeat, .001);
                        var before = Observation.Read(game, 0); Tap(game);
                        Adapter.AdvanceTo(game, 4, .001);
                        var p = game.Players[0];
                        rows.Add((mirror ? "ceiling" : "floor") + " startShift=" + departureShift + " repeatShift=" + repeatShift
                            + " actualRepeatSupport=" + before.Support + " chargeBefore=" + before.Charge
                            + " hits=" + p.Collisions + " finalSupport=" + p.Support + " side=" + p.SupportSide);
                        // Individual shifts must retain the intended maneuver.
                        if (departureShift == 0 || repeatShift == 0)
                            Check(before.Support == SupportKind.None && p.Collisions == 0 && p.Support == SupportKind.Wall
                                && p.SupportSide == (mirror ? 0 : 1), "individual small window semantics");
                    }
            return "DIAGNOSTIC " + checks + " assertions / " + games + " joint small games; the full ±60 ms Cartesian box is not claimed.\n" + string.Join("\n", rows);
        }
    }
}
