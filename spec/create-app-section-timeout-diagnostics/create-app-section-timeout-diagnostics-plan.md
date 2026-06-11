# create-app-section timeout diagnostics — PLAN (ADR)

> Jira: [ENG-90679](https://creatio.atlassian.net/browse/ENG-90679)

## Decision

Implement the entire diagnostic logic in the **service layer**
(`ApplicationSectionCreateService`) and surface it through a typed exception.
The MCP tool only adds a catch-clause that maps the typed exception onto new
optional fields of `ApplicationSectionContextResponse`. This keeps the
`try`-body of `ApplicationSectionCreate` untouched, which minimises the merge
surface with PR #688 (ENG-91274 heartbeat) that wraps the same body in
`McpProgressHeartbeat.RunWithProgressAsync`.

## Design

### Failure classification (new file `clio/Command/ApplicationSectionCreateFailure.cs`)

```csharp
public enum ApplicationSectionCreateFailureClass { Transport, CreatioTimeout, ServerError }

public sealed class ApplicationSectionCreateException : Exception {
    ApplicationSectionCreateFailureClass FailureClass;
    bool? SectionCreated;       // null = unknown
    string RetryGuidance;       // agent-actionable next step
}
```

Classifier rules (defensive — creatio.client uses `HttpWebRequest` under the
hood, but `HttpClient` types are also mapped):

| Observed failure | Class |
|---|---|
| `WebException` Status ∈ {ConnectFailure, NameResolutionFailure, ProxyNameResolutionFailure, SecureChannelFailure, TrustFailure} | `transport` |
| `HttpRequestException` with socket/DNS inner | `transport` |
| `WebException` Status ∈ {Timeout, RequestCanceled, KeepAliveFailure, ReceiveFailure, ConnectionClosed}, `TimeoutException`, `TaskCanceledException` | `creatio-timeout` |
| `WebException` Status = ProtocolError (HTTP 4xx/5xx) | `server-error` |
| `JsonException` while parsing the insert response (HTML error page) | `server-error` |
| InsertQuery `success:false` | `server-error` (keeps the #684 actionable message) |

### Budget

- Insert call: `client.ExecutePostRequest(url, body, requestTimeout: budget)`.
- Budget default **300 000 ms**; env var `CLIO_CREATE_SECTION_TIMEOUT_SECONDS`
  (int seconds, > 0) overrides. Read once per call, not cached.
- Verification readback budget: fixed **30 000 ms**.

### Preparation-phase classification

The pre-insert reads (schema prefix, `get-app-info`, icon resolution, schema
existence check) run before the destructive insert. Network-shaped failures
there are classified with the same rules but always carry
`section-created: false` and retry-safe guidance, because the insert was never
attempted. Non-network failures (validation, missing app id) propagate
unchanged. This also makes the unreachable-environment E2E scenario exercise
the `transport` class deterministically.

### Post-timeout verification

On `creatio-timeout` only:
1. Run `GetSectionRecord` (existing private readback) under the 30 s budget via
   a `SelectQueryHelper.ExecuteSelectQuery` overload that accepts `requestTimeout`.
2. Found → log a recovery note and fall through to `LoadCreatedSection`
   (existing poll loop) → normal success result.
3. Not found → throw `ApplicationSectionCreateException(CreatioTimeout,
   SectionCreated=false, guidance: insert may still be processing; wait, run
   `list-app-sections`, retry only if still absent).
4. Verification threw → same exception with `SectionCreated=null` (unknown).

### MCP surface

- `ApplicationSectionContextResponse` gains optional fields:
  `error-class` (string), `section-created` ("true"|"false"|"unknown"),
  `retry-guidance` (string). All null on success and on non-classified errors.
- `ApplicationToolHelper.CreateSectionContextErrorResponse` gains an overload
  taking the typed exception.
- `ApplicationSectionCreateTool` catch order: typed exception → generic.
- Tool `[Description]` documents the error-class contract for agents.

### Rejected alternatives

- **Fire-and-poll (async job)** — changes the AI contract; rejected for the
  same reason as in ENG-91274.
- **Tool-level timeout wrapper (Task.WhenAny)** — leaves the orphaned HTTP call
  running with no classification of *why* it failed; the service layer knows
  the transport details.
- **Classification inside `CreatioClientAdapter`** — too broad; other commands
  have different side-effect semantics. Scoped to section creation where the
  retry hazard is proven.

## Affected files

- `clio/Command/ApplicationSectionCreateCommand.cs` — budget, classifier wiring,
  verification.
- `clio/Command/ApplicationSectionCreateFailure.cs` — new enum + exception.
- `clio/Common/SelectQueryHelper.cs` — optional `requestTimeout` parameter.
- `clio/Command/McpServer/Tools/ApplicationToolResponses.cs` — new fields.
- `clio/Command/McpServer/Tools/ApplicationToolSupport.cs` — error mapping.
- `clio/Command/McpServer/Tools/ApplicationTool.cs` — catch clause + description.
- Tests: `clio.tests/Command/ApplicationSectionCreateServiceTests.cs`,
  `clio.tests/Command/McpServer/ApplicationToolTests.cs`,
  `clio.mcp.e2e/ApplicationSectionToolE2ETests.cs`.
- Docs: `clio/help/en/create-app-section.txt`,
  `clio/docs/commands/create-app-section.md`, `clio/Commands.md`,
  MCP guidance resources, `docs/McpCapabilityMap.md`.
