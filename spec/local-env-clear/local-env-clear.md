# Clear Local Environment Command

## Overview
Добавить новую команду `clear-local-env` для удаления устаревших приложений со статусом `Deleted` из локального окружения.

## Требования

### Функциональность
1. **Удаление приложений в статусе Deleted**
   - Удалить все приложения, находящиеся в состоянии `Deleted`
   - Выполнить полную очистку связанных ресурсов

2. **Подтверждение операции**
   - По умолчанию: запросить подтверждение у пользователя
   - С флагом `--force`: выполнить без подтверждения
   - Использование: `clio clear-local-env --force`

3. **Управление службами ОС**
   - Обнаружить зарегистрированные в ОС службы
   - Удалить службы, связанные с приложениями в статусе `Deleted`

4. **Логирование действий**
   - Вывод подробного лога всех операций

## Операции при выполнении команды

1. **Проверка и удаление системной службы**
   - Если служба зарегистрирована в ОС
   - Если служба использует директорию удаляемого приложения
   - Удалить службу из ОС

2. **Удаление локальной папки приложения**
   - Удалить директорию приложения со статусом `Deleted`

3. **Обновление конфигурации clio**
   - Удалить окружение из настроек `clio`

## Синтаксис команды

```bash
clio clear-local-env [--force]
```

### Параметры
- `--force` (опционально) - выполнить без запроса подтверждения

## Documentation

### Technical Documents
- [Implementation Plan](./local-env-clear-implementation-plan.md) - Task breakdown, timing, and dependencies
- [Architecture & Design](./local-env-clear-architecture.md) - System design, diagrams, and component relationships

### What's Included

**Implementation Plan** covers:
- Architecture analysis and key dependencies
- 5 implementation tasks with code patterns
- Unit test structure and test cases
- 3-phase implementation timeline (11-14 hours total)
- Success criteria and related code examples

**Architecture & Design** covers:
- Component diagram showing all dependencies
- Data flow from start to finish
- State machine for command execution
- Class relationship diagram
- Detailed sequence diagrams
- Platform-specific behaviors (Windows/Linux/macOS)
- Configuration file before/after examples
- Expected logging output examples
- Testing strategy and integration points

### Getting Started
1. **For implementers**: Start with Implementation Plan
2. **For architects**: See Architecture & Design
3. **For testers**: Review test cases in Implementation Plan
