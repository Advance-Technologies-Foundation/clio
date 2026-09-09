# Story 9: ModifyProcessAsNewVersion contracts, guard and transport

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-09, FR-14
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: done
**Size**: M

---

## As a

developer

## I want

the operation's contracts, its authorization guard and its service entry point

## So that

the clone-and-edit logic lands on a shape that is already wired and tested

---

## Acceptance Criteria

- [x] **AC-01** — Given the service, when its contracts are reflected, then `ModifyProcessAsNewVersion` is present with `BodyStyle = WebMessageBodyStyle.Wrapped` like every sibling
- [x] **AC-02** — Given the request contract, when it is inspected, then it carries the source identity, an optional `packageName` and the **same** `List<ProcessOperationDescriptor>` shape `ModifyProcessRequest` takes
- [x] **AC-03** — Given the caller lacks `CanManageProcessDesign`, when the operation runs, then it is refused before any repository call
- [x] **AC-04** — Given a request with neither `name` nor `uid`, when the operation runs, then it returns `success:false` naming the missing identity
- [x] **AC-05** — Given the wire-contract test, when it runs, then the expected operation count is the previous baseline plus one
- [x] **AC-ERR** — Given an identity that does not resolve, when the operation runs, then it returns `success:false` naming the identity — never an unhandled `ItemNotFoundException` (pre-validate with `FindItemByUId`, never `GetItemByUId`)

## Implementation Notes

New: `Files/src/cs/Contracts/VersionContracts.cs` (shapes in the ADR), `Files/src/cs/Design/ProcessVersionSaveHandler.cs` (guard + transport only at this stage), one `[OperationContract]` after `Ping` (`ProcessDesignService.cs:122-129`), `IProcessDesigner`/`ProcessDesigner.cs` gains the use case, `CrtProcessBuilderApp.cs` gains an `AddScoped` through the `Connection(sp)` helper — the container validates scopes.
Reuse `ProcessOperationDescriptor` itself; do not clone the type. The whole point of this shape is that one descriptor list serves both entry points.
Follow `ProcessModifyHandler` for the response contract: `success` / `errorMessage` / `appliedOperations` / `warnings`. Not `ErrorOr` — referenced but used by no source file.
Express the wire-contract pin as a delta: two other stories rebundle the same package and move the baseline first.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | guard call ordering, identity validation, descriptor reuse, contract reflection | `tests/CrtProcessBuilder/ProcessVersionSaveHandlerTests.cs` |
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | operation count and per-operation `BodyStyle` | `tests/.../ProcessDesignServiceWireContractTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] Operation count and gate call sites move together, in this commit
- [x] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [x] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [x] `docs/process-builder-architecture.md` and `.puml` updated together
- [x] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`) — **the anchor does not exist**: crt-process-builder has no `AGENTS.md`, and its `CLAUDE.md` is 150 lines with no ClioRing section. Verdict stated in the PR body instead: no MCP surface changes here (the package operation is not reached by any clio tool until stories 16-17), so nothing for ClioRing to be compatible with yet.
- [x] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-04
- Implementation completed: 2026-09-04
- Tests passing: `dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf` → **934 passed, 0 failed** (the whole package suite; 9 of them new, plus the 4 wire-contract pins one of which is the operation count). Build: `dotnet build MainSolution.slnx -c dev-nf` → 0 warnings, 0 errors.
- Notes:
  - **The endpoint ships INERT and that is deliberate.** A resolvable source is answered with
    `ProcessVersionSaveHandler.NotYetImplementedMessage` rather than a success, because a caller that
    believed it had created a version would go on to activate one that does not exist. A test pins the
    message so the placeholder cannot outlive stories 10-12, and nothing can reach the operation
    meanwhile — the package is not rebundled into clio until story 15.
  - AC-05 read as a delta, as the story asked: `BaselineOperationContractCount = 5` +
    `VersioningOperationContractCount = 1`. clio's `ExpectedOperationContractCount` and
    `ExpectedAuthorizationGateCallSites` are asserted against the SHIPPED ARCHIVE
    (`BundledProcessBuilderPackageTests.cs:263,275`), so they move at the rebundle, not here. Gate call
    sites in the package went 3 → 4 in this commit, with the count.
  - AC-ERR needed a new repository member. `ResolveSchemaUId` only PARSES a caller-supplied uid — unlike
    the name path it never proves the schema is there — so `ProcessExists(Guid)` was added over
    `Manager.FindItemByUId` (never `GetItemByUId`, which throws the very outcome being asked about).
  - **Deviation, argued:** the new fixture is plain NUnit rather than `BaseComposableAppTestFixture`.
    Every collaborator is substituted, so the Creatio configuration harness buys no coverage — and
    inheriting it would have made all 9 tests unrunnable on this machine (see the blocker below). The
    sibling `ProcessDesignServiceWireContractTests` already sets the precedent.
  - **The harness suites run: 934 passed, 0 failed.** They did not at first — 803 of 934 died in
    `BaseConfigurationTestFixture.SetUp` with `FileLoadException`, because `Libs/UnitTest.dll` is compiled
    against `Creatio.FeatureToggling.TestKit 1.0.18.0` while the TestKit beside it is `1.0.17.0`. The cause
    is NOT a stand version: `tests/CrtProcessBuilder/app.config` is gitignored **by design**
    (`.gitignore:20-21`, "Local test binding-redirect (assembly versions are stand-specific; not
    committed)") and simply did not exist in this checkout. One explicit redirect fixes it. MSBuild's
    automatic redirects do not — an empty app.config emits 43 of them and none for the TestKit, because
    the 1.0.18.0 demand lives inside `UnitTest.dll` and MSBuild sees the direct Libs reference as
    satisfied. clio's own `tpl/UnitTestLibs` ships the same pair, so this is the intended mechanism.
    Measured before the file existed: clean `main` 803/122/925, this branch 803/131/934 — the same 803,
    which is how the story's own tests were verified in the meantime.
  - Environment: `.application` was absent from the checkout, so nothing could build. Restored as
    junctions to the local Creatio (`C:\Projects\creatio`): `core-bin` → the ROOT `bin` (NOT `Terrasoft.WebApp\bin`, which
    has no `Common.Logging.dll`),
    `packages` → `Terrasoft.Configuration\Pkg`, `Lib` → `Terrasoft.Configuration\Lib`. The folder is
    gitignored — per-machine setup, not a repository change.
  - Runtime self-verification NOT performed, and it is not deferred sloppiness: this build's only
    reachable behaviour is a refusal, and the operation cannot be called through clio until stories
    16-17 expose it. It becomes meaningful at story 12.
  - DoD anchor `AGENTS.md:241-299` for the ClioRing verdict does not resolve — this repository has no
    `AGENTS.md`, and `CLAUDE.md` is 150 lines. The reference looks copied from clio's. Verdict recorded
    in the checklist above instead.
