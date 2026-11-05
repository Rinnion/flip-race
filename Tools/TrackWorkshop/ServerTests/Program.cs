using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TrackWorkshop;

int assertions=0;
void Need(bool condition,string context){assertions++;if(!condition)throw new Exception(context);}
ServerOptions Options(params (string Key,string Value)[] entries)
{var values=entries.ToDictionary(e=>e.Key,e=>e.Value);return ServerOptions.FromEnvironment(key=>values.GetValueOrDefault(key));}
void Rejected(string name,string value)
{bool failed=false;try{Options((name,value));}catch(ArgumentException){failed=true;}Need(failed,name+"="+value+" must fail configuration");}
DefaultHttpContext Context(string path="/api/analyze",string host="127.0.0.1:4186",string origin=null,string method="POST",string contentType="application/json")
{
    var context=new DefaultHttpContext();context.Request.Method=method;context.Request.Path=path;context.Request.Host=new HostString(host);
    if(origin!=null)context.Request.Headers.Origin=origin;
    context.Request.ContentType=contentType;context.Response.Body=new MemoryStream();return context;
}
var local=Options();
Need(local.BindAddress.Equals(IPAddress.Loopback)&&local.Port==4186&&local.PublicOrigin==null&&local.MaximumConcurrentRequests==2,"local defaults remain exact");
foreach(string host in new[]{"127.0.0.1:4186","localhost:4186","LOCALHOST:4186"})Need(local.AcceptsHost(new(host)),"local host "+host);
foreach(string host in new[]{"127.0.0.1","localhost:4185","localhost.evil:4186","evil.example:4186","localhost.:4186","localhost:bad"})Need(!local.AcceptsHost(new(host)),"reject local host "+host);
Need(local.AcceptsOrigin(new("localhost:4186"),"http://localhost:4186"),"same local origin");
Need(local.AcceptsOrigin(new("localhost:4186"),""),"CLI origin optional");
foreach(string origin in new[]{"null","https://localhost:4186","http://localhost:4186/","http://127.0.0.1:4186","http://localhost:4186.evil"})Need(!local.AcceptsOrigin(new("localhost:4186"),origin),"reject mismatched origin "+origin);
foreach(var pair in new[]{("WORKSHOP_BIND_ADDRESS","localhost"),("WORKSHOP_BIND_ADDRESS","*"),("WORKSHOP_BIND_ADDRESS",""),
    ("WORKSHOP_BIND_ADDRESS","0.0.0.0"),("WORKSHOP_BIND_ADDRESS","::"),("WORKSHOP_PORT","0"),("WORKSHOP_PORT","65536"),("WORKSHOP_PORT","-1"),("WORKSHOP_PORT","4186 "),
    ("WORKSHOP_PORT","1.5"),("WORKSHOP_MAX_CONCURRENT_REQUESTS","0"),("WORKSHOP_MAX_CONCURRENT_REQUESTS","33"),
    ("WORKSHOP_PUBLIC_ORIGIN","https://*.example"),("WORKSHOP_PUBLIC_ORIGIN","ftp://workshop.example"),("WORKSHOP_PUBLIC_ORIGIN","https://user:password@example.com"),
    ("WORKSHOP_PUBLIC_ORIGIN","https://example.com/path"),("WORKSHOP_PUBLIC_ORIGIN","https://example.com/?query"),("WORKSHOP_PUBLIC_ORIGIN","https://example.com/#part"),
    ("WORKSHOP_PUBLIC_ORIGIN","https://one.example https://two.example"),("WORKSHOP_PUBLIC_ORIGIN","")})Rejected(pair.Item1,pair.Item2);
var server=Options(("WORKSHOP_BIND_ADDRESS","0.0.0.0"),("WORKSHOP_PORT","8080"),("WORKSHOP_PUBLIC_ORIGIN","https://workshop.example"),("WORKSHOP_MAX_CONCURRENT_REQUESTS","1"));
Need(server.BindAddress.Equals(IPAddress.Any)&&server.Port==8080&&server.PublicOrigin=="https://workshop.example"&&server.MaximumConcurrentRequests==1,"public options");
foreach(string host in new[]{"workshop.example","workshop.example:443","WORKSHOP.EXAMPLE"})Need(server.AcceptsHost(new(host)),"public identity accepts "+host);
foreach(string host in new[]{"127.0.0.1:8080","app:8080","workshop.example:8080","workshop.example:80","other.example","workshop.example.evil"})Need(!server.AcceptsHost(new(host)),"public identity rejects "+host);
Need(server.AcceptsOrigin(new("workshop.example"),"https://workshop.example"),"public exact origin");
Need(server.AcceptsOrigin(new("workshop.example"),""),"server CLI without origin remains permitted");
foreach(string origin in new[]{"http://workshop.example","https://workshop.example:443","https://workshop.example/","https://other.example","null","https://workshop.example, https://other.example"})
    Need(!server.AcceptsOrigin(new("workshop.example"),origin),"strict public origin "+origin);
