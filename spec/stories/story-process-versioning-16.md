# Story 16: Expose modify-business-process-as-new-version through MCP

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-09, FR-14, FR-16
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

an MCP tool that applies edits to a new version instead of the running one

## So that

the agent can offer the choice between editing in place and saving a new version

---

## Acceptance Criteria

- [x] **AC-01** — Given a source identity and a list of edits, when the tool runs, then it reports the new version's schema UId, name, platform-allocated number, `isActiveVersion:false`, the family root and the applied-operation count
- [x] **AC-02** — Given the same `operations` payload accepted by `modify-business-process`, when it is passed to this tool, then it is accepted unchanged — one descriptor vocabulary, two destinations
- [x] **AC-03** — Given an environment whose package predates the operation, when the tool runs, then it returns `success:false` naming the required version and the `install-process-builder` hint, and performs no write
- [x] **AC-04** — Given the tool metadata, when it is reflected, then it is `ReadOnly=false, Destructive=false, Idempotent=false, OpenWorld=false` — not destructive, because the source version is untouched
- [~] **AC-05** — Given the new version, when describe runs against it, then it appears as a family member with `isActiveVersion:false` carrying the edits
- [x] **AC-ERR** — Given neither `process-name` nor `process-uid` is supplied, or both are, when the tool runs, then the MCP result is `success:false` with a message naming the violation

## Implementation Notes

