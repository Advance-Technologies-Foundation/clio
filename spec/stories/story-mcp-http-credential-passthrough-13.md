# Story 13: Secret-hygiene sweep — url-only logging across every sink

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-11
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 13; FR-11)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 4 (parse sink), Story 7 (resolution sink), Story 9 (log-capture sink)

---

## As a

platform admin

## I want

every log/response/exception/stdout sink audited so `accessToken`/`cookie`/`password` values never appear anywhere — only non-secret identifiers (`url`)

## So that

a public/shared edge cannot leak the credentials it forwards, even under `--debug`

---

## Acceptance Criteria

- [ ] **AC-01** — Given any passthrough request, when console log, file log, and MCP execution-log messages are inspected (including `--debug`), then no `accessToken`/`cookie`/`password` value appears; only `url` and non-secret identifiers are logged (maps FR-11; AC-11).
- [ ] **AC-02** — Given any MCP tool response and CLI stdout, when inspected, then no secret value appears (maps FR-11; AC-11).
- [ ] **AC-03** — Given any exception path (including `--debug` stack traces), when an error surfaces, then no secret value is embedded in the message (maps FR-11; AC-11).
- [ ] **AC-04** — Given `EnvironmentSettings.ShowSettingsTo` / any settings dump, when invoked, then the secret fields are absent (mirrors Story 3 `[JsonIgnore]`/`[YamlIgnore]`) (maps FR-11).
- [ ] **AC-05** — Given the sweep, when complete, then it mirrors the existing `CreatioAuthClient` "cookie names only, never values" discipline as the reference pattern (maps FR-11).

## Implementation Notes

From ADR step 13 (FR-11):

- Audit **every** sink touched by the passthrough path: header parse (Story 4), resolution (Story 7), the AsyncLocal log capture (Story 9), cache-key building (Story 8 — already hashed), MCP responses, CLI stdout, exception messages, `--debug`.
- Enforce url-only logging; redact/omit secret material at the source (do not rely on downstream filtering).
- Reference discipline: existing `CreatioAuthClient` logs cookie **names only, never values**.
- This is a cross-cutting sweep; the exhaustive assertion is the Story 15b secret-leak test matrix — this story fixes the sinks, Story 15b proves them.

Key files: sinks across `clio/Command/McpServer/**`, `clio/Common/Logger/**`, `EnvironmentSettings.ShowSettingsTo`.
Pattern to follow: `CreatioAuthClient` secret-name-only logging.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | targeted per-sink assertions that a seeded secret does not appear in that sink's output (console/file/MCP-log/response/stdout/exception, incl. `--debug`) | `clio.tests/Command/McpServer/CredentialPassthroughSecretHygieneTests.cs` |

