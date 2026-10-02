# Структура репозитория и сборка

Статус: проект структуры от 2 октября 2026 года. Требования пользователя зафиксированы; детализация проектов и CI ниже является предложением для реализации. Физически пока созданы только документы и `AGENTS.md`.

Сводные требования MVP и критерии приёмки находятся в [технической спецификации](SPECIFICATION.md). Она сохраняет статус предложений по составу проектов и CI, описанных ниже.

## Согласованные требования

- README с кратким описанием функционала, API, сборки, тестирования и деплоя.
- Solution в формате `.slnx`.
- GitHub Actions для сборки, тестирования, Docker packaging и публикации в GHCR.
- Общие настройки сборки в `Directory.Build.props`, версии NuGet-пакетов в CPM.
- Production-код в `src/`, тестовые проекты в `tests/`.
- Настройки для групп проектов и при необходимости отдельных проектов во вложенных `Directory.Build.props`, включая общие тестовые зависимости и `InternalsVisibleTo`.
- Unit-тестовый проект имеет суффикс `.Tests`; тесты используют NUnit на Microsoft.Testing.Platform.
- Доступ `DynamicProxyGenAssembly2` объявляется в `src/Directory.Build.props` для всех production-проектов.
- Версия образа — `YYYY.M.D.<run_number>`; для выбора образа при деплое достаточно тега версии.
- Автоматический деплой не планируется. Pipeline заканчивается публикацией в GHCR.

## Предлагаемое дерево

```text
/
├── README.md
├── AGENTS.md
├── ARCHITECTURE.md
├── docs/
│   ├── REPOSITORY.md
│   └── SPECIFICATION.md
├── Texnokaktus.ProgOlymp.RunnerAgent.slnx
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── .editorconfig
├── .gitignore
├── .dockerignore
├── .config/
│   └── dotnet-tools.json
├── .github/
│   └── workflows/
│       └── dotnet.yml
├── src/
│   ├── Directory.Build.props
│   └── Texnokaktus.ProgOlymp.RunnerAgent/
│       ├── Texnokaktus.ProgOlymp.RunnerAgent.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Dockerfile
│       ├── Protos/
│       ├── Grpc/
│       ├── Storage/
│       ├── Execution/
│       ├── Languages/
│       └── Workspace/
└── tests/
    ├── Directory.Build.props
    ├── Texnokaktus.ProgOlymp.RunnerAgent.Tests/
    │   └── Texnokaktus.ProgOlymp.RunnerAgent.Tests.csproj
    └── Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests/
        ├── Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.csproj
        └── Fixtures/
```

В дерево включены будущие файлы. Не создаём пустые `.csproj`, фиктивные тесты или workflow с заведомо несуществующими входами на стадии проектирования.

Для MVP предлагается один production-проект: Grpc/Storage/Execution/Languages/Workspace остаются каталогами и логическими границами внутри него. Выделение библиотек возможно при появлении самостоятельных потребителей или реальной необходимости ограничить зависимости. `.slnx` включает production- и тестовые проекты с solution folders `src` и `tests`.

Проект `.Tests` проверяет локальную логику без Docker/S3/isolate. `.IntegrationTests` проверяет реальные границы: gRPC, storage, isolate, разделение compilation/execution, лимиты и очистку. Оба проекта используют NUnit на Microsoft.Testing.Platform. Интеграционные fixtures используют временные данные и тестовые credentials; для sandbox сохраняется запрет на сеть и credentials.

В тестах жизненного цикла нужно проверить одну компиляцию на набор из N тестов, асинхронное подтверждение принятия задания, сохранение занятости до выдачи готового результата через `GetResult` и очистку песочниц до освобождения ресурса. `GetResult` освобождает ресурс и сразу удаляет сохранённый результат из состояния агента без удаления артефактов S3; запрос ещё не готового результата или другого задания не должен освобождать ресурс. Для долгоживущего агента проверяется отказ заданию с другим `job_id` во время выполнения и ожидания выдачи результата. Повтор текущего `job_id` должен получать «задание уже принято» без повторного исполнения. Ветка контейнера на одно задание пока остаётся альтернативой; проверки завершения агента и удаления контейнера определяются после выбора её lifecycle.

