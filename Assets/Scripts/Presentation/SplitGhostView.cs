using System;
using FlipRace.Core;
namespace FlipRace.Presentation
{
    /// <summary>Read-only opponent position through the observer's unchanged lane camera.</summary>
    public readonly struct SplitGhostView
    {
        public readonly double Width,Height,Top,Bottom,ScaleX,ScaleY,AnchorX,CanonicalX,X,Y,Gap;
        public readonly bool MirrorX;
        SplitGhostView(double width,double height,GameState game,int observer)
        {
            Width=width;Height=height;Top=Clamp(height*.095,13,25);Bottom=height-Top;
            ScaleY=(Bottom-Top)/RaceConfig.CorridorHeight;AnchorX=Clamp(width*.19,62,195);
            ScaleX=(width-AnchorX-20)/Math.Max(1080,width*1.28);
            var own=game.Players[observer];var other=game.Players[1-observer];
            Gap=other.Distance-own.Distance;CanonicalX=AnchorX+Gap*ScaleX;
            MirrorX=observer==0;X=MirrorX?width-CanonicalX:CanonicalX;
            Y=Top+RaceSimulation.GetPlayerCenterY(other)*ScaleY;
        }
        public bool IsVisible { get { return ScaleX>0&&X>=0&&X<=Width; } }
        public static SplitGhostView Create(double width,double height,GameState game,int observer)
        {
            if(game==null||observer<0||observer>1||width<=82||height<=26)return default(SplitGhostView);
            return new SplitGhostView(width,height,game,observer);
        }
        static double Clamp(double value,double min,double max){return Math.Max(min,Math.Min(max,value));}
    }
}
