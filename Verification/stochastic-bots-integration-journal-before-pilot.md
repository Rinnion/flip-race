# STOCHASTIC BOTS — 5.3.0-trial

31 октября 2026. BSTO00: foundation принята Каталитиком после Risk576/World8/Master144 на 9FB640. Идёт подключение нового бота; текущая фабрика/settings запись остановлена review, UI и stats подготовлены, но не компилировались с новым API. Текущий frozen controller: `9FB640D34AD77B1615AA8D40FEC92F891E26A2D0954908D8BFB08E56DE379378`. UI integration и stats tooling начаты; новый factory API ещё не создан, training/validation/WebGL5.3 не запускались. Стабильная 5.2.1/4184 и ZIP сохранены.

[Контракт BSTO00–07](../../flip-race/docs/flip-race-stochastic-bots-delivery-2026-10-31.md). [Независимая приёмка Каталитика](V5-STOCHASTIC-BOTS-CATALYTIC-ACCEPTANCE.md). [Полный прежний журнал, точные отказы/команды/разрешающие результаты](stochastic-bots-implementation-journal-pre-foundation.md): сохранён byte-for-byte перед уплотнением STATUS. Журнал не заменяет прямое человеческое разрешение.

## Источник, роли и границы

Root и исполнители лично прочитали человеческий источник: Каталитик `01a13ea2-10f9-7821-b785-5c3a7e30b079`, turn `01a19132-3283-7f51-a2b7-82826e058418`, userMessage `01a19132-331f-7383-870e-e91a27d9f121`: «Да все так кажется. Видимо я ошибся в расчетах. Давай такого бота сделаем». Предыдущий userMessage `01a1910f-8206-78a3-aca0-29d236e31326` задаёт локальные умения и фиксированные до старта вероятности.

Прогатик/root — интеграция, проверки и поставка (Sol/high); Каталитик — игровые решения (Astra/high) и независимая приёмка. Предыдущие отказы review не обходились: точные действия были прозрачно переоценены в исходном человеческом чате с отказами и прямым запросом. Это разрешения конкретных команд, не универсальное новое human approval. Сейчас незакрытого отказа нет. Новый отказ останавливает конкретное действие и передаётся с exact call/неисполненным command; иной tool/агент не используется для обхода.

База Unity `0b1225b`, docs `5be10a2`. Чужая `Verification/FIX491-RELEASE.md` не изменяется/не включается. Исторические 1799 планов и отчёты не меняются. UI501 уже закрыт; прежний скриншот шапки не является новым поручением. Арт, JS и экономика вне объёма.

## Фиксированная модель и сохранение

Намеренно меняется BOT: локальные решения из фактического состояния, одна сохранённая случайная интенция на logical ID. q = .50/.65/.75/.90/1. До старта pCollect = q; pHit = (B/C)*(1-q), для Master 0; Tq = Tbase-B*(2q-1). Нет квот, подгонки времени по ходу, зависимости от человека, повторных бросков или полного поиска оставшегося пути. Один компактный чистый эталон — допустимый ориентир, библиотека старых суффиксов не используется. Индивидуальные старые жёсткие диапазоны результата заменены статистикой серии.

Все 10 пар: Tbase 36 с, Tmaster 33.05 с, B 2.95 с; C = 6 с Long/Short/Flow, 10.5 Cascade, 5.5 Mixed. Уникальное препятствие стоит 0.5 с; половины ворот имеют один ID. Tq = 36 / 35.115 / 34.525 / 33.64 / 33.05 с. Неподдерживаемые параметры отклоняются явно, без clamp.

Preservation: BOT/CONTROL/REWARD/FINISH/GATE/VIEW/TOUCH/SOLO. Обычные Press/Advance, физика, геометрия, личные и untouched rewards, failed pending/FIFO/effects, полный finish .25 с, реальные камеры/left-only выбывание и ввод. Master обязан финишировать чисто со всеми совместимыми gain; глобальный рекорд скорости не заявляется. Требуемые статьи/workflow прочитаны; актуальная BOT-статья обновляется после проверки фактического итогового поведения.

