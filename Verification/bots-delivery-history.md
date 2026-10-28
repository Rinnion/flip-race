# V5 BOT5 — 5.2.0-trial

28 октября 2026. Активная поставка: отдельный режим «С ботом», 1 человек + 1 бот, пять уровней на пяти полных трассах и двух готовых профилях. База Unity92b61a6 /5.1.0 принята. Источник: Каталитик01a13ea2-10f9-7821-b785-5c3a7e30b079, прямой userturn01a18388-ed46-71a0-bbf5-11f375fa1404, message01a18388-edc2-7451-9f24-7b18556caed4 («Погнали Делать дальше» после предложения пяти уровней); готовые профили/уровни — userturn01a18278-c0f5-7443-9b7a-3da7fdbd3558. Root проверил источник через read_thread. [Контракт](../../flip-race/docs/flip-race-v5-bots-delivery-2026-10-28.md).

Production/integration owner — Прогатик, Каталитик — scope и независимая приёмка. Историческая просьба о полоске/лапках уже исполнена UI501 и повторно визуально проверена на 4184/5.1.0: Verification/header-center-5.1.png. Она не заменяет BOT5. Стабильная4184 остаётся5.1 до проверки кандидата5.2. Чужой FIX491 сохраняется.

| Требования | Статус | Владелец и доказательство |
| --- | --- | --- |
| BOT-00 | working | Root Core factory; bots_ui Sol/medium: отдельный UI и custom gating |
| BOT-01/02 | working | Фиксированные до калибровки каталоги; 343 плана/50 библиотек; все Master100% и finish обе камеры. bots-first-levels.json, bots-complex-levels.json. Production экспорт ещё проверяется |
| BOT-03/04 | working | Все 343 плана Lagger finish в диапазонах; 1000 фиксированных seed, строгое среднее время по 10 парам. Constant actual outcomes без фильтрации; CascadeNovice поздние ошибки/честное выбывание. Производственный replay ещё не завершён |
| BOT-05 | working | Root BotRaceController exactdistance/shared Advance; bots_first_routes Sol/high строит полный actual Advance/Press replay обычным игроком, переменные кадры и human independence |
| BOT-06/08 | working | UsesSharedRaceCamera включает бот; физическая арифметика не менялась. Требуются новые regression runs |
| BOT-07 | working | bots_ui: Presentation ownership. Root: Web diagnostics + браузер после candidate |
| BOT-09 | todo | Root feature docs/README/evidence/source snapshots |
| BOT-10 | todo | Root regularWebGL5.2/ZIP/fresh extract/HTTP, затем Каталитик acceptance |

## Владение и модели

- Root: Core RaceSimulation.cs/BotRaceController.cs, Web diagnostics, интеграция/регрессии/сборка/доставка/STATUS; Sol/high integration.
- bots_first_routes Sol/high: Tools/BotPrototype/FirstRoutes.cs/.csproj и RuntimeCheck.cs/.csproj, соответствующие bots-first/bots-runtime отчёты. Не production.
- bots_complex_routes Sol/high: ComplexRoutes prototype/levels done; теперь BotPlanCatalog.cs/.meta и ExportCatalog tool, только новые файлы. 104 сложных плана и экспорт всех343.
- bots_ui Sol/medium: Presentation + own UI check. Не Core/Web/build.
- Astra/high metric review завершён; новых решений о физике/камере/процентах не принимали.

## Принятые решения и доказательства

Каталог общий всем уровням: obstacle logicalID после Right+radius без удара; positive только Successful; finish только полный служивший0.25s pulse; монеты/negative исключены. Master доказывает совместимость. Первые6пар239планов (Long/Short/Flow), сложные4пары104плана (Cascade/Mixed); обе камеры и20seed/пара/уровень. Конечная библиотека, детерминированный seedmodcount, повтор выбирает следующий; никаких online search/adaptation.

FirstRoutes «replayCount2278» — повторные симуляции Probe.Run с заново рассчитанными distance dt, не actiontape и не BOT05. Переименовывается в simulationRuns. Complex4555 тоже simulation runs. Actual runtime proof пишет отдельный полный Advance(dt)+Press tape включая rejected, сравнивает все поля/очереди/события с ordinary participant после каждого события.

Cascade31цель:21obstacle+9positive+finish. Для≤60% минимум3 непогашенныхошибки: min37.5s > Constantdeadline37.37636s. Root независимый bots-cascade-bound-audit.json подтверждён Astra/high. Внутреннее избыточное требование finish любого слабого Constant снято Каталитиком (контракт BOT03/04): калибровка полного маршрута/времени при Lagger; Constant те же планы/физика/inputs и честное left-only выбывание, actual goals/status/distance/time сохраняются, без выдачи plannedpercent за actual. Master finish100 обе камеры. Поздние CascadeNovice варианты выбывают x4755..7640/t22.989..36.102; Lagger18/31=58.0645%,37.5s. Исторический BLOCKED первый прототип сохранён неизменным.

Сохранить CONTROL/REWARD/FINISH/GATE/VIEW/TOUCH/SOLO: физика/геометрия/профили, обе части ворот, доступность untouched reward после удара, личные награды, finishdash2малые стрелки/.25s, UI501/половинный коридор, custom data. Бот не получает прав на изменённую физику или человеческий ввод.

## Проверки и следующий шаг

