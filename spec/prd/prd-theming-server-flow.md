# PRD: Theming with AI — Toolkit (vibe-coder / server flow)

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-06-21
**Jira**: ENG-91387 (epic ENG-26797)

---

## Problem Statement

An AI coding agent driving the Creatio AI Toolkit (the "vibe-coder" flow) has no way to create,
restyle, or delete a custom Creatio theme **directly on an environment** through clio — today it
can only read (`list-themes`) and refresh the cache (`clear-themes-cache`). The dev/workspace flow
(Contour A, ENG-90636) requires a clio workspace, a package, and a push, which is unavailable to a
no-code agent that has nothing but a registered environment and credentials. This feature adds the
write half of the catalog so a vibe-coder can manage themes server-side via the native Creatio
`ThemeService`, with no workspace, no package, and no push.

## Goals

- [ ] Goal 1 — Provide create / update / delete theme CLI verbs that operate directly on a Creatio
  environment via the native `ThemeService`.
  Success metric **SM-01**: `clio create-theme`, `clio update-theme`, and `clio delete-theme` each
  exit 0 on a valid request against a live stand and the change is observable via `clio list-themes`.
  Counter **CM-01**: no regression to the already-shipped `list-themes` / `clear-themes-cache`
  behavior (their existing unit/E2E tests stay green).
- [ ] Goal 2 — Expose all three writes as environment-aware MCP tools so the vibe-coder can drive
  the full no-code theme lifecycle from the Toolkit.
  Success metric **SM-02**: the real `clio mcp-server` advertises `create-theme-by-environment`,
  `create-theme-by-credentials`, `update-theme-by-*`, and `delete-theme-by-*` with correct safety
  flags, verified by `clio.mcp.e2e` tests.
  Counter **CM-02**: the MCP JSON-RPC channel stays clean — no command console output leaks into the
  protocol stream (mirror the `list-themes` no-logger read path).
- [ ] Goal 3 — Make the no-code/server flow discoverable and correctly routed for an AI agent.
  Success metric **SM-03**: `get-guidance theming` describes the server flow (create / update /
  delete on an environment) and routes the agent to it from "Which flow"; a discovery test asserts
  the entry resolves.
  Counter **CM-03**: the guidance does NOT restate the `@creatio/theming` token catalog or
  authoring rules — it stays a thin pointer (single source of truth preserved, per ENG-90636 C1).

## Non-goals

- Will NOT add a theme **activation** command. The default theme is the `DefaultTheme` system
  setting (set via `update-sys-setting`); `ThemeService` has no activate endpoint. Activation is
  covered by guidance only.
- Will NOT support **local font binary upload**. The server flow sends `cssContent` text only;
  external fonts via `@import` (e.g. Google Fonts) are fine.
- Will NOT add any workspace / package scaffolding, `push-workspace`, or `push-pkg` step — that is
  ENG-90636's Contour A (dev/workspace flow).
- Will NOT change the behavior of the already-shipped `list-themes` (GetAvailableThemes) or
  `clear-themes-cache` (ClearThemesCache) commands or their MCP tools.
- Will NOT add a ClioGate endpoint, a cliogate version bump, or a `[RequiresPackage]` gate — the
  native `ThemeService` write endpoints are gated at runtime by license + system operation.
- Will NOT sanitize `cssContent` client-side (the server does not either); clio only enforces the
  documented length/format limits before sending.
- Will NOT design the C# class structure, validator topology, or option-class inheritance — that is
  the ADR.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| AI coding agent (vibe-coder, via the Creatio AI Toolkit) | to create a custom theme on an environment from CSS I generated, with no workspace or push | I can deliver branding to a no-code Creatio instance end to end |
