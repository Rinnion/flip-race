using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;
using FlipRace.SharedScreenCheck;
using FlipRace.SharedScreenCheck.Interop;
using UnityEngine;
using Display = FlipRace.SharedScreenCheck.Interop.Display;

static class Program
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks,cases,updates,pointers,applies,equivalenceRuns;
    static readonly List<object> layouts=new List<object>();
    static void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
    static void Near(double a,double b,string why){Check(Math.Abs(a-b)<.0002,why+" "+a+" / "+b);}
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Private).SetValue(o,value);
    static object Field(object o,string name)=>o.GetType().GetField(name,Private).GetValue(o);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Private).Invoke(o,args);
    static int Main()
    {
        try
        {
            foreach(var size in new[]{(w:568,h:320),(w:1280,h:720)})
            foreach(bool adjacent in new[]{false,true})foreach(bool full in new[]{false,true})
            foreach(int choice in new[]{10,11,-1})Matrix(size.w,size.h,adjacent,full,choice);
            Legacy();Equivalence();
            Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,cases,updates,pointers,applies,equivalenceRuns,layouts,
                environment="Production control logic retained; only Screen/DPR, Input, Time, PlayerPrefs, mascot texture acquisition use explicit managed shims. No native rendering/device/storage claim.",
                covered="Measure, Update touch-phase/key routing, Pointer, menu/open/cancel/apply, profiles/custom/track/hero retention, 2 seatings x 2 heights x 2 viewports, margins, rematch; old nontrial split remains."},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static FlipRaceGame Ui(int width,int height,int choice)
    {
        Display.width=width;Display.height=height;Display.PixelRatio=1;Clock.realtimeSinceStartupAsDouble=1;Clock.frameCount=1;Devices.Clear();Prefs.Clear();
        var custom=FlightDraft.From(FlightPresets.FastFlip);custom.Repeats=AirRepeatLimit.None;custom.Jump=2;custom.Flip=4;custom.Gravity=2;custom.Speed=3;
        Prefs.SetString(FlightSettingsStore.Key,FlightSettingsStore.Encode(custom));Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,choice);
        var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));Set(ui,"lanes",new Rect[2]);
        Call(ui,"InitializeFlightMenu");var selection=(FlightSelection)Field(ui,"flightSelection");
        var game=RaceSimulation.CreateTrial(TracksMixed.Get(),selection.Settings);RaceSimulation.SetCharacter(game,0,2);RaceSimulation.SetCharacter(game,1,3);
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);Set(ui,"selectedTrackId",game.Track.Id);Set(ui,"lastFrame",Clock.realtimeSinceStartupAsDouble);
        Call(ui,"Measure");return ui;
    }
    static void Pointer(FlipRaceGame ui,Vector2 point){Call(ui,"Pointer",point);pointers++;}
    static void Tick(FlipRaceGame ui,double dt=.001)
    {Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=dt;Call(ui,"Update");updates++;}
    static void Release(FlipRaceGame ui){Devices.Clear();Tick(ui,0);}
    static void Click(FlipRaceGame ui,Vector2 point){Release(ui);Pointer(ui,point);}
    static string Live(FlipRaceGame ui)=>ui.FieldViewName+"/"+ui.SeatingName+"/"+ui.FieldHeightName;
    static Touch TouchAt(int id,Vector2 point,TouchPhase phase)=>new Touch{fingerId=id,position=new Vector2(point.x*Display.PixelRatio,Display.height-point.y*Display.PixelRatio),phase=phase};
    static void SameChoice(FlipRaceGame ui,string custom,string track,int preset)
    {
        var s=(FlightSelection)Field(ui,"flightSelection");Check(FlightSettingsStore.Encode(s.Custom)==custom&&s.PresetIndex==preset,"View change retains exact custom and selected profile");
        Check(Audit.Fingerprint(ui.Game.Flight)==Audit.Fingerprint(s.Settings),"Live game's exact physical settings match retained selection");
        Check(ui.Game.Track.Id==track&&ui.Game.Players[0].CharacterIndex==2&&ui.Game.Players[1].CharacterIndex==3,"View change retains course and both heroes");
    }
    static void OpenView(FlipRaceGame ui)
    {Call(ui,"OpenTracks");Check(ui.IsPaused&&ui.FlightMenuPage==0,"Actual menu opens prestart");Click(ui,ui.ViewMenuButtonRect.center);Check(ui.FlightMenuPage==5,"Actual entry opens view page");}
    static void Choose(FlipRaceGame ui,int row,int value)
    {Click(ui,ui.ViewOptionRect(row,value).center);}
    static void Matrix(int width,int height,bool adjacent,bool full,int choice)
    {
        cases++;var ui=Ui(width,height,choice);var selection=(FlightSelection)Field(ui,"flightSelection");string custom=FlightSettingsStore.Encode(selection.Custom);var original=ui.Game;
        Check(!ui.SharedEnabled&&ui.FieldHeightName=="half","Ordinary build defaults to split, half preference");
        OpenView(ui);string before=Audit.Fingerprint(original);
        Choose(ui,0,1);Choose(ui,1,adjacent?1:0);Choose(ui,2,full?1:0);
        Check(!ui.SharedEnabled&&before==Audit.Fingerprint(original),"Pending view options do not mutate live game");
        // Full option bounds must remain inside the real measured panel/footer.
        Rect panel=(Rect)Field(ui,"flightPanel"),apply=(Rect)Field(ui,"flightApply");
        for(int r=0;r<3;r++)for(int o=0;o<2;o++){Rect option=ui.ViewOptionRect(r,o);Check(option.xMin>=panel.xMin&&option.xMax<=panel.xMax&&option.yMax<apply.yMin&&option.height>=28,"Measured option visible with full target above footer");}
        Click(ui,((Rect)Field(ui,"flightBack")).center);Click(ui,((Rect)Field(ui,"flightBack")).center);
        Check(!ui.IsPaused&&!ui.SharedEnabled&&before==Audit.Fingerprint(original),"Closing unapplied menu keeps live state");
        OpenView(ui);Check(!(bool)Field(ui,"pendingSharedField")&&!(bool)Field(ui,"pendingSharedFullHeight"),"Reopen snapshots live preferences, discards cancelled draft");
        Choose(ui,0,1);Choose(ui,1,adjacent?1:0);Choose(ui,2,full?1:0);
        Click(ui,apply.center);applies++;
        Check(ui.SharedEnabled&&ui.SeatingName==(adjacent?"adjacent":"opposite")&&ui.FieldHeightName==(full?"full":"half"),"Actual Apply commits selected presentation");
        SameChoice(ui,custom,TracksMixed.Id,choice);Check(ui.Game.Phase=="ready"&&ui.Game.Players.All(p=>!p.Ready&&!p.Flipping),"Apply returns clean readiness without gameplay");
        Check(Prefs.GetInt(FlightSettingsStore.ActiveChoiceKey)==choice&&Prefs.GetString(FlightSettingsStore.Key)==custom,"Apply preserves actual stored custom through memory storage seam");
        string current=Live(ui);Rect arena=(Rect)Field(ui,"arena"),field=ui.SharedFieldRect;Rect[] lanes=(Rect[])Field(ui,"lanes");
        Near(field.center.y,arena.center.y,"Measured shared field centred");Near(field.width,arena.width,"Measured width retained");Near(field.height,full?arena.height:lanes[0].height,"Actual half equals old lane, full equals arena");
        Rect a=ui.ControlAreaRect(0),b=ui.ControlAreaRect(1);
        Near(a.width*a.height+b.width*b.height,arena.width*arena.height,"Whole arena assigned once");
        Check(!a.Overlaps(b),"Control interiors do not overlap");
        for(int slot=0;slot<2;slot++)
        {
            Rect area=ui.ControlAreaRect(slot),ready=ui.ReadyScreenRect(slot);
            Check(ready.xMin>=area.xMin&&ready.xMax<=area.xMax&&ready.yMin>=area.yMin&&ready.yMax<=area.yMax,"Ready button entirely inside own real control region");
            for(int hero=0;hero<4;hero++){Rect card=ui.CharacterScreenRect(slot,hero);Check(card.xMin>=area.xMin&&card.xMax<=area.xMax&&card.yMin>=area.yMin&&card.yMax<=area.yMax,"Every hero card within own region");}
        }
        // Real character handler, with only texture acquisition stubbed.
        Click(ui,ui.ReadyScreenRect(0).center);Check(ui.Game.Players[0].Ready&&!ui.Game.Players[1].Ready,"First slot independently ready");
        Click(ui,ui.CharacterScreenRect(0,3).center);Check(!ui.Game.Players[0].Ready&&!ui.Game.Players[1].Ready,"Changing own hero clears own readiness");
        Click(ui,ui.ReadyScreenRect(1).center);Check(!ui.Game.Players[0].Ready&&ui.Game.Players[1].Ready,"Second slot independently ready");
        Click(ui,ui.CharacterScreenRect(0,2).center);
        Click(ui,ui.CharacterScreenRect(0,3).center);Check(ui.Game.Players.All(p=>p.CharacterIndex==3)&&ui.Game.Players[1].Ready&&!ui.Game.Players[0].Ready,"Same heroes selectable without clearing other readiness");
        Click(ui,ui.ReadyScreenRect(0).center);Check(ui.Game.Phase=="countdown"&&ui.Game.Players.All(p=>!p.Flipping),"Ready click never becomes flight");
        string activeBefore=Audit.Fingerprint(ui.Game);Call(ui,"OpenTracks");Check(Live(ui)==current&&activeBefore==Audit.Fingerprint(ui.Game)&&ui.FlightMenuPage==-1,"Active round cannot open view menu");
        Set(ui,"showTracks",true);Set(ui,"pendingSharedField",false);Call(ui,"ApplyFlightChoice");Check(Live(ui)==current&&activeBefore==Audit.Fingerprint(ui.Game),"Apply guard rejects active countdown");Set(ui,"showTracks",false);
        Release(ui);RaceSimulation.Advance(ui.Game,3);
        var p0=adjacent?new Vector2(arena.x+1,arena.y+1):new Vector2(arena.center.x,arena.y+1);
        var p1=adjacent?new Vector2(arena.xMax-1,arena.y+1):new Vector2(arena.center.x,arena.yMax-1);
        if(!full)Check(!field.Contains(p0)&&!field.Contains(p1),"Both test points exercise empty margins outside central corridor");
        Devices.Touches=new[]{TouchAt(4,p0,TouchPhase.Began)};Tick(ui);Check(ui.Game.Players[0].Flipping&&!ui.Game.Players[1].Flipping,"Actual Update Began sends only slot0");
        foreach(var phase in new[]{TouchPhase.Moved,TouchPhase.Stationary,TouchPhase.Ended,TouchPhase.Canceled})
        {Devices.Touches=new[]{TouchAt(4,p1,phase)};Tick(ui);Check(!ui.Game.Players[1].Flipping,"Drag/crossing/non-Began does not send to slot1");}
        Devices.Touches=new[]{TouchAt(9,p1,TouchPhase.Began)};Tick(ui);Check(ui.Game.Players[1].Flipping,"Independent second Began acts in own region");Release(ui);
        Set(ui,"paused",true);string frozen=Audit.Fingerprint(ui.Game);Devices.Touches=new[]{TouchAt(4,p0,TouchPhase.Began),TouchAt(9,p1,TouchPhase.Began)};Tick(ui);Check(frozen==Audit.Fingerprint(ui.Game),"Paused native-boundary fixture blocks both contacts and time");Devices.Clear();Set(ui,"paused",false);
        string identity=Live(ui);RaceSimulation.Advance(ui.Game,100);Check(ui.Game.Phase=="finished","Actual current course completes before rematch check");
        Click(ui,p0);Check(ui.Game.Phase=="finished"&&ui.Game.Players[0].Ready&&!ui.Game.Players[1].Ready,"Rematch waits for other player");Click(ui,p1);
        Check(ui.Game.Phase=="countdown"&&ui.Game.Elapsed==0&&ui.Game.Players.All(p=>p.CharacterIndex==3)&&Live(ui)==identity,"Rematch resets round, keeps same heroes and view/seating/height");
        // Dedicated fresh ready state for compatibility catalogue checks.
        var readyState=RaceSimulation.CreateTrial(TracksMixed.Get(),selection.Settings);RaceSimulation.SetCharacter(readyState,0,2);RaceSimulation.SetCharacter(readyState,1,3);typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,readyState);
        OpenView(ui);Choose(ui,0,0);bool savedAdjacent=(bool)Field(ui,"pendingSharedAdjacent"),savedFull=(bool)Field(ui,"pendingSharedFullHeight");Choose(ui,1,savedAdjacent?0:1);Choose(ui,2,savedFull?0:1);
        Check((bool)Field(ui,"pendingSharedAdjacent")==savedAdjacent&&(bool)Field(ui,"pendingSharedFullHeight")==savedFull,"Split disables seating/height edits without clearing remembered choices");
        Click(ui,((Rect)Field(ui,"flightApply")).center);applies++;SameChoice(ui,custom,TracksMixed.Id,choice);Check(!ui.SharedEnabled,"Actual Apply returns to old split view");
        Near(ui.ControlAreaRect(0).height,lanes[0].height,"Split controls use original lane");
        Catalogue(ui,custom,choice);
        layouts.Add(new{width,height,adjacent,full,choice,arena=RectData(arena),field=RectData(field),control0=RectData(a),control1=RectData(b)});
    }
    static object RectData(Rect r)=>new{x=r.x,y=r.y,width=r.width,height=r.height};
    static void Catalogue(FlipRaceGame ui,string custom,int choice)
    {
        Call(ui,"OpenTracks");Click(ui,((Rect)Field(ui,"flightTracks")).center);Check(ui.FlightMenuPage==4,"Full course menu opens");
        foreach(int i in Enumerable.Range(0,5)){Rect card=(Rect)Call(ui,"FullTrackRect",i);Click(ui,card.center);Check((string)Field(ui,"pendingFlightTrack")==RaceTracks.GetAt(i).Id,"All five courses retained by real card handler");SameChoice(ui,custom,TracksMixed.Id,choice);}
        Click(ui,((Rect)Field(ui,"flightBack")).center);Click(ui,((Rect)Field(ui,"flightOldTracks")).center);Check(ui.FlightMenuPage==3,"Legacy section menu opens");
        string[] ids=(string[])typeof(FlipRaceGame).GetField("OldTrackIds",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        for(int i=0;i<ids.Length;i++)
        {string before=(string)Field(ui,"pendingFlightTrack");Click(ui,((Rect)Call(ui,"OldTrackRect",i)).center);Check((string)Field(ui,"pendingFlightTrack")==((ids[i]==TracksControl.UnlimitedId&&ui.Game.Flight.AirRepeats!=AirRepeatLimit.Unlimited)?before:ids[i]),"Old section respects current physical repeat constraint");SameChoice(ui,custom,TracksMixed.Id,choice);}
        string finalOld=(string)Field(ui,"pendingFlightTrack"),view=Live(ui);Click(ui,((Rect)Field(ui,"flightApply")).center);applies++;
        SameChoice(ui,custom,finalOld,choice);Check(Live(ui)==view&&ui.Game.Phase=="ready"&&ui.Game.Players.All(p=>!p.Ready),"Actual old-section Apply preserves view, heroes and settings");
    }
    static void Legacy()
    {
        var ui=Ui(568,320,10);typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,RaceSimulation.CreateGame());Set(ui,"sharedField",true);Set(ui,"sharedAdjacent",true);Set(ui,"sharedFullHeight",true);
        Check(!ui.SharedEnabled&&ui.FieldViewName=="split","Nontrial legacy remains original split even with shared session preferences");
        Release(ui);Pointer(ui,ui.ReadyScreenRect(0).center);Pointer(ui,ui.ReadyScreenRect(1).center);Check(ui.Game.Phase=="countdown","Legacy actual readiness still starts countdown");
    }
    static void Equivalence()
    {
        foreach(int choice in new[]{10,11,-1})
        {
            List<string> baseline=null;
            for(int viewMode=0;viewMode<5;viewMode++)
            {
                equivalenceRuns++;var ui=Ui(568,320,choice);var selection=(FlightSelection)Field(ui,"flightSelection");
                // Custom geometry is arranged before readiness; no gameplay
                // fields are injected. Actual Update receives the same keys.
                var stone=new Obstacle{Id="equivalence-stone",Kind="floor",X=300,Width=64,Height=48};
                var fixture=new Track(2200,new[]{stone}){Id=TracksMixed.Id,RiskZones=new[]{new RiskZone{Id="linked",X=220,Y=226,Radius=8,ObstacleId=stone.Id,ClearX=379}}};
                var game=RaceSimulation.CreateTrial(fixture,selection.Settings);RaceSimulation.SetCharacter(game,0,3);RaceSimulation.SetCharacter(game,1,3);
                typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);Set(ui,"sharedField",viewMode>0);Set(ui,"sharedAdjacent",viewMode>=3);Set(ui,"sharedFullHeight",viewMode==2||viewMode==4);
                Devices.Keys.Add(KeyCode.A);Devices.Keys.Add(KeyCode.L);Tick(ui,0);Devices.Clear();
                Check(game.Phase=="countdown"&&game.Players.All(p=>!p.Flipping),"Actual Update A/L readiness retains semantic mapping in every view");
                while(game.Phase=="countdown")Tick(ui,Math.Min(.1,game.CountdownRemaining));
                var snapshots=new List<string>();
                var art=(CorridorArtwork)RuntimeHelpers.GetUninitializedObject(typeof(CorridorArtwork));Set(art,"jumpRotation",new float[2]);Set(art,"jumpRotationTime",new[]{-1f,-1f});
                foreach(double target in new[]{0d,.901,1.31,1.73,2.3,9.71,10.5,11})
                {
                    while(game.Phase=="racing"&&target-game.Elapsed>1e-9)Tick(ui,Math.Min(.05,target-game.Elapsed));
                    if(target==.901){Devices.Keys.Add(KeyCode.L);Tick(ui,0);Devices.Clear();}
                    string fingerprint=Audit.Fingerprint(game);
                    if(viewMode>0)
                    {
                        Rect field=ui.SharedFieldRect;var projection=SharedFieldView.Create(field.width,field.height,game);
                        for(int slot=0;slot<2;slot++)Call(art,"SharedPlayerGeometry",projection,game,slot,(float)game.Elapsed);
                        Check(fingerprint==Audit.Fingerprint(game),"Renderer geometry read path preserves actual Update state");
                    }
                    snapshots.Add(fingerprint);
                }
                Check(game.Phase=="finished"&&game.Players.All(p=>p.FinishTime.HasValue),"Equivalence fixture reaches both real finishes");
                if(baseline==null)baseline=snapshots;
                else for(int i=0;i<snapshots.Count;i++)Check(snapshots[i]==baseline[i],"Same real Update inputs and renderer geometry retain entire Core state across views at sample"+i);
            }
        }
    }
}
