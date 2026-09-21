# ENG-99856 — implementation handover

The entry document for the implementation session. Paste it into a fresh session as the first message,
or read it here. Written 2026-09-21, at the close of the research and the BMAD pipeline.

Everything below is either committed on this branch or verifiable in one command. Where a figure is a
measurement, its predicate and date are quoted with it.

---

## Start here, in this order

1. `eng-99856-multi-instance-handover.md` — the research entry brief (checkouts, where the designer lives,
   the working rules).
2. `eng-99856-multi-instance-platform-facts.md` — the platform evidence, `file:line` under every claim.
   **Seven corrections to the ticket's own framing** are in §1; read them before trusting the Jira text.
3. `eng-99856-multi-instance-contract-answers.md` — the six contract questions, reconciled into one contract.
4. `spec/prd/prd-eng-99856-multi-instance.md`, then `spec/adr/adr-eng-99856-multi-instance.md`.
5. `spec/stories/story-eng-99856-multi-instance-{1..14}.md` and
   `spec/test-plans/tp-eng-99856-multi-instance.md`.

Do **not** re-derive platform facts. They went through adversarial refutation (65 of 319 claims corrected),
a completeness critic, a closure pass and a reconciliation. Quote the existing citation. If you must
establish something new, read platform source and cite `file:line` — never `docs/knowledge/`, `spec/`, or
the Jira description as evidence for a platform behaviour.

## Where to start working, today

Two stories depend on none of the seven open owner decisions and can begin immediately:

- **Story 1 — the describe diagnosis.** It is not a feature. Its only production change is a committed
  test; its deliverable is a written gate document. It blocks stories 9, 10 and 12, and until it exists
  AC-15 has no answer and story 10's shape is unchosen (it is `deferred` in the tracker for that reason).
- **Story 2 — the test harness.** `SubProcessTestSupport.AParameterOn` hardcodes
  `DataValueType = "Text"` (`:265`) and `MakeMultiInstance` (`:211-233`) builds both collections *and* all
  three counters through it. Until this is fixed, no result from those fixtures is evidence. Six call
  sites; each test whose result changes must be triaged in writing as *wrong before* or *wrong now*.

Everything else waits on an owner decision or on story 1.

## The seven owner decisions, still open

Written up in the ADR with an advisory recommendation each; the stories were written against those
recommendations so the set is reviewable, **not** so the decisions are closed. Each dependent story names
its OQ and the one-line consequence of the opposite answer.

| | decision | story affected |
|---|---|---|
| OQ-01 | de-conversion (`enabled: false`) in v1 | 8 (deleted if "no") |
| OQ-02 | retarget on a multi-instance element | 7 |
| OQ-03 | the `inSync` redefinition | 9, 12 |
| OQ-04 | output-collection strictness | 6 |
| OQ-05 | does `validate-process-graph` grow a rule | 6, 13 |
| OQ-06 | in-place `executionMode` / `ignoreErrors` update | 4 |
| OQ-07 | collection SHAPE check | 5 |

OQ-01 and OQ-06 are coupled: answering **both** "no" makes `executionMode` unchangeable once set, because
the only remaining route is `removeElement` + `addElement`, which changes the element UId and drops the
element's flows and mappings.

## Five traps that fail SILENTLY

1. **The construction order is forced.** Create all five parameters → add them to `Parameters` → write all
   five UIds into the options → assign `MultiInstanceOptions` → **only then** touch `SchemaUId`. The two
   collections resolve with the throwing `GetByUId` while the three counters self-heal with `FindByUId`, so
   any deviation throws `ItemNotFoundException` **out of a property setter**. Any assignment to `SchemaUId`,
   even of the same value, re-enters the rebuild.
2. **An empty `"BP6": {}` round-trips as multi-instance ENABLED** with five empty UIds — the writer
   suppresses every field at its default and the reader constructs the options object before reading into
   it. Such an element is permanently unloadable at both design time and run time, and no platform
   validation rule exists to diagnose it. Never emit one.
