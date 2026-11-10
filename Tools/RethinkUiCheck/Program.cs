using System;
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
    static int checks,pointers,configurations,partyModes,partyBotModes,preliminaryStandaloneModes;
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
    static void Apply(FlipRaceGame ui)
    {
        Click(ui,R(ui,"flightApply"));
        if(ui.FlightMenuPage==12)Click(ui,R(ui,"flightApply"));
    }
    static void Open(FlipRaceGame ui){Call(ui,"OpenTracks");Call(ui,"MeasureFlightMenu");Check(ui.FlightMenuPage==0,"real OpenTracks");}
    static void Catalogue(FlipRaceGame ui){Click(ui,R(ui,"flightTracks"));Check(ui.FlightMenuPage==4,"catalogue pointer route");}
    static void Back(FlipRaceGame ui)=>Click(ui,R(ui,"flightBack"));
    static string Pending(FlipRaceGame ui)=>(string)Field(ui,"pendingFlightTrack");
    static bool CanApply(FlipRaceGame ui)=>(bool)Prop(ui,"CanApplyFlight");
    static bool Inside(Rect a,Rect b)=>a.xMin>=b.xMin-.001&&a.xMax<=b.xMax+.001&&a.yMin>=b.yMin-.001&&a.yMax<=b.yMax+.001;
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
    static void CheckRejection(FlipRaceGame ui,string reason)
    {
        var game=ui.Game;long seed=ui.BotAttemptSequenceIndex;
        Check(!CanApply(ui),reason+" Apply disabled");Apply(ui);
        Check(ReferenceEquals(game,ui.Game)&&ui.BotAttemptSequenceIndex==seed,reason+" preserves Game and seed");
        Check(Pending(ui)==TracksRethink.Id,reason+" preserves selected map");
    }
    static void Layout(FlipRaceGame ui)
    {
        Open(ui);Catalogue(ui);Rect panel=R(ui,"flightPanel"),apply=R(ui,"flightApply"),warning=(Rect)Prop(ui,"HiddenWarningButton");
        Check(RaceTracks.All.Length==8&&RaceTracks.All.Select(t=>t.Id).Distinct().Count()==8,"eight unique catalogue maps");
        var cards=new List<Rect>();for(int i=0;i<8;i++) {
            Rect card=TrackRect(ui,i);Check(Inside(card,panel)&&card.height>=44&&card.width>=44,"whole 44-unit track target");
            Check(!card.Overlaps(apply)&&!card.Overlaps(warning)&&cards.All(c=>!c.Overlaps(card)),"track clear of other cards/settings/footer");
            cards.Add(card);Click(ui,card);Check(Pending(ui)==RaceTracks.GetAt(i).Id,"actual pointer selects exact map "+i);
        }
        Check(Pending(ui)==TracksRethink.Id&&(bool)Prop(ui,"AuthoredHiddenWarning"),"rethink uses authored warning UI");
        Click(ui,warning);Check(ui.FlightMenuPage==4,"authored warning row cannot open global choices");
        Back(ui);
    }
    static void CheckRethink(FlipRaceGame ui,string profile)
    {
        Check(ui.Game.Track.Id==TracksRethink.Id&&TrackPackage.MatchesProfile(profile,ui.Game.Flight),"applied exact map and profile "+profile);
        var hidden=ui.Game.Track.Obstacles.Where(obstacle=>obstacle.Hidden).ToArray();
        Check(hidden.Length==6&&hidden.All(obstacle=>obstacle.HiddenWarningDuration==.35),"all six author warnings remain 0.35 seconds");
    }
    static void ProfileAndModes(FlipRaceGame ui,int preset)
    {
        bool fast=preset==FlightSettingsStore.FastFlipPresetIndex;
        string profile=fast?"FastFlip":"Chosen";
        Check(((FlightSelection)Field(ui,"flightSelection")).PresetIndex==preset&&CanApply(ui),"catalogue retains and admits the selected preset");
        Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.SoloMenuButtonRect);Apply(ui);
        Check(ui.SoloEnabled,"actual SOLO applies");CheckRethink(ui,profile);
        foreach(int count in new[]{2,3,4}) {
            Open(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.PartyMenuButtonRect);Click(ui,ui.PartyCountRect(count-2));Apply(ui);
            Check(ui.PartyEnabled&&ui.Game.HumanCount==count&&ui.Game.Players.Length==count,"actual human Party "+count);
            CheckRethink(ui,profile);partyModes++;
        }
        Open(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);
        Check(CanApply(ui)&&Pending(ui)==TracksRethink.Id,"previous BOT admits exact map/profile");Apply(ui);
        Check(ui.BotEnabled&&ui.Game.StochasticBot.ProfileId==profile,"previous BOT applies chosen profile");CheckRethink(ui,profile);
        Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,0));Check(CanApply(ui),"explicit old BOT map enables Apply");Apply(ui);Check(ui.BotEnabled,"old BOT still starts");
        Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,7));Check(Pending(ui)==TracksRethink.Id&&CanApply(ui),"BOT rethink card selects exact supported map");Back(ui);
        Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.SoloMenuButtonRect);Back(ui);Catalogue(ui);Click(ui,TrackRect(ui,7));Back(ui);
        Click(ui,R(ui,"flightEdit"));Check(ui.FlightMenuPage==1,"explicit Edit creates equivalent custom");Click(ui,R(ui,"flightDone"));
        Check(((FlightSelection)Field(ui,"flightSelection")).IsCustom&&CanApply(ui),"equivalent custom allowed");Apply(ui);
        Check(ui.SoloEnabled,"equivalent custom applies in SOLO");CheckRethink(ui,profile);
        Open(ui);Click(ui,R(ui,"flightEdit"));
        // Gravity index 1 differs from both ready-made profiles.
        Rect gravity=(Rect)Call(ui,"FlightFieldRect",1,1);Rect cell=(Rect)Call(ui,"FlightCell",gravity,1,8);Click(ui,cell);Click(ui,R(ui,"flightDone"));
        CheckRejection(ui,"changed custom");
        Check(((string)Call(ui,"FlightApplyRestriction")).Contains("Основной или Быстрый переворот"),"unsupported custom explains both supported profiles");
        Click(ui,R(ui,fast?"flightFastFlip":"flightChosen"));Check(CanApply(ui),"explicit return to a preset restores support");Apply(ui);
    }
    static void FastBotModes()
    {
        var ui=NewUi(FlightSettingsStore.FastFlipPresetIndex);
        Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,7));Back(ui);
        Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);Click(ui,ui.BotAlgorithmRect(true));
        Check(CanApply(ui)&&Pending(ui)==TracksRethink.Id,"Fast preliminary standalone bot admits rethink");Apply(ui);
        Check(ui.BotEnabled&&ui.Game.UsesPrestartBots&&ui.Game.Phase=="ready"&&!ui.Game.Players[0].Ready,"preliminary standalone bot waits for human start");
        CheckRethink(ui,"FastFlip");preliminaryStandaloneModes++;

        foreach(bool preliminary in new[]{false,true}) {
            Open(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.PartyMenuButtonRect);Click(ui,ui.PartyCountRect(2));
            Click(ui,R(ui,"flightApply"));Check(ui.FlightMenuPage==12,"company opens roster");
            if(!ui.PartyEnabled){Click(ui,ui.PartyRosterRoleRect(1));Click(ui,ui.PartyRosterRoleRect(2));}
            Click(ui,ui.BotAlgorithmRect(preliminary));
            Check(CanApply(ui)&&Pending(ui)==TracksRethink.Id,"Fast company bots admit rethink");Apply(ui);
            Check(ui.PartyEnabled&&ui.Game.UsesPrestartBots==preliminary&&ui.Game.HumanCount==2&&ui.Game.Phase=="ready","mixed company applies requested algorithm and roles");
            Check(ui.Game.IsBotSlot(1)&&ui.Game.IsBotSlot(2)&&!ui.Game.Players[0].Ready&&!ui.Game.Players[3].Ready
                &&ui.ReadyScreenRect(1).width==0&&ui.ReadyScreenRect(2).width==0,"mixed company keeps human readiness and bot slots");
            if(!preliminary)
                Check(ui.Game.GetPartyBotSettings(1)?.ProfileId=="FastFlip"&&ui.Game.GetPartyBotSettings(2)?.ProfileId=="FastFlip","both previous bots retain Fast physics");
            CheckRethink(ui,"FastFlip");partyBotModes++;
        }
    }
    static void WarningPersistence(FlipRaceGame ui)
    {
        double[] values={.4,.6,.8,1,1.2};for(int i=0;i<values.Length;i++) {
            Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,5));Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));
            Check(ui.FlightMenuPage==9,"ordinary hidden map still opens warning choices");Click(ui,(Rect)Call(ui,"HiddenWarningChoiceRect",i));Back(ui);Click(ui,TrackRect(ui,7));
            Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));Check(ui.FlightMenuPage==4,"authored timing cannot change global selection");Apply(ui);
            Check(ui.Game.Track.Id==TracksRethink.Id&&ui.Game.HiddenWarningDuration==values[i],"authored map stores untouched session preference");
            Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,6));Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));
            Check(ui.FlightMenuPage==9&&(double)Field(ui,"pendingHiddenWarning")==values[i],"return to ordinary map restores global choice");Apply(ui);
            Check(ui.Game.HiddenWarningDuration==values[i],"ordinary map applies preserved duration");
        }
    }
    static void Main(string[] args)
    {
        context="install authored tracks";
        string folder=Path.Combine(args[0],"Assets","StreamingAssets","Tracks");
        string manifest=File.ReadAllText(Path.Combine(folder,"index.json"));
        Check(TrackJson.TryReadManifest(manifest,out var index,out var error),"manifest reads: "+error);
        var files=index.Files.ToDictionary(name=>name,name=>File.ReadAllText(Path.Combine(folder,name)),StringComparer.Ordinal);
        Check(TrackCatalog.TryInstallBuiltIns(manifest,files,out error),"atomic JSON install: "+error);
        foreach(var size in new[]{(568,320),(1280,720)})foreach(float dpr in new[]{1f,2f,3f}) {
            context=size+" DPR"+dpr;Display.width=(int)(size.Item1*dpr);Display.height=(int)(size.Item2*dpr);Display.PixelRatio=dpr;Display.SafeAreaOverride=null;
            var fast=NewUi(FlightSettingsStore.FastFlipPresetIndex);Layout(fast);ProfileAndModes(fast,FlightSettingsStore.FastFlipPresetIndex);WarningPersistence(fast);
            var chosen=NewUi(FlightSettingsStore.ChosenPresetIndex);Open(chosen);Catalogue(chosen);Click(chosen,TrackRect(chosen,7));Back(chosen);
            ProfileAndModes(chosen,FlightSettingsStore.ChosenPresetIndex);FastBotModes();configurations++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pointers,configurations,partyModes,partyBotModes,preliminaryStandaloneModes,limitations="Managed production JSON catalogue and menu/Pointer/Update with native environment seams. Preliminary-bot cases exercise selection and ready state. Visual fade, font fit, native browser input and physical phone usability require browser checks."}));
    }
}
