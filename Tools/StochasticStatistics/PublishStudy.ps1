param([Parameter(Mandatory=$true)][string]$ConfigPath)
$ErrorActionPreference='Stop'
$repositoryRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$toolRoot=(Resolve-Path -LiteralPath $PSScriptRoot).Path
$config=(Resolve-Path -LiteralPath $ConfigPath).Path
$studiesRoot=[IO.Path]::GetFullPath((Join-Path $toolRoot 'Studies'))+[IO.Path]::DirectorySeparatorChar
if(-not $config.StartsWith($studiesRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Only a reviewed named config inside this tool Studies directory is accepted'}
$configObject=Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
$id=[string]$configObject.studyId
if($id -cnotmatch '^[a-z][a-z0-9]*(-[a-z0-9]+)*$' -or $id.Length -gt64 -or $id -eq 'initial'){throw 'Safe unique study ID required'}
if([IO.Path]::GetFileName($config) -cne ($id+'.json') -or $configObject.phase -cne 'Training'){throw 'Matching filename and Training-only config required'}
$trackedManifest=Join-Path $toolRoot ('Manifests/'+$id+'.json')
foreach($existing in @($trackedManifest,(Join-Path $repositoryRoot ('Verification/stochastic-bots-study-'+$id+'.json')),(Join-Path $repositoryRoot ('Verification/stochastic-bots-study-'+$id+'.txt')),(Join-Path $toolRoot ('Recovery/'+$id+'.claim')))){
    if(Test-Path -LiteralPath $existing){throw ('Study already published/started/reported; never overwrite or rerun: '+$existing)}
}
# No compiler/race is invoked until this explicit publisher command is authorized.
$sourceFiles=@(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Assets/Scripts/Core') -File -Filter '*.cs'
    Get-ChildItem -LiteralPath $toolRoot -File | Where-Object {$_.Extension -in @('.cs','.csproj','.ps1') -or $_.Name -in @('Protocol.json','StudyProtocol.json','StudySchema.json','README.md','StudyREADME.md','.gitignore','.gitattributes')}
    Get-Item -LiteralPath $config
) | Sort-Object FullName -Unique
$sourceRows=@($sourceFiles | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($repositoryRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$publication=Join-Path $toolRoot ('Published/'+$id+'-'+$stamp)
New-Item -ItemType Directory -Path $publication -ErrorAction Stop | Out-Null
foreach($row in $sourceRows){
    $dest=Join-Path $publication ('sources/'+$row.path)
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($dest)) -Force | Out-Null
    [IO.File]::Copy((Join-Path $repositoryRoot $row.path),$dest,$false)
    if((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash -ne $row.sha256){throw ('Source snapshot mismatch: '+$row.path)}
}
$original=Get-Location
try{
    Set-Location -LiteralPath $repositoryRoot
    & dotnet publish (Join-Path $toolRoot 'StochasticStatistics.csproj') -c Release --no-restore --nologo -v:q -o $publication
    if($LASTEXITCODE -ne0){throw ('Release publish failed: '+$LASTEXITCODE)}
}finally{Set-Location -LiteralPath $original.Path}
foreach($row in $sourceRows){
    if((Get-FileHash -LiteralPath (Join-Path $repositoryRoot $row.path) -Algorithm SHA256).Hash -ne $row.sha256){throw ('Source changed during publish; discard this unpublished directory: '+$row.path)}
}
$dll=Join-Path $publication 'StochasticStatistics.dll'
$configRelative='sources/'+[IO.Path]::GetRelativePath($repositoryRoot,$config).Replace('\','/')
$descriptionLines=& dotnet $dll --describe-study (Join-Path $publication $configRelative)
if($LASTEXITCODE -ne0){throw 'Frozen config description rejected; no cohort was run'}
$description=($descriptionLines -join [Environment]::NewLine) | ConvertFrom-Json
$assemblyHash=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
if($description.protocolId -cne 'BSTO-NAMED-TRAINING-1' -or $description.studyId -cne $id -or $description.assemblySha256 -cne $assemblyHash -or $description.configSha256 -cne (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash){throw 'Description assembly/config mismatch'}
[IO.File]::WriteAllText((Join-Path $publication 'parameter-table.json'),$description.parameterTableJson,[Text.UTF8Encoding]::new($false))
# Exact canonical schedule bytes come directly from the same serializer as its hash.
# The description includes a compact canonical string to avoid PowerShell number/escape reserialization.
[IO.File]::WriteAllText((Join-Path $publication 'schedule.json'),$description.scheduleJson,[Text.UTF8Encoding]::new($false))
if((Get-FileHash -LiteralPath (Join-Path $publication 'parameter-table.json') -Algorithm SHA256).Hash -cne $description.parameterTableSha256 -or (Get-FileHash -LiteralPath (Join-Path $publication 'schedule.json') -Algorithm SHA256).Hash -cne $description.scheduleSha256){throw 'Canonical table/schedule bytes mismatch'}
$publishedFiles=@(Get-ChildItem -LiteralPath $publication -Recurse -File | Sort-Object FullName | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($publication,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$manifest=[ordered]@{
    protocolId='BSTO-NAMED-TRAINING-1';studyId=$id;publicationId=$id+'-'+$stamp;phase='Training'
    purpose='Exploratory fixed-before-start named training only; not validation or automatic calibration'
    repositoryRoot=$repositoryRoot;publicationDirectory=$publication;assemblySha256=$assemblyHash;runtimeVersion=$description.runtimeVersion
    configPath=$configRelative;configSha256=$description.configSha256;protocolSha256=(Get-FileHash -LiteralPath (Join-Path $toolRoot 'StudyProtocol.json') -Algorithm SHA256).Hash
    parameterTableSha256=$description.parameterTableSha256;scheduleSha256=$description.scheduleSha256
    expectedN=$description.expectedN;cohorts=$description.cohorts;expectedCases=$description.expectedCases
    camera=$description.camera;humanInput='Idle';maximumParallelism=3;fixedBeforeStart=$true
    parameters=$description.parameters;selectedInputsScope=$description.selectedInputsScope
    heldOut=[ordered]@{allSeedsFrom=1000000;executed=$false;validationSupported=$false}
    sourceFiles=$sourceRows;publishedFiles=$publishedFiles
}
$manifestJson=$manifest | ConvertTo-Json -Depth 30
$manifestPath=Join-Path $publication 'study-manifest.json'
[IO.File]::WriteAllText($manifestPath,$manifestJson,[Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path (Join-Path $toolRoot 'Manifests') -Force | Out-Null
$manifestStream=[IO.FileStream]::new($trackedManifest,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[IO.File]::ReadAllBytes($manifestPath);$manifestStream.Write($bytes,0,$bytes.Length)}finally{$manifestStream.Dispose()}
foreach($row in $publishedFiles){if((Get-FileHash -LiteralPath (Join-Path $publication $row.path) -Algorithm SHA256).Hash -cne $row.sha256){throw ('Final publication hash mismatch: '+$row.path)}}
$manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
if((Get-FileHash -LiteralPath $trackedManifest -Algorithm SHA256).Hash -cne $manifestHash){throw 'Tracked manifest must be byte-exact'}
Get-ChildItem -LiteralPath $publication -Recurse -File | ForEach-Object {$_.IsReadOnly=$true}
Write-Output ('PUBLISHED DLL '+$dll)
Write-Output ('ASSEMBLY SHA256 '+$assemblyHash)
Write-Output ('MANIFEST SHA256 '+$manifestHash)
Write-Output ('CONFIG SHA256 '+$description.configSha256)
Write-Output ('TABLE SHA256 '+$description.parameterTableSha256)
Write-Output ('SCHEDULE SHA256 '+$description.scheduleSha256)
Write-Output ('SELF-CHECK COMMAND: dotnet "'+$dll+'" --self-check-study')
Write-Output ('TRAINING COMMAND (SEPARATE ROOT GO): dotnet "'+$dll+'" --study --study-id '+$id+' --parallelism 3')
Write-Output 'No physical cohort or held-out seed was run.'
