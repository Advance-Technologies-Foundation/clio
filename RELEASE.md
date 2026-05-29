# Release Process

## Быстрый способ: GitHub Copilot команда

### `/release` 🚀

Используйте команду `/release` в GitHub Copilot для автоматического создания нового релиза:

1. Откройте GitHub Copilot Chat
2. Введите `/release`
3. Copilot автоматически:
   - Найдет последний тег релиза
   - Инкрементирует минорную версию на 1
   - Создаст и запушит новый тег
   - Создаст GitHub release (если доступен GitHub CLI)
   - Подтвердит создание тега и релиза

**Пример:**
- Текущий тег: `8.0.1.42`
- Новый тег: `8.0.1.43`
- Автоматически создается GitHub release

## Альтернативные способы

### Автоматические скрипты

Скрипты автоматически создают тег и GitHub release:

#### PowerShell (Windows/macOS/Linux):
```powershell
# Интерактивный режим (создает тег + GitHub release)
.\create-release.ps1

# Автоматический режим (без подтверждения)
.\create-release.ps1 -Force
```

#### Bash (macOS/Linux):
```bash
# Интерактивный режим (создает тег + GitHub release)
./create-release.sh

# Автоматический режим (без подтверждения)
./create-release.sh --force
```

**Что делают скрипты:**
- Находят последний тег
- Инкрементируют версию (X.Y.Z.W → X.Y.Z.W+1)
- Создают и пушат новый тег
- Создают GitHub release (если установлен GitHub CLI)
- Автоматически запускают NuGet публикацию

## Ручной способ: создание релиза

### 1. Pre-release checklist

Before creating a release tag, verify the following:

**BMAD artifacts**
- [ ] All features in this release have an approved test plan in `spec/test-plans/`
- [ ] All stories for this release are `done` in `spec/sprint-status.yaml`
- [ ] Run `/bmad-status` to confirm no feature is stuck in `in-progress` or `review`

**Test gates**
- [ ] `make test-unit` passes locally
- [ ] CI is green on master (`make check-pr`)
- [ ] MCP E2E tests run manually if any MCP tool was changed (not in CI yet)
- [ ] SonarCloud has no new unresolved issues

**Documentation**
- [ ] Command docs updated (`clio/docs/commands/`, `clio/help/en/`, `clio/Commands.md`)
- [ ] AGENTS.md updated if any new policies were introduced
- [ ] What's new draft ready (see [Release Notes](#6-обязательные-release-notes-whats-new) below)

### 1. Подготовка

Убедитесь, что все изменения готовы и протестированы в master ветке.

### 2. Создание релиза

1. **Создайте тег версии** в формате `X.Y.Z.W`:
   ```bash
   git tag 8.0.1.43
   git push origin 8.0.1.43
   ```

