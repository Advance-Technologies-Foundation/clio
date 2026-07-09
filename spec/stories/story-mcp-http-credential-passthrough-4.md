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

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] `--credentials-header-name` (and any new flag) is kebab-case (CLIO001)
- [ ] Services registered in `BindingsModule` (accessor singleton) — no MediatR; no raw `HttpClient`
- [ ] No secret value in any parse error / exception / log (FR-11)
- [ ] MCP surface + docs reviewed (FR-15) — header contract update deferred to Story 14; state review outcome
- [ ] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
