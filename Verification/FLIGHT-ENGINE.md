# FLIGHT-02: независимые параметры и измерение полёта

Проверено 17 октября 2026 на финальном снимке Core ниже. Этот набор выполнен .NET runner; проверка WebGL описывается в отдельном browser-отчёте.

## Контракт

`GameState.Flight` хранит immutable `FlightSettings`: первое действие Flip/Jump, закон Linear/Gravity, независимый лимит воздушных повторов 0/1/∞, тяга, отдельные импульсы Flip/Jump, линейная скорость и высота линейного прыжка. `CreateTrial(Track, FlightSettings)` принимает этот снимок; старые фабрики и MotionMode/Profile сохранены. Четыре пресета 4.2.1 воспроизводятся точно, включая создание нового custom snapshot с теми же публичными значениями. Схема и допустимые уровни локального сохранения проверяются в UI отдельно от Core.

Обычный линейный прыжок разворачивается на точной заданной высоте; первое воздушное нажатие отменяет автоматический возврат и направляет героя к противоположной исходной опоре стене. Стена раньше вершины вызывает обратное движение без восстановления повтора. Допустимая посадка на камень завершает цикл и согласует опору, направление и заряд. Параметры, не относящиеся к выбранному закону, не меняют физику.

`PlayerState.LastFlight` — immutable `FlightMeasurement` с Duration, MaxDistance, Interrupted, EndedAt и исходной опорой. Время и максимум считаются по реальной траектории; экстремум ускоренного движения вычисляется аналитически. Повтор, вершина и инерционное касание не начинают новое измерение. Удар сразу публикует прерванное измерение, которое последующая посадка не превращает в чистое. Незавершённый полёт на финише не публикуется как завершённый. Реванш сохраняет настройки и сбрасывает состояние измерений.

Старый `MotionMode.Linear` сохраняет внутреннюю совместимость с исходной JS-интерполяцией. Публичный custom Linear использует постоянный модуль скорости по фактическому расстоянию; legacy-linear после преобразования в custom не обещает старую длительность .8 с при старте с поднятой опоры. Сброс именно к старому готовому режиму должен выбирать его фабрику.

## Выполненные проверки

- FLIGHT: 12 сценариев / **1 592 assertions**. Все 12 сочетаний first action × law × repeat limit, крайние параметры, оба направления, малые/большие dt, ранний/поздний первый повтор линейного прыжка, реальные посадки, opposite-stone/walkoff, аналитическое измерение, удар, пауза, snapshot, реванш, некорректные enum/NaN/Infinity, exact-copy маршруты четырёх пресетов.
- Полный ParityRunner: старые V1/V2/V3/V4/TUNE/CONTROL/CONTROL-03F — **963 / 3554 / 865 / 1770 / 1009 / 2165 / 1598 assertions**, все PASS; новый FLIGHT — 1592 PASS.
- JS differential: **40 228 снимков / 1 448 208 полей**, PASS, численная погрешность строго меньше 1e-8. JS-источник и baseline не изменены.
- TrackProofControl `--verify`: **20 451 checks**, PASS. Безопасное/идеальное/с trap время 21.0/20.3/20.6 с; Unlimited 13 с; 138 состояний восстановления. Геометрия сохранена.
- TrackProofTune: **47 929 checks**, PASS; совместимость прежних профилей, маршрутов, очереди и recovery.
- ControlStress: **1 518 checks / 24 games**, PASS. Асимметрия игроков, реальные удары, шаги 1/144, .317 и 100 с, точное дофиниширование на границе grace.

Полный stdout: [flight-engine.txt](flight-engine.txt). Независимый аудит повторён на тех же финальных SHA: **68 928 assertions / 1 864 games**, без замечаний. Его отдельные аналитические oracle, scope и воспроизводитель сохранены в [FLIGHT-PHYSICS-AUDIT.md](FLIGHT-PHYSICS-AUDIT.md); этот счёт не смешан с основным набором выше.

## Воспроизведение

Из корня Unity-проекта, .NET 8, без Unity и сторонних NuGet-пакетов:

```powershell
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --flight
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- Tools/engine-parity-fixtures.tsv.gz
dotnet run --project Tools/TrackProofControl/TrackProofControl.csproj -- --verify
dotnet run --project Tools/TrackProofTune/TrackProofTune.csproj
dotnet run --project Tools/ControlStress/ControlStress.csproj -p:UseAppHost=false
```

Unity runner `FlipRace.Editor.EngineParityTests.Run` включает EngineFlightTests. Copy helpers основного runner, TrackProofControl и TrackProofTune сохраняют Game.Flight и новые поля цикла/измерения; immutable FlightSettings/FlightMeasurement разделяются безопасно.

## SHA-256 и история

До FLIGHT, выпущенный Core 4.2.1: `B4D0FCB6CE0A9B731DA96F65293F44461A104202D8F07B39DFDFCEB080DFA46E`. Предыдущие результаты CONTROL-03F сохраняются в CONTROL-FINISH.md; они не заменены новыми числами.

| Файл | Финальный SHA-256 |
|---|---|
| Assets/Scripts/Core/RaceSimulation.cs | F1C3DAD26087616AF055EA18C7B5307455FEF32E77A29B9B86282210AA8F734D |
| Assets/Scripts/Core/FlightSettings.cs | 1E577574D0AF5C7F9D7B8CC62BF464343F9586E9F99DD55C2D3AAE84B52F18D4 |
| Assets/Editor/EngineFlightTests.cs | EBE60B3CDDD7DE3D7C88F9542B21458549EAEFDEFE6DF9D468215B8EFF96D752 |
| Assets/Scripts/Core/MotionProfiles.cs (не изменён) | 719CEE944B50ED87AA2BEFADD3266382486195BD40FC00AF9313FE58D6EBC417 |
| Assets/Scripts/Core/TracksControl.cs (не изменён) | 588BE383B503F40FFC8083450237296AD1A6EA0954488684712FD5A3F74449D4 |
| Assets/Scripts/Core/ContinuousMotion.cs (не изменён) | 98D06EA07562B30F2979C3BFAEBDB31723F1854AB62F953E76AD959A3AD85F36 |
