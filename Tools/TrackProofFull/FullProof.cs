using System;
using FlipRace.Core;
internal static partial class Program
{
    static void FullProof()
    {
        foreach(var track in TracksFull.All)
        {
            foreach(var inputs in new[]{track.SafeRoute,track.RiskRoute,TracksFull.NegativeRoute(track.Id)})
            {
                var entry=Begin(track);
                foreach(double x in inputs){MoveTo(entry,x,100);Tap(entry);}
                MoveTo(entry,7876,100);
                foreach(var p in entry.Players){
                    Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"Authored finish entry has no ordinary effect");
                    foreach(string id in p.CollectedRiskIds)Check(p.SuccessfulRiskIds.Contains(id)||p.FailedRiskIds.Contains(id)||p.NegativeRiskIds.Contains(id),"Authored finish entry has no provisional reward");
                }
            }
            foreach(double step in Steps)
            {
                CheckClean(Run(track,track.SafeRoute,step),0,0,36);
                CheckClean(Run(track,track.RiskRoute,step),10,0,33.2);
                CheckClean(Run(track,TracksFull.NegativeRoute(track.Id),step),10,1,33.5);
            }
            var safe=Run(track,track.SafeRoute);
            for(int i=0;i<track.SafeRoute.Length;i++)foreach(int direction in new[]{-1,1})
            {
                var shifted=Shift(track,track.SafeRoute,safe,i,direction*.06);var p=shifted.Game.Players[0];
                Check(p.Collisions==0 && p.BoostsEarned==0 && p.SlowsEarned==0,"Safe60ms input"+i+" direction"+direction+" hits="+string.Join(',',p.HitObstacleIds));
            }
            Console.WriteLine(track.Id+" safe36/full33.2/negative33.5, 6dt, safe local ±60ms PASS");
        }
    }
}
