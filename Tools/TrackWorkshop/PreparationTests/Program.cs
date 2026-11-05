using System.Text.Json;
using FlipRace.Core;
using TrackWorkshop;

var rows=new List<object>();int assertions=0;
void Check(bool condition,string detail){assertions++;if(!condition)throw new Exception(detail);}
Track Course(string id,Obstacle[] obstacles,double length=1200)
{return new(length,obstacles){Id=id,Name=id,RiskZones=new[]{new RiskZone{Id=id+"-finish",Kind="finish",X=length-100,Y=120,Radius=8}}};}
var simple=Course("prepare-no-seed-simple",new[]{new Obstacle{Id="stone",Kind="floor",X=350,Width=64,Height=48}});
var dense=Course("prepare-no-seed-dense",new[]{
    new Obstacle{Id="floor-a",Kind="floor",X=250,Width=64,Height=48},
    new Obstacle{Id="ceiling-b",Kind="ceiling",X=500,Width=64,Height=48},
    new Obstacle{Id="floor-c",Kind="floor",X=750,Width=64,Height=48}});
foreach(var track in new[]{simple,dense})
{
    var result=BotPreparationSearch.Find(track,FlightPresets.Chosen,.8,new(MaximumStates:150000,MaximumMilliseconds:10000));
    rows.Add(new{id=track.Id,result});Console.Error.WriteLine(track.Id+" found="+result.Found+" states="+result.ExpandedStates+" ms="+result.ElapsedMilliseconds);
    Check(result.Found,track.Id+" unseeded clean finish must be found");Check(result.Inputs.Length>0,"real nonempty inputs");
    Check(result.RewardIds.Contains(track.Id+"-finish"),"finish physically collected");
    var replay=new WorkshopService(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../Assets/StreamingAssets/Tracks")))
        .Replay(new(TrackJson.Write(new TrackPackage(track)),"Chosen",.8,"distance",result.Inputs));
    Check(replay.Summary.CleanFinish&&replay.Summary.FinishBoosts==1,"independent distance replay clean/full finish");
    Check(Math.Abs(replay.Summary.FinishTime.Value-result.FinishTime.Value)<1e-7,"search and distance replay agree");
}
using(var cancelled=new CancellationTokenSource())
{
    cancelled.Cancel();var result=BotPreparationSearch.Find(simple,FlightPresets.Chosen,.8,cancellation:cancelled.Token);
    Check(!result.Found&&result.ExpandedStates==0,"pre-cancel does no search work");
}
var bounded=BotPreparationSearch.Find(dense,FlightPresets.Chosen,.8,new(MaximumStates:1));Check(!bounded.Found&&bounded.ExpandedStates<=1,"hard state bound");
var preparation=new BotPreparationService(new[]{"full-long"});
var ordinary=Course("prepared-custom-bonus",new[]{new Obstacle{Id="stone",Kind="floor",X=350,Width=64,Height=48}});
ordinary.RiskZones=new[]{new RiskZone{Id="ordinary-ground",Kind="instant",X=80,Y=226,Radius=8},ordinary.RiskZones[0]};
string ordinarySource=TrackJson.Write(new TrackPackage(ordinary));
string fixtureDirectory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../Tools/TrackWorkshop/PreparationTests"));
File.WriteAllText(Path.Combine(fixtureDirectory,"raw-custom-bonus.track.json"),ordinarySource);
var prepared=preparation.Prepare(new(ordinarySource,new[]{"Chosen"}));
rows.Add(new{id=ordinary.Id,prepared});
Check(prepared.Status=="prepared"&&prepared.Profiles[0].Status=="ready","unseeded custom bonus map admitted by actual Master proof");
Check(prepared.Profiles[0].RewardIds.Length==2&&prepared.Profiles[0].RewardIds.Contains("ordinary-ground"),"ordinary bonus plus finish physically earned");
Check(TrackJson.TryRead(prepared.PreparedJson,out var preparedPackage,out _),"prepared export strict JSON");
Check(preparedPackage.Track.Id==ordinary.Id&&preparedPackage.References.Length==1,"custom ID preserved and one verified profile exported");
File.WriteAllText(Path.Combine(fixtureDirectory,"prepared-custom-bonus.track.json"),prepared.PreparedJson);
var replayProof=new PreparedReferenceProof(preparedPackage,FlightPresets.Chosen);int proofGuard=0;
while(!replayProof.Complete&&proofGuard++<10000)replayProof.Step(16);
Check(replayProof.Success&&replayProof.CompletedRuns==2,"independent shared reference+actual Master runs");
var hidden=Course("prepared-hidden-bonus",new[]{new Obstacle{Id="hidden",Kind="floor",X=350,Width=64,Height=48,Hidden=true}});
hidden.RiskZones=new[]{new RiskZone{Id="hidden-ordinary",Kind="instant",X=80,Y=226,Radius=8},hidden.RiskZones[0]};
string hiddenSource=TrackJson.Write(new TrackPackage(hidden));
File.WriteAllText(Path.Combine(fixtureDirectory,"raw-hidden-bonus.track.json"),hiddenSource);
var preparedHidden=preparation.Prepare(new(hiddenSource,new[]{"Chosen"}));
rows.Add(new{id=hidden.Id,preparedHidden});
Check(preparedHidden.Status=="prepared","hidden custom map prepared across selected warnings");
Check(TrackJson.TryRead(preparedHidden.PreparedJson,out var hiddenPackage,out _),"hidden export strict");
File.WriteAllText(Path.Combine(fixtureDirectory,"prepared-hidden-bonus.track.json"),preparedHidden.PreparedJson);
var hiddenProof=new PreparedReferenceProof(hiddenPackage,FlightPresets.Chosen);int hiddenGuard=0;
while(!hiddenProof.Complete&&hiddenGuard++<20000)hiddenProof.Step(16);
Check(hiddenProof.Success&&hiddenProof.RequiredRuns==10&&hiddenProof.CompletedRuns==10,"all five warning values + actual Master independently pass");
var noFinish=Course("no-finish",simple.Obstacles);noFinish.RiskZones=Array.Empty<RiskZone>();
Check(preparation.Prepare(new(TrackJson.Write(new TrackPackage(noFinish)),new[]{"Chosen"})).Profiles[0].Status=="unsupported","no finish explanation rather than false prepared");
var noObstacles=Course("no-obstacles",Array.Empty<Obstacle>());
Check(preparation.Prepare(new(TrackJson.Write(new TrackPackage(noObstacles)),new[]{"Chosen"})).Profiles[0].Status=="unsupported","C=0 unsupported");
var restricted=new TrackPackage(ordinary,TrackProfilePolicy.ChosenOnly);
Check(preparation.Prepare(new(TrackJson.Write(restricted),new[]{"FastFlip"})).Profiles[0].Status=="unsupported","chosen-only restriction preserved");
bool reservedRejected=false;try{preparation.Prepare(new(ordinarySource,new[]{"Chosen"},OutputTrackId:"full-long"));}catch(ArgumentException){reservedRejected=true;}
Check(reservedRejected,"reserved output ID rejected");
bool cancelledPrepare=false;using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{preparation.Prepare(new(ordinarySource,new[]{"Chosen"}),cancel.Token);}catch(OperationCanceledException){cancelledPrepare=true;}}
Check(cancelledPrepare,"endpoint preparation cancellation propagated");
var bothProfiles=preparation.Prepare(new(ordinarySource,new[]{"Chosen","FastFlip"},OutputTrackId:"prepared-both-profiles"));
Check(bothProfiles.Status=="prepared"&&bothProfiles.Profiles.All(p=>p.Status=="ready"),"both exact flight profiles can be prepared without seeds on custom geometry");
Check(TrackJson.TryRead(bothProfiles.PreparedJson,out var bothPackage,out _)&&bothPackage.References.Length==2,"only verified profile references combined in one export");
var malformedHints=System.Text.Json.Nodes.JsonNode.Parse(ordinarySource);
malformedHints["botReferences"]=System.Text.Json.Nodes.JsonNode.Parse("[{\"profileId\":\"Chosen\",\"inputs\":[null]}]");
Check(preparation.Prepare(new(malformedHints.ToJsonString(),new[]{"Chosen"})).Status=="prepared","null copied reference hint discarded without HTTP500 or trusting it");
bool catalogBefore=TrackCatalog.HasAttemptedInstall;
var parallel=await Task.WhenAll(Task.Run(()=>preparation.Prepare(new(ordinarySource,new[]{"Chosen"},OutputTrackId:"parallel-a"))),
    Task.Run(()=>preparation.Prepare(new(ordinarySource,new[]{"Chosen"},OutputTrackId:"parallel-b"))));
