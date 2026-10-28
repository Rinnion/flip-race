using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;
using FlipRace.SharedScreenCheck.Interop;
using UnityEngine;
using Display=FlipRace.SharedScreenCheck.Interop.Display;
static class Program
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks,pointers,cases;
    static readonly List<object> Evidence=new List<object>();
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Private).Invoke(o,args);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Private).SetValue(o,value);
    static object Field(object o,string name)=>o.GetType().GetField(name,Private).GetValue(o);
    static FlipRaceGame Ui(int w,int h,bool shared,bool adjacent)
    {
        Display.width=w;Display.height=h;Display.PixelRatio=1;Display.SafeAreaOverride=null;Clock.frameCount=1;Clock.realtimeSinceStartupAsDouble=1;Devices.Clear();Prefs.Clear();
        var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        Set(ui,"lanes",new Rect[2]);Set(ui,"firstPawAt",-10f);Set(ui,"secondPawAt",-10f);Call(ui,"InitializeFlightMenu");
        var game=RaceSimulation.CreateTrial(TracksMixed.Get(),FlightPresets.Chosen);
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);
        Set(ui,"selectedTrackId",game.Track.Id);Set(ui,"lastFrame",1d);Set(ui,"sharedField",shared);Set(ui,"sharedAdjacent",adjacent);Call(ui,"Measure");return ui;
    }
    static void Pointer(FlipRaceGame ui,Vector2 p){Call(ui,"Pointer",p);pointers++;}
    static bool Inside(Rect a,Rect b)=>a.xMin>=b.xMin-.001&&a.xMax<=b.xMax+.001&&a.yMin>=b.yMin-.001&&a.yMax<=b.yMax+.001;
    static float[] R(Rect r)=>new[]{r.x,r.y,r.width,r.height};
    static void Heroes()
    {
        Check(CharacterRoster.Count==6&&RaceConfig.CharacterCount==6,"Six exact cosmetic variants");
        Check(CharacterRoster.Name(4)=="Джек"&&CharacterRoster.Name(5)=="Тыква","Exact new hero names");
        foreach(var size in new[]{(w:568,h:320),(w:844,h:390),(w:1280,h:720)})
        foreach(var mode in new[]{0,1,2})
        {
            cases++;var ui=Ui(size.w,size.h,mode!=0,mode==2);
            for(int slot=0;slot<2;slot++)
            {
                Rect area=ui.ControlAreaRect(slot),ready=ui.ReadyScreenRect(slot);
                Check(Inside(ready,area)&&ready.height>=32,"Pinned readiness inside own control area");
                var cards=new List<Rect>();
                for(int hero=0;hero<6;hero++)
                {
                    Rect card=ui.CharacterScreenRect(slot,hero);cards.Add(card);
                    Check(Inside(card,area)&&card.height>=44,"Whole large card visible at "+size+"/"+mode+"/"+slot+"/"+hero);
                    Check(!card.Overlaps(ready),"Readiness independent of six cards");
                    Check(cards.Take(cards.Count-1).All(c=>!card.Overlaps(c)),"Cards do not overlap");
                    Pointer(ui,card.center);
                    Check(ui.Game.Players[slot].CharacterIndex==hero&&!ui.Game.Players[slot].Ready,"Actual Pointer selects every hero immediately");
                    Check(!ui.Game.Players[1-slot].Ready,"Selection never readies opponent");
                }
                Evidence.Add(new{Kind="six-card-layout",Size=size,Mode=mode,Slot=slot,Area=R(area),Ready=R(ready),Cards=cards.Select(R).ToArray()});
            }
            Pointer(ui,ui.ReadyScreenRect(0).center);Check(ui.Game.Players[0].Ready&&!ui.Game.Players[1].Ready,"Actual own readiness edge");
            Pointer(ui,ui.ReadyScreenRect(1).center);Check(ui.Game.Phase=="countdown","Both fixed ready targets start countdown");
            Check(ui.Game.Players.All(p=>p.CharacterIndex==5),"Both new same heroes preserved into race");
            Check(!RaceSimulation.SetCharacter(ui.Game,0,4),"Character selection locked after readiness");
        }
        var test=RaceSimulation.CreateTrial(TracksMixed.Get(),FlightPresets.Chosen);
        Check(!RaceSimulation.SetCharacter(test,0,-1)&&!RaceSimulation.SetCharacter(test,0,6),"Out-of-roster indices rejected");
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
        {
            string baseline=null;
            for(int hero=0;hero<6;hero++)
            {
                var game=RaceSimulation.CreateTrial(TracksFull.Get(TracksFull.LongId),flight);
                Check(RaceSimulation.SetCharacter(game,0,hero)&&RaceSimulation.SetCharacter(game,1,hero),"Both slots accept hero "+hero);
                RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);RaceSimulation.Advance(game,3);
                // Identical legal edge history, every20 frames, across all cosmetic choices.
                for(int frame=0;game.Phase=="racing"&&frame<5000;frame++)
                {if(frame%20==0){RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);}RaceSimulation.Advance(game,.02);}
                string actual=JsonSerializer.Serialize(game.Players.Select(p=>new{p.Distance,p.Y,p.FinishTime,p.Collisions,p.BoostsEarned,p.SlowsEarned,p.FinishBoostsEarned}));
                if(baseline==null)baseline=actual;Check(actual==baseline,"Cosmetic choice preserves actual physics and results");
            }
        }
    }
    static int projectionFixtures,equivalenceRuns,applies,updates;
    static void NearUI(double a,double b,string why,double tolerance=.0003)
    {Check(Math.Abs(a-b)<=tolerance,why+": "+a+" != "+b);}
    static string Fingerprint(GameState game)=>JsonSerializer.Serialize(game,new JsonSerializerOptions{IncludeFields=true});
    static void Tick(FlipRaceGame ui,double dt=0)
    {Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=dt;Call(ui,"Update");updates++;}
    static void Click(FlipRaceGame ui,Vector2 p){Devices.Clear();Tick(ui);Pointer(ui,p);}
    static FlipRaceGame UiChoice(int w,int h,int choice)
    {
        var ui=Ui(w,h,false,false);
        var custom=FlightDraft.From(FlightPresets.FastFlip);custom.Repeats=AirRepeatLimit.None;custom.Jump=2;custom.Flip=4;custom.Gravity=2;custom.Speed=3;
        Prefs.SetString(FlightSettingsStore.Key,FlightSettingsStore.Encode(custom));Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,choice);Call(ui,"InitializeFlightMenu");
        var selection=(FlightSelection)Field(ui,"flightSelection");var game=RaceSimulation.CreateTrial(TracksMixed.Get(),selection.Settings);
        Check(RaceSimulation.SetCharacter(game,0,4)&&RaceSimulation.SetCharacter(game,1,5),"real cosmetic selection before view setup");
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);Set(ui,"sharedAdjacent",true);Call(ui,"Measure");return ui;
    }
    static void OpenView(FlipRaceGame ui)
    {Call(ui,"OpenTracks");Check(ui.IsPaused&&ui.FlightMenuPage==0,"actual view menu opens");Click(ui,ui.ViewMenuButtonRect.center);Check(ui.FlightMenuPage==5,"actual view navigation");}
    static void ChooseView(FlipRaceGame ui,int kind,bool adjacent)
    {Click(ui,ui.ViewOptionRect(0,kind).center);Click(ui,ui.ViewOptionRect(1,adjacent?1:0).center);}
    static string ViewName(int kind)=>kind==0?"split":kind==1?"split-ghost":"shared";
    static void RetainedChoice(FlipRaceGame ui,string custom,string settings,int choice)
    {
        var selection=(FlightSelection)Field(ui,"flightSelection");Check(FlightSettingsStore.Encode(selection.Custom)==custom,"exact custom draft survives view edit");
        Check(JsonSerializer.Serialize(ui.Game.Flight,new JsonSerializerOptions{IncludeFields=true})==settings,"exact live physics survive view edit");
        Check(ui.Game.Track.Id==TracksMixed.Id&&ui.Game.Players[0].CharacterIndex==4&&ui.Game.Players[1].CharacterIndex==5,"course and both new heroes survive view edit");
        Check(Prefs.GetString(FlightSettingsStore.Key)==custom&&Prefs.GetInt(FlightSettingsStore.ActiveChoiceKey)==choice,"stored custom/profile remain unchanged");
    }
    static void Stage04Menus()
    {
        foreach(var size in new[]{(w:568,h:320),(w:1280,h:720)})foreach(int choice in new[]{10,11,-1})foreach(int kind in new[]{0,1,2}){
            cases++;var ui=UiChoice(size.w,size.h,choice);var selection=(FlightSelection)Field(ui,"flightSelection");string custom=FlightSettingsStore.Encode(selection.Custom),settings=JsonSerializer.Serialize(ui.Game.Flight,new JsonSerializerOptions{IncludeFields=true});
            string original=Fingerprint(ui.Game);OpenView(ui);ChooseView(ui,kind,false);
            Check(ui.FieldViewName=="split"&&Fingerprint(ui.Game)==original,"unapplied view changes cannot mutate live game");
            Rect panel=(Rect)Field(ui,"flightPanel"),apply=(Rect)Field(ui,"flightApply");
            for(int row=0;row<2;row++)for(int option=0;option<(row==0?3:2);option++){
                var rect=ui.ViewOptionRect(row,option);Check(Inside(rect,panel)&&rect.height>=28&&rect.yMax<apply.yMin,"all current options measured above footer");
            }
            foreach(int row in new[]{-1,2,3}){var empty=ui.ViewOptionRect(row,0);Check(empty.width==0&&empty.height==0,"removed Full row offers no target");}
            Check(ui.ViewOptionRect(0,3).width==0&&ui.ViewOptionRect(1,2).width==0,"no hidden fourth view or third seating target");
            if(kind!=2)Check((bool)Field(ui,"pendingSharedAdjacent"),"disabled seating retains remembered shared choice");
            Click(ui,((Rect)Field(ui,"flightBack")).center);Click(ui,((Rect)Field(ui,"flightBack")).center);
            Check(!ui.IsPaused&&ui.FieldViewName=="split"&&Fingerprint(ui.Game)==original,"actual cancel restores live presentation and Core state");
            OpenView(ui);Check(!(bool)Field(ui,"pendingSharedField")&&!(bool)Field(ui,"pendingSplitGhost"),"reopening discards cancelled draft");
            ChooseView(ui,kind,kind==2&&choice==11);Click(ui,((Rect)Field(ui,"flightApply")).center);applies++;
            Check(ui.FieldViewName==ViewName(kind)&&ui.SharedEnabled==(kind==2)&&ui.SplitGhostEnabled==(kind==1),"actual Apply selects exactly requested view");
            Check(ui.FieldHeightName=="half"&&ui.Game.Phase=="ready"&&ui.Game.Players.All(p=>!p.Ready&&!p.Flipping),"Apply yields clean readiness and half field");RetainedChoice(ui,custom,settings,choice);
            Rect arena=(Rect)Field(ui,"arena");var lanes=(Rect[])Field(ui,"lanes");var field=ui.SharedFieldRect;
            NearUI(field.height,lanes[0].height,"shared height always old lane height");NearUI(field.width,arena.width,"shared width preserved");NearUI(field.center.y,arena.center.y,"shared half vertically centred");
            for(int slot=0;slot<2;slot++){
                Check(Inside(ui.ReadyScreenRect(slot),ui.ControlAreaRect(slot)),"ready remains local after view switch");
                for(int hero=0;hero<6;hero++)Check(Inside(ui.CharacterScreenRect(slot,hero),ui.ControlAreaRect(slot)),"all six character choices remain local");
            }
            Evidence.Add(new{Kind="stage04-menu",Size=size,Choice=choice,View=ui.FieldViewName,Field=R(field),Controls=new[]{R(ui.ControlAreaRect(0)),R(ui.ControlAreaRect(1))}});
            // These are explicitly legacy preference fixtures, never gameplay
            // position or motion injection. Both migration entry points are real.
            string before=Fingerprint(ui.Game);bool remembered=(bool)Field(ui,"sharedAdjacent");Set(ui,"sharedFullHeight",true);Set(ui,"pendingSharedFullHeight",true);Call(ui,"Measure");
            Check(!(bool)Field(ui,"sharedFullHeight")&&!(bool)Field(ui,"pendingSharedFullHeight"),"Measure migrates both legacy Full flags");
            Check((bool)Field(ui,"sharedAdjacent")==remembered&&ui.FieldViewName==ViewName(kind)&&Fingerprint(ui.Game)==before,"height migration preserves every other live preference and Core state");RetainedChoice(ui,custom,settings,choice);
            Set(ui,"sharedFullHeight",true);Set(ui,"pendingSharedFullHeight",true);Call(ui,"InitializeFlightMenu");
            Check(!(bool)Field(ui,"sharedFullHeight")&&!(bool)Field(ui,"pendingSharedFullHeight"),"initialization migrates legacy Full flags");RetainedChoice(ui,custom,settings,choice);
            // Apply is also checked against injected legacy pending true.
            OpenView(ui);ChooseView(ui,2,true);Set(ui,"pendingSharedFullHeight",true);Click(ui,((Rect)Field(ui,"flightApply")).center);applies++;
            Check(ui.SharedEnabled&&ui.SeatingName=="adjacent"&&ui.FieldHeightName=="half"&&!(bool)Field(ui,"pendingSharedFullHeight"),"actual Apply clamps legacy pending Full to Half");RetainedChoice(ui,custom,settings,choice);
        }
        var legacy=Ui(568,320,false,false);typeof(FlipRaceGame).GetProperty("Game").SetValue(legacy,RaceSimulation.CreateGame());Set(legacy,"splitGhost",true);Set(legacy,"sharedField",true);Call(legacy,"Measure");
        Check(legacy.FieldViewName=="split"&&!legacy.SplitGhostEnabled&&!legacy.SharedEnabled,"legacy nontrial course remains original split");
    }
    static CorridorArtwork Art()
    {var art=(CorridorArtwork)RuntimeHelpers.GetUninitializedObject(typeof(CorridorArtwork));Set(art,"jumpRotation",new float[2]);Set(art,"jumpRotationTime",new[]{-1f,-1f});return art;}
    static CorridorArtwork.Geometry OwnGeometry(float w,float h)
    {
        float top=Math.Clamp(h*.095f,13,25),sy=(h-2*top)/240,anchor=Math.Clamp(w*.19f,62,195);
        return new CorridorArtwork.Geometry{Top=top,Bottom=h-top,ScaleY=sy,ScaleX=(w-anchor-20)/Math.Max(1080,w*1.28f),Anchor=anchor,Radius=Math.Clamp(14*sy,7.5f,25)};
    }
    static void Stage04Projection()
    {
        foreach(var size in new[]{(w:560f,h:132f),(w:1190f,h:193f),(w:1300f,h:300f)})foreach(int observer in new[]{0,1})foreach(double gap in new[]{-5000d,-300,-1,0,1,300,5000}){
            projectionFixtures++;var game=RaceSimulation.CreateTrial(TracksMixed.Get(),FlightPresets.FastFlip);
            // Synthetic world coordinates only: this block verifies camera
            // projection, not a claim of reachable gameplay or player routes.
            game.Players[observer].Distance=1000;game.Players[1-observer].Distance=1000+gap;game.Players[1-observer].Y=.25;game.Players[observer].Y=.75;
            game.Players[0].CharacterIndex=game.Players[1].CharacterIndex=5;
            string before=Fingerprint(game);var view=SplitGhostView.Create(size.w,size.h,game,observer);var own=OwnGeometry(size.w,size.h);
            double expected=own.Anchor+gap*own.ScaleX;NearUI(view.CanonicalX,expected,"own-camera real-gap formula",.001);
            NearUI(view.X,observer==0?size.w-expected:expected,"exactly one upper local X reflection",.001);NearUI(view.Y,own.Top+67*own.ScaleY,"physical Y converted once, no opponent-lane Y copy");
            NearUI(view.Gap,gap,"signed physical gap retained");Check(view.MirrorX==(observer==0),"mirror belongs to observing lane");
            Check(view.IsVisible==(view.X>=0&&view.X<=size.w),"offscreen condition follows real projected point");
            if(Math.Abs(gap)==5000)Check(view.X<0||view.X>size.w,"offscreen ghost position never glued to edge");
            var ui=Ui(568,320,false,false);typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);var lane=((Rect[])Field(ui,"lanes"))[observer];var measured=SplitGhostView.Create(lane.width,lane.height,game,observer);var screen=ui.GhostScreenPoint(observer);
            NearUI(screen.x,observer==0?lane.xMax-measured.X:lane.x+measured.X,"outer lane rotation composes with local mirror");
            NearUI(screen.y,observer==0?lane.yMax-measured.Y:lane.y+measured.Y,"outer lane alone converts physical Y to screen");
            Check(Fingerprint(game)==before,"pure projection and screen mapping never mutate Core");
        }
        foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(int observer in new[]{0,1})foreach(string state in new[]{"floor","ceiling","stone-floor","stone-ceiling","air","recover","finished"}){
            projectionFixtures++;var game=RaceSimulation.CreateTrial(TracksMixed.Get(),f);game.Phase="racing";
            // Synthetic pose fixtures only. Geometry must match the ordinary
            // body's existing support and gait, including a full overlap.
            foreach(var p in game.Players){p.Distance=1200;p.CharacterIndex=5;p.Y=state.Contains("ceiling")?0:1;p.Support=SupportKind.Wall;p.SupportSide=state.Contains("ceiling")?0:1;}
            var other=game.Players[1-observer];if(state.StartsWith("stone")){other.Support=SupportKind.Stone;other.Y=state=="stone-floor"?164d/212:48d/212;}
            if(state=="air"){other.Support=SupportKind.None;other.Y=.3;other.Flipping=true;other.VerticalSpeed=-300;other.Gravity=-1;other.FlipElapsed=.3;}
            if(state=="recover"){other.Recovering=.3;other.RecoveryElapsed=.2;}
            if(state=="finished")other.FinishTime=33.05;
            var own=OwnGeometry(560,132);string before=Fingerprint(game);var g=Art().OpponentGeometry(560,132,game,observer,1,own);
            bool ceiling=other.Support!=SupportKind.None?other.SupportSide==0:other.Y<.5;double y=13+RaceSimulation.GetPlayerCenterY(other)*own.ScaleY;double radius=Math.Clamp(14*own.ScaleY,7.5f,25);
            if(other.Support==SupportKind.Stone)y+=(ceiling?1:-1)*(radius-14*own.ScaleY);
            bool running=state!="recover"&&state!="finished";if(running&&!other.Flipping)y+=-Math.Abs(Math.Sin(other.Distance/220*25))*radius*.075*(ceiling?-1:1);
            NearUI(g.Y,y,"ghost matches ordinary support/gait centre without overlap displacement");NearUI(g.X,observer==0?560-own.Anchor:own.Anchor,"equal-distance ghost overlaps same canonical anchor");
            NearUI(g.Radius,radius,"opponent uses same legible radius");Check(g.Running==running&&g.Recovering==(state=="recover")&&g.MirrorX==(observer==0),"real locomotion/recovery/observer reflection");
            NearUI(g.MotionTime,1200d/220,"opponent gait uses opponent real distance");
            double rotation=other.Flipping?(1-other.Y)*180:ceiling?180:0;if(f.FirstAction==FirstAction.Jump)rotation=other.Gravity<0?180:0;if(state=="recover")rotation+=.2*14*180/Math.PI;if(observer==0)rotation=-rotation;
            NearUI(g.Rotation,rotation,"opponent actual gravity/air/recovery pose");
            Check(Fingerprint(game)==before,"pose geometry preserves entire gameplay state");
        }
        Check(!SplitGhostView.Create(80,100,RaceSimulation.CreateGame(),0).IsVisible&&!SplitGhostView.Create(560,20,RaceSimulation.CreateGame(),0).IsVisible,"degenerate layout does not fabricate a ghost");
        // Shared camera4.8.1 remains the lagger's anchor and never reframes to
        // the midpoint or scales to fit the leader, including a finished leader.
        var pair=RaceSimulation.CreateTrial(TracksMixed.Get(),FlightPresets.Chosen);pair.Players[0].Distance=1000;pair.Players[1].Distance=7000;pair.Players[1].FinishTime=33.05;
        var shared=SharedFieldView.Create(560,132,pair);NearUI(shared.PlayerX(0),shared.AnchorX,"unchanged shared lagger anchor");NearUI(shared.PlayerX(1)-shared.PlayerX(0),6000*shared.ScaleX,"unchanged real leader gap");Check(!shared.IsPlayerVisible(1)&&shared.PlayerX(1)>560,"finished leader may remain offscreen");
    }
    static void Stage04Equivalence()
    {
        foreach(int choice in new[]{10,11,-1}){
            List<string> baseline=null;
            for(int kind=0;kind<4;kind++){
                equivalenceRuns++;var ui=UiChoice(568,320,choice);var selection=(FlightSelection)Field(ui,"flightSelection");
                var fixture=new Track(2200,new[]{new Obstacle{Id="actual-stone",Kind="floor",X=300,Width=64,Height=48}}){Id=TracksMixed.Id,RiskZones=new[]{new RiskZone{Id="air-coin",Kind="coin",X=325,Y=150,Radius=8},new RiskZone{Id="floor-coin",Kind="coin",X=520,Y=226,Radius=8},new RiskZone{Id="slow",Kind="negative",X=800,Y=226,Radius=8},new RiskZone{Id="instant",Kind="instant",X=1100,Y=226,Radius=8},new RiskZone{Id="finish",Kind="finish",X=1950,Y=226,FinishWidth=28,FinishHeight=8}}};
                var game=RaceSimulation.CreateTrial(fixture,selection.Settings);Check(RaceSimulation.SetCharacter(game,0,5)&&RaceSimulation.SetCharacter(game,1,5),"same new hero selected through Core before actual witness");
                typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);Set(ui,"sharedField",kind>=2);Set(ui,"splitGhost",kind==1);Set(ui,"sharedAdjacent",kind==3);
                Devices.Keys.Add(KeyCode.A);Devices.Keys.Add(KeyCode.L);Tick(ui);Devices.Clear();Check(game.Phase=="countdown"&&game.Players.All(p=>!p.Flipping),"actual Update keyboard readiness in each view");
                while(game.Phase=="countdown")Tick(ui,Math.Min(.1,game.CountdownRemaining));
                var snapshots=new List<string>();var art=Art();
                foreach(double target in new[]{0d,.901,1.31,1.73,2.3,3.0,4.5,9.71,10.5,11.5}){
                    while(game.Phase=="racing"&&target-game.Elapsed>1e-9)Tick(ui,Math.Min(.05,target-game.Elapsed));
                    if(target==.901){Devices.Keys.Add(KeyCode.L);Tick(ui);Devices.Clear();}
                    string before=Fingerprint(game);var lanes=(Rect[])Field(ui,"lanes");
                    for(int observer=0;observer<2;observer++){
                        var view=SplitGhostView.Create(lanes[observer].width,lanes[observer].height,game,observer);var point=ui.GhostScreenPoint(observer);
                        var g=art.OpponentGeometry(lanes[observer].width,lanes[observer].height,game,observer,(float)game.Elapsed,OwnGeometry(lanes[observer].width,lanes[observer].height));
                        Check(!double.IsNaN(view.X)&&!float.IsNaN(point.x)&&!float.IsNaN(g.Y),"real trajectory produces finite observer geometry");
                    }
                    Check(Fingerprint(game)==before,"read-only ghost projections cannot collect, collide or change effects");snapshots.Add(before);
                }
                Check(game.Phase=="finished"&&game.Players.All(p=>p.FinishTime.HasValue),"actual input-stream equivalence reaches both finishes");
                if(baseline==null)baseline=snapshots;else for(int i=0;i<snapshots.Count;i++)Check(snapshots[i]==baseline[i],"identical actual Update stream retains full Core fingerprint across split/ghost/shared at sample"+i);
                Evidence.Add(new{Kind="stage04-actual-update-equivalence",Choice=choice,View=kind,Winner=game.Winner,Players=game.Players.Select(p=>new{p.Distance,p.FinishTime,p.Collisions,p.CoinsEarned,p.BoostsEarned,p.SlowsEarned,p.FinishBoostElapsed}).ToArray()});
            }
        }
    }
    static void Stage04(){Stage04Menus();Stage04Projection();Stage04Equivalence();}
    static int pawCases,pawEquivalenceRuns;
    static Touch TouchAt(int id,Vector2 p,TouchPhase phase)
    {return new Touch{fingerId=id,position=new Vector2(p.x*Display.PixelRatio,Display.height-p.y*Display.PixelRatio),phase=phase};}
    static Track PawTrack()=>new Track(2200,new[]{new Obstacle{Id="paw-stone",Kind="floor",X=600,Width=64,Height=48}}){Id=TracksMixed.Id,RiskZones=new[]{new RiskZone{Id="paw-coin-one",Kind="coin",X=200,Y=226,Radius=8},new RiskZone{Id="paw-coin-two",Kind="coin",X=500,Y=226,Radius=8},new RiskZone{Id="paw-slow",Kind="negative",X=800,Y=226,Radius=8},new RiskZone{Id="paw-instant",Kind="instant",X=1100,Y=226,Radius=8},new RiskZone{Id="paw-finish",Kind="finish",X=1950,Y=226,FinishWidth=28,FinishHeight=8}}};
    static FlipRaceGame PawUi(int w,int h,int choice,int kind,float dpr)
    {
        var ui=UiChoice(w,h,choice);var selection=(FlightSelection)Field(ui,"flightSelection");var game=RaceSimulation.CreateTrial(PawTrack(),selection.Settings);
        Check(RaceSimulation.SetCharacter(game,0,4)&&RaceSimulation.SetCharacter(game,1,5),"real hero setup before paw witness");typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);
        Set(ui,"sharedField",kind>=2);Set(ui,"splitGhost",kind==1);Set(ui,"sharedAdjacent",kind==3);Set(ui,"animationTime",10f);
        var ready0=ui.ReadyScreenRect(0);var ready1=ui.ReadyScreenRect(1);
        Display.width=Mathf.RoundToInt(w*dpr);Display.height=Mathf.RoundToInt(h*dpr);Display.PixelRatio=dpr;
        // Native safeArea is bottom-left device pixels; CSS safe insets are
        // left12/right16/top14/bottom10, deliberately asymmetric.
        Display.SafeAreaOverride=new Rect(12*dpr,10*dpr,(w-28)*dpr,(h-24)*dpr);Call(ui,"Measure");
        Check(R(ui.ReadyScreenRect(0)).SequenceEqual(R(ready0))&&R(ui.ReadyScreenRect(1)).SequenceEqual(R(ready1)),"DPR/safe-area paws never move fixed readiness targets");
        return ui;
    }
    static void ReadyThroughUpdate(FlipRaceGame ui)
    {
        Check(!ui.PawsVisible&&ui.Game.Phase=="ready","paws hidden during real hero choice");
        Click(ui,ui.CharacterScreenRect(0,4).center);Click(ui,ui.CharacterScreenRect(1,5).center);
        Check(ui.Game.Players.All(p=>!p.Flipping&&!p.Ready),"invisible paws do not intercept hero choices");
        Devices.Touches=new[]{TouchAt(1,ui.ReadyScreenRect(0).center,TouchPhase.Began),TouchAt(2,ui.ReadyScreenRect(1).center,TouchPhase.Began)};Tick(ui);Devices.Clear();
        Check(ui.Game.Phase=="countdown"&&!ui.PawsVisible&&ui.Game.Players.All(p=>!p.Flipping),"two real ready edges produce countdown, no jump");Header501(ui,ui.SharedEnabled?(ui.SeatingName=="adjacent"?3:2):ui.SplitGhostEnabled?1:0);
        Devices.Touches=new[]{TouchAt(1,ui.PawScreenRect(0).center,TouchPhase.Began),TouchAt(2,ui.PawScreenRect(1).center,TouchPhase.Began)};Tick(ui);Devices.Clear();
        Check(ui.Game.Players.All(p=>!p.Flipping)&&(float)Field(ui,"firstPawAt")==-10&&(float)Field(ui,"secondPawAt")==-10,"countdown paw locations cannot act or flash");
        while(ui.Game.Phase=="countdown")Tick(ui,Math.Min(.1,ui.Game.CountdownRemaining));
        Check(ui.PawsVisible,"paws appear only on actual racing phase");
    }
    static void FirstActionState(FlipRaceGame ui,int slot,string why)
    {
        var p=ui.Game.Players[slot];var f=ui.Game.Flight;int expected=f.FirstAction==FirstAction.Jump?1:-1;
        Check(p.Flipping&&p.Gravity==expected&&p.AirFlipAvailable,why+" exactly one action, no unintended air repeat");
    }
    static void Header501(FlipRaceGame ui,int kind)
    {
        Call(ui,"Measure");Rect bar=ui.SessionBarRect,arena=(Rect)Field(ui,"arena");string before=Fingerprint(ui.Game);
        Check(ui.SessionCentered==(kind!=3),"session strip centered only for opposite-side seating");
        if(kind==3)Check(bar.yMax<=arena.yMin+.001,"adjacent same-side seating retains edge toolbar");
        else if(kind>=2&&(ui.Game.Phase=="racing"||ui.Game.Phase=="countdown")) {
            NearUI(bar.yMax,ui.SharedFieldRect.yMin,"shared active toolbar immediately above world");
            Check(!bar.Overlaps(ui.SharedFieldRect),"toolbar cannot occlude active shared geometry");
            for(int slot=0;slot<2;slot++) {
                Check(!bar.Overlaps(ui.PawScreenRect(slot)),"active shared toolbar clear of viewport paws");
                Check(!ui.SharedFieldRect.Overlaps(ui.PawScreenRect(slot)),"active shared world clear of viewport paws");
            }
        } else {
            var a=ui.ControlAreaRect(0);var b=ui.ControlAreaRect(1);
            Check(bar.yMin>=a.yMax-.001&&bar.yMax<=b.yMin+.001,"toolbar between upper/lower rows");
            for(int slot=0;slot<2;slot++) {
                Check(!bar.Overlaps(ui.ReadyScreenRect(slot)),"toolbar cannot cover readiness");
                for(int hero=0;hero<6;hero++)Check(!bar.Overlaps(ui.CharacterScreenRect(slot,hero)),"toolbar cannot cover hero cards");
            }
        }
        foreach(string name in new[]{"soundRect","pauseRect","fullscreenRect"})Check(Inside((Rect)Field(ui,name),bar),"session tools remain inside strip "+name);
        NearUI(ui.SharedFieldRect.height,((Rect[])Field(ui,"lanes"))[0].height,"legacy shared world height preserved");
        if(ui.SharedEnabled&&ui.SessionCentered&&ui.Game.Phase=="countdown")for(int slot=0;slot<2;slot++) {
            Rect region=ui.ControlAreaRect(slot);bool upper=slot==0;
            float local=(float)Call(ui,"SharedCountdownCenter",slot),physical=upper?region.yMax-local:region.yMin+local;
            var glyph=new Rect(region.x,physical-30,region.width,60);
            Check((float)Call(ui,"SharedCountdownBandHeight",slot)>=60,"countdown band fits 60px glyph");
            Check(upper?glyph.yMin>=region.yMin-.001&&glyph.yMax<=bar.yMin+.001:
                glyph.yMin>=ui.SharedFieldRect.yMax-.001&&glyph.yMax<=region.yMax+.001,"countdown glyph visible above bar or below world");
        }
        Check(Fingerprint(ui.Game)==before,"session layout never mutates Core");
    }
    static void Pause501(FlipRaceGame ui)
    {
        Rect cover=(Rect)Field(ui,"arena"),bar=ui.SessionBarRect;
        float titleY=(float)typeof(FlipRaceGame).GetProperty("PauseTitleY",Private).GetValue(ui);
        float subtitleY=(float)typeof(FlipRaceGame).GetProperty("PauseSubtitleY",Private).GetValue(ui);
        float iconSize=(float)typeof(FlipRaceGame).GetProperty("PauseIconSize",Private).GetValue(ui);
        var title=new Rect(cover.x+15,titleY,cover.width-30,30);
        var subtitle=new Rect(cover.x+20,subtitleY,cover.width-40,28);
        var resume=(Rect)Field(ui,"resumeRect");
        foreach(var box in new[]{title,subtitle,resume})Check(Inside(box,cover)&&!box.Overlaps(bar),"pause title/subtitle/resume visible outside toolbar");
        Check(!title.Overlaps(subtitle)&&!title.Overlaps(resume)&&!subtitle.Overlaps(resume),"pause text and resume independent");
        if(iconSize>0) {
            var icon=new Rect(cover.center.x-iconSize/2,titleY-iconSize-(ui.SessionCentered&&ui.SharedEnabled?8:14),iconSize,iconSize);
            Check(Inside(icon,cover)&&!icon.Overlaps(bar)&&!icon.Overlaps(title),"optional pause icon wholly visible outside toolbar");
        }
    }
    static void Toolbar501(FlipRaceGame ui,int kind)
    {
        Header501(ui,kind);string before=Fingerprint(ui.Game);
        Click(ui,((Rect)Field(ui,"soundRect")).center);Check(Fingerprint(ui.Game)==before,"sound toolbar tap cannot act");
        Click(ui,((Rect)Field(ui,"pauseRect")).center);Check(ui.IsPaused&&Fingerprint(ui.Game)==before,"pause toolbar tap cannot act");Pause501(ui);
        Call(ui,"Resume");Devices.Clear();Tick(ui);
        before=Fingerprint(ui.Game);Rect bar=ui.SessionBarRect;
        Click(ui,new Vector2(bar.xMin+2,bar.center.y));Check(Fingerprint(ui.Game)==before,"toolbar label/background tap cannot act");
    }
    static void PawBounds(FlipRaceGame ui,int w,int h,int kind)
    {
        Rect safe=new Rect(12,14,w-28,h-24);var first=ui.PawScreenRect(0);var second=ui.PawScreenRect(1);
        Check(!first.Overlaps(second),"two paw targets never overlap");
        for(int slot=0;slot<2;slot++){
            Rect area=ui.ControlAreaRect(slot),r=ui.PawScreenRect(slot);NearUI(r.width,56,"56 CSS pixel paw width");NearUI(r.height,56,"56 CSS pixel paw height");Check(Inside(r,safe),"entire paw lies inside viewport physical safe area");
            bool upper=slot==0&&kind!=3;float inset=w>h&&h<=600?40:8;NearUI(slot==0?r.xMin:r.xMax,slot==0?safe.xMin+inset:safe.xMax-inset,"FIX492 compact40/desktop8 horizontal inset");
            NearUI(upper?r.yMin:r.yMax,upper?safe.yMin+8:safe.yMax-8,"physical top-left for opposite upper, bottom-right otherwise");
        }
    }
    static void Stage05Paws()
    {
        foreach(var size in new[]{(w:568,h:320),(w:800,h:360),(w:960,h:432),(w:1280,h:720)})foreach(int kind in new[]{0,1,2,3})foreach(float dpr in new[]{1f,2f,2.5f,3f})foreach(int choice in new[]{10,11}){
            pawCases++;var ui=PawUi(size.w,size.h,choice,kind,dpr);PawBounds(ui,size.w,size.h,kind);Header501(ui,kind);
            // Actual menu keys are blocked; no hypothetical GUI paw button is
            // inserted, and no gameplay position/timer is assigned by the test.
            OpenView(ui);string menuState=Fingerprint(ui.Game);Devices.Keys.Add(KeyCode.A);Devices.Keys.Add(KeyCode.L);Tick(ui);Devices.Clear();Check(!ui.PawsVisible&&Fingerprint(ui.Game)==menuState,"menu blocks A/L and hides paws");Call(ui,"CloseRules");Devices.Clear();Tick(ui);
            ReadyThroughUpdate(ui);Header501(ui,kind);Toolbar501(ui,kind);float visual=(float)Field(ui,"animationTime");
            var p0=ui.PawScreenRect(0).center;var p1=ui.PawScreenRect(1).center;
            Devices.Touches=new[]{TouchAt(7,p0,TouchPhase.Began),TouchAt(9,p1,TouchPhase.Began)};Devices.MouseDown=Devices.MouseHeld=true;Devices.mousePosition=new Vector3(p1.x*dpr,Display.height-p1.y*dpr,0);Tick(ui);Devices.Clear();
            FirstActionState(ui,0,"simultaneous paw slot0");FirstActionState(ui,1,"simultaneous paw slot1");NearUI((float)Field(ui,"firstPawAt"),visual,"first paw gives immediate visual-clock feedback");NearUI((float)Field(ui,"secondPawAt"),visual,"second paw gives immediate feedback independently");
            foreach(var phase in new[]{TouchPhase.Stationary,TouchPhase.Moved,TouchPhase.Ended,TouchPhase.Canceled}){
                Devices.Touches=new[]{TouchAt(7,p1,phase),TouchAt(9,p0,phase)};Tick(ui,.01);FirstActionState(ui,0,"held/crossing phase "+phase);FirstActionState(ui,1,"held/crossing phase "+phase);
                NearUI((float)Field(ui,"firstPawAt"),visual,"non-Began never refreshes first feedback");NearUI((float)Field(ui,"secondPawAt"),visual,"non-Began never refreshes second feedback");
            }
            Devices.Clear();Tick(ui);Evidence.Add(new{Kind="stage05-paw-layout-multitouch",Size=size,View=kind,Dpr=dpr,Choice=choice,Controls=new[]{R(ui.ControlAreaRect(0)),R(ui.ControlAreaRect(1))},Paws=new[]{R(ui.PawScreenRect(0)),R(ui.PawScreenRect(1))}});
            // A fresh real round isolates crossing into an untouched opponent's
            // region, then background and keyboard compatibility.
            ui=PawUi(size.w,size.h,choice,kind,dpr);ReadyThroughUpdate(ui);p0=ui.PawScreenRect(0).center;p1=ui.PawScreenRect(1).center;visual=(float)Field(ui,"animationTime");
            Devices.Touches=new[]{TouchAt(3,p0,TouchPhase.Began)};Tick(ui);FirstActionState(ui,0,"single paw edge");Check(!ui.Game.Players[1].Flipping,"single paw never acts for opponent");
            foreach(var phase in new[]{TouchPhase.Moved,TouchPhase.Stationary,TouchPhase.Stationary,TouchPhase.Ended,TouchPhase.Canceled}){Devices.Touches=new[]{TouchAt(3,p1,phase)};Tick(ui,.01);Check(!ui.Game.Players[1].Flipping,"drag/hold across boundary never starts opponent");FirstActionState(ui,0,"cross-boundary hold no repeat");}
            Check((float)Field(ui,"secondPawAt")==-10,"cross-boundary hold never flashes untouched paw");Devices.Clear();Tick(ui);
            Vector2 background=ui.ControlAreaRect(1).center;Check(!ui.PawScreenRect(1).Contains(background),"background witness lies outside paw");Pointer(ui,background);FirstActionState(ui,1,"ordinary background edge retained");Check((float)Field(ui,"secondPawAt")==-10,"background input gives no paw-specific feedback");
            Devices.Keys.Add(KeyCode.A);Devices.Keys.Add(KeyCode.L);Tick(ui);Devices.Clear();foreach(var p in ui.Game.Players)Check(p.Gravity==(choice==10?-1:1),"A/L still perform exactly the intended air turn");
            NearUI((float)Field(ui,"firstPawAt"),visual,"keys/background never refresh paw feedback");Check((float)Field(ui,"secondPawAt")==-10,"keys never flash untouched paw");
            Call(ui,"Pause","test pause");string frozen=Fingerprint(ui.Game);Devices.Touches=new[]{TouchAt(3,p0,TouchPhase.Began),TouchAt(4,p1,TouchPhase.Began)};Tick(ui);Check(!ui.PawsVisible&&Fingerprint(ui.Game)==frozen,"paused paw locations cannot advance/act");
            Call(ui,"Resume");Devices.Touches=new[]{TouchAt(3,p0,TouchPhase.Stationary),TouchAt(4,p1,TouchPhase.Stationary)};Tick(ui);Check(Fingerprint(ui.Game)==frozen,"held resume touch never leaks an action");Devices.Clear();Tick(ui);Check(Fingerprint(ui.Game)==frozen&&!(bool)Field(ui,"gameplayInputBlocked"),"release safely clears block without stray action");
            // Real orientation measurement pauses a running game. Restoring
            // landscape and releasing preserves the same Core state.
            Display.width=Mathf.RoundToInt(320*dpr);Display.height=Mathf.RoundToInt(568*dpr);Tick(ui);Check(!ui.PawsVisible&&ui.IsPaused&&Fingerprint(ui.Game)==frozen,"phone portrait hides paws and pauses without gameplay input");Display.width=Mathf.RoundToInt(size.w*dpr);Display.height=Mathf.RoundToInt(size.h*dpr);Tick(ui);Call(ui,"Resume");Devices.Clear();Tick(ui);Check(Fingerprint(ui.Game)==frozen,"orientation return does not invent an action");
        }
        Display.SafeAreaOverride=null;
    }
    static void Fix492PawEdges()
    {
        foreach(var size in new[]{(w:568,h:320),(w:800,h:360),(w:960,h:432),(w:1280,h:720)})foreach(int kind in new[]{0,1,2,3})foreach(float dpr in new[]{1f,2.5f,3f})foreach(int choice in new[]{10,11})foreach(int slot in new[]{0,1})foreach(var point in new[]{(x:.5f,y:.5f),(x:55.5f,y:.5f),(x:.5f,y:55.5f),(x:55.5f,y:55.5f),(x:.5f,y:28f),(x:55.5f,y:28f),(x:28f,y:.5f),(x:28f,y:55.5f),(x:28f,y:28f)})
        {
            var ui=PawUi(size.w,size.h,choice,kind,dpr);ReadyThroughUpdate(ui);Rect r=ui.PawScreenRect(slot);float visual=(float)Field(ui,"animationTime");
            Vector2 p=new Vector2(r.x+point.x,r.y+point.y);
            Devices.Touches=new[]{TouchAt(41,p,TouchPhase.Began)};Tick(ui);Devices.Clear();
            FirstActionState(ui,slot,"FIX492 visible-paw interior edge gives one action");Check(!ui.Game.Players[1-slot].Flipping,"FIX492 near-border click cannot act for opponent");
            NearUI((float)Field(ui,slot==0?"firstPawAt":"secondPawAt"),visual,"FIX492 actual drawn rect and feedback hitrect agree at edges");
            Check((float)Field(ui,slot==0?"secondPawAt":"firstPawAt")==-10,"FIX492 opponent never flashes");
        }
        Display.SafeAreaOverride=null;
        Evidence.Add(new{Kind="ui501-visible-paw-edge-streams",Sizes=new[]{"568x320","800x360","960x432","1280x720"},Dpr=new[]{1f,2.5f,3f},Views=4,Profiles=2,Players=2,InteriorPoints=9,Physical2400x1080=new[]{"800x360@3","960x432@2.5"}});
    }
    static void Stage05Equivalence()
    {
        foreach(int choice in new[]{10,11}){
            List<string> baseline=null;
            for(int kind=0;kind<4;kind++){
                pawEquivalenceRuns++;var ui=PawUi(568,320,choice,kind,1);ReadyThroughUpdate(ui);
                var snapshots=new List<string>();
                foreach(double target in new[]{0d,.06,.2,1d,3d,5d,9.9,12d}){
                    while(ui.Game.Phase=="racing"&&target-ui.Game.Elapsed>1e-9)Tick(ui,Math.Min(.05,target-ui.Game.Elapsed));
                    if(target==0||target==.06){Devices.Touches=new[]{TouchAt(7,ui.PawScreenRect(0).center,TouchPhase.Began),TouchAt(9,ui.PawScreenRect(1).center,TouchPhase.Began)};Tick(ui);Devices.Clear();}
                    snapshots.Add(Fingerprint(ui.Game));
                }
                Check(ui.Game.Phase=="finished"&&ui.Game.Players.All(p=>p.FinishTime.HasValue),"actual paw stream reaches real finishes");Header501(ui,kind);
                if(baseline==null)baseline=snapshots;else for(int i=0;i<snapshots.Count;i++)Check(snapshots[i]==baseline[i],"same actual paw actions preserve entire Core state/coin availability across four views at sample"+i);
                Evidence.Add(new{Kind="stage05-actual-paw-update-equivalence",Choice=choice,View=kind,Winner=ui.Game.Winner,Players=ui.Game.Players.Select(p=>new{p.Distance,p.FinishTime,p.Collisions,p.CoinsEarned,p.BoostsEarned,p.SlowsEarned,p.FinishBoostElapsed}).ToArray()});
            }
        }
        Display.SafeAreaOverride=null;
    }
    static void Stage05(){Stage05Paws();Fix492PawEdges();Stage05Equivalence();}
    static int Main()
    {
        try{Heroes();Stage04();Stage05();Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,cases,pointers,projectionFixtures,equivalenceRuns,applies,updates,pawCases,pawEquivalenceRuns,Evidence,scope="Production Measure/Pointer/Core retained. Native Screen/Input/Time/PlayerPrefs and selection texture acquisition use existing explicit managed shims; native pixels and device timing are separate."},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
