# CONTROL-04: браузер и поставка 4.2.1-trial

17 октября 2026 года. **CONTROL-04 PASS: финальная рабочая Web-сборка, игровые browser-заезды, ZIP и фактический заезд из распакованного архива проверены.** Среда — настоящий Chromium во встроенном браузере Codex на Windows; мобильные размеры являются viewport настольного браузера, не физическим телефоном.

Сборка `Logs/control-release-build.log`: exit0 / `FLIP_BUILD_OK`, **5 файлов / 8 658 019 байт / 8,257 MiB**. Core `b4d0fcb6…`, TracksControl `588be383…`; [независимые проверки и полные SHA](CONTROL-FINISH-AUDIT.md). Рабочий URL: [4.2.1-trial](http://127.0.0.1:4184/?v=4.2.1-trial).

## Реальные игровые маршруты

[Полный сжатый trace](control-track-browser-trace.json.gz): **11 заездов / 89 действий / 3291 различимый снимок состояния**. Управление выполнено настоящими A/L; счётчики и координаты прочитаны из диагностики. Число снимков и интервал публикации состояния не являются видеокадрами, измерением FPS или оценкой производительности устройства.

| Заезд | Верхний / нижний игрок | Награды и ошибки |
|---|---|---|
| Moderate safe / ideal | **21,0 / 21,0 с** и **20,3 / 20,3 с** | Safe без наград; ideal по 2 ordinary + 1 finish; 0 ударов |
| Linear safe / ideal | **21,0 / 21,0 с** и **20,3 / 20,3 с** | Ideal при 568×320, обе опоры и пауза проверены; 0 ударов |
| Jump safe / ideal | **21,0 / 21,0 с** и **20,3 / 20,3 с** | По 2 ordinary + 1 finish на ideal; 0 ударов |
| Unlimited safe / ideal | **21,0 / 21,0 с** и **20,3 / 20,3 с** | По 2 ordinary + 1 finish на ideal; 0 ударов |
| Unlimited, проходы по высоте | **13,0 / 13,0 с** | 12 действий, включая 8 воздушных; четыре прохода, 0 наград / ударов |
| Попадание против пропуска только финиша | **20,3 / 20,4 с** | У обоих 2 ordinary, finish 1/0; 0 ударов; победил верхний |
| Ideal против видимой ловушки | **20,3 / 20,6 с** | У обоих 2 ordinary +1 finish; negative 0/1; 0 ударов; победил верхний |

На всех проверенных финишах физическая дистанция достигла ленты, ordinary active/queue пусты. Одинаковые маршруты дали ничью. Последняя цель меняет именно реальное прибытие на **0,1 с**; её счётчик отделён от двух обычных landing-наград.

В trace и изображениях присутствуют настоящие контакты с обеими метками: [Moderate, нижняя опора](control-final-moderate-floor-landed.png), [верхняя опора](control-final-moderate-ceiling-landed.png), [Linear, нижняя](control-final-mobile-floor-landed.png), [верхняя](control-final-mobile-ceiling-landed.png), [Jump, нижняя](control-final-jump-floor-landed.png), [верхняя](control-final-jump-ceiling-landed.png). Отдельно видны [финишная цель](control-final-moderate-finish-target.png) и [активный рывок](control-final-moderate-finish-dash.png), [mobile-результат](control-final-result-mobile.png), [дуэль](control-final-duel.png), [высотные проходы](control-final-passages-flight.png).

## Пауза, ввод и компактный интерфейс

Linear при **568×320** поставлен на паузу во время ускорения на настоящем камне. До и после ожидания совпали elapsed **4,873**, позиции **1081,754528… / 1074,494528…**, `Support=Stone`, `control-floor-pad`, Y178 и остатки boost **0,5118679245… / 0,5778679245…**. После продолжения оба завершили ideal за 20,3 с. [Пауза на опоре](control-final-mobile-paused-stone.png); check `stone-pause` в trace имеет `okay=true`.

Отдельный [UI smoke](control-ui-smoke.json) подтверждает одинаковых героев Flower **3/3**, раздельную готовность `[true,false]`, отсчёт только после обоих, паузу и portrait **390×844**. Отсчёт **2,845…** и нулевые дистанции точно сохраняются при смене ориентации. После явного продолжения pointer верхней половины даёт `[flipping=true,false]`, нижней — `[true,true]`; свободный участок заканчивается **10,0 / 10,0 с**. Это раздельные pointer-действия, не физический одновременный мультитач.

Финальные изображения: [главное меню](control-final-menu.png), [мобильное меню](control-final-menu-mobile.png), [legacy-экран](control-final-legacy-mobile.png), [правила](control-final-rules-mobile.png), [portrait](control-final-portrait.png), [результат](control-final-result.png). Номер **4.2.1-trial** остаётся рядом с названием в проверенных состояниях. Root подтвердил пустой список console warnings/errors финальной рабочей сборки.

## ZIP и его отдельный снимок

[ZIP 4.2.1-trial](../Releases/flip-race-v4.2.1-trial-webgl.zip): **8 530 094 байта / 8,135 MiB**. SHA-256:

`eed4c07535dc8c63dddd6a62760b4e071f90ae5a1e038cc5686005ff4c08e3b4`

[Manifest](../Releases/flip-race-v4.2.1-trial-webgl.zip.manifest.json) подтверждает корневой `index.html`, четыре относительных runtime-ресурса, WASM и SHA каждой записи. ZIP независимо извлечён в **`Builds/WebGL-v4.2.1`**: ровно пять файлов, все размеры и SHA совпали; [свидетельство распаковки](control-packaged-verification.json).

Скрытый Node PID **83892** обслуживает только **127.0.0.1:4192**. [Распакованная поставка](http://127.0.0.1:4192/) отвечает HTTP200; логи `Logs/control-packaged-server.log` и `.error.log`. **Фактический packaged browser PASS:** видимая версия 4.2.1, полный Moderate ideal у обоих **20,3 с / 2 landing +1 finish / 0 negative / 0 hits**, console warnings/errors пусты. [Сжатый trace](control-packaged-browser-trace.json.gz), [результат](control-packaged-result.png). Это отдельный заезд **8 действий / 309 снимков**; он не включён в 11 заездов / 89 действий / 3291 снимок рабочей сборки и не смешивается с ручным UI smoke.

Сохранённые 4.1.0 ZIP/manifest и `Builds/WebGL-v4.1.0` не изменялись; [reference4188](http://127.0.0.1:4188/) остаётся отдельно. Кандидат 4.2.0 до 03F сохранён без ZIP в `Builds/WebGL-v4.2.0-pre-finish`; [его manifest](control-pre-finish-build-manifest.json) и [ранние четыре заезда](control-pre-finish-browser-trace.json.gz) — исторические свидетельства. `control-free-*` относятся к раннему G2, а `control-linear-mobile-*` — к 4.2.0 до замены финишной награды. Они не выдаются за final4.2.1.

На itch.io ничего не опубликовано. Физический телефон, одновременный мультитач и iframe itch.io не проверены. Выбор наиболее удобного управления остаётся [человеческим плейтестом](CONTROL-PLAYTEST.md), а не результатом автоматических заездов.
