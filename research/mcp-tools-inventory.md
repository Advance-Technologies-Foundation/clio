# Clio MCP — інвентаризація інструментів

Зафіксований стан на 2026-05-22 (гілка `research/mcp-components-cdn`).

## Підсумкові цифри

- **105 реєстрацій** `[McpServerTool(Name=…)]` у 61 файлі під [clio/Command/McpServer/Tools/](../clio/Command/McpServer/Tools)
- **104 унікальних публічних імен** (1 — `StopAllCreatio` — явний deprecated PascalCase alias)
- **2 deprecated-аліаси для legacy-клієнтів** (`StopAllCreatio`, `restart-by-environmentName`)
- Реєстрація — через `WithToolsFromAssembly` у [clio/BindingsModule.cs:548](../clio/BindingsModule.cs#L548); список MCP-tool'ів атрибутно-керований, явного реєстру немає.

## Таймлайн появи

| Місяць | Додано | Хто | Коментар |
|---|---|---|---|
| 2026-03 | 51 | переважно Kyryl Krylov (46) | вибух у перші 4 тижні: основа MCP — start/stop/restart, workspace, схеми, data binding, redis, FSM |
| 2026-04 | 47 | Alex Kravchuk (36), Tetiana Moshon (8), Dmytro Baranovskyi, Vladimir, d-krestov | два «великі» PR: `#527` (18 інструментів — naming + sections + apps) та `#549` (14 — schema/page/SQL) |
| 2026-05 | 7 | Artem Kulykov (4 sys-settings), Marharyta Dymytrova, Dmytro Baranovskyi, Vladimir | темп різко впав |

### Розподіл за авторами

| Автор | Tools | Перший внесок | Основний фокус |
|---|---|---|---|
| Kyryl Krylov / k.krylov | 46 | 2026-03-03 | каркас MCP, lifecycle, schemas, packages, redis, restore-db, FSM, skills |
| Alex Kravchuk / Alexandr | 41 | 2026-03-20 | apps/sections, pages, schemas-generic, SQL-schemas, client unit, hotfix, naming-фіксери |
| Tetiana Moshon | 8 | 2026-04-10 | DataForge (cluster `dataforge-*`) |
| Artem Kulykov | 4 | 2026-05-20 | system settings CRUD |
| Dmytro Baranovskyi | 2 | 2026-04-15 | business rules |
| Vladimir | 2 | 2026-04-09 | link-from-repository-unlocked, generate-source-code |
| Marharyta Dymytrova | 1 | 2026-05-04 | get-schema-name-prefix |
| d-krestov | 1 | 2026-04-20 | get-guidance |

### Найбільші «масові» PR/комміти

| Tools | Subject |
|---|---|
| 18 | Improve MCP tool naming, expand delete-schema, and fix CLI aliases (#527) |
| 14 | feat(mcp): page operations, schema tools, DI cleanup, and IFileSystem migration (#549) |
| 8 | ENG-87092 Clio MCP: Data forge |
| 7 | agentic-dev-flow (#465) |
| 7 | Codex/common (#460) |
| 6 | feat: add MCP tools for workspace package management and unit testing (#462) |
| 5 | Eng 87085 adac clio (#476) |
| 4 | feat(mcp): add SysSettings CRU support for AI agents (ENG-88957) |

## Категорії інструментів

Категорізація — за призначенням, не за файлом. У дужках — кількість.

### App lifecycle (16)
start-creatio, stop-creatio, **stop-all-creatio**, ~~StopAllCreatio~~ (deprecated alias),
**restart-by-environment-name**, ~~restart-by-environmentName~~ (deprecated alias), restart-by-credentials,
deploy-creatio, uninstall-creatio, install-application,
find-empty-iis-port, reg-web-app, list-environments,
assert-infrastructure, show-passing-infrastructure, check-settings-health

### Workspace & packages (18)
create-workspace, push-workspace, restore-workspace,
add-package, new-test-project, list-packages,
pkg-to-db, pkg-to-file-system,
link-from-repository-by-environment, link-from-repository-by-env-package-path, link-from-repository-unlocked,
unlock-for-hotfix, finish-hotfix,
download-configuration-by-environment, download-configuration-by-build,
compile-creatio, get-fsm-mode, set-fsm-mode

### Apps / Sections (8) — усі з одного PR #527
create-app, list-apps, get-app-info, delete-app,
create-app-section, update-app-section, delete-app-section, list-app-sections

### Pages — Freedom UI (7)
create-page, get-page, update-page, list-pages, sync-pages, validate-page, list-page-templates

### Schemas (generic) (7)
create-schema, get-schema, update-schema, delete-schema, sync-schemas,
get-schema-name-prefix, generate-source-code

### Entity schemas (7)
create-entity-schema, update-entity-schema, find-entity-schema,
get-entity-schema-properties, get-entity-schema-column-properties,
modify-entity-schema-column, create-lookup

### Client unit schemas (3)
create-client-unit-schema, update-client-unit-schema, get-client-unit-schema

### SQL schemas (4)
create-sql-schema, get-sql-schema, update-sql-schema, install-sql-schema

### Data binding (6)
create-data-binding, add-data-binding-row, remove-data-binding-row,
create-data-binding-db, upsert-data-binding-row-db, remove-data-binding-row-db

### System settings (4) — новинка від 2026-05-20
create-sys-setting, get-sys-setting, update-sys-setting, list-sys-settings

### Business rules (2)
create-entity-business-rule, create-page-business-rule

### Data Forge (8) — окремий кластер від Tetiana
dataforge-status, dataforge-context, dataforge-initialize, dataforge-update,
dataforge-find-tables, dataforge-find-lookups, dataforge-get-relations, dataforge-get-table-columns

### Process / Modeling (4)
create-user-task, modify-user-task-parameters, generate-process-model, add-item-model

### DB ops (5)
clear-redis-db-by-credentials, clear-redis-db-by-environment,
restore-db-by-credentials, restore-db-by-environment, restore-db-to-local-server

### AI / Guidance / Component info (6)
get-guidance, get-component-info, get-tool-contract,
install-skills, update-skill, delete-skill

## Систематичні проблеми, які впадають в око

### 1. Подвоєння `*-by-credentials` / `*-by-environment`
Послідовно зустрічається у 5 категоріях: restart, clear-redis, restore-db, download-configuration, link-from-repository. Кожна пара робить те саме, але одна бере `environmentName`, інша — кортеж `(url, login, password, isNetCore?)`.

- restart-by-credentials + restart-by-environment-name (+ legacy camelCase)
- clear-redis-db-by-credentials + clear-redis-db-by-environment
- restore-db-by-credentials + restore-db-by-environment (+ restore-db-to-local-server)
- download-configuration-by-environment + download-configuration-by-build
- link-from-repository-by-environment + link-from-repository-by-env-package-path + link-from-repository-unlocked

→ ~10–12 інструментів зі ~5 «логічних» операцій. Один tool з опційними параметрами вирішив би це.

### 2. Schema-сім'я фрагментована
Окремі реєстрації для **generic / entity / client-unit / sql** — у сумі 21 інструмент, з повторюваним патерном `create / get / update / delete / sync / list`. Може бути одна сім'я `*-schema` з типом схеми як параметром.

### 3. Deprecated-aліаси все ще зареєстровані
- `StopAllCreatio` (PascalCase) → `stop-all-creatio`
- `restart-by-environmentName` → `restart-by-environment-name`

Обидва позначені `[Deprecated:…]` в Description, але рахуються як окремі MCP-tools у lookup і збільшують шум для AI.

### 4. Один файл — кілька великих інструментів
- [EntitySchemaTool.cs](../clio/Command/McpServer/Tools/EntitySchemaTool.cs) — 7 інструментів, 37 KB
- [ApplicationTool.cs](../clio/Command/McpServer/Tools/ApplicationTool.cs) — 8 інструментів, 16 KB
- [DataForgeTool.cs](../clio/Command/McpServer/Tools/DataForgeTool.cs) — 8 інструментів, 13 KB
- [BusinessRuleTool.cs](../clio/Command/McpServer/Tools/BusinessRuleTool.cs) — 2 інструменти, 16 KB
- [ToolContractGetTool.cs](../clio/Command/McpServer/Tools/ToolContractGetTool.cs) — 1 інструмент, **154 KB** (?!)

### 5. AGENTS.md живе поруч
[clio/Command/McpServer/AGENTS.md](../clio/Command/McpServer/AGENTS.md) — 17 KB інструкцій для агентів-розробників. Це сигнал, що в команди сформована неявна культура «one tool ↔ one schema attribute», яку варто закріпити явним каталогом і політикою на введення нових інструментів.

## Дані-першоджерело

- [/tmp/clio-mcp-tools-map.tsv](file:///tmp/clio-mcp-tools-map.tsv) — tool ↔ file ↔ line
- [/tmp/clio-mcp-tool-history.tsv](file:///tmp/clio-mcp-tool-history.tsv) — tool ↔ commit ↔ author ↔ date ↔ subject

Команди для повторного збирання:
```bash
# Map tool→file (handles scope-local `const string ToolName`)
python3 -c "$(cat <<'PY'
import os, re, glob
…
PY
)"

# History (first appearance per tool)
for tool in …; do
  git log --reverse -S"$tool" --pretty=format:"%h|%an|%ad|%s" --date=short \
    -- clio/Command/McpServer/Tools/ | head -1
done
```
(повні скрипти у журналі обговорення цього дослідження)
