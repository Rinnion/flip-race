using System;
using FlipRace.Core;
internal static partial class Program
{
    static void SearchShortLow()
    {
        foreach(double trapX in new[]{1760d,1770,1780,1790,1800,1810,1820}) {
        var original=TracksFull.Get(TracksFull.ShortId);
        var t=new Track(original.Length,original.Obstacles){Id=original.Id,RiskZones=(RiskZone[])original.RiskZones.Clone(),SafeRoute=original.SafeRoute,RiskRoute=original.RiskRoute};
        for(int i=0;i<t.RiskZones.Length;i++)if(t.RiskZones[i].Id=="short-hasty-trap")t.RiskZones[i]=new RiskZone{Id="short-hasty-trap",Kind="negative",X=trapX,Y=120,Radius=8};
        var chosen=Run(t,FlightPresets.Chosen,t.RiskRoute);var cp=chosen.Game.Players[0];
        Console.WriteLine($"trapX={trapX} chosen+{cp.BoostsEarned} -{cp.SlowsEarned} hits{cp.Collisions}");
        var f=FlightPresets.FastFlip;var prefix=Begin(t,f);
        foreach(double x in new[]{150d,730,1482-220*FastT(164)}){MoveTo(prefix,x,.1);Tap(prefix);}
        int good=0;double lo=0,hi=0;
        for(double x=1680;x<=1775;x+=.5){
            var g=Clone(prefix);MoveTo(g,x,.1);if(g.Players[0].Support==SupportKind.None)continue;Tap(g);MoveTo(g,1900,.1);
            var p=g.Players[0];if(p.SuccessfulRiskIds.Contains("full-short-5-free-low")&&p.SlowsEarned==0&&p.Collisions==0){if(good++==0)lo=x;hi=x;Console.Write(x+" ");}
        }
        Console.WriteLine($"Short same-wall low clean candidates={good} [{lo},{hi}]");
        }
    }
}
