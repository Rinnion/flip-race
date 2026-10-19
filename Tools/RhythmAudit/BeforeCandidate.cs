using System;
using System.Collections.Generic;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static CourseWitness BeforeFixture(ModuleSpec geometry,ModuleSpec schedule,FlightSettings f,bool full,bool mirror)
    {
        var obstacles=new List<Obstacle>();var zones=new List<RiskZone>();
        AppendModule(geometry,500,mirror,"fixture/",obstacles,zones);
        var inputs=new List<double>();
        if(mirror){inputs.Add(50);if(f.FirstAction==FirstAction.Jump)inputs.Add(50+220*MainRepeatDelay);}
        foreach(double x in GetModuleRoute(schedule,f,full).Inputs)inputs.Add(500+x);
        return new CourseWitness{Track=new Track(500+geometry.Length+100,obstacles.ToArray()){Id="before400-fixture",RiskZones=zones.ToArray()},Flight=f,Purpose=full?"full":"safe",Inputs=inputs.ToArray()};
    }
    static double[] FixtureClocks(CourseWitness w)
    {
        var g=Start(w.Track,w.Flight);var clocks=new List<double>();
        foreach(double x in w.Inputs){FlowTo(g,x,.002);clocks.Add(g.Elapsed);Need(TapBoth(g),"fixture nominal input accepted");}
        Finish(g,.317);Need(g.Players[0].Collisions==0,"fixture baseline clean");return clocks.ToArray();
    }
    static void BeforeCandidateRecovery()
    {
        ModuleSpec old=null,next=null;
        foreach(var m in ReadModules("rhythm-modules-before.json"))if(m.Kind=="before")old=m;
        foreach(var m in ReadModules("rhythm-modules-before400.json"))if(m.Kind=="before")next=m;
        Need(old!=null&&next!=null,"both before snapshots present");
        int samples=0,failed=0,historical=0;
        foreach(var f in Profiles)foreach(bool full in new[]{false,true})foreach(bool mirror in new[]{false,true})foreach(bool oldSchedule in new[]{true,false})
        {
            ModuleSpec schedule=oldSchedule?old:next;
            var clocks=FixtureClocks(BeforeFixture(schedule,schedule,f,full,mirror));
            var candidate=BeforeFixture(next,schedule,f,full,mirror);
            int skip=mirror?(f.FirstAction==FirstAction.Jump?2:1):0;
            for(int changed=skip;changed<candidate.Inputs.Length;changed++)foreach(double shift in new[]{-.96,-.8,-.64,-.4,-.24,-.16,-.08,.08,.16,.24,.4,.64,.8,.96})
            {
                var hit=CourseFirstHit(candidate,clocks,changed,shift);
                if(hit==null||hit.Players[0].LastHit.Id!="fixture/stone")continue;
                context=Name(f)+" full="+full+" mirror="+mirror+" schedule="+(oldSchedule?"330":"400")+" changed="+(changed-skip)+" shift="+shift;
                samples++;string escape;bool rescued=Escape(hit,out escape);if(!rescued)failed++;
                if(oldSchedule&&!full&&f==FlightPresets.FastFlip&&changed==skip+1&&shift==-.64)
                {
                    historical++;var r=Clone(hit);Advance(r,r.Players[0].Recovering+.15,.001);
                    Console.WriteLine("SAME HISTORICAL LATE HIT "+context+" hitLocalX="+(hit.Players[0].Distance-500).ToString("R")+" reactionLocalX="+(r.Players[0].Distance-500).ToString("R")+" escaped="+rescued+" "+escape);
                }
                if(!rescued)Console.WriteLine("NO_FOUND_ESCAPE "+context+" hitX="+hit.Players[0].Distance.ToString("R")+" Y="+RaceSimulation.GetPlayerCenterY(hit.Players[0]).ToString("R"));
            }
        }
        Need(historical==2,"same old late collision replayed on both physical sides");
        Console.WriteLine((failed==0?"PASS":"REVIEW REQUIRED")+" isolated before400 recovery: "+checks+" assertions / "+games+" actual games / "+samples+" first-stone-hit states; no-found-escape="+failed+". Legal controls after remaining penalty+150ms must clear the entire next unionRight+R+1. Includes old330 schedules and new400 schedules; not full-course acceptance.");
    }
}