## Требования и текущая приёмка

| ID | Статус | Доказательство / осталось |
| --- | --- | --- |
| BSTO00 | working | Предыдущий freeze 4B7F: полный risk576 + World8 PASS, все recovery/skip/untouched/pending witnesses. На 9FB640 Master144, target3 и smoke16 PASS; повтор full576/World8 в работе. До закрытия основы UI/build не начинать. |
| BSTO01 | partial | Deep snapshot, immutable map, bounded local controller готовы. Реальный requested/trial bound теперь измеряется; закончить регрессию 9FB640 и независимую приёмку. |
| BSTO02 | partial | Начальные B/C/Tmaster/вероятности проверены; offline refinement и validation-статистика не выполнялись. |
| BSTO03 | partial | Сохранённые logical IDs/rolls и FPS-independent choices проверены в 4B7F576; Master timing diversity на 9FB640 — 12 actual signatures на каждую из четырёх risk-пар. Требуются итоговая матрица и все 50 сочетаний. |
| BSTO04 | partial | Snapshot isolation и ordinary replay PASS; World8 на 4B7F PASS, повтор на 9FB640 в работе. Factory/BOT integration ещё отсутствует. |
| BSTO05 | todo | Все 50 сочетаний, held-out статистика, Master обоих камер, отдельный Constant, измеренная WebGL performance. |
| BSTO06 | todo | После основы: factory/UI/new seed/retry/диагностика, actual desktop + compact browser. |
| BSTO07 | todo | Регрессии, 5.3 regular WebGL, ZIP/fresh extract/HTTP/version, документы и независимая приёмка. 5.2.1 сохранить до принятия. |

## Проверенные артефакты

- [Snapshot](stochastic-bots-snapshot.json): PASS 7693 checks / 225 snapshots / 4050 paired continuations; source stable в своём прогоне. Внутренние aliases сохранены, ветви изолированы; geometry/settings разделяются только read-only. Независимый Sol/high review CLEAR. RaceSnapshot SHA `07E2C8693926A64C0AE2A417501CCA2D1C882DE536987713731842F9A12366BB`, RaceSimulation `5D2C3DB7AF6A9DEF3AF19F5D023FD5B8B926E1E5AF868CC0C406555DBAEF28CA`. Один вспомогательный map hash в старом отчёте относится к доработанному впоследствии map; snapshot код не менялся. Финальный интеграционный прогон обязателен.
- [Map](stochastic-bots-map.json): PASS 4373 checks, 20 clean + 20 empty baselines + 186 directed collisions; все 236 возможностей экспортированы пятью полями и round-trip сверены с фактической картой. Исправлен прежний export [{}]. JSON SHA `E0ECDEBCA751B9AC6CC95E65957064055C832186883C4876B0D35B3EA5AAA763`; map source `374384E8DF23CBC828355E7065FA30127750D38DA51376855DFD9FFAD092470D`.
- [Risk576, freeze 4B7F](stochastic-bots-risk-performancefix-full576.json): PASS 576/576, 432 Lagger + 144 Master Constant, seeds 0..11 × 3 cadences, 0 unrun/0 filtering. 4238408 immediate snapshots / 268332460 field comparisons, replay error 0, sourceStable true. Все Master 33.05/0 hits/9 boosts/full finish. Frame0 unique configs: 90 skip→collect, 85 hit→recovery→collect, 11 untouched linked after hit, 6 pending→failed. SHA `0870D45F44D96B52AA0E4AD83494E7EA5054A5E5423ED7A93BB14D5DE3E10448`.
- [World8 исходный отчёт](stochastic-bots-world.json): предыдущий freeze 4B7F PASS 8/8, 71979 immediate snapshots / 9335844 fields / error0. Q1 bot33.05, idle humans Cascade44.5/Mixed41; 1594/1106 racing slices после bot terminal без новых bot Press/state/cursor/decision changes. Human timed scripts не меняют bot intents/rolls/inputs. Constant human eliminated10.63090909, bot finished36.0. Pauses/terminal freeze проверены. Это ordinary Party2 foundation, не factory/UI. Предыдущий SHA `CAD7FD0CB86C4D21EE475B35467A213CD0192DF000FBC2E8ADC5295F5FD17EF8`; владелец сохраняет baseline отдельно при повторе.
- [Master144, freeze 9FB640](stochastic-bots-master-variation-foundation.json): PASS 23909 checks; четыре пары Cascade/Mixed × Chosen/Fast, seeds0..11 × 3 cadences. Все clean33.05/full9+finish, каждая пара 12 actual emitted input signatures. Строгий source/ordinary replay после каждого Plan и actual horizon checks. SHA `48AFB31290530B348CF45B479DE5C6DEF2A9D5F9F561664B9F60E91482668333`.
- [Dedicated performance/source proof, 9FB640](stochastic-bots-performance-foundation.json): 8 runs / 1212 checks / 219 Plans PASS; Plan p95 37.3122 мс, max51.0927 мс. Это НЕ performance acceptance, условия содержат cold/JIT/нагрузку без фильтрации. Старые mixed-audit Risk пики160–177мс также не скрываются; observer stopwatch не изолирует cross-GC/scheduling. Требуется отдельный итоговый preflight без конкурирующих тестов и реальная WebGL проверка.

