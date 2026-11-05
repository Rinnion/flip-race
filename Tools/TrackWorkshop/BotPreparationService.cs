using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using FlipRace.Core;

namespace TrackWorkshop;

public sealed record PrepareBotRequest(string Json,string[] Profiles=null,double[] Inputs=null,string OutputTrackId=null,double WarningDuration=.8);
public sealed record PreparedProfileResult(string ProfileId,string Status,string Message,double[] Inputs,string[] RewardIds,double? FinishTime,int ExpandedStates,long ElapsedMilliseconds);
public sealed record PrepareBotResult(string Status,string PreparedJson,string OutputTrackId,PreparedProfileResult[] Profiles,string Message);

/// <summary>Request-local search and real Core admission. Never installs the static game catalogue.</summary>
public sealed class BotPreparationService
{
    readonly HashSet<string> builtInIds;
    public BotPreparationService(IEnumerable<string> builtInIds)=>this.builtInIds=new(builtInIds??Array.Empty<string>(),StringComparer.Ordinal);

    public PrepareBotResult Prepare(PrepareBotRequest request,CancellationToken cancellation=default)
    {
        if(request?.Json==null||Encoding.UTF8.GetByteCount(request.Json)>TrackJson.MaximumBytes)throw new ArgumentException("Нужен JSON не больше 2 МиБ.");
        if(!new[]{.4,.6,.8,1,1.2}.Contains(request.WarningDuration))throw new ArgumentException("Недопустимая длительность предупреждения.");
        if(request.Inputs!=null)
        {
            if(request.Inputs.Length>4096)throw new ArgumentException("Нужно не больше 4096 нажатий-подсказок.");
            double prior=-1;foreach(double x in request.Inputs){if(!double.IsFinite(x)||x<0||x<=prior)throw new ArgumentException("Подсказка — возрастающие координаты X, не секунды.");prior=x;}
        }
        var package=ReadGeometry(request.Json,out var seedReferences);
        string[] profiles=request.Profiles??(package.ProfilePolicy==TrackProfilePolicy.ChosenOnly?new[]{"Chosen"}:new[]{"Chosen","FastFlip"});
        if(profiles.Length<1||profiles.Length>2||profiles.Distinct(StringComparer.Ordinal).Count()!=profiles.Length||profiles.Any(p=>p!="Chosen"&&p!="FastFlip"))
            throw new ArgumentException("profiles должен содержать Chosen и/или FastFlip без повторов.");
        string outputId=request.OutputTrackId;
        if(outputId==null)outputId=Reserved(package.Track.Id)?"custom-"+package.Track.Id:package.Track.Id;
        if(Reserved(outputId))throw new ArgumentException("ID занят штатной трассой или зарезервирован. Задайте новый outputTrackId для импорта.");
        package.Track.Id=outputId;
        package=new TrackPackage(package.Track,package.ProfilePolicy,package.WarningMode);
        // Authoritative ID/geometry normalization and bounds, after copy ID and
        // before computing any reference fingerprint or proof.
        if(!TrackJson.TryRead(TrackJson.Write(package),out package,out string normalizedError))throw new ArgumentException(normalizedError);
        var ready=new List<TrackBotReference>();var reports=new List<PreparedProfileResult>();
        foreach(string profile in profiles)
        {
            cancellation.ThrowIfCancellationRequested();
            var watch=Stopwatch.StartNew();var flight=profile=="Chosen"?FlightPresets.Chosen:FlightPresets.FastFlip;
            string unsupported=Unsupported(package,flight);
            if(unsupported!=null){reports.Add(new(profile,"unsupported",unsupported,Array.Empty<double>(),Array.Empty<string>(),null,0,watch.ElapsedMilliseconds));continue;}
            var hints=new List<double[]>();if(request.Inputs?.Length>0)hints.Add(request.Inputs);
            hints.AddRange(seedReferences.Where(r=>r.ProfileId==profile).Select(r=>r.Inputs));
            var found=BotPreparationSearch.Find(package.Track,flight,request.WarningDuration,new(MaximumStates:180000,MaximumMilliseconds:8000),cancellation,hints);
            cancellation.ThrowIfCancellationRequested();
            if(!found.Found){reports.Add(new(profile,"not-found",found.Message,found.Inputs,found.RewardIds,null,found.ExpandedStates,watch.ElapsedMilliseconds));continue;}
            double gain=(found.RewardIds.Length-1)*RaceConfig.BoostTimeGain+RaceConfig.FinishBoostDuration*(RaceConfig.FinishBoostSpeed/RaceConfig.Speed-1);
            var reference=new TrackBotReference(profile,package.ContentHash,found.Inputs,found.RewardIds,gain);
            var candidate=new TrackPackage(package.Track,package.ProfilePolicy,package.WarningMode,new[]{reference});
            var proof=new PreparedReferenceProof(candidate,flight);
            int proofSlices=0;
            while(!proof.Complete&&watch.ElapsedMilliseconds<20000&&proofSlices<100000)
            {
                cancellation.ThrowIfCancellationRequested();proof.Step(8);proofSlices+=8;
            }
            cancellation.ThrowIfCancellationRequested();
            if(!proof.Success)
            {
                string reason=proof.Complete?proof.Failure:"Истёк конечный бюджет проверки маршрута и Мастера.";
                reports.Add(new(profile,"not-found",reason+" Другой маршрут может пройти; непроходимость не доказана.",found.Inputs,found.RewardIds,found.FinishTime,found.ExpandedStates,watch.ElapsedMilliseconds));continue;
            }
            ready.Add(reference);
            reports.Add(new(profile,"ready","Чистый маршрут и настоящий Мастер проверены"+(package.WarningMode==TrackWarningMode.Selected&&package.Track.Obstacles.Any(o=>o.Hidden)?" при всех пяти предупреждениях.":".")+" Оптимальность не утверждается.",
                found.Inputs,found.RewardIds,found.FinishTime,found.ExpandedStates,watch.ElapsedMilliseconds));
        }
        string prepared=null;
        if(ready.Count>0)prepared=TrackJson.Write(new TrackPackage(package.Track,package.ProfilePolicy,package.WarningMode,ready.ToArray()));
        return new(prepared!=null?"prepared":"not-found",prepared,outputId,reports.ToArray(),
            prepared!=null?"Подготовлен отдельный JSON для импорта. BOT доступен только для профилей со статусом ready; исходный черновик не изменён.":"Проверенный эталон не подготовлен. Трасса остаётся доступной для людей; неуспех ограниченного поиска не доказывает непроходимость.");
    }
    bool Reserved(string id)=>builtInIds.Contains(id)||TrackCatalog.IsReservedImportId(id);
    static string Unsupported(TrackPackage package,FlightSettings flight)
    {
        if(!package.SupportsProfile(flight))return "Профиль запрещён этой трассой (chosen-only).";
        if(package.Track.Obstacles.Length==0)return "Для текущей модели бота требуется хотя бы одно препятствие (C > 0).";
        if(package.Track.RiskZones.Count(z=>z.IsFinish)!=1)return "Для текущей модели бота нужен один финишный рывок с полным эффектом.";
        if(package.Track.Length/RaceConfig.FinishBoostSpeed>120)return "Трасса выходит за предел 120 секунд подготовки даже при максимальной скорости.";
        return null;
    }
    static TrackPackage ReadGeometry(string json,out TrackBotReference[] seeds)
    {
        if(TrackJson.TryRead(json,out var original,out string error))
        {seeds=original.References;return new TrackPackage(original.Track,original.ProfilePolicy,original.WarningMode);}
        if(!error.StartsWith("$.botReferences",StringComparison.Ordinal))throw new ArgumentException(error);
        // Strict parser has already excluded syntax/duplicate-key/geometry
        // failures. A stale copied reference may still supply an optional hint.
        JsonObject root;
        try{root=JsonNode.Parse(json,documentOptions:new(){MaxDepth=32}) as JsonObject;}catch(System.Text.Json.JsonException){throw new ArgumentException(error);}
        if(root==null)throw new ArgumentException(error);
        var hints=new List<TrackBotReference>();
        if(root["botReferences"] is JsonArray refs)
        foreach(var item in refs)
        {
            try
            {
                string profile=item?["profileId"]?.GetValue<string>();
                if((profile!="Chosen"&&profile!="FastFlip")||item["inputs"] is not JsonArray inputs||inputs.Count>4096||inputs.Any(n=>n==null))continue;
                var values=inputs.Select(n=>n.GetValue<double>()).ToArray();
                if(values.Any(x=>!double.IsFinite(x)))continue;
                hints.Add(new(profile,"",values,Array.Empty<string>(),0));
            }catch(InvalidOperationException){}catch(FormatException){}
        }
        root["botReferences"]=new JsonArray();
        if(!TrackJson.TryRead(root.ToJsonString(),out var package,out _))throw new ArgumentException(error);
        seeds=hints.ToArray();return package;
    }
}
