# Начальный MVP RunnerAgent

Статус: требования согласованы 2 октября 2026 года; документация полностью мигрирована в OpenSpec 5 ноября 2026 года. Приложение, .NET-проекты, Dockerfile и CI пока отсутствуют. Ни один сценарий приёмки не объявлен пройденным.

Это начальное изменение `implement-runner-agent`, описывающее будущую реализацию. Все требования имеют тип `ADDED`; основные `openspec/specs` пока пусты. Изменение остаётся активным до реализации и приёмки, после чего архивирование перенесёт контракты в основные спецификации.

## Артефакты

- [Proposal](proposal.md) — цель и границы изменения.
- [Design](design.md) — решения, риски, техническая детализация Q-ID и исходный порядок реализации.
- [Repository](repository.md) — подробный план .NET/MSBuild/CPM, Docker, CI, тестирования и ручного деплоя; предложения явно отделены от согласованных решений.
- [Tasks](tasks.md) — отслеживаемый список задач будущей реализации с проверкой готовности каждой задачи.
- [Архитектура](../../../ARCHITECTURE.md) — принятые архитектурные решения и их обоснования.

## Карта переноса контрактов

Нумерация исходных разделов сохранена здесь для прослеживаемости. REQ-ID сохранены в именах требований, AT-ID — в именах сценариев. Каждый сценарий AT встречается один раз; его исходные связи с требованиями сохранены. Дополнительные OPS-01/OPS-02 оформляют существовавшие ненумерованные требования конфигурации и эксплуатации, без изменения их смысла.

| Исходный раздел | Спецификация OpenSpec | Требования |
| --- | --- | --- |
| 1. Назначение и границы | [submission-processing](specs/submission-processing/spec.md) | REQ-01–REQ-03 |
| 2. Среда и внешние границы | [agent-deployment](specs/agent-deployment/spec.md) | REQ-04–REQ-06 |
| 3. Задание и gRPC | [job-api](specs/job-api/spec.md) | REQ-07–REQ-10 |
| 4. Состояние и жизненный цикл | [agent-lifecycle](specs/agent-lifecycle/spec.md) | REQ-11–REQ-14 |
| 5. S3 и целостность | [object-storage](specs/object-storage/spec.md) | REQ-15–REQ-17 |
| 6. Sandbox и язык | [sandbox-execution](specs/sandbox-execution/spec.md) | REQ-18–REQ-21 |
| 7. Лимиты | [resource-limits](specs/resource-limits/spec.md) | REQ-22–REQ-23 |
| 8. Результаты и ошибки | [job-results](specs/job-results/spec.md) | REQ-24–REQ-25 |
| 9. Компоненты | [component-boundaries](specs/component-boundaries/spec.md) | REQ-26–REQ-27 |
| 10. Конфигурация и эксплуатация | [agent-operations](specs/agent-operations/spec.md) | REQ-28, OPS-01–OPS-02 |
| 11. Репозиторий и поставка | [build-and-release](specs/build-and-release/spec.md) | REQ-29–REQ-31 |
| 12. Приёмка | Сценарии AT-01–AT-76 в соответствующих спецификациях | Проверки запланированы |
| 13. Техническая детализация | [Design / Open Questions](design.md#open-questions) | Q-ID и принятые решения |
| 14. Порядок реализации | [Design / Implementation Sequence](design.md#implementation-sequence), [Tasks](tasks.md) | Семь этапов и декомпозиция каркаса |

Прежние техническая спецификация и документ структуры репозитория удалены после переноса. Отдельная устаревающая копия требований больше не поддерживается. Архитектура сохраняет роль реестра решений, README — точки входа.

## Работа с изменением

```sh
openspec status --change implement-runner-agent
openspec show implement-runner-agent
openspec validate --all --strict --no-interactive
```

Требования и сценарии написаны по-русски; структурные заголовки `Requirement`, `Scenario`, `WHEN`/`THEN` и `ADDED Requirements` следуют формату OpenSpec. Длинные таблицы и условия исходного контракта сохранены в контрактных сценариях соответствующих требований; AT-сценарии задают конкретные проверки. Наличие артефактов в статусе `done` означает готовность планирования, а не выполненные implementation-задачи.
