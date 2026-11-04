# Мастерская 0.4: запуск на сервере

Готовый серверный архив распаковывается в отдельную папку. В его корне находятся `Dockerfile`, `compose.yaml`, `Caddyfile`, `.env.example`, `.dockerignore` и этот файл; в `app/` — опубликованные `TrackWorkshop.dll`, runtime/deps JSON, `Web/` и `Catalog/`. Docker собирает образ из готового приложения; исходники Unity и .NET SDK на сервере не нужны. Серверный выпуск использует официальный ASP.NET 10 image и запускается под пользователем `app`. [Microsoft: контейнеры .NET](https://learn.microsoft.com/en-us/dotnet/core/docker/).

На 4 ноября 2026 года поддержка .NET 8 заканчивается 10 ноября 2026 года, а .NET 10 LTS поддерживается до 14 ноября 2028 года. Поэтому серверный пакет публикуется для .NET 10; локальная версия мастерской по умолчанию остаётся на .NET 8. [Официальные сроки поддержки Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

1. Подготовьте Linux-сервер с Docker Engine и Compose V2. Направьте DNS A-запись выбранного домена, например `workshop.example.com`, на сервер; AAAA добавляйте только при работающем IPv6. Для Caddy должны быть доступны TCP 80 и 443, для HTTP/3 — также UDP 443. Caddy получает и продлевает HTTPS-сертификат автоматически. [Условия Automatic HTTPS](https://caddyserver.com/docs/automatic-https).
2. Скопируйте серверный архив на сервер, распакуйте и перейдите в его корневую папку. Все дальнейшие команды выполняются там.
3. Получите хеш пароля интерактивно:

   ```sh
   docker run --rm -it caddy:2 caddy hash-password
   ```

   Введите пароль в приглашении: ввод скрыт, пароль не попадает в аргументы команды или историю shell. Скопируйте выведенный хеш целиком. [Caddy hash-password](https://caddyserver.com/docs/command-line#caddy-hash-password).
4. Выполните `cp .env.example .env`, затем откройте `.env` текстовым редактором. Укажите домен без `https://`, порта и пути, имя пользователя и хеш:

   ```dotenv
   WORKSHOP_DOMAIN=workshop.example.com
   WORKSHOP_USER=developer
   WORKSHOP_PASSWORD_HASH='$2a$14$...весь_выведенный_хеш...'
   ```

   Это пример записи, а не готовый хеш. Сохраните одинарные кавычки вокруг настоящего хеша: Compose прочитает `$` буквально. Все три переменные обязательны; пустой хеш останавливает Compose до запуска. Не задавайте одноимённые переменные в shell: они имеют приоритет над `.env`. [Правила `.env` и подстановки Compose](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/).
5. Проверьте конфигурацию, соберите и запустите:

   ```sh
   docker compose config --quiet
   docker compose build --pull
   docker compose up -d
   docker compose logs --tail=50 caddy app
   ```

   Откройте `https://ваш-домен` и введите выбранные логин и пароль в диалоге браузера. Пароль закрывает всю мастерскую, включая API. [Caddy basic_auth](https://caddyserver.com/docs/caddyfile/directives/basic_auth).

Приложение слушает `0.0.0.0:8080` только внутри контейнерной сети; host port у него отсутствует. Caddy передаёт исходный Host и Origin в `app:8080`. Приложение проверяет публичный origin из `WORKSHOP_PUBLIC_ORIGIN=https://${WORKSHOP_DOMAIN}` и не использует forwarded headers. Изменение домена требует обновить `.env` и повторить `docker compose up -d`. Контейнеры перезапускаются после перезагрузки сервера; сертификаты и конфигурация Caddy хранятся в named volumes. [Официальный Compose-пример Caddy](https://caddyserver.com/docs/running#docker-compose).

Для обновления замените содержимое `app/` новым серверным выпуском, выполните `docker compose build --pull`, затем `docker compose up -d`. Это также получает актуальные патчи базового ASP.NET image. Свой `.env` оставьте на сервере. `docker compose down` останавливает сервисы и сохраняет volumes; добавление `--volumes` удаляет и состояние Caddy.

Если собираете серверный архив из исходников, установите .NET SDK 10 и из корня Unity-проекта выполните:

```sh
dotnet publish Tools/TrackWorkshop/TrackWorkshop.csproj -c Release -p:WorkshopTargetFramework=net10.0 -p:UseAppHost=false --self-contained false -o ./track-workshop-server/app
```

Скопируйте файлы `Tools/TrackWorkshop/Deploy/` в корень `track-workshop-server/` рядом с `app/`. Параметр `WorkshopTargetFramework` действует только для этой команды; локальный запуск по умолчанию остаётся на .NET 8.

Файлы карт открываются и скачиваются через браузер: личной серверной библиотеки и автоматического сохранения чужих правок в 0.4 нет. Один общий логин подходит для небольшой команды разработчиков; индивидуальные аккаунты и права в этот пакет не входят. Проверка YAML не подтверждает доступность DNS, выдачу сертификата или работу удалённого сервера — это проверяется после развёртывания.
