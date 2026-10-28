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
    static int checks,setups,layouts,updates,pointers,inputCases,fullRaces,continuedAfterFirst,inFlightPauses;
    static string context="setup";
    static readonly List<object> Evidence=new List<object>();
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(context+": "+why);}
    static object Call(object ui,string name,params object[] args)=>ui.GetType().GetMethod(name,Private).Invoke(ui,args);
    static object Field(object ui,string name)=>ui.GetType().GetField(name,Private).GetValue(ui);
    static object Prop(object ui,string name)=>ui.GetType().GetProperty(name,Private).GetValue(ui);
    static void Set(object ui,string name,object value)=>ui.GetType().GetField(name,Private).SetValue(ui,value);
    static string State(object value)=>JsonSerializer.Serialize(value,new JsonSerializerOptions{IncludeFields=true});
    static void Near(double a,double b,string why,double tolerance=.003)=>Check(Math.Abs(a-b)<=tolerance,why+" "+a+" != "+b);
    static bool Inside(Rect a,Rect b)=>a.xMin>=b.xMin-.003&&a.xMax<=b.xMax+.003&&a.yMin>=b.yMin-.003&&a.yMax<=b.yMax+.003;
    static float[] Box(Rect r)=>new[]{r.x,r.y,r.width,r.height};
    static void Tick(FlipRaceGame ui,double dt=0){Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=dt;Call(ui,"Update");updates++;}
    static void Click(FlipRaceGame ui,Vector2 point){Devices.Clear();Tick(ui);Call(ui,"Pointer",point);pointers++;}
    static void Apply(FlipRaceGame ui)=>Click(ui,((Rect)Field(ui,"flightApply")).center);
    static Touch TouchAt(Vector2 point,TouchPhase phase)=>new Touch{fingerId=7,position=new Vector2(point.x*Display.PixelRatio,Display.height-point.y*Display.PixelRatio),phase=phase};
    static string Preferences()
    {
        var values=new SortedDictionary<string,string>();foreach(string name in new[]{"Strings","Ints"})foreach(DictionaryEntry item in (IDictionary)typeof(Prefs).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null))values[name+":"+item.Key]=item.Value.ToString();return State(values);
    }
    static void Screen(int w,int h,float dpr)
    {Display.width=Mathf.RoundToInt(w*dpr);Display.height=Mathf.RoundToInt(h*dpr);Display.PixelRatio=dpr;Display.SafeAreaOverride=new Rect(12*dpr,10*dpr,(w-28)*dpr,(h-24)*dpr);}
    static FlipRaceGame NewUi(int preset=FlightSettingsStore.ChosenPresetIndex,bool clear=true)
    {
        Devices.Clear();if(clear){Prefs.Clear();var draft=FlightDraft.From(FlightPresets.FastFlip);draft.Repeats=AirRepeatLimit.One;draft.Gravity=2;draft.Speed=3;Prefs.SetString(FlightSettingsStore.Key,FlightSettingsStore.Encode(draft));Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,preset);Prefs.SetInt("flip-race-sound",0);}
        Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=1;var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));Set(ui,"lanes",new Rect[2]);foreach(string name in new[]{"firstPawAt","secondPawAt","thirdPawAt","fourthPawAt"})Set(ui,name,-10f);
        Call(ui,"InitializeFlightMenu");var selection=(FlightSelection)Field(ui,"flightSelection");typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,RaceSimulation.CreateTrial(RaceTracks.GetAt(0),selection.Settings));Set(ui,"selectedTrackId",ui.Game.Track.Id);Set(ui,"lastFrame",Clock.realtimeSinceStartupAsDouble);Call(ui,"Measure");return ui;
    }
    static void Enter(FlipRaceGame ui,int track,int level,PartyCamera camera)
    {
        Call(ui,"OpenTracks");Check(ui.FlightMenuPage==0,"OpenTracks real menu");Call(ui,"MeasureFlightMenu");Click(ui,((Rect)Field(ui,"flightTracks")).center);Check(ui.FlightMenuPage==4,"full-track list");Click(ui,((Rect)Call(ui,"FullTrackRect",track)).center);Click(ui,((Rect)Field(ui,"flightBack")).center);Check(ui.FlightMenuPage==0,"track selected returns menu");Click(ui,ui.ViewMenuButtonRect.center);Click(ui,ui.BotMenuButtonRect.center);Check(ui.FlightMenuPage==8,"independent BOT route");
        for(int i=0;i<5;i++)Check(Inside(ui.BotDifficultyRect(i),(Rect)Field(ui,"flightPanel"))&&ui.BotDifficultyRect(i).height>=44,"all five visible level choices");
        Click(ui,ui.BotDifficultyRect(level).center);Click(ui,ui.PartyCameraRect((int)camera).center);Apply(ui);Devices.Clear();Tick(ui);
        Check(ui.BotEnabled&&!ui.SoloEnabled&&!ui.PartyEnabled&&ui.Game.Players.Length==2,"isolated human+bot");Check(ui.Game.Phase=="ready"&&!ui.Game.Players[0].Ready&&ui.Game.Players[1].Ready,"bot auto ready, human awaits start");Check(ui.Game.BotDifficulty==(BotDifficulty)level&&ui.Game.Track.Id==RaceTracks.GetAt(track).Id&&ui.Game.PartyCamera==camera,"actual menu applies track level camera");Check(ui.Game.Players[0].CharacterIndex!=ui.Game.Players[1].CharacterIndex,"distinct bot identity");Check(ui.BotInputCursor==0&&ui.Game.BotSeed==0,"new choice clean cursor/seed");setups++;
    }
    static void Layout(FlipRaceGame ui,bool selectHeroes=false)
    {
        layouts++;Rect arena=(Rect)Field(ui,"arena"),safe=(Rect)Call(ui,"SafeUiArea"),bar=ui.SessionBarRect,field=ui.SharedFieldRect,paw=ui.PawScreenRect(0);
        Check(!bar.Overlaps(field),"toolbar outside world");Near(bar.yMax,field.yMin,"toolbar directly above field");Check(Inside(paw,safe)&&!paw.Overlaps(bar)&&!paw.Overlaps(field),"BR paw wholly in safe area and clear of world/bar");Near(paw.width,56,"paw width56");Near(paw.height,56,"paw height56");Near(paw.xMax,safe.xMax-(Display.height/Display.PixelRatio<=600?40:8),"safe right paw inset");Near(paw.yMax,safe.yMax-8,"safe bottom paw inset");
        Check(ui.PawScreenRect(1).width==0&&ui.CharacterScreenRect(1,0).width==0&&ui.ReadyScreenRect(1).width==0,"only human has paw");
        foreach(string name in new[]{"soundRect","pauseRect","fullscreenRect"})Check(Inside((Rect)Field(ui,name),bar),"toolbar tool contained "+name);
        var view=SharedFieldView.Create(field.width,field.height,ui.Game);Near(view.ScaleX,view.ScaleY,"uniform projection");Near(view.CameraLeft,ui.Game.PartyCameraLeft,"actual camera");Near(ui.Game.PartyWindowWidth,1520,"logicalwindow1520");
        if(ui.Game.Phase=="ready")
        {
            var cards=new List<Rect>();for(int hero=0;hero<6;hero++){Rect card=ui.BotCharacterRect(hero);Check(Inside(card,arena)&&card.width>=44&&card.height>=44,"six whole44cards");Check(!card.Overlaps(bar)&&!card.Overlaps(ui.BotStartRect)&&cards.All(c=>!c.Overlaps(card)),"cards independent/bar/start clear");cards.Add(card);if(selectHeroes){Click(ui,card.center);Check(ui.Game.Players[0].CharacterIndex==hero&&!ui.Game.Players[0].Ready&&ui.Game.Players[1].Ready&&ui.Game.Players[1].CharacterIndex!=hero,"actual card selects human only, bot ready/different");}}
            Check(Inside(ui.BotStartRect,arena)&&!ui.BotStartRect.Overlaps(bar)&&ui.BotStartRect.height>=44,"whole start44");Check(!ui.PawsVisible,"selection paw hidden");
        }
        if(ui.Game.Phase=="countdown")Check(Inside(ui.SoloCountdownRect,arena)&&!ui.SoloCountdownRect.Overlaps(bar)&&!ui.SoloCountdownRect.Overlaps(field),"countdown visible outside toolbar/world");
        if(ui.Game.Phase=="finished")foreach(var button in new[]{ui.BotRetryRect,ui.BotTrackChoiceRect})Check(Inside(button,arena)&&!button.Overlaps(bar),"actual result buttons visible");
        string before=State(ui.Game);int cursor=ui.BotInputCursor;int oldW=Display.width,oldH=Display.height;Display.width+=160;Display.height+=80;Call(ui,"Measure");Check(State(ui.Game)==before&&ui.BotInputCursor==cursor,"resize preserves world and bot cursor");Display.width=oldW;Display.height=oldH;Call(ui,"Measure");
    }
    static void Start(FlipRaceGame ui)
    {
        Click(ui,ui.BotStartRect.center);Check(ui.Game.Phase=="countdown"&&!ui.PawsVisible,"one human start immediately countdown");Layout(ui);Devices.Clear();Tick(ui);string state=State(ui.Game);int cursor=ui.BotInputCursor;Devices.Keys.UnionWith(new[]{KeyCode.A,KeyCode.L,KeyCode.S,KeyCode.K});Devices.Touches=new[]{TouchAt(ui.PawScreenRect(0).center,TouchPhase.Began)};Tick(ui);Check(State(ui.Game)==state&&ui.BotInputCursor==cursor,"countdown input has no effects");Devices.Clear();while(ui.Game.Phase=="countdown")Tick(ui,Math.Min(.1,ui.Game.CountdownRemaining));Check(ui.Game.Phase=="racing"&&ui.PawsVisible,"actual Update completes countdown");Check(ui.BotInputCursor==0&&ui.Game.Players[1].Distance<1e-6,"countdown doesn't advance bot edges");Layout(ui);
    }
    static void Fresh(FlipRaceGame ui){Call(ui,"ResetBotRace",ui.Game.BotSeed);Devices.Clear();Tick(ui);Start(ui);}
    static void Inputs(FlipRaceGame ui)
    {
        inputCases++;
        foreach(var xy in new[]{(x:.5f,y:.5f),(x:55.5f,y:.5f),(x:.5f,y:55.5f),(x:55.5f,y:55.5f),(x:.5f,y:28f),(x:55.5f,y:28f),(x:28f,y:.5f),(x:28f,y:55.5f),(x:28f,y:28f)})
        {
            Fresh(ui);Rect paw=ui.PawScreenRect(0);Vector2 p=new Vector2(paw.x+xy.x,paw.y+xy.y);string bot=State(ui.Game.Players[1]);Devices.Touches=new[]{TouchAt(p,TouchPhase.Began)};Devices.MouseDown=Devices.MouseHeld=true;Devices.mousePosition=new Vector3(p.x*Display.PixelRatio,Display.height-p.y*Display.PixelRatio,0);Tick(ui);Check(ui.Game.Players[0].Flipping&&ui.Game.Players[0].AirFlipAvailable,"nine-point touch once, compatibility mouse suppressed");Check(State(ui.Game.Players[1])==bot&&ui.BotInputCursor==0,"human touch cannot control bot");string first=State(ui.Game);foreach(var phase in new[]{TouchPhase.Moved,TouchPhase.Stationary,TouchPhase.Ended}){Devices.Touches=new[]{TouchAt(p,phase)};Tick(ui);Check(State(ui.Game)==first,"move hold end no duplicate");}Devices.Clear();
        }
        Fresh(ui);string before=State(ui.Game);Devices.Keys.UnionWith(new[]{KeyCode.L,KeyCode.S,KeyCode.K});Tick(ui);Check(State(ui.Game)==before,"L S K ignored for both human and bot");Devices.Clear();string botBefore=State(ui.Game.Players[1]);Devices.Keys.Add(KeyCode.A);Tick(ui);Check(ui.Game.Players[0].Flipping&&ui.Game.Players[0].AirFlipAvailable&&State(ui.Game.Players[1])==botBefore,"A controls human exactly once");Devices.Clear();
        Fresh(ui);Vector2 mouse=ui.PawScreenRect(0).center;Devices.MouseDown=Devices.MouseHeld=true;Devices.mousePosition=new Vector3(mouse.x*Display.PixelRatio,Display.height-mouse.y*Display.PixelRatio,0);Tick(ui);Check(ui.Game.Players[0].Flipping&&ui.Game.Players[0].AirFlipAvailable,"mouse edge once");Devices.MouseDown=false;before=State(ui.Game);Tick(ui);Check(State(ui.Game)==before,"mouse hold no repeat");Devices.Clear();
        Fresh(ui);before=State(ui.Game);Click(ui,new Vector2(ui.SessionBarRect.x+2,ui.SessionBarRect.center.y));Check(State(ui.Game)==before,"bar consumes action");Click(ui,((Rect)Field(ui,"pauseRect")).center);Check(ui.IsPaused&&!ui.PawsVisible&&State(ui.Game)==before,"pause button consumes action");int cursor=ui.BotInputCursor;Devices.Keys.Add(KeyCode.A);Devices.Touches=new[]{TouchAt(ui.PawScreenRect(0).center,TouchPhase.Began)};Tick(ui,.2);Check(State(ui.Game)==before&&ui.BotInputCursor==cursor,"paused Update freezes both participants/cursor, eats input");Devices.Clear();Click(ui,((Rect)Field(ui,"resumeRect")).center);Check(!ui.IsPaused,"resume actual pointer");Tick(ui);
        Call(ui,"OpenTracks");Check(ui.FlightMenuPage==-1&&!ui.IsPaused,"active race cannot open selection menu");Call(ui,"ResetBotRace",ui.Game.BotSeed);Devices.Clear();Tick(ui);Call(ui,"OpenTracks");Check(ui.FlightMenuPage==0&&ui.IsPaused,"ready race menu opens");before=State(ui.Game);cursor=ui.BotInputCursor;Devices.Keys.UnionWith(new[]{KeyCode.A,KeyCode.L,KeyCode.S,KeyCode.K});Tick(ui,.1);Check(State(ui.Game)==before&&ui.BotInputCursor==cursor,"open menu freezes and consumes all gameplay keys");Devices.Clear();Call(ui,"CloseRules");Tick(ui);Check(!ui.IsPaused,"menu closes cleanly");
    }
    static object Player(PlayerState p)=>new {Status=p.Status.ToString(),p.Distance,p.TerminalTime,p.FinishTime,p.Collisions,p.BoostsEarned,p.FinishBoostsEarned};
    static void FinishAndRepeat(FlipRaceGame ui,int cadence)
    {
        Devices.Clear();Fresh(ui);string prefs=Preferences(),track=ui.Game.Track.Id,flight=State(ui.Game.Flight),plan=ui.Game.BotPlan.Id;int seed=ui.Game.BotSeed,hero=ui.Game.Players[0].CharacterIndex;BotDifficulty difficulty=ui.Game.BotDifficulty;PartyCamera camera=ui.Game.PartyCamera;bool single=false,pausedInFlight=false;int frames=0;
        double[] framesizes=cadence==0?new[]{1d/30}:cadence==1?new[]{1d/60}:new[]{.005,1d/29,.077,1d/144,.13};
        while(ui.Game.Phase!="finished"&&frames<12000)
        {
            Tick(ui,framesizes[frames++%framesizes.Length]);Check(!ui.IsPaused,"allowed cadence does not auto-pause");
            if(!pausedInFlight&&ui.Game.Phase=="racing"&&ui.Game.Elapsed>2&&ui.BotInputCursor>0)
            {
                string frozen=State(ui.Game);int cursor=ui.BotInputCursor;Click(ui,((Rect)Field(ui,"pauseRect")).center);Tick(ui,.2);Layout(ui);Check(ui.IsPaused&&State(ui.Game)==frozen&&ui.BotInputCursor==cursor,"actual nonzero-cursor pause/resize freezes both players and bot");Click(ui,((Rect)Field(ui,"resumeRect")).center);Tick(ui);Check(!ui.IsPaused&&State(ui.Game)==frozen&&ui.BotInputCursor==cursor,"actual resume does not consume movement or bot edge");pausedInFlight=true;inFlightPauses++;
            }if(ui.Game.Players.Count(p=>p.Status!=PlayerStatus.Running)==1){Check(ui.Game.Phase=="racing","first terminal cannot end second");single=true;}
        }
        Check(ui.Game.Phase=="finished"&&ui.Game.Players.All(p=>p.Status!=PlayerStatus.Running),"actual Update reaches two terminal outcomes");Check(ui.BotInputCursor>0&&ui.Game.Players[1].Distance>1000,"bot moves and uses actual plan without human input");Check(!ui.PawsVisible,"result paw hidden");Layout(ui);Check(Preferences()==prefs,"full result writes no progress/preferences");if(difficulty==BotDifficulty.Master)Check(ui.Game.Players[1].Status==PlayerStatus.Finished&&ui.Game.Players[1].Collisions==0&&ui.Game.Players[1].BoostsEarned==ui.Game.Track.RiskZones.Count(z=>!z.IsNegative&&!z.IsCoin&&!z.IsFinish)&&ui.Game.Players[1].FinishBoostsEarned==1,"Master actual UI runtime all rewards/no hits/finish both cameras");string terminal=State(ui.Game);Click(ui,ui.BotTrackChoiceRect.center);Check(ui.FlightMenuPage==0&&ui.IsPaused&&State(ui.Game)==terminal,"actual result Choose track opens menu without result mutation");Call(ui,"CloseRules");Tick(ui);Check(ui.Game.Phase=="finished"&&!ui.IsPaused&&Preferences()==prefs,"close result menu preserves results and preferences");if(single)continuedAfterFirst++;
        Evidence.Add(new{kind="actual-ui-full-race",track,profile=ui.Game.Flight.FirstAction.ToString(),difficulty=difficulty.ToString(),camera=camera.ToString(),cadence,frames,firstTerminalWaited=single,botCursor=ui.BotInputCursor,human=Player(ui.Game.Players[0]),bot=Player(ui.Game.Players[1])});fullRaces++;
        Click(ui,ui.BotRetryRect.center);Check(ui.Game.BotSeed==unchecked(seed+1)&&ui.Game.BotPlan.Id!=plan&&ui.Game.Phase=="countdown"&&ui.BotInputCursor==0,"actual retry new seed/new plan/fresh countdown cursor");Check(ui.Game.Track.Id==track&&State(ui.Game.Flight)==flight&&ui.Game.BotDifficulty==difficulty&&ui.Game.PartyCamera==camera&&ui.Game.Players[0].CharacterIndex==hero&&ui.Game.Players[1].CharacterIndex!=hero,"retry retains all choices and distinct identity");Check(ui.Game.Elapsed==0&&ui.Game.Players.All(p=>p.Status==PlayerStatus.Running&&p.Distance==0&&p.TerminalTime==null&&p.FinishTime==null&&p.Collisions==0&&p.BoostsEarned==0&&p.CoinsEarned==0&&p.CollectedRiskIds.Count==0),"actual retry clears both results/effects/rewards");Check(Preferences()==prefs,"retry writes no progress/preferences");
    }
    static void CustomAndTransitions()
    {
        context="custom gating";var custom=NewUi(-1);string prefs=Preferences(),draft=State(((FlightSelection)Field(custom,"flightSelection")).Custom),game=State(custom.Game);Call(custom,"OpenTracks");Click(custom,custom.ViewMenuButtonRect.center);int page=custom.FlightMenuPage;Click(custom,custom.BotMenuButtonRect.center);Check(custom.FlightMenuPage==page&&!custom.BotEnabled&&State(custom.Game)==game,"custom BOT entry disabled without game replacement");Check(Preferences()==prefs&&State(((FlightSelection)Field(custom,"flightSelection")).Custom)==draft,"gating preserves all custom draft/preferences");
        var pending=NewUi();Enter(pending,0,2,PartyCamera.Constant);string pendingGame=State(pending.Game),pendingPrefs=Preferences(),pendingDraft=State(((FlightSelection)Field(pending,"flightSelection")).Custom);Call(pending,"OpenTracks");Call(pending,"MeasureFlightMenu");Click(pending,((Rect)Field(pending,"flightCustom")).center);Check(pending.FlightMenuPage==1,"actual pending-bot custom selection opens editor");Click(pending,((Rect)Field(pending,"flightDone")).center);Check(pending.FlightMenuPage==0,"custom editor Done returns main");Apply(pending);Check(pending.FlightMenuPage==0&&State(pending.Game)==pendingGame&&((FlightSelection)Field(pending,"flightSelection")).IsCustom,"pending bot custom Apply disabled without auto substitution");Check(Preferences()==pendingPrefs&&State(((FlightSelection)Field(pending,"flightSelection")).Custom)==pendingDraft,"pending custom rejection preserves draft/preferences");
        foreach(int count in new[]{2,3,4})foreach(int kind in new[]{0,1,2})
        {
            context="mode roundtrip "+count+"/"+kind;var ui=NewUi(FlightSettingsStore.FastFlipPresetIndex);Enter(ui,4,3,PartyCamera.Lagger);Click(ui,ui.BotCharacterRect(4).center);string track=ui.Game.Track.Id,flight=State(ui.Game.Flight);Call(ui,"OpenTracks");Click(ui,ui.ViewMenuButtonRect.center);Click(ui,ui.SoloMenuButtonRect.center);Check(ui.FlightMenuPage==7,"BOT to SOLO actual route");Apply(ui);Check(ui.SoloEnabled&&!ui.BotEnabled&&ui.Game.Players.Length==1&&ui.Game.Players[0].CharacterIndex==4,"SOLO one human retains hero");
            Call(ui,"OpenTracks");Click(ui,ui.ViewMenuButtonRect.center);Click(ui,ui.PartyMenuButtonRect.center);Click(ui,ui.PartyCountRect(count-2).center);Apply(ui);Check(ui.PartyEnabled&&!ui.BotEnabled&&ui.Game.Players.Length==count&&ui.Game.PartyCamera==PartyCamera.Lagger,"company actual count/camera");
            Call(ui,"OpenTracks");Click(ui,ui.ViewMenuButtonRect.center);Click(ui,((Rect)Prop(ui,"ModeLegacyRect")).center);Check(ui.FlightMenuPage==5,"legacy page actual route");Click(ui,ui.ViewOptionRect(0,kind).center);if(kind==2)Click(ui,ui.ViewOptionRect(1,1).center);Apply(ui);Check(!ui.BotEnabled&&!ui.SoloEnabled&&!ui.PartyEnabled&&ui.Game.Players.Length==2&&ui.FieldViewName==(kind==0?"split":kind==1?"split-ghost":"shared"),"legacy actual view restores");
            Enter(ui,4,3,PartyCamera.Lagger);Check(ui.Game.Track.Id==track&&State(ui.Game.Flight)==flight&&ui.Game.Players[0].CharacterIndex==4,"roundtrip preserves track/profile/hero");
        }
    }
    static int Main()
    {
        try
        {
            foreach(var size in new[]{(w:568,h:320),(w:800,h:360),(w:960,h:432),(w:1280,h:800)})foreach(float dpr in new[]{1f,2.5f,3f})foreach(int preset in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
            {
                Screen(size.w,size.h,dpr);context="viewport "+size+" DPR"+dpr+" preset"+preset+" "+camera;
                for(int track=0;track<5;track++)for(int level=0;level<5;level++){var ui=NewUi(preset);Enter(ui,track,level,camera);Layout(ui,level==0&&track==0);if(level==0&&track==0){Start(ui);Inputs(ui);Evidence.Add(new{kind="actual-viewport-input",size=new[]{size.w,size.h},dpr,preset,camera=camera.ToString(),bar=Box(ui.SessionBarRect),field=Box(ui.SharedFieldRect),paw=Box(ui.PawScreenRect(0))});}}
            }
            Screen(800,360,1);foreach(int preset in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})for(int track=0;track<5;track++)for(int level=0;level<5;level++){context="actual full race preset"+preset+" "+camera+" track"+track+" level"+level;var ui=NewUi(preset);Enter(ui,track,level,camera);FinishAndRepeat(ui,(track+level)%3);}
            CustomAndTransitions();Check(fullRaces==100&&continuedAfterFirst>0,"all100 UI races and actual first-terminal continuation covered");
            Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,setups,layouts,updates,pointers,inputCases,fullRaces,continuedAfterFirst,inFlightPauses,Evidence,scope="Independent actual production Update/Pointer/FlightPointer/Measure/BotRaceController and physical Core. Native Screen/Input/Clock/Prefs/DPR/selection texture acquisition are the only aliases. No production gameplay method replacement or injected terminal states. No native rendering/browser/physical multitouch claim."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
