param(
    [string]$ProjectRoot = 'F:/Home/Game/flip-race-unity',
    [string]$BuildRevision = 'iter49-final',
    [string]$UnpackedDirectory = 'Builds/WebGL-v4.9.0-unpacked'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$project = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
$source = Join-Path $project 'Builds/WebGL-v4.9.0-final'
$archive = Join-Path $project 'Releases/flip-race-v4.9.0-trial-webgl.zip'
$manifestPath = $archive + '.manifest.json'
$target = [IO.Path]::GetFullPath((Join-Path $project $UnpackedDirectory))
$allowed = [IO.Path]::GetFullPath((Join-Path $project 'Builds')).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
if (-not $target.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Extraction target escapes project Builds.' }
if (Test-Path -LiteralPath $target) { throw 'Fresh extraction target already exists. No overwrite or deletion performed.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.fileCount -ne 5) { throw 'Expected the five verified public WebGL files.' }
$buildLog = 'Logs/' + $BuildRevision + '-build.log'
$buildLogBody = Get-Content -LiteralPath (Join-Path $project $buildLog) -Raw
$success = [regex]::Match($buildLogBody,'FLIP_BUILD_OK\s+(\d+) bytes at')
if (-not $success.Success -or [long]$success.Groups[1].Value -ne $manifest.totalBytes) { throw 'Build log success / manifest byte count mismatch.' }
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($archiveHash -ne $manifest.archive.sha256) { throw 'Archive differs from package manifest.' }

$zip = [IO.Compression.ZipFile]::OpenRead($archive)
$rows = [Collections.Generic.List[object]]::new()
try {
    if ($zip.Entries.Count -ne 5 -or $null -eq $zip.GetEntry('index.html')) { throw 'ZIP root layout is incorrect.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $zip.Entries) {
        if (-not $seen.Add($entry.FullName)) { throw 'Duplicate ZIP path.' }
        if ($entry.FullName.Contains('\') -or $entry.FullName.Contains(':') -or [IO.Path]::IsPathRooted($entry.FullName)) { throw 'Non-portable ZIP path.' }
        $destination = [IO.Path]::GetFullPath((Join-Path $target $entry.FullName))
        if (-not $destination.StartsWith($target + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'ZIP path escapes extraction target.' }
        $unixType = ($entry.ExternalAttributes -shr 16) -band 0xF000
        if ($unixType -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400) -ne 0) { throw 'ZIP link or reparse entry rejected.' }
        $matches = @($manifest.files | Where-Object { $_.path -ceq $entry.FullName })
        if ($matches.Count -ne 1) { throw 'ZIP path not uniquely present in manifest.' }
        $file = $matches[0]
        $stream = $entry.Open()
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
        finally { $stream.Dispose(); $hasher.Dispose() }
        $original = Join-Path $source $entry.FullName
        $sourceHash = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($sourceHash -ne $entryHash -or $entryHash -ne $file.sha256 -or $entry.Length -ne $file.bytes -or (Get-Item -LiteralPath $original).Length -ne $file.bytes) { throw "Source / ZIP / manifest mismatch: $($entry.FullName)" }
        $rows.Add([ordered]@{path=$entry.FullName;bytes=$entry.Length;sourceSha256=$sourceHash;archiveSha256=$entryHash;manifestSha256=$file.sha256})
    }
} finally { $zip.Dispose() }
if (@(Get-ChildItem -LiteralPath $source -Recurse -File).Count -ne 5) { throw 'Unexpected additional source files.' }

[IO.Compression.ZipFile]::ExtractToDirectory($archive,$target)
if (@(Get-ChildItem -LiteralPath $target -Recurse -File).Count -ne 5) { throw 'Unexpected extraction file count.' }
foreach ($row in $rows) {
    $unpacked = Join-Path $target $row.path
    $unpackedHash = (Get-FileHash -LiteralPath $unpacked -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($unpackedHash -ne $row.sourceSha256 -or (Get-Item -LiteralPath $unpacked).Length -ne $row.bytes) { throw "Extraction mismatch: $($row.path)" }
    $row.unpackedSha256 = $unpackedHash
    $row.allFourMatch = $true
}
$unpackedManifest = Join-Path $project 'Verification/iter49-unpacked-manifest.json'
& node (Join-Path $project 'Tools/verify-build.mjs') $target --json $unpackedManifest
if ($LASTEXITCODE -ne 0) { throw 'Fresh unpacked build verification failed.' }
$unpackedCheck = Get-Content -LiteralPath $unpackedManifest -Raw | ConvertFrom-Json
$html = Get-Content -LiteralPath (Join-Path $target 'index.html') -Raw
if ($html -notmatch 'productVersion:\s*"4\.9\.0-trial"') { throw 'Unexpected runtime product version.' }
$baseline = Join-Path $project 'Releases/flip-race-v4.8.1-trial-webgl.zip'
$baselineHash = (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash.ToLowerInvariant()
if ($baselineHash -ne 'a459e94345dbadeb112a6eac4a1041fba2829e7760127379c1893be90d19f2ef') { throw 'The preserved4.8.1 archive changed.' }
$report = [ordered]@{
    checkedAt=[DateTime]::UtcNow.ToString('o');version='4.9.0-trial';buildRevision=$BuildRevision
    status='PASS static package / fresh extraction verification'
    browserStatus='Separate parent-owned browser check; this script does not start a server or browser.'
    buildLog=$buildLog;sourceDirectory=$source
    archive=$archive;archiveBytes=(Get-Item -LiteralPath $archive).Length;archiveSha256=$archiveHash
    manifest=$manifestPath;manifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    unpackedDirectory=$target;targetDidNotExistBeforeExtraction=$true
    entryPathsValidatedWithinWorkspace=$true;entryReparseLinksRejected=$true;fileCount=5
    webBytes=$manifest.totalBytes;indexAtRoot=$true;runtimeVersionChecked='4.9.0-trial'
    checkedRelativeReferences=$unpackedCheck.checkedReferences;runtime=$unpackedCheck.runtime
    compression=$unpackedCheck.compression;fallbackCodec=$unpackedCheck.fallbackCodec
    wasmHeaderAfterDecompressionVerified=$true;files=$rows.ToArray()
    baseline481=[ordered]@{sha256=$baselineHash;unchanged=$true}
    reproduction="pwsh -File Verification/iter49-package-check.ps1 -BuildRevision $BuildRevision -UnpackedDirectory $UnpackedDirectory (requires a new unpacked target)"
    noPublicUpload=$true
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $project 'Verification/iter49-package-verification.json') -Encoding utf8
Write-Output "PASS: five source / ZIP / manifest / unpacked SHA-256 hashes match. ZIP $archiveHash"
Write-Output "Fresh verified extraction: $target"
