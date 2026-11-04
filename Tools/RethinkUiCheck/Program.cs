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
    static int checks,pointers,configurations,partyModes;
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
    static void ProfileAndModes(FlipRaceGame ui)
    {
        // Start Fast deliberately; never let map selection rewrite its physics.
        Check(((FlightSelection)Field(ui,"flightSelection")).PresetIndex==FlightSettingsStore.FastFlipPresetIndex,"initial Fast choice unchanged by catalogue");
        CheckRejection(ui,"Fast");Click(ui,R(ui,"flightChosen"));Check(CanApply(ui),"explicit Chosen enables map");
        Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.SoloMenuButtonRect);Apply(ui);
        Check(ui.SoloEnabled&&ui.Game.Track.Id==TracksRethink.Id&&TracksRethink.IsSupported(ui.Game.Flight),"actual Chosen SOLO applies rethink");
        foreach(int count in new[]{2,3,4}) {
            Open(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.PartyMenuButtonRect);Click(ui,ui.PartyCountRect(count-2));Apply(ui);
            Check(ui.PartyEnabled&&ui.Game.Players.Length==count&&ui.Game.Track.Id==TracksRethink.Id,"actual rethink Party "+count);partyModes++;
        }
        Open(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);
        Check(CanApply(ui)&&Pending(ui)==TracksRethink.Id,"Chosen rethink BOT now supported without map substitution");Apply(ui);
        Check(ui.BotEnabled&&ui.Game.Track.Id==TracksRethink.Id,"actual rethink Chosen BOT applies");
        Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,0));Check(CanApply(ui),"explicit old BOT map enables Apply");Apply(ui);Check(ui.BotEnabled,"old BOT still starts");
        Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,7));Check(Pending(ui)==TracksRethink.Id&&CanApply(ui),"BOT rethink card selects exact supported map");Back(ui);
        Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.SoloMenuButtonRect);Back(ui);Catalogue(ui);Click(ui,TrackRect(ui,7));Back(ui);
        Click(ui,R(ui,"flightEdit"));Check(ui.FlightMenuPage==1,"explicit Edit creates equivalent Chosen custom");Click(ui,R(ui,"flightDone"));
        Check(((FlightSelection)Field(ui,"flightSelection")).IsCustom&&CanApply(ui),"equivalent Chosen custom allowed");Apply(ui);
        Check(ui.SoloEnabled&&ui.Game.Track.Id==TracksRethink.Id,"equivalent custom applies through real menu");
        Open(ui);Click(ui,R(ui,"flightEdit"));
        // Edit via production field-pointer path: gravity scale 0 -> 1.
        Rect gravity=(Rect)Call(ui,"FlightFieldRect",1,1);Rect cell=(Rect)Call(ui,"FlightCell",gravity,1,8);Click(ui,cell);Click(ui,R(ui,"flightDone"));
        CheckRejection(ui,"changed custom");
        Click(ui,R(ui,"flightChosen"));Check(CanApply(ui),"explicit return to Chosen restores support");Apply(ui);
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
    static void Main()
    {
        foreach(var size in new[]{(568,320),(1280,720)})foreach(float dpr in new[]{1f,2f,3f}) {
            context=size+" DPR"+dpr;Display.width=(int)(size.Item1*dpr);Display.height=(int)(size.Item2*dpr);Display.PixelRatio=dpr;Display.SafeAreaOverride=null;
            var ui=NewUi(FlightSettingsStore.FastFlipPresetIndex);Layout(ui);ProfileAndModes(ui);WarningPersistence(ui);configurations++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pointers,configurations,partyModes,limitations="Managed production menu/Pointer/Update with native environment seams. Does not establish visual fade, font fit, native browser input or physical phone usability."}));
    }
}