Прототипы и bound завершены; production source пока не проверен. Все изменения в рабочем дереве, commit ещё нет. Технический Windows reparse AccessDenied не был auto-review refusal; те же разрешённые записи через scoped PowerShell require_escalated прошли обычную проверку. Настоящие отказы не обходятся.

Следующее: завершить экспорт и actual runtime proof; интегрировать UI; запустить обязательные Core/settings/UI/legacy проверки; candidate5.2 на отдельном порту, actual browser и ZIP/fresh extract/hash; только затем4184 и независимая приёмка. Нерешённых продуктовых блокеров сейчас нет.

Сохранённые SHA256: ZIP5.1 EA75C7103C2C1600073E927B58D0DFB14C9A35970EC00875B8B9AF7C7E6CEF9E; чужойFIX491 3071311647C1391AF4FF9DC16B1DE0EDA1B735D5B117BD078158C684E7F93CD5.
## Снимок интеграции и кандидат

Production source frozen после Bot overlap readability fix. Core BotRaceController78C0AE389AD62F6CFD9882C9F8864A4DD61D2A3177707BF4CE400F5994B4EA67, Catalog670399D4AB8E363F5A633928556D8A289E780D80AF22DC85B6CAC1E1248B4808, RaceSimulation994968AA1FA901B43CB6070E7E7670AA62ABDF654D6B12DA2619825C8CBD9FAB. Изменение RaceSimulation — только классификация/фабрика; действующая физическая арифметика сохранена.

Регрессии на production Core: ParityRunner PASS все встроенные и unchanged JS/C#40228snapshots/1448208fields; SoloCheck PASS84cases/59482snapshots/1478actions; PartyCheck512; FlightSettingsCheck736/114roundtrips. Логи bots-{parity,solo,party,settings}-regression. Final-source SoloUi/PartyUi повторены PASS; BotUiCheck7542/120configs+25Fastapplies; независимый Tools/BotUi выполняет более полную actualUpdate матрицу. Runtime343×2cams×3frames и20seed matrices физически прошли; отдельный слабый human-success witness в harness заменён live-distance actions и финальный отчёт пересчитывается. Этот повтор не затрагивает production. Parent JSONaudit Distance-gap Complex report покрывает actual percase terminalDistance нового runtime proof, не дорисовывает данные калибровки.

RegularWebGL candidate http://127.0.0.1:4222/ PID146428, каталог Builds/WebGL-v5.2.0-final, UnityPID148768 завершился0, Logs/bots-final-build.log FLIP_BUILD_OK9091814bytes. ZIP Releases/flip-race-v5.2.0-trial-webgl.zip8960157bytes, SHA2562BB778F7695B2C359CFF85229ED9C59561F6B9947B00E7F8BC62F2C0ABFC5282. Fresh Builds/WebGL-v5.2.0-unpacked-final; все5source/ZIP/manifest/extraction hashes совпали; baseline5.1/FIX491 сохранены. Package proof Verification/bots-package-verification.json. Внешней публикации нет.

Root сейчас проводит собственный actualbrowser:4viewportready просмотрены (568×320,800×360,960×432,1280×800), MasterConstant реальный заезд запущен. Только после ownUI/browser/runtime окончательных green и HTTP4184 будет переключён. Каталитику пока не передан браузер/viewport во владение. Следующее: завершить ownbrowser, finalruntime+independentmanagedUI; записатьHTTP и переключение; commit/documentfinal, затем независимая приёмка.
Финальные runtime и independentmanagedUI PASS: bots-runtime-check.json4058scenarios/343plans/2cams/3frames, all1000seedcases eachcamera;22,833,691immediatecompletepersonalstates, actualreplayerror0,700human-successallgoals,10inputorderproofs. ПоляterminalDistance/terminalTime/completedIds в каждой записи; Complex original `p` уже содержит реальную дистанцию (не было лишь поля с именемDistance), runtime делает название явным. Никакого пропуска невыживших. SourceStabletrue. IndependentTools/BotUi:280895checks/1200matrix+119transitions/100realraces, все100firstterminalcontinuations, pause/resize иcustomgating; bots-ui.json. NativeMasterConstant33.05s/22из22/nohits, humanelim не остановил бота; nativeCascadeNoviceConstant actual10/31,4754.792116ед, честноеelimination. ПолныйLagger native идёт на финальной сборке.

IndependentreviewSol/high выявилP2 HTMLaria неверно описывал botкак2human. Исправлен ONLYtemplate state.bot branch, финальнаяregularWebGL пересобрана: Builds/WebGL-v5.2.0-final-text, Logs/bots-text-build.log9092229bytes/exit0. FinalZIP8960134bytes SHA2562E078DDA8A639A06E39CC8F9F14A22728630EC06A190A5C4B1332E62D0515B10; fresh Builds/WebGL-v5.2.0-unpacked-text-final. Framework/loader/WASM идентичны ранее проверенному кандидату; data отличается (новая сборка), не выдаётся за byteidentical. Finalcandidate4222PID139808, unpacked4223PID132348; старыйcandidate146428остановлен. Dynamicaria в finalbrowser проверена:1human/A/BRpaw, botautoReady/L-S-Kinactive. HTTP4222/4223 все5filesmatchfinalmanifest. Main4184 всёещё5.1 до окончанияownbrowser. ProductionC# frozen; толькоHTMLподпись исправлена, повтор физики не нужен.