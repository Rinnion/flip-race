param([Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Tag)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $projectRoot
try {
    foreach ($mode in @('', '--parallel', '--recovery', '--entrances')) {
        $probeArgs = @('run', '--project', 'Tools/DenseChainsProbe/DenseChainsProbe.csproj', '-c', 'Release', '--', '--replay', "--tag=-$Tag")
        if ($mode) { $probeArgs += $mode }
        & dotnet @probeArgs
        if ($LASTEXITCODE -ne 0) { throw "Probe failed: $mode (exit $LASTEXITCODE)" }
    }
} finally { Pop-Location }
