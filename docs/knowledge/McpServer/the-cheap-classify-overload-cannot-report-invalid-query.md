---
description: CreatioResponseError.TryClassify's `out bool isUnregisteredEntity` overload can never report ODataErrorKind.InvalidQuery, so a caller that picks it to avoid building the server message silently downgrades every rejected query to server-reported-error
applies-to:
  - clio/Common/CreatioResponseError.cs
  - clio/Command/McpServer/Tools/ODataFileContract.cs
  - clio/Command/McpServer/Tools/ODataReadTool.cs
ticket: "1221"
date: 2026-09-21
---

**What is true** — `CreatioResponseError.TryClassify` has two overloads and they do not classify the
same set. The cheap one, `out bool isUnregisteredEntity`, calls `TryDetectCore` with
`buildMessage: false` and can only distinguish "routing miss" from "some server error". The richer
one, `out ODataErrorKind kind, out string serverDetailForDebugChannel`, is the ONLY one that can
return `ODataErrorKind.InvalidQuery`, because deciding that requires reading the server text (see
[odata-rejects-an-unresolvable-member-in-two-different-body-shapes](odata-rejects-an-unresolvable-member-in-two-different-body-shapes.md)
for why the text has to be walked two levels down). Both read paths therefore use the richer one;
`ODataReadTool.ErrorCodeFor` is shared so the kind maps to the same code from either.

**Why it is this way** — the cheap overload exists for a real reason: an OData error body is allowed
the whole 64 MiB response ceiling, and building its message allocates. That makes it the obvious
pick for the file read path, whose entire purpose is to keep large bodies out of memory and out of
the MCP transcript. The trade is that it is not a cheaper way to get the same answer — it is a
smaller answer.

**What breaks if you ignore it** — picking the cheap overload compiles, passes, and quietly turns
every rejected query into `error-code: server-reported-error`. That code says "the environment had a
problem", so an agent retries; `invalid-query` says "your request is wrong and an unchanged retry
cannot succeed". `odata-read-to-file` shipped that way and answered `server-reported-error` for a
filter on a non-existent lookup column while `odata-read` answered `invalid-query` for the same
body, against the same stand, in the same minute. Nothing failed: there was no test asserting the
code, and the two paths only disagree on a body that both of them reject. If you need the kind
without the allocation, the answer is not this overload — it is to bound the body before
classifying it.
