using System;
using System.Collections.Generic;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static IEnumerable<Track> CurrentGateTracks()
    {
        // Actual current three-track menu plus its two practice sources.
        // Historical release tracks outside this menu are not silently rewritten.
        foreach(var track in RaceTracks.All)yield return track;
        yield return TracksFull.Practice();
        yield return TracksRhythm.Practice();
    }
    static void GatePresence()
    {
        int gates=0,missing=0;
        foreach(var track in CurrentGateTracks())foreach(var o in track.Obstacles)
        {
            if(o.Kind!="gate")continue;
            gates++;ObstacleBand top,bottom;
            bool a=ObstacleGeometry.TryGetBand(o,0,out top),b=ObstacleGeometry.TryGetBand(o,1,out bottom);
            Console.WriteLine(track.Id+" / "+o.Id+" X="+o.X+" top="+(a?top.Height:0)+" bottom="+(b?bottom.Height:0)
                +" offsets="+o.TopOffsetX+"/"+o.BottomOffsetX);
            if(!a||!b||top.Height<=0||bottom.Height<=0)missing++;
        }
        Need(gates>0,"nonempty actual menu/practice gates");
        Need(missing==0,"missing real gate halves="+missing);
        Console.WriteLine("PASS authored gate presence: "+gates+" gates, both nonzero physical halves. This source check does not establish visibility or playable routes.");
    }
    static void ReachDistance(GameState game,double target,double step)
    {
        int guard=0;
        while(game.Phase=="racing"&&game.Players[0].Distance<target-1e-9)
        {
            RaceSimulation.Advance(game,Math.Min(step,(target-game.Players[0].Distance)/Speed));
            if(++guard>100000)throw new Exception("Distance scheduler guard");
        }
        Near(game.Players[0].Distance,target,"actual distance scheduler target");
    }
    static void DepartToOpposite(GameState g,double step)
    {
        Need(TapBoth(g),"supported departure accepted");
        if(g.Flight.FirstAction==FirstAction.Jump)
        {Advance(g,MainRepeatDelay,step);Need(TapBoth(g),"Main real air correction accepted");}
    }
    static void TwoBandPenalty()
    {
        foreach(var f in Profiles)foreach(bool firstTop in new[]{false,true})foreach(bool separate in new[]{false,true})foreach(double step in Steps)
        {
            context=Name(f)+" actual both bands firstTop="+firstTop+" separateIDs="+separate+" dt="+step;
            var pair=new Obstacle{Id="pair",Kind="gate",X=300,Width=16,GapTop=100,GapBottom=140,
                TopOffsetX=firstTop?0:400,BottomOffsetX=firstTop?400:0};
            var obstacles=new List<Obstacle>();
            if(separate)
            {
                obstacles.Add(new Obstacle{Id="first",Kind="gate",X=300,Width=16,GapTop=firstTop?100:0,GapBottom=firstTop?240:140});
                obstacles.Add(new Obstacle{Id="second",Kind="gate",X=700,Width=16,GapTop=firstTop?0:100,GapBottom=firstTop?140:240});
            }
            else obstacles.Add(pair);
            obstacles.Add(new Obstacle{Id="independent-next",Kind="gate",X=1100,Width=16,GapTop=firstTop?0:100,GapBottom=firstTop?140:240});
            var g=Start(new Track(1500,obstacles.ToArray()),f);
            if(firstTop)DepartToOpposite(g,step);
            ReachDistance(g,400,step);
            foreach(var p in g.Players)
            {Need(p.Collisions==1,"first band impact");Need(p.Support==SupportKind.Wall&&p.SupportSide==(firstTop?0:1),"actual wall before second band");}
            DepartToOpposite(g,step);
            ReachDistance(g,685,step);
            foreach(var p in g.Players)
            {Need(p.Support==SupportKind.Wall&&p.SupportSide==(firstTop?1:0),"actual opposite wall intersects second solid band");Need(p.Collisions==1,"before second band front");}
            ReachDistance(g,800,step);
            foreach(var p in g.Players)Need(p.Collisions==(separate?2:1),"shared ID once; distinct ID separately penalized");
            Finish(g,step);
            foreach(var p in g.Players)Need(p.Collisions==(separate?3:2),"immunity does not leak into next obstacle");
        }
        Console.WriteLine("PASS actual contacts with both gate halves: "+checks+" assertions / "+games+" games; both profiles/orientations, common-ID and different-ID controls, next-obstacle independence, six dt. Isolated geometry fixture, actual Press/Advance only.");
    }
    static void AuthoredBandContacts()
    {
        int bands=0,absent=0;
        foreach(var track in CurrentGateTracks())foreach(var o in track.Obstacles)
        {
            if(o.Kind!="gate")continue;
            for(int side=0;side<2;side++)
            {
                ObstacleBand band;
                if(!ObstacleGeometry.TryGetBand(o,side,out band)){absent++;continue;}
                bands++;
                foreach(var f in Profiles)foreach(double step in new[]{1d/144,.317,100})
                {
                    context=track.Id+" / "+o.Id+" band="+side+" "+Name(f)+" dt="+step;
                    // Translate one authored gate so both halves are ahead of
                    // preparation. Preserve each actual width/height/offset.
                    double shift=1000-ObstacleGeometry.Left(o);
                    var copy=new Obstacle{Id=o.Id,Kind="gate",X=o.X+shift,Width=o.Width,GapTop=o.GapTop,GapBottom=o.GapBottom,
                        TopOffsetX=o.TopOffsetX,BottomOffsetX=o.BottomOffsetX};
                    var g=Start(new Track(1600+ObstacleGeometry.Right(copy),new[]{copy}),f);
                    if(side==0)DepartToOpposite(g,step);
                    double contactX=band.X+shift-Radius;
                    Until(g,contactX/Speed-1e-6,step);
                    foreach(var p in g.Players)Need(p.Collisions==0,"no early or phantom opposite-band impact");
                    Advance(g,2e-6,step);
                    foreach(var p in g.Players)
                    {Need(p.Collisions==1,"nonzero authored band produces actual contact");Near(p.LastHit.At,contactX/Speed,"analytic rectangle front contact");}
                }
            }
        }
        Need(bands>0,"nonempty authored bands");
        Console.WriteLine("PASS isolated authored-band contacts: "+bands+" present bands, "+absent+" absent halves reported separately; "+checks+" assertions / "+games+" games. Not an authored-route or gap-window proof; presence scope must independently require absent=0.");
    }
}
