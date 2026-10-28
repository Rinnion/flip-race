using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;

internal static partial class Program
{
    const double SampleStep=.005;
    static readonly double[] Widths={1190,826,550};
    sealed class Interval { public double Start, End; public bool Returned; }
    sealed class CameraRun {
        public string Model;
        public double Width, MaxGap, MaxLeaderFraction, MaxCameraLeft;
        public double MinPlayerX=double.PositiveInfinity;
        public List<Interval>[] Offscreen={new List<Interval>(),new List<Interval>()};
        public double[] FinishScreenX=new double[2];
        public double FirstFinishCameraLeft, LastCameraLeft;
        public int ContinuingLaggerSamples; public bool FinishedLeaderOffscreen;
        public bool[] Open=new bool[2];
        public double[] PreviousX=new double[2];
    }
    static double Project(CameraRun c,SharedFieldView view,GameState game,double worldX,out double cameraLeft)
    {
        cameraLeft=view.CameraLeft;
        return (worldX-cameraLeft)*view.ScaleX;
    }
    sealed class TapRecord { public int Player,Index; public double X,Time; public string Support; public double Speed; }
    sealed class Duel {
        public string Profile,Routes; public double Elapsed; public string Winner;
        public int LeaderChanges;
        public double?[] Finish; public int[] Hits,Ordinary,Final; public double[] Distance;
        public List<TapRecord> Inputs=new List<TapRecord>(); public List<CameraRun> Cameras=new List<CameraRun>();
    }
    static GameState StartFlight(FlightSettings flight)
    {
        var game=RaceSimulation.CreateTrial(TracksMixed.Get(),flight);
        Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"Real ready inputs");
        RaceSimulation.Advance(game,RaceConfig.CountdownDuration);return game;
    }
    static string Fingerprint(GameState game)
    { return JsonSerializer.Serialize(game,new JsonSerializerOptions{IncludeFields=true}); }
    static void CameraContracts()
    {
        var g=StartFlight(FlightPresets.Chosen);
        foreach(double width in Widths) {
            double anchor=Math.Clamp(width*.19,62,195);
            // Synthetic projection-only positions; these are not gameplay witnesses.
            foreach(double start in new[]{-100.0,0,3000,7920,8000})
            foreach(double gap in new[]{0.0,200,600,1000,4000})
            foreach(int leader in new[]{0,1}) {
                g.Players[leader].Distance=start+gap;g.Players[1-leader].Distance=start;
                string before=Fingerprint(g);
                var half=SharedFieldView.Create(width,132.5,g);var full=SharedFieldView.Create(width,289,g);
                Near(half.AnchorX,anchor,"Exact legacy anchor");
                Near(half.ScaleX,(width-anchor-20)/Math.Max(1080,width*1.28),"Unchanged legacy horizontal scale");
                Near(half.ScaleX,full.ScaleX,"Height-independent horizontal scale");
                Near(half.CameraLeft,full.CameraLeft,"Height-independent camera");
                Near(half.PlayerX(0),full.PlayerX(0),"Height-independent player0 X");
                Near(half.PlayerX(1),full.PlayerX(1),"Height-independent player1 X");
                Near(half.CameraLeft,start-anchor/half.ScaleX,"Minimum distance without start/finish clamp");
                Near(half.PlayerX(1-leader),anchor,"Each lagger stays anchored");
                Near(half.PlayerX(leader),anchor+gap*half.ScaleX,"Leader keeps real gap");
                Check(half.IsPlayerVisible(1-leader),"Lagger always visible");
                Check(before==Fingerprint(g),"Synthetic projection preserves complete Core fingerprint");
            }
            double sx=SharedFieldView.Create(width,132.5,g).ScaleX;
            double threshold=(width-anchor)/sx;
            foreach(int leader in new[]{0,1}) {
                foreach(double extra in new[]{-.001,.001,-.001}) {
                    g.Players[1-leader].Distance=3000;g.Players[leader].Distance=3000+threshold+extra;
                    var edge=SharedFieldView.Create(width,132.5,g);
                    Check(edge.IsPlayerVisible(leader)==(extra<0),"Leader exits and returns RIGHT");
                    Near(edge.PlayerX(1-leader),anchor,"Right exit never moves lagger");
                }
            }
            double previous=0;
            foreach(double delta in new[]{-.001,0,.001}) {
                g.Players[0].Distance=5000+delta;g.Players[1].Distance=5000;
                var overtake=SharedFieldView.Create(width,132.5,g);
                Near(Math.Min(overtake.PlayerX(0),overtake.PlayerX(1)),anchor,"Overtake keeps minimum anchored");
                if(delta==0)Near(overtake.PlayerX(0),overtake.PlayerX(1),"Equal-distance overlap");
                if(delta>-.001)Check(Math.Abs(overtake.CameraLeft-previous)<=.001+1e-8,"Continuous camera across overtake");
                previous=overtake.CameraLeft;
            }
        }
    }
    static Duel Run(FlightSettings flight,string profile,double[][] routes,string label,bool clean)
    {
        var game=StartFlight(flight);var next=new int[2];var result=new Duel{Profile=profile,Routes=label};
        foreach(double width in Widths) {
            result.Cameras.Add(new CameraRun{Width=width,Model="production481-lagger"});
        }
        double previousTime=0;bool firstFinished=false;int lastLeader=-1;
        foreach(var c in result.Cameras) {
            var view=SharedFieldView.Create(c.Width,132.5,game);
            for(int p=0;p<2;p++)c.PreviousX[p]=Project(c,view,game,game.Players[p].Distance,out _);
        }
        int guard=0;
        while(game.Phase=="racing") {
            Check(++guard<30000,"Finite real duel scheduler");
            for(int p=0;p<2;p++) if(next[p]<routes[p].Length&&game.Players[p].Distance>=routes[p][next[p]]-1e-7) {
                Near(game.Players[p].Distance,routes[p][next[p]],"Actual input X",2e-7);
                var player=game.Players[p];
                result.Inputs.Add(new TapRecord{Player=p,Index=next[p],X=player.Distance,Time=game.Elapsed,Support=player.Support.ToString(),Speed=player.CurrentSpeed});
                bool accepted=RaceSimulation.Press(game,p);
                if(clean)Check(accepted,"Authored edge accepted");next[p]++;
            }
            double dt=SampleStep;
            for(int p=0;p<2;p++) {
                var player=game.Players[p];
                if(next[p]>=routes[p].Length||player.FinishTime.HasValue)continue;
                double remaining=routes[p][next[p]]-player.Distance;
                if(remaining<=1e-7)continue;
                if(remaining>dt*440+1e-8)continue;
                var test=Clone(game);RaceSimulation.Advance(test,dt);
                if(test.Players[p].Distance<=routes[p][next[p]]+1e-8)continue;
                double lo=0,hi=dt;
                for(int n=0;n<45;n++) {
                    double mid=(lo+hi)/2;test=Clone(game);RaceSimulation.Advance(test,mid);
                    if(test.Players[p].Distance<routes[p][next[p]])lo=mid;else hi=mid;
                }
                dt=(lo+hi)/2;
            }
            Check(dt>1e-10,"Scheduler advances");
            RaceSimulation.Advance(game,dt);
            if(Math.Abs(game.Players[0].Distance-game.Players[1].Distance)>1e-6) {
                int leader=game.Players[0].Distance>game.Players[1].Distance?0:1;
                if(lastLeader>=0&&lastLeader!=leader)result.LeaderChanges++;
                lastLeader=leader;
            }
            bool nowFinished=game.Players.Any(p=>p.FinishTime.HasValue);
            string beforeProjection=guard%100==0?Fingerprint(game):null;
            foreach(var c in result.Cameras) {
                var view=SharedFieldView.Create(c.Width,132.5,game);
                double x0=Project(c,view,game,game.Players[0].Distance,out double cameraLeft),x1=Project(c,view,game,game.Players[1].Distance,out _);
                c.MaxGap=Math.Max(c.MaxGap,view.Gap);c.MaxLeaderFraction=Math.Max(c.MaxLeaderFraction,Math.Max(x0,x1)/c.Width);
                c.MinPlayerX=Math.Min(c.MinPlayerX,Math.Min(x0,x1));
                c.MaxCameraLeft=Math.Max(c.MaxCameraLeft,cameraLeft);c.LastCameraLeft=cameraLeft;
                Near(Math.Min(x0,x1),view.AnchorX,"Actual lagger anchored including after first finish");
                Check(view.IsPlayerVisible(game.Players[0].Distance<=game.Players[1].Distance?0:1),"Actual lagger visible");
                if(nowFinished&&!firstFinished) {c.FirstFinishCameraLeft=cameraLeft;c.FinishScreenX[0]=x0;c.FinishScreenX[1]=x1;}
                if(game.Phase=="racing"&&game.Players.Count(p=>p.FinishTime.HasValue)==1) {
                    c.ContinuingLaggerSamples++;
                    int finished=game.Players[0].FinishTime.HasValue?0:1;
                    if(!view.IsPlayerVisible(finished))c.FinishedLeaderOffscreen=true;
                }
                for(int p=0;p<2;p++) {
                    double x=p==0?x0:x1,old=c.PreviousX[p];bool off=x>c.Width;
                    if(off!=c.Open[p]) {
                        double crossing=previousTime+(game.Elapsed-previousTime)*(old-c.Width)/(old-x);
                        if(off)c.Offscreen[p].Add(new Interval{Start=crossing});
                        else {var interval=c.Offscreen[p][c.Offscreen[p].Count-1];interval.End=crossing;interval.Returned=true;}
                        c.Open[p]=off;
                    }
                    c.PreviousX[p]=x;
                }
            }
            if(beforeProjection!=null)Check(beforeProjection==Fingerprint(game),"Actual projection preserves complete Core fingerprint");
            firstFinished=nowFinished;previousTime=game.Elapsed;
        }
        foreach(var c in result.Cameras)for(int p=0;p<2;p++)if(c.Open[p])c.Offscreen[p][c.Offscreen[p].Count-1].End=game.Elapsed;
        result.Elapsed=game.Elapsed;result.Winner=Convert.ToString(game.Winner);
        result.Finish=game.Players.Select(p=>p.FinishTime).ToArray();result.Hits=game.Players.Select(p=>p.Collisions).ToArray();
        result.Ordinary=game.Players.Select(p=>p.BoostsEarned).ToArray();result.Final=game.Players.Select(p=>p.FinishBoostsEarned).ToArray();
        result.Distance=game.Players.Select(p=>p.Distance).ToArray();
        foreach(var p in game.Players)Check(p.Distance<=7920,"Actual distance finish clamp");
        if(clean) {Check(result.Hits.All(x=>x==0),"Clean actual routes");Check(result.Finish.All(x=>x.HasValue),"Both true finish within grace");}
        if(result.Finish.Any(x=>!x.HasValue)) Near(result.Elapsed,result.Finish.Where(x=>x.HasValue).Min().Value+RaceConfig.FinishGrace,"Exact grace stops unfinished lagger",1e-7);
        return result;
    }
    static void Main(string[] args)
    {
        captureCollision=false;CameraContracts();var runs=new List<Duel>();
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}) {
            string name=flight.FirstAction==FirstAction.Jump?"chosen":"fast";
            var safe=TracksMixed.SafeRoute(flight);var full=TracksMixed.FullRoute(flight);
            runs.Add(Run(flight,name,new[]{full,safe},"full/safe",true));
            runs.Add(Run(flight,name,new[]{safe,full},"safe/full",true));
            var large=Run(flight,name,new[]{full,Array.Empty<double>()},"full/no-action",false);
            Check(large.Hits[0]==0&&large.Hits[1]==8,"Large gap from eight actual collisions");runs.Add(large);
            var swapped=Run(flight,name,new[]{Array.Empty<double>(),full},"no-action/full",false);
            Check(swapped.Hits[0]==8&&swapped.Hits[1]==0,"Swapped large gap from eight actual collisions");runs.Add(swapped);
            var missed=full.Skip(name=="chosen"?1:2).ToArray();
            var comeback=Run(flight,name,new[]{missed,safe},"miss first small then full/safe",false);
            Check(comeback.Hits[0]==1&&comeback.Hits[1]==0,"Exactly one real collision");
            Check(comeback.LeaderChanges==1,"Real leader change after one hit and later rewards");
            // The accepted 4.9.1 chevron shape is optional. These historical
            // route inputs miss it in both presets; eight ordinary rewards and
            // one real collision remain the intended comeback witness.
            Check(comeback.Ordinary[0]==8,"Comeback preserves eight personal ordinary rewards");
            Check(comeback.Final[0]==0,"Historical comeback route opts out of optional 4.9.1 finish target");
            Near(comeback.Finish[0].Value,36 - 8 * RaceConfig.BoostTimeGain + RaceConfig.RecoveryDuration,
                "Eight ordinary rewards plus one collision without optional finish dash");runs.Add(comeback);
            if(name=="fast") {
                var deferred=(double[])safe.Clone();deferred[11]=6500;
                runs.Add(Run(flight,name,new[]{deferred,deferred},"safe delayed setup6080→6500",true));
            }
        }
        string output=args.Length>0?args[0]:"Verification/camera481-check.json";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var largeCameras=runs.Where(r=>r.Routes=="full/no-action"||r.Routes=="no-action/full").SelectMany(r=>r.Cameras).ToList();
        Check(largeCameras.Any(c=>c.Offscreen.Any(xs=>xs.Count>0)),"Actual large gap exits right");
        Check(largeCameras.Any(c=>c.Offscreen.Any(xs=>xs.Any(x=>x.Returned))),"Actual large-gap leader returns from RIGHT");
        Check(largeCameras.Any(c=>c.FinishedLeaderOffscreen&&c.ContinuingLaggerSamples>0),"Finished offscreen leader with second still racing");
        File.WriteAllText(output,JsonSerializer.Serialize(new{Checks=checks,SampleStep,Widths,
            Method="Synthetic projection fixtures labelled separately; actual games use production Press/Advance, unchanged routes and physics. Offscreen intervals mean leader centre RIGHT of field. Complete Core fingerprints checked every100 samples and all synthetic fixtures.",
            Runs=runs},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        foreach(var run in runs) {
            Console.WriteLine(run.Profile+" "+run.Routes+" finish="+string.Join(",",run.Finish)+" hits="+string.Join(",",run.Hits)+" finalX="+string.Join(",",run.Distance));
            foreach(var c in run.Cameras)Console.WriteLine(" "+c.Model+" W"+c.Width+" gap="+c.MaxGap.ToString("F3")+" leader="+c.MaxLeaderFraction.ToString("F4")+" minX="+c.MinPlayerX.ToString("F2")+" off="+string.Join(";",c.Offscreen.Select((list,p)=>p+":"+string.Join(",",list.Select(x=>x.Start.ToString("F4")+".."+x.End.ToString("F4")+(x.Returned?" return":" still out"))))));
        }
        Console.WriteLine("PASS SharedCameraCheck "+checks+" checks / "+runs.Count+" actual games; centre crossings interpolated between <=5 ms samples.");
    }
}
