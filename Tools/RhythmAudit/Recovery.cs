using System;
using System.Collections.Generic;
using System.Reflection;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static object CopyValue(object value)
    {
        if(value==null)return null;
        Type type=value.GetType();
        if(type.IsValueType||value is string||value is Track||value is FlightSettings||value is MotionProfile||value is FlightMeasurement)return value;
        if(value is HashSet<string>)return new HashSet<string>((HashSet<string>)value);
        if(value is Queue<SpeedEffect>)
        {var q=new Queue<SpeedEffect>();foreach(var e in (Queue<SpeedEffect>)value)q.Enqueue((SpeedEffect)CopyValue(e));return q;}
        if(value is PlayerState[])
        {var old=(PlayerState[])value;var next=new PlayerState[old.Length];for(int i=0;i<old.Length;i++)next[i]=(PlayerState)CopyValue(old[i]);return next;}
        object clone;
        if(value is GameState)clone=new GameState();
        else if(value is PlayerState)clone=new PlayerState(((PlayerState)value).Id);
        else if(value is SpeedEffect)clone=new SpeedEffect();
        else if(value is HitInfo)clone=new HitInfo();
        else throw new Exception("Unknown mutable clone type: "+type);
        foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))field.SetValue(clone,CopyValue(field.GetValue(value)));
        return clone;
    }
    static GameState Clone(GameState g){return (GameState)CopyValue(g);}
    static bool UntilHit(GameState game,double at,double step=.002)
    {
        while(game.Phase=="racing"&&game.Elapsed<at-1e-11)
        {RaceSimulation.Advance(game,Math.Min(step,at-game.Elapsed));if(game.Players[0].Collisions>0)return true;}
        return game.Players[0].Collisions>0;
    }
    static GameState PerturbedHit(Track track,FlightSettings f,List<PlannedInput> plan,int changed,double shift)
    {
        var game=Start(track,f);
        for(int i=0;i<plan.Count;i++)
        {
            double at=plan[i].At+(i==changed?shift:0);
            if(at<game.Elapsed-1e-8)return null;
            if(UntilHit(game,at))return game;
            foreach(var p in game.Players)RaceSimulation.Press(game,p.Id);
        }
        return UntilHit(game,100)?game:null;
    }
    static bool Reach(GameState start,double target,int hits,double step=.025)
    {
        var g=Clone(start);
        while(g.Phase=="racing"&&g.Players[0].Distance<target-1e-8)
        {RaceSimulation.Advance(g,Math.Min(step,Math.Max(1e-8,(target-g.Players[0].Distance)/440)));if(g.Players[0].Collisions!=hits)return false;}
        return g.Players[0].Collisions==hits&&g.Players[0].Distance>=target-1e-7;
    }
    static bool TapBoth(GameState g)
    {bool a=RaceSimulation.Press(g,0),b=RaceSimulation.Press(g,1);return a&&b;}
    static bool FromRealWall(GameState state,Obstacle next,int hits,double target)
    {
        var g=Clone(state);var p=g.Players[0];
        if(next.Kind!="gate"||p.ActiveEffect!=null||p.Support!=SupportKind.Wall||p.Flipping)return false;
        var plan=Oracle(new Track(g.Track.Length,new[]{next}),g.Flight);
        if(p.SupportSide!=plan[0].OriginSide||p.Distance>plan[0].X+1e-8)return false;
        Advance(g,(plan[0].X-p.Distance)/Speed,.01);
        if(p.Collisions!=hits||!TapBoth(g))return false;
        if(plan.Count>1)
        {Advance(g,MainRepeatDelay,.01);if(p.Collisions!=hits||!TapBoth(g))return false;}
        return Reach(g,target,hits);
    }
    static bool WaitForWallAndFollow(GameState state,Obstacle next,int hits,double target)
    {
        var g=Clone(state);var p=g.Players[0];
        while(g.Phase=="racing"&&p.Collisions==hits&&p.Distance<target)
        {
            if(p.Support==SupportKind.Wall&&!p.Flipping)return FromRealWall(g,next,hits,target);
            RaceSimulation.Advance(g,.005);
        }
        return false;
    }
    static bool Escape(GameState hit,out string witness)
    {
        witness="";var g=Clone(hit);int hits=g.Players[0].Collisions;
        var ordered=new List<Obstacle>(g.Track.Obstacles);ordered.Sort((a,b)=>ObstacleGeometry.Left(a).CompareTo(ObstacleGeometry.Left(b)));
        int index=ordered.FindIndex(o=>o.Id==g.Players[0].LastHit.Id);Need(index>=0,"hit ID belongs to stage");
        double target=index+1<ordered.Count?ObstacleGeometry.Right(ordered[index+1])+Radius+1:g.Track.Length;
        Advance(g,g.Players[0].Recovering+.15,.01);
        if(g.Players[0].Collisions!=hits)return false;
        if(Reach(g,target,hits)){witness="stay reaches "+target;return true;}
        if(index+1<ordered.Count)
        {
            var next=ordered[index+1];
            if(WaitForWallAndFollow(g,next,hits,target)){witness="natural wall then independent next-gate oracle reaches "+target;return true;}
            int settleLimit=(int)Math.Ceiling((target-g.Players[0].Distance)/Speed/.04)+1;
            for(int delay=0;delay<=settleLimit;delay++)
            {
                var settle=Clone(g);Advance(settle,delay*.04,.02);
                if(settle.Phase!="racing"||settle.Players[0].Collisions!=hits||!TapBoth(settle))continue;
                if(WaitForWallAndFollow(settle,next,hits,target)){witness="settling tap +"+(delay*.04)+", actual wall, then next-gate oracle reaches "+target;return true;}
                if(g.Flight.FirstAction==FirstAction.Jump)
                    foreach(double settleRepeat in new[]{.18,.24,.32,.4})
                    {
                        var corrected=Clone(settle);Advance(corrected,settleRepeat,.02);
                        if(corrected.Phase!="racing"||corrected.Players[0].Collisions!=hits||!TapBoth(corrected))continue;
                        if(WaitForWallAndFollow(corrected,next,hits,target))
                        {witness="settling tap +"+(delay*.04)+", correction +"+settleRepeat+", actual wall, then next-gate oracle reaches "+target;return true;}
                    }
            }
        }
        int maximumDelay=(int)Math.Ceiling((target-g.Players[0].Distance)/Speed/.04)+1;
        for(int delay=0;delay<=maximumDelay;delay++)
        {
            var first=Clone(g);Advance(first,delay*.04,.02);
            if(first.Phase!="racing"||first.Players[0].Collisions!=hits||!TapBoth(first))continue;
            if(Reach(first,target,hits)){witness="tap +"+(delay*.04)+" reaches "+target;return true;}
            for(int secondDelay=4;secondDelay<=30;secondDelay++)
            {
                var second=Clone(first);Advance(second,secondDelay*.04,.02);
                if(second.Phase!="racing"||second.Players[0].Collisions!=hits||!TapBoth(second))continue;
                if(Reach(second,target,hits)){witness="tap +"+(delay*.04)+", repeat +"+(secondDelay*.04)+" reaches "+target;return true;}
            }
        }
        return false;
    }
    static void Recovery(Track supplied=null,Func<Track,FlightSettings,List<PlannedInput>> planner=null)
    {
        var track=supplied??TracksRhythm.Practice();int samples=0,failures=0;
        foreach(var f in Profiles)
        {
            var plan=planner==null?Oracle(track,f):planner(track,f);int local=0;
            for(int i=0;i<plan.Count;i++)foreach(double shift in new[]{-.64,-.4,-.16,.16,.4,.64})
            {
                context=Name(f)+" recovery input="+i+" shift="+shift;
                var hit=PerturbedHit(track,f,plan,i,shift);if(hit==null)continue;
                samples++;local++;string witness;
                if(!Escape(hit,out witness))
                {failures++;Console.WriteLine("NO_FOUND_ESCAPE "+context+" hit="+hit.Players[0].LastHit.Id+" x="+hit.Players[0].Distance+" y="+RaceSimulation.GetPlayerCenterY(hit.Players[0]));}
                else if(local<=3)Console.WriteLine(context+" "+witness);
            }
            Console.WriteLine(Name(f)+" actual first-collision samples="+local);
        }
        Need(samples>0,"nonempty actual-hit sample");Need(failures==0,"no escape found in "+failures+" finite samples; not proof of unavoidable collision");
        Console.WriteLine("PASS RH sampled recovery: "+samples+" actual collision states; remaining penalty +150ms, stay/one or two taps/settling one or two taps plus true wall then next-gate oracle, explicit next unionRight+R+1 reach. Finite bounded sample only.");
    }
}
