using System;
using System.Collections.Generic;
using FlipRace.Core;

namespace FlipRace.MixedAudit
{
    // Preparation only: no authored routes, state injection, or acceptance verdict.
    // A frozen production Track and independently scheduled inputs are supplied
    // by the later stage audit. Input timestamps are actual game elapsed seconds.
    public sealed class ClockInput
    {
        public double At;
        public int Player;
    }

    public sealed class Observation
    {
        public double Elapsed, Distance, Y, VerticalSpeed, HorizontalSpeed;
        public double Recovery, Boost, Slow;
        public SupportKind Support;
        public int Side, Collisions;
        public bool Charge;
        public string SupportObstacle;

        public static Observation Read(GameState game, int player)
        {
            var p = game.Players[player];
            return new Observation {
                Elapsed = game.Elapsed, Distance = p.Distance,
                Y = RaceSimulation.GetPlayerCenterY(p), VerticalSpeed = p.VerticalSpeed,
                HorizontalSpeed = p.CurrentSpeed, Support = p.Support, Side = p.SupportSide,
                SupportObstacle = p.SupportObstacleId, Charge = p.AirFlipAvailable,
                Collisions = p.Collisions, Recovery = p.Recovering,
                Boost = p.BoostRemaining, Slow = p.SlowRemaining
            };
        }
    }

    public sealed class InputObservation
    {
        public ClockInput Input;
        public Observation Before, After;
        public bool Accepted;
    }

    public sealed class RunObservation
    {
        public GameState Game;
        public readonly List<InputObservation> Inputs = new List<InputObservation>();
    }

    public static class Adapter
    {
        public static string Status {
            get { return "PREPARED: generic actual Press/Advance adapter compiled; MIX stage tests have not run."; }
        }

        public static GameState Start(Track track, FlightSettings flight)
        {
            var game = RaceSimulation.CreateTrial(track, flight);
            if (!RaceSimulation.Press(game, 0) || !RaceSimulation.Press(game, 1))
                throw new InvalidOperationException("Two actual readiness inputs must be accepted.");
            RaceSimulation.Advance(game, 3);
            if (game.Phase != "racing") throw new InvalidOperationException("Countdown did not end.");
            return game;
        }

        public static void AdvanceTo(GameState game, double time, double step)
        {
            if (!double.IsFinite(time) || !double.IsFinite(step) || step <= 0)
                throw new ArgumentOutOfRangeException("Finite time and a positive finite step are required.");
            if (time < game.Elapsed - 1e-9) throw new InvalidOperationException("Cannot schedule in the past.");
            int guard = 0;
            while (game.Phase == "racing" && time - game.Elapsed > 1e-10)
            {
                double before = game.Elapsed;
                RaceSimulation.Advance(game, Math.Min(step, time - before));
                if (game.Phase == "racing" && game.Elapsed <= before)
                    throw new InvalidOperationException("No clock progress.");
                if (++guard > 1000000) throw new InvalidOperationException("Scheduler iteration limit.");
            }
        }

        public static InputObservation Press(GameState game, int player, double scheduledTime)
        {
            if (player < 0 || player >= game.Players.Length) throw new ArgumentOutOfRangeException(nameof(player));
            if (game.Phase != "racing") throw new InvalidOperationException("Do not turn a finished run into a rematch.");
            var result = new InputObservation {
                Input = new ClockInput { At = scheduledTime, Player = player },
                Before = Observation.Read(game, player)
            };
            result.Accepted = RaceSimulation.Press(game, player);
            result.After = Observation.Read(game, player);
            return result;
        }

        public static RunObservation Run(Track track, FlightSettings flight,
            IEnumerable<ClockInput> schedule, double step, double deadline)
        {
            var plan = new List<ClockInput>(schedule);
            plan.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Player.CompareTo(b.Player));
            var result = new RunObservation { Game = Start(track, flight) };
            foreach (var input in plan)
            {
                if (!double.IsFinite(input.At) || input.At < 0 || input.At > deadline)
                    throw new ArgumentOutOfRangeException("Input time is outside the requested run.");
                AdvanceTo(result.Game, input.At, step);
                if (result.Game.Phase != "racing") break;
                result.Inputs.Add(Press(result.Game, input.Player, input.At));
            }
            AdvanceTo(result.Game, deadline, step);
            return result;
        }
    }
}
