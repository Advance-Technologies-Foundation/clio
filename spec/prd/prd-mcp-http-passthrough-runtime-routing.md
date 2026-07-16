# PRD: MCP HTTP Passthrough Runtime Routing

**Status**: Approved for implementation — facilitator checkpoint complete
**Author**: PM Agent
**Created**: 2026-07-14
**Jira**: ENG-93348
**Parent**: ENG-92790
**Builds on**: ENG-93208 / PR #830 (`claude/clio-mcp-multi-tenant-73a807`)

---

## Problem Statement

The credential-passthrough path introduced by ENG-93208 constructs an ephemeral
`EnvironmentSettings` from `X-Integration-Credentials`, but it does not set
`EnvironmentSettings.IsNetCore`. The property therefore keeps its CLR default,
`false`, and `ServiceUrlBuilder` produces the .NET Framework `/0/` URL layout for
every passthrough request.

The header contract currently carries `url` plus authentication material only.
It cannot declare whether the target uses the .NET Core/NET8 root layout or the
.NET Framework `/0/` layout. A shared `clio mcp-http` edge can consequently serve
.NET Framework tenants by coincidence, but it cannot reliably address the cloud
.NET Core tenants required by the AI Platform integration.

This is a routing defect, not an authentication defect. A valid bearer token can
still be sent to the wrong endpoint family and fail before the requested MCP
operation reaches Creatio.

## Evidence from the Umbrella Branch

- `CredentialHeaderParser.CredentialPayload` accepts `url`, `accessToken`,
  `accessTokenType`, `cookie`, `login`, and `password`; it has no runtime field.
- `CredentialParseResult` and `CredentialContext` carry the target URL and auth
  material, but no runtime selection.
- `ToolCommandResolver.BuildEphemeralSettings` initializes only `Uri` and the
  selected auth material. It never initializes `IsNetCore`.
- `EnvironmentSettings.IsNetCore` is a non-nullable `bool`, so an unset value is
  indistinguishable from an explicit .NET Framework selection after mapping.
- `ServiceUrlBuilder.Build` prepends `0/` when `IsNetCore == false` and omits it
  when `IsNetCore == true`.
- `ToolCommandResolver.BuildPassthroughCacheKey` currently keys a container by
  URL and credential material, but not runtime routing. Correcting the runtime
  without correcting identity can reuse a container built with the old route.
- `IEnvironmentRuntimeDetectionService` already probes both route families for
  `reg-web-app`, but it cannot be reused unchanged for passthrough: its auth check
  and clone logic do not carry `AccessToken`, and its current diagnostics refer
  to the registration command.

## Goals and Success Measures

### Goal 1 — Route every passthrough request to the correct Creatio runtime

- **SM-01**: The same environment-sensitive MCP operation succeeds against one
  real .NET Core/NET8 tenant and one real .NET Framework tenant through
  `mcp-http`, using only per-request credentials and no registered environment.
- **SM-02**: Captured outbound URLs contain no `/0/` prefix for the .NET Core
  case and contain exactly one `/0/` prefix for the .NET Framework case.

### Goal 2 — Keep the edge safe when the runtime signal is absent or invalid

- **SM-03**: A request with missing or invalid runtime metadata is rejected with
  a caller-actionable, secret-free HTTP 400 response before target validation,
  container/client construction, or outbound Creatio traffic.

### Goal 3 — Preserve tenant and session isolation

- **SM-04**: Requests that differ in their effective runtime routing cannot reuse
  an incompatible cached container. Existing URL/credential isolation,
  in-flight protection, TTL, and eviction behavior remain intact.

### Goal 4 — Avoid regressions outside credential passthrough

- **SM-05**: `clio mcp` (stdio), default `mcp-http`, and named/explicit
  environments keep their current `IsNetCore` behavior and wire contracts.

## Users and Use Cases

| User | Need | Outcome |
|---|---|---|
| AI Platform gateway | Supply the target runtime per request | One edge can call cloud .NET Core and classic .NET Framework tenants |
| Platform operator | Receive a precise error when runtime metadata is missing or invalid | No misleading auth/environment failure and no silent wrong-route fallback |
| Existing clio user | Keep registered-environment and stdio behavior unchanged | The follow-up remains scoped to the passthrough leg |
| QA engineer | Prove both URL layouts through the real HTTP/MCP path | The cloud target is validated, not inferred from unit tests alone |

## Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-01 | Extend the decoded `X-Integration-Credentials` JSON contract with a required, case-insensitive `isNetCore` property encoded as a JSON boolean. `true` means the root .NET Core/NET8 layout; `false` means the .NET Framework `/0/` layout. | Must |
| FR-02 | Keep the parser-side representation tri-state/raw-token so omission remains distinguishable from explicit `false` and invalid JSON types, validate it as required, then carry the validated boolean through the parser result and per-request credential context into ephemeral environment construction without reading or writing the settings repository. | Must |
| FR-03 | When `isNetCore` is explicitly supplied, set `EnvironmentSettings.IsNetCore` to that exact value before building any environment-bound provider, client, route, or command. | Must |
| FR-04 | Reject a missing, `null`, or non-boolean `isNetCore` value as an invalid credential header with a structured HTTP 400 error before target validation or any container/client/outbound-request work. Do not coerce strings, numbers, null, or malformed values to `false`, and do not auto-detect or silently default the runtime. | Must |
| FR-05 | Include the runtime routing value in the passthrough container/session identity, or otherwise prove that a container created for one routing value can never be reused for the other. The preferred invariant is one canonical resolved identity shared by cache acquisition, tenant locking, and in-flight accounting. | Must |
| FR-06 | Preserve the existing authentication precedence and behavior: bearer token, unsupported-cookie rejection, and login/password fallback remain unchanged. Both supported auth paths carry the supplied runtime value. | Must |
| FR-07 | Preserve the existing credential hash, secret redaction, target URL validation, API-key/authorization gates, passthrough-mode gate, TTL/eviction, mixed-input rejection, and no-persistence guarantees from ENG-93208. Validation errors must not reveal tokens, passwords, cookies, or the encoded header. | Must |
| FR-08 | Keep `isNetCore` out of MCP tool arguments on the passthrough HTTP leg; the per-request header remains the authoritative target context. Existing stdio/default-HTTP tool argument behavior remains unchanged. | Must |
| FR-09 | Update every header encoder, live-stand fixture/configuration, `mcp-http` help, detailed command documentation, and the MCP capability/edge contract to supply and describe the required field, examples for both runtimes, and failure behavior. | Must |
| FR-10 | Add unit and real `clio mcp-http` E2E coverage for explicit .NET Core, explicit .NET Framework, missing/invalid fields, context/settings propagation, route generation, cache isolation, secret absence, and existing-mode no-regression. | Must |
| FR-11 | Review ClioRing compatibility. Current repository inspection finds no `mcp-http`, `X-Integration-Credentials`, or credential-context consumer in the Ring production paths; if unchanged, record `ClioRing compatibility reviewed, no Ring-consumed contract changed` with the inspected paths. | Must |

## Required Runtime Policy

The recommended product contract is:

1. Explicit `isNetCore` is authoritative.
2. Missing, `null`, or non-boolean runtime metadata is rejected with HTTP 400.
3. No runtime is inferred, probed, or defaulted by the passthrough path.
4. The validated runtime becomes part of the same bounded tenant/session
   lifecycle and is never persisted.

ENG-93208 is still in Manual Testing and the gateway integration is not yet a
released compatibility surface. Tightening the header contract now is safer and
smaller than adding runtime probes whose semantics vary across proxies and
Creatio versions. It also makes the gateway—the component that selects the
tenant—the authoritative source of deployment type.

## Non-Goals

- Redesigning the ENG-93208 authentication model, API-key gate, SSRF policy,
  concurrency model, or session-cache eviction policy.
- Adding cookie authentication; ENG-93208 intentionally dropped it from v1.
- Persisting request runtime metadata or creating named clio environments.
- Changing `ServiceUrlBuilder`'s established root-versus-`/0/` semantics.
- Inferring runtime from Creatio product version alone.
- Runtime auto-detection or probing. The existing registration detector is not
  bearer-aware and is not part of this contract correction.
- Exposing runtime selection as a new MCP tool argument for passthrough requests.

## Acceptance Criteria

- [ ] **AC-01 — Explicit .NET Core**: Given a valid passthrough header with
  `isNetCore: true`, when an environment-sensitive tool runs, then the ephemeral
  settings use `IsNetCore == true` and the outbound service URL has no `/0/`.
- [ ] **AC-02 — Explicit .NET Framework**: Given the same request shape with
  `isNetCore: false`, then the ephemeral settings use `IsNetCore == false` and
  the outbound service URL contains exactly one `/0/` prefix.
- [ ] **AC-03 — Omitted field**: Given a valid legacy header without
  `isNetCore`, then parsing returns a secret-free HTTP 400 response and no target
  validation, settings, container, client, or outbound Creatio request occurs.
