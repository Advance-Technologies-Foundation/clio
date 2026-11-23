---
description: McpResultSize.InlineReplyBudgetBytes (18,000 bytes) is ONE budget shared by describe-business-process's auto-summary and every web-service tool reply; clio-knowledge holds guidance to 19,000 with an envelope estimate; the web-service narrowing runs at the tool boundary, never in the designer
applies-to:
  - clio/Command/McpServer/McpResultSize.cs
  - clio/Command/ProcessModel/ProcessDigest.cs
  - clio/Command/McpServer/Tools/WebServiceReplyBudget.cs
  - clio/Command/McpServer/Tools/WebServiceTools.cs
  - clio/Command/WebServiceDesigner/WsdlReader.cs
ticket: ENG-107277
date: 2026-11-23
---

**What is true** — `McpResultSize.InlineReplyBudgetBytes` = 18,000 UTF-8 bytes, measured as the agent receives the
result text (`McpResultSize.Of`), is the single number behind `ProcessDigest.AutoSummaryThresholdBytes` and the
web-service reply narrowing (`WebServiceReplyBudget`, `WsdlReader`). clio-knowledge holds every guidance article to
19,000 bytes in `GuidanceInlineReplyBudgetTests`, an article plus a 1,300-byte envelope ESTIMATE, in another repository.
The web-service narrowing runs in the MCP tools (`WebServiceToolRunner.Run`, `TestWebServiceMethodTool`,
`ListWebServicesTool`); `WebServiceDesigner.Describe` always returns every method.

**Why it is this way** — Copilot CLI hands a tool result to the model whole up to 20,480 bytes and writes a larger one
to a file the agent reads back in pieces, one model request per piece (ENG-106667: about a third of a Copilot CAADT
run's cost). clio measures its own replies exactly, so it keeps about 2 KB under the line; clio-knowledge cannot measure
the envelope clio wraps around an article and estimates it. read-wsdl reads an existing service through
`IWebServiceDesigner.Describe` to keep its method codes (D11), so the designer must not narrow.

**What breaks if you ignore it** — raising the constant for one tool raises it for describe-business-process too, and
a reply between 18,000 and 20,480 bytes still fits Copilot but has no room for a longer correlation id or notice.
Moving the narrowing into the designer makes read-wsdl with `service-code` miss the methods it left out: their
operations get no `existing-methods` entry and the agent's `addMethod` is refused as a duplicate code. Equating the two
repositories' numbers "for consistency" either spills guidance or narrows tool replies for nothing.
