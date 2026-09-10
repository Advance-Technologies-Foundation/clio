# Story 17: Expose set-active-business-process-version through MCP

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-08, FR-09, FR-14
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: done
**Size**: M

---

## As a

developer (AI agent acting for a no-code builder)

## I want

an MCP tool that makes a chosen version actual

## So that

rollback is one explicit, auditable gesture

---

## Acceptance Criteria

- [x] **AC-01** — Given a version identity, when the tool runs, then it reports the active version read back after the write
- [x] **AC-02** — Given the read-back does not match, when the tool returns, then it fails and names the version that is actually active
- [x] **AC-03** — Given the tool metadata, when it is reflected, then it is `ReadOnly=false, Destructive=true, Idempotent=true, OpenWorld=false`
- [x] **AC-04** — Given the tool description, when it is read, then it states that activation affects only NEW instances, that running instances stay on their version, that the UI calls this the actual version, and that deleting a version does not exist
- [x] **AC-ERR** — Given neither `version-name` nor `version-uid` is supplied, or both are, when the tool runs, then the MCP result is `success:false` with a message naming the violation

## Implementation Notes

New: `SetActiveProcessVersionTool.cs`, `SetActiveProcessVersionCommand.cs` + options. Same two easily-missed edits as story 16: a `KnownRoute` value with its leading-slash route string, and a `BindingsModule` registration.
The versioned `[RequiresPackage]` floor is declared here, at or below the version story 15 bundled.
Destructive ⇒ it owns its own timeout contract and must NOT route through the 120 s read deadline (`clio/Command/McpServer/AGENTS.md`).
`Idempotent=true` is deliberate: setting the same version active twice yields the same state.
The same two counted pins as story 16 move with this tool.

## Sequencing deviation — this story runs BEFORE story 15

Agreed 2026-09-06. `depends_on: story-process-versioning-15` is inverted deliberately; the dependency is
kept in `sprint-status.yaml` because it still describes the MERGE order.

**Why.** This story touches no package source — the archive's content was final at story 13 — while story 15
CUTS the archive, which is the one step that is expensive to redo: the SHA pin, and the rule that a
same-version re-cut is only legitimate while the cut never left the machine. Cutting before the consumer
exists bets that building and E2E-testing these tools reveals no gap on the package side, and the tools are
exactly where such a gap surfaces. So the archive is cut last, from sources a consumer has exercised.

**What this costs.** Two named tests in `clio.tests/Common/BundledProcessBuilderPackageTests.cs` are RED from
here until story 15 lands, and both go green in that one commit:

| Test | Why it is red |
|---|---|
| `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` | the floor declared here (1.5.0.0) exceeds the version in the still-old archive (1.4.0.40) |
| `ToolContractVersionLiterals_ShouldMatchTheBundledArchiveVersion` | this tool's description names 1.5.0.0 while `ExpectedArchiveVersion` is 1.4.0.40 |

`ExpectedOperationContractCount` stays green here — it counts `[OperationContract]` inside the ARCHIVE, which
still carries five — and moves to 7 in story 15.

**E2E does not need the archive.** `[RequiresPackage]` reads the environment's recorded `SysPackage.Version`,
so a package deployed to the sandbox straight from the workspace (as story 12's verification did) satisfies
the floor at 1.5.0.0 with no bundle involved.

**If the package changes** while these stories run, re-stamp `ModifiedOnUtc` only — 1.5.0.0 has not shipped,
so re-cutting under it stays legitimate.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | arg validation, flag pins, read-back mismatch surfaced, package-floor refusal, route resolution | `clio.tests/Command/McpServer/SetActiveProcessVersionToolTests.cs` |
| E2E `[Category("E2E")]` | activate on a dedicated sandbox behind the destructive opt-in | `clio.mcp.e2e/SetActiveProcessVersionToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] `PassthroughToolClassificationRegistry` row and a `McpCoreToolProfile` decision — row at
  `PassthroughToolClassificationRegistry.cs:375` (`NotApplicable`, class (a)); NOT resident, as with every
  process-designer tool
- [x] `install-process-builder` md + txt and `docs/McpCapabilityMap.md` list the tool
- [x] The two counted pins moved or explicitly ruled out — surfaces dictionary MOVED, `GoLiveToolTypes`
  explicitly ruled out (see notes)
- [x] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`) — in the Dev Agent Record below,
  in the wording the policy requires, with the inspected paths cited
- [x] Code compiles without Roslyn analyzer warnings — no new `CLIO*` or `CS*` warning in any file this
  story added or edited; the two pre-existing `CLIO001` warnings in `MobileDiffApplyValidator` are untouched
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths — DEVIATION, deliberate and on record: the command's
  `Execute` boundary mirrors the shipped error-to-exit-code handler its siblings already carry
  (`ModifyBusinessProcessCommand.cs:267`, `CreateBusinessProcessCommand.cs:209`), so this is the family's
  existing pattern rather than a new one. Every OTHER path added by this story uses a narrow ladder
  (`ProcessLibRead.Guarded`)
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this
  PR** (`AGENTS.md:456-457`) — `a-missing-operation-floor-needs-a-version-literal-not-presence.md` names
  `SetActiveProcessVersionCommand.cs` and covers this floor; no other record names a touched file
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the
  verdict — stated in the Dev Agent Record below; it moves into the PR body when the branch is pushed
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies) — same:
  stated in the Dev Agent Record below, to move into the PR body on push
