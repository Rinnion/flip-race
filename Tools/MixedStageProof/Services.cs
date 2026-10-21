using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static double FastT(double d) {return (Math.Sqrt(1225+5400*d)-35)/2700;}
    static double[] ServiceInputs(string id,FlightSettings f,double speed)
    {
        bool mirror=id.EndsWith(TracksMixedStage.MirrorSuffix,StringComparison.Ordinal),reordered=id.StartsWith(TracksMixedStage.ReorderedId,StringComparison.Ordinal),fast=f==FlightPresets.FastFlip;
        double large=reordered?2200:1300,low=reordered?1300:2200;var a=new List<double>();
        if(mirror){a.Add(0);if(!fast)a.Add(speed*.2);}
        if(fast){a.Add(600-speed*.7);a.Add(700-speed*FastT(164));}
        else if(speed==110){a.Add(600-speed*.7);a.Add(a[a.Count-1]+speed*.2);a.Add(750);a.Add(750+speed*.2);}
        else a.Add(632-speed*350/662.5);
        Action big=()=>{a.Add(large-speed*.65);if(!fast)a.Add(a[a.Count-1]+speed*.2);};
        Action gate=()=>{if(fast){a.Add(low-speed*.8);a.Add(low+48-speed*(FastT(196)+FastT(114))/2);}else a.Add(low+8-speed*350/662.5);};
        if(reordered){gate();big();}
        else{big();if(fast)a.Add(low+48-speed*(FastT(196)+FastT(114))/2);else{a.Add(large+350);a.Add(large+350+speed*.2);gate();}}
        return a.ToArray();
    }
    static void Services()
    {
        var rows=new List<object>();
        foreach(var source in TracksMixedStage.All)foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(double speed in new[]{110d,220d,330d})
        {
            // Explicit isolation fixture: only the constant horizontal service speed
            // is supplied. Vertical trajectories and support still use real Press.
            var t=TracksMixedStage.Get(source.Id);t.RiskZones=Array.Empty<RiskZone>();
            var inputs=ServiceInputs(t.Id,f,speed);var baseline=Run(t,f,inputs,heldSpeed:speed);
            Check(Good(baseline,0),$"service nominal {t.Id}/{f.FirstAction}/{speed}: hits={baseline.Game.Players[0].Collisions}");
            foreach(double step in Steps){var r=Run(t,f,inputs,step,heldSpeed:speed);Check(Good(r,0),"held-speed service six dt");
                if(f==FlightPresets.FastFlip)Check(r.Inputs.All(s=>s.Support==SupportKind.Wall),"slow-speed fallback uses real supported flips, never a restored-air-state shortcut");}
            for(int i=0;i<inputs.Length;i++)foreach(double sign in new[]{-1d,1d}) {
                if(inputs[i]==0&&sign<0)continue;
                var r=Run(t,f,inputs,.317,i,.06*sign,baseline,speed);
                Check(Good(r,0),$"service window {t.Id}/{f.FirstAction}/{speed}/{i}/{sign} hits={r.Game.Players[0].Collisions}");
            }
            rows.Add(new{id=t.Id,profile=f==FlightPresets.Chosen?"chosen":"fast-flip",speed,inputs,samples=baseline.Inputs,marks=baseline.Marks});
            Console.WriteLine($"PASS held isolation {t.Id}/{f.FirstAction}/{speed}, mandatory individual ±60ms");
        }
        File.WriteAllText("Verification/mixed-stage-speed-services.json",JsonSerializer.Serialize(new{scope="held horizontal-speed isolation; real vertical inputs/support; not actual earned effects or timing accounting",checks,runs,rows},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        Console.WriteLine($"PASS speed services: {checks} / {runs} games.");
    }
    static void Pulses()
    {
        var rows=new List<object>();
        foreach(bool mirror in new[]{false,true})foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(string scenario in new[]{"boost-before-low","boost-before-large","slow-before-large"}) {
            string id=TracksMixedStage.ReorderedId+(mirror?TracksMixedStage.MirrorSuffix:"");
            var t=TracksMixedStage.Get(id);bool negative=scenario=="slow-before-large";
            double x=scenario=="boost-before-low"?(f==FlightPresets.Chosen?1140:1180):negative?2070:2000;
            double y=scenario=="boost-before-low"&&f==FlightPresets.Chosen?120:mirror?14:226;
            t.RiskZones=t.RiskZones.Concat(new[]{new RiskZone{Id="actual-service-prefix",Kind=negative?"negative":"instant",X=x,Y=y,Radius=8}}).ToArray();
            var inputs=TracksMixedStage.QuickReturnRoute(id,f);var r=Run(t,f,inputs);
            var p=r.Game.Players[0];
            Console.WriteLine($"PULSE {scenario}/{f.FirstAction}/{mirror} accepted={r.Accepted} hit={p.Collisions} plus={p.BoostsEarned} minus={p.SlowsEarned} ids={string.Join(',',p.HitObstacleIds)} speeds={string.Join(',',r.SpeedAtInput)}");
            Check(r.Accepted&&p.Collisions==0&&(negative?p.NegativeRiskIds:p.SuccessfulRiskIds).Contains("actual-service-prefix"),"real pulse clean nominal");
            foreach(double step in Steps) {
                var run=Run(t,f,inputs,step);var end=run.Game.Players[0];
                Check(run.Accepted&&run.Game.Players.All(q=>q.Collisions==0&&(negative?q.NegativeRiskIds:q.SuccessfulRiskIds).Contains("actual-service-prefix")),"actual pulse six dt and both players");
                var landing=run.Marks.Single(m=>m.Name=="low-return").State;
                Check(landing.Support==SupportKind.Wall&&landing.SupportSide==(mirror?0:1)&&landing.AirFlipAvailable,"real same-wall low landing recharges before large departure");
                int largeIndex=run.Inputs.Count-(f==FlightPresets.FastFlip?1:2);
                Check(run.Inputs[largeIndex].Support==SupportKind.Wall&&run.Inputs[largeIndex].AirFlipAvailable,"Fast/Main large launch starts on actual recovered support");
                if(scenario!="boost-before-low")Near(run.SpeedAtInput[largeIndex],negative?110:330,"actual earned pulse active at large departure");
                else Near(run.Marks.Single(m=>m.Name=="low-first-front").Speed,330,"real boost is active at actual low threshold front");
                Near(end.FinishTime.Value,t.Length/220-.3*end.BoostsEarned+.3*end.SlowsEarned,"only fully served actual pulse accounting");
            }
            rows.Add(new{id,profile=f==FlightPresets.Chosen?"chosen":"fast-flip",scenario,prefixZone=new{x,y,kind=negative?"negative":"instant"},inputs,samples=r.Inputs,speeds=r.SpeedAtInput,marks=r.Marks,
                finish=p.FinishTime,positive=p.BoostsEarned,negative=p.SlowsEarned});
        }
        File.WriteAllText("Verification/mixed-stage-real-pulses.json",JsonSerializer.Serialize(new{scope="separate same-obstacle reordered fixtures with one additional genuinely collected .6s ring; no injected speed/effect/player state",checks,runs,rows},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        Console.WriteLine($"PASS real pulses: {checks} / {runs} games.");
    }
}
