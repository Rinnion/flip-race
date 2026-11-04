using System.Net;
using TrackWorkshop;

var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=args,ContentRootPath=AppContext.BaseDirectory});
// Explicit endpoint overrides environment URLs: this tool cannot bind remotely.
builder.WebHost.ConfigureKestrel(options=>{options.Listen(IPAddress.Loopback,4186);options.Limits.MaxRequestBodySize=16*1024*1024;});
builder.Services.ConfigureHttpJsonOptions(options=>options.SerializerOptions.PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase);
var app=builder.Build();
var service=new WorkshopService(Path.Combine(AppContext.BaseDirectory,"Catalog"));
app.Use(async(context,next)=>{
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'";
    var host=context.Request.Host;
    if(host.Port!=4186||(host.Host!="127.0.0.1"&&host.Host!="localhost")){context.Response.StatusCode=400;return;}
    if(context.Request.Method=="POST"){
        string origin=context.Request.Headers.Origin.ToString();
        if((origin.Length>0&&origin!="http://"+host)||!context.Request.HasJsonContentType()) {context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error="Same-origin JSON requests required."});return;}
    }
    try{await next();}catch(ArgumentException e){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error=e.Message});}
});
app.MapGet("/api/catalog",()=>new{entries=service.Catalog()});
app.MapGet("/api/catalog/{id}",(string id)=>service.CatalogJson(id) is string json?Results.Ok(new{json}):Results.NotFound(new{error="Unknown catalog ID."}));
app.MapPost("/api/analyze",(AnalyzeRequest request)=>service.Analyze(request));
app.MapPost("/api/replay",(ReplayRequest request)=>service.Replay(request));
// Only explicit Web assets are served. No generic project file provider.
foreach(var pair in new[]{("/","index.html","text/html; charset=utf-8"),("/index.html","index.html","text/html; charset=utf-8"),("/app.js","app.js","text/javascript; charset=utf-8"),("/model.mjs","model.mjs","text/javascript; charset=utf-8"),("/styles.css","styles.css","text/css; charset=utf-8")}){
    string file=pair.Item2,type=pair.Item3;app.MapGet(pair.Item1,()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web",file),type));
}
app.Run();
