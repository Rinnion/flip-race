using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using FlipRace.Core;
using FlipRace.Presentation;

namespace FlipRace.SharedScreenCheck
{
    // Gameplay fixtures only change state through CreateTrial, SetCharacter,
    // Press and Advance. Synthetic camera states are isolated in Camera().
    public static class Audit
    {
        static int checks, games, projections, cameraCases;
        static readonly double[] Steps = { 1d/30, 1d/60, 1d/144, .317, 2.5, 100 };
        static readonly List<object> evidence = new List<object>();

        public static object Run()
        {
            checks=games=projections=cameraCases=0; evidence.Clear();
            foreach(double dt in Steps)
            {
                Simultaneous(dt, false); Simultaneous(dt, true);
                Sequential(dt, false); Sequential(dt, true);
                Linked(dt); GateHit(dt); Landing(dt); Finish(dt);
            }
            Camera();
            return new { status="PASS", checks, games, projections, cameraCases,
                maximumSteps=Steps, gameplayStateInjection=false,
                scope="Actual Core transitions plus production pure SharedFieldView; not Unity renderer, UI input, browser or human acceptance.",
                evidence=evidence.ToArray() };
        }

        static void Check(bool value, string why)
        { checks++; if(!value) throw new InvalidOperationException(why); }
        static void Near(double actual,double expected,string why,double tolerance=1e-7)
        { Check(Math.Abs(actual-expected)<=tolerance,why+": actual="+actual.ToString("R")+" expected="+expected.ToString("R")); }
        static Track Track(double length,Obstacle[] obstacles,params RiskZone[] zones)
        { return new Track(length,obstacles) {Id="screen-independent",RiskZones=zones}; }
        static GameState Start(Track track,FlightSettings flight=null)
        {
            var g=RaceSimulation.CreateTrial(track,flight??FlightPresets.Chosen); games++;
            Check(RaceSimulation.SetCharacter(g,0,3),"Select same hero slot0");
            Check(RaceSimulation.SetCharacter(g,1,3),"Select same hero slot1");
            Check(RaceSimulation.Press(g,0)&&RaceSimulation.Press(g,1),"Ready both");
            RaceSimulation.Advance(g,3);
            Check(g.Phase=="racing"&&g.Elapsed==0,"Countdown reaches racing");
            Check(g.Players[0].Id!=g.Players[1].Id&&g.Players.All(p=>p.CharacterIndex==3),"Same heroes retain distinct player identities");
            Observe(g); return g;
        }
        static void To(GameState g,double at,double dt)
        {
            int guard=0;
            while(g.Phase=="racing"&&at-g.Elapsed>1e-10)
            {
                double before=g.Elapsed;
                RaceSimulation.Advance(g,Math.Min(dt,at-before));
                if(g.Phase=="racing"&&g.Elapsed<=before)throw new InvalidOperationException("Clock stalled");
                if(++guard>100000)throw new InvalidOperationException("Scheduler guard");
            }
            Observe(g);
        }
        static void Press(GameState g,int slot)
        { Check(RaceSimulation.Press(g,slot),"Actual racing Press accepted slot"+slot); Observe(g); }
        static RiskZone Ring(string kind,double x)
        { return new RiskZone{Id="personal-ring",Kind=kind,X=x,Y=226,Radius=8,FreeReward=true}; }
        static Obstacle Stone(double x,double width)
        { return new Obstacle{Id="physical-stone",X=x,Width=width,Height=48,Kind="floor"}; }
        static void State(GameState g,int slot,SharedRewardState expected)
        { Check(SharedFieldView.RewardState(g.Players[slot],g.Track.RiskZones[0].Id)==expected,"Personal state "+slot+" = "+expected); }
        static void Empty(PlayerState p)
        { Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"Ordinary effect queue fully served"); }
        static void End(GameState g,double dt,double t0,double t1)
        {
            To(g,20,dt); Check(g.Phase=="finished","Actual race finished");
            Check(g.Players.All(p=>p.FinishTime.HasValue),"Both physical finishes observed");
            Near(g.Players[0].FinishTime.Value,t0,"Slot0 actual finish");
            Near(g.Players[1].FinishTime.Value,t1,"Slot1 actual finish");
            foreach(var p in g.Players){Near(p.Distance,g.Track.Length,"Finish at physical line");Empty(p);}
        }
        static void Record(string name,double dt,GameState g)
        {
            if(dt!=Steps[0])return;
            evidence.Add(new{name,elapsed=g.Elapsed,phase=g.Phase,winner=g.Winner,
                players=g.Players.Select(p=>new {p.Id,p.CharacterIndex,p.Distance,y=RaceSimulation.GetPlayerCenterY(p),
                    p.Collisions,p.BoostsEarned,p.SlowsEarned,p.FinishBoostsEarned,p.FinishTime,
                    support=p.Support.ToString(),p.SupportObstacleId,p.AirFlipAvailable,
                    collected=p.CollectedRiskIds.OrderBy(x=>x).ToArray(),success=p.SuccessfulRiskIds.OrderBy(x=>x).ToArray(),
                    failed=p.FailedRiskIds.OrderBy(x=>x).ToArray(),negative=p.NegativeRiskIds.OrderBy(x=>x).ToArray()}).ToArray()});
        }

