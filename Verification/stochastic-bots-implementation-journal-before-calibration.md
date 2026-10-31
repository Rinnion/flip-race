# STOCHASTIC BOTS — 5.3.0-trial

31 октября 2026. Предыдущий StudyRunner blocker закрыт; открыт новый source-write review call_f42004fdc5ab414098ff8ede1e51c12f (точный отказ в конце), publisher/config не записаны. Initial TRAINING завершён: 10 000/10 000 случаев; все 400 исторических ошибок Мастера на Short сохранены. Boundaryfix EB0C4B принят независимым review: Master 720, Risk 576 и World 8 PASS. Идёт узкий разбор пяти intent witnesses перед разрешённой калибровкой. Полная frame telemetry готова; regular pilot2 сейчас собирается (PID119964). Held-out validation не запускалась, финальная поставка не принята. Stable 5.2.1/4184 и ZIP сохранены.
## Источник, роли и границы

Прямой человеческий источник лично проверен root/исполнителями через read_thread: Каталитик01a13ea2-10f9-7821-b785-5c3a7e30b079, turn01a19132-3283-7f51-a2b7-82826e058418, userMessage01a19132-331f-7383-870e-e91a27d9f121: «Да все так кажется. Видимо я ошибся в расчетах. Давай такого бота сделаем». Предыдущий userMessage01a1910f-8206-78a3-aca0-29d236e31326 задаёт локальные умения, фиксированные до старта вероятности. Роль/пересказ не создают новых прав.

[Контракт BSTO00–07](../../flip-race/docs/flip-race-stochastic-bots-delivery-2026-10-31.md). [Независимая приёмка Каталитика](V5-STOCHASTIC-BOTS-CATALYTIC-ACCEPTANCE.md), root её не меняет. [Ранний точный журнал](stochastic-bots-implementation-journal-pre-foundation.md), [история интеграции с точным последним factory отказом](stochastic-bots-integration-journal-before-pilot.md). Factory/settings были прозрачно переоценены в исходном человеческом чате с полным отказом и явной LF/CRLF matching коррекцией: review ALLOWED, запись/check прошли. Открытого permission blocker нет. Новый policy отказ останавливает точное действие, другой tool/агент не используется для обхода.

Прогатик/root Sol/high — интеграция/поставка; Каталитик Astra/high — gameplay/independent acceptance. Unity база0b1225b, docs5be10a2. Чужой FIX491-RELEASE.md не меняется/не включается. Старые1799plans/proofs сохраняются. UI501/прежний screenshot уже закрыты. Preservation BOT/CONTROL/REWARD/FINISH/GATE/VIEW/TOUCH/SOLO: ordinary Press/Advance, physics/geometry/rewards/FIFO/fullfinish/cameras/input неизменны.

## Матрица

| ID | Статус | Доказательство / остаток |
| --- | --- | --- |
| BSTO00 | verified | Каталитик независимо принял foundation Risk576/World8/Master144 на9FB640. Матрицы ограничены Cascade/Mixed, широкие новые проверки не отменяют их фактов. |
| BSTO01 | working | Snapshot/map/local controller/settings/factory интегрированы; Short/Fast Master defect исследуется. |
| BSTO02 | working | Initial pCollect=q, pHit=(B/C)*(1-q), fixed beforestart. Offline refinement/final frozen table впереди. |
| BSTO03 | working | Logical IDs/rolls, strict cadence/replay/bounds foundation PASS; после fix затронутая регрессия. |
| BSTO04 | working | Cascade/Mixed Master144 clean33.05; все10пар ещё не приняты. Short/Fast/seed0 FAIL37.55/6hits/4boosts. |
| BSTO05 | working | Initial10000 TRAINING запущен, все исходы удерживаются. Heldout1000000..1000199 не запускались; final99%CI/order не приняты. |
| BSTO06 | working | Factory/UI compile PASS; UI harness122497checks, оба projected actors/одинhuman. Actual WebGL UI/performance впереди. |
| BSTO07 | working | Regular pilot5.3.0-trial-pilot готов; finalbuild/ZIP/freshextract/HTTP/docs/acceptance впереди. Stable4184 прежний. |

## Текущие исходники и проверки

Controller SHA3CF865EFF56CD7D24A5888BBF8362AC370D6933B3667E14BDF990032436873D0; Settings392BD5A8060A1DB30F75EA4772B98D1B86958A7A05B029A94E8059AE6F9BED7B; RaceSimulationB97A3C9F3D54C34288AEBD48274CC2F451197818CC3DF77A2DA42A30BE0F7A5C; Map374384E8DF23CBC828355E7065FA30127750D38DA51376855DFD9FFAD092470D. Это initial publication freeze; новый fix создаст новую ревизию, старый опыт не подменяется.

GameState.StochasticBot/settings readonly. CreateStochasticBotRace: human0+bot1, legacy BotPlan=null. Raw q/seed ctor отклоняет игры с новым metadata или legacy tape, обычные Solo/Party training допускает. P2 metadata bypass закрыт.

Snapshot metadata PASS7916checks/231snapshots/4158pairedcontinuations/sourceStable. stochastic-bots-snapshot-metadata.json SHAFF051F5EB9186B23643795790BF5DD137202BB7FA576A0F6D8728A72EC70B27F. Старые proofs не перезаписаны.