3. **The caller stamp is a RUNTIME requirement, not bookkeeping.** `UseOnlyModifiedParameters` defaults
   true and the provider filters the substituted item properties by
   `SourceValue.ModifiedInSchemaUId == caller schema UId`. Get it wrong and the sub-process receives no
   values at all while the element saves, describes and renders perfectly. Reuse
   `ProcessMappingService.BuildSourceValue`, which already stamps it.
4. **`AsksForNothing` swallows the new member.** Until it gains `&& config.MultiInstanceOptions == null`, a
   block carrying only `multiInstanceOptions` returns `MultiInstanceSkipped` — success, nothing done.
5. **A write into the OUTPUT collection is erased.** Those item properties are XOR-derived twins whose
   value `FillCollectionParameters` wipes on every synchronization and `LoadCollectionParameters` never
   reads back. The existing direction guard ACCEPTS them (133 of 170 are `Variable`), which is why the new
   refusal exists.

## Environment, as left on 2026-09-21

- **Branch** `feature/ENG-99856-multi-instance`, six commits, level with `origin/master`, clean tree,
  **not pushed**, no PR.
- **Worktree** `C:\Users\d.krestov\AppData\Local\Temp\claude\wt-eng92707`. The name is the PARENT ticket's —
  it predates this work. Either keep it or make a fresh one; do not be confused by the name.
- **Stand `Creatio`** (`http://d_krestov_n.tscrm.com:40001`) was raised from CrtProcessBuilder **1.6.3.14 to
  1.6.3.31** for the describe measurement. `CrtBase 7.8.0` is installed, so it carries the shipped
  multi-instance fixture `ExpireLicenseNotificationProcess` → element `SubProcess2`. Most other registered
  stands are expired (404).
- `C:\Projects\clio\clio\bin\Release\net10.0\clio.dll` was rebuilt from the *feature* branch by accident;
  it is build output, not sources.

## Operational rules that cost time to rediscover

- **Build and test CrtProcessBuilder in the MAIN checkout, not a git worktree.** The untracked core-bin tree
  is absent there and the workaround yields ~1355 spurious assembly-resolution failures that read as a code
  regression.
- **Run schema-write operations against a stand SEQUENTIALLY.** A parallel burst trips IIS rapid-fail and
  takes down a .NET Framework stand's app pool.
- **`describe-business-process` has no CLI verb** — it is MCP-only. Drive `dotnet clio.dll mcp-server` over
  stdio JSON-RPC and nest the arguments under an `args` key. Working probe scripts were used for the
  measurements in the facts document; re-creating one is ~40 lines of Python.
- **The package test-category vocabulary is `Category = "UnitTests"`, not `Category("Unit")`** — 92 fixtures
  to zero. The test plan carries both deliberately (NUnit categories are additive); do not "fix" it either
  way without reading §2 of the plan.
- **Process-designer E2E is excluded at the RUNNER level**, not merely non-blocking:
  `clio.mcp.e2e/TestSelection/mcp-e2e-selection.json` sets
  `"baseFilter": "TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual"`, which is the TeamCity
  default. An assertion living only there is unenforced. Mirror every load-bearing one at unit level.
- **All seven `subprocess-*` knowledge records** list the bundled archive in `applies-to`, and the rebundle
  changes it — so `make check-knowledge` will report all seven, not the two the ADR names.

## The one thing most likely to be re-derived wrongly

Reading `ProcessDescriber.ReadElementParameters` (it short-circuits the provenance filter for a sub-process
element) together with `ProcessParameterService.ToDescribeParameter` (it recurses `ItemProperties`) makes it
look as though `describe-business-process` already reports the callee's contract for a multi-instance
element.

**It does not.** Measured 2026-09-21 on the stand above, CrtProcessBuilder 1.6.3.31, clio built from
`origin/master` `01c8b677a`: no `itemProperties` on any parameter of any element or of the process, although
the stored metadata carries `L18` with 4, 6 and 4 entries and the projection code is present in the deployed
archive. The ADR then refuted the load-path explanation as well — both `LoadForDescribe` branches funnel
into one factory and the content fork sits below them, in `CreateSchemaInstance` on
`UseInstanceFromMetaData`, with no package-reachable lever to force a metadata instance. Three of four
candidate causes are eliminated in source. **The survivor is environmental**, and story 1 is the diagnosis
that names which shape it takes.
