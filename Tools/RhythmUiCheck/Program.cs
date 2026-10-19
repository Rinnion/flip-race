using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.IO;
using System.Linq;
using FlipRace.Core;
using FlipRace.Presentation;
using UnityEngine;

static class Program
{
 static int checks;
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static Rect Field(object game,string name)=>(Rect)game.GetType().GetField(name,Private).GetValue(game);
 static Rect Method(object game,string name,int index)=>(Rect)game.GetType().GetMethod(name,Private).Invoke(game,new object[]{index});
 static object RectData(Rect r)=>new{x=r.x,y=r.y,width=r.width,height=r.height};
 static bool Overlaps(Rect a,Rect b)=>a.xMin<b.xMax&&b.xMin<a.xMax&&a.yMin<b.yMax&&b.yMin<a.yMax;
 static int Main()
 {
  try { Verify(); return 0; }
  catch(Exception e) { Console.Error.WriteLine(e); return 1; }
 }
 static void Verify()
 {
  string[] ids={TracksFull.LongId,TracksFull.ShortId,TracksFull.FlowId,TracksRhythm.CascadeId,TracksRhythm.DenseId,TracksRhythm.WavesId};
  Check(RaceTracks.All.Select(t=>t.Id).SequenceEqual(ids),"activated catalogue contains the actual six cards in declared order");
  Check(TracksFull.All.Length==3&&TracksRhythm.All.Length==3,"three previous plus three new courses");
  Check(RaceTracks.All.Select(t=>t.Name).Distinct().Count()==6,"distinct visible course names");
  foreach(var track in RaceTracks.All) {
   Check(RaceTracks.IsFull(track.Id)&&ReferenceEquals(RaceTracks.Get(track.Id),track),"actual selection resolves the same full track");
   Check(track.Length==7920&&!string.IsNullOrWhiteSpace(track.Focus),"full length and readable focus");
   Check(track.RiskZones.Count(z=>!z.IsFinish&&!z.IsNegative)==9,"nine ordinary rewards per actual course");
   Check(track.RiskZones.Count(z=>z.IsFinish)==1&&track.RiskZones.Count(z=>z.IsNegative)==2&&track.NegativeEnabled,"one finish reward and two active optional traps");
  }
  Check(RaceTracks.Get(TracksRhythm.PracticeId).Id==TracksRhythm.PracticeId&&!RaceTracks.IsFull(TracksRhythm.PracticeId),"short gate trial remains separate");
  var exports=new List<object>();
  foreach(var viewport in new[]{(w:568f,h:320f),(w:844f,h:390f),(w:1280f,h:720f)})
  {
   // No native MonoBehaviour lifecycle, Update, GUI or fake screen state is run.
   // Only the actual production pure rectangle calculations are invoked.
   var game=RuntimeHelpers.GetUninitializedObject(typeof(FlipRaceGame));
   typeof(FlipRaceGame).GetField("width",Private).SetValue(game,viewport.w);
   typeof(FlipRaceGame).GetField("height",Private).SetValue(game,viewport.h);
   typeof(FlipRaceGame).GetMethod("MeasureFlightMenu",Private).Invoke(game,null);
   Rect panel=Field(game,"flightPanel"),back=Field(game,"flightBack"),apply=Field(game,"flightApply");
   Check(panel.yMin>=28&&panel.yMax<=viewport.h,"panel clears version badge and viewport");
   var oldIds=(string[])typeof(FlipRaceGame).GetField("OldTrackIds",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   Check(oldIds.Length==7&&oldIds.Contains(TracksRhythm.PracticeId),"all seven earlier sections remain available");
   foreach(var page in new[]{(name:"full",method:"FullTrackRect",count:RaceTracks.All.Length,min:44f),(name:"old",method:"OldTrackRect",count:oldIds.Length,min:34f)})
   {
    var cards=new List<Rect>();
    for(int i=0;i<page.count;i++) {
     Rect r=Method(game,page.method,i);Check(r.height>=page.min,"readable full touch height");
     Check(r.xMin>=panel.xMin&&r.xMax<=panel.xMax&&r.yMin>=panel.yMin&&r.yMax<=panel.yMax,"card remains inside panel");
     Check(!Overlaps(r,back)&&!Overlaps(r,apply),"card does not overlap back/apply");
     foreach(var previous in cards)Check(!Overlaps(previous,r),"cards do not overlap");cards.Add(r);
    }
    exports.Add(new{viewport,page=page.name,panel=RectData(panel),apply=RectData(apply),cards=cards.Select((rect,i)=>new{id=page.name=="full"?ids[i]:oldIds[i],rect=RectData(rect)})});
   }
   // Full-card clicks invoke the real menu handler. They only change the draft
   // track, without native lifecycle, player readiness, saved custom or physics.
   var state=RaceSimulation.CreateTrial(RaceTracks.All[0],FlightPresets.Chosen);
   state.Players[0].CharacterIndex=2;state.Players[1].CharacterIndex=3;
   typeof(FlipRaceGame).GetProperty("Game").SetValue(game,state);
   typeof(FlipRaceGame).GetField("flightPage",Private).SetValue(game,4);
   var selection=new FlightSelection(FlightDraft.From(FlightPresets.FastFlip));
   typeof(FlipRaceGame).GetField("flightSelection",Private).SetValue(game,selection);
   string custom=FlightSettingsStore.Encode(selection.Custom);
   var handler=typeof(FlipRaceGame).GetMethod("FlightPointer",Private);
   foreach(int choice in new[]{10,11,-1}) {
    if(choice<0)selection.SelectCustom();else selection.SelectPreset(choice);
    foreach(var i in Enumerable.Range(0,ids.Length)) {
     Rect r=Method(game,"FullTrackRect",i);
     foreach(var point in new[]{r.center,new Vector2(r.xMin+.1f,r.yMin+.1f),new Vector2(r.xMax-.1f,r.yMax-.1f)}) {
      handler.Invoke(game,new object[]{point});
      Check((string)typeof(FlipRaceGame).GetField("pendingFlightTrack",Private).GetValue(game)==ids[i],"whole actual card selects matching id");
      Check(selection.PresetIndex==choice&&FlightSettingsStore.Encode(selection.Custom)==custom,"track click retains preset/custom and exact saved fields");
      Check(ReferenceEquals(typeof(FlipRaceGame).GetProperty("Game").GetValue(game),state)&&state.Phase=="ready","track choice does not rebuild or auto-start game");
      Check(state.Players[0].CharacterIndex==2&&state.Players[1].CharacterIndex==3&&!state.Players[0].Ready&&!state.Players[1].Ready,"track choice preserves heroes and readiness");
     }
    }
   }
   string pending=(string)typeof(FlipRaceGame).GetField("pendingFlightTrack",Private).GetValue(game);
   var left=Method(game,"FullTrackRect",0);var right=Method(game,"FullTrackRect",1);
   handler.Invoke(game,new object[]{new Vector2((left.xMax+right.xMin)/2,left.center.y)});
   Check((string)typeof(FlipRaceGame).GetField("pendingFlightTrack",Private).GetValue(game)==pending&&state.Phase=="ready","grid gap has no hidden selection/gameplay action");
  }
  var bands=new List<object>();
  foreach(var obstacle in TracksRhythm.Practice().Obstacles)
  {
   bool offset=CorridorArtwork.TryOffsetEntry(obstacle,out bool floor);
   Check(offset==(obstacle.TopOffsetX!=obstacle.BottomOffsetX),"cue geometry distinguishes straight and offset");
   if(offset)Check(floor==(obstacle.TopOffsetX<obstacle.BottomOffsetX),"entry cue follows actual part order");
   bool low=CorridorArtwork.TryLowThreshold(obstacle,out bool floorThreshold);
   Check(low==obstacle.Id.EndsWith("-low"),"low family uses two-route hint instead of prescribed crossing arrow");
   if(low)Check(floorThreshold,"stage has physical floor threshold first");
   for(int i=0;i<2;i++)if(ObstacleGeometry.TryGetBand(obstacle,i,out var band))
    bands.Add(new{id=obstacle.Id,part=i,fromFloor=floor,band.X,band.Y,band.Width,band.Height});
  }
  Directory.CreateDirectory("Verification");
  File.WriteAllText("Verification/rhythm-final-ui-layout.json",JsonSerializer.Serialize(new{catalogue=RaceTracks.All.Select(t=>new{t.Id,t.Name,t.Focus}),layouts=exports,bands},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
  Console.WriteLine("RHYTHM FINAL UI PASS "+checks+" checks; actual catalogue/selection handler, 3 viewports, six full/seven previous cards, retained controls/custom/heroes, physical band export.");
 }
}