UI source newfactory/controller/freshattemptuintseed; active Presentation/Web catalog/tape references отсутствуют. Actual q/pCollect/pHit/decisions/planning/truebounds diagnostics20Hz. P2 projectedPlayers исправлен на2, controls/hero/ready/paw=1human. stochastic-bots-ui.json SHA8620A43E0D41E277C428026559D0ED2E84902D04DE052EF7ADCBC50A3E59C56C: PASS122497checks/2468layouts/1302setups/48inputs/5actualUpdate races. FlightUiCompile PASS0warnings/errors. Это не actual browser acceptance.

ParityRunner existing engine-parity-fixtures.tsv.gz PASS40228JS/C#snapshots/1448208fields/tolerance<1e-8; все встроенные engine/control/reward/finish/flight/modes/gates suites PASS. Baseline не регенерировался.

## Сохранённая foundation

Risk576 masterfix JSON SHA2A960C9D70810F2375F37578843232E211C64190CD4F7F3FB7DC683883685F56: PASS576/4237976 immediate snapshots/268300804fields/replay0/sourceStable. Requested≤1000, actual1035.8998941426098, pointwise overrun35.89989414260981≤55;22984944observations==steps,violations0.

World8 masterfix JSON39152A85FE07FFDE92119A4F4BDD20282E40914D8C207AE01DE20674ED238E37: PASS5780872checks/71974snapshots/9335087fields; human continuation/pause/real Constant elimination. Master144 foundation JSON48AFB31290530B348CF45B479DE5C6DEF2A9D5F9F561664B9F60E91482668333: four Cascade/Mixed pairs×12seeds×3cadences,clean33.05,12actual signatures perpair. Dedicated managed performance JSONF780E08BD18854F09AB0EE923A9319FBA71CE1CA16A69EAB5ADC62C735AA3CC6: Planp95 37.3122ms/max51.0927ms, НЕ performance acceptance. Все прежние FAIL/исторические proofs сохранены.

## Initial TRAINING — bots_first_routes Sol/high

Immutable DLL Tools/StochasticStatistics/Published/initial-20261031T104142224-a60eb265/StochasticStatistics.dll SHA0685B469A66930930EFEA185FDBEA632D2AE3CAAA974AAFAADB0D7111788DB09. Manifest615A50EA08B28AE3C842ECAFB4C6766E2D343E525F4439C8F3B1EA8B9A014B54. Initial50tableBBC0050E2E5A184EFB5F5851E8984D9E1C86FEBAE00FE13AD58B70B7915741C4;10000schedule8177827AE97AF9DAE2DA9BEFE24EE28B195E17C2B13396803DD64A600421426D;protocolFB15D22DBC1D87BCB06DC0A13D8D9AC72BAE9622FD4F9A3A9FA983A002F47B4F. Все34publicationfiles сверены. Published/bin/obj ignored, tracked InitialPublicationManifest.json byte-identical.

Команда dotnet <exactDLL> --training --start-seed0 --count200 --parallelism3; root GO после metadata/finalUI checks. Последний progress4000/10000. Нет reroll/filter/retry/auto-calibration; ошибки/реальные terminal времена/выбывания сохраняются, incomplete CI=null, unrun явно. Initial CI exploratory, не validation. Heldout1000000..1000199 зарезервированы/ещё не читались. Параметры заморозить и preregister sample expansion до validation; не fit по повторяемой validation.

Первый MasterInvariant: full-short/FastFlip/Master/seed0/Lagger,Finished37.54999999999847,distance7920,6collisions,4boosts,fullfinish1,11/22goals. Hits full-short-10-low,-12-after-stone,-14-high,-15-low,-7-before-stone,-9-large-pad; все исходы остаются в initial report. Новый дефект вне прежней Cascade/Mixed foundation. Training продолжает исходную DLL неизменной.

Parent independent P2: post-run manifest read exception может попасть в Main catch и стереть completed outcomes как actualCases0/allunrun. Исправление WORKING sampler назначено bots_first_routes ПОСЛЕ текущего batch; immutable DLL не менять. Postrun fault должен сохранять completed/unrun + publicationStable=false/fatalError; setupfailed0 только до запусков. Малый fault-injection regression, не10k повтор.

## Pilot и владельцы

Regular WebGL build FLIP_BUILD_OK9300946bytes/5publicfiles,5.3.0-trial-pilot,Builds/WebGL-v5.3.0-pilot,Logs/stochastic-bots-pilot-build.log. Owned PID108600 завершён exit0. UnityHub36800 не трогался. Cache/CDP timeouts не скрываются, build успешен. Это performance pilot, не final candidate.

Root резервирует отдельный server4225 и собственную browser tab; пока не запущены. Каталитик CUA не трогает. Чистый замер после завершения training CPU load. Stable http://127.0.0.1:4184/ продолжает5.2.1. Не заявляется physicalphone/performance acceptance/готовность5.3.

bots_complex_routes Sol/high владеет controller/.meta+LocalProbe: deterministic Short/Fast seed0 repro→ranked hypotheses→minimal sourcefix→все10pairMaster seeds0..11/cadences/Constant. Без physics/reference/probability changes и ослабления assertions. bots_first_routes Sol/high: immutable training/report, working postrun-retention fix после batch. Root: STATUS/pilot/browser/integration. bots_ui завершил files/checks, UI frozen.

Следующий шаг: Short/Fast repro/rootcause, initial training report, затем чистая WebGL performance probe. Offline calibration/final heldout validation/finalbuild/archive/docs/independent acceptance обязательны. Новые gameplay choices Каталитику; обычные repairs по контракту без нового product approval.
## Checkpoint: initial TRAINING завершён, pilot HTTP готов

