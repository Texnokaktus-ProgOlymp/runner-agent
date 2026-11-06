# RunnerAgent

Агент проверяющей системы для компиляции и исполнения олимпиадных решений в isolate. Первый MVP рассчитан на C++23: один исходный файл компилируется один раз, тесты выполняются последовательно в отдельных песочницах с независимыми лимитами.

**Состояние проекта:** реализация MVP начата. Создан независимый .NET 10 каркас: host с platform/OpenTelemetry, проверка конфигурации workspace, NUnit/MTP-тесты, smoke реального Linux-процесса и базовый GitHub Actions. Добавлен локальный isolate-runner и тестовый Docker-образ; Базовый и производный C++ Dockerfile созданы; RunJob, MinIO и публикация образов ещё не реализованы. Host пока не принимает задания по API; полная приёмка sandbox не завершена.

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
dotnet test --solution Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-build --filter "TestCategory!=Sandbox" --coverage --coverage-output-format cobertura --report-trx --results-directory artifacts/test-results
dotnet reportgenerator '-reports:artifacts/test-results/*.cobertura.xml' '-targetdir:artifacts/coverage' '-reporttypes:Html;MarkdownSummaryGithub'
dotnet publish src/Texnokaktus.ProgOlymp.RunnerAgent -c Release --no-restore -o artifacts/host
```

`.Tests` проверяет локальные правила, а `.IntegrationTests` — настоящий entry point и HTTP pipeline через `WebApplicationFactory`: обязательный SERVICE_NAME, приоритет/фиксацию конфигурации и старт/остановку host без MinIO/isolate. Фабрика находится в `Tools`, lifecycle — в `SetupBase`/`SetupGlobal`; проверки gRPC/storage/isolate будут добавляться по мере реализации. Всего сейчас 33 теста, без пропусков.

## Запуск каркаса

`SERVICE_NAME` обязателен и непустой. `Runner__WorkspaceRoot` переопределяет `Runner:WorkspaceRoot` из `appsettings.json`; по умолчанию `/var/lib/runner-agent`. Подготовьте существующий доступный на запись каталог; корень файловой системы не допускается. При старте создаётся и сразу удаляется уникальный probe-файл; чужие файлы и каталоги не очищаются. Собственные workspace появятся на следующем этапе. JSON читается рядом со сборкой, изменения настроек требуют перезапуска.

Команды ниже требуют Linux amd64 и предварительно подготовленного sandbox (`tests/prototype/environment.sh` внутри приватного privileged контейнера). Обычный запуск в Windows теперь завершается ошибкой до открытия сервера; Windows используется для разработки и host-тестов с заменой внешней среды.

Linux:

```sh
mkdir -p /tmp/runner-agent-workspace
SERVICE_NAME=runner-agent-cpp-23 Runner__WorkspaceRoot=/tmp/runner-agent-workspace dotnet artifacts/host/Texnokaktus.ProgOlymp.RunnerAgent.dll
```

В PowerShell запускайте Linux-проверки через `powershell -NoProfile -File tests/prototype/run.ps1` с запущенным Docker Desktop.

Остановка — Ctrl+C, в Linux также SIGTERM. Штатный выход даёт 0. По решению пользователя общий catch в entry point отсутствует: ошибка старта пока остаётся обычным исключением .NET с ненулевым кодом, без нормализации к 1; управляемые коды выхода будут реализованы в задаче 6.5. HTTP/2 endpoint по умолчанию `http://0.0.0.0:8080`; `ASPNETCORE_URLS`/`Urls` позволяют его переопределить. RPC и health ещё отсутствуют.

В Linux выполните smoke реального процесса:

```sh
sh scripts/smoke.sh artifacts/host/Texnokaktus.ProgOlymp.RunnerAgent.dll
```

Полный smoke проверяет конфигурацию, startup-пробник isolate до открытия сервера, SIGTERM с кодом 0 и сохранность посторонних файлов. Для него требуется подготовленная privileged Linux amd64 среда; он входит в `tests/prototype/run.ps1`. На обычном Linux CI выполняется `sh scripts/smoke.sh artifacts/host/Texnokaktus.ProgOlymp.RunnerAgent.dll configuration`: ошибки конфигурации и отказ до старта в несовместимой среде.

Базовый CI запускается на push `master`, собирает Release, выполняет тесты и smoke, сохраняет доступные отчёты даже при неуспехе. Отдельный AOT job информационный; production остаётся JIT. Запуск реального GitHub workflow и linux-x64 AOT пока не проверены: origin остаётся Gitea, перенос выполняет пользователь. Публикация образа ещё не подключена.

Для следующего этапа нужны cgroups v2, `memory.peak`, контроллеры CPU/memory/pids и управление swap/OOM. После обновления WSL 5 ноября Docker Desktop использует cgroups v2 и ядро 6.18.54.1. В privileged-контейнере доступны cpu/memory/pids, memory.peak, memory.swap.max, memory.oom.group и pids.events. Это проверка базовых возможностей; полноценная приёмка изоляции и лимитов ещё предстоит. Упрощённый режим с cgroups v1 не планируется.

## Локальный sandbox-прототип

Реализован публичный локальный контракт `ISubmissionRunner.RunAsync(LocalSubmission, CancellationToken)`: workspace, исходник и входы представлены локальными путями, compilation/execution limits разделены. Runner не зависит от gRPC/S3. `WorkspaceManager.Create(jobId)` выдаёт отдельный `JobWorkspace`; вызывающий код владеет им и удаляет после чтения/публикации результатов через `Dispose`.

Компиляция выполняется один раз. Для неё и каждого теста box 0 создаётся заново и очищается; повторное использование номера происходит только после удаления предыдущего box/cgroup. Контейнеры имеют приватные cgroup/PID namespaces и отдельные файловые области. Тесты идут в исходном порядке, включая 8, 2, 5; рабочие файлы и временные каталоги между ними не переносятся.

