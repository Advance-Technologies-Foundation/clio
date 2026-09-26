---
description: MCP tool-result text is serialized with UnsafeRelaxedJsonEscaping and describe-business-process writes COMPACT JSON - the default encoder's HTML escaping (quote as ") made a describe of a ten-element process 53-62K chars and spilled it on every call; do not restore it as "safer"
applies-to:
  - clio/BindingsModule.cs
  - clio/Command/DescribeProcessCommand.cs
  - clio.tests/Command/McpServer/McpResultEncodingTests.cs
ticket: ENG-99970
date: 2026-09-26
---

**What is true** — `BindingsModule.CreateMcpSerializerOptions` sets
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so the TEXT a tool result is serialized into writes a quote
inside a string as `\"`, and an apostrophe, backtick, dash or non-ASCII letter as itself. The default
encoder wrote each of them as a six-character `\uXXXX` escape. Control characters are still escaped
(`McpResultEncodingTests` pins both). `DescribeProcessCommand.OutputOptions` is compact (no indentation)
with the same encoder.

**Why it is this way** — describe-business-process returns its graph as a JSON STRING inside the command
result, so the graph is encoded twice: every quote of the indented graph became `"` and every line
break `\r\n`. Measured on 2026-09-26 in two headless Claude Code process builds, every describe of a
ten-element process was 53-62 thousand characters, over Claude Code's inline limit: all 8 were spilled to a
file, and the agent spent 22 and 26 Grep calls reading them back. The same graphs are 30-36 thousand
characters compact with the relaxed encoder. Guidance articles, full of backticks and dashes, come back
about 10% smaller as well. A tool result is read over JSON-RPC and never embedded in an HTML page, which is
the only place the "unsafe" escaping rules matter; every JSON parser reads both forms identically.

**What breaks if you ignore it** — restoring the default encoder "for safety" makes nothing safer and puts
every describe result back over the inline limit, and the spill-and-grep loop costs a process build more
turns than any guidance saving recovers. A test that matched a result's text on `'` or `"` was
matching the escape, not the content; match the character. Re-indenting the describe output for
readability has the same cost: describe has no CLI verb, so nobody reads it in a terminal.