| AI coding agent | to overwrite an existing theme's design tokens / CSS by id | I can iterate on a theme without re-creating it |
| AI coding agent | to delete a theme I created | I can clean up experiments and abandoned drafts |
| developer | the same create/update/delete as CLI verbs with a file-based CSS input | I can manage themes from a script when the CSS exceeds the Windows arg limit |
| QA engineer | each verb to fail with a clear `Error:` message and a non-zero exit on bad input or missing license | I can assert pass/fail deterministically in tests |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Add a `create-theme` CLI verb that calls the native `ThemeService.svc/CreateTheme` to add a new custom theme on the target environment. | Must |
| FR-02 | Add an `update-theme` CLI verb that calls `ThemeService.svc/UpdateTheme` to overwrite an existing theme's content by id (full overwrite; package not movable). | Must |
| FR-03 | Add a `delete-theme` CLI verb that calls `ThemeService.svc/DeleteTheme` to remove a theme by id. | Must |
| FR-04 | `create-theme` accepts CSS via mutually-exclusive `--css-content` (inline) OR `--css-content-file <path>` (read from file); same for `update-theme`. Exactly one is required. | Must |
| FR-05 | `create-theme --id` is optional and auto-generates a UUID v4 (satisfying the id regex) when omitted; `--caption` is optional and is derived from `--css-class-name` when omitted (e.g. `ocean-theme` → `Ocean`); `--css-class-name` and CSS content are required for create. | Must |
| FR-06 | `update-theme` requires `--id` plus `--caption`, `--css-class-name`, and CSS content (full overwrite); it has NO package parameter. | Must |
| FR-07 | `delete-theme` requires `--id` only and no other bespoke flags. | Must |
| FR-08 | `create-theme` accepts an optional `--package-name` (a package NAME), resolved to its UId via `PageSchemaMetadataHelper.QueryPackageUId`; when omitted, omit the `packageUId` key entirely so the server falls back to the `CurrentPackageId` system setting. `update-theme` and `delete-theme` have no package parameter. | Must |
| FR-09 | All three commands treat an explicit `success:false` in the `BaseResponse` as a failure (surface `errorInfo.message`). A genuinely empty body is tolerated as success (the contract default); a non-empty body that is **not** valid JSON is a failure — ThemeService always answers with a JSON `BaseResponse`, so a non-JSON payload (e.g. an auth-redirect login page or a proxy/error page) means the request never reached the service and must not be reported as success. | Must |
| FR-10 | Client-side input validation enforces the server contract before sending: `id` `^[A-Za-z0-9_-]+$` ≤100; `caption` required in the request ≤250 (auto-derived from css-class-name on create when the flag is omitted); `cssClassName` `^[A-Za-z][A-Za-z0-9_-]*$` ≤100; `cssContent` required (empty string allowed, null not) ≤1 MiB. Invalid input fails fast with a user-friendly `Error:` and a non-zero exit, without an HTTP call. | Should |
| FR-11 | Add three env-aware MCP tools — `create-theme`, `update-theme`, `delete-theme` — each exposing the two standard connection-mode variants (`-by-environment` and `-by-credentials`), executed via the env-aware `BaseTool` path (`InternalExecute<TCommand>`). The MCP tools take inline `cssContent` only (no `--css-content-file` equivalent). | Must |
| FR-12 | MCP safety flags: create = `ReadOnly=false, Destructive=false, Idempotent=false`; update = `ReadOnly=false, Destructive=false, Idempotent=true`; delete = `ReadOnly=false, Destructive=true, Idempotent=false`; `OpenWorld=false` on all three. Tool descriptions route the agent to `get-guidance theming`. | Must |
| FR-13 | Add three `ServiceUrlBuilder.KnownRoute` entries continuing the enum after `43` (`CreateTheme`, `UpdateTheme`, `DeleteTheme`), each mapped to `ServiceModel/ThemeService.svc/<Method>` in `KnownRoutes`. (`ServiceUrlBuilder.Build` prepends `0/` for .NET Framework automatically.) | Must |
| FR-14 | Register all three commands in DI (`clio/BindingsModule.cs`) and wire the verbs in `clio/Program.cs`; register the MCP tools via `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`). | Must |
| FR-15 | Update `ThemingGuidanceResource`: replace "No-code / server flow — not yet available in clio" with the now-available server flow (create / update / delete on an environment), route the agent to it from "Which flow", and keep the shared "Source of truth", "List themes", and "Get / set the default theme" sections; do not restate the token catalog. | Must |
| FR-16 | Add CLI help and docs for all three verbs: `help/en/{create,update,delete}-theme.txt`, `docs/commands/{create,update,delete}-theme.md`, `Commands.md` index entries, `Wiki/WikiAnchors.txt` anchors, and the new tools + the updated `theming` resource in `docs/McpCapabilityMap.md`. | Must |
| FR-17 | Commands are gated at runtime by the platform (`CanCustomizeBranding` license + `CanManageThemes` system operation); clio adds NO ClioGate method, NO cliogate bump, and NO `[RequiresPackage]`. Server-surfaced auth/validation errors (`UnauthorizedAccessException`, `SecurityException`, `ArgumentException`, `InvalidOperationException` via `errorInfo.errorCode`) are surfaced to the user, not swallowed. | Must |
| FR-18 | Add unit tests (`[Category("Unit")]`) for each command (route build, request body, `BaseResponse` parsing, validation) and each MCP tool (arg mapping, env-aware execution path, safety flags), plus mandatory `clio.mcp.e2e` coverage per tool. Live-stand E2E is manual / not in CI. | Must |
| FR-19 | All new CLI option long names are kebab-case (CLIO001): `--css-content`, `--css-content-file`, `--css-class-name`, `--package-name`, `--id`, `--caption`. | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New verb | `create-theme` | No |
| New verb | `update-theme` | No |
| New verb | `delete-theme` | No |
| New flag | `--id` (create: optional, auto-UUID; update/delete: required) | No |
| New flag | `--caption` (create: optional, derived from css-class-name when omitted; update: required) | No |
| New flag | `--css-class-name` (create/update: required) | No |
| New flag | `--css-content` (create/update: one of css inputs) | No |
| New flag | `--css-content-file` (create/update: one of css inputs; mutually exclusive with `--css-content`) | No |
| New flag | `--package-name` (create only: optional package NAME → UId; omitted → CurrentPackageId) | No |
| Inherited | `RemoteCommandOptions` (`-e/--environment`, URI/login/password, timeout, etc.) | No |
| MCP tools | `create-theme-by-environment` / `-by-credentials`; `update-theme-by-*`; `delete-theme-by-*` (inline `cssContent` only) | No |

