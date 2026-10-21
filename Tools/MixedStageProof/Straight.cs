using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    sealed class StraightResult { public GameState Game; public List<InputSample> Inputs=new List<InputSample>(); public bool Accepted=true; public InputSample Cleared; public double[] Speeds; public double SpeedAfterInputs, SpeedAtRear; }
    static StraightResult StraightRun(double top,double bottom,double width,FlightSettings f,double speed,double[] inputs,bool mirror=false,double step=2.5,int changed=-1,double shift=0,StraightResult nominal=null,double pulseX=double.NaN,bool slow=false)
    {
        var gate=new Obstacle{Id="straight",Kind="gate",X=800,Width=width,GapTop=mirror?240-bottom:top,GapBottom=mirror?240-top:bottom};
        var track=new Track(1600,new[]{gate},Array.Empty<Cue>()){Id="mixed-straight-isolated",RiskZones=double.IsNaN(pulseX)?Array.Empty<RiskZone>():new[]{new RiskZone{Id="real-prefix",Kind=slow?"negative":"instant",X=pulseX,Y=mirror?14:226,Radius=8}}};
        var r=new StraightResult{Game=RaceSimulation.CreateTrial(track,f)};Tap(r.Game);RaceSimulation.Advance(r.Game,3);
        bool held=double.IsNaN(pulseX)&&speed!=220;
        if(held)foreach(var p in r.Game.Players)p.ActiveEffect=new SpeedEffect{RingId="held-speed-isolation-only",IsNegative=speed==110,Remaining=100,PulseCount=200};
        if(mirror){Tap(r.Game);if(f==FlightPresets.Chosen){AdvanceTime(r.Game,.2,step);Tap(r.Game);}MoveTo(r.Game,300,step);}
        for(int i=0;i<inputs.Length;i++){
            if(i==changed)AdvanceTime(r.Game,nominal.Inputs[i].Time+shift-r.Game.Elapsed,step);else MoveTo(r.Game,inputs[i],step);
            var before=Sample(r.Game);r.Inputs.Add(before);r.Accepted&=RaceSimulation.Press(r.Game,0)&&RaceSimulation.Press(r.Game,1);
            if(before.Support==SupportKind.None){Near(r.Game.Players[0].VerticalSpeed,before.VerticalSpeed,"straight air repeat retains velocity");if(f==FlightPresets.FastFlip)Check(!r.Game.Players[0].AirFlipAvailable,"straight Fast spends One");}
        }
        r.SpeedAfterInputs=r.Game.Players[0].CurrentSpeed;
        MoveTo(r.Game,823,step);r.SpeedAtRear=r.Game.Players[0].CurrentSpeed;
        MoveTo(r.Game,1100,step);r.Cleared=Sample(r.Game);r.Speeds=new[]{r.Game.Players[0].CurrentSpeed};Finish(r.Game,step,!held);runs++;return r;
    }
    static bool StraightGood(StraightResult r,FlightSettings f,bool mirror=false)
    { return r.Accepted&&r.Game.Players.All(p=>p.Collisions==0)&&r.Inputs[0].Support==SupportKind.Wall&&r.Cleared.Support==SupportKind.Wall&&r.Cleared.SupportSide==((f==FlightPresets.FastFlip&&r.Inputs.Count==1)?(mirror?1:0):(mirror?0:1))
        &&(f!=FlightPresets.FastFlip||r.Inputs.Count==1||r.Inputs.Count==2&&r.Inputs[1].Support==SupportKind.None&&r.Inputs[1].AirFlipAvailable); }
    static void StraightSearch()
    {
        var rows=new List<object>();
        foreach(var shape in new[]{(Top:32d,Bottom:232d,Width:8d),(Top:48d,Bottom:228d,Width:8d),(Top:24d,Bottom:216d,Width:8d)}) {
            foreach(double speed in new[]{110d,220d,330d})foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}){
                double[] selected=null;StraightResult baseline=null;double chosenAge=0,chosenQ=0;
                for(double q=0;q<=(f==FlightPresets.Chosen?0:.301)&&selected==null;q+=.005)
                for(double age=.15;age<=.701&&selected==null;age+=.005){
                    if(q>0&&q<.1)continue;
                    var inputs=f==FlightPresets.Chosen||q==0?new[]{800+shape.Width/2-speed*age}:new[]{800+shape.Width/2-speed*age,800+shape.Width/2-speed*(age-q)};
                    var r=StraightRun(shape.Top,shape.Bottom,shape.Width,f,speed,inputs);if(!StraightGood(r,f))continue;
                    bool good=true;for(int i=0;i<inputs.Length&&good;i++)foreach(double sign in new[]{-1d,1d}){var shifted=StraightRun(shape.Top,shape.Bottom,shape.Width,f,speed,inputs,changed:i,shift:sign*.06,nominal:r);if(!StraightGood(shifted,f)){good=false;break;}}
                    if(good){selected=inputs;baseline=r;chosenAge=age;chosenQ=q;}
                }
                Console.WriteLine($"STRAIGHT {shape.Top}/{shape.Bottom}/{shape.Width} {f.FirstAction} speed{speed} {(selected==null?"NONE":string.Join(',',selected))} age{chosenAge} q{chosenQ}");
                if(selected!=null)rows.Add(new{top=shape.Top,bottom=shape.Bottom,width=shape.Width,profile=f.FirstAction.ToString(),speed,inputs=selected,age=chosenAge,q=chosenQ,samples=baseline.Inputs,cleared=baseline.Cleared});
            }
        }
        File.WriteAllText("Verification/mixed-straight-search.json",JsonSerializer.Serialize(new{scope="tool-only straight geometry search, held horizontal service speeds, individual60ms; not a production track",checks,runs,rows},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine($"Straight exploratory {checks} checks/{runs} games");
    }
    static void StraightChosen()
    {
        const double top=48,bottom=228,width=8;var rows=new List<object>();
        foreach(double speed in new[]{110d,220d,330d})foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}) {
            var nominalInputs=f==FlightPresets.Chosen?new[]{804-speed*350/662.5}:speed==110?new[]{804-speed*(.18+(35+2700*.18)/2700),804-speed*((35+2700*.18)/2700)}:new[]{804-speed*(speed==220?.235:.225),804-speed*(.225-.155)};
            var r=StraightRun(top,bottom,width,f,speed,nominalInputs);var windows=new List<double[]>();
            for(int i=0;i<nominalInputs.Length;i++){var pair=new double[2];for(int dir=0;dir<2;dir++)for(double shift=.002;shift<=.14;shift+=.002){var s=StraightRun(top,bottom,width,f,speed,nominalInputs,changed:i,shift:(dir==0?-shift:shift),nominal:r);if(!StraightGood(s,f))break;pair[dir]=shift;}windows.Add(pair);}
            Console.WriteLine($"CHOSEN {f.FirstAction} {speed} good{StraightGood(r,f)} inputs {string.Join(',',nominalInputs)} windows {JsonSerializer.Serialize(windows)}");
            rows.Add(new{speed,profile=f.FirstAction.ToString(),good=StraightGood(r,f),inputs=nominalInputs,windows,samples=r.Inputs,cleared=r.Cleared});
        }
        File.WriteAllText("Verification/mixed-straight-chosen.json",JsonSerializer.Serialize(new{top,bottom,width,rows},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
    static double[] StraightInputs(FlightSettings f,double speed)
    { return f==FlightPresets.Chosen?new[]{804-speed*350/662.5}:speed==110?new[]{804-speed*(.18+(35+2700*.18)/2700),804-speed*((35+2700*.18)/2700)}:new[]{804-speed*(speed==220?.235:.225),804-speed*.07}; }
    static void StraightVerify()
    {
        const double top=48,bottom=228,width=8;var rows=new List<object>();var pulses=new List<object>();
        foreach(bool mirror in new[]{false,true})foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(double speed in new[]{110d,220d,330d}) {
            var inputs=StraightInputs(f,speed);var baseline=StraightRun(top,bottom,width,f,speed,inputs,mirror);
            Check(StraightGood(baseline,f,mirror),"straight chosen nominal");double window=f==FlightPresets.FastFlip&&speed==110?.025:.06;
            foreach(double step in Steps){var r=StraightRun(top,bottom,width,f,speed,inputs,mirror,step);Check(StraightGood(r,f,mirror),"straight both mirrors six dt");Check(r.Cleared.AirFlipAvailable,"straight origin wall recharges");}
            for(int i=0;i<inputs.Length;i++)foreach(double shift in new[]{-window,window}){var r=StraightRun(top,bottom,width,f,speed,inputs,mirror,changed:i,shift:shift,nominal:baseline);Check(StraightGood(r,f,mirror),"straight individual input tolerance");}
            rows.Add(new{mirror,profile=f==FlightPresets.Chosen?"chosen":"fast-flip",speed,inputs,individualWindow=window,samples=baseline.Inputs,cleared=baseline.Cleared});
        }
        foreach(bool mirror in new[]{false,true})foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(bool slow in new[]{false,true}) {
            double speed=slow?110:330,pulseX=slow?760:634;var inputs=StraightInputs(f,speed);
            var baseline=StraightRun(top,bottom,width,f,speed,inputs,mirror,pulseX:pulseX,slow:slow);
            Console.WriteLine($"STRAIGHT actual {(slow?"slow":"boost")} {f.FirstAction}/{mirror}: good={StraightGood(baseline,f,mirror)} front={baseline.SpeedAfterInputs} rear={baseline.SpeedAtRear} hit={baseline.Game.Players[0].Collisions}");
            Check(StraightGood(baseline,f,mirror),"straight actual transition nominal");
            foreach(double step in Steps){var r=StraightRun(top,bottom,width,f,speed,inputs,mirror,step,pulseX:pulseX,slow:slow);Check(StraightGood(r,f,mirror),"straight real ring transition clean");var p=r.Game.Players[0];Check((slow?p.NegativeRiskIds:p.SuccessfulRiskIds).Contains("real-prefix"),"straight real ring earned");Near(r.SpeedAfterInputs,speed,"straight effect live after required action");Near(r.SpeedAtRear,220,"straight effect ended inside gate");Near(p.FinishTime.Value,1600d/220+(slow?.3:-.3),"straight actual pulse finish");}
            pulses.Add(new{mirror,profile=f==FlightPresets.Chosen?"chosen":"fast-flip",kind=slow?"negative":"instant",pulseX,pulseY=mirror?14:226,inputs,samples=baseline.Inputs,speedAfterInputs=baseline.SpeedAfterInputs,speedAtRear=baseline.SpeedAtRear,cleared=baseline.Cleared,finish=baseline.Game.Players[0].FinishTime});
        }
        File.WriteAllText("Verification/mixed-straight-proof.json",JsonSerializer.Serialize(new{scope="isolated straight48/228 width8; service-held speeds separately from real ring transitions; no Assets change",gateX=800,top,bottom,width,checks,runs,rows,pulses},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine($"PASS straight {checks} checks / {runs} games");
    }
}
