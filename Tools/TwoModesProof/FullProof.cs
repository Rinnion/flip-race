using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlipRace.Core;
internal static partial class Program
{
    static bool FullGood(RouteResult r,int negatives=0)
    {
        foreach(var p in r.Game.Players)if(p.Collisions!=0||p.BoostsEarned!=9||p.FinishBoostsEarned!=1||p.SlowsEarned!=negatives
            ||p.ActiveEffect!=null||p.PendingEffects.Count!=0||Math.Abs(p.FinishTime.Value-(33.2+.3*negatives))>1e-8)return false;
        return true;
    }
    static void FullProof()
    {
        foreach(var track in TracksFull.All)foreach(var flight in Profiles) {
            var safe=TracksFull.SafeRoute(track.Id,flight);var full=TracksFull.FullRoute(track.Id,flight);var trap=TracksFull.NegativeRoute(track.Id,flight);
            Check(ReferenceEquals(TracksFull.Get(track.Id,flight),track),"same shared geometry for both profiles");
            foreach(double step in Steps) {
                CheckClean(Run(track,flight,safe,step),0,0,36);
                CheckClean(Run(track,flight,full,step),10,0,33.2);
                CheckClean(Run(track,flight,trap,step),10,1,33.5);
            }
            foreach(var inputs in new[]{safe,full,trap}){
                var g=Begin(track,flight);foreach(double x in inputs){MoveTo(g,x,100);Tap(g);}
                MoveTo(g,7876,100);
                foreach(var p in g.Players){
                    Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"authored clean finish entry");
                    foreach(string id in p.CollectedRiskIds)Check(p.SuccessfulRiskIds.Contains(id)||p.FailedRiskIds.Contains(id)||p.NegativeRiskIds.Contains(id),"no provisional reward at plane");
                }
            }
            var baseline=Run(track,flight,safe);
            for(int i=0;i<safe.Length;i++)foreach(double shift in new[]{-.06,.06})CheckClean(Shift(track,flight,safe,baseline,i,shift),0,0,36);
            baseline=Run(track,flight,full);
            for(int i=0;i<full.Length;i++)foreach(double shift in new[]{-.025,.025})
                Check(FullGood(Shift(track,flight,full,baseline,i,shift)),"full ±25ms "+track.Id+"/"+ProfileId(flight)+" input"+i);
            Console.WriteLine(track.Id+"/"+ProfileId(flight)+" safe36/full33.2/trap33.5;6dt;safe±60ms/full±25ms PASS");
        }
        TailBounds();Console.WriteLine("MODES-04 FULL PASS "+checks+" checks");
    }
    static double ReleaseX(Track track,RiskZone z)
    {
        double x=z.IsLanding?z.X+z.LandingWidth/2:z.X+z.Radius+14;
        foreach(var o in track.Obstacles)if(o.Id==z.ObstacleId){
            if(z.IsLanding)x=Math.Min(x,o.X+o.Width);
            else x=Math.Max(x,Math.Max(z.ClearX,o.X+o.Width+14+1e-7));
        }
        return x;
    }
    static void TailBounds()
    {
        foreach(var t in TracksFull.All){
            var zones=new List<RiskZone>();foreach(var z in t.RiskZones)if(!z.IsFinish)zones.Add(z);
            zones.Sort((a,b)=>ReleaseX(t,a).CompareTo(ReleaseX(t,b)));
            double maximum=0;
            for(int subset=0;subset<(1<<zones.Count);subset++){
                double end=0;for(int i=0;i<zones.Count;i++)if((subset&(1<<i))!=0)end=Math.Max(end,ReleaseX(t,zones[i]))+(zones[i].IsNegative?66:198);
                Check(end<7876,"every attainable reward subset has finite clean tail");maximum=Math.Max(maximum,end);
            }
            Console.WriteLine(t.Id+" all2048 subset bound: effects end byX"+maximum+"; margin"+(7876-maximum));
        }
    }
    static void Windows()
    {
        var rows=new List<object>();
        foreach(var t in TracksFull.All)foreach(var f in Profiles){
            var route=TracksFull.FullRoute(t.Id,f);var baseline=Run(t,f,route);var inputs=new List<object>();double minimum=.15;
            for(int i=0;i<route.Length;i++){
                var limits=new double[2];
                for(int direction=0;direction<2;direction++){
                    double sign=direction==0?-1:1,lo=0,hi=.15;
                    for(int j=1;j<=30;j++){double value=j*.005;bool good;
                        try{good=FullGood(Shift(t,f,route,baseline,i,sign*value));}catch(Exception){good=false;}
                        if(!good){hi=value;break;}lo=value;}
                    if(lo<.15)for(int n=0;n<16;n++){double mid=(lo+hi)/2;bool good;
                        try{good=FullGood(Shift(t,f,route,baseline,i,sign*mid));}catch(Exception){good=false;}
                        if(good)lo=mid;else hi=mid;}
                    limits[direction]=lo;minimum=Math.Min(minimum,lo);
                }
                inputs.Add(new{index=i,x=route[i],time=baseline.Inputs[i].Time,earlyMs=limits[0]*1000,lateMs=limits[1]*1000});
            }
            rows.Add(new{id=t.Id,profile=ProfileId(f),inputs});
            Console.WriteLine(t.Id+"/"+ProfileId(f)+" measured minimum symmetric full window="+(minimum*1000).ToString("F3")+"ms");
        }
        File.WriteAllText("Verification/modes-windows.json",JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
    }
    static void ExportAll()
    {
        var rows=new List<object>();foreach(var f in Profiles){rows.Add(RouteExport(TracksFull.Practice(),f));foreach(var t in TracksFull.All)rows.Add(RouteExport(t,f));}
        File.WriteAllText("Verification/modes-routes.json",JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("Exported Verification/modes-routes.json (both profiles, three races and practice).");
    }
}
