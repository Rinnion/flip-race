using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;
internal static partial class Program
{
    static void MixedRetime()
    {
        var original=TracksMixed.Get();var flights=new[]{FlightPresets.Chosen,FlightPresets.FastFlip};
        var routes=flights.Select(f=>FrozenMixed(f,true)).ToArray();
        var prefixes=flights.Select((f,i)=>{
            var r=new RouteResult{Game=Begin(original,f)};foreach(double x in routes[i].Take(routes[i].Length-1)){MoveTo(r.Game,x,.317);Tap(r.Game,r);}return r;
        }).ToArray();
        var results=new List<object>();
        for(double x=7710;x<=7780;x+=2)for(double y=44;y<=140;y+=2)
        {
            if(!TargetClear(original,x,y))continue;var target=At(original,x,y);bool valid=true;var lists=new List<double>[2];
            for(int i=0;i<2;i++)
            {
                var baseGame=Clone(prefixes[i].Game);baseGame.Track=target;
                var skip=new RouteResult{Game=Clone(baseGame)};MoveTo(skip.Game,routes[i].Last(),.317);Tap(skip.Game);Finish(skip.Game,.317,false);
                if(!Clean(skip,9)||skip.Game.Players[0].FinishBoostsEarned!=0){valid=false;break;}
                lists[i]=new List<double>();
                double start=Math.Max(Math.Ceiling(baseGame.Players[0].Distance)+1,Math.Floor(routes[i].Last()-100));
                for(double tap=start;tap<=Math.Min(routes[i].Last()+100,x+20);tap+=1)
                {
                    try{
                        var take=new RouteResult{Game=Clone(baseGame)};MoveTo(take.Game,tap,.317);Tap(take.Game);Finish(take.Game,.317,false);var p=take.Game.Players[0];
                        if(Clean(take,9)&&p.FinishBoostsEarned==1&&Math.Abs(p.FinishBoostElapsed-.25)<1e-8)lists[i].Add(tap);
                    }catch(Exception){}
                }
                if(lists[i].Count==0){valid=false;break;}
            }
            if(!valid)continue;var a=Longest(lists[0]);var b=Longest(lists[1]);
            var row=new{X=x,Y=y,MainFrom=a.lo,MainTo=a.hi,FastFrom=b.lo,FastTo=b.hi,MinimumWidth=Math.Min(a.hi-a.lo,b.hi-b.lo)};
            if(row.MinimumWidth>=14){results.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));}
        }
        File.WriteAllText("Verification/fix491-mixed-retime.json",JsonSerializer.Serialize(new{Geometry=new{FinishTargetGeometry.StrokeWidth,FinishTargetGeometry.HalfWidth,FinishTargetGeometry.HalfHeight},Results=results},Json));
    }
}
