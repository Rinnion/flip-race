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
                MoveTo(entry,track.Length-110,100);
                foreach(var p in entry.Players){
                    Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"Authored finish entry has no ordinary effect");
                    foreach(string id in p.CollectedRiskIds)Check(p.SuccessfulRiskIds.Contains(id)||p.FailedRiskIds.Contains(id)||p.NegativeRiskIds.Contains(id),"Authored finish entry has no provisional reward");
                }
            }
            foreach(double step in Steps)
            {
                CheckClean(Run(track,track.SafeRoute,step),0,0,36);
                CheckOrdinaryFullMotion(Run(track,track.RiskRoute,step),0);
                CheckOrdinaryFullMotion(Run(track,TracksFull.NegativeRoute(track.Id),step),1);
            }
            var safe=Run(track,track.SafeRoute);
            for(int i=0;i<track.SafeRoute.Length;i++)foreach(int direction in new[]{-1,1})
            {
                var shifted=Shift(track,track.SafeRoute,safe,i,direction*.06);var p=shifted.Game.Players[0];
                Check(p.Collisions==0 && p.BoostsEarned==0 && p.SlowsEarned==0,"Safe60ms input"+i+" direction"+direction+" hits="+string.Join(',',p.HitObstacleIds));
            }
            Console.WriteLine(track.Id+" safe36/full33.3/negative33.6 minus actual optional finish duration, 6dt, safe local ±60ms PASS");
        }
    }
    // Ordinary reward routes retain their authored actions. Optional finish
    // contact is checked separately from the nine ordinary reward pulses.
    static void CheckOrdinaryFullMotion(RouteResult result,int slows)
    {
        foreach(var p in result.Game.Players){
            Check(p.Collisions==0,"Clean ordinary full route: "+string.Join(",",p.HitObstacleIds));
            Check(p.BoostsEarned==9&&p.CompletedBoosts==9&&p.SlowsEarned==slows&&p.CompletedSlows==slows,"Exact ordinary reward ledger");
            Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"All ordinary pulses served");
            Check(p.FinishBoostsEarned==0||p.FinishBoostsEarned==1,"Finish is personally available only once");
            Check(!p.FinishBoost,"Physical tape clears finish pulse");
            Near(p.FinishBoostElapsed,p.FinishBoostsEarned==0?0:.25,"Any earned finish pulse is completely served");
            Near(p.FinishBoostDistanceGained,220*p.FinishBoostElapsed,"Only served finish movement earns extra distance");
            Near(p.FinishTime.Value,36-2.7+.3*slows-p.FinishBoostElapsed,"Actual ordinary route physical arrival");
        }
        Check(Equals(result.Game.Winner,"tie"),"Symmetric ordinary inputs retain physical tie");
    }}
