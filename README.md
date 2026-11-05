# RunnerAgent

Агент проверяющей системы для компиляции и исполнения олимпиадных решений в isolate. Первый MVP рассчитан на C++23: один исходный файл компилируется один раз, тесты выполняются последовательно в отдельных песочницах с независимыми лимитами.

**Состояние проекта:** реализация MVP начата. Создан независимый .NET 10 каркас: host с platform/OpenTelemetry, проверка конфигурации workspace, NUnit/MTP-тесты, smoke реального Linux-процесса и базовый GitHub Actions. RunJob, MinIO, isolate, production Dockerfile и публикация образа ещё не реализованы. Каркас не принимает задания; его запуск не подтверждает безопасность sandbox.

## Документация

- [Начальное изменение OpenSpec](openspec/changes/implement-runner-agent/README.md) — карта всех контрактов и порядок работы.
- [Proposal](openspec/changes/implement-runner-agent/proposal.md) — цель, объём и 11 областей требований.
- [Спецификации](openspec/changes/implement-runner-agent/specs) — REQ-01–REQ-31 и сценарии приёмки AT-01–AT-79, а также ненумерованные эксплуатационные требования OPS-01/OPS-02.
- [Design](openspec/changes/implement-runner-agent/design.md) — решения, риски, технические задачи Q-ID и последовательность реализации.
- [Tasks](openspec/changes/implement-runner-agent/tasks.md) — открытые задачи реализации и способы проверки готовности.
- [План репозитория и поставки](openspec/changes/implement-runner-agent/repository.md) — .NET/MSBuild/CPM, NUnit/MTP, Docker, CI и планируемые команды ручного деплоя.
- [Архитектура](ARCHITECTURE.md) — согласованные архитектурные решения и их обоснование.

Прежние документы технической спецификации и структуры репозитория заменены артефактами OpenSpec. Начальное изменение остаётся активным: его ADDED-требования перейдут в основные `openspec/specs` после реализации, приёмки и архивирования. Готовность планирования не означает готовность приложения.

## API и исполнение MVP

Планируется долгоживущий .NET 10 JIT агент с одним слотом задания. Единственный unary RPC RunJob остаётся открытым на время прогона, публикации и очистки и возвращает итоговый результат. Все конкурентные вызовы, включая тот же job_id, получают RESOURCE_EXHAUSTED без очереди и дедупликации. Отмена, истечение deadline или обнаруженный разрыв останавливают текущую компиляцию/тест и storage; следующие тесты не запускаются. Агент завершает процессы песочницы и очищает среду независимо от отменённого RPC до допуска следующего задания. Опубликованные S3-объекты остаются; результат отменённого вызова не гарантируется, кэша ответов нет. При штатной остановке текущий вызов заканчивает набор и ответ; дополнительного ожидания чтения нет. Мастер сравнивает артефакты и определяет AC/WA.

Control plane — gRPC HTTP/2 без TLS и аутентификации в доверенной внутренней сети; файлы передаются через MinIO с SHA-256. Компиляция и каждый тест получают отдельный isolate sandbox. Согласованный профиль — `g++ -std=c++23 -O2 -pipe -DONLINE_JUDGE` с динамической линковкой; версия g++ определяется образом. Возможности изоляции и cgroups v2 ещё требуется подтвердить интеграционно.

MVP развёртывается вручную в privileged Linux amd64 Docker-контейнере. Планируемый image — `ghcr.io/texnokaktus-progolymp/runner-agent-cpp-23`, версия — `YYYY.M.D.<run_number>`. CI на push в `master` должен опубликовать тот же образ после обязательных проверок и затем создать Git-тег выпуска. Автоматический деплой не планируется. Более безопасное развёртывание на выделенной Judge VM/host и production AOT остаются направлениями развития; информационная AOT-проверка не блокирует JIT.

## Работа с OpenSpec

```sh
openspec status --change implement-runner-agent
openspec show implement-runner-agent
openspec validate --all --strict --no-interactive
```

Ход реализации отслеживается в [tasks.md](openspec/changes/implement-runner-agent/tasks.md).

## Сборка и проверки каркаса

Нужен .NET SDK 10.0.403 (разрешены последующие patch той же feature band). Версии зафиксированы в `global.json`, CPM и tool manifest. Используются NUnit 4.6.1, adapter 6.3.1, MTP 2.4.1, NSubstitute 6.2.0, CodeCoverage 18.11.2, TrxReport 2.4.1, ReportGenerator 5.5.11 и Platform 2026.9.19.2. Встроенные .NET analyzers работают на уровне `10.0-recommended`, предупреждения считаются ошибками.

