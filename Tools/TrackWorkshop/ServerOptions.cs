using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace TrackWorkshop;

/// <summary>Explicit deployment identity, independent of untrusted forwarding headers.</summary>
public sealed class ServerOptions
{
    public IPAddress BindAddress { get; }
    public int Port { get; }
    public string PublicOrigin { get; }
    public int MaximumConcurrentRequests { get; }
    readonly Uri publicUri;

    ServerOptions(IPAddress bindAddress, int port, Uri publicUri, int concurrency)
    {
        BindAddress=bindAddress;Port=port;this.publicUri=publicUri;
        PublicOrigin=publicUri?.GetLeftPart(UriPartial.Authority);
        MaximumConcurrentRequests=concurrency;
    }

    public static ServerOptions FromEnvironment(Func<string,string> read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        string bind=read("WORKSHOP_BIND_ADDRESS")??"127.0.0.1";
        if(string.IsNullOrWhiteSpace(bind)||bind!=bind.Trim()||!IPAddress.TryParse(bind,out var address))
            throw new ArgumentException("WORKSHOP_BIND_ADDRESS must be a literal IPv4 or IPv6 address.");
        int port=ReadInteger(read("WORKSHOP_PORT"),4186,1,65535,"WORKSHOP_PORT");
        int capacity=ReadInteger(read("WORKSHOP_MAX_CONCURRENT_REQUESTS"),2,1,32,"WORKSHOP_MAX_CONCURRENT_REQUESTS");
        string origin=read("WORKSHOP_PUBLIC_ORIGIN");Uri identity=null;
        if(origin!=null)
        {
            if(string.IsNullOrWhiteSpace(origin)||origin!=origin.Trim()||!Uri.TryCreate(origin,UriKind.Absolute,out var uri)
                ||(uri.Scheme!="http"&&uri.Scheme!="https")||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0
                ||uri.AbsolutePath!="/"||uri.Host.Length==0||uri.Host.Contains('*')||uri.HostNameType==UriHostNameType.Unknown)
                throw new ArgumentException("WORKSHOP_PUBLIC_ORIGIN must be one http(s) origin without credentials, path, query, fragment or wildcard.");
            identity=new UriBuilder(uri.Scheme,uri.IdnHost,uri.IsDefaultPort?-1:uri.Port).Uri;
        }
        if(!IPAddress.IsLoopback(address)&&identity==null)
            throw new ArgumentException("Non-loopback binding requires WORKSHOP_PUBLIC_ORIGIN.");
        return new(address,port,identity,capacity);
    }

    static int ReadInteger(string value,int fallback,int minimum,int maximum,string name)
    {
        if(value==null)return fallback;
        if(value.Length==0||!value.All(c=>c>='0'&&c<='9')||!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out int result)
            ||result<minimum||result>maximum)throw new ArgumentException(name+" is outside the supported integer range.");
        return result;
    }

    static bool SameHost(string left,string right)
    {
        left=left.Trim('[',']');right=right.Trim('[',']');
        if(IPAddress.TryParse(left,out var a)&&IPAddress.TryParse(right,out var b))return a.Equals(b);
        return string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
    }

    public bool AcceptsHost(HostString host)
    {
        if(!host.HasValue)return false;
        try
        {
            string name=host.Host;
            int defaultPort=publicUri?.Scheme=="https"?443:80;
            int port=host.Port??defaultPort;
            if(publicUri!=null)return port==publicUri.Port&&SameHost(name,publicUri.IdnHost);
            return port==Port&&(SameHost(name,"localhost")||SameHost(name,"127.0.0.1")||SameHost(name,BindAddress.ToString()));
        }
        catch(FormatException){return false;}
    }

    public bool AcceptsOrigin(HostString host,string origin)
    {
        // Non-browser clients may omit Origin. A present value is never a
        // wildcard, a comma-separated list, "null", or a forwarded identity.
        if(origin.Length==0)return true;
        if(PublicOrigin!=null)return string.Equals(origin,PublicOrigin,StringComparison.Ordinal);
        return string.Equals(origin,"http://"+host.ToUriComponent(),StringComparison.Ordinal);
    }
}

/// <summary>Single admission boundary before endpoint body binding and serialization.</summary>
public sealed class ServerPolicy : IDisposable
{
    readonly ServerOptions options;
    readonly SemaphoreSlim heavyRequests;
    public ServerPolicy(ServerOptions options)
    {
        this.options=options??throw new ArgumentNullException(nameof(options));
        heavyRequests=new SemaphoreSlim(options.MaximumConcurrentRequests,options.MaximumConcurrentRequests);
    }
    public async Task InvokeAsync(HttpContext context,RequestDelegate next)
    {
        context.Response.Headers["X-Content-Type-Options"]="nosniff";
        context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'";
        if(!options.AcceptsHost(context.Request.Host)){await Error(context,400,"Host does not match this Workshop deployment.");return;}
        bool post=HttpMethods.IsPost(context.Request.Method);
        if(post&&(!options.AcceptsOrigin(context.Request.Host,context.Request.Headers.Origin.ToString())||!context.Request.HasJsonContentType()))
        {await Error(context,400,"Same-origin JSON requests required.");return;}
        string path=context.Request.Path.Value?.TrimEnd('/');
        bool heavy=post&&(string.Equals(path,"/api/analyze",StringComparison.OrdinalIgnoreCase)||string.Equals(path,"/api/replay",StringComparison.OrdinalIgnoreCase));
        bool acquired=false;
        if(heavy)
        {
            acquired=heavyRequests.Wait(0);
            if(!acquired){context.Response.Headers["Retry-After"]="1";await Error(context,429,"Workshop is busy. Retry shortly.");return;}
        }
        try
        {
            try{await next(context);}
            catch(ArgumentException e) when(!context.Response.HasStarted){await Error(context,400,e.Message);}
        }
        finally{if(acquired)heavyRequests.Release();}
    }
    static async Task Error(HttpContext context,int status,string message)
    {context.Response.StatusCode=status;await context.Response.WriteAsJsonAsync(new{error=message},context.RequestAborted);}
    public void Dispose()=>heavyRequests.Dispose();
}
