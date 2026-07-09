# Story 6: SSRF / egress validator on the per-request target URL

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-17
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 6; FR-17)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 4 (context supplies the caller-influenced url)

---

## As a

platform admin

## I want

an `ITargetUrlValidator` that runs **before** any client construction or outbound call and blocks link-local / cloud-metadata targets always, with an optional operator-configured origin allowlist

## So that

the caller-influenced passthrough `url` cannot be used as a credential-redirection lever (CWE-918)

---

## Acceptance Criteria

- [ ] **AC-01** — Given a passthrough `url` that is not an absolute `http`/`https` URI, when validated, then it is rejected before any outbound call (maps FR-17).
- [ ] **AC-02** — Given a `url` targeting cloud-metadata `169.254.169.254`, link-local `169.254.0.0/16` or IPv6 `fe80::/10`, or loopback (unless it is the bound host), when validated, then it is **always** rejected regardless of allowlist configuration and no credential is forwarded (maps FR-17; AC-14).
- [ ] **AC-03** — Given `--allowed-base-urls` is configured, when a `url` whose origin is **not** on the allowlist is validated, then it is rejected before any outbound call (maps FR-17; AC-14).
- [ ] **AC-04** — Given `--allowed-base-urls` is **not** configured, when a `url` passes the baseline blocks, then it is permitted (so AC-01/SM-01 succeed with only an API key, no allowlist) (maps FR-17).
- [ ] **AC-05** — Given the validator runs, when it executes, then it runs **before** `ApplicationClientFactory` builds a client / before any credential is forwarded (ordering guarantee) (maps FR-17; AC-14).
- [ ] **AC-ERR** — Given a rejected `url`, when rejected, then a caller-actionable error names the reason (blocked address class / not on allowlist) and no credential value is leaked (maps FR-11).

## Implementation Notes

From ADR step 6 (FR-17):

- New singleton `ITargetUrlValidator { void EnsureAllowed(string url); }` — throws a caller-actionable rejection; invoked in the resolution path **before** client construction (Story 7 calls it).
- Baseline blocks (always, even with no allowlist): absolute http/https required; block `169.254.169.254`, `169.254.0.0/16`, IPv6 `fe80::/10`, loopback (unless bound host).
- Optional allowlist `--allowed-base-urls` (comma-set): when configured, `url` origin must be on it; when not, baseline still applies and other reachable Creatio hosts are permitted (deliberate — API-key gate is the primary trust control).
- **Documented residual:** DNS-rebinding TOCTOU between validation and the client's own resolution is out of scope for v1 — note it in the code and docs.

Key files: new `clio/Command/McpServer/TargetUrlValidator.cs` (+ interface), flag `--allowed-base-urls` (coordinate with Story 12 consolidation).
Pattern to follow: existing host/origin filter parsing in `McpHttpServerCommand`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | reject non-absolute/non-http(s); always-block metadata/link-local/IPv6-link-local/loopback; allowlist enforced when set; permit when allowlist unset + baseline passes; secret-free rejection messages | `clio.tests/Command/McpServer/TargetUrlValidatorTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute. Use literal test hosts (no live DNS) — cross-OS safe.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] `--allowed-base-urls` is kebab-case (CLIO001)
- [ ] `ITargetUrlValidator` registered singleton in `BindingsModule` — no MediatR; no raw `HttpClient`
- [ ] DNS-rebinding TOCTOU residual documented in code + docs
- [ ] MCP surface + docs reviewed (FR-15) — allowlist doc update in Story 14; state outcome
- [ ] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`; no live DNS
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
