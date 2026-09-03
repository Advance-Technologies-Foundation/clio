# PRD: Business process versioning — read, create, set actual

**Status**: Draft (revised after adversarial review 2026-09-02)
**Author**: PM Agent
**Created**: 2026-09-02
**Jira**: ENG-94374

---

## Problem Statement

`describe-business-process` resolves a process by schema Name, and every saved process version is a *separate* schema with its own name, so on a versioned process it describes the base version while the runtime executes the active one — and the response carries no version field to reveal it. Measured on a stock stand: `DescribeProcess(name:"InvoiceVisaProcess")` returns the inactive v0 graph (15 elements, **3** parameters) while the runtime executes `InvoiceVisaProcessInvoice1` (15 elements, **7** parameters); both versions share the caption "Invoice approval", and 12 such families ship with the product. Separately, the toolkit cannot create a version or change which one is actual, so an agent asked to edit a process has no choice but to overwrite the running one — the operation Academy documents as the dangerous one ("If the process has active instances, they might be stopped when you save the changes").

## Vocabulary

- **Active = actual.** The platform's identifiers say `IsActiveVersion` / `SetActiveVersionItem`; the product UI and its REST op say "actual" ("Set as actual version"). This document and the contract use *active*; user-facing text must bridge to *actual*.
- **Version family** — the flat set of schemas whose `ParentSchemaUId` is the same root. A version's parent is always the root, never the previous version.
- **Family root** — the version-0 schema; its own `VersionParentUId` equals its `UId`.
- **Provenance of the active-version answer** — this build answers from the process-library view, not from the schema manager the runtime consults. The response carries that provenance explicitly (non-goal 4 and FR-19), so a consumer can tell what it is trusting.

## Scope reversal on record

The prior recorded decision for this ticket (workspace diary, 2026-08-13) was to ship the read half only and defer create + activate with rationale. That decision is **reversed** by the ticket owner on 2026-09-02: the two driving use cases (save edits as a new version; roll back to a previous version) are exactly the deferred half, so the full scope is in. The estimate correction stands — the work is ~9-10 days, not the ~1 day inherited from ENG-91852's Task 14.

## Goals

- [ ] Goal 1 — describe tells the truth about versions. SM-01: for every process reachable through `describe-business-process` on an environment carrying `CrtProcessBuilder` (the tool is package-gated, so the metric is stated over that population), the response carries `version`, `isActiveVersion`, `activeVersionSchemaUId`/`activeVersionName`, `versionRootSchemaUId`, `versions[]` and the provenance member; describing any of the 12 stock versioned roots by name reports `isActiveVersion:false` and names the active version. Counter: a failed version read MUST NOT turn a previously successful describe into an error — the members are absent, the graph still returns.
- [ ] Goal 2 — an agent can save edits as a new version instead of overwriting the running process. SM-02: `modify-business-process-as-new-version` yields a family member carrying the requested edits, whose `version` is the number **the platform allocated** and whose `isActiveVersion` is `false`, in 100% of calls; run twice against one root it yields 1 then 2; and a rejected edit leaves the family unchanged. Counter: creating a version MUST NOT re-point what the environment executes.
- [ ] Goal 3 — an agent can roll back by making a previous version actual. SM-03: the activation response reports the active version **read back** after the write and returns `success:false` when it is not the requested one, in 100% of calls; after a successful activation exactly one family member is active. Counter: a partial activation (the platform logs-and-swallows sibling deactivation failures) MUST NOT be reported as success.
- [ ] Goal 4 — the version-blind paths this feature owns are closed. SM-04: `describe --process-caption`, `generate-process-model` and `run-process` each resolve to, or name, the active version — three paths, enumerated, not "all of them". Counter: an unversioned process MUST NOT start failing where it previously worked.

## Non-goals

