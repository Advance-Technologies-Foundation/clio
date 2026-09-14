# Adversarial Review: process-versioning BMAD artefact set

**Reviewed**: `spec/prd/prd-process-versioning.md`, `spec/adr/adr-process-versioning.md`, `spec/stories/story-process-versioning-*.md`, `spec/test-plans/tp-process-versioning.md`, the appended rows in `spec/sprint-status.yaml`
**Date**: 2026-09-02
**Jira**: ENG-94374
**Reviewers**: five parallel passes — `bmad-reviewer`'s three lenses applied per artefact, plus a pipeline-mechanics pass
**Verdict on the first revision**: PRD **BLOCK** · ADR **BLOCK** · test plan **BLOCK** · stories **NEEDS REVISION** · pipeline mechanics **NEEDS REVISION** (~130 findings, 14 Critical)
**Verdict after this revision**: revised; a re-review of the mechanics pass is still outstanding

---

## Why the artefacts were hand-written

The set was assembled directly instead of by running `/bmad`, because the material had already been established in a preceding investigation (live measurements on a stand, an adversarial verification of the platform blockers, and four decisions taken by the ticket owner). The pipeline's phase order, templates and conventions were followed; this review is the compensating control for skipping the interactive checkpoints.

---

## Corrections the review forced, verified against the checkout

| # | What the artefacts claimed | What is true | Where it was wrong |
|---|---|---|---|
| 1 | "No `KnownRoute` is added" | clio reaches the package **only** through `ServiceUrlBuilder.KnownRoute` (`DescribeProcess = 51`, `:175`). Two new operations need two new routes and their route strings | ADR Consequences; stories for both tools |
| 2 | "`BindingsModule.cs` is untouched" | true only for the reader *interface* (auto-scan at `:233`). Commands are registered explicitly — `CreateBusinessProcessCommand` `:425`, `ModifyBusinessProcessCommand` `:427`, `DescribeProcessCommand` `:1031` — so the write half **does** edit it, making those stories a full-suite regression trigger | ADR choice 12; the tool stories |
| 3 | "the process-designer family is MCP-only" | `generate-process-model` is a public `[Verb]` (`GenerateProcessModelCommand.cs:11`) whose behaviour this feature changes, so `help/en/`, `docs/commands/`, `Commands.md` and `WikiAnchors.txt` move with it; `get-process-signature` is a `[Verb]` too | PRD CLI Impact |
| 4 | `clio.tests/Command/ProcessModel/ProcessLibResolverTests.cs` | the fixture is `clio.tests/Command/ProcessLibResolverTests.cs` | ADR, story 3, test plan |
| 5 | `VersionParentId` needs to become nullable | `VersionParentUId` is `COALESCE(PS.UId, SS.UId)` and never NULL. The nullable ones are `Version`, `IsActiveVersion` and `ParentId` (`[SchemaProperty("Parent")]`), which is NULL for every root | ADR choice 6, story 1 |
| 6 | the version read costs ~65 ms | that figure was measured with explicit column projection. The model declares `MetaData byte[]` (`VwProcessLib.cs:46-47`), so an unprojected family read drags schema metadata per member | ADR choice 6 / assumption A-05 |
| 7 | a versioned `[RequiresPackage]` floor can land before the rebundle | `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` (`BundledProcessBuilderPackageTests.cs:633`) fails when a declared floor exceeds the bundled archive, so the floor and the archive must land together | ADR choice 16; tracker dependency edges |
| 8 | `DataProviderMock.MockItems(...)` as a static | it is an instance API — `provider.MockItems(nameof(X)).Returns(rows)` (`ApplyEnvironmentManifestCommandTests.cs:71-75`) | test plan's worked example, story 1 DoD |
| 9 | kebab-case is "CLIO001 enforced" | CLIO001 is the DI-construction analyzer (`Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs`); the documented mapping to flag casing does not match the implementation. Kebab-case is a house rule guarded only by a reflection test | PRD, ADR, test plan |
| 10 | absence of a version member is self-explanatory | an unversioned process and a failed read produced byte-identical output. A warning member now distinguishes them | PRD FR-18; ADR choice 6 |
| 11 | the 50-member cap lived in an assumption's risk column while downstream artefacts treated it as behaviour | promoted to FR-17 with its reporting rule and a response member | PRD, ADR contracts |
| 12 | a substring scan proves a key is absent | `DescribeProcessResult` carries `[JsonExtensionData]`, so a server-sent `version` would re-emit. Assertions moved to parsed root-object keys | test plan TC-U-13/14 |
| 13 | 13 stories, one of them covering contracts, cloning, allocation, naming and rollback | that story was not one PR-worth of work. Split three ways, and the guidance work split into an article story and a write-half story; 13 → 16 stories | stories, tracker |

