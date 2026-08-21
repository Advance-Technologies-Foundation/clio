---
description: DataForgeMaintenanceClient.Initialize/Update return a hardcoded "Scheduled" status right after a fire-and-forget POST - success does not mean the data structures are ready, and the literal is pinned by tests
applies-to:
  - clio/Common/DataForge/DataForgeMaintenanceClient.cs
  - clio/Command/McpServer/Tools/DataForgeTool.cs
  - clio.mcp.e2e/DataForgeToolE2ETests.cs
ticket: ENG-92147
date: 2026-08-19
---

**What is true** — `Initialize()` and `Update()` post to
`InitializeDataStructuresAndLookups` / `UpdateDataStructuresAndLookups` and then return
`new DataForgeMaintenanceStatusResult(true, "Scheduled", null)`. The status is a constant, not a
reading: nothing polls, and the POST answer is discarded. Readiness is only observable through the
status/health path, where `DataForgeHealthResult` is derived from the actual liveness and readiness
payload.

**Why it is this way** — the platform endpoints are asynchronous; they accept the request and do the
work later, so there is nothing truthful to report at return time. The literal is also part of the
contract now: `DataForgeToolE2ETests.DataForgeInitialize_Should_Return_Scheduled_Response`,
`...DataForgeUpdate_Should_Return_Scheduled_Response` and
`DataForgeMaintenanceClientTests.Initialize_Should_Use_Rest_Route` assert it.

**What breaks if you ignore it** — a caller that reads `Success = true, "Scheduled"` as "the tables
exist now" then queries them and gets nothing, with no error anywhere to explain it. Any wait must be
a poll of the status endpoint in the caller (or in the test's arrange step), never a sleep after
initialize. And do not "fix" `Initialize()` to return a real status: the three tests above pin the
literal, so the change turns into an unexplained red suite.
