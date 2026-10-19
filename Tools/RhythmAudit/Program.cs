using System;
using System.Collections.Generic;
using System.Globalization;
using FlipRace.Core;

// Independent tool only. The playable stage path never reads authored route
// arrays or modifies GameState fields. Oracle inputs come from physical bands.
public static partial class RhythmIndependentAudit
{
    const double Radius=14, Height=240, Speed=220, MainRepeatDelay=.18;
    static int checks, games;
    static string context;
    static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
    static readonly FlightSettings[] Profiles={FlightPresets.FastFlip,FlightPresets.Chosen};
    sealed class PlannedInput
    {
        public double At, X;
        public int Gate, OriginSide;
        public bool Air;
    }
    sealed class Result
    {
        public GameState Game;
        public int Accepted, Rejected;
        public readonly List<double> Times=new List<double>();
    }
    static void Need(bool yes,string why)
    { checks++;if(!yes)throw new Exception(context+": "+why); }
    static void Near(double actual,double expected,string why,double tolerance=2e-7)
    { Need(!double.IsNaN(actual)&&Math.Abs(actual-expected)<=tolerance,why+" actual="+actual.ToString("R")+" expected="+expected.ToString("R")); }
    static string Name(FlightSettings f){return f.FirstAction==FirstAction.Flip?"Fast":"Main";}
    static GameState Start(Track track,FlightSettings flight)
    {
        var g=RaceSimulation.CreateTrial(track,flight);
        Need(RaceSimulation.Press(g,0)&&RaceSimulation.Press(g,1),"both ready accepted");
        RaceSimulation.Advance(g,3);Need(g.Phase=="racing","actual countdown");games++;return g;
    }
    static void Advance(GameState g,double duration,double step)
    {
        Need(duration>=-1e-8,"nonnegative scheduler duration");
        int guard=0;
        while(duration>1e-12&&g.Phase=="racing")
        {
            double dt=Math.Min(duration,step);RaceSimulation.Advance(g,dt);duration-=dt;
            if(++guard>1000000)throw new Exception("Scheduler iteration guard");
        }
    }
    static void Until(GameState g,double when,double step)
    { Advance(g,Math.Max(0,when-g.Elapsed),step); }
    static void Finish(GameState g,double step)
    {
        int guard=0;
        while(g.Phase=="racing")
        {RaceSimulation.Advance(g,step);if(++guard>1000000||g.Elapsed>120)throw new Exception("Finish guard");}
    }
    // Inverse displacement is independent of the production collision helper.
    // Main uses a different real repeat delay (.18s) from the author's .2s path.
    static double RiseTime(double displacement,FlightSettings f,double mainDelay=MainRepeatDelay)
    {
        double gravity=f.GravityAcceleration;
        if(f.FirstAction==FirstAction.Flip)
            return (Math.Sqrt(f.FlipImpulse*f.FlipImpulse+2*gravity*displacement)-f.FlipImpulse)/gravity;
        double q=mainDelay,jump=f.JumpImpulse;
        double first=jump*q-.5*gravity*q*q,velocity=jump-gravity*q;
        if(displacement<=first)return (jump-Math.Sqrt(jump*jump-2*gravity*displacement))/gravity;
        return q+(Math.Sqrt(velocity*velocity+2*gravity*(displacement-first))-velocity)/gravity;
    }
    static List<PlannedInput> Oracle(Track track,FlightSettings f,double speed=Speed,double mainDelay=MainRepeatDelay)
    {
        var gates=new List<Obstacle>(track.Obstacles);
        gates.Sort((a,b)=>Math.Min(a.X+a.TopOffsetX,a.X+a.BottomOffsetX).CompareTo(Math.Min(b.X+b.TopOffsetX,b.X+b.BottomOffsetX)));
        var plan=new List<PlannedInput>();
        for(int i=0;i<gates.Count;i++)
        {
            var o=gates[i];Need(o.Kind=="gate"&&o.GapTop>0&&o.GapBottom<Height,"two-band stage gate");
            double top=o.X+o.TopOffsetX,bottom=o.X+o.BottomOffsetX;
            int side=top<bottom?1:0;
            double low,high;
            if(side==1)
            {
                low=top+o.Width+Radius-speed*RiseTime(Height-Radius-o.GapTop-Radius,f,mainDelay);
                high=bottom-Radius-speed*RiseTime(Height-Radius-o.GapBottom+Radius,f,mainDelay);
            }
            else
            {
                low=bottom+o.Width+Radius-speed*RiseTime(o.GapBottom-Radius-Radius,f,mainDelay);
                high=top-Radius-speed*RiseTime(o.GapTop+Radius-Radius,f,mainDelay);
            }
            Need(high>low,"analytic monotone interval exists");
            double x=(low+high)/2;
            plan.Add(new PlannedInput{X=x,At=x/speed,Gate=i,OriginSide=side});
            if(f.FirstAction==FirstAction.Jump)
                plan.Add(new PlannedInput{X=x+speed*mainDelay,At=x/speed+mainDelay,Gate=i,OriginSide=side,Air=true});
        }
        for(int i=1;i<plan.Count;i++)Need(plan[i].At>plan[i-1].At,"oracle input order");
        return plan;
    }
    static Result Play(Track track,FlightSettings f,List<PlannedInput> plan,double step,
        int changed=-1,double shift=0,int omit=-1,bool nominal=false,double heldFixtureSpeed=Speed)
    {
        var r=new Result{Game=Start(track,f)};
        if(heldFixtureSpeed!=Speed)HeldSpeedFixture(r.Game,heldFixtureSpeed);
        for(int i=0;i<plan.Count;i++)
        {
            if(i==omit)continue;
            var input=plan[i];double when=input.At+(i==changed?shift:0);
            Need(when>=r.Game.Elapsed-1e-8,"inputs keep their order");Until(r.Game,when,step);
            if(r.Game.Phase!="racing")break;
            foreach(var p in r.Game.Players)
            {
                if(nominal)
                {
                    Near(p.Distance,input.X,"analytic distance at press");
                    Need(input.Air?p.Flipping:p.Support==SupportKind.Wall&&!p.Flipping,"real departure or air correction");
                    if(!input.Air)Need(p.SupportSide==input.OriginSide,"correct origin wall");
                }
                if(RaceSimulation.Press(r.Game,p.Id))r.Accepted++;else r.Rejected++;
            }
            r.Times.Add(r.Game.Elapsed);
        }
        Finish(r.Game,step);return r;
    }
    static void Clean(Result r,Track track,int expectedActions,double physicalSpeed=Speed)
    {
        Need(r.Accepted==expectedActions*2&&r.Rejected==0,"all planned actions accepted on both lanes");
        foreach(var p in r.Game.Players)
        {
            Need(p.Collisions==0,"clean route; hits="+string.Join(",",p.HitObstacleIds));
            Need(p.FinishTime.HasValue,"physical finish reached");Near(p.Distance,track.Length,"finish distance");
            Near(p.FinishTime.Value,track.Length/physicalSpeed,"physical finish clock");
            Need(p.BoostsEarned==0&&p.SlowsEarned==0,"no implicit stage rewards");
        }
        Near(r.Game.Elapsed,track.Length/physicalSpeed,"common actual clock");
    }
    static void Stage()
    {
        var track=TracksRhythm.Practice();Need(track.Obstacles.Length==8,"two intro gates plus six-gate chain");
        foreach(var f in Profiles)
        {
            var plan=Oracle(track,f);int count=f.FirstAction==FirstAction.Flip?8:16;
            Need(plan.Count==count,"oracle action count");
            foreach(double step in Steps)
            {context=Name(f)+" stage dt="+step;Clean(Play(track,f,plan,step,nominal:true),track,count);}
            for(int i=0;i<plan.Count;i++)foreach(double shift in new[]{-.06,.06})
            {context=Name(f)+" input="+i+" shift="+shift;Clean(Play(track,f,plan,1d/144,i,shift),track,count);}
            for(int i=0;i<plan.Count;i++)
            {
                context=Name(f)+" remove="+i;var r=Play(track,f,plan,.317,omit:i);
                foreach(var p in r.Game.Players)Need(p.Collisions>0,"deleting the input changes clean outcome");
            }
            Console.WriteLine(Name(f)+" oracle actions="+count+" intro="+(f.FirstAction==FirstAction.Flip?2:4)+" chain="+(f.FirstAction==FirstAction.Flip?6:12));
            Console.WriteLine("actual input seconds: "+string.Join(",",plan.ConvertAll(p=>p.At.ToString("F9"))));
        }
        Console.WriteLine("PASS independent RH stage: "+checks+" assertions / "+games+" games; geometric oracle, six dt, +/-60ms actual-time shifts, each input removal.");
    }
    static Result Controller(Track track,FlightSettings f,double period,double phase,int hover)
    {
        var r=new Result{Game=Start(track,f)};double next=phase;int lastDecision=0;
        while(r.Game.Phase=="racing")
        {
            var p=r.Game.Players[0];bool press=false;
            if(hover==0&&period>0&&r.Game.Elapsed>=next-1e-10){press=true;next+=period;}
            if((hover==1||hover==3)&&r.Game.Elapsed<1e-10)press=true;
            if(hover==3&&f.FirstAction==FirstAction.Jump&&Math.Abs(r.Game.Elapsed-.18)<1e-8)press=true;
            if(hover==2)
            {
                // Greedy centre controller; legal taps only, no artificial hover state.
                double y=RaceSimulation.GetPlayerCenterY(p);
                int desired=y>132?-1:y<108?1:p.Gravity;
                if(p.Support!=SupportKind.None||desired!=p.Gravity)
                    if(r.Game.Elapsed-lastDecision*.02>=.02-1e-10){press=true;lastDecision=(int)Math.Round(r.Game.Elapsed/.02);}
            }
            if(press)foreach(var player in r.Game.Players)
            {if(RaceSimulation.Press(r.Game,player.Id))r.Accepted++;else r.Rejected++;}
            RaceSimulation.Advance(r.Game,.02);
            if(r.Game.Elapsed>120)throw new Exception("Controller guard");
        }
        return r;
    }
    static bool NoHit(Result r)
    {return r.Game.Players[0].Collisions==0&&r.Game.Players[0].FinishTime.HasValue;}
    static void Bypass()
    {
        var track=TracksRhythm.Practice();int samples=0,clean=0;
        foreach(var f in Profiles)
        {
            foreach(int mode in new[]{0,1,2,3})
            {
                context=Name(f)+" basic bypass="+mode;var r=Controller(track,f,0,0,mode);samples++;
                Console.WriteLine(context+" hits="+r.Game.Players[0].Collisions+" accepted="+r.Accepted/2+" rejected="+r.Rejected/2);
                if(NoHit(r)){clean++;Console.WriteLine("CLEAN BYPASS "+context);}
            }
            foreach(double period in new[]{.25,.35,.45,.6,.8,1.0,1.2,1.5})foreach(double phase in new[]{0,.11,.23,.41})
            {
                context=Name(f)+" metronome="+period+" phase="+phase;
                var r=Controller(track,f,period,phase,0);samples++;
                if(NoHit(r)){clean++;Console.WriteLine("CLEAN BYPASS "+context+" accepted="+r.Accepted/2);}
            }
        }
        Need(clean==0,"bounded simple strategies found "+clean+" clean bypasses requiring review");
        Console.WriteLine("PASS bounded RH bypass probes: "+samples+" actual-play controllers; floor, one-departure, actual ceiling setup, centre feedback and 8 frequencies x4 phases. Not a global minimality proof.");
    }
    public static void Main(string[] args){Run(args.Length==0?"stage":args[0]);}
    public static void Run(string scope)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;checks=0;games=0;context="initial";
        if(scope=="stage")Stage();else if(scope=="bypass")Bypass();else if(scope=="stage-speeds")StageSpeedFixtures();
        else if(scope=="recovery")Recovery();else if(scope=="geometry")Geometry();else if(scope=="recovery-red")RecoveryRed();
        else if(scope=="recovery-map")RecoveryConstraints();else if(scope=="recovery-pitch")RecoveryPitch();
        else if(scope=="parts")GatePresence();else if(scope=="two-band-penalty")TwoBandPenalty();else if(scope=="authored-contacts")AuthoredBandContacts();
        else if(scope=="compact-recovery")CompactRecovery();
        else if(scope=="compact-proposed")CompactProposed();
        else if(scope=="family-stage")FamilyStage();else if(scope=="family-recovery")FamilyRecovery();
        else if(scope=="restored-flow")RestoredFlow();
        else if(scope=="flow-recovery")FlowRecovery();
        else if(scope=="modules")Modules();
        else if(scope=="modules-compact")Modules("rhythm-modules-compact.json");
        else if(scope=="courses-draft")CoursesDraft();
        else if(scope=="courses-bypass")CoursesBypass();
        else if(scope=="courses-before-recovery")CourseRecovery(true);
        else if(scope=="courses-recovery")CourseRecovery(false);
        else if(scope=="courses-before-red")BeforeRecoveryRed();
        else if(scope=="before400-recovery")BeforeCandidateRecovery();
        else if(scope=="courses-final")CoursesDraft(true);
        else if(scope=="courses-duels")CoursesDuels();
        else if(scope=="courses-representatives")RecoveryRepresentatives();
        else if(scope=="courses-recovery-candidate")RecoveryCandidate();
        else if(scope=="courses-recovery-spacing")RecoverySpacing();
        else if(scope=="courses-recovery-replay")RecoverySpacingReplay();
        else if(scope=="courses-recovery-expanded")CourseRecoveryExpanded();
        else throw new ArgumentException("Unimplemented scope: "+scope);
    }
}
