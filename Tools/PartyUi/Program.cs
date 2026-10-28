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
            Near(area.x,arena.x+(corner%2)*arena.width/2,"physical corner X");Near(area.y,arena.y+(corner/2)*arena.height/2,"physical corner Y");
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
            Rect paw=ui.PawScreenRect(slot);Check(Inside(paw,area)&&Inside(paw,safe),"whole paw within quadrant and device safe area");Near(paw.width,56,"56 CSS paw width");Near(paw.height,56,"56 CSS paw height");
            bool upper=corner<2;float inset=h<=600?40:8;
            Near(upper?paw.xMin:paw.xMax,upper?Math.Max(area.xMin,safe.xMin)+inset:Math.Min(area.xMax,safe.xMax)-inset,"physical hand-side paw inset");
            Near(upper?paw.yMin:paw.yMax,upper?Math.Max(area.yMin,safe.yMin)+8:Math.Min(area.yMax,safe.yMax)-8,"physical top/bottom paw inset");
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
    static void Start(FlipRaceGame ui)
    {
        Check(!ui.PawsVisible,"paws hidden during selection");
        for(int slot=0;slot<ui.Game.Players.Length-1;slot++)
        {Click(ui,ui.ReadyScreenRect(slot).center);Check(ui.Game.Players[slot].Ready&&ui.Game.Phase=="ready","each ready waits for remaining participants");Check(!ui.Game.Players[slot+1].Ready,"ready independent of next slot");}
        Click(ui,ui.ReadyScreenRect(ui.Game.Players.Length-1).center);Check(ui.Game.Phase=="countdown"&&!ui.PawsVisible,"all actual readiness starts countdown");
        TouchAll(ui,i=>ui.PawScreenRect(i).center);Check(ui.Game.Players.All(p=>!p.Flipping),"countdown paws cannot act");
        while(ui.Game.Phase=="countdown")Tick(ui,Math.Min(.1,ui.Game.CountdownRemaining));
        Check(ui.PawsVisible,"actual racing enables paws");
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
        Check(!ui.PawsVisible,"finished party has no paws");
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
            {cases++;var ui=Ui(size.w,size.h,dpr,choice,count,camera);Layout(ui,size.w,size.h);Start(ui);Actions(ui);TerminalAndRematch(ui);}
            foreach(int count in new[]{2,3,4})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})foreach(int kind in new[]{0,1,2})Legacy(count,camera,kind);
            Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,cases,updates,pointers,Evidence,scope="Production menu, Measure, Pointer, Update and Core execute. Explicit native Screen/Input/Time/PlayerPrefs and selection-texture shims; no native rendering or physical phone timing claim. Terminal UI fixtures are synthetic, not event-chronology proofs."},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
}