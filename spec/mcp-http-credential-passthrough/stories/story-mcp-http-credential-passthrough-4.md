# Story 4: Header + credential context — X-Integration-Credentials parse, precedence, context seam

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-01, FR-02, FR-04, OQ-02
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 4; FR-01/02, OQ-02, OQ-04)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1 (spike — confirms the context seam), Story 2 (spike)

---

## As a

AI Platform gateway author

## I want

`mcp-http` middleware to parse the `X-Integration-Credentials` header (base64-encoded JSON) into a per-request `CredentialContext`, apply auth-material precedence, and expose it through the singleton `ICredentialContextAccessor`

## So that

each request carries an isolated, precedence-resolved credential context that the resolver can consume — with no reliance on tool args or shared static/config

---

## Acceptance Criteria

- [ ] **AC-01** — Given a request with `X-Integration-Credentials` = base64(JSON `{url, accessToken}`), when middleware runs, then a `CredentialContext { Url, Auth=AccessToken, Transport=Http, PassthroughModeEnabled }` is stored in `HttpContext.Items` and readable via `ICredentialContextAccessor.Current` (maps FR-01/FR-04).
- [ ] **AC-02** — Given a payload carrying more than one of `accessToken`/`cookie`/`login+password`, when parsed, then the effective auth material follows precedence **accessToken → cookie → login+password** (maps FR-02; AC-02).
- [ ] **AC-03** — Given the header carries `{url}` with no usable auth material, or a blank/missing `url`, when parsed, then a structured `AC-ERR` failure names the specific defect (missing url / missing auth material) **without echoing any secret value** (maps FR-02/FR-11/FR-12; AC-ERR).
- [ ] **AC-04** — Given a configurable header name (`--credentials-header-name`, default `X-Integration-Credentials`), when set, then middleware reads credentials from that header; default matches the ADR/PRD (maps OQ-02).
- [ ] **AC-05** — Given a request on the stdio transport (or an HTTP request with no header), when the context is read, then `CredentialContext` is null (or `Transport=Stdio`) and no passthrough is attempted (maps FR-10).
- [ ] **AC-ERR** — Given `X-Integration-Credentials` that is not valid base64 or not valid JSON, when parsed, then clio returns `Error: {message naming the defect}` with a non-zero/structured failure and no secret material leaked (maps AC-ERR/FR-11).

## Implementation Notes

From ADR step 4 + "Key interfaces / contracts":

