$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '../..')
try {
    $revision = 'ef4873cfd55219790dd47f66cab3dd2428716fad'
    $presentation = 'Assets/Scripts/Presentation'
    $mascot = git -c core.quotepath=false show "${revision}:$presentation/MascotArtwork.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Could not load baseline artwork from Git.' }
    $roster = git -c core.quotepath=false show "${revision}:$presentation/CharacterRoster.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Could not load baseline roster from Git.' }
    # Isolate the baseline roster from current naming, normalization and count.
    $rosterText = ($roster -join "`n").Replace('public enum MascotReaction { None, Success, Hit, Win }', '').Replace('RaceConfig.CharacterCount', '6')
    $artText = ($mascot -join "`n").Replace('MascotArtwork', 'BaselineMascotArtwork').Replace('CharacterRoster', 'BaselineRoster')
    $rosterText = $rosterText.Replace('CharacterRoster', 'BaselineRoster')
    $obj = New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'obj')
    $baseline = Join-Path $obj.FullName 'Baseline.g.cs'
    # Each source has top-level using directives; keep both at the top.
    $artText = $artText.Substring($artText.IndexOf('namespace FlipRace.Presentation'))
    ("using System;`nusing System.Collections.Generic;`n" + $rosterText + "`n" + $artText) | Set-Content -LiteralPath $baseline -Encoding utf8
    dotnet run --project Tools/RosterArtworkCheck/RosterArtworkCheck.csproj --configuration Release -- --baseline $revision
    if ($LASTEXITCODE -ne 0) { throw 'Roster artwork verification failed.' }
}
finally {
    Pop-Location
}