Initial10000/10000,0unrun,publicationStable=True,400caseErrors,nonMasterErrors0. All400MasterInvariant failures: full-short/Chosen seeds0..199 mean38.19999999999842 и full-short/FastFlip seeds0..199 mean37.54999999999839. Other8Mastercohorts all200clean,mean33.05. Все failed terminal времена включены в descriptive CI, не фильтровались. Raw Verification/stochastic-bots-training-initial.json172327811bytes SHA715C857AA5C4C3EE39D379AFF4204485356F7FDBE8B9D65844CCA9B83F1F30B9;TXT E50BCA958385C364BD917DFD8C156BD0CB4214B708F128C4AAEABE333EF6ED66. Heldout не запускался. Sampling workers завершены; working postrun-retention P2 исправляется отдельно без замены публикации.

ShortFastseed0 reproduced twice through actual factory/metadata ctor37.55/6hits/4boosts. Original FAIL Tools/StochasticLocalProbe/master-short-repro.json SHAA4342790BBB815CF1AF38175D33644414B4CF54E6F01A27BB9EB0BB1AD6E6700. План на дистанции2399.9999998807907 заменял предстоящий2400edge; Clip(reference,start+1e-6,end) исключал ещё не нажатое действие. Изменение только lowerbound start+1e-6→start; existing lastEmittedDistance guard сохраняется. Target дваждыPASS33.05/0hits/9fullyservedboosts/fullfinish, строгий ordinary replay; near/exactboundary и once-only checks PASS. Владелец готовит all10pairs×12seeds×3cadences×2cams, ждёт CPUfree после раннего WebGL замера. Новая source revision ещё не зафиксирована; pilot/immutabletraining относятся к3CF freeze, не к этому fix.

Owned pilot serverPID125244, http://127.0.0.1:4225/ → Builds/WebGL-v5.3.0-pilot; HTTPversion5.3.0-trial-pilot matched, stable4184/5.2.1 matched. Root-owned browser tab17,1280×720, Каталитик CUA не трогает. Идёт первая WebGL race без heavyCPU training/matrix; actual UI confirmed new StochasticBotController, readonly q/probabilities/seed и2projected actors/1human. Финальное browser ownership release будет явным.

## Checkpoint: ранняя WebGL проба и подготовка frame telemetry

Без одновременно работающих training/Unity build/heavy matrix выполнены две настоящие UI races в pilot3CF,1280×720. Long/Chosen/Amateur/Lagger seed1687041858: bot34.45,1hit,23Plan samples,p95Plan11.4ms/max11.6001ms. Cascade/Chosen/Amateur/Lagger seed3659017165: bot34.75,32Plan samples,p95Plan22.2999ms/max25.2001ms. Ноль Plan samples>50ms. Это ТОЛЬКО Plan cost, не whole-frame p95≤2ms acceptance. Rawreadonly diagnostics stochastic-bots-pilot-early.json + screenshots pilot-long/cascade.png. Исходники pilot соответствуют до-clippingfix3CF, не новой EB0C4B ревизии.

Actual UI retry создаёт attempt3/seed1766177062, без повторного выбора уровня. На568×320 кнопки/pause читаемы; Escape реально paused, elapsed5.187 не меняется между независимыми чтениями, seed/attempt те же. Screenshot pilot-compact-paused.png. Этот третий race НЕ performance-cohort: expanded matrix уже запущена; игру оставили на паузе.

bots_ui владеет дополнительной узкой instrumentation: BotsPresentation.cs, FlipDiagnostics.cs, Update host FlipRaceGame.cs и при необходимости readonly FlipDiagnostics.jslib bridge. Все active bot racingframes включая0plan измерять весь Advance (ordinarysim включена как консервативная upperbound), whole diagnostics build/serialize/publish отдельно. Bounded10000samples, overflow explicit, histogram.05ms upperboundp95, exactmax/sum/over50; noframefilter/coldfilter/postterminaldilution. Countdown/pause/suppressed explicitcohorts; terminaltransition included, freezeafterbotterminal. Scalar20Hz, rawoncepersistentterminal DOMdataset, отдельныйterminalexportcost+previousdiagordinal без саморекурсивнойserialization. UIcompile/meaningfulsmallchecks после изменений; finalWebGLbuild нужен.

Core minimalfix EB0C4B445CD6DCE8EC421BB5B43194120D91164D4A816B13796ECDC696D963B9 frozen. Target12PASS64854immediate wholeplayers ordinaryreplay; expanded720cases10pairs×12seeds×3cadences×2cams работает у bots_complex_routes (session79767), новаяactualfactory/metadatat ctor. Старая foundation/history не перезаписана.

Working statistics postrun-retention P2 исправлен: CheckAfterCohort ловит недоступный manifest/hash exception в publicationStable=false/fatalError и сохраняет накопленные completed/unrun. Syntheticmissingmanifest fixture удерживает2actual observations(inclqualityError)+1unrun. Releasecompile0warnings/errors+selfcheckPASS. WorkingProgram49B2875491B865CF0064C5175A19E86810D4EE9F4D23F880A22AFD9FB6BDEE86. ImmutableDLL/manifest/initialJSON/TXT не менялись.

Для Git большой initialraw JSON оставлен локально и точечно ignored; сохранён lossless stochastic-bots-training-initial.json.gz7904990bytes SHA084248BF48BB5A3CAFEC57D809BE564483B360B72766921065DD0807A660A7A8. Stream decompression SHA точно715C857AA5C4C3EE39D379AFF4204485356F7FDBE8B9D65844CCA9B83F1F30B9, manifest stochastic-bots-training-initial.archive.json. Gzip не теряет ни одного случая/поля. Scoped.gitattributes сохраняет hashed JSON/publication/protocolbytes без line-endingconversion.

