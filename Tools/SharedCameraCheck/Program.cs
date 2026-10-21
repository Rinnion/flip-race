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
        public string Model; public double AnchorFraction=.19, CapFraction=.45;
        public double Width, MaxGap, MaxLeaderFraction, MaxCameraLeft;
        public double MinPlayerX=double.PositiveInfinity;
        public List<Interval>[] Offscreen={new List<Interval>(),new List<Interval>()};
        public double[] FinishScreenX=new double[2];
        public double FirstFinishCameraLeft, LastCameraLeft;
        public bool[] Open=new bool[2];
        public double[] PreviousX=new double[2];
    }
    static double Project(CameraRun c,SharedFieldView view,GameState game,double worldX,out double cameraLeft)
    {
        if(c.Model=="production25/47")cameraLeft=view.CameraLeft;
        else {
            // Explicit counterfactual only. Production scale / geometry remain
            // authoritative; this is not substituted into the actual game.
            double x0=Math.Clamp(game.Players[0].Distance,0,game.Track.Length),x1=Math.Clamp(game.Players[1].Distance,0,game.Track.Length);
            cameraLeft=Math.Min(game.Track.Length-c.AnchorFraction*c.Width/view.ScaleX,
                Math.Max((x0+x1)/2-c.AnchorFraction*c.Width/view.ScaleX,Math.Max(x0,x1)-c.CapFraction*c.Width/view.ScaleX));
        }
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
    static void CameraContracts()
    {
        var g=StartFlight(FlightPresets.Chosen);
        foreach(double width in Widths) {
            // Synthetic positions below exercise only a read-only projection,
            // never serve as a physics / offscreen gameplay witness.
            foreach(double gap in new[]{0.0,200,600,1000,4000}) foreach(int leader in new[]{0,1}) {
                g.Players[leader].Distance=5000;g.Players[1-leader].Distance=5000-gap;
                var half=SharedFieldView.Create(width,132.5,g);var full=SharedFieldView.Create(width,289,g);
                Near(half.ScaleX,full.ScaleX,"Height-independent horizontal scale");
                Near(half.CameraLeft,full.CameraLeft,"Height-independent camera");
                Near(half.PlayerX(0),full.PlayerX(0),"Height-independent player0 X");
                Near(half.PlayerX(1),full.PlayerX(1),"Height-independent player1 X");
                Near(half.PlayerX(leader)-half.PlayerX(1-leader),gap*half.ScaleX,"True physical gap");
                Check(half.PlayerX(leader)<=width*.47+1e-8,"Leader cap47");
                Near(g.Players[leader].Distance,5000,"Projection cannot pull leader");
                Near(g.Players[1-leader].Distance,5000-gap,"Projection cannot pull lagger");
                if(gap==0) Near(half.PlayerX(0),width*.25,"Equal25 anchor");
            }
            g.Players[0].Distance=g.Players[1].Distance=7920;
            var end=SharedFieldView.Create(width,132.5,g);
            Near(end.PlayerX(0),width*.25,"Finish stable anchor");
            Near(end.PlayerX(0),end.PlayerX(1),"Same physical point identical world X");
            var span=width/end.ScaleX;
            foreach(double epsilon in new[]{-.001,.001}) {
                g.Players[0].Distance=5000;g.Players[1].Distance=5000-(.47*span+epsilon);
                var edge=SharedFieldView.Create(width,132.5,g);
                Check(edge.IsPlayerVisible(1)==(epsilon<0),"Current centre visibility threshold47 gap");
                Near(edge.PlayerX(0),width*.47,"Current lagger exit follows cap");
            }
        }
    }
    static Duel Run(FlightSettings flight,string profile,double[][] routes,string label,bool clean)
    {
        var game=StartFlight(flight);var next=new int[2];var result=new Duel{Profile=profile,Routes=label};
        foreach(double width in Widths) {
            result.Cameras.Add(new CameraRun{Width=width,Model="counterfactual19/45"});
            result.Cameras.Add(new CameraRun{Width=width,Model="production25/47",AnchorFraction=.25,CapFraction=.47});
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
            foreach(var c in result.Cameras) {
                var view=SharedFieldView.Create(c.Width,132.5,game);
                double x0=Project(c,view,game,game.Players[0].Distance,out double cameraLeft),x1=Project(c,view,game,game.Players[1].Distance,out _);
                c.MaxGap=Math.Max(c.MaxGap,view.Gap);c.MaxLeaderFraction=Math.Max(c.MaxLeaderFraction,Math.Max(x0,x1)/c.Width);
                c.MinPlayerX=Math.Min(c.MinPlayerX,Math.Min(x0,x1));
                c.MaxCameraLeft=Math.Max(c.MaxCameraLeft,cameraLeft);c.LastCameraLeft=cameraLeft;
                Check(cameraLeft<=game.Track.Length-c.AnchorFraction*c.Width/view.ScaleX+1e-8,"Camera never beyond finish bound");
                if(nowFinished&&!firstFinished) {c.FirstFinishCameraLeft=cameraLeft;c.FinishScreenX[0]=x0;c.FinishScreenX[1]=x1;}
                for(int p=0;p<2;p++) {
                    double x=p==0?x0:x1,old=c.PreviousX[p];bool off=x<0;
                    if(off!=c.Open[p]) {
                        double crossing=previousTime+(game.Elapsed-previousTime)*old/(old-x);
                        if(off)c.Offscreen[p].Add(new Interval{Start=crossing});
                        else {var interval=c.Offscreen[p][c.Offscreen[p].Count-1];interval.End=crossing;interval.Returned=true;}
                        c.Open[p]=off;
                    }
                    c.PreviousX[p]=x;
                }
            }
            firstFinished=nowFinished;previousTime=game.Elapsed;
        }
        foreach(var c in result.Cameras)for(int p=0;p<2;p++)if(c.Open[p])c.Offscreen[p][c.Offscreen[p].Count-1].End=game.Elapsed;
        result.Elapsed=game.Elapsed;result.Winner=Convert.ToString(game.Winner);
        result.Finish=game.Players.Select(p=>p.FinishTime).ToArray();result.Hits=game.Players.Select(p=>p.Collisions).ToArray();
        result.Ordinary=game.Players.Select(p=>p.BoostsEarned).ToArray();result.Final=game.Players.Select(p=>p.FinishBoostsEarned).ToArray();
        result.Distance=game.Players.Select(p=>p.Distance).ToArray();
        foreach(var p in game.Players)Check(p.Distance<=7920,"Actual distance finish clamp");
        if(clean) {Check(result.Hits.All(x=>x==0),"Clean actual routes");Check(result.Finish.All(x=>x.HasValue),"Both true finish within grace");}
        if(clean)foreach(var c in result.Cameras.Where(x=>x.Model=="production25/47")) {
            Check(c.Offscreen.All(x=>x.Count==0),"25/47 keeps all clean centres visible");
            Check(c.MinPlayerX>=14.0,"25/47 keeps body radius14 within left edge at height289");
        }
        if(result.Finish.Any(x=>!x.HasValue)) Near(result.Elapsed,result.Finish.Where(x=>x.HasValue).Min().Value+RaceConfig.FinishGrace,"Exact grace stops unfinished lagger",1e-7);
        return result;
    }
    static object Visibility()
    {
        var rows=new List<object>();var game=StartFlight(FlightPresets.Chosen);
        // These are preparation groups in the published safe witnesses, not
        // alternate physics. Whole obstacle union must be visible before the
        // group's first action, with 20px readable right margin and 150ms reaction.
        // Fast6080 is an unnecessarily early wall setup; a separately replayed
        // actual route defers that action to6500 without changing any geometry.
        var targets=new[]{"small-pad","small-second","large-before","straight","low-floor","middle-offset","small-ceiling","final-offset","low-ceiling"};
        double[][] starts={new[]{515.7735849056604,965.7735849056604,1910,2560,3441.7735849056603,4030.844877314131,6730.773584905661,7240.844877314131,7420},
            new[]{530.0,980,1940,2560,3300,4055.150321356292,6500,7030,7676.917064816087}};
        foreach(double width in Widths)foreach(double cap in new[]{.45,.47}) {
            var view=SharedFieldView.Create(width,132.5,game);
            double ahead=((1-cap)*width-20)/view.ScaleX;
            for(int profile=0;profile<2;profile++)for(int i=0;i<targets.Length;i++) {
                var obstacle=game.Track.Obstacles.Single(x=>x.Id==TracksMixed.Id+"-"+targets[i]);
                double right=ObstacleGeometry.Right(obstacle),lead=right-starts[profile][i];
                double notice=(ahead-lead)/330;
                Check(notice>=.15,"Whole mandatory target preview allows150ms at330");
                rows.Add(new{Width=width,Cap=cap,Profile=profile==0?"chosen":"fast",Target=obstacle.Id,FirstInputX=starts[profile][i],WholeRight=right,PreparationLead=lead,AheadWorld=ahead,SecondsAt330=ahead/330,WholeVisibleBeforeInputSeconds=notice,ReactionSpareSeconds=notice-.15});
            }
        }
        var original=new List<object>();
        foreach(double width in Widths) {
            var view=SharedFieldView.Create(width,132.5,game);
            original.Add(new{Width=width,OriginalFastSetupX=6080,WholeTargetRight=6864,
                PreviewSecondsAt330Cap45=(((.55*width-20)/view.ScaleX)-(6864-6080))/330,
                PreviewSecondsAt330Cap47=(((.53*width-20)/view.ScaleX)-(6864-6080))/330});
        }
        return new{Method="Conservative geometric preview at330, whole physical union,20px readable right margin. Does not claim unchanged X arrays are valid under an artificial held330 effect.",Rows=rows,OriginalUnnecessarilyEarlySetup=original};
    }
    static void Main(string[] args)
    {
        captureCollision=false;CameraContracts();var runs=new List<Duel>();
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}) {
            string name=flight.FirstAction==FirstAction.Jump?"chosen":"fast";
            var safe=TracksMixed.SafeRoute(flight);var full=TracksMixed.FullRoute(flight);
            runs.Add(Run(flight,name,new[]{full,safe},"full/safe",true));
            runs.Add(Run(flight,name,new[]{safe,full},"safe/full",true));
            runs.Add(Run(flight,name,new[]{full,Array.Empty<double>()},"full/no-action",false));
            var missed=full.Skip(name=="chosen"?1:2).ToArray();
            var comeback=Run(flight,name,new[]{missed,safe},"miss first small then full/safe",false);
            Check(comeback.Hits[0]==1&&comeback.Hits[1]==0,"Exactly one real collision");
            Check(comeback.LeaderChanges==1,"Real leader change after one hit and later rewards");
            Near(comeback.Finish[0].Value,34.0,"Lost one reward plus one collision finish");runs.Add(comeback);
            if(name=="fast") {
                var deferred=(double[])safe.Clone();deferred[11]=6500;
                runs.Add(Run(flight,name,new[]{deferred,deferred},"safe delayed setup6080→6500",true));
            }
        }
        string output=args.Length>0?args[0]:"Verification/shared-camera-final.json";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var visibility=Visibility();
        File.WriteAllText(output,JsonSerializer.Serialize(new{Checks=checks,SampleStep,Widths,Visibility=visibility,Runs=runs},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        foreach(var run in runs) {
            Console.WriteLine(run.Profile+" "+run.Routes+" finish="+string.Join(",",run.Finish)+" hits="+string.Join(",",run.Hits)+" finalX="+string.Join(",",run.Distance));
            foreach(var c in run.Cameras)Console.WriteLine(" "+c.Model+" W"+c.Width+" gap="+c.MaxGap.ToString("F3")+" leader="+c.MaxLeaderFraction.ToString("F4")+" minX="+c.MinPlayerX.ToString("F2")+" off="+string.Join(";",c.Offscreen.Select((list,p)=>p+":"+string.Join(",",list.Select(x=>x.Start.ToString("F4")+".."+x.End.ToString("F4")+(x.Returned?" return":" still out"))))));
        }
        Console.WriteLine("PASS SharedCameraCheck "+checks+" checks / "+runs.Count+" actual games; centre crossings interpolated between <=5 ms samples.");
    }
}
