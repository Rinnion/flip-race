param()
$ErrorActionPreference='Stop'
$repository=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$rawPath=Join-Path $repository 'Verification/stochastic-bots-training-initial.json'
$rawHash=(Get-FileHash -LiteralPath $rawPath -Algorithm SHA256).Hash
if($rawHash -ne '715C857AA5C4C3EE39D379AFF4204485356F7FDBE8B9D65844CCA9B83F1F30B9'){throw 'Unexpected initial raw source'}
$mapPath=Join-Path $repository 'Verification/stochastic-bots-map.json'
$map=Get-Content -LiteralPath $mapPath -Raw | ConvertFrom-Json
$bounds=@{}
foreach($pair in $map.pairs){$items=@{};foreach($item in $pair.opportunities){$items[$item.id]=$item};$bounds[$pair.trackId+'/'+$pair.profile]=$items}
$patterns=@(
 [pscustomobject]@{Name='CascadeChosenNoviceAvoidHit';Track='rhythm-cascade';Profile='Chosen';Level='Novice';Kind='Obstacle';Intent='avoid';Actual='hit'},
 [pscustomobject]@{Name='CascadeFastNoviceAvoidHit';Track='rhythm-cascade';Profile='FastFlip';Level='Novice';Kind='Obstacle';Intent='avoid';Actual='hit'},
 [pscustomobject]@{Name='CascadeFastNoviceFinishMiss';Track='rhythm-cascade';Profile='FastFlip';Level='Novice';Kind='Finish';Intent='collect';Actual='missed'},
 [pscustomobject]@{Name='MixedFastNoviceAvoidHit';Track='mixed-tricks';Profile='FastFlip';Level='Novice';Kind='Obstacle';Intent='avoid';Actual='hit'},
 [pscustomobject]@{Name='FlowStrongHitAvoided';Track='full-flow';Profile='';Level='Strong';Kind='Obstacle';Intent='hit';Actual='avoided'}
)
function Plain([Text.Json.JsonElement]$element){return ($element.GetRawText() | ConvertFrom-Json)}
$stream=[IO.File]::OpenRead($rawPath)
try{$document=[Text.Json.JsonDocument]::Parse($stream)}finally{$stream.Dispose()}
try{
 $root=$document.RootElement
 $geometryChecks=[Collections.Generic.List[object]]::new()
 foreach($source in $root.GetProperty('publishedManifest').GetProperty('sourceFiles').EnumerateArray()){
  $path=$source.GetProperty('path').GetString()
  if($path -match '/(Tracks[^/]*|ObstacleGeometry|FinishTargetGeometry|StochasticTrackMap)\.cs$'){
   $published=$source.GetProperty('sha256').GetString();$mapHash=$map.sourceHashes.PSObject.Properties[$path].Value
   if(-not $mapHash -or $mapHash -ne $published){throw ('Geometry reference source mismatch '+$path)}
   $geometryChecks.Add([ordered]@{path=$path;sha256=$published;matches=$true})
  }
 }
 $witnesses=[Collections.Generic.List[object]]::new()
 foreach($pattern in $patterns){
  $candidates=[Collections.Generic.List[object]]::new()
  foreach($case in $root.GetProperty('cases').EnumerateArray()){
   $spec=$case.GetProperty('case');$track=$spec.GetProperty('trackId').GetString();$profile=$spec.GetProperty('profileId').GetString()
   if($track -ne $pattern.Track -or $spec.GetProperty('difficulty').GetString() -ne $pattern.Level -or ($pattern.Profile -and $profile -ne $pattern.Profile)){continue}
   $intentions=@($case.GetProperty('intentions').EnumerateArray());$pairBounds=$bounds[$track+'/'+$profile]
   foreach($intent in $intentions){
    if($intent.GetProperty('kind').GetString() -eq $pattern.Kind -and $intent.GetProperty('intent').GetString() -eq $pattern.Intent -and $intent.GetProperty('actualOutcome').GetString() -eq $pattern.Actual){
     $at=$intent.GetProperty('atDistance').GetDouble();$prior=0
     foreach($earlier in $intentions){if($earlier.GetProperty('atDistance').GetDouble() -lt $at -and ($earlier.GetProperty('intent').GetString() -eq 'skip' -or $earlier.GetProperty('actualOutcome').GetString() -in @('hit','failed'))){$prior++}}
     $id=$intent.GetProperty('id').GetString()
     $candidates.Add([pscustomobject]@{Case=$case;Intention=$intent;PriorDisruptions=$prior;Position=[double]$pairBounds[$id].position;Seed=$spec.GetProperty('seed').GetInt32();Profile=$profile})
    }
   }
  }
  if(-not $candidates.Count){throw ('Requested witness pattern absent: '+$pattern.Name)}
  $selected=$candidates | Sort-Object PriorDisruptions,Position,Seed,Profile | Select-Object -First 1
  $case=$selected.Case;$spec=$case.GetProperty('case');$target=$selected.Intention;$at=$target.GetProperty('atDistance').GetDouble();$pairKey=$spec.GetProperty('trackId').GetString()+'/'+$spec.GetProperty('profileId').GetString();$pairBounds=$bounds[$pairKey]
  $priorIntentions=[Collections.Generic.List[object]]::new()
  foreach($earlier in $case.GetProperty('intentions').EnumerateArray()){
   if($earlier.GetProperty('atDistance').GetDouble() -lt $at -and ($earlier.GetProperty('intent').GetString() -eq 'skip' -or $earlier.GetProperty('actualOutcome').GetString() -in @('hit','failed'))){$priorIntentions.Add((Plain $earlier))}
  }
  $priorHits=@($case.GetProperty('hitIds').EnumerateArray() | ForEach-Object {$_.GetString()} | Where-Object {$pairBounds['obstacle:'+$_].endPosition -lt $at-2e-7})
  $priorFailed=@($case.GetProperty('failedIds').EnumerateArray() | ForEach-Object {$_.GetString()} | Where-Object {$pairBounds['positive:'+$_].endPosition -lt $at-2e-7})
  $priorMisses=@($priorIntentions | Where-Object {$_.intent -eq 'skip' -and $_.actualOutcome -eq 'missed' -and $pairBounds[$_.id].endPosition -lt $at-2e-7} | ForEach-Object {$_.id})
  $targetObject=Plain $target
  $targetObject | Add-Member -NotePropertyName selectedInputs -NotePropertyValue $null
  $targetObject | Add-Member -NotePropertyName selectedInputsAvailability -NotePropertyValue 'NOT_RECORDED: initial raw sampler preserves only initiallySelectedInputCount, not the array or emitted press tape; reproduce from exact seed/source to obtain it.'
  $witnesses.Add([ordered]@{
   pattern=$pattern.Name;caseId=$spec.GetProperty('id').GetString();seed=$spec.GetProperty('seed').GetInt32();trackId=$spec.GetProperty('trackId').GetString();profileId=$spec.GetProperty('profileId').GetString();difficulty=$spec.GetProperty('difficulty').GetString();camera='Lagger';frameSeconds=1.0/60;human='idle'
   rawCaseIndex=$spec.GetProperty('index').GetInt32();rawJsonPointer='/cases/'+$spec.GetProperty('index').GetInt32();settings=Plain ($case.GetProperty('parameters'))
   target=$targetObject;targetOpportunity=$pairBounds[$target.GetProperty('id').GetString()]
   minimalSelection=[ordered]@{matchingIntentions=$candidates.Count;priorDisruptionDecisions=$selected.PriorDisruptions;rank='fewest earlier-decided hit/skip/failed intentions, then earliest physical opportunity position, then lowest seed/profile. Diagnostic selection only, not cohort filtering.'}
   priorRelevantDecisions=$priorIntentions.ToArray();definitelyPriorFacts=[ordered]@{hitIds=$priorHits;failedRewardIds=$priorFailed;settledSkippedGoalIds=$priorMisses;derivation='Final raw sets plus unchanged map EndPosition < target decision AtDistance-2e-7 prove these contacts/windows precede the decision. No precise event timestamp or execution-state projection is inferred.'}
   terminal=[ordered]@{status=$case.GetProperty('physicalStatus').GetString();time=$case.GetProperty('terminalTime').GetDouble();distance=$case.GetProperty('distance').GetDouble();collisions=$case.GetProperty('collisions').GetInt32();boostsEarned=$case.GetProperty('boostsEarned').GetInt32();fullFinish=$case.GetProperty('fullFinishCount').GetInt32();completedGoals=$case.GetProperty('completedGoalCount').GetInt32();totalGoals=$case.GetProperty('goalCount').GetInt32();hitIds=Plain ($case.GetProperty('hitIds'));failedRewardIds=Plain ($case.GetProperty('failedIds'))}
   limits='AtDistance/AtTime refer to intention/planning, not actual collision/collection/Press times. Reason is a bounded-search explanation, not proof of mathematical incompatibility. No new simulation or causal diagnosis was performed.'
  })
 }
 if((Get-FileHash -LiteralPath $rawPath -Algorithm SHA256).Hash -ne $rawHash){throw 'Raw dataset changed'}
 $report=[ordered]@{
  status='EXISTING_RAW_DIAGNOSTIC_WITNESSES';sourceJson='Verification/stochastic-bots-training-initial.json';sourceJsonSha256=$rawHash;assemblySha256=$root.GetProperty('assemblySha256').GetString();manifestSha256=$root.GetProperty('manifestSha256').GetString();controllerSourceSha256='3CF865EFF56CD7D24A5888BBF8362AC370D6933B3667E14BDF990032436873D0';currentControllerIsDifferent='EB0C4B445CD6DCE8EC421BB5B43194120D91164D4A816B13796ECDC696D963B9';heldOutSeedsRun=$false;newRaces=0;parameterChanges=0
  derivationScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;geometryReference=[ordered]@{path='Verification/stochastic-bots-map.json';sha256=(Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash;unchangedGeometrySourceChecks=$geometryChecks.ToArray()}
  interpretation='Five intentionally selected minimal diagnostic witnesses from exploratory TRAINING only. They neither prove legitimate incompatibility nor identify an execution defect. SelectedInputs/actual action chronology were not recorded; missing values are explicit. Original source publication is immutable; a current-controller reproduction must name its different hash.'
  reproduction='Original initial CreateInitial(track,exactPreset,difficulty,uintSeed), actual CreateStochasticBotRace Lagger, ordinary Press human0, controller(game,1), Advance(1/60) until bot terminal. Use recorded seed/settings and name original3CF865 vs repairedEB0C4B source. No reconstruction or replay was executed by this extractor.'
  witnesses=$witnesses.ToArray()
 }
 $output=Join-Path $repository 'Verification/stochastic-bots-training-intent-witnesses.json'
 [IO.File]::WriteAllText($output,($report | ConvertTo-Json -Depth 35))
 foreach($w in $witnesses){[pscustomobject]@{pattern=$w.pattern;caseId=$w.caseId;id=$w.target.id;intent=$w.target.intent;actual=$w.target.actualOutcome;selectedInputs=$null;initiallySelectedInputCount=$w.target.initiallySelectedInputCount;priorRelevantDecisions=$w.priorRelevantDecisions.Count} | ConvertTo-Json -Compress}
 Write-Output ('WITNESSES '+$witnesses.Count+'; SHA256 '+(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash+'; '+$output)
}finally{$document.Dispose()}