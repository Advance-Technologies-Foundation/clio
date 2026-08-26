---
description: the empty "ClioProcessPath" in clio.mcp.e2e/appsettings.json is load-bearing - a machine-specific path there makes every fixture that does not call ResolveFreshClioProcessPath fail with IOException "Failed to connect transport"
applies-to:
  - clio.mcp.e2e/appsettings.json
  - clio.mcp.e2e/appsettings.example.json
  - clio.mcp.e2e/Support/Mcp/ClioExecutableResolver.cs
  - clio.mcp.e2e/Support/Configuration/TestConfiguration.cs
ticket: ENG-90640
date: 2026-08-19
---

**What is true** — `"ClioProcessPath": ""` in the committed `clio.mcp.e2e/appsettings.json` is a
deliberate value, not an unfinished one. `ClioExecutableResolver.Resolve` treats a blank value as
"launch the clio assembly next to the test assembly" (`typeof(McpServerCommand).Assembly.Location`)
and only honours a configured path when it is non-blank. Fixtures that assign
`TestConfiguration.ResolveFreshClioProcessPath()` in their arrange override it per test; the many
fixtures that do not are relying entirely on that blank-value fallback. The related resolution in
`TestConfiguration` derives the target framework from the test assembly's own output directory and
uses `net10.0` only as a last-resort default, because the suite is multi-targeted and CI does not
necessarily run the newest TFM.

**Why it is this way** — the file is committed, so any absolute path in it is one developer's
machine layout imposed on CI and on every other checkout. There is no way to express "the build
output I was compiled into" as a JSON literal, so the empty string is the only portable value.

**What breaks if you ignore it** — filling the field in locally and committing it (or hardcoding a
TFM) makes the stdio MCP server spawn from a directory that does not exist on the agent. The
failures land as `System.IO.IOException: Failed to connect transport` across dozens of unrelated
fixtures at once: the message names the transport, never the missing executable, so the cluster
reads as an MCP protocol or timeout regression and gets investigated in the server code.