- Will NOT delete a version. The product exposes it nowhere (`ProcessVersionsDetail` disables Add/Edit/Copy/Delete) and the platform's delete sets **every** `SysProcessLog` row of that schema to Cancelled (`BaseProcessSchemaManager.BeforeRemoveProcess`). Permanently out of scope, not deferred, and with no exception: this build never removes a version. FR-16 makes removal unnecessary rather than available.
- Will NOT migrate running instances between versions. Instances stay pinned to the version they started on; Academy never describes moving them for processes, and for dynamic cases the same gesture cancels the instance.
- Will NOT make `get-process-signature` version-aware. It resolves by code or caption (`GetProcessSignatureCommand.cs:29-31`) and returns the parameter signature — the very field that differs across versions — so it stays a known version-blind path, excluded deliberately to keep this ticket's surface bounded. Raise a follow-up ticket; do not treat SM-04 as covering it.
- Will NOT surface `IsMaxVersion`. The view computes it over an asymmetric pool with a lexicographic `MAX` over a character column, and two versions of one root created in different packages both report `true`.
- Will NOT promise the runtime's verdict from a client-side read. The process-library view and the schema manager rank candidates by different tail keys, so this build reports **the view's answer with its provenance stated**; the authoritative answer requires the package and is a follow-up.
- Will NOT add a session-mode field to any request. "Keep creating versions for the rest of this session" is agent behaviour and lives in guidance, never in the wire contract.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| developer | describe to tell me which version I am reading and which one runs | I stop explaining a graph that is not the one executing |
| developer (AI agent acting for a no-code builder) | to save the builder's edits as a new version and set it actual only when asked | the builder can iterate on a live process without stopping its instances |
| developer (AI agent acting for a no-code builder) | to make a previous version actual again | a change that turned out wrong can be rolled back |
| QA engineer | version facts in the describe output | I can assert which schema a test actually exercised |
| CI pipeline author | version-blind resolution paths to resolve to the active version | `generate-process-model` stops failing on versioned processes |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | describe reports this schema's `version` and whether it is the active one | Must |
| FR-02 | describe reports the schema UId and name of the version that actually runs | Must |
| FR-03 | describe reports the version-family root as `versionRootSchemaUId` | Must |
| FR-04 | describe reports the family as `versions[]`, ascending by version, marking the active member and the root | Must |
| FR-05 | An **unestablished** version fact is absent from the response; absence never means "version 0" or "unversioned" (an unversioned process genuinely reports 0 — see FR-18 for how the two are told apart) | Must |
| FR-06 | A version read that fails degrades to absent members and never fails the describe | Must |
| FR-07 | One operation applies a list of edits to a **new version** of an existing process and saves it, leaving the source version untouched. The new version is inactive and numbered by the platform's allocator — the toolkit never computes the number itself. An empty edit list is legal and yields a pure snapshot | Must |
| FR-08 | An operation makes a named version the actual one, verifying the result by reading it back, and leaves exactly one member active | Must |
| FR-09 | Saving edits as a new version and making a version actual are separate operations with separate names | Must |
| FR-10 | `describe --process-caption` resolves to the active version instead of an arbitrary family member | Must |
| FR-11 | `generate-process-model` resolves a versioned process instead of failing on caption ambiguity | Should |
| FR-12 | `run-process` states that it starts the active version, not the named one | Should |
| FR-13 | Guidance states the version model: flat family, exactly one actual, instances pinned, rollback affects new runs only, no delete | Must |
| FR-14 | Both new tools refuse an environment whose `CrtProcessBuilder` predates the operations, naming the required version and the install command, and perform no write | Must |
| FR-15 | After the save, the save-as-new-version operation re-reads the family and returns `success:false` naming the observed number when another writer took it | Must |
| FR-16 | A failed edit leaves **nothing** behind: the edits and the new version are one operation, so a rejected edit saves no schema at all | Must |
| FR-17 | `versions[]` is capped at 50 members and the response states when the cap was applied | Must |
| FR-18 | Whenever a version fact is missing the response carries a warning naming THAT fact — not only when the read failed outright: a read that succeeds and establishes less than everything (no member flagged active, two flagged, a NULL column) carries the warning ALONGSIDE the values it did establish. An unversioned process carries no warning | Must |
| FR-19 | The response states the provenance of the active-version answer (the process-library view) | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New MCP tool | `modify-business-process-as-new-version` — the same `operations[]` payload as `modify-business-process`, applied to a new version instead of in place | No |
| New MCP tool | `set-active-business-process-version` | No |
| New response members | `describe-business-process` gains `version`, `isActiveVersion`, `activeVersionSchemaUId`, `activeVersionName`, `versionRootSchemaUId`, `versions[]`, the cap and provenance members — additive | No |
| **Changed behaviour of an existing CLI verb** | `generate-process-model` (`[Verb]` at `GenerateProcessModelCommand.cs:11`, alias `gpm`) resolves a versioned process to its active version instead of failing on caption ambiguity. Doc targets that move with it: `clio/help/en/generate-process-model.txt`, `clio/docs/commands/generate-process-model.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` | Behavioural — a call that failed now succeeds |
| Changed behaviour of an MCP tool | `describe --process-caption` picks the active version instead of an arbitrary family member, and gains a failure mode it did not have: two processes genuinely sharing a caption now return an ambiguity error instead of an arbitrary pick | Behavioural — a call that silently succeeded can now fail |
| No new CLI verb | the two new operations are MCP-only. Note that the process family is **not** entirely MCP-only: `get-process-signature` and `generate-process-model` both carry `[Verb]` | No |

