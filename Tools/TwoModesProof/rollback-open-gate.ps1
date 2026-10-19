$ErrorActionPreference = 'Stop'
$proofRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$proofTemp = Join-Path ([IO.Path]::GetTempPath()) ('flip-modes-gate-rollback-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $proofTemp | Out-Null
$proofCore = Join-Path $proofRepo 'Assets/Scripts/Core'
$proofSource = [IO.File]::ReadAllText((Join-Path $proofCore 'RaceSimulation.cs'))
$proofPattern = '(?m)^[ \t]*if \(\(band == 0 && obstacle\.GapTop <= 0\)\r?\n[ \t]*\|\| \(band == 1 && obstacle\.GapBottom >= RaceConfig\.CorridorHeight\)\) continue;\r?\n'
if ([regex]::Matches($proofSource, $proofPattern).Count -ne 1) { throw 'Expected exactly one open-half guard to roll back.' }
[IO.File]::WriteAllText((Join-Path $proofTemp 'RaceSimulation.cs'), [regex]::Replace($proofSource, $proofPattern, ''))
$proofTests = Join-Path $proofRepo 'Assets/Editor/EngineModesTests.cs'
$proofProject = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Include="$proofCore/*.cs" Exclude="$proofCore/RaceSimulation.cs"/><Compile Include="RaceSimulation.cs"/><Compile Include="$proofTests"/><Compile Include="Program.cs"/></ItemGroup></Project>
"@
[IO.File]::WriteAllText((Join-Path $proofTemp 'Rollback.csproj'), $proofProject)
[IO.File]::WriteAllText((Join-Path $proofTemp 'Program.cs'), @'
using System;
class Program {
    static int Main() {
        try { Console.WriteLine(FlipRace.Editor.EngineModesTests.RunOpenGateRegression()); return 0; }
        catch(Exception e) { Console.WriteLine("EXPECTED RED: " + e.Message); return 7; }
    }
}
'@)
$proofOutput = & dotnet run --project (Join-Path $proofTemp 'Rollback.csproj') 2>&1
$proofExit = $LASTEXITCODE
$proofOutput | ForEach-Object { Write-Output $_ }
if ($proofExit -ne 7 -or ($proofOutput -join "`n") -notmatch 'absent-half collision') { throw 'Rollback did not produce the expected open-gate RED.' }
Write-Output 'PASS: removing only the open-half guard reproduces the real wall-run collision. Production sources were not changed.'
