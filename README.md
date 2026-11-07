# RunnerAgent

.NET 10 агент проверяющей системы: получает задание через gRPC, скачивает исходник и входы из S3/MinIO, компилирует C++23 один раз, выполняет тесты в отдельных isolate-песочницах и публикует stdout/stderr обратно в S3.

**Отложенная работа:** заменить MinIO — проект архивирован. Альтернативу и миграцию выбираем отдельно; текущий этап сохраняет MinIO/S3-контракт и доводит CI до pass. Тестовый MinIO собирается из официального GitHub release binary с закреплённой SHA-256, поскольку прежние registry-образы недоступны.

Реализованы RunJob, storage, лимиты, отмена, восстановление собственных остатков, health и штатная остановка. Production использует JIT и Linux amd64. Локальные проверки выполнены в Docker Desktop с cgroups v2. GitHub CI/GHCR подготовлены; внешний выпуск текущих изменений ещё не выполнен.

## Контракт

Wire-контракт: [runner.proto](src/Texnokaktus.ProgOlymp.RunnerAgent/Protos/runner.proto), сервис `texnokaktus.progolymp.runner.v1.Runner`, единственный unary RPC `RunJob`. Endpoint по умолчанию `http://0.0.0.0:8080`, HTTP/2 без TLS/auth в доверенной внутренней сети.

Запрос задаёт положительный `job_id` int64, точные `language=cpp` / `language_version=23`, исходник, непустой список положительных уникальных `test_id` int32, `result_prefix` и отдельные compilation/execution limits. Ссылки содержат непреобразуемый S3 key и SHA-256 из 32 байт. Тесты идут в переданном порядке; поднабор 8/2/5 допустим. Каждый тест получает свежую песочницу.

Все шесть лимитов обязательны и положительны: CPU/wall в миллисекундах, memory/stdout/stderr в байтах, число процессов. CPU/wall должны помещаться в signed int32 milliseconds isolate; память округляется вверх до страницы и должна помещаться в signed int32 KiB isolate. Неприменимые значения дают INVALID_ARGUMENT до IO. Отдельных лимитов размера запроса/исходника/входов и общего таймаута задания нет.

Один слот занят до завершения публикации и очистки. Конкурентные вызовы получают RESOURCE_EXHAUSTED до бизнес-валидации; после начала остановки — UNAVAILABLE. Нет очереди, истории ID или дедупликации. Повтор ID после освобождения исполняет задание заново: мастер должен выдавать новые ID и префиксы, если хочет сохранить предыдущие артефакты.

Ответ с gRPC OK содержит Completed, CompilationError либо InfrastructureError. RuntimeError/превышение лимита программы сохраняется внутри результата исполнения. Completed означает обработанный набор, включая тесты с инфраструктурными ошибками; AC/WA определяет мастер. Неизвестные метрики/exit code отсутствуют, а не подменяются нулями. При ошибке отдельного входа тест не исполняется, остальные продолжаются. Ошибка публикации не стирает известный verdict и успешно опубликованные ссылки.

Отмена/deadline/обнаруженный разрыв прерывают storage и текущую стадию; cleanup идёт независимо от токена RPC, слот удерживается до его окончания. Доставка результата после отмены не гарантируется. SIGTERM запрещает новые задания, позволяет активному завершить публикацию/cleanup/ответ, затем даёт exit 0. Ошибка старта либо небезопасная очистка даёт exit 1 и запрещает reuse. Опубликованные S3-объекты не удаляются.

Стандартный `grpc.health.v1.Health` доступен на том же порту: пустой service либо полное имя Runner. SERVING сохраняется при занятости, недоступном storage и дренировании активного задания; NOT_SERVING устанавливается непосредственно перед остановкой сервера.

## Сборка и проверки

SDK фиксирует `global.json`, NuGet — Central Package Management. Доступ на чтение приватных platform packages задаётся окружением NuGet вне репозитория.

