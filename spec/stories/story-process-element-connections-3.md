# Story 3: Write side — `setConnections` / `clearConnections`, guards, three-state diagnosis

**Feature**: process-element-connections
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md)
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Decisions**: D1, D1a, D3, D6, D7
**Status**: done
**Size**: L
**Repo**: `ProcessBuilder` — `packages/CrtProcessBuilder/Files/src/cs/`
**Depends on**: story 1 (ships together with story 2 — see D1)

---

## As a

coding agent building a process

## I want

to bind the Activity an element creates to real records, and to be refused with a reason whenever the
binding would not take effect

## So that

I never ship a process that persists, compiles, runs green and writes nothing

---

## Acceptance Criteria

### Contract

- [x] **AC-01** — Two `modify` operations: `setConnections` and `clearConnections`. Each item is
  `{ column, <exactly one source> }`; more than one source, or none, is a validation error naming the
  column.
- [x] **AC-02 (D1a)** — `setConnections` is an **upsert keyed on `column`**. Columns in the request are set
  or re-set; columns **absent are left untouched**. There is no collection-replace behaviour and no
  implicit clearing. A test must prove that setting `Account` alone leaves an existing `Contact` binding
  intact.
- [x] **AC-03** — Sources: `recordId` (+ optional `referenceSchema`), `processParameter`,
  `sourceElement` + `sourceElementParameter`, `expression`.
- [x] **AC-04** — For `recordId`, the macro is **synthesised by the package** from the target parameter's
  own `ReferenceSchemaUId` — the caller never supplies an entity-schema UId. (That requirement is stated in
  the shipped guidance itself: *"You cannot guess these ids"*.)
- [x] **AC-05** — Changing an existing connection, including across dialects, is the same call with a new
  source. No second parameter is created (find-or-reuse, T-1) and no duplicate mapping accumulates — the
  platform's `SourceValue` setter replaces by `FindByTargetUId`. Re-sending an unchanged request is
  idempotent.
- [x] **AC-06 (D1b)** — `clearConnections` **unbinds**: it sets `Source = None` and leaves the element
  parameter in place. It must **not** delete the parameter — for a static connection the platform owns it
  (`SynchronizeParameters` would re-create it) and for any parameter another mapping may target it
  (`SourceValue` resolves schema-wide via `FindByTargetUId`, so removal can leave a dangling target).
- [x] **AC-06a** — Its result **states that a binding was cleared**, because afterwards `describe` filters
  the connection out and "cleared" is otherwise indistinguishable from "never bound".
- [x] **AC-06b** — Clearing an already-unbound column is an **idempotent no-op, not an error**.
- [x] **AC-06c** — After clearing, the column is **not written at runtime**. Pin this with a test rather
  than assuming it: the value carried `ModifiedInSchemaUId` stamped to the process schema and the codegen
  `isOverride` condition keys on exactly that field, so the generated property may still be emitted. Either
  outcome is acceptable as long as nothing is written — channel A already skips an empty Guid before
  `SetColumnValue`.

### Write shape

- [x] **AC-07** — The persisted value is always `Source = Script (3)` plus a macro — the encoding used by
  every designer-authored sample. No other `ProcessSchemaParameterValueSource` is written for a connection.
- [x] **AC-08** — `ModifiedInSchemaUId` is stamped to the process schema UId, which is what makes the
  codegen `isOverride` condition (`ProcessSchemaGeneratorNew.cs:611-617`) emit the property. A test pins
  this: without the stamp the value compiles away silently.
- [x] **AC-09** — A created (dynamic) parameter copies `DataValueTypeUId` and `ReferenceSchemaUId` from the
  column **verbatim**, and the applier **asserts they are non-empty**. This removes the reverse-sync NRE
  by construction rather than by observation.
- [x] **AC-10** — Element-parameter UIds are fresh GUIDs, **not** the column UId (T-3).
- [x] **AC-11** — The `expression` escape hatch is **type-checked against the macro family** for the
  target column type: the four typed-constant families that provably cannot hold a record reference are
  REFUSED. A `[#SysSettings...#]` expression is **accepted with a warning**, not refused — its value type
  cannot be read at design time, so the hole the analysis named (a text setting bound to a lookup connection)
  is made LOUD rather than closed; see deviation 1 and the ADR's residual note. (Today `BuildSourceValue`
  stores `expression` verbatim with no validation — that is the hole this closes.)

### Guards — refuse, never ignore

- [x] **AC-12** — User task not allow-listed → refused, naming the schema.
- [x] **AC-13** — Column not in *registry ∪ element parameters*, validated **per element** → refused.
- [x] **AC-14 (D3)** — `ConnectionCapability`'s effectiveness rule fails → refused. The message names the
  fix in the caller's own vocabulary and makes clear it costs one array element rather than another call,
  e.g. *"connections on `Task1` would not take effect: `CreateActivity` is false. Prepend
  `{"op":"setParameter","parameterName":"CreateActivity","parameterUpdate":{"value":"true"}}` to this
  operations array."*
