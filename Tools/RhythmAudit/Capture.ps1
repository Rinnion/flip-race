param([ValidateSet('stage','bypass','stage-speeds','recovery','geometry','recovery-red','recovery-map','recovery-pitch','parts','two-band-penalty','authored-contacts','compact-recovery','compact-proposed','family-stage','family-recovery','restored-flow','flow-recovery','modules','modules-compact','courses-draft','courses-bypass','courses-before-recovery','courses-recovery','courses-before-red','before400-recovery','courses-final','courses-duels','courses-representatives','courses-recovery-candidate','courses-recovery-spacing','courses-recovery-replay','courses-recovery-expanded')][string]$Scope='stage',[string]$OutputPath,[ValidatePattern('^[a-zA-Z0-9_.-]+[.]json$')][string]$CourseFile='rhythm-course-r2.json',[string]$RecoverySeedFile='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sources=@(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/Scripts/Core') -Filter '*.cs')
$sources+=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs')
$sources+=Get-Item -LiteralPath (Join-Path $PSScriptRoot 'Run.ps1'),(Join-Path $PSScriptRoot 'Capture.ps1')
if($Scope -eq 'modules'){$sources+=Get-Item -LiteralPath (Join-Path $projectRoot 'Verification/rhythm-modules.json')}
if($Scope -eq 'modules-compact'){$sources+=Get-Item -LiteralPath (Join-Path $projectRoot 'Verification/rhythm-modules-compact.json')}
if($Scope.StartsWith('courses-')){$sources+=Get-Item -LiteralPath (Join-Path $projectRoot ('Verification/'+$CourseFile))}
if($Scope -in @('courses-representatives','courses-recovery-candidate','courses-recovery-spacing','courses-recovery-replay')){$sources+=Get-Item -LiteralPath (Join-Path $projectRoot 'Verification/rhythm-r5-recovery-no-found.json')}
if($Scope -eq 'courses-recovery-replay'){$sources+=Get-Item -LiteralPath (Join-Path $projectRoot 'Verification/rhythm-r5-recovery-spacing-candidates.json')}
if($Scope -eq 'before400-recovery'){foreach($n in 'rhythm-modules-before.json','rhythm-modules-before400.json'){$sources+=Get-Item -LiteralPath (Join-Path $projectRoot ('Verification/'+$n))}}
if($RecoverySeedFile){$sources+=Get-Item -LiteralPath (Join-Path $projectRoot ('Verification/'+$RecoverySeedFile))}
$runArgs=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'Run.ps1'),'-Scope',$Scope,'-CourseFile',$CourseFile)
if($RecoverySeedFile){$runArgs+=@('-RecoverySeedFile',$RecoverySeedFile)}
$before=@{}; $lines=@("Independent RHYTHM scope=$Scope", "Captured UTC=$([DateTime]::UtcNow.ToString('o'))")
foreach($file in $sources|Sort-Object FullName){$hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;$before[$file.FullName]=$hash;$lines+="$hash $($file.FullName)"}
$output=@(& (Get-Command pwsh).Source @runArgs 2>&1 | ForEach-Object {
    # Show useful live checkpoints while retaining every output line for the
    # hash-bound capture. Do not wait until a long recovery sweep ends to expose RED.
    if("$_" -match '^(NO_FOUND_ESCAPE|RECOVERY SAMPLE|RECOVERY PROGRESS|CLEAN_SHORTER|BUDGET|PASS|REVIEW REQUIRED)') {[Console]::WriteLine("$_")}
    $_
})
if($LASTEXITCODE -ne 0){$output|ForEach-Object{Write-Output $_};throw 'Audit failed; no PASS capture written'}
foreach($file in $sources){if((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $before[$file.FullName]){throw "Source changed during audit: $($file.FullName)"}}
$lines+=@($output|ForEach-Object{"$_"});$lines+='Source hashes unchanged during execution.'
$text=($lines -join "`n").TrimEnd()+"`n"
if($OutputPath){$resolved=[IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath));[IO.File]::WriteAllText($resolved,$text,[Text.UTF8Encoding]::new($false))}
Write-Output $text
