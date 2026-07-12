# Story 2: OData $metadata Probe Across Environment Matrix (Phase A)

**Feature**: read-column-default-values
**FR coverage**: FR-02
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: ENG-91318 (epic ENG-85256)
**Status**: ready-for-dev
**Size**: M (half day)
**Phase**: A — investigation (documents only; SM-01 counter = **empty code diff**)
**Depends on**: none (gate: access to real Creatio environments per the matrix)

---

## As a

developer (investigator on ENG-91318)

## I want

captured evidence from real Creatio instances of whether OData v4 `$metadata` exposes column default values — for both a lookup-`Const` column and a primitive-default column, on both environment-matrix rows

## So that

the FR-04 keep/adopt/hybrid decision is based on captured CSDL fragments, not speculation about what "the OData approach" can do

---

## Acceptance Criteria

- [ ] **AC-01 (PRD AC-02)** — Given a real Creatio instance with a configured
  **lookup-column `Const` default** and at least one configured **primitive-column
  default** (e.g. Boolean or Text), when its OData `$metadata` is fetched via the
  FR-02 probe mechanism, then
  `spec/read-column-default-values/read-column-default-values-odata-probe.md`
  contains the captured CSDL fragments for **both** columns, stating explicitly
  whether/where default values appear (e.g. CSDL `DefaultValue` attribute /
  annotations) and from which platform version.
- [ ] **AC-02** — Given the environment matrix, when captures are recorded, then
  both rows are covered: one .NET Framework instance (`0/odata/$metadata`) and one
  .NET Core instance (`odata/$metadata`), each on a currently supported 8.x version
  (PRD sufficiency rule).
- [ ] **AC-03** — Given the probe vehicle, when each capture is recorded, then the
  exact `call-service` command line used is recorded next to its output file, and
  **no raw `HttpClient` script** was used anywhere (hard rule:
  `IApplicationClient` only).
- [ ] **AC-ERR (PRD AC-ERR)** — Given an environment where `$metadata` is
  unavailable (older Creatio / OData disabled), when the probe runs, then the
  limitation is recorded as a version-coverage row for the FR-04 comparison matrix
  rather than failing the investigation.
- [ ] **AC-04 (SM-01 counter)** — Given `git diff` for this story's PR, when
  inspected, then it contains **only** files under `spec/` — zero production-code
  changes (in particular: `ODataReadTool` is NOT extended to fetch `$metadata` —
  rejected alternative B in the ADR).

## Implementation Notes

Documents-only story. Deliverable:
`spec/read-column-default-values/read-column-default-values-odata-probe.md`

Probe vehicle (ADR-chosen alternative C — zero code diff):

```bash
clio call-service --service-path "odata/\$metadata" -m GET -d <output-file> -e <env>
```

- GET goes through `IApplicationClient.ExecuteGetRequest`
  (`clio/Query/DataServiceQuery.cs:162-167`).
- `ServiceUrlBuilder.Build(ServicePath)` auto-prepends `0/` for .NET Framework
  environments — the same command serves both matrix rows.
- Output beautifier targets JSON and CSDL is XML — cosmetic only; `-d` saves the
  raw response.

What also belongs in the doc (FR-02): OQ-01 outreach status (what other teams
actually read — `$metadata` `DefaultValue`, `SysSchema` rows via OData, or
post-insert observation); assessment of whether existing `odata-read` could serve
the need without new code. OQ-01 runs in parallel from day one; its fallback is
applied in story 4, not here.

The ADR's "honest prediction" (CSDL `DefaultValue` is primitive-only; `$metadata`
structurally cannot carry a referenced record's display value) is to be **validated,
not assumed** — capture the fragments either way.

Key file: `spec/read-column-default-values/read-column-default-values-odata-probe.md` (new)
Pattern to follow: ADR Implementation Plan, story A2 row

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| None | Documents-only story — no production code, no tests. Verification = captured CSDL fragments in the evidence doc + empty code diff check | — |

## Definition of Done

- [ ] `read-column-default-values-odata-probe.md` exists with CSDL fragments for lookup-`Const` AND primitive-default columns (or AC-ERR rows where unavailable)
- [ ] Both environment-matrix rows covered (.NET Framework + .NET Core, supported 8.x)
- [ ] Every capture annotated with the exact `call-service` command used; no raw `HttpClient`
- [ ] `git diff` contains only `spec/**` files (SM-01 Phase A counter)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing: n/a (documents-only)
- Notes:
