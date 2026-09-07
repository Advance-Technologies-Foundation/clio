# Story 15: Rebundle into clio and move the pins

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-08, FR-14
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: in-progress
**Size**: M

---

## As a

developer

## I want

the new package version bundled into clio with its pins refreshed

## So that

an environment that installs from clio gets the operations the tools require, and no pin lies

---

## Acceptance Criteria

- [x] **AC-01** — Given the stamped package, when `rebundle-process-builder.ps1` runs, then the archive, its SHA-256, the archive version and the descriptor stamp are refreshed together
- [x] **AC-02** — Given the refreshed bundle, when the package tests run, then archive version, SHA, descriptor stamp, operation count and gate call sites all agree with the archive
- [x] **AC-03** — Given the freshly bundled version, when `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` runs, then **no declared `[RequiresPackage]` floor exceeds it** — the tool stories that follow declare their floors at or below this version
- [x] **AC-04** — Given a stand recording an equal-or-higher version, when `install-process-builder --force -e <env>` runs, then the install completes and the package service answers
- [x] **AC-ERR** — Given the archive's SHA does not match the pin after the run, when the tests run, then they fail naming the mismatch

## Implementation Notes

Command: `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <crt-process-builder> -Version <the version story 14 stamped>` — the script lives in clio's root.
An install resolves the archive from clio's BUILD OUTPUT — rebuild clio before verifying.
**The archive lands here, the floors land with the tools.** `BundledProcessBuilderPackageTests.cs:633` fails only when a declared floor *exceeds* the bundled version, so this story ships the archive and stories 16 and 17 then declare their floors — which is why they depend on this one and not the other way round.
`BundledPackages.ProcessBuilderVersion` no longer exists — use `ExpectedArchiveVersion`. The stale doc reference in the package repository is story 14's.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | bundled-package pins and the declared-requirement guard | `clio.tests/Common/BundledProcessBuilderPackageTests.cs` |
| Integration `[Category("Integration")]` | install against a stand, then the package service answers | manual, recorded in the PR |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] Both manual pins moved in one commit, expressed as deltas from the baseline at merge time —
  `ExpectedOperationContractCount` 5 → 7 and `ExpectedAuthorizationGateCallSites` 3 → 5, both in
  `337f63644`, matching the two new gated write operations the archive gained
- [x] Depends on story 14 having stamped the package version first — the archive carries the stamped
  version and `ExpectedProducingCommit` names the package repository's HEAD it was cut from
- [x] Code compiles without Roslyn analyzer warnings — no new `CLIO*` or `CS*` warning in any file this
  story added or edited; the two pre-existing `CLIO001` warnings in `MobileDiffApplyValidator` are untouched
- [x] All new tool names and flags are kebab-case — N/A for this story: it ships an archive and its pins,
  and adds no tool name or flag
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`; the fixture carries `Unit`
  deliberately despite reading from disk, and says why
- [x] No `catch (Exception)` added to clio code paths — this story adds no clio code path
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this
  PR** (`AGENTS.md:456-457`) — no record names `clio/CrtProcessBuilder/CrtProcessBuilder.gz` or the pin
  fixture
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the
  verdict — stated below; it moves into the PR body when the branch is pushed
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies) — same:
  stated below, to move into the PR body on push
- [ ] PR description references this story file — branch not pushed, no PR yet

## Dev Agent Record

- Implementation started: 2026-09-06
- Implementation completed: 2026-09-07 (archive re-cut at 1.4.15.1 after the code review)
- Tests passing: full `Category=Unit` suite green; `BundledProcessBuilderPackageTests` green, which is what
  makes the pins meaningful rather than merely present
- Notes:

**Shipped.** `clio/CrtProcessBuilder/CrtProcessBuilder.gz` re-cut through `rebundle-process-builder.ps1`,
and the four provenance pins plus the two security counts moved with it. The archive shipped at 1.4.15.1;
earlier numbers on this branch were superseded before leaving the machine and are burned rather than reused,
for the reason `ExpectedArchiveSha256` states.

**The two security counts are the reviewability.** A `.gz` renders in a diff as a byte count, so
`ExpectedOperationContractCount` (5 → 7) and `ExpectedAuthorizationGateCallSites` (3 → 5) are the only
clio-side statement about what the new bytes CONTAIN. Neither is written by the script, which is now said in
`docs/agent-instructions/bundled-packages.md`'s pin table and in the `AGENTS.md` read-first bullet — the gap
this story is the change that exercised. The gate count is not one per operation: four write handlers plus
one shared read boundary in `ProcessDesigner.Execute` cover six gated operations, with `Ping` allowlisted
ungated. What the count CANNOT catch is a gate moved rather than removed; that property is bound to its
operation by the guard-deny tests in the package repository, which the constant's remarks now enumerate per
gated operation so the two can be checked against each other.

**The two new operations are each gated in their OWN handler, verified in the package checkout, not assumed.**
`packages/CrtProcessBuilder/Files/src/cs/Design/` at `ca8b3f2` carries exactly five live
`_guard.EnsureCanManageProcessDesign()` calls: `ProcessBuildHandler.cs:73`, `ProcessDesigner.cs:153` (the
shared read boundary), `ProcessModifyHandler.cs:64`, `ProcessVersionActivateHandler.cs:77` and
`ProcessVersionSaveHandler.cs:84`. So the count of 5 is not two gates landing on a path the new operations
never take — each new write operation has its own. Both also carry a deny test asserting the operation fails
AND that the schema repository received no calls at all
(`ProcessVersionSaveHandlerTests.ModifyProcessAsNewVersion_ShouldRefuseBeforeAnyRepositoryCall_WhenTheCallerLacksTheOperation`,
`ProcessVersionActivateHandlerTests.SetActiveProcessVersion_ShouldRefuseBeforeAnyRepositoryCall_WhenTheCallerLacksTheOperation`),
so an unauthorized caller cannot even learn whether the process exists. They are named under the package
repository's `ShouldRefuseBeforeAnyRepositoryCall` convention rather than the older
`ShouldFailAndNotMutate` one.

**Docs verdict.** Docs reviewed and updated: `docs/agent-instructions/bundled-packages.md` gains the three
hand-maintained pins in its table, with which side of the cross-repo boundary moves first; `AGENTS.md`'s
read-first bullet names the two security counts. No command docs are affected — this story adds no verb and
changes no command behaviour.

**MCP verdict.** MCP reviewed, no update required — this story ships an archive and its pins; no tool name,
argument, flag, description, destructive classification, result content or error envelope changed.

**ClioRing compatibility reviewed, no Ring-consumed contract changed.** Inspected
`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing` and `clio-ring/ClioRing.Desktop/actions.json`: no
process-designer tool call and no `clio-run` nested command reaching one. The bundled archive is not part of
any Ring-consumed contract.

**Not re-verified on a stand after the re-cut.** AC-04's install was run against a stand on the earlier cut;
the 1.4.15.1 archive has not been installed. The pins prove the bytes and the inventory, not the install.
