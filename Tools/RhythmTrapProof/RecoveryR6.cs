using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static void RecoveryR6(string artifactPrefix="rhythm-r6-recovery-targeted")
    {
        var requests=JsonSerializer.Deserialize<RecoveryRequest[]>(File.ReadAllText("Verification/rhythm-r5-recovery-no-found.json"),CourseJson);
        using var historical=JsonDocument.Parse(File.ReadAllText("Verification/rhythm-final-routes.json"));
        var oldObstacles=new Dictionary<string,Obstacle[]>();
        foreach(var row in historical.RootElement.EnumerateArray())if(row.GetProperty("profile").GetString()=="chosen")oldObstacles[row.GetProperty("id").GetString()]=JsonSerializer.Deserialize<Obstacle[]>(row.GetProperty("obstacles").GetRawText(),CourseJson);
        var baselines=new Dictionary<string,CourseResult>();var rows=new List<object>();int cases=0,bareCount=0,beforeCount=0,mirrored=0;
        foreach(var r in requests) {
            var old=oldObstacles[r.track];var oldHit=old.Single(o=>o.Id==r.hit);var oldNext=old.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(oldHit)).OrderBy(ObstacleGeometry.Left).First();
            bool topFirst=oldNext.TopOffsetX<oldNext.BottomOffsetX;double y=topFirst?r.Y:240-r.Y,v=topFirst?r.VY:-r.VY;
            bool bare=r.support=="None"&&Math.Abs(y-93.037545)<.001&&Math.Abs(v+133.32386)<.001;
            bool before=r.support=="None"&&Math.Abs(y-83.45055)<.001&&Math.Abs(v+174.57955)<.001;
            if(!bare&&!before)continue;
            var track=TracksRhythm.Draft(r.track);var route=r.route=="safe"?TracksRhythm.SafeRoute(r.track,FlightPresets.Chosen):r.route=="trap"?TracksRhythm.NegativeRoute(r.track,FlightPresets.Chosen):TracksRhythm.FullRoute(r.track,FlightPresets.Chosen);
            string key=r.track+"/"+r.route;if(!baselines.ContainsKey(key))baselines[key]=RunCourse(track,FlightPresets.Chosen,route,1d/144);
            var hit=RecoveryPrefix(track,route,baselines[key].Times,r.input,r.shift);Check(hit!=null&&hit.Players[0].LastHit.Id==r.hit,"R5 action/module maps to same real R6 hit");
            var actualHit=track.Obstacles.Single(o=>o.Id==r.hit);var next=track.Obstacles.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(actualHit)).OrderBy(ObstacleGeometry.Left).First();
            var reaction=Clone(hit);AdvanceTime(reaction,reaction.Players[0].Recovering+.15,.001);double target=ObstacleGeometry.Right(next)+15;
            cases++;if(bare)bareCount++;else beforeCount++;if(!topFirst)mirrored++;
            var witnesses=new List<object>();double[] selected=null;GameState end;List<InputSample> samples;
            if(before)foreach(var candidate in new[]{new[]{.275,.325,.775,.825},new[]{.275,.325,.775,.9}}){var late=(double[])candidate.Clone();late[0]+=.02;
                if(RecoveryTry(reaction,candidate,target,out end,out samples)&&RecoveryTry(reaction,late,target,out end,out samples)){selected=candidate;break;}}
            Check(bare||selected!=null,"R6 before-air reusable catalogue");
            foreach(double lateBy in new[]{0d,.005,.01,.015,.02}){
                double[] offsets;
                if(bare){var adaptive=new List<double>();Check(AdaptiveWallRecovery(reaction,lateBy,0,.05,target,adaptive),"R6 bare-air actual wall adaptive rule");offsets=adaptive.ToArray();}
                else {offsets=(double[])selected.Clone();offsets[0]+=lateBy;}
                foreach(double step in Steps)Check(RecoveryTry(reaction,offsets,target,out end,out samples,step),"R6 first20ms actual six-dt whole next union");
                RecoveryTry(reaction,offsets,target,out end,out samples);
                witnesses.Add(new{extraFirstDelay=lateBy,offsetsSeconds=offsets,inputs=samples,clearedX=end.Players[0].Distance,hits=end.Players[0].Collisions});
            }
            var p=reaction.Players[0];rows.Add(new{r.track,profile="chosen",route=r.route,changedInput=r.input,shiftSeconds=r.shift,
                baselineInputX=route[r.input],baselineInputTime=baselines[key].Times[r.input],hitId=r.hit,nextId=next.Id,
                targetX=target,reactionX=p.Distance,reactionY=RaceSimulation.GetPlayerCenterY(p),reactionVY=p.VerticalSpeed,
                support=p.Support.ToString(),speed=p.CurrentSpeed,boost=p.BoostRemaining,slow=p.SlowRemaining,
                rule=bare?"first toggle, actual opposite wall, jump, repeat after .05":"four-input catalogue",witnesses});
            if(cases%20==0)Console.WriteLine($"{artifactPrefix}: {cases} actual prefixes passed");
        }
        Check(bareCount==72&&beforeCount==6,"all 78 historical affected actions mapped to current runtime");
        File.WriteAllText("Verification/"+artifactPrefix+".json",JsonSerializer.Serialize(rows,CourseJson));
        Console.WriteLine($"PASS {artifactPrefix}: {checks} checks / {cases} actual prefixes ({bareCount}bare-air/{beforeCount}before-air; {mirrored} mirrored); five first-delay samples x six dt; no state injection, no geometry override.");
    }
}
