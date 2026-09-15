---
description: the harness ShutdownTimeout is burned in full on every MCP session disposal because the SDK kills the child without closing stdin, and the suite opens 274 sessions per run
applies-to:
  - clio.mcp.e2e/Support/Mcp/McpServerSession.cs
  - clio.mcp.e2e/Support/Diagnostics/E2ETimingProbe.cs
  - clio.mcp.e2e/Support/Configuration/ReachableSandboxEnvironment.cs
  - clio.mcp.e2e/Support/Configuration/ClioCliCommandRunner.cs
ticket: none
date: 2026-09-11
---

**What is true** — `StdioClientTransportOptions.ShutdownTimeout` is not a ceiling, it is a bill. The
SDK's `StdioClientTransport.DisposeProcess` (ModelContextProtocol 2.2.0) never closes the child's
standard input; it goes straight to `KillTree(ShutdownTimeout)`. Measured with a stopwatch around
`McpServerSession.DisposeAsync`: **2035 ms at a 2 s setting, 529 ms at 500 ms, 280 ms at 250 ms**.
clio's own EOF shutdown is healthy and is NOT the cost — closing stdin by hand over a FIFO makes the
server exit in **0.27 s**.

The multiplier is large and was invisible before `E2ETimingProbe` existed: a full CI run opens
**274 MCP server sessions** and made **184 `ping-app` reachability probes** (1.16 s each) before
those were collapsed into one probe per run. TeamCity bills only test bodies, so none of this
appeared in `AllTestsDuration` or in any per-test duration.

**Why it is this way** — the suite must start the server under test as a real external process
(`clio.mcp.e2e/AGENTS.md`, "Decisions"), so every fixture that needs an isolated server pays a full
process lifecycle. The 2 s value was itself a fix for a 10 s one and was recorded as "resolved",
which hid the fact that the window is spent unconditionally rather than only on a stuck child.

**What breaks if you ignore it** — raising the window "to be safe" costs `274 x delta` per run with
no safety gained, because the time is spent whether or not the child would have exited. A direct
A/B on TeamCity, both builds started within six seconds of each other on different agents:
master **56.3 min** vs branch **50.4 min** (test step 3004 s vs 2638 s). Lowering it does not orphan
processes — the kill still happens; the window only bounds `KillTree` itself.

The counters are permanent: `e2eMcpSessionStarts`, `e2eMcpSessionDisposeMs`, `e2eCliInvocations`,
`e2eFixedCostMs` are published as TeamCity build statistics and written to `e2e-timing.txt` beside
the test assembly. Read them before claiming any e2e timing change; the fixed arrange cost they
report was **17.2 min out of a 45 min test step** before these two changes.
