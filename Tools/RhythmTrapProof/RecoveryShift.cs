using System;
using System.Linq;
using System.Reflection;
using FlipRace.Core;

internal static partial class Program
{
    static Track ShiftFinalGate(Track source,double shift)
    {
        string id=source.Obstacles.OrderBy(ObstacleGeometry.Left).Last().Id;
        var obstacles=source.Obstacles.Select(o=>{var c=new Obstacle();foreach(var f in typeof(Obstacle).GetFields(BindingFlags.Instance|BindingFlags.Public))f.SetValue(c,f.GetValue(o));if(c.Id==id)c.X+=shift;return c;}).ToArray();
        var zones=source.RiskZones.Select(z=>{var c=new RiskZone();foreach(var f in typeof(RiskZone).GetFields(BindingFlags.Instance|BindingFlags.Public))f.SetValue(c,f.GetValue(z));if(c.Id.EndsWith("-hasty-final",StringComparison.Ordinal))c.X+=shift;return c;}).ToArray();
        return new Track(source.Length,obstacles){Id=source.Id,Name=source.Name,RiskZones=zones,NegativeEnabled=source.NegativeEnabled};
    }
    static double[] ShiftFinalInputs(double[] route,bool full,FlightSettings profile,double shift)
    {
        var result=(double[])route.Clone();int end=route.Length-(full?1:0),count=profile==FlightPresets.FastFlip?1:2;
        for(int i=end-count;i<end;i++)result[i]+=shift;return result;
    }
    static void CheckBoundary(Track track,FlightSettings profile,double[] route,bool full,bool negative,double boundary,double step)
    {
        var game=RaceSimulation.CreateTrial(track,profile);Tap(game);RaceSimulation.Advance(game,3);
        for(int i=0;i<route.Length-(full?1:0);i++){MoveTo(game,route[i],step);Tap(game);}
        MoveTo(game,boundary,step);
        foreach(var p in game.Players)Check(p.Support==SupportKind.Wall&&p.SupportSide==0&&!p.Flipping&&p.Collisions==0&&p.ActiveEffect==null&&p.PendingEffects.Count==0,"shifted last gate retains real wall and empty effects by unchanged module end");
    }
    static void RecoveryShift()
    {
        const double shift=20;
        foreach(string id in new[]{TracksRhythm.CascadeId,TracksRhythm.DenseId,TracksRhythm.WavesId}){
            var original=R5Track(id);double boundary=original.Obstacles.Max(ObstacleGeometry.Left)+190;
            var track=ShiftFinalGate(original,shift);
            foreach(var profile in Profiles)foreach(string variant in new[]{"safe","full","negative"}){
                bool full=variant!="safe",negative=variant=="negative";
                double[] old=R5Route(id,profile,variant);
                var route=ShiftFinalInputs(old,full,profile,shift);
                foreach(double step in Steps){CourseGood(RunCourse(track,profile,route,step),full,negative,id+" shifted "+variant);CheckBoundary(track,profile,route,full,negative,boundary,step);}
                var baseline=RunCourse(track,profile,route,.317);int from=route.Length-(full?1:0)-(profile==FlightPresets.FastFlip?1:2);
                for(int i=from;i<route.Length;i++)foreach(double sign in new[]{-1d,1})CourseGood(RunCourse(track,profile,route,.317,i,sign*(full?.025:.06),baseline.Times),full,negative,id+" shifted last window "+variant);
                Console.WriteLine($"SHIFT20 {id}/{profile.FirstAction}/{variant}: six dt, last inputs {(full?25:60)}ms, finish unchanged, real wall+empty by {boundary:R} PASS");
            }
        }
        RecoveryCases(shift);
        Console.WriteLine($"PASS tool-only final-gate+20: {checks} assertions / {runs} actual course games; runtime unchanged.");
    }
}
