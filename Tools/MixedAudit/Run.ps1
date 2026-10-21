param([ValidateSet('prepare','stage','pulses','held','joint','recovery','full-prepare','full')][string]$Scope = 'prepare')
$ErrorActionPreference = 'Stop'
[System.Globalization.CultureInfo]::CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/Scripts/Core') -Filter '*.cs' | ForEach-Object FullName)
$sources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
Add-Type -Path $sources
if ($Scope -eq 'stage') { Write-Output ([FlipRace.MixedAudit.StageAudit]::RunAll()) }
elseif ($Scope -eq 'pulses') { Write-Output ([FlipRace.MixedAudit.StageAudit]::RunPulses()) }
elseif ($Scope -eq 'held') { Write-Output ([FlipRace.MixedAudit.StageAudit]::HeldSmall()) }
elseif ($Scope -eq 'joint') { Write-Output ([FlipRace.MixedAudit.StageAudit]::JointSmall()) }
elseif ($Scope -eq 'recovery') { Write-Output ([FlipRace.MixedAudit.StageAudit]::Recovery()) }
elseif ($Scope -eq 'full-prepare') { Write-Output ([FlipRace.MixedAudit.FullAudit]::Preparation()) }
elseif ($Scope -eq 'full') { [FlipRace.MixedAudit.FullAudit]::RunAll() | ConvertTo-Json -Depth 10 }
else { Write-Output ([FlipRace.MixedAudit.Adapter]::Status) }
