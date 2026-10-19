using System.Globalization;
using System.Text.Json;
using FlipRace.Core;

// Isolated comparison of candidate dimensions. No authored track or Core edits.
internal static class OffsetGateRefinement
{
    static int runs,checks;
    static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
    static void Need(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    static double Fast(double d)=>(Math.Sqrt(1225+5400*d)-35)/2700;
    static double MainRise(double d,double tau=.2){double h=350*tau-.5*662.5*tau*tau,v=350-662.5*tau;
        return d<=h?(350-Math.Sqrt(122500-1325*d))/662.5:tau+(-v+Math.Sqrt(v*v+1325*(d-h)))/662.5;}
    static Obstacle Gate(double x,int side,double offset,double top,double bottom,string id)=>new Obstacle{
        Id=id,Kind="gate",X=x,Width=16,GapTop=top,GapBottom=bottom,
        TopOffsetX=side==1?0:offset,BottomOffsetX=side==1?offset:0};
    static void Advance(GameState g,double time,double step){while(time>1e-12){double dt=Math.Min(step,time);RaceSimulation.Advance(g,dt);time-=dt;}}
    static GameState Prepare(Track track,FlightSettings f,int side,double speed,double step){
        var g=RaceSimulation.CreateTrial(track,f);RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);RaceSimulation.Advance(g,3);
        if(side==0){RaceSimulation.Press(g,0);if(f==FlightPresets.FastFlip)Advance(g,Fast(212)+1e-8,step);
            else{Advance(g,350/662.5,step);RaceSimulation.Press(g,0);Advance(g,Math.Sqrt(2*(212-350*350/1325d)/662.5)+1e-8,step);}}
        if(speed!=220)g.Players[0].ActiveEffect=new SpeedEffect{RingId="held-speed-isolation",IsNegative=speed==110,Remaining=100,PulseCount=200};
        return g;
    }
    static bool Normal(FlightSettings f,int side,double speed,double offset,double top,double bottom,double launch,double a,double b,double step){
        runs++;var g=Prepare(new Track(22000,new[]{Gate(1000,side,offset,top,bottom,"gate")}),f,side,speed,step);
        Advance(g,(launch-g.Players[0].Distance)/speed+a,step);bool ok=RaceSimulation.Press(g,0);
        if(f==FlightPresets.Chosen){Advance(g,.2+b-a,step);ok=RaceSimulation.Press(g,0)&&ok;}
        Advance(g,5,step);return ok&&g.Players[0].Collisions==0;
    }
    static bool Recovery(FlightSettings f,int side,double speed,double offset,double top,double bottom,double pitch,double[] taps,double step){
        runs++;var g=Prepare(new Track(22000,new[]{Gate(1000,side,offset,top,bottom,"hit"),Gate(1000+pitch,1-side,offset,top,bottom,"next")}),f,side,speed,step);
        var p=g.Players[0];double hitX=1000+offset-14;
        Advance(g,(hitX-p.Distance)/speed+.5+.15,step);
        if(p.Collisions!=1||p.Recovering>0||p.Support!=SupportKind.Wall||p.SupportSide!=side)return false;
        double endX=1000+pitch+offset+16+14+1,clearTime=(endX-p.Distance)/speed,last=0;
        foreach(double tap in taps){if(tap>=clearTime)return false;Advance(g,tap-last,step);last=tap;if(!RaceSimulation.Press(g,0))return false;}
        Advance(g,clearTime-last+1e-8,step);
        return p.Collisions==1&&p.Distance>=endX-1e-6;
    }
    static IEnumerable<double[]> Patterns(FlightSettings f){
        if(f==FlightPresets.FastFlip){for(int i=8;i<=120;i++)yield return new[]{0d,i*.01};yield break;}
        // Deliberately finite 3-press rescue catalogue, with >=80ms between presses.
        // A found route is a witness, not an optimum over all unlimited histories.
        for(int a=8;a<=50;a+=2)for(int b=a+8;b<=110;b+=2)yield return new[]{0d,a*.01,b*.01};
    }
    public static void Run(string[] args){
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;runs=checks=0;var rows=new List<object>();
        foreach(var shape in new[]{(offset:192d,top:140d,bottom:100d),(offset:160d,top:120d,bottom:120d),(offset:176d,top:120d,bottom:120d),(offset:96d,top:108d,bottom:132d),(offset:112d,top:110d,bottom:130d)})
        foreach(var f in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(double speed in new[]{110d,220d,330d}){
            double riseFirst=226-shape.top-14,riseSecond=226-shape.bottom+14;
            Func<double,double> t=f==FlightPresets.FastFlip?Fast:d=>MainRise(d);
            double low=1030-speed*t(riseFirst),high=1000+shape.offset-14-speed*t(riseSecond),x=(low+high)/2;
            if(f==FlightPresets.Chosen){double lo=double.NegativeInfinity,hi=double.PositiveInfinity;
                for(int i=-6;i<=6;i++)for(int j=-6;j<=6;j++){double a=i*.01,tau=.2+(j-i)*.01;
                    lo=Math.Max(lo,1030-speed*MainRise(riseFirst,tau)-speed*a);
                    hi=Math.Min(hi,1000+shape.offset-14-speed*MainRise(riseSecond,tau)-speed*a);}
                Need(hi>lo,"joint analytic window");x=(lo+hi)/2;}
            foreach(int side in new[]{0,1})foreach(double step in Steps)foreach(double a in new[]{-.06,0,.06})
            foreach(double b in f==FlightPresets.Chosen?new[]{-.06,0,.06}:new[]{0d})
                Need(Normal(f,side,speed,shape.offset,shape.top,shape.bottom,x,a,b,step),"normal joint jitter");
            double necessaryPitch=shape.offset+.15*speed+speed*t(riseSecond);
            double foundPitch=double.NaN;double[] foundTaps=null;
            foreach(double pitch in Enumerable.Range(0,191).Select(i=>120d+i*2)){
                // True lower bound for Fast; Main can choose a different first air timing.
                if(f==FlightPresets.FastFlip&&pitch<=necessaryPitch)continue;
                foreach(var taps in Patterns(f))if(Recovery(f,1,speed,shape.offset,shape.top,shape.bottom,pitch,taps,100)){
                    foundPitch=pitch;foundTaps=taps;break;}
                if(foundTaps!=null)break;
            }
            if(foundTaps!=null)foreach(int side in new[]{0,1})foreach(double step in Steps)
                Need(Recovery(f,side,speed,shape.offset,shape.top,shape.bottom,foundPitch,foundTaps,step),"actual mirrored two-part recovery witness");
            rows.Add(new{shape.offset,shape.top,shape.bottom,profile=f.FirstAction.ToString(),speed,launchX=x,
                fixedDelayWindowMs=(high-low)/speed*1000,normalJointJitterMs=60,
                firstPartNecessaryPitchAtNominalDelay=necessaryPitch,
                minFoundPitch=double.IsNaN(foundPitch)?(double?)null:foundPitch,rescueTaps=foundTaps,
                recoveryScope="real late wall hit, .5s recovery + .15s reaction, then full next gate; held speed; finite 2-unit pitch/80ms-min-spacing catalogue"});
            Console.WriteLine($"shape={shape.offset}/{shape.top}/{shape.bottom} {f.FirstAction} v={speed} window={(high-low)/speed*1000:F3}ms pitch={foundPitch} taps=[{string.Join(',',foundTaps??Array.Empty<double>())}]");
        }
        var json=JsonSerializer.Serialize(new{checks,runs,rows},new JsonSerializerOptions{WriteIndented=true});
        string path=args.FirstOrDefault(a=>a.EndsWith(".json",StringComparison.OrdinalIgnoreCase));if(path!=null)File.WriteAllText(path,json);
        Console.WriteLine($"PASS refinement comparison: {checks} assertions / {runs} real runs. No production shape was changed.");
    }
}
