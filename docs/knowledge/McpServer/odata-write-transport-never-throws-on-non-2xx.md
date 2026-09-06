---
description: IApplicationClient.ExecuteGetRequest/ExecutePatchRequest/ExecutePostRequest/ExecuteDeleteRequest return the raw response body regardless of HTTP status - a write tool that does not inspect that body will report success even when the server never applied the change
applies-to:
  - clio/Command/McpServer/Tools/ODataUpdateTool.cs
  - clio/Command/McpServer/Tools/ODataDeleteTool.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio/Command/McpServer/Tools/ODataKeyedWrite.cs
  - clio/Common/CreatioResponseError.cs
  - clio/Common/CreatioClientAdapter.cs
ticket: ENG-95971
date: 2026-08-25
---

**What is true** — `CreatioClientAdapter`'s `ExecuteGetRequest`/`ExecutePatchRequest`/
`ExecutePostRequest`/`ExecuteDeleteRequest` do not throw on a non-2xx HTTP status; they return
whatever body the transport received. An IIS/proxy error page (a 401 or 404 HTML page, observed
alternating on the same entity in a client session) or a `{Message}`/`{error}` JSON fault therefore
comes back as an ordinary string, indistinguishable from a real payload unless the caller parses it.
`ODataReadTool` already does this (`JsonDocument.Parse` + `ODataResponseError.TryDetect`), so a bad
response correctly surfaces as `success:false`. Before this ticket, `ODataUpdateTool` and
`ODataDeleteTool` discarded the response entirely and returned `success:true` unconditionally, and
`ODataCreateTool.ParseCreated` treated *any* non-JSON body as proof the record was created. A client
reported "odata-update works, everything else is broken" — the write tools were not actually more
reliable, they were blind to the same failure the read tools correctly reported.

**Why it is this way** — a successful PATCH/DELETE against Creatio's OData v4 endpoint normally
returns `204 No Content` (empty body), so "no exception, empty body" reads as a reasonable
approximation of success. The gap is what happens when the body is non-empty but is not a real
OData payload: nothing forced the write tools to look at it, because the transport layer's contract
is "return the body, don't interpret it" — that responsibility sits entirely with the caller. The
fix is `ODataKeyedWrite.ValidateWriteResponse`: empty body stays success, a body that fails to parse
as JSON or matches `ODataResponseError.TryDetect` is a failure. `ODataCreateTool` shares the same
`ODataResponseError.DescribeNonJsonResponse` diagnostic and now reports `record-created: null`
(unknown, not created) on a non-JSON body, consistent with its existing "unknown side effect" model
for a mid-flight server error.

**What breaks if you ignore it** — any new odata-* write tool (or a change to an existing one) that
calls `client.ExecutePatchRequest(...)`/`ExecutePostRequest(...)`/`ExecuteDeleteRequest(...)` and
returns `success:true` without inspecting the response reintroduces silent data loss: a genuine
server-side failure (auth/session issue, routing error, proxy/IIS error page) is reported to the
agent and the user as a completed write. Route every new keyed write through
`ODataKeyedWrite.ValidateWriteResponse`, and a batch/row-based write through the same
`ODataResponseError.TryDetect` + `DescribeNonJsonResponse` pair `ODataCreateTool` uses.
