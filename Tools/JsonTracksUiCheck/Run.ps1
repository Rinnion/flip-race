param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$snapshot = @()
foreach ($directory in @('Assets/Scripts/Core','Assets/Scripts/Presentation','Assets/Scripts/Web','Assets/StreamingAssets/Tracks')) {
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot $directory))) { if ($BuildOnly) { continue }; throw ('Missing data directory: '+$directory) }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -File | Where-Object { $_.Extension -in @('.cs','.json') } | Sort-Object Name) {
        $snapshot += [ordered]@{path=($directory+'/'+$file.Name);sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    }
}
foreach ($relative in @('Assets/WebGLTemplates/Flip/index.html','Assets/Plugins/WebGL/TrackFiles.jslib','Packages/manifest.json','Packages/packages-lock.json')) {
    $snapshot += [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath (Join-Path $projectRoot $relative) -Algorithm SHA256).Hash}
}
& (Join-Path $projectRoot 'Tools/HiddenUiCheck/Run.ps1') -BuildOnly | Out-Null
$project = Join-Path $PSScriptRoot 'JsonTracksUiCheck.csproj'
if ($BuildOnly) { & dotnet build $project --nologo; if ($LASTEXITCODE -ne 0) { throw 'JSON tracks UI compile failed' }; return }
$bridge = & node (Join-Path $PSScriptRoot 'BridgeCheck.js') $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Track-file browser bridge check failed' }
$result = & dotnet run --project $project -- $projectRoot
if ($LASTEXITCODE -ne 0) { Write-Output $result; throw 'JSON tracks UI check failed' }
foreach ($entry in $snapshot) {
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256) { throw ('Source changed during run: '+$entry.path) }
}
$report = $result | ConvertFrom-Json
$report | Add-Member -NotePropertyName bridge -NotePropertyValue ($bridge | ConvertFrom-Json)
$report | Add-Member -NotePropertyName sourceSnapshot -NotePropertyValue $snapshot
$report | Add-Member -NotePropertyName checkedAt -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
$report | ConvertTo-Json -Depth 12
