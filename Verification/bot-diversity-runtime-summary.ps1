$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputPath = Join-Path $PSScriptRoot 'bot-diversity-runtime.json'
$checks = 0
function Need([bool]$yes, [string]$why) { $script:checks++; if (!$yes) { throw $why } }
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function Load([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable }
function Mean($values) { ($values | Measure-Object -Average).Average }
function Near([double]$a,[double]$b,[double]$tolerance,[string]$why) { Need ([double]::IsFinite($a) -and [double]::IsFinite($b) -and [Math]::Abs($a-$b) -le $tolerance) $why }
function UniqueMap { [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal) }
Push-Location $projectRoot
try {
    $paths = 0..2 | ForEach-Object { 'Verification/bot-diversity-runtime-'+$_+'.json' }
    foreach ($path in $paths) { Need (Test-Path -LiteralPath $path -PathType Leaf) ('Missing final runtime part: '+$path) }
    Need (@(Get-ChildItem -LiteralPath Verification -Filter 'bot-diversity-runtime-*-failure.json').Count -eq 0) 'A failed runtime part exists; resolve it explicitly before aggregating'
    $sourcePath='Verification/bot-diversity-plans.json'
    $finalSha='C062311EF0C0FEBFC643023CB9800F064444BBD3548F67475BDA9E25568949EC'
    Need ((Hash $sourcePath) -ceq $finalSha) 'Final accepted plan report SHA changed'
    $source=Load $sourcePath
    Need ($source.status -ceq 'PASS' -and $source.libraryCount -eq 50 -and $source.planCount -eq 1799 -and $source.sourceStable -eq $true) 'Final source is not complete PASS50/1799'
    $freezePath='Verification/bot-diversity-source-freeze.json'; $freeze=Load $freezePath
    Need ($freeze.status -ceq 'FROZEN' -and $freeze.reportSha256 -ceq $finalSha -and $freeze.libraries -eq 50 -and $freeze.plans -eq 1799 -and $freeze.signatures -eq 490) 'Unexpected freeze declaration'
    $freezeFiles=UniqueMap
    foreach($entry in $freeze.sourceFiles) {
        Need (!$freezeFiles.ContainsKey($entry.path)) ('Duplicate freeze file: '+$entry.path)
        Need ((Hash $entry.path) -ceq $entry.sha256) ('Frozen source changed: '+$entry.path)
        $freezeFiles.Add($entry.path,$entry.sha256)
    }
    $plans=UniqueMap; $libraries=UniqueMap; $pairs=UniqueMap
    $pairOrder=[Collections.Generic.List[string]]::new();$libraryOrder=[Collections.Generic.List[string]]::new()
    foreach($library in $source.libraries) {
        $key=$library.trackId+'/'+$library.profile+'/'+$library.difficulty;$pair=$library.trackId+'/'+$library.profile
        Need (!$libraries.ContainsKey($key)) ('Duplicate library '+$key);$libraries.Add($key,$library);$libraryOrder.Add($key)
        if(!$pairs.ContainsKey($pair)){$pairs.Add($pair,[Collections.Generic.List[object]]::new());$pairOrder.Add($pair)}
        $index=0
        foreach($plan in $library.plans) {
            Need (!$plans.ContainsKey($plan.id)) ('Duplicate published plan '+$plan.id)
            $record=@{plan=$plan;library=$library;key=$key;pair=$pair;index=$index++}
            $plans.Add($plan.id,$record);$pairs[$pair].Add($record)
        }
    }
    Need ($plans.Count -eq 1799 -and $libraries.Count -eq 50 -and $pairs.Count -eq 10) 'Published coverage mismatch'
    $scheduled=UniqueMap;$serial=0
    foreach($pair in $pairOrder) {foreach($record in $pairs[$pair]) {foreach($camera in @('Constant','Lagger')) {foreach($frame in 0..2) {
        $key='every-plan|'+$record.plan.id+'|'+$camera+'|'+$frame
        $scheduled.Add($key,@{batch=$serial%3;record=$record;camera=$camera;frame=$frame;sequence=-1;coverage='every-plan'});$serial++
    }}}}
    foreach($libraryKey in $libraryOrder) {foreach($sequence in 0..19) {foreach($camera in @('Constant','Lagger')) {
        $key='session|'+$libraryKey+'|'+$sequence+'|'+$camera
        $scheduled.Add($key,@{batch=$serial%3;libraryKey=$libraryKey;camera=$camera;frame=$sequence%3;sequence=$sequence;coverage='shuffled-session-sequence'});$serial++
    }}}
    Need ($serial -eq 12794 -and $serial -eq 1799*6+50*20*2) 'Expected schedule size mismatch'
    $seen=UniqueMap;$session=UniqueMap;$idleFrame0=UniqueMap;$constantFrame0=UniqueMap;$order=UniqueMap
    $partEvidence=[Collections.Generic.List[object]]::new();$baselineHashes=$null
    [long]$snapshots=0;[long]$fieldComparisons=0;[long]$advances=0;[long]$presses=0
    $maxTime=0.0;$maxDistance=0.0;$maxEdge=0.0;$totalCases=0
    foreach($batch in 0..2) {
        $path=$paths[$batch];$part=Load $path
        Need ($part.status -ceq 'PASS' -and $part.smoke -eq $false -and $part.sourceStable -eq $true -and $part.batch -eq $batch -and $part.parts -eq 3) ('Invalid/nonfinal runtime part '+$batch)
        $expectedAssigned=[int][Math]::Floor(($serial+2-$batch)/3)
        Need ($part.totalScheduledCases -eq $serial -and $part.assignedCases -eq $expectedAssigned -and $part.cases -eq $expectedAssigned -and $part.evidence.Count -eq $expectedAssigned -and $part.plansCovered -eq 1799) ('Assigned cases filtered/missing in part '+$batch)
        Need ($part.sessionSeed -eq 0x51a21 -and $part.maximumReplayNumericError -eq 0) ('Wrong selection seed/replay mismatch in part '+$batch)
        Need ($part.sourceHashes[$sourcePath] -ceq $finalSha) ('Part provenance report hash mismatch '+$batch)
        if($null -eq $baselineHashes){$baselineHashes=$part.sourceHashes}else {
            Need ($part.sourceHashes.Count -eq $baselineHashes.Count) 'Runtime source hash set differs'
            foreach($entry in $baselineHashes.GetEnumerator()){Need ($part.sourceHashes.Contains($entry.Key) -and $part.sourceHashes[$entry.Key] -ceq $entry.Value) ('Runtime parts used different source '+$entry.Key)}
        }
        foreach($entry in $part.sourceHashes.GetEnumerator()) {
            Need ((Hash $entry.Key) -ceq $entry.Value) ('Runtime source is no longer current '+$entry.Key)
            if($entry.Key.StartsWith('Assets/Scripts/Core/',[StringComparison]::Ordinal)){Need ($freezeFiles.ContainsKey($entry.Key) -and $freezeFiles[$entry.Key] -ceq $entry.Value) ('Runtime Core does not match freeze '+$entry.Key)}
        }
        $sumSnapshots=0L;$sumAdvances=0L;$sumPresses=0L;$partMaxTime=0.0;$partMaxDistance=0.0;$partMaxEdge=0.0
        foreach($row in $part.evidence) {
            Need ($plans.ContainsKey($row.Id)) ('Unknown runtime plan '+$row.Id);$record=$plans[$row.Id];$plan=$record.plan;$library=$record.library
            Need ($row.TrackId -ceq $library.trackId -and $row.Profile -ceq $library.profile -and $row.Level -ceq $library.difficulty -and $row.SignatureId -ceq $plan.signatureId) ('Runtime plan metadata mismatch '+$row.Id)
            Need ($row.camera -cin @('Constant','Lagger') -and $row.frameMode -in 0..2 -and $row.bothTerminal -eq $true -and $row.maximumReplayNumericError -eq 0) ('Invalid camera/frame/terminal/replay '+$row.Id)
            if($row.coverage -ceq 'every-plan') {
                $key='every-plan|'+$row.Id+'|'+$row.camera+'|'+$row.frameMode
                Need ($row.human -ceq 'idle' -and $row.sequenceIndex -eq -1 -and $row.seed -eq $record.index) ('Published-plan coverage changed '+$key)
                if($row.camera -ceq 'Lagger' -and $row.frameMode -eq 0){Need (!$idleFrame0.ContainsKey($row.Id)) 'Duplicate Lagger frame0';$idleFrame0.Add($row.Id,$row)}
                if($row.camera -ceq 'Constant' -and $row.frameMode -eq 0){Need (!$constantFrame0.ContainsKey($row.Id)) 'Duplicate Constant frame0';$constantFrame0.Add($row.Id,$row)}
            }elseif($row.coverage -ceq 'shuffled-session-sequence') {
                Need ($row.sequenceIndex -ge 0 -and $row.sequenceIndex -lt 20 -and $row.frameMode -eq $row.sequenceIndex%3 -and $row.human -ceq @('idle','success','errors')[$row.sequenceIndex%3]) 'Session sequence/cadence/human coverage changed'
                $key='session|'+$record.key+'|'+$row.sequenceIndex+'|'+$row.camera
                $choiceKey=$record.key+'|'+$row.sequenceIndex
                if(!$session.ContainsKey($choiceKey)){$session.Add($choiceKey,[Collections.Generic.List[object]]::new())};$session[$choiceKey].Add($row)
            }else {throw ('Unexpected coverage '+$row.coverage)}
            Need ($scheduled.ContainsKey($key) -and $scheduled[$key].batch -eq $batch -and !$seen.ContainsKey($key)) ('Duplicate/unassigned runtime row '+$key);$seen.Add($key,$row)
            $reference=if($row.camera -ceq 'Lagger'){$plan.lagger}else{$plan.constant}
            Need ($row.status -ceq $reference.status -and $row.Collisions -eq $reference.collisions -and $row.accepted -eq $reference.accepted -and $row.rejected -eq $reference.rejected) ('Reference status/hit/input mismatch '+$key)
            Need (($row.completedIds -join "`n") -ceq ($reference.completedIds -join "`n") -and $row.completed -eq $row.completedIds.Count -and $row.total -eq $library.catalogIds.Count) ('Reference goals mismatch '+$key)
            Need ($row.pauseChecks -eq 2 -and $row.immediateSnapshots -gt 0 -and $row.actualAdvanceCalls -gt 0 -and $row.actualBotPressCalls -eq $row.accepted+$row.rejected) ('Incomplete immediate replay/pause counters '+$key)
            Near ([double]$row.time) ([double]$row.terminalTime) 0 'Terminal time aliases differ';Near ([double]$row.distance) ([double]$row.terminalDistance) 0 'Terminal distance aliases differ'
            Near ([double]$row.timeError) ([Math]::Abs([double]$reference.time-[double]$row.time)) 1e-12 'Reported reference time error mismatch'
            Near ([double]$row.distanceError) ([Math]::Abs([double]$reference.terminalDistance-[double]$row.distance)) 1e-10 'Reported reference distance error mismatch'
            Need ($row.timeError -ge 0 -and $row.timeError -lt 2e-7 -and $row.distanceError -ge 0 -and $row.distanceError -lt 5e-5 -and $row.edgeTimeError -ge 0 -and $row.edgeTimeError -lt 2e-7) ('Reference tolerance exceeded '+$key)
            $score=100.0*$row.completed/$row.total
            if($row.camera -ceq 'Lagger') {
                $bands=@{Novice=@(40,60);Amateur=@(60,70);Confident=@(70,80);Strong=@(85,95);Master=@(100,100)};$band=$bands[$row.Level]
                Need ($row.status -ceq 'Finished' -and $score -ge $band[0]-1e-8 -and $score -le $band[1]+1e-8) ('Lagger band/finish failed '+$key)
            }
            if($row.Level -ceq 'Master'){Need ($row.status -ceq 'Finished' -and $row.completed -eq $row.total -and $row.Collisions -eq 0 -and $row.finishBoosts -eq 1) ('Master100/finish failed '+$key)}
            $sumSnapshots+=[long]$row.immediateSnapshots;$sumAdvances+=[long]$row.actualAdvanceCalls;$sumPresses+=[long]$row.actualBotPressCalls
            $partMaxTime=[Math]::Max($partMaxTime,[double]$row.timeError);$partMaxDistance=[Math]::Max($partMaxDistance,[double]$row.distanceError);$partMaxEdge=[Math]::Max($partMaxEdge,[double]$row.edgeTimeError)
        }
        Need ($part.snapshots -eq $sumSnapshots -and $part.observedAdvances -eq $sumAdvances -and $part.observedPresses -eq $sumPresses -and $part.fieldComparisons -gt $part.snapshots) ('Part summary counters do not match rows '+$batch)
        Near $part.maxReferenceTimeError $partMaxTime 0 'Part time maximum mismatch';Near $part.maxReferenceDistanceError $partMaxDistance 0 'Part distance maximum mismatch';Near $part.maxReferenceEdgeTimeError $partMaxEdge 0 'Part edge maximum mismatch'
        foreach($test in $part.orderEvidence){Need ($test.verified -eq $true -and $test.maximumNumericError -eq 0 -and $test.simultaneousPressPairs -gt 0 -and $test.immediateTwoParticipantSnapshots -gt 0 -and !$order.ContainsKey($test.Id)) 'Invalid/duplicate simultaneous order evidence';$order.Add($test.Id,$test)}
        $snapshots+=[long]$part.snapshots;$fieldComparisons+=[long]$part.fieldComparisons;$advances+=[long]$part.observedAdvances;$presses+=[long]$part.observedPresses;$totalCases+=$part.cases
        $maxTime=[Math]::Max($maxTime,$partMaxTime);$maxDistance=[Math]::Max($maxDistance,$partMaxDistance);$maxEdge=[Math]::Max($maxEdge,$partMaxEdge)
        $partEvidence.Add([ordered]@{path=$path;sha256=Hash $path;batch=$batch;cases=$part.cases;snapshots=$part.snapshots;fieldComparisons=$part.fieldComparisons})
    }
    Need ($totalCases -eq 12794 -and $seen.Count -eq $scheduled.Count -and $idleFrame0.Count -eq 1799 -and $constantFrame0.Count -eq 1799 -and $session.Count -eq 1000) 'Incomplete exact final coverage'
    foreach($choice in $session.GetEnumerator()) {
        Need ($choice.Value.Count -eq 2) ('Missing paired camera choice '+$choice.Key);$a=$choice.Value[0];$b=$choice.Value[1]
        Need ($a.camera -cne $b.camera -and $a.Id -ceq $b.Id -and $a.SignatureId -ceq $b.SignatureId -and $a.seed -eq $b.seed -and $a.frameMode -eq $b.frameMode -and $a.human -ceq $b.human) ('Paired cameras did not use same choice '+$choice.Key)
    }
    foreach($libraryKey in $libraryOrder) {
        $lib=$libraries[$libraryKey];$seenSignatures=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$previous=$null
        $timingSets=UniqueMap;$timingLast=UniqueMap
        foreach($i in 0..19) {
            $row=$session[$libraryKey+'|'+$i][0];$sig=$row.SignatureId
            if($seenSignatures.Count -eq $lib.signatures.Count){$seenSignatures.Clear()}
            Need ($seenSignatures.Add($sig) -or $lib.signatures.Count -eq 1) ('Early signature repeat '+$libraryKey)
            if($lib.signatures.Count -gt 1 -and $null -ne $previous){Need ($sig -cne $previous) ('Signature boundary repeat '+$libraryKey)};$previous=$sig
            $definition=@($lib.signatures|Where-Object {$_.id -ceq $sig});Need ($definition.Count -eq 1 -and $row.Id -cin $definition[0].planIds) 'Runtime session subgroup mismatch'
            if(!$timingSets.ContainsKey($sig)){$timingSets.Add($sig,[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal))}
            if($timingSets[$sig].Count -eq $definition[0].planIds.Count){$timingSets[$sig].Clear()};Need ($timingSets[$sig].Add($row.Id)) ('Early timing repeat '+$libraryKey)
            if($timingLast.ContainsKey($sig) -and $definition[0].planIds.Count -gt 1){Need ($timingLast[$sig] -cne $row.Id) ('Timing boundary repeat '+$libraryKey)};$timingLast[$sig]=$row.Id
        }
    }
    $means=[Collections.Generic.List[object]]::new();$levels=@('Novice','Amateur','Confident','Strong','Master');$minimumGap=[double]::PositiveInfinity
    foreach($pair in $pairOrder) {
        $previous=[double]::PositiveInfinity
        foreach($level in $levels) {
            $lib=$libraries[$pair+'/'+$level];$signatureMeans=[Collections.Generic.List[double]]::new();$scores=[Collections.Generic.List[double]]::new();$finished=0;$eliminated=0
            foreach($signature in $lib.signatures) {
                $times=[Collections.Generic.List[double]]::new()
                foreach($id in $signature.planIds) {$row=$idleFrame0[$id];$times.Add([double]$row.time);$scores.Add(100.0*$row.completed/$row.total)}
                $signatureMeans.Add([double](Mean $times))
            }
            foreach($plan in $lib.plans){if($constantFrame0[$plan.id].status -ceq 'Finished'){$finished++}else{$eliminated++}}
            $mean=[double](Mean $signatureMeans);Need ($mean -lt $previous) ('Signature-uniform runtime time order failed '+$pair+'/'+$level)
            if([double]::IsFinite($previous)){$minimumGap=[Math]::Min($minimumGap,$previous-$mean)};$previous=$mean
            $means.Add([ordered]@{track=$lib.trackId;profile=$lib.profile;level=$level;plans=$lib.plans.Count;signatures=$lib.signatures.Count;signatureUniformMeanTime=$mean;minimumScore=($scores|Measure-Object -Minimum).Minimum;maximumScore=($scores|Measure-Object -Maximum).Maximum;constantFrame0Finished=$finished;constantFrame0Eliminated=$eliminated})
        }
    }
    $expectedOrder=@($source.libraries|Where-Object {$_.difficulty -ceq 'Master'}|ForEach-Object {$_.plans[0].id})
    Need ($order.Count -eq 10 -and $expectedOrder.Count -eq 10) 'Missing simultaneous-order pair coverage'
    foreach($id in $expectedOrder){Need ($order.ContainsKey($id)) ('Missing simultaneous-order plan '+$id)}
    foreach($entry in $freeze.sourceFiles){Need ((Hash $entry.path) -ceq $entry.sha256) ('Source changed during aggregation '+$entry.path)}
    $result=[ordered]@{status='PASS';checks=$checks;checkedAt=[DateTime]::UtcNow.ToString('o');cases=$totalCases;publishedPlanCases=1799*6;sessionCases=50*20*2;plans=1799;libraries=50;signatureGroups=490;sessionChoices=1000;pairedCameras=$true;assignedCasesFiltered=0;parts=$partEvidence;sourceReportSha256=$finalSha;sourceFreezeSha256=Hash $freezePath;sourceHashes=$baselineHashes;sourceStable=$true;snapshots=$snapshots;fieldComparisons=$fieldComparisons;observedAdvances=$advances;observedPresses=$presses;maximumReplayNumericError=0;maxReferenceTimeError=$maxTime;maxReferenceDistanceError=$maxDistance;maxReferenceEdgeTimeError=$maxEdge;minimumAdjacentSignatureUniformTimeGap=$minimumGap;signatureUniformRuntimeMeans=$means;simultaneousOrderPairs=$order.Count;scope='Aggregates three existing physical runtime runs only. Exact all-plan/camera/frame and20-choice session coverage, same-choice paired cameras, current source hashes, immediate replay counters, reference tolerances and actual Lagger-frame0 signature-uniform means. No new simulation. Field-comparison totals are reported by the per-part immediate public-field comparers.'}
    $result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $outputPath -Encoding utf8
    [pscustomobject]@{status='PASS';checks=$checks;cases=$totalCases;snapshots=$snapshots;fieldComparisons=$fieldComparisons;output=$outputPath}|ConvertTo-Json
}catch {
    [ordered]@{status='FAIL';checkedAt=[DateTime]::UtcNow.ToString('o');checks=$checks;error=$_.Exception.Message;scope='Aggregation failed; no partial PASS.'}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $outputPath -Encoding utf8
    throw
}finally {Pop-Location}