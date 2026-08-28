---
description: AvailableIisPortService returns Status "unavailable" for three different outcomes (no free port, non-Windows host, swallowed scan exception) and deliberately reports only counts, never the bound-port list or the exception text
applies-to:
  - clio/Common/IIS/AvailableIisPortService.cs
  - clio/Common/IIS/FindAvailableIisPortResult.cs
  - clio/Command/McpServer/Tools/FindEmptyIisPortTool.cs
  - clio.tests/Common/IIS/AvailableIisPortServiceTests.cs
date: 2026-08-19
---

**What is true** — `AvailableIisPortService.FindAsync` has exactly two `Status` values,
`"available"` and `"unavailable"`, and three distinct paths end in `"unavailable"`: the host is not
Windows, no free port exists in the requested range, and `catch (Exception)` after any failure of
the IIS binding scan or the TCP reservation read. Only the human-readable `Summary` distinguishes
them. On the two failure paths `IisBoundPortCount` and `ActiveTcpPortCount` are reported as `0`.
`FindAvailableIisPortResult` carries counts only — the list of occupied ports and the swallowed
exception message are intentionally absent from the contract.

**Why it is this way** — the result is served to MCP callers, i.e. to an AI agent and through it
into a transcript. A full bound-port list plus raw exception text describes the host's network
topology and installed sites; the counts are enough for the agent to pick a port and to see that
the range is busy. The blanket `catch` exists so a partially failing scan fails closed on
`"unavailable"` instead of recommending a port it could not prove is free.

**What breaks if you ignore it** — two mistakes are predictable. Reading `ActiveTcpPortCount == 0`
as "nothing is listening" is wrong: it also means "the scan threw". And "improving diagnostics" by
appending the exception message or the occupied-port list to `Summary` silently turns a deliberate
information-hiding boundary into a host-topology leak; nothing in the code comments the `catch`, so
the reviewer has no signal that the omission was a decision rather than an oversight.