```powershell
dotnet tool restore
dotnet restore Texnokaktus.ProgOlymp.RunnerAgent.slnx
dotnet build Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-restore
dotnet test --solution Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-build --filter 'TestCategory!=Sandbox&TestCategory!=MinIO&TestCategory!=EndToEnd' --coverage --coverage-output-format cobertura --report-trx --results-directory artifacts/test-results
dotnet reportgenerator '-reports:artifacts/test-results/*.cobertura.xml' '-targetdir:artifacts/coverage' '-reporttypes:Html;MarkdownSummaryGithub'
```

Windows подходит для разработки и тестов host с заменой внешних границ. Реальный агент требует Linux amd64, privileged Docker, cgroups v2 с cpu/memory/pids и memory.peak. При несовместимой среде startup отказывает до открытия сервера.

Из PowerShell 7 (`pwsh`) на Windows/Linux:

```powershell
pwsh -File tests/prototype/run.ps1
pwsh -File tests/prototype/run-storage.ps1
pwsh -File tests/prototype/run-e2e.ps1
```

Первый скрипт собирает базу и C++ образ, выполняет smoke и категорию Sandbox. Второй проверяет настоящий MinIO без заданного серверного региона. Третий проверяет production-образ через сеть/gRPC/S3, отмену, SIGTERM/SIGKILL и фатальную очистку. `-SkipBuild` у третьего пропускает сборку production-образов; тестовый MinIO всё равно собирается из tests/prototype/Minio.Dockerfile (обычно из кеша). TRX и логи сохраняются в `artifacts/`; тестовая инфраструктура удаляется в finally. Категории требуют соответствующей среды и не считаются успешно пройденными при пропуске. Проверки release-скриптов: `sh tests/prototype/check-release.sh` на Linux.

## Образы и конфигурация

`docker/base/Dockerfile` содержит Ubuntu 24.04, .NET runtime 10.0.14, isolate v2.7 (`8f185bb37f3f23e29b33b0c7727c91c13429abe3`), агент и bootstrap. `docker/cpp23/Dockerfile` добавляет g++ 13.3.0 и фиксированный LanguageProfile. База без профиля не является готовым worker. Команды/флаги не принимаются от мастера. C++ использует динамическую линковку и `-DONLINE_JUDGE`; обе стадии получают `ONLINE_JUDGE=1`. Для будущих языков сохраняется общий env-маркер и добавляется соответствующий механизм условной компиляции.

```powershell
dotnet publish src/Texnokaktus.ProgOlymp.RunnerAgent -c Release --no-restore -o artifacts/host
docker build -f docker/base/Dockerfile -t runner-agent-base:local .
docker build -f docker/cpp23/Dockerfile -t runner-agent-cpp-23:local .
```

Настройки читаются при старте стандартными ASP.NET Core providers: appsettings, environment-specific JSON, env, command line. Hot reload отключён. JSON находится рядом со сборкой. Значимые env-настройки:

| Настройка | Значение по умолчанию / назначение |
| --- | --- |
| SERVICE_NAME | Обязательное непустое имя сервиса |
| ASPNETCORE_URLS | HTTP/2 endpoint, по умолчанию http://0.0.0.0:8080 |
| Runner__WorkspaceRoot | /var/lib/runner-agent, существующий доступный каталог |
| S3__Endpoint | http://minio:9000, path-style |
| S3__SourceBucket | runner-sources |
| S3__DataBucket | runner-data; должен отличаться от source bucket |
| S3__SigningRegion | us-east-1 |
| AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY | Обязательные credentials только из env |
| AWS_SESSION_TOKEN | Необязательный session token |