- New `ICredentialContextAccessor { CredentialContext? Current { get; set; } }` — default impl reads/writes `HttpContext.Items` via `IHttpContextAccessor` (registered via `AddHttpContextAccessor()`). **Register singleton** (matches the singleton, `AsyncLocal`-backed `IHttpContextAccessor`; resolver is transient, so lifetime is not forced — singleton is the least-surprising choice). Confirmed viable by Story 1.
- New `sealed record CredentialContext(string Url, CredentialMaterial Auth, McpTransport Transport, bool PassthroughModeEnabled)` — carries transport + mode so FR-19 (Story 10) has the flag at the enforcement point. `CredentialMaterial` = AccessToken | Cookie | LoginPassword (precedence-resolved). DTO/record — `new` allowed.
- Middleware in the `mcp-http` host: base64-decode → JSON-parse `{ url, accessToken?, cookie?, login?, password? }` → precedence → build `CredentialContext` → set accessor. `url` always required.
- Header name configurable; default `X-Integration-Credentials`. (Reference server's split `X-Creatio-Access-Token`/`X-Creatio-Cookie` is a documented follow-up parser mode, not built now.)
- **Secret hygiene:** parse errors name the defect only; never echo token/cookie/password (FR-11).

Key files: new `clio/Command/McpServer/CredentialContext.cs` (+ accessor), middleware in `clio/Command/McpServer/McpHttpServerCommand.cs`
Pattern to follow: Story 1's verified accessor seam; existing middleware wiring in `McpHttpServerCommand.Run`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | base64/JSON parse; precedence accessToken→cookie→login+password; missing url / missing auth / malformed base64/JSON error messages (secret-free); configurable header name | `clio.tests/Command/McpServer/CredentialContextParserTests.cs` |
| Unit `[Category("Unit")]` | accessor round-trip (set in middleware-equivalent → read via `Current`); null when no header / stdio | `clio.tests/Command/McpServer/CredentialContextAccessorTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [x] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [x] `--credentials-header-name` (and any new flag) is kebab-case (CLIO001)
- [x] Services registered (accessor singleton) — no MediatR; no raw `HttpClient`. **Deviation:** registered in the `mcp-http` host (`McpHttpServerCommand.Run`), NOT the shared `BindingsModule`, per the architect's FINAL decision — the accessor depends on `IHttpContextAccessor`, which must not be pulled into the stdio graph. Flagged for Story 7 (see notes).
- [x] No secret value in any parse error / exception / log (FR-11)
- [x] MCP surface + docs reviewed (FR-15) — no MCP tool wraps the `mcp-http` host command; header-contract docs deferred to Story 14; `help/en/mcp-http.txt` needed no stub (no help/ReadmeChecker test regressed)
- [x] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [x] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file — N/A this pass (no commit/PR per work order)

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" -f net10.0` → `Failed: 0, Passed: 1763, Skipped: 1, Total: 1764`
- Notes:
  - **Parser shape:** `ICredentialHeaderParser.TryParse` emits a pure `CredentialParseResult(Url, Auth)`; the middleware wraps it into the full `CredentialContext(Url, Auth, McpTransport.Http, passthroughEnabled)`. Parser has no `HttpContext` dependency (fully unit-testable). Transport/PassthroughModeEnabled are middleware-set, not parser-set.
  - **Precedence:** `accessToken → cookie → login+password`; "present" = non-whitespace; login+password usable only when BOTH are non-whitespace (login-without-password falls through to `no usable auth material`).
  - **Secret hygiene (FR-11):** all parse errors are fixed defect-only strings (`missing url`, `no usable auth material`, `credential header is not valid base64`, `credential header is not valid JSON`, `credential header is empty`). `JsonException`/`FormatException` messages are never surfaced.
  - **PassthroughModeEnabled:** middleware reads `HttpContext.Items["clio.mcp.passthrough-enabled"]`, defaulting to `false`. Authoritative gate is Story 5 (FR-09); this middleware only carries the flag.
  - **DI / auto-registration trap:** `BindingsModule.RegisterAssemblyInterfaceTypes` scans the assembly and auto-registers interface→impl as transient. It picked up `CredentialContextAccessor` (needs `IHttpContextAccessor`) and broke `ValidateOnBuild` in the stdio/tool graph (25 tests failed). Fix: both `ICredentialContextAccessor` and `ICredentialHeaderParser` are excluded from auto-registration and registered explicitly in the `mcp-http` host.
  - **FLAG FOR STORY 7:** when the credential resolver consumes `ICredentialContextAccessor`, it must resolve in BOTH hosts. Options: move the three HTTP-host registrations (`AddHttpContextAccessor()` + accessor singleton + parser singleton) into `BindingsModule`, or make the stdio path tolerate a null accessor. A comment to this effect is in `McpHttpServerCommand.Run`.
  - **REVIEW FIX (2026-07-09, ENG-93208 batch, test gap T2):** `CaptureCredentialContext` gained direct unit coverage in `clio.tests/Command/McpServer/CredentialPassthroughMiddlewareTests.cs` (shared with Story 5's gate). Asserts: a trusted request with a malformed credential header → HTTP 400 short-circuit, no context captured, raw header never echoed (FR-11); and the middleware is inert (forwards, captures nothing, no 400) when the gate did not set `PassthroughEnabledItemKey` — encoding the AC-02 "ignore header unless gated" contract. Method promoted `private static` → `internal static` (no behavior change).
