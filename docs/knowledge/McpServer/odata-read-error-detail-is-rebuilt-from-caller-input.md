---
description: odata-read reports no server wording at all - not even "Could not find a property named X" - so its invalid-query message is rebuilt from the CALLER's own filter/select/expand/order-by names, and only for a CLASSIFIED query-shape failure
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
  `CreatioResponseError`;
- the DETAIL the caller reads is a restatement of the request: the filter fields, `select`, `expand`
  and `order-by` names that arrived in `ODataReadArgs`. What makes echoing them safe is only that they
  are the caller's own text going back to the caller — `ValidateArguments` now holds all four to
  `ODataKeyFormatter.IsValidMemberPath`, but that is a request-hygiene rule, not what authorizes the
  echo;
- the lookup hint (`SysSettingsId` → `SysSettings/Id`) is likewise derived from the request alone:
  `ODataKeyFormatter.IsIdish` (the word-boundary rule, so `Paid` and `Void` are not lookups), longer
  than `Id`, and containing no `/`.

**The whole restatement is gated on `ODataErrorKind.InvalidQuery`.** On any other kind the query is
probably not what failed, so `DescribeCallerQuery` returns null.

**Why it is this way** — the response body is authored by a service or a proxy, and the MCP transcript
is read by a model as trusted content, so no fragment of it may be quoted. But a caller who is told
only "the query was invalid" cannot act: several names went out in one request and none of them is
named back. Restating the request closes that gap with text that was never the server's to begin with.
Gating it on the classification is the other half: a measured
`{"Message":"An error has occurred.","ExceptionMessage":"Object reference ..."}` fault classifies as
`server-reported-error`, and naming the caller's members over it starts the hunt in the wrong place.

**What breaks if you ignore it** — reintroducing `error.message` (or `MessageDetail`, or the
`internalexception` sentence) into `error` reopens issue #1333 silently: nothing fails, the prose simply
appears where a model reads it as guidance. Dropping the caller-input restatement returns `odata-read`
to the state issue #1407 reports — a bare classification with no way to tell which member the server
rejected. Un-gating it is the third failure: a real `TaxId` or `ExternalId` column on an unrelated
server fault gets "filter on `Tax/Id` instead", and the caller rewrites a query that was correct.

## The one sink for the server excerpt, and what it is worth today

The excerpt reaches `ILogger.WriteDebug`, fenced by `UntrustedText.Fenced` and tagged with the
response's `correlation-id`. **That line is not reachable where `odata-read` normally runs.**
`ConsoleLogger.WriteDebug` returns early unless `Program.IsDebugMode`, which is set only from this
process's own arguments (`Program.cs`, `args.Any(x => x == "--debug")`); `odata-read` is a Stage-6
worker tool (`McpWorkerCohort.StageSixNames`) and the worker is spawned as
`mcp-server --mcp-worker` (`McpWorkerCallDispatcher`), with no `--debug`. So in an MCP session the
correlation-id's real job is matching a response to clio's own log lines, not to a server excerpt. Say
that in caller-facing text rather than promising a debug line — and do not plumb `--debug` into the
worker spawn to make the promise true without a measured need for it.
