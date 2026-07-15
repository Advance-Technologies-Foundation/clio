# How An External AI Sees The clio MCP Server

## Scope

This document describes the current `clio` MCP server from the point of view of an external AI client that discovers the server over MCP and decides what it can do with it.

The document is source-driven. It is based on the current assembly registration and MCP attributes in:

- `clio/BindingsModule.cs`
- `clio/Command/McpServer/Tools`
- `clio/Command/McpServer/Prompts`
- `clio/Command/McpServer/Resources`

Snapshot date: `2026-07-09`

## One-sentence summary

An external AI sees `clio` MCP not as a generic system shell, but as a curated Creatio engineering control plane with strong coverage for page editing, schema work, workspace sync, application lifecycle, deployment preflight, and local environment maintenance.

## Discovery Snapshot

Since the lazy-schema split (ENG-90312, PR #743) `tools/list` advertises only the **resident** profile
(~27 discovery/read tools + the executors); the full catalog (~137 invokable tools) stays reachable but
is discovered through `get-tool-contract`, not `tools/list`:

- `~27` resident tools in `tools/list` (see `McpCoreToolProfile`)
- the full invokable catalog (~137 tools) indexed by `get-tool-contract` (each entry carries `resident`
  and `destructive` flags, plus `aliases` when a legacy name maps to it)
- `67` prompts
- `92` resources
- `1` resource template

Important shape of the surface:

- Two transports: **stdio** (`clio mcp` / `mcp-server`) and **Streamable HTTP** (`clio mcp-http`), sharing the same tool surface. The HTTP host additionally offers the credential-passthrough edge (see the targeting-mode note above). The durable unmatched-name handling described below is registered on the stdio transport only.
- Registration goes through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (feature-toggle-aware; `IEnumerable<Type>` into `WithTools`/`WithPrompts`/`WithResources`) — the assembly-wide `*FromAssembly` helpers are no longer used. Discovery returns only the enabled surface: feature-gated tools, prompts, and resources are omitted while their feature flag is off, so the advertised counts reflect the default flag state.
- The tool layer is mixed-generation:
  - newer tools are strongly typed and usually expose `ReadOnly`, `Destructive`, `Idempotent`, and `OpenWorld`
  - older lifecycle tools still expose valid MCP tools, but without the same metadata richness

## Durable Invocation (forgiving unmatched-name handling, ENG-93370)

A `tools/call` naming a tool that is NOT advertised in `tools/list` no longer dead-ends. The stdio
server registers an unmatched-name handler (`McpDurableCallToolHandler` via the SDK's
`WithCallToolHandler`; stdio transport only) that restores the pre-lazy invocation contract:

- **Non-destructive real tool** → executed directly through the same dispatch path `clio-run` uses; the
  result carries a model-visible advisory in `Content` recommending the advertised
  `clio-run {"command":"<tool>","args":{…}}` path, plus a `durable-invocation` audit block in `_meta`.
- **Destructive real tool** → NEVER silently executed. Returns a structured `confirmation-required`
  outcome with a ready-to-retry `clio-run-destructive` call shape — reproducing the per-tool prompt the
  host applied when the tool was still advertised.
- **Renamed/deprecated name** → resolved through `McpToolCompatibilityCatalog` (the MCP analogue of the
  CLI hidden-alias policy; e.g. `restart-by-environmentName` → `restart-by-environment-name`). Catalog
  collisions fail at startup.
- **Unresolvable name** → a structured, machine-readable outcome instead of an opaque error:
  `unknown-tool` (with Levenshtein did-you-mean candidates and the `get-tool-contract` discovery hint),
  `feature-disabled`, `cli-verb-not-mcp-tool`, `deprecated-tool-alias`, or `foreign-command` — every
  outcome carrying a `correlation-id`.

The advertised `tools/list` surface is unchanged by this handler (context economy preserved), and
shipped workspace templates are guarded against naming non-resident tools imperatively by
`WorkspaceTemplateGuidanceDriftTests` (resident-or-bridged oracle).

## What This MCP Fundamentally Is

From a third-party AI perspective, this MCP server is optimized for five kinds of work:

1. Operating a registered Creatio environment
2. Editing Creatio artifacts such as Freedom UI pages, entities, lookups, user tasks, and data bindings
3. Managing local workspaces and package source synchronization
4. Running deployment and infrastructure workflows around local Creatio installations
5. Looking up command help and execution guidance inside the same domain

What it is not:

- not a generic file browser MCP
- not a generic shell/terminal MCP
- not a generic HTTP/browser/web-search MCP
- not an open-world integration hub

For an external AI, this means the surface is narrow but deep: fewer raw primitives, more domain-specific actions.

## How Targeting Works

The MCP surface supports three main execution modes.

### 1. Registered environment mode

Many tools accept `environment-name` and resolve a target from local clio settings. This is the default and most consistent path.

Typical examples:

- `get-page`
- `list-apps`
- `push-workspace`
- `compile-creatio`
- `restore-db-by-environment`

### 2. Explicit connection mode

Some tools accept direct connection arguments such as `uri`, `login`, and `password`. Older lifecycle tools may also require `userName` and `isNetCore`.

Typical examples:

- `get-page`
- `update-page`
- `delete-app`
- `clear-redis-db-by-credentials`
- `restart-by-credentials`

### 3. Pure local mode

Some tools operate only on the local machine or on local curated data and do not require a target Creatio environment.

Typical examples:

- `component-info`
- `create-workspace`
- `list-environments`
- `find-empty-iis-port`

### 4. HTTP credential-passthrough edge (multi-tenant) + standard OAuth authorization

The `mcp-http` HTTP host adds a fourth, opt-in targeting mode: **per-request credential
passthrough**. Instead of a pre-registered environment, a gateway supplies the target tenant
URL, credentials, and an explicit `isNetCore` runtime boolean on each request via an
`X-Integration-Credentials: <base64 JSON>` header. `true` selects the root .NET Core/NET 8
routes; `false` selects the .NET Framework layout with exactly one `/0/` segment. The runtime
field is required, is matched case-insensitively by property name, and is header context
rather than an MCP tool argument. Missing or non-boolean values are rejected with HTTP 400
before target validation, client creation, or outbound calls; clio never defaults or probes the
tenant runtime. The runtime is part of the in-memory cache and lock identity.
Most of the registered tool surface then executes against an **ephemeral, in-memory** per-tenant
container (nothing persisted; pooled with idle-TTL / LRU eviction) — but not every tool honors
the header yet: see "Per-tool passthrough support (ENG-93347)" immediately below for the audited
exceptions.

`mcp-http` also supports **standard MCP OAuth 2.1 Resource-Server authorization**
(`--auth-authority`; off by default): when configured, EVERY request to the endpoint —
passthrough and pre-registered `-e <env>` access alike — requires a valid bearer JWT, and the
edge serves Protected Resource Metadata (RFC 9728) at `/.well-known/oauth-protected-resource`
for discovery. The legacy `--platform-api-key` gate is retained only as a non-OAuth dev/offline
fallback: with `--auth-authority` configured it is bypassed entirely (the two schemes cannot
share one `Authorization` header); with no OAuth configured (default) it is the sole gate for
the passthrough leg, fail-closed and off by default — with no key configured the credential
header is ignored and `mcp-http` behaves as stdio-parity. The inbound MCP/gateway bearer token
is never forwarded to Creatio; the tenant credential is a separate, distinct plane. See
[`docs/commands/mcp-http.md`](../clio/docs/commands/mcp-http.md) for the full contract (header
shapes, SSRF allowlist, the mode-gated plaintext-arg policy, and the OAuth option reference).

#### Per-tool passthrough support (ENG-93347)

ENG-93347 audited every resident tool that reaches — or derives target-specific information
from — a Creatio environment, and brought each into one of two states below. This is **not**
the PRD's full out-of-scope audit; the remaining ~135 tools were already passthrough-capable
before this feature (class a/b in the PRD's classification: they already resolve their
target-scoped service per request through `IToolCommandResolver`) or are not
environment-sensitive at all (telemetry, guidance, local infra, `list-environments`, etc.).

**Passthrough-supported** — executes against the header tenant; `environment-name` (and, where
the tool accepts them, `uri`/`login`/`password`) becomes optional, and supplying it **together**
with an active passthrough header is rejected ("not accepted when credential passthrough is
enabled") rather than silently honored:

- `list-apps`, `get-app-info`, `create-app`, `create-app-section`, `update-app-section`,
  `delete-app-section`, `list-app-sections` — the application-lifecycle family, including every
  nested lookup each tool performs (caption-culture resolution, polling/readback).
- `get-user-culture` — profile-culture lookup; previously the one real active-tenant data leak
  in this audit (a header-only call with no active environment configured would silently read
  the configured active environment's culture with its stored credentials), now closed.
- `update-page`, `sync-pages` — the platform-version probe that scopes chart-widget/component
  validation to the target's real version. Each tool's page-write path was already
  passthrough-capable before this feature.
- `get-component-info` — the `environment-name`/`uri` (mixed-input) path. The header-only,
  no-argument path was already compliant before this feature (documented `latest-fallback`).
- `build-theme` — the version-resolution probe only. Falls back **soft** (not an error) to the
  newest bundled template when no header-derived tenant is available, and on mixed input (a
  header plus an explicit `environment-name`) — never a header-blind name lookup. The soft
  fallback is not silent: when the caller explicitly named an `environment-name` that could not
  be resolved, the result carries a non-fatal `warnings` entry naming the environment and the
  newest-version fallback (the resolution catch is scoped to `EnvironmentResolutionException`, so
  an unexpected fault surfaces as a real error rather than a silent newest-version build).

**Passthrough-unsupported** — fails fast with one uniform error naming the tool and the
alternative (register the target environment and use the stdio path, or a non-passthrough
`mcp-http` request), returned **before** any Creatio-reaching call:

- `link-from-repository-by-environment`
- `link-from-repository-unlocked`
- `link-from-repository-by-env-package-path` — **except** its local-only `skip-preparation=true`
  branch, which never reaches Creatio and is unaffected by passthrough.

These three remain unsupported by design: the environment name doubles as a local
package-directory selector with no passthrough equivalent, and routing was judged
disproportionate for v1 (see the ADR's decision matrix for `link-from-repository-*`).

## What An AI Learns About Execution Semantics

The external AI does not just discover tool names. The source gives it a specific execution model.

### Serialized execution

Most tool execution flows are serialized through a shared lock in `BaseTool` or an equivalent lock path. In practice, the server behaves like a single-lane control plane, not a parallel action bus.

Implication for external AI:

- do not assume safe parallel writes
- batch tools are valuable because they reduce repeated lock acquisition and round trips

### Environment-aware command resolution

Environment-sensitive tools do not simply reuse one startup-time command instance. They resolve per-target command instances through `IToolCommandResolver`.

Implication for external AI:

- repeated calls against the same environment are stable
- session-like reuse exists for cached target containers
- switching target credentials or environment names meaningfully changes execution context

### Mixed response models

The external AI sees two broad response styles.

Structured domain responses:

- `get-page`
- `list-pages`
- `sync-pages`
- `component-info`
- `list-apps`
- `get-app-info`
- `find-app`
- `create-app`
- `delete-app`
- `assert-infrastructure`
- `show-passing-infrastructure`
- `get-entity-schema-properties`
- `get-entity-schema-column-properties`

Generic command envelopes:

- many local/devops tools return `CommandExecutionResult`
- that shape is mostly `exit-code`, `execution-log-messages`, and optional `log-file-path`

Implication for external AI:

- newer design surfaces are easier to automate semantically
- older execution surfaces require log parsing and success/failure inference from generic output

### MCP tool exit codes

The `exit-code` on a `CommandExecutionResult` envelope follows one contract across every
environment-aware tool (the shared `BaseTool` execution path):

| `exit-code` | Meaning | Examples |
|---|---|---|
| `0` | Success | the command ran and reported success |
| `1` | **Expected, caller-actionable failure** — bad input or an unmet precondition the caller can fix | unknown environment name, missing URI, empty required argument, a required package (e.g. `cliogate`) not installed |
| `-1` | **Unexpected runtime failure** — an exception the caller could not have anticipated | DI/bootstrap/wiring errors, a failed HTTP/verification call, an exception thrown inside the command |

The split lets an external AI distinguish "I sent something wrong, let me correct and retry" (`1`)
from "clio itself failed, retrying the same call won't help" (`-1`). Source of truth:
`CommandExecutionResult.FromValidationError` / `FromResolverError` (code `1`) versus
`FromError` / `FromException` (code `-1`); a deliberate environment failure is raised as
`EnvironmentResolutionException` so it is never conflated with an unexpected `InvalidOperationException`
from the DI container.

#### Version gate (exit 78)

A command declaring `[RequiresCreatioVersion]` adds one more **expected, caller-actionable** outcome
on top of the `0`/`1`/`-1` contract: when the target environment runs an older core version than the
command's floor — or its version is undeterminable (the gate fails closed) — the `BaseTool` path
returns the distinct `exit-code` `78` (`Program.CreatioVersionRequirementExitCode`) with the stable
`CreatioVersionRequirementException.ErrorCode` (`version-too-old` / `version-undeterminable`) embedded
in the message. Typed-response tools (`create-theme`, `list-themes`, `check-theming-access`) carry no
exit code, so they refuse with `{ success: false, error }` where the same stable ErrorCode travels in
the `error` message. The gate is enforced at the shared `ResolveCommand` chokepoint, ordered before
the package gate — the same relative precedence as the CLI dispatch gate (feature-toggle →
creatio-version → package).

The refusal path is unit-proven; at the end-to-end level each gated tool's contract is asserted to
advertise its floor. A live e2e refusal test requires an environment below the floor, which the
harness does not provision — that remains the one documented gap.

### Workspace path rules

Workspace-oriented tools validate local absolute paths and reject network paths.

Implication for external AI:

- `workspace-path` is not optional sugar
- many local development flows are intentionally anchored to a real checked-out workspace on disk

## Capability Map By Domain

### 1. Freedom UI Page Engineering

This is one of the strongest and most AI-friendly parts of the MCP surface.

- `list-pages`
  Discover candidate Freedom UI pages by package or schema pattern.
- `get-page`
  Read a page as a merged bundle plus raw editable JavaScript body.
- `update-page`
  Write a full page body back to Creatio, optionally in `dry-run` mode.
- `sync-pages`
  Save many pages in one call with optional validation and optional read-back verification.
- `component-info`
  Inspect a shipped local catalog of Freedom UI component contracts, grouped by category or returned in detail mode.

What an external AI can practically do here:

- discover pages before editing
- read the effective merged page structure
- inspect unfamiliar `crt.*` component types without guessing
- rewrite page bodies directly
- batch page saves to reduce MCP chatter

What makes this area especially good for AI:

- `get-page` returns both high-level bundle data and raw editable body
- `sync-pages` includes client-side validation and optional verification
- `component-info` is local, deterministic, and does not require a live Creatio target

### 2. Application Lifecycle In Creatio

This area gives the AI a clean application-level view of the platform.

- `list-apps`
  Return installed applications as structured JSON.
- `get-app-info`
  Return application context, packages, and related metadata for a single installed app.
- `find-app`
  Find applications and their sections in a single call by name, code, or substring pattern. Maps an imprecise app name to its real code without an N+1 `list-apps` + per-app `list-app-sections` scan.
- `create-app`
  Create a Creatio application and return its structured context.
- `create-app-section`
  Add a section to an existing installed application; returns the created section, entity, and page readback.
- `update-app-section`
  Update metadata (caption, description, icon) of an existing section; returns before/after readback.
- `delete-app-section`
  Remove a section from an existing application; returns the deleted-section readback.
- `list-app-sections`
  List the sections of an existing installed application.
- `delete-app`
  Uninstall an application by name or code.
- `install-application`
  Install an application package into a target environment.
- `add-package-dependency`
  Add one or more package dependencies to a package via `PackageService.svc`. This is the recovery path when the schema designer or compiler fails for a package that extends objects owned by an app/package missing from its dependency list (classic symptom: `GetSchemaDesignItem returned an HTML error page` on a layered object). Idempotent — re-adding an existing dependency is a no-op. See `get-guidance name=package-dependencies`.
- `remove-package-dependency`
  Remove one or more package dependencies from a package via `PackageService.svc` — the symmetric counterpart of `add-package-dependency`, used to roll back a dependency added only to unblock the schema designer. Matched by name (case-insensitive); idempotent — removing an absent dependency is a no-op.

What an external AI can practically do here:

- enumerate installed apps
- map a fuzzy or partial app name to its real code (and see its sections) in a single call
- inspect one app precisely before modifying or replacing it
- create new apps from a template
- add, update, list, and remove sections inside an existing app
- remove apps
- install packaged apps into an environment
- add missing package dependencies to recover a broken schema designer or compile

The AI sees this as a higher abstraction layer than package-level commands.

**Long-running / progress contract.** `create-app`, `create-app-section`,
`update-app-section`, `delete-app-section`, `list-app-sections`, and `get-app-info`
call the Creatio backend synchronously and can take minutes on a cold or busy
environment. They emit `notifications/progress` on a fixed cadence (default 15 s,
overridable via the `CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS` environment variable) so MCP
clients reset their inactivity timeout. A progress notification means the server is
still working — the AI must await completion and must not retry or fall back to raw SQL
or manual UI on a perceived client timeout.

### 3. Entity, Lookup, And Schema Design

This is the second major design-oriented surface after page tools.

- `create-entity-schema`
- `create-lookup`
- `update-entity-schema`
- `modify-entity-schema-column`
- `get-entity-schema-properties`
- `get-entity-schema-column-properties`
- `sync-schemas`

What an external AI can practically do here:

- create entities directly in a remote package
- create explicit lookup schemas
- read structured schema metadata before mutating
- mutate one column or a whole schema batch
- execute composite schema changes in one call

Why `sync-schemas` matters:

- it reduces round trips
- it batches create/update/seed actions
- it is a better fit for agents that want one atomic plan execution instead of many tiny tool calls

### 4. User Task Engineering

This is a focused but meaningful workspace-backed capability.

- `create-user-task`
  Create a user task in a workspace package and build the package.
- `modify-user-task-parameters`
  Add, remove, or update parameters on an existing user task.

What an external AI can practically do here:

- create a new user task as part of solution delivery
- evolve the parameter contract after creation

Important constraint:

- these tools are workspace-backed, not pure remote schema helpers

### 5. Data Binding And Seed Data

The surface exposes both local package-first and remote DB-first data binding flows.

Local package binding tools:

- `create-data-binding`
- `add-data-binding-row`
- `remove-data-binding-row`

DB-first binding tools:

- `create-data-binding-db`
- `upsert-data-binding-row-db`
- `remove-data-binding-row-db`

What an external AI can practically do here:

- create seed data artifacts in package form
- edit individual binding rows locally
- create and mutate bindings directly against a remote database-backed workflow

How an AI should interpret this:

- the server supports both source-driven and remote-first data seeding strategies
- the AI can choose between reproducible local artifacts and direct environment mutation

### 6. Workspace Bootstrap And Local Delivery

This part makes the MCP useful for real development work, not just runtime inspection.

- `create-workspace`
- `add-package`
- `new-test-project`
- `download-configuration-by-environment`
- `download-configuration-by-build`
- `link-from-repository-by-environment`
- `link-from-repository-by-env-package-path`
- `pkg-to-file-system`
- `pkg-to-db`
- `push-workspace`
- `restore-workspace`
- `delete-schema`
- `add-item-model`
- `generate-process-model`

What an external AI can practically do here:

- create an empty local workspace
- add new packages into that workspace
- create a test project for a package
- hydrate `.application` from an environment or build archive
- link repository contents into a live environment package directory
- sync package storage between DB and file system
- push local source to Creatio
- restore local source from Creatio
- delete a schema while validating workspace ownership
- generate model code from environment artifacts

This is where the MCP stops being a pure remote API and becomes a source-delivery assistant.

### 7. Environment Registry And Local Configuration

This part is small but important because many other tools depend on it.

- `reg-web-app`
  Register or update a local clio environment definition.
- `list-environments`
  Return registered local environments and settings as structured JSON.
- `get-pkg-list`
  Read remote package inventory from the selected environment.
- `get-user-culture`
  Resolve the logged-in user's profile culture (e.g. `en-US`, `uk-UA`) from
  `ApplicationInfoService.svc/GetApplicationInfo` (no cliogate). Read-only, non-destructive.
  Returns `{ success, culture, resolvedFrom, reason }`. Call once per session before creating
  entities and reuse it for all generated names/labels/captions; on `success:false` ask the user
  which language to use instead of silently defaulting.
- `describe-environment`
  Describe a Creatio environment as ONE source-independent report (read-only). The field set is the
  same with or without cliogate: `coreVersion` plus locale/user/workspace/maintainer metadata is
  ALWAYS reported (`ApplicationInfoService`, session only); `dbEngineType`, `frameworkKind` and
  `frameworkDescription` are added WITHOUT cliogate via the admin-gated
  `GetSystemEnvironmentInfo` (needs `CanManageSolution`); `productName` and `licenseInfo` are added
  only when cliogate `>= 2.0.0.32` is installed. Best-effort: an unavailable source is skipped and
  the call still succeeds. The required base probe returns classified, secret-safe Error logs for
  invalid/unavailable targets, authentication failures, non-Creatio content, and unusable Creatio
  responses. Read `get-guidance name=describe-environment` for the full field catalogue.

What an external AI can practically do here:

- onboard a target environment into clio configuration
- inspect what the local machine already knows about environments
- inspect package inventory before choosing install, page, or schema operations
- detect the profile language to apply to created entity names/labels/captions
- read a target environment's version, database engine, framework, product and license

Important note:

- `list-environments` explicitly returns unmasked settings, which is powerful but sensitive
- dbHub installation and source reconciliation are intentionally CLI-only. `install-dbhub` creates or repairs
  a current-user Scheduled Task, while `sync-dbhub` reads secret-bearing local database configuration and mutates
  a workstation TOML file. Neither operation is exposed as an MCP tool without a concrete authorization model.

### 8. Deployment, Restore, And Infrastructure Preflight

This is the ops-heavy part of the MCP surface.

- `assert-infrastructure`
- `show-passing-infrastructure`
- `find-empty-iis-port`
- `deploy-creatio`
- `restore-db-by-environment`
- `restore-db-by-credentials`
- `restore-db-to-local-server`
- `get-fsm-mode`
- `set-fsm-mode`
- `compile-creatio`
- `uninstall-creatio`

What an external AI can practically do here:

- inspect infra readiness before deployment
- derive recommended deployment choices from passing infra only
- choose a safe local IIS port
- deploy Creatio from an archive
- restore a database in several targeting modes
- toggle FSM mode and then compile
- fully uninstall a local Creatio instance

How the AI should think about this area:

- this is not just build/deploy
- it is a local-host and target-environment control surface
- destructive power is high, especially for restore and uninstall flows

**Typed stage-event progress contract (`deploy-creatio` / `uninstall-creatio`).** Both tools
emit a versioned, typed progress stream over MCP `notifications/progress` in the
`_meta.clioStageEvent` field, so a GUI consumer (the clio-ring guided-deploy UX) can render a
live, GitHub-Actions-style step list instead of parsing log lines. The stream is:

- one `manifest` event up front listing every stage that will run, in order;
- a `stage` event per transition (`running` → `done` / `failed` / `warning` / `skipped`, carrying
  `index` / `total` / `durationMs`);
- one terminal `run-completed` event with `outcome` = `success` / `failure`.

Deploy stages: `stage-build` (network-source only; otherwise `skipped` `not-applicable`) →
`unzip` → `copy-files` → `restore-db` → `deploy-app` → `configure-conn-strings` →
`register-env` → `wait-ready`. Uninstall stages: `read-config` → `stop-iis` → `delete-iis` →
`drop-db` → `delete-files` → conditional `delete-apppool-profile` → `unregister` (final).
The profile stage resolves only the registered `IIS APPPOOL\<name>` SID/profile. Missing and
non-Windows profiles are `skipped` `not-applicable`; exhausted deletion retries emit `warning`
with `APPPOOL_PROFILE_DELETE_FAILED`, while the tool keeps exit code 0, returns `IsError=false`,
and terminates with `success-with-warnings`; the warning stage retains the detail.
The target IIS site/application is validated before removal. Pool assignments are rechecked after
target deletion; a pool still used by another application and its Windows profile are preserved,
and the profile stage is `skipped` `not-applicable`.
Failure is honest: a stage that fails is emitted `failed`, the remaining stages `skipped`
(`after-failure`), and the run ends `run-completed` `failure` — a non-zero stage result is
never masked as success. The envelope is stamped with `schemaVersion` (currently `1`), is
purely additive (tool arguments, descriptions, and `Destructive` flags are unchanged), and is
forward-compatible: an unknown field or a bumped schema version does not break a mirrored
consumer.

### 9. Runtime Control And Maintenance

These tools are operational rather than design-oriented.

- `start-creatio`
- `stop-creatio`
- `stop-all-creatio`
- `restart-by-environment-name`
- `restart-by-credentials`
- `clear-redis-db-by-environment`
- `clear-redis-db-by-credentials`

What an external AI can practically do here:

- start and stop environments
- restart an instance through a registered environment or explicit credentials
- flush Redis caches
- stop all local Creatio instances

Special behavior:

- `start-creatio` and `stop-creatio` are wired for MCP progress notifications

### 10. Browser Session Handoff

These tools let an external AI obtain an authenticated Creatio browser session so it can verify UI
changes in a real browser without ever seeing the login page.

- `get-browser-session` (`ReadOnly=false`, `Destructive=false`, `Idempotent=false`) — authenticates against a forms-auth environment and returns the absolute path to a Playwright-compatible `storageState` file in `session-file-path`. Args: `environment-name` (required), `force-refresh` (optional).
- `clear-browser-session` (`ReadOnly=false`, `Destructive=true`, `Idempotent=true`) — deletes the cached session so the next call re-authenticates. Args: `environment-name` (required).

What an external AI can practically do here:

- get a `storageState` file path and feed it to Playwright's `storageState` option to open Creatio already logged in
- force a fresh login or clear a stale session

Important behavior and safety:

- Cookie values are **never** returned over MCP — only the file path. The session file is written owner-only on disk.
- `output-path` is intentionally CLI-only and not exposed on the MCP surface (an agent cannot redirect the bearer-token file).
- Forms-auth environments only (login + password). OAuth-only environments return `success=false` with an error — there is no OAuth token-to-cookie exchange.
- A Safe-flagged environment in the non-interactive MCP context fails closed with a structured error instead of hanging the stdio server.
- `open-web-app --authenticated` (Mode A — launches a local desktop browser already signed in via CDP cookie injection) is intentionally **CLI-only and not an MCP tool**: it opens a GUI window on the operator's machine, which is meaningless for a headless/remote MCP server. The agent-facing surface for an authenticated session is `get-browser-session` (returns a `storageState` path); Mode A is a human convenience built on the same session machinery.

### 11. Business Process Modeling

These tools help an external AI design Creatio business processes (BPMN). clio makes no LLM call —
an MCP prompt/guidance teaches the agent the intent→BPMN translation; deterministic tools execute.
All tools in this section except `get-process-signature` are gated behind the `process-designer`
feature toggle and require the `clioprocessbuilder` (ProcessDesignService) package on the target
environment.

- `create-business-process` (`ReadOnly=false`, `Destructive=false`, `Idempotent=false`, `OpenWorld=false`, **environment-sensitive**) — builds a NEW process from a declarative JSON descriptor (`name`, `caption`, `packageName`, `elements[]`, `flows[]`, `parameters[]`, `mappings[]`) and saves it server-side in one call; diagram layout is automatic. The buildable slice is startEvent/signalStart/endEvent/userTask joined by plain sequence flows; unsupported elements are rejected with a clear message, and a signal start carries no record filter (it fires for every record of its object).
- `modify-business-process` (`ReadOnly=false`, `Destructive=true`, `Idempotent=false`, `OpenWorld=false`, **environment-sensitive**) — edits an EXISTING process (by `process-name` or `process-uid`) with an ordered operations array: addElement / removeElement / addFlow / removeFlow / addParameter / addMapping / setParameter / removeParameter. Atomic: any failed operation aborts the whole edit (nothing is saved). Removals are not structurally validated and every edit re-lays-out the whole diagram; re-sending addMapping overwrites a binding in place (there is no removeMapping/clear op).
- `list-user-tasks` (`ReadOnly=true`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false`, **environment-sensitive**) — returns the environment's user-task palette (built-in + custom; name + UId) for `userTaskName` selection when building a process.
- `validate-process-graph` (`ReadOnly=true`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false`, **environment-sensitive**) — validates a planned process graph (`nodes` by `data-id`, `edges` by `flow-kind` = sequence|conditional|default) against the BPMN connection rules R1–R17 (enforced subset: R1–R3, R7, R9–R15, R17). The graph is validated **in-memory**, but the tool first resolves the target environment (named by `environment-name`) and queries its installed packages to require the `clioprocessbuilder` package. Returns structured findings (`severity` error/warning, `rule-id`, `message`, `node-name`/`source`/`target`). A validation pass does NOT imply buildability — the rules cover the full BPMN catalog while the builder covers only the slice above.
- `describe-business-process` (`ReadOnly=true`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false`, **environment-sensitive**) — reads an existing process and returns a STRUCTURED graph (`elements` `[{name,uid,caption,type,buildType,userTaskName,parameters,signal?}]`, `flows` `[{source,target,kind}]`, process `parameters`) instead of raw escaped metadata, so the agent can explain what a process does ("read & explain", the inverse of generation). Identify the process by exactly one of `process-name` / `process-uid` / `process-caption` (+ `environment-name`, optional `culture`). Each parameter carries `direction` and `isResult` (detect outputs by `isResult`); parameter values carry their `source` (ConstValue/Mapping/Script) and raw `expression` — expressions are returned verbatim, not decoded into semantics. Unbound element inputs are omitted.
- `get-process-signature` (`ReadOnly=true`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false`, **environment-sensitive**) — reads a process's parameter signature (codes, captions, CLR types, direction, lookup reference schema). Shipped and NOT feature-gated: it reads the built-in DataService, not ProcessDesignService. Primary workflow: the `run-process-button` guidance.

What an external AI can practically do here:

- pre-check a planned process graph for invalid connections (start with an incoming flow, default without a sibling conditional, orphan/unreachable nodes, etc.) before building anything
- build or edit the supported slice declaratively and verify the result with a describe read-back
- pair with the `process-modeling` guidance (`get-guidance name=process-modeling`) for the element catalog + rules

Companion surfaces (see the `process-modeling` guidance):

- `get-guidance name=process-modeling` — the BPMN element catalog, connection rules, and the validate-then-drive recipe.
- `generate-process-model` — reads an existing process into a C# model (existing tool).

### 12. Theming

These tools brand a Creatio app: build a custom theme from brand colours and fonts, apply it to an environment, and manage the theme catalog. `build-theme` and `advise-theme-palette` run offline; the rest act on a registered environment (`environment-name`) via the native ThemeService, which requires Creatio 10.0.0 or later — on an older (or version-undeterminable) environment they refuse with the version-gate error (see "Version gate (exit 78)"). All theming tools take a single `args` object with kebab-case fields.

- `build-theme`
  Render a theme's `theme.css` (and, in workspace mode, `theme.json`) from a primary colour, optional secondary/accent/system colours, and fonts, over a bundled version-pinned template. Writes into a workspace package when given `workspace-directory` + `package-name`, otherwise returns the CSS. Never mutates an environment.
- `advise-theme-palette`
  Stateless offline advisor that scores brand-colour choices (readability on white, accent similarity) and returns a verdict per operation, so the agent never judges a colour by eye.
- `create-theme`
  Create a theme on the environment from inline `css-content` plus a caption.
- `update-theme`
  Full overwrite of an existing theme by id (caption, CSS class name, CSS content).
- `delete-theme`
  Delete a theme by id; deleting an unknown id is an error.
- `list-themes`
  List custom themes (id, caption, CSS class name, CSS file path). An empty list means no themes or no `CanCustomizeBranding` license.
- `clear-themes-cache`
  Refresh the theme catalog cache; needed only when theme files change on the environment outside a clio install.
- `check-theming-access`
  Report whether the caller has the `CanManageThemes` operation and `CanCustomizeBranding` license, to gate authoring on a real permission check.

What an external AI can practically do here:

- build a theme offline (`build-theme`) with `advise-theme-palette` driving the palette, then commit it to a workspace package and push, or apply it directly with `create-theme`
- restyle, remove, and confirm themes on an environment
- precheck theming permissions before authoring, and set the default via the `DefaultTheme` system setting (see the theming guidance)

Companion surfaces (see the `theming` guidance):

- `get-guidance name=theming` — the palette conversation, the build step, and the workspace/dev vs no-code/server delivery flows.

## Prompt Layer: What The AI Gets Beyond Raw Tools

The prompt layer does not add new execution power, but it materially changes how an external AI can reason about the surface.

The prompt layer acts as embedded operating guidance:

- page prompts teach a workflow: `get-tool-contract` -> `list-pages` -> `get-page` -> `component-info` -> `update-page` or `sync-pages`
- deployment prompts teach a workflow: `assert-infrastructure` -> `show-passing-infrastructure` -> `find-empty-iis-port` -> `deploy-creatio`
- FSM prompts encode the operational rule that mode changes should be followed by full compilation
- workspace prompts tell the AI when absolute local paths are required
- help lookup prompt teaches the AI how to read CLI help through resources
- schema and application prompts now point to a shared modeling guide for DB-first app creation, lookup design, defaults, batch-first workflows, and contract bootstrap through `get-tool-contract`

Important observation:

- prompts are workflow-oriented, not purely tool-oriented
- some prompts cover multiple tools in one narrative
- some tool families are guided indirectly rather than with one prompt per tool

## Resource Layer: What The AI Can Read

The MCP resource surface is still small, but it now has one MCP-native guidance article in addition to CLI help.

- `docs://help/command/{commandName}`
  Generic command help lookup by CLI verb name or alias
- `docs://help/restart`
  Dedicated help resource for the restart command
- `docs://help/flushdb`
  Dedicated help resource for Redis flush help
- `docs://mcp/guides/app-modeling`
  Canonical modeling guide for DB-first app creation, lookup behavior, default semantics, and batch-first page/schema workflows
- `docs://mcp/guides/theming`
  Canonical MCP guidance for managing custom Creatio themes with clio — create, restyle, delete, list, and set the default — and shipping them to a Creatio environment

How an external AI should interpret resources:

- most resources are command-centric, but the modeling guide is intentionally cross-tool
- they are good for understanding CLI semantics
- they can now also carry stable MCP-owned workflow guardrails that would otherwise be duplicated in consumer AGENTS instructions

That means prompts, tool descriptions, and the modeling guide together carry the MCP-specific guidance surface.

## Instruction Ownership: What Should Move Into clio MCP

The most stable guidance candidates for MCP ownership are the instructions that describe the `clio` contract itself rather than one consumer repository's orchestration policy.

Good MCP-level candidates:

- transport and invocation contract such as stdio-only execution, exact discovered tool names, and kebab-case JSON argument naming
- DB-first semantics for schema tools, including the fact that successful schema mutations are immediately usable without a compile step
- batch-first workflow guidance such as preferring `sync-schemas` and `sync-pages` when the plan is already known
- application modeling rules tied directly to tool behavior, especially that `create-app` typically returns the canonical main entity for single-record-type apps
- lookup modeling rules tied directly to `create-lookup`, especially `BaseLookup` inheritance, inherited `Name` / `Description`, and `Name` as the display field
- default-value semantics that are tool-domain specific, such as seed rows not satisfying a `defaults to X` requirement without explicit schema or UI defaults
- page modification workflow guidance tied to MCP tools, for example `get-tool-contract` -> `list-pages` -> `get-page` -> `component-info` -> `update-page` or `sync-pages`

Guidance that should stay outside clio MCP:

- business-analysis flow, approval gates, and required plan document formats
- repository-specific artifact layout such as `output/<AppName>/...`
- consumer-specific evidence rules, sync-pages plan embedding, and final-report formatting
- repository naming policies that are not universally true for clio users, such as enforcing one custom prefix for every package, page, entity, and column
- orchestration-only concerns such as stale workflow state handling, internal script usage, and when the user should or should not see implementation internals

## External-AI Strengths Of This MCP

From an external AI point of view, the strongest aspects are:

- rich domain specificity for Creatio development instead of generic low-level primitives
- high-value read/write page tooling
- strong schema-design surface with both granular and batch operations
- built-in deployment preflight reasoning path
- local curated `component-info` catalog for Freedom UI work
- explicit workspace-aware flows for real source delivery
- broad use of structured responses in the newer tool families

## External-AI Friction Points And Rough Edges

This surface is powerful, but a third-party AI will still notice several inconsistencies.

### 1. ~~Mixed naming styles~~ (Fixed)

All tool names now use strict kebab-case. Previous inconsistencies such as
`restart-by-environmentName`, `StopAllCreatio`, and `show-webApp-list` have been
corrected to `restart-by-environment-name`, `stop-all-creatio`, and
`show-web-app-list` respectively. The tool has since been renamed again to its current
MCP name, `list-environments` (`show-web-app-list`/`show-web-app`/`env`/`envs` remain valid
CLI-only aliases of the `list-environments` verb, per `Commands.md`).

### 2. Mixed argument styles

Newer tools usually accept one structured args object with hyphenated JSON keys.

Older tools may expose flat scalar arguments such as:

- `environmentName`
- `userName`
- `isNetCore`
- `workspaceName`

Implication:

- external AI should inspect each tool schema literally
- argument-name conventions are not globally uniform

### 3. ~~Mixed metadata richness~~ (Fixed)

All lifecycle tools now declare explicit safety metadata (`ReadOnly`, `Destructive`,
`Idempotent`, `OpenWorld` flags). This includes:

- `start-creatio`
- `stop-creatio`
- `stop-all-creatio`
- `restart-by-environment-name`
- `restart-by-credentials`
- `clear-redis-db-by-environment`
- `clear-redis-db-by-credentials`
- `clear-themes-cache`
- `list-themes`
- `create-theme`
- `update-theme`
- `delete-theme`
- `check-theming-access`

### 4. Mixed response shapes

Some tools are semantically typed.
Some are execution-log envelopes.

Implication:

- tool-calling planners should classify tools before using them in fully autonomous loops

### 5. Strong local-machine assumptions

Many workflows assume:

- local clio config exists
- absolute local workspace paths exist
- target environments are reachable from the local machine
- the MCP client can spawn a local stdio server

Implication:

- this MCP is ideal for local editor agents
- it is weaker for fully remote hosted agents with no local machine context

## Recommended Mental Model For Third-Party AI Integrations

If you are integrating another AI client with this MCP, the most reliable mental model is:

1. Treat `clio` MCP as a domain-specific Creatio operations API, not as a generic automation substrate.
2. Start with discovery and group tools into page, schema, workspace, application, infra, and runtime buckets.
3. Prefer the structured read tools before mutating anything.
4. Prefer batch tools such as `sync-schemas` and `sync-pages` when the workflow is already known.
5. Use prompts as recipes, not just as help text.
6. Use resources both for CLI help and for stable cross-tool modeling guidance such as `docs://mcp/guides/app-modeling`.
7. Expect destructive power and local-machine side effects in many tools.

## Most Natural AI Workflows

The current surface is especially well shaped for these workflows:

- Freedom UI editing
  Discover a page, inspect it, inspect unfamiliar component contracts, then save the new raw body.
- Entity and lookup delivery
  Read schema state, apply column updates, or batch multiple schema actions through `sync-schemas`.
- Workspace delivery
  Create a workspace, add a package, hydrate configuration, push changes, and restore when needed.
- Deployment preflight
  Assert infra, filter to passing choices, choose an IIS port, then deploy.
- Operational maintenance
  Start, stop, restart, flush Redis, restore DB, compile, and toggle FSM mode.

## Source Of Truth For Future Updates

If this capability map drifts, the correct places to re-audit are:

- `clio/BindingsModule.cs`
- `clio/Command/McpServer/Tools/*.cs`
- `clio/Command/McpServer/Prompts/*.cs`
- `clio/Command/McpServer/Resources/*.cs`
- `clio.tests/Command/McpServer/*.cs`
- `clio.mcp.e2e/*.cs`

That is the real public surface. Everything else is commentary.