var custom=Options(("WORKSHOP_BIND_ADDRESS","127.0.0.2"),("WORKSHOP_PORT","4199"));
Need(custom.AcceptsHost(new("127.0.0.2:4199"))&&custom.AcceptsHost(new("localhost:4199")),"configured loopback and aliases");
var ipv6=Options(("WORKSHOP_BIND_ADDRESS","::1"),("WORKSHOP_PORT","4198"));Need(ipv6.AcceptsHost(new("[::1]:4198")),"IPv6 loopback");
var customPublic=Options(("WORKSHOP_PUBLIC_ORIGIN","http://example.com:9000"));
Need(customPublic.AcceptsHost(new("example.com:9000"))&&!customPublic.AcceptsHost(new("example.com")),"explicit public port");
var ipv6Public=Options(("WORKSHOP_BIND_ADDRESS","::"),("WORKSHOP_PUBLIC_ORIGIN","https://[::1]:8443"));Need(ipv6Public.AcceptsHost(new("[::1]:8443")),"IPv6 public identity");

using(var policy=new ServerPolicy(local))
{
    bool called=false;
    var request=Context(host:"evil.example:4186");request.Request.Headers["X-Forwarded-Host"]="localhost:4186";
    await policy.InvokeAsync(request,_=>{called=true;return Task.CompletedTask;});Need(request.Response.StatusCode==400&&!called,"forwarded header cannot authorize host");
    request=Context(origin:"http://evil.example");await policy.InvokeAsync(request,_=>{called=true;return Task.CompletedTask;});Need(request.Response.StatusCode==400&&!called,"bad origin rejected before binding");
    request=Context(contentType:"text/plain");await policy.InvokeAsync(request,_=>{called=true;return Task.CompletedTask;});Need(request.Response.StatusCode==400&&!called,"non-JSON POST rejected");
    request=Context(origin:"http://localhost:4186",host:"localhost:4186");await policy.InvokeAsync(request,_=>{called=true;return Task.CompletedTask;});Need(called&&request.Response.StatusCode==200,"normal local request allowed");
    Need(request.Response.Headers["X-Content-Type-Options"]=="nosniff"&&request.Response.Headers.ContentSecurityPolicy.ToString().Contains("frame-ancestors 'none'"),"security headers retained");
}
using(var policy=new ServerPolicy(server))
{
    var allowed=Context(host:"workshop.example",origin:"https://workshop.example");allowed.Request.Scheme="http";allowed.Request.Headers["X-Forwarded-Proto"]="malicious";
    bool called=false;await policy.InvokeAsync(allowed,_=>{called=true;return Task.CompletedTask;});Need(called,"configured identity behind plain HTTP proxy without forwarding trust");
}

// A blocked downstream delegate includes body binding and response writing.
// This proves admission deterministically without timing simulation load.
using(var policy=new ServerPolicy(local))
{
    var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int entered=0;
    async Task Held(HttpContext _){if(Interlocked.Increment(ref entered)==2)started.SetResult();await release.Task;}
    var first=policy.InvokeAsync(Context(),Held);var second=policy.InvokeAsync(Context(path:"/api/replay"),Held);await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    foreach(string path in new[]{"/api/analyze","/api/analyze/","/API/REPLAY","/api/replay/","/api/prepare-bot","/API/PREPARE-BOT/"})
    {
        var rejected=Context(path:path);await policy.InvokeAsync(rejected,_=>throw new Exception("429 must not bind/read body"));
        Need(rejected.Response.StatusCode==429&&rejected.Response.Headers["Retry-After"]=="1","capacity rejects route variant "+path);
    }
    bool health=false;await policy.InvokeAsync(Context(path:"/healthz",method:"GET",contentType:null),_=>{health=true;return Task.CompletedTask;});Need(health,"GET health bypasses heavy capacity");
    release.SetResult();await Task.WhenAll(first,second);
    bool enteredAfter=false;await policy.InvokeAsync(Context(),_=>{enteredAfter=true;return Task.CompletedTask;});Need(enteredAfter,"capacity released after full downstream response");
}
using(var policy=new ServerPolicy(Options(("WORKSHOP_MAX_CONCURRENT_REQUESTS","1"))))
{
    foreach(Exception failure in new Exception[]{new ArgumentException("invalid track"),new InvalidOperationException("endpoint failure"),new OperationCanceledException("disconnect")})
    {
        var failed=Context();try{await policy.InvokeAsync(failed,_=>Task.FromException(failure));}catch(Exception e) when(ReferenceEquals(e,failure)){}
        if(failure is ArgumentException)Need(failed.Response.StatusCode==400,"validation error status");
        bool entered=false;await policy.InvokeAsync(Context(),_=>{entered=true;return Task.CompletedTask;});Need(entered,"release after "+failure.GetType().Name);
    }
}
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions,coverage=new[]{"environment validation","local aliases","exact public Host/Origin","proxy identity without forwarded trust","prebinding failfast capacity incl trailing slash","GET availability","lease release on errors/cancel","security headers"}},new JsonSerializerOptions{WriteIndented=true}));
