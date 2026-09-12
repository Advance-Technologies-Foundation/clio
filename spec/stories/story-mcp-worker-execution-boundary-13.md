# Story 13: `get-page` exits 0 when it fails

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: folded-in (found while running TC-E-901 on a live stand)
**Status**: ready-for-dev
**Size**: S

## As a
script, CI step or e2e arrange step that checks an exit code

## I want
`get-page` to exit non-zero when it did not get the page

## So that
"the command succeeded" and "the page is on disk" stop being different facts

## Design
- Measured on a live stand: `clio get-page --schema-name <missing>` prints
  `{"success":false,"page":null,"error":"Schema 'X' not found"}` and exits **0**.
- This was found because TC-E-901's arrange step asserts `read.ExitCode.Should().Be(0)`. On a stand without
  the seeded fixture that assertion PASSED while nothing was written, and the test failed later at the
  `meta.json` existence check with a message that pointed at the wrong thing.
- The MCP surface is unaffected — a tool result carries `success:false` and callers read it. This is the CLI
  exit-code contract only.
- Check the sibling read commands for the same shape before fixing just this one: a failed read that exits 0
  is a class, not an instance. `list-pages`, `get-schema`, `get-client-unit-schema` and
  `get-classic-page-sources` are the obvious candidates.
- Careful with the blast radius: anything that currently treats exit 0 as "carry on" would start failing —
  which is the point, but it means the change wants a scan of callers, including `clio.mcp.e2e` arrange
  steps and any toolkit script.

## Acceptance Criteria
- [ ] AC-01 — `get-page` on a missing schema exits non-zero and still prints the same structured envelope.
- [ ] AC-02 — `get-page` on a real page still exits 0 and writes `body.js`, `bundle.json` and `meta.json`.
- [ ] AC-03 — The sibling read commands are audited; each is either fixed the same way or explicitly recorded
      as deliberately exit-0, with the reason.
- [ ] AC-04 — TC-E-901's arrange step keeps its exit-code assertion and it now means something.

## Tests
`clio.tests` — `[Category("Unit")]`, `Module=Command`: missing schema exits non-zero; successful read exits 0.
Plus the existing TC-E-901 arrange step as the live regression.
