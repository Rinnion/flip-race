using System;
using UnityEngine;
namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        float firstPawAt=-10,secondPawAt=-10;
        public bool PawsVisible { get { return Game!=null&&Game.Phase=="racing"&&!paused&&!showRules&&!showTracks&&!Portrait; } }
        public Rect PawScreenRect(int slot)
        {
            if(slot<0||slot>1)throw new ArgumentOutOfRangeException(nameof(slot));
            Rect area=ControlAreaRect(slot),safe=SafeUiArea();
            bool rotated=SharedEnabled?SharedRotated(slot):slot==0;
            float left=Mathf.Max(area.xMin,safe.xMin)+8,right=Mathf.Min(area.xMax,safe.xMax)-8;
            float top=Mathf.Max(area.yMin,safe.yMin)+8,bottom=Mathf.Min(area.yMax,safe.yMax)-8;
            return new Rect(rotated?left:right-56,rotated?top:bottom-56,56,56);
        }
        void LocalRacePress(int slot,Vector2 point)
        {
            // One shared pointer edge reaches Act exactly once. The paw is
            // artwork over that existing area, never a second GUI.Button path.
            if(PawsVisible&&PawScreenRect(slot).Contains(point))
            {if(slot==0)firstPawAt=animationTime;else secondPawAt=animationTime;}
            Act(slot);
        }
        void DrawPaws()
        {
            if(!PawsVisible)return;
            Matrix4x4 canvas=GUI.matrix;
            for(int slot=0;slot<2;slot++)
            {
                if(Game.Players[slot].FinishTime.HasValue)continue;
                Rect r=PawScreenRect(slot);bool rotated=SharedEnabled?SharedRotated(slot):slot==0;
                GUI.matrix=canvas*Matrix4x4.TRS(new Vector3(r.center.x,r.center.y,0),Quaternion.Euler(0,0,rotated?180:0),Vector3.one);
                float age=animationTime-(slot==0?firstPawAt:secondPawAt);
                bool pressed=age>=0&&age<.16f;
                Color accent=accents[slot],pale=pales[slot];
                StrokeRound(new Rect(-28,-28,56,56),17,new Color(accent.r,accent.g,accent.b,pressed?.6f:.25f),
                    new Color(pale.r,pale.g,pale.b,pressed?.42f:.18f));
                Color ink=new Color(accent.r,accent.g,accent.b,pressed?.85f:.60f);
                Ellipse(new Rect(-11,-1,22,17),ink);
                Ellipse(new Rect(-17,-13,8,11),ink);Ellipse(new Rect(-9,-21,8,12),ink);
                Ellipse(new Rect(1,-21,8,12),ink);Ellipse(new Rect(10,-13,8,11),ink);
            }
            GUI.color=Color.white;GUI.matrix=canvas;
        }
    }
}
