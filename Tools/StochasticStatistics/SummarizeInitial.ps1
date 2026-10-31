param()
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$inputPath = Join-Path $repository 'Verification/stochastic-bots-training-initial.json'
$inputHash = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
if($inputHash -ne '715C857AA5C4C3EE39D379AFF4204485356F7FDBE8B9D65844CCA9B83F1F30B9') { throw 'Unexpected initial dataset; this summary is bound to the immutable completed initial cohort' }
function Number([Text.Json.JsonElement]$element,[string]$property) { $value=$element.GetProperty($property); if($value.ValueKind -eq [Text.Json.JsonValueKind]::Null){ return $null }; return $value.GetDouble() }
function F($value,[string]$format='F6') { if($null -eq $value){return 'null'}; return ([double]$value).ToString($format,[Globalization.CultureInfo]::InvariantCulture) }
$stream = [IO.File]::OpenRead($inputPath)
try { $document = [Text.Json.JsonDocument]::Parse($stream) } finally { $stream.Dispose() }
try {
    $root=$document.RootElement
    if($root.GetProperty('actualCases').GetInt32() -ne 10000 -or $root.GetProperty('unrunCaseIds').GetArrayLength() -ne 0 -or $root.GetProperty('heldOutSeedsRun').GetBoolean()){ throw 'Initial cohort completeness/reservation mismatch' }
    $rows = [Collections.Generic.List[object]]::new()
    foreach($cohort in $root.GetProperty('cohorts').EnumerateArray()) {
        $p=$cohort.GetProperty('parameters'); $metrics=$cohort.GetProperty('metrics'); $time=$metrics.GetProperty('actualTerminalTime')
        $decisions=0; $matched=0; $unresolved=0; $breakdown=[Collections.Generic.List[object]]::new()
        foreach($bucket in $cohort.GetProperty('intentionVersusActual').EnumerateArray()) {
            $n=$bucket.GetProperty('decisions').GetInt32(); $m=$bucket.GetProperty('matched').GetInt32(); $u=$bucket.GetProperty('unresolved').GetInt32()
            $decisions+=$n; $matched+=$m; $unresolved+=$u
            $breakdown.Add([ordered]@{key=$bucket.GetProperty('key').GetString();decisions=$n;matched=$m;unmet=$n-$m;unresolved=$u;unmetRate=if($n){($n-$m)/[double]$n}else{$null};actualOutcomes=($bucket.GetProperty('actualOutcomes').GetRawText() | ConvertFrom-Json)})
        }
        $mean=Number $time 'mean'; $target=Number $p 'targetMeanTime'
        $rows.Add([ordered]@{
            cohortId=$p.GetProperty('cohortId').GetString(); trackId=$p.GetProperty('trackId').GetString();profileId=$p.GetProperty('profileId').GetString();difficulty=$p.GetProperty('difficulty').GetString()
            quality=Number $p 'quality';collectProbability=Number $p 'collectProbability';hitProbability=Number $p 'hitProbability';targetMeanTime=$target
            cases=$cohort.GetProperty('observedCases').GetInt32();terminalCases=$cohort.GetProperty('terminalCases').GetInt32();errors=$cohort.GetProperty('errors').GetInt32();masterQuality=$cohort.GetProperty('masterQuality').GetString()
            timeObserved=$time.GetProperty('observed').GetInt32();timeComplete=$time.GetProperty('complete').GetBoolean();meanTime=$mean;timeSampleStd=Number $time 'sampleStandardDeviation';lower99=Number $time 'lower99';upper99=Number $time 'upper99';meanMinusTarget=if($null -ne $mean){$mean-$target}else{$null}
            meanHits=Number ($metrics.GetProperty('collisions')) 'mean';meanBoostsEarned=Number ($metrics.GetProperty('ordinaryBoostsEarned')) 'mean';meanCompletedBoostPulses=Number ($metrics.GetProperty('completedBoostPulses')) 'mean';meanFullFinish=Number ($metrics.GetProperty('fullFinishCount')) 'mean';meanUnreachableReasons=Number ($metrics.GetProperty('unreachableCount')) 'mean'
            intendedDecisions=$decisions;matchedIntentions=$matched;unmetIntentions=$decisions-$matched;unresolvedIntentions=$unresolved;unmetIntentRate=if($decisions){($decisions-$matched)/[double]$decisions}else{$null};intentionBreakdown=$breakdown.ToArray()
        })
    }
    $failureSeeds=@{}
    foreach($case in $root.GetProperty('cases').EnumerateArray()) {
        $spec=$case.GetProperty('case')
        if($spec.GetProperty('difficulty').GetString() -eq 'Master' -and $case.GetProperty('error').ValueKind -ne [Text.Json.JsonValueKind]::Null) {
            $key=$spec.GetProperty('trackId').GetString()+'/'+$spec.GetProperty('profileId').GetString()
            if(-not $failureSeeds.ContainsKey($key)){$failureSeeds[$key]=[Collections.Generic.List[int]]::new()}
            $failureSeeds[$key].Add($spec.GetProperty('seed').GetInt32())
        }
    }
    $failures=@($failureSeeds.Keys | Sort-Object | ForEach-Object { [ordered]@{pair=$_;count=$failureSeeds[$_].Count;seeds=@($failureSeeds[$_] | Sort-Object)} })
    if((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash -ne $inputHash){throw 'Source dataset changed during derived summary'}
    $summary=[ordered]@{
        status='DERIVED_INITIAL_TRAINING_SUMMARY';sourceDataStatus=$root.GetProperty('status').GetString();sourceJson='Verification/stochastic-bots-training-initial.json';sourceJsonSha256=$inputHash;summaryScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        exploratoryOnly=$true;validationAcceptance=$false;heldOutSeedsRun=$false;cases=10000;cohorts=50;seedsPerCohort='0..199';publicationStable=$root.GetProperty('publicationStable').GetBoolean()
        assemblySha256=$root.GetProperty('assemblySha256').GetString();manifestSha256=$root.GetProperty('manifestSha256').GetString();parameterTableSha256=$root.GetProperty('parameterTableSha256').GetString();scheduleSha256=$root.GetProperty('scheduleSha256').GetString()
        method='Derived only from the completed raw initial report; no races/retries/calibration/parameter changes. Mean/SD/99% CI copied from all-terminal cohort metrics; terminal quality failures stay included. Unmet intent rate=(all observed decisions-matched)/all observed decisions, unresolved included; individual kind/intent buckets preserved.'
        masterFailures=$failures;rows=$rows.ToArray()
    }
    $jsonPath=Join-Path $repository 'Verification/stochastic-bots-training-initial-summary.json'
    $textPath=Join-Path $repository 'Verification/stochastic-bots-training-initial-summary.txt'
    [IO.File]::WriteAllText($jsonPath,($summary | ConvertTo-Json -Depth 30))
    $lines=[Collections.Generic.List[string]]::new()
    $lines.Add('INITIAL TRAINING SUMMARY — EXPLORATORY ONLY, NOT VALIDATION ACCEPTANCE')
    $lines.Add('10000 cases;50 cohorts;seeds0..199 each;no held-out execution;source raw SHA256 '+$inputHash)
    $lines.Add('All400 Master failures: Short/Chosen0..199 and Short/FastFlip0..199. Failed terminal times remain included. Original raw JSON/TXT unchanged.')
    $lines.Add('Cohort | meanTime [99%CI] | hits | earnedBoosts | completedBoostPulses | fullFinish | unmetIntentRate | errors')
    foreach($row in $rows) {$lines.Add($row.cohortId+' | '+(F $row.meanTime)+' ['+(F $row.lower99)+','+(F $row.upper99)+'] | '+(F $row.meanHits 'F3')+' | '+(F $row.meanBoostsEarned 'F3')+' | '+(F $row.meanCompletedBoostPulses 'F3')+' | '+(F $row.meanFullFinish 'F3')+' | '+(F $row.unmetIntentRate 'F5')+' | '+$row.errors)}
    [IO.File]::WriteAllLines($textPath,$lines)
    Write-Output ('SUMMARY '+$rows.Count+' cohorts;raw preserved;no races; JSON '+$jsonPath)
    Get-FileHash -LiteralPath $jsonPath,$textPath -Algorithm SHA256 | ForEach-Object {$_.Hash+' '+$_.Path}
} finally {$document.Dispose()}