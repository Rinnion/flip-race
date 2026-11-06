$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$regression = & (Join-Path $projectRoot 'Tools/Iter49Ui/Run.ps1') | ConvertFrom-Json
if ($regression.status -ne 'PASS') { throw 'Presentation regression failed' }
& dotnet run --project (Join-Path $PSScriptRoot 'SurfaceRunUiCheck.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Surface run UI check failed' }
