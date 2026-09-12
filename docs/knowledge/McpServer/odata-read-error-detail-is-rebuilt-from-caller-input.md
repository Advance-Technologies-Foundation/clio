---
description: odata-read reports no server wording at all - not even "Could not find a property named X" - so its invalid-query message is rebuilt from the CALLER's own filter/select/expand/order-by names, and the raw-lookup-column hint is a caller-input heuristic, not an echo of the server
applies-to:
  - clio/Command/McpServer/Tools/ODataReadTool.cs
  - clio/Common/CreatioResponseError.cs
ticket: GH-1407
date: 2026-09-12
---

**What is true** — issue #1407 was reported against a build whose `odata-read` still surfaced
`The query specified in the URI is not valid. Could not find a property named 'Name' ...`. By the time
it was fixed that had already stopped being true: the redaction work (issue #1333,
`server-prose-never-reaches-a-non-debug-diagnostic-field.md`) had made
`CreatioResponseError.DescribeServerReportedReadError` a fixed local sentence, so BOTH reported failure
modes answered with the same opaque text and the useful property-name error was gone as well. Do not
"restore" it — the diagnosis has to be rebuilt locally instead:

- the classification comes from the server text but only a kind (`ODataErrorKind`) leaves
  `CreatioResponseError`; the text itself reaches exactly one sink, `ILogger.WriteDebug`, fenced by
  `UntrustedText.Fenced` and tagged with the response's `correlation-id`;
- the DETAIL the caller reads is a restatement of the request: the filter fields, `select`, `expand`
  and `order-by` names that arrived in `ODataReadArgs`. What makes echoing them safe is only that they
  are the caller's own text going back to the caller - `select`, `expand` and `order-by` are NOT
  validated anywhere (`BuildQueryString` merely trims and joins them); filter fields alone have passed
  `ODataKeyFormatter.IsValidMemberPath`. Do not justify the echo by a validation that covers one of the
  four;
- the lookup hint (`SysSettingsId` → `SysSettings/Id`) is likewise derived from the request alone:
  a filter field that ends in `Id`, is not `Id`, and contains no `/`.

Two platform facts the classifier depends on, both observed on a real .NET Framework stand (10.2.117):

1. A filter on a raw foreign-key column answers `error.message` = `"An error has occurred."` and hides
   the cause TWO levels down, in `error.innererror.internalexception.message` =
   `"Column by path SysSettingsId not found in schema SysSettingsValue."`. Classifying on the headline
   alone therefore reports a query-shape failure as an unexplained server error, which is why
   `TryClassify` walks the `innererror` / `internalexception` chain.
2. An unknown property names itself in the headline
   (`"The query specified in the URI is not valid. Could not find a property named 'Name' on type ..."`),
   so the two cases need different traversal but land on the same `invalid-query` kind.

**Why it is this way** — the response body is authored by a service or a proxy, and the MCP transcript
is read by a model as trusted content, so no fragment of it may be quoted. But a caller who is told
only "the query was invalid" cannot act: several names went out in one request and none of them is
named back. Restating the request closes that gap with text that was never the server's to begin with.

**What breaks if you ignore it** — reintroducing `error.message` (or `MessageDetail`, or the
`internalexception` sentence) into `error` reopens issue #1333 silently: nothing fails, the prose simply
appears where a model reads it as guidance. Conversely, dropping the caller-input restatement returns
`odata-read` to the state issue #1407 reports — a bare classification with no way to tell which member
the server rejected, and no hint that a lookup must be filtered through its navigation path.
