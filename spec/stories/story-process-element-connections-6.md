# Story 6: `SysVariable` connections — the write-time refusal, its rebundle and the E2E that can only follow it

**Feature**: process-element-connections
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md)
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Decisions**: extends D3 (the value contract) — no new decision taken
**Status**: ready-for-dev
**Size**: S
**Repo**: `ProcessBuilder` — `packages/CrtProcessBuilder/Files/src/cs/Connections/`, then `clio`
**Depends on**: story 3 (the write kernel this refuses inside)

---

## As a

coding agent linking an Activity to the current user

## I want

a system-variable name I got wrong to be refused at the write, with the usable ones named

## So that

a successful write MEANS the binding resolves — instead of leaving me to verify it myself, or to hedge

---

## Why this exists

Found by reading a real session rather than by planning. The agent bound
`[#SysVariable.CurrentUserAccount#]`, could not establish that the name was real (it is), spent two thirds
of the task failing to verify it, and then delivered a caveat that was wrong. Its own reasoning named the
cause: the builder accepts any `[#...#]`-shaped token, so acceptance proved nothing.

This family is checkable, unlike the `[#SysSettings...#]` residual recorded in the ADR. The vocabulary is
nine values hardcoded in `Terrasoft.Core/SystemValue.cs`, and `SystemValueManager` answers at design time —
the same call the designer's own picker uses. It is also the family where NOT checking is worst:
`GeneratorUtilities.ConvertToCodeSysVariableMacros` resolves the name during code generation with
`GetInstanceByName`, which throws, so an unknown name does not write an empty column — it stops the whole
process compiling, far from the edit and naming nothing.

The designer never needed the check because its menu is built from the target column's reference schema
(`MappingMenuBuilder.js:208-215`), so an invalid name is unreachable there. This binder is the first writer
with no such gate.

---

## Acceptance Criteria

- [x] **AC-01** — A `[#SysVariable...#]` whose name does not resolve through `SystemValueManager` is refused,
  and the refusal names the three that can carry a record: `CurrentUserContact`, `CurrentUserAccount`,
  `CurrentUser`. `FindInstanceByName`, not `GetInstanceByName` — the `Get` overload throws in the platform's
  own vocabulary and loses the alternatives.
- [x] **AC-02** — A variable that resolves but whose value type is not `Guid` (`CurrentDate`,
  `CurrentUserRoles`, …) is refused as unable to hold a record id.
- [x] **AC-03** — A recognised variable is stored VERBATIM: the check validates, it never rewrites.
- [x] **AC-04** — Unit tests use the REAL `SystemValueManager`, not a substitute. It hardcodes its nine items
  in `InitializeAllItemCollection`, so it self-populates with no database and no configuration — which makes
  the set production checks against the very set the tests assert on. Reaching it needs
  `ProcessDesignTestSupport.SetupSystemValues`: this harness wires no app-level `ManagerProvider` on purpose
  and installing one detaches what the base fixture set up (measured: 37 of 50 binder tests fail).
- [x] **AC-05** — Guidance and the tool/prompt text name the three variables, keyed by the target column's
  entity, and describe BOTH outcomes for a wrong name — refused by a current package, stored unchecked by an
  older one. An absolute claim either way is false on half the estate.
- [ ] **AC-06** — Rebundle from the MERGED package sources (never from a working tree), re-pin
  `ExpectedArchiveSha256` and `ExpectedDescriptorModifiedOnUtc` in `BundledProcessBuilderPackageTests`, and
  raise the archive version.
- [ ] **AC-07** — `clio.mcp.e2e` coverage for both refusals. **Cannot precede AC-06**: an install resolves the
  bundled archive from the BUILD OUTPUT, so an E2E written earlier exercises the old assembly and passes
  vacuously — which is worse than no test.

---

## Deliberately out of scope

- **The variable-to-entity check** (a Contact variable bound to an Account column). No server-side
  variable→entity mapping exists — the designer's equivalent is a client-side table — so it means copying
  platform trivia into the package, and it is being measured first. Consequence recorded in the code:
  `AutoGuid` and `SequentialGuid` are Guid-typed and pass, though they GENERATE a value rather than
  referencing a record.
- **A first-class `systemVariable` source** beside `recordId`. After AC-05 its remaining value is ergonomics
  only, and adding it write-side alone would break the D11 invariant that a decoded read-back is exactly what
  `setConnections` accepts.

---

## Definition of Done

- [ ] AC-01 … AC-07 all met
- [ ] Package suite green (`-c dev-nf`)
- [ ] `Category=Unit&Module=McpServer` green
- [ ] Diary entries in both repositories
