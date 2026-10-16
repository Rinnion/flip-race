using System;
using FlipRace.Core;

internal static partial class Program
{
    // Deliberately local test geometry. Do not expose this as TracksControl
    // before the engine and free-browser gates have passed.
    static Track LandingFixture(bool ceiling)
    {
        return new Track(2200, new[] { new Obstacle { Id = "fixture-stone", X = 700,
            Width = 180, Height = 48, Kind = ceiling ? "ceiling" : "floor" } })
        {
            Id = ceiling ? "tool-ceiling" : "tool-floor",
            RiskZones = new[] { new RiskZone { Id = "fixture-pad", Kind = "landing",
                ObstacleId = "fixture-stone", X = 750, LandingWidth = 44,
                Y = ceiling ? 48 : 192 } }
        };
    }
    static GameState ArriveFixture(bool ceiling, double step)
    {
        var game = Start(LandingFixture(ceiling), MotionMode.GravityAirFlip, MotionProfiles.Baseline);
        // Both start at floor. Establish the mirrored starting wall before
        // the actual landing maneuver; never assign simulation coordinates.
        if (!ceiling) { Tap(game); AdvanceTime(game, .8, step); }
        double fallTime = Math.Sqrt(2 * (212 - 48) / MotionProfiles.Baseline.GravityAcceleration);
        MoveTo(game, 750 - RaceConfig.Speed * fallTime, step);
        Tap(game); AdvanceTime(game, fallTime, step);
        return game;
    }
    static void LandingFixtures()
    {
        foreach (bool ceiling in new[] { false, true })
        foreach (double step in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5 })
        {
            var game = ArriveFixture(ceiling, step); var p = game.Players[0];
            double plane = ceiling ? 62 : 178;
            Near(p.Distance, 750, "Landing center-X", 1e-7);
            Near(RaceSimulation.GetPlayerCenterY(p), plane, "Physical landing plane");
            Check(p.Collisions == 0 && p.BoostsEarned == 1 && p.InstantBoostsEarned == 1,
                "Precise contact awards instant without hit");
            Check(p.Support == SupportKind.Stone && p.SupportSide == (ceiling ? 0 : 1)
                && p.SupportObstacleId == "fixture-stone" && !p.Flipping && p.AirFlipAvailable,
                "Attracting landing creates stone support and restores charge");
            var depart = Clone(game); Tap(depart); AdvanceTime(depart, .1, step);
            Check(depart.Players[0].Support == SupportKind.None && depart.Players[0].Flipping
                && depart.Players[0].Collisions == 0, "Departure uses stone support without phantom side collision");
            Check(ceiling ? RaceSimulation.GetPlayerCenterY(depart.Players[0]) > plane
                : RaceSimulation.GetPlayerCenterY(depart.Players[0]) < plane,
                "Departure moves into corridor from physical stone plane");
            AdvanceTime(game, .2, step);
            Near(p.Distance, 816, "Reward immediately accelerates supported run", 1e-7);
            Near(RaceSimulation.GetPlayerCenterY(p), plane, "Run remains on entire face outside marker");
            Check(p.Support == SupportKind.Stone && p.Collisions == 0 && p.BoostsEarned == 1,
                "Leaving marker while supported is safe and does not re-award");
            AdvanceTime(game, .3, step);
            Check(p.Distance > 880 && p.Support == SupportKind.None && p.Flipping,
                "Boosted edge walkoff actually leaves stone support");
            Check(ceiling ? RaceSimulation.GetPlayerCenterY(p) < plane : RaceSimulation.GetPlayerCenterY(p) > plane,
                "Walkoff follows gravity toward wall behind stone");
            Finish(game, step); CheckClean(new RouteResult { Game = game }, 1, 0, 9.7);
        }
    }
}