All flags: **kebab-case only** (CLIO001 enforced). No existing flags renamed; no aliases required.

## Acceptance Criteria

- [ ] AC-01: Given a registered environment and a caller with `CanCustomizeBranding` + `CanManageThemes`,
  when `clio create-theme --caption "X" --css-class-name "x-theme" --css-content "…" -e <env>` runs,
  then a new theme is created (server returns `success:true`), clio exits 0, and the theme appears in
  `clio list-themes -e <env>`.
- [ ] AC-02: Given `create-theme` with `--id` omitted, when the command runs, then clio sends an
  auto-generated UUID v4 that matches `^[A-Za-z0-9_-]+$` and the create succeeds.
- [ ] AC-03: Given `create-theme --package-name <name>` for a package that exists, when the command
  runs, then `PageSchemaMetadataHelper.QueryPackageUId` resolves it and that `packageUId` is sent;
  given `--package-name` omitted, then no `packageUId` key is sent and the server falls back to the `CurrentPackageId` system setting.
- [ ] AC-04: Given an existing theme id, when `clio update-theme --id <id> --caption "Y"
  --css-class-name "y-theme" --css-content-file ./theme.css` runs, then the theme content is
  overwritten in its current package, clio exits 0, and `update-theme`'s MCP idempotent flag is true.
- [ ] AC-05: Given an existing theme id, when `clio delete-theme --id <id>` runs, then the theme is
  removed and clio exits 0; given a non-existent id, then the server returns an error and clio prints
  `Error: …` and exits non-zero (delete is not idempotent at the response level).
- [ ] AC-06: Given both `--css-content` and `--css-content-file` are supplied, when the command runs,
  then clio prints `Error: …` (mutually exclusive) and exits non-zero without an HTTP call.
- [ ] AC-07: Given `--css-content-file` points at a path that does not exist, when the command runs,
  then clio prints `Error: …` and exits non-zero without an HTTP call.
- [ ] AC-08: Given `cssContent` larger than 1 MiB, or a `css-class-name` not matching
  `^[A-Za-z][A-Za-z0-9_-]*$`, or a missing required field, when the command runs, then clio fails
  fast with `Error: …` and a non-zero exit before calling the service.
