# How An External AI Sees The clio MCP Server

## Scope

This document describes the current `clio` MCP server from the point of view of an external AI client that discovers the server over MCP and decides what it can do with it.

The document is source-driven. It is based on the current assembly registration and MCP attributes in:

- `clio/BindingsModule.cs`
- `clio/Command/McpServer/Tools`
- `clio/Command/McpServer/Prompts`
- `clio/Command/McpServer/Resources`

Snapshot date: `2026-03-26`

## One-sentence summary

An external AI sees `clio` MCP not as a generic system shell, but as a curated Creatio engineering control plane with strong coverage for page editing, schema work, workspace sync, application lifecycle, deployment preflight, and local environment maintenance.

## Discovery Snapshot

From MCP discovery, the surface currently exposes:

- `60` tools
- `50` prompts
- `4` resources
- `53` tools with explicit safety metadata
- `7` legacy operational tools without explicit `ReadOnly` / `Destructive` / `Idempotent` flags

Important shape of the surface:

- Transport is stdio only.
- Registration is assembly-wide via `WithToolsFromAssembly`, `WithPromptsFromAssembly`, and `WithResourcesFromAssembly`.
- The tool layer is mixed-generation:
  - newer tools are strongly typed and usually expose `ReadOnly`, `Destructive`, `Idempotent`, and `OpenWorld`
  - older lifecycle tools still expose valid MCP tools, but without the same metadata richness

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
- `show-webApp-list`
- `find-empty-iis-port`

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
- `create-app`
  Create a Creatio application and return its structured context.
- `delete-app`
  Uninstall an application by name or code.
- `install-application`
  Install an application package into a target environment.

What an external AI can practically do here:

- enumerate installed apps
- inspect one app precisely before modifying or replacing it
- create new apps from a template
- remove apps
- install packaged apps into an environment

The AI sees this as a higher abstraction layer than package-level commands.

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
- `show-webApp-list`
  Return registered local environments and settings as structured JSON.
- `get-pkg-list`
  Read remote package inventory from the selected environment.

What an external AI can practically do here:

- onboard a target environment into clio configuration
- inspect what the local machine already knows about environments
- inspect package inventory before choosing install, page, or schema operations

Important note:

- `show-webApp-list` explicitly returns unmasked settings, which is powerful but sensitive

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

### 9. Runtime Control And Maintenance

These tools are operational rather than design-oriented.

- `start-creatio`
- `stop-creatio`
- `StopAllCreatio`
- `restart-by-environmentName`
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

## Prompt Layer: What The AI Gets Beyond Raw Tools

The `50` prompts do not add new execution power, but they materially change how an external AI can reason about the surface.

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
- page-editing workflow guidance tied to MCP tools, for example `get-tool-contract` -> `list-pages` -> `get-page` -> `component-info` -> `update-page` or `sync-pages`

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

### 1. Mixed naming styles

Examples:

- `restart-by-environmentName`
- `show-webApp-list`
- `StopAllCreatio`

Implication:

- clients should use exact discovered names
- do not normalize case or assume strict kebab-case

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

### 3. Mixed metadata richness

Most newer tools declare safety metadata.

Legacy lifecycle tools such as:

- `start-creatio`
- `stop-creatio`
- `StopAllCreatio`
- `restart-by-*`
- `clear-redis-db-by-*`

do not currently expose the same explicit safety fields inline.

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
