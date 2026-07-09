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

- [ ] **AC-01 (FR-19 reject)** — Given `PassthroughModeEnabled && Transport == Http` (both read from `ICredentialContextAccessor.Current`), when a tool call carries explicit `uri/login/password` args, then it is rejected with an error pointing the caller to the header (maps FR-19; AC-16).
- [ ] **AC-02 (FR-19 no-regression)** — Given passthrough mode **off**, or the **stdio** transport, when the same call supplies those args, then behavior is exactly 8.1.0.72 (args honored) (maps FR-19/FR-10; AC-16/AC-10).
- [ ] **AC-03 (FR-19 mechanism)** — Given enforcement, when implemented, then it is a **mode-scoped check** where options are consumed for resolution (BaseTool resolution path / `ToolCommandResolver`), **not** removal of args from the shared MCP primitives (HTTP and stdio register the same primitives) (maps FR-19).
- [ ] **AC-04 (FR-13)** — Given an MCP tool call missing a `required` argument, when received, then a clear **structured** validation error is returned **up front, before dispatch** — not a late/opaque failure at execution (maps FR-13; AC-13).
- [ ] **AC-ERR** — Given a rejected plaintext-arg request, when rejected, then the error is secret-free and does not echo the plaintext password/token (maps FR-11).

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

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
