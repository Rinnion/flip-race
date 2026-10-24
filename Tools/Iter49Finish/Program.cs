using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;

// ITER49-00 only: actual Press/Advance route witnesses and isolated contact/effect
// fixtures. No state injection in gameplay witnesses; no native pixels claimed.
internal static partial class Program
{
    static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
    static readonly List<object> Evidence=new List<object>();
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
    static GameState Begin(Track track,FlightSettings flight=null)
    {
        var game=RaceSimulation.CreateTrial(track,flight??FlightPresets.Chosen);
        Tap(game);RaceSimulation.Advance(game,RaceConfig.CountdownDuration);
        Check(game.Phase=="racing"&&game.Elapsed==0,"Actual readiness/countdown");
        return game;
    }
    static RiskZone Pad(double x=200,double y=226,double width=28,double height=8,string id="pad")
    { return new RiskZone{Id=id,Kind="finish",X=x,Y=y,FinishWidth=width,FinishHeight=height}; }
    static Track Isolated(RiskZone[] zones,Obstacle[] obstacles=null)
    { return new Track(1000,obstacles??Array.Empty<Obstacle>()){RiskZones=zones}; }
    static string Snapshot(object value) { return JsonSerializer.Serialize(value,Json); }
    static string Hash(string path) { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
    static RouteResult Replay(Track track,FlightSettings flight,double[] route,double step)
    {
        var r=new RouteResult{Game=Begin(track,flight)};
        foreach(double x in route){MoveTo(r.Game,x,step);Tap(r.Game,r);}
        Finish(r.Game,step);CaptureReward(r);return r;
    }
    static double[] MixedRoute(FlightSettings flight,bool take)
    {
        // The old last input aimed at the removed X7876 vertical plane. Keep
        // production author arrays unchanged at gate00; explicitly omit it here.
        var route=TracksMixed.FullRoute(flight).Where(x=>x<7800).ToList();
        if(!take) {
            if(flight.FirstAction==FirstAction.Jump)route[route.Count-1]+=10;
            else route.Add(7730); // One legal airborne repeat avoids the pad.
        }
        return route.ToArray();
    }
    static void MixedProof()
    {
        var original=TracksMixed.Get();
        string originalObstacles=Snapshot(original.Obstacles);
        var pad=original.RiskZones.Single(z=>z.IsFinish);
        Near(original.Length,7920,"Mixed length preserved");
        Check(pad.X-pad.FinishWidth/2>7730,"Visible pad starts after expanded upper gate");
        Check(pad.X+pad.FinishWidth/2+RaceConfig.PlayerRadius<=original.Length-110,"Latest circular body contact leaves full pulse room");
        Near(pad.X+pad.FinishWidth/2+RaceConfig.PlayerRadius,7808,"Two world units conservative clearance spare");
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        foreach(double step in Steps) {
            var take=Replay(TracksMixed.Get(),flight,MixedRoute(flight,true),step);
            var skip=Replay(TracksMixed.Get(),flight,MixedRoute(flight,false),step);
            var counterfactual=TracksMixed.Get();
            counterfactual.RiskZones=counterfactual.RiskZones.Where(z=>!z.IsFinish).ToArray();
            var sameInputWithoutPad=Replay(counterfactual,flight,MixedRoute(flight,true),step);
            foreach(var p in take.Game.Players) {
                Check(p.Collisions==0&&p.BoostsEarned==9&&p.SlowsEarned==0,"Actual clean full take preserves nine ordinary rewards");
                Check(p.FinishBoostsEarned==1&&!p.FinishBoost,"Exactly one completed finish pulse per player");
                Near(p.FinishBoostElapsed,.25,"Full moving timer at tape");
                Near(p.FinishBoostDistanceGained,55,"Actual extra motion, no timestamp subtraction");
                Near(p.FinishTime.Value,33.05,"Actual take finish");
            }
            foreach(var p in skip.Game.Players) {
                Check(p.Collisions==0&&p.BoostsEarned==9&&p.SlowsEarned==0,"Actual clean skip preserves nine ordinary rewards");
                Check(p.FinishBoostsEarned==0,"Optional finish pickup skipped");
                Near(p.FinishTime.Value,33.3,"Actual skip finish");
            }
            Near(skip.Game.Players[0].FinishTime.Value-take.Game.Players[0].FinishTime.Value,.25,"Clean real get/skip advantage");
            Near(sameInputWithoutPad.Game.Players[0].FinishTime.Value-take.Game.Players[0].FinishTime.Value,.25,"Identical input isolated pad gain");
            Check(Snapshot(TracksMixed.Get().Obstacles)==originalObstacles,"No obstacle/union changes");
            var hit=take.Rewards.Single(h=>h.Id==pad.Id);
            // Replay the real inputs to just before activation to record actual
            // circular contact coordinates, without constructing a player state.
            var contact=Begin(TracksMixed.Get(),flight);
            foreach(var input in take.Inputs.Where(i=>i.Time<hit.At-1e-8)) {
                AdvanceTime(contact,input.Time-contact.Elapsed,step);Tap(contact);
            }
            AdvanceTime(contact,hit.At-contact.Elapsed,step);
            var cp=contact.Players[0];double cx=cp.Distance,cy=RaceSimulation.GetPlayerCenterY(cp);
            double dx=Math.Max(0,Math.Abs(cx-pad.X)-pad.FinishWidth/2);
            double dy=Math.Max(0,Math.Abs(cy-pad.Y)-pad.FinishHeight/2);
            Near(dx*dx+dy*dy,196,"Real body touches exact visible rectangle",2e-5);
            Check(7920-cx>=110,"Actual take leaves full pulse runway");
            Evidence.Add(new{Kind="actual-mixed",Profile=flight.FirstAction.ToString(),Step=step,
                Pad=pad,Contact=new{X=cx,Y=cy,Time=hit.At,Remaining=7920-cx},
                TakeFinish=take.Game.Players[0].FinishTime,SkipFinish=skip.Game.Players[0].FinishTime,
                TakeInputs=take.Inputs,SkipInputs=skip.Inputs});
        }
    }
    static void ContactProof()
    {
        foreach(double step in Steps)
        foreach(double gap in new[]{0d,8,14,14.01}) {
            // Actual grounded body path y226, rectangle bottom 226-gap.
            var pad=Pad(200,222-gap);
            var game=Begin(Isolated(new[]{pad}));
            if(gap<=14) {
                double expected=186-Math.Sqrt(196-gap*gap);
                MoveTo(game,expected-.001,step);
                Check(game.Players.All(p=>p.FinishBoostsEarned==0),"No invisible expanded rectangle corner contact");
                MoveTo(game,expected,step);
                Check(game.Players.All(p=>p.FinishBoostsEarned==1),"Inclusive exact circular face/corner/tangent contact");
                Near(game.Players[0].Distance,expected,"Swept activation X",1e-7);
                Near(game.Players[0].FinishBoostElapsed,0,"No consumed timer at contact",1e-7);
                Finish(game,step);
                Near(game.Players[0].FinishTime.Value,1000d/220-.25,"Contact fixture complete gain");
            } else {
                Finish(game,step);
                Check(game.Players.All(p=>p.FinishBoostsEarned==0),"Body path outside visible rectangle plus true radius misses");
                Near(game.Players[0].FinishTime.Value,1000d/220,"No phantom gain");
            }
            Evidence.Add(new{Kind="actual-grounded-contact",Step=step,VerticalGap=gap,
                Earned=game.Players[0].FinishBoostsEarned,Finish=game.Players[0].FinishTime});
        }
        // Deliberately invalid late placement on an isolated test track proves
        // tape truncation credits only served motion, never an automatic .25s.
        var partial=Begin(Isolated(new[]{Pad(980)}));
        RaceSimulation.Advance(partial,100);
        double served=48d/440;
        Near(partial.Players[0].FinishBoostElapsed,served,"Finish truncates actual remaining pulse");
        Near(partial.Players[0].FinishBoostDistanceGained,220*served,"Only actual extra motion is credited at tape");
        Near(partial.Players[0].FinishTime.Value,1000d/220-served,"No fake full gain on deliberately short runway");
        Check(!partial.Players[0].FinishBoost,"Tape clears unfinished active pulse");
        // One single Advance spans the whole rectangle, effect expiration and tape.
        var swept=Begin(Isolated(new[]{Pad()}));RaceSimulation.Advance(swept,100);
        Check(swept.Players.All(p=>p.FinishBoostsEarned==1),"Whole pad crossed in one public Advance");
        Near(swept.Players[0].FinishBoostElapsed,.25,"Large-step pulse duration");
    }
    static void TimerAndRecovery()
    {
        foreach(double step in Steps) {
            var game=Begin(Isolated(new[]{Pad()},new[]{new Obstacle{Id="impact-after-pad",Kind="floor",X=230,Width=20,Height=48}}));
            MoveTo(game,172,step);var p=game.Players[0];
            Check(p.FinishBoost&&p.CurrentSpeed==440,"Immediate fixed x2 speed");
            AdvanceTime(game,.1,step);
            Near(p.Distance,216,"Actual 44 moving units before collision");
            Check(p.Collisions==1&&p.Recovering>0,"Actual obstacle collision during pulse");
            Near(p.FinishBoostElapsed,.1,"Timer measured only movement");
            string frozen=Snapshot(game);RaceSimulation.Advance(game,0);
            Check(Snapshot(game)==frozen,"Zero advance/pause call does not consume timer");
            AdvanceTime(game,.49,step);
            Near(p.Distance,216,"Recovery freezes X");
            Near(p.FinishBoostElapsed,.1,"Recovery freezes finish pulse timer");
            AdvanceTime(game,.01,step);
            AdvanceTime(game,.15,step);
            Near(p.Distance,282,"Remaining .15 moving seconds travel66");
            Near(p.FinishBoostElapsed,.25,"Exactly .25 served after recovery");
            Check(!p.FinishBoost&&p.CurrentSpeed==220,"Pulse expires to base speed");
            Finish(game,step);
            Near(p.FinishTime.Value,1000d/220+.5-.25,"Real arrival includes recovery and served finish gain");
        }
    }
    static void Interactions()
    {
        foreach(bool slowFirst in new[]{false,true})
        foreach(double step in Steps) {
            var a=new RiskZone{Id="first",Kind=slowFirst?"negative":"positive",FreeReward=!slowFirst,X=50,Y=226,Radius=8};
            var b=new RiskZone{Id="queued",Kind=slowFirst?"positive":"negative",FreeReward=slowFirst,X=90,Y=226,Radius=8};
            var game=Begin(Isolated(new[]{a,b,Pad(100)}));
            MoveTo(game,72,step);var p=game.Players[0];
            Check(p.FinishBoost&&p.ActiveEffect!=null&&p.PendingEffects.Count==1,"Finish preempts active effect with queue intact");
            var active=p.ActiveEffect;var pending=p.PendingEffects.Peek();
            double remaining=active.Remaining,elapsed=active.Elapsed,queuedRemaining=pending.Remaining;
            AdvanceTime(game,.125,step);
            Near(p.Distance,127,"No multiplied speed during ordinary boost/slow");
            Near(active.Remaining,remaining,"Suspend active remaining timer");
            Near(active.Elapsed,elapsed,"Suspend ordinary elapsed ledger");
            Near(pending.Remaining,queuedRemaining,"Suspend pending queue");
            Check(ReferenceEquals(active,p.ActiveEffect)&&ReferenceEquals(pending,p.PendingEffects.Peek()),"Preserve exact effects and FIFO identities");
            AdvanceTime(game,.125,step);
            Check(!p.FinishBoost&&ReferenceEquals(active,p.ActiveEffect),"Resume same active effect after finish pulse");
            Near(active.Remaining,remaining,"Expiration boundary consumes no ordinary time");
            Near(p.CurrentSpeed,slowFirst?110:330,"Restore original ordinary speed");
            Finish(game,step);
            Check(p.CompletedBoosts==1&&p.CompletedSlows==1,"Both suspended ordinary pulses complete once");
            Near(p.FinishTime.Value,1000d/220-.25,"Opposite ordinary pulses cancel with full finish gain");
        }
        // An instant pickup during the finish pulse retains existing preemption
        // semantics but still cannot multiply speed or consume ordinary timers.
        var overlap=Begin(Isolated(new[]{
            new RiskZone{Id="slow",Kind="negative",X=50,Y=226,Radius=8},
            Pad(100),new RiskZone{Id="instant",Kind="instant",X=130,Y=226,Radius=8}}));
        MoveTo(overlap,72,.317);var op=overlap.Players[0];var suspended=op.ActiveEffect;
        double oldRemaining=suspended.Remaining;
        MoveTo(overlap,108,.317);
        Check(op.FinishBoost&&op.CurrentSpeed==440&&op.InstantBoostsEarned==1,"New instant during pulse never produces3x");
        Check(op.ActiveEffect!=suspended&&op.PendingEffects.Peek()==suspended,"Existing instant priority keeps suspended slow at queue front");
        Near(suspended.Remaining,oldRemaining,"Retain slow remainder during concurrent pickup");
        Finish(overlap,.317);
        Check(op.CompletedBoosts==1&&op.CompletedSlows==1,"Concurrent pickup queue settles normally");
        Near(op.FinishTime.Value,1000d/220-.25,"Concurrent pickup finish ledger matches real travel");
    }
    static void OnceAndRestart()
    {
        var game=Begin(Isolated(new[]{Pad(200),Pad(400,id:"second-pad")}));
        MoveTo(game,172,.317);
        AdvanceTime(game,.01,.317);
        Check(game.Players.All(p=>p.FinishBoostsEarned==1),"Independent simultaneous personal pickup");
        AdvanceTime(game,.01,.317);
        Check(game.Players.All(p=>p.FinishBoostsEarned==1),"Lingering contact does not repeat");
        Finish(game,100);
        Check(game.Players.All(p=>p.FinishBoostsEarned==1),"A second finish zone cannot stack or reaward");
        Tap(game);RaceSimulation.Advance(game,3);
        Check(game.Players.All(p=>p.FinishBoostsEarned==0&&!p.FinishBoost&&p.FinishBoostElapsed==0&&p.LastFinishBoost==null),"Restart clears personal pulse and ledger");
        Finish(game,100);
        Check(game.Players.All(p=>p.FinishBoostsEarned==1),"New race can collect personally again");
    }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;captureCollision=false; if(args.Length>0 && args[0]=="--inventory") { PadInventory(); return; }
        if(args.Length>0 && args[0]=="--branches") { BranchInventory(); return; }
        if(args.Length>0 && args[0]=="--baseline") { SaveBaseline(); return; }
        if(args.Length>0 && args[0]=="--all") { AllTracksProof(); return; }
        var hashes=Directory.GetFiles("Assets/Scripts/Core","*.cs").ToDictionary(p=>p,Hash);
        MixedProof();ContactProof();TimerAndRecovery();Interactions();OnceAndRestart();
        Check(hashes.All(k=>Hash(k.Key)==k.Value),"Core sources unchanged during proof");
        File.WriteAllText("Verification/iter49-00-finish.json",JsonSerializer.Serialize(new{
            Status="PASS",Stage="ITER49-00 Mixed only",Checks=checks,Steps,SourceHashes=hashes,
            Scope="Production Press/Advance real trajectories; pause is caller withholding Advance (zero-step tested). Renderer/native pixels and other four track locations are root gate01 work.",
            Evidence},Json));
        Console.WriteLine("PASS ITER49-00: "+checks+" checks;12 Mixed take/skip/same-input comparisons;24 exact grounded contact fixtures; moving timer/recovery/FIFO/overlap/once/restart.");
    }
}
