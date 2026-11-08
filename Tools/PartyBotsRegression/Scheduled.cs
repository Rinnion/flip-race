using FlipRace.Core;

static partial class Program
{
    sealed class ScriptedSource : IRaceScheduledInput
    {
        readonly double[] times;
        readonly Action<int, double, bool> pressed;
        readonly Action<double> advanced;
        int next;
        public GameState Race { get; }
        public int Slot { get; }

        public ScriptedSource(GameState game, int slot, double[] times, Action<int, double, bool> pressed, Action<double> advanced = null)
        {
            Race = game;
            Slot = slot;
            this.times = times;
            this.pressed = pressed;
            this.advanced = advanced;
        }

        public bool TryGetPress(double remaining, out double step)
        {
            step = next == times.Length ? remaining : Math.Min(remaining, times[next] - Race.Elapsed);
            return next < times.Length && step <= 0;
        }

        public void PressCompleted(bool accepted)
        {
            pressed(Slot, Race.Elapsed, accepted);
            next++;
        }

        public void AdvanceCompleted(double delta, bool wasRunning) => advanced?.Invoke(delta);
    }

    static void ScheduledContract()
    {
        var track = new Track(7920, Array.Empty<Obstacle>());
        var game = RaceSimulation.CreateParty(track, FlightPresets.FastFlip, 4, PartyCamera.Lagger, -288.8, 1520);
        for (int slot = 0; slot < 4; slot++) RaceRunner.Press(game, slot);
        RaceRunner.Advance(game, 3);
        var ordinary = RaceSnapshot.Clone(game);
        var presses = new List<(int Slot, double Time)>();
        var steps = new List<double>();
        void Pressed(int slot, double time, bool accepted)
        {
            Need(accepted == RaceSimulation.Press(ordinary, slot), "Scheduled command did not use ordinary legality");
            presses.Add((slot, time));
            Same(game, ordinary, "Scheduled command differs from ordinary press");
        }
        var source0 = new ScriptedSource(game, 0, new[] { .125, .25 }, Pressed, delta =>
        {
            steps.Add(delta);
            RaceSimulation.Advance(ordinary, delta);
            Same(game, ordinary, "Scheduled shared step advanced the world more than once");
        });
        var source2 = new ScriptedSource(game, 2, new[] { .0625, .125 }, Pressed);
        var source3 = new ScriptedSource(game, 3, new[] { .125, .1875 }, Pressed);
        var sources = new IRaceScheduledInput[] { source0, null, source2, source3 };
        RaceRunner.AdvanceScheduled(game, .375, sources, paused: true);
        Same(game, ordinary, "Pause changed scheduled world");
        Need(steps.Count == 0 && presses.Count == 0, "Pause polled or committed sources");
        Throws<ArgumentException>(() => RaceRunner.AdvanceScheduled(game, .4,
            new IRaceScheduledInput[] { source0, source0, source2, source3 }), "Duplicate slot was accepted");
        Throws<ArgumentException>(() => RaceRunner.AdvanceScheduled(game, .4,
            new IRaceScheduledInput[] { new ScriptedSource(ordinary, 0, new[] { .125 }, Pressed), null, source2, source3 }),
            "Foreign race source was accepted");
        Same(game, ordinary, "Invalid source mutated the race");
        RaceRunner.AdvanceScheduled(game, .375, sources);
        Need(presses.SequenceEqual(new[] { (2, .0625), (0, .125), (2, .125), (3, .125), (3, .1875), (0, .25) }),
            "Common schedule did not honor earliest boundary and ascending slot ties");
        Need(steps.SequenceEqual(new[] { .0625, .0625, .0625, .0625, .125 }), "Common slice schedule is incorrect");
        Need(game.Elapsed == .375, "Shared clock diverged under multiple sources");
    }
}
