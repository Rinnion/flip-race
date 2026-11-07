$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$regression = & (Join-Path $projectRoot 'Tools/Iter49Ui/Run.ps1') | ConvertFrom-Json
if ($regression.status -ne 'PASS') { throw 'Presentation regression failed' }
$botSource = Join-Path $projectRoot 'Assets/Scripts/Presentation/BotsPresentation.cs'
$generated = Join-Path $PSScriptRoot 'obj/managed-source'
[IO.Directory]::CreateDirectory($generated) | Out-Null
$clockAlias = "using Time = FlipRace.SharedScreenCheck.Interop.Clock;`n"
[IO.File]::WriteAllText((Join-Path $generated 'BotsPresentation.cs'), $clockAlias + [IO.File]::ReadAllText($botSource))
$result = & dotnet run --project (Join-Path $PSScriptRoot 'SurfaceRunUiCheck.csproj')
if ($LASTEXITCODE -ne 0) { Write-Output $result; throw 'Surface run UI check failed' }
foreach ($entry in $regression.sourceSnapshot) {
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw ('Production source changed during run: ' + $entry.path)
    }
}
$result
