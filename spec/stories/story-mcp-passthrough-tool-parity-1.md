# Story 1: Credential-Passthrough Tool Guard + link-from-repository Fail-Fast (class c3)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-04, FR-05a (c3 requiredness split), FR-07 (decision-matrix rows for the `link-from-repository-*` family), FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

CI pipeline author (the AI-Platform gateway operator driving `clio mcp-http` over passthrough)

## I want

`link-from-repository-by-environment`, `link-from-repository-by-env-package-path`, and
`link-from-repository-unlocked` to reject calls made under authorized credential passthrough with one
uniform, tool-owned error — and to keep requiring `environment-name` explicitly (never via
`Link4RepoOptionsValidator`'s generic message) on non-passthrough calls

## So that

the gateway gets a predictable, safe failure instead of a confused-deputy call against a registered
environment's stored credentials, while stdio and registered-env `mcp-http` callers keep exactly today's
behavior

---

## Branching note (applies to every story in this feature)

Cut the implementation branch from, and target the PR at, `claude/clio-mcp-multi-tenant-73a807` — **not**
`master`. The passthrough seam (`CredentialContext`, `ICredentialContextAccessor`,
`IToolCommandResolver.Resolve`'s passthrough branch, `HasExplicitCredentialArgs`) does not exist on
`master`; branching from `master` will not compile against it (PRD "Branching Constraint", ADR "Branching
constraint").

## Acceptance Criteria

- [ ] **AC-01** — Given authorized credential passthrough (valid `X-Integration-Credentials` header,
  passthrough active) and no `environment-name`, when `link-from-repository-by-environment` or
  `link-from-repository-unlocked` is called, then the call returns the single uniform "not supported under
  credential passthrough" error naming the tool and the alternative (register the environment / use the
  stdio path) **before** any Creatio-reaching call — never a generic validator message (ADR Decision matrix,
  `link-from-repository-*` rows; PRD FR-04).
- [ ] **AC-02** — **Mixed input (confused-deputy, PRD AC-06).** Given authorized passthrough with **both**
  the header **and** an explicit `environment-name` naming a *different* registered environment, when either
  of the two guarded name-based tools runs, then the guard rejects the call before any Creatio-reaching call
  — it **never** uses the named environment's stored credentials (Security mode iii closed for this tool
  family).
- [ ] **AC-03** — **`link-from-repository-by-env-package-path` with `skip-preparation=false` (the
  Creatio-reaching branch) fails fast, header-only.** Given authorized passthrough with a valid header, no
  `environment-name`, and `skip-preparation` false/absent, when the package-path variant is called, then the
  guard returns the uniform "not supported under credential passthrough" error **before** any preparation
  Creatio call (`Link4RepoCommand.cs:289,310` — maintainer read/write + lock/design-mode) executes (ADR
  decision-matrix row: "Fail-fast (v1) unless `skip-preparation=true`").
- [ ] **AC-04** — **`link-from-repository-by-env-package-path` with `skip-preparation=false`, mixed input.**
  Given authorized passthrough with **both** the header **and** an explicit `environment-name` (a different
  registered environment), when the package-path variant is called with `skip-preparation=false`, then the
  guard rejects the call before any preparation Creatio call — the named environment's stored credentials
  are never used.
- [ ] **AC-05** — Given `link-from-repository-by-env-package-path` with `skip-preparation=true`, when it is
  called under passthrough, then the guard does **not** fire (this variant makes no Creatio call in that
  mode) — and `envPkgPath` stays `[Required]` unconditionally on every transport (ADR OQ-03).
- [ ] **AC-06** — Given a **non-passthrough** call (stdio, or registered-environment `mcp-http`) to
  `link-from-repository-by-environment` or `link-from-repository-unlocked` with a blank `environment-name`,
  when it runs, then the tool itself returns an explicit `"environment-name is required for
  <tool> outside credential passthrough."` error — it must **never** fall through to
  `Link4RepoOptionsValidator`'s generic "Either path to creatio directory or environment name must be
  provided" message (ADR verification #6, OQ-03 "Chosen" alternative).
- [ ] **AC-07** — Given stdio or registered-environment `mcp-http` with `environment-name` supplied, when any
  of the three `link-from-repository-*` tools run, then behavior matches the pre-change baseline exactly
  (PRD AC-09 / SM-03).
- [ ] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope for this story: the middleware returns HTTP 400
  **before** any tool is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not
  add handling for that case.
  (b) Given a **valid** header on one of these guard-only unsupported tools, when the guard fires, then the
  response is the typed error envelope (`{ "success": false, "error": "<uniform not-supported message>" }`
  or an MCP `CallToolResult` with `IsError=true`) — no process exit code requirement, and no
  `accessToken`/`login`/`password` in the message.

## Implementation Notes

- New interface `ICredentialPassthroughToolGuard` with `IsPassthroughActive` and
  `BuildUnsupportedMessage(toolName, alternativeGuidance)`; register it via constructor injection in
  `BindingsModule.cs` (DI, no MediatR, no `new`). **CLIO005 watch-out:** the guard is registered and
  consumed in the SAME slice (consumer = `LinkFromRepositoryTool` via the `BaseTool` helper) precisely so
  the registration is never dead — do not split registration and consumption into separate commits.
- New `private protected CommandExecutionResult? BaseTool<T>.RejectIfPassthroughUnsupported(string toolName,
  string alternativeGuidance)` helper — returns `null` when the guard should not fire, otherwise the typed
  rejection. Call it first in each of `LinkFromRepositoryTool`'s three methods
  (`LinkFromRepositoryTool.cs:40,63,85`).
- For `LinkFromRepositoryByEnvironment` and `LinkFromRepositoryUnlocked` only: after the guard passes
  (non-passthrough), add the explicit blank-`environment-name` check per the ADR's `OQ-03` code sample —
  do **not** rely on `Link4RepoOptionsValidator` (`Link4RepoOptionsValidator.cs:75-124`) to produce this
  message.
- Relax `[Required]` on `environment-name` for these two methods only (schema-optional so a header-only
  passthrough call reaches the guard instead of being rejected at MCP binding). `envPkgPath` on
  `link-from-repository-by-env-package-path` stays `[Required]` unconditionally — no relaxation.
- `link-from-repository-by-env-package-path`'s guard fires **exactly when `!SkipPreparation`** (its
  preparation path is the one that reaches Creatio); both branches of that condition must be covered by
  tests (AC-03/AC-04 for `false`, AC-05 for `true`). Do not gate the whole method.

