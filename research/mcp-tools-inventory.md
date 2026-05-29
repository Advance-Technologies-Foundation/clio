# Clio MCP — tools inventory

Snapshot captured on 2026-05-22 (branch `research/mcp-components-cdn`).

## Headline numbers

- **105 `[McpServerTool(Name=…)]` registrations** across 61 files under [clio/Command/McpServer/Tools/](../clio/Command/McpServer/Tools)
- **104 unique public tool names** (one — `StopAllCreatio` — is an explicit deprecated PascalCase alias)
- **2 deprecated aliases** preserved for legacy clients (`StopAllCreatio`, `restart-by-environmentName`)
- Tools are registered automatically via `WithToolsFromAssembly` in [clio/BindingsModule.cs:548](../clio/BindingsModule.cs#L548); the tool list is attribute-driven, there is no explicit registry.

## Timeline

| Month | Added | Lead author | Comment |
|---|---|---|---|
| 2026-03 | 51 | mostly Kyryl Krylov (46) | scaffold burst in the first four weeks: lifecycle (start/stop/restart), workspace, schemas, data binding, redis, FSM |
| 2026-04 | 47 | Alex Kravchuk (36), Tetiana Moshon (8), Dmytro Baranovskyi, Vladimir, d-krestov | two batch PRs: `#527` (18 tools — naming + sections + apps) and `#549` (14 — schema/page/SQL) |
| 2026-05 | 7 | Artem Kulykov (4 sys-settings), Marharyta Dymytrova, Dmytro Baranovskyi, Vladimir | cadence dropped sharply |

### Authors

| Author | Tools | First contribution | Focus area |
|---|---|---|---|
| Kyryl Krylov / k.krylov | 46 | 2026-03-03 | MCP scaffolding, lifecycle, schemas, packages, redis, restore-db, FSM, skills |
| Alex Kravchuk / Alexandr | 41 | 2026-03-20 | apps/sections, pages, generic/SQL/client-unit schemas, hotfix, naming fixes |
| Tetiana Moshon | 8 | 2026-04-10 | DataForge (the `dataforge-*` cluster) |
| Artem Kulykov | 4 | 2026-05-20 | system settings CRUD |
| Dmytro Baranovskyi | 2 | 2026-04-15 | business rules |
| Vladimir | 2 | 2026-04-09 | link-from-repository-unlocked, generate-source-code |
| Marharyta Dymytrova | 1 | 2026-05-04 | get-schema-name-prefix |
| d-krestov | 1 | 2026-04-20 | get-guidance |

### Largest batch PRs / commits

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

## Tools by category

Categorised by purpose, not by source file. Count in parentheses.

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

### Apps / Sections (8) — all from PR #527
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

### System settings (4) — new as of 2026-05-20
create-sys-setting, get-sys-setting, update-sys-setting, list-sys-settings

### Business rules (2)
create-entity-business-rule, create-page-business-rule

### Data Forge (8) — distinct cluster from Tetiana
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

## Systemic issues that stand out

### 1. `*-by-credentials` / `*-by-environment` doubling
The pattern recurs across five categories: restart, clear-redis, restore-db, download-configuration, link-from-repository. Each pair performs the same operation but one takes `environmentName`, the other takes the `(url, login, password, isNetCore?)` tuple.

- restart-by-credentials + restart-by-environment-name (+ legacy camelCase)
- clear-redis-db-by-credentials + clear-redis-db-by-environment
- restore-db-by-credentials + restore-db-by-environment (+ restore-db-to-local-server)
- download-configuration-by-environment + download-configuration-by-build
- link-from-repository-by-environment + link-from-repository-by-env-package-path + link-from-repository-unlocked

→ ~10–12 tools covering ~5 logical operations. One tool with optional parameters would replace each pair.

### 2. Schema family is fragmented
Separate registrations for **generic / entity / client-unit / sql** — 21 tools in total, with a repeating `create / get / update / delete / sync / list` pattern. A single `*-schema` family with the schema type as a parameter would cover the same surface.

### 3. Deprecated aliases still registered
- `StopAllCreatio` (PascalCase) → `stop-all-creatio`
- `restart-by-environmentName` → `restart-by-environment-name`

Both are marked `[Deprecated:…]` in their Description, but they still count as separate MCP tools at lookup time and add noise for the AI agent.

### 4. One file hosts multiple large tools
- [EntitySchemaTool.cs](../clio/Command/McpServer/Tools/EntitySchemaTool.cs) — 7 tools, 37 KB
- [ApplicationTool.cs](../clio/Command/McpServer/Tools/ApplicationTool.cs) — 8 tools, 16 KB
- [DataForgeTool.cs](../clio/Command/McpServer/Tools/DataForgeTool.cs) — 8 tools, 13 KB
- [BusinessRuleTool.cs](../clio/Command/McpServer/Tools/BusinessRuleTool.cs) — 2 tools, 16 KB
- [ToolContractGetTool.cs](../clio/Command/McpServer/Tools/ToolContractGetTool.cs) — 1 tool, **154 KB** (worth a closer look)

### 5. AGENTS.md lives next door
[clio/Command/McpServer/AGENTS.md](../clio/Command/McpServer/AGENTS.md) — 17 KB of guidance for developer agents. This signals the team has formed an implicit "one tool ↔ one schema attribute" culture worth codifying as an explicit catalogue plus a policy that gates the introduction of new tools.

## Source data

- `/tmp/clio-mcp-tools-map.tsv` — tool ↔ file ↔ line
- `/tmp/clio-mcp-tool-history.tsv` — tool ↔ commit ↔ author ↔ date ↔ subject

Regeneration outline:
```bash
# Map tool → file (scope-aware `const string ToolName` resolution)
python3 scope_aware_extractor.py

# First-appearance commit per tool name
for tool in <names>; do
  git log --reverse -S"$tool" --pretty=format:"%h|%an|%ad|%s" --date=short \
    -- clio/Command/McpServer/Tools/ | head -1
done
```
