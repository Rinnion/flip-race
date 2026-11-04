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
using var policy=new ServerPolicy(deployment);
app.Use((HttpContext context,RequestDelegate next)=>policy.InvokeAsync(context,next));
app.MapGet("/healthz",()=>new{status="ok",version="0.4.0"});
app.MapGet("/api/catalog",()=>new{entries=service.Catalog()});
app.MapGet("/api/catalog/{id}",(string id)=>service.CatalogJson(id) is string json?Results.Ok(new{json}):Results.NotFound(new{error="Unknown catalog ID."}));
app.MapPost("/api/analyze",(AnalyzeRequest request)=>service.Analyze(request));
app.MapPost("/api/replay",(ReplayRequest request)=>service.Replay(request));
// Only explicit Web assets are served. No generic project file provider.
foreach(var pair in new[]{("/","index.html","text/html; charset=utf-8"),("/index.html","index.html","text/html; charset=utf-8"),("/app.js","app.js","text/javascript; charset=utf-8"),("/model.mjs","model.mjs","text/javascript; charset=utf-8"),("/edit-model.mjs","edit-model.mjs","text/javascript; charset=utf-8"),("/styles.css","styles.css","text/css; charset=utf-8")}){
    string file=pair.Item2,type=pair.Item3;app.MapGet(pair.Item1,()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web",file),type));
}
app.Run();