Каталитик назначил independent Astra/high read-only training balance review; offline calibration пока не выбрана, ждёт measured facts и закрытие execution defect. Held-out по-прежнему не запускался.

## Checkpoint: boundaryfix принят, q<1 регрессия и intent witnesses

Master expanded720/720PASS322.77sec/sourceStable наEB0C4B445CD6DCE8EC421BB5B43194120D91164D4A816B13796ECDC696D963B9:5tracks×2profiles×12seeds×3cadences×2cams,allclean33.05/0hits/9fullyservedboosts/fullfinish.4,003,156immediate ordinarywholeplayersreplay;3,837,252ForecastObservations==Steps,violations0;actualmax1009.547834/pointwiseover9.547834≤55;caps/cadence toleranceunchanged. Everypair/camera12actualinputsignatures. Bytecopy Verification/stochastic-bots-master-all-pairs-clippingfix.json SHA85C255291B33E2985133E362AD8A584E60932FD84183A66997A93E191572C866. OriginalFAIL preserved as stochastic-bots-master-short-original-fail.json A4342790BBB815CF1AF38175D33644414B4CF54E6F01A27BB9EB0BB1AD6E6700. Каталитик независимым Sol/high review прочитал sourceguard/proof и принял этотузкийfix безповторнойматрицы; physics/settings/map неизменны.

RootstrictRisk smoke16 наEB PASS exit0, отдельный новый stochastic-bots-risk-clippingfix-smoke; full576 сейчасsession74187, отдельный stochastic-bots-risk-clippingfix-full. World8 послеэтого. Бывшийdefault smoke запуск был остановлен autoreview: exactcommand dotnet run --project Tools/StochasticRisk/StochasticRisk.csproj -- --smoke --max-horizon=1000 --max-trial-horizon=1055 --max-trial-overrun=55 --max-candidates=96 (workdirUnity,require_escalated). Точныйотказ:

```
This action was rejected due to unacceptable risk.
Reason: Smoke-запуск записывает в стандартный output stem и может перезаписать уже принятые Verification/stochastic-bots-risk.json/.txt; перед запуском нужно сохранить эти артефакты и задать отдельный output path.
Do not bypass this rejection through a workaround or indirect execution. Continue with a safer alternative, or carry out checks to prove that the action is authorized or low risk before trying again. Complete unaffected work without asking for confirmation. Report anything that remains blocked, clarify why it was blocked by auto-review, inform the user of the risk and ask for approval.
```

Отклонённыйзапуск не исполнился. Поддержанная безопаснаяальтернатива: Tools/StochasticRisk/Program.cs теперьпринимаеттолько scoped --output-stem=Verification/stochastic-bots-risk-[a-z0-9-]+ и отказывает ДОraces при existingJSON/TXT, outputassertions/schedule/limitsне менялись. Read-onlySHA старогоdefaultJSON/masterfix-fullсовпадает2A960C9D...,TXT E42075F6...; данныеужеимелисьвacceptedbytecopies. Отдельные review дляtoolpatch и namedrun ALLOWED. Нет обходаотказа/новогоблокера.

Root independentпрочитал working sampler CheckAfterCohort + actual2observed/1unrun faultfixture. Каталитик независимымSol/highreviewзакрылpostrunretentionP2,immutableDLL0685B469совпадает.

Initial compact50cohort summary готов (безraces): stochastic-bots-training-initial-summary.json SHA891CE779E9085F8C24FE13BBB04A9AFD2A97011086A9472C0F9E22AB20E5B985;TXT276EA57A55EAA593DA4B027692DF0AA9384355178EEA48D719E7FBADFD9BF123. ВсеfailedMastertimesсохранены, noheldout. Каталитик/Astrahigh измеренияпрочитали: сохранитьpCollect=q; одинkнаtrack/profileдля4младшихуровней,pHit=k*(B/C)*(1-q),Master1/0. Большинствогруппbias±.12неподгонятьпошуму. CascadeChosenoffset+.396/.284/.178/.133,Fast+.307/.170/.120/.108. Доcalibration ограниченноисследовать5perIDwitnesses: Cascade avoid→hit/finishmiss,MixedFastavoid→hit,FlowStronghit→avoided. Исполнительныйдефектнепрятатькоэффициентом; легальнуюнесовместимостьdocument. bots_first_routes извлекаетexistingrawwitnesses, SelectedInputs тамнеписались→явноnull/notRecorded, непроизводитьихзаднимчислом. bots_complex_routes воспроизводит5boundedcases наEB actualfactory/ordinaryreplay; Corefrozenдоrootcause. Не расширятьнаполнуюновуюматрицубезпричины.

Послеwitness review — training k-sensitivity дляCascade,однаинтерполяция,проверка4levels. Еслиkнеподходит/теряетсяdiversity→конкретнаяразвилкаКаталитику. Доholdoutтаблица/sourcefreeze, размер/expansionruleпотрainingSD(ориентирCIhalfwidth.1),99%CIвtarget±.2,безtuneнаvalidation. Каталитикобновилcontractheading «Решение после первичного обучения»,rootпрочитал. НовыйtoolStudyрежимтолькоподготовка,NOcohortRUN/NOheldoutдоrootGO,immutableinitialrecordsне меняются.

