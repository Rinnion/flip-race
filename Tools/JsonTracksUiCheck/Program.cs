using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    static int checks,pointers,botPairs;
    static string context="setup";
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(context+": "+why);}
    static object Call(object ui,string name,params object[] args)=>ui.GetType().GetMethod(name,Private).Invoke(ui,args);
    static object Field(object ui,string name)=>ui.GetType().GetField(name,Private).GetValue(ui);
    static object Prop(object ui,string name)=>ui.GetType().GetProperty(name,Private).GetValue(ui);
    static void Set(object ui,string name,object value)=>ui.GetType().GetField(name,Private).SetValue(ui,value);
    static Rect R(FlipRaceGame ui,string name)=>(Rect)Field(ui,name);
    static Rect Slot(FlipRaceGame ui,int slot)=>(Rect)Call(ui,"FullTrackRect",slot);
    static void Tick(FlipRaceGame ui){Devices.Clear();Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=.01;Call(ui,"Update");}
    static void Click(FlipRaceGame ui,Rect rect){Tick(ui);Call(ui,"Pointer",rect.center);pointers++;}
    static void Apply(FlipRaceGame ui)=>Click(ui,R(ui,"flightApply"));
    static void Back(FlipRaceGame ui)=>Click(ui,R(ui,"flightBack"));
    static string Pending(FlipRaceGame ui)=>(string)Field(ui,"pendingFlightTrack");
    static bool CanApply(FlipRaceGame ui)=>(bool)Prop(ui,"CanApplyFlight");
    static bool Inside(Rect a,Rect b)=>a.xMin>=b.xMin-.001&&a.xMax<=b.xMax+.001&&a.yMin>=b.yMin-.001&&a.yMax<=b.yMax+.001;
    static FlipRaceGame NewUi(int preset=FlightSettingsStore.ChosenPresetIndex)
    {
        Prefs.Clear();Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,preset);
        var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        Set(ui,"lanes",new Rect[2]);Set(ui,"botSessionSeed",123u);Set(ui,"botSeedInitialized",true);
        foreach(string name in new[]{"firstPawAt","secondPawAt","thirdPawAt","fourthPawAt"})Set(ui,name,-10f);
        Call(ui,"InitializeFlightMenu");var selection=(FlightSelection)Field(ui,"flightSelection");
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,RaceSimulation.CreateTrial(RaceTracks.GetAt(0),selection.Settings));
        Set(ui,"selectedTrackId",ui.Game.Track.Id);Set(ui,"lastFrame",Clock.realtimeSinceStartupAsDouble);Call(ui,"Measure");return ui;
    }
    static void Open(FlipRaceGame ui){Call(ui,"OpenTracks");Call(ui,"MeasureFlightMenu");}
    static void Catalogue(FlipRaceGame ui){Click(ui,R(ui,"flightTracks"));Check(ui.FlightMenuPage==4,"catalogue real pointer route");}
    static string LocalJson(string id,bool authored=false)
    {
        var track=new Track(1400,new[]{new Obstacle{Id=id+"-stone",Kind="floor",X=700,Width=80,Height=64,Hidden=true,
            HiddenRevealDistance=authored?(double?)220:null,HiddenWarningDuration=authored?(double?).35:null}}){Id=id,Name="Локальная "+id};
        return TrackJson.Write(new TrackPackage(track,authored?TrackProfilePolicy.ChosenOnly:TrackProfilePolicy.Any,authored?TrackWarningMode.Authored:TrackWarningMode.Selected));
    }
    static void BootGate()
    {
        context="bootstrap gate";var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        var load=(IEnumerator)Call(ui,"LoadTrackCatalog");Check(load.MoveNext(),"loader yields actual fetch before constructing Game");
        Check(ui.Game==null&&(bool)Field(ui,"catalogLoading"),"no playable C# fallback while requests pending");
        Call(ui,"CatalogLoadFailed","Нет index.json");Tick(ui);
        Check(ui.Game==null&&!(bool)Field(ui,"catalogLoading")&&(string)Field(ui,"catalogLoadMessage")=="Нет index.json","load failure retains error and no Game; Update null gate safe");
        load.DisposeIfPossible();
    }
    static void DisposeIfPossible(this IEnumerator enumerator){if(enumerator is IDisposable d)d.Dispose();}
    static void BuiltInLayout()
    {
        context="eight builtins";var ui=NewUi();Open(ui);Catalogue(ui);Rect panel=R(ui,"flightPanel");
        Check(RaceTracks.All.Length==8&&TrackCatalog.Entries.Length==8,"eight JSON entries installed before play");
        var cards=new List<Rect>();for(int i=0;i<8;i++) {
            Rect card=Slot(ui,i);float expectedWidth=(panel.width-48)/3;
            Check(Math.Abs(card.x-(panel.x+16+i%3*(expectedWidth+8)))<.001&&Math.Abs(card.y-(panel.y+88+i/3*49))<.001,"existing eight card positions preserved");
            Check(Inside(card,panel)&&card.height==44&&cards.All(c=>!c.Overlaps(card))&&!card.Overlaps(R(ui,"flightApply")),"whole disjoint card");cards.Add(card);
            Click(ui,card);Check(Pending(ui)==RaceTracks.GetAt(i).Id,"exact loaded track selected");
        }
        Rect import=ui.TrackImportRect;Check(import==Slot(ui,8)&&import.height==44&&Inside(import,panel)&&cards.All(c=>!c.Overlaps(import)),"ninth cell reserved for import");
        Check((int)Prop(ui,"TrackPageCount")==1,"eight tracks one page");
    }
    static void ImportAndModes(string suffix)
    {
        context="session imports "+suffix;var ui=NewUi();Open(ui);Catalogue(ui);
        Click(ui,Slot(ui,5));Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));Click(ui,(Rect)Call(ui,"HiddenWarningChoiceRect",1));Back(ui);
        string id="local-ui-"+suffix;string json=LocalJson(id);var before=ui.Game;
        ui.ImportTrackFile(json);
        Check(ReferenceEquals(before,ui.Game)&&Pending(ui)==id&&ui.FlightMenuPage==4,"successful import selects local card without replacing race");
        Check(TrackCatalog.TryGet(id,out var entry)&&entry.Origin==TrackOrigin.Local,"local provenance preserved");
        Check((int)Field(ui,"trackCatalogPage")==1&&RaceTracks.All.Length>=9,"import opens extra page");
        Check(ui.TrackImportStatus.Contains("Только в этой сессии"),"explicit session-only success notice");
        Rect panel=R(ui,"flightPanel"),prev=(Rect)Prop(ui,"TrackPreviousPageRect"),next=(Rect)Prop(ui,"TrackNextPageRect");
        Check(prev.height==44&&next.height==44&&Inside(prev,panel)&&Inside(next,panel)&&!prev.Overlaps(next)&&!prev.Overlaps(R(ui,"flightApply"))&&!next.Overlaps(ui.TrackImportRect),"paging controls 44 and disjoint");
        Click(ui,prev);Check((int)Field(ui,"trackCatalogPage")==0,"previous real pointer");Click(ui,next);Check((int)Field(ui,"trackCatalogPage")==1,"next real pointer");
        Back(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);long seed=ui.BotAttemptSequenceIndex;
        Check(Pending(ui)==id&&!CanApply(ui),"imported BOT blocked without changing selected map");Apply(ui);
        Check(ReferenceEquals(before,ui.Game)&&ui.BotAttemptSequenceIndex==seed,"rejected imported BOT creates no game or seed");
        Click(ui,ui.SoloMenuButtonRect);Apply(ui);Check(ui.SoloEnabled&&ui.Game.Track.Id==id&&ui.Game.HiddenWarningDuration==.6,"imported SOLO applies with timer preserved");
        ui.Game.Phase="finished";ui.Game.Players[0].Status=PlayerStatus.Finished;ui.Game.Players[0].FinishTime=1;Tick(ui);Click(ui,ui.SoloRetryRect);
        Check(ui.Game.Phase=="countdown"&&ui.Game.Track.Id==id&&ui.Game.HiddenWarningDuration==.6,"local retry retains actual track and setting");ui.Game.Phase="ready";
        Open(ui);Catalogue(ui);int count=TrackCatalog.Entries.Length;var game=ui.Game;ui.ImportTrackFile(json);
        Check(TrackCatalog.Entries.Length==count&&ReferenceEquals(game,ui.Game)&&ui.FlightMenuPage==10&&!string.IsNullOrEmpty(ui.TrackImportStatus),"duplicate rejected with visible error without mutation");
        Back(ui);ui.ImportTrackFile("{");Check(ui.FlightMenuPage==10&&TrackCatalog.Entries.Length==count&&ReferenceEquals(game,ui.Game),"malformed JSON preserves catalog and race");
        Back(ui);ui.ImportTrackError("Ошибка чтения <не HTML>");Check(ui.FlightMenuPage==10&&ui.TrackImportStatus=="Ошибка чтения <не HTML>","file-read error becomes visible literal message");Back(ui);
        string authoredId=id+"-authored";ui.ImportTrackFile(LocalJson(authoredId,true));
        Check((bool)Prop(ui,"AuthoredHiddenWarning"),"warning policy from imported metadata");Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));Check(ui.FlightMenuPage==4,"authored local timing disables global choices");
        Back(ui);Click(ui,R(ui,"flightFastFlip"));Check(!CanApply(ui),"chosen-only local metadata blocks Fast");Apply(ui);Check(ReferenceEquals(game,ui.Game),"unsupported profile leaves race unchanged");
        Click(ui,R(ui,"flightChosen"));Apply(ui);Check(ui.Game.Track.Id==authoredId&&ui.Game.HiddenWarningDuration==.6,"explicit supported profile preserves global timer on authored local map");
        Open(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.PartyMenuButtonRect);Click(ui,ui.PartyCountRect(2));Apply(ui);
        Check(ui.PartyEnabled&&ui.Game.Players.Length==4&&ui.Game.Track.Id==authoredId,"local human party uses same loaded track");
        ui.Game.Phase="racing";Set(ui,"gameplayInputBlocked",false);count=TrackCatalog.Entries.Length;game=ui.Game;
        ui.ImportTrackFile(LocalJson(id+"-late"));ui.ImportTrackError("Поздняя ошибка чтения");
        Check(ReferenceEquals(game,ui.Game)&&TrackCatalog.Entries.Length==count&&!(bool)Field(ui,"gameplayInputBlocked"),"late file callbacks cannot import or suppress gameplay input");
    }
    static void BotCompatibility()
    {
        context="15 builtin BOT pairs";for(int index=0;index<8;index++)foreach(int preset in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex}) {
            var ui=NewUi(preset);Open(ui);Catalogue(ui);Click(ui,Slot(ui,index));Back(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);
            bool expected=index!=7||preset==FlightSettingsStore.ChosenPresetIndex;
            Check(CanApply(ui)==expected,"metadata exact-profile BOT guard");var game=ui.Game;Apply(ui);
            if(expected){Check(ui.BotEnabled&&ui.Game.Track.Id==RaceTracks.GetAt(index).Id,"actual JSON builtin bot factory selected exact map");botPairs++;}
            else Check(ReferenceEquals(game,ui.Game),"Rethink Fast still rejected");
        }
        foreach(var id in new[]{TracksV4.OpenId,TracksV4.ChallengeId,TracksControl.ChallengeId,TracksControl.UnlimitedId,TracksTune.ChallengeId,TracksFull.PracticeId,TracksRhythm.PracticeId})
            Check(TrackCatalog.SupportsProfile(id,FlightPresets.Chosen),"legacy practice profile support retained "+id);
    }
    static void Main(string[] args)
    {
        Display.width=568;Display.height=320;Display.PixelRatio=1;BootGate();
        string folder=Path.Combine(args[0],"Assets","StreamingAssets","Tracks"),manifest=File.ReadAllText(Path.Combine(folder,"index.json"));
        Check(TrackJson.TryReadManifest(manifest,out var index,out var error),"manifest reads: "+error);
        var files=index.Files.ToDictionary(name=>name,name=>File.ReadAllText(Path.Combine(folder,name)),StringComparer.Ordinal);
        Check(TrackCatalog.TryInstallBuiltIns(manifest,files,out error),"atomic JSON install: "+error);BuiltInLayout();BotCompatibility();ImportAndModes("compact");
        Display.width=2560;Display.height=1440;Display.PixelRatio=2;Display.SafeAreaOverride=null;ImportAndModes("desktop");
        Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pointers,botPairs,entries=TrackCatalog.Entries.Length,limitations="Actual menu/Pointer/Update/import callbacks with established native environment seams. Loader pre-fetch/error gate is managed; real URL fetch, native file chooser, rendering and phone usability require browser verification. Retry uses explicit synthetic terminal fixture."}));
    }
}
