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
    static int checks,pointers,layouts,settings,modes;
    static string context;
    static object Call(object ui,string name,params object[] args)=>ui.GetType().GetMethod(name,Private).Invoke(ui,args);
    static object Field(object ui,string name)=>ui.GetType().GetField(name,Private).GetValue(ui);
    static object Prop(object ui,string name)=>ui.GetType().GetProperty(name,Private).GetValue(ui);
    static void Set(object ui,string name,object value)=>ui.GetType().GetField(name,Private).SetValue(ui,value);
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(context+": "+why);}
    static void Tick(FlipRaceGame ui){Devices.Clear();Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=.01;Call(ui,"Update");}
    // Production top-level Pointer consumes menu input before gameplay.
    static void Click(FlipRaceGame ui,Rect rect){Tick(ui);Call(ui,"Pointer",rect.center);pointers++;}
    static Rect R(FlipRaceGame ui,string field)=>(Rect)Field(ui,field);
    static Rect TrackRect(FlipRaceGame ui,int index)=>(Rect)Call(ui,"FullTrackRect",index);
    static void Apply(FlipRaceGame ui)=>Click(ui,R(ui,"flightApply"));
    static void Open(FlipRaceGame ui){Call(ui,"OpenTracks");Call(ui,"MeasureFlightMenu");Check(ui.FlightMenuPage==0,"real OpenTracks");}
    static void Catalogue(FlipRaceGame ui){Click(ui,R(ui,"flightTracks"));Check(ui.FlightMenuPage==4,"catalogue pointer route");}
    static void Back(FlipRaceGame ui)=>Click(ui,R(ui,"flightBack"));
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
    static void LayoutAndCards(FlipRaceGame ui)
    {
        Open(ui);Catalogue(ui);Rect panel=R(ui,"flightPanel"),apply=R(ui,"flightApply"),warning=(Rect)Prop(ui,"HiddenWarningButton");
        Check(RaceTracks.All.Length==8&&RaceTracks.All.Select(t=>t.Id).Distinct().Count()==8,"eight unique catalogue maps; legacy seven selection cases retained below");
        var cards=new List<Rect>();
        for(int i=0;i<7;i++) {
            Rect card=TrackRect(ui,i);Check(Inside(card,panel)&&card.height>=44&&card.width>=44,"whole 44-unit track target");
            Check(!card.Overlaps(apply)&&!card.Overlaps(warning)&&cards.All(c=>!c.Overlaps(card)),"track target disjoint from cards/settings/footer");
            cards.Add(card);Click(ui,card);Check((string)Field(ui,"pendingFlightTrack")==RaceTracks.GetAt(i).Id,"actual card selects exact track");
        }
        Check(ui.Game.Elapsed==0&&ui.Game.Players.All(p=>p.Distance==0&&!p.Ready),"catalogue taps never start or move gameplay");
        Click(ui,warning);Check(ui.FlightMenuPage==9,"warning page pointer route");
        cards.Clear();for(int i=0;i<5;i++) {
            Rect card=(Rect)Call(ui,"HiddenWarningChoiceRect",i);Check(Inside(card,panel)&&card.height>=44&&card.width>=44,"whole warning target");
            Check(!card.Overlaps(apply)&&cards.All(c=>!c.Overlaps(card)),"warning target disjoint");cards.Add(card);
        }
        Back(ui);Check(ui.FlightMenuPage==4,"warning Back returns catalogue");Back(ui);layouts++;
    }
    static void WarningAndSolo(FlipRaceGame ui)
    {
        double[] values={.4,.6,.8,1,1.2};string flight=JsonSerializer.Serialize(ui.Game.Flight,new JsonSerializerOptions{IncludeFields=true});
        for(int i=0;i<values.Length;i++) {
            Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,5+i%2));Click(ui,(Rect)Prop(ui,"HiddenWarningButton"));
            Click(ui,(Rect)Call(ui,"HiddenWarningChoiceRect",i));Check((double)Field(ui,"pendingHiddenWarning")==values[i],"warning selected via pointer");
            Back(ui);Back(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.SoloMenuButtonRect);Apply(ui);
            Check(ui.SoloEnabled&&TracksHidden.IsNew(ui.Game.Track.Id)&&ui.Game.HiddenWarningDuration==values[i],"SOLO applies new map and warning after final factory");
            Check(JsonSerializer.Serialize(ui.Game.Flight,new JsonSerializerOptions{IncludeFields=true})==flight,"warning leaves profile physics unchanged");
            // Synthetic completion tests result Retry routing, not reachability.
            ui.Game.Phase="finished";ui.Game.Players[0].Status=PlayerStatus.Finished;ui.Game.Players[0].FinishTime=1;Tick(ui);
            Click(ui,ui.SoloRetryRect);Check(ui.Game.Phase=="countdown"&&ui.Game.Elapsed==0&&ui.Game.HiddenWarningDuration==values[i],"actual SOLO Retry retains warning and resets attempt");
            ui.Game.Phase="ready";
            Open(ui);Check((double)Field(ui,"pendingHiddenWarning")==values[i],"menu reopen retains active value");Catalogue(ui);Click(ui,TrackRect(ui,(i+1)%5));Apply(ui);
            Check(ui.Game.HiddenWarningDuration==values[i]&&!TracksHidden.IsNew(ui.Game.Track.Id),"track change retains session warning");settings++;
        }
    }
    static void BotAndParty(FlipRaceGame ui)
    {
        for(int map=5;map<7;map++) {
            Open(ui);Catalogue(ui);Click(ui,TrackRect(ui,map));Back(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.BotMenuButtonRect);
            string id=RaceTracks.GetAt(map).Id;var before=ui.Game;long seedIndex=ui.BotAttemptSequenceIndex;
            Check(ui.FlightMenuPage==8&&(string)Field(ui,"pendingFlightTrack")==id&&!(bool)Prop(ui,"CanApplyFlight"),"new map retained and BOT Apply disabled");
            Apply(ui);Check(ReferenceEquals(before,ui.Game)&&ui.BotAttemptSequenceIndex==seedIndex,"disabled Apply creates no game or seed");
            Back(ui);Catalogue(ui);Click(ui,TrackRect(ui,map==5?6:5));Check((string)Field(ui,"pendingFlightTrack")==id,"disabled new BOT card ignores pointer");
            Click(ui,TrackRect(ui,0));Check((bool)Prop(ui,"CanApplyFlight"),"explicit old card enables BOT Apply");Apply(ui);
            Check(ui.BotEnabled&&ui.Game.Track.Id==RaceTracks.GetAt(0).Id,"explicit old map starts BOT");
            double warning=ui.Game.HiddenWarningDuration;ui.Game.Phase="finished";foreach(var p in ui.Game.Players){p.Status=PlayerStatus.Finished;p.FinishTime=1;}Tick(ui);
            Click(ui,ui.BotRetryRect);Check(ui.Game.Phase=="countdown"&&ui.Game.HiddenWarningDuration==warning,"BOT Retry retains warning");ui.Game.Phase="ready";
            foreach(int count in new[]{2,3,4}) {
                Open(ui);Click(ui,ui.ViewMenuButtonRect);Click(ui,ui.PartyMenuButtonRect);Click(ui,ui.PartyCountRect(count-2));Back(ui);Catalogue(ui);Click(ui,TrackRect(ui,map));Apply(ui);
                Check(ui.PartyEnabled&&ui.Game.Players.Length==count&&ui.Game.Track.Id==id,"new map applies human party count "+count);modes++;
            }
        }
    }
    static void Main()
    {
        foreach(var size in new[]{(568,320),(1280,720)})foreach(float dpr in new[]{1f,2f,3f})foreach(int preset in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex}) {
            context=size+" DPR"+dpr+" preset"+preset;Display.width=(int)(size.Item1*dpr);Display.height=(int)(size.Item2*dpr);Display.PixelRatio=dpr;Display.SafeAreaOverride=null;
            var ui=NewUi(preset);Check(ui.Game.HiddenWarningDuration==.8,"default warning .8");LayoutAndCards(ui);WarningAndSolo(ui);BotAndParty(ui);
        }
        Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pointers,layouts,settings,partyModes=modes,limitations="Managed real menu/Pointer/Update methods with native environment seams; synthetic terminal fixtures test Retry only. Does not establish visual rendering, native browser input or phone usability."}));
    }
}
