using System;
using FlipRace.Core;
using UnityEngine;
namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        bool pendingBot;
        BotDifficulty pendingBotDifficulty = BotDifficulty.Novice;
        BotRaceController botController;
        BotPlanShuffle botShuffle;
        uint botSessionSeed;
        long botAttemptSequenceIndex;
        public uint BotSessionSeed { get { return botSessionSeed; } }
        public long BotAttemptSequenceIndex { get { return botAttemptSequenceIndex; } }
        BotPlan NextBotPlan(Track track, FlightSettings flight, BotDifficulty difficulty)
        {
            int[][] groups = BotPlanCatalog.SignatureGroups(track.Id, flight, difficulty);
            if (botShuffle == null)
            {
                botSessionSeed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
                botShuffle = new BotPlanShuffle(botSessionSeed);
            }
            string profile = flight.FirstAction == FirstAction.Jump ? "Chosen" : "FastFlip";
            string key = track.Id + "/" + profile + "/" + difficulty;
            BotPlanChoice choice = botShuffle.Next(key, groups);
            botAttemptSequenceIndex = choice.SequenceIndex;
            return BotPlanCatalog.SelectAt(track.Id, flight, difficulty, choice.PlanIndex, choice.AttemptSeed);
        }
        static readonly string[] BotLevelNames = { "Новичок", "Любитель", "Уверенный", "Сильный", "Мастер" };
        public bool BotEnabled { get { return Game != null && Game.IsBot; } }
        public int SelectedBotDifficulty { get { return (int)(BotEnabled ? Game.BotDifficulty : pendingBotDifficulty); } }
        public int SelectedBotSeed { get { return BotEnabled ? Game.BotSeed : 0; } }
        public int BotInputCursor { get { return BotActionCursor; } }
        public int BotActionCursor { get { return botController == null ? 0 : botController.Cursor; } }
        public Rect BotMenuButtonRect { get { MeasureFlightMenu(); return ModeChoiceRect(2); } }
        Rect ModeChoiceRect(int option)
        { float cw=(flightPanel.width-40)/3; return new Rect(flightPanel.x+16+option*(cw+4),flightPanel.y+210,cw,28); }
        public Rect BotDifficultyRect(int option)
        { MeasureFlightMenu(); float cw=(flightPanel.width-48)/5; return new Rect(flightPanel.x+16+option*(cw+4),flightPanel.y+63,cw,44); }
        Rect BotPersonalArea(int slot)
        { Rect a=SoloOverlayRect; return slot==1?new Rect(a.x,a.y,a.width,Mathf.Max(70,a.height-140)):new Rect(a.x,a.yMax-140,a.width,140); }
        public Rect BotCharacterRect(int hero)
        { Rect a=BotPersonalArea(0);float w=Mathf.Min(a.width-16,560),cw=(w-8)/3;float top=a.y+Mathf.Max(0,(a.height-140)/2);return new Rect(a.center.x-w/2+hero%3*(cw+4),top+hero/3*48,cw,44); }
        public Rect BotStartRect { get { Rect a=BotPersonalArea(0);return new Rect(a.center.x-110,BotCharacterRect(0).y+96,220,44); } }
        public Rect BotRetryRect { get { Rect a=BotPersonalArea(0);return new Rect(a.center.x-214,a.yMax-45,210,44); } }
        public Rect BotTrackChoiceRect { get { Rect r=BotRetryRect;return new Rect(r.xMax+8,r.y,r.width,r.height); } }
        void EnterBotMenu()
        {
            if(flightSelection.IsCustom)return;
            pendingBot=true;pendingSolo=pendingParty=false;
            if(!RaceTracks.IsFull(pendingFlightTrack))pendingFlightTrack=RaceTracks.GetAt(0).Id;
            flightPage=8;BlockGameplayInput();
        }
        Rect ModeLegacyRect { get { return new Rect(flightPanel.xMax-160,flightPanel.y+39,144,22); } }
        void DrawModeChoices()
        {
            if(flightPage!=5)FlightButton(ModeLegacyRect,"Прежние виды ›",false,true,9);
            FlightButton(ModeChoiceRect(0),"Один ›",pendingSolo,true,10);
            FlightButton(ModeChoiceRect(1),"Компания ›",pendingParty,true,10);
            FlightButton(BotMenuButtonRect,flightSelection.IsCustom?"С ботом · готовый профиль":"С ботом ›",pendingBot,!flightSelection.IsCustom,9);
        }
        bool ModeChoicePointer(Vector2 p)
        {
            if(flightPage!=5 && ModeLegacyRect.Contains(p)){pendingBot=pendingSolo=pendingParty=false;flightPage=5;BlockGameplayInput();return true;}
            if(ModeChoiceRect(0).Contains(p)){pendingBot=false;EnterSoloMenu();return true;}
            if(ModeChoiceRect(1).Contains(p)){pendingBot=pendingSolo=false;pendingParty=true;flightPage=6;BlockGameplayInput();return true;}
            if(BotMenuButtonRect.Contains(p)){EnterBotMenu();return true;}
            return false;
        }
        void BotMenuPointer(Vector2 p)
        {
            for(int i=0;i<5;i++)if(BotDifficultyRect(i).Contains(p)){pendingBotDifficulty=(BotDifficulty)i;return;}
            for(int i=0;i<2;i++)if(PartyCameraRect(i).Contains(p)){pendingPartyCamera=(PartyCamera)i;return;}
        }
        void DrawBotChoices()
        {
            Text(new Rect(flightPanel.x+16,flightPanel.y+43,flightPanel.width-32,15),"УРОВЕНЬ СОПЕРНИКА",8,muted);
            for(int i=0;i<5;i++)FlightButton(BotDifficultyRect(i),BotLevelNames[i],(int)pendingBotDifficulty==i,true,10);
            Text(new Rect(flightPanel.x+16,flightPanel.y+109,flightPanel.width-32,15),"КАМЕРА",8,muted);
            FlightButton(PartyCameraRect(0),"Постоянный скролл",pendingPartyCamera==PartyCamera.Constant,true,10);
            FlightButton(PartyCameraRect(1),"За последним · без выбывания",pendingPartyCamera==PartyCamera.Lagger,true,10);
            label.wordWrap=true;
            Text(new Rect(flightPanel.x+16,flightPanel.y+175,flightPanel.width-32,32),flightSelection.IsCustom?"Боты доступны только с Основным или Быстрым переворотом. Выбери готовый профиль.":"Один человек и бот. Награды личные. Оба бегут до своего исхода.",10,muted);
            label.wordWrap=false;DrawModeChoices();DrawFlightApplyFooter();
        }
        void ResetBotRace()
        {
            int hero=Game.Players[0].CharacterIndex;
            BotPlan selectedPlan=NextBotPlan(Game.Track,Game.Flight,Game.BotDifficulty);
            Game=RaceSimulation.CreateBotRace(Game.Track,Game.Flight,selectedPlan,Game.PartyCamera,PartyStartLeft,PartyWindowWidth);
            Game.Players[0].CharacterIndex=hero;Game.Players[1].CharacterIndex=(hero+1)%CharacterRoster.Count;
            botController=new BotRaceController(Game);
        }
        void BotPointer(Vector2 p)
        {
            if(Game.Phase=="ready")
            {
                for(int h=0;h<CharacterRoster.Count;h++)if(BotCharacterRect(h).Contains(p)){SelectCharacter(0,h);Game.Players[1].CharacterIndex=(h+1)%CharacterRoster.Count;return;}
                if(BotStartRect.Contains(p))Act(0);
            }
            else if(Game.Phase=="finished")
            { if(BotRetryRect.Contains(p))Act(0);else if(BotTrackChoiceRect.Contains(p))OpenTracks(); }
            else if(arena.Contains(p))LocalRacePress(0,p);
        }
        void DrawBotField()
        {
            Rect field=SharedFieldRect;Matrix4x4 canvas=GUI.matrix;
            GUI.matrix=canvas*Matrix4x4.Translate(new Vector3(field.x,field.y,0));
            var world=artwork.DrawSharedWorld(field.width,field.height,Game,animationTime);
            var human=world.Player(0);var bot=world.Player(1);
            bool bothVisible=Game.Players[0].Status!=PlayerStatus.Eliminated && Game.Players[1].Status!=PlayerStatus.Eliminated
                && world.View.IsPlayerVisible(0) && world.View.IsPlayerVisible(1);
            bool overlap=bothVisible && Mathf.Abs(human.X-bot.X)<Mathf.Max(human.Radius,bot.Radius)*1.2f
                && Mathf.Abs(human.Y-bot.Y)<Mathf.Max(human.Radius,bot.Radius)*1.2f;
            bool samePose=overlap && Mathf.Abs(human.X-bot.X)<.5f && Mathf.Abs(human.Y-bot.Y)<.5f
                && Mathf.Abs(Mathf.DeltaAngle(human.Rotation,bot.Rotation))<1;
            // Keep exact physical origins. Draw the translucent bot first when
            // poses differ; matching poses expose both existing texture halves.
            for(int pass=0;pass<2;pass++)
            {
                int slot=1-pass;
                if(Game.Players[slot].Status==PlayerStatus.Eliminated||!world.View.IsPlayerVisible(slot))continue;
                var hero=world.Player(slot);DrawSharedMascot(slot,hero,samePose,slot==1 && overlap && !samePose);
                artwork.DrawSharedPersonalForeground(field.width,field.height,Game,slot,animationTime,hero);
            }
            // Identity labels follow the true projection on opposite sides of
            // the body. They are painted after both heroes so neither hides one.
            for(int slot=0;slot<2;slot++)
            {
                if(Game.Players[slot].Status==PlayerStatus.Eliminated||!world.View.IsPlayerVisible(slot))continue;
                var hero=world.Player(slot);float side=slot==0?1:-1;
                float bx=Mathf.Clamp(hero.X+side*20,18,field.width-18);
                float by=Mathf.Clamp(hero.Y+side*(hero.Radius*2.2f+8),9,field.height-9);
                Round(new Rect(bx-17,by-8,34,16),5,pales[slot]);Text(new Rect(bx-17,by-8,34,16),slot==0?"ТЫ":"БОТ",8,accents[slot],true,TextAnchor.MiddleCenter);
            }
            GUI.matrix=canvas;
            Fill(new Rect(0,field.y,field.x,field.height),paper);Fill(new Rect(field.xMax,field.y,width-field.xMax,field.height),paper);
            if(Game.Phase=="ready"||Game.Phase=="finished")
            {
                for(int slot=0;slot<2;slot++)Fill(BotPersonalArea(slot),new Color(.98f,.984f,.949f,.97f));
                if(Game.Phase=="ready")
                {
                    Rect a=BotPersonalArea(1);float y=a.center.y-32;
                    GUI.color=Color.white;GUI.DrawTexture(new Rect(a.center.x-100,y,64,64),MascotArtwork.Create(Game.Players[1].CharacterIndex,1));
                    Text(new Rect(a.center.x-10,y+2,210,25),"Бот · "+BotLevelNames[(int)Game.BotDifficulty],16,accents[1],true);
                    Text(new Rect(a.center.x-10,y+29,210,24),PlayerName(1)+" · Готов",11,muted);
                    for(int h=0;h<CharacterRoster.Count;h++)
                    {
                        Rect c=BotCharacterRect(h);bool chosen=Game.Players[0].CharacterIndex==h;
                        StrokeRound(c,7,chosen?accents[0]:Hex(0xd9dece),chosen?pales[0]:paper);
                        float s=Mathf.Min(40,c.width*.35f);GUI.color=Color.white;GUI.DrawTexture(new Rect(c.x+2,c.y+2,s,40),MascotArtwork.Create(h,0));
                        Text(new Rect(c.x+s+4,c.y,c.width-s-6,44),CharacterRoster.Name(h),10,ink,chosen,TextAnchor.MiddleCenter);
                    }
                    Round(BotStartRect,7,pales[0]);Text(BotStartRect,"Начать →",12,accents[0],true,TextAnchor.MiddleCenter);
                }
                else
                {
                    for(int slot=0;slot<2;slot++)
                    {
                        Rect a=BotPersonalArea(slot);var p=Game.Players[slot];float y=a.y+Mathf.Max(2,(a.height-(slot==0?140:88))/2);
                        string who=slot==0?"Ты · "+PlayerName(0):"Бот · "+BotLevelNames[(int)Game.BotDifficulty]+" · "+PlayerName(1);
                        Text(new Rect(a.x+8,y,a.width-16,24),who,15,accents[slot],true,TextAnchor.MiddleCenter);
                        Text(new Rect(a.x+8,y+27,a.width-16,22),p.Status==PlayerStatus.Finished?"Финиш · "+Number(p.FinishTime.Value,2)+" с":"Выбыл слева · "+Number(p.Distance,0)+" ед",12,ink,true,TextAnchor.MiddleCenter);
                        Text(new Rect(a.x+8,y+53,a.width-16,22),"+"+Rewards(p)+" · −"+p.SlowsEarned+CoinScore(p)+" · ударов "+p.Collisions,10,muted,false,TextAnchor.MiddleCenter);
                    }
                    Round(BotRetryRect,7,pales[0]);Text(BotRetryRect,"Ещё раз →",11,accents[0],true,TextAnchor.MiddleCenter);
                    StrokeRound(BotTrackChoiceRect,7,Hex(0xd9dece),paper);Text(BotTrackChoiceRect,"Выбрать трассу",11,ink,true,TextAnchor.MiddleCenter);
                }
            }
            else if(Game.Phase=="countdown")Text(SoloCountdownRect,Math.Ceiling(Game.CountdownRemaining).ToString(),48,accents[0],true,TextAnchor.MiddleCenter);
            else for(int slot=0;slot<2;slot++)
            {
                var p=Game.Players[slot];string state=p.Status==PlayerStatus.Eliminated?"Выбыл":p.Status==PlayerStatus.Finished?"Финиш · "+Number(p.FinishTime.Value,2)+" с":!world.View.IsPlayerVisible(slot)?"Впереди →":"+"+Rewards(p)+CoinScore(p);
                Text(new Rect(arena.x+8,slot==1?field.y-48:field.yMax+8,arena.width-120,22),(slot==0?"Ты":"Бот · "+BotLevelNames[(int)Game.BotDifficulty])+" · "+state,10,accents[slot],true);
            }
        }
    }
}