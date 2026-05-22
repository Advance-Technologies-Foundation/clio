# ENG-90312 — Consolidate Clio MCP tools

Jira: [ENG-90312](https://creatio.atlassian.net/browse/ENG-90312)
Branch: `xenodochial-moser-229844` (single PR)
Status: planning — pending approval

## 1. Goal

Reduce the number of MCP tools registered by clio from **105** down toward the Jira target while preserving capability and tool UX. The published tool list must fit the MCP / Anthropic 128 hard limit with a comfortable margin and leave headroom for host-agent tools and concurrent MCP servers. The Jira-stated `≤ 65` is treated as a stretch target: we aim for it through the explicit AC #1–3 work plus per-resource consolidation, but we do **not** force the number below 65 by breaking tool ergonomics. Expected final range after this PR: **~70–73**, with `count ≤ 80` enforced as a hard CI ratchet (room for a small follow-up).

## 2. Acceptance criteria (from Jira) — interpretation for this PR

| # | Criterion | Block | Notes |
|---|---|---|---|
| AC1 | Deprecated aliases (`StopAllCreatio`, `restart-by-environmentName`) are no longer registered as MCP tools | A | Strict |
| AC2 | `*-by-environment` / `*-by-credentials` pairs are collapsed into single MCP tools with discriminator | B | Strict |
| AC3 | Schema CRUD family is consolidated (`create-schema`, `get-schema`, `update-schema`, `delete-schema`, `list-schemas` with `schema-type`) | C | Strict |
| AC4 | `clio/Command/McpServer/AGENTS.md` documents the 128 hard limit, soft target, and rules for new MCP tools | E | Strict |
| AC5 | Final tool count is `≤ 65`, verified by a unit test that asserts the bound | D, E | **Soft interpretation**: ratchet test enforces `count ≤ <final achieved>` (target 70–73). PR description documents the final number and any UX-driven gaps below 65. A follow-up Jira ticket can close the remaining 5–8 tools if the team chooses. |
| AC6 | No regression in `clio.mcp.e2e`; CLI verbs continue to work | F | Strict |

## 3. Architectural decisions (locked)

### 3.1 MCP vs CLI separation
CLI verbs are declared via `[Verb]` on Options classes (`clio/Command/StopCommand.cs:19`, `clio/Command/RestartCommand.cs:6`). Removing `[McpServerTool]` from a tool method leaves the CLI verb intact. Therefore AC1's "kept only as CLI aliases if required" is a no-op — no CLI work needed for alias removal.

### 3.2 Argument model: discriminator + nested payload
All consolidated tools use a top-level args record with a `mode` (or `schema-type`) discriminator and a per-mode nested payload that carries the mode-specific required fields. Rationale: different modes have non-overlapping required fields (entity schema needs columns, sql schema needs sql text, restore-db needs db-server-uri vs environment-name). A flat record with all-optional fields would hide the contract from AI consumers; nested discriminated payload makes the contract explicit in the published JSON Schema.

### 3.3 Not all "by-env / by-credentials" pairs are the same
| Tool family | Pair semantics |
|---|---|
| `restart-*` | Creatio creds (url + login + password) vs env name |
| `clear-redis-db-*` | Creatio creds vs env name |
| `restore-db-*` (×3) | **DB-server** creds (db-server-uri, db-user, db-password) vs env vs local-server-config |
| `download-configuration-*` (×2) | env vs **build zip path** (no creds at all) |
| `link-from-repository-*` (×3) | env vs pkg-path vs **unlocked query** |

Implication: simple `(env OR creds)` validation works only for `restart` and `clear-redis-db`. The other three need a proper `mode` discriminator.

### 3.4 Single PR, ordered commits
All work lands in one PR. Commits are ordered by block (A → B → C → D → E → F). The budget reflection test (Block D) lands first with a ratchet so each subsequent block lowers the ceiling.

## 4. Tool inventory (105 → ~73)

### 4.1 Block A — deprecated aliases (−2)
| Removed | Kept |
|---|---|
| `StopAllCreatio` (PascalCase) at `clio/Command/McpServer/Tools/StopTool.cs:52` | `stop-all-creatio` |
| `restart-by-environmentName` (camelCase) at `clio/Command/McpServer/Tools/RestartTool.cs:33` | `restart-by-environment-name` |

Result: 105 → 103.

### 4.2 Block B — env / creds pairs (−7)
| File | Before | After |
|---|---|---|
| `RestartTool.cs` | `restart-by-environment-name`, `restart-by-credentials` (+ the legacy alias removed in Block A) | `restart-creatio` with `RestartArgs(mode: environment\|credentials, env-name?, url?, login?, password?, is-net-core?)` |
| `ClearRedisTool.cs` | `clear-redis-db-by-environment`, `clear-redis-db-by-credentials` | `clear-redis-db` with same env\|credentials discriminator |
| `RestoreDbTool.cs` | `restore-db-by-environment`, `restore-db-by-credentials`, `restore-db-to-local-server` | `restore-db` with `mode: environment\|db-credentials\|local-server` and nested mode-specific payloads |
| `DownloadConfigurationTool.cs` | `download-configuration-by-environment`, `download-configuration-by-build` | `download-configuration` with `source: environment\|build` |
| `LinkFromRepositoryTool.cs` | `link-from-repository-by-environment`, `link-from-repository-by-env-package-path`, `link-from-repository-unlocked` | `link-from-repository` with `mode: by-env\|by-pkg-path\|unlocked` |

Shared helper: a new `CommandExecutionResult.ValidateExactlyOneMode(...)` next to the existing `ValidateCredentials` at `clio/Command/McpServer/Tools/CommandExecutionResult.cs:39`.

Result: 103 → 96.

### 4.3 Block C — schema family (−11)

Current schema-related tools (21):

```
create-schema (source-code), update-schema, get-schema, delete-schema (generic),
sync-schemas, get-schema-name-prefix, generate-source-code,
create-entity-schema, update-entity-schema, create-lookup,
find-entity-schema, get-entity-schema-properties,
get-entity-schema-column-properties, modify-entity-schema-column,
create-client-unit-schema, update-client-unit-schema, get-client-unit-schema,
create-sql-schema, update-sql-schema, get-sql-schema, install-sql-schema
```

After consolidation:

| Verb (new) | Replaces | Behavior |
|---|---|---|
| `create-schema(schema-type, payload)` | `create-schema`, `create-entity-schema`, `create-lookup`, `create-client-unit-schema`, `create-sql-schema` | Dispatches to existing `*Command` classes by `schema-type` |
| `update-schema(schema-type, payload)` | `update-schema`, `update-entity-schema`, `update-client-unit-schema`, `update-sql-schema` | Same dispatch pattern |
| `get-schema(schema-type, name, ...)` | `get-schema`, `get-client-unit-schema`, `get-sql-schema`, `get-entity-schema-properties` | Returns discriminated response: `body` for source-code/client-unit/sql, `properties` for entity |
| `delete-schema` | unchanged (already generic) | Kept |
| `list-schemas(schema-type, filter?)` | `find-entity-schema` (and adds listing for other types) | New |

Extensions kept (justified):
- `modify-entity-schema-column` — column-level mutation, distinct from schema-level update
- `get-entity-schema-column-properties` — folded into `get-schema(schema-type=entity, column?)`
- `install-sql-schema` — separate install flow (creates a DB object, not a metadata schema)
- `sync-schemas` — workspace-level pull, not per-schema
- `get-schema-name-prefix` — utility, no CRUD analog
- `generate-source-code` — code generation, not CRUD

`schema-type` enum: `source-code | entity | client-unit | sql | lookup | process`.

Implementation pattern: each consolidated verb is one new tool class (`SchemaCreateTool`, `SchemaUpdateTool`, `SchemaGetTool`, `SchemaListTool`). Inside each — a `switch` over `schema-type`, delegating to the existing `*Command` instances via `ResolveCommand<T>(options)`. No domain rewrite.

Old type-specific tool classes lose their `[McpServerTool]` attributes; the CLI verbs they back stay registered via `[Verb]`.

Result: 96 → 85 (schema family 21 → 10; net −11). The `get-entity-schema-column-properties` fold listed in §4.4 is technically a Block D commit but builds on the `get-schema(column?)` parameter introduced in Block C.

### 4.4 Block D — per-resource consolidation (full scope, −12)

The plan applies both clean read/list mergers and action-based mergers in this PR:

#### Clean mergers (low UX risk, −4)
| Replacement | Saving |
|---|---|
| `get-sys-setting` + `list-sys-settings` → `sys-setting(name?)` (empty name = list) | −1 |
| `list-apps` + `get-app-info` → `apps(name?)` (empty name = list) | −1 |
| `dataforge-find-tables` + `dataforge-find-lookups` → `dataforge-find(kind: tables\|lookups)` | −1 |
| `get-entity-schema-column-properties` folded into `get-schema(schema-type=entity, column?)` | −1 |

#### Action-based mergers (medium UX risk, −8)
| Replacement | Saving |
|---|---|
| `create-sys-setting` + `update-sys-setting` → `upsert-sys-setting` | −1 |
| `add-data-binding-row` + `remove-data-binding-row` → `data-binding-row(action: add\|remove)` | −1 |
| `upsert-data-binding-row-db` + `remove-data-binding-row-db` → `data-binding-row-db(action: upsert\|remove)` | −1 |
| `create-app-section` + `update-app-section` + `delete-app-section` + `list-app-sections` → `app-section(action: create\|update\|delete\|list)` | −3 |
| `unlock-for-hotfix` + `finish-hotfix` → `hotfix(action: unlock\|finish)` | −1 |
| `pkg-to-db` + `pkg-to-file-system` → `pkg-mode(target: db\|file-system)` | −1 |

Not consolidated (UX cost not worth it):
- `restore-workspace` vs `create-workspace` — distinct semantics
- `start-creatio` / `stop-creatio` / `stop-all-creatio` — start and stop are not symmetric; `all` flag would muddy the contract
- DataForge `initialize` / `update` / `context` / `status` — distinct lifecycle states

### 4.5 Final count projection

| Block | Saving | Running count |
|---|---|---|
| Baseline | — | 105 |
| Block A | −2 | 103 |
| Block B | −7 | 96 |
| Block C | −11 | 85 |
| Block D (clean + action-based) | −12 | **73** |

Expected final: **~70–73**. CI ratchet locked at `count ≤ 75` to allow ±2 wiggle for late discoveries during implementation. If we land closer to 70, the ratchet tightens accordingly before merge.

## 5. Implementation order (commits inside the single PR)

1. **Ratchet test (first commit)** — introduce `clio.tests/Command/McpServer/McpToolBudgetTests.cs` using reflection over `McpServerToolAttribute` (pattern from `clio.tests/Command/McpServer/RestoreDbToolTests.cs:233`). Initial assertion: `count ≤ 105`. Every subsequent block tightens the ratchet.
2. **Block A** — remove the two deprecated aliases; lower ratchet to 103.
3. **Block B** — consolidate the five env/creds families; lower ratchet to 96.
4. **Block C** — consolidate schema family; lower ratchet to 85.
5. **Block D** — per-resource consolidation (clean + action-based per §4.4); lower ratchet to 73 or the actual achieved number, whichever is lower.
6. **Block E** — update `clio/Command/McpServer/AGENTS.md` with the budget policy (128 hard, 60 soft target, "extend with mode/discriminator before adding a new tool", "deprecation = remove, no aliases"). Lock the ratchet at the final number plus a small wiggle (`≤ final + 2`).
7. **Block F** — sync unit tests and `clio.mcp.e2e` tests with new tool names and arg shapes; full green run.
8. **Block G** — add a "Breaking changes — MCP tool consolidation" section to `RELEASE.md` with the full old-name → new-name migration table and one example payload per consolidated tool. The same table is duplicated in the PR description body for review-time visibility.

## 6. Files touched (expected)

### Production code
- `clio/Command/McpServer/Tools/StopTool.cs`
- `clio/Command/McpServer/Tools/RestartTool.cs`
- `clio/Command/McpServer/Tools/ClearRedisTool.cs`
- `clio/Command/McpServer/Tools/RestoreDbTool.cs`
- `clio/Command/McpServer/Tools/DownloadConfigurationTool.cs`
- `clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs`
- `clio/Command/McpServer/Tools/SchemaCreateTool.cs`, `SchemaUpdateTool.cs`, `GetSchemaTool.cs`, `DeleteSchemaTool.cs`, `EntitySchemaTool.cs`
- `clio/Command/McpServer/Tools/ClientUnitSchemaCreateTool.cs`, `ClientUnitSchemaUpdateTool.cs`, `GetClientUnitSchemaTool.cs`
- `clio/Command/McpServer/Tools/SqlSchemaCreateTool.cs`, `SqlSchemaUpdateTool.cs`, `SqlSchemaGetTool.cs`
- `clio/Command/McpServer/Tools/SysSettingsTool.cs`, `ApplicationTool.cs`, `DataForgeTool.cs`, `DataBindingTool.cs`, `DataBindingDbTool.cs` (Block D)
- `clio/Command/McpServer/Tools/CommandExecutionResult.cs` (new `ValidateExactlyOneMode` helper)
- `clio/Command/McpServer/AGENTS.md`

### Documentation
- `RELEASE.md` — new "Breaking changes — MCP tool consolidation" section with migration table

### Tests
- `clio.tests/Command/McpServer/McpToolBudgetTests.cs` (new)
- `clio.tests/Command/McpServer/*Tests.cs` — update arg signatures
- `clio.mcp.e2e/*E2ETests.cs` — update tool names and payloads

### Out of scope
- DataForge initialize/update flows (per Jira)
- Hotfix and skills feature areas (per Jira)
- CLI verbs themselves (per Jira)
- MCP resources and prompts (per Jira)

## 7. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Breaking change in the MCP contract for already-integrated AI clients (Cursor, Claude Desktop, OpenAI agents) | Migration table in `RELEASE.md` ("Breaking changes" section, Block G) plus PR description, mapping every old tool name to its new replacement and one example payload. Old CLI verbs continue to work, so users can fall back to CLI invocations during migration. |
| Discriminator validation produces unhelpful errors for AI consumers | One shared `ValidateExactlyOneMode` helper with explicit messages ("schema-type=entity requires `columns` payload"). |
| Schema family has the largest diff and the highest test-blast | Order commits per verb (create → update → get → list). Run e2e per commit, not only at the end. |
| Block D needs Block C to land first (`get-entity-schema-column-properties` fold) | Keep Block D after Block C in commit order; the ratchet test catches accidental reorderings. |
| Final count stays above 75 after Block A–D | If discovered during implementation, PR description proposes one of: (a) drop one action-based merger whose UX cost is highest and accept the higher count, (b) extend Block D with one of the "not consolidated" items from §4.4. Decision is surfaced to reviewers, not buried. |

### Non-risk: clio-satellite
The Chrome extension at [Advance-Technologies-Foundation/clio-satellite](https://github.com/Advance-Technologies-Foundation/clio-satellite) calls Creatio HTTP services directly (`/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain`, `/ServiceModel/AppInstallerService.svc/ClearRedisDb`) and does **not** invoke clio CLI or clio MCP. Verified by code search — zero references to any consolidated tool name. The consolidation does not affect it.

## 8. Definition of done

- `McpToolBudgetTests` passes with `count ≤ final + 2` (target final = 70–73).
- All `clio.tests` and `clio.mcp.e2e` pass.
- `clio/Command/McpServer/AGENTS.md` contains the new budget-policy section (128 hard, 60 soft, "extend before add", "deprecation = remove, no aliases", link to the ratchet test).
- `RELEASE.md` contains a "Breaking changes — MCP tool consolidation" section with the full migration table and an example payload per consolidated tool.
- PR description duplicates the migration table and reports the final tool count plus an explanation if the count is above 65.
- Manual smoke: `clio mcp-server` started locally; `tools/list` JSON-RPC reply matches the final count; one tool from each consolidated family invoked successfully end-to-end.
- A follow-up Jira ticket is filed if the final count is above 65, listing the remaining consolidation candidates that were skipped for UX reasons.
