# create-app-section timeout diagnostics — QA plan

> Jira: [ENG-90679](https://creatio.atlassian.net/browse/ENG-90679)

## Unit — `Module=Command` (`ApplicationSectionCreateServiceTests`)

| ID | Case | Expected |
|----|------|----------|
| TC-U-1 | Insert call passes finite default budget (300 000 ms) to `ExecutePostRequest` | timeout argument = 300 000 |
| TC-U-2 | `CLIO_CREATE_SECTION_TIMEOUT_SECONDS=42` | timeout argument = 42 000 |
| TC-U-3 | Env var invalid (`abc`, `0`, `-5`) | default 300 000 used |
| TC-U-4 | Insert throws `WebException(ConnectFailure)` | `ApplicationSectionCreateException` with `Transport`, `SectionCreated=false`, retry-safe guidance |
| TC-U-5 | Insert throws `WebException(Timeout)`, verification readback finds the section | success result returned (recovered) |
| TC-U-6 | Insert throws `WebException(Timeout)`, verification readback returns no rows | exception with `CreatioTimeout`, `SectionCreated=false`, wait-verify guidance |
| TC-U-7 | Insert throws `WebException(Timeout)`, verification readback itself throws | exception with `CreatioTimeout`, `SectionCreated=null` |
| TC-U-8 | Insert throws `WebException(ProtocolError)` | exception with `ServerError` |
| TC-U-9 | Insert returns HTML (JSON parse failure) | exception with `ServerError`, message mentions non-JSON response |
| TC-U-10 | Insert returns `success:false` | exception with `ServerError`, preserves the #684 actionable message |
| TC-U-15 | Insert throws `TaskCanceledException` / `OperationCanceledException` / `TimeoutException` (HttpClient-era shapes) | `CreatioTimeout` |
| TC-U-16 | Insert throws bare `HttpRequestException` | `Transport` |
| TC-U-17 | Insert throws `HttpRequestException` with `StatusCode=500` | `ServerError` |
| TC-U-18 | Insert throws `HttpRequestException` with transient `StatusCode` (503) | `CreatioTimeout` (wait-then-verify, not "fix inputs") |
| TC-U-19 | Insert throws `InvalidOperationException` wrapping `WebException(Timeout)` / `AggregateException` wrapping nested `SocketException` | chain walk classifies the inner cause |
| TC-U-20 | Readback after timeout returns a pre-existing section bound to the same entity (different Id) | NOT recovered: `CreatioTimeout`, `SectionCreated=false`, no UpdateQuery issued (verification matches strictly by the generated section Id) |
| TC-U-21 | Env var value whose ms equivalent exceeds `int.MaxValue` (`3000000`) | clamped to `int.MaxValue` |
| TC-U-22 | Insert returns JSON `null` (empty response) | `ServerError`, `SectionCreated=unknown`, spinner closed |

## Unit — `Module=McpServer` (`ApplicationToolTests`)

| ID | Case | Expected |
|----|------|----------|
| TC-U-11 | Service throws typed exception (`CreatioTimeout`, `SectionCreated=false`) | envelope: `success:false`, `error-class:"creatio-timeout"`, `section-created:"false"`, `retry-guidance` non-empty |
| TC-U-12 | Service throws typed exception (`SectionCreated=null`) | `section-created:"unknown"` |
| TC-U-13 | Service throws plain exception | envelope unchanged vs current behavior (`error` only, new fields null) |
| TC-U-14 | Success path | new fields null/absent |

## E2E — `clio.mcp.e2e` (not in CI; gated on sandbox)

| ID | Case | Expected |
|----|------|----------|
| TC-E-1 | `create-app-section` against an unreachable environment URI | `success:false` + `error-class:"transport"` + `section-created:"false"` |
| TC-E-2 | Happy-path section creation (existing suite) | unchanged; new fields absent/null |

## Regression scope

`dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"` — both
touched modules. No shared-infrastructure files changed (BindingsModule only if
a new registration is required — re-evaluate trigger rule 4 at commit time).