2. **Создайте GitHub Release**:
   
   **Вариант A: GitHub CLI (рекомендуется)**
   ```bash
   gh release create 8.0.1.43 --title "Release 8.0.1.43" --notes "Automated release 8.0.1.43"
   ```
   
   **Вариант B: Веб-интерфейс**
   - Перейдите на страницу [Releases](https://github.com/Advance-Technologies-Foundation/clio/releases)
   - Нажмите "Create a new release"
   - Выберите созданный тег
   - Заполните описание релиза
   - Нажмите "Publish release"

### 3. Автоматическая публикация

После создания релиза автоматически запустится workflow `release-to-nuget`, который:

1. ✅ **Извлечет версию** из тега (поддерживает форматы `8.0.1.43` и `v8.0.1.43`)
2. ✅ **Проверит формат** версии (должен быть `X.Y.Z.W`)
3. ✅ **Запустит тесты** clio
4. ✅ **Выполнит анализ кода** через SonarQube
5. ✅ **Соберет пакет** clio с версией из тега
6. ✅ **Опубликует в NuGet** автоматически

### 4. Требования к версии

- **Правильные форматы**: `8.0.1.43`, `v8.0.1.43`
- **Неправильные форматы**: `8.0.1`, `v8.0`, `release-8.0.1.43`

### 5. Локальная сборка с версией

Для локальной сборки с определенной версией:

```bash
dotnet pack .\clio\clio.csproj -c Release --output ./output /p:AssemblyVersion=8.0.1.43 /p:FileVersion=8.0.1.43 /p:Version=8.0.1.43
```

### 6. Обязательные Release Notes (What's new)

> **ВАЖНО:** Каждый релиз **обязан** содержать описание изменений (What's new) в GitHub Release.
> Эти заметки отображаются пользователям при выполнении команды `clio update`.

При разработке через кодинг-агенты (GitHub Copilot, Claude Code и др.) агент **обязан**:

1. **Вести список изменений** в процессе разработки — каждый значимый коммит должен содержать понятное описание
2. **Формировать What's new** при создании релиза — краткий, понятный список изменений на английском языке
3. **Включать What's new в GitHub Release** — через параметр `--notes` в `gh release create`

**Формат What's new:**
```markdown
- Add feature X for better Y
- Fix issue with Z when doing W
- Improve performance of operation Q
```

**Пример создания релиза с What's new:**
```bash
gh release create 8.0.2.65 --title "Release 8.0.2.65" --notes "$(cat <<'EOF'
- Fix player name display in environment list
- Add interactive quiz easter egg
- Improve update command UX with spinner and release notes
EOF
)"
```

**Без What's new** пользователи команды `clio update` не увидят описание изменений, что снижает доверие к обновлениям.

### 7. Примечания

- **cliogate проект** остается без изменений версионирования
- **Только clio пакет** получает версию из тега релиза
- При локальной разработке используется версия по умолчанию `8.0.1.42`

## Breaking changes — MCP tool consolidation (ENG-90312)

This release consolidates the MCP tool registry from **105 to 24 tools** in a single hop. Every non-read-only tool is now invoked through a single dispatcher (`clio-run`) keyed by an `args.command` discriminator. The 23 read-only tools (`list-environments`, `get-schema`, `apps`, `sys-setting`, `dataforge-find`, …) remain registered flat so hosts can continue to auto-approve them. CLI verbs (`clio restart`, `clio clear-redis-db`, …) are unaffected.

### Wire shape

```jsonc
// Before — direct top-level tool call
{ "tool": "restart-creatio",
  "args": { "mode": "environment", "environment-name": "dev" } }

// After — clio-run envelope; command discriminator selects the operation
{ "tool": "clio-run",
  "args": { "command": "restart-creatio", "mode": "environment", "environment-name": "dev" } }
```

The MCP SDK publishes `clio-run.args` as a JSON Schema `anyOf` over the 52 non-read-only commands; AI clients see the per-command field schema during `tools/list` without a separate discovery round trip.

### Three before/after examples

```jsonc
// 1. restart-creatio
// Before:
{ "tool": "restart-creatio", "args": { "mode": "environment", "environment-name": "dev" } }
// After:
{ "tool": "clio-run", "args": { "command": "restart-creatio", "mode": "environment", "environment-name": "dev" } }

// 2. create-schema (entity)
// Before:
{ "tool": "create-schema", "args": { "schema-type": "entity", "schema-name": "UsrVehicle", "package-name": "UsrPkg", "environment-name": "dev", "title-localizations": { "en-US": "Vehicle" } } }
// After:
{ "tool": "clio-run", "args": { "command": "create-schema", "schema-type": "entity", "schema-name": "UsrVehicle", "package-name": "UsrPkg", "environment-name": "dev", "title-localizations": { "en-US": "Vehicle" } } }

// 3. app-section (action=create)
// Before:
{ "tool": "app-section", "args": { "action": "create", "environment-name": "dev", "application-code": "UsrApp", "caption": "Orders" } }
// After:
{ "tool": "clio-run", "args": { "command": "app-section", "action": "create", "environment-name": "dev", "application-code": "UsrApp", "caption": "Orders" } }
```

### Migration

For every legacy MCP tool name listed in the migration table below, the new wire shape is:

```
old: { "tool": "<legacy-name>", "args": { ...fields... } }
new: { "tool": "clio-run",      "args": { "command": "<legacy-name>", ...same fields... } }
```

The 105 → 24 hop combines two consolidation passes:
1. Phase 1 collapsed env/creds pairs, schema CRUD, and per-resource action sets into 11 discriminator-bearing tools (105 → 75 surface).
2. Phase 2 folded every non-read-only tool — including those Phase-1 consolidations — behind `clio-run`, leaving 23 read-only tools flat (75 → 24 surface).

AI clients that integrated against the intermediate Phase-1 tool names (`restart-creatio`, `clear-redis-db`, `restore-db`, `create-schema`, `app-section`, …) hit a single break in this release: those names disappear from `tools/list` and must move under `clio-run`. The CLI verbs they back are still available via shell.

### Migration table

| Legacy MCP tool | New MCP tool | Discriminator argument | Example payload |
|---|---|---|---|
| `StopAllCreatio` | `stop-all-creatio` | — (removed; canonical kebab-case name kept) | `{}` |
| `restart-by-environmentName` | `restart-creatio` | `mode=environment` | `{"mode":"environment","environment-name":"dev"}` |
| `restart-by-environment-name` | `restart-creatio` | `mode=environment` | `{"mode":"environment","environment-name":"dev"}` |
| `restart-by-credentials` | `restart-creatio` | `mode=credentials` | `{"mode":"credentials","url":"http://localhost:5000","login":"Supervisor","password":"...","is-net-core":true}` |
| `clear-redis-db-by-environment` | `clear-redis-db` | `mode=environment` | `{"mode":"environment","environment-name":"dev"}` |
| `clear-redis-db-by-credentials` | `clear-redis-db` | `mode=credentials` | `{"mode":"credentials","url":"http://localhost:5000","login":"Supervisor","password":"..."}` |
| `restore-db-by-environment` | `restore-db` | `mode=environment` | `{"mode":"environment","environment-name":"sandbox","backup-path":"C:\\backups\\db.backup"}` |
| `restore-db-by-credentials` | `restore-db` | `mode=db-credentials` | `{"mode":"db-credentials","db-server-uri":"mssql://localhost:1433","db-user":"sa","db-password":"...","backup-path":"C:\\backups\\db.bak","db-name":"sandbox_db"}` |
| `restore-db-to-local-server` | `restore-db` | `mode=local-server` | `{"mode":"local-server","db-server-name":"local-sql","backup-path":"C:\\backups\\db.bak","db-name":"sandbox_db"}` |
| `download-configuration-by-environment` | `download-configuration` | `source=environment` | `{"source":"environment","workspace-path":"C:\\workspace","environment-name":"dev"}` |
| `download-configuration-by-build` | `download-configuration` | `source=build` | `{"source":"build","workspace-path":"C:\\workspace","build-path":"C:\\creatio.zip"}` |
| `link-from-repository-by-environment` | `link-from-repository` | `mode=by-env` | `{"mode":"by-env","repo-path":"C:\\Repo","environment-name":"dev","packages":"PkgA,PkgB"}` |
| `link-from-repository-by-env-package-path` | `link-from-repository` | `mode=by-pkg-path` | `{"mode":"by-pkg-path","repo-path":"C:\\Repo","env-pkg-path":"C:\\Creatio\\Terrasoft.Configuration\\Pkg","packages":"*"}` |
| `link-from-repository-unlocked` | `link-from-repository` | `mode=unlocked` | `{"mode":"unlocked","repo-path":"C:\\Repo","environment-name":"dev"}` |
| `create-schema` (source-code) | `create-schema` | `schema-type=source-code` | `{"schema-type":"source-code","schema-name":"UsrMyHelper","package-name":"UsrPkg","environment-name":"dev"}` |
| `create-entity-schema` | `create-schema` | `schema-type=entity` | `{"schema-type":"entity","schema-name":"UsrVehicle","package-name":"UsrPkg","environment-name":"dev","title-localizations":{"en-US":"Vehicle"}}` |
| `create-lookup` | `create-schema` | `schema-type=lookup` | `{"schema-type":"lookup","schema-name":"UsrOrderStatus","package-name":"UsrPkg","environment-name":"dev","title-localizations":{"en-US":"Order status"}}` |
| `create-client-unit-schema` | `create-schema` | `schema-type=client-unit` | `{"schema-type":"client-unit","schema-name":"UsrUtils","package-name":"UsrPkg","environment-name":"dev"}` |
| `create-sql-schema` | `create-schema` | `schema-type=sql` | `{"schema-type":"sql","schema-name":"UsrSql","package-name":"UsrPkg","environment-name":"dev"}` |
| `update-schema` (source-code) | `update-schema` | `schema-type=source-code` | `{"schema-type":"source-code","schema-name":"UsrMyHelper","body":"...","environment-name":"dev"}` |
| `update-entity-schema` | `update-schema` | `schema-type=entity` | `{"schema-type":"entity","schema-name":"UsrVehicle","package-name":"UsrPkg","environment-name":"dev","operations":[{"action":"add","column-name":"UsrColour","type":"ShortText","title-localizations":{"en-US":"Colour"}}]}` |
| `update-client-unit-schema` | `update-schema` | `schema-type=client-unit` | `{"schema-type":"client-unit","schema-name":"UsrUtils","body":"...","environment-name":"dev"}` |
| `update-sql-schema` | `update-schema` | `schema-type=sql` | `{"schema-type":"sql","schema-name":"UsrSql","body":"...","environment-name":"dev"}` |
| `get-schema` (source-code) | `get-schema` | `schema-type=source-code` | `{"schema-type":"source-code","schema-name":"UsrMyHelper","environment-name":"dev"}` |
| `get-entity-schema-properties` | `get-schema` | `schema-type=entity` | `{"schema-type":"entity","schema-name":"UsrVehicle","package-name":"UsrPkg","environment-name":"dev"}` |
| `get-entity-schema-column-properties` | `get-schema` | `schema-type=entity` + `column` | `{"schema-type":"entity","schema-name":"UsrVehicle","package-name":"UsrPkg","environment-name":"dev","column":"UsrColour"}` |
| `get-client-unit-schema` | `get-schema` | `schema-type=client-unit` | `{"schema-type":"client-unit","schema-name":"UsrUtils","environment-name":"dev"}` |
| `get-sql-schema` | `get-schema` | `schema-type=sql` | `{"schema-type":"sql","schema-name":"UsrSql","environment-name":"dev"}` |
| `find-entity-schema` | `list-schemas` | `schema-type=entity` | `{"schema-type":"entity","environment-name":"dev","search-pattern":"Order"}` |
| `list-apps` | `apps` | — (omit id/code to list) | `{"environment-name":"dev"}` |
| `get-app-info` | `apps` | id or code | `{"environment-name":"dev","code":"UsrMyApp"}` |
| `get-sys-setting` | `sys-setting` | code | `{"environment-name":"dev","code":"SchemaNamePrefix"}` |
| `list-sys-settings` | `sys-setting` | — (omit code) | `{"environment-name":"dev"}` |
| `create-sys-setting` | `upsert-sys-setting` | — (creates if missing) | `{"environment-name":"dev","code":"UsrFoo","value-type-name":"Text","value":"bar"}` |
| `update-sys-setting` | `upsert-sys-setting` | — (updates if exists) | `{"environment-name":"dev","code":"UsrFoo","value":"baz"}` |
| `dataforge-find-tables` | `dataforge-find` | `kind=tables` | `{"environment-name":"dev","kind":"tables","query":"Order"}` |
| `dataforge-find-lookups` | `dataforge-find` | `kind=lookups` | `{"environment-name":"dev","kind":"lookups","query":"Status"}` |
| `add-data-binding-row` | `data-binding-row` | `action=add` | `{"action":"add","package-name":"UsrPkg","binding-name":"UsrBinding","workspace-path":"C:\\Workspace","values":"{}"}` |
| `remove-data-binding-row` | `data-binding-row` | `action=remove` | `{"action":"remove","package-name":"UsrPkg","binding-name":"UsrBinding","workspace-path":"C:\\Workspace","key-value":"..."}` |
| `upsert-data-binding-row-db` | `data-binding-row-db` | `action=upsert` | `{"action":"upsert","environment-name":"dev","package-name":"UsrPkg","binding-name":"UsrBinding","values":"{}"}` |
| `remove-data-binding-row-db` | `data-binding-row-db` | `action=remove` | `{"action":"remove","environment-name":"dev","package-name":"UsrPkg","binding-name":"UsrBinding","key-value":"..."}` |
| `create-app-section` | `app-section` | `action=create` | `{"action":"create","environment-name":"dev","application-code":"UsrApp","caption":"Orders"}` |
| `update-app-section` | `app-section` | `action=update` | `{"action":"update","environment-name":"dev","application-code":"UsrApp","section-code":"UsrOrders","caption":"Renamed"}` |
| `delete-app-section` | `app-section` | `action=delete` | `{"action":"delete","environment-name":"dev","application-code":"UsrApp","section-code":"UsrOrders"}` |
| `list-app-sections` | `app-section` | `action=list` | `{"action":"list","environment-name":"dev","application-code":"UsrApp"}` |
| `pkg-to-file-system` | `pkg-mode` | `target=file-system` | `{"target":"file-system","environment-name":"dev"}` |
| `pkg-to-db` | `pkg-mode` | `target=db` | `{"target":"db","environment-name":"dev"}` |

### Phase 2 — full list of commands now under `clio-run`

The 52 non-read-only commands routed by `clio-run`:

`add-item-model`, `add-package`, `app-section`, `clear-redis-db`, `compile-creatio`, `create-app`, `create-data-binding`, `create-data-binding-db`, `create-entity-business-rule`, `create-page`, `create-page-business-rule`, `create-schema`, `create-user-task`, `create-workspace`, `data-binding-row`, `data-binding-row-db`, `dataforge-initialize`, `dataforge-update`, `delete-app`, `delete-schema`, `delete-skill`, `deploy-creatio`, `download-configuration`, `finish-hotfix`, `generate-process-model`, `generate-source-code`, `get-page`, `install-application`, `install-skills`, `install-sql-schema`, `link-from-repository`, `modify-entity-schema-column`, `modify-user-task-parameters`, `new-test-project`, `pkg-mode`, `push-workspace`, `reg-web-app`, `restart-creatio`, `restore-db`, `restore-workspace`, `set-fsm-mode`, `start-creatio`, `stop-all-creatio`, `stop-creatio`, `sync-pages`, `sync-schemas`, `uninstall-creatio`, `unlock-for-hotfix`, `update-page`, `update-schema`, `update-skill`, `upsert-sys-setting`.

The 23 read-only tools that remain registered flat:

`apps`, `assert-infrastructure`, `check-settings-health`, `dataforge-context`, `dataforge-find`, `dataforge-get-relations`, `dataforge-get-table-columns`, `dataforge-status`, `find-empty-iis-port`, `get-component-info`, `get-fsm-mode`, `get-guidance`, `get-schema`, `get-schema-name-prefix`, `get-tool-contract`, `list-environments`, `list-packages`, `list-page-templates`, `list-pages`, `list-schemas`, `show-passing-infrastructure`, `sys-setting`, `validate-page`.

### Notes

- CLI verbs (`clio restart`, `clio clear-redis-db`, `clio restore-db`, etc.) are unchanged. The consolidation only affects the MCP `tools/list` surface served by `clio mcp-server`.
- The 23 read-only tools keep their `ReadOnly = true` safety flag so hosts (Claude Desktop, Cursor, Claude Code) can auto-approve them. The `clio-run` tool is marked `Destructive = true` because its union includes destructive commands — every call through `clio-run` will go through the host's destructive-call confirmation path.
- Final MCP tool count after consolidation: **24** (down from 105). The budget ratchet is enforced by `clio.tests/Command/McpServer/McpToolBudgetTests.cs`.
- The dispatcher's argument validation lives on each per-command `*RunArgs` record. AI agents receive explicit "mode='X' requires field Y" / "schema-type=entity requires title-localizations" errors instead of silent fallback paths.
- Hotfix (`unlock-for-hotfix`, `finish-hotfix`) and skills (`install-skills`, `update-skill`, `delete-skill`) families are routed through `clio-run` as of this release.
