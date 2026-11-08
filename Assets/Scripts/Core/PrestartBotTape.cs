using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    internal readonly struct PrestartBotEdge
    {
        internal readonly double Time, Distance, Y;
        internal readonly int Collisions, NearbyHidden;

        internal PrestartBotEdge(GameState game, int slot)
        {
            var player = game.Players[slot];
            Time = game.Elapsed;
            Distance = player.Distance;
            Y = player.Y;
            Collisions = player.Collisions;
            NearbyHidden = HiddenSignature(game, player.Distance);
        }

        internal static int HiddenSignature(GameState game, double distance)
        {
            int signature = 0;
            for (int i = 0; i < game.Track.Obstacles.Length; i++)
            {
                var obstacle = game.Track.Obstacles[i];
                if (!obstacle.Hidden || ObstacleGeometry.Right(obstacle) < distance - 30
                    || ObstacleGeometry.Left(obstacle) > distance + 125) continue;
                signature = unchecked(signature * 31 + (i + 1) * 3 + (int)HiddenObstacles.GetPhase(game, obstacle));
            }
            return signature;
        }
    }

    internal sealed class PrestartBotPlans
    {
        readonly PrestartBotEdge[][] tapes;
        internal readonly uint Seed;
        internal PrestartBotPlans(PrestartBotEdge[][] tapes, uint seed) { this.tapes = tapes; Seed = seed; }
        internal PrestartBotEdge[] GetTape(int slot) => tapes[slot];
    }

    // Verification uses the same scheduled command ingress as a live race.
    internal sealed class PrestartReplayInput : IRaceScheduledInput
    {
        readonly double[] tape;
        readonly List<PrestartBotEdge> accepted = new List<PrestartBotEdge>();
        PrestartBotEdge pending;
        int cursor;
        public GameState Race { get; }
        public int Slot { get; }
        internal int Rejected { get; private set; }
        internal bool AllRead => cursor == tape.Length;
        internal PrestartBotEdge[] Accepted() => accepted.ToArray();

        internal PrestartReplayInput(GameState race, int slot, double[] tape)
        { Race = race; Slot = slot; this.tape = tape; }

        public bool TryGetPress(double remaining, out double step)
        {
            step = Math.Min(remaining, PrestartBotSearch.Quantum);
            if (AllRead) return false;
            double until = tape[cursor] - Race.Elapsed;
            if (until > 1e-9) { step = Math.Min(step, until); return false; }
            pending = new PrestartBotEdge(Race, Slot);
            return true;
        }

        public void PressCompleted(bool applied)
        {
            if (applied) accepted.Add(pending);
            else Rejected++;
            cursor++;
        }

        public void AdvanceCompleted(double delta, bool wasRunning) { }
    }

    internal sealed class PrestartBotController : IRaceScheduledInput
    {
        internal const int MaximumCorrections = 8;
        readonly PrestartBotEdge[] tape;
        int cursor, lastRepair = -1, collisionOffset;
        double scheduleOffset;
        public GameState Race { get; }
        public int Slot { get; }
        internal int Corrections { get; private set; }
        internal int Accepted { get; private set; }
        internal int Skipped { get; private set; }

        internal PrestartBotController(GameState race, int slot, PrestartBotEdge[] tape)
        { Race = race; Slot = slot; this.tape = tape; }

        public bool TryGetPress(double remaining, out double step)
        {
            step = Math.Min(remaining, PrestartBotSearch.Quantum);
            var player = Race.Players[Slot];
            while (cursor < tape.Length)
            {
                var edge = tape[cursor];
                double until = edge.Time + scheduleOffset - Race.Elapsed;
                if (until > 1e-9) { step = Math.Min(step, until); return false; }
                double behind = edge.Distance - player.Distance;
                bool legal = RaceSimulation.CanPress(Race, Slot);
                bool drifted = Math.Abs(behind) > 26 || Math.Abs(edge.Y - player.Y) > .18;
                bool changedContact = player.Collisions != edge.Collisions + collisionOffset
                    || edge.NearbyHidden != PrestartBotEdge.HiddenSignature(Race, player.Distance);
                if (lastRepair != cursor && Corrections < MaximumCorrections && (!legal || drifted || changedContact))
                {
                    lastRepair = cursor;
                    Corrections++;
                    collisionOffset = player.Collisions - edge.Collisions;
                    double delay = player.Recovering > 0 ? Math.Min(.55, player.Recovering + 1e-7)
                        : behind > 26 ? Math.Min(.45, behind / Math.Max(RaceConfig.Speed, player.CurrentSpeed)) : 0;
                    if (delay > 0)
                    {
                        scheduleOffset += delay;
                        step = Math.Min(step, delay);
                        return false;
                    }
                    if (legal && SaferToWait()) { cursor++; Skipped++; continue; }
                }
                if (legal) return true;
                cursor++;
                Skipped++;
            }
            return false;
        }

        bool SaferToWait()
        {
            var idle = RaceSnapshot.ForBotForecast(Race, Slot);
            var pressed = RaceSnapshot.Clone(idle);
            if (!RaceSimulation.Press(pressed, 0)) return true;
            RaceSimulation.Advance(idle, .5);
            RaceSimulation.Advance(pressed, .5);
            var a = idle.Players[0];
            var b = pressed.Players[0];
            return b.Collisions > a.Collisions || b.SlowsEarned > a.SlowsEarned;
        }

        public void PressCompleted(bool applied)
        {
            if (applied) Accepted++;
            else Skipped++;
            cursor++;
        }

        public void AdvanceCompleted(double delta, bool wasRunning) { }
    }
}