## Decisions taken by the ticket owner during the review

| Question | Decision |
|---|---|
| `get-process-signature` — make it version-aware or exclude it? | **Exclude**, as an explicit PRD non-goal with a follow-up; SM-04 rewritten to enumerate the three paths actually in scope instead of claiming none remain |
| Two concurrent creates both allocating N+1 | **Re-read after the save and fail** with the observed number (PRD FR-15, ADR choice 14) — no locking |
| The unremovable duplicate left by a failed `create → modify` | First answered as a compensating delete; **reversed on 2026-09-02 after the re-check** showed that compensation had no surface and that its "created in this session" promise was unenforceable server-side. Adopted alternative **B** instead: one `ModifyProcessAsNewVersion` operation that clones, applies the edits and saves as a new version, so the duplicate never exists. The objection that had rejected B — a second modify surface diverging from the first — does not hold: operation semantics live in `IProcessOperationExecutor`, and `ProcessModifyHandler` only delegates to it (`Design/ProcessModifyHandler.cs:55-110`), so both entry points share one applier (ADR choice 13, rewritten) |
| Scope | Full A + C + D in one Jira ticket; the earlier recorded deferral of the write half is reversed on record in the PRD |

## Findings deliberately not acted on

- **Persona wording** — "no-code builder" rows were restated as "developer (AI agent acting for a no-code builder)" rather than removed; the value statement is worth keeping visible.
- **A dedicated `spec/reviews/` entry per artefact** — one combined record (this file) instead of five, because the findings interlock.
- **PostgreSQL / Oracle view variants** — declared out of scope in the test plan rather than covered; the measurements behind this feature were taken on MSSQL and the variants order NULLs differently.
- **`IsMaxVersion` correctness** — left as a product concern; the field is not surfaced.

## Outstanding

- The re-check (2026-09-02, two passes) returned NEEDS REVISION on both, with **no Critical** findings, and both passes independently confirmed story 1 can legally start. 11 of 13 corrections landed cleanly; correction #4 was over-applied (the source path, unlike the fixture path, **is** under `ProcessModel/`) and correction #13's split left story 11 and story 13 still oversized.
- Fixed after the re-check: the resolver source path, the `KnownRoute` strings' leading slash, the missing `ActiveVersionSource` on `ProcessVersionFacts`, the projection mechanism (a narrow model type), PRD AC-04's self-contradiction (the warning **is** a version member), the new describe ambiguity error in CLI Impact, OQ-02's due date, story 4's guidance independence, story 5's Allure citation, and a truncation test case.
- **Closed**: the compensation question. Alternative B was adopted (see the decisions table), which removes FR-16's removal semantics entirely, keeps the operation count at +2, and deletes the unenforceable "created in this session" guarantee. PRD FR-07/FR-16, AC-05a-d, AC-11, the CLI Impact row, ADR Decision, alternatives B/C/C′, choices 3 and 13, the contracts, the tool table and the consequences were rewritten accordingly.
- Still open: story 11 (allocation + naming + compensation) and story 13 (package stamp + clio pins, two repositories) are each more than one PR.
- PRD OQ-01 (which package receives a new version) and OQ-02 (`UseNewSchemaHierarchyFolding` state) are gated on story 8's spike; OQ-03 (rebundle sequencing) on story 13.

---

## Third round — the alternative-B reversal, reviewed (2026-09-02)

Two passes over the rewritten write half: one on the reversal's internal consistency, one hunting the risks alternative B brings that the two-call shape did not.

**Verdict**: NEEDS REVISION on both, and the second pass found two defects that outrank everything earlier in this file.

