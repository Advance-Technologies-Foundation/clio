---
description: the MCP tool path and the CLI path build byte-identical Creatio request URLs including the 0/ WebAppAlias prefix, so a reported "works in CLI, fails in MCP" symptom is never a route-construction difference
applies-to:
  - clio/Command/McpServer/Tools/ToolCommandResolver.cs
  - clio/Common/ServiceUrlBuilder.cs
  - clio/Environment/ConfigurationOptions.cs
ticket: "#1106"
date: 2026-09-12
---

**What is true** — a name-addressed MCP tool call and the same CLI command against the same registered
profile issue the SAME requests. Measured through a logging TCP relay in front of a .NET Framework
stand (`IsNetCore: false`), `list-packages` produced, in both directions and in this order:
`POST /<alias>/ServiceModel/AuthService.svc/Login` (no `0/` — the documented login exception),
`POST /<alias>/0/ping`, `POST /<alias>/0/DataService/json/SyncReply/SelectQuery`, with an identical
cookie. Flipping the profile to `IsNetCore: true` dropped the `0/` segment on BOTH paths identically.
The reason is structural rather than incidental: `ToolCommandResolver.ResolveSettingsAndKey` reads the
stored profile and merges it through `EnvironmentSettings.Fill`, which copies `IsNetCore`, and the
per-environment child container hands that same instance to `ServiceUrlBuilder`. There is no MCP-side
runtime re-detection and no default that could disagree with the profile.

**Why it is this way** — the MCP surface deliberately resolves each call into a child container built
from the registered profile, rather than from the process-active environment a long-lived host was
started with. The `0/` decision therefore has exactly one input on both surfaces: the profile's
`IsNetCore` flag.

**What breaks if you ignore it** — "the CLI works but MCP fails on the same profile" is a recurring
report (issue #1106), and the natural first hypothesis is that the MCP path builds a route without the
`0/` prefix and gets redirected to the login HTML page. Chasing it costs a full investigation round and
finds nothing. A `SelectQuery returned an HTML page instead of JSON` answer from an MCP tool is a
SESSION or SERVER-STATE condition (a rejected login, a redirect, an unhandled server error), not a URL
built differently — start there. If you need to re-measure rather than trust this record, a Host-header-
rewriting TCP relay in front of the stand plus a throwaway profile pointed at it shows both paths' request
lines directly; nothing else exposes the URL, because the MCP error text redacts it.
