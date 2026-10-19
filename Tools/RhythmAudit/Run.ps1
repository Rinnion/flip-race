param([ValidateSet('stage','bypass','stage-speeds','recovery','geometry','recovery-red','recovery-map','recovery-pitch','parts','two-band-penalty','authored-contacts','compact-recovery','compact-proposed','family-stage','family-recovery','restored-flow','flow-recovery','modules','modules-compact','courses-draft','courses-bypass','courses-before-recovery','courses-recovery','courses-before-red','before400-recovery','courses-final','courses-duels','courses-representatives','courses-recovery-candidate','courses-recovery-spacing','courses-recovery-replay','courses-recovery-expanded')][string]$Scope='stage',[ValidatePattern('^[a-zA-Z0-9_.-]+[.]json$')][string]$CourseFile='rhythm-course-r2.json',[string]$RecoverySeedFile='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sources=@(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/Scripts/Core') -Filter '*.cs' | ForEach-Object FullName)
$sources+=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
Add-Type -Path $sources
[RhythmIndependentAudit]::ProjectRoot=$projectRoot
[RhythmIndependentAudit]::CourseFile=$CourseFile
[RhythmIndependentAudit]::RecoverySeedFile=$RecoverySeedFile
[RhythmIndependentAudit]::Run($Scope)
