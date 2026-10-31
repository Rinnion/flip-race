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
    static readonly Dictionary<FlipRaceGame,long> Models=new Dictionary<FlipRaceGame,long>(ReferenceEqualityComparer.Instance);
    static readonly Dictionary<FlipRaceGame,HashSet<uint>> Seeds=new Dictionary<FlipRaceGame,HashSet<uint>>(ReferenceEqualityComparer.Instance);
    static int selections,syntheticTerminalFixtures,sameAttemptInputFixtures;
    static uint ExpectedSeed(uint session,long sequence)
    {unchecked{uint n=session+(uint)sequence*0x9e3779b9u;n^=n>>16;n*=0x7feb352du;n^=n>>15;n*=0x846ca68bu;return n^(n>>16);}}
    static string Selection(FlipRaceGame ui)=>State(ui.Game.StochasticBot)+"|"+ui.BotSessionSeed+"|"+ui.BotAttemptSequenceIndex;
    static void CheckNextSelection(FlipRaceGame ui)
    {
        Check(ui.Game.StochasticBot!=null&&ui.Game.BotPlan==null,"active bot uses immutable stochastic settings with no whole tape");
        if(!Models.ContainsKey(ui)){Models.Add(ui,0);Seeds.Add(ui,new HashSet<uint>());}
        long sequence=++Models[ui];uint seed=ExpectedSeed(ui.BotSessionSeed,sequence);
        Check(ui.BotAttemptSequenceIndex==sequence&&ui.BotAttemptSeed==seed&&ui.Game.StochasticBot.Seed==seed,"actual Apply/Retry uses session+monotonic sequence mix");
        Check(Seeds[ui].Add(seed),"attempt seed never repeats in tested session");
        double[] qualities={.5,.65,.75,.9,1};var settings=ui.Game.StochasticBot;
        Check(ui.BotQuality==qualities[ui.SelectedBotDifficulty]&&ui.BotCollectProbability==ui.BotQuality,"actual fixed level quality/collect probability");
        string profile=ui.Game.Flight.FirstAction==FirstAction.Jump?"Chosen":"FastFlip";
        Check(settings.TrackId==ui.Game.Track.Id&&settings.ProfileId==profile,"immutable settings identify the selected track and exact ready profile");
        double expectedHit=ui.BotQuality==1?0:settings.BonusGain/settings.CollisionCostSum*(1-ui.BotQuality);
        Near(ui.BotHitProbability,expectedHit,"common B/C hit-probability formula; Master remains zero",1e-12);
        Check(ui.BotGoalsTotal==StochasticTrackMap.Create(ui.Game.Track,ui.Game.Flight).Opportunities.Length&&ui.BotPositiveGoalCount==10,"actual map opportunity counts, nine ordinary plus finish");
        Check(ui.BotDecisions.Count==0&&ui.BotPlanningCalls==0&&ui.BotPlanningSamples.Count==0,"new controller is empty before real racing");selections++;
    }
    static void SameAttemptInputFixture(FlipRaceGame ui)
    {
        var previous=ui.Game;int hero=previous.Players[0].CharacterIndex;string selection=Selection(ui);
        var next=RaceSimulation.CreateStochasticBotRace(previous.Track,previous.Flight,previous.StochasticBot,previous.PartyCamera,FlipRaceGame.PartyStartLeft,FlipRaceGame.PartyWindowWidth);
        next.Players[0].CharacterIndex=hero;next.Players[1].CharacterIndex=(hero+1)%CharacterRoster.Count;
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,next);Call(ui,"InitializeBotController");Call(ui,"Measure");sameAttemptInputFixtures++;
        Check(Selection(ui)==selection,"same-attempt input fixture never draws a new seed");
    }
    static void TerminalFixture(FlipRaceGame ui)
    {
        // Explicit synthetic terminal fixture for selection-order tests; full
        // physical reachability remains covered by the five Update races below.
        ui.Game.Phase="finished";foreach(var p in ui.Game.Players){p.Status=PlayerStatus.Finished;p.FinishTime=1;p.Ready=false;}
        Devices.Clear();Tick(ui);syntheticTerminalFixtures++;
    }
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
    static FlipRaceGame NewUi(int preset=FlightSettingsStore.ChosenPresetIndex,bool clear=true,bool fixedSession=true)
    {
        Devices.Clear();if(clear){Prefs.Clear();var draft=FlightDraft.From(FlightPresets.FastFlip);draft.Repeats=AirRepeatLimit.One;draft.Gravity=2;draft.Speed=3;Prefs.SetString(FlightSettingsStore.Key,FlightSettingsStore.Encode(draft));Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,preset);Prefs.SetInt("flip-race-sound",0);}
        Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=1;var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));Set(ui,"lanes",new Rect[2]);foreach(string name in new[]{"firstPawAt","secondPawAt","thirdPawAt","fourthPawAt"})Set(ui,name,-10f);
        if(fixedSession){Set(ui,"botSessionSeed",123u);Set(ui,"botSeedInitialized",true);}
        Check(typeof(FlipRace.Web.FlipDiagnostics).GetMethod("ReadBotTelemetry",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{ui})==null,"inactive diagnostics are null-safe before Game initialization");
        Call(ui,"InitializeFlightMenu");var selection=(FlightSelection)Field(ui,"flightSelection");typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,RaceSimulation.CreateTrial(RaceTracks.GetAt(0),selection.Settings));Set(ui,"selectedTrackId",ui.Game.Track.Id);Set(ui,"lastFrame",Clock.realtimeSinceStartupAsDouble);Call(ui,"Measure");return ui;
    }
    static void Enter(FlipRaceGame ui,int track,int level,PartyCamera camera)
    {
        Call(ui,"OpenTracks");Check(ui.FlightMenuPage==0,"OpenTracks real menu");Call(ui,"MeasureFlightMenu");Click(ui,((Rect)Field(ui,"flightTracks")).center);Check(ui.FlightMenuPage==4,"full-track list");Click(ui,((Rect)Call(ui,"FullTrackRect",track)).center);Click(ui,((Rect)Field(ui,"flightBack")).center);Check(ui.FlightMenuPage==0,"track selected returns menu");Click(ui,ui.ViewMenuButtonRect.center);Click(ui,ui.BotMenuButtonRect.center);Check(ui.FlightMenuPage==8,"independent BOT route");
        for(int i=0;i<5;i++)Check(Inside(ui.BotDifficultyRect(i),(Rect)Field(ui,"flightPanel"))&&ui.BotDifficultyRect(i).height>=44,"all five visible level choices");
        Click(ui,ui.BotDifficultyRect(level).center);Click(ui,ui.PartyCameraRect((int)camera).center);Apply(ui);Devices.Clear();Tick(ui);
        Check(ui.BotEnabled&&!ui.SoloEnabled&&!ui.PartyEnabled&&ui.Game.Players.Length==2,"isolated human+bot");Check(ui.Game.Phase=="ready"&&!ui.Game.Players[0].Ready&&ui.Game.Players[1].Ready,"bot auto ready, human awaits start");Check(ui.Game.BotDifficulty==(BotDifficulty)level&&ui.Game.Track.Id==RaceTracks.GetAt(track).Id&&ui.Game.PartyCamera==camera,"actual menu applies track level camera");Check(ui.Game.Players[0].CharacterIndex!=ui.Game.Players[1].CharacterIndex,"distinct bot identity");Check(ui.BotInputCursor==0,"new choice clean cursor");CheckNextSelection(ui);setups++;
    }
    static void Layout(FlipRaceGame ui,bool selectHeroes=false)
    {
        layouts++;Rect arena=(Rect)Field(ui,"arena"),safe=(Rect)Call(ui,"SafeUiArea"),bar=ui.SessionBarRect,field=ui.SharedFieldRect,paw=ui.PawScreenRect(0);
        Check(!bar.Overlaps(field),"toolbar outside world");Near(bar.yMax,field.yMin,"toolbar directly above field");Check(Inside(paw,safe)&&!paw.Overlaps(bar)&&!paw.Overlaps(field),"BR paw wholly in safe area and clear of world/bar");Near(paw.width,56,"paw width56");Near(paw.height,56,"paw height56");Near(paw.xMax,safe.xMax-(Display.height/Display.PixelRatio<=600?40:8),"safe right paw inset");Near(paw.yMax,safe.yMax-8,"safe bottom paw inset");
        Check(ui.PawScreenRect(1).width==0&&ui.CharacterScreenRect(1,0).width==0&&ui.ReadyScreenRect(1).width==0,"only human has paw");
        var diagnostic=(FlipRace.Web.FlipDiagnostics)RuntimeHelpers.GetUninitializedObject(typeof(FlipRace.Web.FlipDiagnostics));
        Set(diagnostic,"game",ui);
        using(var exported=JsonDocument.Parse((string)Call(diagnostic,"WithLayout","{\"fixture\":true}")))
        {
            var json=exported.RootElement;
            Check(json.GetProperty("projectedPlayers").GetArrayLength()==ui.Game.Players.Length,"diagnostics exports physical projections for human AND bot");
            foreach(string name in new[]{"characterRects","readyRects","pawRects"})Check(json.GetProperty(name).GetArrayLength()==1,"diagnostics exports only human control/selection rectangles: "+name);
            var projection=SharedFieldView.Create(field.width,field.height,ui.Game);
            for(int slot=0;slot<ui.Game.Players.Length;slot++)
            {
                var actual=json.GetProperty("projectedPlayers")[slot];
                Near(actual[0].GetDouble(),field.x+projection.PlayerX(slot),"diagnostic actual physical X slot"+slot);
                Near(actual[1].GetDouble(),field.y+projection.PlayerY(slot),"diagnostic actual physical Y slot"+slot);
            }
        }
        foreach(string name in new[]{"soundRect","pauseRect","fullscreenRect"})Check(Inside((Rect)Field(ui,name),bar),"toolbar tool contained "+name);
        var view=SharedFieldView.Create(field.width,field.height,ui.Game);Near(view.ScaleX,view.ScaleY,"uniform projection");Near(view.CameraLeft,ui.Game.PartyCameraLeft,"actual camera");Near(ui.Game.PartyWindowWidth,1520,"logicalwindow1520");
        if(ui.Game.Phase=="ready")
        {
            var cards=new List<Rect>();for(int hero=0;hero<6;hero++){Rect card=ui.BotCharacterRect(hero);Check(Inside(card,arena)&&card.width>=44&&card.height>=44,"six whole44cards");Check(!card.Overlaps(bar)&&!card.Overlaps(ui.BotStartRect)&&cards.All(c=>!c.Overlaps(card)),"cards independent/bar/start clear");cards.Add(card);if(selectHeroes){Click(ui,card.center);Check(ui.Game.Players[0].CharacterIndex==hero&&!ui.Game.Players[0].Ready&&ui.Game.Players[1].Ready&&ui.Game.Players[1].CharacterIndex!=hero,"actual card selects human only, bot ready/different");}}
            Check(Inside(ui.BotStartRect,arena)&&!ui.BotStartRect.Overlaps(bar)&&ui.BotStartRect.height>=44,"whole start44");Check(!ui.PawsVisible,"selection paw hidden");
        }
        if(ui.Game.Phase=="countdown")Check(Inside(ui.SoloCountdownRect,arena)&&!ui.SoloCountdownRect.Overlaps(bar)&&!ui.SoloCountdownRect.Overlaps(field),"countdown visible outside toolbar/world");
        if(ui.Game.Phase=="finished")foreach(var button in new[]{ui.BotRetryRect,ui.BotTrackChoiceRect})Check(Inside(button,arena)&&!button.Overlaps(bar),"actual result buttons visible");
        string selected=Selection(ui);string before=State(ui.Game);int cursor=ui.BotInputCursor;int oldW=Display.width,oldH=Display.height;Display.width+=160;Display.height+=80;Call(ui,"Measure");Check(State(ui.Game)==before&&ui.BotInputCursor==cursor&&Selection(ui)==selected,"resize preserves world, selected plan, sequence and bot cursor");Display.width=oldW;Display.height=oldH;Call(ui,"Measure");
    }
    static void Start(FlipRaceGame ui)
    {
        string selected=Selection(ui);Click(ui,ui.BotStartRect.center);Check(ui.Game.Phase=="countdown"&&!ui.PawsVisible,"one human start immediately countdown");Layout(ui);Devices.Clear();Tick(ui);string state=State(ui.Game);int cursor=ui.BotInputCursor;Devices.Keys.UnionWith(new[]{KeyCode.A,KeyCode.L,KeyCode.S,KeyCode.K});Devices.Touches=new[]{TouchAt(ui.PawScreenRect(0).center,TouchPhase.Began)};Tick(ui);Check(State(ui.Game)==state&&ui.BotInputCursor==cursor,"countdown input has no effects");Devices.Clear();while(ui.Game.Phase=="countdown")Tick(ui,Math.Min(.1,ui.Game.CountdownRemaining));Check(ui.Game.Phase=="racing"&&ui.PawsVisible,"actual Update completes countdown");Check(Selection(ui)==selected,"start/countdown never draws another plan");Check(ui.BotInputCursor==0&&ui.Game.Players[1].Distance<1e-6,"countdown doesn't advance bot edges");Layout(ui);
    }
    static void Fresh(FlipRaceGame ui){SameAttemptInputFixture(ui);Devices.Clear();Tick(ui);Start(ui);}
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
        Call(ui,"OpenTracks");Check(ui.FlightMenuPage==-1&&!ui.IsPaused,"active race cannot open selection menu");SameAttemptInputFixture(ui);Devices.Clear();Tick(ui);Call(ui,"OpenTracks");Check(ui.FlightMenuPage==0&&ui.IsPaused,"ready race menu opens");before=State(ui.Game);cursor=ui.BotInputCursor;Devices.Keys.UnionWith(new[]{KeyCode.A,KeyCode.L,KeyCode.S,KeyCode.K});Tick(ui,.1);Check(State(ui.Game)==before&&ui.BotInputCursor==cursor,"open menu freezes and consumes all gameplay keys");Devices.Clear();Call(ui,"CloseRules");Tick(ui);Check(!ui.IsPaused,"menu closes cleanly");
    }
    static object Player(PlayerState p)=>new {Status=p.Status.ToString(),p.Distance,p.TerminalTime,p.FinishTime,p.Collisions,p.BoostsEarned,p.FinishBoostsEarned};
    static void FinishAndRepeat(FlipRaceGame ui,int cadence)
    {
        Devices.Clear();Fresh(ui);string prefs=Preferences(),track=ui.Game.Track.Id,flight=State(ui.Game.Flight),plan=Selection(ui);uint seed=ui.BotAttemptSeed;int hero=ui.Game.Players[0].CharacterIndex;BotDifficulty difficulty=ui.Game.BotDifficulty;PartyCamera camera=ui.Game.PartyCamera;bool single=false,pausedInFlight=false;int frames=0;
        double[] framesizes=cadence==0?new[]{1d/30}:cadence==1?new[]{1d/60}:new[]{.005,1d/29,.077,1d/144,.13};
        while(ui.Game.Phase!="finished"&&frames<12000)
        {
            Tick(ui,framesizes[frames++%framesizes.Length]);Check(!ui.IsPaused,"allowed cadence does not auto-pause");
            if(!pausedInFlight&&ui.Game.Phase=="racing"&&ui.Game.Elapsed>2&&ui.BotInputCursor>0)
            {
                string frozen=State(ui.Game);int cursor=ui.BotInputCursor;Click(ui,((Rect)Field(ui,"pauseRect")).center);Tick(ui,.2);Layout(ui);Check(ui.IsPaused&&State(ui.Game)==frozen&&ui.BotInputCursor==cursor,"actual nonzero-cursor pause/resize freezes both players and bot");Click(ui,((Rect)Field(ui,"resumeRect")).center);Tick(ui);Check(!ui.IsPaused&&State(ui.Game)==frozen&&ui.BotInputCursor==cursor,"actual resume does not consume movement or bot edge");pausedInFlight=true;inFlightPauses++;
            }if(ui.Game.Players.Count(p=>p.Status!=PlayerStatus.Running)==1){Check(ui.Game.Phase=="racing","first terminal cannot end second");single=true;}
        }
        Check(ui.Game.Phase=="finished"&&ui.Game.Players.All(p=>p.Status!=PlayerStatus.Running),"actual Update reaches two terminal outcomes");Check(ui.BotInputCursor>0&&ui.Game.Players[1].Distance>1000,"bot moves and uses actual local decisions without human input");Check(!ui.PawsVisible,"result paw hidden");Layout(ui);Check(Preferences()==prefs,"full result writes no progress/preferences");if(difficulty==BotDifficulty.Master)Check(ui.Game.Players[1].Status==PlayerStatus.Finished&&ui.Game.Players[1].Collisions==0&&ui.Game.Players[1].BoostsEarned==ui.Game.Track.RiskZones.Count(z=>!z.IsNegative&&!z.IsCoin&&!z.IsFinish)&&ui.Game.Players[1].FinishBoostsEarned==1,"Master actual UI runtime all rewards/no hits/finish both cameras");string terminal=State(ui.Game);Click(ui,ui.BotTrackChoiceRect.center);Check(ui.FlightMenuPage==0&&ui.IsPaused&&State(ui.Game)==terminal,"actual result Choose track opens menu without result mutation");Call(ui,"CloseRules");Tick(ui);Check(ui.Game.Phase=="finished"&&!ui.IsPaused&&Preferences()==prefs,"close result menu preserves results and preferences");if(single)continuedAfterFirst++;
        Evidence.Add(new{kind="actual-ui-full-race",track,profile=ui.Game.Flight.FirstAction.ToString(),difficulty=difficulty.ToString(),camera=camera.ToString(),cadence,frames,firstTerminalWaited=single,botCursor=ui.BotInputCursor,human=Player(ui.Game.Players[0]),bot=Player(ui.Game.Players[1])});fullRaces++;Telemetry(ui);
        Click(ui,ui.BotRetryRect.center);CheckNextSelection(ui);Check(ui.BotAttemptSeed!=seed&&Selection(ui)!=plan&&ui.Game.Phase=="countdown"&&ui.BotInputCursor==0,"actual retry creates fresh stochastic seed/controller/countdown");Check(ui.Game.Track.Id==track&&State(ui.Game.Flight)==flight&&ui.Game.BotDifficulty==difficulty&&ui.Game.PartyCamera==camera&&ui.Game.Players[0].CharacterIndex==hero&&ui.Game.Players[1].CharacterIndex!=hero,"retry retains all choices and distinct identity");Check(ui.Game.Elapsed==0&&ui.Game.Players.All(p=>p.Status==PlayerStatus.Running&&p.Distance==0&&p.TerminalTime==null&&p.FinishTime==null&&p.Collisions==0&&p.BoostsEarned==0&&p.CoinsEarned==0&&p.CollectedRiskIds.Count==0),"actual retry clears both results/effects/rewards");Check(Preferences()==prefs,"retry writes no progress/preferences");
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
    static void NoDrawOperations(FlipRaceGame ui)
    {
        string selected=Selection(ui),prefs=Preferences();Call(ui,"OpenTracks");Devices.Clear();Tick(ui,.1);Call(ui,"CloseRules");Tick(ui);
        Call(ui,"Pause","selection invariant");Tick(ui,.1);Call(ui,"Measure");Call(ui,"Resume");Tick(ui);
        Check(Selection(ui)==selected&&Preferences()==prefs,"menu open/close, pause/resume and measure do not draw or persist seed");
        Check(ui.BotSessionSeed==123u,"explicit fixed session seed retained");
    }
    static void FreshSessionSeeds()
    {
        context="new UI sessions";Screen(800,360,1);var seeds=new HashSet<uint>();
        for(int i=0;i<8;i++)
        {
            var ui=NewUi(FlightSettingsStore.ChosenPresetIndex,true,false);string prefs=Preferences();Enter(ui,0,0,PartyCamera.Constant);
            seeds.Add(ui.BotSessionSeed);Check(ui.BotAttemptSequenceIndex==1,"new session starts its key sequence");Check(Preferences()==prefs,"initial session seed does not create PlayerPrefs seed/progress data");
            string selected=Selection(ui);uint seed=ui.BotSessionSeed;Call(ui,"OpenTracks");Call(ui,"CloseRules");Tick(ui);Check(ui.BotSessionSeed==seed&&Selection(ui)==selected,"lazy session initialization runs once");
        }
        Check(seeds.Count>1,"fresh UI instances obtain varying initial session seeds");Evidence.Add(new{kind="actual-lazy-session-seeds",sessions=8,distinctInitialSeeds=seeds.Count});
    }
    static void MixedSequences()
    {
        Screen(800,360,1);context="deterministic session mixed attempts";var a=NewUi();var b=NewUi();
        Enter(a,3,0,PartyCamera.Lagger);Enter(b,3,0,PartyCamera.Lagger);
        for(int i=0;i<100;i++)
        {
            Check(Selection(a)==Selection(b),"fixed session reproduces entire mixed Apply/Retry sequence");
            NoDrawOperations(a);NoDrawOperations(b);
            if(i%3==0){Enter(a,i%5,i%5,(PartyCamera)(i%2));Enter(b,i%5,i%5,(PartyCamera)(i%2));}
            else
            {
                TerminalFixture(a);TerminalFixture(b);Click(a,a.BotRetryRect.center);Click(b,b.BotRetryRect.center);CheckNextSelection(a);CheckNextSelection(b);
                SameAttemptInputFixture(a);SameAttemptInputFixture(b);
            }
        }
        Evidence.Add(new{kind="synthetic-terminal-and-real-menu-sequence",attemptsPerSession=a.BotAttemptSequenceIndex,syntheticTerminalFixtures,algorithm="session seed plus monotonic sequence uint permutation; no catalogue or shuffle"});
    }
    static void Telemetry(FlipRaceGame ui)
    {
        Check(ui.BotDecisions.Count>0&&ui.BotPlanningCalls>0&&ui.BotForecastSteps>0,"actual local planning ran through UI Update");
        Check(ui.BotForecastObservations==ui.BotForecastSteps&&ui.BotForecastHorizonViolations==0,"all forecast steps observed, zero violations");
        Check(ui.BotMaximumRequestedHorizon<=1000+2e-7&&ui.BotMaximumTrialHorizon<=1055+2e-7&&ui.BotMaximumTrialOvershoot<=55+2e-7,"requested and actual discarded-trial pointwise bounds");
        var value=typeof(FlipRace.Web.FlipDiagnostics).GetMethod("ReadBotTelemetry",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{ui});
        string telemetry=JsonSerializer.Serialize(value,value.GetType(),new JsonSerializerOptions{IncludeFields=true});
        using(var document=JsonDocument.Parse(telemetry))
        {
            var root=document.RootElement;Check(root.GetProperty("q").GetDouble()==ui.BotQuality&&root.GetProperty("decisionCount").GetInt32()==ui.BotDecisions.Count,"diagnostic fixed probabilities and actual decision count");
            Check(root.GetProperty("planningCalls").GetInt32()==ui.BotPlanningCalls&&root.GetProperty("planningSamples").GetArrayLength()==ui.BotPlanningSamples.Count,"diagnostic actual per-plan samples");
            Check(root.GetProperty("maximumTrialHorizon").GetDouble()==ui.BotMaximumTrialHorizon&&root.GetProperty("forecastObservations").GetInt32()==ui.BotForecastSteps,"diagnostic actual trial bound, no requested-as-actual alias");
            Check(root.GetProperty("lastPlan").GetProperty("number").GetInt32()==ui.BotLastPlan.Number,"diagnostic last local plan");
        }
        Check(!telemetry.Contains("Signature")&&!telemetry.Contains("catalog"),"telemetry has no whole-plan/library identifiers");
        var performance=ui.BotPerformance;
        Check(performance.Terminal&&performance.Sealed&&performance.OverflowFrames==0,"actual Update timing capture is complete and frozen after bot terminal");
        Check(performance.ActiveFrames>0&&performance.ZeroPlanActiveFrames>0&&performance.Advance.Count==performance.ActiveFrames,"actual active and zero-plan frames all included");
        Check(performance.CountdownFrames>0&&performance.PausedFrames>0,"actual countdown and paused cohorts recorded separately");
        var raw=(string)typeof(FlipRace.Web.FlipDiagnostics).GetMethod("PerformanceTerminalJson",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{ui});
        using(var terminal=JsonDocument.Parse(raw))
        {
            var frames=terminal.RootElement.GetProperty("frames");Check(frames.GetArrayLength()==performance.TotalFrames,"terminal export includes every frame, no sampling");
            int active=0;foreach(var sample in frames.EnumerateArray())if(sample.GetProperty("cohort").GetString()=="Active")active++;
            Check(active==performance.ActiveFrames&&terminal.RootElement.GetProperty("attemptSeed").GetUInt32()==ui.BotAttemptSeed,"terminal export cohort and seed exact");
        }
        int frozenCount=performance.StoredCount;var frozenSamples=State(performance.CopySamples());Tick(ui,.1);Tick(ui,.1);
        Check(performance.StoredCount==frozenCount&&State(performance.CopySamples())==frozenSamples,"actual post-terminal updates cannot dilute timing sample population");
        Evidence.Add(new{kind="actual-ui-frame-instrumentation",performance.TotalFrames,performance.ActiveFrames,performance.ZeroPlanActiveFrames,performance.CountdownFrames,performance.PausedFrames,performance.SuppressedFrames,performance.OverflowFrames,scope="Managed harness timings only; browser callback and WebGL timing acceptance are separate."});
        Evidence.Add(new{kind="actual-ui-local-planning",seed=ui.BotAttemptSeed,q=ui.BotQuality,decisions=ui.BotDecisions.Count,ui.BotPlanningCalls,ui.BotCandidateSimulations,ui.BotForecastSteps,ui.BotMaximumRequestedHorizon,ui.BotMaximumTrialHorizon,ui.BotMaximumTrialOvershoot});
    }
    static void PerformanceChecks()
    {
        context="bounded performance instrumentation";
        var empty=new BotFramePerformance();Check(empty.Advance.Count==0&&empty.Advance.P95UpperBound==0&&empty.StoredCount==0,"zero-frame quantile/count are exact");
        var p=new BotFramePerformance();
        for(int i=0;i<100;i++)
        {
            p.Record(new BotFrameSample(i,.01,i*.01,BotFrameCohort.Active,true,i==0?1:0,i*.01),false);
            p.RecordDiagnostics(i,.02);
        }
        Check(p.ActiveFrames==100&&p.ZeroPlanActiveFrames==99&&p.StoredCount==100,"all frames including zero-plan frames retained");
        Near(p.Advance.P95UpperBound,.95,"histogram nearest rank95 upper bound",1e-12);
        Near(p.Diagnostics.Sum,2,"synchronous diagnostic cost attached to each matching frame",1e-12);
        Near(p.Combined.Sum,p.Advance.Sum+2,"combined per-frame cost includes diagnostics",1e-10);
        p.Record(new BotFrameSample(100,.1,1,BotFrameCohort.Paused,false,0,0),false);
        p.Record(new BotFrameSample(101,.1,1,BotFrameCohort.Countdown,true,0,75),false);
        p.Record(new BotFrameSample(102,.1,1,BotFrameCohort.Suppressed,false,0,0),false);
        Check(p.ActiveFrames==100&&p.TotalFrames==103&&p.PausedFrames==1&&p.CountdownFrames==1&&p.SuppressedFrames==1,"other cohorts explicit and do not dilute active denominator");
        p.Record(new BotFrameSample(103,.1,1.1,BotFrameCohort.Active,true,1,75),true);
        p.RecordDiagnostics(103,2);p.Seal();
        var before=p.CopySamples();p.Record(new BotFrameSample(104,.1,1.2,BotFrameCohort.Active,true,0,0),false);p.RecordDiagnostics(103,1000);
        Check(p.StoredCount==104&&p.ActiveFrames==101&&p.Advance.Over50==1&&p.Advance.Maximum==75&&p.TerminalFrame==103,"cold outlier and terminal transition included, post-terminal excluded");
        Check(State(before)==State(p.CopySamples()),"sealed terminal sample buffer freezes including diagnostic associations");
        before[0]=default;Check(p.CopySamples()[0].AdvanceInvoked,"export copy cannot mutate retained frame data");
        var overflow=new BotFramePerformance();for(int i=0;i<BotFramePerformance.Capacity+1;i++)overflow.Record(new BotFrameSample(i,.01,0,BotFrameCohort.Active,true,0,.2),false);
        Check(overflow.StoredCount==10000&&overflow.TotalFrames==10001&&overflow.OverflowFrames==1&&overflow.ActiveFrames==10001,"bounded storage explicitly reports overflow without filtering totals");
        var reset=new BotFramePerformance();Check(reset.StoredCount==0&&!reset.Sealed&&!reset.Terminal,"new attempt resets instrumentation only");
        Evidence.Add(new{kind="synthetic-timing-instrumentation",histogramBinMilliseconds=.05,capacity=BotFramePerformance.Capacity,zeroPlanFrames=p.ZeroPlanActiveFrames,overflow.OverflowFrames,scope="Injected known durations test accounting and bounds; these synthetic values are not measured performance."});
    }
    static void ObserverFaultCapture()
    {
        context="actual one-shot observer fault inside Advance";Screen(800,360,1);
        var ui=NewUi();Enter(ui,3,4,PartyCamera.Lagger);Start(ui);
        var controller=(StochasticBotController)Field(ui,"botController");var performance=ui.BotPerformance;
        int beforeCount=performance.StoredCount;double beforeElapsed=ui.Game.Elapsed,beforeDistance=ui.Game.Players[1].Distance;
        const string marker="STOCHASTIC_UI_ONE_SHOT_OBSERVER_FAULT";bool thrown=false;
        controller.Observer=slice=>{if(slice.Kind=="advance"){controller.Observer=null;throw new InvalidOperationException(marker);}};
        try{Tick(ui,.05);}
        catch(TargetInvocationException ex){Check(ex.InnerException is InvalidOperationException&&ex.InnerException.Message==marker,"actual observer exception preserved by Update");thrown=true;}
        Check(thrown&&controller.Observer==null,"one-shot observer actually ran from inside the production controller");
        Check(performance.StoredCount==beforeCount+1&&performance.ActiveCaptureFaultFrames==1&&performance.CaptureFaultFrames==1,"finally retains exactly one active fault frame");
        var sample=performance.CopySamples().Last();
        Check(sample.CaptureFault&&sample.AdvanceInvoked&&sample.AdvanceMilliseconds>=0,"fault sample retains invocation and measured elapsed CPU duration");
        Check(ui.Game.Elapsed>beforeElapsed&&ui.Game.Elapsed<beforeElapsed+.05&&ui.Game.Players[1].Distance>beforeDistance,"partial ordinary Advance state remains real; no rollback or fabricated completion");
        Near(sample.Elapsed,ui.Game.Elapsed,"fault sample captures exact partial physical elapsed",1e-12);
        int faultFrame=sample.Frame;double partialElapsed=ui.Game.Elapsed,partialDistance=ui.Game.Players[1].Distance,cpuMilliseconds=sample.AdvanceMilliseconds;
        int frames=0;while(ui.Game.Players[1].Status==PlayerStatus.Running&&frames++<3000)Tick(ui,1d/30);
        Check(ui.Game.Players[1].Status==PlayerStatus.Finished,"one-shot fault fixture resumes ordinary Update to actual bot terminal");Tick(ui);
        Check(performance.Sealed&&performance.ActiveCaptureFaultFrames==1&&performance.CopySamples().Count(f=>f.CaptureFault)==1,"terminal export retains fault instead of replacing or dropping the failed frame");
        var summary=typeof(FlipRace.Web.FlipDiagnostics).GetMethod("ReadBotPerformance",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{ui});
        using(var parsed=JsonDocument.Parse(JsonSerializer.Serialize(summary,summary.GetType(),new JsonSerializerOptions{IncludeFields=true})))
            Check(!parsed.RootElement.GetProperty("complete").GetBoolean(),"active capture fault prevents a complete=true scalar claim after terminal");
        string raw=(string)typeof(FlipRace.Web.FlipDiagnostics).GetMethod("PerformanceTerminalJson",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{ui});
        using(var parsed=JsonDocument.Parse(raw))
        {
            Check(parsed.RootElement.GetProperty("activeCaptureFaultFrames").GetInt32()==1,"terminal JSON explicitly exports active fault counter");
            var fault=parsed.RootElement.GetProperty("frames").EnumerateArray().Single(f=>f.GetProperty("frame").GetInt32()==faultFrame);
            Check(fault.GetProperty("captureFault").GetBoolean(),"terminal raw log explicitly identifies the actual fault frame");
        }
        Evidence.Add(new{kind="actual-one-shot-observer-exception",faultFrame,partialElapsed,partialDistance,cpuMilliseconds,resumedUpdateFrames=frames,activeCaptureFaultFrames=performance.ActiveCaptureFaultFrames,botFinish=ui.Game.Players[1].FinishTime,scope="A real observer throws after an ordinary physical Advance slice inside production Update. The finally block records duration and partial state; after the one-shot fault the real race continues to terminal, and measurement completeness remains false. Separate from the five normal full races."});
    }
    static void SuppressedDiagnosticFixture()
    {
        context="synthetic suppression-counter diagnostic mapping";Screen(800,360,1);
        var ui=NewUi();Enter(ui,3,0,PartyCamera.Lagger);
        Check(ui.BotSuppressedInputs==0,"new controller starts with zero suppressed inputs");
        var controller=(StochasticBotController)Field(ui,"botController");string before=State(ui.Game);int cursor=ui.BotInputCursor;
        // Explicit counter-only fixture: no fake physical event or accepted press.
        typeof(StochasticBotController).GetProperty("SuppressedInputs").SetValue(controller,7);
        Check(ui.BotSuppressedInputs==7&&ui.BotInputCursor==cursor&&State(ui.Game)==before,"read-only suppression getter reports separate counter without creating gameplay input");
        var diagnostic=typeof(FlipRace.Web.FlipDiagnostics).GetMethod("ReadBotTelemetry",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{ui});
        using(var json=JsonDocument.Parse(JsonSerializer.Serialize(diagnostic,diagnostic.GetType(),new JsonSerializerOptions{IncludeFields=true})))
            Check(json.RootElement.GetProperty("suppressedInputs").GetInt32()==7,"stochastic snapshot publishes suppressedInputs separately");
        Evidence.Add(new{kind="synthetic-counter-only-diagnostic-fixture",suppressedInputs=7,cursor,scope="Injected counter verifies mapping only; not a physical suppression or an accepted Press event. Physical suppression equivalence is a separate Core proof."});
    }
    static int Main()
    {
        try
        {
            SuppressedDiagnosticFixture();
            PerformanceChecks();
            ObserverFaultCapture();
            foreach(var size in new[]{(w:568,h:320),(w:800,h:360),(w:960,h:432),(w:1280,h:800)})foreach(float dpr in new[]{1f,2.5f,3f})foreach(int preset in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
            {
                Screen(size.w,size.h,dpr);context="viewport "+size+" DPR"+dpr+" preset"+preset+" "+camera;
                for(int track=0;track<5;track++)for(int level=0;level<5;level++){var ui=NewUi(preset);Enter(ui,track,level,camera);Layout(ui,level==0&&track==0);if(level==0&&track==0){Start(ui);Inputs(ui);Evidence.Add(new{kind="actual-viewport-input",size=new[]{size.w,size.h},dpr,preset,camera=camera.ToString(),bar=Box(ui.SessionBarRect),field=Box(ui.SharedFieldRect),paw=Box(ui.PawScreenRect(0))});}}
            }
            Screen(800,360,1);
            foreach(int preset in new[]{FlightSettingsStore.ChosenPresetIndex,FlightSettingsStore.FastFlipPresetIndex})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
            {
                context="actual full stochastic UI race preset"+preset+" "+camera;var ui=NewUi(preset);Enter(ui,3,4,camera);FinishAndRepeat(ui,(int)camera);
            }
            context="actual Novice UI recovery";var novice=NewUi();Enter(novice,4,0,PartyCamera.Lagger);FinishAndRepeat(novice,2);
            FreshSessionSeeds();MixedSequences();CustomAndTransitions();Check(fullRaces==5&&continuedAfterFirst>0,"five actual Update races including both cameras/Master and lower-level continuation");
            Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,setups,layouts,updates,pointers,inputCases,fullRaces,continuedAfterFirst,inFlightPauses,selections,syntheticTerminalFixtures,sameAttemptInputFixtures,Evidence,scope="Independent actual production Update/Pointer/FlightPointer/Measure/StochasticBotController and physical Core. Native Screen/Input/Clock/Prefs/DPR/selection texture acquisition are the only aliases. No production gameplay method replacement. Explicit synthetic terminal fixtures test selection ordering; same-attempt factory fixtures isolate repeated input checks. Actual terminal reachability is covered separately by five full Update races. No native rendering/browser/physical multitouch claim."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
