# Архитектурное ревью сервиса

Дата: 7 ноября 2026 года. Проверенная revision: `4a56727310016bd447f85172355b31df3af75c10`.

Область по запросу пользователя — весь сервис: production-код, публичный контракт, composition root, зависимости, конфигурация, sandbox/bootstrap, Docker и локальная логика поставки. Проверка не ограничивалась последним коммитом. Рабочее дерево перед ревью было чистым; production-код, тесты и архитектурные правила в ходе ревью не изменялись.

Результат: два подтверждённых несоответствия — одно высокой и одно средней важности. Исправления не выполнялись. Успех существующего набора тестов не закрывает эти замечания.

Последующая работа после проверенной revision: [исправления и повторное ревью AR-01/AR-02](architecture-review-fixes.md). Далее сохранены исходные наблюдения на 4a56727.

## AR-01 — Workspace может попасть в разрешённый системный mount

Важность: **высокая**. Основание: REQ-19, также REQ-18 и OPS-01; `ARCHITECTURE.md`, разделы 5 и 8. Sandbox не должен видеть рабочую область агента; исполнению теста передаются только необходимые файлы.

**Где:** `src/Texnokaktus.ProgOlymp.RunnerAgent/Workspace/RunnerOptions.cs:9` и `src/Texnokaktus.ProgOlymp.RunnerAgent/Execution/IsolateSandbox.cs:211`.

`RunnerOptions.Validate` принимает существующий доступный абсолютный каталог, отличный от корня файловой системы. При этом isolate целиком монтирует `/usr`, `/bin`, `/lib` и `/lib64`. Между настраиваемым workspace и этими mounts нет проверки пересечения. Например, `/usr/local` проходит валидацию, а созданный внутри него `runner-agent-jobs` становится доступен решению через `/usr`. Read-only mount позволяет читать входы других тестов и файлы агента.

**Воспроизведение:** production-образ запущен с `Runner__WorkspaceRoot=/usr/local`, обычными тестовыми credentials и отдельным MinIO. В RunJob переданы два теста: вход №1 — `current-test`, вход №2 — `OTHER_TEST_SENTINEL`. Программа первого теста вместо stdin читает другой вход:

```cpp
for (auto const& entry : std::filesystem::directory_iterator("/usr/local/runner-agent-jobs")) {
    if (entry.is_directory()) {
        std::ifstream file(entry.path() / "input-2");
        std::cout << file.rdbuf();
    }
}
```

Ответ имеет общий статус `Completed`; stdout теста №1 содержит `OTHER_TEST_SENTINEL`. Значит, отдельные boxes не обеспечивают обещанную файловую границу при этой разрешённой конфигурации. Настройка по умолчанию `/var/lib/runner-agent` в данном эксперименте не проверялась на такое пересечение; утверждение об утечке credentials не делается.

**Минимальное исправление:** до открытия API отклонять workspace, пересекающийся с доступными sandbox системными деревьями, либо исключить его из mounts. Проверять фактические пути с учётом symlink в родительских каталогах. Для проверки исправления повторить эксперимент с `/usr/local` и с родительским symlink: старт должен безопасно отказать либо sandbox не должен прочитать sentinel.

Локальные доказательства: `artifacts/architecture-review/workspace-boundary-probe.txt`, `workspace-agent.log`, `run-workspace-probe.ps1`, `probe/Program.cs`. Повтор: `pwsh -File artifacts/architecture-review/run-workspace-probe.ps1 -SkipBuild` из корня репозитория. Скрипт создаёт и удаляет собственные тестовые контейнеры и сеть.

## AR-02 — Retry policy повторяет ошибки вне согласованного перечня

Важность: **средняя**. Основание: REQ-17, AT-19, AT-63; `ARCHITECTURE.md`, раздел 4. Кроме обрывов соединения и сетевых таймаутов, разрешены HTTP 429/500/502/503/504 и S3 SlowDown; прочие ошибки не повторяются.

**Где:** `src/Texnokaktus.ProgOlymp.RunnerAgent/Storage/S3ObjectStorage.cs:211`.

`IsTransient` принимает любой HTTP-код от 500, дополнительные S3-коды и практически любое клиентское исключение из catch-фильтра. Это шире REQ-17. Проверка выполнена через production adapter и AWSSDK.S3 4.0.104.8 с локальным HTTP backend; ответ содержал XML error с кодом `Failure`.

| HTTP-ответ | Фактические запросы | По REQ-17 |
| --- | --- | --- |
| 501 | 3 | 1 |
| 505 | 3 | 1 |
| 503, контроль | 3 | 3 |
| 403, контроль | 1 | 1 |

**Последствие:** неповторяемый отказ создаёт два дополнительных обращения и минимум 700 мс задержки на каждой операции, удерживая единственный слот. При большом наборе и сбоях публикации задержки накапливаются. Тесты проверяют 403/404/503 и не обнаруживают расширение перечня.

**Минимальное исправление:** использовать точный allowlist HTTP/S3-ошибок и отдельно распознавать обрыв соединения и сетевой timeout. Не считать произвольный `AmazonClientException` временным без проверки причины. Проверка исправления — backend с 501/505, постоянной клиентской ошибкой и контрольными временными отказами; постоянные ошибки должны давать одну попытку, временные — максимум три с задержками 200/500 мс.

Локальные доказательства: `artifacts/architecture-review/storage-retry-probe.txt` и `probe/Program.cs`. Повтор: `dotnet run --project artifacts/architecture-review/probe/ReviewProbe.csproj -c Release --no-launch-profile`. Exit 0 пробников означает завершённый эксперимент, а соответствие требованиям оценивается по наблюдениям выше.

