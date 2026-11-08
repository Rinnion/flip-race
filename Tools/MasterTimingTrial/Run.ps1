param([Parameter(Mandatory)][string]$Output, [switch]$IncludeC)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = [IO.Path]::GetFullPath($Output)
$revision = '223040614abcbefc2251aace56aed5ce44b4ee48'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('master-timing-' + [guid]::NewGuid().ToString('N'))
$frozen = Join-Path $scratch 'frozen'
$zip = Join-Path $scratch 'frozen.zip'
$patch = Join-Path $PSScriptRoot 'master-time-first.patch'
$project = Join-Path $PSScriptRoot 'MasterTimingTrial.csproj'
New-Item -ItemType Directory -Path $scratch | Out-Null

function Assert-Exit([string]$Action) {
    if ($LASTEXITCODE -ne 0) { throw "$Action failed with exit code $LASTEXITCODE" }
}

git -C $root archive --format=zip "--output=$zip" $revision Assets/Scripts/Core Assets/StreamingAssets/Tracks
Assert-Exit 'Extract frozen sources'
Expand-Archive -LiteralPath $zip -DestinationPath $frozen
$coreA = Join-Path $frozen 'Assets/Scripts/Core'
$tracks = Join-Path $frozen 'Assets/StreamingAssets/Tracks'
$coreB = Join-Path $scratch 'candidate-core'
Copy-Item -LiteralPath $coreA -Destination $coreB -Recurse
git -C $coreB apply --no-index --check $patch
Assert-Exit 'Check isolated overlay'
git -C $coreB apply --no-index $patch
Assert-Exit 'Apply isolated overlay'

$changed = @(
    foreach ($file in Get-ChildItem -LiteralPath $coreA -Filter '*.cs') {
        $aHash = (Get-FileHash -LiteralPath $file.FullName).Hash
        $bHash = (Get-FileHash -LiteralPath (Join-Path $coreB $file.Name)).Hash
        if ($aHash -ne $bHash) { [ordered]@{ file = $file.Name; a = $aHash; b = $bHash } }
    }
)
if (($changed.file | Sort-Object) -join ',' -ne 'PrestartBotPreparation.cs,PrestartBotSearch.cs') {
    throw 'The experimental overlay changed unexpected Core files.'
}

$changedC = $null
if ($IncludeC) {
    $frontierPatch = Join-Path $PSScriptRoot 'master-frontier-time.patch'
    $coreC = Join-Path $scratch 'frontier-core'
    Copy-Item -LiteralPath $coreB -Destination $coreC -Recurse
    git -C $coreC apply --no-index --check $frontierPatch
    Assert-Exit 'Check frontier overlay'
    git -C $coreC apply --no-index $frontierPatch
    Assert-Exit 'Apply frontier overlay'
    $changedC = @(
        foreach ($file in Get-ChildItem -LiteralPath $coreB -Filter '*.cs') {
            $bHash = (Get-FileHash -LiteralPath $file.FullName).Hash
            $cHash = (Get-FileHash -LiteralPath (Join-Path $coreC $file.Name)).Hash
            if ($bHash -ne $cHash) { [ordered]@{ file = $file.Name; b = $bHash; c = $cHash } }
        }
    )
    if ($changedC.Count -ne 1 -or $changedC[0].file -ne 'PrestartBotSearch.cs') {
        throw 'The frontier overlay changed unexpected Core files.'
    }
}

function Invoke-Trial([string]$Label, [string]$Core) {
    $artifacts = Join-Path $scratch $Label
    $raw = Join-Path $scratch "$Label.json"
    dotnet build $project -c Release -p:UseAppHost=false "-p:CoreRoot=$Core" --artifacts-path $artifacts --nologo | Out-Host
    Assert-Exit "Build $Label"
    $dll = Join-Path $artifacts 'bin/MasterTimingTrial/release/MasterTimingTrial.dll'
    dotnet $dll $Label $tracks $raw | Out-Host
    Assert-Exit "Run $Label"
    Get-Content -Raw -LiteralPath $raw | ConvertFrom-Json
}

function Row-Key($row) { "$($row.trackId)/$($row.profile)/$($row.surface)/$($row.difficulty)/$($row.seed)/$($row.stage)" }

