# V5 BOT5 — 5.2.0-trial

28 октября 2026. Собственная техническая приёмка завершена; независимая приёмка Каталитика ожидается. Production source frozen. Играть: http://127.0.0.1:4184/ — PID97332, Builds/WebGL-v5.2.0-final-text. Для независимой проверки доступны идентичный candidate4222PID139808 и свежая extraction4223PID132348. Root освободил viewport; browser ownership передан Каталитику, свои дальнейшие UI-действия root согласует.

## Источник и владельцы

Прямой user «Погнали Делать дальше»: Каталитик01a13ea2-10f9-7821-b785-5c3a7e30b079, turn01a18388-ed46-71a0-bbf5-11f375fa1404, message01a18388-edc2-7451-9f24-7b18556caed4 после предложения пяти уровней ботов. Готовые профили/уровни: userturn01a18278-c0f5-7443-9b7a-3da7fdbd3558. Root сверил прямой источник через read_thread. [Контракт](../../flip-race/docs/flip-race-v5-bots-delivery-2026-10-28.md). Историческое уточнение полоски/лапок уже принято UI501, сохранено; повторный снимок header-center-5.1.png не заменяет BOT5.

Прогатик — production/integration/tests/build/docs, Sol/high. Специалисты остановлены: FirstRoutes/runtime Sol/high; ComplexRoutes/catalog/independentBotUi Sol/high; Presentation/focusedUI Sol/medium; metric designreview Astra/high. Каталитик — scope и независимая финальная приёмка. Новых чатов/целей/публикации нет. [Промежуточная история](bots-delivery-history.md).

## Матрица

verified ниже означает собственное доказательство root; независимая финальная приёмка перечисляется отдельно.

| ID | Статус | Доказательство |
| --- | --- | --- |
| BOT-00 | verified | IsBot/CreateBotRace, отдельное меню,5tracks×2presets×5levels; custom blocked без подменыdraft. bots-ui.json1200matrix+119transition setups |
| BOT-01 | verified | До калибровки фиксированы goalIDs/hash;343productionplans/50libraries. ExportCatalog16592checks точных doubles/IDs/копий/seed. bots-first-levels.json, bots-complex-levels.json |
| BOT-02 | verified | ВсеMaster100%/finish обоими камерами; ordinaryPress/Advance, полный finishpulse. Runtime4058case matrix; nativeMasterConstant33.05s/22of22/nohits |
| BOT-03 | verified | Все343Laggerплана finish в диапазонах; допустимые реальныеcollision/recoveryварианты; Constantactualoutcomesбезфильтрации,12CascadeNoviceplanseliminate. bots-runtime-check.json, nativeCascadeNoviceConstant4754.792116/10of31 иLagger37.5s/18of31 |
| BOT-04 | verified |20fixedseeds×50libraries×2cameras,строгиеLaggermeansпо10парам;sameSeed/nextSeed,700successfulhumanfullroutes+idle/errors/orderindependence. Actual terminalDistance/terminalTime/completedIds в каждой runtime записи |
| BOT-05 | verified | РеальныйBotRaceController:343plans×2cams×3frames+2000seedcases=4058;22,833,691immediatecompletePlayerStates,1,340,330,340fieldchecks,actualreplayerror0.22,681,980observedAdvance/85533Press,10orderproofs,309580personalterminalmarginfreezechecks. Representativeobservedactualtape thirdordinaryreplayPASS. Nativepause/resize/retry и~32renderFPS на800×360 проверочномdesktop/IAB; физическийтелефоннепроверен |
| BOT-06 | verified | Existing sharedcamera/allterminal path; no3.5s cutoff; both outcomes/leftonly/finishpriority preserved. PartyCheck512; runtimeallcases +100actualUpdatefirstterminalcontinuations; nativebotfinish37.5/humanfinish44.5 приLagger |
| BOT-07 | verified | BotUi280895checks,1319setups/2848layouts/48detailedinputcases/100actualfullraces,100nonzerocursorpause/resize,custom+mode roundtrips. FocusedBotUi7542/120+25Fast. Native4sizes568×320/800×360/960×432/1280×800,10level/profile/tracksetups,oneBRpaw/start, menus/results/actualruntime/accessibility; bots-native.json/screenshots |
| BOT-08 | verified | ParityRunnerallbuiltins+unchangedJS/C#40228snapshots/1448208fields; SoloCheck84cases/59482states/1478actions; PartyCheck512; settings736/114physicalroundtrips. FinalsourceSoloUi4962/28cases,PartyUi71100/216cases. Logs/reports bots-*-regression. CONTROL/REWARD/FINISH/GATE/VIEW/TOUCH/SOLO unchanged physics/geometry/presets |
| BOT-09 | verified | BOT article +affected featurecrossreferences/catalog, README/toolsREADME, source snapshots in runtime/UI reports, historykept. Docscommitpending below |
| BOT-10 | working | FinalregularWebGL/ZIP/extraction/HTTP ownPASS below; independentКаталитик acceptance pending. No externalupload |

