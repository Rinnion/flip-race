using System;
using FlipRace.Core;

namespace FlipRace.Editor
{
    // Finish stroke contact and the existing moving-clock effect contract.
    // Simulator fixtures use actual Press/Advance, without player injection;
    // standalone geometry fixtures isolate exact tangencies and strip entry.
    public static class EngineFinishTests
    {
        static int assertions;
        static readonly double[] Frames={1d/30,1d/60,1d/144,.317,2.5,100};
        static readonly MotionMode[] Modes={MotionMode.GravityAirFlip,MotionMode.LinearAirFlip,
            MotionMode.JumpGravityFlip,MotionMode.GravityUnlimited,MotionMode.Linear};
        public static string RunAll()
        {
            assertions=0;
            Action[] tests={StrokeGeometry,PairedHitMissAndTie,ExactBodyBoundaries,LegacyFieldsCannotExpandTarget,SweptOnceAndLateTape,
                SuspendedOrdinaryLedger,ConcurrentInstant,CollisionFreeze,PauseSnapshotAndRematch,ExactGraceFinish};
            foreach(var test in tests)try{test();}catch(Exception e){throw new Exception("FINISH FAIL "+test.Method.Name+": "+e.Message,e);}
            return "PASS: chevron finish "+tests.Length+" scenarios / "+assertions+" assertions.";
        }
        static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
        static void Near(double actual,double expected,string message,double tolerance=2e-8)
        {Check(Math.Abs(actual-expected)<=tolerance,message+": "+actual+" vs "+expected);}
        const double BodyStrokeRadius = 15.5; // Independently specified r14 body + stroke3/2.
        static readonly double CenterlineReach = 10.5 + Math.Sqrt(168);
        static RiskZone TargetAt(double firstContactX) { return Target(firstContactX + CenterlineReach); }
        static RiskZone Target(double x=2000,double y=226,double height=8,string id="finish")
        {return new RiskZone{Id=id,Kind="finish",X=x,Y=y,FinishWidth=28,FinishHeight=height,Radius=10000};}
        static Track Empty(double length=2200,RiskZone[] zones=null,Obstacle[] obstacles=null)
        {return new Track(length,obstacles??Array.Empty<Obstacle>()){RiskZones=zones??new[]{Target()}};}
        static GameState Start(Track track,MotionMode mode=MotionMode.GravityAirFlip)
        {
            var profile=mode==MotionMode.JumpGravityFlip?MotionProfiles.Jump:
                mode==MotionMode.LinearAirFlip||mode==MotionMode.Linear?MotionProfiles.Baseline:MotionProfiles.Moderate;
            var game=RaceSimulation.CreateTrial(track,mode,profile);
            Tap(game);RaceSimulation.Advance(game,3);Check(game.Phase=="racing"&&game.Elapsed==0,"actual readiness/countdown");return game;
        }
        static void Tap(GameState game)
        {Check(RaceSimulation.Press(game,0)&&RaceSimulation.Press(game,1),"accepted personal action edges");}
        static void AdvanceFor(GameState game,double duration,double frame)
        {
            Check(duration>=-1e-9,"monotonic public input clock");
            while(duration>1e-12&&game.Phase=="racing"){double dt=Math.Min(duration,frame);RaceSimulation.Advance(game,dt);duration-=dt;}
        }
        static void NoOrdinary(PlayerState p)
        {Check(p.BoostsEarned==0&&p.CompletedBoosts==0&&p.CompletedSlows==0&&p.ActiveEffect==null&&p.PendingEffects.Count==0,"finish remains separate from ordinary effects");}
        static void PairedHitMissAndTie()
        {
            foreach(var mode in Modes)foreach(double frame in Frames){
                var game=Start(Empty(),mode);AdvanceFor(game,1850d/220,frame);
                Check(RaceSimulation.Press(game,1),"opponent takes a real avoiding flight");
                AdvanceFor(game,100,frame);var p=game.Players[0];var q=game.Players[1];
                Check(p.Collisions==0&&q.Collisions==0,"independent clean take/skip");
                Near(p.FinishTime.Value,9.75,"full real take arrival");Near(q.FinishTime.Value,10,"real skip arrival");
                Near(q.FinishTime.Value-p.FinishTime.Value,.25,"actual independent gain");
                Near(p.LastFinishBoost.At,(2000-CenterlineReach)/220,"circular body reaches visible stroke end");
                Near(p.FinishBoostElapsed,.25,"full moving timer");Near(p.FinishBoostDistanceGained,55,"real extra55 units");
                Check(p.FinishBoostsEarned==1&&q.FinishBoostsEarned==0&&Equals(game.Winner,0),"personal collection and winner");
                Check(!p.FinishBoost&&!q.FinishBoost&&p.Distance==2200&&q.Distance==2200,"physical finish clears pulse");NoOrdinary(p);NoOrdinary(q);
                var tied=Start(Empty(),mode);AdvanceFor(tied,100,frame);
                Check(Equals(tied.Winner,"tie")&&tied.Players[0].FinishBoostsEarned==1&&tied.Players[1].FinishBoostsEarned==1,"same pad available separately to both");
                Near(tied.Elapsed,9.75,"same physical tie timestamp");
            }
        }
        static void StrokeGeometry()
        {
            Near(FinishTargetGeometry.StrokeWidth,3,"shared visible stroke thickness");
            Near(FinishTargetGeometry.HalfWidth,10.5,"specified centerline half-width");
            Near(FinishTargetGeometry.HalfHeight,8.5,"specified centerline half-height");
            Check(FinishTargetGeometry.SegmentCount==4,"two complete chevrons");
            double[,] expected={{-10.5,-8.5,-2.1,0},{-2.1,0,-10.5,8.5},{2.1,-8.5,10.5,0},{10.5,0,2.1,8.5}};
            for(int i=0;i<4;i++){
                var s=FinishTargetGeometry.GetSegment(i);
                Near(s.X1,expected[i,0],"specified stroke start X");Near(s.Y1,expected[i,1],"specified stroke start Y");
                Near(s.X2,expected[i,2],"specified stroke end X");Near(s.Y2,expected[i,3],"specified stroke end Y");
            }
            Near(FinishTargetGeometry.DistanceSquaredToStrokes(-6.3,-4.25),0,"actual stroke interior");
            Near(FinishTargetGeometry.DistanceSquaredToStrokes(0,0),4.41,"visible gap has no centerline");
            Check(double.IsInfinity(FinishTargetGeometry.FirstContact(0,0,0,0,0,0,100)),"unpainted gap is not a rectangular target");
            double nx=-8.5/Math.Sqrt(142.81),ny=8.4/Math.Sqrt(142.81);
            Near(FinishTargetGeometry.FirstContact(-6.3+3*nx,-4.25+3*ny,-nx,-ny,0,0,100),1.5,"normal entry through diagonal stroke interior");
            double tx=-6.3-1.5*nx,ty=-4.25-1.5*ny;
            Near(FinishTargetGeometry.FirstContact(tx,ty-1,0,2,-2,0,100),1,"quadratic interior tangency",1e-7);
            Check(double.IsInfinity(FinishTargetGeometry.FirstContact(tx,ty-1-.00001,0,2,-2,0,100)),"quadratic stroke tangent miss");
            Near(FinishTargetGeometry.FirstContact(-50,24,1,0,0,14,100),39.5,"large-dt rounded endpoint tangent");
            Check(double.IsInfinity(FinishTargetGeometry.FirstContact(-50,24.00001,1,0,0,14,100)),"outside rounded endpoint misses");
            Near(FinishTargetGeometry.FirstContact(-10.5,40,0,-10,2,14,100),2,"quartic curved endpoint entry",1e-7);
            Near(FinishTargetGeometry.FirstContact(-10.5,49,0,-10,2,14,100),5,"quartic double-root endpoint tangent",1e-7);
            Check(double.IsInfinity(FinishTargetGeometry.FirstContact(-10.5,49.00001,0,-10,2,14,100)),"quartic curved endpoint miss");
        }
        static void ExactBodyBoundaries()
        {
            foreach(double frame in Frames)foreach(double gap in new[]{0d,8,14,15.5,15.50001}){
                // Floor center226; nearest lower-left stroke endpoint is
                // (1989.5,226-gap). Legacy rectangle/ring sizes have no effect.
                var game=Start(Empty(zones:new[]{Target(2000,217.5-gap)}));var p=game.Players[0];
                if(gap<=BodyStrokeRadius){
                    double x=1989.5-Math.Sqrt(BodyStrokeRadius*BodyStrokeRadius-gap*gap);
                    AdvanceFor(game,(x-.001)/220,frame);Check(p.FinishBoostsEarned==0,"no invisible corner/box pickup");
                    AdvanceFor(game,.001/220+1e-11,frame);Near(p.Distance,x,"rounded endpoint contact X",1e-6);
                    Near((1989.5-p.Distance)*(1989.5-p.Distance)+gap*gap,BodyStrokeRadius*BodyStrokeRadius,"actual endpoint boundary",1e-6);
                    Check(p.FinishBoostsEarned==1,"inclusive stroke boundary gap="+gap+" frame="+frame);Near(p.FinishBoostElapsed,0,"no duration consumed at activation");
                    Near(p.LastFinishBoost.At,x/220,"exact continuous event time");
                    AdvanceFor(game,100,frame);Near(p.FinishTime.Value,9.75,"boundary retains full effect");
                }else{
                    AdvanceFor(game,100,frame);Check(p.FinishBoostsEarned==0,"outside exact tangent misses despite Radius10000");Near(p.FinishTime.Value,10,"clean miss costs nothing");
                }
            }
        }
        static void LegacyFieldsCannotExpandTarget()
        {
            foreach(double frame in Frames)foreach(double width in new[]{0d,28,10000}){
                var zone=Target(2000,200);zone.FinishWidth=width;zone.FinishHeight=10000;
                var game=Start(Empty(zones:new[]{zone}));AdvanceFor(game,100,frame);
                Check(game.Players[0].FinishBoostsEarned==0,"legacy dimensions and Radius cannot enlarge strokes");
                Near(game.Players[0].FinishTime.Value,10,"legacy dimensions do not award time");
                zone=Target();zone.FinishWidth=0;zone.FinishHeight=-1;
                game=Start(Empty(zones:new[]{zone}));AdvanceFor(game,100,frame);
                Check(game.Players[0].FinishBoostsEarned==1,"obsolete dimensions cannot disable valid stroke contact");
            }
        }
        static void SweptOnceAndLateTape()
        {
            foreach(double frame in Frames){
                var swept=Start(Empty(zones:new[]{Target(),Target(2000,id:"duplicate"),Target(2100,id:"later")}));
                AdvanceFor(swept,100,frame);var p=swept.Players[0];
                Check(p.FinishBoostsEarned==1&&p.SuccessfulRiskIds.Count==1,"whole stroke sweep and later contacts never repeat");
                Near(p.FinishTime.Value,9.75,"no stacked credit");
                var late=Start(Empty(1000,new[]{Target(980)}));AdvanceFor(late,100,frame);p=late.Players[0];
                double served=(20+CenterlineReach)/440;
                Near(p.FinishBoostElapsed,served,"late geometry serves only remaining movement");
                Near(p.FinishBoostDistanceGained,220*served,"late extra distance is physical");
                Near(p.FinishTime.Value,1000d/220-served,"no fabricated quarter-second at tape");Check(!p.FinishBoost,"tape clears partial pulse");
                foreach(double x in new[]{0d,CenterlineReach,43,44,45,63,64,65}){
                    var small=Start(Empty(44,new[]{Target(x)}));AdvanceFor(small,100,frame);
                    double first=Math.Max(0,x-CenterlineReach);bool valid=x<44;
                    Near(small.Elapsed,valid?first/220+(44-first)/440:.2,"geometric contact before/at/after tape");
                    Check(small.Players[0].FinishBoostsEarned==(valid?1:0),"only zones centered within the course award before tape");
                }
            }
        }
        static RiskZone Ordinary(string id,string kind,double x)
        {return new RiskZone{Id=id,Kind=kind,X=x,Y=226,Radius=8,FreeReward=kind=="positive"};}
        static void SuspendedOrdinaryLedger()
        {
            foreach(bool slowFirst in new[]{false,true})foreach(double frame in Frames){
                var game=Start(Empty(1000,new[]{Ordinary("first",slowFirst?"negative":"positive",50),Ordinary("queued",slowFirst?"positive":"negative",90),TargetAt(72)}));
                double speed=slowFirst?110:330,at=28d/220+44/speed;AdvanceFor(game,at,frame);var p=game.Players[0];
                Check(p.FinishBoost&&p.CurrentSpeed==440&&p.ActiveEffect!=null&&p.PendingEffects.Count==1,"finish preempts speed without multiplying");
                var active=p.ActiveEffect;var queued=p.PendingEffects.Peek();double remaining=active.Remaining,elapsed=active.Elapsed;
                Near(p.Distance,72,"actual overlap contact");AdvanceFor(game,.125,frame);Near(p.Distance,127,"fixed x2 motion under boost/slow");
                Near(active.Remaining,remaining,"active remaining suspended");Near(active.Elapsed,elapsed,"active ledger suspended");Near(queued.Remaining,.6,"queued clock suspended");
                AdvanceFor(game,.125,frame);Check(!p.FinishBoost&&ReferenceEquals(active,p.ActiveEffect)&&ReferenceEquals(queued,p.PendingEffects.Peek()),"same effect/FIFO identities resume");
                Near(active.Remaining,remaining,"ordinary clock unchanged at exact expiration");Near(p.CurrentSpeed,speed,"ordinary speed restored");
                AdvanceFor(game,100,frame);Check(p.CompletedBoosts==1&&p.CompletedSlows==1,"both full ordinary pulses still served");
                Near(p.FinishTime.Value,1000d/220-.25,"balanced ordinary pair plus true finish gain");Near(p.FinishBoostDistanceGained,55,"finish ledger counts base-relative motion");
            }
        }
        static void ConcurrentInstant()
        {
            foreach(double frame in Frames){
                var game=Start(Empty(1000,new[]{Ordinary("slow","negative",50),TargetAt(72),Ordinary("instant","instant",130)}));
                AdvanceFor(game,28d/220+44d/110,frame);var p=game.Players[0];var slow=p.ActiveEffect;double remaining=slow.Remaining;
                AdvanceFor(game,36d/440,frame);
                Check(p.FinishBoost&&p.CurrentSpeed==440&&p.InstantBoostsEarned==1,"instant during pulse never multiplies speed");
                Check(p.ActiveEffect!=slow&&ReferenceEquals(p.PendingEffects.Peek(),slow),"normal instant priority preserves original slow at queue front");
                Near(slow.Remaining,remaining,"overlap retains suspended slow remainder");AdvanceFor(game,100,frame);
                Check(p.CompletedBoosts==1&&p.CompletedSlows==1,"overlapping ordinary effects settle completely");Near(p.FinishTime.Value,1000d/220-.25,"overlap physical arrival ledger");
            }
        }
        static void CollisionFreeze()
        {
            foreach(bool simultaneous in new[]{false,true})foreach(double frame in Frames){
                var stone=new Obstacle{Id="stone",Kind="floor",X=230,Width=20,Height=48};
                var game=Start(Empty(1000,new[]{TargetAt(simultaneous?216:172)},new[]{stone}));
                double hitAt=simultaneous?216d/220:172d/220+.1;AdvanceFor(game,hitAt,frame);var p=game.Players[0];
                Check(p.Collisions==1&&p.FinishBoost&&p.Recovering>0,"actual impact with active pulse");Near(p.Distance,216,"true collision X");
                double served=simultaneous?0:.1;Near(p.FinishBoostElapsed,served,"only pre-hit movement consumes pulse");
                Check(!RaceSimulation.Press(game,0),"pulse grants no recovery immunity");AdvanceFor(game,.49,frame);
                Near(p.Distance,216,"recovery freezes distance");Near(p.FinishBoostElapsed,served,"recovery freezes moving timer");
                AdvanceFor(game,100,frame);Near(p.FinishBoostElapsed,.25,"remaining pulse eventually served");Near(p.FinishBoostDistanceGained,55,"full physical gain after collision");
                Near(p.FinishTime.Value,1000d/220+.5-.25,"real finish includes collision delay");
            }
        }
        static void PauseSnapshotAndRematch()
        {
            var game=Start(Empty(1000,new[]{TargetAt(172)}));AdvanceFor(game,172d/220+.04,.317);var p=game.Players[0];
            Check(p.FinishBoost,"snapshot within pulse");double x=p.Distance,served=p.FinishBoostElapsed,time=game.Elapsed;
            RaceSimulation.Advance(game,0);RaceSimulation.Advance(game,double.NaN);
            Check(p.Distance==x&&p.FinishBoostElapsed==served&&game.Elapsed==time,"withheld/invalid advance does not consume pulse");
            var clone=new GameState{Mode=game.Mode,Motion=game.Motion,Profile=game.Profile,Flight=game.Flight,Track=game.Track,Phase=game.Phase,Elapsed=game.Elapsed,
                Players=new[]{EngineTuneTests.CopyMotion(p),EngineTuneTests.CopyMotion(game.Players[1])}};
            Check(!ReferenceEquals(clone.Players[0].LastFinishBoost,p.LastFinishBoost),"snapshot owns event record");
            AdvanceFor(game,100,100);AdvanceFor(clone,100,1d/144);Near(game.Elapsed,clone.Elapsed,"snapshot preserves exact physical result");
            Near(game.Elapsed,1000d/220-.25,"paused original real finish");
            Check(RaceSimulation.SetCharacter(game,0,3),"select hero before rematch");Check(RaceSimulation.Press(game,0),"first ready");
            Check(p.FinishBoostsEarned==1,"result retained until both ready");Check(RaceSimulation.Press(game,1),"second ready");p=game.Players[0];
            Check(!p.FinishBoost&&p.FinishBoostsEarned==0&&p.FinishBoostElapsed==0&&p.FinishBoostDistanceGained==0&&p.LastFinishBoost==null,"rematch clears pulse/once/event ledger");
            Check(p.CharacterIndex==3,"rematch keeps hero");RaceSimulation.Advance(game,3);AdvanceFor(game,100,100);
            Check(p.FinishBoostsEarned==1,"next race personally collects again");Near(p.FinishTime.Value,1000d/220-.25,"new real finish");
        }
        static void ExactGraceFinish()
        {
            foreach(int count in new[]{7,9})foreach(double frame in Frames){
                var obstacles=new Obstacle[count];for(int i=0;i<count;i++)obstacles[i]=new Obstacle{Id="impact-"+i,Kind="floor",X=300+i*180,Width=40,Height=48};
                var game=Start(Empty(2200,new[]{Target()},obstacles));Check(RaceSimulation.Press(game,0),"winner departs floor");
                AdvanceFor(game,1800d/220,frame);Check(RaceSimulation.Press(game,0),"winner deliberately returns after final stone");AdvanceFor(game,100,frame);
                Near(game.Players[0].FinishTime.Value,9.75,"clean winner actual quarter-second gain");Near(game.Elapsed,13.25,"exact winner grace bound with pulse");
                Check(Equals(game.Winner,0)&&game.Players[1].Collisions==count,"real impact chain retains winner");
                if(count==7){Check(game.Players[1].FinishTime.HasValue,"arrival exactly at deadline accepted");Near(game.Players[1].FinishTime.Value,13.25,"deadline finish timestamp");Check(game.Players[1].FinishBoostsEarned==1,"deadline keeps personal reward");}
                else{Check(!game.Players[1].FinishTime.HasValue,"large advance cannot finish after grace");Near(game.Players[1].Distance,(13.25-4.5)*220,"loser stops at deadline distance");}
            }
        }
    }
}