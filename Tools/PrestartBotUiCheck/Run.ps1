param([switch]$CharacterRoster)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Get-SourceSnapshot {
    foreach ($directory in @('Assets/Scripts/Core','Assets/Scripts/Presentation','Assets/Scripts/Web')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -Filter '*.cs' | Sort-Object Name) {
            [ordered]@{path=($directory+'/'+$file.Name);sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
        }
    }
}
$sourceSnapshot = @(Get-SourceSnapshot)
$generated = Join-Path $PSScriptRoot 'obj/managed-source'
[IO.Directory]::CreateDirectory($generated) | Out-Null
foreach ($name in @('FlipRaceGame.cs','FlightSettingsMenu.cs','PrestartBotCache.cs','BotsPresentation.cs')) {
    $source = [IO.File]::ReadAllText((Join-Path $projectRoot ('Assets/Scripts/Presentation/' + $name)))
    $aliases = "using PlayerPrefs = FlipRace.SharedScreenCheck.Interop.Prefs;`nusing Time = FlipRace.SharedScreenCheck.Interop.Clock;`n"
    if ($name -eq 'FlipRaceGame.cs') {
        $aliases += "using Screen = FlipRace.SharedScreenCheck.Interop.Display;`nusing Input = FlipRace.SharedScreenCheck.Interop.Devices;`n"
        $seams = @{
            'FlipRace.Web.FlipDiagnostics.FlipPixelRatio()' = 'FlipRace.SharedScreenCheck.Interop.Display.PixelRatio'
            'MascotArtwork.GetFrame(hero, slot, animationTime, false, false, false)' = 'FlipRace.SharedScreenCheck.Interop.MascotTextures.GetFrame(hero, slot, animationTime, false, false, false)'
        }
        foreach ($native in $seams.Keys) {
            if (($source.Split($native).Length - 1) -ne 1) { throw ('Unexpected native seam count: ' + $native) }
            $source = $source.Replace($native, $seams[$native])
        }
    }
    [IO.File]::WriteAllText((Join-Path $generated $name), $aliases + $source, [Text.UTF8Encoding]::new($false))
}
$arguments = @($projectRoot)
if ($CharacterRoster) { $arguments += '--character-roster' }
$result = & dotnet run --project (Join-Path $PSScriptRoot 'PrestartBotUiCheck.csproj') -- @arguments
if ($LASTEXITCODE -ne 0) { $result; throw 'Pre-start bot UI check failed' }
if (($sourceSnapshot | ConvertTo-Json -Compress) -cne (@(Get-SourceSnapshot) | ConvertTo-Json -Compress)) {
    throw 'Production source changed during run'
}
$report = $result | ConvertFrom-Json
$report | Add-Member -NotePropertyName sourceSnapshot -NotePropertyValue $sourceSnapshot
$report | ConvertTo-Json -Depth 8