- [ ] AC-09: Given the caller lacks the `CanCustomizeBranding` license or `CanManageThemes` operation,
  when any write command runs, then the server returns `success:false` with the corresponding
  `errorInfo` and clio surfaces `Error: …` (the `errorInfo.message`) and exits non-zero.
- [ ] AC-10: Given the real `clio mcp-server`, when an MCP client lists tools, then
  `create-theme-by-environment`, `create-theme-by-credentials`, `update-theme-by-environment`,
  `update-theme-by-credentials`, `delete-theme-by-environment`, `delete-theme-by-credentials` are
  advertised with the safety flags in FR-12, and their descriptions reference `get-guidance theming`.
- [ ] AC-11: Given `get-guidance theming`, when it resolves, then its text describes the no-code/server
  flow (create / update / delete on an environment) and routes to it from "Which flow", retains the
  shared List-themes and Get/set-default sections, and does not restate the token catalog.
- [ ] AC-ERR: Given any invalid input or a server `success:false`, clio prints `Error: {message}` and
  exits non-zero; on success it exits 0.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | The native `ThemeService.svc/{CreateTheme,UpdateTheme,DeleteTheme}` endpoints exist and behave per the documented `IThemeService` contract on all supported Creatio versions. | Commands fail on older stands; may need a version gate or fallback. |
| A-02 | The three write endpoints return the same `BaseResponse {success, errorInfo}` shape as `ClearThemesCache`, so the existing parse/tolerance pattern transfers verbatim. | `success:false` detection or error surfacing breaks; could misreport pass/fail. |
| A-03 | `UpdateTheme` cannot move a theme between packages, so update legitimately needs no package parameter. | Users expecting to re-home a theme are surprised; needs guidance note. |
| A-04 | `DeleteTheme` of a non-existent id returns an explicit error (not idempotent), so clio should surface a failure rather than treat it as a no-op. | Delete may need idempotent semantics; AC-05 and the MCP `Idempotent` flag would change. |
| A-05 | `--package-name` resolution via `PageSchemaMetadataHelper.QueryPackageUId` (the `create-page` pattern) works for theme packages and an inactive target package surfaces as `InvalidOperationException` from the server. | create-theme package targeting fails for theme packages; may need a different resolver. |
| A-06 | The Windows ~32 KB single-arg cap (and CSS up to ~1 MiB) justifies the `--css-content-file` alternative; the MCP transport has no equivalent limit so inline-only is acceptable for tools. | If MCP also has a practical size cap, tools may need a file/URI input too. |
| A-07 | Auto-generating a UUID v4 for `create-theme --id` is acceptable to users and to the server's `^[A-Za-z0-9_-]+$` id rule (UUIDs with hyphens match). | Users may want human-readable default ids; trivial to change the generator. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Should these three commands ship behind a `[FeatureToggle]` while the server flow stabilizes, or ship enabled like `list-themes` / `clear-themes-cache`? (ENG-90636 siblings shipped enabled.) **RESOLVED then SUPERSEDED (2026-07-01):** first resolved as ship-enabled (ADR D1); later reversed by the ENG-90636 native-build consolidation — all theme commands (incl. these three) are now gated under one `[FeatureToggle("theming")]` key (`adr-theming-native-build.md` OQ-02). | PM / Architect | before story creation |
| OQ-02 | Is `delete-theme` meant to be idempotent (treat "not found" as success) at the clio layer, or surface the server error as a failure? Confirms AC-05 and the MCP `Idempotent` flag. | Kuvarzin (UC) | before ADR |
| OQ-03 | For `update-theme`, is a true full overwrite required (caption + css-class-name + css all mandatory), or should clio support partial updates by reading the existing theme first? Current scope = full overwrite. | Kuvarzin (UC) | before ADR |
| OQ-04 | Should `create-theme` reject an `--id` that already exists with a clio-friendly message, or rely solely on the server's `InvalidOperationException` ("id already exists")? | Architect | ADR |
| OQ-05 | The ticket says route enum should "continue after 43"; the repo currently has `ClearThemesCache = 43`, so new routes are 44/45/46. Confirm no other in-flight branch claims those values. | Architect | before implementation |
| OQ-06 | Should `--css-content-file` enforce a `.css` extension / UTF-8 encoding, or accept any readable text file as-is? | Architect | ADR |