Note: the full cross-sink matrix is Story 15b; this story's tests cover each sink it fixes.
Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute; use a distinctive seeded secret literal and assert absence.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=McpServer|Module=Common)"`

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] No new CLI flags (any touched kebab-case, CLIO001)
- [ ] Any touched services resolved via `BindingsModule` DI — no MediatR; no raw `HttpClient`
- [ ] Every audited sink emits url-only / secret-free output, including `--debug`
- [ ] MCP surface + docs reviewed (FR-15) — state outcome
- [ ] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] Targeted `dotnet test --filter "Category=Unit&(Module=McpServer|Module=Common)"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: `dotnet test --filter "Category=Unit&(Module=McpServer|Module=Common)" -f net10.0` → 2571 passed, 0 failed, 3 skipped. New fixture `CredentialPassthroughSecretHygieneTests` → 12/12 passed.
- Notes:

### Per-sink audit (all CLEAN — no source changes; confirms two prior reviews)

| Sink | File | Verdict |
|------|------|---------|
| Header parse | `CredentialHeaderParser.TryParse` | CLEAN — errors are fixed literals ("not valid base64/JSON", "missing url", "no usable auth"); never echoes the header/payload. |
| Resolution FR-19 reject | `ToolCommandResolver.Resolve` | CLEAN — message names the channel (X-Integration-Credentials); reads no supplied value. |
| Resolution FR-12 errors | `ToolCommandResolver.ResolvePassthrough` | CLEAN — cookie / missing-auth / non-Bearer messages name the missing piece or the scheme NAME (`AccessTokenType`, not a secret); never a value. |
| Cache keys | `BuildPassthroughCacheKey` / `BuildCacheKey` | CLEAN — credential material SHA-256 hashed (full hash) before entering the key. |
| Log capture + scoped file sinks | `ConsoleLogger` (Story 9) | CLEAN — content-agnostic; captures only caller-supplied strings; no path logs `settings.AccessToken/Cookie/Password` or `context.Auth`. `LogMessage.ScopedSinks` is `internal` (STJ never serializes it). |
| MCP response envelope | `CommandExecutionResult` | CLEAN — failing passthrough resolve → `FromResolverError` over the secret-free `EnvironmentResolutionException`; individual tool catches redact via `SensitiveErrorTextRedactor`. |
| Middleware 401/400 bodies | `McpHttpServerCommand.EnforcePlatformApiKeyGate` / `CaptureCredentialContext` | CLEAN — 401 body is a fixed literal; 400 body carries only the defect-only parse error. |
| Platform API-key gate | `PlatformApiKeyGate.IsAuthorized` | CLEAN — constant-time `FixedTimeEquals`, no early-out, key material never echoed. |
| Request-header logging (`--debug`) | `McpHttpServerCommand.Run` | CLEAN — no `UseHttpLogging`/`AddHttpLogging`/header dump. `Authorization` flows only into `IsAuthorized`; the credential header only into `parser.TryParse`. Nothing logs raw header values. |
| Exception paths / `--debug` | `CommandExecutionResult.FormatExceptionChain` | CLEAN — emits `[TypeName] Message` per level; **no stack traces ever enter the envelope** (AC-03 holds because traces are never embedded, not merely scrubbed). |
| `EnvironmentSettings.ShowSettingsTo` / serialization | `ConfigurationOptions.ShowSettingsTo` | CLEAN — `AccessToken/AccessTokenType/Cookie` are `[YamlIgnore]`+`[Newtonsoft.Json.JsonIgnore]` (Story 3); ShowSettingsTo serializes via Newtonsoft. Already covered by `Common/EnvironmentSettingsTests`. |

### Residual / fixes
None. Every audited sink was already secret-free at source; no redaction or omission was added (the task requires not inventing changes). Reference discipline (`CreatioAuthClient` names-only) confirmed present.

### Tests added
`clio.tests/Command/McpServer/CredentialPassthroughSecretHygieneTests.cs` — seeds one distinctive literal `SUPER-SECRET-TOKEN-9c3f2a` as accessToken/cookie/password and asserts ABSENCE across: header-parse error (missing-url + malformed-JSON), FR-19 reject, FR-12 cookie/missing-auth/non-Bearer, both cache keys, the System.Text.Json-serialized `CommandExecutionResult` for a failing passthrough resolve (`FromResolverError` + `FromException`), the `SensitiveErrorTextRedactor` (Bearer/cookie/password shapes), and Newtonsoft serialization of an ephemeral passthrough `EnvironmentSettings`. AC-04's ShowSettingsTo/JSON/YAML absence remains authoritatively covered by `Common/EnvironmentSettingsTests`.

### Scope flags for Story 15b
`FromException` (BaseTool catch-all, exit -1) is NOT run through `SensitiveErrorTextRedactor`. For a failing passthrough *resolve* this is clean (that path throws `EnvironmentResolutionException` → `FromResolverError`, secret-free by construction). A hypothetical "`command.Execute` throws with a secret in its own message" is not a known passthrough leak (the token lives in `EnvironmentSettings`/`IApplicationClient`, which do not echo it) — the exhaustive cross-sink assertion is Story 15b.

### MCP / docs
No verb/tool contract change (audit + tests only). MCP reviewed, no update required. Command docs → Story 14.
