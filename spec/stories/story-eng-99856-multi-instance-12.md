# Story 12: Ship the package — rebundle, four provenance pins, two hand-moved security counts, three floors

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-23, FR-24 (not triggered under the working assumption)
**AC coverage**: AC-21 (its package half)
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md) — *Delivery cost*
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — *Sequencing*, *Dependencies*
**Jira**: ENG-99856
**Status**: review
**Size**: M (half day — the script does most of it; the cost is the two counts it does **not** write, the
version choice, and the floor raises)
**Repo**: clio (+ a tag in crt-process-builder)
**Depends on**: stories 3, 4, 5, 6, 7, 8, 9 — **all package behaviour must be in** before the archive is
cut — and story 10 under Outcome 2 (a new wire member after the cut means a second bump)
**Blocks**: story 13

---

## This is the delivery tax, costed as work

The contract is one member. The delivery is not. This story is the part of it that moves bytes and
version numbers, and every item in it has a **silent** failure mode: an unchanged version reaches new
installs only; a stale provenance pin points a reviewer at bytes that are not shipped; a security count
left at the old number passes a test that was supposed to notice a widened service surface.

## As a

release engineer

## I want

the rebundled CrtProcessBuilder archive, its pins, its two hand-maintained counts and the three package
floors moved together, in one commit

## So that

an environment that already has the package is actually asked to update, and a reviewer can prove which
sources produced the archive clio ships

---

## Owner decision this story assumes

**OQ-03 — `inSync` frozen** (see story 9). Under that assumption **no floor is added to
`DescribeProcessCommand`**, which today carries only an unversioned presence gate
(`clio/Command/DescribeProcessCommand.cs:17-18`).
*If the owner redefines `inSync`*, FR-24 activates and this story raises a **fourth** floor — on describe
— because a silent meaning change on a shipped field is invisible to every deserializer, schema check and
version negotiation.

## Acceptance Criteria