## План, метрика и ограничения

343 finiteplans:239Long/Short/Flow,104Cascade/Mixed. Обычно8планов/level первых трасс (FlowFastStrong7),6младших/2Master сложных. Seed modulo library, отрицательныеint корректны; retryseed+1, нетадаптациичеловеку/онлайнпоиска. CatalogcleanobstacleRight+radius/nohit,positiveSuccessful,fullfinishpulse;coins/negativeexcluded. Каталоги22/22/22/31/21. Пропуск остаётся в знаменателе.

Полная калибровка и strictmean — Lagger. Constantодинаковаяфизика/правила: слабыйботможетвыбыть,plannedpercentнепоказываетсякакactual. Ранее внутреннееfinish-every-Constantусловие снято Каталитиком/Astraпосле31goalCascadebound: min37.5s>37.37636s deadline. Геометрия/камера/штрафы не менялись. OriginalprototypeBLOCKEDсохранён какистория. Complexoriginal `p` содержитactualdistance (namedDistanceотсутствовал); новыйruntimeявноназываетterminalDistance. Probe.Run simulationRuns невыдаютсязаactualtape replay.

## Сборка и доставка

- FinalUnity Logs/bots-text-build.log: FLIP_BUILD_OK9092229bytes, exit0; Builds/WebGL-v5.2.0-final-text. Исходный5.2candidate9091814bytes остаётсяисторическим, неfinal.
- Releases/flip-race-v5.2.0-trial-webgl.zip:8960134bytes, SHA2562E078DDA8A639A06E39CC8F9F14A22728630EC06A190A5C4B1332E62D0515B10;5files/rootindex. Package manifest и Verification/bots-package-verification.json, fresh Builds/WebGL-v5.2.0-unpacked-text-final. Всеsource/ZIP/manifest/extractionbytesmatch.
- Verification/bots-http.json:4222/4223/4184 все5filesmatchfinalmanifest, no-store,version/aria-templatebranch. Actualnativefinalariaправильноописывает1human/A/BRpaw/autoReadyBot/L-S-Kinactive. НезависимыйP2HTMLreviewисправленONLYtemplatebranch, финальнаяUnityсборка повторена; framework/loader/WASMбайтысовпадаютспервым5.2candidate,dataноваясборкаотличается и невыдаётсязаidentical.
- СохраненыZIP5.1 SHA256EA75C7103C2C1600073E927B58D0DFB14C9A35970EC00875B8B9AF7C7E6CEF9E; чужойFIX491 SHA2563071311647C1391AF4FF9DC16B1DE0EDA1B735D5B117BD078158C684E7F93CD5. 4184прежнийPID103804проверенпоcommandlineиостановленпослеownchecks; oldcandidate146428остановлен. Временные4222/4223 остаютсядлянезависимойприёмки, убратьпосленеё.

## Source freeze и проверки

CoreController78C0AE389AD62F6CFD9882C9F8864A4DD61D2A3177707BF4CE400F5994B4EA67; Catalog670399D4AB8E363F5A633928556D8A289E780D80AF22DC85B6CAC1E1248B4808; RaceSimulation994968AA1FA901B43CB6070E7E7670AA62ABDF654D6B12DA2619825C8CBD9FAB. RaceSimulation меняет только классификацию/фабрику; арифметика прежняя. ПолныйSourceHashsnapshot в runtime/UI reports. Productionнеизмененпослеfinalproof, кромеисправленноготекстаHTML.

Reproduce: dotnet run --project Tools/BotPrototype/RuntimeCheck.csproj; Tools/BotUi/Run.ps1; Tools/BotUiCheck/Run.ps1; Tools/SoloUi/Run.ps1; Tools/PartyUi/Run.ps1; ParityRunner с неизменённойfixtures; FlightSettingsCheck; Verification/bots-package-check.ps1 сновойfresh target; nodeTools/BotPrototype/VerifyHttp.mjs origins. ТехническийWindowsAccessDenied(reparse) разрешён штатнойreviewedscopedэскалацией; настоящихauto-reviewrefusalsвэтойпоставкенебыло.

Следующее: root commit explicit owned paths, затем Каталитик независимыйUI/browser/ZIP/HTTP review наfreeze; исправитьконкретныедефектыеслинайдены, послеacceptanceостановитьсвои4222/4223. Не выдавать dispatch или собственныйPASS за независимуюприёмку. Продуктовых/техническихблокеровсейчаснет.

Документация BOT/affectedfeatures/каталог/контракт закоммичена в flip-race99a2b93. Каталитик начал independentbrowser наfresh4223 и владеетviewport; root не выполняет browserUI до завершения. Nativeevidence Verification/bots-native.json/screenshots копирована из собственногоtemp, финальныйHTTP4184/4222/4223 PASS. ZIP/template/source заморожены.