        static void Simultaneous(double dt,bool negative)
        {
            var zone=Ring(negative?"negative":"instant",220);
            var g=Start(Track(2200,Array.Empty<Obstacle>(),zone));
            To(g,.899,dt);State(g,0,SharedRewardState.Available);State(g,1,SharedRewardState.Available);
            To(g,.901,dt);
            foreach(int slot in new[]{0,1})
            {
                State(g,slot,negative?SharedRewardState.Negative:SharedRewardState.Succeeded);
                Check(!SharedFieldView.MarkerAvailable(g.Players[slot],zone.Id),"Contact removes only owner's availability marker");
                Check(g.Players[slot].ActiveEffect!=null,"Effect starts on actual contact");
            }
            Check(!ReferenceEquals(g.Players[0].ActiveEffect,g.Players[1].ActiveEffect),"Effects are personal objects");
            Check(!ReferenceEquals(g.Players[0].CollectedRiskIds,g.Players[1].CollectedRiskIds),"Collected sets are personal objects");
            Check(!SharedFieldView.ObjectVisible(g,zone),"Both consumed ordinary object can disappear");
            End(g,dt,negative?10.3:9.7,negative?10.3:9.7);
            Check((string)g.Winner=="tie","Simultaneous identical paths tie");
            Check(g.Players.All(p=>negative?p.SlowsEarned==1:p.BoostsEarned==1),"Once per player");
            Record(negative?"simultaneous-negative":"simultaneous-instant",dt,g);
        }
        static void Sequential(double dt,bool negative)
        {
            var zone=Ring(negative?"negative":"instant",440);
            var g=Start(Track(2200,new[]{Stone(100,64)},zone));
            Press(g,1); // Own jump clears the stone; the other slot receives a real .5s penalty.
            To(g,1.91,dt);
            Check(g.Players[0].Collisions==1&&g.Players[1].Collisions==0,"Personal stone collision");
            State(g,0,SharedRewardState.Available);State(g,1,negative?SharedRewardState.Negative:SharedRewardState.Succeeded);
            Check(SharedFieldView.ObjectVisible(g,zone)&&SharedFieldView.MarkerAvailable(g.Players[0],zone.Id),"Other player's reward remains available/visible");
            Check(!SharedFieldView.MarkerAvailable(g.Players[1],zone.Id),"Only collector's marker is consumed");
            Record(negative?"first-negative-only":"first-positive-only",dt,g);
            To(g,2.41,dt); State(g,0,negative?SharedRewardState.Negative:SharedRewardState.Succeeded);
            Check(g.Players.All(p=>p.CollectedRiskIds.Contains(zone.Id)),"Second player receives same shared object later");
            End(g,dt,negative?10.8:10.2,negative?10.3:9.7);
            Check((int)g.Winner==1,"First clean player wins independent of hero identity");
            Record(negative?"sequential-negative-finish":"sequential-positive-finish",dt,g);
        }
        static void Linked(double dt)
        {
            var stone=Stone(300,64);
            var zone=Ring("positive",220);zone.FreeReward=false;zone.ObstacleId=stone.Id;zone.ClearX=379;
            var g=Start(Track(2200,new[]{stone},zone));
            To(g,.901,dt);
            foreach(int slot in new[]{0,1})
            {
                State(g,slot,SharedRewardState.Pending);
                Check(!SharedFieldView.MarkerAvailable(g.Players[slot],zone.Id),"Pending marker does not promise recollection");
                Check(g.Players[slot].BoostsEarned==0&&g.Players[slot].ActiveEffect==null,"Contact alone does not award linked boost");
            }
            Check(SharedFieldView.ObjectVisible(g,zone),"Both pending keep shared object visible");
            Press(g,1); To(g,1.31,dt);
            State(g,0,SharedRewardState.Failed);State(g,1,SharedRewardState.Pending);
            Check(g.Players[0].Collisions==1&&g.Players[1].Collisions==0,"Hit cancels only its already-pending reward");
            Check(g.Players.All(p=>p.BoostsEarned==0),"No premature success during pending");
            Record("pending-and-failed",dt,g);
            To(g,1.73,dt);State(g,0,SharedRewardState.Failed);State(g,1,SharedRewardState.Succeeded);
            Check(g.Players[1].BoostsEarned==1&&g.Players[0].BoostsEarned==0,"Clear awards only successful player");
            Check(!SharedFieldView.ObjectVisible(g,zone),"Failed/succeeded ordinary object has no available owner");
            End(g,dt,10.5,9.7);State(g,0,SharedRewardState.Failed);
            Record("linked-success-versus-failure",dt,g);
        }
        static void GateHit(double dt)
        {
            var gate=new Obstacle{Id="one-offset-gate",Kind="gate",X=100,Width=16,GapTop=110,GapBottom=130,TopOffsetX=0,BottomOffsetX=112};
            var g=Start(Track(2200,new[]{gate}),FlightPresets.FastFlip);Press(g,0);
            To(g,.4,dt);
            Check(g.Players[0].Collisions==1&&g.Players[1].Collisions==0,"Earlier upper-band hit belongs only to slot0");
            To(g,1.01,dt);
            Check(g.Players.All(p=>p.Collisions==1&&p.HitObstacleIds.SetEquals(new[]{gate.Id})),"Later lower-band hit remains physical for other slot");
            To(g,2,dt);
            Check(g.Players.All(p=>p.Collisions==1),"Both bands retain one penalty per logical ID/player");
            Check(ObstacleGeometry.TryGetBand(gate,0,out var upper)&&ObstacleGeometry.TryGetBand(gate,1,out var lower),"Both physical bands still exist after personal hits");
            Near(ObstacleGeometry.Right(gate),228,"Union still includes lower half");
            End(g,dt,10.5,10.5);Record("two-personal-hits-one-shared-gate",dt,g);
        }
        static void Landing(double dt)
        {
            var stone=Stone(300,500);
            var pad=new RiskZone{Id="personal-pad",Kind="landing",ObstacleId=stone.Id,X=350,LandingWidth=12};
            var g=Start(Track(2200,new[]{stone},pad));
            double flight=(350+Math.Sqrt(350*350-2*662.5*48))/662.5;
            double launch=350/220d-flight;
            To(g,launch,dt);Press(g,0);Press(g,1);To(g,350/220d+1e-6,dt);
            foreach(var p in g.Players)
            {
                Check(p.Support==SupportKind.Stone&&p.SupportObstacleId==stone.Id,"Analytic descending jump lands on real stone");
                Check(p.AirFlipAvailable&&p.BoostsEarned==1,"Landing recharges and awards once");
                Check(!SharedFieldView.MarkerAvailable(p,pad.Id),"Consumed landing marker removed");
            }
            Check(SharedFieldView.ObjectVisible(g,pad),"Landing rail remains visible after both collected");
            To(g,350/220d+.7,dt);
            foreach(var p in g.Players)
            {Check(p.Support==SupportKind.Stone&&p.Distance>550,"Consumed rail retains physical support away from marker");Check(p.BoostsEarned==1,"No repeated landing award while supported");}
            Record("consumed-pad-still-supports",dt,g);
            End(g,dt,9.7,9.7);
        }
        static void Finish(double dt)
        {
            var zone=new RiskZone{Id="personal-finish",Kind="finish",X=836,Y=226,FinishHeight=16};
            var g=Start(Track(880,Array.Empty<Obstacle>(),zone));
            To(g,760/220d,dt);Press(g,0);To(g,3.90001,dt);
            Check(g.Players[1].FinishTime.HasValue&&!g.Players[0].FinishTime.HasValue&&g.Phase=="racing","First finisher does not end the other player's run");
            Check(g.Players[1].FinishBoostsEarned==1&&g.Players[0].FinishBoostsEarned==0,"Finish target is personal");
            State(g,0,SharedRewardState.Available);State(g,1,SharedRewardState.Succeeded);
            Check(SharedFieldView.ObjectVisible(g,zone),"Missed-but-uncollected object remains available state");
            Record("one-finished-other-still-racing",dt,g);
            End(g,dt,4,3.9);Check((int)g.Winner==1,"Personal .1s target determines winner");
        }

        static void Observe(GameState g)
        {
            string before=Fingerprint(g);
            foreach(var size in new[]{new[]{568d,115},new[]{568d,230},new[]{1280d,270},new[]{1280d,540}})
            {
                var view=SharedFieldView.Create(size[0],size[1],g); projections++;
                for(int slot=0;slot<2;slot++)
                {
                    Near(view.PlayerX(slot),view.WorldX(g.Players[slot].Distance),"Shared canonical X");
                    Near(view.PlayerY(slot),view.WorldY(RaceSimulation.GetPlayerCenterY(g.Players[slot])),"Shared canonical Y without player mirror");
                }
                foreach(var zone in g.Track.RiskZones)
                {SharedFieldView.ObjectVisible(g,zone);foreach(var p in g.Players)SharedFieldView.MarkerAvailable(p,zone.Id);}
            }
            Check(before==Fingerprint(g),"Production pure presentation calls must not mutate Core fields, effects, sets, measurements or track");
        }
        static void Camera()
        {
            // These are expressly synthetic projection fixtures, not reachable
            // gameplay/recovery evidence. No Press/Advance result is claimed.
            foreach(double width in new[]{568d,1280d})
            foreach(double height in new[]{115d,230d,540d})
            {
                var g=RaceSimulation.CreateTrial(Track(7920,Array.Empty<Obstacle>()),FlightPresets.Chosen);
                var initial=SharedFieldView.Create(width,height,g);double sx=initial.ScaleX;
                Near(initial.PlayerX(0),width*.25,"Equal-distance anchor");
                Near(initial.PlayerX(0),initial.PlayerX(1),"Overlapping players keep exact physical X");
                Near(initial.PlayerY(0),initial.PlayerY(1),"Overlapping players keep exact physical Y");
                foreach(double gapPx in new[]{0d,.2*width,.44*width,.47*width,.48*width,.8*width})
                foreach(int leader in new[]{0,1})
                {
                    g.Players[leader].Distance=3000+gapPx/sx;g.Players[1-leader].Distance=3000;
                    string before=Fingerprint(g);var view=SharedFieldView.Create(width,height,g);cameraCases++;
                    Near(view.ScaleX,sx,"Camera never scales with gap");
                    Near(view.Gap,gapPx/sx,"Indicator uses actual world gap");
                    Near(view.PlayerX(leader)-view.PlayerX(1-leader),gapPx,"No fake clamped follower position");
                    Check(view.PlayerX(leader)<=.47*width+1e-8,"Leader cap immediate");
                    if(gapPx>.47*width+1e-8)Check(!view.IsPlayerVisible(1-leader),"Follower leaves view when physical gap exceeds capped leader position");
                    if(gapPx==0)Check(view.IsPlayerVisible(0)&&view.IsPlayerVisible(1),"Returning pair visible");
                    Check(before==Fingerprint(g),"Synthetic projection is also read-only");
                }
                g.Players[0].Distance=g.Players[1].Distance=7920;
                var finish=SharedFieldView.Create(width,height,g);
                g.Players[0].Distance=g.Players[1].Distance=8000;
                Near(SharedFieldView.Create(width,height,g).CameraLeft,finish.CameraLeft,"Camera cannot chase beyond physical finish");
            }
        }
        public static string Fingerprint(object value)
        {var builder=new StringBuilder();Append(builder,value,0);return builder.ToString();}
        static void Append(StringBuilder b,object value,int depth)
        {
            if(depth>30)throw new InvalidOperationException("Fingerprint recursion guard");
            if(value==null){b.Append("null;");return;}
            Type t=value.GetType();b.Append(t.FullName).Append(':');
            if(t.IsPrimitive||t.IsEnum||value is string||value is decimal)
            {b.Append(Convert.ToString(value,CultureInfo.InvariantCulture)).Append(';');return;}
            if(value is IEnumerable sequence)
            {b.Append('[');foreach(var item in sequence)Append(b,item,depth+1);b.Append(']');return;}
            foreach(var field in t.GetFields(BindingFlags.Instance|BindingFlags.Public).OrderBy(f=>f.Name))
            {b.Append(field.Name).Append('=');Append(b,field.GetValue(value),depth+1);}
            foreach(var prop in t.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0).OrderBy(p=>p.Name))
            {b.Append(prop.Name).Append('=');Append(b,prop.GetValue(value),depth+1);}
            b.Append(';');
        }
    }
}
