using System;
using System.Runtime.CompilerServices;

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
        // A clone can have the same immutable lineup, but only the original
        // GameState key owns live controllers and advances scheduled inputs.
        static readonly ConditionalWeakTable<GameState, PartyBotRuntime> partyRuntimes =
            new ConditionalWeakTable<GameState, PartyBotRuntime>();

        sealed class PartyBotRuntime
        {
            PartyBotAttempt attempt;
            StochasticBotController[] inputs;

            internal StochasticBotController[] GetInputs(GameState game)
            {
                if (!ReferenceEquals(attempt, game.PartyBots))
                {
                    var nextInputs = new StochasticBotController[game.Players.Length];
                    for (int slot = 0; slot < nextInputs.Length; slot++)
                    {
                        var settings = game.GetPartyBotSettings(slot);
                        if (settings != null) nextInputs[slot] = new StochasticBotController(game, slot, settings);
                    }
                    inputs = nextInputs;
                    attempt = game.PartyBots;
                }
                return inputs;
            }
        }

        internal static void RegisterPartyBots(GameState game)
        { partyRuntimes.Add(game, new PartyBotRuntime()); }

        internal static StochasticBotController GetPartyBotController(GameState game, int slot)
        { return game.GetPartyBotSettings(slot) != null && partyRuntimes.TryGetValue(game, out var runtime)
            ? runtime.GetInputs(game)[slot] : null; }

        public static bool Press(GameState game, int slot)
        { return !(game.IsParty && game.IsBotSlot(slot)) && RaceSimulation.Press(game, slot); }

        public static bool StartAllBotParty(GameState game)
        { return RaceSimulation.StartAllBotParty(game); }

        static bool CanAdvance(double delta, bool paused)
        { return !paused && !double.IsNaN(delta) && !double.IsInfinity(delta) && delta > 0; }

        public static void Advance(GameState game, double delta, IRaceScheduledInput input = null, bool paused = false)
        {
            if (!CanAdvance(delta, paused)) return;
            if (input != null && game.PartyBots != null)
                throw new ArgumentException("This party owns its scheduled inputs.", nameof(input));
            if (partyRuntimes.TryGetValue(game, out var runtime))
            {
                if (game.Phase == "countdown" || game.Phase == "racing")
                    AdvanceInputs(game, delta, null, runtime.GetInputs(game));
                return;
            }
            if (input == null)
            {
                RaceSimulation.Advance(game, delta);
                return;
            }
            ValidateSource(game, input);
            AdvanceInputs(game, delta, input, null);
        }

        // For other adapters, the index is the source's slot, with null entries
        // for slots without scheduled input. Live configured parties own theirs.
        internal static void AdvanceScheduled(GameState game, double delta, IRaceScheduledInput[] inputs, bool paused = false)
        {
            if (!CanAdvance(delta, paused)) return;
            if (game.PartyBots != null)
                throw new ArgumentException("This party owns its scheduled inputs.", nameof(inputs));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.Length != game.Players.Length || inputs.Length > 4)
                throw new ArgumentException("An input or null is required for each of up to four slots.", nameof(inputs));
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                if (input == null) continue;
                ValidateSource(game, input);
                if (input.Slot != i) throw new ArgumentException("Each input must be indexed by its own slot.", nameof(inputs));
            }
            AdvanceInputs(game, delta, null, inputs);
        }

        static void ValidateSource(GameState game, IRaceScheduledInput input)
        {
            if (!ReferenceEquals(game, input.Race))
                throw new ArgumentException("The input source belongs to another race.", nameof(input));
            int slot = input.Slot;
            if (slot < 0 || slot >= game.Players.Length)
                throw new ArgumentException("The input source must belong to a race slot.", nameof(input));
        }

        static void AdvanceInputs(GameState game, double delta, IRaceScheduledInput single, IRaceScheduledInput[] group)
        {
            // Existing explicit single-source adapters receive callbacks for
            // every slice. Owned groups notify only each active slot's span.
            bool legacyCallbacks = single != null;
            int count = legacyCallbacks ? 1 : group.Length;
            double remaining = delta;
            int iterations = 0;
            while (remaining > 0)
            {
                if (++iterations > 100000) throw new InvalidOperationException("Race runner step guard");
                double step = remaining;
                int activeSources = 0;
                if (game.Phase == "countdown")
                    step = Math.Min(remaining, Math.Max(1e-9, game.CountdownRemaining));
                else if (game.Phase == "racing")
                {
                    int dueSources = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var input = single ?? group[i];
                        if (input == null) continue;
                        if (game.Players[input.Slot].Status == PlayerStatus.Running) activeSources |= 1 << i;
                        else if (!legacyCallbacks) continue;

                        if (input.TryGetPress(remaining, out double sourceStep)) dueSources |= 1 << i;
                        else
                        {
                            if (!(sourceStep > 0 && sourceStep <= remaining))
                                throw new InvalidOperationException("The input source must return a positive step within the remaining time.");
                            step = Math.Min(step, sourceStep);
                        }
                    }
                    if (dueSources != 0)
                    {
                        // Inspect every source before applying same-time edges.
                        // Apply and acknowledge them in increasing slot order.
                        for (int i = 0; i < count; i++)
                        {
                            if ((dueSources & (1 << i)) == 0) continue;
                            var input = single ?? group[i];
                            input.PressCompleted(RaceSimulation.Press(game, input.Slot));
                        }
                        continue;
                    }
                }
                else if (!legacyCallbacks) break;

                bool wasRunning = legacyCallbacks && game.Players[single.Slot].Status == PlayerStatus.Running;
                double started = game.Elapsed;
                RaceSimulation.Advance(game, step);
                if (legacyCallbacks) single.AdvanceCompleted(step, wasRunning);
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        if ((activeSources & (1 << i)) == 0) continue;
                        var player = game.Players[i];
                        if (player.TerminalTime.HasValue)
                            group[i].AdvanceCompleted(Math.Max(0, player.TerminalTime.Value - started), true);
                        else if (game.Elapsed > started)
                            group[i].AdvanceCompleted(step, true);
                    }
                }
                remaining -= step;
            }
        }
    }
}
