using System;
using FlipRace.Core;
using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        bool pendingSolo;
        public bool SoloEnabled { get { return Game != null && Game.IsSolo; } }
        public Rect SoloMenuButtonRect
        {
            get { MeasureFlightMenu(); return ModeChoiceRect(0); }
        }
        Rect SoloPartyRect { get { return ModeChoiceRect(1); } }
        Rect SoloLegacyRect { get { return ModeLegacyRect; } }
        Rect SoloOverlayRect { get { return new Rect(arena.x,session.yMax+6,arena.width,Mathf.Max(0,arena.yMax-session.yMax-6)); } }
        float SoloContentTop(float contentHeight) { Rect r=SoloOverlayRect; return r.y+Mathf.Max(2,(r.height-contentHeight)/2); }
        public Rect SoloCharacterRect(int hero)
        {
            Rect r=SoloOverlayRect; float w=Mathf.Min(r.width-16,560),cw=(w-8)/3;
            return new Rect(r.center.x-w/2+(hero%3)*(cw+4),SoloContentTop(170)+22+(hero/3)*48,cw,44);
        }
        public Rect SoloStartRect { get { Rect r=SoloOverlayRect; return new Rect(r.center.x-110,SoloContentTop(170)+126,220,44); } }
        public Rect SoloRetryRect { get { Rect r=SoloOverlayRect; float w=Mathf.Min(r.width-24,420); return new Rect(r.center.x-w/2,SoloContentTop(142)+98,(w-8)/2,44); } }
        public Rect SoloTrackChoiceRect { get { Rect r=SoloRetryRect; return new Rect(r.xMax+8,r.y,r.width,r.height); } }
        public Rect SoloCountdownRect { get { return new Rect(arena.x,(SharedFieldRect.yMax+arena.yMax)/2-30,arena.width,60); } }
        public string SoloResultTitle { get { return Game.Players[0].Status==PlayerStatus.Finished?"Трасса пройдена":"Выбыл"; } }
        void EnterSoloMenu()
        {
            pendingBot=false; pendingSolo=true; pendingParty=false;
            if(!RaceTracks.IsFull(pendingFlightTrack))pendingFlightTrack=RaceTracks.GetAt(0).Id;
            flightPage=7; BlockGameplayInput();
        }
        void SoloMenuPointer(Vector2 point)
        {
            for(int i=0;i<2;i++)if(PartyCameraRect(i).Contains(point)){pendingPartyCamera=(PartyCamera)i;return;}
            if(SoloPartyRect.Contains(point)){pendingSolo=false;pendingParty=true;flightPage=6;BlockGameplayInput();return;}
            if(SoloLegacyRect.Contains(point)){pendingBot=pendingSolo=pendingParty=false;flightPage=5;BlockGameplayInput();}
        }
        void DrawSoloChoices()
        {
            Text(new Rect(flightPanel.x+16,flightPanel.y+43,flightPanel.width-192,24),"Один герой · та же полная трасса",12,ink,true);
            Text(new Rect(flightPanel.x+16,flightPanel.y+73,flightPanel.width-32,22),PendingFlightTrackName(),11,muted);
            Text(new Rect(flightPanel.x+16,flightPanel.y+109,flightPanel.width-32,15),"КАМЕРА",8,muted);
            FlightButton(PartyCameraRect(0),"Постоянный скролл",pendingPartyCamera==PartyCamera.Constant,true,10);
            FlightButton(PartyCameraRect(1),"За героем — без выбывания",pendingPartyCamera==PartyCamera.Lagger,true,10);
            label.wordWrap=true;
            Text(new Rect(flightPanel.x+16,flightPanel.y+175,flightPanel.width-32,32),pendingPartyCamera==PartyCamera.Constant
                ?"Карта движется сама. Полностью ушёл за левый край — попытка завершена."
                :"Камера следует за героем. Можно пройти всю трассу после ошибок.",10,muted);
            label.wordWrap=false;
            DrawModeChoices();
            DrawFlightApplyFooter();
        }
        void SoloPointer(Vector2 point)
        {
            if(Game.Phase=="ready")
            {
                for(int hero=0;hero<CharacterRoster.Count;hero++)if(SoloCharacterRect(hero).Contains(point)){SelectCharacter(0,hero);return;}
                if(SoloStartRect.Contains(point))Act(0);
            }
            else if(Game.Phase=="finished")
            {
                if(SoloRetryRect.Contains(point))Act(0);
                else if(SoloTrackChoiceRect.Contains(point))OpenTracks();
            }
            else if(arena.Contains(point))LocalRacePress(0,point);
        }
        void DrawSoloField()
        {
            Rect field=SharedFieldRect; Matrix4x4 canvas=GUI.matrix;
            GUI.matrix=canvas*Matrix4x4.Translate(new Vector3(field.x,field.y,0));
            var world=artwork.DrawSharedWorld(field.width,field.height,Game,animationTime);
            if(Game.Players[0].Status!=PlayerStatus.Eliminated && world.View.IsPlayerVisible(0))
            {
                var hero=world.Player(0); DrawSharedMascot(0,hero,false,false);
                artwork.DrawSharedPersonalForeground(field.width,field.height,Game,0,animationTime,hero);
            }
            GUI.matrix=canvas;
            Fill(new Rect(0,field.y,field.x,field.height),paper);
            Fill(new Rect(field.xMax,field.y,width-field.xMax,field.height),paper);
            if(Game.Phase=="ready")DrawSoloSelection();
            else if(Game.Phase=="finished")DrawSoloResult();
            else if(Game.Phase=="countdown")Text(SoloCountdownRect,Math.Ceiling(Game.CountdownRemaining).ToString(),48,accents[0],true,TextAnchor.MiddleCenter);
            else
            {
                var p=Game.Players[0];
                DrawSurfaceRunStatus(0,new Rect(arena.x+8,field.yMax+32,92,20));
                Text(new Rect(arena.x+8,field.yMax+8,arena.width-120,22),(!world.View.IsPlayerVisible(0)?"Впереди → · ":"")+PlayerName(0)+" · "+Number(Game.Elapsed,1)+" с · +"+Rewards(p)+" · −"+p.SlowsEarned+CoinScore(p),compact?9:11,accents[0],true);
            }
        }
        void DrawSoloSelection()
        {
            Fill(SoloOverlayRect,new Color(.98f,.984f,.949f,.95f));
            Rect area=SoloOverlayRect;
            Text(new Rect(area.x,SoloContentTop(170),area.width,20),"Выбери героя",12,ink,true,TextAnchor.MiddleCenter);
            for(int hero=0;hero<CharacterRoster.Count;hero++)
            {
                Rect card=SoloCharacterRect(hero);bool chosen=Game.Players[0].CharacterIndex==hero;
                StrokeRound(card,7,chosen?accents[0]:Hex(0xd9dece),chosen?pales[0]:Hex(0xf5f6ec));
                float size=Mathf.Min(40,card.width*.35f);GUI.color=Color.white;
                GUI.DrawTexture(new Rect(card.x+2,card.center.y-size/2,size,size),MascotArtwork.Create(hero,0));
                Text(new Rect(card.x+size+4,card.y,card.width-size-6,card.height),CharacterRoster.Name(hero),compact?10:11,ink,chosen,TextAnchor.MiddleCenter);
            }
            Round(SoloStartRect,7,pales[0]);Text(SoloStartRect,"Начать →",12,accents[0],true,TextAnchor.MiddleCenter);
        }
        void DrawSoloResult()
        {
            Fill(SoloOverlayRect,new Color(.98f,.984f,.949f,.95f));Rect area=SoloOverlayRect;float top=SoloContentTop(142);var p=Game.Players[0];
            Text(new Rect(area.x+8,top,area.width-16,30),SoloResultTitle,23,ink,true,TextAnchor.MiddleCenter);
            string outcome=p.Status==PlayerStatus.Finished?"Время: "+Number(p.FinishTime.Value,2)+" с":"Полностью за левым краем · "+Number(p.Distance,0)+" ед";
            Text(new Rect(area.x+8,top+34,area.width-16,22),outcome,11,accents[0],true,TextAnchor.MiddleCenter);
            Text(new Rect(area.x+8,top+60,area.width-16,22),"+"+Rewards(p)+" · −"+p.SlowsEarned+CoinScore(p)+" · ударов "+p.Collisions,10,muted,false,TextAnchor.MiddleCenter);
            Round(SoloRetryRect,7,pales[0]);Text(SoloRetryRect,"Ещё раз →",11,accents[0],true,TextAnchor.MiddleCenter);
            StrokeRound(SoloTrackChoiceRect,7,Hex(0xd9dece),paper);Text(SoloTrackChoiceRect,"Выбрать трассу",11,ink,true,TextAnchor.MiddleCenter);
        }
    }
}