Проверки набора тестов должны подтверждать последовательный запуск в порядке запроса, продолжение после runtime error и превышения лимита, наличие результатов всех тестов и независимое применение общего профиля execution limits из задания к каждому запуску. Лимиты компиляции проверяются отдельно. Для `GetResult` проверяется немедленный ответ «не завершено» во время обработки без ожидания окончания задания и без освобождения ресурса. Проверка корректности ответов и определение AC/WA относятся к мастеру; агент тестируется на правильную передачу статусов исполнения и выходных артефактов.

Проверки контракта также подтверждают возврат заданных мастером `job_id`/`test_id` типа `int64` без потери значений, чтение исходного кода из отдельного bucket и использование общего bucket для входных данных тестов и выходных артефактов. Оба bucket задаются конфигурацией, ключи выходных объектов формируются внутри префикса от мастера. Проверяется SHA-256 входных файлов без требования версий объектов S3. Операция отмены задания в текущий API не входит; остановка самого агента рассматривается отдельно.

Профиль C++23, включая команду и флаги компиляции, определяется агентом; тесты проверяют выбор этого профиля и применение resource limits из задания. gRPC-проверки MVP выполняются по HTTP/2 без TLS и без аутентификации, как и предусмотрено для доверенной внутренней сети. Интеграционные проверки storage используют MinIO и production-реализацию `IObjectStorage` на `AWSSDK.S3`.

Штатная остановка проверяется отдельно: агент отклоняет новые задания, завершает текущий набор, публикует артефакты и обслуживает `GetResult` до выхода процесса. Проверяются настраиваемый таймаут ожидания готового результата (по умолчанию 30 секунд), отсчёт после завершения задания и очистки либо с запроса остановки при уже готовом результате, выход после выдачи результата и выход с потерей непрочитанного результата по таймауту без удаления S3-артефактов. Время выполнения задания не расходует этот таймаут. Незанятый агент завершается сразу.

Сценарии сбоев включают инфраструктурную ошибку одного теста с отдельным статусом и продолжением набора, остановку процесса с `OutputLimitExceeded` при превышении отдельного лимита stdout или stderr и сохранение каждого потока в пределах его лимита, а также отсутствие восстановления текущего задания и непрочитанного результата после перезапуска агента. Лимиты потоков проверяются независимо, включая случай, когда их сумма превышает каждый отдельный лимит, но сами потоки своих лимитов не превышают. Для MVP состояние хранится в памяти; опубликованные S3-артефакты от него не зависят.

В план тестирования также входят согласованные сценарии:

- Ошибка компиляции возвращает `CompilationError`, данные компиляции и журнал компиляции; коллекция результатов тестов пуста, исполнение тестов не начинается.
- Resilience policy MinIO повторяет временные ошибки скачивания/загрузки с задержкой, не превышая трёх попыток всего с учётом настроек SDK. Проверяются успешное восстановление, исчерпание попыток и отсутствие повторов при отсутствии объекта или несовпадении SHA-256.
- Во время обычной работы долгоживущего агента непрочитанный результат сохраняет ресурс занятым без ограничения времени. Таймаут штатной остановки не применяется до начала завершения агента.

## Общие настройки сборки

| Файл | Ответственность |
| --- | --- |
| `global.json` | Версия .NET SDK 10, политика `rollForward` и `test.runner=Microsoft.Testing.Platform`; единые настройки для локальной разработки и CI |
| `Directory.Build.props` | `TargetFramework=net10.0`, `LangVersion=14.0`, `Nullable=enable`, `ImplicitUsings=enable`, deterministic build и настройки анализаторов |
| `Directory.Packages.props` | `ManagePackageVersionsCentrally=true` и версии через `PackageVersion` |
| `src/Directory.Build.props` | Импорт корневых настроек, общие production-настройки и `InternalsVisibleTo` для тестовых assemblies и `DynamicProxyGenAssembly2` |
| `tests/Directory.Build.props` | Импорт корневых настроек, `IsTestProject=true`, `IsPackable=false`, `OutputType=Exe`, `EnableNUnitRunner=true`, общие test packages |
| `.config/dotnet-tools.json` | Зафиксированная версия ReportGenerator; установка через `dotnet tool restore` |
| `.csproj` | Тип SDK проекта, его собственные references, protobuf generation и специфические свойства |