- [x] PR description references this story file — open in clio#1410 (https://github.com/Advance-Technologies-Foundation/clio/pull/1410)

## Dev Agent Record

- Implementation started: 2026-09-07
- Implementation completed: 2026-09-07
- Tests passing: 11142 unit (11124 + 18 new). The SAME two tests stay red as after story 16 — no new red.
- Notes:

**Shipped.** `SetActiveProcessVersionTool` + args record, `SetActiveProcessVersionCommand` with its options /
service / request / result, `KnownRoute.SetActiveProcessVersion = 69` mapped to
`/rest/ProcessDesignService/SetActiveProcessVersion`, the two `BindingsModule` registrations, the
`PassthroughToolClassificationRegistry` row (`NotApplicable`, class (a)) and the surfaces-dictionary entry.
Unit coverage in `SetActiveProcessVersionToolTests` (12) and `SetActiveProcessVersionServiceTests` (6), E2E
in `SetActiveProcessVersionToolE2ETests` (4).

**The read-back IS the operation, and the failure branches carry the weight.** The platform logs and
SWALLOWS a failure to deactivate a sibling, so a call reporting plain success could leave two members flagged
active with PACKAGE ORDER deciding which one runs. So the service never echoes the request: a mismatch throws
and names the version the environment actually reports as actual, and remaining active siblings are counted
into the message with what that means. Three of the six service tests exist only for those branches.

**`Destructive=true` is what excludes this tool from the 120 s read-response deadline** —
`McpReadDeadlineGate.IsRetrySafe` is `!destructive && …`, and that gate admits no server write, idempotent
or not. Pinned by `SetActiveProcessVersion_ShouldNotBeBoundedByTheReadResponseDeadline` rather than left to
the flag: a deadline that abandoned this call mid-switch would leave the family in exactly the two-active
state the read-back exists to catch, with nobody reading it back. `Idempotent=true` is about REPEATING the
call — same version twice, same state — not a claim that an abandoned call is safe to retry blindly; the
E2E asserts the repeat, and the tool comment says which of the two the flag means.

**The description carries four statements an agent cannot derive**, each pinned by a test because each is a
wrong assumption an agent otherwise reports as fact: activation reaches NEW instances only; instances already
running stay on their version and finish on it; the UI word is "actual" where the platform's data says
"active"; and NO operation anywhere deletes a version, so rollback means activating an earlier one rather
than removing the newer one. Plus an explicit ASK FIRST — the product's own designer asks before making a
version actual, so an agent must not chain this onto `modify-business-process-as-new-version` by itself.

**Same two counted pins as story 16, resolved the same way.** Surfaces dictionary MOVED (this description
names 1.5.0.0). `ProcessDesignerGoLiveTests`' `GoLiveToolTypes` ruled out for the same reason — that set
records the ENG-96132 go-live and counts five — with the substance pinned by
`SetActiveProcessVersion_ShouldNotBeFeatureGated` here. `McpCoreToolProfile`: NOT resident, as with every
process-designer tool. The versioned-floor test in `ProcessDesignerRequiresPackageAttributeTests` was
PARAMETERISED over both versioning options types rather than duplicated, since both floors exist for the same
missing-operation reason.

**No new red.** `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` and
`ToolContractVersionLiterals_ShouldMatchTheBundledArchiveVersion` were already red from story 16 and this
story adds a second floor and a second surface to the same two — both go green in story 15's commit.

**Docs verdict.** `install-process-builder` docs + help list the tool; `docs/McpCapabilityMap.md` gains its
row. No CLI verb exists (MCP-only, like every process-designer command), so `clio/help/en/<verb>.txt`,
`clio/docs/commands/<verb>.md`, `clio/Commands.md` and `WikiAnchors.txt` need no entry.

**MCP verdict.** MCP reviewed and updated: new tool, args record, registry row, capability map, install-hint
surfaces. No prompt added — the sequence this tool belongs to is guidance (story 18), not a per-tool prompt.

**Knowledge base.** No new record: the missing-operation-floor fact written in story 16
(`docs/knowledge/Command/a-missing-operation-floor-needs-a-version-literal-not-presence.md`) covers this
floor too, and its `applies-to` now names both command files. No existing record names a file this story
touches.

**ClioRing compatibility reviewed, no Ring-consumed contract changed.** Same inspection as story 16 —
`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, `clio-ring/ClioRing.Desktop/actions.json` carry no
process-designer tool call. Purely additive: a new tool name, no existing name, argument, flag or result
shape altered.

**Not verified on a stand.** The E2E fixture is gated on `McpE2E:Sandbox:EnvironmentName`, which is not
configured on this machine, so AC-01/AC-02's read-back behaviour is asserted but not yet observed over the
real MCP path. It runs with story 15's stand verification. The underlying package operation WAS observed on
a stand during story 13 (version 2 of "Story 12 verification" activated and left active).
