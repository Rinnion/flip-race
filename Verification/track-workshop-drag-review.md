# Track Workshop: проверка переноса мышью

4 ноября 2026. Read-only review, Sol/high. Область: инструмент редактирования; production Core, JSON-каталог и игровая механика не меняются. Владелец UI — workshop_ui; итоговую браузерную приёмку и пакет проверяет root. Затронутые контракты: GATE, HIDDEN, REWARD, FINISH, CONTROL, TRACKFILE.

## Инварианты до реализации

Источник: `Assets/Scripts/Core/TrackJson.cs`, `ObstacleGeometry.cs`, `RaceSimulation.cs`, `FinishTargetGeometry.cs`.

- Gate X: базовый X в `[0,length]`, обе полосы с собственными offsets целиком в трассе. Width, offsets и logical ID сохраняются. Gate Y: сдвиг обоих краёв проёма на одну величину, сохранение его высоты; `gapTop>0`, `gapBottom<240`, оба в `[0,240]`.
- Камень: перенос X в `[0,length-width]`, без изменения kind/height/hidden/nullable timing.
- Группа obstacle + linked rewards: единый допустимый delta, сохраняющий расположение связанных reward X и заданных числовых `clearX`/`safeFlipX`/`riskFlipX`. Не добавлять отсутствующие поля ради переноса; ID, obstacleId, reward Y, placement и freeReward сохраняются.
- Reward XY: X в `[0,length]`, Y в `[0,240]`; у finish X строго меньше length. Radius и сырые legacy finish dimensions сохраняются; контакт стрелок задаёт Core.
- Landing: только X; остаётся связь с floor/ceiling stone и пересечение окна `X±landingWidth/2` с камнем. Сырая Y не определяет физическую плоскость и не должна подменяться координатой рисунка.
- SafeRoute/riskRoute, cues, неизвестные и остальные неизменяемые поля JSON не переписываются. Перенос не готовит новый бот-эталон и не переназначает нажатия.
- Drag разрешён только для текущего проверенного документа и revision. Исходный JSON с duplicate keys не должен нормализоваться в рабочий через JSON.parse. После изменения геометрии прежние references остаются явно устаревшими; очистка — явное сохранение без references.
- Pointer cancellation/lost capture очищает gesture state; старый HTTP-response не возвращает предыдущую геометрию или путь.

## Результат bounded source review

Проверены новые `Tools/TrackWorkshop/Web/model.mjs` и `app.js` после интеграции переноса. Source blockers не обнаружены. Полные матрицы не повторялись; тесты и браузерную приёмку выполняют UI-owner и root.

- `patchNumbers` заменяет только выбранные числовые токены: остальные байты, неизвестные поля, длинные десятичные значения, route arrays и botReferences сохраняются. Собственный разбор отвергает duplicate keys. Drag стартует только при `state.valid` для текущей проверенной revision.
- `translateObject` считает единый допустимый delta группы. Gate X учитывает обе полосы и offsets, Gate Y сохраняет высоту проёма и обе ненулевые части. Камни остаются привязаны к своей стене. Связанные rewards сохраняют obstacleId, сдвигаются вместе с камнем/воротами и заданными положительными порогами.
- Нулевые `clearX`/`safeFlipX`/`riskFlipX` не сдвигаются: сохраняются sentinel/default значения. В production сбор связанной награды при `clearX=0` всё равно ждёт `ObstacleGeometry.Right + PlayerRadius`, поэтому перенос камня не оставляет старый абсолютный порог.
- Landing preview использует raw `landingWidth` для пересчёта clipped окна. Пропущенное значение получает default 0 и отвергается production TrackJson; валидный drag не может получить отсутствующий/нулевой landingWidth. Сырая Y сохраняется, плоскость берётся от камня. Finish bounds учитывают четыре штриха, поэтому X остаётся строго до финиша.
- Предпросмотр живёт отдельно от `state.analysis`. Pointerup сначала снимает global `drag`, затем освобождает capture; `lostpointercapture` не может сделать второй commit. Cancel/Escape/blur сбрасывают preview и readonly. Нормальная отмена не меняет JSON.
- Commit сверяет исходный текст и revision, записывает token patch и сразу запускает `edited`, который инвалидирует путь/старые responses. Последующая серверная проверка заменяет preview либо показывает ошибку с последней проверенной схемой. Undo допускается только если текущий текст в точности равен результату последнего переноса; восстанавливает полный прежний source и повторно валидирует.

Изменения игровых исходников, каталога, backend API или эталонов в этом review не выполнялись. Итоговое утверждение о работающем жесте и опубликованном пакете требует отдельной браузерной проверки root.
