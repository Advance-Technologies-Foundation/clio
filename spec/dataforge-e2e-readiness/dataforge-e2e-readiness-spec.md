# DataForge E2E readiness — stand-gated arrange spec (ENG-92147)

## Status

Stand-gated. Not implementable from a developer workstation. This document is the
deliverable for the environment/arrange root cause of the 8 failing DataForge MCP E2E
fixtures; whoever runs the E2E suite against a real stand executes the investigation and
the chosen remedy below.

## Problem

On the E2E job, all 8 DataForge fixtures fail with:

```
Expected response.Success to be True ... but found False
```

Affected fixtures (`clio.mcp.e2e/DataForgeToolE2ETests.cs`):

- `DataForgeStatus_Should_Return_Structured_Status_Response`
- `DataForgeStatus_Should_Ignore_Poisoned_Proxy_Environment_Variables`
- `DataForgeFindTables_Should_Return_Table_Matches`
- `DataForgeFindLookups_Should_Return_Structured_Response`
- `DataForgeGetRelations_Should_Return_Relation_Paths`
- `DataForgeGetTableColumns_Should_Return_Contact_Columns`
- `DataForgeContext_Should_Return_TableColumn_Coverage`
- `DataForgeInitialize_Should_Return_Scheduled_Response` / `DataForgeUpdate_Should_Return_Scheduled_Response`
  (destructive opt-in)

The harness-side MCP envelope handling was already fixed (ENG-91827). The CLI-side
structured-error contract is verified by unit tests (ENG-92147 — see
`clio.tests/Command/McpServer/DataForgeToolTests.cs`): a `Success=false` payload from the
DataForge service is surfaced as a clean structured `Success=false` MCP response, never a
protocol error. Therefore the remaining failure is **not** clio code — the DataForge
service is returning `Success=false` (or is unreachable) for every op on the
freshly-deployed Studio + PostgreSQL stand. The most likely roots:

1. `CrtDataForge` is not present on the platform build under test (the proxy endpoints
   `/rest/DataForgeSchemaReadService/*` and `/rest/DataForgeMaintenanceService/*` return an
   HTML 404 page rather than a JSON envelope), or
2. `CrtDataForge` is present but its index/data structures and lookups have never been
   initialized on the fresh instance, so reads return `Success=false` /
   readiness != 200.

## Pinned target

