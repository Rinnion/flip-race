using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static readonly double[] Steps = { 1d/30, 1d/60, 1d/144, .317, 2.5, 100 };
    static int runs;
    sealed class Result {
        public GameState Game; public bool Accepted=true;
        public List<InputSample> Inputs=new List<InputSample>();
        public List<Mark> Marks=new List<Mark>();
        public List<bool> ChargedAfter=new List<bool>();
        public List<double> SpeedAtInput=new List<double>();
    }
    sealed class Mark { public string Name; public InputSample State; public double Speed; }
    static InputSample Sample(GameState game) { var p=game.Players[0];return new InputSample{X=p.Distance,Time=game.Elapsed,CenterY=RaceSimulation.GetPlayerCenterY(p),VerticalSpeed=p.VerticalSpeed,
        Support=p.Support,SupportSide=p.SupportSide,SupportObstacleId=p.SupportObstacleId,Airborne=p.Flipping,AirFlipAvailable=p.AirFlipAvailable}; }
    static Result Run(Track track,FlightSettings flight,double[] inputs,double step=.317,int changed=-1,double shift=0,Result nominal=null,double heldSpeed=220)
    {
        runs++;var r=new Result{Game=RaceSimulation.CreateTrial(track,flight)};Tap(r.Game);RaceSimulation.Advance(r.Game,3);
        if(heldSpeed!=220)foreach(var p in r.Game.Players)p.ActiveEffect=new SpeedEffect{RingId="held-speed-isolation-only",IsNegative=heldSpeed==110,Remaining=100,PulseCount=200};
        var marks=new List<(string Name,double X)>{("small-pad-plane",632),("small-return",900),("large-opposite",track.Obstacles.Single(o=>o.Id=="mixed-large").X+100),
            ("low-first-front",track.Obstacles.Single(o=>o.Id=="mixed-low").X-14),("low-return",track.Obstacles.Single(o=>o.Id=="mixed-low").X+220)}.OrderBy(m=>m.X).ToList();int markIndex=0;
        Action<double> move=x=>{while(markIndex<marks.Count&&marks[markIndex].X<x-1e-7){var m=marks[markIndex++];if(m.X<r.Game.Players[0].Distance-1e-7)continue;MoveTo(r.Game,m.X,step);r.Marks.Add(new Mark{Name=m.Name,State=Sample(r.Game),Speed=r.Game.Players[0].CurrentSpeed});}MoveTo(r.Game,x,step);};
        for(int i=0;i<inputs.Length;i++) {
            if(i==changed)AdvanceTime(r.Game,nominal.Inputs[i].Time+shift-r.Game.Elapsed,step); else move(inputs[i]);
            var p=r.Game.Players[0];r.Inputs.Add(Sample(r.Game));r.SpeedAtInput.Add(p.CurrentSpeed);double vy=p.VerticalSpeed;bool airborne=p.Flipping;
            r.Accepted&=RaceSimulation.Press(r.Game,0)&&RaceSimulation.Press(r.Game,1);
            if(airborne)Near(p.VerticalSpeed,vy,"air repeat preserves velocity");r.ChargedAfter.Add(p.AirFlipAvailable);
        }
        while(markIndex<marks.Count){var m=marks[markIndex++];if(m.X<r.Game.Players[0].Distance-1e-7)continue;MoveTo(r.Game,m.X,step);r.Marks.Add(new Mark{Name=m.Name,State=Sample(r.Game),Speed=r.Game.Players[0].CurrentSpeed});}
        Finish(r.Game,step,heldSpeed==220);return r;
    }
    static bool Good(Result r,int rewards) { return r.Accepted&&r.Game.Players.All(p=>p.Collisions==0&&p.BoostsEarned==rewards&&p.SlowsEarned==0); }
    static double[] Route(string variant,string id,FlightSettings f) { return variant=="safe"?TracksMixedStage.SafeRoute(id,f):variant=="full"?TracksMixedStage.FullRoute(id,f):TracksMixedStage.QuickReturnRoute(id,f); }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;captureCollision=false;
        try {
            if(args.Contains("--services")){Services();return;}
            if(args.Contains("--pulses")){Pulses();return;}
            if(args.Contains("--straight-search")){StraightSearch();return;}
            if(args.Contains("--straight-chosen")){StraightChosen();return;}
            if(args.Contains("--straight")){StraightVerify();return;}
            if(args.Contains("--negative-search")){NegativeSearch();return;}
            if(args.Contains("--negative-proof")){int index=Array.IndexOf(args,"--source");NegativeProof(index<0?"Verification/mixed-course-windows-r1.json":args[index+1]);return;}
            if(args.Contains("--course-recovery")){CourseRecovery();return;}
            if(args.Contains("--course-native")){CourseNative();return;}
            var output=new List<object>();
            var routeResults=new Dictionary<string,Result>();
            foreach(var t in TracksMixedStage.All)foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(string variant in new[]{"safe","full","quickreturn"}) {
                var inputs=Route(variant,t.Id,f);var r=Run(t,f,inputs);var p=r.Game.Players[0];
                Console.WriteLine($"{t.Id}/{f.FirstAction}/{variant} good={Good(r,variant=="full"?2:0)} hits={p.Collisions} earned={p.BoostsEarned} time={p.FinishTime:R} IDs={string.Join(',',p.HitObstacleIds)} inputs={string.Join(',',inputs)}");
                if(args.Contains("--verify")) {
                    Check(Good(r,variant=="full"?2:0),"nominal "+t.Id+"/"+f.FirstAction+"/"+variant);
                    Semantics(r,t,f,variant);
                    foreach(double step in Steps){var tested=Run(t,f,inputs,step);Check(Good(tested,variant=="full"?2:0),"six dt clean");Semantics(tested,t,f,variant);}
                    double window=variant=="safe"?.06:.025;
                    for(int i=0;i<inputs.Length;i++)foreach(double sign in new[]{-1d,1d}) {
                        if(inputs[i]==0&&sign<0)continue;
                        var shifted=Run(t,f,inputs,.317,i,sign*window,r);
                        Check(Good(shifted,variant=="full"?2:0),$"window {t.Id}/{f.FirstAction}/{variant} index{i}/{sign} hits={shifted.Game.Players[0].Collisions} rewards={shifted.Game.Players[0].BoostsEarned}");
                    }
                }
                output.Add(new{id=t.Id,profile=f==FlightPresets.Chosen?"chosen":"fast-flip",preset=f==FlightPresets.Chosen?10:11,settings=f,variant,inputs,samples=r.Inputs,marks=r.Marks,hits=p.Collisions,rewards=p.BoostsEarned,finish=p.FinishTime});
                routeResults.Add(t.Id+"/"+f.FirstAction+"/"+variant,r);
            }
            File.WriteAllText("Verification/mixed-stage-draft.json",JsonSerializer.Serialize(output,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            var browser=new List<object>();
            foreach(var t in TracksMixedStage.All)foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}) {
                Func<string,Result> result=variant=>routeResults[t.Id+"/"+f.FirstAction+"/"+variant];
                browser.Add(new{id=t.Id,name=t.Name,profile=f==FlightPresets.Chosen?"chosen":"fast-flip",preset=f==FlightPresets.Chosen?10:11,settings=f,length=t.Length,
                    safe=Route("safe",t.Id,f),full=Route("full",t.Id,f),quickreturn=Route("quickreturn",t.Id,f),
                    times=new{safe=result("safe").Inputs.Select(i=>i.Time),full=result("full").Inputs.Select(i=>i.Time),quickreturn=result("quickreturn").Inputs.Select(i=>i.Time)},
                    finish=new{safe=result("safe").Game.Players[0].FinishTime,full=result("full").Game.Players[0].FinishTime,quickreturn=result("quickreturn").Game.Players[0].FinishTime},
                    obstacles=t.Obstacles,zones=t.RiskZones});
            }
            File.WriteAllText("Verification/mixed-stage-routes.json",JsonSerializer.Serialize(browser,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            Console.WriteLine($"{(args.Contains("--verify")?"PASS stage":"Nominal export")}: {checks} checks / {runs} actual games.");
        } catch(Exception e) { Console.Error.WriteLine(e);Environment.ExitCode=1; }
    }
    static void Semantics(Result r,Track t,FlightSettings f,string variant)
    {
        int origin=t.Id.EndsWith(TracksMixedStage.MirrorSuffix,StringComparison.Ordinal)?0:1;
        int setup=origin==0?(f==FlightPresets.FastFlip?1:2):0;
        var small=r.Marks.Single(m=>m.Name=="small-return").State;
        Check(small.Support==SupportKind.Wall&&small.SupportSide==origin,"small returns to real origin wall before next manoeuvre");
        Check(small.AirFlipAvailable,"origin landing recharges repeat");
        var large=r.Marks.Single(m=>m.Name=="large-opposite").State;
        Check(large.Support==SupportKind.Wall&&large.SupportSide!=origin,"large requires and achieves opposite wall");
        var low=r.Marks.Single(m=>m.Name=="low-return").State;
        Check(low.Support==SupportKind.Wall&&low.SupportSide==origin&&low.AirFlipAvailable,"low full union and real original-wall recharge");
        if(variant=="full") {
            var pad=r.Marks.Single(m=>m.Name=="small-pad-plane").State;
            Check(pad.Support==SupportKind.Stone&&pad.SupportObstacleId=="mixed-small","full route lands on exact narrow physical small face");
            Check(r.Game.Players.All(p=>p.SuccessfulRiskIds.Contains("mixed-small-pad")&&p.SuccessfulRiskIds.Contains("mixed-large-before")),"full independent exact reward IDs");
        } else if(f==FlightPresets.FastFlip) {
            Check(r.Inputs[setup].Support==SupportKind.Wall&&r.Inputs[setup].SupportSide==origin,"Fast small departure is real wall");
            Check(r.Inputs[setup+1].Support==SupportKind.None&&r.Inputs[setup+1].AirFlipAvailable&&!r.ChargedAfter[setup+1],"Fast small uses exactly one actual airborne return");
        } else Check(r.Inputs[setup].Support==SupportKind.Wall&&r.Inputs[setup+1].X>900,"Main small uses one natural jump without a repeat");
        Near(r.Game.Players[0].FinishTime.Value,t.Length/220-(variant=="full"?.6:0),"actual stage finish budget");
        Check(Equals(r.Game.Winner,"tie"),"two halves exact tie");
    }
}
