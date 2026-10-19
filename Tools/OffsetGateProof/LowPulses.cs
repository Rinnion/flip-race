using System.Text.Json;
using FlipRace.Core;

internal static class LowGatePulseProof
{
    static int checks,runs;
    static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
    static void Need(bool ok,string m){checks++;if(!ok)throw new Exception(m);}
    static void Advance(GameState g,double time,double step){while(time>1e-12){double dt=Math.Min(step,time);RaceSimulation.Advance(g,dt);time-=dt;}}
    static double T(double d)=>(Math.Sqrt(1225+5400*d)-35)/2700;
    static (bool clean,bool earned,double time,double eventX) Route(FlightSettings f,int origin,bool negative,double launch,double step=100,double? clock=null,double shift=0){
        runs++;int initial=f==FlightPresets.FastFlip?1-origin:origin;
        double y=negative&&f==FlightPresets.Chosen?(origin==1?170:70):(initial==1?226:14);
        var track=new Track(22000,new[]{LowGateFamilyProof.Shape(1000,origin)}){
            RiskZones=new[]{new RiskZone{Id="pulse",Kind=negative?"negative":"instant",X=negative?954:808,Y=y}}};
        var g=RaceSimulation.CreateTrial(track,f);RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);RaceSimulation.Advance(g,3);
        if(initial==0){RaceSimulation.Press(g,0);if(f==FlightPresets.FastFlip)Advance(g,T(212)+1e-8,step);else{
            Advance(g,350/662.5,step);RaceSimulation.Press(g,0);Advance(g,Math.Sqrt(2*(212-122500/1325d)/662.5)+1e-8,step);}}
        var p=g.Players[0];
        if(clock.HasValue)Advance(g,clock.Value+shift-g.Elapsed,step);
        else while(launch-p.Distance>1e-7)RaceSimulation.Advance(g,Math.Min(step,Math.Max(1e-9,(launch-p.Distance)/440)));
        double at=g.Elapsed;bool supported=p.Support==SupportKind.Wall;bool accepted=RaceSimulation.Press(g,0);Advance(g,5,step);
        bool earned=p.BoostsEarned+p.SlowsEarned==1;
        var evt=negative?p.LastSlow:p.LastInstant;
        return(supported&&accepted&&p.Collisions==0&&p.Support==SupportKind.Wall&&p.SupportSide==origin,earned,at,evt==null?double.NaN:220*evt.At);
    }
    public static void Run(string[] args){checks=runs=0;var rows=new List<object>();
        foreach(var f in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(bool negative in new[]{false,true}){
            double chosen=double.NaN;double eventX=0;
            for(double x=780;x<=1030;x+=2){var nominal=Route(f,1,negative,x);if(!nominal.clean||!nominal.earned)continue;
                if(!Route(f,1,negative,x,clock:nominal.time,shift:-.06).clean||!Route(f,1,negative,x,clock:nominal.time,shift:.06).clean)continue;
                chosen=x;eventX=nominal.eventX;break;}
            Need(!double.IsNaN(chosen),"real low pulse window "+f.FirstAction+" "+negative);
            foreach(int origin in new[]{0,1})foreach(double step in Steps){var n=Route(f,origin,negative,chosen,step);Need(n.clean&&n.earned,"nominal real pulse in mirrored family");
                foreach(double shift in new[]{-.06,.06})Need(Route(f,origin,negative,chosen,step,n.time,shift).clean,"real low pulse shift");}
            rows.Add(new{profile=f.FirstAction.ToString(),negative,launchX=chosen,individualGateWindowMs=60,eventX,endX=eventX+(negative?66:198)});
        }
        string json=JsonSerializer.Serialize(new{checks,runs,rows},new JsonSerializerOptions{WriteIndented=true});
        string path=args.FirstOrDefault(a=>a.EndsWith(".json",StringComparison.OrdinalIgnoreCase));if(path!=null)File.WriteAllText(path,json);
        Console.WriteLine(json);Console.WriteLine($"PASS real low-gate pulses: {checks} assertions / {runs} games. Optional ring capture not promised under shifted inputs.");
    }
}