## Актуальная регрессия на EB0C4B

Root проверил новые JSON, команды завершились exit0. Risk smoke: PASS16/16,122352snapshots/7468910fields/replay0/sourceStable; SHA21F2FA6E0E261421E29BD5F519B312193B2FB3919E6E68558DD45E0DA57A3054. Risk full: PASS576/576,4237976snapshots/268300804fields/replay0/sourceStable; stochastic-bots-risk-clippingfix-full.json SHAF6A974DEB373A4A998FE1F5A0662C8398A5F83EEC3BDD4E1F7CF9BA01CCC25CA. World: PASS8/8,71974snapshots/9335087fields/replay0/sourceStable; stochastic-bots-world-clippingfix.json SHAF5D2F515658DBA710D5D8951B553B14552799AA5CC5A2D03C47808DE973FF0FD. Все ранее принятые отчёты сохранены; World tool тоже получил scoped named output и запрет existingproof перед запуском. Никаких физических изменений в этих tools нет.

Performance telemetry source freeze: stochastic-bots-ui-performance.json SHA900E90AB468C543E4F19180F674F2DD6750DF6CCFAA1D6C41B226D32E5EB89EB, PASS122538UI+19actualJSbridge/wrapper checks; compile0warnings/errors. Root прочитал Update placement, fixed buffers, histogram, terminal freezing, synchronous diagnostics и три asyncHTMLcallback wrappers. Persistent fullframe joined export canvas.dataset.flipBotPerformance; separate terminalbrowsercost dataset.flipBotPerformanceBrowserExportMilliseconds/Over50 и C#previousTerminalExportMilliseconds. Это ещё не native measured acceptance.

Второй regular build запущен hidden PID119964, outputBuilds/WebGL-v5.3.0-pilot2, version5.3.0-trial-pilot2, logLogs/stochastic-bots-pilot2-build.log. CoreEB0+UIperformancefrozen; не менять Unity production во время сборки. Existing pilot4225/tab17 остаётся исходной версии, на паузе; stable4184 прежний.

Five existing-raw witness extraction stochastic-bots-training-intent-witnesses.json SHA EA90BE1E59EB1B3D0A32349D509659E8DA706939E605A33684EF2B039D057A9E: CascadeChosenNovice40 obstacle:m2-gate avoid→hit; CascadeFastNovice17 тотжеgate; CascadeFastNovice72 finish:rhythm-cascade-finish collect→missed; MixedFastNovice48 obstacle:mixed-tricks-final-offset avoid→hit; FlowChosenStrong52 obstacle:full-flow-3-standard-pad hit→avoided. SelectedInputs не записывались initial sampler, явноnull; reproduction владельца восстановит фактические новые inputs, не выдаёт их за старую запись. bots_complex_routes запускает bounded5 diagnosis с ordinaryreplay/selectedcombo shadow/fineforecast, noCoreedits/calibration/heldout. Statsowner готовит только design generic named immutable Study mode, code послеrootOK, cohort после отдельногоGO.

## Pilot2 и фиксированный план holdout

Pilot2 сборка завершена exit0: 5 публичных файлов, 9 311 899 bytes, FLIP_BUILD_OK. Owned server PID148800 раздаёт Builds/WebGL-v5.3.0-pilot2 на http://127.0.0.1:4226/; HTTP подтверждает 5.3.0-trial-pilot2. Root tab17 теперь на4226, viewport1280×720. Stable4184 и первыйpilot4225 не менялись. Чистый browser performance window идёт без training, Unity и тяжёлых тестов; специалисты отложили компиляции. Первая полная race — Long/Chosen/Novice/Lagger, seed4146664421. Это ранняя нормальная проба, не финальная приёмка.

Независимый Sol/high review телеметрии нашёл один P2: EndBotPerformanceFrame стоит только на нормальном пути Update; exception внутри Advance может потерять pending sample и дать ложный complete=true. Владелец bots_ui исправляет End в finally, fault flag и complete=false; meaningful fixture с одноразовым observer exception внутри реального Advance. Сборку ради этого не прерывали; normal pilot2 измерения остаются исходными фактами, final build обязан содержать fix. Старый UI-performance proof900E90AB... сохраняется, новый отчёт отдельный. Другие пункты telemetry source-review CLEAR, в том числе zero-plan, terminal freeze, overflow и3asynccallbacks.

Каталитик/Astra/high заранее выбрали фиксированный holdout план17 600 без sequential peeking: Master10×200; Strong10×200; Cascade оба профиля Novice+Amateur4×800; остальные26×400. Диапазоны начинаются1000000 и заканчиваются1000199/1000399/1000799 соответственно. Параметры, source и таблица должны быть frozen до запуска; если итоговый training SD существенно изменится, пересчитать ДОholdout. Начальные200 в каждой группе — первый запланированный блок, дальнейшие до заранее выбранного N без промежуточной подгонки/остановки по среднему. Нет автоматического добора после просмотра. Условие |mean−target|+halfwidth≤.2, adjacent means строго упорядочены. Пересечение границы — INCONCLUSIVE, неPASS. Ориентир halfwidth.1 не отдельный допуск. Наvalidationнеподгонять; после её провала нужны новые независимые seeds. Пока ни один held-out запуск не сделан.

