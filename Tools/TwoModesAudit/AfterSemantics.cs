using System;
using FlipRace.Core;

public static partial class TwoModesIndependentAudit
{
    static Track LinkedFixture(int stoneSide,double clearX,double ringX=1070,double floorY=170,string placement="after")
    {
        return new Track(2200,new[]{new Obstacle{Id="linked-stone",Kind=stoneSide==1?"floor":"ceiling",X=1000,Width=64,Height=48}})
        { RiskZones=new[]{new RiskZone{Id="linked-ring",ObstacleId="linked-stone",Placement=placement,
            X=ringX,Y=stoneSide==1?floorY:240-floorY,Radius=8,ClearX=clearX}} };
    }
    static double ApproachTime(FlightSettings flight)
    { return flight.FirstAction==FirstAction.Jump ? (350+Math.Sqrt(350*350-2*662.5*48))/662.5 : FlipTime(164); }
    static GameState Approach(FlightSettings flight,int stoneSide,double landingPlaneX,double clearX,double step)
    {
        var game=Prepare(LinkedFixture(stoneSide,clearX),flight,
            flight.FirstAction==FirstAction.Jump?stoneSide:1-stoneSide,step);
        MoveTo(game,landingPlaneX-220*ApproachTime(flight),step);Tap(game);return game;
    }
    static void AfterSemantics()
    {
        foreach(var flight in new[]{ExpectedMain,ExpectedFast})foreach(int side in new[]{0,1})foreach(double step in Steps)
        {
            context="after geometric confirmation "+flight.FirstAction+" side="+side+" dt="+step;
            var game=Approach(flight,side,1080,0,step);
            double boundary=1000+64+14+1e-7;
            MoveTo(game,boundary-1e-4,step);
            foreach(var p in game.Players)
                Check(p.Collisions==0&&p.CollectedRiskIds.Contains("linked-ring")&&p.BoostsEarned==0,
                    "actual airborne collection stays pending before expanded rear boundary despite early ClearX");
            MoveTo(game,boundary+1e-4,step);
            foreach(var p in game.Players)
            {
                Check(p.Collisions==0&&p.BoostsEarned==1&&p.SuccessfulRiskIds.Contains("linked-ring"),
                    "geometrically safe after confirms without waiting for landing");
                Check(p.Flipping&&p.Support==SupportKind.None,"confirmation is genuinely still airborne");
            }
            Advance(game,100,step);
            foreach(var p in game.Players)
            { Near(p.FinishTime.Value,9.7,"actual full reward finish");Check(p.CompletedBoosts==1&&p.ActiveEffect==null&&p.PendingEffects.Count==0,"one fully served reward"); }

            context="explicit later ClearX "+flight.FirstAction+" side="+side+" dt="+step;
            game=Approach(flight,side,1080,1150,step);MoveTo(game,1149.9999,step);
            foreach(var p in game.Players)Check(p.BoostsEarned==0&&p.CollectedRiskIds.Contains("linked-ring"),"larger authored ClearX respected");
            MoveTo(game,1150.0001,step);
            foreach(var p in game.Players)Check(p.BoostsEarned==1&&p.Collisions==0,"later boundary confirms once");

            context="pending then real rear contact "+flight.FirstAction+" side="+side+" dt="+step;
            game=Approach(flight,side,1064,0,step);MoveTo(game,1060,step);
            foreach(var p in game.Players)Check(p.Collisions==0&&p.CollectedRiskIds.Contains("linked-ring")&&p.BoostsEarned==0,"ring collected before later physical collision");
            MoveTo(game,1080,step);
            foreach(var p in game.Players)Check(p.Collisions==1&&p.FailedRiskIds.Contains("linked-ring")&&p.BoostsEarned==0,"actual hit before boundary cancels pending");

            context="untouched ring after historical hit "+flight.FirstAction+" side="+side+" dt="+step;
            game=Prepare(LinkedFixture(side,0),flight,side,step);MoveTo(game,986,step);Advance(game,1e-8,step);
            foreach(var p in game.Players)Check(p.Collisions==1&&!p.CollectedRiskIds.Contains("linked-ring"),"collision precedes untouched ring");
            Advance(game,game.Players[0].Recovering+.15,step);MoveTo(game,1040,step);Tap(game);MoveTo(game,1078.001,step);
            foreach(var p in game.Players)Check(p.BoostsEarned==1&&!p.FailedRiskIds.Contains("linked-ring")&&p.Collisions==1,"historical obstacle hit does not veto new collection");
            Advance(game,100,step);
            foreach(var p in game.Players)Near(p.FinishTime.Value,10.2,"actual penalty plus later full reward");

            context="late contact beyond safety "+flight.FirstAction+" side="+side+" dt="+step;
            // Run on the opposite wall to the linked stone, then pick up a new
            // circle entirely after its clearance slab. No collision is hidden.
            var t=LinkedFixture(1-side,0,1300,60);
            t.RiskZones[0].Y=side==1?180:60;
            game=Prepare(t,flight,side,step);
            double contactTime=flight.FirstAction==FirstAction.Jump ? (350-Math.Sqrt(350*350-2*662.5*46))/662.5 : FlipTime(46);
            MoveTo(game,1300-220*contactTime,step);Tap(game);
            while(!game.Players[0].CollectedRiskIds.Contains("linked-ring")&&game.Elapsed<20)Advance(game,.001,step);
            foreach(var p in game.Players)Check(p.Distance>1078&&p.Flipping&&p.BoostsEarned==1&&p.Collisions==0,"late physical collection confirms immediately while airborne");
        }
        ContactImpactOrdering();
        Console.WriteLine("PASS after semantics independent: "+checks+" assertions / "+games+" games; both expected profiles, sides, six dt.");
    }
    static void ContactImpactOrdering()
    {
        foreach(var flight in new[]{ExpectedMain,ExpectedFast})foreach(int side in new[]{0,1})
        foreach(string placement in new[]{"before","after"})foreach(double offset in new[]{-1e-4,0d,1e-4})
        foreach(double step in new[]{1d/144,.317,100})
        {
            context="contact/impact ordering "+flight.FirstAction+" side="+side+" placement="+placement+" offset="+offset+" dt="+step;
            var t=LinkedFixture(side,0,1008+offset,226,placement);
            // Circle radius8+hero14: center1008 enters at986, the expanded front face.
            var game=Prepare(t,flight,side,step);Advance(game,100,step);
            foreach(var p in game.Players)
            {
                Check(p.Collisions==1&&p.CollectedRiskIds.Contains("linked-ring"),"one actual hit and contact");
                Check(p.FailedRiskIds.Contains("linked-ring")== (offset<0),"only previously collected ring cancelled");
                Check(p.BoostsEarned==(offset<0?0:1)&&p.CompletedBoosts==p.BoostsEarned,"untouched tied/later ring awarded once");
                Near(p.FinishTime.Value,offset<0?10.5:10.2,"ordering changes actual finish by one reward");
                Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,"no zero-time loop or effect residue");
            }
        }
    }
}