## Проверенные границы и зависимости

Основания ревью — `AGENTS.md`, `ARCHITECTURE.md`, активные OpenSpec requirements/design/repository и фактические csproj/props. Отдельного project-rules или discovery-отчёта не найдено; предпочтения AEP не использовались как дополнительные требования.

- Transport преобразует wire-модели в локальные запросы; admission проверяет stopping/busy перед валидацией. Единственный unary RunJob, nullable метрики, исходные ID и самостоятельные compilation/execution limits согласованы с контрактом.
- `Program` явно собирает singleton-компоненты и adapters. Зависимости от AWSSDK/gRPC в одном production assembly допустимы: REQ-26 задаёт логические границы. Локальный `ISubmissionRunner`/`IsolateSubmissionRunner` не обращается к gRPC/S3; дополнительное дробление проектов не требуется.
- Orchestration связывает storage/workspace/runner/publication. Прослежены распространение отмены, независимая cleanup, удержание слота, сохранение известных результатов, запрет reuse после небезопасной очистки и graceful drain.
- Storage разделяет bucket по ролям, проверяет SHA-256 до sandbox, не использует S3 keys как локальные пути и независимо публикует потоки. Credentials фиксируются из env без fallback, startup не требует сетевой доступности MinIO. Отклонение retry policy описано в AR-02.
- Execution использует отдельные boxes, очищенное окружение, cgroup CPU/memory/pids, независимые потоки, классификацию подтверждённых нарушений и отдельную инфраструктурную диагностику. Пересечение workspace с mounts описано в AR-01.
- Проверены регистрации ActivitySource/Meter, отдельная трасса задания, отсутствие ID в метках метрик, health/lifetime, ownership/recovery, разделение базового и языкового образов. Workflow и release scripts прочитаны; реальная внешняя поставка не выполнялась.

## Ревизия и артефакты

Входная revision developer handoff — `2ee1199a261408988f33b578fdcdda611b13a082`, выходная совпадает с проверенным HEAD. Сохранённый разработчиком `artifacts/mvp.patch` и заново полученный `git diff --binary HEAD^ HEAD` имеют одинаковый SHA-256: `07c15fb426181af4368b622b8de8d6c0ebea7264339496f532a04e2715e7d538`.

Docker IDs сверены с handoff:

- base: `sha256:da3ea36501a2b6a5a94c61493a696ca975344d0ff296c72a29a82831a1d1dff2`;
- C++23: `sha256:e614921d7a0ce63458fe2d930bcf0796deee139c6a33200720a894ba7dd95379`.

SHA-256 сохранённого published assembly в `artifacts/host` также совпал: `ac95afcbde960e952a133fd80f80708cccb89a93c80b14f961a27751f69b3458`. Образы были собраны до коммита из сохранённого diff; image metadata сама по себе не доказывает Git SHA. Образы не пересобирались и не публиковались в рамках ревью.

## Выполненные проверки

Среда: Windows/SDK 10.0.403, Linux amd64 Docker с cgroups v2 для реального sandbox. Все завершённые команды ниже дали exit 0.

| Команда / проверка | Результат |
| --- | --- |
| `dotnet build Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-restore` | 0 warnings, 0 errors |
| `dotnet test --solution Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-build --filter 'TestCategory!=Sandbox&TestCategory!=MinIO&TestCategory!=EndToEnd' --report-trx --results-directory artifacts/architecture-review/verified-tests` | 98 passed, 0 failed/skipped; 5 мин 35 с для integration-проекта |
| Категория Sandbox через тот же bootstrap и test assembly в отдельном privileged контейнере C++23 | 26 passed, 0 failed/skipped; результаты `artifacts/architecture-review/sandbox-results` |
| `pwsh -File artifacts/architecture-review/run-storage.ps1` | 1 passed, 0 failed/skipped на настоящем MinIO |
| `pwsh -File artifacts/architecture-review/run-e2e.ps1 -SkipBuild` | 13 passed, 0 failed/skipped на production-образе |
| Два диагностических пробника | Воспроизведены AR-01 и AR-02 |

Итого существующих тестов: **138 passed, 0 failed, 0 skipped**. Копии storage/E2E scripts меняют только пути отчётов, чтобы сохранить результаты разработчика. Дополнительные пробники находятся вне production/test source в игнорируемом `artifacts/architecture-review`. Первый обычный прогон прерван во время долгой паузы в stdout; он не учитывается как успешная проверка. Повтор вне sandbox полностью завершён; промежуточный TRX показал, что тесты продолжали выполняться. Причина задержек не установлена.

## Ограничения и передача

Ревью выполнено в отдельной сессии с доступом к коду и отчёту разработчика. Это не black-box QA, не полный security audit и не формальное доказательство отсутствия иных дефектов. Пакетные зависимости оценивались по фактическим версиям/контрактам и поведению, а не по непроверенной копии upstream source.

Не выполнялись реальные GitHub Actions/GHCR/tag publication, приёмка стандартного GitHub runner и общей storage quota, ручной деплой, повтор AOT publication/runtime suite, полный анализ слоёв на секреты и экспорт в реальный telemetry collector. Получение последнего health Watch transition на остановке отдельно не проверялось. Существующие локальные release/AOT отчёты не выдаются за собственный повтор этих проверок.

Машиночитаемая передача: `artifacts/architecture-review/AR-01.json`, `AR-02.json`, `handoff.json`; результаты связаны с указанным SHA и REQ-ID. Требования и принятые архитектурные решения не пересмотрены. Для закрытия замечаний нужны исправления и повтор воспроизведения; независимая приёмка и внешний выпуск остаются незавершёнными.
