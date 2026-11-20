# Story 1: Rebundle CrtProcessBuilder with the timer start and raise the floor

**Feature**: eng-91849-timer-start-element
**Jira**: ENG-91849 Timer start element (start by time / schedule) (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-91849-timer-start-element.md](../prd/spec-eng-91849-timer-start-element.md) - R12
**ADR**: [adr-eng-91849-timer-start-element.md](../adr/adr-eng-91849-timer-start-element.md) - D8
**Test plan**: [tp-eng-91849-timer-start-element.md](../test-plans/tp-eng-91849-timer-start-element.md)
**Status**: review
**Repo**: clio (one PR titled `ENG-91849 Timer start element`, branch `feature/ENG-91849-timer-start-element`).
Stories 2-6 ship in the same PR.
**Depends on**: the crt-process-builder PR (the `timerStart` element, `setTimer`, the scheduler read, the message-start
describe fix) merged and cut
**Blocks**: [story 2](story-eng-91849-timer-start-element-2.md), [story 3](story-eng-91849-timer-start-element-3.md),
[story 4](story-eng-91849-timer-start-element-4.md), [story 5](story-eng-91849-timer-start-element-5.md)

---

## As a

clio maintainer shipping the process builder inside clio

## I want

the bundled CrtProcessBuilder archive to carry the timer start, and clio to refuse a stand whose package predates it

## So that

the drift tests read the real timer contract, and an agent on an old stand gets the install hint instead of an
"unknown key" refusal that does not say the package is behind

---

## Acceptance Criteria

- AC1: the archive is produced by `rebundle-process-builder.ps1 -Version <the PR's final cut>` from the merged package;
  the version goes up; the four PROVENANCE pins are refreshed by the script and the hand-maintained pins (the schema
  descriptor stamp `ExpectedSchemaDescriptorModifiedOnUtc`, `ExpectedOperationContractCount`,
  `ExpectedAuthorizationGateCallSites`) are moved by hand when the shipped sources moved them (TC-U-19).
- AC2: the archive declares every timer wire name clio sends or reads (`timerStart`, `timer` and its members,
  `weekday`, `setTimer`, `storedCron`, `designerKind`, `scheduled`) (TC-U-20).
- AC3: create, modify and modify-as-new-version carry `[RequiresPackage]` = the PR's final cut; the floor tests and the
  e2e floor comments name it (TC-U-18).
- AC4: the bundled-packages checklist of `docs/agent-instructions/bundled-packages.md` is followed.
- AC5: the PR merges only after the TeamCity process-designer lane is green on it.

## Definition of Done

- [ ] Package PR merged and cut; clio rebundled from it. A raised version on every cut that left the cutting machine;
  while the number is unreleased and installed nowhere, a same-number re-cut is allowed (1.6.6.187 was re-cut from the
  package's gate-1 restamp `b282c77d`).
- [ ] TC-U-18, TC-U-19 and TC-U-20 green on the rebundled archive.
- [ ] The drift tests of stories 2 and 4 (TC-U-07, TC-U-08) green on the rebundled archive.
- [ ] TeamCity process-designer lane green on the clio PR.
- [ ] A rebundle that comes after a package review fix repeats AC1-AC3 with the new cut (the floor is the final cut).
