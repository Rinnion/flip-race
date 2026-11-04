$ErrorActionPreference = 'Stop'
$workshopRoot = $PSScriptRoot
Write-Host 'Track Workshop 0.2 - http://127.0.0.1:4186/'
Write-Host 'Keep this window open. Press Ctrl+C to stop.'
if (Test-Path -LiteralPath (Join-Path $workshopRoot 'TrackWorkshop.dll')) {
    & dotnet (Join-Path $workshopRoot 'TrackWorkshop.dll')
} else {
    & dotnet run --project (Join-Path $workshopRoot 'TrackWorkshop.csproj') --no-launch-profile
}
exit $LASTEXITCODE