Key files: `clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs`,
`clio/Command/McpServer/Tools/BaseTool.cs` (the two-arg `ExecuteWithCleanLog`/lock plumbing lives here at
`:63`), `clio/BindingsModule.cs`.
Pattern to follow: existing `BaseTool<T>` helper methods (`ExecuteWithCleanLog`, `InternalExecute`) for
where a new cross-cutting helper lives on the base class; `Link4RepoCommand.cs:289,310,382,437` for which
branches actually reach Creatio.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Guard rejection (header-only, no env-name) for `link-from-repository-by-environment` / `-unlocked`; mixed-input rejection (AC-02); **package-path variant with `skip-preparation=false`: header-only rejection (AC-03) AND mixed-input rejection (AC-04), asserting no preparation call was made**; `skip-preparation=true` NOT rejected (AC-05); non-passthrough blank-name explicit error (not the FluentValidation message); stdio/registered-env-with-name unchanged | `clio.tests/Command/McpServer/LinkFromRepositoryToolPassthroughTests.cs` |
| Unit `[Category("Unit")]` | `ICredentialPassthroughToolGuard` / `BaseTool.RejectIfPassthroughUnsupported` in isolation (active vs inactive passthrough, message shape/no secret leak) | `clio.tests/Command/McpServer/CredentialPassthroughToolGuardTests.cs` |
| Integration `[Category("Integration")]` | none required for this slice (no new I/O) | — |
| E2E `[Category("E2E")]` | Owned by **Story 15** (dedicated E2E story): header-only + header+`environment-name` for one Creatio-reaching `link-from-repository-*` branch, extending `McpHttpMultiTenantE2ETests` (PRD FR-08); manual only, MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] All `CLIO*` diagnostics clean in changed files — **including CLIO005** for the new
  `ICredentialPassthroughToolGuard` DI registration (registered + consumed in this same slice) (FR-10)
- [ ] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` — **and the full unit suite**, because this story touches
  `clio/BindingsModule.cs` (repo full-suite trigger rule 4) (ADR slice 9)
- [ ] All new CLI flags are kebab-case (no new CLI flags introduced by this story; MCP args stay kebab-case)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
