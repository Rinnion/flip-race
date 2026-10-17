using System;
using FlipRace.Core;
public static partial class FlightIndependentAudit
{
    static GameState OnStone(FlightSettings tested, int side, double step, double height = 48, bool wide = false)
    {
        var stone = new Obstacle { Id = "audit-stone", Kind = side == 1 ? "floor" : "ceiling", X = 3000, Width = wide ? 3000 : 300, Height = height };
        var track = new Track(220000, new[] { stone }) { RiskZones = new[] {
            new RiskZone { Id = "audit-pad", Kind = "landing", ObstacleId = stone.Id,
                X = wide ? 4500 : 3100, LandingWidth = wide ? 3000 : 52 } } };
        var prep = new FlightSettings(FirstAction.Flip, tested.Law, tested.AirRepeats, tested.GravityAcceleration,
            tested.FlipImpulse, tested.JumpImpulse, tested.LinearSpeed, tested.LinearJumpHeight);
        var g = Prepare(prep, 1 - side, step, track);
        double time = tested.Law == MotionLaw.Linear ? (212 - height) / tested.LinearSpeed
            : Travel(212 - height, tested.FlipImpulse, tested.GravityAcceleration);
        M(g, 3100d / 220 - time - g.Elapsed, step); Tap(g); M(g, time + 1e-9, step);
        foreach (var p in g.Players)
        {
            C(p.Support == SupportKind.Stone && p.SupportSide == side && p.SupportObstacleId == stone.Id && !p.Flipping, "actual stable marked-stone preparation");
            N(p.Distance, 3100, "real landing X", 1e-5); N(RaceSimulation.GetPlayerCenterY(p), WallY(side) + Away(side) * height, "real stone plane");
            C(p.Collisions == 0 && p.BoostsEarned == 1, "preparation is clean actual once-reward");
        }
        g.Flight = tested; return g;
    }
    static void StoneCases()
    {
        foreach (var law in new[] { MotionLaw.Linear, MotionLaw.Gravity })
        foreach (var action in new[] { FirstAction.Flip, FirstAction.Jump })
        foreach (var repeat in Repeats) foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "stone " + law + "/" + action + "/" + repeat + " side" + side + " dt" + step;
            var f = new FlightSettings(action, law, repeat);
            var g = OnStone(f, side, step); double gap = 3300 - g.Players[0].Distance, boost = g.Players[0].BoostRemaining;
            double toEdge = gap <= boost * 330 ? gap / 330 : boost + (gap - boost * 330) / 220;
            double edgeAt = g.Elapsed + toEdge; M(g, toEdge + 1e-9, step);
            foreach (var p in g.Players)
            {
                C(p.Support == SupportKind.None && p.Flipping && p.FlightActive, "actual walkoff starts independent measured fall");
                C(!p.LinearJumpActive, "walkoff does not invent jump apex");
                N(p.VerticalSpeed, law == MotionLaw.Linear ? -Away(side) * f.LinearSpeed : -Away(side) * f.GravityAcceleration * 1e-9, "walkoff no departure impulse");
            }
            double fall = law == MotionLaw.Linear ? 48 / f.LinearSpeed : Math.Sqrt(96 / f.GravityAcceleration);
            M(g, fall, step); Support(g, side, repeat);
            foreach (var p in g.Players) { Measurement(p, fall, 48, edgeAt + fall, side, false, "audit-stone"); C(p.Collisions == 0, "no trailing-face phantom hit"); }

            var d = OnStone(f, side, step, 48, true); double start = d.Elapsed; Tap(d);
            double duration = law == MotionLaw.Linear ? (action == FirstAction.Flip ? 164 : 224) / f.LinearSpeed
                : action == FirstAction.Flip ? Travel(164, f.FlipImpulse, f.GravityAcceleration) : 2 * f.JumpImpulse / f.GravityAcceleration;
            double maximum = action == FirstAction.Flip ? 164 : law == MotionLaw.Linear ? 112 : f.JumpImpulse * f.JumpImpulse / (2 * f.GravityAcceleration);
            M(d, duration + 1e-9, step);
            foreach (var p in d.Players)
            {
                C(!p.Flipping && !p.FlightActive && p.Collisions == 0, "raised departure and return/arrival complete cleanly");
                C(p.Support == (action == FirstAction.Flip ? SupportKind.Wall : SupportKind.Stone), "actual reached support");
                C(p.AirFlipAvailable == (repeat != AirRepeatLimit.None) && p.BoostsEarned == 1, "support recharges repeats but not once reward");
                Measurement(p, duration, maximum, start + duration, side, false, "audit-stone");
            }
        }
        foreach (double stoneHeight in new[] { 20d, 48 }) foreach (var repeat in Repeats)
        foreach (double speed in new[] { 212 / 1.2, 265, 530 }) foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "linear stone jump wall-before-or-at-apex H" + stoneHeight + " v" + speed + " r" + repeat + " side" + side + " dt" + step;
            var f = new FlightSettings(FirstAction.Jump, MotionLaw.Linear, repeat, 2700, 420, 550, speed, 192);
            var g = OnStone(f, side, step, stoneHeight, true); double start = g.Elapsed, height = 212 - stoneHeight, oneWay = height / speed; Tap(g);
            M(g, oneWay, step);
            foreach (var p in g.Players)
            {
                N(RaceSimulation.GetPlayerCenterY(p), WallY(1 - side), "wall precedes or exactly ties apex");
                N(p.VerticalSpeed, -Away(side) * speed, "single automatic reflection");
                C(p.Support == SupportKind.None && p.Flipping && p.FlightActive && p.LinearJumpReturning, "reflection is still one jump");
                C(p.AirFlipAvailable == (repeat != AirRepeatLimit.None), "wall reflection grants no new charge");
                N(p.FlightDuration, oneWay, "same measurement clock"); N(p.FlightMaxDistance, height, "max uses reachable height not selected192");
            }
            M(g, oneWay + 1e-9, step);
            foreach (var p in g.Players)
            {
                C(p.Support == SupportKind.Stone && !p.Flipping && p.Collisions == 0 && p.BoostsEarned == 1, "return is legal same marked stone once");
                Measurement(p, 2 * oneWay, height, start + 2 * oneWay, side, false, "audit-stone");
            }
        }
    }
    static void InterruptAndReset()
    {
        foreach (var law in new[] { MotionLaw.Linear, MotionLaw.Gravity })
        foreach (var action in new[] { FirstAction.Flip, FirstAction.Jump })
        foreach (var repeat in Repeats) foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "interrupted " + law + "/" + action + "/" + repeat + " side" + side + " dt" + step;
            var f = new FlightSettings(action, law, repeat);
            var track = new Track(220000, new[] { new Obstacle { Id = "audit-blocker", Kind = "gate", X = 3000, Width = 20, GapTop = 240, GapBottom = 240 } });
            var g = Prepare(f, side, step, track); M(g, (2986d - 22) / 220 - g.Elapsed, step); double start = g.Elapsed; Tap(g); M(g, .1, step);
            double maximum = law == MotionLaw.Linear ? f.LinearSpeed * .1
                : (action == FirstAction.Jump ? f.JumpImpulse : f.FlipImpulse) * .1
                    + (action == FirstAction.Jump ? -1 : 1) * .5 * f.GravityAcceleration * .01;
            foreach (var p in g.Players)
            {
                C(p.Collisions == 1 && p.Recovering > 0 && !p.FlightActive, "first actual hit finalizes interrupted metric");
                Measurement(p, .1, maximum, start + .1, side, true);
                C(!RaceSimulation.Press(g, p.Id), "recovery input rejected");
            }
            var first = g.Players[0].LastFlight; M(g, 3, step);
            foreach (var p in g.Players) { C(!p.Flipping && p.LastFlight.Interrupted && !p.FlightActive, "later landing cannot fake a clean measurement"); Measurement(p, .1, maximum, start + .1, side, true); }
            C(ReferenceEquals(first, g.Players[0].LastFlight), "interrupted immutable sample preserved");
            Tap(g); M(g, 3, step); foreach (var p in g.Players) C(p.LastFlight != null && !p.LastFlight.Interrupted && !p.FlightActive, "new actual launch can produce next clean sample");
        }
        foreach (var f in new[] { FlightPresets.Moderate, FlightPresets.Linear, FlightPresets.Jump, FlightPresets.Unlimited })
        foreach (double step in Steps)
        {
            context = "incomplete finish/rematch";
            var g = Start(f, new Track(22, Array.Empty<Obstacle>())); Tap(g); M(g, .2, step);
            foreach (var p in g.Players) C(p.FinishTime.HasValue && p.LastFlight == null && !p.FlightActive, "unfinished flight at race finish is not completed measurement");
            C(RaceSimulation.Press(g, 0) && g.Phase == "finished", "rematch waits for both"); C(RaceSimulation.Press(g, 1) && g.Phase == "countdown", "second ready starts rematch");
            C(ReferenceEquals(g.Flight, f), "immutable actual settings retained across rematch");
            foreach (var p in g.Players) C(p.LastFlight == null && !p.FlightActive && !p.LinearJumpActive && p.Distance == 0, "rematch clears all measurement and jump dynamics");
        }
    }
}