Check(parallel.All(p=>p.Status=="prepared"),"concurrent requests both validated");
Check(parallel[0].OutputTrackId=="parallel-a"&&parallel[1].OutputTrackId=="parallel-b","request IDs isolated");
Check(TrackCatalog.HasAttemptedInstall==catalogBefore,"preparation never mutates process-wide catalogue");
using(var admission=new ServerPolicy(ServerOptions.FromEnvironment(name=>name=="WORKSHOP_MAX_CONCURRENT_REQUESTS"?"1":null)))
{
    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Microsoft.AspNetCore.Http.DefaultHttpContext Http(string path)
    {
        var c=new Microsoft.AspNetCore.Http.DefaultHttpContext();c.Request.Method="POST";c.Request.Path=path;c.Request.Host=new("localhost:4186");c.Request.ContentType="application/json";c.Response.Body=new MemoryStream();return c;
    }
    var held=admission.InvokeAsync(Http("/api/prepare-bot"),_=>release.Task);
    var rejected=Http("/API/PREPARE-BOT/");bool entered=false;
    await admission.InvokeAsync(rejected,_=>{entered=true;return Task.CompletedTask;});
    Check(!entered&&rejected.Response.StatusCode==429&&rejected.Response.Headers["Retry-After"]=="1","preparation admission includes case/trailing slash and failfast capacity");
    release.SetResult();await held;
}
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions,rows},new JsonSerializerOptions{WriteIndented=true}));
