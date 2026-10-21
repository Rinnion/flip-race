# MIX — пятая карточка и совместимость каталога

21 октября 2026. **PASS: 2 870 проверок реальных обработчиков** и **12 проверок отдельной ветки `FLIP_MIX_STAGE`**. [Основной stdout](mixed-catalog-check.txt), [геометрия карточек](mixed-catalog-layout.json), [stage stdout](mixed-catalog-stage-branch.txt). Старый `PostPlaytestCatalogCheck` и его исторические результаты 4.6.1 не изменены.

Изменены только `RaceTracks` в `TracksRhythm.cs` и два обращения к индексу в `FlightSettingsMenu.cs`. Каталог содержит прежние Long, Short, Flow, Cascade, затем **«Полоса уловок» / `mixed-tricks`**. Физика, параметры управления, стрелка и старые геометрии сохранены.

| Вход | Результат |
|---|---|
| `Get(int)` 0/1/2/3 | Long / Short / Flow / Cascade — прежний внешний контракт |
| `Get(int)` 4/5 и недействительные значения | Cascade; добавление карточки не переопределяет старый индекс |
| `GetAt(int)` 0…4 | Текущая UI-позиция, включая новую fifth card на 4 |
| `GetAt(int)` вне каталога | Cascade |
| Строковые Dense/Waves | Нормализуются в Cascade |
| Строковый `mixed-tricks` | Новая трасса, общий `Get()` для обоих профилей |

`InitialFlightTrack` и `FlightPointer` используют `GetAt`; остальная передача выбранной трассы остаётся по строковому ID. Никакой новой persistence индекса/трассы не добавлено. В отдельной stage-конфигурации `All` по-прежнему содержит четыре коротких участка, их `GetAt`, `IsFull` и строковый `Get` работают; legacy-индексы остаются прежними.

Новый [MixedCatalogCheck](../Tools/MixedCatalogCheck/README.md) вызывает production `FullTrackRect`, `FlightPointer`, `CreateSelectedFlight`, `BeginFlightMenu` и `Act`. На 568×320, 844×390 и 1280×720 проверены центр и четыре внутренних угла каждой карточки. Пятая карточка активна, шестой слот не реагирует. Карточки высотой 44 px не пересекают footer или постоянную версию. Выбор не начинает игру и не меняет текущую гонку; применение оставляет обоих игроков неготовыми. Main, Fast и отдельный custom None сохраняются. Для старых Dense/Waves и новой mixed проверены все 16 пар героев, ready/finished, реальное ожидание второго игрока и переход countdown→racing.

Исходные SHA после интеграции:

- `TracksRhythm.cs`: `7B90CF8E786A997B8863FDC677EA5E07155D65898B1D0A53116DAC709F5441D7`.
- `FlightSettingsMenu.cs`: `5B084615A30EA97B23ACF3A3CDB2FC87E4686ABE37ACE0CE83154A3DEA13263A`.
- Финальный `TracksMixed.cs`: `01C67639E567F2325D668678786AE5EAFDB5352C8204D8CC876EF288645210E0`.

Воспроизведение из корня проекта:

```powershell
dotnet run --project Tools/MixedCatalogCheck/MixedCatalogCheck.csproj
```

Это выполнение настоящих методов с Unity value types в .NET, не отрисованный браузер и не испытание физического касания. Финальный browser/build/package остаются отдельным gate; [машинная фиксация](mixed-full-verification.json) содержит baseline-сверку и текущие SHA.
