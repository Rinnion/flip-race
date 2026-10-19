using System;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    static void Geometry()
    {
        foreach(double topOffset in new[]{-192d,0d,192d})foreach(double bottomOffset in new[]{-192d,0d,192d})
        foreach(bool topPresent in new[]{false,true})foreach(bool bottomPresent in new[]{false,true})
        {
            context="band bounds top="+topOffset+" bottom="+bottomOffset+" exists="+topPresent+"/"+bottomPresent;
            var o=new Obstacle{Id="pair",Kind="gate",X=1000,Width=16,GapTop=topPresent?140:0,
                GapBottom=bottomPresent?100:240,TopOffsetX=topOffset,BottomOffsetX=bottomOffset};
            ObstacleBand top,bottom;
            Need(ObstacleGeometry.TryGetBand(o,0,out top)==topPresent,"top existence");
            Need(ObstacleGeometry.TryGetBand(o,1,out bottom)==bottomPresent,"bottom existence");
            if(topPresent){Near(top.X,1000+topOffset,"top x",0);Near(top.Y,0,"top y",0);Near(top.Height,140,"top height",0);}
            if(bottomPresent){Near(bottom.X,1000+bottomOffset,"bottom x",0);Near(bottom.Y,100,"bottom y",0);Near(bottom.Height,140,"bottom height",0);}
            double left=topPresent?1000+topOffset:bottomPresent?1000+bottomOffset:1000;
            double right=topPresent?1016+topOffset:bottomPresent?1016+bottomOffset:1000;
            if(topPresent&&bottomPresent){left=Math.Min(1000+topOffset,1000+bottomOffset);right=Math.Max(1016+topOffset,1016+bottomOffset);}
            Near(ObstacleGeometry.Left(o),left,"union existing left",0);Near(ObstacleGeometry.Right(o),right,"union existing right",0);
        }
        foreach(var f in Profiles)foreach(int side in new[]{0,1})foreach(double offset in new[]{192d,-192d})foreach(double step in Steps)
        {
            context=Name(f)+" physical wall contact side="+side+" offset="+offset+" dt="+step;
            var o=new Obstacle{Id="offset-pair",Kind="gate",X=800,Width=16,GapTop=140,GapBottom=100,TopOffsetX=offset,BottomOffsetX=-offset};
            var g=Start(new Track(1800,new[]{o}),f);
            if(side==0)
            {
                Need(TapBoth(g),"real ceiling preparation departure");
                if(f.FirstAction==FirstAction.Jump){Advance(g,MainRepeatDelay,step);Need(TapBoth(g),"real ceiling preparation air switch");Advance(g,RiseTime(212,f)-MainRepeatDelay+1e-8,step);}
                else Advance(g,RiseTime(212,f)+1e-8,step);
                Need(g.Players[0].Support==SupportKind.Wall&&g.Players[0].SupportSide==0,"ceiling attained");
            }
            double firstX=o.X+(side==0?o.TopOffsetX:o.BottomOffsetX)-Radius;
            Until(g,firstX/Speed-1e-6,step);Need(g.Players[0].Collisions==0,"before shifted solid boundary");
            Advance(g,2e-6,step);Need(g.Players[0].Collisions==1,"shifted-band collision at expected x");
            Near(g.Players[0].LastHit.At,firstX/Speed,"exact first impact time");
            Finish(g,step);Need(g.Players[0].Collisions==1,"one logical gate penalty on wall traversal");
        }
        Console.WriteLine("PASS RH initial geometry: "+checks+" assertions / "+games+" games; signed offsets, absent-band union bounds, actual mirrored wall contacts, six dt, shared-ID penalty.");
    }
}
