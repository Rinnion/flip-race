using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;
using FlipRace.SharedScreenCheck;
using UnityEngine;

static class Program
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks,geometryCalls,hookCalls;
    static void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
    // Unity Rect/Geometry values are floats; use sub-millipixel tolerance.
    static void Near(double a,double b,string why,double tolerance=1e-4){Check(Math.Abs(a-b)<=tolerance,why+" "+a+" / "+b);}
    static void Set(object obj,string name,object value){obj.GetType().GetField(name,Private).SetValue(obj,value);}
    static object Call(object obj,string name,params object[] args){return obj.GetType().GetMethod(name,Private).Invoke(obj,args);}
    static int Main()
    {
        try
        {
            Geometry();StageHooks();
            Console.WriteLine(JsonSerializer.Serialize(new {status="PASS",checks,geometryCalls,hookCalls,
                scope="Actual production SharedPlayerGeometry and stage Pointer/rectangle hooks, using managed Unity value types. Synthetic layout fixture, not Measure/OnGUI/Update/native rendering or SCREEN02 menu acceptance."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Geometry()
    {
        var art=(CorridorArtwork)RuntimeHelpers.GetUninitializedObject(typeof(CorridorArtwork));
        Set(art,"jumpRotation",new float[2]);Set(art,"jumpRotationTime",new[]{-1f,-1f});
        var stone=new Obstacle{Id="stone",Kind="floor",X=300,Width=64,Height=48};
        var track=new Track(2200,new[]{stone}){RiskZones=new[]{new RiskZone{Id="linked",Kind="positive",X=220,Y=226,Radius=8,ObstacleId=stone.Id,ClearX=379}}};
        var game=RaceSimulation.CreateTrial(track,FlightPresets.Chosen);
        RaceSimulation.SetCharacter(game,0,3);RaceSimulation.SetCharacter(game,1,3);
        RaceSimulation.Press(game,0);RaceSimulation.Press(game,1);RaceSimulation.Advance(game,3);
        foreach(double at in new[]{0d,.901,1.31,1.73,2.1,9.71,10.51})
        {
            if(at>.901&&game.Elapsed<.902)Check(RaceSimulation.Press(game,1),"Real jump creates distinct trajectories");
            while(game.Phase=="racing"&&at-game.Elapsed>1e-10)RaceSimulation.Advance(game,Math.Min(1d/60,at-game.Elapsed));
            string fingerprint=Audit.Fingerprint(game);
            foreach(var size in new[]{new[]{550d,133d},new[]{550d,290d},new[]{1190d,201.6},new[]{1190d,435.2}})
            {
                var view=SharedFieldView.Create(size[0],size[1],game);
                for(int slot=0;slot<2;slot++)
                {
                    var output=(CorridorArtwork.Geometry)Call(art,"SharedPlayerGeometry",view,game,slot,(float)at);geometryCalls++;
                    Near(output.X,(float)view.WorldX(game.Players[slot].Distance),"Production renderer geometry exact common X");
                    Near(output.Y,(float)view.WorldY(RaceSimulation.GetPlayerCenterY(game.Players[slot])),"Production renderer geometry exact common Y");
                    Check(!output.MirrorX,"No individual shared-world mirror");
                    Check(output.Recovering==(game.Players[slot].Recovering>0),"Personal recovery presentation");
                    Check(output.Running==(game.Phase=="racing"&&!game.Players[slot].FinishTime.HasValue&&game.Players[slot].Recovering<=0),"Personal running/finished state");
                }
            }
            Check(fingerprint==Audit.Fingerprint(game),"Production renderer geometry read path does not alter simulation");
        }
        Check(game.Players[0].FailedRiskIds.Contains("linked")&&game.Players[1].SuccessfulRiskIds.Contains("linked"),"Renderer sampled actual pending/failed/success transitions");
        Near(game.Players[0].FinishTime.Value,10.5,"Renderer read path loser finish");Near(game.Players[1].FinishTime.Value,9.7,"Renderer read path winner finish");
    }
    static FlipRaceGame Ui(GameState game,float width,float height,Rect arena,float laneHeight)
    {
        var ui=(FlipRaceGame)RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
        typeof(FlipRaceGame).GetProperty("Game").SetValue(ui,game);
        Set(ui,"width",width);Set(ui,"height",height);Set(ui,"compact",height<=600);
        Set(ui,"arena",arena);Set(ui,"lanes",new[]{new Rect(arena.x,arena.y,arena.width,laneHeight),new Rect(arena.x,arena.yMax-laneHeight,arena.width,laneHeight)});
        Set(ui,"sharedField",true); // stage default: opposite seating and half height.
        return ui;
    }
    static void Pointer(FlipRaceGame ui,Vector2 p){Call(ui,"Pointer",p);hookCalls++;}
    static void StageHooks()
    {
        foreach(var size in new[]{(w:568f,h:320f,arena:new Rect(9,28,550,290),lane:133f),(w:1280f,h:720f,arena:new Rect(45,172,1190,435.2f),lane:201.6f)})
        {
            // Explicit layout fixture. This verifies real field/control getters
            // and routing, not the native Screen/PixelRatio-based Measure method.
            var game=RaceSimulation.CreateTrial(new Track(220,Array.Empty<Obstacle>()){Id=TracksMixed.Id},FlightPresets.FastFlip);
            RaceSimulation.SetCharacter(game,0,3);RaceSimulation.SetCharacter(game,1,3);
            var ui=Ui(game,size.w,size.h,size.arena,size.lane);
            Check(ui.SharedEnabled&&ui.SeatingName=="opposite"&&ui.FieldHeightName=="half","Stage default shared opposite/half");
            Rect field=ui.SharedFieldRect;
            Near(field.width,size.arena.width,"Full original arena width");Near(field.height,size.lane,"Height of one old lane");Near(field.center.y,size.arena.center.y,"Field centred in arena");
            var top=ui.ControlAreaRect(0);var bottom=ui.ControlAreaRect(1);
            Near(top.yMin,size.arena.yMin,"Top input begins at arena edge");Near(bottom.yMax,size.arena.yMax,"Bottom input ends at arena edge");Near(top.yMax,bottom.yMin,"Control regions cover entire arena without dead middle");
            Pointer(ui,ui.ReadyScreenRect(0).center);Check(game.Players[0].Ready&&!game.Players[1].Ready,"Only first explicit readiness button");
            Pointer(ui,ui.ReadyScreenRect(1).center);Check(game.Phase=="countdown"&&game.Players.All(p=>!p.Flipping),"Ready tap starts countdown without flight");
            RaceSimulation.Advance(game,3);
            var first=new Vector2(size.arena.center.x,size.arena.yMin+1);
            var second=new Vector2(size.arena.center.x,size.arena.yMax-1);
            Check(!field.Contains(first)&&!field.Contains(second),"Test points lie in blank margins outside drawn corridor");
            Pointer(ui,first);Check(game.Players[0].Flipping&&!game.Players[1].Flipping,"Top blank margin routes only slot0");
            Pointer(ui,second);Check(game.Players.All(p=>p.Flipping),"Bottom blank margin routes only slot1");
            string before=Audit.Fingerprint(game);Pointer(ui,new Vector2(size.arena.xMin-1,size.arena.center.y));Check(before==Audit.Fingerprint(game),"Outside arena no gameplay");
            Set(ui,"showRules",true);before=Audit.Fingerprint(game);Pointer(ui,first);Check(before==Audit.Fingerprint(game),"Rules modal prevents arena gameplay");Set(ui,"showRules",false);
            Set(ui,"paused",true);before=Audit.Fingerprint(game);Pointer(ui,second);Check(before==Audit.Fingerprint(game),"Pause prevents arena gameplay");Set(ui,"paused",false);
            Set(ui,"gameplayInputBlocked",true);before=Audit.Fingerprint(game);Pointer(ui,first);Check(before==Audit.Fingerprint(game),"Consumed UI input blocker prevents gameplay");Set(ui,"gameplayInputBlocked",false);
            RaceSimulation.Advance(game,2);Check(game.Phase=="finished","Real short game finish");
            Pointer(ui,first);Check(game.Phase=="finished"&&game.Players[0].Ready&&!game.Players[1].Ready,"Rematch requires second readiness");
            Pointer(ui,second);Check(game.Phase=="countdown"&&game.Elapsed==0,"Second rematch input resets Core");
            Check(ui.SharedEnabled&&ui.SeatingName=="opposite"&&ui.FieldHeightName=="half"&&game.Players.All(p=>p.CharacterIndex==3),"Rematch preserves stage view and same heroes");
        }
    }
}
