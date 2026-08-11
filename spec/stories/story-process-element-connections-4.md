# Story 4: clio surface — MCP contract, guidance, rebundle, E2E

**Feature**: process-element-connections
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md)
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Decisions**: D1, D2 (wire naming), D8 (open), D12 (open)
**Status**: ready-for-dev
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

- [ ] **AC-01** — The wire field is **`connections`**, never `activityConnections`, and carries no host
  member (D2). This is load-bearing: it is what lets a host entity be added later as an optional field
  instead of a breaking rename.
- [ ] **AC-02** — `modify-business-process` accepts the two new `op` tokens; the tool `[Description]` and
  the curated contract describe them, including the D1a upsert semantics ("columns you do not list are left
  alone") and the fact that `recordId` needs no schema UId.
- [ ] **AC-03** — `describe-business-process` surfaces `connections[]` with both the raw value and the
  decoded source.
- [ ] **AC-04 (D8)** — An environment whose installed package predates this feature is **detected and
  reported**. This is mandatory, not defensive: measured on a live stand, a request carrying a
  future-shaped `connections` array is answered **normally with the member silently ignored** (no contract
  implements `IExtensibleDataObject`, checked across all 25 `[DataContract]` types, so it holds at every
  nesting level). `[RequiresPackage]` is presence-only and a pin test asserts the **absence** of a version
  literal, so the existing gate cannot carry this. Mechanism is D8 — take the decision in this story.
- [ ] **AC-05** — Guidance updated in `clio-knowledge` (`guidance/mcp/guides/processes/process-modeling.md`
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

- [ ] **AC-06** — The guidance PR bumps `libraryVersion` **and** `sequence` (clio rejects changed content
  under a reused sequence). A body edit needs no local re-pin; if a guide is added or renamed, the routing
  article and `curated-knowledge-names.json` follow.
- [ ] **AC-07** — Rebundle via `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version
  X.Y.Z.W`. `-Version` is required and must go **up**: clio compares the shipped version against what the
  environment recorded, so an unchanged version reaches new installs only and nobody who already has the
  package is asked to update.
- [ ] **AC-08** — E2E coverage added in `clio.mcp.e2e`. The existing process-designer surface is **43 tests
  across 9 files**; this feature extends three: `ModifyBusinessProcessToolE2ETests` (16 today — the two new
  operations), `DescribeProcessToolE2ETests` (**2 today** — the projection plus D11's per-dialect
  round-trip, six new cases, so this file more than triples), and `CreateBusinessProcessToolE2ETests` (14
  today) if `connections` is accepted in the build descriptor. `ValidateProcessGraphToolE2ETests` needs
  nothing — connections are not edges.
- [ ] **AC-09** — Docs reviewed per the command-documentation policy; if no CLI verb is added (D12), state
  **"MCP-only, no CLI doc surface"** explicitly in the change summary rather than leaving it implied.
- [ ] **AC-10** — Shipped workspace templates (`clio/tpl/**`) reviewed against the
  resident-or-bridged oracle; `WorkspaceTemplateGuidanceDriftTests` green.
- [ ] **AC-11** — ClioRing compatibility statement in the change summary — either the commands/results, or
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

## Definition of Done

- [ ] All AC met
- [ ] Targeted tests run and named in the change summary (Module=McpServer at minimum)
- [ ] MCP review statement included ("MCP reviewed, no update required" is not available here — this story
  *is* the MCP change)
- [ ] Diary entry appended
