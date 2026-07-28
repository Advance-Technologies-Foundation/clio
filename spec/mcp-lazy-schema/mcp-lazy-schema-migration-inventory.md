# MCP lazy-schema — migration & consumer inventory

**Feature**: mcp-lazy-schema
**Story**: [story-mcp-lazy-schema-9.md](../stories/story-mcp-lazy-schema-9.md)
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Type**: documents-only (no production code)
**Data captured from**: spike branch `spike/mcp-lazy-schema` @ `f594e913`
**Status**: this is evidence for Stories 1 / 6 / 7 / 10 / 11. The core-vs-long-tail
split and the alias targets below are **proposals**; Story 7 finalises the core set
and Story 1's profile config is the source of truth for membership.

> All counts are extracted by parsing `[McpServerTool(...)]` attributes (constant
> names resolved per declaring class), the `ToolContractCatalog.Contracts` dictionary,
> and grepping the three consumer repos. The method is reproducible — see
> *Appendix: method*.

---

## 0. Headline numbers

| Metric | Value |
|---|--:|
| Total registered MCP tools (`[McpServerTool(Name=…)]`, unique names) | **126** |
| ReadOnly = true | 39 |
| Destructive = true | 62 |
| Neither (non-destructive writes / local-effect) | 25 |
| Proposed flat **core** set (Story 7 to finalise) | **20** |
| **Long-tail** (behind `clio-run` / `clio-run-destructive`) | **106** |
| Long-tail routed to `clio-run` (safe) | 44 |
| Long-tail routed to `clio-run-destructive` | 62 |
| Curated contracts in `ToolContractCatalog.Contracts` | **61** |
| Curated contracts returned by default (`CanonicalToolNames`) | 47 |
| Tools with **no** curated contract (Story 6 gap, excl. the 2 executors) | **63** |
| Long-tail tool names referenced by ≥1 external consumer / e2e | **76** |
| Deprecation-alias rows to add (long-tail, excl. executors) | **104** (75 consumer-backed) |

The 126 includes the 2 executors themselves (`clio-run`, `clio-run-destructive`) and the
discovery tools (`get-tool-contract`, `get-guidance`) that already shipped on this branch.

---

## 1. Full MCP tool catalog (×126)

Legend: **RO** = ReadOnly, **Destr** = Destructive, **Idemp** = Idempotent
(`Y` = true, `·` = false). **Tier** = proposed core vs long-tail (§2).
**Curated** = present in `ToolContractCatalog.Contracts`. **Consumer** = referenced
by CAADT / adaclio / clio e2e (§4).

All tools live in `clio/Command/McpServer/Tools/`.

