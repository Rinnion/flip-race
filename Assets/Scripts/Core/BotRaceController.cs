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
    // Advances the shared world and emits only ordinary input edges from a fixed plan.
    public sealed class BotRaceController
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
        void Step(double dt)
        {
            bool wasRunning=game.Players[slot].Status==PlayerStatus.Running;
            RaceSimulation.Advance(game,dt);
            // Stop measuring risk once this participant has reached an outcome.
            if(wasRunning)MinimumSampledMargin=Math.Min(MinimumSampledMargin,game.Players[slot].Distance+RaceConfig.PlayerRadius-game.PartyCameraLeft);
            Observer?.Invoke(new BotSlice{Kind="advance",Delta=dt,Time=game.Elapsed,Cursor=cursor,Distance=game.Players[slot].Distance});
        }
        public void Advance(double delta,bool paused=false)
        {
            if(paused||double.IsNaN(delta)||double.IsInfinity(delta)||delta<=0)return;
            double remaining=delta;int guard=0;
            while(remaining>0)
            {
                if(++guard>100000)throw new InvalidOperationException("Controller step guard");
                if(game.Phase=="countdown")
                {
                    double part=Math.Min(remaining,Math.Max(1e-9,game.CountdownRemaining));Step(part);remaining-=part;continue;
                }
                if(game.Phase!="racing"){Step(remaining);break;}
                var player=game.Players[slot];
                if(player.Status==PlayerStatus.Running&&cursor<distances.Length&&distances[cursor]-player.Distance<=2e-7)
                {
                    bool accepted=RaceSimulation.Press(game,slot);
                    Observer?.Invoke(new BotSlice{Kind="press",Time=game.Elapsed,Distance=player.Distance,Cursor=cursor,Accepted=accepted});
                    cursor++;continue;
                }
                double dt=Math.Min(remaining,MaxSlice);
                if(player.Status==PlayerStatus.Running&&cursor<distances.Length)
                {
                    // Recovery and the remaining distance come from the live player.
                    // 440 is the maximum speed of all currently supported effects.
                    dt=player.Recovering>0?Math.Min(dt,player.Recovering):Math.Min(dt,Math.Max(1e-9,(distances[cursor]-player.Distance)/RaceConfig.FinishBoostSpeed));
                }
                Step(dt);remaining-=dt;
            }
        }
    }
}