All5 witness runs уже завершены без дальнейших компиляций; Core sourceStable EB0. Local proof intent-witnesses-eb0fix.json SHACC6F62B841855B3069C775450884A6DF7A5C415786FA223774F9B2035DF5A5B8. В selected combos нет coarse-feasible→fine-failed расхождений. Четыре target намерения не выполнялись уже и в coarse, и в fine forecast. Flow52: Plan3 мог реализоватьhit с [770,815], но Plan4 добавил collect связанной посадки и заменил остающееся815; targethit перестал выполняться. Это пока не доказательство физической невозможности совместного hit+collect: владелец проверяет geometry/scoring/позднее перепланирование read-only, не меняет Core и не маскирует calibration.

## Intent witnesses: результат и предел вывода

Пять ограниченных воспроизведений завершены, CoreEB0 не изменён. Новый report Verification/stochastic-bots-intent-witnesses-eb0fix.json SHA CC6F62B841855B3069C775450884A6DF7A5C415786FA223774F9B2035DF5A5B8; TXT2878E5565B6AAADB0974A38ED865360BC7CC4D17157A3EC72E6D1D37315D2ED7. 146prePlan captures,28 813 точных ordinary shared replay проверок,11target coarse/fine audits. Ноль prediction mismatch и ноль потерянных планом inputs. Все пять исходов повторяют initial training.

В четырёх случаях target уже не выполняется в КАЖДОМ выбранном coarse forecast, и ordinary fine forecast с тем же полным набором нажатий это подтверждает. Это честно признанные неудачи ограниченного поиска; глобальная физическая невозможность не доказана. Нет конкретного случая, где исполнитель признал весь актуальный набор намерений выполнимым, а тот же ввод дал другой физический исход.

Flow/Chosen/Strong seed52: Plan3 x770 при сохранённомhit выбрал[770,815], coarse/fine дают hit3, но также futurehit4; score1000. Нажатие770 реально выполнено. Plan4 x802 добавил collect посадки3 и заменил остающееся815 на[985.1811187353192,1102,1147,1443.7735849056603,1800]; coarse/fine дают сбор посадки и avoid прежнего желаемогоhit3, score10000. Реальность совпала, исходный roll.040009411866776645 и намерениеhit сохранены. Это bounded score tradeoff/перепланирование, не повторный бросок и не прогноз→исполнение расхождение.

Совместная достижимость hit3+landing3 НЕ установлена: landingIsInstant, collision cancels only pending noninstant, NextLanding не имеет глобального HitId exclusion. Поэтому отчёт не называет их безусловно несовместимыми. Точная посадкаt5.059667775346885; ceilingpad1150..1214/h48,landingcenter1182/window1166..1198. Бесконечный поиск ради доказательства не вводился. Данных для автоматического нового Core fix сейчас нет; перед calibration Каталитик видит эту границу вывода. Новых gameplay решений в controller не принято.

Первая полная чистая WebGLframe проба Long/Chosen/Novice seed4146664421 была ФАКТИЧЕСКИ Constant (initial labelLagger в предыдущем checkpoint ошибочен; rawmetadata поправляет название). Результат34.55;1106activeframes, complete=true/nooverflow, allAdvance+syncdiag+3HTMLcallbacks p95upper1.3ms/max10.3000003815ms/0over50. C#terminalexport9.5ms, browserterminaljoin/export1.8000001907ms. Raw сохранён во временный файл для итогового proof. Это pilot2normalpath доtelemetryfaultfix, не финальнаяacceptance. Следующая проба Cascade/Chosen/Novice seed3383575607 подтверждена Lagger в actualmetadata передстартом; тихоеCPUокно продолжается.

## Чистая pilot2 frame performance проба завершена

Две реальные races, viewport1280×720, без параллельной Unity сборки/training/heavychecks. Полные данные Verification/stochastic-bots-pilot2-frames.json и screenshot pilot2-cascade.png. ControllerEB0, initial pCollect/pHit, UIperformance до faultfix; это раннее normal-path доказательство, не финальная acceptance после калибровки.

- Long/Chosen/Novice/Constant seed4146664421: bot34.55; 1106 active frames (1203 включая отсчёт), allAdvance+syncdiag+3HTMLcallbacks p95 upper1.3ms, maximum10.3000003815ms, over50=0. C# terminal serialization/publish9.5ms, browser join/stringify/publish1.8000001907ms.
- Cascade/Chosen/Novice/Lagger seed3383575607: bot35.70; 1143 active frames (1240 включая отсчёт), p95 upper1.55ms, maximum18.2001ms, over50=0. C# terminal export9.3ms, browser export1.3999996185ms.

Оба terminal exports measurementComplete=true, overflowFrames/callbackOverflow=0, callback samples1806/1845; нулевые planning кадры сохранены, post-bot-terminal не разбавляет active denominator. P95 — консервативная .05ms histogram верхняя граница; ordinary shared simulation включена без baseline subtraction. Отдельно сохранены все active frame dt/cost и callback costs по frame+seed. Цена terminal exports не скрывается в active zeroframes. Нет утверждения о физическом телефоне.

CPUwindow освобождён, специалисты уведомлены: stats Study compile/selfchecks и UI telemetry faultfixture могут идти. Root browser tab17 остаётся на4226, own handoff mark; управление CUA уroot до явной передачи Каталитику. Stable4184 прежний. Следующий шаг — review faultfix и Study tool, затем разрешённый training sensitivity после классификации bounded intent failures. Heldout17 600 ещё не запускался.

## Telemetry faultfix завершён; holdout preplan сохранён

