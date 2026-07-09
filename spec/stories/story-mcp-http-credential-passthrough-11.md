# Story 11: Incubation feature-flag gate on the passthrough behavior

**Feature**: mcp-http-credential-passthrough
**FR coverage**: OQ-03 (incubation gating); supports FR-10 (verb stays available)
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 11; OQ-03)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: S (< 2h)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 5 (api-key gate / passthrough-mode wiring)

---

## As a

platform admin

## I want

the passthrough **behavior** gated behind an incubation feature flag checked at middleware-wiring time — **not** a `[FeatureToggle]` on the `mcp-http` verb

## So that

the multi-tenant leg is doubly-gated (incubation flag AND api-key gate) while ENG-92869 stabilizes, without hiding the shipped `mcp-http` verb (which would regress FR-10/AC-10)

---

## Acceptance Criteria

- [ ] **AC-01** — Given the incubation flag `mcp-http-credential-passthrough` is **disabled** (default), when `McpHttpServerCommand.Run` wires middleware, then the passthrough middleware/credential leg is **not** wired, and the verb, stdio, and `-e <env>` remain fully available and behave as 8.1.0.72 (maps OQ-03/FR-10; AC-10).
- [ ] **AC-02** — Given the flag is **enabled**, when `Run` wires middleware, then the passthrough leg is wired (still additionally gated by the api-key gate from Story 5) (maps OQ-03).
- [ ] **AC-03 (negative)** — Given the design, when reviewed, then there is **no** `[FeatureToggle]` on `McpHttpServerCommandOptions` (that would hide the verb entirely and regress FR-10) (maps OQ-03).
- [ ] **AC-04** — Given both gates, when passthrough is honored, then it requires **both** the incubation flag enabled AND a matching api-key (doubly-gated) (maps OQ-03/FR-09).

## Implementation Notes

From ADR step 11 (OQ-03 correction):

- Check `IFeatureToggleService`/`ISettingsRepository.IsFeatureEnabled("mcp-http-credential-passthrough")` inside `McpHttpServerCommand.Run` at middleware-wiring time. Do **NOT** put `[FeatureToggle]` on the options class.
- Passthrough is doubly-gated: incubation flag **and** the api-key gate (Story 5). The verb, stdio, `-e <env>` remain always-available.
- Lift the flag when ENG-92869 stabilizes (documented follow-up).

Key files: `clio/Command/McpServer/McpHttpServerCommand.cs` (`Run` wiring), feature-toggle service usage.
Pattern to follow: existing `IFeatureToggleService` / feature-flag checks in the codebase (behavior-level, not verb-level).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | flag off → passthrough middleware not wired, verb/stdio/-e unaffected; flag on → wired; doubly-gated (flag+key) | `clio.tests/Command/McpServer/PassthroughIncubationGateTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] No `[FeatureToggle]` on `McpHttpServerCommandOptions` (verb stays available)
- [ ] No new CLI flags (any touched kebab-case, CLIO001)
- [ ] Feature-toggle check resolved via DI (`BindingsModule`) — no MediatR; no raw `HttpClient`
- [ ] MCP surface + docs reviewed (FR-15) — incubation note in Story 14; state outcome
- [ ] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
