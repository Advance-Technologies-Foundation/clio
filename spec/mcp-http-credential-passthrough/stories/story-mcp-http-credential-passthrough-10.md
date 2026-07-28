# Story 10: Mode-gated plaintext-arg policy (FR-19) + up-front required-arg validation (FR-13)

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-19, FR-13
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 10; FR-19/FR-13)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 4 (context carries transport+mode), Story 5 (mode flag from the api-key gate)

---

## As a

platform admin / QA engineer

## I want

plaintext `uri/login/password` tool-args rejected over HTTP **only when passthrough mode is enabled**, and MCP `required` args validated up front before dispatch

## So that

secrets cannot be smuggled as plaintext args on the multi-tenant edge, while stdio and default HTTP keep behaving exactly as 8.1.0.72 — and callers get clear early validation errors

---

## Acceptance Criteria

- [x] **AC-01 (FR-19 reject)** — Given `PassthroughModeEnabled && Transport == Http` (both read from `ICredentialContextAccessor.Current`), when a tool call carries explicit `uri/login/password` args, then it is rejected with an error pointing the caller to the header (maps FR-19; AC-16).
- [x] **AC-02 (FR-19 no-regression)** — Given passthrough mode **off**, or the **stdio** transport, when the same call supplies those args, then behavior is exactly 8.1.0.72 (args honored) (maps FR-19/FR-10; AC-16/AC-10).
- [x] **AC-03 (FR-19 mechanism)** — Given enforcement, when implemented, then it is a **mode-scoped check** where options are consumed for resolution (BaseTool resolution path / `ToolCommandResolver`), **not** removal of args from the shared MCP primitives (HTTP and stdio register the same primitives) (maps FR-19).
- [~] **AC-04 (FR-13)** — Given an MCP tool call missing a `required` argument, when received, then a clear **structured** validation error is returned **up front, before dispatch** — not a late/opaque failure at execution (maps FR-13; AC-13). **PARTIAL / NEEDS-DECISION:** SDK validates the required top-level parameter up front (body never runs → structured error via `McpToolErrorFilter`); nested `[Required]` fields are schema-advertised but not runtime-enforced. Closing the nested gap server-side is a design decision deferred to the architect (see Dev Agent Record, options A/B/C).
- [x] **AC-ERR** — Given a rejected plaintext-arg request, when rejected, then the error is secret-free and does not echo the plaintext password/token (maps FR-11).

## Implementation Notes

From ADR step 10 (FR-19/FR-13):

- FR-19: enforce at the point options are consumed for resolution (BaseTool resolution path / `ToolCommandResolver`), reading `PassthroughModeEnabled` + `Transport` from `ICredentialContextAccessor.Current`. Do **not** strip args from the shared MCP primitives.
- FR-13: validate MCP tool `required` args up front (before dispatch) → structured validation error. Locate the pre-dispatch validation seam in the MCP tool pipeline.
- Error text must not echo any supplied secret (FR-11).

Key files: `clio/Command/McpServer/BaseTool.cs` / `ToolCommandResolver.cs` (FR-19 gate), MCP tool arg-validation seam (FR-13).
Pattern to follow: Story 4's `CredentialContext` transport/mode fields; existing tool arg mapping.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | reject plaintext args when passthrough+HTTP; honored when mode off / stdio; enforcement does not remove shared primitives; secret-free rejection | `clio.tests/Command/McpServer/TransportArgPolicyTests.cs` |
| Unit `[Category("Unit")]` | required-arg missing → structured up-front validation error before dispatch | `clio.tests/Command/McpServer/RequiredArgValidationTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] No new CLI flags (any touched kebab-case, CLIO001)
- [ ] Enforcement services resolved via `BindingsModule` DI — no MediatR; no raw `HttpClient`
- [ ] Rejection errors secret-free (FR-11)
- [ ] MCP surface + docs reviewed (FR-15) — mode-gated arg policy doc in Story 14; state outcome
- [ ] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09 (FR-19 complete; FR-13 characterized — see decision note)
- Tests passing: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit" -f net10.0` → 5144 passed, 0 failed, 35 skipped. Targeted: `TransportArgPolicyTests` + `RequiredArgValidationTests` + existing `ToolCommandResolverTests` → 22/22.

### FR-19 — enforcement point + exact reject condition

- **Where:** `clio/Command/McpServer/Tools/ToolCommandResolver.Resolve<TCommand>` — at the top, immediately
  after reading `credentialContextAccessor.Current` and BEFORE `ResolvePassthrough` is taken. This is the
  single resolution chokepoint both BaseTool execution paths pass through. Not added to `GetTenantKey`
  (it only computes a lock key; for typed-response tools it runs first, builds the passthrough key, then
  `Resolve` throws inside the lock and `finally` releases it — no leak, no double-gate).