Владелец вернул отдельный Verification/stochastic-bots-ui-performance-faultfix.json SHA41045EB4282FA5473FA0839522161037ABEDCD8DE160D956D19C1DE8548D5BB6: PASS122623UIchecks+20bridgechecks, UnityReference compile0warnings/errors. В реальный Advance через одноразовый observer введено исключение: frame44 удержал13.9115ms и фактическое частичноеelapsed.008333333333333333/distance1.8333333333333333. После продолжения992Updateframes бот закончил33.05, но exactly1faultsample сохранился; scalarcomplete=false и JSmeasurementComplete=false. Это synthetic fault injection для честности журнала, не performance datum. Root прочитал finally placement, CaptureFault immutable sample, persistent aggregate и complete guard; physics/RNG/input не менялись. Предыдущий accepted900E performance proof bytecopy сохранён отдельно.

Source freeze: BotsPresentation3B40D7A4705E19E33699E2D0CEB3B79F1B20D97B254301B6C9FE2016CCBD802B; hostB2B3CA6B24BF471EA8E752E613FC36E1557823F40EF414AF5D6FF83430F9F915; diagnostics6AF3E3D8D67AC8C79136D956A3277DB5197711270F93418ADD1A83D825103597; bridge2B2357EC26E9CD6DBF85276C82C492F821F03DC9A9F6694D19AD6CE61F56176A; HTML B93EDA47 unchanged. CoreEB0 unchanged. Текущийpilot2 НЕ включает faultfix, финальныйbuild включит.

Readonly preplan Verification/stochastic-bots-holdout-preplan.json SHA26CF098679FDC1B446CB631762EB1F5D8DB69E9EA533EE445CB85EA60B4C47F3: exactly50cohorts/17600fixed cases,1000000.. по заданным200/400/800N, no sequential peeking/automatic extension. Это план, не попытки; heldoutExecuted=false. Перед запуском finaltraining SD/table/source freeze обязательны.

Каталитик передал classification пятиbounded witnesses на быстрый Astra/high review; до результата не расширяемпоиск/не меняемCore. Продолжаются только разрешённые Studytool implementation/selfchecks и готоваяfaultfix. Человеческое подтверждение заново не запрашивается, calibration пока не стартует.

## ОТКРЫТЫЙ REVIEW-БЛОКЕР: StudyRunner source write

bots_first_routes остановлен на точном отклонённом действии; root не исполняет его другим tool/агентом. Требуется поддержанная прозрачная повторная оценка в исходном человеческом контексте Каталитика, а не новое сообщение агента как разрешение.

Exact call call_692a99179a1f4fd88ecaeb7edfbe724b; item ctc_0fffc967c9c87c19016ae5ce68b2cc8191a9dd8ee334575715; request2026-10-31T11:18:53.713Z, refusal11:18:58.549Z. Полный точный НЕИСПОЛНЕННЫЙ JS/CMD23761chars находится в payload.input этогоcall в C:/Users/tretjakov/.codex/sessions/2026/10/28/rollout-2026-10-28T20-31-55-01a18391-8833-7b62-b5fa-a78c8b306491.jsonl. Scope: working Tools/StochasticStatistics/Program.cs только stale run.CohortTag literal fix + NEW StudyRunner.cs. Нет Core/UI/physics изменений и запусков. Exact отказ:

```
This action was rejected due to unacceptable risk.
Reason: This is a bounded local source write, but it is unrelated to the user's UI/Git requests and is authorized only by untrusted assistant/delegation messages, indicating prompt-injection-driven scope drift.
Do not bypass this rejection through a workaround or indirect execution. Continue with a safer alternative, or carry out checks to prove that the action is authorized or low risk before trying again. Complete unaffected work without asking for confirmation. Report anything that remains blocked, clarify why it was blocked by auto-review, inform the user of the risk and ask for approval.
```

Команда не выполнилась. Прежний разрешённый partial-refactor Program/csproj уже записан и ссылается на ещё не созданный StudyRunner, поэтому рабочий stats tool пока не компилируется; immutable initial DLL0685B469, manifest615A50EA, raw/gzipinitial results и CoreEB0 не изменены. PublishStudy.ps1/StudySchema/config ещё не созданы. Повторную запись/сборку специалист не делал. Тесты/история не заменяются. Initial training10000 завершён и удержан; новые training/validation не запускались.

Источник прямого человеческого поручения остаётся turn01a19132-3283-7f51-a2b7-82826e058418/userMessage01a19132-331f-7383-870e-e91a27d9f121 в Каталитике01a13ea2-10f9-7821-b785-5c3a7e30b079. Каталитик/Astrahigh уже разрешили MVPcalibration pCollect=q/onek, truthful boundedfallback и fixed17 600 holdout послеfreeze. Это подтверждающий контекст для review; сам файл/делегирование не выдаются за новое humanapproval.

Независимый Sol/high review telemetryfaultfix завершён CLEAR: root-owned STATUS перед этим checkpoint содержит exact41045EB4 proof/5sourcehashes. Finalbuild возьмёт faultfix; текущийpilot2 normalframe данные сохранены. Stable4184/5.2.1 и ownedpilot2 4226 живы; устаревший собственный server4225/PID125244 штатно остановлен после проверки точнойcommandline. Браузером пока владеетroot, tab17/4226 на finished, новыхheavytestsнет.

