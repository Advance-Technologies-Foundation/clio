# Story 15: Rebundle into clio and move the pins

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-08, FR-14
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
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

- [ ] **AC-01** — Given the stamped package, when `rebundle-process-builder.ps1` runs, then the archive, its SHA-256, the archive version and the descriptor stamp are refreshed together
- [ ] **AC-02** — Given the refreshed bundle, when the package tests run, then archive version, SHA, descriptor stamp, operation count and gate call sites all agree with the archive
- [ ] **AC-03** — Given the freshly bundled version, when `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` runs, then **no declared `[RequiresPackage]` floor exceeds it** — the tool stories that follow declare their floors at or below this version
- [ ] **AC-04** — Given a stand recording an equal-or-higher version, when `install-process-builder --force -e <env>` runs, then the install completes and the package service answers
- [ ] **AC-ERR** — Given the archive's SHA does not match the pin after the run, when the tests run, then they fail naming the mismatch

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

- [ ] Both manual pins moved in one commit, expressed as deltas from the baseline at merge time
- [ ] Depends on story 14 having stamped the package version first
- [ ] Code compiles without Roslyn analyzer warnings
- [ ] All new tool names and flags are kebab-case
- [ ] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] No `catch (Exception)` added to clio code paths
- [ ] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [ ] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [ ] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
