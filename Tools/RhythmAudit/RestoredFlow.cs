using System;
using System.Collections.Generic;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static void FlowTo(GameState g,double x,double step,bool stopOnHit=false)
    {
        int guard=0;
        while(g.Phase=="racing"&&x-g.Players[0].Distance>1e-7)
        {
            // A distance target reached during collision freeze must wait the
            // remaining recovery, not take millions of nanosecond slices.
            RaceSimulation.Advance(g,Math.Min(step,Math.Max(1e-8,g.Players[0].Recovering+(x-g.Players[0].Distance)/440)));
            if(stopOnHit&&g.Players[0].Collisions>0)return;
            if(++guard>200000)throw new Exception(context+": Flow distance scheduler guard target="+x+" current="+g.Players[0].Distance);
        }
    }
    static double[] FlowRoute(FlightSettings f,int kind)
    {return kind==0?TracksFull.SafeRoute(TracksFull.FlowId,f):kind==1?TracksFull.FullRoute(TracksFull.FlowId,f):TracksFull.NegativeRoute(TracksFull.FlowId,f);}
    static Result FlowPlay(FlightSettings f,int kind,double step,int changed=-1,double shift=0,double[] clocks=null)
    {
        var track=TracksFull.Get(TracksFull.FlowId);var route=FlowRoute(f,kind);var r=new Result{Game=Start(track,f)};
        for(int i=0;i<route.Length;i++)
        {
            if(i==changed)Until(r.Game,clocks[i]+shift,step);
            else FlowTo(r.Game,route[i],step);
            r.Times.Add(r.Game.Elapsed);
            foreach(var p in r.Game.Players){if(RaceSimulation.Press(r.Game,p.Id))r.Accepted++;else r.Rejected++;}
        }
        FlowTo(r.Game,7876-1e-5,step);
        foreach(var p in r.Game.Players)
            Need(p.ActiveEffect==null&&p.PendingEffects.Count==0&&p.SuccessfulRiskIds.Count+p.FailedRiskIds.Count+p.NegativeRiskIds.Count==p.CollectedRiskIds.Count,"ordinary effects and linked pending absent before finish target");
        Finish(r.Game,step);
        double expected=kind==0?36:kind==1?33.2:33.5;
        foreach(var p in r.Game.Players)
        {
            Need(p.Collisions==0&&p.FinishTime.HasValue,"clean actual finish");Near(p.Distance,7920,"physical finish position");Near(p.FinishTime.Value,expected,"physical finish clock");
            Need(p.BoostsEarned==(kind==0?0:9)&&p.CompletedBoosts==p.BoostsEarned,"nine complete ordinary rewards or safe none");
            Need(p.SlowsEarned==(kind==2?1:0)&&p.CompletedSlows==p.SlowsEarned,"trap ledger");
            Need(p.FinishBoostsEarned==(kind==0?0:1)&&!p.FinishBoost,"finish boost distinct and complete");
            Need(p.ActiveEffect==null&&p.PendingEffects.Count==0,"empty final effects");
        }
        Need(r.Rejected==0&&r.Accepted==route.Length*2,"all witness actions accepted");Near(r.Game.Elapsed,expected,"actual shared clock");
        return r;
    }
    static void FlowTail()
    {
        var track=TracksFull.Get(TracksFull.FlowId);var releases=new List<double>();double finish=0;
        foreach(var z in track.RiskZones)
        {
            if(z.IsFinish){finish=z.X;continue;}
            double x=z.IsLanding?z.X+z.LandingWidth/2:z.X+z.Radius+Radius;
            if(!z.IsLanding&&!z.IsInstant&&!z.IsNegative&&!z.FreeReward)
            {
                double clear=z.ClearX;
                foreach(var o in track.Obstacles)if(o.Id==z.ObstacleId)clear=Math.Max(clear,ObstacleGeometry.Right(o)+Radius+1e-7);
                x=Math.Max(x,clear);
            }
            releases.Add(x+1e-6);
        }
        releases.Sort();double end=0;
        foreach(double release in releases)end=Math.Max(end,release)+330*.6;
        Need(end<finish,"conservative all-pulse workload exhausted before finish target");
        foreach(var o in track.Obstacles)Need(ObstacleGeometry.Right(o)+Radius<finish,"no obstacle in final strip");
        Near(finish,7876,"unchanged exact finish target");Near(track.Length-finish,44,"clean final44");
        Console.WriteLine("Flow conservative ordinary-tail bound="+end.ToString("R")+" margin="+(finish-end).ToString("R")+"; every optional pulse charged198world, all subsets bounded by all-pulses case, collision freeze advances neither X nor service.");
    }
    static void RestoredFlow()
    {
        foreach(var f in Profiles)for(int kind=0;kind<3;kind++)
        {
            Result baseline=null;
            foreach(double step in Steps)
            {context=Name(f)+" restored Flow kind="+kind+" dt="+step;baseline=FlowPlay(f,kind,step);}
            if(kind<2)
            {
                var clocks=baseline.Times.ToArray();double reserve=kind==0?.06:.025;
                for(int i=0;i<clocks.Length;i++)foreach(int sign in new[]{-1,1})
                {context=Name(f)+" restored Flow kind="+kind+" input="+i+" shift="+(sign*reserve);FlowPlay(f,kind,1d/144,i,sign*reserve,clocks);}
            }
        }
        FlowTail();
        Console.WriteLine("PASS independently scheduled restored Flow: "+checks+" assertions / "+games+" games. Author distance arrays used as witnesses, independent real-speed scheduler and actual finish/ledger checks; six dt, each safe60ms/full25ms, conservative union-bound clean finish. Not independent route discovery.");
    }
    static void FlowRecovery()
    {
        var track=TracksFull.Get(TracksFull.FlowId);int cases=0,failed=0;
        foreach(var f in Profiles)for(int kind=0;kind<2;kind++)
        {
            context=Name(f)+" Flow recovery baseline="+kind;
            var baseline=FlowPlay(f,kind,1d/144);var route=FlowRoute(f,kind);int local=0;
            for(int changed=0;changed<route.Length;changed++)
            {
                bool affected=false;
                foreach(double x in new[]{300d,1550,6400})if(Math.Abs(route[changed]-x)<300)affected=true;
                if(!affected)continue;
                foreach(double shift in new[]{-.64,-.4,-.16,.16,.4,.64})
                {
                    context=Name(f)+" Flow recovery kind="+kind+" input="+changed+" shift="+shift;
                    var g=Start(track,f);bool invalid=false;
                    for(int i=0;i<route.Length&&g.Players[0].Collisions==0;i++)
                    {
                        if(i==changed)
                        {
                            double at=baseline.Times[i]+shift;
                            if(at<g.Elapsed-1e-8){invalid=true;break;}
                            UntilHit(g,at);
                        }
                        else FlowTo(g,route[i],.002,true);
                        if(g.Players[0].Collisions>0)break;
                        TapBoth(g);
                    }
                    if(invalid)continue;
                    if(g.Players[0].Collisions==0)UntilHit(g,100);
                    var hit=g.Players[0].LastHit;
                    if(hit==null||!(hit.Id=="full-flow-1-passage"||hit.Id=="full-flow-4-passage"||hit.Id=="full-flow-13-passage"))continue;
                    cases++;local++;string witness;
                    if(!Escape(g,out witness)){failed++;Console.WriteLine("NO_FOUND_ESCAPE "+context+" actual="+hit.Id+" x="+g.Players[0].Distance);}
                    else if(local<=3)Console.WriteLine(context+" hit="+hit.Id+" "+witness);
                }
            }
            Console.WriteLine(Name(f)+" Flow kind="+kind+" restored-gate actual-hit states="+local);
        }
        Need(cases>0,"nonempty restored gate impact sample");Need(failed==0,"finite search failed cases="+failed);
        Console.WriteLine("PASS targeted restored Flow recovery: "+cases+" actual first-hit states at the three restored gates, remaining recovery+150ms, explicit next unionRight+R+1. Author nominal witnesses; independent perturbations and actual legal branch search, not exhaustive error coverage.");
    }
}
