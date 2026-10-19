using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlipRace.Core;
internal static partial class Program
{
    static RouteResult Shift(Track track, FlightSettings flight, double[] route, RouteResult baseline, int changed, double seconds)
    {
        var result=new RouteResult { Game=Begin(track,flight) };
        for(int i=0;i<route.Length;i++) {
            if(i==changed) AdvanceTime(result.Game,baseline.Inputs[i].Time+seconds-result.Game.Elapsed);
            else MoveTo(result.Game,route[i],.01);
            Tap(result.Game,result,flight==FlightPresets.FastFlip);
        }
        Finish(result.Game,.317);return result;
    }
    static void StageProof()
    {
        var track=TracksFull.Practice();
        foreach(var flight in Profiles) {
            foreach(double step in Steps) {
                CheckClean(Run(track,flight,TracksFull.SafeRoute(track.Id,flight),step),0,0,15);
                CheckClean(Run(track,flight,TracksFull.FullRoute(track.Id,flight),step),5,0,13.5);
                var game=Begin(track,flight);
                foreach(double x in TracksFull.FullRoute(track.Id,flight)) if(x<800) {MoveTo(game,x,step);Tap(game);}
                MoveTo(game,732,step);
                Check(game.Players[0].Support==SupportKind.Stone,"narrow real support");
                AdvanceTime(game,.15,step);
                Check(game.Players[0].Support==SupportKind.None && game.Players[0].Collisions==0,
                    "natural narrow walkoff after150ms without forced input or hit");
                foreach(int branch in new[]{0,1,2}) {
                    game=Begin(track,flight);
                    foreach(double x in TracksFull.FullRoute(track.Id,flight)) if(x<800){MoveTo(game,x,step);Tap(game);}
                    double[] actions = flight==FlightPresets.FastFlip
                        ? branch==0?new[]{1118d,1242}:branch==1?new[]{1118d}:new[]{1300-220*FastT(166)}
                        : branch==0?new[]{1100d,1180}:branch==1?new[]{1100d}:new[]{1170d,1225};
                    foreach(double x in actions){MoveTo(game,x,step);Tap(game,null,flight==FlightPresets.FastFlip);}
                    MoveTo(game,1323,step);var p=game.Players[0];
                    Check(p.SuccessfulRiskIds.Contains("practice-chain-1")== (branch!=2),"first chain independent");
                    Check(p.SuccessfulRiskIds.Contains("practice-chain-2")== (branch!=1),"second chain independent");
                    Check(p.Collisions==0,"independent chain route clear");
                }
            }
            var safe=TracksFull.SafeRoute(track.Id,flight);var baseline=Run(track,flight,safe);
            for(int i=0;i<safe.Length;i++)foreach(double seconds in new[]{-.06,.06})
                CheckClean(Shift(track,flight,safe,baseline,i,seconds),0,0,15);
        }
        Console.WriteLine("MODES-03 STAGE PASS "+checks+" checks; both profiles/6dt, safe ±60ms, narrow walkoff and independent chain.");
    }
    static double FastT(double d){return (Math.Sqrt(1225+5400*d)-35)/2700;}
    static object RouteExport(Track track,FlightSettings flight) {
        var safe=TracksFull.SafeRoute(track.Id,flight);var full=TracksFull.FullRoute(track.Id,flight);
        var run=Run(track,flight,full);var safeRun=Run(track,flight,safe);
        var negative=TracksFull.IsFull(track.Id)?TracksFull.NegativeRoute(track.Id,flight):null;
        var negativeRun=negative==null?null:Run(track,flight,negative);
        return new {id=track.Id,name=track.Name,profile=ProfileId(flight),preset=flight==FlightPresets.Chosen?10:11,
            flight,length=track.Length,safe,full,negative,
            safeFinish=track.Length/220,fullFinish=run.Game.Players[0].FinishTime,
            negativeFinish=negativeRun==null?(double?)null:negativeRun.Game.Players[0].FinishTime,
            inputs=run.Inputs,safeInputs=safeRun.Inputs,negativeInputs=negativeRun==null?null:negativeRun.Inputs,
            obstacles=track.Obstacles,zones=track.RiskZones};
    }
    static void ExportStage() {
        var rows=new List<object>();foreach(var f in Profiles)rows.Add(RouteExport(TracksFull.Practice(),f));
        Directory.CreateDirectory("Verification");
        File.WriteAllText("Verification/modes-stage-routes.json",JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("Exported Verification/modes-stage-routes.json");
    }
}
