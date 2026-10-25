using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;
internal static partial class Program
{
    static Track At(Track t,double x,double y,bool include=true)
    {
        var c=Candidate(t,y,include);foreach(var z in c.RiskZones)if(z.IsFinish)z.X=x;return c;
    }
    static (double lo,double hi) Longest(List<double> xs)
    {
        if(xs.Count==0)return(0,0);
        double start=xs[0],previous=start,bestStart=start,bestEnd=start;
        foreach(double x in xs.Skip(1)){if(x-previous>1.001)start=x;previous=x;if(x-start>bestEnd-bestStart){bestStart=start;bestEnd=x;}}
        return(bestStart,bestEnd);
    }
    static bool TargetClear(Track t,double x,double y)
    {
        foreach(var o in t.Obstacles)for(int i=0;i<2;i++)if(ObstacleGeometry.TryGetBand(o,i,out var b))
            if(x+12>b.Left&&x-12<b.Right&&y+10>b.Top&&y-10<b.Bottom)return false;
        return true;
    }
    static void MixedGrid()
    {
        var original=TracksMixed.Get();var flights=new[]{FlightPresets.Chosen,FlightPresets.FastFlip};
        var prefixes=flights.Select(f=>{
            var r=new RouteResult{Game=Begin(original,f)};foreach(double x in FrozenMixed(f,true)){MoveTo(r.Game,x,.317);Tap(r.Game,r);}Check(r.Game.Players.All(p=>p.FinishBoostsEarned==0),"prefix precedes any target contact");return r;
        }).ToArray();
        var results=new List<object>();
        for(double x=7710;x<=7780;x+=5)for(double y=44;y<=136;y+=4)
        {
            if(!TargetClear(original,x,y))continue;
            var target=At(original,x,y);var lists=new List<double>[2];bool valid=true;
            for(int i=0;i<2;i++)
            {
                var baseGame=Clone(prefixes[i].Game);baseGame.Track=target;
                var skip=new RouteResult{Game=Clone(baseGame)};Finish(skip.Game,.317,false);
                if(!Clean(skip,9)||skip.Game.Players[0].FinishBoostsEarned!=0){valid=false;break;}
                lists[i]=new List<double>();
                for(double tap=Math.Ceiling(baseGame.Players[0].Distance)+1;tap<=Math.Min(x+20,7790);tap+=1)
                {
                    try{
                        var take=new RouteResult{Game=Clone(baseGame)};MoveTo(take.Game,tap,.317);Tap(take.Game);Finish(take.Game,.317,false);var p=take.Game.Players[0];
                        if(Clean(take,9)&&p.FinishBoostsEarned==1&&Math.Abs(p.FinishBoostElapsed-.25)<1e-8)lists[i].Add(tap);
                    }catch(Exception){}
                }
                if(lists[i].Count==0){valid=false;break;}
            }
            if(!valid)continue;
            var a=Longest(lists[0]);var b=Longest(lists[1]);
            var row=new{X=x,Y=y,MainFrom=a.lo,MainTo=a.hi,FastFrom=b.lo,FastTo=b.hi,MinimumWidth=Math.Min(a.hi-a.lo,b.hi-b.lo)};
            results.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));
        }
        File.WriteAllText("Verification/fix491-mixed-grid.json",JsonSerializer.Serialize(new{Geometry=new{FinishTargetGeometry.StrokeWidth,FinishTargetGeometry.HalfWidth,FinishTargetGeometry.HalfHeight},Results=results},Json));
    }
}
