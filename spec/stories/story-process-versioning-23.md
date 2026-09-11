# Story 23: Rebundle CrtProcessBuilder at 1.6.2.3 with the create-path fix

**Feature**: process-versioning
**Jira**: ENG-94374
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: review
**Size**: S

---

## As a

no-code builder on an environment installed from clio's bundled archive

## I want

the create-path fix to reach my environment

## So that

a refused `create-business-process` stops telling me to delete a schema that does not exist

---

## Why it was safe to cut before the merge

The archive is cut from a **commit**, and clio's provenance pin names it. Cutting from a feature
branch is only safe when that commit survives the merge — which it does here, because the package
branch is merged with a MERGE commit rather than squashed or rebased. The producing commit
`d0af0fc` is also tagged `crtprocessbuilder-1.6.2.3`, so it stays reachable regardless.

## Acceptance Criteria

- [x] **AC-01** — Given the producing commit is tagged, when `rebundle-process-builder.ps1` is run with the next version, then the archive, the SHA pin, the version pin, the stamp pin and the producing-commit pin all move together
- [x] **AC-02** — Given the rebundle, when `BundledProcessBuilderPackageTests` runs, then it is green — including the two counts the script does NOT write, which were re-verified against the new bytes and did not move (7 operations, 5 gate call sites): the fix touches a handler's rollback gate, not the service surface
- [x] **AC-03** — Given the `[RequiresPackage]` literals, when they are reviewed, then they still read `1.6.1.0` — the two operations first exist there, and raising the floor would refuse environments that can already run them
- [x] **AC-04** — Given the version to cut, when it is chosen, then it is above EVERY existing cut and not merely above `main`: `1.6.2.3`, because unmerged `ENG-95986` already holds 1.6.2.1 and 1.6.2.2 and a lower bundled version is refused rather than ignored
- [ ] **AC-05** — Given the create-path paragraph in `process-version-writes`, when the archive has shipped, then it names 1.6.2.3 as the version that fixes it

## Implementation Notes

Read `docs/agent-instructions/bundled-packages.md` first. The normal path is one call:
`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version X.Y.Z.W`. `-Version` must go
UP — clio compares the shipped version against the one the environment recorded, so an unchanged
number reaches new installs only.

Raise it deliberately: `RequiredPackageChecker` throws on a convergence refusal, and a rebundle
during ENG-91853 left a reviewer's clio refusing `describe-business-process` against a stand that had
not been updated.
