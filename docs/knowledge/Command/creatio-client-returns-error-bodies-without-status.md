---
description: Creatio.Client's POST returns an error body without its HTTP status and throws nothing that carries one, so a caller classifies the body (call-service, the process compile) and never an HttpRequestException status
applies-to:
  - clio/Query/DataServiceQuery.cs
  - clio/Command/CompileBusinessProcessCommand.cs
ticket: 1220
date: 2026-09-30
---

**What is true** — the `Creatio.Client` methods behind `IApplicationClient` return the body of a failed
service call and not its HTTP status, and nothing thrown on that path carries one. A Creatio error
envelope (`Code` plus `Exception` or `Message`), the sign-in page and an IIS error page therefore reach
the caller as ordinary strings. Every caller that has to tell a failure from an answer classifies the
BODY: `call-service` before it writes `--destination`, the process compile before it reads the compile
result.

**Why it is this way** — POST and DELETE use `HttpClient` and read `Content` without checking
`IsSuccessStatusCode`; GET's compatibility helper similarly returns the body from a `WebException`.
The shared client contract exposes only `string`, so a caller cannot recover a missing status
without replacing that transport contract.

**What breaks if you ignore it** — an HTTP failure is saved and reported as `Result saved` with exit
code 0. Automation then parses an error envelope or an IIS HTML page as if the service succeeded,
and the original request failure is discovered only downstream. The opposite mistake is as easy: a
catch that classifies `HttpRequestException.StatusCode` on a POST never fires, because no exception
carries a status there, and a unit test that stubs one passes while production takes the other branch.
For the process compile the cost is the caller's next step: an error body read as "no result" sends
an agent to wait for a compile that may never have started, and one read as a result reports a
refused request as a compile that failed.
