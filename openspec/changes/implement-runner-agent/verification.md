# Проверка реализации MVP

Проверка разработчиком 7 ноября 2026 года. Это не независимое архитектурное ревью или black-box QA. Изменение остаётся активным; внешний выпуск текущей версии по ранее принятому решению пользователя не запускается в этой пачке.

## Область

Входная revision: `2ee1199`. Добавлены публичный gRPC-контракт, S3 SDK adapter, orchestration, атомарный слот, отмена/дренирование/health, crash recovery, telemetry и workflow поставки. Сохранены один production-проект, локальный runner без gRPC/S3 зависимостей, фиксированный профиль производного образа и ONLINE_JUDGE. Код отформатирован Rider MCP.

Команды и параметры запуска приведены в корневом README. Полные TRX, coverage, AOT log и image IDs сохраняются в локальном `artifacts/`; этот каталог не входит в Git. Точный digest итогового diff и артефакта фиксируется в локальном handoff после проверок.

## Сопоставление приёмочных сценариев

Итоговые локальные прогоны: Release build — exit 0, warnings/errors 0; обычный NUnit/MTP — 98/98; Sandbox — 26/26; MinIO — 1/1; E2E — 13/13. Всего 138 выполненных тестов, failures/skipped 0. TRX: artifacts/verified-tests, artifacts/sandbox-results, artifacts/minio-tests, artifacts/e2e-tests. Cobertura/HTML/Markdown сформированы в artifacts/verified-tests и artifacts/verified-coverage; покрытие обычного набора 56.2%, native Docker-прогоны в него не включены.

Финальная база: `sha256:da3ea36501a2b6a5a94c61493a696ca975344d0ff296c72a29a82831a1d1dff2`. C++23: `sha256:e614921d7a0ce63458fe2d930bcf0796deee139c6a33200720a894ba7dd95379`. docker save/load восстановил эти же IDs. gzip archive SHA-256: `a73649131faa09f4a9f1b3ec6fddbda920d81d253682e87aee00053c9b384319`.

`sh tests/prototype/check-release.sh` на Linux завершился с exit 0: дата коммита в Москве, идемпотентный тег, конфликт другого коммита до registry mutations, отсутствие тега при сбое image push и сохранение образов при сбое tag push. Rider reformat выполнен, YAML проверен Rider get_file_problems без errors. OpenSpec strict validation и git diff --check успешны.

Workflow дополнительно проверен actionlint 1.7.7 в одноразовом Linux Go-контейнере: exit 0. Это проверка YAML/expressions, она не подтверждает доступность GitHub runner или прав GHCR.

Точный deadline status проверен через настоящий TCP/Kestrel, включая DeadlineExceeded и дальнейший reuse. TestServer используется для pipeline/pre-cancel: его in-memory abort/response не считается доказательством сетевой семантики. E2E readiness после рестарта MinIO перечитывает опубликованный Docker-порт и ожидает готовности с учётом временного HTTP EOF.

Таблица показывает источники доказательств и ограничения; наличие теста не означает независимую приёмку всех формулировок сценария.

