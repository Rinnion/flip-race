using System.Diagnostics;
using FlipRace.Core;

namespace TrackWorkshop;

public sealed record BotSearchLimits(int MaximumStates=150000,int BeamWidth=128,int MaximumMilliseconds=10000,double Step=.05,double MaximumRaceTime=120);
public sealed record BotSearchResult(bool Found,double[] Inputs,string[] RewardIds,double? FinishTime,int ExpandedStates,long ElapsedMilliseconds,string Message);

/// <summary>Bounded offline witness search. Every branch is ordinary production input and simulation.</summary>
public static class BotPreparationSearch
{
    sealed record Edge(double X,Edge Previous,int Count);
    sealed record Node(GameState Game,Edge Inputs);

    public static BotSearchResult Find(Track track,FlightSettings flight,double warningDuration,BotSearchLimits limits=null,
        CancellationToken cancellation=default,IEnumerable<double[]> seeds=null)
    {
        limits??=new();
        if(limits.MaximumStates<1||limits.BeamWidth<1||limits.BeamWidth>512||limits.MaximumMilliseconds<1||limits.Step<.005||limits.Step>.1
            ||limits.MaximumRaceTime<=0||limits.MaximumRaceTime>120)throw new ArgumentException("Invalid preparation search limits.");
        var clock=Stopwatch.StartNew();int expanded=0;
        bool Within()=>!cancellation.IsCancellationRequested&&expanded<limits.MaximumStates&&clock.ElapsedMilliseconds<limits.MaximumMilliseconds;
        Node best=null;
        bool Good(GameState game)
        {
            var p=game.Players[0];
            int ordinary=track.RiskZones.Count(z=>!z.IsNegative&&!z.IsCoin&&!z.IsFinish&&p.SuccessfulRiskIds.Contains(z.Id));
            return p.Status==PlayerStatus.Finished&&p.Collisions==0&&p.SlowsEarned==0&&p.FailedRiskIds.Count==0&&p.FinishBoostsEarned==1
                &&p.ActiveEffect==null&&p.PendingEffects.Count==0&&p.CompletedBoosts==ordinary
                &&Math.Abs(p.FinishBoostElapsed-RaceConfig.FinishBoostDuration)<1e-7;
        }
        void Consider(Node node)
        {
            if(!Good(node.Game)||node.Inputs==null)return;
            var p=node.Game.Players[0];
            if(best==null||p.FinishTime<best.Game.Players[0].FinishTime-1e-7
                ||(Math.Abs(p.FinishTime.Value-best.Game.Players[0].FinishTime.Value)<1e-7&&(node.Inputs?.Count??0)<(best.Inputs?.Count??0)))best=node;
        }
        GameState Begin()
        {
            var game=RaceSimulation.CreateSolo(track,flight,PartyCamera.Lagger,-288.8,1520);
            HiddenObstacles.ConfigureWarning(game,warningDuration);RaceSimulation.Press(game,0);RaceSimulation.Advance(game,3);return game;
        }
        // Existing/manual inputs are hints only. They are replayed rather than
        // trusted, and an empty seed collection leaves the same beam search.
        foreach(var seed in seeds??Array.Empty<double[]>())
        {
            if(!Within())break;
            var game=Begin();Edge edges=null;double previous=-1;
            foreach(double x in seed)
            {
                if(!Within()||!double.IsFinite(x)||x<0||x>=track.Length||x<=previous){game=null;break;}
                previous=x;
                while(game.Phase=="racing"&&game.Players[0].Distance<x-1e-7&&game.Elapsed<limits.MaximumRaceTime&&Within())
                {
                    RaceSimulation.Advance(game,Math.Min(.05,Math.Max(1e-10,(x-game.Players[0].Distance)/RaceConfig.FinishBoostSpeed)));expanded++;
                }
                if(game.Phase!="racing"||game.Players[0].Collisions>0||!RaceSimulation.Press(game,0)){game=null;break;}
                edges=new Edge(game.Players[0].Distance,edges,(edges?.Count??0)+1);
            }
            if(game==null)continue;
            while(game.Phase=="racing"&&game.Elapsed<limits.MaximumRaceTime&&Within()){RaceSimulation.Advance(game,.05);expanded++;}
            Consider(new(game,edges));
        }
        var beam=new List<Node>{new(Begin(),null)};
        // Preserve a small finite improvement tail after the first clean
        // finish. Rank prefers actual earned bonuses; this is a witness search,
        // never a claim that every compatible bonus or global optimum was found.
        int improvementGenerations=0;
        for(int generation=0;beam.Count>0&&Within();generation++)
        {
            if(best!=null&&improvementGenerations++>=8)break;
            var candidates=new Dictionary<string,Node>(StringComparer.Ordinal);
            foreach(var node in beam)
            {
                if(!Within())break;
                var player=node.Game.Players[0];
                for(int press=0;press<2&&Within();press++)
                {
                    if(press==1&&(!RaceSimulation.CanPress(node.Game,0)||(node.Inputs?.Count??0)>=4096))continue;
                    var game=RaceSnapshot.Clone(node.Game);Edge edges=node.Inputs;
                    if(press==1)
                    {
                        if(!RaceSimulation.Press(game,0))continue;
                        edges=new(player.Distance,edges,(edges?.Count??0)+1);
                    }
                    RaceSimulation.Advance(game,limits.Step);expanded++;
                    var p=game.Players[0];
                    if(p.Collisions>0||p.SlowsEarned>0||game.Elapsed>limits.MaximumRaceTime)continue;
                    var child=new Node(game,edges);
                    if(p.Status==PlayerStatus.Finished){Consider(child);continue;}
                    string key=StateKey(game);
                    if(!candidates.TryGetValue(key,out var prior)||Rank(child)>Rank(prior))candidates[key]=child;
                }
            }
            beam=candidates.Values.OrderByDescending(Rank).Take(limits.BeamWidth).ToList();
        }
        if(best==null)return new(false,Array.Empty<double>(),Array.Empty<string>(),null,expanded,clock.ElapsedMilliseconds,
            cancellation.IsCancellationRequested?"Подготовка отменена.":"В пределах бюджета не найден чистый маршрут с полным финишным рывком. Это не доказывает непроходимость трассы.");
        var result=new double[best.Inputs.Count];for(var e=best.Inputs;e!=null;e=e.Previous)result[e.Count-1]=e.X;
        var success=best.Game.Players[0];
        return new(true,result,track.RiskZones.Where(z=>!z.IsNegative&&!z.IsCoin&&success.SuccessfulRiskIds.Contains(z.Id)).Select(z=>z.Id).ToArray(),success.FinishTime,
            expanded,clock.ElapsedMilliseconds,"Найден чистый маршрут; оптимальность не утверждается.");
    }
    static double Rank(Node node)
    {
        var p=node.Game.Players[0];return p.Distance+p.BoostsEarned*12+p.FinishBoostsEarned*30-(node.Inputs?.Count??0)*.05;
    }
    static string StateKey(GameState game)
    {
        var p=game.Players[0];
        // Mutable hidden clocks and earned IDs are part of branch identity.
        // Coarse kinematic buckets are search pruning, never collision rules.
        return string.Join("|",(int)(p.Distance/5),(int)(RaceSimulation.GetPlayerCenterY(p)/6),(int)(p.VerticalSpeed/50),p.Gravity,p.AirFlipAvailable,p.Flipping,
            p.Support,p.SupportSide,p.ActiveEffect?.IsNegative,(int)((p.ActiveEffect?.Remaining??0)*10),p.BoostsEarned,
            string.Join(",",p.SuccessfulRiskIds.Order()),string.Join(",",game.HiddenObstacles.OrderBy(s=>s.Key).Select(s=>s.Key+":"+s.Value.Phase+":"+s.Value.RevealedAt)));
    }
}
