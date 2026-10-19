using System;
using System.Collections.Generic;
using System.Reflection;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    static GameState CopyGame(GameState source)
    {
        var copy=new GameState();
        foreach(var field in typeof(GameState).GetFields(BindingFlags.Public|BindingFlags.Instance))field.SetValue(copy,field.GetValue(source));
        copy.Players=new PlayerState[source.Players.Length];
        for(int i=0;i<copy.Players.Length;i++)
        {
            var p=source.Players[i];var q=new PlayerState(p.Id);
            foreach(var field in typeof(PlayerState).GetFields(BindingFlags.Public|BindingFlags.Instance))
            {
                object value=field.GetValue(p);
                if(value is HashSet<string>)value=new HashSet<string>((HashSet<string>)value);
                else if(value is SpeedEffect)value=CopyEffect((SpeedEffect)value);
                else if(value is Queue<SpeedEffect>)
                {var queue=new Queue<SpeedEffect>();foreach(var e in (Queue<SpeedEffect>)value)queue.Enqueue(CopyEffect(e));value=queue;}
                field.SetValue(q,value);
            }
            copy.Players[i]=q;
        }
        return copy;
    }
    static SpeedEffect CopyEffect(SpeedEffect source)
    {
        if(source==null)return null;var copy=new SpeedEffect();
        foreach(var field in typeof(SpeedEffect).GetFields(BindingFlags.Public|BindingFlags.Instance))field.SetValue(copy,field.GetValue(source));
        return copy;
    }
    static bool AdvanceToHit(GameState game,double targetTime)
    {
        while(game.Phase=="racing"&&game.Elapsed<targetTime-1e-12)
        {RaceSimulation.Advance(game,Math.Min(.005,targetTime-game.Elapsed));if(game.Players[0].Collisions>0)return true;}
        return game.Players[0].Collisions>0;
    }
    static GameState FirstShiftedHit(Track track,FlightSettings flight,double[] route,double[] times,int changed,double shift)
    {
        var game=Start(track,flight);
        for(int i=0;i<route.Length&&game.Phase=="racing";i++)
        {
            if(i==changed)
            {
                double when=times[i]+shift;if(when<game.Elapsed-1e-10)return null;
                if(AdvanceToHit(game,when))return game;
            }
            else
            {
                while(game.Phase=="racing"&&route[i]-game.Players[0].Distance>2e-7)
                {
                    RaceSimulation.Advance(game,Math.Min(.005,Math.Max(1e-9,(route[i]-game.Players[0].Distance)/440)));
                    if(game.Players[0].Collisions>0)return game;
                }
                if(route[i]<game.Players[0].Distance-1e-6)continue;
            }
            if(game.Phase=="racing")
            {RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);}
        }
        return AdvanceToHit(game,100)?game:null;
    }
    static bool TryReachNext(GameState original,double target,int hits)
    {
        var game=CopyGame(original);
        while(game.Phase=="racing"&&game.Players[0].Distance<target-2e-7)
        {
            RaceSimulation.Advance(game,Math.Min(.02,Math.Max(1e-9,(target-game.Players[0].Distance)/440)));
            if(game.Players[0].Collisions!=hits)return false;
        }
        return game.Players[0].Collisions==hits&&game.Players[0].Distance>=target-2e-7;
    }
    static bool EscapeAfterReaction(GameState original,out string witness)
    {
        var game=CopyGame(original);var p=game.Players[0];int hits=p.Collisions;witness="";
        int index=Array.FindIndex(game.Track.Obstacles,o=>o.Id==p.LastHit.Id);
        Check(index>=0,"actual hit has an authored obstacle");
        if(index==game.Track.Obstacles.Length-1){witness="last obstacle, no next";return true;}
        var next=game.Track.Obstacles[index+1];double target=next.X+next.Width+R+1;
        Advance(game,p.Recovering+.15,.01);
        if(p.Collisions!=hits)return false;
        if(TryReachNext(game,target,hits)){witness="stay reaches "+target;return true;}
        for(int delay=0;delay<=40;delay++)
        {
            var one=CopyGame(game);Advance(one,delay*.05,.02);
            if(one.Phase!="racing"||one.Players[0].Collisions!=hits)continue;
            if(!RaceSimulation.Press(one,0)||!RaceSimulation.Press(one,1))continue;
            if(TryReachNext(one,target,hits)){witness="tap after "+(delay*.05)+" reaches "+target;return true;}
            for(int repeat=3;repeat<=36;repeat++)
            {
                var two=CopyGame(one);Advance(two,repeat*.05,.02);
                if(two.Phase!="racing"||two.Players[0].Collisions!=hits)continue;
                if(!RaceSimulation.Press(two,0)||!RaceSimulation.Press(two,1))continue;
                if(TryReachNext(two,target,hits)){witness="taps after "+(delay*.05)+", then "+(repeat*.05)+" reach "+target;return true;}
            }
        }
        return false;
    }
    static void FullRecoveryAudit()
    {
        int samples=0,failures=0;
        foreach(var flight in new[]{ExpectedMain,ExpectedFast})foreach(var source in TracksFull.All)foreach(bool full in new[]{false,true})
        {
            var track=TracksFull.Get(source.Id,flight);var route=full?TracksFull.FullRoute(track.Id,flight):TracksFull.SafeRoute(track.Id,flight);
            context="recovery baseline "+track.Id+" "+flight.FirstAction+" full="+full;
            double[] times;FullRouteGame(track,flight,full?1:0,1d/144,out times);int local=0;
            for(int i=0;i<route.Length;i++)foreach(double shift in new[]{-.5,-.25,-.1,.1,.25,.5})
            {
                context="recovery "+track.Id+" "+flight.FirstAction+" full="+full+" input="+i+" shift="+shift;
                var hit=FirstShiftedHit(track,flight,route,times,i,shift);if(hit==null)continue;
                samples++;local++;string witness;bool escaped=EscapeAfterReaction(hit,out witness);
                if(!escaped){failures++;Console.WriteLine("NO_ESCAPE "+context+" obstacle="+hit.Players[0].LastHit.Id+" "+State(hit.Players[0]));}
                else if(local<=2)Console.WriteLine("RECOVERY "+context+" "+witness);
            }
            Console.WriteLine("RECOVERY SAMPLES "+track.Id+" "+flight.FirstAction+" full="+full+" count="+local);
        }
        Check(failures==0,"finite recovery sample has "+failures+" states without found escape");
        Console.WriteLine("PASS independent recovery: "+samples+" real collision states, remaining recovery+150ms then stay/up to two accepted taps; explicit next trailing reach. Finite single-input sample, not all possible errors.");
    }
}
