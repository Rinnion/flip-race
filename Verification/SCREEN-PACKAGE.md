# SCREEN-03: упаковка 4.8.0-trial

21 октября 2026 года. **PASS: независимая статическая проверка финальной сборки r3, ZIP и свежей распаковки.** Запуск распакованной игры в браузере проверяется отдельно владельцем поставки.

- Сборка: `Builds/WebGL-v4.8.0-final`; журнал `Logs/screen-final-r3-build.log` содержит `FLIP_BUILD_OK 8714404 bytes` и завершение Unity с кодом 0.
- Архив: `Releases/flip-race-v4.8.0-trial-webgl.zip`, **8 583 353 байта** (8,186 MiB).
- SHA-256 архива: `c467e454a994f889c579be6ced0ba1c22ca9d524fd71a4138e7a0c37ba696843`.
- Отдельная распаковка: `Builds/WebGL-v4.8.0-unpacked-r3`. Каталог перед проверкой отсутствовал; существующие файлы не удалялись и не перезаписывались.
- Предыдущая распаковка r2 сохранена в `Builds/WebGL-v4.8.0-unpacked`; её отчёты сохранены как `screen-package-verification-r2.json` и `screen-unpacked-manifest-r2.json`. Они не являются текущей поставкой.
- Для всех пяти файлов совпали четыре SHA-256: исходная сборка, манифест, поток записи ZIP и распакованный файл. Совпали также размеры и полный набор путей.
- `index.html` находится в корне; четыре ссылки на ресурсы относительные и существуют. После распаковки повторно проверены gzip fallback и заголовок WebAssembly. `productVersion` равен `4.8.0-trial`.
- Архив 4.7.0 сохранён без изменений: `d38c199eb6e93b8d0177f1f40c34d61319191f880ed181eae877c05f6784147f`.

Команды из корня проекта:

```powershell
./Tools/package.ps1 -BuildDirectory ./Builds/WebGL-v4.8.0-final -Output ./Releases/flip-race-v4.8.0-trial-webgl.zip
./Verification/screen-package-check.ps1
```

Вторая команда требует отсутствующего каталога распаковки и намеренно отказывается перезаписывать существующий. Полные результаты: [screen-package-verification.json](screen-package-verification.json), [screen-unpacked-manifest.json](screen-unpacked-manifest.json), манифест рядом с ZIP. Проверка не запускает сервер, не загружает игру на внешние сервисы и не заменяет браузерный или человеческий тест.
