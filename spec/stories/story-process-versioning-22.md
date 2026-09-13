# Story 22: Stop the create path warning about a schema it never created

**Feature**: process-versioning
**Jira**: ENG-94374
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: review
**Size**: S

---

## As a

no-code builder whose `create-business-process` call was refused

## I want

the refusal to tell me only why it was refused

## So that

I do not go looking for a half-made process that was never created

---

## Source

Manual testing of this story, 2026-09-11, on CrtProcessBuilder 1.6.1.9. Any descriptor-validation
failure of `create-business-process` answered with *"the draft could NOT be rolled back — a partially
created process schema 'UsrBPEng94374Base' may still exist in the package and has to be deleted
manually before the name can be reused."* `SysSchema` after that failure: count 0.

`IProcessSchemaRepository.Rollback` already documents, as a load-bearing precondition, that it may
only be called once a save has been ATTEMPTED: the draft is created with `addToDesignItems: false`,
so `RemoveItemByUId` resolves through `AllItems.FindByRealUId` → null and the platform's `RemoveItem`
dereferences it. On a never-saved item the rollback therefore cannot succeed — it catches, warns and
answers `false` every time. `ProcessVersionSaveHandler` gates on `saveAttempted` for exactly this
reason (1.6.1.1); `ProcessBuildHandler` never did.

## Acceptance Criteria

- [x] **AC-01** — Given a build that fails BEFORE the save, when the response is read, then no rollback was attempted and the message carries the refusal alone
- [x] **AC-02** — Given that response, when it is read, then it contains neither "may still exist" nor an instruction to delete anything
- [x] **AC-03** — Given a build that fails at or after the save attempt, when the response is read, then the rollback is still attempted and its outcome still reported
- [x] **AC-04** — Given the reported scenario (a duplicate element name in the descriptor), when the round-trip test runs, then it asserts both AC-01 and AC-02

## Verification

Measured on a live stand (`creatio_2`, CrtProcessBuilder **1.6.2.3** cut from this branch, 2026-09-11),
over the real MCP path. `create-business-process` with a valid graph whose conditional flow names a
parameter the process does not declare:

```
The condition on the flow from 'Choose' to 'EndA' references 'Amont', which is not a parameter of
this process. This process declares no parameters; add one in 'parameters[]', ...
```

The reason alone — no "may still exist", no instruction to delete anything. Two things make this the
right path rather than a lucky one: the message is raised in `ConditionParameterNames`, whose text
exists ONLY in the package, so the call reached the server handler instead of being turned away by
clio's own `ProcessGraphValidator`; and it is raised after `CreateSchemaDraft` and before the save,
which is exactly the window the old code reported a failed cleanup for.

An ESQ over `SysSchema` filtered on the probe's name answers `count: 0` afterwards — so the sentence
the old build appended was false about a schema that never existed.

The OLD behaviour was not re-run on the stand: that means installing 1.6.1.9 back over a newer
package, which `install-process-builder` refuses without `--force`, for a result the unit suite already
pins. Before is code plus tests; after is a stand.

## Implementation Notes

The wording of `DescribeRollback` is untouched: it was never wrong, it was being asked a question it
could not be asked.

**Restamped to 1.6.2.3 and cut**, once it was settled that the branch merges with a MERGE commit
rather than a squash: the producing commit `d0af0fc` therefore stays reachable, which is the only
thing that made cutting before the merge safe. Tagged `crtprocessbuilder-1.6.2.3`. Story 23 carries
the clio side.