## Исправленные дефекты и реальные границы

FPS seed4: исходный 4B7F-предшественник менял сохранённый timing combo на 64/32 ед при микроразнице начальной дистанции; actual inputs/outcome тогда сходились. Исправлено stable per-candidate hash rank вместо modulo ties.Count, без округления физического state. Исходный [FAIL15/576](stochastic-bots-risk-failure-cadence-seed4.json) сохранён. Индивидуальная диагностика теперь не помечает все цели из-за aggregate ошибки; met actual intent очищает причину.

Master seed7 на 37D101: joint -6 перенёс original1718.5/1741.5 в1712.5/1735.5 до nextLead1736; новый Plan воскресил original1741.5, добавил1753.5, привёл к первому hit m4-stone и финалу42.5/16hits/5boosts/finish0. Минимальный 9FB640 guard требует original И retimed changed edges до nextLead, чтобы originals не возвращались из reference. Seed7 теперь33.05/0/9/finish1, 44inputs; 144 strict regression PASS. Независимый reviewer подтвердил source guard и сузил прежний общий continuation finding до этого механизма; дополнительных требований к full suffix нет.

Engineering limits: requested/committed1000, кандидат96 (Master≤13), inputs≤12, forecast step≤.125с, v≤440. Реальные все trial, включая discard, наблюдаются после каждого Advance ДО rollback: MaximumRequestedHorizon≤1000; MaximumTrialHorizon≤1055; pointwise MaximumTrialOvershoot≤55 относительно endpoint именно своего Plan; violations0 и observations==steps. ε2e-7 явно. Discarded events/effects целиком откатываются, scoring/intent/outcome их не читают. 1000 — выбранный технический лимит, не число пользователя. Requested-only legacy counter не выдаётся за actual. Live Press/Advance и physics неизменны; CPU durations не влияют на решения.

На Master144 observed requested/trial max1000.0000000000005, overshoot0. Новый risk smoke16 также 624089 observations==steps, violations0. Полная dual-bound матрица ещё идёт.

## Владение и следующий шаг

- Root: STATUS, интеграция/factory/UI после foundation gate, итоговые проверки/build/docs. Сейчас production-файлы не редактирует.
- bots_complex_routes Sol/high: controller/.meta и Tools/StochasticLocalProbe; freeze9FB640, больше изменений нет.
- bots_ui Sol/medium: Tools/StochasticRisk и новые risk proof. На9FB640 target3→smoke16 PASS, full576 в процессе; строгие FPS/replay/candidate/dual-bound assertions не ослаблялись, первый дефект останавливает матрицу. Отдельные masterfix-repro/smoke/full сохраняются точно.
- bots_first_routes Sol/high: Tools/StochasticWorldCheck и его proof, повторWorld8 на9FB640 в процессе; Core не правит. Map tool/report завершены.
- Каталитик: собственный acceptance файл, независимый review/final acceptance; root его не переписывает.

