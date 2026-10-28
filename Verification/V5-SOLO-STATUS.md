# V5 SOLO — 5.1.0-trial

Собственная приёмка Прогатика завершена 28 октября 2026. Независимая приёмка Каталитика: source, основной браузер и финальный текст/ZIP прошли; проверка окончательного HTTP4184/перезагрузки ещё ожидается. Production заморожен. Блокеров реализации нет.

Источник: прямые сообщения пользователя в Каталитике `01a13ea2-10f9-7821-b785-5c3a7e30b079`, turns `01a18278-c0f5-7443-9b7a-3da7fdbd3558` (один человек, та же полная трасса, без сохранений) и `01a18304-5248-7142-b086-bfbde85cee46` (следующий этап после положительной пробы constant). Прогатик сверил их через read_thread. [Контракт](../../flip-race/docs/flip-race-v5-solo-delivery-2026-10-28.md). База `3a23526` / 5.0.1-trial, UI501 сохранён.

| ID | Статус | Доказательство |
| --- | --- | --- |
| SOLO-00 | verified | Ровно один участник, самостоятельный вход «Один», безопасные переходы; Core и фактический UI. |
| SOLO-01 | verified | Те же пять полных трасс и физика; 84 сценария SOLO/PARTY, 59 482 полных личных снимка, оба профиля и камеры. |
| SOLO-02 | verified | Constant L/36, окно1520×240/C0−288,8, выход только слева; Lagger без выбывания. Core границы/36+/приоритет финиша; native обе камеры/пауза/resize. |
| SOLO-03 | verified | Один старт, немедленные finish/elimination, полный сброс временного состояния. Native elimination1036 и finish39,50с; retry сразу countdown. |
| SOLO-04 | verified | Один нормальный выбор/результат/личная метка, BR56×56, A, нейтральная пауза. Native568/800/960/1280, managed safe area/DPR и вся лапка. |
| SOLO-05 | verified | Нет новых записей прогресса/результата. Managed prefs snapshot/reload и native reload с нулевыми дистанцией/наградами; существующие настройки сохраняются. |
| SOLO-06 | verified | PartyCheck512, PartyUi71 100, legacyUI72 892, все ParityRunner suites и неизменённые40 228 JS/C# snapshots. UI501/профили/данные трасс сохранены. |
| SOLO-07 | verified | [SOLO](../../flip-race/docs/features/solo-play.md), VIEW/TOUCH/REWARD и каталог обновлены; сохранение CONTROL/FINISH/GATE описано и проверено. |
| SOLO-08 | working | Финальный WebGL/ZIP/fresh extraction/HTTP/root browser прошли. Каталитик подтвердил текст и ZIP; ожидается его окончательный4184/reload verdict. |

## Реализация и владение

Прогатик — единственный production owner и интегратор, Sol/high. Специалист solo_core Sol/high: минимальное расширение Core и Tools/SoloCheck; solo_ui Sol/medium: новый Tools/SoloUi и целевые UI-отчёты. Ограниченное readonly размещение — Astra/high; независимое source review — Sol/high. Дополнительных игровых решений нет. Source review исправил четыре строки справки и боковые маски; Каталитик запросил нейтральную SOLO-паузу. Последняя правка затронула только тексты паузы/portrait, без Core/ввода/геометрии.

Core: `CreateSolo(...)`, `IsSolo`, `UsesSharedRaceCamera`; компания по-прежнему2–4, legacy2. Один и компания используют общую физику, камеру и терминальные события. Новая SoloPresentation, безопасные переходы настроек/выбора героя, one-player shared projection/artwork, диагностика/aria. Нового арта нет; [арт-направление](../../flip-race/docs/flip-race-art-direction.md) сохранено. Данные трасс/Resources diff отсутствует; эталоны не регенерировались.

Core SHA256 `57ae382f08bf73a0bdc1a9726248dd08d022bdd6f2ae7d5982c52cd8304604f2`. Конечные33-файловые production snapshots во всех трёх UI-отчётах повторно сверены после текстовой правки и сборки.

## Проверки

