# Story 4: clio surface — MCP contract, guidance, rebundle, E2E

**Feature**: process-element-connections
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md)
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Decisions**: D1, D2 (wire naming), D8 (TAKEN in this story — see the ADR), D12 (open)
**Status**: review
**Size**: M
**Repo**: `clio` (+ the committed `CrtProcessBuilder.gz`)
**Depends on**: stories 2 and 3

---

## As a

coding agent reading clio's MCP surface

## I want

the connections operations to be discoverable, correctly described, and covered end to end

## So that

I can use them without reading the package source, and a stale package tells me so instead of silently
ignoring my request

---

## Acceptance Criteria

- [x] **AC-01** — The wire field is **`connections`**, never `activityConnections`, and carries no host
  member (D2). This is load-bearing: it is what lets a host entity be added later as an optional field
  instead of a breaking rename.
- [x] **AC-02** — `modify-business-process` accepts the two new `op` tokens; the tool `[Description]` and
  the curated contract describe them, including the D1a upsert semantics ("columns you do not list are left
  alone") and the fact that `recordId` needs no schema UId.
- [x] **AC-03** — `describe-business-process` surfaces `connections[]` with both the raw value and the
  decoded source.
- [x] **AC-04 (D8)** — An environment whose installed package predates this feature is **detected and
  reported**. This is mandatory, not defensive: measured on a live stand, a request carrying a
  future-shaped `connections` array is answered **normally with the member silently ignored** (no contract
  implements `IExtensibleDataObject`, checked across all `[DataContract]` types — 25 when measured, 27 after this
  feature — so it holds at every
  nesting level). `[RequiresPackage]` is presence-only and a pin test asserts the **absence** of a version
  literal, so the existing gate cannot carry this. Mechanism is D8 — take the decision in this story.
- [x] **AC-05** — Guidance updated in `clio-knowledge` (`guidance/mcp/guides/processes/process-modeling.md`
  — 316 lines; note the path is **not** `guidance/process-modeling.md`, and the repo is reachable via
  `gh api`, not `WebFetch`). The article currently says **nothing** about connections — zero occurrences of
  "Connected to", "ActivityConnection", "EntityConnection". Seven passages, pinned to lines:

  | Line | Change |
  |---|---|
  | 9–15 (tool list) | `list-user-tasks` gains the deprecation caveat and the note that two schemas share the caption "Send email" |
  | 17+ "What you can build today" | mention connections |
  | 178 (modify-op vocabulary) | add `setConnections` / `clearConnections` |
  | 231+ "Parameters / mapping / formulas" | the connections subsection, incl. upsert semantics and when to prefer `addMapping` |
  | **289** (Lookup-macro paragraph) | for connections, `recordId` replaces hand-crafting `[#Lookup.{schemaUId}.{recordId}#]` |
  | describe section | the `connections[]` projection, `deprecated`, `writesConnectionsAtRuntime` |
  | 293+ R1–R17 | state explicitly that connections are **not** graph edges and `validate-process-graph` is unaffected |

- [x] **AC-06** — The guidance PR bumps `libraryVersion` **and** `sequence` (clio rejects changed content
  under a reused sequence). A body edit needs no local re-pin; if a guide is added or renamed, the routing
  article and `curated-knowledge-names.json` follow.
- [x] **AC-07** — Rebundle via `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version
  X.Y.Z.W`. `-Version` is required and must go **up**: clio compares the shipped version against what the
  environment recorded, so an unchanged version reaches new installs only and nobody who already has the
  package is asked to update.
- [x] **AC-08** — E2E coverage added in `clio.mcp.e2e`. The existing process-designer surface is **43 tests
  across 9 files**; this feature extends three: `ModifyBusinessProcessToolE2ETests` (16 today — the two new
  operations), `DescribeProcessToolE2ETests` (**2 today** — the projection plus D11's per-dialect
  round-trip, six new cases, so this file more than triples), and `CreateBusinessProcessToolE2ETests` (14
  today) if `connections` is accepted in the build descriptor. `ValidateProcessGraphToolE2ETests` needs
  nothing — connections are not edges.
- [x] **AC-09** — Docs reviewed per the command-documentation policy; if no CLI verb is added (D12), state
  **"MCP-only, no CLI doc surface"** explicitly in the change summary rather than leaving it implied.
- [x] **AC-10** — Shipped workspace templates (`clio/tpl/**`) reviewed against the
  resident-or-bridged oracle; `WorkspaceTemplateGuidanceDriftTests` green.
- [x] **AC-11** — ClioRing compatibility statement in the change summary — either the commands/results, or
  `ClioRing compatibility reviewed, no Ring-consumed contract changed` with the inspected paths cited.

## Implementation Notes

The process-designer MCP tools are **non-resident**: they do not appear in `tools/list` and are reached
through the resident `clio-run` executor. Their contracts come from `get-tool-contract` with
`{"args": {"tool-names": [...]}}`. Keep that routing intact — a reachability test should pin *which* path
applies, not merely that the name resolves.

One trap that invalidates local verification: an install command resolves the bundled archive from the
**build output** directory, so `clio compress -d <repo path>` has no effect until clio is rebuilt.

When driving the service directly during development, pass the service path **without a leading slash** —
`rest/ProcessDesignService/DescribeProcess`. Git Bash rewrites a leading-slash argument into
`C:/Program Files/Git/rest/...`, which the stand rejects as a dangerous request path; `ServiceUrlBuilder`
normalises the slashless form and prepends the `0/` web-app alias itself.

## Deviations recorded at implementation time

1. **D8 was already solved by shipped machinery, so the story built a proof rather than a detector.**
   `IBundledPackageConvergence` already refuses when the environment carries an older bundled-package version
   than the distribution ships, and `RequiredPackageChecker` already runs it on every triggered
   `[RequiresPackage]` — which is every gated process-designer call. What was missing was the *decision* and
   evidence that the chain covers the connections case, so AC-04 landed as: D8 recorded in the ADR with its
   two rejected alternatives, plus two pin tests driving the SHIPPED attribute through the REAL checker and
   the REAL convergence rule (behind → refused naming both versions and the verb; converged → allowed). The
   rebundle's mandatory version bump is what arms it.
2. **AC-02's "curated contract" does not exist for these tools.** `ToolContractGetTool` carries no definition
   for `modify-business-process` or `describe-business-process` — it names them only inside
   `install-process-builder`'s contract, as the remedy. Their `[Description]` is therefore the agent-facing
   contract, and that is where the semantics went.
3. **Only Debug/net8.0 carries the new archive locally.** The rebundle script refreshes ONE build output and
   warns about the others; the net10.0 output could not be rebuilt because running `clio mcp-server` processes
   hold `clio.exe`. Anyone verifying an install locally must use the refreshed output or rebuild — an install
   resolves the archive from the build output, not the repository. CI is unaffected.
4. **AC-06's re-pin was not needed.** `curated-knowledge-names.json` follows a guide being ADDED or RENAMED;
   this was a body edit to an existing guide, so the local pin is untouched. (It is separately 9 sequences
   behind the live library — pre-existing, not this feature.)
5. **AC-09: MCP-only, stated rather than implied.** The four process-designer options classes carry no
   `[Verb]`, so there is no `docs/commands`, `help/en` or `WikiAnchors` target to update. D12 — whether to add
   a CLI verb and take on that doc surface — stays open.

6. **AC-08 landed in ONE fixture, not three.** All six connections cases live in
   `ModifyBusinessProcessToolE2ETests`, not split across it and `DescribeProcessToolE2ETests` as the AC
   describes: each case has to bind through modify and then verify through describe, so splitting them would
   have duplicated the arrange and left neither half meaningful alone. `CreateBusinessProcessToolE2ETests` is
   untouched because a build descriptor does not carry connections.
7. **The clio-side DTOs did not carry the new fields, and the first pass of this story shipped descriptions
   promising output that never arrived.** `DescribedElement` had no `connections`/`deprecated`/
   `writesConnectionsAtRuntime` and `ModifyProcessResultDto` had no `warnings`, so System.Text.Json dropped
   all four silently — the same failure class this whole feature exists to remove, on our side of the wire.
   Caught by the pre-PR review; the DTOs, a new `DescribedConnection` type and the warning surfacing were
   added before the PR was opened, and the E2E assertions were rewritten to go through the typed model
   because a substring of the serialized envelope passed for the wrong reason in five of the six cases.
8. **The six E2E cases were then RUN on a stand, and the deviation is what running them found.**
   `krestov-test` carried a hand-built `CrtProcessBuilder 1.1.0.1` against this clio's bundled `1.1.0.0` — a
   higher version with pre-feature code, which is precisely the case D8 records as accepted-not-guarded. So
   the first run failed on the platform's own `Operation 'setConnections' is not supported. Supported: …`
   rather than on anything this story wrote. `install-process-builder --force` (which also compiled the
   configuration — a source-only package needs that) then produced **6/6 green in 2m03s**. The ADR's D8
   section now records that the uncovered case degrades to a LOUD error, and why: an unknown member of a
   known contract is dropped in silence, an unknown operation NAME is refused by name.
9. **`writesConnectionsAtRuntime` was asserted by nothing, and now is.** The field was promised in
   `DescribeProcessTool`'s `[Description]` ("FALSE is the answer that matters"), declared on the DTO after
   deviation 7, and covered by no test at either level — so the rule was proven while the delivery was not,
   which is deviation 7's exact failure class one layer up. `ModifyBusinessProcessToolE2ETests` now asserts
   it on the wire (`true` for a perform task, which has no `CreateActivity` gate) via a `ReadTaskAsync`
   helper extracted from `ReadConnectionsAsync`.
10. **The runtime tail was measured for a STATIC connection column, which no green test above can do.** A connection that persists,
    compiles and reads back correctly can still write nothing — trap T-2, the `ModifiedInSchemaUId` stamp.
    On `UsrConnProbe1`/`CONNPROBE`: the pre-existing activity had `AccountId` set by the older `addMapping`
    path and `ContactId` NULL; after `setConnections` bound Contact to a **real** record and one
    `ProcessEngineService.svc/RunProcess`, the new activity carries the named Contact with `AccountId`
    unchanged as the control. A fixed-record connection is therefore effective at run time. Note for reuse:
    the E2E cases bind `Guid.NewGuid()` ids, which is correct for persistence and unusable for a run —
    `Activity.AccountId`/`ContactId` are foreign keys. What this does NOT close, stated because story 4's own
    earlier note mis-assigned it to these six cases: the **created/dynamic** parameter tail at task completion.
    Every case here binds a pre-existing STATIC column and none runs a process, so no E2E case can reach it, and
    a probe cannot either today — `EntityConnectionBinder.ResolveColumn` refuses a column that exists on the host
    but is neither registered nor element-declared, so the created path needs a column that IS registered and NOT
    declared. Making one is what story 5 builds, so the check belongs to story 5 acceptance.

## Definition of Done

- [x] All AC met
- [x] Targeted tests run and named in the change summary:
  `dotnet test clio.tests --filter "Category=Unit&(Module=McpServer|Module=Command|Module=Common)"` →
  7810 passed / 18 skipped / 0 failed (net8.0)
- [x] MCP review statement included — this story IS the MCP change: two tool `[Description]`s, the modify
  prompt, and two new pin tests. No curated `get-tool-contract` entry exists for the process-designer tools,
  so the `[Description]` is their contract surface
- [x] Diary entry appended
- Manual (human/agent-driven) coverage lives in
  [process-element-connections-manual-tests.md](../process-element-connections/process-element-connections-manual-tests.md).
  It is not a substitute for the six E2E cases below and does not gate this story — it covers what they
  structurally cannot: the designer round-trip, the stale-package refusal, and the runtime tail of each
  macro dialect
- [x] Verified on a live stand (krestov-test), for a perform task on STATIC connection columns: 6/6 green after
  `install-process-builder --force`; `writesConnectionsAtRuntime` asserted on the wire; and the runtime
  tail measured on `UsrConnProbe1` — a fixed-record connection populates a pre-existing STATIC column of the
  created Activity record (not a created column, and not a created parameter),
  with the pre-existing mapping-sourced column unchanged as the control
