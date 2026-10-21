param()
$ErrorActionPreference = 'Stop'
[System.Globalization.CultureInfo]::CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/Scripts/Core') -Filter '*.cs' | ForEach-Object FullName)
$sources += Join-Path $projectRoot 'Assets/Scripts/Presentation/SharedFieldView.cs'
$sources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
Add-Type -Path $sources
[FlipRace.SharedScreenCheck.Audit]::Run() | ConvertTo-Json -Depth 12
