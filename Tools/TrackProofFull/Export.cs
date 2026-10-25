using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using FlipRace.Core;
internal static partial class Program
{
    static void Export()
    {
        var all=new List<object>();
        foreach(var t in TracksFull.All){var f=Run(t,t.RiskRoute);all.Add(new{id=t.Id,name=t.Name,safe=t.SafeRoute,full=t.RiskRoute,negative=TracksFull.NegativeRoute(t.Id),inputs=f.Inputs,obstacles=t.Obstacles,zones=t.RiskZones});}
        File.WriteAllText("Verification/fix491-tracks-routes.json",JsonSerializer.Serialize(all,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("Exported Verification/fix491-tracks-routes.json");
    }
    static void Windows()
    {
        var all=new List<object>();
        foreach(var track in TracksFull.All)
        {
            var baseline=Run(track,track.RiskRoute);var rows=new List<object>();double minimum=1000;
            for(int i=0;i<track.RiskRoute.Length;i++) {
                double[] widths=new double[2];int j=0;
                foreach(int sign in new[]{-1,1}){
                    for(int ms=5;ms<=150;ms+=5){
                        var r=Shift(track,track.RiskRoute,baseline,i,sign*ms/1000.0);var p=r.Game.Players[0];
                        if(p.Collisions!=0||p.BoostsEarned!=9||p.SlowsEarned!=0)break;
                        widths[j]=ms;
                    }j++;
                }
                minimum=Math.Min(minimum,Math.Min(widths[0],widths[1]));
                Check(widths[0]>=5&&widths[1]>=5,"Every ideal edge has a nonzero measured window");
                rows.Add(new{index=i,x=track.RiskRoute[i],time=baseline.Inputs[i].Time,earlyMs=widths[0],lateMs=widths[1]});
            }
            all.Add(new{id=track.Id,inputs=rows});Console.WriteLine("WINDOWS "+track.Id+" all positive, local min ±"+minimum+"ms, grid5ms");
        }
        File.WriteAllText("Verification/fix491-tracks-windows.json",JsonSerializer.Serialize(all,new JsonSerializerOptions{WriteIndented=true}));
    }
}
