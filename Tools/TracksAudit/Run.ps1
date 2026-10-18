$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$auditSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/Scripts/Core') -Filter '*.cs' | ForEach-Object FullName)
$auditSources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
Add-Type -Path $auditSources
[TracksIndependentAudit]::Run()
