param([ValidateSet('analytics','after','late-tail','bound','stage','gates','full','recovery','windows')][string]$Scope='analytics')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sources=@(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/Scripts/Core') -Filter '*.cs' | ForEach-Object FullName)
$sources+=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
Add-Type -Path $sources
[TwoModesIndependentAudit]::Run($Scope)
