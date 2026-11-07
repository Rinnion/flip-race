using System;

namespace FlipRace.Core
{
    // A source is bound to one race and slot. It can request one input edge at
    // the current time, or limit the next advance. It does not mutate the race.
    public interface IRaceScheduledInput
    {
        GameState Race { get; }
        int Slot { get; }
        bool TryGetPress(double remaining, out double step);
        void PressCompleted(bool accepted);
        void AdvanceCompleted(double delta, bool wasRunning);
    }

    public static class RaceRunner
    {
        public static bool Press(GameState game, int slot)
        { return RaceSimulation.Press(game, slot); }

        public static void Advance(GameState game, double delta, IRaceScheduledInput input = null, bool paused = false)
        {
            if (paused || double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0) return;
            if (input == null)
            {
                RaceSimulation.Advance(game, delta);
                return;
            }
            if (!ReferenceEquals(game, input.Race))
                throw new ArgumentException("The input source belongs to another race.", nameof(input));
            int slot = input.Slot;
            if (slot < 0 || slot >= game.Players.Length)
                throw new ArgumentException("The input source must belong to a race slot.", nameof(input));

            double remaining = delta;
            int iterations = 0;
            while (remaining > 0)
            {
                if (++iterations > 100000) throw new InvalidOperationException("Race runner step guard");
                double step = remaining;
                if (game.Phase == "countdown")
                    step = Math.Min(remaining, Math.Max(1e-9, game.CountdownRemaining));
                else if (game.Phase == "racing")
                {
                    if (input.TryGetPress(remaining, out step))
                    {
                        input.PressCompleted(Press(game, slot));
                        continue;
                    }
                    if (!(step > 0 && step <= remaining))
                        throw new InvalidOperationException("The input source must return a positive step within the remaining time.");
                }

                bool wasRunning = game.Players[slot].Status == PlayerStatus.Running;
                RaceSimulation.Advance(game, step);
                input.AdvanceCompleted(step, wasRunning);
                remaining -= step;
            }
        }
    }
}
