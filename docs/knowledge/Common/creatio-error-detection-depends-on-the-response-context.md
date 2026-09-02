---
description: CreatioResponseError classifies a body against the endpoint that produced it, because the same JSON shape is an error from an OData controller and a payload from a custom service
applies-to:
  - clio/Common/CreatioResponseError.cs
ticket: 1220
date: 2026-08-31
---

**What is true** — `CreatioResponseError.TryDetect` takes a `CreatioResponseContext` and runs a
different detector set for each. `CreatioResponseContext.ODataPayload` (the `odata-*` tools) keeps
the bare `{"Message"[,"MessageDetail"]}` routing shape as an error signal and does not run the
`BaseResponse` detector. `CreatioResponseContext.Service` (`call-service`, the configuration
services, `AuthService`) runs `BaseResponse` and accepts the bare-`Message` shape only when the text
is one of the ASP.NET routing-miss wordings. Only the OData control annotations
(`@odata.context`, `@odata.id`, `@odata.etag`) count as proof that a body is a payload; `Id`, `id`
and `value` do not, because any envelope can carry them.

**Why it is this way** — an OData endpoint's payload shape is fixed by the protocol, so a body whose
only member is `Message` cannot be an entity. A custom service owns its own contract and may answer
`{"Message":"OK"}`. Symmetrically, `success` is a `BaseResponse` envelope flag on a service body but
an ordinary entity column on an OData body.

**What breaks if you ignore it** — running the OData detectors against a service body exits 1 on a
valid `{"Message":"OK"}` and refuses to write `--destination`. Running the service detectors against
an OData body reports a created record as failed *after* the write, which invites a duplicate retry.
Treating `Id` as proof of a payload lets `{"Code":-1,"Exception":"...","Id":"..."}` be saved and exit
0 — the false success this whole contract exists to remove.