| Сценарии | Проверки |
| --- | --- |
| AT-01, AT-02, AT-05, AT-06, AT-20, AT-40 | EndToEndTests: настоящая компиляция, порядок 8/2/5, S3 output hashes, optional metrics, повтор ID, workspace отсутствует после ответа; RunJobTests: публичный pipeline |
| AT-03, AT-04, AT-07, AT-30, AT-34–AT-36 | RunJobTests: busy до валидации, отмена отклонённого вызова не затрагивает первый, удержание слота во время независимой cleanup, pre-cancel; E2E: busy во время реального процесса и reuse |
| AT-08–AT-10, AT-21, AT-42–AT-46, AT-48, AT-79 | Ошибка компиляции, source/input hash mismatch, per-test runtime error, ошибки подготовки/публикации, сохранение других ссылок и известных результатов; фатальная cleanup первого/последнего теста даёт неполный InfrastructureError/полный Completed |
| AT-11–AT-16, AT-52–AT-62 | SandboxExecutionTests/EnvironmentTests и локальные tests: CPU/wall, group CPU/OOM/pids, оба compiler streams, независимые stdout/stderr, namespace/files/environment/fd/network, readback метрик, потеря metadata, приоритет нарушений, числовые границы |
| AT-17–AT-19, AT-47, AT-49, AT-63–AT-66 | Production AWSSDK через HTTP fault backend: 403/404 без повторов, 503 ровно три попытки и задержки, оборванный download заново без Range, hash mismatch, стабильные upload bytes/hash и отмена; настоящий MinIO без server region, path-style и два bucket; credentials snapshot/no fallback |
| AT-22–AT-25, AT-29, AT-50–AT-51, AT-77–AT-78 | E2E: SIGTERM с активным заданием, отмена при остановке, exit 0; SIGKILL/exit 137 и восстановление только собственных остатков; fatal cleanup/exit 1, включая отмену; smoke: idle exit 0 и startup exit 1 |
| AT-26, AT-32 | Инспекция контракта и зависимостей: единственный unary RPC, нет отдельного чтения/отмены; local runner работает через локальные контракты. Независимое архитектурное ревью не выполнено |
| AT-27, AT-74–AT-76 | Локально build/JIT image/tests, docker save/load с сохранением IDs, размер архива и release shell fixtures: дата коммита Europe/Moscow, повтор, чужой тег, ошибка image push и tag push. GitHub runner, реальные GHCR права/публикация и конкурирующие workflow ещё не проверены |
| AT-28 | Base/derived Dockerfile, whitelist build context, отсутствие compiler/profile в базе, отсутствие SDK/NuGet credentials в final image; sandbox environment/proc/fd tests. Полный независимый анализ слоёв не выполнялся |
| AT-31, AT-38 | Отмена подготовки/компиляции/исполнения/публикации, deadline и независимая cleanup проверяются. Физический разрыв клиентского TCP во время реального исполнения и последующий reuse проверены. Отдельный эксперимент с потерей только финального ответа не выполнен; обнаружение разрыва использует стандартный ServerCallContext.CancellationToken |
| AT-33, AT-39, AT-41 | Публичные gRPC tests: INVALID_ARGUMENT без IO для IDs, language/version, hash, missing/overflow limits, keys/prefix; структурированные результаты при gRPC OK |
| AT-37, AT-72–AT-73 | HostConfigurationTests, RunnerOptions/Workspace tests, реальный контейнерный endpoint; env/CLI приоритет, startup snapshot, существующий writable workspace и сохранение чужих файлов |
| AT-67, AT-70–AT-71 | Activity/Meter listeners: отдельный root trace и отсутствие ID labels; generated logs с ID/причиной, static storage diagnostics. Реальный внешний collector/export и все варианты исключений сторонних providers отдельно не принимались |
| AT-68–AT-69 | Health при busy, дренировании и недоступном MinIO; старт без доступного storage, startup/fatal cleanup exit 1 и graceful exit 0. NOT_SERVING реализован по ApplicationStopping; получение последнего статуса Watch в момент остановки отдельно не проверялось |

## Поставка и AOT

Локальная linux-x64 Native AOT публикация production-проекта со всеми зависимостями завершилась с exit 0 в SDK 10.0.403. Есть IL2104/IL3000/IL3002/IL3053 warnings у reflection-зависимостей Serilog/OpenTelemetry. Полный integration-набор AOT-бинарника не выполнялся; production остаётся JIT, informational job не блокирует выпуск.

gzip-архив проверенной пары JIT образов локально занимает 182892299 байт (около 174 МиБ). CI ограничивает архив 200 МиБ, хранит один день и удаляет после успешного выпуска; при неуспехе возможен повтор publish до истечения retention. Отчёты хранятся три дня. Свободный бюджет общей организации неизвестен и должен быть проверен перед первым внешним запуском; неограниченное накопление неудачных выпусков не гарантируется.

Реальный workflow/GHCR, ручной деплой опубликованного тега и независимая приёмка остаются внешними шагами. После их выполнения нужно обновить этот отчёт и только затем решать вопрос архивирования OpenSpec. Для архитектурного review передаются revision/diff и артефакт; для независимого QA — публичные требования, proto, запуск и образ той же версии, отдельно от implementation plan.
