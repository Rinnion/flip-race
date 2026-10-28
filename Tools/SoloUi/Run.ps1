$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceSnapshot = @()
foreach ($directory in @('Assets/Scripts/Core','Assets/Scripts/Presentation','Assets/Scripts/Web')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -Filter '*.cs' | Sort-Object Name) {
        $sourceSnapshot += [ordered]@{path=($directory+'/'+$file.Name);sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    }
}
$generated = Join-Path $PSScriptRoot 'obj/managed-source'
[IO.Directory]::CreateDirectory($generated) | Out-Null
$metadata = @()
foreach ($name in @('FlipRaceGame.cs', 'FlightSettingsMenu.cs')) {
    $path = Join-Path $projectRoot ('Assets/Scripts/Presentation/' + $name)
    $source = [IO.File]::ReadAllText($path)
    $aliases = "using PlayerPrefs = FlipRace.SharedScreenCheck.Interop.Prefs;`nusing Time = FlipRace.SharedScreenCheck.Interop.Clock;`n"
    if ($name -eq 'FlipRaceGame.cs') {
        $aliases += "using Screen = FlipRace.SharedScreenCheck.Interop.Display;`nusing Input = FlipRace.SharedScreenCheck.Interop.Devices;`n"
        $nativeCall = 'FlipRace.Web.FlipDiagnostics.FlipPixelRatio()'
        if (($source.Split($nativeCall).Length - 1) -ne 1) { throw 'Unexpected DPR seam count' }
        $source = $source.Replace($nativeCall, 'FlipRace.SharedScreenCheck.Interop.Display.PixelRatio')
        $textureCall = 'MascotArtwork.GetFrame(hero, slot, animationTime, false, false, false)'
        if (($source.Split($textureCall).Length - 1) -ne 1) { throw 'Unexpected selection texture seam count' }
        $source = $source.Replace($textureCall, 'FlipRace.SharedScreenCheck.Interop.MascotTextures.GetFrame(hero, slot, animationTime, false, false, false)')
    }
    # No method body is replaced. The aliases redirect only native environment
    # dependencies, plus DPR and selection's native texture acquisition above.
    [IO.File]::WriteAllText((Join-Path $generated $name),($aliases + $source).Replace("`r`n","`n").TrimEnd()+"`n",[Text.UTF8Encoding]::new($false))
    $metadata += [ordered]@{path=('Assets/Scripts/Presentation/'+$name);sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
$result = & dotnet run --project (Join-Path $PSScriptRoot 'SoloUi.csproj')
if ($LASTEXITCODE -ne 0) { Write-Output $result; throw 'Solo UI interop check failed' }
foreach ($entry in $metadata) {
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'Production source changed during run' }
}
foreach ($entry in $sourceSnapshot) {
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256) { throw ('Production source changed during run: '+$entry.path) }
}
$report = $result | ConvertFrom-Json
$report | Add-Member -NotePropertyName sourceCopies -NotePropertyValue $metadata
$report | Add-Member -NotePropertyName sourceSnapshot -NotePropertyValue $sourceSnapshot
$report | Add-Member -NotePropertyName checkedAt -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
$report | ConvertTo-Json -Depth 12
