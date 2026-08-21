---
description: a live clio MCP server process holds clio/bin/Debug/<tfm>/clio.exe and its DLLs open, so a Debug build fails with MSB3027 file-in-use - and switching to Release changes which binary clio.mcp.e2e drives
applies-to:
  - clio/clio.csproj
  - clio.mcp.e2e/
date: 2026-08-19
---

**What is true** — when an editor or agent has a clio MCP server running, that process runs from
`clio/bin/Debug/<tfm>/` and keeps `clio.exe` plus its assemblies open. Any `dotnet build` or
`dotnet test` that would rewrite that output fails with `MSB3027` / "being used by another process".
Three ways out, in order of preference: kill the `clio` MCP subprocesses; build and test with
`-c Release`, which writes a different output directory; or, when only test code changed, build with
`-p:BuildProjectReferences=false` and run with `--no-build` so the existing binary is reused.

**Why it is this way** — the MCP server is a long-lived child process of the editor, not something
the build knows about, and Windows will not let a running image be replaced. Nothing in the build
output names the MCP server as the holder.

**What breaks if you ignore it** — the build error names a file, not a culprit, so it reads as a
stale-artifact problem and invites `clean`, which fails the same way. The subtler trap is the escape
hatch itself: `clio.mcp.e2e` resolves the sibling `clio/bin/<cfg>/<tfm>/clio.exe`, so a run switched
to `-c Release` to dodge the lock is exercising a *different* build than the Debug one you edited. If
the point of the run is to verify your change, verify which configuration the e2e host actually
launched.
