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
            public Geometry Player0, Player1, Player2, Player3;
            public Geometry Player(int index) { return index==0?Player0:index==1?Player1:index==2?Player2:Player3; }
        }

        /// <summary>One canonical world, then all personal backdrops. No player mirror or state writes.</summary>
        public SharedFieldGeometry DrawSharedWorld(float w,float h,GameState game,float time)
        {
            var view=SharedFieldView.Create(w,h,game);
            if(view.ScaleX<=0) return default(SharedFieldGeometry);
            var result=new SharedFieldGeometry {View=view,
                Player0=SharedPlayerGeometry(view,game,0,time),Player1=game.Players.Length>1?SharedPlayerGeometry(view,game,1,time):default(Geometry),
                Player2=game.Players.Length>2?SharedPlayerGeometry(view,game,2,time):default(Geometry),
                Player3=game.Players.Length>3?SharedPlayerGeometry(view,game,3,time):default(Geometry)};
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
                var phase=HiddenObstacles.GetPhase(game,obstacle);
                if(phase==HiddenObstaclePhase.Hidden)continue;
                if(phase==HiddenObstaclePhase.Warning) {
                    if(ObstacleGeometry.TryGetBand(obstacle,0,out var warningBand))
                        HiddenObstacleOutline((float)view.WorldX(warningBand.X),(float)view.WorldY(warningBand.Y),
                            (float)warningBand.Width*sx,(float)warningBand.Height*sy);
                    continue;
                }
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
            for(int i=0;i<game.Players.Length;i++)if(view.IsPlayerVisible(i)&&(!game.UsesSharedRaceCamera||game.Players[i].Status!=PlayerStatus.Eliminated))SharedPersonalBackdrop(game,i,time,result.Player(i));
            GUI.matrix=previous;GUI.color=previousColor;return result;
        }

        public Geometry OpponentGeometry(float w,float h,GameState game,int observer,float time,Geometry own)
        {
            int other=1-observer;var p=game.Players[other];
            var projection=SplitGhostView.Create(w,h,game,observer);
            var result=SharedPlayerGeometry(SharedFieldView.Create(w,h,game),game,other,time);
            result.X=(float)projection.X;result.Y=(float)projection.Y;
            // Match the ordinary sprite's actual support and gait adjustments.
            // There is no collision/overlap-specific displacement.
            bool ceiling=p.Support!=SupportKind.None?p.SupportSide==0:p.Y<.5;
            if(p.Support==SupportKind.Stone)
                result.Y+=(ceiling?1:-1)*(result.Radius-(float)RaceConfig.PlayerRadius*own.ScaleY);
            if(result.Running&&!p.Flipping)
                result.Y+=-Mathf.Abs(Mathf.Sin(result.MotionTime*25))*result.Radius*.075f*(ceiling?-1:1);
            result.MirrorX=observer==0;
            if(result.MirrorX)result.Rotation=-result.Rotation;
            result.Top=own.Top;result.Bottom=own.Bottom;result.ScaleX=own.ScaleX;result.ScaleY=own.ScaleY;result.Anchor=own.Anchor;
            return result;
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
                Radius=game.UsesSharedRaceCamera?(float)(RaceConfig.PlayerRadius*view.ScaleY):Mathf.Clamp((float)(RaceConfig.PlayerRadius*view.ScaleY),7.5f,25),Rotation=rotation,MirrorX=false,
                Top=(float)view.Top,Bottom=(float)view.Bottom,ScaleX=(float)view.ScaleX,ScaleY=(float)view.ScaleY,
                Anchor=(float)view.PlayerX(index),FinishX=(float)view.WorldX(game.Track.Length),
                Running=game.Phase=="racing"&&!p.FinishTime.HasValue&&p.Recovering<=0&&(!game.UsesSharedRaceCamera||p.Status==PlayerStatus.Running),Recovering=p.Recovering>0,
                MotionTime=(float)(p.Distance/RaceConfig.Speed),FlipProgress=flight};
        }

        void DrawSharedReward(float w,GameState game,RiskZone zone,float time,SharedFieldView view)
        {
            float x=(float)view.WorldX(zone.X),y=(float)view.WorldY(zone.Y),sy=(float)view.ScaleY;
            if(x< -30||x>w+30||!SharedFieldView.ObjectVisible(game,zone))return;
            bool available0=SharedFieldView.MarkerAvailable(game.Players[0],zone.Id);
            bool available1=game.Players.Length>1&&SharedFieldView.MarkerAvailable(game.Players[1],zone.Id);
            bool anyAvailable=available0||available1;
            for(int i=2;i<game.Players.Length;i++)anyAvailable|=SharedFieldView.MarkerAvailable(game.Players[i],zone.Id);
            float radius=Mathf.Max(6.5f,(float)(zone.Radius+4)*sy),badgeY=y-radius-8;
            if(zone.IsLanding) {
                foreach(var stone in game.Track.Obstacles)if(stone.Id==zone.ObstacleId) {
                    if(HiddenObstacles.GetPhase(game,stone)!=HiddenObstaclePhase.Solid)return;
                    bool ceiling=stone.Kind=="ceiling";
                    y=(float)view.WorldY(ceiling?stone.Height:RaceConfig.CorridorHeight-stone.Height);
                    LandingPad(x,y,(float)(zone.LandingWidth*view.ScaleX),ceiling,!anyAvailable,time);
                    badgeY=y+(ceiling?12:-12);break;
                }
            }else if(zone.IsFinish) {
                DrawFinishTarget(x,y,(float)view.ScaleX,sy);
                badgeY=y-(float)(FinishTargetGeometry.HalfHeight+FinishTargetGeometry.StrokeWidth/2)*sy-8;
            }else if(zone.IsCoin) {
                DrawCoin(x,y,radius,time);
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
            for(int i=0;i<game.Players.Length;i++) {
                float markerX=x+(i-(game.Players.Length-1)*.5f)*10;
                if(SharedFieldView.MarkerAvailable(game.Players[i],zone.Id)&&
                    FullyInside(new Rect(markerX-3.4f,badgeY-3.4f,6.8f,6.8f)))
                    DrawPersonalBadge(markerX,badgeY,i,3.4f);
            }
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
            if((game.UsesSharedRaceCamera?(geometry.X+geometry.Radius<0||geometry.X-geometry.Radius>w):(geometry.X<0||geometry.X>w))||(game.UsesSharedRaceCamera&&game.Players[playerIndex].Status==PlayerStatus.Eliminated))return;
            Color previousColor=GUI.color;
            DrawPersonalForeground(w,h,game,playerIndex,time,geometry,false,game.Phase=="finished");
            // Distinct nested contours and fixed owner badges remain visible
            // even when sprites occupy exactly the same physical point.
            float r=geometry.Radius*(playerIndex==0?1.78f:playerIndex==1?2.02f:playerIndex==2?2.26f:2.5f);
            int segments=playerIndex==0?32:playerIndex==2?3:4;
            float angle=playerIndex==3?Mathf.PI/4:0;
            for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments-Mathf.PI/2+angle,b=(i+1)*Mathf.PI*2/segments-Mathf.PI/2+angle;
                Line(geometry.X+Mathf.Cos(a)*r,geometry.Y+Mathf.Sin(a)*r,
                    geometry.X+Mathf.Cos(b)*r,geometry.Y+Mathf.Sin(b)*r,Alpha(Ink[playerIndex],.68f),1.3f);}
            float badgeX=geometry.X+(game.Players.Length==2?(playerIndex==0?-6:6):(playerIndex-(game.Players.Length-1)*.5f)*11),badgeY=Mathf.Clamp(geometry.Y-r-6,5,h-5);
            if(FullyInside(new Rect(badgeX-4,badgeY-4,8,8)))DrawPersonalBadge(badgeX,badgeY,playerIndex,4);
            GUI.color=previousColor;
        }

        public void DrawPersonalBadge(float x,float y,int playerIndex,float size=6)
        {
            Rect previousClip=clipBounds;Color previousColor=GUI.color;
            clipBounds=new Rect(x-size-1,y-size-1,size*2+2,size*2+2);
            Color ink=Ink[Mathf.Clamp(playerIndex,0,3)];
            if(playerIndex==0)Ellipse(x,y,size,size,ink);
            else if(playerIndex==1)for(float row=-size;row<=size;row+=.75f){float half=size-Mathf.Abs(row);Fill(new Rect(x-half,y+row,half*2,.9f),ink);}
            else if(playerIndex==2)for(float row=-size;row<=size;row+=.75f){float half=(row+size)*.5f;Fill(new Rect(x-half,y+row,half*2,.9f),ink);}
            else Fill(new Rect(x-size,y-size,size*2,size*2),ink);
            clipBounds=previousClip;GUI.color=previousColor;
        }
    }
}