- [x] **AC-15** — Manual-send `EmailTemplateUserTask` with `CreateActivity == false` is **allowed** — the
  manual path has no gate, so refusing it is a false positive on a legitimate configuration.
- [x] **AC-16** — `ActivityUserTask` never triggers the effectiveness guard (no such parameter exists).
- [x] **AC-17** — The two deprecation guards of §3.6 refuse with **distinct** messages: a
  product-deprecated element and a mechanically-incapable one are different diagnoses.
- [x] **AC-18 (D6/D7)** — Exactly one authorization gate, `CanManageProcessDesign`, unconditional. No
  privilege check depends on request content, and no partial application is possible.
- [x] **AC-19** — Three-state diagnosis, each state distinguishable in the response: (1) no host column for
  that entity → refuse, naming the data-model step; (2) column exists, **no registry row** → the value
  would be written at runtime but the connection is invisible in the designer, absent from the record
  page's detail, and ignored by Next Steps, email auto-relations and quick-add → warn explicitly (state
  (2) also applies when *re-setting* an already-bound-but-unregistered connection); (3) both present →
  bind.
- [x] **AC-20** — No guard or refusal from this story reaches `describe`.

## Implementation Notes

Files: `Connections/EntityConnectionBinder.cs` (the **shared** read/write kernel used by both build and
modify — mirror `SignalTriggerBinder`'s role so the two paths cannot drift), plus the two operation
strategies and their registration in the `op` dispatch.

Reuse, do not reimplement: `ProcessMappingService.BuildSourceValue` already stamps
`ModifiedInSchemaUId = schema.UId` and already routes the four dialects;
`ParameterTypeCompatibility` already exempts Lookup-target + Guid-source, which is why a `Guid` process
parameter binds to an `Account` connection without a cast (measured).

Do **not** create an `Activity` column here (D6) — refuse with state (1) and name the registration recipe.

## Deviations recorded at implementation time

1. **AC-11 is partly a warning rather than a refusal.** The four typed-constant macro families that provably
   cannot hold a record reference (date, date-time, time, boolean) ARE refused. A **system setting** is
   accepted with an explicit warning instead: its value type is not knowable without reading the setting, and
   refusing it would break story 2's AC-08 round-trip for a dialect designer-authored processes legitimately
   contain. The hole the analysis named — a text setting bound to a lookup connection persisting silently — is
   therefore made loud, not closed. Closing it needs a `SysSettings` value-type read; the ticket also puts
   system settings out of scope as a *supported* source, so this is not advertised.
2. **`clearConnections` is deliberately NOT gated on the effectiveness rule.** AC-14 constrains
   `setConnections`; unbinding can only reduce what is written, and a maintainer cleaning up a legacy element
   whose connections could never fire is exactly who needs it.
3. **AC-06c is pinned at the unit layer as `Source = None` plus an empty value**, which is what stops the
   column being written. A true "not written at run time" assertion needs a real run — story 4's E2E.
4. **A new response member and a new collaborator were needed for AC-06a/AC-19.** An operation strategy
   applies and returns nothing, so there was no channel for an outcome that succeeded with a caveat. Added a
   scoped notice collector plus `warnings` on the modify response, attached only to a SAVED edit. Both
   additions are additive on the wire.
5. **AC-17's "two deprecation guards" is realised as two distinct STATUSES, not two guards.** D9 forbids the
   deprecation predicate from driving a connections refusal, so there is no product-deprecation guard at all: all
   three retired schemas are refused through the capability's mechanism table, each with its own reason, and the
   distinguishable diagnoses are `MechanismUnsupported` (per-schema facts) vs `NotAllowListed` vs `NotEffective`.
   The substance AC-17 asks for — a caller can tell the diagnoses apart — is delivered and now asserted; the guard
   it literally describes would contradict the ADR.

6. **Finding that changed a guard's meaning:** an end event IS a parametrized flow node, so "the element
   carries no parameters" is nearly unreachable and is NOT what protects a non-user-task element — the
   capability verdict is. The guard stays as a cheap backstop; the test pins the real protection.

## Definition of Done

- [x] All AC met; AAA, `because` on every assertion, `[Description]` on every test method
- [x] Cross-platform tests; behaviour classes interfaced and DI-registered
- [x] Verification matrix rows in plan §8 covered at the UNIT layer, including the two Send-email rows
  (automatic → refused, manual → allowed). Four §8 rows need a real run or a designer, not two; of those, story 4's stand check
  closed "written at run time" (for a STATIC column). The created-parameter tail at task completion is NOT
  reachable from story 4's E2E — none of those cases runs a process, and `ResolveColumn` refuses a column that
  is neither registered nor element-declared, so the created path needs a registered-but-undeclared column.
  Carried to story 5 AC-08, which is where such a column can first be produced. One §8 row is NOT covered at
  any layer and is not merely deferred: "describe output fed into build → refused on policy grounds" needs the
  deprecation refusal that was never implemented (see story 1 AC-08 and the plan's D9 entry)
- [x] No new `CLIO*` diagnostics in touched files (`CLIO*` analyzers run in the clio repo only; the
  `ProcessBuilder` build is warning-free)
- [x] Diary entry appended
