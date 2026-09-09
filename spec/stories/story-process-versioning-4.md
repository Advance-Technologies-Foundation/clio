# Story 4: State the version contract on the MCP surface

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-01, FR-02, FR-12, FR-19
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: in-progress
**Size**: S

---

## As a

developer

## I want

the process tools to say which version they read or start

## So that

an agent stops silently conflating a named version with the running one

---

## Acceptance Criteria

- [x] **AC-01** *(delivered by story 2)* — Given `describe-business-process`, when its description is read, then it names the version members, states that `process-name` targets one specific version rather than the running one, and states that the active-version answer comes from the process library
- [x] **AC-02** *(amended — see notes)* — Given `run-process`, when its description is read, then it states that it starts the ACTIVE version, not the named one
- [x] **AC-03** *(delivered by story 2)* — Given the describe prompt, when it is read, then it instructs reading `isActiveVersion` first and re-describing `activeVersionName` when it is false
- [x] **AC-04** — Given `docs/McpCapabilityMap.md`, when the describe row is read, then it lists the version members and the by-name caveat
- [x] **AC-ERR** — Given the description names a guidance article, when `WorkspaceTemplateGuidanceDriftTests` runs, then the named article is present in `curated-knowledge-names.json` and is not feature-gated

## Implementation Notes

**Scope reduced on 2026-09-03 — AC-01 and AC-03 were delivered by story 2, not skipped.** Story 2 changed the
describe output, and `AGENTS.md` makes an MCP review mandatory for exactly that trigger ("Command output"), so
the tool `[Description]` and the prompt had to be aligned in the same change rather than one story later. What
landed there: the version paragraph inserted before "Identify the process by exactly one of…" as this story
specifies, and a new prompt **step 2** that has to be read before narrating (existing steps renumbered 3 and 4).
Story 2 also pinned both channels — `DescribeProcessToolTests.ActiveVersionInvariant_Should_Appear_In_All_Clio_Channels`
plus a per-token test — so the "new description tokens asserted" row below is satisfied for the describe channel.

**What is left for this story**: AC-02 (`RunProcessTool` must state that it starts the ACTIVE version, not the
named one), AC-04 (`docs/McpCapabilityMap.md:744`), AC-ERR (the `WorkspaceTemplateGuidanceDriftTests` gate), and
the RunProcess half of the token assertions. The DoD items below still apply in full — the read-deadline check
and the capability-map edit have NOT been done.

Files: `DescribeProcessTool.cs` (`[Description]` is one 7437-character line at `:28`), `RunProcessTool.cs`, `DescribeProcessPrompt.cs` (`:31-37`), `docs/McpCapabilityMap.md:744`.
Insert the version paragraph BEFORE the sentence "Identify the process by exactly one of process-name / process-uid / process-caption." — it qualifies exactly that sentence.
Do NOT restore `[FeatureToggle("process-designer")]`: its absence is pinned by `ProcessDesignerGoLiveTests.cs:62`.
No four-part version literals in the description — `BundledProcessBuilderPackageTests.cs:885` polices them.
The version paragraph names **no** guidance article, so this story does not depend on the guidance stories (6/7); if that changes, add the dependency, because `WorkspaceTemplateGuidanceDriftTests.cs:537` resolves every article an ungated description names.
The description carries three counted claims ("with ONE exception", "with four exceptions", "the one exception being") — keep them consistent.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | the four tool-flag reflection pins still pass; new description tokens asserted | `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` |
| Unit `[Category("Unit")]` | guidance-name drift over the ungated tool descriptions | `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] `docs/McpCapabilityMap.md` edited in the same commit as the tool description (no automated guard on that file)
- [x] Read-deadline check: describe stays `ReadOnly=true`; the version read is projected and measured
- [x] Code compiles without Roslyn analyzer warnings
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-03
- Implementation completed: 2026-09-03
- Tests passing: `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel)"` → 8142 passed, 0 failed, 14 skipped. 2 new pins in `RunProcessToolTests`; the describe-side pins landed with story 2. `WorkspaceTemplateGuidanceDriftTests` green (AC-ERR): none of the added text names a guidance article, so nothing new has to resolve in `curated-knowledge-names.json`.
- Notes:
  - **AC-02 amended, and this is a spec deviation worth reading.** It asked run-process to state that it "starts the ACTIVE version, not the named one". That claim is NOT verified. The only evidence is the SCHEDULED path (`ProcessRunner.TryRunScheduledProcess` → `GetActiveVersionItem`); `run-process` launches by `ProcessStartArgs.SchemaName` through `ProcessEngineService.svc/RunProcess`, and nothing establishes that this endpoint folds a non-active code onto the active version. Settling it requires actually launching a versioned process, which on this stand means a stock invoice-approval process with real side effects.
  - So the shipped text says what IS true and what to DO: a code names one version; the platform's own triggers and schedules run the family's ACTIVE version; read `isActiveVersion` and launch the code from `activeVersionName`; and the fold is explicitly marked NOT established so an agent cannot rely on it. That instruction is correct either way — if the endpoint does fold, passing the active code changes nothing. Recorded as **OQ-04** in the PRD, due before any guidance article describes launching a versioned process.
  - A test pins the "NOT established" wording, so a later edit cannot quietly upgrade the gap into a promise.
  - Story 3 improved this surface for free: `run-process` refuses a caption while naming the code it resolved to, and that code is now the ACTIVE version's — so the refusal message is the short path to the right code. Both the description and the argument text say so.
  - AC-01 and AC-03 were delivered by story 2 (see the scope note above); this story added AC-02, AC-04 and the RunProcess half of the token assertions.
  - Read-deadline check: describe stays `ReadOnly=true`, pinned by a story-2 test. The version read is projected — story 1 removed `MetaData` / `MetaDataModifiedOn` from `VwProcessLib`, so no query over the process library carries the metadata blob any more — and was measured at ~65 ms on the local stand during the story-1 spike. Not re-measured here: this story changed no code path, only shipped text.
  - Docs: `docs/McpCapabilityMap.md` describe row now lists every version member with the absent-means-unknown rule and the caption behaviour; the run-process row carries the same caveat as the tool. No CLI doc target applies — neither describe nor run-process has a `[Verb]`. 
