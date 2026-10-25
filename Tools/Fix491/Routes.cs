using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;
internal static partial class Program
{
    static double[] Ordinary(Track track,FlightSettings flight)=>track.Id==TracksMixed.Id?TracksMixed.FullRoute(flight):TracksRhythm.IsFull(track.Id)?TracksRhythm.FullRoute(track.Id,flight):TracksFull.FullRoute(track.Id,flight).Where(x=>x<7500).ToArray();
    static Track Candidate(Track track,double y,bool include=true)
    {
        return new Track(track.Length,track.Obstacles,track.Cues){Id=track.Id,Name=track.Name,NegativeEnabled=track.NegativeEnabled,
            RiskZones=track.RiskZones.Where(z=>include||!z.IsFinish).Select(z=>z.IsFinish?new RiskZone{Id=z.Id,Kind="finish",X=7780,Y=y,FinishWidth=10,FinishHeight=10}:z).ToArray()};
    }
    static RouteResult Replay(Track track,FlightSettings flight,double[] inputs,double step=1d/60)
    {
        var r=new RouteResult{Game=Begin(track,flight)};
        foreach(double x in inputs){MoveTo(r.Game,x,step);Tap(r.Game,r);}Finish(r.Game,step,false);CaptureReward(r);return r;
    }
    static bool Clean(RouteResult r,int ordinary)=>r.Game.Players.All(p=>p.Collisions==0&&p.SlowsEarned==0&&p.BoostsEarned==ordinary);
    static void Search(bool flowOnly=false)
    {
        Console.WriteLine("Geometry centerline="+FinishTargetGeometry.HalfWidth+","+FinishTargetGeometry.HalfHeight+" stroke="+FinishTargetGeometry.StrokeWidth);
        var evidence=new List<object>();
        foreach(var original in RaceTracks.All.Where(t=>!flowOnly||t.Id==TracksFull.FlowId))
        foreach(double y in original.Id==TracksFull.FlowId?new[]{176d}:original.Id==TracksMixed.Id?new[]{112d,120d,128d,136d}:new[]{64d,80d,96d})
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        {
            var track=Candidate(original,y);var route=Ordinary(track,flight);
            int ordinary=track.RiskZones.Count(z=>!z.IsFinish&&!z.IsNegative&&!z.IsCoin);
            var skip=Replay(track,flight,route);var sp=skip.Game.Players[0];
            var hits=new List<double>();
            for(double x=Math.Max(route.Last()+1,7600);x<=7790;x+=1)
            {
                try{
                    var r=Replay(track,flight,route.Concat(new[]{x}).ToArray(),.317);var p=r.Game.Players[0];
                    if(Clean(r,ordinary)&&p.FinishBoostsEarned==1&&Math.Abs(p.FinishBoostElapsed-.25)<1e-8)hits.Add(x);
                }catch(Exception){}
            }
            var item=new{track=track.Id,y,profile=flight.FirstAction.ToString(),skipClean=Clean(skip,ordinary),skipCollect=sp.FinishBoostsEarned,skipFinish=sp.FinishTime,
                lastOrdinary=route.Last(),takeInputs=hits.ToArray()};evidence.Add(item);
            Console.WriteLine(track.Id+" Y"+y+" "+flight.FirstAction+" skip="+sp.FinishBoostsEarned+" clean="+Clean(skip,ordinary)+" extra="+string.Join(",",hits));
        }
        File.WriteAllText(flowOnly?"Verification/fix491-flow-candidate.json":"Verification/fix491-candidates.json",JsonSerializer.Serialize(new{Geometry=new{FinishTargetGeometry.HalfWidth,FinishTargetGeometry.HalfHeight,FinishTargetGeometry.StrokeWidth},Evidence=evidence},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