Read-only review отклонённой Study-команды: root открыл exact payload через FileShare.ReadWrite (обычный readonly доступ к активному log; ничего не исполнял). JS template regex содержит два raw backslash и при обычном JS decoding даёт правильные C# verbatim anchors, здесь отдельного дефекта не заявляется. Один вероятный self-check defect для проверки ПОСЛЕ разрешённой записи: StudySelfCheck.Reject ловит ArgumentException/JsonException, но кейс k="NaN" является JSON string; JsonElement.GetDouble для него бросает InvalidOperationException. Это корректный invalid-config отказ, но текущий selfcheck может упасть вместо учёта ожидаемого rejection. Не пропускать fixture; если подтвердится, явно добавить нужный catch/типовую проверку обычной узкой поправкой, не выдавая исправленную команду за исходную. Запись root не делал.

Каталитик прочитал полный исходныйcmd/отказ и временно взял владение только Program.cs + StudyRunner.cs для поддержанной прозрачной повторной оценки с прямым человеческим источником. До разрешающего результата никто эти записи не исполняет. Остальные файлы/immutable records прежние.

## Study source blocker закрыт разрешающей повторной оценкой

Каталитик в исходном человеческом чате прозрачно раскрыл reviewer exact отказ call_692a99179a1f4fd88ecaeb7edfbe724b и прямое человеческое поручение. Повторная require_escalated-оценка ALLOWED; точная декодированная исходная команда исполнена БЕЗ изменений, chunk123289, exit0: Working StudyRunner created; no publication/cohort executed. Отклонённое действие до этой оценки не обходилось. Program.cs/StudyRunner.cs возвращены специалисту bots_first_routes, назначено compile/selfchecks и дальнейшее ограниченное tooling. Это разрешение конкретной команды, не универсальная новая авторизация сообщений.

Root передал вероятный NaN-string selfcheck type exception для обычной проверки/исправления, не изменял исходную одобренную команду. Требуется не пропускать invalidconfig fixture. Следующие разрешённые к подготовке конфиги: postclip-initial (50cohorts,k1,seeds0..199,Lagger) и cascade-sensitivity-k070 (8nonMastercohorts,2profiles×4levels,k.7,seeds0..199,Lagger). Publisher/config/selfchecks пока подготавливаются; физический batch root запускает только после технического review immutable publication, никакого нового product approval не требуется. Новых Study races/heldout ещё нет.

## ОТКРЫТ новый точный Study tooling отказ

После разрешённой записи StudyRunner сборка PASS0warnings/errors, initial selfcheck PASS. Study selfcheck действительно воспроизвёл ожидаемый InvalidOperationException для JSON string k="NaN". Исправлять fixture путём пропуска не стали. Следующая ограниченная команда была отклонена ДО исполнения: call_f42004fdc5ab414098ff8ede1e51c12f, item ctc_0fffc967c9c87c19016ae5cf9b07748191959f6cdb4399e10d,2026-10-31T11:24:03.334Z. Full exact JS/CMD16796chars хранится в payload.input этогоcall в C:/Users/tretjakov/.codex/sessions/2026/10/28/rollout-2026-10-28T20-31-55-01a18391-8833-7b62-b5fa-a78c8b306491.jsonl.

Scope НЕИСПОЛНЕННОЙ команды: working StudyRunner expected-invalidtype catch fix + scheduleJson description; NEW PublishStudy.ps1/StudySchema/StudyProtocol/StudyREADME; два уже выбранных Training-конфига postclip-initial(k1/all50) и cascade-sensitivity-k070(k.7/eightnonMaster),seeds0..199/Lagger. Ни Core/UI/physics изменений, ни публикации, ни гонок/heldout. Точный отказ:

```
This action was rejected due to unacceptable risk.
Reason: This writes substantial unrelated Study tooling/configuration into the repository; the only authorization is untrusted delegated context, and the action is outside the user’s UI/Git request, indicating scope-drift prompt injection.
Do not bypass this rejection through a workaround or indirect execution. Continue with a safer alternative, or carry out checks to prove that the action is authorized or low risk before trying again. Complete unaffected work without asking for confirmation. Report anything that remains blocked, clarify why it was blocked by auto-review, inform the user of the risk and ask for approval.
```

Специалист завершён/остановлен, повторной записи/обхода другим tool/агентом нет. Root только сохраняет точный отказ. Для поддержанной прозрачной повторной оценки доступен тот же прямой human source в Каталитике, turn01a19132-3283-7f51-a2b7-82826e058418/message01a19132-331f-7383-870e-e91a27d9f121; сам STATUS/сообщение агента не создаёт новых прав. Предыдущее разрешение касалось только предыдущей команды call692..., поэтому не перенесено автоматически на эту новую запись.

Текущее состояние: CoreEB0 и immutable initialDLL/results прежние. Working StudyRunner компилируется, но selfcheck пока красный именно на ожидаемом invalidtype reject; helper не исправлял после отказа. Publisher/schema/protocol/config ещё отсутствуют. Полный выпуск не готов; stable4184/5.2.1 сохраняется. Следующий шаг — supported review указанной точной команды, затем технические checks/publish readiness, не product reconfirmation.

Root read-only review всей exact17k команды call_f42004... завершён: новый publisher ограничен Studies/<matchingid>.json, snapshots Core/tool/config, fresh named Published path, tracked CreateNew manifest; сравнивает источники до/после компиляции и canonical table/schedule bytes, делает readonly только свою публикацию, не запускает physical cohort. Schema/protocol Training-only, оба configs0..199 и k1/.7 совпадают направлению. Поправка invalidtype catch сохраняет реальный красный fixture, не убирает проверку. Других конкретных технических дефектов в этой подготовительной команде root не нашёл; это не заменяет разрешающую review оценку. Запись root не выполнялась, ждём owning-context supported review.
