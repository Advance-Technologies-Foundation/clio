# Story 11: Stand verification — does the designer open it, does it iterate, and can a fixture exist at all

**Feature**: eng-99856-multi-instance
**FR coverage**: none directly — this story discharges PRD assumptions **A-02, A-03, A-04, A-05**, which
are the ones no unit test can reach
**AC coverage**: SM-01's manual half ("the process designer opens and renders it, recorded"), and the
precondition for AC-17's E2E half
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md) — Assumptions Index
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — *Test strategy*, last row
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §4, §6, §10
**Jira**: ENG-99856
**Status**: review
**Size**: M (half day — four questions, all sequential, on one stand)
**Repo**: none (a recorded run) + `spec/eng-99856-multi-instance/`
**Depends on**: stories 3, 4, 6 (the write path must be able to build the element), plus a dev cut of the
package pushed to a stand with `push-pkg`
**Blocks**: story 13 (the E2E fixture cannot be designed before A-05 is answered)

---

## As a

QA engineer accepting this feature

## I want

the four questions that only a running stand can answer, answered once and written down

## So that

the feature does not ship on unit evidence alone, and an E2E fixture is designed against what is possible
rather than what was hoped

---

## Acceptance Criteria

- [x] **AC-01** (A-02) — Given an element **the applier built**, when it is opened in the process
  designer, then it opens and renders. This is not cosmetic: the client resolves all five multi-instance
  parameters with the throwing `Terrasoft.Collection#get` while loading localizable values, so a
  construction defect surfaces as a **designer exception**, not a validation message. Record a screenshot
  or the exception text.
- [x] **AC-02** (A-03) — Given a converted element with a bound input collection of N items, when the
  process runs, then the output collection fills **one item per completed iteration** and the three
  counters land. Record N, the observed counts, and the `SysProcessLog` / `SysProcessElementLog` rows
  that evidence it.
- [x] **AC-03** (A-03, the counter-trap) — Given the run, when `CompletedIterationsCount` is read, then
  the reading is taken **after** completion, and the record notes why a mid-run reading is meaningless:
  `FlowJoinIteratorGateway` uses it as the parallel barrier's arrival counter and the End token then
  **overwrites** it with `TotalIterationsCount − FailedIterationsCount` before persisting
  (`ProcessInstanceCollectionParametersDataWriter.ActualizeResultParameters`).
- [x] **AC-04** (A-04) — Given `executionMode: "parallel"` **together with** `useBackgroundMode: true` —
  39 of the 61 shipped multi-instance elements are in that state, so it is the **majority** shape — when
  the process runs, then the behaviour is recorded: whether iterations genuinely overlap and whether
  anything reorders. Parallel alone does not mean concurrent threads (`FlowVisitor` drives a
  single-threaded FIFO queue); concurrency comes from `FlowBackgroundToken`, inserted only when
  `UseBackgroundMode` is true.
- [x] **AC-05** (A-05) — Given the target stand, when an attempt is made to build a multi-instance element
  **by hand in the designer**, then the answer is recorded with the blocking reason if it fails. Two gates
  apply (`process-subprocess-schema.js:201`): the feature `UseMultiInstanceSubProcess`
  (`868e51db-4582-40c1-9605-7b70d9b38e8b`, shipped enabled but **per role** — check the role of the user
  you are logged in as) and `!parentSchema.useForceCompile` — an **embedded** process schema hard-codes
  `useForceCompile: true` (`embedded-process-schema.js:57`), so a sub-process element inside one can never
  be multi-instance.
- [x] **AC-06** — Given all four answers, when they are written to
  `spec/eng-99856-multi-instance/eng-99856-multi-instance-stand-verification-<date>.md`, then each carries
  the environment name, the package version, the clio commit, the timestamp and the exact process/element
  names, and each assumption is marked **confirmed**, **refuted** or **not reached**.
- [x] **AC-07** — Given any refuted assumption, when it is recorded, then the consequence named in the
  PRD's assumption index is restated and routed: A-02 → a construction defect to fix in story 3; A-03 →
  the contract is writable but the feature does nothing useful at run time; A-04 → the majority shape is
  the untested one; A-05 → no E2E happy path, and story 13 says so explicitly instead of quietly shipping
  a weaker fixture.
- [x] **AC-ERR** — Given a stand failure, when one occurs, then the run **stops** and the state is
  recorded. Do not retry a schema write in parallel to catch up: a parallel burst trips IIS rapid-fail and
  downs a .NET Framework stand's app pool.

## Implementation Notes

**Sequential, always.** Every schema write against the stand goes one at a time. This is the single
operational rule that has cost this project the most time when ignored.

