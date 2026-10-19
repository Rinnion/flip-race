# 4.5.0-trial — контроль двух режимов

19 октября 2026. **MODES-01–05 PASS:** финальный Web, ZIP, браузерная матрица и два заезда из распакованного архива проверены. Основание: [план](../../flip-race/docs/flip-race-two-modes-fixes-plan.md) и [аналитический аудит](../../flip-race/docs/flip-race-two-modes-audit-2026-10-19.md). Исторические отчёты 4.4 review не переписываются.

| Gate | Зависимость | Критерий | Текущее состояние |
|---|---|---|---|
| MODES-01 | Сохранённая 4.4 review | Воспроизводимая задержка, первый/повторный выбор обеих половин, исправление, старт и первый запуск анимации | **PASS в desktop IAB**; [отчёт задержки](MODES-LATENCY.md), [неизменность рисунка](MODES-ARTWORK.md) |
| MODES-02 | 01 | Две готовые кнопки + custom; точные поля FastFlip; миграция старых ID без потери custom; удалённые hitbox не действуют | **Source/codec/browser PASS**: 670 assertions,111 физических round trips; фактические oldID1/custom migration, Fast/custom reload, reset11, clipboard, compact menu. [Точный scope](MODES-UI.md) |
| MODES-03 | 02 | Общий короткий участок: gate, before/after, независимая цепочка, 64×48 landing и безопасный walk-off в обеих физиках; старый late-tail RED исправлен | **Source/independent PASS**: stage 2 466 assertions /90 игр, safe15/full13.5 обеих физик; after и исторический late-tail GREEN. [Независимый отчёт](MODES-AUDIT-PLAN.md); финальный браузер относится к05 |
| MODES-04 | 03 | Три трассы × два профиля: safe 36, full 33.2, trap, окна, восстановление после реакции 150 мс, чистый финиш для всех историй наград | **Independent PASS** на Tracks717C/CoreCFA0: 29 693 assertions /296 игр, 146 safe±60 мс, 9 465 full-window симуляций, 370 recovery states и 2¹¹ release-subsets/track. [Итоговый аудит](MODES-AUDIT.md) |
| MODES-05 | 04 | Финальный Web, реальные браузерные маршруты/обе половины/custom/reload, ZIP и загрузка распакованного архива | **PASS**:6safe/6full сочетаний; намеренная final-only дуэль; два negative-заезда из ZIP, console final/unpacked[]. Все попытки сохранены в [браузерном отчёте](MODES-BROWSER.md). Публикации нет |

Web `Builds/WebGL-v4.5.0-final`: 5 файлов /8 680 367 байт (8.278 MiB), `Logs/modes-final-build.log` success0. [ZIP](../Releases/flip-race-v4.5.0-trial-webgl.zip): 8 549 700 байт (8.154 MiB), SHA-256 `a200ad9852502a8cafdf3171814c6dfef30499b9c83d73a5ea5a8643590eb8e4`. Новый `Builds/WebGL-v4.5.0-unpacked` извлечён без перезаписи существовавшего каталога. Независимо совпали все5 SHA между ZIP/manifest/распакованным/Web; root index,4relativeURL,gzip и decodedWASM header проверены. [Доказательство архива](modes-package-verification.json) включает фактическую браузерную загрузку и Chosen/Long +Fast/Flow negative: обоим33,5с/0hits. Временный сервер4198 после проверки остановлен; текущая игра на4184.

## Сохранённые исходные поставки

- 4.4 review: [ZIP](../Releases/flip-race-v4.4.0-trial-review-webgl.zip), 8 549 837 байт; SHA-256 `130ce3cd23c843fcaa36e710bf236fcf76c245ad1e9305be62a7a835663c9cae`. Web `Builds/WebGL-v4.4.0-review`: 5 файлов / 8 680 125 байт; [проверка пакета](tracks-review-package-verification.json).
- 4.3: [ZIP](../Releases/flip-race-v4.3.0-trial-webgl.zip), SHA-256 `f48ba88622f28f80adf92871b54670bdf8c1e36053a0460750dde9b94b032c9a`; сохранён `Builds/WebGL-v4.3.0-final`.
- [Старый late-tail RED](TRACKS-LONG-TAIL-RED.txt) остаётся историческим доказательством. Новый независимый стенд хранит неизменную копию геометрии D3 для проверки исправления даже после перестановки текущих трасс.

Пределы: desktop Chromium IAB с мобильными viewport не равен физическому телефону. Физический multitouch, производительность телефонов и itch.io iframe пока не проверены. Готовность одного gate не означает готовность выпуска.
