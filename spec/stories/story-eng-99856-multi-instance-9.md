# Story 9: `describe-business-process` reports the multi-instance options — and the clio DTO mirrors them

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-17, FR-02 (the clio mirror), FR-24 (not triggered under the working assumption)
**AC coverage**: AC-13, AC-14
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — D0-a, *The clio mirror*
**Contract**: [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md) — Q3
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: M (half day, two repositories — **two PRs, package first**; the clio half is a DTO and its tests)
**Repo**: crt-process-builder + clio
**Depends on**: stories 1 (the gate must be written before describe is touched), 3
**Blocks**: stories 10, 12, 13

---

## As a

AI agent that has just read a process

## I want

`describe-business-process` to report the multi-instance options and the five role names

## So that

I can read an existing element, change one thing and write it back without inventing what is there

---

## Owner decision this story assumes

**OQ-03 — is `inSync` redefined** to ask the mirror question against the two collections' item properties
instead of the five top-level parameters? Working assumption: **`inSync` is frozen**; if the mirror
question is worth answering, it is answered by a **differently-named** field.
A silent meaning change on a shipped field is the one kind of break no consumer can detect — no
deserializer, no schema check and no version negotiation sees it.
*If the owner decides to redefine it*, FR-24 activates: `DescribeProcessCommand` gains an explicit
`[RequiresPackage]` floor (it carries only an unversioned presence gate today at
`clio/Command/DescribeProcessCommand.cs:17-18`), story 12 raises a fourth floor, and the guidance in
story 14 documents the meaning change loudly.

## Acceptance Criteria

- [ ] **AC-01** (AC-13/FR-17) — Given a multi-instance element, when `describe-business-process` runs,
  then the response carries a `multiInstanceOptions` block with `enabled`, `executionMode`, `ignoreErrors`
  and the five role **names**: `inputCollection`, `outputCollection`, `completedIterationsCount`,
  `terminatedIterationsCount`, `totalIterationsCount`.
- [ ] **AC-02** (AC-13/FR-17) — Given the same response, when `multiInstance` is inspected, then it is
  still a JSON **boolean** (`DescribeContracts.cs:1439-1440`; clio `bool?` at
  `IProcessDescriber.cs:1108-1109`). Widening it breaks clio's deserializer.
- [ ] **AC-03** (AC-14/FR-17) — Given an element whose `JE5` names a UId that does not resolve, when
  describe runs, then that role reports `null`, the call **succeeds**, and no exception escapes. Every
  role resolves through the tolerant `Parameters.FindByUId`, never the throwing `GetByUId` — an
  unresolvable role is exactly the state the rebuild throws on (`ProcessSchemaActivity.cs:378-379`), and
  describe must **report** a malformed element rather than die on it.
- [ ] **AC-04** — Given a single-instance element, when describe runs, then no `multiInstanceOptions`
  block is emitted at all (not an empty object), so the block's presence is itself informative.
- [ ] **AC-05** (FR-02) — Given clio, when the response is deserialized, then `DescribedSubProcess`
  (`clio/Command/ProcessModel/IProcessDescriber.cs:1080`) carries a **typed**
  `DescribedMultiInstanceOptions` member with XML docs on every property. `DescribedSubProcess` does carry
  `[JsonExtensionData]` (`:1147`), so an unmirrored block would round-trip as raw extension data rather
  than vanish — mirror it typed anyway: the XML doc on a typed member is the agent-facing contract
  surface, extension data carries none, and clio's own tests can only assert against typed members.
- [ ] **AC-06** (FR-02) — Given clio's serializer (`DescribeProcessCommand.cs:51-54`, `WhenWritingNull`
  suppression only), when a role is `null`, then the key is omitted rather than emitted as `null`, and a
  test pins which of the two the agent sees.
- [ ] **AC-07** — Given the block's name, when it is compared with the write side, then it is
  **identical**: `multiInstanceOptions`. Every configuration block in `DescribeContracts.cs` is named
  identically to its write counterpart (`signal` :148, `filter` :156, `readData` :164, `accessRights`
  :200, `email` :213, `preconfiguredPage` :231, `subProcess` :241, `connections` :251, `approval` :310);
  `multiInstanceDetails` would be invented vocabulary.
- [ ] **AC-08** (D0-a) — Given describe, when the diff is reviewed, then it reports `itemProperties` only
  from the loaded instance and **never synthesises them**, does not change `LoadForDescribe`, and does not
  invalidate any manager cache from a read path.
- [ ] **AC-ERR** — Given a malformed element (an empty `BP6`, an unresolvable role, a missing counter),
  when describe runs, then it succeeds with nulls; describe has no refusals. Any write-path refusal that
  reaches describe is a defect.

## Implementation Notes

Package side:

- `Files/src/cs/Contracts/DescribeContracts.cs` — `DescribeMultiInstanceOptions` + the member on
  `DescribeSubProcessInfo`.
- `Files/src/cs/Elements/SubProcessElementHandler.cs` — `:98` is the existing
  `bool isMultiInstance = subProcess.MultiInstanceOptions != null` derivation; emit the block beside it.
- Report `executionMode` as the **string** the write side accepts (`"Sequential"` / `"Parallel"`), so a
  describe output is re-appliable without translation. Report the effective value, including the
  suppressed default — a caller cannot act on "absent".

clio side:

- `clio/Command/ProcessModel/IProcessDescriber.cs` — `DescribedMultiInstanceOptions` on
  `DescribedSubProcess` (member near `:1049`), plus the retraction of the multi-instance refusal statement
  at `:1105` (counted in story 14's inventory — coordinate so it is retracted once, not twice).
- Note for the implementer: the **write** descriptor crosses clio as opaque JSON
  (`CreateBusinessProcessArgs.descriptor` is a string on `CreateBusinessProcessTool`), so FR-02's
  obligation on the write side is documentation (the tool `[Description]`, story 13), not a DTO. Verify
  that is still true before concluding there is nothing to mirror.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | the block is emitted with five role names; absent on a single-instance element; a null role on an unresolvable UId with no exception; `executionMode` reported as a re-appliable string | `tests/UnitTests/CrtProcessBuilder.Tests/SubProcessElementHandlerTests.cs` |
| Unit `[Category("Unit")]` (clio) | the block deserializes into the typed DTO; `multiInstance` stays a `bool`; a null role survives a round trip; the serializer omits rather than emits null | `clio.tests/Command/McpServer/DescribeProcessMultiInstanceTests.cs` |
| E2E | describe a converted element end to end | `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs` (story 13) |

Test naming: `Describe_ShouldReportNullRole_WhenCounterUIdDoesNotResolve`.

## Definition of Done

- [ ] Block name identical on both sides; `multiInstance` still a `bool`
- [ ] Every role resolved with `FindByUId`; describe never throws on a malformed element
- [ ] Typed clio DTO with XML docs; no reliance on `[JsonExtensionData]`
- [ ] No load-path change, no cache invalidation, no synthesised item properties (D0-a)
- [ ] OQ-03's assumption and the FR-24 consequence if reversed are restated in the PR description
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `[Category("Unit")]` —
      never `[Category("UnitTests")]`
- [ ] No new `CLIO*` diagnostics in the touched clio files
- [ ] No new CLI flag (kebab-case rule vacuous); the JSON members stay camelCase deliberately
- [ ] Validated locally: package —
      `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf` in the
      **main checkout**; clio —
      `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=ProcessModel|Module=McpServer)" --no-build`
- [ ] Both PRs reference this story file and link each other; **package merges first**

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
