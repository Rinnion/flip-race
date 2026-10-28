using System;
using System.Collections;
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
    static int checks,cases,updates,pointers;
    static readonly List<object> Evidence=new List<object>();
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Private).Invoke(o,args);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Private).SetValue(o,value);
    static object Field(object o,string name)=>o.GetType().GetField(name,Private).GetValue(o);
    static string State(object g)=>JsonSerializer.Serialize(g,new JsonSerializerOptions{IncludeFields=true});
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<.003,message+": "+a+" != "+b);
    static bool Inside(Rect a,Rect b)=>a.xMin>=b.xMin-.003&&a.xMax<=b.xMax+.003&&a.yMin>=b.yMin-.003&&a.yMax<=b.yMax+.003;
    static float[] R(Rect r)=>new[]{r.x,r.y,r.width,r.height};
    static void Tick(FlipRaceGame ui,double dt=0){Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=dt;Call(ui,"Update");updates++;}
    static void Click(FlipRaceGame ui,Vector2 p){Devices.Clear();Tick(ui);Call(ui,"Pointer",p);pointers++;}
    static void Apply(FlipRaceGame ui)=>Click(ui,((Rect)Field(ui,"flightApply")).center);
    static Touch TouchAt(Vector2 p,TouchPhase phase)=>new Touch{fingerId=7,position=new Vector2(p.x*Display.PixelRatio,Display.height-p.y*Display.PixelRatio),phase=phase};
    static string PrefSnapshot()
    {
        var values=new SortedDictionary<string,string>();
        foreach(string name in new[]{"Strings","Ints"})foreach(DictionaryEntry entry in (IDictionary)typeof(Prefs).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null))values[name+":"+entry.Key]=entry.Value.ToString();
        return State(values);
    }
    static FlipRaceGame NewUi(bool clear=true,int choice=-1)
    {
        Devices.Clear();if(clear){Prefs.Clear();var draft=FlightDraft.From(FlightPresets.FastFlip);draft.Repeats=AirRepeatLimit.One;draft.Gravity=2;draft.Speed=3;Prefs.SetString(FlightSettingsStore.Key,FlightSettingsStore.Encode(draft));Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,choice);Prefs.SetInt("flip-race-sound",0);}
        Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=1;
        var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));Set(ui,"lanes",new Rect[2]);
        foreach(string name in new[]{"firstPawAt","secondPawAt","thirdPawAt","fourthPawAt"})Set(ui,name,-10f);
        Call(ui,"InitializeFlightMenu");var selection=(FlightSelection)Field(ui,"flightSelection");
        Game(ui,RaceSimulation.CreateTrial(RaceTracks.GetAt(1),selection.Settings));Set(ui,"selectedTrackId",ui.Game.Track.Id);Set(ui,"lastFrame",Clock.realtimeSinceStartupAsDouble);Call(ui,"Measure");return ui;
    }
    static void Game(FlipRaceGame ui,GameState game){typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);Call(ui,"Measure");}
    static void Solo(FlipRaceGame ui,PartyCamera camera)
    {
        Call(ui,"OpenTracks");Check(ui.FlightMenuPage==0,"actual OpenTracks");Click(ui,ui.ViewMenuButtonRect.center);
        if(ui.FlightMenuPage==6)Click(ui,ui.SoloMenuButtonRect.center);
        else if(ui.FlightMenuPage==5)Click(ui,ui.SoloMenuButtonRect.center);
        Check(ui.FlightMenuPage==7,"independent actual SOLO menu route");Click(ui,ui.PartyCameraRect((int)camera).center);Apply(ui);
        Check(ui.SoloEnabled&&!ui.PartyEnabled&&ui.Game.Players.Length==1&&ui.Game.Phase=="ready"&&!ui.IsPaused,"actual apply one clean solo participant");Check(ui.Game.PartyCamera==camera,"actual solo camera choice");Devices.Clear();Tick(ui);
    }
    static void Layout(FlipRaceGame ui,int w,int h)
    {
        Rect bar=ui.SessionBarRect,field=ui.SharedFieldRect,arena=(Rect)Field(ui,"arena"),safe=(Rect)Call(ui,"SafeUiArea"),paw=ui.PawScreenRect(0);
        Near(bar.yMax,field.yMin,"toolbar above geometry in every solo phase");Check(!bar.Overlaps(field),"toolbar cannot occlude world");
        Check(Inside(paw,safe),"whole SOLO paw inside safe area");Near(paw.width,56,"56 CSS width");Near(paw.height,56,"56 CSS height");Near(paw.xMax,safe.xMax-(h<=600?40:8),"bottom right safe horizontal inset");Near(paw.yMax,safe.yMax-8,"bottom safe inset");Check(!paw.Overlaps(bar)&&!paw.Overlaps(field),"paw clear of toolbar and playable world");
        foreach(string name in new[]{"soundRect","pauseRect","fullscreenRect"})Check(Inside((Rect)Field(ui,name),bar),"toolbar tool inside bar "+name);
        var view=SharedFieldView.Create(field.width,field.height,ui.Game);Near(view.ScaleX,view.ScaleY,"uniform 1520x240 projection");Near(view.CameraLeft,ui.Game.PartyCameraLeft,"Core camera boundary");Near(ui.Game.PartyWindowWidth,1520,"same logical window");
        if(ui.Game.Phase=="ready"){
            var cards=new List<Rect>();for(int hero=0;hero<6;hero++){Rect card=ui.SoloCharacterRect(hero);Check(Inside(card,arena)&&card.width>=44&&card.height>=44,"all six whole >=44 hero cards");Check(!card.Overlaps(bar)&&!card.Overlaps(ui.SoloStartRect)&&cards.All(c=>!c.Overlaps(card)),"cards independent of toolbar/start/each other");cards.Add(card);Click(ui,card.center);Check(ui.Game.Players[0].CharacterIndex==hero&&!ui.Game.Players[0].Ready,"real hero Pointer selects without readiness");}
            Check(Inside(ui.SoloStartRect,arena)&&!ui.SoloStartRect.Overlaps(bar),"start visible outside toolbar");
        }
        if(ui.Game.Phase=="countdown")Check(Inside(ui.SoloCountdownRect,arena)&&!ui.SoloCountdownRect.Overlaps(bar)&&!ui.SoloCountdownRect.Overlaps(field),"whole countdown glyph outside world and toolbar");
        if(ui.Game.Phase=="finished")foreach(var button in new[]{ui.SoloRetryRect,ui.SoloTrackChoiceRect})Check(Inside(button,arena)&&!button.Overlaps(bar),"result actions entirely visible");
        string state=State(ui.Game);int oldW=Display.width,oldH=Display.height;Display.width+=160;Display.height+=80;Call(ui,"Measure");Check(State(ui.Game)==state,"resize leaves physical state/camera/results unchanged");Display.width=oldW;Display.height=oldH;Call(ui,"Measure");
        Evidence.Add(new{kind="solo-layout",size=new[]{w,h},dpr=Display.PixelRatio,phase=ui.Game.Phase,camera=ui.Game.PartyCamera.ToString(),bar=R(bar),field=R(field),paw=R(paw)});
    }
    static void Start(FlipRaceGame ui,int w,int h)
    {
        Check(!ui.PawsVisible,"selection paw hidden");Click(ui,ui.SoloStartRect.center);Check(ui.Game.Phase=="countdown"&&!ui.PawsVisible,"single actual start immediately counts down");Layout(ui,w,h);
        Devices.Clear();Tick(ui);string countdown=State(ui.Game);Devices.Touches=new[]{TouchAt(ui.PawScreenRect(0).center,TouchPhase.Began)};Devices.Keys.Add(KeyCode.A);Tick(ui);Check(State(ui.Game)==countdown,"countdown paw/key edge cannot act or move camera");Devices.Clear();while(ui.Game.Phase=="countdown")Tick(ui,Math.Min(.1,ui.Game.CountdownRemaining));Check(ui.PawsVisible&&ui.Game.Phase=="racing","actual Update reaches race");Layout(ui,w,h);
    }
    static void FreshRace(FlipRaceGame ui)
    {
        var old=ui.Game;var game=RaceSimulation.CreateSolo(old.Track,old.Flight,old.PartyCamera,old.PartyCameraStartLeft,old.PartyWindowWidth);game.Players[0].CharacterIndex=old.Players[0].CharacterIndex;game.Phase="racing";Game(ui,game);Devices.Clear();Tick(ui);
    }
    static void Inputs(FlipRaceGame ui)
    {
        foreach(var xy in new[]{(x:.5f,y:.5f),(x:55.5f,y:.5f),(x:.5f,y:55.5f),(x:55.5f,y:55.5f),(x:.5f,y:28f),(x:55.5f,y:28f),(x:28f,y:.5f),(x:28f,y:55.5f),(x:28f,y:28f)}){
            FreshRace(ui);Rect paw=ui.PawScreenRect(0);Vector2 p=new Vector2(paw.x+xy.x,paw.y+xy.y);Devices.Touches=new[]{TouchAt(p,TouchPhase.Began)};Devices.MouseDown=Devices.MouseHeld=true;Devices.mousePosition=new Vector3(p.x*Display.PixelRatio,Display.height-p.y*Display.PixelRatio,0);Tick(ui);
            var player=ui.Game.Players[0];Check(player.Flipping&&player.AirFlipAvailable,"each of nine paw points exactly one Began, compatibility mouse suppressed");string first=State(player);
            foreach(var phase in new[]{TouchPhase.Moved,TouchPhase.Stationary,TouchPhase.Ended}){Devices.Touches=new[]{TouchAt(p,phase)};Tick(ui);Check(State(player)==first,"hold/move/end no extra action");}Devices.Clear();
        }
        FreshRace(ui);Devices.Keys.UnionWith(new[]{KeyCode.L,KeyCode.S,KeyCode.K});string before=State(ui.Game);Tick(ui);Check(State(ui.Game)==before,"inactive L/S/K neither action nor absent index");Devices.Clear();Devices.Keys.Add(KeyCode.A);Tick(ui);Check(ui.Game.Players[0].Flipping&&ui.Game.Players[0].AirFlipAvailable,"A exactly one action");Devices.Clear();
        FreshRace(ui);Rect field=ui.SharedFieldRect;Devices.Touches=new[]{TouchAt(field.center,TouchPhase.Began)};Tick(ui);Check(ui.Game.Players[0].Flipping,"free playable background acts");Devices.Clear();
        FreshRace(ui);Vector2 mouse=ui.PawScreenRect(0).center;Devices.MouseDown=Devices.MouseHeld=true;Devices.mousePosition=new Vector3(mouse.x*Display.PixelRatio,Display.height-mouse.y*Display.PixelRatio,0);Tick(ui);Check(ui.Game.Players[0].Flipping&&ui.Game.Players[0].AirFlipAvailable,"real mouse edge exactly once");Devices.MouseDown=false;before=State(ui.Game);Tick(ui);Check(State(ui.Game)==before,"mouse held no repeat");Devices.Clear();
        FreshRace(ui);before=State(ui.Game);Click(ui,new Vector2(ui.SessionBarRect.x+2,ui.SessionBarRect.center.y));Check(State(ui.Game)==before,"toolbar background consumes input");Click(ui,((Rect)Field(ui,"pauseRect")).center);Check(ui.IsPaused&&State(ui.Game)==before&&!ui.PawsVisible,"actual pause consumes action");Tick(ui,.2);Check(State(ui.Game)==before,"paused world frozen");
        Rect cover=(Rect)Field(ui,"arena"),bar=ui.SessionBarRect;foreach(string prop in new[]{"PauseTitleY","PauseSubtitleY"}){float y=(float)typeof(FlipRaceGame).GetProperty(prop,Private).GetValue(ui);var box=new Rect(cover.x+20,y,cover.width-40,prop=="PauseTitleY"?30:28);Check(Inside(box,cover)&&!box.Overlaps(bar),"pause text visible "+prop);}Check(Inside((Rect)Field(ui,"resumeRect"),cover)&&!((Rect)Field(ui,"resumeRect")).Overlaps(bar),"resume visible outside toolbar");Click(ui,((Rect)Field(ui,"resumeRect")).center);Check(!ui.IsPaused,"actual resume");Devices.Clear();Tick(ui);
    }
    static void Results(FlipRaceGame ui,int w,int h)
    {
        string prefs=PrefSnapshot(),track=ui.Game.Track.Id,flight=State(ui.Game.Flight);int hero=ui.Game.Players[0].CharacterIndex;PartyCamera camera=ui.Game.PartyCamera;
        foreach(var status in new[]{PlayerStatus.Finished,PlayerStatus.Eliminated}){
            FreshRace(ui);var p=ui.Game.Players[0];p.Status=status;p.FinishTime=status==PlayerStatus.Finished?12.34:null;p.Distance=500;p.CoinsEarned=7;p.BoostsEarned=2;p.Collisions=3;p.CollectedRiskIds.Add("synthetic-result");ui.Game.Phase="finished";ui.Game.Elapsed=12.34;Call(ui,"Measure");
            Check(ui.SoloResultTitle==(status==PlayerStatus.Finished?"Трасса пройдена":"Выбыл")&&!ui.PawsVisible,"specific single outcome, paw hidden");Layout(ui,w,h);Check(PrefSnapshot()==prefs,"finish/elimination writes no progress/result preferences");
            Click(ui,ui.SoloRetryRect.center);Check(ui.Game.Phase=="countdown"&&ui.Game.Players.Length==1,"actual retry immediately restarts solo countdown");Check(ui.Game.Track.Id==track&&State(ui.Game.Flight)==flight&&ui.Game.PartyCamera==camera&&ui.Game.Players[0].CharacterIndex==hero,"retry retains full track hero controls camera");p=ui.Game.Players[0];Check(p.Status==PlayerStatus.Running&&p.FinishTime==null&&p.Distance==0&&p.CoinsEarned==0&&p.BoostsEarned==0&&p.Collisions==0&&p.CollectedRiskIds.Count==0&&ui.Game.Elapsed==0,"retry resets temporary result/rewards/distance");Check(PrefSnapshot()==prefs,"retry writes no progress preferences");
        }
        ui.Game.Phase="finished";ui.Game.Players[0].Status=PlayerStatus.Eliminated;Call(ui,"Measure");Click(ui,ui.SoloTrackChoiceRect.center);Check(ui.FlightMenuPage==0&&ui.IsPaused,"actual result Choose track opens menu");Check(PrefSnapshot()==prefs,"track menu writes no progress");
        var reloaded=NewUi(false);Check(reloaded.Game.Phase=="ready"&&reloaded.Game.Elapsed==0&&reloaded.Game.Players.All(p=>p.Distance==0&&p.CoinsEarned==0&&p.FinishTime==null),"reloaded initialized UI restores no result/distance/rewards");Check(State(reloaded.Game.Flight)==flight&&Prefs.GetInt("flip-race-sound",1)==0&&PrefSnapshot()==prefs,"reload retains prior controls and sound settings");
    }
    static void Transitions()
    {
        foreach(int count in new[]{2,3,4})foreach(int kind in new[]{0,1,2})foreach(int seat in kind==2?new[]{0,1}:new[]{0}){
            var ui=NewUi();Solo(ui,PartyCamera.Lagger);Click(ui,ui.SoloCharacterRect(4).center);string track=ui.Game.Track.Id,flight=State(ui.Game.Flight);
            Call(ui,"OpenTracks");Click(ui,ui.ViewMenuButtonRect.center);Check(ui.FlightMenuPage==7,"solo reopens own menu");Click(ui,((Rect)typeof(FlipRaceGame).GetProperty("SoloPartyRect",Private).GetValue(ui)).center);Check(ui.FlightMenuPage==6,"solo to company route");Click(ui,ui.PartyCountRect(count-2).center);Apply(ui);Check(ui.PartyEnabled&&ui.Game.Players.Length==count&&ui.Game.Players[0].CharacterIndex==4&&ui.Game.PartyCamera==PartyCamera.Lagger,"solo to company preserves hero/camera and actual selected count");
            Call(ui,"OpenTracks");Click(ui,ui.ViewMenuButtonRect.center);Click(ui,ui.PartyLegacyRect.center);Check(ui.FlightMenuPage==5,"company to legacy actual route");Click(ui,ui.ViewOptionRect(0,kind).center);if(kind==2)Click(ui,ui.ViewOptionRect(1,seat).center);Apply(ui);Check(!ui.SoloEnabled&&!ui.PartyEnabled&&ui.Game.Players.Length==2&&ui.FieldViewName==(kind==0?"split":kind==1?"split-ghost":"shared"),"legacy two player view restores without missing index");if(kind==2)Check((bool)Field(ui,"sharedAdjacent")== (seat==1),"legacy shared seating choice");
            Solo(ui,PartyCamera.Lagger);Check(ui.Game.Track.Id==track&&State(ui.Game.Flight)==flight&&ui.Game.Players[0].CharacterIndex==4,"round trip preserves full track hero controls");Evidence.Add(new{kind="actual-menu-roundtrip",count,legacy=kind,seat});
        }
    }
    static int Main(){try{
        foreach(var size in new[]{(w:568,h:320),(w:800,h:360),(w:960,h:432),(w:1280,h:800)})foreach(float dpr in new[]{1f,2.5f,3f})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger}){
            cases++;Display.width=Mathf.RoundToInt(size.w*dpr);Display.height=Mathf.RoundToInt(size.h*dpr);Display.PixelRatio=dpr;Display.SafeAreaOverride=new Rect(12*dpr,10*dpr,(size.w-28)*dpr,(size.h-24)*dpr);var ui=NewUi();Solo(ui,camera);Layout(ui,size.w,size.h);Start(ui,size.w,size.h);Inputs(ui);Results(ui,size.w,size.h);
        }foreach(int choice in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger}){cases++;var ui=NewUi(true,choice);Solo(ui,camera);Layout(ui,1280,800);Start(ui,1280,800);Inputs(ui);Results(ui,1280,800);}Transitions();Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,cases,updates,pointers,Evidence,scope="Actual production Update, Pointer, menu, layout and Core; only native environment/texture shims. Synthetic terminal/input fixtures test UI dispatch and reset, not Core reachability. No native rendering, browser, or physical multitouch claim."},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
