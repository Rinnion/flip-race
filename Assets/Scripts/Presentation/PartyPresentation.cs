using System;
using FlipRace.Core;
using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        public const double PartyWindowWidth = 1520, PartyStartLeft = -288.8;
        bool pendingParty;
        int pendingPartyCount = 4;
        PartyCamera pendingPartyCamera;
        public bool PartyEnabled { get { return Game != null && Game.IsParty; } }
        public Rect PartyMenuButtonRect { get { MeasureFlightMenu(); return ModeChoiceRect(1); } }
        public Rect PartyCountRect(int option)
        { MeasureFlightMenu(); float cw=(flightPanel.width-40)/3; return new Rect(flightPanel.x+16+option*(cw+4),flightPanel.y+61,cw,44); }
        public Rect PartyCameraRect(int option)
        { MeasureFlightMenu(); float cw=(flightPanel.width-36)/2; return new Rect(flightPanel.x+16+option*(cw+4),flightPanel.y+126,cw,44); }
        public Rect PartyLegacyRect { get { return ModeLegacyRect; } }
        public Rect PartyFieldRect
        {
            get
            {
                float h=lanes[0].height, inset=Mathf.Clamp(h*.095f,13,25);
                float w=Mathf.Min(arena.width,(h-2*inset)*(float)(PartyWindowWidth/RaceConfig.CorridorHeight));
                return new Rect(arena.center.x-w/2,CommonFieldTop(h),w,h);
            }
        }
        int PartyCorner(int slot)
        {
            if(slot<0||slot>=Game.Players.Length)throw new ArgumentOutOfRangeException(nameof(slot));
            return PartyCorner(slot, Game.Players.Length);
        }
        static int PartyCorner(int slot, int count) => count == 2 ? (slot == 0 ? 0 : 3) : count == 3 && slot == 2 ? 3 : slot;
        bool PartyRotated(int slot) { return PartyCorner(slot)<2; }
        Rect PartyControlArea(int slot)
        { int corner=PartyCorner(slot); float half=SessionCentered?(arena.height-session.height)/2:arena.height/2; return new Rect(arena.x+(corner%2)*arena.width/2,corner<2?arena.y:arena.yMax-half,arena.width/2,half); }
        Rect PartyScreenRect(int slot,Rect local)
        { Rect r=PartyControlArea(slot); return PartyRotated(slot)?new Rect(r.xMax-local.xMax,r.yMax-local.yMax,local.width,local.height):new Rect(r.x+local.x,r.y+local.y,local.width,local.height); }
        Rect PartyReadyRect(int slot)
        { Rect r=PartyControlArea(slot); return new Rect(3,r.height-45,r.width-6,44); }
        bool AllBotParty => PartyEnabled && Game.HumanCount == 0;
        public Rect PartyAllBotActionRect
        {
            get
            {
                if (!AllBotParty || (Game.Phase != "ready" && Game.Phase != "finished")) return default(Rect);
                int bottomRight = Game.Players.Length - 1;
                Rect ready = PartyScreenRect(bottomRight, PartyReadyRect(bottomRight));
                float w = Mathf.Min(164, arena.width * .3f);
                return new Rect(arena.center.x - w / 2, ready.y, w, ready.height);
            }
        }

        string PartyBotName(int slot) => "Бот " + (slot + 1);
        string PartyBotLevelName(int slot) => BotLevelNames[(int)Game.GetBotDifficulty(slot).Value];
        int TotalHits() { int n=0;foreach(var p in Game.Players)n+=p.Collisions;return n; }
        int TotalRewards() { int n=0;foreach(var p in Game.Players)n+=Rewards(p);return n; }
        int TotalSlows() { int n=0;foreach(var p in Game.Players)n+=p.SlowsEarned;return n; }
        void PartyPointer(Vector2 point)
        {
            if (PartyAllBotActionRect.Contains(point))
            {
                if (Game.UsesPrestartBots)
                {
                    PrestartStartPointer(-1);
                    return;
                }
                string phase = Game.Phase;
                if (RaceRunner.StartAllBotParty(Game)) Beep(tapClip);
                if (Game.Phase == "countdown")
                {
                    if (phase == "finished") round++;
                    suppressFrame = true;
                }
                return;
            }
            for(int slot=0;slot<Game.Players.Length;slot++)
            {
                if(!PartyControlArea(slot).Contains(point))continue;
                if(Game.Phase=="ready")
                {
                    if(CharacterPickerPointer(slot,point))return;
                    if(ReadyScreenRect(slot).Contains(point))Act(slot);
                }
                else if(Game.Phase=="finished") { if(ReadyScreenRect(slot).Contains(point))Act(slot); }
                else if (!Game.IsBotSlot(slot)) LocalRacePress(slot,point);
                return;
            }
        }
        void PartyMenuPointer(Vector2 point)
        {
            for(int i=0;i<3;i++)if(PartyCountRect(i).Contains(point)){pendingPartyCount=i+2;return;}
            for(int i=0;i<2;i++)if(PartyCameraRect(i).Contains(point)){pendingPartyCamera=(PartyCamera)i;return;}
            if(SoloMenuButtonRect.Contains(point)){EnterSoloMenu();return;}
            if(PartyLegacyRect.Contains(point)){pendingParty=false;flightPage=5;BlockGameplayInput();}
        }
        void DrawPartyChoices()
        {
            Text(new Rect(flightPanel.x+16,flightPanel.y+43,flightPanel.width-192,15),"УЧАСТНИКИ · ВОКРУГ ОДНОГО ЭКРАНА",8,muted);
            for(int i=0;i<3;i++)FlightButton(PartyCountRect(i),(i+2)+" места",pendingPartyCount==i+2,true,12);
            Text(new Rect(flightPanel.x+16,flightPanel.y+109,flightPanel.width-32,15),"КАМЕРА",8,muted);
            FlightButton(PartyCameraRect(0),"Постоянный скролл",pendingPartyCamera==PartyCamera.Constant,true,10);
            FlightButton(PartyCameraRect(1),"За последним",pendingPartyCamera==PartyCamera.Lagger,true,10);
            label.wordWrap=true;
            Text(new Rect(flightPanel.x+16,flightPanel.y+175,flightPanel.width-32,32),pendingPartyCamera==PartyCamera.Constant
                ? "Карта движется сама. Полностью ушёл за левый край — выбыл. Справа выбывания нет."
                : "Камера следует за последним бегущим. Выбывания за экран нет.",10,muted);
            label.wordWrap=false;
            DrawModeChoices();
            DrawFlightApplyFooter();
        }
        void DrawPartyField()
        {
            Rect field=PartyFieldRect; Matrix4x4 canvas=GUI.matrix;
            GUI.matrix=canvas*Matrix4x4.Translate(new Vector3(field.x,field.y,0));
            var world=artwork.DrawSharedWorld(field.width,field.height,Game,animationTime);
            for(int slot=0;slot<Game.Players.Length;slot++)
            {
                if(Game.Players[slot].Status==PlayerStatus.Eliminated||!world.View.IsPlayerVisible(slot))continue;
                var g=world.Player(slot);
                DrawSharedMascot(slot,g,false,false);
                artwork.DrawSharedPersonalForeground(field.width,field.height,Game,slot,animationTime,g);
                // A fixed numbered badge is readable even with identical heroes.
                float bx=Mathf.Clamp(g.X+(slot-1.5f)*13,8,field.width-8);
                float by=Mathf.Clamp(g.Y-g.Radius*2.2f-8,9,field.height-9);
                Round(new Rect(bx-7,by-7,14,14),7,pales[slot]);
                Text(new Rect(bx-7,by-7,14,14),(slot+1).ToString(),9,accents[slot],true,TextAnchor.MiddleCenter);
            }
            GUI.matrix=canvas;
            // Mask decoration outside the immutable logical window, then paint personal UI.
            Fill(new Rect(0,field.y,field.x,field.height),paper);
            Fill(new Rect(field.xMax,field.y,width-field.xMax,field.height),paper);
            for(int slot=0;slot<Game.Players.Length;slot++)
            {
                Rect region=PartyControlArea(slot);
                GUI.matrix=canvas*(PartyRotated(slot)?Matrix4x4.TRS(new Vector3(region.xMax,region.yMax,0),Quaternion.Euler(0,0,180),Vector3.one):Matrix4x4.Translate(new Vector3(region.x,region.y,0)));
                if(Game.Phase=="ready")DrawPartySelection(slot,region.width,region.height);
                else if(Game.Phase=="countdown")
                {
                    Text(new Rect(0,SharedCountdownCenter(slot)-35,region.width,70),Math.Ceiling(Game.CountdownRemaining).ToString(),48,accents[slot],true,TextAnchor.MiddleCenter);
                }
                else if(Game.Phase=="finished")DrawPartyResult(slot,region.width,region.height);
                else DrawPartyStatus(slot,region.width,region.height,world.View.IsPlayerVisible(slot));
            }
            GUI.matrix=canvas;
            Rect allBotAction = PartyAllBotActionRect;
            if (allBotAction.width > 0)
            {
                Round(allBotAction, 6, Hex(0xdbe7ca));
                Text(allBotAction, PrestartActionLabel(Game.Phase == "ready" ? "Старт →" : "Повтор →"), 13, ink, true, TextAnchor.MiddleCenter);
            }
            Fill(new Rect(arena.center.x-.5f,arena.y,1,8),Hex(0xb7beaa));
            Fill(new Rect(arena.center.x-.5f,arena.yMax-8,1,8),Hex(0xb7beaa));
        }
        void DrawPartySelection(int slot,float w,float h)
        {
            GUI.color=Color.white;GUI.DrawTexture(new Rect(0,0,w,h),veil);
            DrawCharacterPicker(slot,PartyCharacterGrid(slot),10);
            Rect ready=PartyReadyRect(slot);
            if (Game.IsBotSlot(slot))
            {
                if (AllBotParty && !PartyRotated(slot))
                {
                    float inset = PartyAllBotActionRect.width / 2 + 3;
                    if (PartyCorner(slot) == 3) ready.xMin += inset;
                    else ready.width -= inset;
                }
                Round(ready, 6, pales[slot]);
                string status = Game.UsesPrestartBots && !Game.PrestartReady ? "" : " · Готов";
                Text(ready, PartyBotName(slot) + " · " + PartyBotLevelName(slot) + status, 10, accents[slot], true, TextAnchor.MiddleCenter);
                return;
            }
            Round(ready,6,Game.Players[slot].Ready?Hex(0xdbe7ca):pales[slot]);
            Text(ready,(slot+1)+" · "+PrestartActionLabel(Game.Players[slot].Ready?"Готов · ждём остальных":"Готов →"),10,accents[slot],true,TextAnchor.MiddleCenter);
        }
        void DrawPartyStatus(int slot,float w,float h,bool visible)
        {
            var p=Game.Players[slot];string state=p.Status==PlayerStatus.Eliminated?"Выбыл · "+Number(p.Distance,0)+" ед"
                :p.Status==PlayerStatus.Finished?"Финиш · "+Number(p.FinishTime.Value,2)+" с":!visible?"Впереди →":"+"+Rewards(p)+" · −"+p.SlowsEarned+" · монеты "+p.CoinsEarned;
            float sw=Mathf.Min(w-105,255);int corner=PartyCorner(slot); float statusX=corner==1||corner==2?w-sw-4:4;Rect status=new Rect(statusX,h-25,sw,22);
            DrawSurfaceRunStatus(slot,new Rect(statusX,h-49,92,20));
            Round(status,5,new Color(.98f,.98f,.94f,.94f));
            string identity = Game.IsBotSlot(slot) ? PartyBotName(slot) : (slot+1)+" · "+PlayerName(slot);
            Text(status,identity+" · "+state,compact?8:10,accents[slot],true,TextAnchor.MiddleCenter);
        }
        void DrawPartyResult(int slot,float w,float h)
        {
            GUI.color=Color.white;GUI.DrawTexture(new Rect(0,0,w,h),veil);
            var p=Game.Players[slot];
            string title = Game.Winner is int winner ? (Game.IsBotSlot(winner) ? PartyBotName(winner) : "Игрок " + (winner + 1)) + " победил"
                : Game.Winner is string ? "Финиш вместе!" : "Все выбыли";
            string outcome=p.Status==PlayerStatus.Finished?"Финиш · "+Number(p.FinishTime.Value,2)+" с":"Выбыл · "+Number(p.Distance,0)+" ед";
            Text(new Rect(4,5,w-8,25),title,compact?15:19,ink,true,TextAnchor.MiddleCenter);
            string identity = Game.IsBotSlot(slot) ? PartyBotName(slot) + " · " + PartyBotLevelName(slot) : (slot+1)+" · "+PlayerName(slot);
            Text(new Rect(4,32,w-8,22),identity+" · "+outcome,compact?9:11,accents[slot],true,TextAnchor.MiddleCenter);
            Text(new Rect(4,56,w-8,18),"+"+Rewards(p)+" · −"+p.SlowsEarned+" · монеты "+p.CoinsEarned+" · ударов "+p.Collisions,compact?9:10,muted,false,TextAnchor.MiddleCenter);
            if (Game.IsBotSlot(slot)) return;
            Rect ready=PartyReadyRect(slot);Round(ready,6,p.Ready?Hex(0xdbe7ca):pales[slot]);
            Text(ready,PrestartActionLabel(p.Ready?"Готов · ждём остальных":"Ещё раз →"),11,accents[slot],true,TextAnchor.MiddleCenter);
        }
    }
}