## Dependencies

- Depends on: ENG-90636 "Theming with AI — Clio dev flow" (Contour A) — ships `list-themes`,
  `clear-themes-cache`, the `ThemingGuidanceResource`, the `ThemeService` route wiring pattern, and
  the `ServiceUrlBuilder.KnownRoute` baseline (`ClearThemesCache = 43`). This branch
  (`feature/ENG-91387-theming-server-flow`) is stacked on
  `feature/ENG-90636-theming-with-ai-clio-dev-flow`.
- Depends on: native Creatio `ThemeService` write endpoints (`IThemeService`,
  `Terrasoft.Core.ServiceModelContract.Theme.Interfaces`) on the target environment, gated by the
  `CanCustomizeBranding` license + `CanManageThemes` system operation (runtime, not a clio gate).
- Depends on: `PageSchemaMetadataHelper.QueryPackageUId` for `create-theme --package-name` resolution.
- Blocks: the no-code half of epic ENG-26797 "Theming with AI" — completes the catalog
  (read + cache + create + update + delete) so the Creatio AI Toolkit vibe-coder can manage themes
  end to end without a workspace.

## Risks & Regression

- **RR-01**: `BindingsModule.cs` and `Program.cs` change (DI + verb wiring), so per smart-regression
  rule 4 the **full unit suite** is required in addition to targeted `Module=Command|Common|McpServer`.
- **RR-02**: `ThemingGuidanceResource` and `GuidanceCatalog` are shared with ENG-90636; the
  guidance-resolution and `get-guidance theming` discovery tests must stay green (CM-03).
- **RR-03**: `ServiceUrlBuilder` is in `Common/` (shared); adding three routes is additive but the
  route-build tests for the existing theme routes must not regress.
- **RR-04**: MCP registration must use `McpFeatureToggleFilter.RegisterEnabledPrimitives`
  (`IEnumerable<Type>`) — never a `Type[]` and never `*FromAssembly`, or the new tools silently
  register nothing.
- **RR-05**: `cssContent` is sent unsanitized (matching the server); the only client-side guard is the
  documented length/format validation in FR-10. CSS content is opaque text to clio.

## Testing Levels

| Level | Category | What to cover | Where |
|-------|----------|--------------|-------|
| Unit | `[Category("Unit")]` | Per command: route build (new `KnownRoute` → `ServiceModel/ThemeService.svc/<Method>`, `0/`-prefixed on NetFW), request-body construction (camelCase keys; `packageUId` key omitted when no `--package-name`; auto-UUID), `--css-content` vs `--css-content-file` mutual exclusion + file read, FR-10 validation, `BaseResponse` parsing (success / success=false+errorInfo / empty body→success / non-empty non-JSON→failure). | `clio.tests/Command/{Create,Update,Delete}ThemeCommand*.Tests.cs` |
| Unit | `[Category("Unit")]` | Per MCP tool: arg mapping, env-aware `InternalExecute<TCommand>` selection, safety flags (FR-12), description routes to `get-guidance theming`. | `clio.tests/Command/McpServer/{Create,Update,Delete}ThemeToolTests.cs` |
| Unit | `[Category("Unit")]` | `get-guidance theming` resolves; server-flow section present; token catalog not restated (CM-03). | `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` (extend) |
| E2E (MCP) | `clio.mcp.e2e` (NOT in CI — manual) | All six tool variants advertised by the real `clio mcp-server` with correct safety flags; representative create/update/delete round-trip. | `clio.mcp.e2e/` |
| E2E (live stand) | Manual only | Full create → list → update → delete round-trip against a real Creatio environment with the required license/operation. | Manual runbook |

All unit tests follow `MethodName_ShouldExpectedBehavior_WhenCondition`, explicit AAA, a `because`
on every assertion, and `[Description]` on every test; prefer `BaseCommandTests<TOptions>` for command
fixtures. MCP E2E is mandatory per repo policy but is not in CI yet — flag this in the test plan.
