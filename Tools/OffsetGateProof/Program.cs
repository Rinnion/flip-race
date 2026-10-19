using System.Globalization;
using System.Text.Json;
using FlipRace.Core;

CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
if(args.Contains("--refine")){OffsetGateRefinement.Run(args);return;}
if(args.Contains("--low-family")){LowGateFamilyProof.Run(args);return;}
if(args.Contains("--low-pulses")){LowGatePulseProof.Run(args);return;}
int checks=0,runs=0;
const double Offset=192,AirDelay=.2;
bool paired=args.Contains("--paired");
double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
double FastTime(double d)=>(Math.Sqrt(1225+5400*d)-35)/2700;
double MainTime(double d,double tau=AirDelay){double h=350*tau-.5*662.5*tau*tau,v=350-662.5*tau;
    return d<=h?(350-Math.Sqrt(350*350-1325*d))/662.5:tau+(-v+Math.Sqrt(v*v+1325*(d-h)))/662.5;}
Track Shape(int side,string effect="held")
{
    var halves=new[]{
        new Obstacle{Id="offset",Kind="gate",X=1000+(side==1?0:Offset),Width=16,GapTop=140,GapBottom=240},
        new Obstacle{Id="offset",Kind="gate",X=1000+(side==1?Offset:0),Width=16,GapTop=0,GapBottom=100}};
    var track=new Track(22000,paired?halves:new[]{new Obstacle{Id="offset",Kind="gate",X=1000,Width=16,
        GapTop=140,GapBottom=100,TopOffsetX=side==1?0:Offset,BottomOffsetX=side==1?Offset:0}});
    double floorY=side==1?226:14;
    if(effect=="boost-end")track.RiskZones=new[]{new RiskZone{Id=effect,Kind="instant",X=920,Y=floorY}};
    if(effect=="slow-end")track.RiskZones=new[]{new RiskZone{Id=effect,Kind="negative",X=1030,Y=floorY}};
    if(effect=="boost-start")track.RiskZones=new[]{new RiskZone{Id=effect,Kind="instant",X=1100,Y=120}};
    if(effect=="slow-start")track.RiskZones=new[]{new RiskZone{Id=effect,Kind="negative",X=1100,Y=120}};
    return track;
}
void Advance(GameState game,double duration,double step){while(duration>1e-12){double dt=Math.Min(duration,step);RaceSimulation.Advance(game,dt);duration-=dt;}}
bool Tap(GameState game){bool a=RaceSimulation.Press(game,0),b=RaceSimulation.Press(game,1);return a&&b;}
GameState Prepare(FlightSettings flight,int side,double speed,string effect,double step)
{
    var game=RaceSimulation.CreateTrial(Shape(side,effect),flight);Need(Tap(game),"ready");RaceSimulation.Advance(game,3);
    if(side==0){Need(Tap(game),"real ceiling setup");if(flight==FlightPresets.FastFlip)Advance(game,FastTime(212)+1e-8,step);
        else{Advance(game,350/662.5,step);Need(Tap(game),"main ceiling setup air");Advance(game,Math.Sqrt(2*(212-350*350/(2*662.5))/662.5)+1e-8,step);}}
    foreach(var p in game.Players){Need(p.Support==SupportKind.Wall&&p.SupportSide==side,"true initial wall");
        if(effect=="held"&&speed!=220)p.ActiveEffect=new SpeedEffect{RingId="isolated-held-speed",IsNegative=speed==110,Remaining=100,PulseCount=200};}
    return game;
}
void MoveTo(GameState game,double x,double step,string effect)
{
    if(effect=="held"){Advance(game,(x-game.Players[0].Distance)/game.Players[0].CurrentSpeed,step);return;}
    int guard=0;while(x-game.Players[0].Distance>1e-7){double gap=x-game.Players[0].Distance;
        RaceSimulation.Advance(game,Math.Min(step,Math.Max(1e-9,gap/440)));if(++guard>100000)throw new Exception("distance guard");}
}
(bool clean,double time,GameState game) Run(FlightSettings flight,int side,double speed,double x,double step=100,string effect="held",
    double? nominalClock=null,double shiftFirst=0,double shiftSecond=0)
{
    runs++;var game=Prepare(flight,side,speed,effect,step);
    if(nominalClock.HasValue)Advance(game,nominalClock.Value+shiftFirst-game.Elapsed,step);else MoveTo(game,x,step,effect);
    double time=game.Elapsed;bool grounded=game.Players[0].Support==SupportKind.Wall;
    bool accepted=Tap(game);
    if(flight==FlightPresets.Chosen){Advance(game,AirDelay+shiftSecond-shiftFirst,step);accepted=Tap(game)&&accepted;}
    Advance(game,5,step);
    return(grounded&&accepted&&game.Players.All(p=>p.Collisions==0),time,game);
}
var rows=new List<object>();
foreach(var flight in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(double speed in new[]{110d,220d,330d}){
    Func<double,double> t=flight==FlightPresets.FastFlip?FastTime:d=>MainTime(d);
    double low=1030-speed*t(72),high=1178-speed*t(140),x=(low+high)/2;
    // Center the intersection of the independent +/-60ms input perturbations.
    if(flight==FlightPresets.Chosen){double aLow=double.NegativeInfinity,aHigh=double.PositiveInfinity;
        for(int a=-6;a<=6;a++)for(int b=-6;b<=6;b++){double s=a*.01,d=AirDelay+(b-a)*.01;
            aLow=Math.Max(aLow,1030-speed*MainTime(72,d)-speed*s);aHigh=Math.Min(aHigh,1178-speed*MainTime(140,d)-speed*s);}
        Need(aHigh>aLow,"common Main joint window");x=(aLow+aHigh)/2;}
    foreach(int side in new[]{0,1})foreach(double step in Steps){var nominal=Run(flight,side,speed,x,step);Need(nominal.clean,"held nominal "+flight.FirstAction+" "+speed);
        foreach(double a in new[]{-.06,0,.06})foreach(double b in flight==FlightPresets.Chosen?new[]{-.06,0,.06}:new[]{0d}){
            var shifted=Run(flight,side,speed,x,step,nominalClock:nominal.time,shiftFirst:a,shiftSecond:b);
            Need(shifted.clean,"held jitter "+flight.FirstAction+" "+speed+" "+side+" "+a+" "+b);}}
    rows.Add(new{kind="held",profile=flight.FirstAction.ToString(),speed,x,airDelay=flight==FlightPresets.Chosen?AirDelay:0,low,high,windowMs=(high-low)/speed*1000});
}
foreach(var flight in new[]{FlightPresets.FastFlip,FlightPresets.Chosen})foreach(string effect in new[]{"boost-end","slow-end","boost-start","slow-start"}){
    double bestX=double.NaN,bestSlack=-1;double bestTime=0;
    for(double x=880;x<=1120;x+=2){var nominal=Run(flight,1,220,x,effect:effect);if(!nominal.clean)continue;
        var p=nominal.game.Players[0];if(p.BoostsEarned+p.SlowsEarned!=1)continue;
        double slack=0;for(int ms=5;ms<=150;ms+=5){bool good=true;
            foreach(double a in new[]{-ms/1000d,ms/1000d}){good&=Run(flight,1,220,x,effect:effect,nominalClock:nominal.time,shiftFirst:a).clean;
                if(flight==FlightPresets.Chosen)good&=Run(flight,1,220,x,effect:effect,nominalClock:nominal.time,shiftSecond:a).clean;}
            if(!good)break;slack=ms;}
        if(slack>bestSlack){bestX=x;bestSlack=slack;bestTime=nominal.time;}}
    Need(bestSlack>=60,"real effect window "+flight.FirstAction+" "+effect+" best="+bestSlack+" x="+bestX);
    foreach(int side in new[]{0,1})foreach(double step in Steps){var nominal=Run(flight,side,220,bestX,step,effect);Need(nominal.clean,"real effect nominal");
        foreach(double a in new[]{-.06,0,.06})foreach(double b in flight==FlightPresets.Chosen?new[]{-.06,0,.06}:new[]{0d}){
            var shifted=Run(flight,side,220,bestX,step,effect,nominal.time,a,b);
            Need(shifted.clean,"real effect jitter "+flight.FirstAction+" "+effect+" a="+a+" b="+b);}}
    var witness=Run(flight,1,220,bestX,effect:effect);var observed=witness.game.Players[0];
    bool negative=effect.StartsWith("slow",StringComparison.Ordinal);
    double eventAt=(negative?observed.LastSlow:observed.LastInstant).At,eventX=220*eventAt;
    rows.Add(new{kind="real-effect",profile=flight.FirstAction.ToString(),effect,x=bestX,time=bestTime,
        individualWindowAtLeastMs=bestSlack,jointTestedMs=60,nominalEventX=eventX,nominalEffectEndX=eventX+(negative?66:198)});
}
string json=JsonSerializer.Serialize(new{representation=paired?"historical two-half fixture":"one production offset obstacle",checks,runs,rows},new JsonSerializerOptions{WriteIndented=true});
string output=args.FirstOrDefault(a=>a.EndsWith(".json",StringComparison.OrdinalIgnoreCase));if(output!=null)File.WriteAllText(output,json);
Console.WriteLine(json);
Console.WriteLine($"PASS offset candidate: {checks} checks / {runs} actual runs. Held fixtures isolate speeds; transition fixtures collect real .6s effects.");
