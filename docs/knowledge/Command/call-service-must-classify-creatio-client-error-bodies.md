---
description: call-service receives error bodies without HTTP status from Creatio.Client, so it must classify Creatio JSON and IIS HTML before writing the destination file
applies-to:
  - clio/Query/DataServiceQuery.cs
ticket: 1220
date: 2026-08-27
---

**What is true** — the `Creatio.Client` methods used by `IApplicationClient` return the response body
for failed service calls, but do not return the HTTP status alongside it. A Creatio error envelope
(`Code` plus `Exception`) and an IIS error page therefore look like ordinary strings to
`BaseServiceCommand` unless it classifies them before writing `--destination`.

**Why it is this way** — POST and DELETE use `HttpClient` and read `Content` without checking
`IsSuccessStatusCode`; GET's compatibility helper similarly returns the body from a `WebException`.
The shared client contract exposes only `string`, so `call-service` cannot recover a missing status
without replacing that transport contract.

**What breaks if you ignore it** — an HTTP failure is saved and reported as `Result saved` with exit
code 0. Automation then parses an error envelope or an IIS HTML page as if the service succeeded,
and the original request failure is discovered only downstream.
