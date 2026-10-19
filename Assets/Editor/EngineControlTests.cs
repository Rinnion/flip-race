using System;
using FlipRace.Core;

namespace FlipRace.Editor
{
    public static class EngineControlTests
    {
        static int assertions;
        public static string RunAll()
        {
            assertions = 0;
            Action[] tests = { FloorLandingAndRun, FloorDeparture, ForeignCollisionAtLanding,
                WalkOffAndMirror, PadBoundariesAndWrongContact, PersistentPadAndWrongReturn, InertialAndTangentContact,
                LandingFinishesLinkedManeuver, ProfileEdgeFlip, LandingPauseSnapshotAndReset,
                IndependentPlayersAndPartialFinish, LandingAndNegativeTie, TangentNearMiss, LinearAirReverse,
                ModerateAndLinearFlights, OrdinaryJumpAndAirGravity, UnlimitedInputEdges, FourModesOnStone,
                NewModesEffectsSnapshotsAndReset };
            foreach (var test in tests)
                try { test(); }
                catch (Exception error) { throw new Exception("CONTROL FAIL " + test.Method.Name + ": " + error.Message, error); }
            return "PASS: CONTROL " + tests.Length + " scenarios / " + assertions + " assertions.";
        }
        static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new Exception(message); }
        static void Near(double actual, double expected, string message, double tolerance = 1e-8)
        { Check(Math.Abs(actual - expected) < tolerance, message + ": " + actual + " vs " + expected); }
        static Track StoneTrack(bool ceiling = false, double zoneX = 350, double width = 12, double stoneWidth = 200)
        {
            return new Track(2200, new[] { new Obstacle { Id = "stone", Kind = ceiling ? "ceiling" : "floor",
                X = 300, Width = stoneWidth, Height = 48 } })
            { RiskZones = new[] { new RiskZone { Id = "pad", Kind = "landing", ObstacleId = "stone",
                X = zoneX, Y = ceiling ? 48 : 192, LandingWidth = width } } };
        }
        static GameState Start(Track track, MotionProfile profile = null)
        {
            var game = RaceSimulation.CreateTrial(track, MotionMode.GravityAirFlip, profile);
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            return game;
        }
        static void Tap(GameState game)
        { Check(RaceSimulation.Press(game, 0) && RaceSimulation.Press(game, 1), "both action edges accepted"); }
        static void AdvanceFor(GameState game, double duration, double frame)
        {
            while (duration > 1e-12 && game.Phase == "racing")
            { double step = Math.Min(duration, frame); RaceSimulation.Advance(game, step); duration -= step; }
        }
        static void FloorLandingAndRun()
        {
            foreach (double frame in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 100d })
            {
                var game = Start(StoneTrack()); Tap(game); AdvanceFor(game, .8, frame);
                double fall = Math.Sqrt(328 / 662.5);
                AdvanceFor(game, (350 - 220 * fall) / 220 - .8, frame); Tap(game);
                AdvanceFor(game, fall, frame);
                var p = game.Players[0];
                Check(p.Collisions == 0 && p.BoostsEarned == 1, "plane + center-X landing replaces the stone collision");
                Near(p.Distance, 350, "landing center-X"); Near(RaceSimulation.GetPlayerCenterY(p), 178, "stone top plus player radius");
                Check(p.Support == SupportKind.Stone && p.SupportObstacleId == "stone" && p.SupportSide == 1
                    && !p.Flipping && p.AirFlipAvailable, "landing creates explicit stone support and restores repeat");
                Near(p.BoostRemaining, .6, "landing awards immediate full boost");
                AdvanceFor(game, .2, frame);
                Near(p.Distance, 416, "boost moves along the whole stone face outside the narrow pad");
                Near(RaceSimulation.GetPlayerCenterY(p), 178, "supported center stays on the face");
                Check(p.Collisions == 0 && p.BoostsEarned == 1 && !p.HitObstacleIds.Contains("stone"),
                    "legal contact is neither a hit nor global obstacle immunity");
            }
        }
        static GameState Arrive(bool ceiling, double x, double frame, Track track = null, MotionProfile profile = null)
        {
            profile = profile ?? MotionProfiles.Baseline;
            var game = Start(track ?? StoneTrack(ceiling), profile);
            double a = profile.GravityAcceleration, u = profile.DepartureImpulse;
            if (!ceiling) { Tap(game); AdvanceFor(game, 424 / (u + Math.Sqrt(u * u + 424 * a)), frame); }
            double fall = 328 / (u + Math.Sqrt(u * u + 328 * a));
            AdvanceFor(game, x / 220 - fall - game.Elapsed, frame);
            Tap(game); AdvanceFor(game, fall, frame);
            return game;
        }
        static void FloorDeparture()
        {
            var game = Arrive(false, 350, .317); var p = game.Players[0];
            Tap(game);
            Check(p.Support == SupportKind.None && p.SupportObstacleId == null && p.Flipping
                && p.Gravity == -1 && p.FlipTo == 0, "floor stone departs toward ceiling with explicit airborne state");
            AdvanceFor(game, .1, .317);
            Near(RaceSimulation.GetPlayerCenterY(p), 174.6875, "stone departure starts at its own physical plane");
            Near(p.VerticalSpeed, -66.25, "stone departure uses existing profile without phantom impulse");
            Check(p.Collisions == 0 && p.BoostsEarned == 1, "departure does not hit or recollect its own face");
        }
        static void ForeignCollisionAtLanding()
        {
            var track = StoneTrack();
            track.Obstacles = new[] { track.Obstacles[0], new Obstacle { Id = "foreign", Kind = "floor", X = 364, Width = 64, Height = 92 } };
            var game = Arrive(false, 350, .317, track); var p = game.Players[0];
            Check(p.Collisions == 1 && p.LastHit.Id == "foreign" && !p.HitObstacleIds.Contains("stone"),
                "landing priority suppresses only its own stone, not a simultaneous foreign side hit");
            Check(p.BoostsEarned == 1 && p.Support == SupportKind.Stone && p.SupportObstacleId == "stone",
                "the independent legal contact awards and establishes support in the same event");
            Near(p.LastInstant.At, 350d / 220, "landing reward is not postponed until recovery ends");
            Near(p.Recovering, .5, "foreign hit keeps its whole recovery");
            AdvanceFor(game, .25, .317);
            Near(p.Distance, 350, "foreign impact freezes the supported runner");
            Near(p.BoostRemaining, .6, "landing impulse duration is frozen during recovery");
        }
        static void Finish(GameState game, double frame)
        {
            int guard = 0;
            while (game.Phase == "racing")
            { RaceSimulation.Advance(game, frame); if (++guard > 100000) throw new Exception("finish stalled"); }
        }
        static void WalkOffAndMirror()
        {
            foreach (bool ceiling in new[] { false, true })
            foreach (double frame in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 100d })
            {
                var game = Arrive(ceiling, 350, frame, StoneTrack(ceiling)); var p = game.Players[0];
                Check(p.Support == SupportKind.Stone && p.SupportSide == (ceiling ? 0 : 1)
                    && p.AirFlipAvailable && p.Collisions == 0, "mirror lands on its own corridor-facing stone face");
                Near(RaceSimulation.GetPlayerCenterY(p), ceiling ? 62 : 178, "mirrored center plane");
                AdvanceFor(game, 150d / 330, frame);
                Near(p.Distance, 500, "support ends at the physical trailing edge, not expanded collision slab");
                Check(p.Support == SupportKind.None && p.SupportObstacleId == null && p.Flipping && p.AirFlipAvailable,
                    "walk-off creates airborne state without spending its available flip");
                Near(p.VerticalSpeed, 0, "walk-off adds no departure impulse");
                AdvanceFor(game, .1, frame);
                Near(RaceSimulation.GetPlayerCenterY(p), ceiling ? 58.6875 : 181.3125, "walk-off follows existing gravity from rest");
                Check(p.Collisions == 0 && !p.HitObstacleIds.Contains("stone"), "expanded trailing slab cannot create a phantom hit");
                AdvanceFor(game, Math.Sqrt(96 / 662.5) - .1, frame);
                Check(p.Support == SupportKind.Wall && p.SupportSide == (ceiling ? 0 : 1) && !p.Flipping && p.AirFlipAvailable,
                    "wall landing is distinct from the previous stone support");
                Finish(game, frame);
                Near(game.Elapsed, 9.7, "full landing boost gives actual .3s advantage");
                Check(p.FinishTime == game.Players[1].FinishTime && Equals(game.Winner, "tie")
                    && p.BoostsEarned == 1 && p.CompletedBoosts == 1, "mirror and both players finish with exactly one full reward");

                var departure = Arrive(ceiling, 350, frame, StoneTrack(ceiling));
                Tap(departure); AdvanceFor(departure, .1, frame); var d = departure.Players[0];
                Near(RaceSimulation.GetPlayerCenterY(d), ceiling ? 65.3125 : 174.6875, "departure from either raised face is continuous");
                Check(d.Support == SupportKind.None && d.Gravity == (ceiling ? 1 : -1)
                    && d.FlipTo == (ceiling ? 1 : 0) && d.Collisions == 0, "mirror departure has matching target, gravity and support");
            }
        }
        static void PadBoundariesAndWrongContact()
        {
            foreach (bool ceiling in new[] { false, true })
            foreach (double frame in new[] { 1d / 144, .317, 100d })
            foreach (double x in new[] { 344d, 356d, 343.999, 356.001 })
            {
                var game = Arrive(ceiling, x, frame, StoneTrack(ceiling)); var p = game.Players[0];
                bool legal = x >= 344 && x <= 356;
                Check(p.BoostsEarned == (legal ? 1 : 0) && p.Collisions == (legal ? 0 : 1),
                    "center-X endpoints are inclusive; player/ring radius does not widen the pad");
                Check(legal ? p.Support == SupportKind.Stone : p.Support == SupportKind.None && p.Recovering > 0,
                    "only a legal center-plane intersection creates support");
            }
            var track = StoneTrack(zoneX: 286 + 220 * Math.Sqrt(96 / 662.5));
            var side = Start(track); AdvanceFor(side, 286d / 220, .317); var s = side.Players[0];
            Check(s.Collisions == 1 && s.BoostsEarned == 0 && s.LastHit.Id == "stone", "earlier real side entry remains a hit");
            Near(s.LastHit.At, 1.3, "side contact occurs at expanded leading boundary");
            AdvanceFor(side, .5, .317); Tap(side); AdvanceFor(side, Math.Sqrt(96 / 662.5), .317);
            Near(s.Distance, track.RiskZones[0].X, "outbound crossing passes through the exact pad center");
            Near(RaceSimulation.GetPlayerCenterY(s), 178, "outbound crossing reaches pad plane");
            Check(s.BoostsEarned == 0 && !s.CollectedRiskIds.Contains("pad") && s.Support == SupportKind.None,
                "moving out of the stone through its marked face gives neither bonus nor support");
        }
        static void PersistentPadAndWrongReturn()
        {
            foreach (bool ceiling in new[] { false, true })
            foreach (double frame in new[] { 1d / 144, .317, 100d })
            {
                var game = Arrive(ceiling, 350, frame, StoneTrack(ceiling, width: 40)); var p = game.Players[0];
                Tap(game); AdvanceFor(game, .01, frame); Tap(game);
                AdvanceFor(game, .01 * (1 + Math.Sqrt(2)), frame);
                Check(p.Support == SupportKind.Stone && p.AirFlipAvailable && p.Collisions == 0,
                    "collected pad still accepts an exact return and restores support");
                Near(p.Distance, 350 + 330 * .01 * (2 + Math.Sqrt(2)), "second landing uses the actual boosted trajectory");
                Check(p.BoostsEarned == 1 && p.InstantBoostsEarned == 1 && p.SuccessfulRiskIds.Count == 1,
                    "repeat legal contact never duplicates the reward");
                Tap(game); AdvanceFor(game, .06, frame); Tap(game);
                AdvanceFor(game, .06 * (1 + Math.Sqrt(2)), frame);
                Check(p.Collisions == 1 && p.Recovering > 0 && p.Support == SupportKind.None && !p.AirFlipAvailable,
                    "new landing outside the mark hits even after an earlier successful landing");
                Check(p.BoostsEarned == 1 && p.SuccessfulRiskIds.Contains("pad") && p.FailedRiskIds.Count == 0,
                    "a later wrong contact does not revoke the earned immediate reward");
            }
        }
        static void InertialAndTangentContact()
        {
            foreach (bool ceiling in new[] { false, true })
            foreach (double tau in new[] { .55, Math.Sqrt(164 / 662.5) })
            foreach (double frame in new[] { 1d / 30, 1d / 144, .317, 100d })
            {
                double x = 450, contact = 2 * tau - Math.Sqrt(Math.Max(0, 2 * tau * tau - 328 / 662.5));
                var game = Start(StoneTrack(ceiling, zoneX: x));
                if (!ceiling) { Tap(game); AdvanceFor(game, .8, frame); }
                AdvanceFor(game, x / 220 - contact - game.Elapsed, frame);
                Tap(game); AdvanceFor(game, tau, frame); Tap(game);
                AdvanceFor(game, contact - tau, frame); var p = game.Players[0];
                Check(p.BoostsEarned == 1 && p.Collisions == 0 && p.Flipping && p.Support == SupportKind.None
                    && !p.AirFlipAvailable && p.Gravity == (ceiling ? 1 : -1), "inertial and tangent pad contact award without support or recharge"
                    + " ceiling=" + ceiling + " tau=" + tau + " frame=" + frame + " earned=" + p.BoostsEarned
                    + " hits=" + p.Collisions + " y=" + RaceSimulation.GetPlayerCenterY(p) + " vy=" + p.VerticalSpeed);
                Near(RaceSimulation.GetPlayerCenterY(p), ceiling ? 62 : 178, "inertial contact clamps to the actual marked face", 1e-6);
                Near(p.VerticalSpeed, 0, "inward velocity is stopped but no outward kick is added", 1e-4);
                Check(!RaceSimulation.Press(game, 0), "inertial contact cannot issue a free repeat");
                AdvanceFor(game, .1, frame);
                Near(RaceSimulation.GetPlayerCenterY(p), ceiling ? 65.3125 : 174.6875, "gravity alone leaves the contacted face", 1e-5);
                Check(p.Collisions == 0 && p.BoostsEarned == 1, "departure from inertial touch is not a second contact");
            }
        }
        static void LandingFinishesLinkedManeuver()
        {
            // The linked reward now accelerates the approach before this
            // landing. Keep the real landing inside a wide marked interval.
            var track = StoneTrack(false, 375, 100);
            track.Obstacles = new[] { new Obstacle { Id = "earlier", Kind = "ceiling", X = 20, Width = 64, Height = 1 }, track.Obstacles[0] };
            double contactAge = Math.Sqrt(328 / 662.5) - 30d / 220;
            track.RiskZones = new[] { track.RiskZones[0], new RiskZone { Id = "after", Kind = "positive", Placement = "after",
                ObstacleId = "earlier", ClearX = 99, X = 320, Y = 14 + .5 * 662.5 * contactAge * contactAge } };
            var game = Arrive(false, 350, .317, track); var p = game.Players[0];
            Check(p.BoostsEarned == 2 && p.InstantBoostsEarned == 1 && p.SuccessfulRiskIds.Contains("after")
                && p.PendingEffects.Count == 0 && p.ActiveEffect.PulseCount == 2,
                "MODES: stone landing extends an already geometrically confirmed linked reward");
            Check(p.ActiveEffect.RingId == "after" && p.ActiveEffect.Elapsed > 0,
                "linked effect is already partly served when landing extends it");
            Near(p.LastInstant.At, 350d / 220, "vertical landing keeps its actual timestamp under earlier boost");
        }
        static void ProfileEdgeFlip()
        {
            foreach (var profile in MotionProfiles.All)
            foreach (bool ceiling in new[] { false, true })
            {
                var game = Arrive(ceiling, 350, .317, StoneTrack(ceiling), profile); var p = game.Players[0];
                Check(p.Support == SupportKind.Stone && p.Collisions == 0, "all previous gravity profiles support legal stone landings");
                AdvanceFor(game, 150d / 330 + .1, .317);
                double incoming = (ceiling ? -1 : 1) * profile.GravityAcceleration * .1;
                Near(p.VerticalSpeed, incoming, "edge fall adds no profile departure kick");
                Tap(game);
                Near(p.VerticalSpeed, incoming, "edge input is an air flip that retains fall velocity");
                Check(!p.AirFlipAvailable && p.Gravity == (ceiling ? 1 : -1) && !RaceSimulation.Press(game, 0),
                    "edge fall permits the existing one repeat, never a new grounded jump");

                var jump = Arrive(ceiling, 350, .317, StoneTrack(ceiling), profile);
                Tap(jump);
                Near(jump.Players[0].VerticalSpeed, (ceiling ? 1 : -1) * profile.DepartureImpulse,
                    "departure from stable stone applies the profile's one ordinary impulse");
            }
        }
        static GameState Copy(GameState game)
        {
            return new GameState { Mode = game.Mode, Motion = game.Motion, Profile = game.Profile, Flight = game.Flight, Track = game.Track,
                Phase = game.Phase, Elapsed = game.Elapsed, CountdownRemaining = game.CountdownRemaining,
                Winner = game.Winner, WinnerAt = game.WinnerAt,
                Players = new[] { EngineTuneTests.CopyMotion(game.Players[0]), EngineTuneTests.CopyMotion(game.Players[1]) } };
        }
        static void LandingPauseSnapshotAndReset()
        {
            foreach (double sinceLanding in new[] { .1, 150d / 330 + .01 })
            {
                var game = Arrive(false, 350, .317); AdvanceFor(game, sinceLanding, .317); var p = game.Players[0];
                var clone = Copy(game);
                Check(clone.Players[0].Support == p.Support && clone.Players[0].SupportSide == p.SupportSide
                    && clone.Players[0].SupportObstacleId == p.SupportObstacleId && clone.Players[0].EdgeClearObstacleId == p.EdgeClearObstacleId,
                    "snapshot retains stone support or the local outbound edge guard");
                double x = p.Distance, y = p.Y, velocity = p.VerticalSpeed, boost = p.BoostRemaining;
                RaceSimulation.Advance(game, 0); RaceSimulation.Advance(game, double.NaN);
                Check(p.Distance == x && p.Y == y && p.VerticalSpeed == velocity && p.BoostRemaining == boost
                    && p.Support == clone.Players[0].Support, "pause leaves contact state and earned effect frozen");
                Finish(game, 100); Finish(clone, 1d / 144);
                Check(p.FinishTime == clone.Players[0].FinishTime && p.Collisions == 0 && clone.Players[0].Collisions == 0,
                    "cloned support continues identically under a different frame subdivision");
                Near(game.Elapsed, 9.7, "snapshot support route physically finishes");
                RaceSimulation.SetCharacter(game, 0, 3); Tap(game); p = game.Players[0];
                Check(p.Support == SupportKind.Wall && p.SupportSide == 1 && p.SupportObstacleId == null
                    && p.EdgeClearObstacleId == null && p.Y == 1 && p.VerticalSpeed == 0 && p.AirFlipAvailable,
                    "rematch starts on the floor without stale stone contact state");
                Check(p.BoostsEarned == 0 && p.SuccessfulRiskIds.Count == 0 && p.ActiveEffect == null
                    && p.PendingEffects.Count == 0 && p.CharacterIndex == 3 && ReferenceEquals(game.Profile, MotionProfiles.Baseline),
                    "rematch clears pad and effects while retaining hero and profile");
            }
        }
        static void IndependentPlayersAndPartialFinish()
        {
            var game = Start(StoneTrack()); RaceSimulation.Press(game, 0); AdvanceFor(game, .8, .317);
            double fall = Math.Sqrt(328 / 662.5);
            AdvanceFor(game, 350d / 220 - fall - game.Elapsed, .317); RaceSimulation.Press(game, 0);
            AdvanceFor(game, fall, .317); Finish(game, 100);
            Check(game.Players[0].BoostsEarned == 1 && game.Players[0].Collisions == 0
                && game.Players[1].BoostsEarned == 0 && game.Players[1].Collisions == 1, "both players independently hit or land on the same stone");
            Near(game.Players[0].FinishTime.Value, 9.7, "collector actual finish");
            Near(game.Players[1].FinishTime.Value, 10.5, "other player's independent side penalty");
            Check(Equals(game.Winner, 0), "landing changes the actual race winner");
            foreach (double frame in new[] { 1d / 30, .317, 100d })
            {
                var track = StoneTrack(); track.Length = 375;
                var partial = Arrive(false, 350, frame, track); Finish(partial, frame); var p = partial.Players[0];
                Near(partial.Elapsed, 5d / 3, "finish while still supported uses only actual partial boost");
                Near(p.FinishTime.Value, 5d / 3, "canonical timestamp agrees with actual partial travel");
                Check(p.Support == SupportKind.Stone && p.BoostsEarned == 1 && p.CompletedBoosts == 0 && Equals(partial.Winner, "tie"),
                    "unserved pad bonus is not credited after the physical finish");
            }
        }
        static void LandingAndNegativeTie()
        {
            var track = StoneTrack();
            track.RiskZones = new[] { track.RiskZones[0], new RiskZone { Id = "slow", Kind = "negative", X = 350, Y = 198, Radius = 6 } };
            var game = Arrive(false, 350, .317, track); var p = game.Players[0];
            Check(p.BoostsEarned == 1 && p.SlowsEarned == 1 && p.ActiveEffect.RingId == "pad"
                && p.PendingEffects.Count == 1 && p.PendingEffects.Peek().RingId == "slow", "simultaneous pad and negative preserve immediate boost plus queued full slow");
            Near(p.LastInstant.At, p.LastSlow.At, "both distinct effects were earned at contact");
            Finish(game, 100);
            Near(game.Elapsed, 10, "opposing fully served effects preserve exact physical base finish");
            Check(p.CompletedBoosts == 1 && p.CompletedSlows == 1 && p.ActiveEffect == null && p.PendingEffects.Count == 0,
                "landing support never loses FIFO duration or partial accounting");
        }
        static void TangentNearMiss()
        {
            foreach (bool ceiling in new[] { false, true })
            foreach (double frame in new[] { 1d / 30, 100d })
            {
                var track = StoneTrack(ceiling, zoneX: 450); track.Obstacles[0].Height -= 1e-5;
                var game = Start(track);
                if (!ceiling) { Tap(game); AdvanceFor(game, .8, frame); }
                double tau = Math.Sqrt(164 / 662.5);
                AdvanceFor(game, 450d / 220 - 2 * tau - game.Elapsed, frame);
                Tap(game); AdvanceFor(game, tau, frame); Tap(game); AdvanceFor(game, tau + .1, frame);
                Check(game.Players[0].BoostsEarned == 0 && game.Players[0].Collisions == 0,
                    "near-tangent miss cannot be promoted into a contact by radius or frame tolerance");
            }
        }
        static GameState StartMode(MotionMode mode, Track track = null, MotionProfile profile = null)
        {
            var game = RaceSimulation.CreateTrial(track ?? new Track(2200, Array.Empty<Obstacle>()), mode, profile);
            RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1); RaceSimulation.Advance(game, 3);
            return game;
        }
        static void LinearAirReverse()
        {
            var game = StartMode(MotionMode.LinearAirFlip); Tap(game); AdvanceFor(game, .2, .317); var p = game.Players[0];
            Near(RaceSimulation.GetPlayerCenterY(p), 173, "new linear mode moves exactly 265 units per second");
            Near(p.VerticalSpeed, -265, "linear velocity is explicit");
            double y = p.Y; Tap(game);
            Near(p.Y, y, "linear air reversal never teleports position"); Near(p.VerticalSpeed, 265, "linear air reversal changes only velocity sign");
            Check(!p.AirFlipAvailable && !RaceSimulation.Press(game, 0), "linear permits exactly one reversal until landing");
            AdvanceFor(game, .2, .317);
            Check(p.Y == 1 && !p.Flipping && p.Support == SupportKind.Wall && p.AirFlipAvailable,
                "linear return takes .2 seconds for its remaining53 units, not another .8 seconds");
        }
        static readonly MotionMode[] ControlModes = { MotionMode.GravityAirFlip, MotionMode.LinearAirFlip,
            MotionMode.JumpGravityFlip, MotionMode.GravityUnlimited };
        static MotionProfile ProfileFor(MotionMode mode)
        { return mode == MotionMode.JumpGravityFlip ? MotionProfiles.Jump : mode == MotionMode.LinearAirFlip ? MotionProfiles.Baseline : MotionProfiles.Moderate; }
        static void ModerateAndLinearFlights()
        {
            Check(MotionProfiles.All.Length == 4 && ReferenceEquals(MotionProfiles.Get("base"), MotionProfiles.Baseline)
                && ReferenceEquals(MotionProfiles.Get("moderate"), MotionProfiles.Moderate) && ReferenceEquals(MotionProfiles.Get("jump"), MotionProfiles.Jump),
                "new profiles do not change the four historical presets or their lookup");
            Check(RaceSimulation.CreateTrial(new Track(100, Array.Empty<Obstacle>()), MotionMode.GravityAirFlip).Profile == MotionProfiles.Baseline,
                "existing GravityAirFlip default stays baseline");
            foreach (double frame in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100d })
            {
                var moderate = StartMode(MotionMode.GravityAirFlip, profile: MotionProfiles.Moderate);
                Tap(moderate); AdvanceFor(moderate, .1, frame); var p = moderate.Players[0];
                Near(RaceSimulation.GetPlayerCenterY(p), 214, "moderate first .1 seconds has gravity1000 and only70 impulse");
                Near(p.VerticalSpeed, -170, "moderate initial velocity plus acceleration");
                double y = p.Y, vy = p.VerticalSpeed; Tap(moderate);
                Check(p.Y == y && p.VerticalSpeed == vy && p.Gravity == 1 && !p.AirFlipAvailable && !RaceSimulation.Press(moderate, 0),
                    "moderate repeat preserves momentum and position and is limited to one");
                AdvanceFor(moderate, .4, frame);
                Check(p.Support == SupportKind.Wall && p.Y == 1 && p.AirFlipAvailable,
                    "moderate70 return after this repeat is exactly .5 seconds from departure");

                var linear = StartMode(MotionMode.LinearAirFlip); Tap(linear); AdvanceFor(linear, .8, frame);
                Check(linear.Players[0].Y == 0 && !linear.Players[0].Flipping, "full linear transfer remains212/265=.8s");
                Tap(linear); AdvanceFor(linear, .3, frame); var l = linear.Players[0];
                Near(RaceSimulation.GetPlayerCenterY(l), 93.5, "ceiling linear departure mirrors the floor");
                double position = l.Y; Tap(linear);
                Near(l.Y, position, "ceiling reversal is continuous"); Near(l.VerticalSpeed, -265, "ceiling reversal keeps speed magnitude");
                AdvanceFor(linear, .3, frame);
                Check(l.Y == 0 && l.Support == SupportKind.Wall && l.AirFlipAvailable, "mirrored remainder reaches its real wall in .3 seconds");
                Finish(linear, frame); Near(linear.Elapsed, 10, "linear reversal never changes unboosted forward duration");
            }
        }
        static void OrdinaryJumpAndAirGravity()
        {
            foreach (double frame in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5, 100d })
            {
                var game = StartMode(MotionMode.JumpGravityFlip); Tap(game); var p = game.Players[0];
                Check(ReferenceEquals(game.Profile, MotionProfiles.Jump) && p.Gravity == 1 && p.VerticalSpeed == -400,
                    "ordinary jump starts away400 while retaining gravity toward the floor");
                AdvanceFor(game, .4, frame);
                Near(RaceSimulation.GetPlayerCenterY(p), 119, "ordinary jump height after .4 seconds");
                Near(p.VerticalSpeed, -135, "ordinary jump slows under unchanged gravity");
                AdvanceFor(game, 32d / 53 - .4, frame);
                Near(RaceSimulation.GetPlayerCenterY(p), 105.24528301886792, "ordinary apex rises120.7547 units above the support");
                Near(p.VerticalSpeed, 0, "ordinary apex at400/662.5");
                AdvanceFor(game, 32d / 53, frame);
                Check(p.Y == 1 && !p.Flipping && p.AirFlipAvailable && p.Support == SupportKind.Wall,
                    "without another tap the ordinary jump returns to its original support after1.207547s");

                var falling = StartMode(MotionMode.JumpGravityFlip); Tap(falling); AdvanceFor(falling, .8, frame);
                var f = falling.Players[0]; Near(f.VerticalSpeed, 130, "second tap fixture is already falling");
                double y = f.Y, incoming = f.VerticalSpeed; Tap(falling);
                Check(f.Y == y && f.VerticalSpeed == incoming && f.Gravity == -1 && !f.AirFlipAvailable && !RaceSimulation.Press(falling, 0),
                    "second jump tap changes only gravity, without400 impulse or a third input");
                AdvanceFor(falling, 130 / 662.5, frame);
                Near(f.VerticalSpeed, 0, "falling jump first brakes before turning");
                Check(f.Flipping && falling.Elapsed > .8, "old .8 flight timer cannot truncate a hybrid flight");
                AdvanceFor(falling, Math.Sqrt(2 * (RaceSimulation.GetPlayerCenterY(f) - 14) / 662.5), frame);
                Check(f.Y == 0 && !f.Flipping && f.AirFlipAvailable, "the hybrid reaches the other wall and recharges only on landing");

                var late = StartMode(MotionMode.JumpGravityFlip); Tap(late); AdvanceFor(late, 1.1, frame); Tap(late); var q = late.Players[0];
                double wrongWall = (328.75 - Math.Sqrt(328.75 * 328.75 - 2 * 662.5 * 39.1875)) / 662.5;
                AdvanceFor(late, wrongWall, frame);
                Check(q.Y == 1 && q.Flipping && q.Support == SupportKind.None && !q.AirFlipAvailable,
                    "late gravity reversal may touch the old wall inertially without regaining the repeat");
                Near(q.VerticalSpeed, 0, "wrong-wall contact removes only incoming velocity");
                AdvanceFor(late, .1, frame);
                Near(RaceSimulation.GetPlayerCenterY(q), 222.6875, "wrong-wall departure receives no new400 impulse");
                Near(q.VerticalSpeed, -66.25, "gravity alone leaves the wrong wall");
            }
        }
        static void UnlimitedInputEdges()
        {
            var game = StartMode(MotionMode.GravityUnlimited); Tap(game); AdvanceFor(game, .1, .317); var p = game.Players[0];
            Check(ReferenceEquals(game.Profile, MotionProfiles.Moderate), "unlimited shares the moderate comparison profile");
            double y = p.Y, velocity = p.VerticalSpeed;
            for (int index = 0; index < 257; index++)
            {
                Tap(game);
                Check(p.Y == y && p.VerticalSpeed == velocity && p.AirFlipAvailable,
                    "every distinct unlimited edge is accepted without a cap, cooldown, position jump or extra impulse");
                Check(p.Gravity == (index % 2 == 0 ? 1 : -1), "unlimited gravity sign follows each input edge");
            }
            AdvanceFor(game, .4, .317);
            Check(p.Y == 1 && p.Support == SupportKind.Wall && p.AirFlipAvailable, "unlimited odd number of reversals returns to the floor analytically");
        }
        static double TransferTime(MotionMode mode, double distance)
        {
            if (mode == MotionMode.LinearAirFlip) return distance / 265;
            var profile = ProfileFor(mode); double a = profile.GravityAcceleration, u = profile.DepartureImpulse;
            if (mode == MotionMode.JumpGravityFlip)
            {
                // First .1s is an ordinary jump, then one air tap changes only a.
                double travelled = u * .1 - .5 * a * .01, incoming = u - a * .1;
                return .1 + 2 * (distance - travelled) / (incoming + Math.Sqrt(incoming * incoming + 2 * a * (distance - travelled)));
            }
            return 2 * distance / (u + Math.Sqrt(u * u + 2 * a * distance));
        }
        static void Transfer(GameState game, double time, double frame)
        {
            Tap(game);
            if (game.Motion == MotionMode.JumpGravityFlip) { AdvanceFor(game, .1, frame); Tap(game); time -= .1; }
            AdvanceFor(game, time, frame);
        }
        static GameState ModeLanding(MotionMode mode, bool ceiling, double frame)
        {
            var track = StoneTrack(ceiling, zoneX: 750, width: 40); track.Obstacles[0].X = 700;
            var game = StartMode(mode, track, ProfileFor(mode));
            if (!ceiling) Transfer(game, TransferTime(mode, 212), frame);
            double fall = TransferTime(mode, 164);
            AdvanceFor(game, 750d / 220 - fall - game.Elapsed, frame); Transfer(game, fall, frame);
            return game;
        }
        static void FourModesOnStone()
        {
            foreach (var mode in ControlModes)
            foreach (bool ceiling in new[] { false, true })
            foreach (double frame in new[] { 1d / 30, 1d / 144, .317, 100d })
            {
                var game = ModeLanding(mode, ceiling, frame); var p = game.Players[0];
                Near(p.Distance, 750, "four-mode arrival uses actual analytic trajectory at the narrow pad");
                Check(p.Collisions == 0 && p.Support == SupportKind.Stone && p.AirFlipAvailable && p.BoostsEarned == 1,
                    "all four controls accept and recharge on legal stone landing");
                var departure = Copy(game); Tap(departure); var d = departure.Players[0];
                double u = mode == MotionMode.LinearAirFlip ? 265 : ProfileFor(mode).DepartureImpulse;
                Near(d.VerticalSpeed, (ceiling ? 1 : -1) * u, "stone departure uses the chosen linear speed or initial impulse");
                Check(d.Gravity == (mode == MotionMode.JumpGravityFlip ? (ceiling ? -1 : 1) : (ceiling ? 1 : -1))
                    && d.Support == SupportKind.None && d.Flipping, "hybrid alone keeps support-side gravity on its first jump");
                AdvanceFor(departure, .05, frame);
                Check(d.Collisions == 0 && d.BoostsEarned == 1, "starting from the legal face never creates a t0 hit or repeat award");
                double vy = d.VerticalSpeed; Tap(departure);
                Near(d.VerticalSpeed, mode == MotionMode.LinearAirFlip ? -vy : vy, "air input from stone obeys the selected velocity rule");
                Check(d.AirFlipAvailable == (mode == MotionMode.GravityUnlimited), "stone departure does not add extra limited repeats");

                AdvanceFor(game, 150d / 330, frame);
                Check(p.Support == SupportKind.None && p.Flipping && p.AirFlipAvailable, "all modes walk off the physical edge into airborne state");
                Near(p.VerticalSpeed, mode == MotionMode.LinearAirFlip ? (ceiling ? -265 : 265) : 0,
                    "linear edge fall starts265 toward the same wall; gravity modes start from rest");
                double untilWall = mode == MotionMode.LinearAirFlip ? 48d / 265 : Math.Sqrt(96 / ProfileFor(mode).GravityAcceleration);
                AdvanceFor(game, untilWall, frame);
                Check(p.Y == (ceiling ? 0 : 1) && p.Support == SupportKind.Wall && p.Collisions == 0,
                    "edge fall reaches its wall using remaining distance, without teleport or phantom collision");
                Finish(game, frame); Near(game.Elapsed, 9.7, "every control receives the same complete physical landing benefit");
            }
        }
        static void NewModesEffectsSnapshotsAndReset()
        {
            foreach (var mode in ControlModes)
            {
                var profile = ProfileFor(mode);
                double y = mode == MotionMode.LinearAirFlip ? 120 : mode == MotionMode.JumpGravityFlip ? 119 : 118;
                var track = new Track(2200, Array.Empty<Obstacle>())
                { RiskZones = new[] { new RiskZone { Id = "instant", Kind = "instant", X = 88, Y = y, Radius = 6 } } };
                var game = StartMode(mode, track, profile); Tap(game); AdvanceFor(game, .4, .317); var p = game.Players[0];
                Check(p.BoostsEarned == 1 && p.BoostRemaining > 0, "exact swept free instant works on every new trajectory");
                Near(p.VerticalSpeed, mode == MotionMode.LinearAirFlip ? -265 : mode == MotionMode.JumpGravityFlip ? -135 : -470,
                    "horizontal reward preserves each mode's transverse velocity");
                var clone = Copy(game); double distance = p.Distance, pos = p.Y, boost = p.BoostRemaining;
                RaceSimulation.Advance(game, 0);
                Check(p.Distance == distance && p.Y == pos && p.BoostRemaining == boost, "pause freezes new control and effect state");
                Finish(game, 100); Finish(clone, 1d / 144);
                Near(p.FinishTime.Value, 9.7, "new mode actual full instant finish");
                Check(p.FinishTime == clone.Players[0].FinishTime && p.CompletedBoosts == 1, "snapshot keeps mode, support and impulse accounting");
                Tap(game);
                Check(game.Motion == mode && game.Profile == profile && game.Players[0].Support == SupportKind.Wall
                    && game.Players[0].BoostsEarned == 0 && game.Players[0].VerticalSpeed == 0 && game.Players[0].AirFlipAvailable,
                    "rematch preserves chosen control and profile while resetting motion/support/effects");

                var partialTrack = new Track(55, Array.Empty<Obstacle>()) { RiskZones = new[] {
                    new RiskZone { Id = "slow", Kind = "negative", X = 0, Y = 226 },
                    new RiskZone { Id = "instant", Kind = "instant", X = 42, Y = 226 } } };
                var partial = StartMode(mode, partialTrack, profile); Finish(partial, 100);
                Near(partial.Elapsed, .3, "new controls retain suspended slow served-time accounting at a partial finish");
                Near(partial.Players[0].FinishTime.Value, .3, "partial canonical timestamp equals real finish in every mode");
            }
        }
    }
}
