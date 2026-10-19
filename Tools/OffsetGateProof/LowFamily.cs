using System.Text.Json;
using FlipRace.Core;

internal static class LowGateFamilyProof
{
    static int runs,checks;
    static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
    static void Need(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static double T(double d)=>(Math.Sqrt(1225+5400*d)-35)/2700;
    static void Advance(GameState g,double time,double step){while(time>1e-12){double dt=Math.Min(step,time);RaceSimulation.Advance(g,dt);time-=dt;}}
    public static Obstacle Shape(double x,int origin,string id="low")=>new Obstacle{Id=id,Kind="gate",X=x,Width=16,
        GapTop=origin==1?114:16,GapBottom=origin==1?224:126,TopOffsetX=origin==1?80:0,BottomOffsetX=origin==1?0:80};
    static GameState Prepare(FlightSettings f,int side,int origin,double speed,double step){
        var g=RaceSimulation.CreateTrial(new Track(22000,new[]{Shape(1000,origin)}),f);
        RaceSimulation.Press(g,0);RaceSimulation.Press(g,1);RaceSimulation.Advance(g,3);
        if(side==0){RaceSimulation.Press(g,0);if(f==FlightPresets.FastFlip)Advance(g,T(212)+1e-8,step);
            else{Advance(g,350/662.5,step);RaceSimulation.Press(g,0);Advance(g,Math.Sqrt(2*(212-122500/1325d)/662.5)+1e-8,step);}}
        var p=g.Players[0];Need(p.Support==SupportKind.Wall&&p.SupportSide==side,"real initial wall");
        if(speed!=220)p.ActiveEffect=new SpeedEffect{RingId="held-isolation",IsNegative=speed==110,Remaining=100,PulseCount=200};return g;
    }
    static bool Route(FlightSettings f,int origin,double speed,double x,double[] after,bool opposite=false,double step=100,
        int shiftIndex=-1,double shift=0,bool requireSame=true){
        runs++;var g=Prepare(f,opposite?1-origin:origin,origin,speed,step);var p=g.Players[0];
        double first=(x-p.Distance)/speed,last=0;Advance(g,first+(shiftIndex==0?shift:0),step);
        bool ok=RaceSimulation.Press(g,0);last=shiftIndex==0?shift:0;
        for(int i=0;i<after.Length;i++){double when=after[i]+(shiftIndex==i+1?shift:0);if(when<last)return false;
            Advance(g,when-last,step);bool airborne=p.Flipping;ok=RaceSimulation.Press(g,0)&&airborne&&ok;last=when;}
        Advance(g,4,step);return ok&&p.Collisions==0&&p.Support==SupportKind.Wall&&(!requireSame||p.SupportSide==origin);
    }
    static double Window(FlightSettings f,double speed,double x,double[] after,bool opposite,int index){
        for(int ms=5;ms<=150;ms+=5)foreach(int sign in new[]{-1,1})
            if(!Route(f,1,speed,x,after,opposite,shiftIndex:index,shift:sign*ms/1000d))return ms-5;
        return 150;
    }
    public static void Run(string[] args){runs=checks=0;var rows=new List<object>();
        foreach(double speed in new[]{110d,220d,330d}){
            double fastLow=1030-speed*T(196),fastHigh=1066-speed*T(114),fastX=(fastLow+fastHigh)/2;
            double mainX=1008-speed*350/662.5;
            foreach(int side in new[]{0,1})foreach(double step in Steps)foreach(double shift in new[]{-.06,0,.06}){
                Need(Route(FlightPresets.FastFlip,side,speed,fastX,Array.Empty<double>(),true,step,0,shift),"accessible one-tap opposite-wall Fast");
                Need(Route(FlightPresets.Chosen,side,speed,mainX,Array.Empty<double>(),false,step,0,shift),"ordinary same-wall Main jump");}
            rows.Add(new{kind="accessible",speed,fastOppositeLaunch=fastX,fastFullWindowMs=(fastHigh-fastLow)/speed*1000,mainJumpLaunch=mainX,verifiedMs=60});

            double bestX=0,bestDelay=0,bestWindow=-1;
            for(int delay=10;delay<=20;delay++)for(double x=880;x<=990;x+=2){double tau=delay*.01;
                if(!Route(FlightPresets.FastFlip,1,speed,x,new[]{tau}))continue;
                double window=Math.Min(Window(FlightPresets.FastFlip,speed,x,new[]{tau},false,0),Window(FlightPresets.FastFlip,speed,x,new[]{tau},false,1));
                if(window>bestWindow){bestX=x;bestDelay=tau;bestWindow=window;}}
            Need(bestWindow>=15,"expert one-airflip same-wall witness with measured local window");
            foreach(int side in new[]{0,1})foreach(double step in Steps)foreach(int index in new[]{0,1})foreach(double sign in new[]{-1d,0,1})
                Need(Route(FlightPresets.FastFlip,side,speed,bestX,new[]{bestDelay},false,step,index,sign*bestWindow/1000),"mirrored expert window");
            rows.Add(new{kind="expert-fast-return",speed,launchX=bestX,airDelay=bestDelay,eachInputWindowAtLeastMs=bestWindow,endsOnOriginalWall=true});

            double mainAirX=double.NaN;
            for(double x=650;x<=1000;x+=2)if(Route(FlightPresets.Chosen,1,speed,x,new[]{.2,.3})){
                bool ok=true;for(int i=0;i<3;i++)foreach(double shift in new[]{-.03,.03})ok&=Route(FlightPresets.Chosen,1,speed,x,new[]{.2,.3},shiftIndex:i,shift:shift);
                if(ok){mainAirX=x;break;}}
            Need(!double.IsNaN(mainAirX),"optional Main air correction same-wall witness");
            foreach(int side in new[]{0,1})foreach(double step in Steps)Need(Route(FlightPresets.Chosen,side,speed,mainAirX,new[]{.2,.3},step:step),"Main corrected mirror");
            rows.Add(new{kind="optional-main-correction",speed,launchX=mainAirX,airTimes=new[]{.2,.3},individualTestedMs=30});
        }
        string json=JsonSerializer.Serialize(new{shape=new{width=16,lowerHeight=16,upperHeight=114,upperOffset=80},checks,runs,rows},new JsonSerializerOptions{WriteIndented=true});
        string path=args.FirstOrDefault(a=>a.EndsWith(".json",StringComparison.OrdinalIgnoreCase));if(path!=null)File.WriteAllText(path,json);
        Console.WriteLine(json);Console.WriteLine($"PASS low-gate family: {checks} assertions / {runs} actual runs. Held-speed geometry isolation; no production track edits.");
    }
}
