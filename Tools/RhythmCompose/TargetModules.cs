using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static Track RefinedModule(string kind,bool recoveryBefore=false)
    {
        if(kind=="before")return new Track(recoveryBefore?610:580,new[]{
            new Obstacle{Id="stone",Kind="floor",X=180,Width=64,Height=92},
            new Obstacle{Id="exit-gate",Kind="gate",X=recoveryBefore?400:330,Width=16,GapTop=110,GapBottom=130,TopOffsetX=112}
        }){Id=kind,RiskZones=new[]{new RiskZone{Id="before",ObstacleId="stone",X=80,Y=120,Radius=8,ClearX=259}}};
        if(kind=="chain")return new Track(720,new[]{
            new Obstacle{Id="chain-gate-1",Kind="gate",X=100,Width=16,GapTop=110,GapBottom=130,BottomOffsetX=112},
            new Obstacle{Id="chain-gate-2",Kind="gate",X=420,Width=16,GapTop=110,GapBottom=130,TopOffsetX=112}
        }){Id=kind,RiskZones=new[]{
            new RiskZone{Id="chain-1",Kind="instant",X=134,Y=120,Radius=8},
            new RiskZone{Id="chain-2",Kind="instant",X=454,Y=120,Radius=8}}};
        return new Track(620,new[]{new Obstacle{Id="after-stone",Kind="floor",X=160,Width=64,Height=92}})
        {Id="after",RiskZones=new[]{new RiskZone{Id="after-ring",ObstacleId="after-stone",Placement="after",X=340,Y=120,Radius=8,ClearX=239}}};
    }
    static List<double> Seed(string kind, FlightSettings f,bool full,bool recoveryBefore=false)
    {
        bool fast=f==FlightPresets.FastFlip;
        if(kind=="before"){
            var route=new List<double>(fast?(full?new[]{22d,306}:new[]{78d,335})
                :(full?new[]{22.5,45.5,269.3,335.3}:new[]{57.5,118.5,310.8,354.8}));
            if(recoveryBefore)for(int i=fast?1:2;i<route.Count;i++)route[i]+=70;
            return route;
        }
        if(kind=="chain")return new List<double>(fast?(full?new[]{84d,404}:new[]{118d,438})
            :(full?new[]{58.5,111,378.5,431}:new[]{97.5,134.5,417.5,454.5}));
        return new List<double>(fast?(full?new[]{58d,281}:new[]{58d,200})
            :(full?new[]{37.5,98.5,256,300}:new[]{37.5,98.5,335,379}));
    }
    static void RefineTargets(string[] args)
    {
        var rows=new List<object>();
        foreach(string kind in new[]{"before","after","chain"})foreach(var f in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(bool full in new[]{false,true})
        {
            bool recoveryBefore=args.Contains("--before400");
            var track=RefinedModule(kind,recoveryBefore);var seed=Seed(kind,f,full,recoveryBefore);
            var node=Replay(track,f,seed,1,full);
            if(node==null){Console.WriteLine($"SEED RED {kind}/{f.FirstAction}/{full}: {string.Join(',',seed)}");continue;}
            node=Center(track,f,node,1,full);var times=Times(track,f,node.Inputs);var windows=new List<double>();
            for(int i=0;i<node.Inputs.Count;i++){
                double radius=0;for(double d=.005;d<=.1501;d+=.005){
                    if(Replay(track,f,node.Inputs,1,full,i,-d,times)==null||Replay(track,f,node.Inputs,1,full,i,d,times)==null)break;radius=d;}
                windows.Add(radius);}
            var p=node.Game.Players[0];bool empty=p.ActiveEffect==null&&p.PendingEffects.Count==0;
            Console.WriteLine($"{kind}/{f.FirstAction}/{full}: {string.Join(',',node.Inputs)}; windows={string.Join(',',windows)}; empty={empty}; time={node.Game.Elapsed:R}");
            rows.Add(new{kind,profile=f==FlightPresets.FastFlip?"fast-flip":"chosen",full,route=node.Inputs,times,windows,empty,time=node.Game.Elapsed,
                length=track.Length-20,obstacles=track.Obstacles,zones=track.RiskZones});
        }
        var path=args.Length>1?args[1]:"Verification/rhythm-module-refined-candidates.json";
        File.WriteAllText(path,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