- [ ] **AC-04 — No implicit routing**: No passthrough execution path obtains its
  runtime from the CLR `false` default, a fixed .NET Core default, or a runtime
  probe; every successful request traces to the validated header boolean.
- [ ] **AC-05 — Invalid field**: Given `isNetCore` as a string, number, null, or
  malformed JSON, then parsing fails with a structured non-secret error and no
  Creatio command/client/container is created.
- [ ] **AC-06 — Context propagation and cache identity**: Given the same URL and
  credential with different explicit runtime values, then
  `CredentialParseResult`, `CredentialContext`, and `EnvironmentSettings` carry
  the matching supplied value, and the requests cannot acquire the
  same incompatible environment-bound container, and cache/lock/in-flight keys
  remain aligned.
- [ ] **AC-07 — Validation order**: Given a missing/invalid runtime value, header
  validation fails before `TargetUrlValidator`; given a valid runtime and blocked
  target URL, SSRF validation still rejects the target before any other outbound
  request.
- [ ] **AC-08 — Secret hygiene**: Given parse, validation, route, auth, and tool
  failures, no raw header, token, password, cookie, or encoded credential payload
  appears in logs, stdout, exceptions, MCP content, cache keys, or test snapshots.
- [ ] **AC-09 — No persistence**: Given either explicit runtime value, no
  environment, runtime flag, credential, or probe result is written to
  `appsettings.json`, session files, or other disk state.
- [ ] **AC-10 — No regression**: Stdio, default HTTP, and named/explicit
  environment tests keep their existing route behavior; the change is active
  only when a passthrough credential context exists.
- [ ] **AC-11 — Real E2E**: The committed MCP E2E suite demonstrates a successful
  environment-sensitive operation against real .NET Core and .NET Framework
  tenants through `POST /mcp`. At least the .NET Core case must use the
  AI-Platform-representative bearer path.
- [ ] **AC-12 — Documentation**: Help/docs show base64-decoded JSON examples for
  both boolean values, define omission behavior, and retain the existing warning
  that credentials belong in the gated header rather than tool arguments.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| A boolean DTO collapses omitted to `false` | The original defect survives under a new field name | Keep the field nullable until policy resolution is complete; test omission separately |
| Runtime is absent from the cache/lock identity | A corrected request reuses a wrong-route container | Derive all lifecycle keys from one effective resolved target identity |
| Gateway rollout omits the new required field | Older payloads receive HTTP 400 | Coordinate the contract with ENG-92869 before the unreleased umbrella feature ships |
| Naming/type drift (`netCore`, strings, enums) fragments the contract | Gateway and clio disagree at runtime | Canonicalize on `isNetCore` as a JSON boolean and pin examples/tests |
| E2E validates only local .NET Framework stands | Cloud blocker remains undiscovered | Make a real bearer-auth .NET Core tenant a release gate |

## Dependencies

- ENG-93208 / PR #830 must remain the base branch and target of this work.
- The AI Platform gateway must add the required boolean field before the
  umbrella feature ships.
- Real test access is required to one .NET Core/NET8 tenant with a bearer token
  and one .NET Framework tenant.
- Command/MCP changes require documentation review, MCP unit tests, and MCP E2E
  coverage under repository policy.

## Open Questions for the Architect

| ID | Question | Recommendation |
|---|---|---|
| OQ-01 | Can the AI Platform gateway add required boolean `isNetCore` before the umbrella feature ships? | Yes; coordinate this as one contract change with ENG-92869 |
| OQ-02 | Is HTTP 400 fail-closed behavior approved for missing/invalid runtime metadata? | Yes; no default and no detection |
| OQ-03 | Where is the validated runtime incorporated so cache, tenant lock, in-flight guard, and DI container all consume one identity? | Validate once, carry it on `CredentialContext`, and include it in the canonical passthrough key |
| OQ-04 | Which real .NET Core tenant and bearer token will be used for mandatory E2E? | Identify it before implementation is declared complete |
| OQ-05 | Does the platform expect a future runtime enum rather than the existing clio boolean? | Use `isNetCore` now; a future multi-runtime need should be a versioned contract change |

## Phase 1 Checkpoint Decision

Proceed to ADR only after confirming:

1. The public header field name is `isNetCore` and its JSON type is boolean.
2. Missing/invalid values fail closed with HTTP 400; no default or detection.
3. A real .NET Core bearer tenant and a .NET Framework tenant are available for
   the E2E acceptance gate.
