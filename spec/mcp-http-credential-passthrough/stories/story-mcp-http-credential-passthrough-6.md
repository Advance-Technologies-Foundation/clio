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

- [x] **AC-01** — Given a passthrough `url` that is not an absolute `http`/`https` URI, when validated, then it is rejected before any outbound call (maps FR-17).
- [x] **AC-02** — Given a `url` targeting cloud-metadata `169.254.169.254`, link-local `169.254.0.0/16` or IPv6 `fe80::/10`, or loopback (unless it is the bound host), when validated, then it is **always** rejected regardless of allowlist configuration and no credential is forwarded (maps FR-17; AC-14).
- [x] **AC-03** — Given `--allowed-base-urls` is configured, when a `url` whose origin is **not** on the allowlist is validated, then it is rejected before any outbound call (maps FR-17; AC-14).
- [x] **AC-04** — Given `--allowed-base-urls` is **not** configured, when a `url` passes the baseline blocks, then it is permitted (so AC-01/SM-01 succeed with only an API key, no allowlist) (maps FR-17).
- [~] **AC-05** — Given the validator runs, when it executes, then it runs **before** `ApplicationClientFactory` builds a client / before any credential is forwarded (ordering guarantee) (maps FR-17; AC-14). *(Story 6 builds + registers the validator; the actual invocation ordering is wired by Story 7. The registration is HTTP-host-scoped so Story 7 must resolve it from the resolution path in the HTTP host — see Dev Agent Record.)*
- [x] **AC-ERR** — Given a rejected `url`, when rejected, then a caller-actionable error names the reason (blocked address class / not on allowlist) and no credential value is leaked (maps FR-11).

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

- [x] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [x] `--allowed-base-urls` is kebab-case (CLIO001)
- [x] `ITargetUrlValidator` registered singleton — no MediatR; no raw `HttpClient`. *(Registered as an instance in the mcp-http host, mirroring Story 5's `IPlatformApiKeyGate`, because its policy is resolved at Run time from the bound host + `--allowed-base-urls`. Added to the `RegisterAssemblyInterfaceTypes` skip-list in `BindingsModule.cs` so it does not enter the stdio `ValidateOnBuild` graph.)*
- [x] DNS-rebinding TOCTOU residual documented in code (class-level `<remarks>` on `TargetUrlValidator` + inline comment at the baseline-block call site); docs deferred to Story 14
- [~] MCP surface + docs reviewed (FR-15) — new `--allowed-base-urls` option added; full help/docs update deferred to Story 14 (consistent with Story 4/5, whose options are also not yet in `help/en/mcp-http.txt`). No MCP tool/prompt/resource is tied to the `mcp-http` host command, so no MCP artifact update is required.
- [x] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`; no live DNS (literal hosts only)
- [x] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file *(no PR opened in this work order)*

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: yes — targeted `Category=Unit&Module=McpServer` → 1796 passed / 1 skipped / 0 failed; full `Category=Unit` (BindingsModule touched) → 5065 passed / 35 skipped / 0 failed
- Notes:
  - New files: `clio/Command/McpServer/TargetUrlValidator.cs` (`ITargetUrlValidator` + `TargetUrlValidator` + `TargetUrlNotAllowedException` + `AllowedBaseUrlsConfiguration` comma-set parser); `clio.tests/Command/McpServer/TargetUrlValidatorTests.cs`.
  - Baseline blocks apply to IP-literal hosts only (via `Uri.HostNameType` + `IPAddress.TryParse(DnsSafeHost)`). Metadata `169.254.169.254`, IPv4 link-local `169.254.0.0/16`, IPv6 link-local `fe80::/10`, and loopback (127.0.0.0/8, ::1) are always blocked; loopback is permitted only when the bound host is itself loopback. Baseline wins over the allowlist (tested).
  - Allowlist origin comparison uses `Uri.GetLeftPart(UriPartial.Authority)` (scheme+host+port, default port omitted) with `OrdinalIgnoreCase` — scheme- and port-sensitive (tested).
  - **STORY-7 WIRING FLAG:** the validator is registered HTTP-host-only (not in the shared `BindingsModule`/stdio graph). Story 7 must call `EnsureAllowed` from the credential-resolution path that runs inside the `mcp-http` host, resolving `ITargetUrlValidator` from `app.Services` (like the Story 5 gate) — it will NOT be resolvable in the stdio host. This satisfies the AC-05 ordering guarantee (validator runs before `ApplicationClientFactory` builds a client).
  - Story 6 does NOT wire the validator into the request pipeline / middleware (per work order); it only builds + registers it and adds the option.
  - Hardening from review: IPv4-mapped IPv6 literals (e.g. `[::ffff:169.254.169.254]`, `[::ffff:127.0.0.1]`) are normalized to their IPv4 form in `TryGetIpLiteral` before the address-class checks, so the dual-stack encoding cannot bypass the metadata / link-local / loopback blocks in the default no-allowlist mode. Two discriminating tests added.
  - **FOLLOW-UP (architect decision, out of scope here):** a `--allowed-base-urls` value whose entries ALL fail to parse as origins currently yields an empty allowlist → silent fail-open to no-allowlist "permit anything passing baseline" mode. Consider failing Run-time construction (fail-closed) when a supplied entry does not parse, rather than dropping it. Left as-is because the work order specified the ctor takes an already-parsed origin set and normalizes defensively.
  - Alternative numeric IP encodings (decimal/octal integer hosts) that `Uri` classifies as a DNS host rather than an IP literal fall into the documented DNS-rebinding TOCTOU v1 residual (comment updated to cover them).
  - **REVIEW FIX (2026-07-09, ENG-93208 batch, security M1 — SSRF trailing-dot bypass):** verified empirically on .NET 10 that decimal/hex/octal integer hosts (`http://2130706433/`, `http://0x7f000001/`, `http://0177.0.0.1/`, `http://2852039166/`) are canonicalized by `Uri` to `HostNameType=IPv4` and were therefore ALREADY blocked — the prior class-remark claiming they were DNS-classified residuals was factually wrong and has been corrected. The real remaining bypass was a single trailing dot (`http://169.254.169.254./`, `http://127.0.0.1./`), which `Uri` classifies as `Dns` (raw-host `IPAddress.TryParse` fails). `TryGetIpLiteral` now, as a fallback, strips EXACTLY one trailing dot and re-parses; any successful parse (with the existing IPv4-mapped-IPv6 normalization) runs through the baseline blocks. Characterization tests added for all four integer forms + both trailing-dot forms (BLOCKED), plus two RFC1918-permitted tests (`10.0.0.5`, `192.168.1.10`) documenting the deliberate v1 residual. Literal hosts only, no live DNS. DNS-rebinding TOCTOU (names that RESOLVE to a blocked IP) remains the sole out-of-v1-scope residual.