- **Exact condition:** `Current is not null && Current.Transport == McpTransport.Http &&
  Current.PassthroughModeEnabled && HasExplicitCredentialArgs(options)`, where
  `HasExplicitCredentialArgs` is true when ANY of `options.Uri | Login | Password | ClientId |
  ClientSecret | Environment` is non-blank (chained `||`, no nested ternary — S3358 clean). On a hit it
  throws `EnvironmentResolutionException` (→ exit 1 via `CommandExecutionResult.FromResolverError`) with a
  header-pointing, **secret-free** message that echoes no supplied value.
- **Env NAME is rejected too:** on the passthrough branch the name is ignored today (the header context
  wins), so leaving it unflagged would let a caller believe a named environment took effect. Rejected with
  the same generic message.
- **No new ctor deps / DI change:** `credentialContextAccessor` was already injected and the five auth
  fields are already on `EnvironmentOptions` — pure add-a-throw. Both host graphs' ValidateOnBuild is
  therefore unaffected.

### CLI / stdio no-regression confirmation

- The gate reads `Current`, which is non-null ONLY on an authorized HTTP passthrough request (Story 4/5
  middleware); in the stdio host and the per-environment ephemeral containers the accessor is the null
  object, so stdio and default-HTTP never reach the guard and honor args exactly as 8.1.0.72 (AC-02).
- No args are stripped from the shared MCP primitives (HTTP and stdio register the same primitives); the
  check is mode-scoped where options are consumed for resolution (AC-03). Proven behaviorally by
  `Resolve_ShouldHonorExplicitArgs_WhenNoCredentialContextPresent` — the same explicit uri/login/password
  resolve without rejection when `Current == null`. Existing `ToolCommandResolverTests` (which use empty
  `EnvironmentOptions`, so `HasExplicitCredentialArgs` is false) all still pass.

### FR-13 — seam finding (SDK-validates vs added)

Characterized against the real SDK path (ModelContextProtocol 1.4.0, `McpServerTool.Create` + production
`CreateMcpSerializerOptions()`), pinned by `RequiredArgValidationTests`:

- **Top-level required parameter — validated up front by the SDK.** A call missing the required top-level
  `args` parameter throws `ArgumentException` from `InvokeAsync` (`AIFunctionFactory` marshaller) BEFORE the
  tool body runs — the spy confirms the body never executes, so the command is never dispatched. In the live
  pipeline that throw is converted to a structured `IsError` result by `McpToolErrorFilter` (see
  `McpToolErrorFilterTests`). This is the "already validated by the SDK/pipeline — test added" case.
- **Nested required fields — schema-advertised but NOT runtime-enforced (gap).** The individual arguments a
  client sends inside the wrapped `args` object are advertised in the input schema's `required` array (so a
  schema-compliant client rejects a missing field before sending), but System.Text.Json does not honor
  DataAnnotations `[Required]` on a record constructor parameter, so a hand-crafted request omitting one
  deserializes it to null and reaches the tool body. Documented by
  `Invoke_ShouldReachBody_DocumentsNestedRequiredNotRuntimeEnforced_WhenInnerRequiredFieldMissing`.
- **DECISION (coordinator, 2026-07-09): Option A accepted.** AC-04 is met via SDK top-level up-front
  enforcement + full input-schema advertisement of nested `required`. Rationale: FR-13 is priority
  **Should** (not Must); the passthrough feature is gated OFF (incubation, Story 11); option B is a
  cross-cutting deserialization change to **every** MCP tool (regression risk) not warranted for a Should-FR
  on a not-yet-shipping surface; schema-compliant clients already reject a missing nested field before
  sending. **Follow-up (tracked):** nested-required RUNTIME enforcement (option B — the `[Required]`-aware
  `JsonTypeInfo` modifier) is deferred; revisit when the incubation flag nears lift or if a client is
  observed sending schema-noncompliant calls. The characterization test pins current behavior so any future
  SDK/serializer change surfaces loudly.

### MCP / docs

- FR-15: no verb/flag/tool-contract change; the behavior change is on the passthrough HTTP edge only. MCP
  reviewed, no update required (mode-gated arg-policy doc is Story 14, per DoD).

### Notes

- Modified: `clio/Command/McpServer/Tools/ToolCommandResolver.cs` (guard + `HasExplicitCredentialArgs`).
- Added tests: `clio.tests/Command/McpServer/TransportArgPolicyTests.cs`,
  `clio.tests/Command/McpServer/RequiredArgValidationTests.cs`.
