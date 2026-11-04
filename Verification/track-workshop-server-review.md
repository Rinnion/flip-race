# Track Workshop 0.4: независимое ревью сервера

Статус: **итоговое ревью исходников завершено, нерешённых блокеров нет**. База — `6b537a6`, сверена рабочая версия 0.4 от 4 ноября 2026. Reviewer: Sol/high. [STATUS поставки](TRACK-WORKSHOP-SERVER-STATUS.md).

## Область

Целевая проверка Program/ServerOptions/ServerPolicy и развёртывания: неизменность локального запуска, явный удалённый bind, Host/Origin, отсутствие доверия forwarded-заголовкам, TLS/basic auth на Caddy, приватный порт приложения, ограничение одновременных POST и изоляция расчётов. Reviewer не меняет сервер, UI, физику и конфигурацию развёртывания.

## Результаты

- `WorkshopService` после конструктора только читает каталог; файлы JSON хранятся как строки, входной запрос не выбирает произвольный путь или URL.
- Каждый Analyze/Replay заново разбирает собственный TrackPackage. Replay создаёт отдельные GameState, hidden states, списки samples/events и наборы наблюдений. Изменяемого общего состояния расчётов не найдено.
- `FlightSettings` содержит readonly-поля; общие `FlightPresets` не являются изменяемыми настройками конкретного запроса. `RaceSnapshot.Clone` копирует изменяемый timeline.
- `ServerOptions` сохраняет default `127.0.0.1:4186`; non-loopback bind без конкретного `WORKSHOP_PUBLIC_ORIGIN` отвергается. Bind принимает IP, порт и concurrency — проверенные числа; origin не содержит credentials, path, query, fragment или wildcard.
- `ServerPolicy` проверяет Host и JSON Content-Type до endpoint; присутствующий Origin должен точно совпадать с настроенной идентичностью. Публичный Host сопоставляется с публичным origin, а не с адресом приватного upstream. Wildcard CORS не добавлен.
- `ServerPolicy` берёт fail-fast lease до model binding/body read, держит его через `await next(context)` и сериализацию, освобождает в finally. При исчерпании capacity — 429 с Retry-After; GET health остаётся доступен.
- Program явно отключает обработку forwarded-заголовков через `PostConfigure<ForwardedHeadersOptions>` с `ForwardedHeaders.None`. Поэтому даже generic hosting-переменная `ASPNETCORE_FORWARDEDHEADERS_ENABLED` не включает доверие к таким заголовкам. Возможность автоматического включения middleware сверена с [документацией Microsoft](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).
- По явному решению root запрос JSON без Origin разрешён и в серверном режиме для CLI; проверке подлежит точное совпадение любого присутствующего Origin. Отсутствие Origin само по себе не считается дефектом этой поставки.
- Compose не публикует порт app; app подключён только к internal backend. Caddy подключён к backend и edge, публикует только входные 80/443 и хранит TLS-состояние в named volumes. Docker запускает приложение под непривилегированным пользователем app.
- `basic_auth` покрывает весь Caddy host, включая UI/API/health; user/hash передаются runtime placeholders, `.env` содержит пример с пустым обязательным hash. Правила hash и placeholders сверены с [Caddy basic_auth](https://caddyserver.com/docs/caddyfile/directives/basic_auth). Proxy использует HTTP `app:8080` без переписывания Host; сохранение исходного Host соответствует [официальным defaults Caddy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy#defaults).

## Найденное и исправленное

**P1: обход concurrency через trailing slash — исправлено.** Первоначальная классификация тяжёлого запроса сравнивала Path строго с `/api/analyze` и `/api/replay`, тогда как endpoint routing принимает также завершающий slash. Это позволяло обойти lease адресами `/api/analyze/` и `/api/replay/`. Владелец заменил классификацию на case-insensitive сравнение после `TrimEnd('/')`; проверены код исправления и четыре варианта пути в тесте исчерпанной capacity. Других материальных дефектов в согласованном объёме не найдено.

## Проверки и ограничения

- Reviewer прочитал итоговые Program.cs, ServerOptions.cs/ServerPolicy, csproj, WorkshopService.cs, API.md, FlightSettings.cs и соответствующие места RaceSimulation/RaceSnapshot; полный набор Deploy-файлов, включая .dockerignore и инструкции.
- Прочитаны исходники `ServerTests/Program.cs` и результат `ServerTests/results.json`: **75 assertions, passed=true**. Тесты владельца проверяют конфигурацию, Host/Origin, prebinding admission, trailing-slash/case варианты, доступность GET и освобождение lease после ошибок/отмены. Reviewer не выдаёт этот запуск за собственный и не повторяет матрицу.
- Проверка `git diff --check` по проверенным серверным/deployment-файлам прошла. Код отключения forwarded middleware перечитан после финального изменения владельца.
- Reviewer не запускал Docker/Compose, не обращался к стороннему Docker context, не проверял настоящий DNS/сертификат/диалог Basic Auth и не выпускал архив. Нативные HTTP/browser и package checks выполняет root. Заключение об отсутствии блокеров относится к исходникам и конфигурации, а не к уже работающему внешнему серверу.