Deploy the dev cut with `push-pkg` (**not** `push-package`, which does not exist):

```
dotnet run --project clio/clio.csproj --framework net8.0 -- push-pkg <path>/CrtProcessBuilder.gz -e <env>
dotnet run --project clio/clio.csproj --framework net8.0 -- list-packages -e <env>   # verify the version
```

Note that an `install-process-builder` run resolves the bundled archive from the **build output**
directory, so a `clio compress -d <repo path>` has no effect until clio is rebuilt — which is why this
story uses `push-pkg` of an explicit archive and records the version it verified.

Do **not** clean up the stand residue the parent (ENG-92707) left behind: it is explicitly out of scope,
and removing it would make this feature's manual baseline unreproducible.

For AC-02, the runtime evidence recipe that works CLI-only: start the process, then read
`SysProcessLog` for the root row and `SysProcessElementLog` for the per-element rows — the root row alone
cannot tell you which branch or how many iterations ran, and a root row with no `CompleteDate` is a
**parked** process, not a hung one.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Manual (stand, sequential) | A-02 designer renders; A-03 iteration + counters; A-04 parallel + background; A-05 hand-built fixture feasibility | `spec/eng-99856-multi-instance/eng-99856-multi-instance-stand-verification-<date>.md` |
| Unit | none — by construction; everything here is what unit tests cannot reach | — |

## Definition of Done

- [x] All four assumptions marked confirmed / refuted / not reached, each with its evidence
- [x] Every refuted assumption routed to the story that must absorb it
- [x] Environment, package version, clio commit and timestamps recorded
- [x] Every stand write was sequential; the record says so
- [x] No stand cleanup performed (explicitly out of scope)
- [x] The A-05 answer is stated plainly enough for story 13 to design its fixture from it
- [x] PR description (the spec commit) references this story file

## Dev Agent Record

- Implementation started: 2026-09-22
- Implementation completed: 2026-09-22
- A-02 / A-03 / A-04 / A-05 verdicts: **confirmed / confirmed / confirmed (with a finding) / not reached**
- Record: [eng-99856-multi-instance-stand-verification-2026-09-22.md](../eng-99856-multi-instance/eng-99856-multi-instance-stand-verification-2026-09-22.md)
- Notes:
  - **A-02** — the designer's own properties panel renders the applier-built element in full
    ("Sub-process (Call activity)", Execution mode, the input collection and its item properties), and
    it displays the dotted per-item source in the DESIGNER'S notation:
    `[#Read three contacts.Collection of records:Full name#]`. No console exception through load or
    through the panel opening.
  - **A-03** — one caller run produced three callee runs and three `ProcessSchemaSubProcess` element-log
    rows under one `FlowIteratorGateway`. The counters read `completed=3 total=3 terminated=0`, taken
    after the element completed, by branching on them (the only readable route — see below). The output
    collection was measured by feeding it into a SECOND multi-instance element, which iterated three
    times; a probe callee branching on the value it was handed reported `PROBE the echo landed` 3/3, so
    both dotted per-item paths bound at RUN TIME. That is the `ContainerUId` backfill validated live.
  - **A-04** — `Parallel` + `useBackgroundMode: true` (the majority shape) runs and preserves input
    order, but the iterations DO NOT OVERLAP on this stand: they are spaced ~300 ms apart through the
    background job queue, and the element took 1282 ms against 105 ms for Sequential.
  - **A-05** — both gates are open (`UseMultiInstanceSubProcess` evaluates true in the running designer;
    `UseForceCompile`/`BK31` is absent from our processes and from the shipped one alike), and the
    conversion route is identified in platform source: there is NO dedicated control, the conversion is a
    side effect of `collectionMappingSet` (`ProcessFlowElementPropertiesPage.js`). The gesture itself was
    not completed — the Select parameter dialog reported "There are no parameters of required type" for a
    Text target although the collection does carry item properties. Recorded, not explained.
  - **Two by-products.** (1) The designer sets `useBackgroundMode = true` on EVERY conversion and back to
    false on de-conversion; clio's converted element carries `false`. Given the owner's "как в дизайнере"
    ruling this is a decision to take, not a defect — matching the designer costs an order of magnitude
    in wall clock. (2) A multi-instance input collection binds from a Read data element's
    `ResultCompositeObjectList`, never `ResultEntityCollection`; only the former shares the
    `651ec16f-...` type.
  - **Why the counters are read by branching.** A completed element's `SysProcessElementData` row is
    gone, and a parked process's `SysProcessData` blob carries structure only — a `Variable` process
    parameter written by a formula task does not appear in it. There is no SQL route to a live process's
    parameter values; the element log is the route.