- [SoloCheck](solo-core.json), [журнал Core/регрессии](solo-core-checks.log):84 сценария,59 482 полных сравнения,1 478 одинаковых действий.7 662 156 assertions — рекурсивные поля, не число независимых прохождений.74 финиша/10 выбываний; столкновения, ускорения/ловушки/монеты/рывок, чистый повтор. PartyCheck512 и40 228 fixtures/1 448 208 полей PASS с допуском1e−8.
- [SoloUi](solo-ui.json):4 962 assertions/28 cases/3 258 actual Update/686 Pointer. [PartyUi](solo-party-ui.json):71 100/216. [Legacy](solo-legacy-ui.json):72 892/27. Переходы SOLO→company2/3/4→split/ghost/shared opposite/adjacent→SOLO, меню/ввод/prefs/повтор. Старые проверки не ослаблены. [Описание](../Tools/SoloUi/README.md).
- [Настройки](solo-settings-check.txt):736 assertions/114 физических round trips. FlightUiCompile:0 warnings/errors.
- [Native](solo-native.json): одна готовность/цифра/лапка, реальный клик по её краю; constant выход слева, чистый retry/reload, полный full-short/Fast/Lagger финиш39,50с, возвратcompany4 и ghost→SOLO. Размеры568×320/800×360/960×432/1280×800, пауза/resize/portrait, версия и aria. Console warn/error0. Основная поведенческая матрица снята до последней правки только текста; финальный text-build отдельно загружен из ZIP, проверены исправленная пауза и запуск.
- [Последняя пауза](solo-final-pause-568.png), [финальная гонка](solo-final-racing-568.png), [одиночный финиш](solo-finished-tablet.png), [готовый4184](solo-stable-ready.png), [компания после SOLO](solo-to-company4.png).

Каталитик независимо подтвердил source по хешам, actual Constant/Тыква/start/elimination/retry, Fast/Lagger полный финиш38,00с, компанию4/UI501 и console0. На финальной распаковке подтвердил «Гонка приостановлена.» и независимо сверил все5 ZIP entries с build/unpacked, прежнюю5.0.1 и чужой FIX491.

## Финальная поставка

[Играть4184](http://127.0.0.1:4184/) · [ZIP5.1.0](../Releases/flip-race-v5.1.0-trial-webgl.zip) · [manifest](../Releases/flip-race-v5.1.0-trial-webgl.zip.manifest.json).

Финальная текстовая сборка: `Builds/WebGL-v5.1.0-final-text`, Unity6000.0.64f1 regular WebGLTrial, `Logs/solo-text-build.log`, exit0,5 файлов/9 048 270байт. [Маркеры журнала](solo-build.txt). Предварительный `Builds/WebGL-v5.1.0-final` не является финальной поставкой.

ZIP8 917 868байт, SHA256 `ea75c7103c2c1600073e927b58d0dfb14c9a35970ec00875b8b9af7c7e6cef9e`. [Независимая упаковка](solo-package-verification.json): source/manifest/ZIP/fresh extraction хеши всех5 файлов совпали, root index/relative refs/gzip/WASM/path safety проверены. Финальная свежая распаковка `Builds/WebGL-v5.1.0-unpacked-text-final`; прежняя распаковка5.1 не финальная.

[HTTP4184](solo-stable-http.json): все5 файлов совпадают с финальным manifest. [Сервер](solo-stable-server.json): PID103804, каталог `Builds/WebGL-v5.1.0-final-text`. Вкладка с готовым SOLO оставлена, override viewport сброшен, свои промежуточные вкладки закрыты. Для независимой приёмки пока оставлены4220 (тот же final-text) и4221 (финальная распаковка); PID/каталоги в solo-candidate-server.json/solo-unpacked-server.json. После завершения приёмки их можно остановить;4184 оставить.

Сохранены5.0.1 ZIP `b8da93cd29a279a115d8c1e0562aebfa431a245ab2750caa2a0ef6bf0bc61d4e` и её сборка;4.9.1/4.9.2/5.0.0 тоже сверены упаковщиком. Чужой `Verification/FIX491-RELEASE.md` не изменён/не включается: SHA256 `3071311647c1391af4ff9dc16b1de0eda1b735d5b117bd078158c684e7f93cd5`.

Ограничение: viewport и managed touch не заменяют физический телефон; человеческий плейтест не блокирует поставку. Внешней публикации/загрузки не было. Следующее действие: независимый verdict Каталитика по финальному4184/reload и закрытие SOLO-08.