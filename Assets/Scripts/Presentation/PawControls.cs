using System;
using UnityEngine;
namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        float firstPawAt=-10,secondPawAt=-10,thirdPawAt=-10,fourthPawAt=-10;
        public bool PawsVisible { get { return Game!=null&&Game.Phase=="racing"&&!paused&&!showRules&&!showTracks&&!Portrait; } }
        public Rect PawScreenRect(int slot)
        {
            if(slot<0||slot>=Game.Players.Length)throw new ArgumentOutOfRangeException(nameof(slot));
            if(BotEnabled && slot!=0)return default(Rect);
            Rect safe=SafeUiArea(); int corner=PawCorner(slot);
            float horizontalInset=compact?40:8;
            float x=corner%2==0?safe.xMin+horizontalInset:safe.xMax-horizontalInset-56;
            float y=corner<2?safe.yMin+8:safe.yMax-8-56;
            return new Rect(x,y,56,56);
        }
        void LocalRacePress(int slot,Vector2 point)
        {
            if((BotEnabled||PartyEnabled||SoloEnabled) && Game.Phase=="racing" && Game.Players[slot].Status!=FlipRace.Core.PlayerStatus.Running)return;
            // One shared pointer edge reaches Act exactly once. The paw is
            // artwork over that existing area, never a second GUI.Button path.
            if(PawsVisible&&PawScreenRect(slot).Contains(point))
            {if(slot==0)firstPawAt=animationTime;else if(slot==1)secondPawAt=animationTime;else if(slot==2)thirdPawAt=animationTime;else fourthPawAt=animationTime;}
            Act(slot);
        }
        void DrawPaws()
        {
            if(!PawsVisible)return;
            Matrix4x4 canvas=GUI.matrix;
            for(int slot=0;slot<(BotEnabled?1:Game.Players.Length);slot++)
            {
                if(Game.Players[slot].FinishTime.HasValue||((BotEnabled||PartyEnabled||SoloEnabled)&&Game.Players[slot].Status!=FlipRace.Core.PlayerStatus.Running))continue;
                Rect r=PawScreenRect(slot);bool rotated=PartyEnabled?PartyRotated(slot):SharedEnabled?SharedRotated(slot):slot==0;
                GUI.matrix=canvas*Matrix4x4.TRS(new Vector3(r.center.x,r.center.y,0),Quaternion.Euler(0,0,rotated?180:0),Vector3.one);
                float age=animationTime-(slot==0?firstPawAt:slot==1?secondPawAt:slot==2?thirdPawAt:fourthPawAt);
                bool pressed=age>=0&&age<.16f;
                Color accent=accents[slot],pale=pales[slot];
                StrokeRound(new Rect(-28,-28,56,56),17,new Color(accent.r,accent.g,accent.b,pressed?.6f:.25f),
                    new Color(pale.r,pale.g,pale.b,pressed?.42f:.18f));
                Color ink=new Color(accent.r,accent.g,accent.b,pressed?.85f:.60f);
                Ellipse(new Rect(-11,-1,22,17),ink);
                Ellipse(new Rect(-17,-13,8,11),ink);Ellipse(new Rect(-9,-21,8,12),ink);
                Ellipse(new Rect(1,-21,8,12),ink);Ellipse(new Rect(10,-13,8,11),ink);
                if(PartyEnabled)Text(new Rect(13,11,13,14),(slot+1).ToString(),10,accents[slot],true,TextAnchor.MiddleCenter);
            }
            GUI.color=Color.white;GUI.matrix=canvas;
        }
    }
}