All flags and tool names: **kebab-case only**. Tool contracts are indexed through `get-tool-contract` and documented in `docs/McpCapabilityMap.md` §11, which moves in the same change as any description.

## Acceptance Criteria

- [ ] AC-01: Given a process with no versions, when describe runs, then `version` is 0, `isActiveVersion` is true, `versions[]` holds one entry marked as the root, and no read-failure warning is present.
- [ ] AC-02: Given a versioned family, when describe runs against the root **by name**, then `isActiveVersion` is false and `activeVersionName` names the version the runtime executes.
- [ ] AC-03: Given a versioned family, when describe runs against the active version by UId, then `isActiveVersion` is true.
- [ ] AC-04: Given the version read establishes NOTHING, when describe runs, then the graph is returned, none of the version value members (`version`, `isActiveVersion`, `activeVersionSchemaUId`, `activeVersionName`, `versionRootSchemaUId`, `versions`, `activeVersionSource`) appears in the serialized output, and `versionReadWarning` — the one version member present in this case — names the failure. Given the read establishes SOME facts and not others, then the established members appear **together with** `versionReadWarning` naming what could not be established, and the members that could not be established stay absent.
- [ ] AC-05a: Given an existing process with no versions, when the operation runs with a list of edits, then a new family member exists carrying those edits, with `isActiveVersion:false` and `version` equal to 1, and the source version is unchanged **both on disk and in memory** — see AC-05e.
- [ ] AC-05b: Given that same root, when the operation runs a second time, then the new member's `version` is 2.
- [ ] AC-05c: Given an edit list one of whose operations is rejected, when the operation runs, then it returns `success:false` and **no** new family member exists.
- [ ] AC-05e: Given edits that add or remove flows and elements, when the operation completes, then the SOURCE schema instance held in the manager's cache is untouched: its elements' `Outgoings`/`Incomings` counts are unchanged and its `Group` localizable string is still bound to its own resources. A byte-identical database row is not sufficient evidence.
- [ ] AC-05d: Given an empty edit list, when the operation runs, then a new inactive version exists that is an exact copy of the source.
- [ ] AC-06a: Given a family whose v1 is active, when set-active runs against v0, then the response reports v0 as the active version, read back after the write.
- [ ] AC-06b: Given that same call, when the family is re-read, then exactly one member is active and it is v0.
- [ ] AC-07: Given a substituted version reader that reports a different member as active after the write, when set-active completes, then it returns `success:false` naming the member that is actually active.
- [ ] AC-08: Given a versioned process addressed by caption, when describe or `generate-process-model` runs, then the active version is used and the caption ambiguity error does not appear.
- [ ] AC-09: Given two processes that genuinely share a caption, when either command runs, then the existing ambiguity error is still returned.
- [ ] AC-10: Given an environment whose `CrtProcessBuilder` predates the operations, when either new tool runs, then it returns `success:false` naming the required version and the `install-process-builder` hint, and performs no write.
- [ ] AC-11: Given two save-as-new-version calls interleaved against one root, when both complete, then no two members share a version number and the losing call returns `success:false` naming the number that was taken.
- [ ] AC-13: Given a family with more than 50 members, when describe runs, then 50 are returned and the response states that the cap was applied.