- Platform build: **10.0.0.740** (PostgreSQL).
- Studio configuration with `CrtDataForge` expected to be bundled in supported 10.0.0+
  builds (per the tool's `PlatformRequirementDescription`).
- A registered, reachable clio environment named per `McpE2E:Sandbox:EnvironmentName`.

## Step 1 — Determine on the stand whether CrtDataForge is present and reachable

Run against the pinned stand (replace `<env>` with the configured sandbox environment):

```bash
# A. Is the package installed?
dotnet run --project clio/clio.csproj --framework net10.0 -- list-packages -e <env> | grep -i CrtDataForge

# B. Does the maintenance status endpoint return JSON (service present) or HTML (absent)?
#    Use the dataforge-status MCP tool through clio-run, or call the proxy route directly
#    via clio's get-info/raw request path. A JSON GetServiceStatusResult => present;
#    an HTML <!DOCTYPE ...> body => CrtDataForge proxy route does not exist.
dotnet run --project clio/clio.csproj --framework net10.0 -- get-info -e <env>
```

Decision branch based on Step 1:

- **Present and reachable (JSON envelope returned), but not ready** → go to Step 2A.
- **Absent (HTML 404 / package missing)** → go to Step 2B.

## Step 2A — CrtDataForge present: add deterministic initialize + readiness poll to E2E arrange

Make the read fixtures deterministic by initializing the index in arrange and polling
readiness before the read assertions run. This is an **E2E arrange** change in
`clio.mcp.e2e` (no production-code change).

1. In `DataForgeToolE2ETests.ArrangeAsync` (or a shared one-time fixture setup), after the
   environment is confirmed reachable, invoke the existing `dataforge-initialize` MCP tool
   once against the sandbox environment. It already returns `Scheduled` (see the landmine
   note below) — treat `Scheduled` as "accepted", not "ready".
2. Poll `dataforge-status` until `response.Status.Status == "Ready"` **and**
   `response.Health.DataStructureReadiness` and `response.Health.LookupsReadiness` are
   true, or until a bounded timeout (recommend 5 min, polling every 10–15 s). The poll
   reuses the existing tool path — no new clio surface.
3. Gate this arrange behind a setting (e.g. `McpE2E:DataForge:InitializeAndWait=true`) so
   non-DataForge E2E runs are unaffected and the destructive initialize is opt-in (mirror
   the existing `AllowDestructiveMcpTests` gate used by the initialize/update fixtures).
4. Only after readiness is confirmed do the read fixtures (find-tables, find-lookups,
   get-relations, get-table-columns, context, status) assert `Success=true`.

Expected outcome: the 6 read fixtures + the 2 maintenance fixtures go green because the
service is now actually initialized and ready on the fresh stand.

## Step 2B — CrtDataForge absent: add a service-presence skip guard

If `CrtDataForge` is not part of the platform build under test, the 8 fixtures must
**skip** (not fail), consistent with the existing reachability guard
(`ResolveReachableEnvironmentAsync` → `Assert.Ignore`).

1. Add a one-time presence probe in arrange that calls `dataforge-status` and inspects the
   response: if the maintenance status comes back `Unavailable` (the HTML-404 / not-present
   mapping already implemented in `DataForgeMaintenanceClient.GetFullStatus`, see
   `GetFullStatus_Should_Return_Unavailable_When_Proxy_Returns_Html`), call
   `Assert.Ignore("CrtDataForge is not installed on '<env>'; DataForge E2E skipped.")`.
2. This keeps the DataForge E2E suite honest: green where the service exists, skipped where
   it does not, never a false-red on a build that legitimately ships without DataForge.

## Landmine — do NOT change `DataForgeMaintenanceClient.Initialize()` `Scheduled` contract here

`DataForgeMaintenanceClient.Initialize()` (and `Update()`) unconditionally returns
`new DataForgeMaintenanceStatusResult(true, "Scheduled", null)` after the proxy POST with
no poll/verify. Two E2E fixtures hard-assert exactly `response.Status.Status == "Scheduled"`
(`DataForgeInitialize_Should_Return_Scheduled_Response`,
`DataForgeUpdate_Should_Return_Scheduled_Response`), and one unit test
(`Initialize_Should_Use_Rest_Route`) asserts `"Scheduled"`.

Changing `Initialize()` to surface the real post-init status would break those fixtures.
Because those E2E fixtures are sandbox-gated and do not run on a developer workstation, the
break would not be visible locally. Therefore **the readiness wait belongs in E2E arrange
(Step 2A), polling `dataforge-status` — not inside `Initialize()`**. If a future change
genuinely needs `Initialize()` to verify rather than fire-and-forget, that is a separate
ADR/decision and must update all three of the above tests in the same change.

## Acceptance for whoever runs this against a stand

- Step 1 conclusively records, in the run notes, whether CrtDataForge is present + reachable
  on the pinned 10.0.0.740 PostgreSQL stand.
- If present: Step 2A arrange change lands in `clio.mcp.e2e`, gated by a setting, and the 8
  fixtures pass on the stand.
- If absent: Step 2B skip guard lands, and the 8 fixtures skip (not fail) on builds without
  DataForge.
- `DataForgeMaintenanceClient.Initialize()`'s `Scheduled` contract is left intact unless a
  separate decision says otherwise.

## Scope boundary

This spec covers only the environment/arrange root that makes the 8 DataForge E2E fixtures
deterministic. The clio-side structured-error contract for the read ops is verified by
unit tests under ENG-92147 and is out of scope for this stand-gated work.
