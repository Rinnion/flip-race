using System;
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
    static int checks,pointers,newTrackSetups,oldTrackSetups,retries,rejections;
    static string context;
    static object Call(object ui,string name,params object[] args)=>ui.GetType().GetMethod(name,Private).Invoke(ui,args);
    static object Field(object ui,string name)=>ui.GetType().GetField(name,Private).GetValue(ui);
    static object Prop(object ui,string name)=>ui.GetType().GetProperty(name,Private).GetValue(ui);
    static void Set(object ui,string name,object value)=>ui.GetType().GetField(name,Private).SetValue(ui,value);
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(context+": "+why);}
    static void Tick(FlipRaceGame ui){Devices.Clear();Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=.01;Call(ui,"Update");}
    static void Click(FlipRaceGame ui,Rect rect){Tick(ui);Call(ui,"Pointer",rect.center);pointers++;}
    static Rect R(FlipRaceGame ui,string field)=>(Rect)Field(ui,field);
    static Rect TrackRect(FlipRaceGame ui,int index)=>(Rect)Call(ui,"FullTrackRect",index);
    static void Apply(FlipRaceGame ui)=>Click(ui,R(ui,"flightApply"));
    static void Open(FlipRaceGame ui){Call(ui,"OpenTracks");Call(ui,"MeasureFlightMenu");Check(ui.FlightMenuPage==0,"real OpenTracks");}
    static void Catalogue(FlipRaceGame ui){Click(ui,R(ui,"flightTracks"));Check(ui.FlightMenuPage==4,"catalogue pointer route");}
    static void Back(FlipRaceGame ui)=>Click(ui,R(ui,"flightBack"));
    static string Pending(FlipRaceGame ui)=>(string)Field(ui,"pendingFlightTrack");
    static bool CanApply(FlipRaceGame ui)=>(bool)Prop(ui,"CanApplyFlight");
    static FlipRaceGame NewUi(int preset)
    {
        Prefs.Clear();Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,preset);
        var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        Set(ui,"lanes",new Rect[2]);Set(ui,"botSessionSeed",123u);Set(ui,"botSeedInitialized",true);
        foreach(string name in new[]{"firstPawAt","secondPawAt","thirdPawAt","fourthPawAt"})Set(ui,name,-10f);
        Call(ui,"InitializeFlightMenu");var selection=(FlightSelection)Field(ui,"flightSelection");
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,RaceSimulation.CreateTrial(RaceTracks.GetAt(0),selection.Settings));
        Set(ui,"selectedTrackId",ui.Game.Track.Id);Set(ui,"lastFrame",Clock.realtimeSinceStartupAsDouble);Call(ui,"Measure");return ui;
    }
    static void ChooseWarning(FlipRaceGame ui,int option)
    {
        Catalogue(ui);Click(ui,TrackRect(ui,5));Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));
        Check(ui.FlightMenuPage==9,"ordinary hidden map opens global timer");Click(ui,(Rect)Call(ui,"HiddenWarningChoiceRect",option));Back(ui);
    }
    static void Setup(int track,int preset,int level,int warningIndex,PartyCamera camera)
    {
        double[] delays={.4,.6,.8,1,1.2};var ui=NewUi(preset);Open(ui);ChooseWarning(ui,warningIndex);
        Click(ui,TrackRect(ui,track));Check(Pending(ui)==RaceTracks.GetAt(track).Id,"exact new map selected without substitution");
        if(track==7){Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));Check(ui.FlightMenuPage==4,"authored timer cannot overwrite global preference");}
        Back(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);
        Check(ui.FlightMenuPage==8&&CanApply(ui),"supported map/profile enables BOT before start");
        for(int i=0;i<5;i++)Check(ui.BotDifficultyRect(i).height>=44,"difficulty remains full-size");
        Click(ui,ui.BotDifficultyRect(level));Click(ui,ui.PartyCameraRect((int)camera));long sequence=ui.BotAttemptSequenceIndex;Apply(ui);
        // No Update occurs between Apply's production Pointer return and these assertions.
        Check(ui.BotEnabled&&ui.Game.Track.Id==RaceTracks.GetAt(track).Id&&ui.Game.BotDifficulty==(BotDifficulty)level,"Apply creates requested map/level");
        Check(ui.Game.HiddenWarningDuration==delays[warningIndex]&&ui.Game.Elapsed==0&&ui.BotPlanningCalls==0,"selected timer applied before first Update or plan");
        Check(ui.Game.StochasticBot.TrackId==ui.Game.Track.Id&&ui.Game.StochasticBot.ProfileId==(preset==FlightSettingsStore.ChosenPresetIndex?"Chosen":"FastFlip"),"initial settings use exact track/profile");
        Check(ui.BotAttemptSequenceIndex==sequence+1&&ui.Game.Phase=="ready"&&!ui.Game.Players[0].Ready&&ui.Game.Players[1].Ready,"one new seed and explicit human start");
        Check(ui.Game.PartyCamera==camera,"camera preserved");
        Click(ui,ui.BotStartRect);
        Check(ui.Game.Phase=="countdown"&&ui.Game.Elapsed==0&&ui.Game.HiddenWarningDuration==delays[warningIndex],"start retains timer without race advancement");
        Tick(ui);Check(ui.Game.HiddenWarningDuration==delays[warningIndex]&&ui.Game.Elapsed==0&&ui.BotPlanningCalls==0,"first countdown Update retains timer");
        // Explicit synthetic terminal fixture isolates result retry wiring from full-route reachability.
        ui.Game.Phase="finished";foreach(var p in ui.Game.Players){p.Status=PlayerStatus.Finished;p.FinishTime=1;}
        uint seed=ui.BotAttemptSeed;Click(ui,ui.BotRetryRect);
        Check(ui.Game.HiddenWarningDuration==delays[warningIndex]&&ui.Game.Elapsed==0&&ui.Game.Phase=="countdown","retry retains global timer before Update");
        Check(ui.BotAttemptSeed!=seed&&ui.BotAttemptSequenceIndex==sequence+2&&ui.BotPlanningCalls==0,"retry creates one fresh seed and empty controller");
        Check(ui.Game.Track.Id==RaceTracks.GetAt(track).Id&&ui.Game.BotDifficulty==(BotDifficulty)level&&ui.Game.PartyCamera==camera,"retry preserves track/level/camera");retries++;
        ui.Game.Phase="ready";Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,track==7?5:7));
        Check((double)Field(ui,"pendingHiddenWarning")==delays[warningIndex],"map change keeps pending global duration");
        if(track==7){Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));Check(ui.FlightMenuPage==9,"leaving authored map restores timer choices");}
        newTrackSetups++;
    }
    static void RejectionsAndHumanCustom()
    {
        var ui=NewUi(FlightSettingsStore.FastFlipPresetIndex);Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,7));Back(ui);
        Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);var previous=ui.Game;long sequence=ui.BotAttemptSequenceIndex;
        Check(Pending(ui)==TracksRethink.Id&&!CanApply(ui),"rethink Fast selected but explicitly unsupported");Apply(ui);
        Check(ReferenceEquals(previous,ui.Game)&&ui.BotAttemptSequenceIndex==sequence,"rethink Fast rejection creates no game or seed");rejections++;
        Back(ui);Click(ui,R(ui,"flightChosen"));Check(CanApply(ui),"explicit Chosen resolves profile restriction");
        Click(ui,R(ui,"flightEdit"));Click(ui,R(ui,"flightDone"));
        Check(((FlightSelection)Field(ui,"flightSelection")).IsCustom&&!CanApply(ui),"equivalent custom remains unsupported in BOT");Apply(ui);
        Check(ReferenceEquals(previous,ui.Game)&&ui.BotAttemptSequenceIndex==sequence,"custom BOT rejection preserves game and seed");rejections++;
        Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.SoloMenuButtonRect);Check(CanApply(ui),"equivalent Chosen custom still allowed in human SOLO");Apply(ui);
        Check(ui.SoloEnabled&&ui.Game.Track.Id==TracksRethink.Id,"human equivalent custom applies");
        Open(ui);Click(ui,ui.ViewMenuButtonRect);int page=ui.FlightMenuPage;Click(ui,ui.BotMenuButtonRect);
        Check(ui.FlightMenuPage==page&&ui.SoloEnabled,"custom cannot enter BOT through mode button");rejections++;
    }
    static void OldMaps()
    {
        foreach(int preset in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex})foreach(int track in Enumerable.Range(0,5))foreach(int level in Enumerable.Range(0,5)) {
            var ui=NewUi(preset);Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,track));Back(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);Click(ui,ui.BotDifficultyRect(level));Apply(ui);
            Check(ui.BotEnabled&&ui.Game.Track.Id==RaceTracks.GetAt(track).Id&&ui.Game.BotDifficulty==(BotDifficulty)level,"original five maps/two profiles/five levels remain enabled");oldTrackSetups++;
        }
    }
    static void Main()
    {
        foreach(var size in new[]{(568,320),(1280,720)})foreach(float dpr in new[]{1f,2f,3f}) {
            Display.width=(int)(size.Item1*dpr);Display.height=(int)(size.Item2*dpr);Display.PixelRatio=dpr;Display.SafeAreaOverride=null;
            foreach(int track in new[]{5,6,7})foreach(int preset in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex}) {
                if(track==7&&preset==FlightSettingsStore.FastFlipPresetIndex)continue;
                foreach(int warning in Enumerable.Range(0,5))foreach(int level in Enumerable.Range(0,5)) {
                    context=size+" DPR"+dpr+" track"+track+" preset"+preset+" warning"+warning+" level"+level;
                    Setup(track,preset,level,warning,(PartyCamera)(level%2));
                }
            }
            context=size+" DPR"+dpr+" rejections";RejectionsAndHumanCustom();
        }
        context="old map compatibility";OldMaps();
        Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pointers,newTrackSetups,oldTrackSetups,retries,rejections,limitations="Production menu/Pointer/Update through established native environment seams. Synthetic terminal fixtures exercise result Retry, not full-track bot reachability. No visual, native browser or physical phone claim."}));
    }
}
