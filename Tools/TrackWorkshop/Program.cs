using TrackWorkshop;

var deployment=ServerOptions.FromEnvironment();
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=args,ContentRootPath=AppContext.BaseDirectory});
// Explicit listener and identity override generic URL configuration. Forwarded
// headers are intentionally not enabled; the configured public origin is final.
builder.WebHost.ConfigureKestrel(options=>{options.Listen(deployment.BindAddress,deployment.Port);options.Limits.MaxRequestBodySize=16*1024*1024;});
builder.Services.PostConfigure<ForwardedHeadersOptions>(options=>options.ForwardedHeaders=Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.None);
builder.Services.ConfigureHttpJsonOptions(options=>options.SerializerOptions.PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase);
var app=builder.Build();
var service=new WorkshopService(Path.Combine(AppContext.BaseDirectory,"Catalog"));
var preparation=new BotPreparationService(service.Catalog().Select(e=>e.Id));
using var policy=new ServerPolicy(deployment);
using var live=new LiveSocket(deployment);
app.Use((HttpContext context,RequestDelegate next)=>policy.InvokeAsync(context,next));
app.UseWebSockets();
app.MapGet("/healthz",()=>new{status="ok",version="0.5.22"});
app.MapGet("/api/catalog",()=>new{entries=service.Catalog()});
app.MapGet("/api/catalog/{id}",(string id)=>service.CatalogJson(id) is string json?Results.Ok(new{json}):Results.NotFound(new{error="Unknown catalog ID."}));
app.MapPost("/api/analyze",(AnalyzeRequest request)=>service.Analyze(request));
app.MapPost("/api/replay",(ReplayRequest request)=>service.Replay(request));
app.MapPost(LaterPressAnalysis.Path,async(LaterPressRequest request,HttpContext context)
    =>await Task.Run(()=>LaterPressAnalysis.Find(request,context.RequestAborted),context.RequestAborted));
app.MapPost("/api/input-tolerance",async(InputToleranceRequest request,HttpContext context)
    =>await Task.Run(()=>InputToleranceAnalysis.Find(request,context.RequestAborted),context.RequestAborted));
app.MapPost("/api/input-tolerance/preview",async(InputTolerancePreviewRequest request,HttpContext context)
    =>await Task.Run(()=>InputToleranceAnalysis.Preview(request,context.RequestAborted),context.RequestAborted));
app.MapPost("/api/pair-tolerance",async(PairToleranceRequest request,HttpContext context)
    =>await Task.Run(()=>PairToleranceAnalysis.Find(request,context.RequestAborted),context.RequestAborted));
app.MapPost("/api/pair-tolerance/preview",async(PairTolerancePreviewRequest request,HttpContext context)
    =>await Task.Run(()=>PairToleranceAnalysis.Preview(request,context.RequestAborted),context.RequestAborted));
app.MapPost("/api/prepare-bot",async(PrepareBotRequest request,HttpContext context)=>await Task.Run(()=>preparation.Prepare(request,context.RequestAborted),context.RequestAborted));
app.MapPost("/api/route-search",async(RouteSearchRequest request,HttpContext context)
    =>await Task.Run(()=>service.SearchRoutes(request,context.RequestAborted),context.RequestAborted));
app.MapPost(RouteSearchStream.Path,(RouteSearchRequest request,HttpContext context)
    =>RouteSearchStream.HandleAsync(service,request,context,app.Logger));
app.MapPost("/api/analysis-target",async(AnalysisTargetRequest request,HttpContext context)
    =>await Task.Run(()=>AnalysisTarget.Resolve(request,context.RequestAborted),context.RequestAborted));
app.MapPost(ToleranceBatchStream.Path,(ReplayRequest request,HttpContext context)
    =>ToleranceBatchStream.HandleAsync(request,context,app.Logger));
app.MapPost(ToleranceBatchStream.CheckpointPath,async(ToleranceBatchCheckpointRequest request,HttpContext context)
    =>await Task.Run(()=>ToleranceBatch.SaveCheckpoint(request,context.RequestAborted),context.RequestAborted));
app.MapGet("/api/live",live.HandleAsync);
// Only explicit Web assets are served. No generic project file provider.
foreach(var pair in new[]{("/","index.html","text/html; charset=utf-8"),("/index.html","index.html","text/html; charset=utf-8"),("/app.js","app.js","text/javascript; charset=utf-8"),("/model.mjs","model.mjs","text/javascript; charset=utf-8"),("/edit-model.mjs","edit-model.mjs","text/javascript; charset=utf-8"),("/draft.mjs","draft.mjs","text/javascript; charset=utf-8"),("/object-source.mjs","object-source.mjs","text/javascript; charset=utf-8"),("/live.mjs","live.mjs","text/javascript; charset=utf-8"),("/styles.css","styles.css","text/css; charset=utf-8")}){
    string file=pair.Item2,type=pair.Item3;app.MapGet(pair.Item1,()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web",file),type));
}
app.MapGet("/authoring-fields.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","authoring-fields.mjs"),"text/javascript; charset=utf-8"));
app.MapGet("/route-analysis.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","route-analysis.mjs"),"text/javascript; charset=utf-8"));
app.MapGet("/run-settings.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","run-settings.mjs"),"text/javascript; charset=utf-8"));
app.MapGet("/search-progress.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","search-progress.mjs"),"text/javascript; charset=utf-8"));
app.MapGet("/later-press.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","later-press.mjs"),"text/javascript; charset=utf-8"));
app.MapGet("/input-tolerance.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","input-tolerance.mjs"),"text/javascript; charset=utf-8"));
app.MapGet("/input-tempo.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","input-tempo.mjs"),"text/javascript; charset=utf-8"));
app.MapGet("/pair-tolerance.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","pair-tolerance.mjs"),"text/javascript; charset=utf-8"));
app.MapGet("/tolerance-batch.mjs",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web","tolerance-batch.mjs"),"text/javascript; charset=utf-8"));
app.Run();