| # | Tool name | RO | Destr | Idemp | Class | Tier | Curated | Consumer |
|---|---|:--:|:--:|:--:|---|---|:--:|:--:|
| 1 | `StopAllCreatio` | · | Y | Y | StopTool | long-tail | — | — |
| 2 | `add-data-binding-row` | · | Y | · | AddDataBindingRowTool | long-tail | Y | Y |
| 3 | `add-item-model` | · | Y | · | AddItemModelTool | long-tail | — | Y |
| 4 | `add-package` | · | · | · | WorkspacePackageTool | long-tail | — | Y |
| 5 | `add-package-dependency` | · | · | Y | AddPackageDependencyTool | long-tail | — | Y |
| 6 | `assert-infrastructure` | Y | · | Y | AssertInfrastructureTool | long-tail | Y | Y |
| 7 | `check-auth-code-flow` | Y | · | Y | CheckAuthCodeFlowTool | long-tail | — | Y |
| 8 | `check-settings-health` | Y | · | Y | SettingsHealthTool | long-tail | Y | Y |
| 9 | `clear-browser-session` | · | Y | Y | ClearBrowserSessionTool | long-tail | — | Y |
| 10 | `clear-redis-db-by-credentials` | · | Y | · | ClearRedisTool | long-tail | — | — |
| 11 | `clear-redis-db-by-environment` | · | Y | · | ClearRedisTool | long-tail | — | — |
| 12 | `clio-run` | · | · | · | ClioRunTool | (executor) | — | Y |
| 13 | `clio-run-destructive` | · | Y | · | ClioRunDestructiveTool | (executor) | — | — |
| 14 | `compile-creatio` | · | Y | · | CompileCreatioTool | long-tail | Y | Y |
| 15 | `create-app` | · | Y | · | ApplicationCreateTool | long-tail | Y | Y |
| 16 | `create-app-section` | · | Y | · | ApplicationSectionCreateTool | long-tail | Y | Y |
| 17 | `create-client-unit-schema` | · | · | · | ClientUnitSchemaCreateTool | long-tail | — | Y |
| 18 | `create-data-binding` | · | Y | · | CreateDataBindingTool | long-tail | Y | Y |
| 19 | `create-data-binding-db` | · | Y | · | CreateDataBindingDbTool | long-tail | Y | Y |
| 20 | `create-entity-business-rule` | · | Y | · | CreateEntityBusinessRuleTool | long-tail | Y | Y |
| 21 | `create-entity-schema` | · | Y | · | CreateEntitySchemaTool | long-tail | Y | Y |
| 22 | `create-lookup` | · | Y | · | CreateLookupTool | long-tail | Y | Y |
| 23 | `create-page` | · | Y | · | PageCreateTool | long-tail | — | Y |
| 24 | `create-page-business-rule` | · | Y | · | CreatePageBusinessRuleTool | long-tail | Y | Y |
| 25 | `create-schema` | · | · | · | SchemaCreateTool | long-tail | — | Y |
| 26 | `create-sql-schema` | · | · | · | SqlSchemaCreateTool | long-tail | — | Y |
| 27 | `create-sys-setting` | · | Y | · | SysSettingCreateTool | long-tail | Y | Y |
| 28 | `create-user-task` | · | · | · | CreateUserTaskTool | long-tail | — | — |
| 29 | `create-workspace` | · | · | · | CreateWorkspaceTool | long-tail | — | Y |
| 30 | `dataforge-context` | Y | · | Y | DataForgeTool | long-tail | Y | Y |
| 31 | `dataforge-find-lookups` | Y | · | Y | DataForgeTool | CORE | Y | Y |
| 32 | `dataforge-find-tables` | Y | · | Y | DataForgeTool | CORE | Y | Y |
| 33 | `dataforge-get-relations` | Y | · | Y | DataForgeTool | long-tail | Y | Y |
| 34 | `dataforge-get-table-columns` | Y | · | Y | DataForgeTool | long-tail | Y | Y |
| 35 | `dataforge-initialize` | · | Y | · | DataForgeTool | long-tail | Y | Y |
| 36 | `dataforge-status` | Y | · | Y | DataForgeTool | CORE | Y | Y |
| 37 | `dataforge-update` | · | Y | · | DataForgeTool | long-tail | Y | Y |
| 38 | `delete-adac` | · | Y | Y | DeleteSkillTool | long-tail | — | — |
| 39 | `delete-app` | · | Y | Y | ApplicationDeleteTool | long-tail | Y | Y |
| 40 | `delete-app-section` | · | Y | · | ApplicationSectionDeleteTool | long-tail | Y | Y |
| 41 | `delete-schema` | · | Y | · | DeleteSchemaTool | long-tail | — | Y |
| 42 | `deploy-creatio` | · | Y | · | InstallerCommandTool | long-tail | Y | Y |
| 43 | `download-configuration-by-build` | · | · | · | DownloadConfigurationTool | long-tail | — | Y |
| 44 | `download-configuration-by-environment` | · | · | · | DownloadConfigurationTool | long-tail | — | Y |
| 45 | `execute-esq` | Y | · | Y | ExecuteEsqTool | long-tail | Y | Y |
| 46 | `experimental` | · | · | Y | ExperimentalTool | long-tail | — | Y |
| 47 | `find-app` | Y | · | Y | FindAppTool | CORE | — | Y |
| 48 | `find-empty-iis-port` | Y | · | Y | FindEmptyIisPortTool | long-tail | Y | Y |
| 49 | `find-entity-schema` | Y | · | Y | FindEntitySchemaTool | CORE | Y | Y |
| 50 | `finish-hotfix` | · | · | · | PackageHotfixTool | long-tail | — | Y |
| 51 | `generate-process-model` | · | Y | · | GenerateProcessModelTool | long-tail | — | Y |
| 52 | `generate-source-code` | · | · | Y | GenerateSourceCodeTool | long-tail | — | — |
| 53 | `get-app-info` | Y | · | Y | ApplicationGetInfoTool | CORE | Y | Y |
| 54 | `get-browser-session` | · | · | · | GetBrowserSessionTool | long-tail | — | Y |
| 55 | `get-client-unit-schema` | Y | · | Y | GetClientUnitSchemaTool | long-tail | — | Y |
| 56 | `get-component-info` | Y | · | Y | ComponentInfoTool | CORE | Y | Y |
| 57 | `get-entity-schema-column-properties` | Y | · | Y | GetEntitySchemaColumnPropertiesTool | CORE | Y | Y |
| 58 | `get-entity-schema-properties` | Y | · | Y | GetEntitySchemaPropertiesTool | CORE | Y | Y |
| 59 | `get-fsm-mode` | Y | · | Y | FsmModeTool | long-tail | — | Y |
| 60 | `get-guidance` | Y | · | Y | GuidanceGetTool | CORE | Y | Y |
| 61 | `get-identity-assertion` | · | · | · | GetIdentityAssertionTool | long-tail | — | — |
| 62 | `get-identity-public-jwk` | Y | · | Y | GetIdentityPublicJwkTool | long-tail | — | — |
| 63 | `get-page` | · | · | Y | PageGetTool | CORE | Y | Y |
| 64 | `get-process-signature` | Y | · | Y | GetProcessSignatureTool | long-tail | — | Y |
| 65 | `get-schema` | Y | · | Y | GetSchemaTool | long-tail | — | Y |
| 66 | `get-schema-name-prefix` | Y | · | Y | SchemaNamePrefixTool | long-tail | Y | Y |
| 67 | `get-sql-schema` | Y | · | Y | SqlSchemaGetTool | long-tail | — | Y |
| 68 | `get-sys-setting` | Y | · | Y | SysSettingGetTool | CORE | Y | Y |
| 69 | `get-tool-contract` | Y | · | Y | ToolContractGetTool | CORE | Y | Y |
| 70 | `get-user-culture` | Y | · | Y | GetUserCultureTool | long-tail | — | Y |
| 71 | `install-adac` | · | · | Y | InstallSkillsTool | long-tail | — | — |
| 72 | `install-application` | · | Y | · | InstallApplicationTool | long-tail | — | Y |
| 73 | `install-gate` | · | · | Y | InstallGateTool | long-tail | Y | Y |
| 74 | `install-sql-schema` | · | Y | · | SqlSchemaInstallTool | long-tail | — | Y |
| 75 | `link-from-repository-by-env-package-path` | · | Y | · | LinkFromRepositoryTool | long-tail | — | — |
| 76 | `link-from-repository-by-environment` | · | Y | · | LinkFromRepositoryTool | long-tail | — | — |
| 77 | `link-from-repository-unlocked` | · | Y | · | LinkFromRepositoryTool | long-tail | — | — |
| 78 | `list-app-sections` | Y | · | Y | ApplicationSectionGetListTool | CORE | Y | Y |
| 79 | `list-apps` | Y | · | Y | ApplicationGetListTool | CORE | Y | Y |
| 80 | `list-creatio-builds` | Y | · | Y | ListCreatioBuildsTool | long-tail | Y | — |
| 81 | `list-environments` | Y | · | Y | ShowWebAppListTool | CORE | — | Y |
| 82 | `list-packages` | Y | · | Y | GetPkgListTool | CORE | — | Y |
| 83 | `list-page-templates` | Y | · | Y | PageTemplatesListTool | long-tail | — | Y |
| 84 | `list-pages` | Y | · | Y | PageListTool | CORE | Y | Y |
| 85 | `list-sys-settings` | Y | · | Y | SysSettingsListTool | CORE | Y | Y |
| 86 | `modify-entity-schema-column` | · | Y | · | ModifyEntitySchemaColumnTool | long-tail | Y | Y |
| 87 | `modify-user-task-parameters` | · | Y | · | ModifyUserTaskParametersTool | long-tail | — | — |
| 88 | `new-test-project` | · | · | · | CreateTestProjectTool | long-tail | — | — |
| 89 | `new-ui-project` | · | · | · | CreateUiProjectTool | long-tail | Y | — |
| 90 | `odata-create` | · | · | · | ODataCreateTool | long-tail | Y | Y |
| 91 | `odata-delete` | · | Y | Y | ODataDeleteTool | long-tail | Y | Y |
| 92 | `odata-read` | Y | · | Y | ODataReadTool | long-tail | Y | Y |
| 93 | `odata-update` | · | Y | Y | ODataUpdateTool | long-tail | Y | Y |
| 94 | `pkg-to-db` | · | Y | · | LoadPackagesTool | long-tail | — | — |
| 95 | `pkg-to-file-system` | · | Y | · | LoadPackagesTool | long-tail | — | — |
| 96 | `push-workspace` | · | Y | · | PushWorkspaceTool | long-tail | Y | Y |
| 97 | `reg-web-app` | · | · | · | RegWebAppTool | long-tail | — | Y |
| 98 | `regenerate-identity-signing-key` | · | Y | · | RegenerateIdentitySigningKeyTool | long-tail | — | — |
| 99 | `remove-data-binding-row` | · | Y | · | RemoveDataBindingRowTool | long-tail | Y | Y |
| 100 | `remove-data-binding-row-db` | · | Y | · | RemoveDataBindingRowDbTool | long-tail | Y | Y |
| 101 | `restart-by-credentials` | · | Y | · | RestartTool | long-tail | — | — |
| 102 | `restart-by-environment-name` | · | Y | · | RestartTool | long-tail | — | — |
| 103 | `restart-by-environmentName` | · | Y | · | RestartTool | long-tail | — | — |
| 104 | `restore-db-by-credentials` | · | Y | · | RestoreDbTool | long-tail | — | — |
| 105 | `restore-db-by-environment` | · | Y | · | RestoreDbTool | long-tail | — | — |
| 106 | `restore-db-to-local-server` | · | Y | · | RestoreDbTool | long-tail | — | Y |
| 107 | `restore-workspace` | · | Y | · | RestoreWorkspaceTool | long-tail | Y | Y |
| 108 | `set-fsm-mode` | · | Y | · | FsmModeTool | long-tail | — | Y |
| 109 | `show-passing-infrastructure` | Y | · | Y | ShowPassingInfrastructureTool | long-tail | Y | Y |
| 110 | `start-creatio` | · | · | Y | StartTool | long-tail | — | — |
| 111 | `stop-all-creatio` | · | Y | Y | StopTool | long-tail | — | — |
| 112 | `stop-creatio` | · | Y | Y | StopTool | long-tail | — | — |
| 113 | `sync-pages` | · | Y | · | PageSyncTool | long-tail | Y | Y |
| 114 | `sync-schemas` | · | Y | · | SchemaSyncTool | long-tail | Y | Y |
| 115 | `uninstall-creatio` | · | Y | · | UninstallCreatioTool | long-tail | — | — |
| 116 | `unlock-for-hotfix` | · | · | Y | PackageHotfixTool | long-tail | — | Y |
| 117 | `update-adac` | · | · | Y | UpdateSkillTool | long-tail | — | — |
| 118 | `update-app-section` | · | Y | · | ApplicationSectionUpdateTool | long-tail | Y | Y |
| 119 | `update-client-unit-schema` | · | Y | · | ClientUnitSchemaUpdateTool | long-tail | — | Y |
| 120 | `update-entity-schema` | · | Y | · | UpdateEntitySchemaTool | long-tail | Y | Y |
| 121 | `update-page` | · | Y | · | PageUpdateTool | long-tail | Y | Y |
| 122 | `update-schema` | · | Y | · | SchemaUpdateTool | long-tail | — | Y |
| 123 | `update-sql-schema` | · | Y | · | SqlSchemaUpdateTool | long-tail | — | Y |
| 124 | `update-sys-setting` | · | Y | Y | SysSettingUpdateTool | long-tail | Y | Y |
| 125 | `upsert-data-binding-row-db` | · | Y | · | UpsertDataBindingRowDbTool | long-tail | Y | Y |
| 126 | `validate-page` | Y | · | Y | PageValidateTool | CORE | Y | Y |