function Compare-Trials($left, $right, [bool]$sameSearch) {
    if ($left.rows.Count -ne 64 -or $right.rows.Count -ne $left.rows.Count) { throw 'Incomplete matrix.' }
    $candidateByKey = @{}
    foreach ($row in $right.rows) { $candidateByKey.Add((Row-Key $row), $row) }
    foreach ($baseline in $left.rows) {
        $key = Row-Key $baseline
        $candidate = $candidateByKey[$key]
        if (!$candidate -or $baseline.trackHash -ne $candidate.trackHash) { throw "Mismatched input $key" }
        $sameActual = (ConvertTo-Json -InputObject $baseline.actual -Depth 12 -Compress) -ceq
            (ConvertTo-Json -InputObject $candidate.actual -Depth 12 -Compress)
        $sameCensus = $baseline.candidates.censusSha256 -ceq $candidate.candidates.censusSha256
        $lowerLevel = $baseline.difficulty -ne 'Master'
        if (($sameSearch -or $lowerLevel) -and (!$sameCensus -or $baseline.preparation.ExpandedStates -ne $candidate.preparation.ExpandedStates)) {
            throw "Changed search work or full candidates $key"
        }
        if ($lowerLevel -and (!$sameActual -or $baseline.cacheSha256 -cne $candidate.cacheSha256)) {
            throw "Changed lower-level trace, outcome or cache $key"
        }
        $delta = if ($null -ne $candidate.actual.FinishTime) { [Math]::Round(($candidate.actual.FinishTime - $baseline.actual.FinishTime), 9) }
        [ordered]@{
            key = $key; track = $baseline.trackId; profile = $baseline.profile; surface = $baseline.surface
            difficulty = $baseline.difficulty; seed = $baseline.seed; stage = $baseline.stage; sameActual = $sameActual; sameCensus = $sameCensus
            deltaSeconds = $delta
            a = $baseline; candidate = $candidate
        }
    }
}

function Summarize($rows) {
    $masterCold = @($rows | Where-Object { $_.difficulty -eq 'Master' -and $_.stage -eq 'cold' })
    $masterWarm = @($rows | Where-Object { $_.difficulty -eq 'Master' -and $_.stage -eq 'warm' })
    $lower = @($rows | Where-Object { $_.difficulty -ne 'Master' })
    [ordered]@{
        masterCold = $masterCold.Count; masterColdFaster = @($masterCold | Where-Object { $null -ne $_.deltaSeconds -and $_.deltaSeconds -lt 0 }).Count
        masterColdSlower = @($masterCold | Where-Object { $null -ne $_.deltaSeconds -and $_.deltaSeconds -gt 0 }).Count
        masterColdWithoutFinish = @($masterCold | Where-Object { $null -eq $_.deltaSeconds }).Count
        masterWarm = $masterWarm.Count; masterWarmFaster = @($masterWarm | Where-Object { $null -ne $_.deltaSeconds -and $_.deltaSeconds -lt 0 }).Count
        masterWarmSlower = @($masterWarm | Where-Object { $null -ne $_.deltaSeconds -and $_.deltaSeconds -gt 0 }).Count
        masterWarmWithoutFinish = @($masterWarm | Where-Object { $null -eq $_.deltaSeconds }).Count
        lowerExact = @($lower | Where-Object sameActual).Count
    }
}

$a = Invoke-Trial 'A' $coreA
$b = Invoke-Trial 'B' $coreB
$comparisons = @(Compare-Trials $a $b $true)
$summary = Summarize $comparisons
$report = [ordered]@{
    revision = $revision; overlaySha256 = (Get-FileHash -LiteralPath $patch).Hash; changedCore = $changed
    config = [ordered]@{
        mode = 'standalone with idle human'; camera = 'Lagger'; warningSeconds = .8; liveStep = '1/60'; prepWorkPerSlice = 192
        countdownSeconds = 3; surfaceRate = 5; curve = 'Linear'; cap = 30; decay = 200; resetOnCollision = $true
        coldMasterSeeds = @(12101, 12102, 12103); warmBankSeed = 12101; warmSeed = 12102; lowerSeed = 12101
        cacheIsolation = 'Each executable generates its own bank in memory, then warms with its own bank.'
    }
    summary = $summary; comparisons = $comparisons
}
if ($IncludeC) {
    $c = Invoke-Trial 'C' $coreC
    $comparisonsC = @(Compare-Trials $a $c $false)
    $report.frontierOverlaySha256 = (Get-FileHash -LiteralPath $frontierPatch).Hash
    $report.changedFrontierCore = $changedC
    $report.summaryC = Summarize $comparisonsC
    $report.comparisonsC = $comparisonsC
}
$report | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $destination -Encoding utf8
$summary | ConvertTo-Json
if ($IncludeC) { $report.summaryC | ConvertTo-Json }
Write-Output $destination
