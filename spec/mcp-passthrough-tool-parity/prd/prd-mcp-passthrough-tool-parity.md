# PRD: MCP `mcp-http` Credential-Passthrough Tool Parity

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-07-10
**Jira**: ENG-93347 (sub-task of ENG-92790; direct predecessor ENG-93208 / PR #830; blocks ENG-92869)

---

## Branching Constraint (read first)

This work sits **on top of** the credential-passthrough infrastructure delivered by ENG-93208.
All passthrough plumbing (`CredentialContext`, `CredentialContextAccessor`, `CredentialHeaderParser`,
the `ToolCommandResolver` passthrough branch, `McpHttpServerCommand` middleware) is **already present**
in the umbrella branch `claude/clio-mcp-multi-tenant-73a807`.

- The implementation branch for ENG-93347 **must be cut from `claude/clio-mcp-multi-tenant-73a807`**
  (not `master`) and the eventual PR **must target `claude/clio-mcp-multi-tenant-73a807`** (not `master`).
- Rationale: the fix depends on `CredentialContext` / `ICredentialContextAccessor` /
  `ToolCommandResolver.Resolve`'s passthrough branch, none of which exist on `master`. Branching off
  `master` would produce a build that cannot compile against the passthrough seam.

---

## Problem Statement

Per-request credential passthrough (`X-Integration-Credentials`) is honored **only** on request paths that
resolve their target-scoped service/settings **per request** through `IToolCommandResolver` — the only place
the per-request `CredentialContext` is read (`ToolCommandResolver.Resolve`, `credentialContextAccessor.Current`,
`ToolCommandResolver.cs:101` reads the context; the env-arg rejection is at `:111-118`). Some resident tools (or some code paths **within** an otherwise-correct tool)
instead reach Creatio through a **directly-injected service bound to the root/bootstrap container** (a domain
service such as `IApplicationListService`, or `ISettingsRepository.GetEnvironment`). Those paths never see the
header. From the AI-Platform gateway's view the failure is silent and inconsistent — sometimes an opaque
"Environment name is required" error, sometimes (worse) a **wrong-tenant** result using stored credentials.

Who has the problem: the **AI-Platform gateway author (CI pipeline author)** driving one shared
`clio mcp-http` edge over passthrough, and the **QA engineer** who cannot certify a consistent tool set.
Why now: ENG-93208 shipped the passthrough edge; ENG-92869 (AI-Platform integration) cannot proceed while
name-based tools are unusable or unsafe and the failure mode is indistinguishable from a caller mistake.

## The invariant (discriminator) — per dependency-path, not per tool

Resolver use at the **tool** level is **necessary but not sufficient**: a single tool can have one path that
honors the header and another that does not. Example: `update-page` resolves its write command through the
resolver (`PageUpdateTool.cs:64`, honors the header) yet resolves its validation platform-version through
`ISettingsRepository.GetEnvironment` (`PageUpdateTool.cs:273`, header-blind). The audit is therefore
**per dependency-path**, stated as:

> **Every request path that reaches — or derives target-specific information from — a Creatio environment must
> obtain its target-scoped service/settings through `IToolCommandResolver`, unless it deliberately returns a
> documented, non-tenant fallback (e.g. an explicit `latest` with a machine-readable flag).**

## Goals

- [ ] Goal 1 — Every environment-sensitive request **path** behaves correctly and consistently under passthrough
  - **SM-01**: 100% of environment-sensitive resident-tool paths, when invoked over `mcp-http` under authorized
    passthrough, either (a) execute against the header-supplied tenant, (b) return the uniform "not supported
    under credential passthrough" error, or (c) return a documented non-tenant fallback with a machine-readable
    flag — measured by a per-path passthrough test matrix (FR-08). **Counter**: **zero** environment-sensitive
    paths return a generic "environment name is required / not found" error, and **zero** silently resolve the
    active/registered tenant, under passthrough.
- [ ] Goal 2 — The per-path audit is captured and kept authoritative
  - **SM-02**: A committed classification (this PRD's audit) classifies every `[McpServerToolType]` tool
    (class-a enumerated representatively; all broken / class-b / per-tool-matrix / out-of-scope tools listed by
    name) and, for the broken/matrix tools, per dependency-path; a guard (FR-06) prevents new header-blind
    Creatio paths. **Counter**: no environment-sensitive path is left unclassified or drifts back to a bypass
    without a failing guard.
- [ ] Goal 3 — No regression to today's single-tenant / stdio / registered-environment behavior
  - **SM-03**: `clio mcp` (stdio) and `clio mcp-http -e <env>` with a registered environment (name-based tools
    invoked with an explicit `environment-name`) behave exactly as before this change; existing MCP unit + e2e
    suites stay green. **Counter**: no functional regression on the registered-env/stdio path. *(A latency
    target is intentionally not asserted here — see OQ-05; if a perf claim is wanted, define a threshold + harness.)*

## Non-goals

- **Will NOT** re-open or redesign the passthrough transport, header contract, API-key gate, SSRF allowlist,
  execution-lock de-globalization, secret-hygiene, or the **middleware error semantics** (no-header → forward;
  malformed header → HTTP 400 before any tool) — those are ENG-93208 and are treated as fixed ground. This task
  only brings the remaining **tool paths** into parity with that contract.
- **Will NOT** add token/cookie auth material beyond what ENG-93208 supports (bearer access token; cookie
  dropped in v1).
- **Will NOT** change out-of-scope, non-environment-sensitive tools (local workspace / local infra / telemetry /
  guidance).
- **Will NOT** promise "route through resolver" for every broken tool: where routing is disproportionate for v1,
  the uniform fail-fast (Goal 1b) or a documented non-tenant fallback (Goal 1c) is an acceptable in-scope
  resolution, decided per tool in the ADR.

## Audit — Resident Tool Passthrough Classification (core deliverable)

### Class (a) — BaseTool + per-request resolver — PASSTHROUGH-CAPABLE (works today)

BaseTool subclasses constructed **with** an `IToolCommandResolver` that execute via
`InternalExecute<TCommand>(options)` or `ExecuteWithCleanLog(...)` + `ResolveCommand<TCommand>` — the command is
resolved per request against the header context. Reference/working example: `GetCreatioInfoTool`
(`describe-environment`), the only tool proven multi-tenant today (`McpHttpMultiTenantE2ETests.cs:33`).

This is the large majority — e.g. `describe-environment`, `page-create`/`page-list`/`page-get`/
`page-templates-list`, `EntitySchema*` (`create-entity-schema`, `create-lookup`, `update-entity-schema`,
`find-entity-schema`, …), `Schema*` / `SqlSchema*` / `ClientUnitSchema*` families, `Theme*` write/read pairs
except `build-theme` (see matrix), `Identity*` / OAuth tools, `DataBindingDb*`, `CreateDataBinding`,
`UserTask*`, `find-app`, `install-application`, `get-pkg-list`, `get-schema`, `get-client-unit-schema`,
`download-configuration`, `restart`, `start`/`stop`, `clear-redis`, `restore-db`, `load-packages`,
`add-package-dependency`, `remove-package-dependency`, `package-hotfix`, `generate-source-code`,
`generate-process-model`, `fsm-mode`, `delete-schema`, `create-ui-project`, `create-workspace`,
`workspace-sync`, `add-package`, `create-test-project`. **No change required.**

### Class (b) — direct `IToolCommandResolver.Resolve<T>(...)` (not BaseTool) — PASSTHROUGH-CAPABLE

Not derived from `BaseTool`, but calls `commandResolver.Resolve<T>(options)` per request. Note
`IToolCommandResolver.Resolve<T>` is **unconstrained** (`ToolCommandResolver.cs:15`), so these already resolve
**services** (not just `Command<T>`) through the resolver — e.g. DataForge resolves `IDataForgeContextService`
(`DataForgeEnrichmentBuilder.cs:41`). Relevant for OQ-01 routing options.

| Tool(s) | Class | Resolution |
|---------|-------|-----------|
| `add-item-model` | AddItemModelTool | `commandResolver.Resolve<AddItemCommand>` |
| `compile-configuration` / `compile-package` | CompileCreatioTool | `commandResolver.Resolve` |
| DataForge tools | DataForgeTool | `commandResolver.Resolve<TService>` (service, unconstrained) |
| `sync-schemas` | SchemaSyncTool | `commandResolver.Resolve` |
| `get-browser-session` / `clear-browser-session` | Get/ClearBrowserSessionTool | resolver-ctor |
| `execute-esq` | ExecuteEsqTool | resolver-ctor |
| `get-schema-name-prefix` | SchemaNamePrefixTool | resolver-ctor |
| `odata-read` / `odata-create` / `odata-update` / `odata-delete` | ODataReadTool / ODataCreateTool / ODataUpdateTool / ODataDeleteTool | resolver-ctor |
| `get-sys-setting` / `list-sys-settings` / `create-sys-setting` / `update-sys-setting` | SysSettings* | resolver-ctor |
| `create-entity-business-rule` / `create-page-business-rule` | CreateEntityBusinessRuleTool / CreatePageBusinessRuleTool | `commandResolver.Resolve<IEntityBusinessRuleService>` per request |

> `clio-run` / `clio-run-destructive` are **passthrough-capable via the target tool**: they re-invoke the
> resolved target tool **within the SAME request context** (`ClioRunTool.cs:122` — they do **not** use
> `EnvironmentScopedCommandExecutor`), so they inherit the target's passthrough behavior — including its
> breakage if the target is broken. They neither add nor launder the fix.

**No change required to class (a)/(b).** (Listed so the broken set is provably exhaustive.)

### Class (c) — BROKEN — the whole tool reaches Creatio outside the resolver, ignoring the header

**(c1) Name-only via a directly-injected DOMAIN service.** The tool injects a domain service (bound to the
root/bootstrap container) and calls `service.Method(args.EnvironmentName, …)`. Its args expose **only**
`environment-name` (no `uri`/`login`/`password`) — `ApplicationToolArgs.cs:11,21`, `environment-name` marked
`[Required]`. **Header-only failure mode = HARD FAILURE:** the service validates and throws
`ArgumentException("Environment name is required.")` **before any repository lookup and before touching any
tenant-scoped dependency** (`ApplicationListService.cs:52-55`, `ApplicationInfoService.cs:120-123`,
`ApplicationSectionCreateCommand.cs:185-187`). **Resolving the domain service from the tenant container is NOT
sufficient** — the tool still calls `service.GetApplications(args.EnvironmentName, …)` with a null name
(`ApplicationTool.cs:30`) and the service throws regardless of which container produced it; the child
container's `ISettingsRepository` is still the filesystem/bootstrap repo (`BindingsModule.cs:203`) and does not
synthesize an environment name. Routing c1 therefore needs a **service-contract/refactor step** (see OQ-01a).
This is an availability / opaque-error defect, **not** a proven data leak (empty name never reaches an
environment). The leak risk is the *mixed-input* confused-deputy case — see Security modes (ii)/(iii).

| Tool | Class | Injected service (bypass path) |
|------|-------|--------------------------------|
| `list-apps` | ApplicationGetListTool | `IApplicationListService.GetApplications(args.EnvironmentName!, …)` — ticket reference example |
| `get-app-info` | ApplicationGetInfoTool | `IApplicationInfoService.GetApplicationInfo(args.EnvironmentName, …)` |
| `create-app` | ApplicationCreateTool | `IApplicationCreateService.CreateApplication(args.EnvironmentName, …)` |
| `create-app-section` | ApplicationSectionCreateTool | `IApplicationSectionCreateService.CreateSection(args.EnvironmentName, …)` |
| `update-app-section` | ApplicationSectionUpdateTool | `IApplicationSectionUpdateService.UpdateSection(args.EnvironmentName, …)` |
| `delete-app-section` | ApplicationSectionDeleteTool | `IApplicationSectionDeleteService.DeleteSection(args.EnvironmentName, …)` |
| `list-app-sections` | ApplicationSectionGetListTool | `IApplicationSectionGetListService.GetSections(args.EnvironmentName, …)` |

**(c2) Via directly-injected `ISettingsRepository.GetEnvironment(options)`.** `get-user-culture`
(GetUserCultureTool) calls `settingsRepository.GetEnvironment(options)` (`GetUserCultureTool.cs:82`) as its
**only** environment path. **Header-only failure mode = REAL ACTIVE-TENANT DATA LEAK:** with neither env-name
nor URI supplied, `GetEnvironment` calls `FindEnvironment(null)`, which returns the **configured active
environment** (a valid `ActiveEnvironmentKey`) or `null` — it does **not** pick the first environment
(`ConfigurationOptions.cs:638-652` calling `:621-629`). So the leak is real **only when an active environment
is configured** on the edge: under passthrough `get-user-culture` then reads that *active* tenant's user culture
using its **stored** credentials — silently, with no error. This is the most dangerous class-c row.

**(c3) Link-from-repository family — non-resolver `InternalExecute(options)`, reaches Creatio.** Three real
resident/redispatchable tools (`[McpServerToolType]` discovery is inherited/intentional,
`McpFeatureToggleFilter.cs:89`) share one root-bound `Link4RepoCommand` and dispatch via the non-generic
`InternalExecute(options)` (`LinkFromRepositoryTool.cs:40,63,85`), so they never consult the header. Their
Creatio access varies per path:

| Tool | Reaches Creatio? | Path |
|------|------------------|------|
| `link-from-repository-by-environment` | Yes — preparation | Maintainer read/write + lock/design-mode via `ISysSettingsManager` / `IPackageLockManager` / `IFileDesignModePackages` (`Link4RepoCommand.cs:289,310`) against the named registered env |
| `link-from-repository-by-env-package-path` | Partly | targets an explicit local package dir; still runs the same preparation Creatio calls unless `skip-preparation` is set |
| `link-from-repository-unlocked` | Yes — always | queries the site for unlocked packages via `_applicationPackageListProvider.GetPackages` (`Link4RepoCommand.cs:382,437`) |

Header-only ⇒ hard failure / opaque (no resolver, name-based). Header+env-name ⇒ confused-deputy (uses the named
registered env's stored creds). Each Creatio-reaching branch must be classified and covered (FR-07/FR-08).

### Per-tool state matrix — header-version / mixed-path tools

These tools are **not** uniformly broken; each has a specific per-path state. Uses the real MCP names.

Each row is split by input: **header-only** (no env-name) and **header+env-name** (mixed input).

| Tool | Header-only state | Header + `environment-name` state | Required work |
|------|-------------------|-----------------------------------|---------------|
| `update-page` (PageUpdateTool) | version probe (`ResolvePlatformVersionAsync`, `:104`) runs **before** the resolver-backed write (`ResolveCommand` `:64`) and calls root `settingsRepository.GetEnvironment` (`:273`) with blank args → `FindEnvironment(null)` selects the **configured active** registered tenant and probes it with stored creds; `latest` happens **only if that lookup/probe FAILS** (not "always latest") | same probe uses the supplied env-name (root repo) before the resolver sees the request | make the version probe header-aware, or fail-fast before it; never probe active/named tenant under passthrough (OQ-02) |
| `sync-pages` (PageSyncTool) | version probe returns `null` on blank env → `latest` (`:74,:97`); then `[Required]` `environment-name` (`:951`) collides with the resolver's env-arg rejection (`:111-118`) → **uncallable as contracted** | version probe runs against the **named registered** env via `GetEnvironment` (`:74,:97`) using stored creds **before** the resolver rejects the env arg (`Resolve` `:283`) — a data path, not just a requiredness conflict | conditional-requiredness (FR-05a) **and** move/guard the named-env probe so it never runs under passthrough |
| `get-component-info` (ComponentInfoTool) | **compliant**: neither env-name nor uri → `CreateNoActiveEnvironmentFallback` (loud `latest-fallback`, `ComponentInfoTool.cs:267`; proven `ComponentInfoToolTests.cs:606`) | **NOT compliant**: `hasEnvironment` (env-name OR uri, `:172`) → `ResolveEnvironmentSettings` → root `GetEnvironment` probes the **named registered** tenant (`:261,:279`) with stored creds | header-only needs nothing; **mixed-input** must be guarded so it does not probe the named registered tenant under passthrough (OQ-02) |
| `build-theme` (BuildThemeTool) | header-blind: with a header (no env-name) it silently uses the **newest** template version instead of the tenant's (local CSS/descriptor build, no per-request Creatio auth) | with `environment-name` builds an **authenticated** platform-version resolver (`BuildThemeCommand.cs:251` → `PlatformVersionResolverFactory.cs:40` creates an `IApplicationClient`) against the named env | make version header-aware, fail-fast, or document a non-tenant fallback per OQ-02 |

### Out of scope — not environment-sensitive (no Creatio credential involved)

No passthrough path needed; sanctioned to run the injected command directly: telemetry
(`get-telemetry-consent`, `send-telemetry`, `withdraw-telemetry-consent`, `get-telemetry-*`), `get-guidance`,
`get-tool-contract`, infrastructure assertions (`assert-infrastructure`, `show-passing-infrastructure`,
`show-web-app-list`), `find-empty-iis-port`, `list-creatio-builds` (local `ISettingsRepository` file lookup —
**note this is the exact false-positive the FR-06 guard must not trip on**), `advise-theme-palette`,
`reg-web-app` (writes local `appsettings.json`), `uninstall-creatio` / `install-creatio`
(local infra), skills (`install-skills`, `update-skill`, `delete-skill`), local data-binding row edits
(`add-data-binding-row`, `remove-data-binding-row`), `get-settings-health`.

## Security — three distinct modes (do NOT claim all c1 header-only calls "fall back")

| Mode | Scenario | Actual behavior | Severity | Proof / caveat |
|------|----------|-----------------|----------|----------------|
| (i) | class-c1 (7 Application tools), header **only**, no env-name | domain service throws `ArgumentException("Environment name is required.")` before any repo lookup or tenant dependency | **Availability / opaque error** — NOT a proven leak | `ApplicationListService.cs:52-55`; `ApplicationInfoService.cs:120-123`; `ApplicationSectionCreateCommand.cs:185-187` |
| (ii) | `get-user-culture`, header **only**, no env-name / URI | `GetEnvironment`→`FindEnvironment(null)` returns the **configured active** env (or `null` if none) and reads it with **stored** creds | **REAL active-tenant data leak — only when an active env is configured** (silent) | `GetUserCultureTool.cs:82`; `ConfigurationOptions.cs:638-652` calling `:621-629` (configured-active, not first) |
| (iii) | any bypass tool, header **AND** explicit `environment-name` | tool ignores the header, uses the **named registered** env's **stored** creds | **Confused-deputy / privilege escalation — CONDITIONAL** | `ApplicationTool.cs:30`. Whether this is priv-esc depends on **deployment facts the code cannot prove** (who holds the platform API key vs. who registered the env). Frame as conditional; verify with the ADR/deployment model |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Every **class (c1)** tool (`list-apps`, `get-app-info`, `create-app`, `create-app-section`, `update-app-section`, `delete-app-section`, `list-app-sections`) must, under authorized passthrough, **either** execute against the header tenant **or** return the uniform "not supported under credential passthrough" error — never the generic `ArgumentException("Environment name is required.")`. It must **never** silently resolve an active/registered environment. | Must |
| FR-02 | **`get-user-culture` (class c2) must close the active-tenant leak (Security mode ii).** Under passthrough it must honor the header tenant or fail-fast uniformly; it must **never** fall through to the configured active environment (`FindEnvironment(null)`). Its explicit-arg (`uri`/`login`/`password`) and registered-environment behavior on stdio/registered paths is unchanged. | Must |
| FR-03 | **Per-tool matrix resolution.** `sync-pages` must resolve its contract/passthrough conflict (FR-05a). `update-page` and `build-theme` must not silently use a non-tenant platform version under passthrough — either resolve it via the header context (OQ-02) **or** return a documented non-tenant fallback with a machine-readable flag (as `get-component-info` already does). `get-component-info` is **already compliant** (loud `latest-fallback`); header-version parity for it is optional (OQ-02). | Must |
| FR-04 | Provide a **single, uniform** "not supported under credential passthrough" error message + shape, reused by every tool-level fail-fast path, naming the tool and the supported alternative (register the environment / use the stdio path), leaking no secret material. The fail-fast must fire **before** any active/registered-environment fallback. This is the **valid-header/unsupported-tool** case only (middleware handles no-header and malformed-header — see AC-ERR). | Must |
| FR-05 | Where a broken tool is **routed** (not fail-fast), it must obtain its target-scoped service/settings per request through `IToolCommandResolver`, sharing the ENG-93208 identity branch, per-tenant lock, cache key, and in-flight guard — no new resolver bypass, no second `settings.Fill`. **Note:** for class-c1, resolving the existing domain service through the resolver is **not by itself executable** — the service throws on the null name before using any tenant dependency (`ApplicationListService.cs:52-55`); routing requires a service-contract/refactor so the operation runs on the resolver-selected tenant **without a name lookup** (OQ-01a). | Must |
| FR-05a | **Conditional-requiredness of `environment-name`.** For routed tools, `environment-name` must become **conditionally** required: **forbidden** under authorized HTTP passthrough (the resolver rejects env args, `ToolCommandResolver.cs:111-118`), and **required/resolvable** on stdio / registered-environment / default-HTTP paths. The MCP argument contract (`[Required]` today, e.g. `sync-pages` `PageSyncArgs.EnvironmentName`) and the ctor non-optional params (Application args) must be reconciled so server-side binding does not reject an authorized-passthrough request **before** the tool runs. | Must |
| FR-06 | Add a **guard** that prevents new header-blind Creatio paths. It must be **path-based, not constructor-reflection-based** — constructor reflection cannot distinguish a request-path tenant bypass from legitimate local `ISettingsRepository` use (e.g. `list-creatio-builds`). Acceptable mechanisms: (a) a Roslyn analyzer (`CLIO*`) with analyzer-fixture tests, **or** (b) an explicit audited allowlist **plus a discovery assertion that the complete registered/redispatchable tool set EXACTLY matches the allowlist** and an explicit mapping from every environment-sensitive tool/path to its behavior test — a newly added tool must FAIL the guard until it is classified and mapped (a missing test must not silently pass). Constructor reflection alone does **not** satisfy this. | Should |
| FR-07 | The per-path audit + state matrix in this PRD is the reference inventory (including the three `link-from-repository-*` tools and their Creatio-reaching branches); the route-vs-fail-fast-vs-documented-fallback decision per broken tool/path must be recorded in the ADR as an explicit **decision matrix** and reflected by tests (FR-08). | Must |
| FR-08 | **Test coverage (mandatory).** Unit `[Category("Unit")]` for **every named broken tool and every matrix path** (not just one representative), asserting the per-path passthrough behavior, reusing `CredentialPassthrough*Tests` / `ToolCommandResolver*Tests` patterns; include a **mixed-input** test (header AND `environment-name` present) proving no confused-deputy. MCP e2e in `clio.mcp.e2e/` extending `McpHttpMultiTenantE2ETests` for **both** header-only and header-plus-`environment-name` cases, covering at least `list-apps`, one section tool, `get-user-culture`, and one `link-from-repository-*` Creatio-reaching branch; routed tools also extend `McpHttpConcurrencyIsolationE2ETests`. Every Creatio-reaching branch of the link family must have a passthrough behavior test. Flag: MCP e2e is not in CI yet. | Must |
| FR-09 | **MCP surface + docs review (mandatory).** For every touched tool, review/update its `[Description]` (note passthrough support/limitation + conditional-requiredness), `docs/McpCapabilityMap.md`, and any affected `help/en/*.txt` / `docs/commands/*.md` / `Commands.md` / guidance article. State "MCP reviewed, no update required" where accurate. | Must |
| FR-10 | No new `CLIO*` diagnostics in changed files; all changes via DI + constructor injection (no MediatR; Creatio access only via `IApplicationClient`); no camelCase args introduced. | Must |

## CLI Impact

Behavioral fix to existing MCP tools. **No new CLI verb and no new CLI flag** — `mcp-http` and its flags are
all delivered by ENG-93208 and unchanged. The only contract change is at the **MCP argument** level.

| Change | Details | Breaking? |
|--------|---------|-----------|
| MCP arg contract | `environment-name` becomes **conditionally** required for routed tools (forbidden under authorized passthrough; required/resolvable otherwise). Must be reconciled at binding so authorized-passthrough requests are not rejected pre-tool (FR-05a). | No for stdio/registered; enables the passthrough path |
| Tool behavior | class-c1/c2 tools honor the header or fail-fast uniformly; `get-user-culture` active-tenant leak closed | No — additive; registered-env/stdio unchanged |
| Tool behavior | `update-page`/`build-theme` no longer silently use a non-tenant platform version (header-aware or documented fallback); `get-component-info` already compliant | No — surfaces a documented fallback instead of silent |

All (any) args: **kebab-case only** (CLIO001 enforced).

## Acceptance Criteria

- [ ] AC-01: Given authorized passthrough and zero registered environments, when `list-apps` is called with a valid `X-Integration-Credentials` header and no `environment-name`, then it returns the tenant's applications **or** the uniform "not supported under credential passthrough" error — never `Environment name is required. (Parameter 'environmentName')`.
- [ ] AC-02: Given the same setup, when each other class-c1 tool (`get-app-info`, `create-app`, `create-app-section`, `update-app-section`, `delete-app-section`, `list-app-sections`) is called under passthrough with no `environment-name`, then each behaves **according to the ADR's explicit per-tool decision matrix** (execute-with-header or uniform fail-fast).
- [ ] AC-03: Given authorized passthrough with the header and no env-name/URI, when `get-user-culture` runs, then it resolves the **header** tenant or fails fast uniformly — it must **never** read the active/registered environment's culture (Security mode ii closed). Its explicit-arg/registered behavior on stdio is unchanged.
- [ ] AC-04: Given authorized passthrough, when `update-page` / `sync-pages` / `build-theme` / `get-component-info` run (header-only AND header+env-name), then **no named/active registered-environment repository lookup or version probe occurs before header routing or rejection**, and the platform version is either header-derived or a documented non-tenant fallback flag (e.g. `latest-fallback`) — never a silent non-tenant probe. `get-component-info` header-only stays compliant; its **mixed-input** path must be guarded too.
- [ ] AC-05: Given `sync-pages` under authorized passthrough, when it is called, then its `[Required]` `environment-name` contract does not cause a pre-tool binding rejection **and** the resolver's env-arg rejection does not make it uncallable — i.e. `environment-name` is treated as conditionally required (FR-05a) and the call succeeds against the header tenant or fails with the uniform error.
- [ ] AC-06: **Mixed input (confused-deputy).** Given authorized passthrough with **both** a header **and** an explicit `environment-name` naming a *different* registered environment, when any environment-sensitive tool runs (including `sync-pages`/`get-component-info` version probes and the `link-from-repository-*` preparation paths), then it executes against the **header** tenant (or fails per the resolver's env-arg rejection) **before** any named-env repository/version probe — it must **never** use the named registered environment's stored credentials (Security mode iii closed).
- [ ] AC-07: Given a routed class-c tool under two concurrent different-credential requests, when both run, then each resolves a distinct authenticated container via `IToolCommandResolver` with no cross-tenant session/log bleed (reuses ENG-93208 isolation).
- [ ] AC-08: Given the guard (FR-06), when a new/edited tool reaches Creatio on a request path without going through `IToolCommandResolver`, then the guard fails — while legitimate local `ISettingsRepository` use (e.g. `list-creatio-builds`) does **not** trip it. **Constructor reflection alone does not satisfy this AC** (must be a Roslyn analyzer with fixtures, or allowlist + per-tool behavior tests).
- [ ] AC-09: Given `clio mcp` (stdio) and `clio mcp-http -e <env>`, when any touched tool is invoked with `environment-name`, then behavior matches the pre-change baseline and existing MCP unit + e2e suites stay green.
- [ ] AC-10: Given the changed tools, when `docs/McpCapabilityMap.md` and each touched `[Description]` are inspected, then they reflect passthrough support/limitation + conditional-requiredness (FR-09), verified by a docs-review checklist item.
- [ ] AC-ERR (scoped to ENG-93347 only — middleware cases are ENG-93208, unchanged): Given a **valid** header on an **unsupported** (fail-fast) tool under authorized passthrough, when it runs, then the tool returns its typed error envelope `{ "success": false, "error": "<uniform not-supported-under-passthrough message>" }` (the existing Application-family shape, `ApplicationToolResponses.cs:11`) — **or**, for a resolver/filter-raised failure, an MCP `CallToolResult` with `IsError=true` (`McpToolErrorFilter.cs:96`). Do **not** require a process exit code: only `CommandExecutionResult` carries `ExitCode`, and these typed-response tools do not return one. No `accessToken`/`login`/`password` may leak. **Out of scope here (do not re-implement):** a request with **no** header → passthrough disabled, forwarded as the registered/default path (`McpHttpServerCommand.cs:272`); a **malformed/unusable** header → HTTP 400 **before** any tool (`McpHttpServerCommand.cs:317`; `CredentialPassthroughMiddlewareTests.cs:167`). A tool-level uniform error cannot cover these without changing ENG-93208 transport semantics (a non-goal).

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | ENG-93208's passthrough seam (`CredentialContext`, `ICredentialContextAccessor`, `ToolCommandResolver.Resolve` branch incl. the env-arg rejection at `:111-118`, per-tenant lock/cache) is present and correct on `claude/clio-mcp-multi-tenant-73a807` and is the sole integration point. | If the seam changes under this branch, routed paths must re-integrate; rebase, don't fork. |
| A-02 | `IToolCommandResolver.Resolve<T>` is unconstrained (`ToolCommandResolver.cs:15`) so a `Command<T>` wrapper is not strictly required to resolve a service — **but** resolving the existing Application service is not by itself executable: the tool passes `args.EnvironmentName` (null under passthrough) and the service throws before any tenant dependency (`ApplicationListService.cs:52-55`), and the child container's `ISettingsRepository` is still the filesystem repo (`BindingsModule.cs:203`). Routing therefore needs a service-contract/refactor (OQ-01a). | If the refactor is heavier than expected, uniform fail-fast (FR-01) is the v1 fallback for that tool. |
| A-03 | The class-c1 defect is **root/bootstrap-container binding**, not one-time construction: services + tools are `AddTransient` (`BindingsModule.cs:296,401`) and MCP methods are instantiated per call (`McpToolInvokerRegistry.cs:121`), but they fall back to the **root** provider, which binds the domain service to the startup environment. | If some tools are actually resolved from the per-call provider already, they may not be broken; the per-path tests (FR-08) are the ground truth. |
| A-04 | class-c1 args are name-only (`ApplicationToolArgs.cs:11,21`, `environment-name` `[Required]`); no explicit-arg credential fallback exists for these tools. | If args are later widened to accept `uri`/creds, the fail-fast/routing decision would need revisiting. |
| A-05 | Security mode (iii) (confused-deputy) being a genuine privilege escalation depends on **deployment facts** (platform-API-key holder vs. env registrant) not provable from code alone. | If the deployment model co-locates the API-key holder and env registrant, the severity is lower; the ADR must state the assumed trust model. |
| A-06 | A per-tool outcome of fail-fast **or** documented non-tenant fallback is acceptable for v1 where full routing is disproportionate. | If the gateway requires *all* name-based tools to execute against the header, scope grows to full routing of the class-c set. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Per broken tool/path: **route via the resolver** vs **fail-fast** vs **documented non-tenant fallback**. Routing sub-options: **(a)** a **service-contract/refactor** so the operation runs on the resolver-selected tenant **without a name lookup** — e.g. a tenant-scoped operation accepting injected `IApplicationClient`/`EnvironmentSettings`, or a dedicated adapter/command; simply calling `Resolve<IApplicationListService>` is **not** executable (the service throws on the null name, `ApplicationListService.cs:52-55`). **(b)** introduce a `Command<T>` only where command semantics add value; **(c)** reject via a **centralized pre-binding passthrough guard** (single chokepoint instead of per-tool edits). | Architect | ADR |
| OQ-02 | Header-aware platform-version resolution for `update-page` / `build-theme` (and optionally `get-component-info`): does the platform-version resolver factory (`PlatformVersionResolverFactory.Create(settings)`) have a path fed from `CredentialContext`, or must one be added — vs. simply emitting the documented `latest-fallback` flag (as `get-component-info` already does)? | Architect | ADR |
| OQ-03 | Reconcile `[Required]` `environment-name` (e.g. `sync-pages`, Application ctor args) with authorized-passthrough forbidding env args: make it conditionally required at the binding layer, relax `[Required]`, or gate at the middleware/pre-binding guard (ties to OQ-01c). | Architect | ADR |
| OQ-04 | Guard (FR-06) mechanism: Roslyn analyzer (with fixtures) vs. audited allowlist + per-tool behavior tests. Must exclude legitimate local `ISettingsRepository` users (`list-creatio-builds`). | Architect | ADR |
| OQ-05 | Is a passthrough-path **latency** target part of acceptance? If yes, define threshold + harness; if no, keep SM-03 functional-only (current default). | PM + Architect | ADR |

## Dependencies

- **Depends on / builds on**: ENG-93208 (PR #830, branch `claude/clio-mcp-multi-tenant-73a807`) — the entire
  passthrough infrastructure incl. the middleware error semantics (no-header forward; malformed → 400) and the
  resolver env-arg rejection. This task is meaningless without it and **must branch from and target it**.
- **Depends on**: `ToolCommandResolver`, `BaseTool`, `ICredentialContextAccessor`, `McpHttpServerCommand`,
  the class-c1 domain services + their env-scoped registration, `ISettingsRepository`, `IApplicationClient`,
  `PlatformVersionResolverFactory`.
- **Blocks**: ENG-92869 (AI-Platform integration) and ENG-92790 (end-to-end research).
- **Parent**: ENG-92790.

## Notes for the Architect (from PRD grounding — all code-verified)

- The **only** header-reading integration point is `ToolCommandResolver.Resolve` (`credentialContextAccessor.Current`,
  `:101`) and its cache-key/tenant-key siblings. Audit **per dependency-path**, not per tool: `update-page`
  proves a single tool can honor the header on its write path (`:61`) yet be header-blind on its version path (`:267`).
- Root cause of class-c1 is **root/bootstrap-container binding**, not one-time construction. Services and tools
  are `AddTransient` (`BindingsModule.cs:296,401`) and instantiated per call (`McpToolInvokerRegistry.cs:121`),
  but the domain service falls back to the root provider bound to the startup environment. Mirrors the ENG-93208
  "child container bypasses factory" finding: resolve per request against `CredentialContext`.
- Security is **three distinct modes** — do not flatten them. (i) c1 header-only = hard failure (services throw
  before repo, `ApplicationListService.cs:52-55` / `ApplicationInfoService.cs:120-123`). (ii) `get-user-culture`
  header-only = **real active-tenant leak, only when an active env is configured** (`FindEnvironment(null)`,
  `ConfigurationOptions.cs:638-652` / `:621-629`).
  (iii) header + env-name = confused-deputy, **conditional** on deployment facts.
- `get-component-info` is **already compliant** (loud `latest-fallback`, `ComponentInfoTool.cs:169,252`;
  `ComponentInfoToolTests.cs:606`) — do not "fix" it into a regression. `build-theme` is the genuinely
  header-blind, environment-version-sensitive one (authenticated resolver at `BuildThemeCommand.cs:251` →
  `PlatformVersionResolverFactory.cs:40`).
- Routing does **not** need a `Command<T>` wrapper: `Resolve<T>` is unconstrained (`:15`) and tools already
  resolve services through it (DataForge, `DataForgeEnrichmentBuilder.cs:41`). Consider a centralized
  pre-binding guard (OQ-01c) over per-tool edits.
- The FR-06 guard must be path-based; constructor reflection false-positives on `list-creatio-builds`
  (legitimate local `ISettingsRepository`). Reuse `CredentialPassthrough*Tests`, `ToolCommandResolver*Tests`,
  `McpHttpMultiTenantE2ETests` (only proves `describe-environment` across two tenants today, `:33`),
  `McpHttpConcurrencyIsolationE2ETests`, `McpHttpNoRegressionE2ETests`.