`NuGet.Config` направляет `Texnokaktus.ProgOlymp.*` в GitHub Packages, остальные зависимости — в nuget.org; аудит выполняется через nuget.org. Для чистого cache нужен доступ на чтение platform/OpenTelemetry в GitHub Packages. Credentials не входят в репозиторий: настройте `NuGetPackageSourceCredentials_github` в окружении в стандартном формате NuGet `Username=<login>;Password=<token>;ValidAuthenticationTypes=Basic`. CI использует встроенный `GITHUB_TOKEN` с `packages: read`; репозиторию необходимо предоставить read-доступ к двум пакетам. Токен не передаётся приложению или smoke.

```sh
dotnet tool restore
dotnet restore Texnokaktus.ProgOlymp.RunnerAgent.slnx
dotnet build Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-restore
dotnet test --solution Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-build --coverage --coverage-output-format cobertura --report-trx --results-directory artifacts/test-results
dotnet reportgenerator '-reports:artifacts/test-results/*.cobertura.xml' '-targetdir:artifacts/coverage' '-reporttypes:Html;MarkdownSummaryGithub'
dotnet publish src/Texnokaktus.ProgOlymp.RunnerAgent -c Release --no-restore -o artifacts/host
```

`.Tests` проверяет локальные правила, а `.IntegrationTests` — настоящий entry point и HTTP pipeline через `WebApplicationFactory`: обязательный SERVICE_NAME, приоритет/фиксацию конфигурации и старт/остановку host без MinIO/isolate. Фабрика находится в `Tools`, lifecycle — в `SetupBase`/`SetupGlobal`; проверки gRPC/storage/isolate будут добавляться по мере реализации. Всего сейчас 33 теста, без пропусков.

## Запуск каркаса

`SERVICE_NAME` обязателен и непустой. `Runner__WorkspaceRoot` переопределяет `Runner:WorkspaceRoot` из `appsettings.json`; по умолчанию `/var/lib/runner-agent`. Подготовьте существующий доступный на запись каталог; корень файловой системы не допускается. При старте создаётся и сразу удаляется уникальный probe-файл; чужие файлы и каталоги не очищаются. Собственные workspace появятся на следующем этапе. JSON читается рядом со сборкой, изменения настроек требуют перезапуска.

Linux:

```sh
mkdir -p /tmp/runner-agent-workspace
SERVICE_NAME=runner-agent-cpp-23 Runner__WorkspaceRoot=/tmp/runner-agent-workspace dotnet artifacts/host/Texnokaktus.ProgOlymp.RunnerAgent.dll
```

PowerShell:

```powershell
New-Item -ItemType Directory -Force artifacts/workspace | Out-Null
$env:SERVICE_NAME = 'runner-agent-cpp-23'
$env:Runner__WorkspaceRoot = (Resolve-Path artifacts/workspace).Path
dotnet run --project src/Texnokaktus.ProgOlymp.RunnerAgent -c Release --no-build
```

Остановка — Ctrl+C, в Linux также SIGTERM. Штатный выход даёт 0. По решению пользователя общий catch в entry point отсутствует: ошибка старта пока остаётся обычным исключением .NET с ненулевым кодом, без нормализации к 1; управляемые коды выхода будут реализованы в задаче 6.5. HTTP/2 endpoint по умолчанию `http://0.0.0.0:8080`; `ASPNETCORE_URLS`/`Urls` позволяют его переопределить. RPC и health ещё отсутствуют.

В Linux выполните smoke реального процесса:

```sh
sh scripts/smoke.sh artifacts/host/Texnokaktus.ProgOlymp.RunnerAgent.dll
```

Smoke проверяет отсутствие/пустой `SERVICE_NAME`, некорректный workspace, успешный запуск и SIGTERM с кодом 0, сохранность постороннего файла и отсутствие оставшихся probes. Он не требует privileged Docker, MinIO или isolate. Проверка прошла также в `mcr.microsoft.com/dotnet/aspnet:10.0.14` на linux/amd64.

Базовый CI запускается на push `master`, собирает Release, выполняет тесты и smoke, сохраняет доступные отчёты даже при неуспехе. Отдельный AOT job информационный; production остаётся JIT. Запуск реального GitHub workflow и linux-x64 AOT пока не проверены: origin остаётся Gitea, перенос выполняет пользователь. Публикация образа ещё не подключена.

Для следующего этапа нужны cgroups v2, `memory.peak`, контроллеры CPU/memory/pids и управление swap/OOM. После обновления WSL 5 ноября Docker Desktop использует cgroups v2 и ядро 6.18.54.1. В privileged-контейнере доступны cpu/memory/pids, memory.peak, memory.swap.max, memory.oom.group и pids.events. Это проверка базовых возможностей; полноценная приёмка изоляции и лимитов ещё предстоит. Упрощённый режим с cgroups v1 не планируется.
