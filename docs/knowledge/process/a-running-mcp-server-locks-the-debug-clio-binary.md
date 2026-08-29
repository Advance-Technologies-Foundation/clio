---
description: a live clio MCP server process holds the clio build output it runs from (Debug OR Release) open, so rebuilding that output fails with MSB3021/MSB3027 file-in-use - switching configurations changes which binary clio.mcp.e2e drives, and the e2e harness itself leaks one server per session
applies-to:
  - clio/clio.csproj
  - clio.mcp.e2e/
date: 2026-08-27
---

**What is true** — when an editor or agent has a clio MCP server running, that process keeps the
build output it was launched from — `clio/bin/<Configuration>/<tfm>/` — open: `clio.exe`/`clio.dll`
plus its assemblies. Any `dotnet build` or `dotnet test` that would rewrite THAT output fails with
`MSB3027`/`MSB3021` "being used by another process". Release is not an escape when the server was
launched from Release (an MCP client configured against the repo's Release output re-spawns its
server on the next tool call, taking the lock again). The `clio.mcp.e2e` harness ALSO leaks one
`clio ... mcp-server` child per test session, so a lock can appear minutes after the run finished.
Ways out, in order of preference: kill the `mcp-server` processes holding the target output (check
BOTH configurations; MSB3027 names the PID); build the other configuration; or, when only test code
changed, build with `-p:BuildProjectReferences=false` and run with `--no-build` so the existing
binary is reused.

**Why it is this way** — the MCP server is a long-lived child process of the editor, not something
the build knows about, and Windows will not let a running image be replaced. Nothing in the build
output names the MCP server as the holder.

**What breaks if you ignore it** — the build error names a file, not a culprit, so it reads as a
stale-artifact problem and invites `clean`, which fails the same way. The subtler trap is the escape
hatch itself: `clio.mcp.e2e` resolves the sibling `clio/bin/<cfg>/<tfm>/clio.exe`, so a run switched
to `-c Release` to dodge the lock is exercising a *different* build than the Debug one you edited. If
the point of the run is to verify your change, verify which configuration the e2e host actually
launched.