Команда risk: dotnet run --project Tools/StochasticRisk/StochasticRisk.csproj -- --max-horizon=1000 --max-trial-horizon=1055 --max-trial-overrun=55 --max-candidates=96; для targeted добавить --repro-cadence-seed4, для smoke --smoke. До source freeze/прохождения предыдущего этапа следующий не запускать. World source freeze согласован.

После текущих PASS: независимая foundation приёмка, затем integration и статистический этап. Training seeds отделены от заранее объявленного validation набора; параметры frozen до validation, CI по фактическим временам ВСЕХ validation runs. При offline refinement не подбирать на повторяемых validation200, не отбирать успешные/выживших. Начать200 held-out seeds/сочетание Lagger, заранее определить добавление следующего набора при неопределённости; цель99%CI внутри Tq±.2с и строгий порядок соседних средних. Constant отдельно. Release/end-state sampler разрешён при сохранённых строгих replay proofs. Тяжёлая статистика пока не запускалась.

## Foundation accepted и текущая интеграция

Каталитик сам прочитал актуальные Risk576 и World8 JSON/SHA, закрыл независимую foundation-приёмку в этом объёме и поручил продолжать integration/calibration без дополнительного ритуального согласования. BSTO02/05/06/07, performance и все50сочетаний остаются открыты.

Итоговый dual-bound Risk576 на9FB640: PASS576/576, 4237976 immediate snapshots / 268300804fields / replay0 / sourceStable. Requested max1000+5e-13, actual transient1035.8998941426098, pointwise overrun35.89989414260981≤55. Все22984944ForecastObservations==ForecastSteps, violations0. Master обеих камер33.05clean,12actualinputsignatures/12seedsнакаждуюrisk-пару. Сохранённые stochastic-bots-risk-masterfix-full.json SHA2A960C9D70810F2375F37578843232E211C64190CD4F7F3FB7DC683883685F56; TXT E42075F63C395D10481DCF64CF83C3A50A8F6E3E9BF9D4DB4695C85BEA78B95F. Managed maxAdvance70.1951мс/p95до.3717 — неWebGLperformanceacceptance.

World-masterfix8 PASS/sourceStable: 5780872checks /71974snapshots /9335087fields/replay0. Все прежние checks human continuation, no botPress послеterminal, пауза, human-input independence и настоящееConstantelimination сохранены. Новый JSON stochastic-bots-world-masterfix.json SHA39152A85FE07FFDE92119A4F4BDD20282E40914D8C207AE01DE20674ED238E37; TXT5BC0B251B7C0300513D1143F8727F8D38FF2A1C7468B02B268BD1FF48EE8BA5B. Оригинальный4B7FworldJSON/TXT не перезаписан.

Новые владельцы после foundation:

- bots_complex_routes: временно RaceSimulation.cs ТОЛЬКО readonlymetadata/factory; NEWStochasticBotSettings.cs/.meta; controlleroverloads/fixedprobs; LocalProbefactorychecks. Физику/oldCreateBotRace/1799 не менять. Действие сейчас review-blocked, см. ниже.
- bots_ui: BotsPresentation.cs, FlightSettingsMenu.cs, FlipDiagnostics.cs и NEWTools/StochasticUiCheck. ActiveUIпереподключён к ожидаемому newfactory/controller, sessionseed+monotonicattemptindex вместоcatalogshuffle. Presentation/Web rg не находит activelegacycatalog/tape references. Actualsettings/decisions/planning/truebound diagnostics подготовлены. Compile/run ждут CoreAPI.
- bots_first_routes: NEWTools/StochasticStatistics, исходныйTRAININGprotocol/manifest/report. Только подготовка tooling. 50сочетаний×200trainingseeds0..199/Lagger; heldout1000000..1000199зарезервированы и не запускаются. ReleasepublishimmutableDLL/source/configmanifest/assemblySHA; доrootexplicitready training не запускается. Всеисходы сохраняются; runtimeerrors делаютсоответствующийcohortCIнеполным/null,нефильтруются; genuineeliminatedвходитпоTerminalTime. Fatalsetup→явныйunrunmanifest. Никакойauto-calibration/параметровпоходугонки.
- Root: добавил immutableStochasticBotSettingsreflectionguard+metadatafixture вTools/StochasticSnapshotCheck, новый--stochastic-metadataотдельныйoutput, старыйsnapshotproofнепереписывается. ВRaceSnapshot.cs изменён толькоXMLdocпроsharedreadonlytype, логикаклона таже. Не компилировалось доCoreAPI. Послеfreeze — integratedclonechecks, source freezeдляstats publish, раннийWebGLpilotпараллельноtraining. 4184/5.2.1 не переключается.

