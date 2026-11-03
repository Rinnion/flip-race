param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$snapshot = @()
foreach ($directory in @('Assets/Scripts/Core','Assets/Scripts/Presentation','Assets/Scripts/Web')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -Filter '*.cs' | Sort-Object Name) {
        $snapshot += [ordered]@{path=($directory+'/'+$file.Name);sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    }
}
$generated = Join-Path $PSScriptRoot 'obj/managed-source'
[IO.Directory]::CreateDirectory($generated) | Out-Null
foreach ($name in @('FlipRaceGame.cs','FlightSettingsMenu.cs','BotsPresentation.cs')) {
    $source = [IO.File]::ReadAllText((Join-Path $projectRoot ('Assets/Scripts/Presentation/'+$name)))
    $aliases = "using PlayerPrefs = FlipRace.SharedScreenCheck.Interop.Prefs;`nusing Time = FlipRace.SharedScreenCheck.Interop.Clock;`n"
    if ($name -eq 'FlipRaceGame.cs') {
        $aliases += "using Screen = FlipRace.SharedScreenCheck.Interop.Display;`nusing Input = FlipRace.SharedScreenCheck.Interop.Devices;`n"
        $dpr = 'FlipRace.Web.FlipDiagnostics.FlipPixelRatio()'
        $texture = 'MascotArtwork.GetFrame(hero, slot, animationTime, false, false, false)'
        if (($source.Split($dpr).Length-1) -ne 1 -or ($source.Split($texture).Length-1) -ne 1) { throw 'Native seam changed' }
        $source = $source.Replace($dpr,'FlipRace.SharedScreenCheck.Interop.Display.PixelRatio').Replace($texture,'FlipRace.SharedScreenCheck.Interop.MascotTextures.GetFrame(hero, slot, animationTime, false, false, false)')
    }
    [IO.File]::WriteAllText((Join-Path $generated $name),($aliases+$source).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
}
$project = Join-Path $PSScriptRoot 'HiddenUiCheck.csproj'
if ($BuildOnly) { & dotnet build $project --nologo; if ($LASTEXITCODE -ne 0) { throw 'Hidden UI compile failed' }; return }
$result = & dotnet run --project $project
if ($LASTEXITCODE -ne 0) { Write-Output $result; throw 'Hidden UI check failed' }
foreach ($entry in $snapshot) {
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256) { throw ('Production source changed during run: '+$entry.path) }
}
$report = $result | ConvertFrom-Json
$report | Add-Member -NotePropertyName sourceSnapshot -NotePropertyValue $snapshot
$report | Add-Member -NotePropertyName checkedAt -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
$report | ConvertTo-Json -Depth 12
