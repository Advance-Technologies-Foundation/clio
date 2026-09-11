# Story 23: Rebundle CrtProcessBuilder once the create-path fix is on main

**Feature**: process-versioning
**Jira**: ENG-94374
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: S

---

## As a

no-code builder on an environment installed from clio's bundled archive

## I want

the create-path fix to reach my environment

## So that

a refused `create-business-process` stops telling me to delete a schema that does not exist

---

## Blocked by

Story 22 being merged into `crt-process-builder`'s `main`. The archive is cut from a **commit**, not
from a working tree, and clio's provenance pin names that commit — so cutting from the feature branch
would pin clio to a commit no permanent ref holds.

## Acceptance Criteria

- [ ] **AC-01** — Given story 22 is on `main` and tagged, when `rebundle-process-builder.ps1` is run with the next version, then the archive, the SHA pin, the version pin, the stamp pin and the producing-commit pin all move together
- [ ] **AC-02** — Given the rebundle, when `BundledProcessBuilderPackageTests` runs, then it is green — including the two counts the script does NOT write (`ExpectedOperationContractCount`, `ExpectedAuthorizationGateCallSites`), which must be re-verified by hand against the new bytes
- [ ] **AC-03** — Given the `[RequiresPackage]` literals, when they are reviewed, then they still read `1.6.1.0` — the two operations first exist there, and raising the floor would refuse environments that can already run them
- [ ] **AC-04** — Given the process-versions article, when the create-path paragraph is re-read, then it names the shipped fix version only once that version exists

## Implementation Notes

Read `docs/agent-instructions/bundled-packages.md` first. The normal path is one call:
`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version X.Y.Z.W`. `-Version` must go
UP — clio compares the shipped version against the one the environment recorded, so an unchanged
number reaches new installs only.

Raise it deliberately: `RequiredPackageChecker` throws on a convergence refusal, and a rebundle
during ENG-91853 left a reviewer's clio refusing `describe-business-process` against a stand that had
not been updated.