### The two blockers, and why they change the design

`ProcessSchema`'s object-graph clone **shares mutable state with the live, app-cached source instance**:

- Cloned elements keep their back-pointer to the source (`ProcessSchemaBaseElement.cs:86`). The collection's repair (`ProcessSchemaFlowElement.cs:297-302`) never fires, because `ProcessSchema.cs:149-153` assigns the parent in an object initializer that runs *after* the constructor body has already added every element, leaving `ParentMetaSchema` null (`MetaItemCollection.cs:97`). A sequence flow's `SourceRefUId` setter then **writes** into the source's `Outgoings` (`ProcessSchemaSequenceFlow.cs:143-152`), and the source instance is an app-level singleton (`Manager.cs:320-329`), so `addFlow` / `removeFlow` / `removeElement` on the clone corrupt the running process's graph for every later request.
- `Group` is aliased outright: `ProcessSchema.cs:143` assigns it, the setter merges into a null target, and `LocalizableString.Merge` returns the source object unchanged (`LocalizableString.cs:270-272`). `InitializeLocalizableValues()` (`:1412`) then rebinds **the source's** binding to the clone's empty resources. `Caption` and `Description` escape only because `Schema.cs:122-123` wrap them in a new instance.

Both violate the feature's headline safety property, and both are **invisible to every assertion the artefacts specified**: the source's database row stays byte-for-byte identical, which is exactly what PRD AC-05a and the clone story asserted.

**Amendment**: the clone is materialised through a **metadata round-trip** with `CreatedInOwnerSchemaUId` (`BL8`) repaired afterwards (ADR choice 17). This is the mechanism the platform itself uses — `CreateSchemaCopy:929-942` calls `Clone()` and discards it in favour of `CloneSchemaUsingMetaData:532-555` — and it removes the aliasing, the incomplete typed `GetMetaItems` walk and the UId-rewrite-scope question in one stroke. The traps write-up's "never use the blunt whole-UId replace" objection was specifically that it rewrites `BL8`; repairing that one key answers it.

### Also amended

- **No design session for the clone** (choice 18): `DesignSchema` on a UId absent from `SysSchema` dereferences a null design item and `SaveSchema(Guid, …)` throws `ItemNotFoundException`; `SaveSchema(item, …)` needs no session and is the only viable route — which is also what keeps the atomicity claim true. The clone must be renamed before the save, because it starts life with the source's `Name` (`MetaItem.cs:108`) and both `CheckIsValidSchemaName` and `SchemaDuplicationDetector` gate it.
- **The response's version facts come from a post-save re-read** (choice 19), as both production copy paths do.
- **A false save is not an exception** (choice 20): `SaveSchema` returns `false` without committing or throwing when source generation fails (`SchemaManager.cs:1666-1685`), and `ProcessSchemaRepository.cs:82`'s rollback guard looks the item up in a different collection from the one the save path registers it in.
- Contracts: `SetActiveProcessVersionRequest` was used but never defined; `warning: string` became `warnings: List<string>` to match the live sibling (`ModifyContracts.cs:271`); both responses gained a warning channel.
- PRD: **AC-05e** added — the source instance's `Outgoings`/`Incomings` counts and `Group` binding must be asserted unchanged **in memory**, because a row comparison cannot see this class of defect. AC-12 is left deliberately unused.
- Test plan: TC-U-44 no longer asserts the deleted three-step flow; the Traceability table was renumbered after the splits; TC-U-30a-d added (in-memory source integrity, empty-list snapshot, the non-throwing false save, the stale-package refusal).
- Stories: 18 now. The clone story shed the typed-walk work and fits one PR; the old story 11 split into "apply the edits through the shared applier" and "number, name and persist"; the version floor moved off the rebundle story onto the two tool stories, which is what the archive pin actually requires.

### Verified correct and left alone

The operation and gate-call-site arithmetic — **+2 / +2 → 7 and 5** — is right, and for a non-obvious reason: the shared middle extracted from `ProcessModifyHandler` (`:76-88`) **excludes** the guard at `:61`, so each new handler gates in its own handler. With the third (compensating) operation removed, the count is correct where +3 would have been wrong.