Нет fallback к AWS profiles/metadata. Buckets и права подготавливаются снаружи. Startup не обращается к MinIO. Storage делает максимум три попытки с задержками 200/500 мс только для подтверждённых обрывов соединения/сетевых timeout, HTTP 429/500/502/503/504 и S3 SlowDown; встроенные SDK retries отключены. Hash mismatch, 403/404 и локальные IO-ошибки не повторяются. Каждый повтор download начинается заново в `.partial`, в sandbox попадают только проверенные байты. Артефакты: `{prefix}/compilation/{stdout,stderr}.txt` и `{prefix}/tests/{test_id}/{stdout,stderr}.txt` в data bucket.

## Ручной запуск

Подготовьте Docker network с доступным MinIO и защищённый env-файл с SERVICE_NAME/AWS/S3 настройками. Пример использует проверенный локальный образ; после выпуска подставьте GHCR тег версии.

```sh
docker run -d --name runner-agent --privileged --cgroupns=private \
  --network judge -p 127.0.0.1:8080:8080 \
  --env-file /secure/runner-agent.env \
  --mount type=volume,source=runner-agent-workspace,target=/var/lib/runner-agent \
  runner-agent-cpp-23:local
docker kill --signal=TERM runner-agent
docker wait runner-agent
```

Оператор должен дать активному заданию завершиться; обычный короткий timeout `docker stop` может закончиться SIGKILL. Linux workspace сначала канонизируется вместе с родительскими symlink. Пути внутри /usr, /bin, /lib, /lib64 и /var/lib/runner-agent-dev запрещены до probe и открытия API. Привилегированный оператор должен сохранять доверенную mount topology; realpath не обнаруживает bind-mount aliases и последующие изменения mounts. После аварии startup удаляет только собственные помеченные workspace в `runner-agent-jobs`, очищает собственный box/cgroup 0 и проверяет среду пробным процессом. Чужие файлы сохраняются. Блокировки workspace и `/run/isolate/runner-agent.lock` исключают двух владельцев. Privileged Docker принят для MVP; дальнейшее направление — dedicated judge VM/host с native isolate.

## Наблюдаемость и CI

Логи содержат ID, стадии и статусы; содержимое файлов, S3 keys и credentials не логируются приложением. Каждое принятое задание создаёт отдельный trace `runner.job`, стадии — дочерние spans; входящий RPC связывается link. ActivitySource/Meter: `Texnokaktus.ProgOlymp.RunnerAgent`. Метрики: `runner.jobs`, `runner.rejections`, `runner.active_calls`, `runner.stage.duration` (seconds), `runner.storage.errors`, `runner.storage.retries`. Метки ограничены outcome/reason/stage, без job_id/test_id. Экспорт использует общую platform/OpenTelemetry конфигурацию.

Workflow на push master выполняет Release build, тесты и smoke, Docker/MinIO/E2E. Проверенные образы передаются между jobs через gzip Docker archive с проверкой image IDs; publish их не пересобирает. Архив ограничен 200 МиБ, retention 1 день, удаляется из Actions после успешной публикации; отчёты — 3 дня. Наличие общей свободной квоты организации проверяется оператором перед первым CI; локальный архив занимает около 174 МиБ.

Публикация: `ghcr.io/texnokaktus-progolymp/runner-agent-base` и `runner-agent-cpp-23`, версия `YYYY.M.D.run_number` по дате исходного коммита Europe/Moscow. После обоих push создаётся Git-тег `v<version>`; повтор на том же коммите допустим, чужой тег не перезаписывается. Начатый publish не отменяется новым push. Автодеплоя и интеграции с Gitea-зеркалом нет. Отдельный AOT job информационный; успешная JIT-поставка от него не зависит.

## Документация

[Архитектура](ARCHITECTURE.md), [OpenSpec](openspec/changes/implement-runner-agent/README.md), [задачи](openspec/changes/implement-runner-agent/tasks.md), [структура и поставка](openspec/changes/implement-runner-agent/repository.md). Изменение остаётся активным до завершения приёмки; готовность кода не подменяет независимый review и реальный внешний выпуск.
