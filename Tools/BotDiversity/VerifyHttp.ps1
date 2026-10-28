param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$ProjectRoot = 'F:/Home/Game/flip-race-unity',
    [string]$ManifestPath = 'Verification/bot-diversity-unpacked-manifest.json',
    [string]$OutputName = 'bot-diversity-http-candidate.json',
    [string]$ExpectedVersion = '5.2.1-trial'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
if ($OutputName -notmatch '^bot-diversity-http(?:-[A-Za-z0-9_-]+)?\.json$') { throw 'Output must be an owned bot-diversity-http*.json filename.' }
$manifestFile = [IO.Path]::GetFullPath((Join-Path $project $ManifestPath))
if (-not $manifestFile.StartsWith($project + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest escapes project.' }
$base = [Uri]::new($BaseUrl.TrimEnd('/') + '/', [UriKind]::Absolute)
if ($base.Scheme -notin @('http','https') -or -not $base.IsLoopback -or $base.Query -or $base.Fragment) { throw 'Expected a loopback HTTP(S) base URL without query/fragment.' }
$manifestHash = (Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if ($manifest.fileCount -ne 5 -or @($manifest.files).Count -ne 5 -or @($manifest.files | Where-Object path -CEQ 'index.html').Count -ne 1) { throw 'Expected the five-file WebGL manifest with root index.html.' }
$output = Join-Path $project ('Verification/' + $OutputName)
$rows = [Collections.Generic.List[object]]::new()
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AutomaticDecompression = [Net.DecompressionMethods]::None
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$client.DefaultRequestHeaders.CacheControl = [Net.Http.Headers.CacheControlHeaderValue]::new()
$client.DefaultRequestHeaders.CacheControl.NoCache = $true
$client.DefaultRequestHeaders.Pragma.ParseAdd('no-cache')
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$version = $null
try {
    foreach ($file in $manifest.files) {
        $relative = [string]$file.path
        if ($relative.Contains('\') -or $relative.Contains(':') -or [IO.Path]::IsPathRooted($relative) -or @($relative.Split('/') | Where-Object { $_ -eq '.' -or $_ -eq '..' -or $_ -eq '' }).Count -gt 0 -or -not $seen.Add($relative)) { throw 'Nonportable or duplicate manifest path.' }
        $escaped = ($relative.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
        $uri = [Uri]::new($base, $escaped + '?verify=' + [Guid]::NewGuid().ToString('N'))
        $response = $client.GetAsync($uri).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne 200) { throw "Unexpected HTTP status for ${relative}: $([int]$response.StatusCode)" }
            [byte[]]$bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            $sha = [Security.Cryptography.SHA256]::HashData($bytes)
            $actual = [Convert]::ToHexString($sha).ToLowerInvariant()
            $matches = $actual -ceq [string]$file.sha256 -and $bytes.LongLength -eq [long]$file.bytes
            $rows.Add([ordered]@{path=$relative;url=$uri.AbsoluteUri;httpStatus=[int]$response.StatusCode;bytes=$bytes.LongLength;expectedBytes=[long]$file.bytes;sha256=$actual;expectedSha256=[string]$file.sha256;matchesManifest=$matches})
            if (-not $matches) { throw "HTTP content differs from verified manifest: $relative" }
            if ($relative -ceq 'index.html') {
                $html = [Text.Encoding]::UTF8.GetString($bytes)
                $match = [regex]::Match($html,'productVersion:\s*"([^"]+)"')
                if (-not $match.Success) { throw 'HTTP index has no runtime productVersion.' }
                $version = $match.Groups[1].Value
                if ($version -cne $ExpectedVersion) { throw "Served version $version differs from $ExpectedVersion." }
            }
        } finally { $response.Dispose() }
    }
    if ((Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifestHash) { throw 'Manifest changed during HTTP verification.' }
    [ordered]@{checkedAt=[DateTime]::UtcNow.ToString('o');status='PASS';baseUrl=$base.AbsoluteUri;expectedVersion=$ExpectedVersion;actualVersion=$version;manifest=$manifestFile;manifestSha256=$manifestHash;fileCount=$rows.Count;files=$rows.ToArray();allServedFilesMatchManifest=$true;automaticHttpDecompression=$false;serverStartedByCheck=$false;noPublicUpload=$true} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8
    Write-Output "PASS: served $version; all five HTTP file hashes/lengths match manifest. Report $output"
} catch {
    [ordered]@{checkedAt=[DateTime]::UtcNow.ToString('o');status='FAIL';baseUrl=$base.AbsoluteUri;expectedVersion=$ExpectedVersion;actualVersion=$version;manifest=$manifestFile;manifestSha256=$manifestHash;error=$_.Exception.Message;files=$rows.ToArray();serverStartedByCheck=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8
    throw
} finally { $client.Dispose(); $handler.Dispose() }