> AC-12 was withdrawn on 2026-09-02 together with the compensating delete; the number is deliberately left unused so references to it fail loudly rather than silently re-point.
- [ ] AC-ERR-MCP: Given an unknown or ambiguous identity, when either new tool runs, then the result is `success:false` with a message naming the identity — the MCP result envelope, not a process exit code.
- [ ] AC-ERR-CLI: Given an unknown process, when `generate-process-model` runs, then clio prints `Error: {message}` and exits non-zero, as it does today.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | `VwProcessLib` returns every member of a family under the caller's rights — **verified live** on 12 families | if false on another stand, the read must move server-side |
| A-02 | A value assigned to a schema instance before save persists as a `SysSchemaProperty` row — **verified**: `Assign` → `ParseObject` rewrites every extra property on each save, and the toolkit's own `Tag` already persists | if false, the platform's allocator cannot be trusted and FR-07 needs another source |
| A-03 | The `<root><Package><N>` name shape applies only to versions this build creates — **verified**: most stock version schemas are hand-named | nothing may parse a name to detect versionhood |
| A-04 | The view's active-version answer matches the runtime while one member carries the flag — **verified** on 3 families including 2 cross-package | families tying on the first two ordering keys diverge; FR-19's provenance member is the mitigation |
| A-05 | The version read fits the 120 s read deadline — a single row measured at ~65 ms **with explicit column projection** | the model declares `MetaData byte[]`, so an unprojected family read pulls schema metadata per row; the reader must project or the figure does not hold |
| A-06 | The platform's allocator is package-scoped (`GetMaxProcessVersionInPackage` filters `PackageUId`) | two versions of one root in different packages both get 1; FR-17's cap and the provenance member keep the report honest, but the numbers repeat |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Which package receives a new version when the source package is not editable — the client decides this today (`getDesignPackageUId` vs current package) and the server has no such logic | Architect | before FR-07 |
| OQ-02 | Is `UseNewSchemaHierarchyFolding` enabled on the target estate? It changes the manager's third ordering key. FR-02 and FR-19 do **not** depend on the answer — the response states its provenance instead of naming an ordering key — so this is due before the guidance article describes divergence, not before the read half ships | Architect | before the guidance stories (6 / 18) |
| OQ-03 | Order of the `CrtProcessBuilder` rebundle relative to the in-flight version stamps in that repository, and therefore when the versioned `[RequiresPackage]` floor may land | Dev | before FR-14 ships |
| OQ-04 | Does `ServiceModel/ProcessEngineService.svc/RunProcess` fold a NON-active version code onto the active version, or start exactly the schema named? Only the scheduled path is evidenced (`ProcessRunner.TryRunScheduledProcess` -> `GetActiveVersionItem`); settling it needs a launch on a disposable stand, so `run-process` currently tells the agent to pass the active code explicitly and says the fold is not established | Dev | before any guidance article describes launching a versioned process (story 6 / 18) |

## Dependencies

- Depends on: `CrtProcessBuilder` gaining two operations; the bundled archive, its pins and the tools' version floor move together (`BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement`).
- Depends on: two new `ServiceUrlBuilder.KnownRoute` entries — clio reaches the package only through that map.
- Depends on: a `clio-knowledge` PR for the `process-versions` guidance article (FR-13), and the clio-side curated-name re-pin that follows it.
- Depends on: the ClioRing MCP compatibility gate being re-stated for the two new tools.
- Blocks: nothing else in the backlog.
- Related: ENG-91852 (this work was split out of its Task 14), ENG-90883 (parent research), ENG-95791 (`run-process`, whose description FR-12 amends).
