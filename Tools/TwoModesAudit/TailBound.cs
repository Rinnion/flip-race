using System;
using System.Collections.Generic;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    sealed class Release { public string Id;public double X,Weight; }
    static void ConditionalTailBounds()
    {
        Console.WriteLine("CONDITIONAL geometric-release bound only: valid after independent after-clear semantics passes; not a current-runtime PASS.");
        foreach(var track in TracksFull.All)
        {
            var releases=new List<Release>(); double finish=double.NaN;
            foreach(var zone in track.RiskZones)
            {
                if(zone.IsFinish){finish=zone.X;continue;}
                double latest;
                if(zone.IsLanding)latest=zone.X+zone.LandingWidth/2;
                else
                {
                    latest=zone.X+zone.Radius+R;
                    if(!zone.IsInstant&&!zone.IsNegative&&!zone.FreeReward)
                    {
                        double safe=zone.ClearX;
                        foreach(var stone in track.Obstacles)if(stone.Id==zone.ObstacleId)safe=Math.Max(safe,stone.X+stone.Width+R+1e-7);
                        latest=Math.Max(latest,safe);
                    }
                }
                releases.Add(new Release{Id=zone.Id,X=latest+1e-6,Weight=zone.IsNegative?66:198});
            }
            releases.Sort((a,b)=>a.X.CompareTo(b.X));
            double worstConservative=0,worstWeighted=0;
            // Exhaustive subsets of potential ordinary +/- pickups. This is an
            // upper-envelope proof, not a claim that all subsets are playable.
            for(int mask=0;mask<(1<<releases.Count);mask++)
            {
                double conservative=0,weighted=0;
                for(int index=0;index<releases.Count;index++)if((mask&(1<<index))!=0)
                {
                    var release=releases[index];
                    conservative=Math.Max(conservative,release.X)+198;
                    weighted=Math.Max(weighted,release.X)+release.Weight;
                }
                worstConservative=Math.Max(worstConservative,conservative);worstWeighted=Math.Max(worstWeighted,weighted);
            }
            Console.WriteLine(track.Id+" subsets="+(1<<releases.Count)+" latest="+releases[releases.Count-1].X.ToString("R")
                +" conservativeCompleteX="+worstConservative.ToString("R")+" weightedCompleteX="+worstWeighted.ToString("R")
                +" finish="+finish.ToString("R")+" conservativeMargin="+(finish-worstConservative).ToString("R"));
        }
    }
}
