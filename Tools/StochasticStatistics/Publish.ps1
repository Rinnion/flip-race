param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$toolRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$trackedManifest = Join-Path $toolRoot 'InitialPublicationManifest.json'
if (Test-Path -LiteralPath $trackedManifest) { throw 'Tracked initial publication manifest already exists; preserve it and request an explicitly named next publication' }
$protocolPath = Join-Path $toolRoot 'Protocol.json'
$protocol = Get-Content -LiteralPath $protocolPath -Raw | ConvertFrom-Json
if ($protocol.protocolId -ne 'BSTO-INITIAL-TRAINING-1') { throw 'Unexpected protocol' }
$sourceFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Assets/Scripts/Core') -Filter '*.cs' -File
    Get-ChildItem -LiteralPath $toolRoot -File | Where-Object { $_.Name -in @('Program.cs','StochasticStatistics.csproj','Protocol.json','Publish.ps1','README.md','.gitignore') }
) | Sort-Object FullName
$sourceRows = @($sourceFiles | ForEach-Object { [ordered]@{ path = [IO.Path]::GetRelativePath($repositoryRoot,$_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$publication = Join-Path $toolRoot ('Published/initial-' + $stamp)
if (Test-Path -LiteralPath $publication) { throw 'Publication path must be fresh' }
New-Item -ItemType Directory -Path $publication | Out-Null
foreach ($row in $sourceRows) {
    $destination = Join-Path $publication ('sources/' + $row.path)
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    [IO.File]::Copy((Join-Path $repositoryRoot $row.path),$destination,$false)
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $row.sha256) { throw ('Snapshot copy hash mismatch: ' + $row.path) }
}
$originalLocation = Get-Location
try {
    Set-Location -LiteralPath $repositoryRoot
    & dotnet publish (Join-Path $toolRoot 'StochasticStatistics.csproj') -c Release --no-restore --nologo -v:q -o $publication
    if ($LASTEXITCODE -ne 0) { throw ('Release publication failed with exit ' + $LASTEXITCODE) }
} finally { Set-Location -LiteralPath $originalLocation.Path }
foreach ($row in $sourceRows) {
    if ((Get-FileHash -LiteralPath (Join-Path $repositoryRoot $row.path) -Algorithm SHA256).Hash -ne $row.sha256) { throw ('Source changed while publishing; do not use this directory: ' + $row.path) }
}
$dll = Join-Path $publication 'StochasticStatistics.dll'
$descriptionLines = & dotnet $dll --describe-initial
if ($LASTEXITCODE -ne 0) { throw 'Initial parameter description failed; no cohort was run' }
$description = ($descriptionLines -join "`n") | ConvertFrom-Json
$assemblyHash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
if ($description.assemblySha256 -ne $assemblyHash -or $description.parameters.Count -ne 50) { throw 'Assembly/50 parameter table mismatch' }
$publishedFiles = @(Get-ChildItem -LiteralPath $publication -Recurse -File | Sort-Object FullName | ForEach-Object { [ordered]@{ path = [IO.Path]::GetRelativePath($publication,$_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
$manifest = [ordered]@{
    protocolId = $protocol.protocolId
    publicationId = 'initial-' + $stamp
    purpose = 'INITIAL TRAINING ONLY; no validation acceptance/calibration selection'
    repositoryRoot = $repositoryRoot
    publicationDirectory = $publication
    assemblySha256 = $assemblyHash
    runtimeVersion = $description.runtimeVersion
    protocolSha256 = (Get-FileHash -LiteralPath $protocolPath -Algorithm SHA256).Hash
    scheduleSha256 = $description.scheduleSha256
    parameterTableSha256 = $description.parameterTableSha256
    parameterTableJson = $description.parameterTableJson
    parameters = $description.parameters
    training = [ordered]@{ startSeed=0; count=200; cohorts=50; expectedCases=10000; maximumParallelism=3; camera='Lagger'; human='idle'; fixedBeforeStart=$true }
    heldOut = [ordered]@{ startSeed=1000000; count=200; reserved=$true; executed=$false }
    sourceFiles = $sourceRows
    publishedFiles = $publishedFiles
}
$manifestJson = $manifest | ConvertTo-Json -Depth 20
$manifestPath = Join-Path $publication 'training-manifest.json'
[IO.File]::WriteAllText($manifestPath,$manifestJson)
$trackedManifest = Join-Path $toolRoot 'InitialPublicationManifest.json'
if (Test-Path -LiteralPath $trackedManifest) { throw 'Tracked initial publication manifest already exists; preserve it, request explicit next publication name' }
[IO.File]::WriteAllText($trackedManifest,$manifestJson)
Get-ChildItem -LiteralPath $publication -Recurse -File | ForEach-Object { $_.IsReadOnly = $true }
Write-Output ('PUBLISHED DLL ' + $dll)
Write-Output ('ASSEMBLY SHA256 ' + $assemblyHash)
Write-Output ('MANIFEST SHA256 ' + (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash)
Write-Output ('TRACKED MANIFEST ' + $trackedManifest)
Write-Output ('TRAINING COMMAND: dotnet "' + $dll + '" --training --start-seed 0 --count 200 --parallelism 3')
Write-Output 'NO TRAINING WAS STARTED. Reserved seeds1000000..1000199 were not run.'