param([string]$Baseline = '39097d0')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$relative = 'Assets/Scripts/Presentation/MascotArtwork.cs'
$before = (& git -C $projectRoot show ($Baseline + ':' + $relative)) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read baseline git blob' }
$after = Get-Content -LiteralPath (Join-Path $projectRoot $relative) -Raw
foreach ($source in @($before,$after)) {
    if ([regex]::Matches($source,'public static class MascotArtwork').Count -ne 1) { throw 'Unexpected mascot declaration' }
}
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Baseline.g.cs') -Value $before.Replace('public static class MascotArtwork','public static class BaselineMascot').TrimEnd() -Encoding utf8
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Current.g.cs') -Value $after.Replace('public static class MascotArtwork','public static class CurrentMascot').TrimEnd() -Encoding utf8
$sources = @{ BaselineCommit=(& git -C $projectRoot rev-parse $Baseline); CurrentSource=(Get-FileHash -LiteralPath (Join-Path $projectRoot $relative) -Algorithm SHA256).Hash; RosterSource=(Get-FileHash -LiteralPath (Join-Path $projectRoot 'Assets/Scripts/Presentation/CharacterRoster.cs') -Algorithm SHA256).Hash }
$sources | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'sources.json') -Encoding utf8
& 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $PSScriptRoot 'MascotPoseCheck.csproj') -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$dll=Join-Path $PSScriptRoot 'bin/Release/net10.0/MascotPoseCheck.dll'
foreach ($mode in @('perf-old','perf-new','compare')) {
    & 'C:\Program Files\dotnet\dotnet.exe' $dll $mode (Join-Path $PSScriptRoot ($mode+'.json'))
    if ($LASTEXITCODE -ne 0) { throw ($mode+' failed') }
}
