using System;
using FlipRace.Core;
using UnityEngine;
namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        bool splitGhost;
        public bool SplitGhostEnabled { get { return Trial&&!sharedField&&splitGhost; } }
        public Vector2 GhostScreenPoint(int observer)
        {
            Rect lane=lanes[observer];var view=SplitGhostView.Create(lane.width,lane.height,Game,observer);
            Rect point=LaneScreenRect(observer,new Rect((float)view.X,(float)view.Y,0,0));
            return new Vector2(point.x,point.y);
        }
        void DrawOpponentGhost(int observer,float w,float h,CorridorArtwork.Geometry own)
        {
            int other=1-observer;var p=Game.Players[other];
            var view=SplitGhostView.Create(w,h,Game,observer);
            var g=artwork.OpponentGeometry(w,h,Game,observer,animationTime,own);
            // Do not clamp a false body to an edge. If any of its rotated source
            // rectangle is outside the lane, replace it with a small gap arrow.
            Rect unit=MascotArtwork.UnitBounds;
            float angle=g.Rotation*Mathf.Deg2Rad,c=Mathf.Cos(angle),s=Mathf.Sin(angle);
            bool fits=view.IsVisible;
            for(int corner=0;corner<4;corner++)
            {
                float x=(corner%2==0?unit.xMin:unit.xMax)*g.Radius*(g.MirrorX?-1:1);
                float y=(corner<2?unit.yMin:unit.yMax)*g.Radius;
                float px=g.X+c*x-s*y,py=g.Y+s*x+c*y;
                fits &= px>=0&&px<=w&&py>=0&&py<=h;
            }
            if(!fits)
            {
                bool right=view.X>w/2;
                float rw=108;
                Rect edge=new Rect(right?w-rw-5:5,Mathf.Clamp((float)view.Y-10,30,h-28),rw,20);
                Round(edge,5,new Color(.98f,.98f,.94f,.8f));
                Text(edge,(right?"→ ":"← ")+Number(Math.Abs(view.Gap),0)+(view.Gap>=0?" ед впереди":" ед позади"),8,muted,false,TextAnchor.MiddleCenter);
                return;
            }
            MascotReaction reaction=MascotReaction.None;float age=0;
            if(p.Recovering>0)reaction=MascotReaction.Hit;
            else if(p.FinishTime.HasValue&&Game.Winner is int&&(int)Game.Winner==other&&animationTime-winnerReactionAt<1.2f)
            {reaction=MascotReaction.Win;age=animationTime-winnerReactionAt;}
            else if(p.LastFinishBoost!=null&&Game.Elapsed-p.LastFinishBoost.At<.65)
            {reaction=MascotReaction.Success;age=(float)(Game.Elapsed-p.LastFinishBoost.At);}
            else if(p.LastSuccess!=null&&Game.Elapsed-p.LastSuccess.At<.65)
            {reaction=MascotReaction.Success;age=(float)(Game.Elapsed-p.LastSuccess.At);}
            var texture=MascotArtwork.GetMotionFrame(p.CharacterIndex,other,animationTime,g.MotionTime,g.FlipProgress,
                g.Running,p.Flipping,g.Recovering,reaction,age);
            Matrix4x4 lane=GUI.matrix;
            GUI.matrix=lane*Matrix4x4.TRS(new Vector3(g.X,g.Y,0),Quaternion.Euler(0,0,g.Rotation),new Vector3(g.MirrorX?-1:1,1,1));
            GUI.color=new Color(1,1,1,.30f);
            GUI.DrawTexture(new Rect(unit.x*g.Radius,unit.y*g.Radius,unit.width*g.Radius,unit.height*g.Radius),texture);
            GUI.color=Color.white;GUI.matrix=lane;
        }
    }
}