## Точный новый блокер: factory/settings

31 октября13:25:25.852UTC+03:00: functions.exec call_84518de2d13b4db4a1e718400c7c6717, item ctc_081d5b3679f0c1de016ae5c1dddf3081919b5ca81c391c76ab, ordinal2335. tools.exec_command workdirF:/Home/Game/flip-race-unity, require_escalated/yield1000/max_output_tokens2200.

Действие: NEWCore/StochasticBotSettings.cs/.meta; Core/StochasticBotController.csoverloads/fixedprobabilities; RaceSimulation.csreadonlymetadata+CreateStochasticBotRacefactory; Tools/StochasticLocalProbe/Program.cs; затем --factory100camera/metadatachecksсownfactory-check.json. Oldmechanics и старыефабрики не меняются. Полныйточныйcustom_tool_call.input19275символов хранится в C:/Users/tretjakov/.codex/sessions/2026/10/28/rollout-2026-10-28T20-32-06-01a18391-b467-7e42-bfbe-7f895e18ce13.jsonl, ordinal2335/call84518. Отдельногоpatchнет.

Полный отказ verbatim:

```text
This action was rejected due to unacceptable risk.
Reason: This writes and changes Unity core gameplay/controller behavior and probe files, but the trusted user requests authorize only UI and Git work; bot-scope approval appears only in untrusted agent history.
Do not bypass this rejection through a workaround or indirect execution. Continue with a safer alternative, or carry out checks to prove that the action is authorized or low risk before trying again. Complete unaffected work without asking for confirmation. Report anything that remains blocked, clarify why it was blocked by auto-review, inform the user of the risk and ask for approval.
```

Ничего этим вызовом не записано/не собрано/не запущено; SettingsExists=false, controller9FB640неизменён. Повторной оценки/обхода специалист не делал, root эту запись другим toolнеисполняет. Назначенныйвладелецостановлен доsupportedreview/handback в исходном человеческом контексте.

ВАЖНОЕread-only technical finding до повторного исполнения: RaceSimulation.cs содержитCRLF1313lines, а $oldmultiline needle в НЕИСПОЛНЕННОЙкоманде содержитLF. Поэтому оригинальнаяточнаякоманда даже послеallowостановится на Missing metadata baseline до записи. Скрыто выдавать исправленныйcmdзаидентичныйнельзя. Коррекциядолжнабытьявноописанным отдельнооценённымдействием: нормализовать $race в памяти CRLF→LF до Contains/Replace, при томжеmetadata/factoryscope. Исходнаякомандаиотказсохраненынеизменёнными; исправленнаяCoreзапись ещё не запрашивалась/не выполнялась.

СогласованныйAPI: immutableStochasticBotSettings readonlyTrackId/ProfileId/Difficulty/Seed(uint)/Quality/CollectProbability/HitProbability/BaseTime/MasterTime/BonusGain/CollisionCostSum; CreateInitial и явныйCreateFixedдляofflinefrozenпараметров. GameState.StochasticBotreadonly; BotSeedостаётсяint uncheckedотuintдляlegacycompat, newconsumersчитаютполныйuintизsettings. NewfactoryreturnsIsBottrue,IsParty/IsSolofalse,BotPlan=null,bot1Ready. Controller(game,slot=1)читаетsettings; explicit(game,slot,settings)валидируетcompatibility, oldq/seedctorinitialproofсохранён. Вероятностификсируютсядостарта,номинальныйQualityуправляетMasterconstraints. Runtimeперекалибровкинет.