Версия установленного .NET SDK выбирается до вычисления `Directory.Build.props`, поэтому она фиксируется в `global.json`. Атрибут `Sdk` (`Microsoft.NET.Sdk.Web` для gRPC host, `Microsoft.NET.Sdk` для тестов) остаётся в `.csproj`. `TargetFramework` и версия C# задаются отдельно от версии SDK. Конкретную patch-версию SDK выбираем при создании рабочего каркаса; `latest`/preview для C# не используем.

MSBuild автоматически находит только ближайший `Directory.Build.props`. Поэтому файлы в `src/` и `tests/` начинают с явного импорта:

```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)../Directory.Build.props" />
  <!-- Настройки этой группы проектов. -->
</Project>
```

Если отдельному проекту понадобится собственный `Directory.Build.props`, он аналогично импортирует родительский файл своей группы. Для единичного свойства достаточно `.csproj`; отдельный props нужен при полезной группировке настроек. Поздно вычисляемые свойства и build targets при необходимости размещаются в `Directory.Build.targets`.

`PublishAot` не включается глобально: разработка и обычные тесты используют JIT. Проверка AOT выполняется отдельно для production entry point на целевой Linux-платформе. Настройки CI, например `ContinuousIntegrationBuild`, применяются условно, без изменения обычного локального режима.

## CPM, тестовые пакеты и InternalsVisibleTo

Версии всех NuGet-зависимостей хранятся в корневом `Directory.Packages.props`. `PackageReference` в проектах и общих props не содержит `Version`. Дополнительные CPM-файлы в `src/` и `tests/` изначально не нужны.

Для доступа к MinIO production-проект использует `AWSSDK.S3`; версия пакета фиксируется в CPM. Совместимость выбранной версии с целевой Native AOT публикацией проверяется отдельно.

Тестовая платформа — Microsoft.Testing.Platform (MTP), framework — NUnit. Используем нативный MTP-режим `dotnet test` из .NET 10. В `global.json` наряду с настройками SDK добавляется секция:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

