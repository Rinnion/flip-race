using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static void Semantics()
    {
        quiet=true;var rows=new List<object>();
        var track=TracksMixed.Get();Check(track.Obstacles.Count(o=>o.Kind=="gate")==5,"five distinct-purpose gates");
        Check(track.Obstacles.Count(o=>o.Width==200)==2,"large before and large landing bodies");
        foreach(var o in track.Obstacles.Where(o=>o.Kind=="gate"))Check(ObstacleGeometry.TryGetBand(o,0,out var a)&&ObstacleGeometry.TryGetBand(o,1,out var b)&&a.Height>0&&b.Height>0,"both real gate halves");
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(bool full in new[]{false,true}){
            var plan=Plan(flight,full);var inputs=plan.Inputs.ToArray();var r=Replay(flight,inputs);Check(Clean(r,full),"semantic replay clean");
            var actual=full?TracksMixed.FullRoute(flight):TracksMixed.SafeRoute(flight);
            Check(actual.Length==inputs.Length&&actual.Zip(inputs,(x,p)=>Math.Abs(x-p.X)<1e-7).All(x=>x),"runtime author arrays match exact planner exports");
            bool fast=flight==FlightPresets.FastFlip;
            if(!full&&fast)Check(r.Samples[0].Support==SupportKind.Wall&&r.Samples[1].Support==SupportKind.None&&r.Samples[1].AirFlipAvailable,"first small uses actual One air return");
            if(!full&&!fast)Check(r.Samples[0].Support==SupportKind.Wall&&r.Samples[1].X>900&&r.Samples[1].Support==SupportKind.Wall,"Main first small one natural jump");
            if(full){int chain=Array.FindIndex(inputs,p=>p.Decision=="4: цепочка, другая высота под ускорением");Check(chain>=0&&r.Speeds[chain]==330,"second chain input uses first earned boost");}
            var g=RaceSimulation.CreateTrial(track,flight);Tap(g);RaceSimulation.Advance(g,3);int input=0;
            foreach(var m in new (string name,double x,int side,bool stone,bool optional)[]{("standard-face",632d,1,true,true),("small-return",900d,1,false,false),
                ("large-opposite",2300d,0,false,false),("straight-same-wall",3260d,1,false,false),("low-floor-return",3800d,1,false,false),
                ("large-pad-face",5326d,1,true,true),("after-return",6330d,1,false,false),("mirrored-low-return",7825d,0,false,false)}){
                while(input<inputs.Length&&inputs[input].X<=m.x){MoveTo(g,inputs[input++].X,.05);Tap(g);}
                MoveTo(g,m.x,.05);var p=g.Players[0];
                int expectedSide=m.name=="after-return"&&!full&&!fast?0:m.side;
                if(!m.optional||full){Check(p.Support==(m.stone?SupportKind.Stone:SupportKind.Wall)&&p.SupportSide==expectedSide,"concrete support "+m.name+" "+(fast?"fast":"main")+" "+(full?"full":"safe")+" actual "+p.Support+" y"+RaceSimulation.GetPlayerCenterY(p));Check(!p.Flipping&&p.AirFlipAvailable,"landing really recharges "+m.name);}
                rows.Add(new{profile=fast?"fast-flip":"chosen",variant=full?"full":"safe",mark=m.name,x=m.x,time=g.Elapsed,p.Support,p.SupportSide,p.SupportObstacleId,p.AirFlipAvailable,p.CurrentSpeed,p.BoostsEarned,p.Collisions});
            }
        }
        File.WriteAllText("Verification/mixed-course-semantics-r2.json",JsonSerializer.Serialize(rows,Json));
        Console.WriteLine("SEMANTICS PASS "+checks+" checks; real same-wall/air return/opposite wall/narrow+large Stone/actual boosted chain, both profiles.");
    }
}
