---
description: IApplicationClient.ExecuteGetRequest returns the response body for ANY status code and turns HttpRequestException/TaskCanceledException into an empty string, so a caller cannot tell 200 from 404 or from a failed request
applies-to:
  - clio/Common/IApplicationClient.cs
  - clio/Common/CreatioClientAdapter.cs
  - clio/Command/Theming/GetThemeCommand.cs
ticket: ENG-93991
date: 2026-09-11
---

**What is true** — the synchronous GET on the shared Creatio client contract
(`IApplicationClient.ExecuteGetRequest`, backed by `Creatio.Client` 2.0.2 `CreatioClient`) exposes no
success indicator at all. Its `ReadResponseBody` helper reads `Content` and returns it whatever the
status code is — there is no `EnsureSuccessStatusCode` on that path; in the whole client the call
appears only in the upload methods. On top of that, `ExecuteGetRequest` catches
`HttpRequestException` and `TaskCanceledException` and returns `string.Empty`. A 200 with an empty
body, a 404, a 403 and a connection failure are therefore a single indistinguishable outcome at the
call site: the empty string.

`ICreatioApplicationClient.ExecuteGetRequestAsync` does return an `HttpResponseMessage` with a
status, but that interface is deliberately excluded from DI auto-registration in `BindingsModule`
(alongside `IApplicationClient` and `IOwnedApplicationClient`) because application-client
implementations have ownership-sensitive constructors. Constructor-injecting it fails
`ValidateOnBuild`; reaching it means going through `IApplicationClientFactory` and an
`IOwnedApplicationClient` the caller disposes.

**Why it is this way** — the contract is a stable public surface with implementations outside this
repository (the same reason `ExecutePutRequest` is a defaulted member rather than an abstract one),
so it was never widened with a status-carrying GET.

**What breaks if you ignore it** — a command that treats the returned string as "the resource" reports
success on a resource that was never served. `GetThemeCommand.TryFetchCss` is the worked example: the
theme CSS it returns is fed straight back into `update-theme`, so accepting an empty body would let a
read-edit-update round trip overwrite a real stylesheet with nothing. It guards the body instead of the
status — empty/whitespace, a leading `<` (an IIS error page or login redirect) and a leading `{` (a
platform JSON error envelope) are all refused — because the status is not reachable from here.