В `tests/Directory.Build.props` задаются `EnableNUnitRunner=true` и `OutputType=Exe`. NUnit runner поставляется через `NUnit3TestAdapter`; выбираем совместимую версию с поддержкой MTP 1.7 или новее. Свойство `TestingPlatformDotnetTestSupport` для нативного режима .NET 10 не требуется. Основание: [NUnit и MTP](https://docs.nunit.org/articles/vs-test-adapter/NUnit-And-Microsoft-Test-Platform.html), [MTP-режим dotnet test](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test#mtp-mode-of-dotnet-test).

Общие зависимости тестов: `NUnit`, `NUnit3TestAdapter`, `NSubstitute`, `Microsoft.Testing.Extensions.CodeCoverage` и `Microsoft.Testing.Extensions.TrxReport`.

Пример формы общего объявления, без пока не выбранных версий:

```xml
<ItemGroup>
  <PackageReference Include="NUnit" PrivateAssets="all" />
  <PackageReference Include="NUnit3TestAdapter" PrivateAssets="all" />
  <PackageReference Include="NSubstitute" PrivateAssets="all" />
  <PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" PrivateAssets="all" />
  <PackageReference Include="Microsoft.Testing.Extensions.TrxReport" PrivateAssets="all" />
</ItemGroup>
```

Для конкретного проекта команда использует `dotnet test --project <path>`, для всего solution — `dotnet test --solution <path>`. Покрытие в Cobertura включается через `--coverage --coverage-output-format cobertura`, TRX — через `--report-trx`. В CI оба тестовых проекта запускаются отдельно на соответствующих этапах; ReportGenerator обрабатывает полученные Cobertura-файлы. См. [покрытие MTP](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage) и [отчёты MTP](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports).

Тестовые зависимости не попадают в корневые или production props. Пакеты, нужные только интеграционным fixtures, подключаются в IntegrationTests, с версиями в общем CPM. Конкретные версии фиксируются при создании проектов и проверяются совместным restore/build/test.

`InternalsVisibleTo` задаётся на стороне production assembly. Для предложенных имён проектов в `src/Directory.Build.props` подходит:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="$(MSBuildProjectName).Tests" />
</ItemGroup>
```

Здесь предполагается, что `AssemblyName` совпадает с именем проекта. IntegrationTests по умолчанию проверяет публичные границы. Доступ для прокси NSubstitute также объявляется в `src/Directory.Build.props` и наследуется всеми production-проектами:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
</ItemGroup>
```

## Docker

Dockerfile располагается рядом с production `.csproj`, а build context — корень репозитория. Это даёт сборке доступ к `global.json`, общим props и CPM. Файлы, влияющие на restore, копируются до исходного кода для эффективного кэширования. `.dockerignore` исключает `.git`, локальные secrets, `bin/obj`, TestResults и coverage reports, сохраняя необходимые build inputs.

Планируется multi-stage build: SDK-стадия публикует агента, финальная Linux-стадия содержит agent + isolate + g++/runtime libraries для выбранного C++23. Конкретная версия g++ ещё выбирается. Для JIT MVP ей также нужен .NET runtime; целевой AOT-образ собирается после проверки совместимости. Типовой минимальный образ для обычного .NET web service здесь недостаточен без isolate и toolchain.

Секреты NuGet при реальной необходимости передаются через BuildKit secret mounts. Ни токены registry, ни S3 credentials не записываются в Dockerfile, build args или слои образа. Runtime credentials задаются при развёртывании агента.

Жизненный цикл контейнера ещё выбирается: долгоживущий агент либо контейнер на один набор тестов. Во второй ветке агент завершает работу после выдачи результата мастеру или истечения таймаута ожидания, а контейнер останавливается и удаляется; владельца управления контейнером нужно определить отдельно. При штатной остановке агент заканчивает принятое задание и ожидает `GetResult` с настраиваемым таймаутом, по умолчанию 30 секунд. Настройки остановки приложения/контейнера должны учитывать время завершения задания и последующее ожидание результата; конкретные настройки уточняются при реализации. Это runtime orchestration, не автоматический деплой через CI.

## GitHub Actions

Путь workflow: `.github/workflows/dotnet.yml`. Обязательный результат — сборка, тесты, Docker image и публикация в GHCR. Следующие детали предлагаются для первого рабочего pipeline.

### Выводы из изученного pipeline

Изученный workflow запускается на push в `master`. Job сборки устанавливает .NET 10, выполняет restore/build/test, собирает Cobertura coverage и публикует Markdown summary. Зависимый Docker job формирует версию из даты коммита в часовом поясе Europe/Moscow и номера workflow run, собирает образы для amd64/arm64, отправляет их в GHCR и создаёт Git-тег. Также предусмотрены submodules и приватный NuGet source.

Для RunnerAgent полезны разделение проверки и публикации, отчёт покрытия, Buildx, OCI metadata и связь версии образа с исходным коммитом. Особенности, которые требуют адаптации:

- Добавить проверки pull request; публикация доступна только для доверенного push в основную ветку.
- Использовать SDK из `global.json` и одну конфигурацию `Release` на этапах restore/build/test/publish.
- Зафиксировать ReportGenerator как local tool и сохранять TRX/coverage artifacts, включая результаты неуспешных тестовых запусков, когда файлы созданы.
- Начать с `linux/amd64`; arm64 и QEMU добавлять после проверки isolate, toolchain и тестов на этой архитектуре. Это предложение, а не уже согласованное ограничение продукта.
- Submodules и приватный NuGet source подключать только при появлении соответствующих зависимостей.
- `contents: write` нужен для Git-тега, но не для отправки образа в GHCR. Для публикации достаточно `contents: read` и `packages: write`.
- В summary публикации возвращать полное имя образа с тегом версии для ручного деплоя.
- Не считать успешную сборку образа доказательством работоспособности isolate: перед публикацией проверить реальный запуск C++ в этом образе.

### Предлагаемые этапы

1. **Build/test.** Checkout, setup SDK и MTP-режима по `global.json`, tool restore, dependency restore, Release build solution, запуск `.Tests` через `dotnet test --project` с MTP coverage и TRX. Вывести coverage summary и сохранить результаты. PR выполняется без credentials публикации.
2. **Docker/integration.** После unit tests собрать `linux/amd64` image через Buildx с `push: false`, `load: true`; запустить `.IntegrationTests` через MTP против этого локального образа. Выполнять на одноразовом Linux runner с рабочими privileged Docker/cgroups. Для PR использовать окружение без production secrets; постоянный judge-host для этого не применяется.
3. **Publish.** Только для успешного push в основную ветку загрузить проверенный image из предыдущего этапа, присвоить тег версии, выполнить login в GHCR с `GITHUB_TOKEN` и push. Чтобы выделить публикации отдельные permissions, передавать между jobs Docker image через artifact (`docker save/load`), вместе с версией и revision. Образ после проверки не пересобирать. Вернуть полное имя образа с тегом версии в summary.
4. **AOT compatibility.** При появлении production-проекта отдельная CI-проверка `PublishAot=true` для целевого Linux RID. До её стабилизации AOT остаётся целью; условие обязательности этого check нужно согласовать. Переключение production-образа на AOT требует также интеграционных тестов этого варианта.

PR получает сборку, unit tests и проверку образа, но не push в registry и не Git-тег. Publishing job не запускается при пропущенных или неуспешных обязательных проверках. Для PR допускается отмена устаревших runs; публикация уже прошедшего проверки образа не должна прерываться новым push без выбранной политики.

### Registry, версии и теги

Предлагаемый image: `ghcr.io/texnokaktus-progolym/runner-agent`; owner и image path приводятся к нижнему регистру. Для переносимости owner можно получать из GitHub context. Обозначение `judge-cpp` в архитектуре описывает состав первого образа, а не отдельный согласованный registry package.

Формат тега версии: `YYYY.M.D.<run_number>`, где дата берётся из исходного коммита в Europe/Moscow. Повторный запуск того же workflow сохраняет версию. Revision коммита включается в OCI labels. Версия передаётся в Docker build и metadata агента согласованно; точное отображение в .NET version properties определяется при реализации.

Для ручного деплоя образ выбирается по тегу версии: `image:<version>`.

Отправка в GHCR завершает pipeline упаковки. Автоматический деплой не планируется; развёртывание на judge-host выполняется вручную.

## Что уточнить перед созданием каркаса

- Принять либо скорректировать предложение: один production-проект и два тестовых проекта; суффикс unit-тестового проекта `.Tests` согласован.
- Выбрать SDK patch, версии packages/tools/actions и Linux base image.
- Утвердить основную ветку для публикации и image name.
- Проверить доступность нужной конфигурации isolate/cgroups на выбранном CI runner и определить fixtures интеграционных тестов.
- Выбрать жизненный цикл контейнера и уточнить оставшиеся детали асинхронного протокола; освобождение ресурса и немедленное удаление результата при `GetResult` согласованы. Подробности — в [архитектуре](../ARCHITECTURE.md).
- Согласовать AOT check и момент перехода production image на Native AOT.

После этого создавать реальные `.slnx`, props, проекты, Dockerfile и workflow вместе с проверяемым минимальным приложением. README обновляется одновременно: планируемые команды заменяются проверенными инструкциями.
