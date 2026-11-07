using System;
using FlipRace.Core;
namespace FlipRace.Core
{
    public struct BotSlice
    {
        public string Kind;
        public double Delta,Time,Distance;
        public bool Accepted;
        public int Cursor;
    }
    // Supplies ordinary input edges from a fixed plan.
    public sealed class BotRaceController : IRaceScheduledInput
    {
        public const double MaxSlice=1d/120;
        readonly GameState game;
        readonly int slot;
        readonly double[] distances;
        int cursor;
        public int Cursor {get{return cursor;}}
        public double MinimumSampledMargin {get;private set;}
        public double ConservativeMargin {get{return MinimumSampledMargin-game.PartyCameraSpeed*MaxSlice;}}
        public Action<BotSlice> Observer;
        private BotRaceController(GameState game,int slot,double[] distances)
        {
            if(game==null||slot<0||slot>=game.Players.Length)throw new ArgumentOutOfRangeException(nameof(slot));
            this.game=game;this.slot=slot;this.distances=(double[])distances.Clone();
            for(int i=0;i<distances.Length;i++)if(double.IsNaN(distances[i])||double.IsInfinity(distances[i])||distances[i]<0||(i>0&&distances[i]<distances[i-1]))throw new ArgumentException("Ordered finite distance plan required");
            MinimumSampledMargin=game.Players[slot].Distance+RaceConfig.PlayerRadius-game.PartyCameraLeft;
        }
        public BotRaceController(GameState game) : this(game, 1,
            game != null && game.IsBot ? game.BotPlan.Distances : throw new ArgumentException("A bot race is required", nameof(game))) { }
        public void Advance(double delta, bool paused = false)
        { RaceRunner.Advance(game, delta, this, paused); }

        GameState IRaceScheduledInput.Race => game;
        int IRaceScheduledInput.Slot => slot;

        bool IRaceScheduledInput.TryGetPress(double remaining, out double step)
        {
            step = Math.Min(remaining, MaxSlice);
            var player = game.Players[slot];
            if (player.Status != PlayerStatus.Running || cursor >= distances.Length) return false;
            double gap = distances[cursor] - player.Distance;
            if (gap <= 2e-7) return true;
            step = player.Recovering > 0 ? Math.Min(step, player.Recovering)
                : Math.Min(step, Math.Max(1e-9, gap / player.MaximumHorizontalSpeed));
            return false;
        }

        void IRaceScheduledInput.PressCompleted(bool accepted)
        {
            Observer?.Invoke(new BotSlice { Kind = "press", Time = game.Elapsed,
                Distance = game.Players[slot].Distance, Cursor = cursor, Accepted = accepted });
            cursor++;
        }

        void IRaceScheduledInput.AdvanceCompleted(double delta, bool wasRunning)
        {
            // Stop measuring risk once this participant has reached an outcome.
            if(wasRunning)MinimumSampledMargin=Math.Min(MinimumSampledMargin,game.Players[slot].Distance+RaceConfig.PlayerRadius-game.PartyCameraLeft);
            Observer?.Invoke(new BotSlice { Kind = "advance", Delta = delta, Time = game.Elapsed,
                Cursor = cursor, Distance = game.Players[slot].Distance });
        }
    }
}
