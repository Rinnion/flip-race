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
 const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;
 static int checks,cases;
 static void Check(bool b,string m){checks++;if(!b)throw new Exception(m);}
 static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,P).Invoke(o,a);
 static object Field(object o,string n)=>o.GetType().GetField(n,P).GetValue(o);
 static void Set(object o,string n,object v)=>o.GetType().GetField(n,P).SetValue(o,v);
 static void Tick(FlipRaceGame u,double dt=0){Clock.frameCount++;Clock.realtimeSinceStartupAsDouble+=dt;Call(u,"Update");}
 static void Click(FlipRaceGame u,Rect r){Devices.Clear();Tick(u);Call(u,"Pointer",r.center);}
 static bool Inside(Rect a,Rect b)=>a.xMin>=b.xMin-.01&&a.xMax<=b.xMax+.01&&a.yMin>=b.yMin-.01&&a.yMax<=b.yMax+.01;
 static FlipRaceGame Ui(int preset,int track)
 {
  Prefs.Clear();Devices.Clear();Prefs.SetInt(FlightSettingsStore.ActiveChoiceKey,preset);
  var u=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));Set(u,"lanes",new Rect[2]);Call(u,"InitializeFlightMenu");
  var selection=(FlightSelection)Field(u,"flightSelection");typeof(FlipRaceGame).GetProperty("Game").SetValue(u,RaceSimulation.CreateTrial(RaceTracks.GetAt(track),selection.Settings));
  Set(u,"lastFrame",Clock.realtimeSinceStartupAsDouble);Call(u,"Measure");return u;
 }
 static void Enter(FlipRaceGame u,int level,int camera)
 {
  Call(u,"OpenTracks");Click(u,u.ViewMenuButtonRect);Click(u,u.BotMenuButtonRect);Check(u.FlightMenuPage==8,"explicit bot route");
  Click(u,u.BotDifficultyRect(level));Click(u,u.PartyCameraRect(camera));Click(u,(Rect)Field(u,"flightApply"));
  Check(u.BotEnabled&&!u.SoloEnabled&&u.Game.Players.Length==2&&u.Game.Phase=="ready","isolated bot apply");
  Tick(u);Check(u.SelectedBotDifficulty==level&&(int)u.Game.PartyCamera==camera&&u.Game.Players[1].Ready,"level camera auto-ready");
 }
 static void Layout(FlipRaceGame u)
 {
  Check(Math.Abs(u.SessionBarRect.yMax-u.SharedFieldRect.yMin)<.01,"all-phase toolbar above field");
  var a=(Rect)Field(u,"arena");for(int h=0;h<6;h++)
  {
   Rect r=u.BotCharacterRect(h);Check(r.height>=44&&r.width>=44&&Inside(r,a)&&!r.Overlaps(u.SessionBarRect),"normal 44px human selection fits");
   for(int j=0;j<h;j++)Check(!r.Overlaps(u.BotCharacterRect(j)),"selection cards disjoint");
  }
  Check(Inside(u.BotStartRect,a)&&!u.BotStartRect.Overlaps(u.BotCharacterRect(5)),"start fits below cards");
  Check(u.CharacterScreenRect(1,0).width==0&&u.ReadyScreenRect(1).width==0&&u.PawScreenRect(1).width==0,"bot has no human controls");
  Rect safe=(Rect)Call(u,"SafeUiArea"),paw=u.PawScreenRect(0);Check(paw.width==56&&paw.height==56&&Inside(paw,safe)&&Math.Abs(paw.yMax-(safe.yMax-8))<.01,"one BR56 safe paw");
 }
 static void Exercise(FlipRaceGame u)
 {
  Click(u,u.BotCharacterRect(4));Check(u.Game.Players[0].CharacterIndex==4&&u.Game.Players[1].CharacterIndex==5,"distinct bot identity follows human selection");
  Devices.Keys.Add(KeyCode.L);Devices.Keys.Add(KeyCode.S);Devices.Keys.Add(KeyCode.K);Tick(u);Devices.Clear();Check(u.Game.Phase=="ready","L S K cannot start or control bot");
  Call(u,"OpenTracks");double e=u.Game.Elapsed;Devices.Keys.Add(KeyCode.A);Tick(u,.02);Devices.Clear();Check(u.Game.Phase=="ready"&&u.Game.Elapsed==e,"menu consumes A");Call(u,"CloseRules");Tick(u);
  Click(u,u.BotStartRect);Check(u.Game.Phase=="countdown","one normal start begins countdown");
  Layout(u);Tick(u);for(int i=0;i<130&&u.Game.Phase!="racing";i++)Tick(u,.025);Check(u.Game.Phase=="racing","bot shares countdown");
  Call(u,"Pause","test");e=u.Game.Elapsed;int cursor=u.BotInputCursor;Tick(u,.1);Check(u.Game.Elapsed==e&&u.BotInputCursor==cursor,"pause freezes bot and world");Call(u,"Resume");Tick(u);
  var old=u.Game;var plan=u.Game.BotPlan;int seed=u.SelectedBotSeed;u.Game.Phase="finished";foreach(var p in u.Game.Players){p.Status=PlayerStatus.Eliminated;p.Ready=false;p.CoinsEarned=7;}
  Click(u,u.BotRetryRect);Check(!ReferenceEquals(old,u.Game)&&u.SelectedBotSeed==seed+1&&u.Game.Phase=="countdown"&&u.BotInputCursor==0,"retry recreates controller and seed plan");Check(u.Game.Players[0].CharacterIndex==4&&u.Game.Players[1].CharacterIndex==5&&u.Game.Players.All(p=>p.Distance==0&&p.CoinsEarned==0),"retry preserves heroes and clears personal state");
  u.Game.Phase="finished";Click(u,u.BotTrackChoiceRect);Check(u.FlightMenuPage==0,"result exits to track settings");
 }
 static int Main(){try{
  foreach(var size in new[]{(568,320),(800,360),(960,432),(1280,800)})foreach(float dpr in new[]{1f,2.5f,3f})foreach(int camera in new[]{0,1})foreach(int level in Enumerable.Range(0,5)){
   Display.width=(int)(size.Item1*dpr);Display.height=(int)(size.Item2*dpr);Display.PixelRatio=dpr;Display.SafeAreaOverride=new Rect(12*dpr,10*dpr,(size.Item1-28)*dpr,(size.Item2-24)*dpr);
   var u=Ui(FlightSettingsStore.ChosenPresetIndex,cases%5);Enter(u,level,camera);Layout(u);Exercise(u);cases++;
  }
  Display.width=800;Display.height=360;Display.PixelRatio=1;Display.SafeAreaOverride=null;
  for(int track=0;track<5;track++)for(int level=0;level<5;level++){var u=Ui(FlightSettingsStore.FastFlipPresetIndex,track);Enter(u,level,0);Check(u.Game.Track.Id==RaceTracks.GetAt(track).Id,"fast profile all five tracks");}
  var custom=Ui(-1,0);string before=FlightSettingsStore.Encode(((FlightSelection)Field(custom,"flightSelection")).Custom);Call(custom,"OpenTracks");Click(custom,custom.ViewMenuButtonRect);Click(custom,custom.BotMenuButtonRect);Check(custom.FlightMenuPage==5&&!custom.BotEnabled&&((FlightSelection)Field(custom,"flightSelection")).IsCustom,"custom blocks bot entry without auto preset");Check(FlightSettingsStore.Encode(((FlightSelection)Field(custom,"flightSelection")).Custom)==before,"manual draft intact");
  Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,cases,scope="Actual managed UI Update/Pointer/menu/controller and rectangle checks; no native render or physical touch claim."},new JsonSerializerOptions{WriteIndented=true}));return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}