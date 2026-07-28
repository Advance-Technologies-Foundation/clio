# get-info target classification - PLAN (ADR)

> GitHub: [#390](https://github.com/Advance-Technologies-Foundation/clio/issues/390)

## Decision

Keep classification inside `GetCreatioInfoCommand`, immediately around the required base probe.
Private helpers will validate and redact the target URI, inspect response shape without echoing the
body, walk wrapped exception chains, and emit one stable error. This avoids changing the shared
transport abstraction or successful output contract.

## Classification order

1. Parse `EnvironmentSettings.Uri` as an absolute HTTP/HTTPS URI. Reject unsupported or malformed
   input without echoing an unsafe raw value.
2. Execute `ApplicationInfoService.GetApplicationInfo`.
3. If the response is Creatio's known authentication envelope/login page, return an
   authentication-specific error.
4. If content is clearly HTML or non-JSON, classify the reachable target as non-Creatio.
5. If JSON is malformed or lacks `applicationInfo.sysValues`, return the stable
   unexpected-response error.
6. Only after the base report succeeds, run both optional enrichment paths.

Exception-chain rules:

- `UnauthorizedAccessException`, HTTP 401/403, and known session-expired responses ->
  authentication.
- `TaskCanceledException`, `TimeoutException`, transport `HttpRequestException`, and network
  `WebException` statuses -> unavailable/connection failure.
- Other required-probe exceptions -> unexpected response, with only exception type metadata under
  `--debug`.

## Diagnostics and redaction

- User-visible target text is derived from a parsed URI with user-info, query, and fragment removed.
- Response bodies and exception messages are never logged by this command.
- Debug output contains classification, response length, exception type chain, and safe HTTP/status
  metadata only.

## Optional enrichment

`GetSystemEnvironmentInfo`, ClioGate compatibility detection, and ClioGate `GetSysInfo` remain
best-effort. ClioGate compatibility lookup moves under the same guarded enrichment path so its
failure cannot replace a successful base report.

## MCP and ClioRing

The existing `describe-environment` MCP tool remains a thin environment-aware adapter. Its
description/guidance and tests will document and assert that base-probe failures use the same
classified command envelope. ClioRing invokes `get-info` through `clio-run`; no Ring code or schema
change is expected, but Ring tests and Windows x64 NativeAOT publish are mandatory compatibility
checks.

## Rejected alternatives

- Changing `IApplicationClient` to return HTTP status/content metadata: too broad for a scoped bug
  and would affect many commands.
- Probing a second endpoint before ApplicationInfoService: adds latency and can introduce a second
  source of contradictory classification.
- Matching arbitrary exception messages: unstable and risks leaking credentials or URLs.
