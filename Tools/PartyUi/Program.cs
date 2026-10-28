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
namespace FlipRace.PartyUiCheck
{
static class Program
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks,cases,updates,pointers;
    static readonly List<object> Evidence=new List<object>();
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Private).Invoke(o,args);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Private).SetValue(o,value);
    static object Field(object o,string name)=>o.GetType().GetField(name,Private).GetValue(o);
    static void Near(double a,double b,string why,double tolerance=.0003)=>Check(Math.Abs(a-b)<=tolerance,why+": "+a+" != "+b);
    static bool Inside(Rect a,Rect b)=>a.xMin>=b.xMin-.001&&a.xMax<=b.xMax+.001&&a.yMin>=b.yMin-.001&&a.yMax<=b.yMax+.001;
    static float[] R(Rect r)=>new[]{r.x,r.y,r.width,r.height};
    static string State(GameState g)=>JsonSerializer.Serialize(g,new JsonSerializerOptions{IncludeFields=true});
    static void Tick(FlipRaceGame ui,double dt=0){Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=dt;Call(ui,"Update");updates++;}
    static void Click(FlipRaceGame ui,Vector2 p){Devices.Clear();Tick(ui);Call(ui,"Pointer",p);pointers++;}
    static Touch TouchAt(int id,Vector2 p,TouchPhase phase)=>new Touch{fingerId=id,position=new Vector2(p.x*Display.PixelRatio,Display.height-p.y*Display.PixelRatio),phase=phase};
    static void TouchAll(FlipRaceGame ui,Func<int,Vector2> point,TouchPhase phase=TouchPhase.Began,bool mouse=false)
    {
        Devices.Clear();Devices.Touches=Enumerable.Range(0,ui.Game.Players.Length).Select(i=>TouchAt(i+21,point(i),phase)).ToArray();
        Devices.MouseDown=Devices.MouseHeld=mouse;Vector2 m=point(ui.Game.Players.Length-1);Devices.mousePosition=new Vector3(m.x*Display.PixelRatio,Display.height-m.y*Display.PixelRatio,0);Tick(ui);Devices.Clear();
    }
    static FlipRaceGame Ui(int w,int h,float dpr,int choice,int count,PartyCamera camera)
    {
        Display.width=Mathf.RoundToInt(w*dpr);Display.height=Mathf.RoundToInt(h*dpr);Display.PixelRatio=dpr;
        Display.SafeAreaOverride=new Rect(12*dpr,10*dpr,(w-28)*dpr,(h-24)*dpr);
        Clock.frameCount=1;Clock.realtimeSinceStartupAsDouble=1;Devices.Clear();Prefs.Clear();
        var draft=FlightDraft.From(FlightPresets.FastFlip);draft.Repeats=AirRepeatLimit.One;draft.Gravity=2;draft.Speed=3;
        Prefs.SetString(FlightSettingsStore.Key,FlightSettingsStore.Encode(draft));Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,choice);
        var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        Set(ui,"lanes",new Rect[2]);foreach(string name in new[]{"firstPawAt","secondPawAt","thirdPawAt","fourthPawAt"})Set(ui,name,-10f);
        Call(ui,"InitializeFlightMenu");var selection=(FlightSelection)Field(ui,"flightSelection");
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,RaceSimulation.CreateTrial(TracksMixed.Get(),selection.Settings));
        Set(ui,"selectedTrackId",ui.Game.Track.Id);Set(ui,"lastFrame",1d);Call(ui,"Measure");
        Call(ui,"OpenTracks");Check(ui.FlightMenuPage==0&&ui.IsPaused,"real menu opens");
        Click(ui,ui.ViewMenuButtonRect.center);Check(ui.FlightMenuPage==5,"real view navigation");
        Click(ui,ui.PartyMenuButtonRect.center);Check(ui.FlightMenuPage==6,"real party navigation");
        Click(ui,ui.PartyCountRect(count-2).center);Click(ui,ui.PartyCameraRect((int)camera).center);
        string flight=JsonSerializer.Serialize(selection.Settings,new JsonSerializerOptions{IncludeFields=true});
        Click(ui,((Rect)Field(ui,"flightApply")).center);
        Check(ui.PartyEnabled&&ui.Game.Players.Length==count&&ui.Game.PartyCamera==camera&&!ui.IsPaused,"actual party apply count/camera");
        Check(JsonSerializer.Serialize(ui.Game.Flight,new JsonSerializerOptions{IncludeFields=true})==flight,"actual apply preserves selected preset/manual physics");
        Check(ui.Game.Phase=="ready"&&ui.Game.Players.All(p=>!p.Ready),"apply gives independent clean readiness");
        Devices.Clear();Tick(ui);return ui;
    }
    static void Layout(FlipRaceGame ui,int w,int h)
    {
        Rect arena=(Rect)Field(ui,"arena"),safe=new Rect(12,14,w-28,h-24);
        var areas=new List<Rect>();var paws=new List<Rect>();
        for(int slot=0;slot<ui.Game.Players.Length;slot++)
        {
            int corner=ui.Game.Players.Length==2?(slot==0?0:3):ui.Game.Players.Length==3&&slot==2?3:slot;
            Rect area=ui.ControlAreaRect(slot);areas.Add(area);
            Near(area.x,arena.x+(corner%2)*arena.width/2,"physical corner X");Near(area.y,corner<2?arena.y:arena.yMax-(arena.height-ui.SessionBarRect.height)/2,"physical corner Y excludes session strip");
            Check(areas.Take(areas.Count-1).All(a=>!a.Overlaps(area)),"independent corner regions");
            Rect ready=ui.ReadyScreenRect(slot);Check(Inside(ready,area)&&ready.width>=44&&ready.height>=44,"44 readiness entirely in own quadrant");
            var cards=new List<Rect>();
            for(int hero=0;hero<6;hero++)
            {
                Rect card=ui.CharacterScreenRect(slot,hero);
                Check(Inside(card,area)&&card.width>=44&&card.height>=44,"all six >=44 hero cards entirely in own quadrant "+w+"/"+h+"/"+slot+"/"+hero);
                Check(!card.Overlaps(ready)&&cards.All(c=>!card.Overlaps(c)),"hero/readiness targets do not overlap");
                cards.Add(card);
                Click(ui,card.center);Check(ui.Game.Players[slot].CharacterIndex==hero&&!ui.Game.Players[slot].Ready,"actual Pointer selects all six heroes");
                Check(ui.Game.Players.All(p=>!p.Ready),"selection cannot ready another slot");
            }
            Rect paw=ui.PawScreenRect(slot);Check(Inside(paw,safe),"whole paw within viewport safe area");Near(paw.width,56,"56 CSS paw width");Near(paw.height,56,"56 CSS paw height");
            bool upper=corner<2,leftPaw=corner%2==0;float inset=h<=600?40:8;
            Near(leftPaw?paw.xMin:paw.xMax,leftPaw?safe.xMin+inset:safe.xMax-inset,"physical viewport left/right paw inset");
            Near(upper?paw.yMin:paw.yMax,upper?safe.yMin+8:safe.yMax-8,"physical top/bottom paw inset");
            Check(paws.All(p=>!p.Overlaps(paw)),"independent paw regions");paws.Add(paw);
        }
        var zone=ui.Game.Track.RiskZones.First();
        for(int owner=0;owner<ui.Game.Players.Length;owner++) {
            ui.Game.Players[owner].CollectedRiskIds.Add(zone.Id);
            for(int slot=0;slot<ui.Game.Players.Length;slot++)
                Check(SharedFieldView.MarkerAvailable(ui.Game.Players[slot],zone.Id)==(slot!=owner),"personal reward marker changes only for its owner");
            ui.Game.Players[owner].CollectedRiskIds.Remove(zone.Id);
        }
        Rect field=ui.PartyFieldRect;var view=SharedFieldView.Create(field.width,field.height,ui.Game);
        Near(view.ScaleX,view.ScaleY,"uniform party projection");Near(view.Left,0,"party field excludes horizontal letterbox",.001);
        Near(view.CameraLeft,ui.Game.PartyCameraLeft,"presentation uses Core camera");
        for(int slot=0;slot<ui.Game.Players.Length;slot++)Near(view.PlayerX(slot),(ui.Game.Players[slot].Distance-ui.Game.PartyCameraLeft)*view.ScaleX,"actual physical centre "+slot);
        string frozen=State(ui.Game);double left=ui.Game.PartyCameraLeft;
        int oldWidth=Display.width,oldHeight=Display.height;
        Display.width=Mathf.RoundToInt((w+160)*Display.PixelRatio);Display.height=Mathf.RoundToInt((h+80)*Display.PixelRatio);Call(ui,"Measure");
        var resized=SharedFieldView.Create(ui.PartyFieldRect.width,ui.PartyFieldRect.height,ui.Game);
        Near(resized.ScaleX,resized.ScaleY,"resized projection remains uniform");Near(resized.CameraLeft,left,"resize leaves camera boundary unchanged");
        Check(State(ui.Game)==frozen,"resize cannot mutate race or outcomes");
        Display.width=oldWidth;Display.height=oldHeight;Call(ui,"Measure");
        Evidence.Add(new{Kind="actual-menu-layout",Choice=ui.IsCustomFlight?-1:(int)Field(ui,"activeFlightPreset"),Size=new[]{w,h},Dpr=Display.PixelRatio,Count=ui.Game.Players.Length,Camera=ui.Game.PartyCamera.ToString(),Areas=areas.Select(R),Paws=paws.Select(R),Field=R(field)});
    }
    static void Header(FlipRaceGame ui)
    {
        Call(ui,"Measure");Rect bar=ui.SessionBarRect,arena=(Rect)Field(ui,"arena");
        Check(ui.SessionCentered,"party toolbar centered for opposite sides");
        string frozen=State(ui.Game);
        if(ui.Game.Phase=="ready"||ui.Game.Phase=="finished") {
            Near(bar.center.y,arena.center.y,"ready/results toolbar between player rows");
            for(int slot=0;slot<ui.Game.Players.Length;slot++) {
                Check(!bar.Overlaps(ui.ControlAreaRect(slot))&&!bar.Overlaps(ui.ReadyScreenRect(slot)),"toolbar outside personal rows/readiness");
                for(int hero=0;hero<6;hero++)Check(!bar.Overlaps(ui.CharacterScreenRect(slot,hero)),"toolbar cannot cover hero card");
            }
        } else {
            Rect field=ui.PartyFieldRect;Near(bar.yMax,field.yMin,"active toolbar immediately above unchanged world");
            Check(!bar.Overlaps(field),"active toolbar cannot occlude geometry");
            Rect safe=(Rect)Call(ui,"SafeUiArea");float inset=(bool)Field(ui,"compact")?40:8;
            for(int corner=0;corner<4;corner++) {
                var paw=new Rect(corner%2==0?safe.xMin+inset:safe.xMax-inset-56,corner<2?safe.yMin+8:safe.yMax-8-56,56,56);
                Check(!bar.Overlaps(paw),"active toolbar clear of all four viewport paw corners");
                Check(!field.Overlaps(paw),"unchanged world clear of all four viewport paw corners");
            }
            Near(field.height,((Rect[])Field(ui,"lanes"))[0].height,"party keeps legacy lane world height");
        }
        foreach(string name in new[]{"soundRect","pauseRect","fullscreenRect"})Check(Inside((Rect)Field(ui,name),bar),"session tool stays inside moved strip "+name);
        if(ui.Game.Phase=="countdown")for(int slot=0;slot<ui.Game.Players.Length;slot++) {
            Rect region=ui.ControlAreaRect(slot);bool upper=(bool)Call(ui,"PartyRotated",slot);
            float local=(float)Call(ui,"SharedCountdownCenter",slot);
            float physical=upper?region.yMax-local:region.yMin+local;
            var glyph=new Rect(region.x,physical-30,region.width,60);
            float band=(float)Call(ui,"SharedCountdownBandHeight",slot);
            Check(band>=60,"countdown band has room for full 60px glyph");
            Check(upper?glyph.yMin>=region.yMin-.001&&glyph.yMax<=bar.yMin+.001:
                glyph.yMin>=ui.SharedFieldRect.yMax-.001&&glyph.yMax<=region.yMax+.001,"countdown glyph wholly visible above toolbar or below world");
        }
        Check(State(ui.Game)==frozen,"toolbar measurement cannot mutate immutable Core camera or state");
    }
    static void PauseLayout(FlipRaceGame ui)
    {
        Rect cover=(Rect)Field(ui,"arena"),bar=ui.SessionBarRect;
        float titleY=(float)typeof(FlipRaceGame).GetProperty("PauseTitleY",Private).GetValue(ui);
        float subtitleY=(float)typeof(FlipRaceGame).GetProperty("PauseSubtitleY",Private).GetValue(ui);
        float iconSize=(float)typeof(FlipRaceGame).GetProperty("PauseIconSize",Private).GetValue(ui);
        var title=new Rect(cover.x+15,titleY,cover.width-30,30);
        var subtitle=new Rect(cover.x+20,subtitleY,cover.width-40,28);
        var resume=(Rect)Field(ui,"resumeRect");
        foreach(var box in new[]{title,subtitle,resume})Check(Inside(box,cover)&&!box.Overlaps(bar),"pause title/subtitle/resume visible outside session strip");
        Check(!title.Overlaps(subtitle)&&!title.Overlaps(resume)&&!subtitle.Overlaps(resume),"pause text and resume independent");
        if(iconSize>0) {
            var icon=new Rect(cover.center.x-iconSize/2,titleY-iconSize-8,iconSize,iconSize);
            Check(Inside(icon,cover)&&!icon.Overlaps(bar)&&!icon.Overlaps(title),"optional pause icon fully visible outside toolbar");
        }
    }
    static void Edges(FlipRaceGame ui)
    {
        var old=ui.Game;
        foreach(int slot in Enumerable.Range(0,old.Players.Length))
        foreach(var point in new[]{(x:.5f,y:.5f),(x:55.5f,y:.5f),(x:.5f,y:55.5f),(x:55.5f,y:55.5f),(x:.5f,y:28f),(x:55.5f,y:28f),(x:28f,y:.5f),(x:28f,y:55.5f),(x:28f,y:28f)}) {
            // Fresh Core state isolates every native-input edge; actual Update
            // owns hit-testing, input dispatch and action below.
            var next=RaceSimulation.CreateParty(old.Track,old.Flight,old.Players.Length,old.PartyCamera,old.PartyCameraStartLeft,old.PartyWindowWidth);next.Phase="racing";
            typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,next);Call(ui,"Measure");
            Rect paw=ui.PawScreenRect(slot);
            Devices.Touches=new[]{TouchAt(82,new Vector2(paw.x+point.x,paw.y+point.y),TouchPhase.Began)};Tick(ui);Devices.Clear();
            int first=next.Flight.FirstAction==FirstAction.Jump?1:-1;
            Check(next.Players[slot].Flipping&&next.Players[slot].Gravity==first&&next.Players[slot].AirFlipAvailable,"all nine visible paw points give exactly one own action");
            Check(next.Players.Where((p,i)=>i!=slot).All(p=>!p.Flipping),"outer viewport paw edge cannot act for another slot");
        }
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,old);Call(ui,"Measure");
    }
    static void ToolbarInput(FlipRaceGame ui)
    {
        Header(ui);string before=State(ui.Game);
        Click(ui,((Rect)Field(ui,"soundRect")).center);Check(State(ui.Game)==before,"toolbar sound tap cannot act for a player");
        Click(ui,((Rect)Field(ui,"pauseRect")).center);Check(ui.IsPaused&&State(ui.Game)==before,"toolbar pause tap cannot act for a player");PauseLayout(ui);
        Call(ui,"Resume");Devices.Clear();Tick(ui);
        before=State(ui.Game);Rect bar=ui.SessionBarRect;
        Click(ui,new Vector2(bar.xMin+2,bar.center.y));Check(State(ui.Game)==before,"toolbar label/background tap cannot act for a player");
    }
    static void Start(FlipRaceGame ui)
    {
        Check(!ui.PawsVisible,"paws hidden during selection");
        for(int slot=0;slot<ui.Game.Players.Length-1;slot++)
        {Click(ui,ui.ReadyScreenRect(slot).center);Check(ui.Game.Players[slot].Ready&&ui.Game.Phase=="ready","each ready waits for remaining participants");Check(!ui.Game.Players[slot+1].Ready,"ready independent of next slot");}
        Click(ui,ui.ReadyScreenRect(ui.Game.Players.Length-1).center);Check(ui.Game.Phase=="countdown"&&!ui.PawsVisible,"all actual readiness starts countdown");
        Header(ui);TouchAll(ui,i=>ui.PawScreenRect(i).center);Check(ui.Game.Players.All(p=>!p.Flipping),"countdown paws cannot act");
        while(ui.Game.Phase=="countdown")Tick(ui,Math.Min(.1,ui.Game.CountdownRemaining));
        Check(ui.PawsVisible,"actual racing enables paws");Header(ui);
        Devices.Touches=new[]{TouchAt(51,ui.PawScreenRect(0).center,TouchPhase.Began)};Tick(ui);
        Check(ui.Game.Players[0].Flipping&&ui.Game.Players.Skip(1).All(p=>!p.Flipping),"isolated edge acts only for owner");
        foreach(var phase in new[]{TouchPhase.Moved,TouchPhase.Stationary,TouchPhase.Ended}) {
            Devices.Touches=new[]{TouchAt(51,ui.PawScreenRect(ui.Game.Players.Length-1).center,phase)};Tick(ui);
            Check(ui.Game.Players.Skip(1).All(p=>!p.Flipping),"crossing into untouched quadrant cannot act for it");
        }
        Devices.Clear();
        // Restart the input fixture through actual UI readiness, retaining heroes.
        var old=ui.Game;var next=RaceSimulation.CreateParty(old.Track,old.Flight,old.Players.Length,old.PartyCamera,old.PartyCameraStartLeft,old.PartyWindowWidth);
        for(int i=0;i<old.Players.Length;i++)next.Players[i].CharacterIndex=old.Players[i].CharacterIndex;
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,next);
        for(int i=0;i<next.Players.Length;i++)Click(ui,ui.ReadyScreenRect(i).center);
        while(next.Phase=="countdown")Tick(ui,Math.Min(.1,next.CountdownRemaining));
    }
    static void Actions(FlipRaceGame ui)
    {
        int count=ui.Game.Players.Length;
        TouchAll(ui,i=>ui.PawScreenRect(i).center,TouchPhase.Began,true);
        int first=ui.Game.Flight.FirstAction==FirstAction.Jump?1:-1;
        Check(ui.Game.Players.All(p=>p.Flipping&&p.Gravity==first&&p.AirFlipAvailable),"simultaneous touches act exactly once; compatibility mouse suppressed");
        string state=State(ui.Game);var stamps=new[]{"firstPawAt","secondPawAt","thirdPawAt","fourthPawAt"}.Take(count).Select(n=>(float)Field(ui,n)).ToArray();
        foreach(var phase in new[]{TouchPhase.Stationary,TouchPhase.Moved,TouchPhase.Ended,TouchPhase.Canceled})
        {TouchAll(ui,i=>ui.PawScreenRect((i+1)%count).center,phase);Check(State(ui.Game)==state,"hold/move/end/cancel across zones cannot act");}
        Check(stamps.SequenceEqual(new[]{"firstPawAt","secondPawAt","thirdPawAt","fourthPawAt"}.Take(count).Select(n=>(float)Field(ui,n))),"non-Began never refreshes feedback");
        // A second fresh edge gives exactly the permitted air repeat.
        TouchAll(ui,i=>ui.PawScreenRect(i).center);Check(ui.Game.Players.All(p=>p.Gravity==-first&&p.AirFlipAvailable==(ui.Game.Flight.AirRepeats==AirRepeatLimit.Unlimited)),"quick repeat is one air turn per participant");
        double elapsed=ui.Game.Elapsed,camera=ui.Game.PartyCameraLeft;
        Call(ui,"Pause","party harness");state=State(ui.Game);TouchAll(ui,i=>ui.PawScreenRect(i).center);
        for(int n=0;n<5;n++)Tick(ui,.2);
        Check(!ui.PawsVisible&&State(ui.Game)==state,"pause blocks input and all simulation");
        Near(ui.Game.Elapsed,elapsed,"pause freezes elapsed");Near(ui.Game.PartyCameraLeft,camera,"pause freezes camera");
        Call(ui,"Resume");TouchAll(ui,i=>ui.PawScreenRect(i).center,TouchPhase.Stationary);
        Check(State(ui.Game)==state,"held resume touch cannot leak action");Devices.Clear();Tick(ui);
        Check(!(bool)Field(ui,"gameplayInputBlocked"),"release clears resume block");
    }
    static void TerminalAndRematch(FlipRaceGame ui)
    {
        // Deliberately synthetic terminal fixtures test UI gating and rematch,
        // not reachability or event chronology (covered by Core checks).
        ui.Game.Players[0].Status=PlayerStatus.Eliminated;
        float terminalStamp=(float)Field(ui,"firstPawAt");
        string before=JsonSerializer.Serialize(ui.Game.Players[0],new JsonSerializerOptions{IncludeFields=true});
        Devices.Touches=new[]{TouchAt(90,ui.PawScreenRect(0).center,TouchPhase.Began)};Tick(ui);Devices.Clear();
        Check(JsonSerializer.Serialize(ui.Game.Players[0],new JsonSerializerOptions{IncludeFields=true})==before,"terminal player's actual Update touch cannot act");
        Check((float)Field(ui,"firstPawAt")==terminalStamp,"terminal touch cannot flash a paw");
        ui.Game.Players[0].Status=PlayerStatus.Finished;ui.Game.Players[0].FinishTime=ui.Game.Elapsed;
        before=JsonSerializer.Serialize(ui.Game.Players[0],new JsonSerializerOptions{IncludeFields=true});
        Devices.Touches=new[]{TouchAt(91,ui.PawScreenRect(0).center,TouchPhase.Began)};Tick(ui);Devices.Clear();
        Check(JsonSerializer.Serialize(ui.Game.Players[0],new JsonSerializerOptions{IncludeFields=true})==before&&(float)Field(ui,"firstPawAt")==terminalStamp,"finished touch cannot act or flash a paw");
        ui.Game.Phase="finished";foreach(var p in ui.Game.Players){p.Status=PlayerStatus.Eliminated;p.Ready=false;}
        Check(!ui.PawsVisible,"finished party has no paws");Header(ui);
        int count=ui.Game.Players.Length;var camera=ui.Game.PartyCamera;var heroes=ui.Game.Players.Select(p=>p.CharacterIndex).ToArray();
        for(int slot=0;slot<count-1;slot++){Click(ui,ui.ReadyScreenRect(slot).center);Check(ui.Game.Phase=="finished"&&ui.Game.Players[slot].Ready,"rematch readiness independent");}
        Click(ui,ui.ReadyScreenRect(count-1).center);
        Check(ui.Game.Phase=="countdown"&&ui.Game.Players.Length==count&&ui.Game.PartyCamera==camera,"actual rematch preserves party count and camera");
        Check(heroes.SequenceEqual(ui.Game.Players.Select(p=>p.CharacterIndex))&&ui.Game.Players.All(p=>p.Status==PlayerStatus.Running),"rematch retains all heroes and resets terminal states");
    }
    static void Legacy(int count,PartyCamera camera,int kind)
    {
        var ui=Ui(568,320,1,10,count,camera);
        Call(ui,"OpenTracks");Click(ui,ui.ViewMenuButtonRect.center);Check(ui.FlightMenuPage==6,"party reopens its own choices");
        Click(ui,ui.PartyLegacyRect.center);Check(ui.FlightMenuPage==5,"actual return to legacy");
        Click(ui,ui.ViewOptionRect(0,kind).center);Click(ui,((Rect)Field(ui,"flightApply")).center);
        Check(!ui.PartyEnabled&&ui.Game.Players.Length==2&&ui.FieldViewName==(kind==0?"split":kind==1?"split-ghost":"shared"),"actual legacy apply restores requested two-player view");
    }
    static int Main()
    {
        try
        {
            foreach(var size in new[]{(w:568,h:320),(w:800,h:360),(w:960,h:432),(w:1280,h:720)})
            foreach(float dpr in new[]{1f,2.5f,3f})foreach(int count in new[]{2,3,4})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})foreach(int choice in new[]{10,11,-1})
            {cases++;var ui=Ui(size.w,size.h,dpr,choice,count,camera);Layout(ui,size.w,size.h);Header(ui);Start(ui);Edges(ui);ToolbarInput(ui);Actions(ui);TerminalAndRematch(ui);}
            foreach(int count in new[]{2,3,4})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})foreach(int kind in new[]{0,1,2})Legacy(count,camera,kind);
            Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,cases,updates,pointers,Evidence,scope="Production menu, Measure, Pointer, Update and Core execute. Explicit native Screen/Input/Time/PlayerPrefs and selection-texture shims; no native rendering or physical phone timing claim. Terminal UI fixtures are synthetic, not event-chronology proofs."},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
}