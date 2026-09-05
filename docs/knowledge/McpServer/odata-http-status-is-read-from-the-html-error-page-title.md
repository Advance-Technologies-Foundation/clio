---
description: the odata-* transport exposes no HTTP status, so odata-read's status-code member is parsed out of the IIS error page <title>; a page without a status in its title yields no status-code at all
applies-to:
  - clio/Common/CreatioResponseError.cs
  - clio/Command/McpServer/Tools/ODataReadTool.cs
  - clio/Command/McpServer/Tools/ODataFieldValidation.cs
ticket: GH-1325
date: 2026-09-05
---

**What is true** — the GET the odata-* tools actually issue is the synchronous
`IApplicationClient.ExecuteGetRequest`, which returns the response body and nothing else: no out
parameter, no property and no exception carries the HTTP status, and a non-2xx does not throw.
A status-bearing GET *does* exist — `ICreatioApplicationClient.ExecuteGetRequestAsync` returns an
`HttpResponseMessage` — but it is not interchangeable with the synchronous one: in
`CreatioClientAdapter` the synchronous methods go through `ExecuteRequest`, the reauth wrapper that
retries once after `ReauthExecutor.IsSessionExpiredResponse` recognises an expired session in the
*body string*, while `ExecuteGetRequestAsync` only passes through `_loginDiagnostics.TrackRequestAsync`
and has no such retry. Switching the read path to the async overload for the sake of the status would
silently drop session recovery for every odata-* read.

So `status-code` on `ODataReadResponse` is not the transport's status. It is the three digits parsed
out of the IIS/proxy error page's own title — `<title>404 - File or directory not found.</title>`,
`<title>HTTP Error 500.0 - ...</title>`, `<title>502 Bad Gateway</title>` — by
`CreatioResponseError.TryGetMarkupErrorStatusCode`, and only 4xx/5xx are accepted. Nothing else from
that page reaches the caller: the read path copies no fragment of a server or proxy body into an MCP
transcript, so the status digits are the single exception, and they are chosen precisely because they
are not tenant data. A page whose title states no status (Creatio's own `<title>Request Error</title>`,
an SSO login page) leaves `status-code` absent and gets the neutral "not a JSON OData response"
diagnosis — never the "may not be exposed / use execute-esq" steer, which is reserved for the 404.
`ODataFieldValidation`'s pre-write probe runs its non-JSON body through the same classification, so a
probe against an entity behind an IIS 404 reports the status and the retry hint too.

**Why it is this way** — `core-rules` documents a legitimate 404 window: after
`create-entity-schema`/`create-lookup` the entity's OData controller is rebuilt asynchronously
(~1-2 min). A caller following that rule has to tell that transient 404 from an entity that is
permanently not exposed over OData, and there is no other place in this process where the status is
observable. Widening `IApplicationClient` to carry the status is a source-breaking change to a
public contract with implementations outside this repository — the same reason `ExecutePutRequest`
was added as a defaulted member rather than an abstract one — and the async overload that already
carries it cannot be used until it has the reauth wrapper's parity.

**What breaks if you ignore it** — assume the status came from the transport and you will trust it
where no HTML page was involved: a JSON routing error (`{"Message":...,"MessageDetail":...}`, which
Creatio serves with HTTP 200) sets no `status-code`, and a caller that branches on
`status-code == 404` alone will treat that path as a non-404 and skip the retry the async gap needs.
The JSON branch carries `CreatioResponseError.UnregisteredEntityHint` in the message instead; both
branches must be handled. Conversely, deriving a status from anything but the title — the presence
of the string "404" anywhere in the page, say — misreports a 405 or a 503 as the retryable case and
sends the caller into a 1-2 minute wait for an outage that will not clear.
