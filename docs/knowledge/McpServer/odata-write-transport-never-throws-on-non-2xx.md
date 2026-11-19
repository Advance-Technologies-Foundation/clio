---
description: IApplicationClient.ExecuteGetRequest/ExecutePatchRequest/ExecutePostRequest/ExecuteDeleteRequest return the raw response body regardless of HTTP status - a write tool that does not inspect that body will report success even when the server never applied the change
applies-to:
  - clio/Command/McpServer/Tools/ODataUpdateTool.cs
  - clio/Command/McpServer/Tools/ODataDeleteTool.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio/Command/McpServer/Tools/ODataKeyedWrite.cs
  - clio/Common/CreatioResponseError.cs
  - clio/Common/CreatioClientAdapter.cs
  - clio/Common/SiteResponse.cs
  - clio/Common/SiteResponseCapture.cs
  - clio/Common/IStatusPreservingApplicationClient.cs
  - clio/Command/RuntimeVerification/RecordDeletionService.cs
ticket: ENG-95971
date: 2026-11-19
---

**What is true** — `CreatioClientAdapter`'s `ExecuteGetRequest`/`ExecutePatchRequest`/
`ExecutePostRequest`/`ExecuteDeleteRequest` do not throw on a non-2xx HTTP status; they return
whatever body the transport received. An IIS/proxy error page (a 401 or 404 HTML page, observed
alternating on the same entity in a client session) or a `{Message}`/`{error}` JSON fault therefore
comes back as an ordinary string, indistinguishable from a real payload unless the caller parses it.
`ODataReadTool` already does this (`JsonDocument.Parse` + `CreatioResponseError.TryDetect`), so a bad
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
as JSON or matches `CreatioResponseError.TryDetect` is a failure. `ODataCreateTool` shares the same
`CreatioResponseError.DescribeNonJsonResponse` diagnostic — which, when the body is an HTML error
page stating its status in the title, adds a sentence naming it ("The server answered with an
HTTP 404 error page."), the only place a write caller can learn which hop answered — and now reports `record-created: null`
(unknown, not created) on a non-JSON body, consistent with its existing "unknown side effect" model
for a mid-flight server error.

**What breaks if you ignore it** — any new odata-* write tool (or a change to an existing one) that
calls `client.ExecutePatchRequest(...)`/`ExecutePostRequest(...)`/`ExecuteDeleteRequest(...)` and
returns `success:true` without inspecting the response reintroduces silent data loss: a genuine
server-side failure (auth/session issue, routing error, proxy/IIS error page) is reported to the
agent and the user as a completed write. Route every new keyed write through
`ODataKeyedWrite.ValidateWriteResponse`, and a batch/row-based write through the same
`CreatioResponseError.TryDetect` + `DescribeNonJsonResponse` pair `ODataCreateTool` uses.

**Since the site-response change (clio #1988)** the odata write tools no longer depend on the body alone. odata-update, odata-delete and odata-create send through `SiteCall` (`IStatusPreservingApplicationClient.SendKeepingStatus`), which keeps the HTTP status when the client implements that interface (`CreatioClientAdapter` does; with any other client `SiteCall` falls back to the string method and the status is null). The string methods above still return the body for any status, so this record stays true for any caller that uses them directly.

- `ODataKeyedWrite.ValidateWriteResponse(SiteHttpResponse)` fails a known non-2xx even when the body is empty or an unrecognised JSON shape. A non-JSON body keeps its `DescribeNonJsonResponse` text. For an empty or unrecognised body, a refusal without processing (below) reads "refused … without processing it, so this request was not applied", a 5xx reads "not confirmed", and any other 3xx or 4xx reads "refused, which does not prove it was not applied".
- A failed result carries `site-response` (the status, the site's own message and code, and the body kind) and, beside it, `diagnostic.side-effect`.
- `side-effect` is `none` only when the site refused the request for its shape: HTTP 405, 413, 414 or 415 with an empty, page or plain-text body, answering the request clio sent (not one the client sent after following a redirect, see `SiteHttpResponse.Redirected`).
  - That holds for one PATCH (odata-update) or one row's POST (odata-create).
  - It never holds for a DELETE: the DELETE goes without a body, and creatio.client can repeat it through a 300, 307 or 308 unseen.
  - A 401 or 403 never proves it either.

The `transport-outcome` value `response-received` still means only that an answer arrived. Unrecognised bodies must not be echoed as diagnostic previews. Retain the uncertainty and inspect the affected records.
