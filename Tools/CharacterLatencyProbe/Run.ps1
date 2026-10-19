param([string]$ReportName = 'report.json')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourcePath = Join-Path $projectRoot 'Assets/Scripts/Presentation/FlipRaceGame.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
$match = [regex]::Match($source,'(?m)^\s*void SelectCharacter\s*\([^)]*\)\s*\{')
if (!$match.Success) { throw 'SelectCharacter seam not found' }
$brace = $source.IndexOf('{',$match.Index)
$depth = 1
for ($end=$brace+1; $end -lt $source.Length -and $depth -gt 0; $end++) {
    if ($source[$end] -eq '{') { $depth++ }
    if ($source[$end] -eq '}') { $depth-- }
}
$method = $source.Substring($match.Index,$end-$match.Index)
if (!$method.Contains('RaceSimulation.SetCharacter(Game, slot, hero);')) { throw 'State seam changed' }
if (!$method.Contains('MascotArtwork.GetFrame(hero, slot, animationTime, false, false, false);')) { throw 'Frame seam changed' }
$method = $method.Replace('RaceSimulation.SetCharacter(Game, slot, hero);','RaceSimulation.SetCharacter(Game, slot, hero); MarkState();')
$method = $method.Replace('MascotArtwork.GetFrame(hero, slot, animationTime, false, false, false);','MascotArtwork.GetFrame(hero, slot, animationTime, false, false, false); MarkFrameReady();')
$generated = 'using FlipRace.Core; using FlipRace.Presentation; using UnityEngine; public sealed partial class LatencyProbeUi {' + $method + '}'
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'ExtractedSelector.g.cs') -Value $generated -Encoding utf8
$hashes = @($sourcePath,(Join-Path $projectRoot 'Assets/Scripts/Presentation/MascotArtwork.cs'),(Join-Path $projectRoot 'Assets/Scripts/Presentation/CharacterRoster.cs'),(Join-Path $projectRoot 'Assets/Scripts/Core/RaceSimulation.cs')) | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 | Select-Object Path,Hash }
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($ReportName + '.sources.json')) -Encoding utf8
& 'C:\Program Files\dotnet\dotnet.exe' run --project (Join-Path $PSScriptRoot 'CharacterLatencyProbe.csproj') -c Release -- (Join-Path $PSScriptRoot $ReportName)
exit $LASTEXITCODE
