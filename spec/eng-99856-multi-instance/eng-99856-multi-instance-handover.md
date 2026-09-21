# ENG-99856 — research handover

The entry document for this feature folder, and a self-contained brief: paste it into a fresh session as
the first message, or read it here. Nothing in it depends on the session that produced it.

Written 2026-09-21, at the close of ENG-92707. Everything below was verified against platform or package
source at that date; where a figure is a measurement, its predicate and date are quoted with it.

---

## The task

Research phase for **ENG-99856** — *Sub-process element: support MULTI-INSTANCE (running the callee
once per item of a collection)*. Sub-task of ENG-92707, which shipped the Sub-process element WITHOUT
multi-instance support, by an explicit decision.

Read the issue first: https://creatio.atlassian.net/browse/ENG-99856 — its description already carries
the platform facts established while building the parent, including the metadata layout and one
implementation trap. Do not re-derive what is written there; verify it if you are about to depend on it.

**Do not start coding.** Per `AGENTS.md`, a non-trivial feature needs a PRD and an ADR in `spec/prd/`
and `spec/adr/` before implementation. Six contract questions are open (listed in the issue). Run
`/bmad` or produce the research doc first.

## Where the facts already are — read before searching

| What | Where |
|---|---|
| The brief, the metadata layout, the trap | ENG-99856 description |
| The rebuild, measured and corrected | `docs/knowledge/platform/subprocess-sync-flattens-a-multi-instance-element.md` (clio, master) |
| Five more sub-process records | `docs/knowledge/platform/subprocess-*.md` (clio, master) |
| Traps, deferred questions, the serialization capture | `spec/eng-92707-sub-process-element/` (clio, master) |
| The 61/416 corpus scan | `docs/sub-process-element-capture.md` (CrtProcessBuilder) |

## The local checkouts, and what each one actually is

Surveyed 2026-09-21. Only three of these are git repositories; the rest are checked-out trees or a
deployed instance, so "the branch" is a meaningful question for three of them and not for the others.

| Path | What it is | Git |
|---|---|---|
| `C:\Projects\Creatio2` | **Creatio core sources** — the authority for server and core-client behaviour. Everything under `TSBpm/Src/Lib`. | `tscore-git.creatio.com/creatio/core.git`, branch `trunk` |
| `C:\Projects\PackageStore` | **Product packages**, checked out. The process designer's own client schemas live here. Also the corpus every "N of M shipped elements" figure was scanned over. | not a repo |
| `C:\Projects\workspace\ProcessBuilder` | **CrtProcessBuilder** — the package this work changes. | `creatio.ghe.com/engineering/crt-process-builder`, working tree on a chore branch — read `origin/main` |
| `C:\Projects\clio` | **clio** — the CLI and MCP surface. | `github.com/Advance-Technologies-Foundation/clio`, working tree on an UNRELATED feature branch — read `origin/master` |
| `C:\Projects\clio-knowledge` | The **shipped guidance library** an agent reads through `get-guidance`. Not internal notes. | `github.com/.../clio-knowledge` |
| `C:\Projects\WorkPackageStore` | Working/custom packages (CreatioStateMachine, CrtGenAICopilot and others). Not product. | not a repo |
| `C:\Projects\Creatio` | A **deployed instance** — `Terrasoft.WebApp`, `Web.config`, `bin`. Build output, not sources. | not a repo |
| `C:\Projects\MyWorkspace` | A clio workspace (packages, projects, tasks). | not a repo |

## Where the DESIGNER is implemented

This matters for multi-instance specifically, because conversion is a client behaviour: the only place
the platform assigns `MultiInstanceOptions` server-side is the metadata reader. Whatever converts an
element does it in the browser and sends the result down.

The designer's code is split across two trees, and you will need both.

