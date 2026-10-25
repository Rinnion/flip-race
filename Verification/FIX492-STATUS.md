# FIX492 status

Source: direct human request in Catalytic chat01a13ea2-10f9-7821-b785-5c3a7e30b079, turn01a171e0-8667-7401-ae79-fd5a9164790a (read_thread verified): RedmiNote14 physical2400x1080, near-edge paws seem partially unresponsive; move inward horizontally, vertical placement is fine. Assume game paws, as stated to human. Baselineef2d730/4.9.1.

Owner: Progatik Sol/high; small bounded change handled directly. Preserve Catalytic's pre-existing uncommitted Verification/FIX491-RELEASE.md (hash in fix492-before.json), never stage it as this feature. No Core/tracks/bonus/input semantics change.

|ID|Scope|Status|Evidence/next|
|---|---|---|---|
|P01|Compactlandscape horizontal inset40 instead8, keepY/56size, PCunchanged|verified|RED compact40/desktop8 inset20!=52 onoldcode; GREEN aftercompactinset40, Yand56unchanged.|
|P02|Entire visible rect clicks, own player/singleedge/multitouch/phase/background preserved|verified|GREEN actualUpdate:800x360/960x432/568x320/PC, DPR1/2/2.5/3, asymmetricsafe, nineinsideedgepoints/eachplayer; exactfeedback + opponentuntouched; existing hold/phase/twoBegan preserved.|
|P03|Regular4.9.2 Web+ZIP/extraction,800x360/960x432/568x320/PCnative|verified|FreshZIP/5four-wayhashes; nativeextraction800edgeclicksPASS,console0. Stable4184 now4.9.2; finalreload/docs/commit next.|

Hardware edge rejection has NOT been reproduced and its OS/browser cause is unproven. RED tests the directly requested layout/input contract, not the physical Redmi symptom.
Next: regular4.9.2 WebGL, native800x360/960x432/568x320/PC, package/extract. Core hashes unchanged and pre-existingFIX491reporthash preserved.

Targeted UI GREEN:22854checks,128pawcases,8actualUpdateequivalence streams,46930Updatecalls; 864fresh actualBeganedge/corner/center streams with exactpawfeedback/opponentuntouched. Physical2400x1080 modeled as800x360@DPR3 and960x432@DPR2.5. Native browser CSS viewports remain a separate check. Build PID105872; logLogs/fix492-final-build.log.

CANDIDATE READY: http://127.0.0.1:4214/ serves actual Builds/WebGL-v4.9.2-final, version4.9.2-trial. RegularUnityFLIP_BUILD_OK/exit0. Candidate server PID92088. Root owns independent800x360split/edge check; Progatik native960x432/568x320/PC and package. Core/production frozen.

Native candidate PASS: root800x360split49/695 vsold17/727 (Y36/293unchanged); own960x432ghost49/855,568sharedadjacent188/463,Y253,PCsharedopposite53/1171 unchanged. Actualleft/rightinsideedge clicks each triggeronlyownsingleaction; no consoleerrors. RootUIacceptancePASS. ZIP8888496bytes/SHA9385b21ffe7ad91a3b0117736e9e6fee2cc707ddb1ac5296982a7711d0946466; staticfresh5file4wayhashesPASS; extraction4215 browsernext.

DELIVERY VERIFIED: actual unpacked4215 version4.9.2/800x360 pawrects49/695, sameY/56; twoinsideedge native clicks oneownaction; console0. Stable4184→Builds/WebGL-v4.9.2-final PID107292. Root UI/source independentacceptancePASS. Next: finalstable reload/docs/commit, no further gameplay tests. Pre-existingFIX491report remains untouched/unstaged.

FINAL P01/P02/P03 VERIFIED. Stable4184 reload actual4.9.2 PASS; extractedZIP800edge streamsPASS; Coreunchanged and originalFIX491reporthash unchanged. Source/UI independentacceptance byCatalytic PASS; delivery artifacts ready for final read. Next: exactown temporaryservercleanup/localcommit/final. No additional matrices.

COMPLETE: Catalytic FINAL ACCEPTANCE P01-P03 PASS, independently ZIPhash/5rootentries/four-wayhashes and actual4184version4.9.2/error0. All required work done. Temporary4214/4215 ownservers stopped after exactPID/cmdline verification;4184PID107292 and all4.9.1/4.9.2builds/ZIP preserved. No active specialists. Pre-existingFIX491note unchanged and excluded fromcommit. Next action:none; final local commit/report.
