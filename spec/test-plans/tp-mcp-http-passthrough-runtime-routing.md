# Test Plan: MCP HTTP Passthrough Runtime Routing

**Feature**: mcp-http-passthrough-runtime-routing
**Jira**: [ENG-93348](https://creatio.atlassian.net/browse/ENG-93348)
**PRD**: [prd-mcp-http-passthrough-runtime-routing.md](../prd/prd-mcp-http-passthrough-runtime-routing.md)
**ADR**: [adr-mcp-http-passthrough-runtime-routing.md](../adr/adr-mcp-http-passthrough-runtime-routing.md)
**Stories**: [story 1](../stories/story-mcp-http-passthrough-runtime-routing-1.md), [story 2](../stories/story-mcp-http-passthrough-runtime-routing-2.md), [story 3](../stories/story-mcp-http-passthrough-runtime-routing-3.md), [story 4](../stories/story-mcp-http-passthrough-runtime-routing-4.md)
**Author**: QA Planner Agent
**Status**: Ready for implementation
**Created**: 2026-07-14

---

## Quality Objective

Prove that every authorized credential-passthrough request routes from an explicit, validated JSON
boolean `isNetCore`; that the value reaches ephemeral settings before client construction; that runtime is
part of the canonical cache/lock/in-flight identity; and that both real Creatio runtime families work
through the same `clio mcp-http` process without changing stdio or registered-environment behavior.

Unit success alone is insufficient. Exit requires:

1. process-level captured URLs proving root routing for .NET Core and exactly one `/0/` for .NET Framework;
2. a real .NET Core/NET8 bearer passthrough run;
3. a real .NET Framework passthrough run.

MCP E2E is manual/not in CI. Missing live stands are a release blocker for this story, not a reason to
mark the E2E cases passed.

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---:|---:|---|
| Omitted runtime becomes CLR `false` and silently routes through `/0/` | High before fix | Critical | Raw-token parser matrix, middleware short-circuit, compile-required context fields |
| Correct parsing but `BuildEphemeralSettings` drops the value | Medium | Critical | Direct settings assertions plus process route capture |
| Container created for one runtime is reused for the other | Medium | High | Opposite-runtime cache-key inequality and cache/lock/in-flight key equivalence |
| A green tool response masks a wrong route or redirect | Medium | High | Assert captured outbound path, not only MCP result |
| Validation or logging exposes credentials | Low | Critical | Sentinel-secret matrix across parse, middleware, resolver, key, logs, response, and capture |
| New required field breaks old E2E producers ambiguously | High | Medium | Compile-required encoder parameter and strict stand configuration |
| Shared stdio/default/named-environment behavior regresses | Low | High | Explicit no-header and registered-environment regression cases |
| Live .NET Core bearer stand is unavailable | Medium | High | Treat as an open operational prerequisite and block completion |

## Scope

### In scope

- Raw decoded-header parsing and validation order.
- Exact propagation through `CredentialParseResult`, `CredentialContext`, and `EnvironmentSettings`.
- Root versus `/0/` route generation.
- Runtime discrimination in canonical passthrough identity.
- Cache, tenant-lock, and in-flight key equivalence.
- No persistence and secret hygiene.
- Real process HTTP 400 behavior before tool/outbound work.
- Manual live Core bearer and Framework MCP E2E.
- Multi-tenant, concurrency, OAuth authorization, mixed-input, stdio, default HTTP, and named-environment regression.
- Documentation, MCP surface, and ClioRing compatibility review gates.

### Out of scope

- Runtime auto-detection or probing.
- Changing `ServiceUrlBuilder` semantics.
- Cookie passthrough support or OAuth token minting.
- Adding `isNetCore` to MCP tool arguments.
- Real deployment, uninstall, or any destructive live operation.
- ClioRing implementation or Ring AOT validation unless the implementation unexpectedly changes a Ring-consumed contract.

## Test Environments and Data

### Automated unit/process environment

- macOS, Linux, or Windows; tests use loopback/free ports and platform-neutral paths.
- A real `clio mcp-http` child process for HTTP/process cases.
- A local recording upstream/proxy or equivalent request-capture seam that records method and path but
  redacts authorization, cookies, passwords, tokens, and `X-Integration-Credentials`.
- Sentinel secrets unique to each test; assertions check that they are absent from all captured sinks.
- No test may write a passthrough environment to the user's real `appsettings.json`.

### Manual live environment

Required variables:

```text
CLIO_MCP_HTTP_E2E_PLATFORM_API_KEY
CLIO_MCP_HTTP_E2E_TENANT1_URL
CLIO_MCP_HTTP_E2E_TENANT1_TOKEN
CLIO_MCP_HTTP_E2E_TENANT1_IS_NET_CORE=true
CLIO_MCP_HTTP_E2E_TENANT2_URL
CLIO_MCP_HTTP_E2E_TENANT2_TOKEN or _LOGIN/_PASSWORD
CLIO_MCP_HTTP_E2E_TENANT2_IS_NET_CORE=false
CLIO_MCP_HTTP_E2E_REGISTERED_ENV
```

Tenant 1 must be a real .NET Core/NET8 tenant using bearer passthrough. Tenant 2 must be a distinct real
.NET Framework tenant. The stand loader accepts only strict Boolean text for the two runtime variables and
must skip/fail with a clear setup defect if either is missing or invalid. Neither passthrough tenant may be
pre-registered in clio settings.

Use a read-only environment-sensitive tool such as `list-apps` or `describe-environment`. Never use deploy,
uninstall, schema mutation, or another destructive operation for this proof.

## Traceability Matrix

| Requirement | Evidence | Owner |
|---|---|---|
| FR-01 | TC-U-01..03, TC-P-01, TC-E-01/02 | Story 1, 3 |
| FR-02 | TC-U-01..10, TC-I-01 | Story 1, 2 |
| FR-03 | TC-U-11..13, TC-P-03/04, TC-E-01/02 | Story 2, 3 |
| FR-04 | TC-U-04..08, TC-P-01/02 | Story 1, 3 |
| FR-05 | TC-U-14..16, TC-E-03 | Story 2, 3 |
| FR-06 | TC-U-10..12, TC-E-01/02 | Story 1, 2, 3 |
| FR-07 | TC-U-06..10, TC-U-14..18, TC-I-01, TC-P-01..04, TC-E-03/04 | Story 1, 2, 3, 4 |
| FR-08 | TC-U-09, TC-U-19, TC-E-05, RG-MCP | Story 2, 4 |
| FR-09 | TC-E-C01/02, TC-E-06, RG-DOC | Story 3, 4 |
| FR-10 | TC-U-01..19, TC-I-01, TC-E-C01/02, TC-P-01..04, TC-E-01..05 | Story 1..4 |
| FR-11 | RG-RING | Story 4 |
| AC-01 | TC-U-01/07/11/12, TC-P-03, TC-E-01 | Story 1..3 |
| AC-02 | TC-U-02/07/11/13, TC-P-04, TC-E-02 | Story 1..3 |
| AC-03 | TC-U-04, TC-U-08, TC-P-01 | Story 1, 3 |
| AC-04 | TC-U-07/11, code-review gate forbidding defaults/probes | Story 1, 2 |
| AC-05 | TC-U-05/06, TC-U-08, TC-P-02 | Story 1, 3 |
| AC-06 | TC-U-07/11/14..16 | Story 1, 2 |
| AC-07 | TC-U-06/08, TC-P-01/02 | Story 1, 3 |
| AC-08 | TC-U-10/15/17, TC-P-01..04, TC-E-04 | Story 1..4 |
| AC-09 | TC-U-17, TC-I-01, no-write review gate | Story 2, 4 |
| AC-10 | TC-U-09/18/19, TC-E-05 | Story 1, 2, 4 |
| AC-11 | TC-P-03/04, TC-E-01/02 | Story 3 |
| AC-12 | TC-E-06, RG-DOC | Story 4 |

## Unit Tests — `clio.tests/Command/McpServer`

All new or changed tests use `[Category("Unit")]`, `[Property("Module", "McpServer")]`,
`[Description("...")]`, explicit Arrange/Act/Assert, and a `because` explanation on every assertion.

### Header and middleware — Story 1

| ID | Test / assertion | Primary fixture |
|---|---|---|
| TC-U-01 | `TryParse_ShouldCarryTrueRuntime_WhenIsNetCoreIsTrue` — parse result is exactly `true` for bearer and login/password shapes | `CredentialHeaderParserTests` |
| TC-U-02 | `TryParse_ShouldCarryFalseRuntime_WhenIsNetCoreIsFalse` — explicit `false` is not treated as omission | `CredentialHeaderParserTests` |
| TC-U-03 | Case variants of the property name follow the existing case-insensitive policy | `CredentialHeaderParserTests` |
| TC-U-04 | Missing property returns `missing isNetCore`; no CLR default is produced | `CredentialHeaderParserTests` |
| TC-U-05 | `null`, string, number, array, and object return `isNetCore must be a JSON boolean`; no coercion | `CredentialHeaderParserTests` |
| TC-U-06 | Validation order is JSON object → URL → runtime → auth; invalid runtime is reported before missing auth | `CredentialHeaderParserTests` |
| TC-U-07 | Middleware copies exact `true`/`false` into `CredentialContext` | `CredentialPassthroughMiddlewareTests`, `CredentialPassthroughAuthHardeningTests` |
| TC-U-08 | Missing/invalid runtime returns HTTP 400 before accessor publication and next delegate | `CredentialPassthroughMiddlewareTests` |
| TC-U-09 | No credential header leaves accessor unset and preserves named/default HTTP behavior | `CredentialPassthroughMiddlewareTests` |
| TC-U-10 | Bearer precedence, non-Bearer rejection, cookie rejection, login/password fallback, authorization gate, and secret-free errors remain unchanged | parser/auth-hardening/secret-hygiene fixtures |

### Settings, route, and canonical identity — Story 2

| ID | Test / assertion | Primary fixture |
|---|---|---|
| TC-U-11 | `BuildEphemeralSettings` maps both runtime values exactly for bearer and login/password without changing auth fields | `ToolCommandResolverTests` |
| TC-U-12 | Core settings produce an outbound service path with no `/0/` | `ToolCommandResolverTests` plus existing `ServiceUrlBuilder` seam |
| TC-U-13 | Framework settings produce exactly one `/0/`, including a guard against double prefixing | same |
| TC-U-14 | Same normalized URL/auth with opposite runtime values produces different full SHA-256 passthrough keys | `ToolCommandResolverTests` or focused cache-key fixture |
| TC-U-15 | For each runtime, `GetTenantKey`, cache acquire key, and `LastResolvedTenantKey` are identical | `TenantKeyEquivalenceTests` |
| TC-U-16 | Runtime-different requests cannot share the tenant execution lock/in-flight identity; same-runtime equivalent requests do | tenant-key/lock fixture |
| TC-U-17 | Resolver writes no runtime, environment, or credentials to settings/session/disk | `ToolCommandResolverNoWriteTests` |
| TC-U-18 | Parse/resolver/key/route failures and serialization contain no raw or encoded secret; key stays opaque | `CredentialPassthroughSecretHygieneTests` |
| TC-U-19 | Registered/default settings still source `IsNetCore` from existing configuration and keep their pre-change key/route | resolver/no-regression fixtures |

## Integration Test — real filesystem

All integration tests use `[Category("Integration")]`, `[Property("Module", "McpServer")]`, and the
same repository test-style requirements as unit tests.

- **TC-I-01** `PassthroughResolution_ShouldWriteNoRuntimeCredentialOrEnvironmentState_WhenBothRuntimeValuesAreUsed` — create a real cross-platform temporary directory containing a byte-snapshotted `appsettings.json`; execute safe resolution for `true` and `false` through a stub client/container; assert the file remains byte-identical and no session, token, runtime, environment, or probe-result file appears. Always remove the temporary directory in teardown.

## Process-Level HTTP Tests — real `clio mcp-http`

These cases exercise the actual HTTP middleware order and route builder. The capture fixture must expose
only sanitized method/path evidence.

| ID | Test / assertion |
|---|---|
| TC-P-01 | Raw request missing `isNetCore` receives HTTP 400; recording upstream sees zero requests; body/stdout contain no secret |
| TC-P-02 | Table-driven `null`, string, number, array, object, and malformed JSON requests receive HTTP 400 before tool/outbound activity |
| TC-P-03 | `isNetCore: true` reaches the recording upstream on the root route with no `/0/`; MCP request contains no registered environment |
| TC-P-04 | `isNetCore: false` reaches the Framework route with exactly one `/0/`; repeat request verifies no double prefix |

## Manual MCP E2E — `clio.mcp.e2e`

Use the `test-mcp-tool` skill while implementing these cases. They remain `[Category("E2E")]` and are not
part of normal CI.

Support/config contract cases:

- **TC-E-C01** `CredentialEncoder_ShouldEmitJsonBoolean_WhenRuntimeIsSupplied` — decode bearer and login/password helper output for both values and assert `isNetCore` is a JSON Boolean, never a string.
- **TC-E-C02** `RequireOrIgnore_ShouldRejectConfiguration_WhenTenantRuntimeIsMissingOrInvalid` — strict stand parsing accepts only Boolean `true`/`false`; missing, blank, `0`, `1`, or `yes` produces a clear manual-gate defect and never a default. Preserve and restore process environment and keep the fixture non-parallel.

| ID | Test / assertion | Primary fixture |
|---|---|---|
| TC-E-01 | Real Core/NET8 bearer tenant succeeds through `POST /mcp`, header-only, no registered environment | focused runtime-routing fixture or `McpHttpMultiTenantE2ETests` |
| TC-E-02 | Real Framework tenant succeeds through the same process with explicit `false` | same |
| TC-E-03 | Opposite-runtime tenants run concurrently without cross-tenant result/log/cache/lock bleed | `McpHttpConcurrencyIsolationE2ETests` |
| TC-E-04 | Valid OAuth-authenticated principal plus passthrough header still reaches only the selected tenant; inbound MCP JWT is not forwarded | `McpHttpOAuthAuthorizationE2ETests` |
| TC-E-05 | Stdio, default HTTP/no header, and `mcp-http -e <registered-env>` retain their existing routing and tool contract | `McpHttpNoRegressionE2ETests` |
| TC-E-06 | Every encoder call supplies a Boolean; strict stand runtime variables are honored; multi-tenant/mixed-input cases remain green | support helpers plus `McpHttpMultiTenantE2ETests` |

The PR execution record must identify the redacted tenant/runtime/auth combination for TC-E-01/02 and
record pass, fail, or blocked. `Assert.Ignore` due to absent configuration is not a pass.

## Documentation and Compatibility Gates — Story 4

- **RG-DOC** — use `document-command`; verify `clio/help/en/mcp-http.txt`,
  `clio/docs/commands/mcp-http.md`, and `docs/McpCapabilityMap.md` show literal Boolean examples for both
  runtimes, omission/invalid HTTP 400 behavior, no default/detection, header-only authority, and no secrets
  in tool arguments. Review `clio/Commands.md` and `clio/Wiki/WikiAnchors.txt`; if unchanged record
  `docs reviewed, no update required`.
- **RG-MCP** — review tools, prompts, resources, routing guidance, argument schemas, descriptions, and safety
  flags. Expected result: `MCP reviewed, no update required`; `isNetCore` must not appear as a tool argument.
- **RG-RING** — search `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, and
  `clio-ring/ClioRing.Desktop/actions.json`. If no consumer is found, record exactly
  `ClioRing compatibility reviewed, no Ring-consumed contract changed`. Ring tests/AOT publish are required
  only if implementation expands into a Ring-consumed contract.

## Regression Gate

Primary files that must stay green:

- `CredentialHeaderParserTests.cs`;
- `CredentialPassthroughMiddlewareTests.cs`;
- `CredentialPassthroughAuthHardeningTests.cs`;
- `ToolCommandResolverTests.cs`;
- `TenantKeyEquivalenceTests.cs`;
- `ToolCommandResolverNoWriteTests.cs`;
- `CredentialPassthroughSecretHygieneTests.cs`;
- `McpHttpMultiTenantE2ETests.cs`;
- `McpHttpConcurrencyIsolationE2ETests.cs`;
- `McpHttpOAuthAuthorizationE2ETests.cs`;
- `McpHttpNoRegressionE2ETests.cs`.

Targeted automated command:

```shell
dotnet test clio.tests/clio.tests.csproj \
  --filter "Category=Unit&Module=McpServer"

dotnet test clio.tests/clio.tests.csproj \
  --filter "Category=Integration&Module=McpServer"
```

Manual affected E2E command:

```shell
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj \
  --filter "FullyQualifiedName~McpHttp"
```

Run the full unit suite only if implementation expands to `BindingsModule.cs`, `Program.cs`,
`clio/Common/`, test infrastructure, or more than three modules. Always run `git diff --check` and confirm
no new `CLIO*` diagnostics in modified files.

## Entry Criteria

- Stories 1 and 2 are implemented before process/live E2E work begins.
- The canonical header field remains `isNetCore`, required JSON Boolean, with no default or detector.
- Local recording fixture can observe routes without capturing secrets.
- Both live tenants are identified and reachable; Tenant 1 has a usable bearer token.
- Platform API key/OAuth configuration is available for the applicable E2E fixtures.

## Exit Criteria

- All TC-U, TC-I, TC-E-C, and TC-P cases pass on the affected target frameworks.
- TC-E-01 and TC-E-02 pass against real distinct tenants; no skip is counted as success.
- TC-P-03/04 provide sanitized route evidence for root versus exactly one `/0/`.
- TC-E-03..06 and existing affected MCP E2E cases pass or have a documented non-product infrastructure blocker; TC-E-01/02 cannot be waived.
- Targeted regression suite passes with no new `CLIO*` diagnostics.
- Secret and no-write assertions pass; no credentials appear in logs, snapshots, captures, or keys.
- RG-DOC, RG-MCP, and RG-RING are complete with required summary statements.
- Every FR-01..11 and AC-01..12 has evidence in the traceability matrix.
- Comprehensive pre-PR three-lens agent review has no unresolved Blocker/High finding.

## Residual Risks

- Live bearer-token expiry or stand downtime can block TC-E-01; it does not invalidate unit/process results,
  but it prevents feature completion.
- A local recording upstream proves path construction but not every reverse-proxy rewrite in the AI Platform
  deployment; retain the real live Core/Framework success cases.
- E2E remains manual/not in CI, so future regressions depend on disciplined PR execution until the harness is
  promoted to CI.
- Gateway rollout must add the required Boolean atomically with this unreleased contract; an older producer
  will intentionally receive HTTP 400.
