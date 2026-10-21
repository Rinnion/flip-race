using System;
using FlipRace.Core;
using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class CorridorArtwork
    {
        public struct SharedFieldGeometry
        {
            public SharedFieldView View;
            public Geometry Player0, Player1;
            public Geometry Player(int index) { return index==0?Player0:Player1; }
        }

        /// <summary>One canonical world, then both personal backdrops. No player mirror or state writes.</summary>
        public SharedFieldGeometry DrawSharedWorld(float w,float h,GameState game,float time)
        {
            var view=SharedFieldView.Create(w,h,game);
            if(view.ScaleX<=0) return default(SharedFieldGeometry);
            var result=new SharedFieldGeometry {View=view,
                Player0=SharedPlayerGeometry(view,game,0,time),Player1=SharedPlayerGeometry(view,game,1,time)};
            Matrix4x4 previous=GUI.matrix;Color previousColor=GUI.color;
            clipBounds=new Rect(0,0,w,h);
            float top=(float)view.Top,bottom=(float)view.Bottom,sx=(float)view.ScaleX,sy=(float)view.ScaleY;
            Landscape(w,h,top,bottom,view.CameraLeft,sx,time,0);
            Fill(new Rect(0,0,w,2),Alpha(Hex(0x7b9181),.35f));
            Fill(new Rect(0,h-2,w,2),Alpha(Hex(0x7b9181),.35f));
            float start=(float)view.WorldX(0)-result.Player0.Radius*1.6f;
            if(start>-20&&start<w)
                for(float y=top+8;y<bottom-8;y+=8)Line(start,y,start,Mathf.Min(y+3,bottom-8),Hex(0xd9d1be),1.5f);
            foreach(var obstacle in game.Track.Obstacles) {
                float left=(float)view.WorldX(ObstacleGeometry.Left(obstacle));
                float right=(float)view.WorldX(ObstacleGeometry.Right(obstacle));
                if(right< -35||left>w+35)continue;
                // Personal collision immunity never fades shared physical geometry.
                if(obstacle.Kind=="gate") {
                    for(int part=0;part<2;part++)if(ObstacleGeometry.TryGetBand(obstacle,part,out var band))
                        GateHalf((float)view.WorldX(band.X),(float)band.Width*sx,(float)view.WorldY(band.Y),
                            (float)band.Height*sy,part==0,false,time);
                    if(!TryLowThreshold(obstacle,out _)&&TryOffsetEntry(obstacle,out bool fromFloor))
                        GateEntryArrow(left-10,fromFloor?bottom-9:top+9,fromFloor);
                } else {
                    bool marked=false;
                    foreach(var zone in game.Track.RiskZones)if(zone.IsLanding&&zone.ObstacleId==obstacle.Id){marked=true;break;}
                    Rock(left,(float)obstacle.Width*sx,(float)obstacle.Height*sy,obstacle.Kind=="ceiling"?top:bottom,
                        obstacle.Kind=="ceiling",false,marked,obstacle.Width>=160);
                }
            }
            foreach(var zone in game.Track.RiskZones)DrawSharedReward(w,game,zone,time,view);
            float finish=(float)view.WorldX(game.Track.Length);
            if(finish<w+40&&finish> -50)Finish(finish,top,bottom,sy);
            for(int i=0;i<2;i++)if(view.IsPlayerVisible(i))SharedPersonalBackdrop(game,i,time,result.Player(i));
            GUI.matrix=previous;GUI.color=previousColor;return result;
        }

        Geometry SharedPlayerGeometry(SharedFieldView view,GameState game,int index,float time)
        {
            var p=game.Players[index];
            bool ceiling=p.Support!=SupportKind.None?p.SupportSide==0:p.Y<.5;
            float rotation=ceiling?180:0,flight=Mathf.Clamp01((float)(p.FlipElapsed/RaceConfig.FlipDuration));
            if(p.Flipping)rotation=(float)(1-p.Y)*180;
            if(game.Flight!=null&&game.Flight.Law==MotionLaw.Gravity) {
                double impulse=game.Flight.FirstAction==FirstAction.Jump?game.Flight.JumpImpulse:game.Flight.FlipImpulse;
                flight=Mathf.Clamp01((float)(Math.Abs(p.VerticalSpeed)/Math.Sqrt(impulse*impulse+
                    2*game.Flight.GravityAcceleration*(RaceConfig.CorridorHeight-2*RaceConfig.PlayerRadius))));
            }
            if(game.Flight!=null&&game.Flight.Law==MotionLaw.Linear)flight=Mathf.Clamp01((float)(1-p.Y));
            if(game.Flight!=null&&game.Flight.FirstAction==FirstAction.Jump) {
                float target=p.Gravity<0?180:0;
                if(jumpRotationTime[index]<0||p.Support!=SupportKind.None||game.Phase=="ready")jumpRotation[index]=target;
                else jumpRotation[index]=Mathf.MoveTowardsAngle(jumpRotation[index],target,Mathf.Clamp(time-jumpRotationTime[index],0,.1f)*720);
                jumpRotationTime[index]=time;rotation=jumpRotation[index];
            }else jumpRotationTime[index]=-1;
            if(p.Recovering>0)rotation+=(float)p.RecoveryElapsed*14*Mathf.Rad2Deg;
            // Keep the exact common projected centre. Gait is inside the sprite;
            // no player-specific bob or overlap offset moves this world point.
            return new Geometry {X=(float)view.PlayerX(index),Y=(float)view.PlayerY(index),
                Radius=Mathf.Clamp((float)(RaceConfig.PlayerRadius*view.ScaleY),7.5f,25),Rotation=rotation,MirrorX=false,
                Top=(float)view.Top,Bottom=(float)view.Bottom,ScaleX=(float)view.ScaleX,ScaleY=(float)view.ScaleY,
                Anchor=(float)view.PlayerX(index),FinishX=(float)view.WorldX(game.Track.Length),
                Running=game.Phase=="racing"&&!p.FinishTime.HasValue&&p.Recovering<=0,Recovering=p.Recovering>0,
                MotionTime=(float)(p.Distance/RaceConfig.Speed),FlipProgress=flight};
        }

        void DrawSharedReward(float w,GameState game,RiskZone zone,float time,SharedFieldView view)
        {
            float x=(float)view.WorldX(zone.X),y=(float)view.WorldY(zone.Y),sy=(float)view.ScaleY;
            if(x< -30||x>w+30||!SharedFieldView.ObjectVisible(game,zone))return;
            bool available0=SharedFieldView.MarkerAvailable(game.Players[0],zone.Id);
            bool available1=SharedFieldView.MarkerAvailable(game.Players[1],zone.Id);
            float radius=Mathf.Max(6.5f,(float)(zone.Radius+4)*sy),badgeY=y-radius-8;
            if(zone.IsLanding) {
                foreach(var stone in game.Track.Obstacles)if(stone.Id==zone.ObstacleId) {
                    bool ceiling=stone.Kind=="ceiling";
                    y=(float)view.WorldY(ceiling?stone.Height:RaceConfig.CorridorHeight-stone.Height);
                    LandingPad(x,y,(float)(zone.LandingWidth*view.ScaleX),ceiling,!available0&&!available1,time);
                    badgeY=y+(ceiling?12:-12);break;
                }
            }else if(zone.IsFinish) {
                float half=(float)zone.FinishHeight*sy*.5f;Color ink=Hex(0x235f58);
                GUI.color=Color.white;Texture(new Rect(x-11,y-8,11,16),finishPointer,new Rect(0,0,1,1));
                Line(x,y-half,x,y+half,ink,2);
                Line(x-3,y-half,x+3,y-half,ink,1.5f);Line(x-3,y+half,x+3,y+half,ink,1.5f);
                badgeY=y-12;
            }else if(zone.IsInstant) {
                Color ink=Hex(0x398d80);
                Ellipse(x,y,radius+5+Mathf.Sin(time*4)*1.5f,radius+5,Alpha(Hex(0x9ddccb),.32f));
                for(int i=0;i<6;i++){float a=i*Mathf.PI/3,b=(i+1)*Mathf.PI/3;
                    Line(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius,ink,1.8f);}
                Line(x+radius*.2f,y-radius*.58f,x-radius*.3f,y+radius*.06f,ink,2);
                Line(x-radius*.3f,y+radius*.06f,x+radius*.3f,y-radius*.06f,ink,2);
                Line(x+radius*.3f,y-radius*.06f,x-radius*.2f,y+radius*.58f,ink,2);
            }else {
                Color ink=zone.IsNegative?Hex(0x99587f):Hex(0xb58a35);
                Ellipse(x,y,radius+5,radius+5,Alpha(zone.IsNegative?Hex(0xe5bad7):Hex(0xf6df99),.25f));
                int segments=zone.IsNegative?4:28;
                for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments-Mathf.PI/2,b=(i+1)*Mathf.PI*2/segments-Mathf.PI/2;
                    Line(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius,ink,2);}
                Line(x-radius*.4f,y,x+radius*.4f,y,ink,1.6f);
                if(!zone.IsNegative)Line(x,y-radius*.4f,x,y+radius*.4f,ink,1.6f);
            }
            // Fixed owner slots. A consumed/pending/failed marker never returns.
            if(available0&&FullyInside(new Rect(x-8.4f,badgeY-3.4f,6.8f,6.8f)))DrawPersonalBadge(x-5,badgeY,0,3.4f);
            if(available1&&FullyInside(new Rect(x+1.6f,badgeY-3.4f,6.8f,6.8f)))DrawPersonalBadge(x+5,badgeY,1,3.4f);
        }

        void SharedPersonalBackdrop(GameState game,int index,float time,Geometry g)
        {
            var p=game.Players[index];float x=g.X,y=g.Y,r=g.Radius;
            bool ceiling=p.Support!=SupportKind.None?p.SupportSide==0:p.Y<.5;
            float shadow=ceiling?g.Top+3:g.Bottom-3;
            if(p.Support==SupportKind.Stone)shadow=g.Top+((float)RaceSimulation.GetPlayerCenterY(p)+(ceiling?-1:1)*(float)RaceConfig.PlayerRadius)*g.ScaleY;
            Ellipse(x,shadow,r*1.5f,Mathf.Max(1.5f,r*.18f),Alpha(Ink[index],p.Flipping?.05f:.16f));
            if(p.BoostRemaining>0||p.FinishBoost) {
                Color trail=Hex(0x398d80);Ellipse(x,y,r*2,r*1.8f,Alpha(Hex(0x9ddccb),.2f));
                for(int i=0;i<4;i++){float drift=(time*105+i*19)%72;
                    Line(x-r-drift,y+(i-1.5f)*r*.58f,x-r-drift-17,y+(i-1.5f)*r*.58f,Alpha(trail,(1-drift/72)*.65f),1.5f);}
            }
            if(p.SlowRemaining>0)Ellipse(x,y,r*2,r*1.8f,Alpha(Hex(0xd5a3c3),.3f));
            Ellipse(x,y,r*1.5f,r*1.5f,Alpha(Pale[index],g.Recovering?.12f:.24f));
        }

        public void DrawSharedPersonalForeground(float w,float h,GameState game,int playerIndex,float time,Geometry geometry)
        {
            if(geometry.X<0||geometry.X>w)return;
            Color previousColor=GUI.color;
            DrawPersonalForeground(w,h,game,playerIndex,time,geometry,false,game.Phase=="finished");
            // Two differently sized contours and fixed badge slots remain visible
            // even when both sprites occupy exactly the same physical point.
            float r=geometry.Radius*(playerIndex==0?1.78f:2.02f);
            int segments=playerIndex==0?32:4;
            for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments-Mathf.PI/2,b=(i+1)*Mathf.PI*2/segments-Mathf.PI/2;
                Line(geometry.X+Mathf.Cos(a)*r,geometry.Y+Mathf.Sin(a)*r,
                    geometry.X+Mathf.Cos(b)*r,geometry.Y+Mathf.Sin(b)*r,Alpha(Ink[playerIndex],.68f),1.3f);}
            float badgeX=geometry.X+(playerIndex==0?-6:6),badgeY=Mathf.Clamp(geometry.Y-r-6,5,h-5);
            if(FullyInside(new Rect(badgeX-4,badgeY-4,8,8)))DrawPersonalBadge(badgeX,badgeY,playerIndex,4);
            GUI.color=previousColor;
        }

        public void DrawPersonalBadge(float x,float y,int playerIndex,float size=6)
        {
            Rect previousClip=clipBounds;Color previousColor=GUI.color;
            clipBounds=new Rect(x-size-1,y-size-1,size*2+2,size*2+2);
            Color ink=Ink[playerIndex==0?0:1];
            if(playerIndex==0)Ellipse(x,y,size,size,ink);
            else for(float row=-size;row<=size;row+=.75f){float half=size-Mathf.Abs(row);Fill(new Rect(x-half,y+row,half*2,.9f),ink);}
            clipBounds=previousClip;GUI.color=previousColor;
        }
    }
}
