
$source=Get-Content Assets/Scripts/Presentation/FlipRaceGame.cs -Raw
$core=Get-Content Assets/Scripts/Core/RaceSimulation.cs -Raw
$trackV3=(Get-Content Assets/Scripts/Core/TracksV3.cs -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$core += [Environment]::NewLine + $trackV3
$methods=foreach($name in @('Measure','Pointer','ToggleMode','NewGame','SelectCharacter','OpenTracks','TrackChoiceRect','CharacterRect','Act','Resume','OpenRules','CloseRules','BlockGameplayInput')){
$m=[regex]::Match($source,'(?m)^\s*(?:void|Rect) '+$name+'\s*\([^)]*\)\s*\{');if(!$m.Success){throw $name};$p=$source.IndexOf('{',$m.Index);$d=1
for($e=$p+1;$e -lt $source.Length -and $d -gt 0;$e++){if($source[$e] -eq '{'){$d++};if($source[$e] -eq '}'){$d--}}
$source.Substring($m.Index,$e-$m.Index)
}
$shim=@'
namespace UnityEngine {
 public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
 public struct Rect {public float x,y,width,height;public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}public float xMax{get{return x+width;}}public float yMax{get{return y+height;}}public Vector2 center{get{return new Vector2(x+width/2,y+height/2);}}public bool Contains(Vector2 p){return p.x>=x&&p.x<xMax&&p.y>=y&&p.y<yMax;}}
 public static class Mathf {public static float Min(float a,float b){return a<b?a:b;}public static float Max(float a,float b){return a>b?a:b;}public static float Clamp(float a,float b,float c){return Max(b,Min(a,c));}}
 public static class Time {public static double realtimeSinceStartupAsDouble=1;public static int frameCount=1;}
 public static class Screen {public static int width=844,height=390;}
}
namespace FlipRace.Web {public static class FlipDiagnostics{public static float ratio=1;public static float FlipPixelRatio(){return ratio;}}}
namespace UiReadOnly {
 using System;using FlipRace.Core;using UnityEngine;
 static class MascotArtwork{public static void GetFrame(int a,int b,float c,bool d,bool e,bool f){}}
 public class ReviewUi{
 public GameState Game;bool paused,showRules,showTracks,wasPausedBeforeRules,suppressFrame,gameplayInputBlocked,compact,selectedNegative,trackNegativeChoice;
 int inputBlockFrame,round=1;string selectedTrackId;double lastFrame;float width,height,uiScale,animationTime=0;object tapClip=null;
 Rect shell,session,arena,divider,footer,helpRect,headerHelp,pauseRect,soundRect,fullscreenRect,practiceRect,rulesPanel,rulesClose,rulesDone,resumeRect,trackButton,trackPanel,trackClose,negativeChoiceRect;Rect[] lanes=new Rect[2];
 bool Active{get{return Game.Phase=="racing"||Game.Phase=="countdown";}}bool Portrait{get{return height>width&&width<=700;}}
 void Beep(object x){}void Pause(string x){paused=true;suppressFrame=true;}void ToggleSound(){}
'@

$test=@'
 static int count;static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
 static ReviewUi Fresh(int w,int h,float ratio){Screen.width=(int)(w*ratio);Screen.height=(int)(h*ratio);FlipRace.Web.FlipDiagnostics.ratio=ratio;var v=new ReviewUi();v.Game=RaceSimulation.CreateGame();v.selectedTrackId=v.Game.Track.Id;v.Game.Players[0].CharacterIndex=0;v.Game.Players[1].CharacterIndex=1;v.Measure();return v;}
 public static string Run(){
 foreach(var size in new[]{new[]{1280,720},new[]{844,390},new[]{568,320}})foreach(float ratio in new[]{1f,2f}){
 var u=Fresh(size[0],size[1],ratio);
 Check(!u.Game.Track.NegativeEnabled&&!u.selectedNegative,"default negative off");
 u.Act(0);Check(u.Game.Players[0].Ready,"one ready");
 u.OpenTracks();Check(u.paused&&u.showTracks&&!u.trackNegativeChoice,"open begins committed choice");
 u.Pointer(u.negativeChoiceRect.center);Check(u.trackNegativeChoice&&!u.selectedNegative&&!u.Game.Track.NegativeEnabled,"toggle remains draft");
 u.Pointer(u.trackClose.center);Check(!u.showTracks&&!u.paused&&!u.Game.Track.NegativeEnabled&&u.Game.Players[0].Ready,"cancel preserves config and ready");
 u.gameplayInputBlocked=false;u.OpenTracks();Check(!u.trackNegativeChoice,"reopen discards cancelled draft");
 u.Pointer(u.negativeChoiceRect.center);u.Pointer(u.TrackChoiceRect(0).center);
 Check(u.selectedNegative&&u.Game.Track.NegativeEnabled&&!u.showTracks&&!u.paused,"track applies config");
 Check(!u.Game.Players[0].Ready&&!u.Game.Players[1].Ready&&u.Game.Elapsed==0,"commit resets ready");
 u.Act(0);Check(!u.Game.Players[0].Ready,"same-frame commit cannot leak ready");
 u.gameplayInputBlocked=false;u.SelectCharacter(0,2);u.SelectCharacter(1,3);
 u.Act(0);u.Act(1);Check(u.Game.Phase=="countdown"&&u.Game.Track.NegativeEnabled,"both ready negative countdown");
 var track=u.Game.Track;
 u.Game.Phase="finished";u.Game.Players[0].Ready=false;u.Game.Players[1].Ready=false;
 u.Game.Players[0].ActiveEffect=new SpeedEffect{IsNegative=true,Remaining=.4};u.Game.Players[0].PendingEffects.Enqueue(new SpeedEffect{Remaining=.6});u.Game.Players[0].SlowsEarned=1;
 u.Act(0);Check(u.Game.Phase=="finished","rematch needs second");
 u.Act(1);Check(u.Game.Phase=="countdown"&&Object.ReferenceEquals(track,u.Game.Track)&&u.Game.Track.NegativeEnabled,"rematch retains exact track/config");
 Check(u.Game.Players[0].CharacterIndex==2&&u.Game.Players[1].CharacterIndex==3,"rematch retains heroes");
 Check(u.Game.Players[0].ActiveEffect==null&&u.Game.Players[0].PendingEffects.Count==0&&u.Game.Players[0].SlowsEarned==0,"rematch clears effects");
 u.ToggleMode();Check(u.Game.Mode=="practice"&&u.Game.Track.NegativeEnabled&&u.selectedNegative,"practice preserves config");
 u.gameplayInputBlocked=false;u.ToggleMode();Check(u.Game.Mode=="race"&&u.Game.Track.Id==track.Id&&u.Game.Track.NegativeEnabled,"return preserves config and track");
 Check(!u.Game.Players[0].Ready&&!u.Game.Players[1].Ready&&u.Game.Elapsed==0&&u.Game.Players[0].CharacterIndex==2,"return reset retains hero");
 u.gameplayInputBlocked=false;u.OpenTracks();Check(u.trackNegativeChoice,"reopen uses committed on");
 u.Pointer(u.negativeChoiceRect.center);u.Pointer(u.TrackChoiceRect(0).center);Check(!u.Game.Track.NegativeEnabled&&!u.selectedNegative,"off applies");
 Check(u.negativeChoiceRect.y>=u.TrackChoiceRect(TracksV3.All.Length-1).yMax&&u.negativeChoiceRect.yMax<=u.trackPanel.yMax,"toggle rect inside panel and clear of tracks");
 }
 return "PASS "+count+" V3 extracted UI checks: negative draft/cancel/commit, readiness, same-frame guard, rematch queue reset, practice return, CSS DPR geometry.";
 }}}
'@
Add-Type -TypeDefinition ($core+[Environment]::NewLine+$shim+($methods -join [Environment]::NewLine)+$test) -IgnoreWarnings -WarningAction SilentlyContinue
[UiReadOnly.ReviewUi]::Run()

