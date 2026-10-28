using System;
using System.Collections.Generic;
using System.Linq;
using FlipRace.Core;
namespace FlipRace.BotPrototype
{
    public sealed class Edge
    {
        public double Distance, Time;
        public bool Accepted;
    }
    public sealed class ProbeResult
    {
        public GameState Game;
        public string[] GoalIds, CompletedIds, MissedIds;
        public Edge[] Actions;
        public double[] Distances;
        public double MinMargin, MinMarginLowerBound;
        public int Total { get { return GoalIds.Length; } }
        public int Completed { get { return CompletedIds.Length; } }
        public double Score { get { return 100d*Completed/Total; } }
        public double Time { get { return Game.Players[0].TerminalTime ?? Game.Elapsed; } }
        public PlayerStatus Status { get { return Game.Players[0].Status; } }
        public bool Finished { get { return Status==PlayerStatus.Finished; } }
        public int Collisions { get { return Game.Players[0].Collisions; } }
        public int Accepted { get { return Actions.Count(a=>a.Accepted); } }
        public int Rejected { get { return Actions.Length-Accepted; } }
    }
    public static class Probe
    {
        public static double[] Safe(Track t,FlightSettings f)
        {return t.Id==TracksMixed.Id?TracksMixed.SafeRoute(f):TracksRhythm.IsFull(t.Id)?TracksRhythm.SafeRoute(t.Id,f):TracksFull.SafeRoute(t.Id,f);}
        public static double[] Full(Track t,FlightSettings f)
        {return t.Id==TracksMixed.Id?TracksMixed.FullRoute(f):TracksRhythm.IsFull(t.Id)?TracksRhythm.FullRoute(t.Id,f):TracksFull.FullRoute(t.Id,f);}
        public static double[] Master(Track t,FlightSettings f)
        {
            var a=Full(t,f).ToList();bool jump=f.FirstAction==FirstAction.Jump;
            double aim=t.Id==TracksMixed.Id?(jump?7639:7650):t.Id==TracksRhythm.CascadeId?(jump?7758:7724):(jump?7736:7706);
            if(t.Id==TracksMixed.Id)a[a.Count-1]=aim;
            else {if(TracksFull.IsFull(t.Id))a.RemoveAll(x=>x>=7500);a.Add(aim);}
            return a.OrderBy(x=>x).ToArray();
        }
        public static string[] Catalog(Track t)
        {return t.Obstacles.Select(o=>"obstacle:"+o.Id).Concat(t.RiskZones.Where(z=>!z.IsNegative&&!z.IsCoin).Select(z=>(z.IsFinish?"finish:":"positive:")+z.Id)).OrderBy(x=>x,StringComparer.Ordinal).ToArray();}
        public static ProbeResult Run(Track t,FlightSettings f,double[] distances,PartyCamera camera=PartyCamera.Constant,double step=1d/120)
        {
            if(step<=0||double.IsNaN(step)||double.IsInfinity(step))throw new ArgumentOutOfRangeException(nameof(step));
            for(int i=0;i<distances.Length;i++)if(distances[i]<0||(i>0&&distances[i]<distances[i-1]))throw new ArgumentException("Monotone distance tape required");
            var g=RaceSimulation.CreateSolo(t,f,camera,-288.8,1520);RaceSimulation.Press(g,0);RaceSimulation.Advance(g,RaceConfig.CountdownDuration);
            var p=g.Players[0];double minimum=p.Distance+RaceConfig.PlayerRadius-g.PartyCameraLeft;
            Action<double> advance=dt=>{RaceSimulation.Advance(g,dt);minimum=Math.Min(minimum,p.Distance+RaceConfig.PlayerRadius-g.PartyCameraLeft);};
            var edges=new List<Edge>();
            foreach(double target in distances)
            {
                int guard=0;
                while(g.Phase=="racing"&&target-p.Distance>2e-7)
                {
                    double dt=p.Recovering>0?Math.Min(step,p.Recovering):Math.Min(step,Math.Max(1e-9,(target-p.Distance)/440));
                    advance(dt);if(++guard>200000)throw new InvalidOperationException("distance guard");
                }
                if(g.Phase!="racing")break;
                var edge=new Edge{Distance=p.Distance,Time=g.Elapsed};edge.Accepted=RaceSimulation.Press(g,0);edges.Add(edge);
            }
            int end=0;while(g.Phase=="racing"&&g.Elapsed<150){advance(step);if(++end>200000)throw new InvalidOperationException("finish guard");}
            var goals=Catalog(t);var completed=new List<string>();
            foreach(var o in t.Obstacles)if(!p.HitObstacleIds.Contains(o.Id)&&p.Distance>=ObstacleGeometry.Right(o)+RaceConfig.PlayerRadius-1e-7)completed.Add("obstacle:"+o.Id);
            foreach(var z in t.RiskZones)
            {
                if(z.IsNegative||z.IsCoin)continue;
                if(z.IsFinish){if(p.FinishBoostsEarned>0&&p.FinishBoostElapsed>=RaceConfig.FinishBoostDuration-1e-7)completed.Add("finish:"+z.Id);}
                else if(p.SuccessfulRiskIds.Contains(z.Id))completed.Add("positive:"+z.Id);
            }
            return new ProbeResult{Game=g,GoalIds=goals,CompletedIds=completed.OrderBy(x=>x,StringComparer.Ordinal).ToArray(),MissedIds=goals.Except(completed).ToArray(),Actions=edges.ToArray(),Distances=(double[])distances.Clone(),MinMargin=minimum,MinMarginLowerBound=minimum-220*step};
        }
    }
}