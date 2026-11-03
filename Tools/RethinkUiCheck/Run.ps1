param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$snapshot = @()
foreach ($directory in @('Assets/Scripts/Core','Assets/Scripts/Presentation','Assets/Scripts/Web')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -Filter '*.cs' | Sort-Object Name) {
        $snapshot += [ordered]@{path=($directory+'/'+$file.Name);sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    }
}
# Reuse exactly the existing native environment transforms; no new body substitutions.
& (Join-Path $projectRoot 'Tools/HiddenUiCheck/Run.ps1') -BuildOnly | Out-Null
$project = Join-Path $PSScriptRoot 'RethinkUiCheck.csproj'
if ($BuildOnly) { & dotnet build $project --nologo; if ($LASTEXITCODE -ne 0) { throw 'Rethink UI compile failed' }; return }
$result = & dotnet run --project $project
if ($LASTEXITCODE -ne 0) { Write-Output $result; throw 'Rethink UI check failed' }
foreach ($entry in $snapshot) {
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256) { throw ('Production source changed during run: '+$entry.path) }
}
$report = $result | ConvertFrom-Json
$report | Add-Member -NotePropertyName sourceSnapshot -NotePropertyValue $snapshot
$report | Add-Member -NotePropertyName checkedAt -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
$report | ConvertTo-Json -Depth 12
