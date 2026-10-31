param([Parameter(Mandatory=$true)][string]$ConfigPath)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$tool=(Resolve-Path -LiteralPath $PSScriptRoot).Path
$config=(Resolve-Path -LiteralPath $ConfigPath).Path
$allowed=[IO.Path]::GetFullPath((Join-Path $tool 'Configs'))+[IO.Path]::DirectorySeparatorChar
if(-not $config.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Only a reviewed tool Configs/<id>.json is accepted'}
$c=Get-Content -LiteralPath $config -Raw | ConvertFrom-Json;$id=[string]$c.studyId
if($id -cnotmatch '^[a-z][a-z0-9]*(-[a-z0-9]+)*$' -or $id.Length -gt64 -or $id -eq 'initial' -or [IO.Path]::GetFileName($config) -cne ($id+'.json')){throw 'Safe matching study ID/file required'}
if($c.phase -cnotin @('Validation','ConstantAudit') -or $c.camera -cne $(if($c.phase -ceq 'Validation'){'Lagger'}else{'Constant'})){throw 'Exact validation phase/camera required'}
$preplan=Join-Path $repo 'Verification/stochastic-bots-holdout-preplan-v2.json'
$preplanHash='2233FBCDF0B7E65D5AD1EFD864A74988B00BFC032B4CCDF7AF146F197FE7319D'
if((Get-FileHash -LiteralPath $preplan -Algorithm SHA256).Hash -cne $preplanHash -or $c.preplanSha256 -cne $preplanHash){throw 'Predeclared plan hash mismatch'}
$tracked=Join-Path $tool ('Manifests/'+$id+'.json')
$phaseClaim=Join-Path $tool ('Recovery/'+$preplanHash+'-'+[string]$c.phase+'.claim')
foreach($path in @($tracked,$phaseClaim,(Join-Path $tool ('Recovery/'+$id+'.claim')),(Join-Path $repo ('Verification/stochastic-bots-validation-'+$id+'.json')),(Join-Path $repo ('Verification/stochastic-bots-validation-'+$id+'.txt')))){if(Test-Path -LiteralPath $path){throw ('Existing publication/start/report: '+$path)}}
$files=@(
    Get-ChildItem -LiteralPath (Join-Path $repo 'Assets/Scripts/Core') -File -Filter '*.cs'
    Get-Item -LiteralPath (Join-Path $repo 'Tools/StochasticStatistics/Program.cs'),(Join-Path $repo 'Tools/StochasticStatistics/StudyRunner.cs')
    Get-ChildItem -LiteralPath $tool -File | Where-Object {$_.Extension -in @('.cs','.csproj','.ps1','.json','.md') -or $_.Name -in @('.gitignore','.gitattributes')}
    Get-Item -LiteralPath $config,$preplan
) | Sort-Object FullName -Unique
$sources=@($files | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($repo,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$published=Join-Path $tool ('Published/'+$id+'-'+$stamp)
New-Item -ItemType Directory -Path $published -ErrorAction Stop | Out-Null
foreach($row in $sources){
    $dest=Join-Path $published ('sources/'+$row.path);New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($dest)) -Force | Out-Null
    [IO.File]::Copy((Join-Path $repo $row.path),$dest,$false)
    if((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash -cne $row.sha256){throw ('Snapshot mismatch: '+$row.path)}
}
$old=Get-Location
try{Set-Location -LiteralPath $repo;& dotnet publish (Join-Path $tool 'StochasticValidation.csproj') -c Release --nologo -v:q -o $published;if($LASTEXITCODE -ne0){throw 'Publish failed'}}finally{Set-Location -LiteralPath $old.Path}
foreach($row in $sources){if((Get-FileHash -LiteralPath (Join-Path $repo $row.path) -Algorithm SHA256).Hash -cne $row.sha256){throw ('Source changed during publish: '+$row.path)}}
$dll=Join-Path $published 'StochasticValidation.dll'
$configRelative='sources/'+[IO.Path]::GetRelativePath($repo,$config).Replace('\','/')
$preplanRelative='sources/'+[IO.Path]::GetRelativePath($repo,$preplan).Replace('\','/')
$lines=& dotnet $dll --describe-validation (Join-Path $published $configRelative) (Join-Path $published $preplanRelative)
if($LASTEXITCODE -ne0){throw 'Description rejected actual factory/config/preplan; no cohort run'}
$d=($lines -join [Environment]::NewLine) | ConvertFrom-Json;$assembly=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
if($d.assemblySha256 -cne $assembly -or $d.studyId -cne $id -or $d.configSha256 -cne (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash){throw 'Published description mismatch'}
[IO.File]::WriteAllText((Join-Path $published 'parameter-table.json'),$d.parameterTableJson,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $published 'schedule.json'),$d.scheduleJson,[Text.UTF8Encoding]::new($false))
if((Get-FileHash -LiteralPath (Join-Path $published 'parameter-table.json') -Algorithm SHA256).Hash -cne $d.parameterTableSha256 -or (Get-FileHash -LiteralPath (Join-Path $published 'schedule.json') -Algorithm SHA256).Hash -cne $d.scheduleSha256){throw 'Canonical table/schedule mismatch'}
$publishedFiles=@(Get-ChildItem -LiteralPath $published -Recurse -File | Sort-Object FullName | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($published,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$manifest=[ordered]@{
    protocolId='BSTO-FINAL-VALIDATION-1';studyId=$id;phase=$d.phase;camera=$d.camera;publicationId=$id+'-'+$stamp;repositoryRoot=$repo;publicationDirectory=$published
    assemblySha256=$assembly;runtimeVersion=$d.runtimeVersion;configPath=$configRelative;configSha256=$d.configSha256;preplanPath=$preplanRelative;preplanSha256=$preplanHash
    parameterTableSha256=$d.parameterTableSha256;scheduleSha256=$d.scheduleSha256;parameters=$d.parameters;expectedCases=$d.expectedCases;cohorts=50;maximumParallelism=3
    humanInput='Idle';fixedBeforeStart=$true;automaticExtension=$false;inspectBetweenBlocks=$false
    protocolSha256=(Get-FileHash -LiteralPath (Join-Path $tool 'Protocol.json') -Algorithm SHA256).Hash;sourceFiles=$sources;publishedFiles=$publishedFiles
}
$manifestPath=Join-Path $published 'validation-manifest.json'
[IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path (Join-Path $tool 'Manifests') -Force | Out-Null
$stream=[IO.FileStream]::new($tracked,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[IO.File]::ReadAllBytes($manifestPath);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
foreach($row in $publishedFiles){if((Get-FileHash -LiteralPath (Join-Path $published $row.path) -Algorithm SHA256).Hash -cne $row.sha256){throw ('Final hash mismatch: '+$row.path)}}
$manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
if((Get-FileHash -LiteralPath $tracked -Algorithm SHA256).Hash -cne $manifestHash){throw 'Tracked manifest must match exact bytes'}
Get-ChildItem -LiteralPath $published -Recurse -File | ForEach-Object {$_.IsReadOnly=$true}
Write-Output ('DLL '+$dll)
Write-Output ('ASSEMBLY SHA256 '+$assembly)
Write-Output ('MANIFEST SHA256 '+$manifestHash)
Write-Output ('TABLE SHA256 '+$d.parameterTableSha256)
Write-Output ('SCHEDULE SHA256 '+$d.scheduleSha256)
Write-Output ('SELF-CHECK: dotnet "'+$dll+'" --self-check-validation "'+(Join-Path $published $preplanRelative)+'"')
Write-Output ('RUN ONLY AFTER SEPARATE ROOT GO: dotnet "'+$dll+'" --validation --study-id '+$id+' --parallelism 3')
Write-Output 'Publication metadata only; no Press/Advance/controller/cohort/held-out execution.'
