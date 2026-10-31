using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlipRace.Core;

static class Program
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks, snapshots, continuationSteps;
    static string context = "setup";
    static readonly List<object> cases = new List<object>();
    static void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(context + ": " + reason); }
    static bool Scalar(Type t) { return t.IsPrimitive || t.IsEnum || t == typeof(decimal) || t == typeof(string) || Nullable.GetUnderlyingType(t) != null && Scalar(Nullable.GetUnderlyingType(t)); }
    static FieldInfo[] Members(Type t) { return t.GetFields(Fields).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray(); }
    static string Hash(string text) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))); }
    // Test-only reflection traverses actual fields, including private camera history.
    static string State(object value)
    {
        var text = new StringBuilder(); var seen = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        void Visit(object item)
        {
            if (item == null) { text.Append("null;"); return; }
            Type t = item.GetType(); text.Append(t.FullName).Append(':');
            if (Scalar(t)) { text.Append(item is double d ? d.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(item, CultureInfo.InvariantCulture)).Append(';'); return; }
            if (seen.TryGetValue(item, out int id)) { text.Append('@').Append(id).Append(';'); return; }
            seen.Add(item, seen.Count);
            if (item is HashSet<string> set) { text.Append(set.Comparer.GetType().FullName); foreach (string s in set.OrderBy(s => s, StringComparer.Ordinal)) Visit(s); }
            else if (item is IEnumerable sequence) foreach (object child in sequence) Visit(child);
            else foreach (var field in Members(t)) { text.Append(field.Name).Append('='); Visit(field.GetValue(item)); }
            text.Append('}');
        }
        Visit(value); return Hash(text.ToString());
    }
    static void Schema()
    {
        context = "reflection schema guard";
        var gameRefs = new Dictionary<string, Type> { {"BotPlan",typeof(BotPlan)}, {"StochasticBot",typeof(StochasticBotSettings)}, {"Profile",typeof(MotionProfile)}, {"Flight",typeof(FlightSettings)}, {"Track",typeof(Track)}, {"Players",typeof(PlayerState[])}, {"Winner",typeof(object)} };
        var playerRefs = new Dictionary<string, Type> { {"LastFlight",typeof(FlightMeasurement)}, {"PendingEffects",typeof(Queue<SpeedEffect>)}, {"ActiveEffect",typeof(SpeedEffect)} };
        foreach (string name in new[] {"HitObstacleIds","CollectedRiskIds","SuccessfulRiskIds","FailedRiskIds","NegativeRiskIds"}) playerRefs.Add(name, typeof(HashSet<string>));
        foreach (string name in new[] {"LastHit","LastCoin","LastSuccess","LastInstant","LastSlow","LastFinishBoost"}) playerRefs.Add(name, typeof(HitInfo));
        void Audit(Type t, Dictionary<string, Type> refs)
        {
            foreach (var f in Members(t)) if (!Scalar(f.FieldType)) Check(refs.TryGetValue(f.Name, out Type expected) && expected == f.FieldType, "unclassified mutable/reference field " + t.Name + "." + f.Name);
            foreach (var pair in refs) Check(t.GetField(pair.Key, Fields)?.FieldType == pair.Value, "expected reference policy " + t.Name + "." + pair.Key);
        }
        Audit(typeof(GameState), gameRefs); Audit(typeof(PlayerState), playerRefs);
        void Exact(Type t, string names) { Check(string.Join(",", Members(t).Select(f => f.Name)) == string.Join(",", names.Split(',').OrderBy(s => s, StringComparer.Ordinal)), "manual child clone schema " + t.Name); }
        Exact(typeof(HitInfo), "Id,Message,At"); Exact(typeof(SpeedEffect), "RingId,IsNegative,Remaining,Elapsed,PulseCount,CompletedPulses");
        foreach (Type t in new[] {typeof(FlightSettings), typeof(MotionProfile), typeof(FlightMeasurement), typeof(StochasticBotSettings)})
            foreach (var f in Members(t)) Check(f.IsInitOnly && Scalar(f.FieldType), "shared immutable field " + t.Name + "." + f.Name);
        foreach (var f in Members(typeof(BotPlan))) Check(f.IsInitOnly && (Scalar(f.FieldType) || f.Name == "distances" && f.FieldType == typeof(double[])), "immutable BotPlan backing storage");
        Check(typeof(BotPlan).GetProperty("Distances").CanWrite == false, "no BotPlan public setter");
        bool invalid = false; try { RaceSnapshot.Clone(null); } catch (ArgumentNullException) { invalid = true; } Check(invalid, "null source rejected");
        invalid = false; try { RaceSnapshot.Clone(new GameState { Winner = new StringBuilder() }); } catch (ArgumentException) { invalid = true; } Check(invalid, "mutable unsupported Winner rejected");
    }
    static HashSet<object> Timeline(GameState game)
    {
        var nodes = new HashSet<object>(ReferenceEqualityComparer.Instance) {game};
        void Add(object v)
        {
            if (v == null || Scalar(v.GetType()) || !nodes.Add(v)) return;
            if (v is IEnumerable seq) { foreach (object c in seq) Add(c); return; }
            foreach (var f in Members(v.GetType())) if (!Scalar(f.FieldType) && f.FieldType != typeof(FlightMeasurement)) Add(f.GetValue(v));
        }
        Add(game.Players); return nodes;
    }
    static void MutateTimeline(GameState game)
    {
        foreach (object node in Timeline(game))
        {
            if (node is HashSet<string> set) { set.Add("branch-only"); continue; }
            if (node is Queue<SpeedEffect> queue) { queue.Enqueue(new SpeedEffect { RingId = "branch-only", Remaining = .123 }); continue; }
            if (node is PlayerState || node is GameState || node is HitInfo || node is SpeedEffect)
                foreach (var f in Members(node.GetType())) if (!f.IsInitOnly)
                {
                    if (f.FieldType == typeof(double)) f.SetValue(node, (double)f.GetValue(node) + .123);
                    else if (f.FieldType == typeof(int)) f.SetValue(node, (int)f.GetValue(node) + 1);
                    else if (f.FieldType == typeof(bool)) f.SetValue(node, !(bool)f.GetValue(node));
                    else if (f.FieldType == typeof(string)) f.SetValue(node, "branch-only");
                    else if (f.FieldType == typeof(double?)) f.SetValue(node, (double?)123.456);
                }
        }
    }
    static void Capture(GameState source, string label, bool record = true)
    {
        context = label; snapshots++;
        string before = State(source), frozen = State(new object[] {source.Track, source.Flight, source.Profile});
        var a = RaceSnapshot.Clone(source); var b = RaceSnapshot.Clone(source);
        Check(State(a) == before && State(b) == before, "all fields equal immediately");
        Check(ReferenceEquals(source.Track, a.Track) && ReferenceEquals(source.Flight, a.Flight) && ReferenceEquals(source.Profile, a.Profile) && ReferenceEquals(source.BotPlan, a.BotPlan), "shared read-only inputs");
        var originalNodes = Timeline(source); var branchNodes = Timeline(a); var otherNodes = Timeline(b);
        Check(!originalNodes.Overlaps(branchNodes) && !originalNodes.Overlaps(otherNodes) && !branchNodes.Overlaps(otherNodes), "all mutable graph nodes disjoint");
        MutateTimeline(a); Check(State(source) == before && State(b) == before, "arbitrary nested branch writes isolated");
        a = RaceSnapshot.Clone(source);
        for (int step = 0; step < 18; step++)
        {
            if (step % 5 == 0) for (int slot = 0; slot < source.Players.Length; slot++) Check(RaceSimulation.Press(a, slot) == RaceSimulation.Press(b, slot), "same legal input acceptance");
            double dt = new[] {.003, 1d/60, .031, .08}[step % 4];
            RaceSimulation.Advance(a, dt); RaceSimulation.Advance(b, dt); continuationSteps++;
            Check(State(a) == State(b), "deterministic continuation entire state");
        }
        Check(State(source) == before, "forecast never mutates live timeline");
        Check(State(new object[] {source.Track, source.Flight, source.Profile}) == frozen, "Press/Advance never mutates shared inputs");
        string other = State(b); RaceSimulation.Press(a, 0); RaceSimulation.Advance(a, .41); Check(State(b) == other, "diverging continuation never mutates sibling");
        if (record) cases.Add(new { label, source.Phase, source.Elapsed, camera=source.PartyCamera.ToString(), players=source.Players.Select(p=>new {p.Id,status=p.Status.ToString(),p.Distance,p.Recovering,p.BoostsEarned,p.InstantBoostsEarned,p.SlowsEarned,queue=p.PendingEffects.Count,collected=p.CollectedRiskIds.ToArray(),successful=p.SuccessfulRiskIds.ToArray(),failed=p.FailedRiskIds.ToArray(),hits=p.HitObstacleIds.ToArray(),p.FinishBoost}).ToArray() });
    }
    static void Start(GameState g) { for (int i=0;i<g.Players.Length;i++) RaceSimulation.Press(g,i); RaceSimulation.Advance(g,3); Check(g.Phase == "racing", "real readiness/countdown"); }
    static Track Open(double length = 2200) { return new Track(length, Array.Empty<Obstacle>()); }
    static void Tap(GameState g) { for (int i=0;i<g.Players.Length;i++) Check(RaceSimulation.Press(g,i), "legal tap"); }
    static void SyntheticGraph()
    {
        context = "synthetic structural coverage (not a reachability claim)";
        var g=RaceSimulation.CreateParty(Open(),FlightPresets.Chosen,4,PartyCamera.Lagger,-288.8,1520);
        foreach(var p in g.Players)
        {
            foreach(var f in Members(typeof(PlayerState)))
            {
                if(f.FieldType==typeof(double)) f.SetValue(p, 1.234 + p.Id);
                else if(f.FieldType==typeof(int)) f.SetValue(p, 7+p.Id);
                else if(f.FieldType==typeof(bool)) f.SetValue(p,true);
                else if(f.FieldType==typeof(string)) f.SetValue(p,"scalar-"+f.Name);
                else if(f.FieldType==typeof(double?)) f.SetValue(p,(double?)4.567);
            }
            p.LastFlight=new FlightMeasurement(.4,50,true,1,0,"rock");
            p.ActiveEffect=new SpeedEffect {RingId="active",Remaining=.3,Elapsed=.3,IsNegative=true,PulseCount=2,CompletedPulses=1};
            p.PendingEffects.Enqueue(new SpeedEffect {RingId="queued",Remaining=.5,Elapsed=.1});
            foreach(var f in Members(typeof(PlayerState)))
            {
                if(f.FieldType==typeof(HashSet<string>)) f.SetValue(p,new HashSet<string>(new[]{"A","b"},StringComparer.OrdinalIgnoreCase));
                if(f.FieldType==typeof(HitInfo)) f.SetValue(p,new HitInfo{Id=f.Name,At=.123,Message="test"});
            }
            p.LastInstant=p.LastSuccess;
        }
        g.Winner="tie";g.WinnerAt=1.25;g.PartyLastCameraLeft=123.4;
        Capture(g,"synthetic/all-timeline-fields");
        var clone=RaceSnapshot.Clone(g);
        Check(ReferenceEquals(clone.Players[0].LastInstant,clone.Players[0].LastSuccess),"actual instant/success internal alias retained");
        Check(clone.Players[0].HitObstacleIds.Contains("a"),"set comparer retained");
        // Adversarial identity graph also covers duplicate array entries and cross-player aliases.
        g.Players[2]=g.Players[1];g.Players[0].PendingEffects.Enqueue(g.Players[0].ActiveEffect);
        g.Players[1].PendingEffects=g.Players[0].PendingEffects;g.Players[1].LastHit=g.Players[0].LastHit;
        g.Players[1].HitObstacleIds=g.Players[0].HitObstacleIds;
        clone=RaceSnapshot.Clone(g);
        Check(ReferenceEquals(clone.Players[1],clone.Players[2]),"repeated player identity retained");
        Check(ReferenceEquals(clone.Players[0].PendingEffects,clone.Players[1].PendingEffects),"shared FIFO identity retained");
        Check(ReferenceEquals(clone.Players[0].ActiveEffect,clone.Players[0].PendingEffects.Last()),"queued/active repeated effect identity retained");
        Check(ReferenceEquals(clone.Players[0].LastHit,clone.Players[1].LastHit)&&ReferenceEquals(clone.Players[0].HitObstacleIds,clone.Players[1].HitObstacleIds),"cross-player graph aliases retained within copy");
        string before=State(clone);MutateTimeline(g);Check(State(clone)==before,"source nested writes cannot mutate prior clone");
        var nullable=new GameState {Players=new PlayerState[]{null,new PlayerState(1){PendingEffects=null,HitObstacleIds=null}},Flight=null};
        Check(State(RaceSnapshot.Clone(nullable))==State(nullable),"optional null references preserved structurally");
        var plan=new BotPlan("snapshot-fixture","custom","Chosen",BotDifficulty.Master,17,0,"fixture",new[]{10d,20d});
        var bot=new GameState(Open(),PartyCamera.Constant,-288.8,1520,false,plan){Track=Open(),Flight=FlightPresets.Chosen};
        clone=RaceSnapshot.Clone(bot);Check(clone.IsBot&&!clone.IsSolo&&!clone.IsParty&&ReferenceEquals(clone.BotPlan,plan),"immutable bot metadata retained without activating legacy tapes");
        double first=plan.GetDistance(0);plan.Distances[0]=999;Check(plan.GetDistance(0)==first,"BotPlan getter is defensive");
    }
    static void StochasticMetadata()
    {
        context="immutable stochastic metadata";
        var track=RaceTracks.Get(TracksRhythm.CascadeId);
        foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
        {
            var settings=StochasticBotSettings.CreateFixed(track,FlightPresets.Chosen,BotDifficulty.Confident,0xF1234567u,.75,.031);
            var game=RaceSimulation.CreateStochasticBotRace(track,FlightPresets.Chosen,settings,camera,-288.8,1520);
            Check(game.IsBot&&!game.IsParty&&!game.IsSolo&&game.BotPlan==null,"stochastic factory classification without legacy tape");
            Check(ReferenceEquals(game.StochasticBot,settings)&&game.BotDifficulty==BotDifficulty.Confident&&unchecked((uint)game.BotSeed)==settings.Seed,"fixed settings and full seed bits retained");
            Check(settings.CollectProbability==.75&&settings.HitProbability==.031,"probabilities fixed before start");
            string immutableBefore=State(settings);
            var clone=RaceSnapshot.Clone(game);
            Check(ReferenceEquals(clone.StochasticBot,settings),"readonly scalar stochastic metadata shared by snapshot");
            Capture(game,"stochastic/ready/"+camera);
            Check(RaceSimulation.Press(game,0),"human readiness starts stochastic race");
            RaceSimulation.Advance(game,.2);Capture(game,"stochastic/countdown/"+camera);
            RaceSimulation.Advance(game,2.8);RaceSimulation.Advance(game,.4);
            Capture(game,"stochastic/racing/"+camera);
            Check(State(settings)==immutableBefore,"snapshot branches and ordinary continuations preserve frozen settings");
        }
    }
    static void ActualSpecialCases()
    {
        context="actual lifecycle";
        foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
        {
            var g=RaceSimulation.CreateParty(Open(),FlightPresets.Chosen,4,camera,-288.8,1520);
            Capture(g,"actual/ready/"+camera);for(int i=0;i<4;i++)RaceSimulation.Press(g,i);RaceSimulation.Advance(g,1.25);Capture(g,"actual/countdown/"+camera);
            RaceSimulation.Advance(g,1.75);Tap(g);RaceSimulation.Advance(g,.17);Capture(g,"actual/airborne/"+camera);
            g.Phase="paused";Capture(g,"pause-shell/"+camera);g.Phase="racing";RaceSimulation.Advance(g,100);Capture(g,"actual/terminal-tie/"+camera);
            Check(g.Phase=="finished"&&Equals(g.Winner,"tie"),"actual terminal tie");
        }
        var track=Open();track.RiskZones=new[]{new RiskZone{Id="slow",Kind="negative",X=50,Y=226},new RiskZone{Id="ordinary",X=75,Y=226,FreeReward=true},new RiskZone{Id="instant",Kind="instant",X=100,Y=226,FreeReward=true},new RiskZone{Id="instant2",Kind="instant",X=145,Y=226,FreeReward=true},new RiskZone{Id="coin",Kind="coin",X=175,Y=226}};
        var fx=RaceSimulation.CreateParty(track,FlightPresets.Chosen,2,PartyCamera.Lagger,-288.8,1520);Start(fx);bool pending=false,preempt=false,pulse=false;
        for(int i=0;i<240;i++)
        {
            RaceSimulation.Advance(fx,1d/120);var p=fx.Players[0];
            if(!pending&&p.ActiveEffect?.IsNegative==true&&p.PendingEffects.Count>0){pending=true;Capture(fx,"actual/ordinary-queued-behind-slow");}
            if(!preempt&&p.ActiveEffect?.IsNegative==false&&p.PendingEffects.FirstOrDefault()?.IsNegative==true&&p.PendingEffects.Peek().Elapsed>0){preempt=true;Capture(fx,"actual/instant-preempts-partially-served-slow");Check(ReferenceEquals(RaceSnapshot.Clone(fx).Players[0].LastSuccess,RaceSnapshot.Clone(fx).Players[0].LastInstant)==false,"separate snapshots do not share event nodes");}
            if(!pulse&&p.ActiveEffect?.PulseCount>1){pulse=true;Capture(fx,"actual/merged-instant-pulses");}
        }
        Check(pending&&preempt&&pulse,"real FIFO/preempted slow/merged instant reached");Capture(fx,"actual/effects-and-personal-coin");
        // Exact pending-before-hit ordering from the existing V4 contract, no state injection.
        track=new Track(2200,new[]{new Obstacle{Id="stone",Kind="floor",X=300,Width=64,Height=48}}){RiskZones=new[]{new RiskZone{Id="linked",ObstacleId="stone",X=305.978,Y=226,ClearX=379}}};
        var linked=RaceSimulation.CreateTrial(track,MotionMode.Gravity);Start(linked);RaceSimulation.Advance(linked,285.99/220);
        Check(linked.Players[0].CollectedRiskIds.Contains("linked")&&!linked.Players[0].SuccessfulRiskIds.Contains("linked"),"real linked pending reached");Capture(linked,"actual/linked-pending-before-impact");
        RaceSimulation.Advance(linked,.001);Check(linked.Players[0].FailedRiskIds.Contains("linked")&&linked.Players[0].Recovering>0,"real impact fails pending");Capture(linked,"actual/failed-pending-and-recovery");
        track=new Track(2200,new[]{new Obstacle{Id="stone",Kind="floor",X=100,Width=64,Height=48}}){RiskZones=new[]{new RiskZone{Id="after",ObstacleId="stone",Placement="after",X=300,Y=173,ClearX=321}}};
        var recover=RaceSimulation.CreateTrial(track,MotionMode.Gravity);Start(recover);RaceSimulation.Advance(recover,86d/220);Capture(recover,"actual/hit-with-untouched-reward");
        RaceSimulation.Advance(recover,.5);RaceSimulation.Advance(recover,126d/220);Tap(recover);RaceSimulation.Advance(recover,.4);Capture(recover,"actual/post-recovery-pending");
        RaceSimulation.Advance(recover,.1);Check(recover.Players[0].SuccessfulRiskIds.Contains("after")&&!recover.Players[0].FailedRiskIds.Contains("after"),"untouched reward succeeds after hit");Capture(recover,"actual/post-recovery-award");
        track=Open();track.RiskZones=new[]{new RiskZone{Id="finish",Kind="finish",X=2000,Y=217}};
        var finish=RaceSimulation.CreateSolo(track,FlightPresets.Chosen,PartyCamera.Lagger,-288.8,1520);Start(finish);RaceSimulation.Advance(finish,9.04);Check(finish.Players[0].FinishBoost,"actual finish dash active");Capture(finish,"actual/active-finish-dash");RaceSimulation.Advance(finish,10);Capture(finish,"actual/solo-finished");
        var eliminated=RaceSimulation.CreateSolo(new Track(15840,Array.Empty<Obstacle>()),FlightPresets.Chosen,PartyCamera.Constant,-30,1520);Start(eliminated);RaceSimulation.Advance(eliminated,2);Check(eliminated.Players[0].Status==PlayerStatus.Eliminated,"real left-only elimination");Capture(eliminated,"actual/constant-eliminated");
    }
    static void BranchOrderAndTerminal()
    {
        context="adversarial branch order from partially served slow";
        var track=Open();track.RiskZones=new[]{new RiskZone{Id="slow",Kind="negative",X=50,Y=226},new RiskZone{Id="instant-air",Kind="instant",X=85,Y=170,FreeReward=true}};
        var source=RaceSimulation.CreateParty(track,FlightPresets.Chosen,2,PartyCamera.Lagger,-288.8,1520);Start(source);RaceSimulation.Advance(source,.35);
        Check(source.Players[0].ActiveEffect?.IsNegative==true&&source.Players[0].ActiveEffect.Elapsed>0,"actual partially served slow source");string sourceBefore=State(source);
        GameState Forecast(bool jump)
        {
            var g=RaceSnapshot.Clone(source);if(jump)Check(RaceSimulation.Press(g,0),"A legal jump toward instant");for(int i=0;i<30;i++)RaceSimulation.Advance(g,1d/120);return g;
        }
        var aFirst=Forecast(true);var bSecond=Forecast(false);var bFirst=Forecast(false);var aSecond=Forecast(true);
        Check(State(aFirst)==State(aSecond)&&State(bFirst)==State(bSecond),"A then B equals B then A for each distinct branch");
        Check(State(source)==sourceBefore,"both branch orders preserve source");
        var a=aFirst.Players[0];var b=bFirst.Players[0];
        Check(a.InstantBoostsEarned==1&&a.ActiveEffect?.IsNegative==false&&a.PendingEffects.Peek().IsNegative&&a.PendingEffects.Peek().Elapsed>0,"A instant preempts and retains same served slow");
        Check(b.InstantBoostsEarned==0&&b.ActiveEffect?.IsNegative==true,"B wait remains slow and misses air instant");
        Check(ReferenceEquals(a.LastInstant,a.LastSuccess)&&!ReferenceEquals(a.LastSuccess,source.Players[0].LastSuccess),"A event identity local to snapshot");
        Capture(aFirst,"adversarial/A-instant-after-partial-slow");Capture(bFirst,"adversarial/B-wait-after-partial-slow");
        cases.Add(new{label="adversarial/A-B-vs-B-A",sourceHash=sourceBefore,aHash=State(aFirst),bHash=State(bFirst),differentPhysicalOutcomes=true});
        context="bot terminal inside slice while human continues";
        foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
        {
            track=Open();track.RiskZones=new[]{new RiskZone{Id="finish",Kind="finish",X=2000,Y=217}};
            // Metadata-only fixture: no catalog initialization or historical action tape/controller.
            var plan=new BotPlan("snapshot-terminal-fixture","custom","Chosen",BotDifficulty.Master,31,0,"fixture",Array.Empty<double>());
            var g=new GameState(track,camera,-288.8,1520,false,plan){Mode="bot",Track=track,Flight=FlightPresets.Chosen};Start(g);
            RaceSimulation.Advance(g,1850d/220);Check(RaceSimulation.Press(g,0),"human avoids finish target");RaceSimulation.Advance(g,1.4);
            Check(g.IsBot&&g.Players[1].Status==PlayerStatus.Finished&&g.Players[0].Status==PlayerStatus.Running&&g.Phase=="racing","bot reaches terminal mid-slice, human still running");
            Check(!RaceSimulation.Press(g,1),"terminal bot input rejected");Capture(g,"actual/bot-terminal-human-running/"+camera);
            var clone=RaceSnapshot.Clone(g);string live=State(g),bot=State(clone.Players[1]);clone.Phase="paused";double elapsed=clone.Elapsed;double cameraLeft=clone.PartyCameraLeft;
            RaceSimulation.Advance(clone,10);Check(clone.Elapsed==elapsed&&clone.PartyCameraLeft==cameraLeft&&!RaceSimulation.Press(clone,0),"pause boundary freezes active human and camera");
            clone.Phase="racing";RaceSimulation.Advance(clone,.1);Check(clone.Players[0].Distance>g.Players[0].Distance&&State(clone.Players[1])==bot,"only running human continues after resume");
            RaceSimulation.Advance(clone,10);Check(clone.Phase=="finished"&&Equals(clone.Winner,1)&&State(clone.Players[1])==bot,"first terminal bot frozen while human physically finishes");
            Check(State(g)==live,"pause/resume and terminal forecasts leave live race unchanged");Capture(clone,"actual/bot-and-human-both-finished/"+camera);
        }
    }
    static void AuthoredRoutes()
    {
        foreach(var track in RaceTracks.All)foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger})
        {
            var g=RaceSimulation.CreateParty(track,flight,2,camera,-288.8,1520);Start(g);int next=0, frame=0;string input=State(track);double nextCapture=0;
            while(g.Phase=="racing"&&frame++<8000)
            {
                var p=g.Players[0];if(next<track.SafeRoute.Length&&p.Distance>=track.SafeRoute[next]-1e-8){RaceSimulation.Press(g,0);next++;}
                if(frame%97==0)RaceSimulation.Press(g,1);
                double dt=new[]{1d/30,1d/60,.007,.041}[frame%4];
                if(next<track.SafeRoute.Length&&p.Status==PlayerStatus.Running&&p.Recovering==0&&track.SafeRoute[next]>p.Distance)dt=Math.Min(dt,(track.SafeRoute[next]-p.Distance)/p.CurrentSpeed);
                RaceSimulation.Advance(g,dt);
                if(g.Elapsed>=nextCapture){Capture(g,"authored/"+track.Id+"/"+(ReferenceEquals(flight,FlightPresets.Chosen)?"Chosen":"FastFlip")+"/"+camera,false);nextCapture+=4;}
            }
            Check(g.Phase=="finished","authored bounded run terminates");Capture(g,"authored-terminal/"+track.Id+"/"+(ReferenceEquals(flight,FlightPresets.Chosen)?"Chosen":"FastFlip")+"/"+camera);
            Check(State(track)==input,"entire authored geometry/routes unchanged");
        }
    }
    static object Performance()
    {
        var g=RaceSimulation.CreateParty(Open(),FlightPresets.Chosen,4,PartyCamera.Lagger,-288.8,1520);Start(g);
        foreach(var p in g.Players){p.ActiveEffect=new SpeedEffect{RingId="slow",IsNegative=true,Elapsed=.2,Remaining=.4};for(int i=0;i<3;i++)p.PendingEffects.Enqueue(new SpeedEffect{RingId="q"+i,Remaining=.6});p.LastInstant=p.LastSuccess=new HitInfo{Id="last",At=.1};foreach(var f in Members(typeof(PlayerState)))if(f.FieldType==typeof(HashSet<string>))for(int i=0;i<12;i++)((HashSet<string>)f.GetValue(p)).Add("item"+i);}
        for(int i=0;i<200;i++)RaceSnapshot.Clone(g);GC.Collect();long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();int count=5000;GameState last=null;
        for(int i=0;i<count;i++)last=RaceSnapshot.Clone(g);watch.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;GC.KeepAlive(last);
        return new{clones=count,players=4,queuedEffectsPerPlayer=3,setEntriesPerPlayer=60,elapsedMilliseconds=watch.Elapsed.TotalMilliseconds,microsecondsPerClone=watch.Elapsed.TotalMilliseconds*1000/count,bytesPerClone=(double)allocated/count,environment=".NET 8 managed desktop sample; not Unity/WebGL latency proof"};
    }
    static Dictionary<string,string> SourceHashes()
    {return Directory.GetFiles("Assets/Scripts/Core","*.cs").ToDictionary(p=>p.Replace('\\','/'),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),StringComparer.Ordinal);}
    static int Main(string[] args)
    {
        bool metadata=args.Contains("--stochastic-metadata");var stem=metadata?"stochastic-bots-snapshot-metadata":"stochastic-bots-snapshot";var started=DateTime.UtcNow;var hashes=SourceHashes();var stopwatch=Stopwatch.StartNew();object perf=null;string error=null;
        try{Schema();SyntheticGraph();if(metadata)StochasticMetadata();ActualSpecialCases();BranchOrderAndTerminal();AuthoredRoutes();perf=Performance();}catch(Exception e){error=e.ToString();}
        var after=SourceHashes();var changed=hashes.Keys.Union(after.Keys).Where(p=>!hashes.TryGetValue(p,out string before)||!after.TryGetValue(p,out string value)||value!=before).ToArray();if(changed.Length!=0)error=(error??"")+" Source changed: "+string.Join(",",changed);
        stopwatch.Stop();var report=new{status=error==null?"PASS":"FAIL",requirementIds=new[]{"BSTO00","BSTO01","BSTO04","CONTROL","REWARD","FINISH","GATE","VIEW","TOUCH","SOLO"},baseline="0b1225b",startedUtc=started,elapsedSeconds=stopwatch.Elapsed.TotalSeconds,checks,snapshots,continuationSteps,sourceStable=changed.Length==0,sourceHashes=hashes,changedSources=changed,sharedInputs="Track geometry/routes/cues are caller-frozen race input; FlightSettings/MotionProfile/FlightMeasurement/BotPlan/StochasticBotSettings immutable. Production clone contains no reflection or serialization.",limitations=new[]{"Structural fixture intentionally fills fields and aliases; it is not a reachable gameplay proof.","Actual scenario fixtures and authored tracks use only ordinary Press/Advance; pause is the existing Phase shell.","Snapshot checks do not establish stochastic policy, WebGL frame budget, visuals, or physical touch behavior.","No legacy catalog tape or bot controller was activated."},performance=perf,cases,error};
        Directory.CreateDirectory("Verification");File.WriteAllText("Verification/"+stem+".json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText("Verification/"+stem+".txt",report.status+" RaceSnapshot.Clone\n"+checks+" checks; "+snapshots+" source snapshots; "+continuationSteps+" paired continuation steps.\nSource stable: "+report.sourceStable+"\n"+JsonSerializer.Serialize(perf)+"\n"+(error??"No mutable timeline alias across source or sibling branches. See JSON for fixtures, immutable-input contract and limits.")+"\n");
        Console.WriteLine(report.status+" checks="+checks+" snapshots="+snapshots+" continuationSteps="+continuationSteps+" sourceStable="+report.sourceStable);if(error!=null)Console.WriteLine(error);return error==null?0:1;
    }
}