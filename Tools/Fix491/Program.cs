using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

// Production Press/Advance reproductions. No production geometry changes.
// Wall fixtures isolate a stationary supported arrival at the actual final
// target; full races and Mixed routes use real readiness and actual inputs.
internal static partial class Program
{
    static GameState Begin(Track track,FlightSettings flight)
    {
        var g=RaceSimulation.CreateTrial(track,flight);
        Tap(g);RaceSimulation.Advance(g,RaceConfig.CountdownDuration);return g;
    }
    static int Main(string[] args)
    {
        captureCollision=false; System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
        if(args.Length>0 && args[0]=="--search"){Search();return 0;}
        if(args.Length>0 && args[0]=="--save-baseline"){SaveBaseline();return 0;}
        if(args.Length>0 && args[0]=="--mixed-grid"){MixedGrid();return 0;}
        if(args.Length>0 && args[0]=="--mixed-retime"){MixedRetime();return 0;}
        if(args.Length>0 && args[0]=="--flow"){Search(true);return 0;}
        if(args.Length>0 && args[0]=="--candidate-proof"){Proof(true);return 0;}
        if(args.Length>0 && args[0]=="--proof"){Proof(false);return 0;}
        var evidence=new List<object>();int failures=0;
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        foreach(var track in RaceTracks.All)
        {
            var whole=Begin(track,flight);Finish(whole,1d/60,false);
            var wp=whole.Players[0];
            evidence.Add(new{kind="whole-race-no-actions",track=track.Id,profile=flight.FirstAction.ToString(),finishBoosts=wp.FinishBoostsEarned,collisions=wp.Collisions,finish=wp.FinishTime});
            if(wp.FinishBoostsEarned!=0)failures++;
            foreach(int side in new[]{0,1})
            {
                var g=Begin(track,flight);
                foreach(var p in g.Players){p.Distance=track.RiskZones.Single(z=>z.IsFinish).X-80;p.Y=side;p.Gravity=side==0?-1:1;p.Support=SupportKind.Wall;p.SupportSide=side;}
                Finish(g,1d/60,false);var p0=g.Players[0];
                evidence.Add(new{kind="supported-final-run-in-no-actions",track=track.Id,profile=flight.FirstAction.ToString(),side,finishBoosts=p0.FinishBoostsEarned,collisions=p0.Collisions,finish=p0.FinishTime});
                if(p0.FinishBoostsEarned!=0)failures++;
            }
        }
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        foreach(bool full in new[]{false,true})
        {
            var track=TracksMixed.Get();var g=Begin(track,flight);var inputs=FrozenMixed(flight,full);
            foreach(double x in inputs){MoveTo(g,x,1d/60);Tap(g);}
            var last=g.Players[0];var atLast=new{distance=last.Distance,centerY=RaceSimulation.GetPlayerCenterY(last),support=last.Support.ToString(),last.Flipping};
            Finish(g,1d/60,false);var p=g.Players[0];
            evidence.Add(new{kind="mixed-normal-final-obstacle-no-target-action",profile=flight.FirstAction.ToString(),route=full?"full":"safe",lastInput=atLast,finishBoosts=p.FinishBoostsEarned,collisions=p.Collisions,finish=p.FinishTime});
            if(p.FinishBoostsEarned!=0||p.Collisions!=0||p.SlowsEarned!=0||p.BoostsEarned!=(full?9:0))failures++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new{stage="FIX491 automatic-collection regression; frozen baseline3214a10 inputs",failures,evidence},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(failures==0?"PASS no automatic finish collection":"FAIL automatic finish collection reproduced: "+failures+" cases");
        return failures==0?0:1;
    }
}
