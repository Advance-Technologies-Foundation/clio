---
description: an agent calls a long-tail MCP tool through clio-run without reading its description, so a rule written only there does not change what the agent does
applies-to:
  - clio/Command/McpServer/Tools/WebServiceTools.cs
ticket: ENG-105475
date: 2026-11-13
---

**What is true** — an isolated agent (claude-sonnet-5-5, clio MCP only, 2026-11-13) asked to "add 2 and 3 as a test"
with a SOAP service read `core-rules`, `routing`, the no-names `get-tool-contract` index and two guides, then called
`test-web-service-method` through `clio-run` without asking for the tool's contract. The index line it saw was the
description's first sentence, "Test-calls a web service method."; the rule in the description's second sentence
("a request to test is not a confirmation ... call only after their explicit yes") never reached it, and it called the
SOAP method unasked. With a refusal in the tool instead (`user-confirmed` gate, `confirmation-required: true`), the same
case asked the user first.

**Why it is this way** — long-tail tools are not in `tools/list`; agents learn them from the compact index, whose
purpose is the first sentence cut to 48 characters, and `clio-run` dispatches a call without requiring the contract.

**What breaks if you ignore it** — a safety rule placed only in a long-tail tool's description (a confirmation, a
precondition, a forbidden argument) is silently not followed. Put it where the call path enforces it (a refusal the agent
reads on its first call) or in the guide the agent reads for the task; the first sentence cannot carry it.
