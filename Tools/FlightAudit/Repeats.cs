using System;
using FlipRace.Core;
public static partial class FlightIndependentAudit
{
    static void RepeatRules()
    {
        foreach (var law in new[] { MotionLaw.Linear, MotionLaw.Gravity })
        foreach (var action in new[] { FirstAction.Flip, FirstAction.Jump })
        foreach (var repeat in Repeats) foreach (int side in new[] { 0, 1 })
        foreach (double age in action == FirstAction.Jump && law == MotionLaw.Gravity ? new[] { .12, .55, .95 } : new[] { .12, .55 }) foreach (double step in Steps)
        {
            context = "repeats " + law + "/" + action + "/" + repeat + " side" + side + " age" + age + " dt" + step;
            var f = new FlightSettings(action, law, repeat, 662.5, 0, 400, 265, 112);
            var g = Prepare(f, side, step); double start = g.Elapsed; Tap(g); M(g, age, step);
            foreach (var p in g.Players) C(p.Flipping, "tap is actually airborne");
            var p0 = g.Players[0]; double y = p0.Y, vy = p0.VerticalSpeed, time = p0.FlightDuration, max = p0.FlightMaxDistance;
            int gravity = p0.Gravity; bool accepted = RaceSimulation.Press(g, 0);
            C(accepted == (repeat != AirRepeatLimit.None), "first accepted count");
            N(p0.Y, y, "air tap no positional snap"); N(p0.FlightDuration, time, "air tap does not reset measurement time");
            N(p0.FlightMaxDistance, max, "air tap does not reset measured maximum");
            if (law == MotionLaw.Gravity || !accepted) N(p0.VerticalSpeed, vy, "gravity or rejected edge no impulse");
            else N(p0.VerticalSpeed, action == FirstAction.Jump ? Away(side) * f.LinearSpeed : -vy, "linear first action semantics");
            C(p0.Gravity == (accepted ? action == FirstAction.Jump && law == MotionLaw.Linear ? (int)Away(side) : -gravity : gravity), "gravity/target sign correct");
            C(g.Players[1].Gravity == gravity && g.Players[1].Y == y && g.Players[1].VerticalSpeed == vy, "other player unchanged");
            double firstVelocity = p0.VerticalSpeed; int firstGravity = p0.Gravity;
            C(RaceSimulation.Press(g, 0) == (repeat == AirRepeatLimit.Unlimited), "second separate edge count");
            if (repeat == AirRepeatLimit.Unlimited)
            {
                N(p0.VerticalSpeed, law == MotionLaw.Linear ? -firstVelocity : firstVelocity, "subsequent linear reversal vs inertia");
                C(p0.Gravity == -firstGravity, "second reversal");
                for (int i = 0; i < 32; i++) C(RaceSimulation.Press(g, 0), "unlimited zero-delay edges not throttled");
                N(p0.Y, y, "many edges still no teleport");
            }
            double expectedY = RaceSimulation.GetPlayerCenterY(p0), expectedV = p0.VerticalSpeed, a = law == MotionLaw.Gravity ? p0.Gravity * f.GravityAcceleration : 0;
            M(g, .01, step); N(RaceSimulation.GetPlayerCenterY(p0), expectedY + expectedV * .01 + .5 * a * .0001, "post-tap actual movement");
            N(p0.VerticalSpeed, expectedV + a * .01, "post-tap no kick");
        }
        // First edge exactly at or on either side of the automatic linear apex.
        foreach (int side in new[] { 0, 1 }) foreach (double offset in new[] { -1e-7, 0, 1e-7 }) foreach (double step in Steps)
        {
            context = "linear apex input side" + side + " offset" + offset + " dt" + step;
            var f = new FlightSettings(FirstAction.Jump, MotionLaw.Linear, AirRepeatLimit.One);
            var g = Prepare(f, side, step); double start = g.Elapsed; Tap(g); M(g, 112d / 265 + offset, step);
            double y = RaceSimulation.GetPlayerCenterY(g.Players[0]); Tap(g);
            foreach (var p in g.Players) { N(p.VerticalSpeed, Away(side) * 265, "first tap heads opposite original wall"); C(!p.LinearJumpActive && !p.AirFlipAvailable, "automatic apex cancelled and repeat spent"); }
            double rest = Math.Abs(WallY(1 - side) - y) / 265; M(g, rest + 1e-9, step); Support(g, 1 - side, AirRepeatLimit.One);
            foreach (var p in g.Players) Measurement(p, g.Elapsed - start - 1e-9, 212, g.Elapsed - 1e-9, side);
        }
        foreach (var law in new[] { MotionLaw.Linear, MotionLaw.Gravity })
        foreach (var action in new[] { FirstAction.Flip, FirstAction.Jump })
        foreach (var repeat in new[] { AirRepeatLimit.One, AirRepeatLimit.Unlimited })
        foreach (int side in new[] { 0, 1 }) foreach (double step in Steps)
        {
            context = "completed repeat measurement " + law + "/" + action + "/" + repeat + " side" + side + " dt" + step;
            var f = new FlightSettings(action, law, repeat, 662.5, 70, 400);
            var g = Prepare(f, side, step); double start = g.Elapsed;
            double age = action == FirstAction.Flip ? .1 : law == MotionLaw.Linear ? .55 : .2;
            Tap(g); M(g, age, step); Tap(g);
            double duration, height; int endSide;
            if (law == MotionLaw.Linear)
            {
                if (action == FirstAction.Flip) { duration = 2 * age; height = age * 265; endSide = side; }
                else { double q = 224 - age * 265; duration = age + (212 - q) / 265; height = 212; endSide = 1 - side; }
            }
            else if (action == FirstAction.Flip)
            {
                double q = 70 * age + .5 * 662.5 * age * age, v = 70 + 662.5 * age;
                duration = age + (v + Math.Sqrt(v * v + 2 * 662.5 * q)) / 662.5;
                height = q + v * v / (2 * 662.5); endSide = side;
            }
            else
            {
                double q = 400 * age - .5 * 662.5 * age * age, v = 400 - 662.5 * age;
                duration = age + Travel(212 - q, v, 662.5); height = 212; endSide = 1 - side;
            }
            M(g, duration - age + 1e-9, step); Support(g, endSide, repeat);
            foreach (var p in g.Players) Measurement(p, duration, height, start + duration, side);
        }
        HiddenValues();
    }
    static void HiddenValues()
    {
        foreach (var law in new[] { MotionLaw.Linear, MotionLaw.Gravity })
        foreach (var action in new[] { FirstAction.Flip, FirstAction.Jump }) foreach (int side in new[] { 0, 1 })
        {
            context = "inactive values " + law + "/" + action + " side" + side;
            var first = new FlightSettings(action, law, AirRepeatLimit.Unlimited, 662.5, 70, 400, 265, 112);
            var second = law == MotionLaw.Linear
                ? new FlightSettings(action, law, AirRepeatLimit.Unlimited, 2700, 420, 550, 265, 112)
                : new FlightSettings(action, law, AirRepeatLimit.Unlimited, 662.5,
                    action == FirstAction.Flip ? 70 : 420, action == FirstAction.Jump ? 400 : 550, 530, 192);
            var a = Prepare(first, side, .317); var b = Prepare(second, side, .317); Tap(a); Tap(b);
            for (int frame = 0; frame < 120; frame++)
            {
                M(a, .01, .01); M(b, .01, .01);
                if (frame == 7 || frame == 23) { Tap(a); Tap(b); }
                for (int i = 0; i < 2; i++)
                {
                    var x = a.Players[i]; var y = b.Players[i];
                    N(x.Y, y.Y, "inactive parameters do not change trajectory"); N(x.VerticalSpeed, y.VerticalSpeed, "inactive parameters do not change speed");
                    C(x.Gravity == y.Gravity && x.Support == y.Support && x.AirFlipAvailable == y.AirFlipAvailable, "inactive parameters do not change semantics");
                    N(x.FlightMaxDistance, y.FlightMaxDistance, "inactive parameters do not change measurement");
                }
            }
            C(first.FlipImpulse == 70 && second.LinearJumpHeight == (law == MotionLaw.Linear ? 112 : 192), "settings remain immutable physical values");
        }
    }
}
