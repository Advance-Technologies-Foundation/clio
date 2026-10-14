---
description: MCP tool-result text is serialized with McpResultJsonEncoder (relaxed, Format chars still escaped) and describe-business-process writes COMPACT JSON - the default encoder's HTML escaping (a quote as \u0022) made a describe of a ten-element process 53-62K chars and spilled it on every call; do not restore it as "safer"
applies-to:
  - clio/BindingsModule.cs
  - clio/Command/McpServer/McpResultJsonEncoder.cs
  - clio/Command/DescribeProcessCommand.cs
  - clio/clio.csproj
  - clio.tests/Command/McpServer/McpResultEncodingTests.cs
  - clio.mcp.e2e/ToolContractGetToolE2ETests.cs
  - clio/Command/CreatioArtifactMergeService.cs
ticket: ENG-99970
date: 2026-09-26
---

**What is true** — `BindingsModule.CreateMcpSerializerOptions` sets `McpResultJsonEncoder`:
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` plus the BMP characters of Unicode category Format. The TEXT a
tool result is serialized into writes a quote inside a string as `\"`, and an apostrophe, backtick, dash or
non-ASCII letter as itself; the default encoder wrote each of them as a six-character `\uXXXX` escape.
Still escaped: C0/C1 control characters (ESC, CR, LF, BEL, CSI, OSC), U+2028/U+2029, every astral character,
and - the one addition over the relaxed encoder - category Format: bidi controls such as U+202E, zero-width
characters, the soft hyphen. `McpResultEncodingTests` pins each group. `DescribeProcessCommand.OutputOptions`
is compact (no indentation) with the same encoder. `clio.csproj` allows unsafe code for this one class,
because `JavaScriptEncoder`'s abstract members take `char*`.

**Why it is this way** — describe-business-process returns its graph as a JSON STRING inside the command
result, so the graph is encoded twice: every quote of the indented graph became `\u0022` and every line
break `\r\n`. Measured on 2026-09-26 in headless Claude Code process builds, every describe of a ten-element
process was 53-62 thousand characters, over Claude Code's inline limit: all were spilled to a file, and the
agent spent 14-26 Grep calls per run reading them back. The same graphs are 30-36 thousand characters compact
with the relaxed encoder. Guidance articles, full of backticks and dashes, come back about 10% smaller as
well. The saving is in the result TEXT the model reads: the SDK writes the JSON-RPC frame around it with
its own default encoder, and the agent CLI decodes that frame before the model sees the text. A tool result
is never embedded in an HTML page, the context the HTML-escaping rules exist for; every JSON parser reads
both forms identically. Format characters are kept
escaped because they are invisible and reorder or hide text, and Creatio data is written by whoever has
access to the environment.

**What breaks if you ignore it** — restoring the default encoder "for safety" makes nothing safer and puts
every describe result back over the inline limit, and the spill-and-grep loop costs a process build more
turns than any guidance saving recovers. Replacing the custom encoder with the plain relaxed one lets a bidi
override in a Creatio caption reach the agent raw, where it reorders the transcript and any approval prompt
that quotes the caption. A test that matched a result's text on `\u0027` or `\u0022` was matching the escape,
not the content; match the character. Re-indenting the describe output for readability has the same cost as
the old encoder: describe has no CLI verb, so nobody reads it in a terminal.
