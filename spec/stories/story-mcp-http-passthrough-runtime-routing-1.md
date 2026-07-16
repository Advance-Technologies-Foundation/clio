# Story 1: Header contract and fail-closed runtime propagation

**Feature**: mcp-http-passthrough-runtime-routing
**Jira**: [ENG-93348](https://creatio.atlassian.net/browse/ENG-93348)
**FR coverage**: FR-01, FR-02, FR-04, FR-06, FR-07 · AC-03, AC-04, AC-05, AC-07, AC-08
**PRD**: [prd-mcp-http-passthrough-runtime-routing.md](../prd/prd-mcp-http-passthrough-runtime-routing.md)
**ADR**: [adr-mcp-http-passthrough-runtime-routing.md](../adr/adr-mcp-http-passthrough-runtime-routing.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: none

---

## As a

gateway operator sending per-request credentials to `clio mcp-http`

## I want

the decoded header to require an explicit JSON boolean `isNetCore` and carry it through request context

## So that

an absent or malformed runtime can never silently select the .NET Framework route family.

## Acceptance Criteria

- [ ] **AC-01 (PRD AC-03, AC-05)** — `CredentialHeaderParser` accepts case-insensitive `isNetCore` property names only when the value is the JSON boolean `true` or `false`; missing, `null`, string, number, and malformed JSON inputs fail with the ADR-defined secret-free error.
- [ ] **AC-02 (PRD AC-07)** — validation order remains JSON shape → URL → `isNetCore` → auth. Missing or invalid runtime is rejected before `TargetUrlValidator`, middleware context assignment, the next delegate, container/client construction, or outbound traffic.
- [ ] **AC-03 (PRD AC-04)** — successful parsing has no implicit routing path: no CLR `false` default, fixed default, or runtime probe can manufacture the value.
- [ ] **AC-04 (PRD AC-06)** — `CredentialParseResult` and `CredentialContext` gain compile-required positional `bool IsNetCore`; middleware copies the exact parsed value without defaulting, coercion, or probing.
- [ ] **AC-05 (PRD AC-08)** — all new validation responses, exceptions, logs, and tests omit the encoded header, token, password, cookie, and decoded credential payload.
- [ ] **AC-06** — an absent credential header remains valid for named/default HTTP operation; the required field applies only when an authorized passthrough header is present. Bearer precedence, unsupported-cookie rejection, and login/password fallback remain unchanged.
- [ ] **AC-07** — public C# APIs changed by this story have authoritative XML documentation; every construction site supplies an explicit runtime value.

## Implementation Notes

- Preserve the raw `isNetCore` JSON token in the private parser DTO until validation can distinguish omission, `null`, wrong types, `true`, and `false`.
- Return `missing isNetCore` for omission and `isNetCore must be a JSON boolean` for present non-boolean values, without echoing input.
- Add `IsNetCore` to `CredentialParseResult` in `clio/Command/McpServer/CredentialHeaderParser.cs` and to `CredentialContext` in `clio/Command/McpServer/CredentialContext.cs`.
- Copy the parsed value in the credential middleware in `clio/Command/McpServer/McpHttpServerCommand.cs`.
- Do not add runtime detection or an MCP tool argument.

## Test Requirements

Use the `test-mcp-tool` skill. Extend:

- `CredentialHeaderParserTests` for `true`, `false`, case-insensitive property name, missing, `null`, string, number, malformed JSON, auth preservation, and secret-free errors;
- `CredentialPassthroughMiddlewareTests` and `CredentialPassthroughAuthHardeningTests` for exact propagation and HTTP 400 short-circuit before accessor/next delegate;
- all affected tests constructing `CredentialContext` or `CredentialParseResult` with an explicit boolean.

Tests must use `[Description]`, explicit Arrange/Act/Assert, and a `because` explanation on every assertion.

## Definition of Done

- [ ] Production and unit-test changes satisfy all acceptance criteria.
- [ ] `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"` passes.
- [ ] No new `CLIO*` diagnostics exist in modified files; `git diff --check` passes.
- [ ] MCP tools/prompts/resources are reviewed; no contract update is expected in this slice.
- [ ] No command documentation change is required in this slice; final documentation is owned by Story 4.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
