using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;
using TrackWorkshop;

string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
var service=new WorkshopService(Path.Combine(root,"Assets/StreamingAssets/Tracks"));
int assertions=0,pairs=0;
var evidence=new List<object>();
void Need(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
void Near(double a,double b,string message)=>Need(Math.Abs(a-b)<1e-7,message+" "+a+" vs "+b);
void Rejected(Action action,string message){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Need(rejected,message);}
JsonNode Analyze(string json)=>JsonSerializer.SerializeToNode(service.Analyze(new(json)),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
string Edit(string json,Action<JsonNode> action){var n=JsonNode.Parse(json);action(n);return n.ToJsonString();}

// Independent frozen times extracted from the accepted 14e22ba snapshot in
// Tools/JsonTracksCheck/accepted-14e22ba.json. Not recalculated from today's B.
var expected=new Dictionary<string,double>{
 ["full-long/Chosen"]=33.05,["full-long/FastFlip"]=33.05,["full-short/Chosen"]=33.05,["full-short/FastFlip"]=33.05,
 ["full-flow/Chosen"]=33.05,["full-flow/FastFlip"]=33.05,["rhythm-cascade/Chosen"]=33.05,["rhythm-cascade/FastFlip"]=33.05,
 ["mixed-tricks/Chosen"]=33.05,["mixed-tricks/FastFlip"]=33.05,["hidden-intercepts/Chosen"]=33.35,["hidden-intercepts/FastFlip"]=32.75,
 ["hidden-manifestation/Chosen"]=33.95,["hidden-manifestation/FastFlip"]=33.95,["rethink/Chosen"]=34.85};
Need(service.Catalog().Length==8,"8 catalog entries");
foreach(var entry in service.Catalog())
{
    string json=service.CatalogJson(entry.Id);Need(TrackJson.TryRead(json,out var p,out _),"strict catalog");var analysis=Analyze(json);
    Need(analysis["gameValid"].GetValue<bool>(),"original game valid");
    foreach(var obstacle in p.Track.Obstacles)
    {
        var shown=analysis["obstacles"].AsArray().Single(o=>o["id"].GetValue<string>()==obstacle.Id);
        foreach(var b in shown["bands"].AsArray())
        {
            Need(ObstacleGeometry.TryGetBand(obstacle,b["index"].GetValue<int>(),out var actual),"band exists");
            Near(b["x"].GetValue<double>(),actual.X,"band X");Near(b["y"].GetValue<double>(),actual.Y,"band Y");
            Near(b["width"].GetValue<double>(),actual.Width,"band width");Near(b["height"].GetValue<double>(),actual.Height,"band height");
        }
        if(obstacle.Kind=="gate")Need(shown["bands"].AsArray().Count==2,"both gate bands");
    }
    foreach(var zone in p.Track.RiskZones)
    {
        var shown=analysis["rewards"].AsArray().Single(r=>r["id"].GetValue<string>()==zone.Id);
        if(zone.IsFinish)
        {
            Need(shown["segments"].AsArray().Count==4,"4 physical finish strokes");
            for(int i=0;i<4;i++){var actual=FinishTargetGeometry.GetSegment(i);Near(shown["segments"][i]["x1"].GetValue<double>(),zone.X+actual.X1,"finish X1");Near(shown["segments"][i]["y2"].GetValue<double>(),zone.Y+actual.Y2,"finish Y2");}
            Near(shown["strokeWidth"].GetValue<double>(),3,"finish width");
        }
        if(zone.IsLanding)
        {
            var stone=p.Track.Obstacles.Single(o=>o.Id==zone.ObstacleId);
            Near(shown["left"].GetValue<double>(),Math.Max(stone.X,zone.X-zone.LandingWidth/2),"landing left");
            Near(shown["right"].GetValue<double>(),Math.Min(stone.X+stone.Width,zone.X+zone.LandingWidth/2),"landing right");
            Near(shown["planeY"].GetValue<double>(),stone.Kind=="floor"?240-stone.Height:stone.Height,"landing surface");
        }
    }
    foreach(var reference in p.References)
    {
        var result=service.Replay(new(json,reference.ProfileId,.8,"distance",reference.Inputs));pairs++;
        Need(result.Summary.CleanFinish,entry.Id+" clean");Near(result.Summary.FinishTime.Value,expected[entry.Id+"/"+reference.ProfileId],"frozen time");
        Need(result.Summary.AcceptedInputs==reference.Inputs.Length,"all reference inputs accepted");
        Need(result.Summary.Slows==0&&result.Summary.FinishBoosts==1,"no slows and finish collected");
        Need(reference.RewardIds.All(id=>result.Summary.SuccessfulIds.Contains(id)),"all selected rewards");
        Need(result.Samples.Count<=20000,"bounded samples");
        foreach(var input in result.Inputs)Near(input.X.Value,input.Value,"exact distance edge including bonus boundaries");
        if(p.Track.Obstacles.Any(o=>o.Hidden))Need(result.HiddenStates.All(s=>s.Phase=="Solid"&&s.RevealedAt.HasValue&&s.SolidAt>=s.RevealedAt),"real hidden timeline");
        var byTime=service.Replay(new(json,reference.ProfileId,.8,"time",result.Inputs.Select(i=>i.Time.Value).ToArray()));
        Near(byTime.Summary.FinishTime.Value,result.Summary.FinishTime.Value,"time axis equivalence");Need(byTime.Summary.CleanFinish,"time axis clean");
        evidence.Add(new{id=entry.Id,profile=reference.ProfileId,result.Summary,result.Samples.Count});
    }
}
Need(pairs==15,"15 reference pairs");
string original=service.CatalogJson("full-long");
string stale=Edit(original,n=>n["track"]["obstacles"][0]["x"]=301);
var staleAnalysis=Analyze(stale);Need(staleAnalysis["staleReferences"].GetValue<bool>()&&!staleAnalysis["gameValid"].GetValue<bool>(),"stale explicit");
Need(staleAnalysis["canonicalJson"]==null&&staleAnalysis["references"].AsArray().Count==0,"no silent stale export");
Need(TrackJson.TryRead(staleAnalysis["cleanedJson"].GetValue<string>(),out var cleaned,out _)&&cleaned.References.Length==0,"cleaned export strict and no refs");
Rejected(()=>Analyze(stale.Replace("\"length\":7920","\"length\":7920,\"length\":7920")),"duplicate geometry plus stale rejected");
Rejected(()=>Analyze(Edit(stale,n=>n["track"]["obstacles"][0]["width"]=-1)),"invalid geometry plus stale rejected");
Rejected(()=>Analyze("{"),"malformed JSON");Rejected(()=>Analyze(new string('x',TrackJson.MaximumBytes+1)),"size limit");
ReplayRequest Request(double[] values)=>new(original,"Chosen",.8,"time",values);
Rejected(()=>service.Replay(Request(new double[4097])),"input count limit");
foreach(var bad in new[]{new[]{-1d},new[]{1d,1d},new[]{2d,1d},new[]{double.NaN},new[]{double.PositiveInfinity}})Rejected(()=>service.Replay(Request(bad)),"bad input values");
Rejected(()=>service.Replay(Request(Array.Empty<double>()) with{Axis="nonsense"}),"axis validation");
Rejected(()=>service.Replay(Request(Array.Empty<double>()) with{Profile="custom"}),"profile validation");
Rejected(()=>service.Replay(new(service.CatalogJson("rethink"),"FastFlip",.8,"distance",Array.Empty<double>())),"chosen only");
var ignored=service.Replay(new(original,"FastFlip",.8,"time",new[]{0d,.01,.02,119d}));
Need(ignored.Inputs[0].Status=="accepted"&&ignored.Inputs[1].Status=="accepted"&&ignored.Inputs[2].Status=="ignored"&&ignored.Inputs[3].Status=="unreached","accepted ignored and late input");
var refReplay=service.Replay(new(original,"Chosen",.8,"distance",TrackJson.TryRead(original,out var source,out _)?source.References[0].Inputs:Array.Empty<double>()));
var late=service.Replay(new(original,"Chosen",.8,"time",refReplay.Inputs.Select(i=>i.Time.Value).Append(35d).ToArray()));
Near(late.Summary.FinishTime.Value,33.05,"late Press does not restart race");Need(late.Inputs[^1].Status=="unreached","post finish input unused");
string empty=TrackJson.Write(new TrackPackage(new Track(1000000,Array.Empty<Obstacle>()){Id="long-test",Name="Long"}));
var timeout=service.Replay(new(empty,"Chosen",.8,"time",Array.Empty<double>()));
Need(timeout.Summary.Status=="TimedOut"&&timeout.Summary.Elapsed<=120.0000001&&timeout.Samples.Count<=20000,"bounded time and response");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions,tracks=8,pairs,source="accepted-14e22ba frozen times",evidence},new JsonSerializerOptions{WriteIndented=true}));
