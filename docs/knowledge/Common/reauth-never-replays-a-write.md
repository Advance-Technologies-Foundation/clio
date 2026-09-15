---
description: automatic re-authentication replays a call only when the caller said it may - PUT/PATCH/DELETE never replay, POST replays unless the caller used ExecuteNonReplayablePostRequest, and the read/write split for POST is a call-site decision because SelectQuery is a POST too
applies-to:
  - clio/Common/IReauthExecutor.cs
  - clio/Common/ReauthExecutor.cs
  - clio/Common/NoReauthExecutor.cs
  - clio/Common/CreatioClientAdapter.cs
  - clio/Common/IApplicationClient.cs
  - clio/Query/DataServiceQuery.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio/Command/McpServer/Tools/EmailTemplateTool.cs
ticket: "#1313"
date: 2026-09-12
---

**What is true** — `IReauthExecutor.Execute` takes a required `replayAllowed` flag, and the rule behind
it is not the HTTP verb alone:

- `ExecutePutRequest`, `ExecutePatchRequest`, `ExecuteDeleteRequest` pass `false`. Every clio call site
  of those three verbs is a record write, so the whole verb can be decided in `CreatioClientAdapter`.
- `ExecutePostRequest` passes `true`. POST is the one verb clio uses for **both** directions:
  DataService `SelectQuery` reads, `CallConfigurationService` and record-creating writes all go
  through it. Turning replay off for POST would switch off the expired-session recovery that
  `ReauthExecutor` exists for.
- `CallConfigurationService` and the `UploadAlmFile` / `UploadAlmFileByChunk` / `UploadFile` family
  also pass `true`, even though they are not reads. The rule is not "every write passes `false`" -
  it is "the verbs clio only ever uses for record writes refuse replay, and so does a POST the
  caller declared a write". Those four are the calls most likely to outlive a session (package
  install, long compile triggers, multi-megabyte uploads), so they keep the recovery.
- A caller that knows its POST is a write asks for it by name: `ExecuteNonReplayablePostRequest`, a
  defaulted member on `IApplicationClient` whose default body simply forwards to `ExecutePostRequest`.
- `GET` keeps replaying.

`call-service` / `dataservice` therefore decides POST **per call site, by intent**, through
`BaseServiceCommand<T>.AllowsPostReplay(options)`:

- `dataservice -t select` is a read - a POST on the fixed `SelectQuery` route that changes nothing -
  so `DataServiceQuery` overrides the hook to allow the replay. Refusing it there would turn a
  recoverable stale session into a failed read and prevent no duplicate.
- `dataservice -t insert|update|delete` and every `call-service` POST keep the base `false`: the
  `call-service` endpoint is user-supplied, so its response shape is unknown and the body-based
  detector can misfire on a committed write.
- `--method PUT|PATCH|DELETE` needs no hook - `CreatioClientAdapter` refuses replay for those verbs
  outright. The trade-off is deliberate: `odata-update` / `odata-delete` and any `call-service`
  PUT/PATCH/DELETE lose the automatic session recovery, so an expired session surfaces to the caller
  as `ExpiredSession` and the operator retries by hand after checking the target.

Adoption of `ExecuteNonReplayablePostRequest` is deliberately staged. Its callers are the POSTs that
create or change a record against an endpoint whose response shape clio does not control:
`DataServiceQuery` (`call-service` / `dataservice`, where the endpoint is user-supplied, minus the
`select` read above),
`ODataCreateTool` and `EmailTemplateTool`'s create branch. Fixed-route POST writes elsewhere
(`DataBindingCommand`,
`CreateBusinessProcessCommand`, `ApplicationSectionDeleteCommand`, `PageUpdateOptions`, and
`RemoteCommand` through the generic `ExecutePostRequest<T>`, which has no non-replayable counterpart)
still replay. Move one over when its endpoint is shown to answer a committed write with markup -
not speculatively.

When replay is refused, the executor still performs the login, and the caller gets the original
unauthorized response back (or, on the `JsonException` path, the original exception). `call-service`
classifies that body as `ServiceResponseFailure.ExpiredSession`. That message deliberately does not
promise the request was rejected - it says the write may or may not have been applied and to verify
the target before re-running, because the ambiguity that forbids the automatic replay forbids the
promise too.

**Why it is this way** — the expired-session detector is body-based (`IsSessionExpiredResponse`): it
reads the response, not the HTTP status, because on-prem .NET Framework Creatio answers an expired
cookie with HTTP 200 and an HTML login page. A body-based test cannot distinguish "the server
rejected this write unauthenticated" from "the write committed and its legitimate response happens to
contain `/Login/`". `call-service` points at an arbitrary user-supplied endpoint, so the second case
is reachable, and replaying it commits the write twice.

`maxAttempts: 1` does not prevent this: that argument bounds `Creatio.Client`'s own transport retry,
which is a different layer from the reauth retry above it.

**What breaks if you ignore it** — passing `replayAllowed: true` from a write call site brings back a
silently duplicated PUT/PATCH/POST: two records created, or an update applied twice, with `success`
reported and nothing in the log. Passing `false` from a read call site (or from `ExecutePostRequest`
wholesale) breaks the other direction just as quietly: a long-running operation that outlives its
session stops recovering and starts returning raw login HTML to its caller, which is exactly the
ENG-90393 symptom the executor was written for. Do **not** re-add a caller-facing `MaxAttempts`-style
escape hatch to re-enable replay - that was added in `56addae21` and removed the same day in
`a0f2ac01f`; see the remarks in `DataServiceQuery.ExecuteServiceRequest`.