New: `Tools/ProcessDesigner/ModifyProcessAsNewVersionTool.cs` (name `modify-business-process-as-new-version` as an `internal const`), `clio/Command/ModifyProcessAsNewVersionCommand.cs` + options.
**Two edits that are easy to miss:** a `ServiceUrlBuilder.KnownRoute` value plus its `"/rest/ProcessDesignService/ModifyProcessAsNewVersion"` string, with the leading slash as all five sibling `ProcessDesignService` entries have (`:315-322`); and `services.AddTransient<ModifyProcessAsNewVersionCommand>()` in `BindingsModule.cs` next to `:425`/`:427`/`:1031`. Touching `BindingsModule.cs` is a full-suite regression trigger and forces the full three-lens review.
The versioned `[RequiresPackage]` floor is declared HERE, at or below the version story 15 bundled — a floor above it fails `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement`. Add the `[TestCase]` to the matching list in `ProcessDesignerRequiresPackageAttributeTests`, including its stale-package detector cases (`:144`).
The description must say what the product calls this gesture ("Save new version") and that the source version keeps running until something activates the new one.
Two counted pins move with a new tool: `ProcessDesignerGoLiveTests.cs:27-33` lists the process-designer tool types and its `[Description]` at `:61` counts them, and `BundledProcessBuilderPackageTests.cs:885` drives off a surfaces dictionary. Decide explicitly whether the new tool joins `GoLiveToolTypes`.
Add the `PassthroughToolClassificationRegistry` row with its own tool family, and take a `McpCoreToolProfile` decision. Do not add the tool to `DurableInvocationGateCompletenessTests`' reviewed-silently-executable list: a mutating tool loses `ReadOnly=true` instead.

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
| Unit `[Category("Unit")]` | arg validation, descriptor pass-through, flag pins, package-floor refusal including the stale-package case, route resolution | `clio.tests/Command/McpServer/ModifyProcessAsNewVersionToolTests.cs` |
| E2E `[Category("E2E")]` | save a new version against a dedicated sandbox behind the destructive opt-in | `clio.mcp.e2e/ModifyProcessAsNewVersionToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] `install-process-builder.md:24-27` and `help/en/install-process-builder.txt:16-19` list the new tool
- [x] `docs/McpCapabilityMap.md` gains the tool row in the same commit
- [x] The two counted pins (`ProcessDesignerGoLiveTests`, the version-literal surfaces dictionary) moved or explicitly ruled out — surfaces dictionary MOVED, GoLive set explicitly ruled out (see notes)
- [x] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`)
- [x] Code compiles without Roslyn analyzer warnings
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths — the command's boundary mirrors the sibling command's shipped error-to-exit-code handler, not a new one
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`) — one ADDED; no existing record names a touched file
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [x] PR description references this story file — open in clio#1410 (https://github.com/Advance-Technologies-Foundation/clio/pull/1410)

## Dev Agent Record

- Implementation started: 2026-09-06
- Implementation completed: 2026-09-06
- Tests passing: 11124 unit (baseline 11102 + 22 new). **Two tests are RED on purpose** — see below.
- Notes:

**Shipped.** `ModifyProcessAsNewVersionTool` + args record, `ModifyProcessAsNewVersionCommand` with its
options / service / request / result, `KnownRoute.ModifyProcessAsNewVersion = 68` mapped to
`/rest/ProcessDesignService/ModifyProcessAsNewVersion` (leading slash, as its five siblings have), and the two
`BindingsModule` registrations. Unit coverage in `ModifyProcessAsNewVersionToolTests` (14) and
`ModifyProcessAsNewVersionServiceTests` (8, the only coverage of the clio->server wire contract), E2E in
`ModifyProcessAsNewVersionToolE2ETests` (4).

**The two intentional reds**, both green in story 15's commit and both consequences of building this before
the rebundle (see "Sequencing deviation" above): `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement`
(this floor is 1.5.0.0; the committed archive still carries 1.4.0.40) and
`ToolContractVersionLiterals_ShouldMatchTheBundledArchiveVersion` (this tool's description names 1.5.0.0).
`ExpectedOperationContractCount` stays green — it counts `[OperationContract]` inside the ARCHIVE, still five.

**Why the floor is a version literal and not presence-only.** Its siblings name a version because an older
server MISHANDLES a newer input form. This one names a version because the OPERATION does not exist before
1.5.0.0: `ProcessDesignService` is WCF, so an older package answers the POST with a 404 — a transport fault
carrying an HTML error page that names nothing, which reads as clio being broken and never surfaces the
`install-process-builder` remediation. Recorded as a knowledge record
(`docs/knowledge/Command/a-missing-operation-floor-needs-a-version-literal-not-presence.md`) because the same
trap waits for every future operation added to a bundled package.

**The two counted pins.** The surfaces dictionary MOVED — the tool's description is now pinned there, which
is what makes its 1.5.0.0 literal unable to drift (and is one of the two reds). `ProcessDesignerGoLiveTests`'
`GoLiveToolTypes` was explicitly RULED OUT: that set records what shipped at the ENG-96132 go-live and its
own `[Description]` counts five members, so adding a tool shipping now would both falsify the count and
backdate a GA claim. The substance it guards — no `[FeatureToggle]` on the type — is pinned instead by
`ModifyProcessAsNewVersion_ShouldNotBeFeatureGated` in this tool's own fixture.

**`McpCoreToolProfile` decision: NOT resident.** No process-designer tool is in `CoreToolTypes`; they are
long-tail, discoverable through `get-tool-contract` and reachable through `clio-run`. Making this one
resident would put it in every session's `tools/list` payload while its five siblings stay out — an
inconsistency with a token cost and no benefit. `PassthroughToolClassificationRegistry` row added as
`NotApplicable`: `BaseTool<T>.InternalExecute<TCommand>`, class (a), already resolver-backed. Not added to
`DurableInvocationGateCompletenessTests`' reviewed-silently-executable list, as the story says — it mutates.

**Design points worth review.** (1) An omitted `operations` array is ACCEPTED here and sent as `[]`, where
`modify-business-process` refuses the same input — a version with no edits is a plain snapshot, which is how
an agent takes a restore point, and copying the sibling's refusal would remove that gesture. (2)
`Destructive=false` is a substantive claim, not a formality: nothing existing is opened, saved or activated,
so a consent-gated caller must not be stopped by it. (3) The command says on EVERY success that the source is
still the actual one — not only when surprising — because an agent that assumes the edit is live stops one
step early. (4) A failure that still names a version appends that the version EXISTS and cannot be deleted;
one raised before the save relays the server message unchanged.

**Docs verdict.** `install-process-builder` docs + help and `docs/McpCapabilityMap.md` updated. No CLI verb
exists for this command (MCP-only, exactly like `modify-business-process`), so `clio/help/en/<verb>.txt`,
`clio/docs/commands/<verb>.md`, `clio/Commands.md` and `WikiAnchors.txt` need no entry.

**MCP verdict.** MCP reviewed and updated: new tool, args record, registry row, capability map, install-hint
surfaces. No prompt added — the operations vocabulary is `modify-business-process`'s and its prompt already
covers it; a second prompt restating it would be the drift the surfaces pin exists to end.

**ClioRing compatibility reviewed, no Ring-consumed contract changed.** Inspected
`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing` and `clio-ring/ClioRing.Desktop/actions.json` for
`business-process` / `ProcessDesign` / `list-user-tasks`: no match. Ring consumes no process-designer tool,
and this change is purely additive to the MCP surface (a new tool name; no existing name, argument, flag or
result shape altered).

**AC-05 is not closed here.** Describing the new version as a family member with `isActiveVersion:false` is
asserted in the E2E test, which is gated on a configured sandbox and was not run — the local stand carries
the package but `McpE2E:Sandbox:EnvironmentName` is not configured on this machine. The assertion is written
and runs with story 15's stand verification.