- [ ] **AC-01** (FR-23) — Given the rebundle, when it runs, then the package version goes **UP**. A reused
  version reaches new installs only; nobody who already has the package is ever asked to update. Choose
  the number deliberately: it must be free in **both** histories (clio's bundled line and the package
  repo's) and not above the global maximum in flight elsewhere.
- [ ] **AC-02** (FR-23) — Given `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version X.Y.Z.W`,
  when it completes, then all **four** provenance pins are refreshed: the SHA computed from the archive it
  just produced, the version from `-Version`, the `ModifiedOnUtc` stamp from the package descriptor after
  the restamp, and the producing commit from that repository's HEAD before it.
- [ ] **AC-03** (FR-23) — Given the two counts the script does **not** write —
  `ExpectedOperationContractCount` and `ExpectedAuthorizationGateCallSites` in
  `clio.tests/Common/BundledProcessBuilderPackageTests.cs` — when the service surface changed, then they
  are moved **by hand in the same commit**, and the PR states what changed on the package side to justify
  each movement. If neither moved, the PR says so explicitly rather than leaving it unsaid.
- [ ] **AC-04** (FR-23) — Given the three `[RequiresPackage]` floors on `CreateBusinessProcessCommand`,
  `ModifyBusinessProcessCommand` and `ModifyProcessAsNewVersionCommand`, when they are raised, then each
  reads the rebundled version. Verify **before editing** that all three currently read `1.6.2.1` — this
  worktree is level with `origin/master`, so A-07's rebase precondition is satisfied, but a floor raise
  authored against a stale literal is silently lost in a merge.
- [ ] **AC-05** — Given an environment below the new floor, when a gated command runs, then it is refused
  with a message naming the version. Confirm the refusal path still reads well after the raise; this is
  the only user-visible consequence of the change and the PRD classifies it as non-breaking on that basis.
- [ ] **AC-06** — Given `clio.tests/Common/BundledProcessBuilderPackageTests.cs`, when the suite runs,
  then the SHA-256 and `ModifiedOnUtc` pins match the committed archive and the inventory check passes.
- [ ] **AC-07** — Given the package descriptor, when it is restamped, then **both** `PackageVersion` and
  `ModifiedOnUtc` move. The descriptor's `ModifiedOnUtc` — not `PackageVersion` — decides whether the
  recorded version is rewritten at all; `clio set-pkg-version` does both in one step and leaves the
  provenance marker the pins expect.
- [ ] **AC-08** — Given the package `UId`, when the rebundle completes, then it is **unchanged**. A
  package is matched by `UId`; changing it is a silent failure that installs a second package.
- [ ] **AC-09** — Given `RELEASE.md`, when it is updated, then it carries the new floors and the rebundled
  version.
- [ ] **AC-10** (AC-21) — Given the PR description, when it is reviewed, then it states the rebundled
  version, its four provenance pins, the two hand-moved security counts (with justification or an explicit
  "unchanged"), and the new floors.
- [ ] **AC-ERR** — Given a rebundle whose inventory check fails or whose archive SHA does not match the
  pin, when the suite runs, then it fails loudly; do not re-pin to make it pass without establishing why
  the bytes changed.

## Implementation Notes

Read [docs/agent-instructions/bundled-packages.md](../../docs/agent-instructions/bundled-packages.md)
**before** touching any of: `clio/CrtProcessBuilder/*.gz`, `clio/Common/BundledPackages.cs`,
`clio/Common/BundledPackageCatalog.cs` / `BundledPackageConvergence.cs`,
`clio.tests/Common/BundledProcessBuilderPackageTests.cs`, or a `[RequiresPackage]` literal. It carries the
procedure and the three platform facts whose failure modes are silent.

**Do not reintroduce a version constant** — see
[spec/adr/adr-bundled-package-version-source-of-truth.md](../adr/adr-bundled-package-version-source-of-truth.md).

**Raise the version deliberately, not casually.** `RequiredPackageChecker` throws on a convergence
refusal: a rebundle during ENG-91853 left a reviewer's clio refusing `describe-business-process` against
a stand one archive behind. The convergence refusal fires on **any** environment recording a package older
than the one clio ships — floor or no floor — so the floors are not the only thing that gates an
un-upgraded stand.

Build and test CrtProcessBuilder in the **main checkout, not a git worktree**: the untracked core-bin tree
is absent there and the workaround yields ~1355 spurious assembly-resolution failures that read as a code
regression.

Because this story touches `clio/Common/`, the smart-regression rule 4 trigger applies: run the **full**
unit suite, not a module filter.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (clio) | SHA-256 / version / `ModifiedOnUtc` / producing-commit pins; the two security counts; archive inventory | `clio.tests/Common/BundledProcessBuilderPackageTests.cs` |
| Unit `[Category("Unit")]` (clio) | each raised `[RequiresPackage]` floor is read as expected by the command fixtures (`BaseCommandTests<TOptions>` where a fixture exists) | `clio.tests/Command/` |
| Manual | `list-packages -e <env>` shows the new version after `push-pkg`/install | recorded in the PR |

Test naming: `BundledArchive_ShouldMatchExpectedSha256_WhenRebundled`.

## Definition of Done

- [ ] Version went up and is free in both histories; the choice is justified in one sentence
- [ ] Four provenance pins refreshed by the script; two security counts moved by hand or explicitly
      declared unchanged, in the same commit
- [ ] Three floors raised from the verified `1.6.2.1`; the describe floor **not** added (OQ-03 frozen)
- [ ] Package `UId` unchanged; both `PackageVersion` and `ModifiedOnUtc` moved
- [ ] `RELEASE.md` updated
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `[Category("Unit")]`
- [ ] No new `CLIO*` diagnostics in touched files
- [ ] Validated locally: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` — the **full**
      suite, because `clio/Common/` changed (smart-regression rule 4)
- [ ] PR description references this story file and carries the AC-10 statement verbatim

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Rebundled version:
- Provenance pins / security counts:
- Tests passing:
- Notes:
