using System.Text.Json;
using FlipRace.Core;

static partial class Program
{
    static int checks;
    static void Require(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static object P(object row,string key)=>row.GetType().GetProperty(key)?.GetValue(row);
    static double V(object row,string key)=>Convert.ToDouble(P(row,key));
    static object Signal(Result r,string id)=>r.Events.First(e=>(string)P(e,"id")==id&&(string)P(e,"phase")=="Warning");
    static object Edge(Result r,double requested)=>r.Inputs.First(e=>Math.Abs(V(e,"requested")-requested)<1e-6);
    static double[] Change(double[] tape,double original,double replacement)=>tape.Select(x=>Math.Abs(x-original)<1e-6?replacement:x).ToArray();
    static void Save(string name,object data)=>File.WriteAllText("Verification/v541-rethink-tracks-"+name+".json",JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    static object Measure(Track track,double[] route,string name,string hidden,double entry,double action,double start,double end,bool pair=false,bool noReturn=false,double? wrongTap=null,bool requireReward=false,bool requireSupport=false)
    {
        var entries=new List<object>();
        foreach(double jitter in new[]{-11d,0,11}){
            var template=Change(route,entry,entry+jitter);var baseline=Run(track,template);Require(baseline.Clean,name+" nominal jitter");
            var points=new List<object>();var accepted=new List<bool>();object first=null,last=null;Result firstWitness=null,lastWitness=null;
            for(double correction=start;correction<=end;correction+=5){
                var tape=Change(template,action,correction);if(pair)tape=Change(tape,action+44,correction+44);
                var result=Run(track,tape);var input=Edge(result,correction);
                bool usable=result.Clean&&(!requireReward||result.Earned==1&&result.StoneLandings>0)&&(!requireSupport||(string)P(input,"support")!="None"&&!(bool)P(input,"Flipping"));
                accepted.Add(usable);
                points.Add(new{x=correction,result.Clean,usable,result.Hits,result.Rejected,result.Earned,time=V(input,"time"),y=V(input,"y"),vy=V(input,"vy"),speed=V(input,"speed"),support=P(input,"support"),airborne=P(input,"Flipping")});
                if(usable){if(first==null){first=input;firstWitness=result;}last=input;lastWitness=result;}
            }
            Require(first!=null&&last!=null,name+" success exists");
            int firstAccepted=accepted.IndexOf(true),lastAccepted=accepted.LastIndexOf(true);
            Require(accepted.Skip(firstAccepted).Take(lastAccepted-firstAccepted+1).All(x=>x),name+" sampled successful window is contiguous");
            double window=V(last,"time")-V(first,"time"),lastAfter=V(last,"time")-V(Signal(lastWitness,hidden),"revealedAt");
            Require(window>=.15-1e-8,name+" successful window >=150ms");
            if(!noReturn)Require(lastAfter>=.35-1e-8,name+" last save >=350ms after actual reveal");
            Result wrong=null;
            if(wrongTap.HasValue){
                var wrongTape=new List<double>(template){wrongTap.Value};wrongTape.Sort();wrong=Run(track,wrongTape.ToArray());
                Require(wrong.HitIds.Contains(hidden),name+" wrong no-return tap hits hidden");
                Require(V(Edge(wrong,wrongTap.Value),"time")-V(Signal(wrong,hidden),"revealedAt")>=.35-1e-8,name+" wrong decision after350ms");
            }
            entries.Add(new{entryJitterMs=jitter/220*1000,signal=Signal(baseline,hidden),firstSuccessfulInput=first,lastSuccessfulInput=last,windowSeconds=window,lastInputAfterSignal=lastAfter,
                meaning=noReturn?"Subsequent jump after abstaining; NOT hidden-reaction window":"Post-reveal correction window",points,firstWitness,lastWitness,wrong});
            Console.WriteLine($"{name} entry={jitter/220*1000:+0;-0;0}ms: X{V(first,"x")}..{V(last,"x")}, window={window:F3}s lastAfterSignal={lastAfter:F3}s");
        }
        return new{name,entries};
    }
    static void FullProof()
    {
        var track=TracksRethink.Get();var safe=Run(track,TracksRethink.SafeRoute());var air=Run(track,TracksRethink.AirRoute());var pad=Run(track,TracksRethink.LandingRoute());var finish=Run(track,TracksRethink.FinishRoute());var bonus=Run(track,TracksRethink.BonusRoute());
        Require(safe.Clean&&safe.Earned==0&&safe.FinishEarned==0&&Math.Abs(safe.Finish.Value-36)<1e-6,"Full safe36 and optional rewards");
        foreach(var r in new[]{air,pad,bonus})Require(r.Clean&&r.Earned==1&&r.Completed==1&&Math.Abs(r.Gain-66)<1e-6&&Math.Abs(safe.Finish.Value-r.Finish.Value-.3)<1e-6,"Full real reward .3s");
        Require(pad.StoneLandings>0,"Real marked-stone landing");
        Require(finish.Clean&&finish.FinishEarned==1&&Math.Abs(finish.FinishGain-55)<1e-6&&Math.Abs(safe.Finish.Value-finish.Finish.Value-.25)<1e-6,"Optional real finish .25s");
        Require(track.Obstacles.Where(o=>o.Kind=="gate").All(o=>o.GapTop>0&&o.GapBottom<240),"Both gate parts");
        var signal=Signal(bonus,"rethink-m3-price");var touch=bonus.Events.Single(e=>(string)P(e,"id")=="rethink-m3-price-bonus"&&(string)P(e,"phase")=="Reward");
        Require(V(signal,"revealedAt")>V(touch,"at"),"Price signal after actual bonus touch");
        Require(V(Edge(bonus,2920),"speed")==330&&V(Edge(bonus,2920),"effectRemaining")>0,"Price jump during active x1.5 boost");
        double delay=V(Edge(safe,2935.7735849056603),"time")-V(Signal(safe,"rethink-m3-price"),"revealedAt");
        var unchangedDelay=Run(track,TracksRethink.BonusRoute(),new(){[2920]=V(signal,"revealedAt")+delay});
        Require(unchangedDelay.HitIds.Contains("rethink-m3-price"),"Same delay after signal fails with bonus");
        foreach(double jitter in new[]{-11d,0,11})Require(Run(track,Change(TracksRethink.SafeRoute(),2684,2684+jitter)).Clean,"Price no-reward entry +/-50ms");
        Save("full",new{checks,safe,air,pad,finish,bonus,priceContrast=new{sameDelayAfterSignal=delay,actualBonusTouch=V(touch,"at"),actualReveal=V(signal,"revealedAt"),earlierResponseDelay=V(Edge(bonus,2920),"time")-V(signal,"revealedAt"),unchangedDelay}});
        Console.WriteLine($"FULL safe36.00; air/pad/bonus35.70; finish35.75; price same-delay {delay:F3}s hits, earlier {V(Edge(bonus,2920),"time")-V(signal,"revealedAt"):F3}s clean.");
    }
    static void NegativeProof()
    {
        var track=TracksRethink.Get();var idle=Run(track,Array.Empty<double>());double apex=40d/220+350d/662.5;
        var center=new List<double>{40d/220,apex};for(double t=apex+.2;t<36;t+=.4)center.Add(t);
        var repeatingCenter=Run(track,center.ToArray(),allTimes:true);var oneHit=Run(track,TracksRethink.SafeRoute().Where(x=>x!=2935.7735849056603).ToArray());
        Require(idle.Hits>0&&!idle.Clean,"Idle fails");Require(repeatingCenter.Hits>0&&!repeatingCenter.Clean,"Simple repeated center oscillation fails");Require(oneHit.Hits==1&&oneHit.Finish.HasValue,"One actual hit can finish");
        Require(TracksRethink.IsSupported(new(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.5,0,350,265,112)),"Equivalent exact profile supported");
        foreach(var f in new FlightSettings[]{null,FlightPresets.FastFlip,new(FirstAction.Flip,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.5,0,350,265,112),new(FirstAction.Jump,MotionLaw.Linear,AirRepeatLimit.Unlimited,662.5,0,350,265,112),new(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.One,662.5,0,350,265,112),new(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,663,0,350,265,112),new(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.5,1,350,265,112),new(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.5,0,351,265,112),new(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.5,0,350,266,112),new(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.5,0,350,265,113),new(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.Unlimited,662.5,0,350,265,112,true)})Require(!TracksRethink.IsSupported(f),"Non-exact profile rejected");
        Save("negative",new{checks,idle,repeatingCenter,oneHit});Console.WriteLine($"NEGATIVE idle={idle.Hits} center={repeatingCenter.Hits}; one-hit finish={oneHit.Finish:F2}.");
    }
    static void WindowProof()
    {
        var module=TracksRethink.ModuleOne();var tape=TracksRethink.ModuleOneRoute();
        Save("module1",Measure(module,tape,"M1 no-return subsequent jump","rethink-m1-no-return",360,900,620,1000,pair:true,noReturn:true,wrongTap:530));
        var three=TracksRethink.FirstThree();var route=TracksRethink.FirstThreeRoute();
        var m2=Measure(three,route,"M2 cancellation","rethink-m2-cancel-landing",1460,1600,1520,1740);
        var noResponse=Run(three,route.Where(x=>x!=1600).ToArray());Require(noResponse.HitIds.Contains("rethink-m2-cancel-landing"),"Ignoring cancellation hits hidden");Save("three",new{m2,noResponse});
        var track=TracksRethink.Get();var rows=new List<object>{
            Measure(track,TracksRethink.SafeRoute(),"M4 no-return subsequent jump","rethink-m4-no-return",4320,4860,4580,4960,pair:true,noReturn:true,wrongTap:4490),
            Measure(track,TracksRethink.SafeRoute(),"M5 cancellation","rethink-m5-cancel-landing",5420,5560,5480,5700),
            Measure(track,TracksRethink.LandingRoute(),"M6 post-landing supported jump","rethink-m6-again",6695.1811187353192,7200,7100,7300,requireReward:true,requireSupport:true),
            // Settled-ground price approach; these previous-input jitters lead
            // to the same signal pose, not an airborne robustness envelope.
            Measure(track,TracksRethink.BonusRoute(),"M3 price boosted ground approach","rethink-m3-price",2004,2920,2820,3020)};
        Save("windows",new{checks,rows});
    }
    static void Main(string[] args)
    {
        if(!args.Contains("--windows")){FullProof();NegativeProof();}
        if(!args.Contains("--quick"))WindowProof();
        Save("summary",new{checks,passed=true,scope=args.Length==0?"full":string.Join(' ',args),limitations="Finite production-physics witnesses; no human habit/fun proof, whole-course simultaneous jitter or global route-impossibility proof."});
        Console.WriteLine($"PASS RETHINK {checks} assertions.");
    }
}