Прототип использует .NET runtime 10.0.14 / Ubuntu 24.04, isolate 2.7 (commit `8f185bb37f3f23e29b33b0c7727c91c13429abe3`) и g++ 13.3.0 (`13.3.0-6ubuntu2~24.04.1`). Docker build проверяет версию g++. Image собирается из опубликованного host без SDK, NuGet credentials и файлов заданий. Это экспериментальный образ, не production-поставка.

После обычных restore-команд на машине с Linux amd64 Docker и cgroups v2:

```powershell
powershell -NoProfile -File tests/prototype/run.ps1
```

Из PowerShell 7 на Linux используйте `pwsh -File tests/prototype/run.ps1`. Скрипт собирает Release, публикует host, создаёт `runner-agent-prototype:local` и выполняет NUnit-категорию `Sandbox` в одноразовом privileged-контейнере с `--cgroupns=private`. Исходники/build output монтируются только для чтения, TRX сохраняется в `artifacts/sandbox-results`. Shell bootstrap нормализует CRLF внутри контейнера; настройки окончаний строк репозитория не меняются. Тестовый runner запускается из полного build output: `dotnet publish` тестового проекта не переносит все runtime-зависимости MTP.

`environment.sh` предназначен только для такого одноразового контейнера: проверяет приватный cgroup namespace, переносит управляющие процессы в отдельную cgroup, включает cpu/memory/pids для boxes и создаёт `/etc/isolate`. Процессы sandbox работают с UID/GID 60000. `/usr`, `/bin`, `/lib`, `/lib64` доступны только для чтения, `/proc` ограничен isolate; запись разрешена в `/box` и свежий `/tmp`. Вместо полного `/dev` предоставляются только null/zero/random/urandom. Окружение isolate очищается целиком; задаются PATH/LANG, внутри isolate также используется его стандартный `LIBC_FATAL_STDERR_=1`.

Stdout/stderr независимо ограничиваются при чтении из pipe и сохраняются вне доступного sandbox дерева. CPU учитывается всей cgroup, wall time берётся из монотонных часов isolate, peak memory читается из `memory.peak` в байтах до cleanup. Лимит памяти округляется вверх до страницы с проверкой переполнения; результат содержит прочитанный `memory.max`. Native timers принимают положительные миллисекунды до `int.MaxValue`; память после округления должна помещаться в signed int32 KiB. Неприменимые значения отвергаются до запуска.

Для каждой стадии выставляются `memory.oom.group=1` и `pids.max`, isolate отключает swap. Причины выбираются по подтверждённым событиям/метрикам, один сигнал не доказывает превышение. При отмене выполняются cgroup.kill и SIGTERM keeper, чтобы isolate завершил и собрал proxy-процессы; cleanup не использует отменённый токен. Source-generated POSIX interop требует `AllowUnsafeBlocks` только в host-проекте. Ошибка cleanup помечает среду непригодной и запрещает следующие вызовы локального sandbox; автоматическое завершение всего агента будет подключено вместе с orchestration.

Проверены 39 автономных тестов и Linux-сценарии `Sandbox`: одна компиляция/порядок/независимые файлы, compilation error, CPU дочернего процесса, независимый wall time, групповой OOM, процессы/потоки с обработанным отказом, два потока вывода, UID/environment/proc/network isolation и отмена без оставшихся box/cgroup/proxy. Startup-проверка cgroups v2, обязательных интерфейсов, swap/OOM/pids, блокировки агента и пробного isolate-процесса выполняется до открытия сервера. Полная приёмка AT-01–AT-79, gRPC, MinIO, crash recovery и production CI пока не завершены.

При выборе параметров toolchain ориентируемся на [настройки Яндекс Контеста](https://contest.yandex.ru/docs/ru/compilers). Общее правило `ONLINE_JUDGE` и порядок адаптации параметров зафиксированы в [архитектуре](ARCHITECTURE.md#8-внутренние-компоненты).

## Базовый и языковые образы

`docker/base/Dockerfile` содержит Ubuntu/.NET runtime, isolate, агент и общий bootstrap. Компилятор и профиль в базе отсутствуют. `docker/cpp23/Dockerfile` наследуется от проверенной базы, устанавливает g++ и добавляет `appsettings.Production.json` с секцией `LanguageProfile`.

```powershell
dotnet publish src/Texnokaktus.ProgOlymp.RunnerAgent -c Release --no-restore -o artifacts/host
docker build -f docker/base/Dockerfile -t runner-agent-base:local .
docker build -f docker/cpp23/Dockerfile --build-arg BASE_IMAGE=runner-agent-base:local -t runner-agent-cpp-23:local .
docker run --rm --privileged --cgroupns=private -e SERVICE_NAME=runner-agent-cpp-23 -p 8080:8080 runner-agent-cpp-23:local
```

Профиль задаёт язык/версию, имена файлов и команды с массивами аргументов; shell для команд компиляции/исполнения не используется. Общий ConfiguredLanguageProvider сохраняет snapshot профиля при старте. Для другого environment требуется соответствующий `appsettings.<Environment>.json` с профилем либо настройки окружения; стандартный environment образа — Production. База без профиля не запускает worker. Профиль содержит только доверенные настройки образа: credentials остаются вне него, запрос RunJob не может менять команды.

Драйвер `tests/prototype/run.ps1` собирает оба образа, проверяет отсутствие компилятора в базе и выполняет сценарии на производном C++23 образе. Новый язык требует собственного toolchain/runtime, профиля и проверок; готовая поддержка других языков пока не заявляется. CI-публикация обоих образов и полный gRPC/MinIO pipeline ещё не реализованы.