**1. The designer package** — `C:\Projects\PackageStore\CrtProcessDesigner\branches\7.8.0\Schemas\`
The property pages and view configs. The ones read during ENG-92707:
`SubProcessPropertiesPage`, `ProcessFlowElementPropertiesPage`, `RootUserTaskPropertiesPage`,
`ProcessSchemaParameterViewConfig` (the parameter ROW — four cells, no code column),
`ProcessSchemaParameterEditPage`, `ProcessSchemaParameterEditModule`, `MappingEditMixin`.
Note `EventSubProcessPropertiesPage` exists separately — the EVENT sub-process is a different element
that shares the platform class.

**2. The core client managers** —
`C:\Projects\Creatio2\TSBpm\Src\Lib\Terrasoft.Nui\Resources\Terrasoft\manager\`
The client-side schema model the pages operate on. Under
`process-flow-element-schema-manager/`: `parametrized-process-schema-element.js` (this is where
`clearParameters` removes mapping rows and `_prepareClonedParameter` mints a fresh UId),
`process-activity-schema.js` (`findParameterByNameOrByUId` — name first, and the UId branch cannot
match), `base-process-schema-element.js`. Under `base-schema-manager/`: `base-schema.js`
(`getDisplayValue` = caption-or-name). Under `process-schema-manager/`: `process-schema.js` (what
serialization actually writes).

**A trap in these paths.** The same client files also exist as a DEPLOYED copy under
`Creatio2\TSBpm\Src\Lib\Terrasoft.WebApp.Loader\Terrasoft.WebApp\Conf\content\`. Read the source
tree, not `Conf/content` — the deployed copy can lag, and citing it proves nothing about what ships.

**Server side** — `C:\Projects\Creatio2\TSBpm\Src\Lib\Terrasoft.Core\Process\` is where
`ProcessSchemaActivity`, `ProcessSchemaSubProcess`, `ProcessSchemaMultiInstanceOptions` and
`ProcessEnum` live; `Terrasoft.Core.Process` and `Terrasoft.Core.Process.Tests` alongside it hold the
execution engine and the platform's own tests, which are usable as evidence.

## The code sites you will need first

**Platform — `Terrasoft.Core/Process/ProcessSchemaActivity.cs`**
- `SynchronizeParametersInternal` — the rebuild. For a multi-instance element it clones the two
  collections and three counters, `Parameters.Clear()`, re-runs the ordinary diff, pushes the result
  into the collections' `ItemProperties`, clears again, and reloads exactly the five.
- `GetClonedCompletedIterationsCountParameter` and its two siblings — the counters SELF-HEAL:
  `TryCopyParameter` → on a miss `CreateIntegerParameter` → writes the new UId back into the options.
- `GetClonedInputCollectionParameter` → `GetInputCollectionParameter()` →
  `Parameters.GetByUId(MultiInstanceOptions.InputCollectionParameterUId)` — **`GetByUId` THROWS on a
  miss**, where `FindByUId` returns null. The collections do NOT self-heal. This asymmetry decides how a
  first conversion must be written: create both collection parameters, add them, and write their UIds
  into the options BEFORE any synchronization runs.
- `GetRemovedSchemaParameters` — the prune arm, `if (source == null && !target.IsDynamic)`. See the
  open loose end below.

**Platform — other**
- `ProcessSchemaMultiInstanceOptions.cs` — seven fields, `JE1`–`JE7`. `BP6` on the element.
- `ProcessEnum.cs` — `MultiInstanceExecutionMode` = `Sequential` | `Parallel`.
- `ProcessSchemaParameter.IsDynamic` = `CreatedInSchemaUId == BaseProcessSchema.UId`, and for an element
  parameter `BaseProcessSchema` resolves to the CALLER's schema.
- `MetaItemCollection.Add` backfills `CreatedInSchemaUId` from the parent schema whenever it is empty.

**Package**
- `Elements/SubProcessApplier.cs` — `EnsureNotMultiInstance` is the current refusal, a pre-condition at
  the top of both `Apply` and `Synchronize`.
- `Elements/SubProcessElementHandler.cs` — how `multiInstance` reaches describe.
- `Mappings/ProcessMappingService.cs` — how a mapping is written today, for every element kind.

**Designer client** — conversion is a CLIENT behaviour; no server API converts an element.
`SubProcessPropertiesPage.js`, `MappingEditMixin.js`, `ProcessFlowElementPropertiesPage.js`.

## Working rules, learned expensively on the parent

1. **Read `origin/main` / `origin/master` with `git show`, not the working tree.** Both the clio and
   ProcessBuilder checkouts sit on other branches. A reviewer's conclusion was wrong once for exactly
   this reason.
2. **Do not cite `docs/knowledge/` or `spec/` as EVIDENCE for a platform fact.** They are prior
   conclusions, several of which were wrong before being corrected. Use them to find which type or
   method to inspect, then verify in platform source and cite `file:line`.
3. **`GetByUId` throws; `FindByUId` returns null.** This distinction is load-bearing here.
4. **Version numbers come from git, never from prose.** For this element, everything below
   CrtProcessBuilder 1.6.3.26 is a branch-only number that never shipped; the element landed on `main`
   in one squash commit at 1.6.3.26.
5. **Quote a measurement with its predicate, date and scope.** Two scans of the same corpus with
   different attribution predicates gave 1 672 and 1 728 rows.
6. **Run schema-write operations against a stand SEQUENTIALLY.** A parallel burst trips IIS rapid-fail
   and takes down the .NET Framework stand's app pool.
7. The sub-process mechanism was stated wrongly five times on the parent. If you find yourself
   paraphrasing how a schema instance converges, check whether the guidance deliberately refuses to —
   it does, and so does `ProcessSchemaRepository.LoadForDescribe`.

## One open loose end in the file you will be editing

`SubProcessApplier`'s class summary on `main` still says:

> the platform removes such a row itself for every non-dynamic parameter, **this contract produces no
> dynamic ones**, and the state could not be constructed below a live stand

PR #72 corrected the neighbouring clause about the dependency scanner but left this one. It does not
hold: `IsDynamic` is "created in the CALLER's schema", so the owner-created parameter the hazard is
about is precisely the dynamic one the prune arm SKIPS — and `MetaItemCollection` backfills
`CreatedInSchemaUId` from the parent schema whenever it is empty, so a parameter added without a stamp
becomes dynamic. T-27 is therefore unobserved rather than prevented. Worth correcting while you are in
this file; it is the same prune arm the multi-instance rebuild passes through.

## Parent state, so you know what is settled

- Package PR #68 (the element) and #72 (the comment fix) — MERGED. `main` at 1.6.3.29.
- clio PR #1560 — MERGED. clio master bundles CrtProcessBuilder 1.6.3.29.
- clio-knowledge PR #176 (the run-time guidance) — MERGED at libraryVersion 1.15.38.
- Still the owner's, not yours: whether AC-4 is satisfied (parity holds on five keys; `BK15.GT1` and
  `BL8` differ, both analysed and neither a defect), and the stand residue from the parent's manual
  testing.
- Related, deliberately NOT part of this sub-task: **ENG-99852**, which measures builder-vs-designer
  metadata parity beyond the Sub-process element.

## What the research should produce

Answers to the six contract questions in the issue, grounded in source, plus a recommendation on scope.
In particular: how a mapping INTO the collection is expressed, whether describe should report the item
properties and in what shape, what happens to existing mappings on conversion, and whether
de-conversion is in scope at all.
