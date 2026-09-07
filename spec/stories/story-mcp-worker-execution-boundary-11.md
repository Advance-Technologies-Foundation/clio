# Story 11: Transport failures must not surface as domain answers (PageSchemaMetadataHelper)

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: folded-in
**Status**: ready-for-dev
**Size**: S

## As a
caller who got `"Failed to query SysPackage"`

## I want
to be told that the request was rejected, timed out, or returned a login page

## So that
I stop debugging my data when the actual problem is my session

## Design
- `PageSchemaMetadataHelper.ExecuteSelectQuery` (`clio/Command/PageSchemaMetadataHelper.cs:33-46`) ends in a bare `catch { return (new JArray(), false); }`. An HTML login page, a timeout and a 500 all collapse into one domain-shaped failure — while the identical command through the CLI returns `success:true` in ~1 s.
- This is a **second, unguarded copy** of the SelectQuery plumbing sitting next to the one ENG-93365 already fixed. Route it through `ServiceResponseJsonGuard` and drop the bare `catch`.
- Independent of every stage: it is a defect today and stays a defect after the worker boundary lands.

## Acceptance Criteria
- [ ] AC-01 — An HTML login page produces an auth/transport error naming the cause, never `"Failed to query SysPackage"` (TC-U-F01).
- [ ] AC-02 — A 500 and a timeout each produce distinct, caller-actionable errors (TC-U-F01).
- [ ] AC-03 — The bare `catch` is gone; an unexpected exception is not converted into a domain answer (TC-U-F02).
- [ ] AC-04 — A genuine empty result is still an empty result — not an error.

## Tests
`clio.tests` — `[Category("Unit")]`, `Module=Command`. TC-U-F01, TC-U-F02.

## Notes
Feature DoD item: "a transport or auth failure never surfaces as a domain answer."
