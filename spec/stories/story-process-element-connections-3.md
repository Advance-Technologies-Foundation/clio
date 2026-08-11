# Story 3: Write side — `setConnections` / `clearConnections`, guards, three-state diagnosis

**Feature**: process-element-connections
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md)
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Decisions**: D1, D1a, D3, D6, D7
**Status**: ready-for-dev
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

- [ ] **AC-01** — Two `modify` operations: `setConnections` and `clearConnections`. Each item is
  `{ column, <exactly one source> }`; more than one source, or none, is a validation error naming the
  column.
- [ ] **AC-02 (D1a)** — `setConnections` is an **upsert keyed on `column`**. Columns in the request are set
  or re-set; columns **absent are left untouched**. There is no collection-replace behaviour and no
  implicit clearing. A test must prove that setting `Account` alone leaves an existing `Contact` binding
  intact.
- [ ] **AC-03** — Sources: `recordId` (+ optional `referenceSchema`), `processParameter`,
  `sourceElement` + `sourceElementParameter`, `expression`.
- [ ] **AC-04** — For `recordId`, the macro is **synthesised by the package** from the target parameter's
  own `ReferenceSchemaUId` — the caller never supplies an entity-schema UId. (That requirement is stated in
  the shipped guidance itself: *"You cannot guess these ids"*.)
- [ ] **AC-05** — Changing an existing connection, including across dialects, is the same call with a new
  source. No second parameter is created (find-or-reuse, T-1) and no duplicate mapping accumulates — the
  platform's `SourceValue` setter replaces by `FindByTargetUId`. Re-sending an unchanged request is
  idempotent.
- [ ] **AC-06 (D1b)** — `clearConnections` **unbinds**: it sets `Source = None` and leaves the element
  parameter in place. It must **not** delete the parameter — for a static connection the platform owns it
  (`SynchronizeParameters` would re-create it) and for any parameter another mapping may target it
  (`SourceValue` resolves schema-wide via `FindByTargetUId`, so removal can leave a dangling target).
- [ ] **AC-06a** — Its result **states that a binding was cleared**, because afterwards `describe` filters
  the connection out and "cleared" is otherwise indistinguishable from "never bound".
- [ ] **AC-06b** — Clearing an already-unbound column is an **idempotent no-op, not an error**.
- [ ] **AC-06c** — After clearing, the column is **not written at runtime**. Pin this with a test rather
  than assuming it: the value carried `ModifiedInSchemaUId` stamped to the process schema and the codegen
  `isOverride` condition keys on exactly that field, so the generated property may still be emitted. Either
  outcome is acceptable as long as nothing is written — channel A already skips an empty Guid before
  `SetColumnValue`.

### Write shape

- [ ] **AC-07** — The persisted value is always `Source = Script (3)` plus a macro — the encoding used by
  every designer-authored sample. No other `ProcessSchemaParameterValueSource` is written for a connection.
- [ ] **AC-08** — `ModifiedInSchemaUId` is stamped to the process schema UId, which is what makes the
  codegen `isOverride` condition (`ProcessSchemaGeneratorNew.cs:611-617`) emit the property. A test pins
  this: without the stamp the value compiles away silently.
- [ ] **AC-09** — A created (dynamic) parameter copies `DataValueTypeUId` and `ReferenceSchemaUId` from the
  column **verbatim**, and the applier **asserts they are non-empty**. This removes the reverse-sync NRE
  by construction rather than by observation.
- [ ] **AC-10** — Element-parameter UIds are fresh GUIDs, **not** the column UId (T-3).
- [ ] **AC-11** — The `expression` escape hatch is **type-checked against the macro family** for the
  target column type. A text system setting bound to a Lookup connection is refused. (Today
  `BuildSourceValue` stores `expression` verbatim with no validation — that is the hole this closes.)

### Guards — refuse, never ignore

- [ ] **AC-12** — User task not allow-listed → refused, naming the schema.
- [ ] **AC-13** — Column not in *registry ∪ element parameters*, validated **per element** → refused.
- [ ] **AC-14 (D3)** — `ConnectionCapability`'s effectiveness rule fails → refused. The message names the
  fix in the caller's own vocabulary and makes clear it costs one array element rather than another call,
  e.g. *"connections on `Task1` would not take effect: `CreateActivity` is false. Prepend
  `{"op":"setParameter","parameterName":"CreateActivity","parameterUpdate":{"value":"true"}}` to this
  operations array."*
- [ ] **AC-15** — Manual-send `EmailTemplateUserTask` with `CreateActivity == false` is **allowed** — the
  manual path has no gate, so refusing it is a false positive on a legitimate configuration.
- [ ] **AC-16** — `ActivityUserTask` never triggers the effectiveness guard (no such parameter exists).
- [ ] **AC-17** — The two deprecation guards of §3.6 refuse with **distinct** messages: a
  product-deprecated element and a mechanically-incapable one are different diagnoses.
- [ ] **AC-18 (D6/D7)** — Exactly one authorization gate, `CanManageProcessDesign`, unconditional. No
  privilege check depends on request content, and no partial application is possible.
- [ ] **AC-19** — Three-state diagnosis, each state distinguishable in the response: (1) no host column for
  that entity → refuse, naming the data-model step; (2) column exists, **no registry row** → the value
  would be written at runtime but the connection is invisible in the designer, absent from the record
  page's detail, and ignored by Next Steps, email auto-relations and quick-add → warn explicitly (state
  (2) also applies when *re-setting* an already-bound-but-unregistered connection); (3) both present →
  bind.
- [ ] **AC-20** — No guard or refusal from this story reaches `describe`.

## Implementation Notes

Files: `Connections/EntityConnectionBinder.cs` (the **shared** read/write kernel used by both build and
modify — mirror `SignalTriggerBinder`'s role so the two paths cannot drift), plus the two operation
strategies and their registration in the `op` dispatch.

Reuse, do not reimplement: `ProcessMappingService.BuildSourceValue` already stamps
`ModifiedInSchemaUId = schema.UId` and already routes the four dialects;
`ParameterTypeCompatibility` already exempts Lookup-target + Guid-source, which is why a `Guid` process
parameter binds to an `Account` connection without a cast (measured).

Do **not** create an `Activity` column here (D6) — refuse with state (1) and name the registration recipe.

## Definition of Done

- [ ] All AC met; AAA, `because` on every assertion, `[Description]` on every test method
- [ ] Cross-platform tests; behaviour classes interfaced and DI-registered
- [ ] Verification matrix rows in plan §8 covered, including the two Send-email rows (automatic → refused,
  manual → allowed)
- [ ] No new `CLIO*` diagnostics in touched files
- [ ] Diary entry appended
