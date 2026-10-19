using System;
using System.Collections.Generic;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static List<PlannedInput> FamilyPlan(Track track,FlightSettings f,bool quick=false)
    {
        var compact=new List<Obstacle>();Obstacle straight=null,low=null;
        foreach(var o in track.Obstacles)
        {
            if(o.TopOffsetX==o.BottomOffsetX)straight=o;
            else if(o.GapTop==110&&o.GapBottom==130)compact.Add(o);
            else low=o;
        }
        Need(compact.Count==8&&straight!=null&&low!=null,"known stage2 family inventory");
        var plan=Oracle(new Track(track.Length,compact.ToArray()),f);
        Action<double,bool,int> add=(x,air,side)=>plan.Add(new PlannedInput{At=x/Speed,X=x,Air=air,OriginSide=side,Gate=plan.Count<18?8:9});
        bool fast=f.FirstAction==FirstAction.Flip;
        double centre=straight.X+straight.Width/2;
        // Independent physical passage interval at the current floor wall.
        double straightX=fast?centre-Speed*.5*(RiseTime(12,f)+RiseTime(200,f)):centre-Speed*f.JumpImpulse/f.GravityAcceleration;
        add(straightX,false,1);
        if(!quick)
        {
            double lowX=fast?low.X+48-Speed*.5*(RiseTime(196,f)+RiseTime(114,f))
                :low.X+low.Width/2-Speed*f.JumpImpulse/f.GravityAcceleration;
            add(lowX,false,fast?0:1);
        }
        else if(fast)
        {
            add(low.X-280,false,0); // Genuine opposite-to-origin wall preparation.
            const double q=.17;
            double d=f.FlipImpulse*q+.5*f.GravityAcceleration*q*q,v=f.FlipImpulse+f.GravityAcceleration*q;
            double tIn=RiseTime(16,f);
            double tOut=q+(v+Math.Sqrt(v*v+2*f.GravityAcceleration*(d-16)))/f.GravityAcceleration;
            double launch=(low.X+low.Width+Radius-Speed*tOut+low.X-Radius-Speed*tIn)/2;
            add(launch,false,1);add(launch+Speed*q,true,1);
        }
        else
        {
            // Separate optional Main witness; independently executed and
            // perturbation-tested, not claimed as an independently discovered
            // optimum. Two corrections retain the unlimited Main contract.
            add(low.X-242,false,1);add(low.X-198,true,1);add(low.X-176,true,1);
        }
        return plan;
    }
    static void FamilyStage()
    {
        var track=TracksRhythm.Practice();
        foreach(var f in Profiles)foreach(bool quick in new[]{false,true})
        {
            var plan=FamilyPlan(track,f,quick);int coreCount=f.FirstAction==FirstAction.Flip?8:16;
            Need(plan.Count==(quick?coreCount+4:coreCount+2),"compact and family accepted-action count");
            foreach(double step in Steps)
            {
                context=Name(f)+" stage2 quick="+quick+" dt="+step;
                var r=Play(track,f,plan,step,nominal:true);Clean(r,track,plan.Count);
                Need(r.Game.Players[0].SupportSide==1,"final low ends at same floor for quick / accessible finish");
                Console.WriteLine(context+" accepted="+plan.Count+" finish="+r.Game.Elapsed.ToString("R"));
            }
            for(int i=0;i<plan.Count;i++)foreach(int sign in new[]{-1,1})
            {
                double reserve=quick&&i>=coreCount+1?(f.FirstAction==FirstAction.Flip?.04:.03):.06;
                context=Name(f)+" stage2 quick="+quick+" input="+i+" shift="+(sign*reserve);
                Clean(Play(track,f,plan,1d/144,i,sign*reserve),track,plan.Count);
            }
            if(!quick)for(int i=0;i<plan.Count;i++)
            {
                context=Name(f)+" stage2 removal="+i;
                var r=Play(track,f,plan,.317,omit:i);
                foreach(var p in r.Game.Players)Need(p.Collisions>0,"required input deletion causes actual collision");
            }
        }
        Console.WriteLine("PASS independent stage2 family: "+checks+" assertions / "+games+" games. Own compact .18s/Main oracle, own accessible straight/low schedule; optional Fast .17s same-wall return, separate optional Main correction witness. Six dt, individual mandatory60ms / optional Fast40ms Main30ms, accepted actions and required-input removal. No player state injection.");
    }
    static void FamilyRecovery()
    {
        var track=TracksRhythm.Practice();
        Recovery(track,(t,f)=>FamilyPlan(t,f));
    }
}