### Notable data points

- **Multi-tool classes** (one `*Tool.cs` declares several `[McpServerTool]`): `DataForgeTool`
  (8), `ApplicationTool` (7 — create/delete/get/list/section CRUD), `EntitySchemaTool` (7),
  `SysSettingsTool` (4), `IdentityAssertionTool` (4), `RestoreDbTool` (3),
  `LinkFromRepositoryTool` (3), `RestartTool` (3), `StopTool` (3), `DataBindingTool` /
  `DataBindingDbTool` (3 each), `SkillManagementTool` (3), `PackageHotfixTool` (2),
  `LoadPackagesTool` (2), `ClearRedisTool` (2), `DownloadConfigurationTool` (2),
  `WorkspaceSyncTool` (2), `WorkspacePackageTool` (2), `UserTaskTool` (2), `BusinessRuleTool` (2).
  Migration touches these classes once but moves N tool registrations.
- **camelCase / PascalCase legacy names already shipped** (do not "fix" silently — they are
  back-compat surfaces, treat as aliases): `restart-by-environmentName` (#103),
  `StopAllCreatio` (#1).
- **`get-page` is the only non-ReadOnly tool proposed for core** — it is `Destructive=false,
  Idempotent=true` (a read that may write a `.clio-pages` file locally). It is the read-path
  page tool consumers expect flat; keep it in core but it is not auto-approve-as-readonly.

---

## 2. Proposed core (flat) set vs long-tail — **PROPOSAL for Story 7**

Selection rule applied: `ReadOnly = true` **and** high-frequency discovery / list / get /
find / status (the "discover → describe" entry path that must stay zero round-trip), plus
`get-page` as the canonical page-read tool. Everything else is long-tail.

### Proposed core (20)

| Tool | Why core |
|---|---|
| `list-apps` | app discovery entry point |
| `get-app-info` | app inspection (high-freq in every workflow) |
| `find-app` | app lookup |
| `list-app-sections` | section discovery |
| `list-pages` | page discovery |
| `get-page` | page read (non-destructive; only non-RO core member) |
| `list-packages` | package discovery |
| `list-environments` | environment discovery |
| `get-component-info` | Freedom UI component lookup (ENG-89871 hot path) |
| `find-entity-schema` | entity lookup |
| `get-entity-schema-properties` | entity inspection |
| `get-entity-schema-column-properties` | column inspection |
| `dataforge-find-tables` | Data Forge discovery |
| `dataforge-find-lookups` | Data Forge discovery |
| `dataforge-status` | Data Forge readiness check |
| `get-guidance` | guidance index (replaces heavy descriptions) |
| `get-tool-contract` | the lazy-schema describe tool itself |
| `get-sys-setting` | settings read |
| `list-sys-settings` | settings discovery |
| `validate-page` | page validation (read-only, pre-write gate) |

Plus the two executors `clio-run` / `clio-run-destructive` are always in `tools/list`
(they are not "core commands" — they are the long-tail entry points).

### Long-tail (106) — safe vs destructive routing

Routing is taken **directly from each tool's `Destructive` flag**:
`Destructive=false` → `clio-run` (safe surface), `Destructive=true` → `clio-run-destructive`.

| Routing | Count | Examples |
|---|--:|---|
| `clio-run` (safe) | 44 | `execute-esq`, `odata-read`, `odata-create`, `dataforge-context`, `get-schema`, `add-package`, `add-package-dependency`, `create-workspace`, `assert-infrastructure`, `check-settings-health`, `find-empty-iis-port`, `list-creatio-builds`, `reg-web-app`, `get-process-signature`, `install-gate` … |
| `clio-run-destructive` | 62 | `update-page`, `sync-schemas`, `sync-pages`, `create-app`, `create-app-section`, `create-entity-schema`, `modify-entity-schema-column`, `delete-app`, `deploy-creatio`, `compile-creatio`, `install-application`, `restore-workspace`, `push-workspace`, `odata-update`, `odata-delete`, `update-sys-setting` … |

> **Security note from ADR §Security:** `clio-run` must never be `ReadOnly`/auto-approve and
> `clio-run-destructive` aggregates all 62 destructive verbs behind one surface. A host that
> blanket-allows `clio-run-destructive` thereby allows `delete-app`, `delete-schema`,
> `uninstall-creatio`, etc. without per-tool prompts. This split is the agreed mitigation
> (ADR resolved decision 3).

The complete long-tail list with routing is the **alias table in §5** (one row per long-tail tool).

---

## 3. Per-command artifact matrix (the 8 maintenance artifacts ×N)

Per AGENTS.md, every touched command must keep 8 artifact types aligned. This section
quantifies how much surface area the migration (Story 11) realistically touches.

### Repo-wide artifact inventory (denominators)

| Artifact type | Location | File count (today) |
|---|---|--:|
| MCP **tool** files (`*Tool.cs`, multi-tool) | `clio/Command/McpServer/Tools/` | 78 files → **126 tools** |
| MCP **prompt** classes | `clio/Command/McpServer/Prompts/*Prompt.cs` | 28 |
| MCP **resource** classes | `clio/Command/McpServer/Resources/*Resource*.cs` | 33 |
| `clio.tests` MCP **unit** test files | `clio.tests/Command/McpServer/*.cs` | 111 |
| `clio.mcp.e2e` **e2e** test files | `clio.mcp.e2e/*.cs` | 84 |
| CLI **help** (`-H`) | `clio/help/en/*.txt` | 142 |
| **docs** (GitHub) | `clio/docs/commands/*.md` | 182 |
| **Commands.md** index | `clio/Commands.md` | 1 (71 long-tail tool names appear inside) |
| **McpCapabilityMap** | `docs/McpCapabilityMap.md` | 1 |

### Artifact presence for the long-tail (104 non-executor long-tail tools)

| Artifact | How many long-tail tools have it | Source of count |
|---|--:|---|
| MCP tool registration | 104 / 104 (by definition) | §1 |
| MCP prompt references the tool name | ≥24 distinct long-tail tools across **19** prompt files | grep `Prompts/*.cs` |
| MCP resource references the tool name | (subset) across **28** resource files | grep `Resources/*.cs` |
| `clio.tests` unit coverage | partial — many multi-tool classes share one test file | `clio.tests/Command/McpServer` (111 files) |
| `clio.mcp.e2e` coverage | partial — e2e exists for most high-traffic verbs (84 files) | `clio.mcp.e2e` |
| `help/en/<verb>.txt` | **33** long-tail tools have an exact-name help file | exact basename match |
| `docs/commands/<verb>.md` | **40** long-tail tools have an exact-name doc | exact basename match |
| `Commands.md` entry | 71 long-tail tool names appear in the index | grep |

> The help/docs counts use **exact tool-name = verb** matching and therefore **undercount**:
> several MCP tool names are not 1:1 with a CLI verb (e.g. `sync-schemas` ↔ a different verb,
> `odata-*` map to one OData command family, `restart-by-*` are three MCP facets of one
> `restart` verb). Story 11 must resolve each MCP tool name to its **canonical verb**
> (`[Verb("…", Aliases=…)]`) before editing help/docs, per AGENTS.md "Resolve aliases".

### Representative sample (10 high-traffic long-tail commands × 8 artifacts)

`T`=MCP tool, `P`=MCP prompt, `R`=MCP resource, `U`=clio.tests unit, `E`=clio.mcp.e2e,
`H`=help/en, `D`=docs/commands, `C`=Commands.md. `Y`=present, `~`=present via family/shared
file (not 1:1), `—`=absent / not found by exact name.

| Command | T | P | R | U | E | H | D | C |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `update-page` | Y | Y (PagePrompt) | Y (PageModification…) | Y | Y (PageUpdateTool) | ~ | ~ | Y |
| `sync-schemas` | Y | ~ | Y | Y | Y (SchemaSyncTool) | — | — | Y |
| `sync-pages` | Y | Y (WorkspaceSync…) | Y | Y | Y (PageSyncTool) | — | — | Y |
| `create-app` | Y | Y (ApplicationPrompt) | Y (AppModeling…) | Y | Y (ApplicationTool) | Y | Y | Y |
| `create-app-section` | Y | Y (ApplicationPrompt) | Y | Y | Y (ApplicationSection…) | Y | Y | Y |
| `create-entity-schema` | Y | Y (EntitySchemaPrompt) | Y (Esq…) | Y | Y (EntitySchemaTool) | Y | Y | Y |
| `modify-entity-schema-column` | Y | Y (EntitySchemaPrompt) | Y | Y | Y (EntitySchemaTool) | ~ | ~ | Y |
| `create-data-binding-db` | Y | Y (DataBindingDbPrompt) | Y (DataBindings…) | Y | Y (DataBindingDbTool) | Y | Y | Y |
| `deploy-creatio` | Y | Y (DeployCreatioPrompt) | Y (DeployLifecycle…) | Y | Y (DeployCreatioTool) | ~ | ~ | Y |
| `reg-web-app` | Y | Y (RegWebAppPrompt) | — | Y | Y (RegWebAppTool) | Y | Y | Y |

### Story 11 migration-volume estimate

Realistic touch count per moved long-tail command (per AGENTS.md mandatory targets):

- 1 tool-class edit (some classes move several tools at once → fewer file edits than tools)
- 0–1 prompt edit (only the ~19 prompt files that name the tool)
- 0–1 resource edit (only the ~28 resource files that name the tool)
- 1 unit test fixture update (the e2e/unit invoke by `*Tool.ToolName`; the **registration**
  surface change — moving a tool behind `clio-run` — breaks **every** test that does
  `CallToolAsync("<flat-name>")`; see §4)
- 1 e2e update for covered verbs (84 files exist; high-traffic verbs all covered)
- 1 help, 1 docs, 1 Commands.md line, 1 McpCapabilityMap line per **canonical verb**

**Order-of-magnitude:** moving 106 long-tail tools across the 8 artifact classes touches on
the order of **300–400 file edits**, dominated by (a) the 84 e2e files + 111 unit files that
bind to flat tool names and (b) the 142 help + 182 docs + Commands.md/McpCapabilityMap doc set.
The single biggest mechanical risk is the **e2e/unit `CallToolAsync("<flat-name>")` bindings**
(§4) — they fail the moment a tool leaves `tools/list`, regardless of whether an alias exists,
unless the alias is itself registered as a thin flat proxy.

---

## 4. Consumers of flat tool names (breaking-change surface) — **AC-02**

How tool names are referenced determines breakage severity:

- **adaclio** allow-lists clio by **prefix** (`--allowedTools mcp__clio`) — it does **not**
  pin individual `mcp__clio__<name>` calls, so the orchestrator's allow-list survives the
  move. BUT its **prompts / runbooks / preconditions** name bare tool names as instructions
  to the agent, and its **python tests** assert on those names.
- **CAADT** references bare names in its MCP client (`mcp_client.py`), its **tests**
  (`test_mcp_client.py`, `test_default_contract_docs.py`), and its context/runbook docs.
- **clio e2e / unit** invoke tools by `*Tool.ToolName` via `Session.CallToolAsync(name, …)`.
  These are **hard bindings** — a moved tool that disappears from `tools/list` fails the
  call unless a flat alias proxy is still registered.

### Distinct tools referenced, by repo (most-referenced first)

**clio.mcp.e2e** (`/Users/a-kravchuk/Projects/clio-mcp-spike/clio.mcp.e2e/`) — hard bindings:

| Tool | refs | Tier |
|---|--:|---|
| `update-page` | 122 | long-tail (destr) |
| `get-page` | 79 | CORE |
| `sync-schemas` | 73 | long-tail (destr) |
| `sync-pages` | 69 | long-tail (destr) |
| `create-app` | 44 | long-tail (destr) |
| `create-app-section` | 41 | long-tail (destr) |
| `create-workspace` | 40 | long-tail (safe) |
| `create-lookup` | 39 | long-tail (destr) |
| `get-app-info` | 38 | CORE |
| `validate-page` | 30 | CORE |
| `list-apps` | 30 | CORE |
| `update-app-section` / `modify-entity-schema-column` / `list-packages` / `list-app-sections` | 28 each | mixed |
| `get-component-info` | 23 | CORE |
| `delete-app-section` / `create-data-binding-db` | 22 each | long-tail (destr) |
| `find-app` | 21 | CORE |
| `install-application` / `create-entity-business-rule` | 20 each | long-tail (destr) |
| … (≈50 more tools referenced, see §1 `Consumer=Y`) | | |

**CAADT** (`/Users/a-kravchuk/Projects/creatio-ai-app-development-toolkit/`):

| File | Top tool refs |
|---|---|
| `runtime/scripts/mcp_client.py` | `get-tool-contract` ×9 (real discovery binding), `create-app`, `update-page`, `sync-schemas`, `create-lookup` |
| `tests/test_mcp_client.py` | `sync-schemas` ×7, `list-pages` ×7, `sync-pages` ×4, `list-apps` ×4, `create-data-binding-db` ×2, `create-app` ×2, `get-tool-contract` ×3, `create-lookup` |
| `tests/test_default_contract_docs.py` | `get-tool-contract`, `get-page`, `sync-schemas`, `sync-pages`, `list-pages`, `get-app-info`, `create-app` |
| `context/clio-cli-reference.md`, `context/essentials.md`, `context/naming-conventions.md`, `context/model-discovery-evidence.md`, `context/INDEX.md` | doc references to many tool names |
| `runbooks/01-environment-setup.md`, `runbooks/02-requirements-gathering.md` | workflow references (`reg-web-app`, `create-app`, …) |
| `skills/creatio-app-orchestrator/SKILL.md`, `AGENTS.md`, `README.md`, `RELEASE-NOTES.md`, `installer/install.py` | scattered references |

**adaclio** (`/Users/a-kravchuk/Projects/creatio-adaclio-testing/`):

| File | Top tool refs |
|---|---|
| `scripts/lib/orchestrator.py` | `--allowedTools mcp__clio` prefix (**not** per-tool); robust to the move |
| `scripts/lib/preconditions.py`, `scripts/lib/update_configs.py` | tool-name references |
| `scripts/test_*.py` (codex_command_building, evidence_detection, mcp_health, preconditions, run_reporting) | assert on tool names / `mcp__clio` |
| `README.md`, `docs/agent-bootstrap.md`, `docs/caadt-automation-guide.md` | doc references |
| `prompts/user-custom/components/component-prompt-catalog.md` | component-flow tool references |
| top refs by name: `create-app` ×38, `sync-pages` ×18, `sync-schemas` ×16, `list-pages` ×7, `reg-web-app` ×6, `modify-entity-schema-column` ×6 |

> **False positive excluded:** `scripts/lib/_vendor/pyyaml/yaml/resolver.py` matched
> `experimental` as an unrelated substring — it is a vendored library, **not** a clio consumer.

### Breaking set

**76 distinct long-tail tool names** are referenced by ≥1 consumer/e2e (column `Consumer=Y`
**and** `Tier=long-tail` in §1). These are the names that break on a core-by-default flip
**unless aliased** (§5). The CORE tools that are also referenced (e.g. `get-page`, `list-apps`,
`find-app`) do **not** break — they stay flat.

### AC-ERR — unverified consumers

All three named repos were present locally and grepped successfully (no "unverified — risk"
entries). **Caveat:** this inventory covers only the three repos named in Story 9. Any
**other** integration that pins flat clio MCP tool names (CI configs, personal agent setups,
third-party skills) is **out of scope and unverified — treat as residual risk** when flipping
the default.

---

## 5. Deprecation-alias list — **AC-03, feeds Story 10**

For every long-tail tool (excluding the two executors), the proposed deprecation alias maps the
**flat name → the matching executor** with `command=<flat-name>`. Routing follows the
`Destructive` flag. `Consumer ref?` flags the 75 that have a live consumer (must ship the
alias **with** the default flip — ADR resolved decision 2).

> Story 10 decides the **alias mechanism**: a thin flat proxy tool that forwards to the
> executor (keeps `CallToolAsync("<flat-name>")` working for e2e/CAADT) vs a documented
> rename only. Given §4's hard e2e/unit bindings, a registered flat proxy is strongly
> indicated for at least the `Consumer ref? = yes` rows.

| Flat (deprecated) tool name | Routes to | Consumer ref? |
|---|---|:--:|
| `StopAllCreatio` | `clio-run-destructive` (`command=StopAllCreatio`) | — |
| `add-data-binding-row` | `clio-run-destructive` (`command=add-data-binding-row`) | yes |
| `add-item-model` | `clio-run-destructive` (`command=add-item-model`) | yes |
| `add-package` | `clio-run` (`command=add-package`) | yes |
| `add-package-dependency` | `clio-run` (`command=add-package-dependency`) | yes |
| `assert-infrastructure` | `clio-run` (`command=assert-infrastructure`) | yes |
| `check-auth-code-flow` | `clio-run` (`command=check-auth-code-flow`) | yes |
| `check-settings-health` | `clio-run` (`command=check-settings-health`) | yes |
| `clear-browser-session` | `clio-run-destructive` (`command=clear-browser-session`) | yes |
| `clear-redis-db-by-credentials` | `clio-run-destructive` (`command=clear-redis-db-by-credentials`) | — |
| `clear-redis-db-by-environment` | `clio-run-destructive` (`command=clear-redis-db-by-environment`) | — |
| `compile-creatio` | `clio-run-destructive` (`command=compile-creatio`) | yes |
| `create-app` | `clio-run-destructive` (`command=create-app`) | yes |
| `create-app-section` | `clio-run-destructive` (`command=create-app-section`) | yes |
| `create-client-unit-schema` | `clio-run` (`command=create-client-unit-schema`) | yes |
| `create-data-binding` | `clio-run-destructive` (`command=create-data-binding`) | yes |
| `create-data-binding-db` | `clio-run-destructive` (`command=create-data-binding-db`) | yes |
| `create-entity-business-rule` | `clio-run-destructive` (`command=create-entity-business-rule`) | yes |
| `create-entity-schema` | `clio-run-destructive` (`command=create-entity-schema`) | yes |
| `create-lookup` | `clio-run-destructive` (`command=create-lookup`) | yes |
| `create-page` | `clio-run-destructive` (`command=create-page`) | yes |
| `create-page-business-rule` | `clio-run-destructive` (`command=create-page-business-rule`) | yes |
| `create-schema` | `clio-run` (`command=create-schema`) | yes |
| `create-sql-schema` | `clio-run` (`command=create-sql-schema`) | yes |
| `create-sys-setting` | `clio-run-destructive` (`command=create-sys-setting`) | yes |
| `create-user-task` | `clio-run` (`command=create-user-task`) | — |
| `create-workspace` | `clio-run` (`command=create-workspace`) | yes |
| `dataforge-context` | `clio-run` (`command=dataforge-context`) | yes |
| `dataforge-get-relations` | `clio-run` (`command=dataforge-get-relations`) | yes |
| `dataforge-get-table-columns` | `clio-run` (`command=dataforge-get-table-columns`) | yes |
| `dataforge-initialize` | `clio-run-destructive` (`command=dataforge-initialize`) | yes |
| `dataforge-update` | `clio-run-destructive` (`command=dataforge-update`) | yes |
| `delete-adac` | `clio-run-destructive` (`command=delete-adac`) | — |
| `delete-app` | `clio-run-destructive` (`command=delete-app`) | yes |
| `delete-app-section` | `clio-run-destructive` (`command=delete-app-section`) | yes |
| `delete-schema` | `clio-run-destructive` (`command=delete-schema`) | yes |
| `deploy-creatio` | `clio-run-destructive` (`command=deploy-creatio`) | yes |
| `download-configuration-by-build` | `clio-run` (`command=download-configuration-by-build`) | yes |
| `download-configuration-by-environment` | `clio-run` (`command=download-configuration-by-environment`) | yes |
| `execute-esq` | `clio-run` (`command=execute-esq`) | yes |
| `experimental` | `clio-run` (`command=experimental`) | yes |
| `find-empty-iis-port` | `clio-run` (`command=find-empty-iis-port`) | yes |
| `finish-hotfix` | `clio-run` (`command=finish-hotfix`) | yes |
| `generate-process-model` | `clio-run-destructive` (`command=generate-process-model`) | yes |
| `generate-source-code` | `clio-run` (`command=generate-source-code`) | — |
| `get-browser-session` | `clio-run` (`command=get-browser-session`) | yes |
| `get-client-unit-schema` | `clio-run` (`command=get-client-unit-schema`) | yes |
| `get-fsm-mode` | `clio-run` (`command=get-fsm-mode`) | yes |
| `get-identity-assertion` | `clio-run` (`command=get-identity-assertion`) | — |
| `get-identity-public-jwk` | `clio-run` (`command=get-identity-public-jwk`) | — |
| `get-process-signature` | `clio-run` (`command=get-process-signature`) | yes |
| `get-schema` | `clio-run` (`command=get-schema`) | yes |
| `get-schema-name-prefix` | `clio-run` (`command=get-schema-name-prefix`) | yes |
| `get-sql-schema` | `clio-run` (`command=get-sql-schema`) | yes |
| `get-user-culture` | `clio-run` (`command=get-user-culture`) | yes |
| `install-adac` | `clio-run` (`command=install-adac`) | — |
| `install-application` | `clio-run-destructive` (`command=install-application`) | yes |
| `install-gate` | `clio-run` (`command=install-gate`) | yes |
| `install-sql-schema` | `clio-run-destructive` (`command=install-sql-schema`) | yes |
| `link-from-repository-by-env-package-path` | `clio-run-destructive` (`command=link-from-repository-by-env-package-path`) | — |
| `link-from-repository-by-environment` | `clio-run-destructive` (`command=link-from-repository-by-environment`) | — |
| `link-from-repository-unlocked` | `clio-run-destructive` (`command=link-from-repository-unlocked`) | — |
| `list-creatio-builds` | `clio-run` (`command=list-creatio-builds`) | — |
| `list-page-templates` | `clio-run` (`command=list-page-templates`) | yes |
| `modify-entity-schema-column` | `clio-run-destructive` (`command=modify-entity-schema-column`) | yes |
| `modify-user-task-parameters` | `clio-run-destructive` (`command=modify-user-task-parameters`) | — |
| `new-test-project` | `clio-run` (`command=new-test-project`) | — |
| `new-ui-project` | `clio-run` (`command=new-ui-project`) | — |
| `odata-create` | `clio-run` (`command=odata-create`) | yes |
| `odata-delete` | `clio-run-destructive` (`command=odata-delete`) | yes |
| `odata-read` | `clio-run` (`command=odata-read`) | yes |
| `odata-update` | `clio-run-destructive` (`command=odata-update`) | yes |
| `pkg-to-db` | `clio-run-destructive` (`command=pkg-to-db`) | — |
| `pkg-to-file-system` | `clio-run-destructive` (`command=pkg-to-file-system`) | — |
| `push-workspace` | `clio-run-destructive` (`command=push-workspace`) | yes |
| `reg-web-app` | `clio-run` (`command=reg-web-app`) | yes |
| `regenerate-identity-signing-key` | `clio-run-destructive` (`command=regenerate-identity-signing-key`) | — |
| `remove-data-binding-row` | `clio-run-destructive` (`command=remove-data-binding-row`) | yes |
| `remove-data-binding-row-db` | `clio-run-destructive` (`command=remove-data-binding-row-db`) | yes |
| `restart-by-credentials` | `clio-run-destructive` (`command=restart-by-credentials`) | — |
| `restart-by-environment-name` | `clio-run-destructive` (`command=restart-by-environment-name`) | — |
| `restart-by-environmentName` | `clio-run-destructive` (`command=restart-by-environmentName`) | — |
| `restore-db-by-credentials` | `clio-run-destructive` (`command=restore-db-by-credentials`) | — |
| `restore-db-by-environment` | `clio-run-destructive` (`command=restore-db-by-environment`) | — |
| `restore-db-to-local-server` | `clio-run-destructive` (`command=restore-db-to-local-server`) | yes |
| `restore-workspace` | `clio-run-destructive` (`command=restore-workspace`) | yes |
| `set-fsm-mode` | `clio-run-destructive` (`command=set-fsm-mode`) | yes |
| `show-passing-infrastructure` | `clio-run` (`command=show-passing-infrastructure`) | yes |
| `start-creatio` | `clio-run` (`command=start-creatio`) | — |
| `stop-all-creatio` | `clio-run-destructive` (`command=stop-all-creatio`) | — |
| `stop-creatio` | `clio-run-destructive` (`command=stop-creatio`) | — |
| `sync-pages` | `clio-run-destructive` (`command=sync-pages`) | yes |
| `sync-schemas` | `clio-run-destructive` (`command=sync-schemas`) | yes |
| `uninstall-creatio` | `clio-run-destructive` (`command=uninstall-creatio`) | — |
| `unlock-for-hotfix` | `clio-run` (`command=unlock-for-hotfix`) | yes |
| `update-adac` | `clio-run` (`command=update-adac`) | — |
| `update-app-section` | `clio-run-destructive` (`command=update-app-section`) | yes |
| `update-client-unit-schema` | `clio-run-destructive` (`command=update-client-unit-schema`) | yes |
| `update-entity-schema` | `clio-run-destructive` (`command=update-entity-schema`) | yes |
| `update-page` | `clio-run-destructive` (`command=update-page`) | yes |
| `update-schema` | `clio-run-destructive` (`command=update-schema`) | yes |
| `update-sql-schema` | `clio-run-destructive` (`command=update-sql-schema`) | yes |
| `update-sys-setting` | `clio-run-destructive` (`command=update-sys-setting`) | yes |
| `upsert-data-binding-row-db` | `clio-run-destructive` (`command=upsert-data-binding-row-db`) | yes |

---

## 6. Curated-contract coverage gap — **AC-03, feeds Story 6**

The ADR requires that **every long-tail command have a curated contract** because the
reflection fallback (`McpToolSchemaCatalog`) is lossy (first-param-only, enum→string,
nested→object). Current state:

| Metric | Value |
|---|--:|
| Curated contracts in `ToolContractCatalog.Contracts` | **61** |
| …of which returned by default (`CanonicalToolNames`) | 47 |
| Total tools | 126 |
| Tools **without** a curated contract (excl. the 2 executors) | **63** |

> The ADR's "~46 curated contracts" figure tracks the `CanonicalToolNames` default-return set
> (47); the full `Contracts` dictionary actually holds **61** entries (the extra 14 are
> curated but not in the default list, e.g. `dataforge-initialize`, `dataforge-update`,
> `delete-app`, `new-ui-project`, `find-empty-iis-port`, `install-gate`,
> `show-passing-infrastructure`, `deploy-creatio`, `list-creatio-builds`,
> `assert-infrastructure`, `push-workspace`, `restore-workspace`). They resolve via the
> `Contracts` map but are not part of the canonical default payload.

### Uncovered commands Story 6 must add curated contracts for (63)

`StopAllCreatio` `add-item-model` `add-package` `add-package-dependency`
`check-auth-code-flow` `clear-browser-session` `clear-redis-db-by-credentials`
`clear-redis-db-by-environment` `create-client-unit-schema` `create-page` `create-schema`
`create-sql-schema` `create-user-task` `create-workspace` `delete-adac` `delete-schema`
`download-configuration-by-build` `download-configuration-by-environment` `experimental`
`find-app` `finish-hotfix` `generate-process-model` `generate-source-code`
`get-browser-session` `get-client-unit-schema` `get-fsm-mode` `get-identity-assertion`
`get-identity-public-jwk` `get-process-signature` `get-schema` `get-sql-schema`
`get-user-culture` `install-adac` `install-application` `install-sql-schema`
`link-from-repository-by-env-package-path` `link-from-repository-by-environment`
`link-from-repository-unlocked` `list-environments` `list-packages` `list-page-templates`
`modify-user-task-parameters` `new-test-project` `pkg-to-db` `pkg-to-file-system`
`reg-web-app` `regenerate-identity-signing-key` `restart-by-credentials`
`restart-by-environment-name` `restart-by-environmentName` `restore-db-by-credentials`
`restore-db-by-environment` `restore-db-to-local-server` `set-fsm-mode` `start-creatio`
`stop-all-creatio` `stop-creatio` `uninstall-creatio` `unlock-for-hotfix` `update-adac`
`update-client-unit-schema` `update-schema` `update-sql-schema`

> **Important — three uncovered tools are in the proposed CORE** (`find-app`,
> `list-environments`, `list-packages`). Core tools resolve their lazy schema through
> `get-tool-contract` too; if they have no curated contract they fall back to the lossy
> reflection catalog. Story 6 must curate **at least these three core tools first**, then the
> remaining 60 long-tail tools. (`get-page` and the other core members already have curated
> contracts.)

---

## 7. Story hand-offs (AC-05 reconciliation)

- **Story 1 (profile config):** §1 `Tier` column assigns **every** tool to core or long-tail —
  no tool is unassigned (AC-05 satisfied). The 2 executors are always-registered, not "core
  commands". Story 1's config is authoritative if it diverges.
- **Story 6 (curated contracts):** §6 — 63 uncovered tools; prioritise the 3 uncovered **core**
  tools.
- **Story 7 (finalise core):** §2 — the 20-tool proposal; tune membership and the index taxonomy.
- **Story 10 (aliases + default flip):** §5 — 104 alias rows (75 consumer-backed). Ship aliases
  **with** the flip (ADR resolved decision 2). Decide proxy-vs-doc mechanism using §4 bindings.
- **Story 11 (migration):** §3 — ~300–400 file edits across 8 artifact classes; resolve each
  MCP tool name to its canonical verb before editing help/docs.

---

## Appendix: method

- **Tool catalog:** parsed every `[McpServerTool(Name=…, ReadOnly=…, Destructive=…,
  Idempotent=…)]` in `clio/Command/McpServer/Tools/*.cs`, resolving constant `Name` references
  **per declaring class** (each nested `*Tool` re-declares `ToolName`; a file-global const map
  mis-resolves them — verified by cross-checking `ClioRunTool`, `IdentityAssertionTool`,
  `SkillManagementTool`, `BusinessRuleTool` directly). One match in
  `CommandDestructivenessClassifier.cs` is a doc-comment, not a real registration, and is excluded.
- **Curated contracts:** counted keys in the `ToolContractCatalog.Contracts` dictionary and the
  `CanonicalToolNames` array in `ToolContractGetTool.cs`.
- **Consumers:** grepped a longest-match alternation of all 126 tool names across the three repos
  (`.py/.md/.json/.txt/.yaml/.cs`), excluding `__pycache__` and one vendored pyyaml false positive.
- **Artifacts:** counted files under each artifact directory; matched long-tail tool names against
  `help/en` and `docs/commands` basenames (exact name; undercounts non-1:1 verb mappings).
