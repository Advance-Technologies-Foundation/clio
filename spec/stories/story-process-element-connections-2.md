# Story 2: Read side — the hybrid connection reader and the `describe` projection

**Feature**: process-element-connections
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md)
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Decisions**: D11, D9 (the `deprecated` flag)
**Status**: in-progress
**Size**: M
**Repo**: `ProcessBuilder` — `packages/CrtProcessBuilder/Files/src/cs/`
**Depends on**: story 1

---

## As a

coding agent that has just built or edited a process

## I want

`describe-business-process` to tell me which connections an element has, what each is bound to, and in the
same vocabulary I would use to write them

## So that

I can verify the process I asked for, and feed the output straight back in without translating a platform
metapath

---

## Acceptance Criteria

- [x] **AC-01** — Per element, `describe` emits `connections[]`, filtered to `Source != None` (T-7). An
  unbound connection never appears; the designer's ability to show unbound rows is deliberately not
  reproduced.
- [x] **AC-02** — Each emitted connection carries the **raw** persisted value verbatim under a stable
  field **and** a decoded source, per the D11 table: `[#Lookup.{schemaUId}.{recordId}#]` →
  `{ recordId, referenceSchema }`; `[#…[Element:{e}].[Parameter:{p}]#]` →
  `{ sourceElement, sourceElementParameter }`; `[#…[Parameter:{p}]#]` → `{ processParameter }`.
- [x] **AC-03** — The decoded shape is **exactly** what `setConnections` accepts, so `describe` output is
  re-appliable without translation.
- [x] **AC-04** — Anything else — an unrecognised macro, or a known dialect whose identifiers do not
  resolve — degrades to `{ expression: "<raw>" }`. The decoder must **never fail and never emit a
  half-decoded source**.
- [x] **AC-05** — Schema-UId→name resolution uses the tolerant `EntitySchemaResolver.FindNameByUId`
  (returns null rather than throwing). An unresolvable reference degrades per AC-04.
- [x] **AC-06** — Element and parameter UIds are resolved to names from the schema already being walked —
  no extra round trip.
- [x] **AC-07** — Elements carry `deprecated` (from `UserTaskDeprecationPolicy`, story 1) and
  `writesConnectionsAtRuntime`, so a legacy process built on a retired element is **readable and
  explainable** even though it may not be authored.
- [x] **AC-08** — Round-trip test **per dialect**: `describe → setConnections → describe` is stable for
  each of fixed record, element output, process parameter, system variable, system setting.
- [x] **AC-09** — Forward-compatibility test: an unrecognised macro round-trips through `expression`
  without loss. This is the pinned case that proves a future platform macro degrades instead of breaking
  `describe`.
- [x] **AC-10** — No refusal or guard from the write path reaches `describe`. Reading a process must never
  fail because its connections would be rejected on write.

## Implementation Notes

File to add: `Connections/EntityConnectionReader.cs` — mirrors `FilterDescriptorReader`'s role (it already
does exactly this job for filters: parse the persisted form back into the contract shape). Read that class
first; match its structure.

Contract change: `Contracts/DescribeContracts.cs` + `Describe/ProcessDescriber.cs`.

The macro vocabulary is a **fixed regex set** at `Terrasoft.Core/GeneratorUtilities.cs:50-69` — that
closed set is what makes AC-04's degradation rule safe to rely on. Do not attempt to be more permissive
than it.

Reference shapes measured on a live process (plan §5.1 and §2.6):

```
process parameter  [#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{818185d1-…}]#]
element output     [#[IsOwnerSchema:false].[IsSchema:false].[Element:{950d1f2c-…}].[Parameter:{921cce13-…}]#]
fixed record       [#Lookup.{referenceObjectSchemaUId}.{recordId}#]
system variable    [#SysVariable.CurrentUserContact#]
system setting     [#SysSettings.SomeCode#]
```

Note the asymmetry the fixed-record dialect creates and why AC-02 exists: **neither** GUID in
`[#Lookup.…#]` appears anywhere else in the describe payload, so a caller cannot cross-reference it —
unlike the two `[Parameter:…]` dialects, whose UIds do appear on the emitted parameter lists.
`displayValue` does not exist in the contracts and `DisplayValue` is not persisted, so there is no label
to fall back on.

## Deviations recorded at implementation time

1. **AC-08's five dialects became six cases.** The round-trip is pinned per dialect as specified, and the
   *system-setting* row is covered — but only because the write side ACCEPTS a system-setting expression
   rather than refusing it (see story 3's note on AC-11). Refusing it would have made a connection read back
   from a designer-authored process un-re-appliable, which contradicts AC-03.
2. **`source` and `registered` were added to the emitted connection** beyond the AC list. `registered` is the
   same half-citizen fact story 3 must warn about on write, and reporting it on read is what lets a caller
   diagnose an EXISTING process; `source` makes a connection stored with an unusual value source visible
   instead of silently normalised on a round trip.
3. **`writesConnectionsAtRuntime` is three-valued, not two.** `null` means not established — a non-user-task
   element, an unresolvable schema, or a user task outside the supported set, about which the package has no
   measured claim. Reporting `false` there would assert something it cannot back.

## Definition of Done

- [x] All AC met; AAA structure, `because` on every assertion, `[Description]` on every test method
- [x] Cross-platform tests
- [x] Public API documented; DI-registered behind an interface
- [x] No new `CLIO*` diagnostics in touched files (`CLIO*` analyzers run in the clio repo only; the
  `ProcessBuilder` build is warning-free)
- [x] Diary entry appended
