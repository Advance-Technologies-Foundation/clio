# ENG-105556 AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page - work plan

| | |
|---|---|
| Issue | ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page" (Task; epic ENG-92704 "Create BP via AI Toolkit"; component bpms tools) |
| Status | Proposal, 2026-11-08. Local, untracked. Written BEFORE the owner has answered Q1..Q29: every step that depends on an answer names its Q and follows the recommended option of the [decisions file](eng-105556-minimal-dcm-decisions.md); a different answer re-opens only the steps that cite that Q |
| Inputs | [design file](eng-105556-minimal-dcm-design.md) (what each component does; authoritative for names and contracts); [decisions file](eng-105556-minimal-dcm-decisions.md) (Q1..Q29); research notes `<research-dir>/` (`_verdicts.md` and g1..g6 override r1..r10) |
| Owns | the order of work, the work packages per repository, the PR split and merge order, the cut/rebundle procedure, the policy gates of clio `AGENTS.md`, stand use, cleanup and the definition of done. It does NOT own hours ([estimate file](eng-105556-minimal-dcm-estimate.md)) or test ids ([test plan](eng-105556-minimal-dcm-test-plan.md): its id families and their BMAD names, section 2). It OWNS the AC1..AC6 numbering (section 12); the test plan's coverage matrix follows it |
| Written so far | nothing: no product code, no branch, no Jira, GitHub, GHE or SVN write, no stand write. Stand `Creatio` was only read during research |
| Baselines | crt-process-builder `origin/main` `a3f58852`, clio `origin/master` `69788ccd5`, clio-knowledge `origin/master` `622134b`, core TSBpm `a95349820`, creatio-ui `7ca9210bd0e`, PackageStore `*/branches/7.8.0`, stand `Creatio` (.NET Framework 4.8, MSSql, read-only) [measured r10 §1, v r10-C1] |

Evidence tags as in the design file: `[traced path:line]`, `[traced, judge]` (re-read by the design judge),
`[corpus N/M]`, `[measured]` (query in the cited note), `[academy URL]`, `[note]`, `[inferred]`; `(v rX-CY)` = row of
`_verdicts.md`; `(g3)`, `(r7 §1.4)` = research note section; `(D §n)` = section n of the design file; `M1..M10` = the
stand measurements of D §14.5. Path aliases: `PB/` = `packages/CrtProcessBuilder/Files/src/cs/`, `PBT/` =
`tests/UnitTests/CrtProcessBuilder.Tests/` (both in crt-process-builder), `CLIO/`, `KB/` = repository roots. No
package version number, `libraryVersion` value or archive size is forecast anywhere in this file: every cut takes
the first free number at the moment it is cut (section 7).

---

## 0. Critical path

```
Q1..Q29 answered ──> day 0: drift check, CI-platform API check (D0.3), .NET 8 stand request (D0.5),
      │               read-write stand chosen (D0.6), cut-claim place (D0.7)
      │
      └──> BMAD docs PR (clio, full /bmad: PRD, ADR, stories, test plan) ── merged before the package PR leaves draft
                                  │
       package slice A (seams: IPackageCatalog split, guard, contracts) ──> slice B (pure: planner,
       reader, composer, dependency planner, parity) ──> slice C (writes: repository, settings writer,
       verifier, compensation) ──> slice D (handler, BuildCase op, composition, mirror 8 -> 9)
                                  │
              experimental cut ──> install on the read-write stand ──> STOP gate: M1, M5, M6 (+ M2, M4)
                                  │
              gate 1 ──> package draft PR ──> cut ──> clio branch: rebundle, route, tool, page step, e2e
                                  │
              KB PR (article, routing, process-modeling fix) ── in parallel with the clio branch
                                  │
              SVN AutoTestClioMcp seeded object (owner commits) ── CAADT prompt + Playwright spec (draft PRs)
                                  │
              final cut ──> E2E + CAADT + manual M1..M7 on .NET Framework ──> same on the .NET 8 stand (M8)
                                  │
              gate 3 ──> human merges: package -> KB (publishes) -> clio (re-pin if needed) -> CAADT repos
```

The STOP gate is the residual risk of Q6/Q7: nothing yet proves on a stand that a route-A DCM (object model +
`SaveSchema`, D §5 W4) opens in the DCM designer with the expected stages and no error (traced equal to route B only,
g2 §4), that `IPackageSchemaDataCreator` called from a CrtProcessBuilder WCF request binds into the requested package
(M6, r4 open question 1), or what `RemoveItemByUId` leaves behind (M5). If M1 shows an error box or a reset stage, or
M6 binds into another package, work stops, the evidence goes to the owner, Q6 (and possibly Q1) is re-opened, and
slices C-D are re-planned before the clio branch starts.

No code is written before the PRD and the ADR of the BMAD docs PR exist on their branch (clio `AGENTS.md` "BMAD
development pipeline": no code until the ADR exists); they need not be merged for slice A to start.

---

## 1. Phase 0: decisions and day 0

| Step | What | Who | Definition of done |
|---|---|---|---|
| D0.1 | Answer Q1..Q29 | owner | each Q carries an "Agreed by the owner" line in the decisions file; every answer that differs from the recommendation is carried into the design file and into this plan by one sweep (grep of all spec files for the replaced wording) before slice A starts |
| D0.2 | Drift check, after `git fetch` in every repository (a stale ref is not evidence of absence): `git log a3f58852..origin/main -- PB/EntryPoints PB/Design PB/Schema PB/EntitySchemaResolver.cs PBT/EntryPoints`; `git log 69788ccd5..origin/master -- clio/Common/ServiceUrlBuilder.cs clio/Command/McpServer clio/Command/ProcessModel clio/Command/PageUpdateOptions.cs clio/Command/RelatedPages clio.tests/Common/BundledProcessBuilderPackageTests.cs clio.tests/Command/McpServer`; `git log 622134b..origin/master -- guidance/mcp/guides/processes guidance/mcp/guides/routing.md bundle-source.json`; `svn log -l 20` on `PS/AutoTestClioMcp`; `git ls-remote` for open clio branches that claim `KnownRoute` values (two remote branches already claim 122, `_verdicts.md` conflict 4) | lead | a dated drift note at the top of this file; every moved file re-read; the next free `KnownRoute` value and the operation-count / gate-count pins re-read from master [traced, judge CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:292,361,373,384] |
| D0.3 | **CI-platform API check.** The design traced core trunk `a95349820`; the package CI compiles against Creatio 8.3.4, `net472` [traced pb `AGENTS.md:74`], and the research snapshot carries no `.application` reference binaries. Build a throw-away spike (never committed) on `-c dev-nf` against the 8.3.4 core-bin (`clio dconf --build <8.3.4 zip>`, pb `AGENTS.md:96-98`) that references every public member of the reuse map (D §11 core rows): `DcmSchemaManager.CreateSchema/SaveSchema/GetProcessValidationResult/RemoveItemByUId`, `DcmEntityUtilities` ctor + `FindEnabledDcmSchemas`, `BaseProcessSchemaManager.GetIsActiveVersion(Guid)`, `IBaseProcessSchemaManagerItem.GetIsEnabled()`, `IPackageSchemaDataCreator`, `PackageElementUtilities.DeletePackageSchemaData`, `DBSecurityEngine.GetCanExecuteOperation/GetCanModifyPackageElement/GetEntitySchemaOperationsRightLevel`, `WorkspaceUtilities.GetPackageIdByUId/GetPackageHierarchyIdsById`, `EntitySchemaColumn.CreatedInSchemaUId` | lead | a table "member / present on 8.3.4 / present on trunk" in this file; any member missing on 8.3.4 goes to the owner as a new decision (fallback named, e.g. an own scan with the public `GetIsActiveVersion(Guid)` instead of `FindEnabledDcmSchemas`) BEFORE slice A. **The same spike under `-c dev-n8` is MANDATORY** (CrtProcessBuilder ships source-only and is compiled by the target, clio `AGENTS.md` "Bundled Creatio packages"; CI compiles net472 only; no .NET 8 stand exists, g5 §1): the net-core core-bin comes from `clio dconf --build` over the existing `StudioNet8` 10.0.0.942 zip (estimate §9) or a net-core 8.3.4 zip, named in the table with its source; a compile error that occurs only on the netstandard2.0 target would otherwise first appear at install on a .NET 8 environment and break every process-designer tool there. The table gets a third column "present on net-core" |
| D0.4 | Read the internal records before code (clio `AGENTS.md` "Knowledge base"): `CLIO/docs/knowledge/ProcessModel/`, `McpServer/`, `platform/`, `Package/`; `grep -rli dcm CLIO/docs/knowledge` = 0 files, `grep -rli "dcm\|stage\|progress"` = 22 files at `69788ccd5` [measured locally 2026-11-08: `git rev-parse --short HEAD` = 69788ccd5]; must-read before CL-5/CL-6: `McpServer/grid-containers-position-by-layoutconfig-not-item-order.md`, `Command/section-page-schemas-ignore-entity-schema-name.md` (page placement and record-page path), and `platform/a-freedom-ui-page-is-ready-when-its-outlet-is-quiet.md` (browser checks); crt-process-builder `docs/process-builder-architecture.md`, `docs/bundling-into-clio.md`, `.ai/specs/template.md` | lead | the records that touch the changed paths listed in the drift note |
| D0.5 | **.NET 8 stand request** (Q24): no registered `isNetCore` environment is reachable (8/8 refuse TCP) and the ProcessDesigner lane deploys .NET Framework only (g5 §0-2) [measured g5 §1]. Request: `DeployApplicationCommon` with a `StudioNet8` PostgreSQL zip (a trunk one needs `BuildProductNet8`; an older 10.0.0 one exists, g5 §2.3), then `install-process-builder` from the branch build, then `reg-web-app` (IsNetCore auto-detected) | owner (provisions); lead prepares the exact request text | the stand is registered, `describe-environment` answers `frameworkKind` = .NET (Core), `list-packages` shows CrtProcessBuilder; until then every .NET 8 statement in code comments, results and guidance says "expected", never "measured" (D §8) |
| D0.6 | **Read-write stand for the STOP gate and E2E development.** Stand `Creatio` runs an unmerged CrtProcessBuilder feature build of another branch, not main (v r10-C2): installing this task's cut replaces it for its owner. Options: a disposable ProcessDesigner-lane stand or a personal stand; stand `Creatio` only if the owner says so explicitly | owner | the stand named here with its runtime; every write on it needs the owner's yes, one step at a time (section 10) |
| D0.7 | **Where cut numbers are claimed.** Not covered by Q1..Q29; the epic's precedent is a claim comment on the Jira issue (spec/eng-92710-call-web-service-element plan §6) | owner decides; the lead writes to Jira only on an explicit yes and reads the comment back | the claim place is named here; no number is stamped before it is claimed (section 7) |
| D0.8 | Seed-object decision for the E2E (Q25): (a) lookup column + 4-value lookup with an integer `Position` + tabbed Freedom UI record page + related-page add-on in SVN `AutoTestClioMcp` (deployed on every ProcessDesigner-lane stand [measured g5 §2.1]), with a `LocalOnly` interim fixture until it ships; (b) a product object (`Webhook.Status` has 4 values, unmeasured on the lane, v r10-C9) | owner | the arrange route written into the test plan; for (a) the owner agrees to make the SVN commit (agents never commit to SVN) |
| D0.9 | **AI Toolkit skills read.** The Jira title is "AI Toolkit: ..." and the CAADT scheduler matrix rotates modes (`skill`, `auto-discover`, `clio_mcp_only`) (r7 §7, `<research-dir>/r7-clio-and-knowledge.md:367-380`); the toolkit repository `creatio-ai-app-development-toolkit` (a CAADT metarepo member; distribution in `CLIO/clio/Common/Skills/ToolkitDistribution.cs:4-29`) was not read in research. Read its app-creation skills for any "status / stages / progress bar" routing that should point to `process-dynamic-case` | lead | either a toolkit PR added to the PR list (section 7, estimate) or a written reason none is needed; the scheduler-matrix modes the new CAADT prompt runs in (E-2) named here [inferred: no plan row named the toolkit before] |

---

## 2. Phase 1: BMAD docs PR (clio, docs-only)

Branch `docs/ENG-105556-minimal-dcm-spec` from a fresh `origin/master`; PR title
`ENG-105556 AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page` (Jira
title verbatim). Draft until the owner approves the content. **Full `/bmad` pipeline, not `/bmad-spec`:** the
repository's spec command excludes features that change MCP tool signatures [traced CLIO/.claude/commands/bmad-spec.md:22-25],
and this one adds an MCP tool, a public service operation and work in three repositories, a "significant feature"
(clio `AGENTS.md` "When to use each command"). The pm-agent writes the PRD, then the architect-agent the ADR, the
story-writer the stories, the qa-planner the test plan, each from this folder.

| File | Content | Definition of done |
|---|---|---|
| `spec/prd/prd-eng-105556-minimal-dcm.md` | the four scope items, the acceptance criteria AC1..AC6 of section 12 of this plan (this plan owns the numbering), out of scope (D §16: steps, stage conditions, alternative stages, transition rules, modifying an existing DCM, Classic/mobile/mini pages, typed pages without `page-schema-name`) | every AC traceable to a Jira acceptance line; no Jira key other than ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page" and ENG-92704 "Create BP via AI Toolkit", each written with its title |
| `spec/adr/adr-eng-105556-minimal-dcm.md` | decisions with their rejected alternatives: Q1 (write in CrtProcessBuilder; rejected: clio-only client of the designer endpoints, a core API, a CrtGenAI dependency), Q2 (op placement), Q3 (one tool), Q6 (route A behind `IDcmSchemaSaver`), Q7 (schema save last), Q12 (equivalent re-run), Q16 (dependencies), Q18 (page write path), Q22 (floor), Q27 (steps approach recorded, not taken), Q28 (no toggle); the extension seams of D §12 as constraints on this task | each decision cites the evidence row of the decisions file; the "steps later" seams are listed as invariants a reviewer can check |
| `spec/stories/story-eng-105556-minimal-dcm-1.md` | case write: package `BuildCase` + clio tool with `add-progress-bar=false`; DoD = AC1, AC2, AC4 (case half), AC5 (TC-U-PB, TC-U-CL) | DoD boxes mirror section 12 |
| `spec/stories/story-eng-105556-minimal-dcm-2.md` | record page + progress bar: resolver, checks, fragment, page step, retry path; DoD = AC3, AC4 (page half) | same |
| `spec/stories/story-eng-105556-minimal-dcm-3.md` | guidance, E2E, CAADT, .NET 8 evidence; DoD = AC5 (TC-U-KB, TC-I, TC-I-CA, TC-I-AR, TC-I-M), AC6 | same |
| `spec/test-plans/tp-eng-105556-minimal-dcm.md` | from the [test plan](eng-105556-minimal-dcm-test-plan.md) of this folder, with its ids renamed into the two families clio `AGENTS.md` makes mandatory ("all TC-U-* and TC-I-* test cases must be implemented" [traced CLIO/AGENTS.md:785]): **TC-U-PB-nn** = TC-U-nn and TC-P-nn (package unit and parity), **TC-U-CL-nn** = TC-C-nn (clio unit), **TC-U-KB-nn** = TC-K-nn (clio-knowledge tests), **TC-I-nn** = TC-I-01..14 (MCP E2E), **TC-I-CA-n** = CA-n (CAADT), **TC-I-AR-n** = AR-n (headless agent runs), **TC-I-M-n** = M1..M10 (stand measurements); manual Jira cases keep TC-01..TC-13 and are listed under AC2/AC3/AC4 by name | every row of the test plan has a BMAD id; every AC of section 12 maps to at least one id; the mapping table is stated once at the top of the BMAD test plan |
| `spec/sprint-status.yaml` | three story rows, `in-progress` (implementation has started by the time this PR merges, so `ready-for-dev` is never true). The clio feature PR flips them to `review` when it opens; `done` is flipped in the next clio PR that touches the tracker after the last merge, with only the really ticked DoD boxes | rows parse; statuses follow clio `AGENTS.md` "Sprint tracker" |
| `spec/eng-105556-minimal-dcm/` | this folder (design, decisions, plan, test plan, facts, reuse, traps, use cases, estimate) plus a sanitised companion `eng-105556-minimal-dcm-evidence.md` that holds every cited `_verdicts.md` row (claim, verdict, path:line) and the cited g/r note sections, so reviewers can check the load-bearing claims (Q5 rights, Q7 write order, conflicts 1/6/7) without the local notes (about 700 `(v rX-CY)` / `g5 §n` references across the folder) | before the commit: the "Local, untracked" lines dropped; absolute local paths replaced by repository-relative ones; every `(v rX-CY)` cited by the ADR or the design resolves in the evidence file; grep for the stand host name and for credentials = 0 |

Review: gate 1 (section 9) as a full 3-lens fan-out, the correctness lens checking the docs against the cited code.
Gate 2 skips docs-only commits. Pushing the branch and opening the PR wait for the owner's word.

---

## 3. Phase 2: package (crt-process-builder)

Fresh worktree at a short path (net472 MAX_PATH), branch `feature/ENG-105556-minimal-dcm` from a fresh `origin/main`;
not the read-only research snapshot `<crt-process-builder>`. Package-side spec
`.ai/specs/ENG-105556-minimal-dcm.md` from `.ai/specs/template.md` [traced ls pb105556/.ai/specs].

Validation for every commit: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
and `-c dev-n8` (the suite builds as net8.0 under `dev-n8`; CI runs net472 only, v r8-C6). The package PR stays
draft until a `dev-n8` build and test run against the net-core core-bin of D0.3 is recorded in its body. Every new fixture is
`[TestFixture(Category = "UnitTests"), Category("PreCommit")]`, otherwise CI skips it silently and
`CiContractGuardTests` fails [traced pb `AGENTS.md:61-69`]. Test style: AAA, `because:` on every assertion,
`[Description]` on every test, no Jira key in names or descriptions.

### 3.1 Slice A: seams, guard, contracts (behaviour-neutral)

| Task | Component | Definition of done |
|---|---|---|
| P-A1 | `IPackageCatalog` extracted as a BASE interface: `interface IProcessSchemaRepository : IPackageCatalog` (package resolve, foreign/locked check, prefix move to `IPackageCatalog`, and `IProcessSchemaRepository` inherits them) [traced, judge PB/Schema/IProcessSchemaRepository.cs:20,92,125,131]; `ProcessSchemaRepository` implements both; DI registers `IPackageCatalog` to the same scoped instance. The 4 handler constructors (`ProcessBuildHandler.cs:67`, `ProcessCompileHandler.cs:53`, `ProcessModifyHandler.cs:41`, `ProcessVersionSaveHandler.cs:86`), the composition root and the ~50 `Substitute.For<IProcessSchemaRepository>()` test doubles stay untouched; only the new DCM code depends on `IPackageCatalog` | every existing suite green on dev-nf and dev-n8; no call site changes behaviour; composition tripwire (`CrtProcessBuilderAppTests`) green |
| P-A2 | `ProcessDesignGuard.EnsureCanManageDcm()` (Q5): General user, `CanManageDcm`, `GetCanModifyPackageElement` (= General + `CanManageSolution`), `CanAppend` on `SysDcmSettings` and `SysDcmSchemaInSettings`; each refusal names the missing right; no read before the gate (D §5 S1) [traced, judge CORE/Terrasoft.Core/DcmProcess/DcmSchemaManager.cs:132-139; DBSecurityEngine.cs:3707,5344-5347] | `ProcessDesignGuardTests`: portal user, each missing right, all present; deny tests pin that the gate is on the execution path (bundled-packages.md: a call-site count cannot tell a gate that moved) |
| P-A3 | `BuildCaseRequest : ExtensibleRequestContract` with `CaseStageRequest : ExtensibleRequestContract { Value }`, `BuildCaseResponse` with `ErrorCode`, `Case`, `Settings`, `RequiredPackages`, `MissingDependencies`, `Obligations` (D §4); unknown keys through `IUnknownRequestKeyDetector`, `steps` mapped to `steps-not-supported` (D §3.1) | wire-contract tests pin every DataMember name; a request with `steps`, `color`, `filters` is refused with the stable code; operation count still 8 (no op yet) |

One commit each. Gate 2: one combined lens (new code, one module) except P-A1 (shared repository interface): full
fan-out if its diff exceeds about 50 lines.

### 3.2 Slice B: pure pieces and parity

| Task | Component | Definition of done |
|---|---|---|
| P-B1 | `CaseStagePlanner` (pure, Q8, Q10, Q11, Q29): explicit list (Id, else exact display value; ambiguity refused; permutation required) > `order-column` > first integer non-boolean column of `Position`, `StageOrder` (+ `StageInnerOrder`), `Number`, `StageNumber` > display ascending; `CreatedOn` then `Id` as tie-breaks with a warning; bounds 2..20 as ONE constant; `Stage1..N`, caption = display value, fresh UId per stage, `StageRecordId` = row Id, colour `#0C8701`, connections every ordered pair n*(n-1), no self-loops (D §5 S10) | `CaseStagePlannerTests` cover every row of D §14.1's planner line; `orderSource` string per branch; no platform mock |
| P-B2 | `StageValueReader` (Q9): ESQ over the reference schema under the caller's rights and profile culture, `Id`, primary display column, `CreatedOn`, candidate integer columns, at most `MaxCaseStages + 1` rows (D §5 S9) | `StageValueReaderTests` with `[MockSettings(RequireMock.DBEngine)]` + `SetUpTestData` (r8 §3.6): requested columns and row bound pinned |
| P-B3 | `DcmSchemaComposer` (Q6, Q29): receives the item from `IDcmSchemaRepository.CreateItem(name, uid)` (P-C1; creation is NOT in the composer, D §5 S13); `PackageUId` on schema AND item; `Caption`; `CultureName` = caller culture; `EntitySchemaUId`, `StageColumnUId`; `Enabled = true` explicitly; stages and connections from the plan; `Elements` empty; `InitializeLocalizableValues()` (D §5 S13) [traced, judge CORE/Terrasoft.Core/SchemaManager.cs:3335-3355,5506-5515] | `DcmSchemaComposerTests` over a real item `new DcmSchemaManagerItem(mgr){Instance = new DcmSchema(mgr)}` (GeneratedDcmSaver.Tests pattern; a plain `RequireMock.DCM` substitute returns a null `DefSchema` and `Instance`, v r8-C3) |
| P-B4 | `CaseDependencyPlanner` (Q16), open input set: column-defining package = `((IManager)uc.EntitySchemaManager).FindItemByRealUId(column.CreatedInSchemaUId)` kept only when its `Name` equals the entity name, else the entity's root (non-extend) item (mirror of the internal `FindItemWhereColumnIsDefined`); object and lookup root packages = `((IManager)mgr).FindItemByRealUId(runtimeSchema.UId).PackageUId`; never `GetItemByName` / `GetItemByUId(...).PackageUId` (the item collection holds the last replacement with the root UId) [traced CORE/Terrasoft.Core/SchemaManager.cs:4026-4042; CORE/Terrasoft.Core/Entities/EntitySchemaManager.cs:1487-1490; CORE/Terrasoft.Core/Manager.cs:181,259-261]; `CrtCoreBase`, `CrtBase`; reachability through `WorkspaceUtilities` (D §5 S12; g3 "What the tool should do" 1) | `CaseDependencyPlannerTests`, including (a) column added in a replacement in package X that is not the top replacement and (b) an object with two unrelated replacements; the input set is a collection type a later step source can add to (D §12) |
| P-B5 | `DcmMetadataParityTests` (v r5-C10): `WriteMetaData` of a composed 3-stage case has the key set of the shipped `KnowledgeArticlePublishing` DCM, UIds compared by role, colour aside; fixture committed with a `PROVENANCE.md` (source path, SVN revision, SHA-256) and a `.gitattributes` `-text` line, the epic's fixture convention | parity green; every excluded key named with its reason |

### 3.3 Slice C: writes, verification, compensation

| Task | Component | Definition of done |
|---|---|---|
| P-C1 | `IDcmSchemaRepository` + `IDcmSchemaSaver` (route A, Q6): `CreateItem(name, uid)` = `DcmSchemaManager.CreateSchema(name, DefSchema, uc, uid, false)` (the package's own precedent keeps creation in the repository, `ProcessSchemaRepository.cs:65-68`); `SaveSchema(item, uc, true, true)` in `try/finally ReleaseResourceCache(uid)`; `RemoveItemByUId` for compensation; pattern of `ProcessSchemaRepository` [traced PB/Schema/ProcessSchemaRepository.cs:184,189,336,371] | `DcmSchemaRepositoryTests` with `[MockSettings(RequireMock.DBEngine \| RequireMock.DCM)]` (v r8-C1); saved item read from the `InternalSaveSchema` received call (v r8-C2); success and failure through `NSubstituteExtension.Protected(...).Returns(...)` (v r8-C7); `CreateItem` through `TestDcmSchemaManager` / `ForPartsOf` (v r8-C3); cache released on both paths |
| P-C2 | `DcmSettingsWriter` (Q13, Q14, Q15): find `SysDcmSettings` by the platform reader's predicate (object OR `SysModuleEntity`); create / reuse / `settings-conflict`; object form, `Filters = "[{\"columnUId\":null}]"`; link row `SysDcmSchemaInSettings` with the PLANNED DCM UId (plain Guid column [traced, judge PS CrtBase/Schemas/SysDcmSchemaInSettings/metadata.json:21-28]); bindings `SysDcmSettings_<id:N>`, `SysDcmSchemaInSettings_<id:N>` through `IPackageSchemaDataCreator`, then read `SysPackageSchemaData` back (validation failures return silently, v r4-C7); compensation deletes bindings (`DeletePackageSchemaData`) then rows | `DcmSettingsWriterTests` (`QueryAssert.Got<Insert>`, substituted binder adapter, r8 §3.7): row columns, reuse writes the link row only, names unique per row, missing read-back = failure, compensation order |
| P-C3 | `CaseVerifier` (D §5 V1-V2): item registered, root enabled, `GetIsActiveVersion(item.Id)`, `EntitySchemaUId`/`StageColumnUId`, stage count/order/`StageRecordId`/colour, connection count; `IEnabledCaseReader.FindEnabled(entity.UId)` returns exactly ours | `CaseVerifierTests` (substituted `IEnabledCaseReader`): disabled, not active version, stage mismatch, competing case each -> failure |
| P-C4 | `IEnabledCaseReader` (DI-registered package adapter: `FindEnabled(entityUId)`, `FindDisabled(entityUId)`) implemented over `new DcmEntityUtilities(uc).FindEnabledDcmSchemas(...)` (or the D0.3 fallback); the core class is sealed, its interface internal and it calls the internal `GetIsActiveVersion(item)`, so it cannot be driven by substitutes and is covered only by E2E (TC-I-02/TC-I-06). Equivalence and existing-case check (Q12) on top of it: equivalent = same object, column, ordered `StageRecordId` list, link row present; disabled DCMs -> note N5 (D §5 S7, D §9) | `CaseBuildHandlerTests` (P-D1) with a substituted `IEnabledCaseReader` |

### 3.4 Slice D: handler, operation, composition, docs

| Task | Component | Definition of done |
|---|---|---|
| P-D1 | `CaseBuildHandler.Build`: S1..S21, W1..W4, V1..V2 in the order of D §5 (Q7: settings row, link row, bindings + read-back, schema save LAST, verify); process-local lock keyed by entity UId between S7 and V2; `ValidateOnly` answers `planned` and writes nothing; three failure outcomes `refused` / `failed-rolled-back` / `failed-leftover` with the leftover list (D §5 "Failure outcomes"); notes N1..N5 never contain the compile marker phrase | `CaseBuildHandlerTests` cover every line of D §14.1's handler row, including `Received.InOrder` for W1 -> W2 -> W3 -> W4 and "no read when the gate refuses" |
| P-D2 | `ProcessDesignService.BuildCase` (Q2 recommended: ninth operation on the existing service): `[OperationContract]` + `[WebInvoke(POST, Json, Wrapped)]` like the eight existing ones [traced, judge PB/EntryPoints/WebService/ProcessDesignService.cs:39-46]; `ProcessDesigner` facade method; DI lines in `CrtProcessBuilderApp.cs`; the service's doc comment names both gates | `ProcessDesignServiceTests` (op delegated), `ProcessDesignServiceWireContractTests` mirror 8 -> 9 (`5 + 2 + 1` arithmetic re-stated, r7 §1.4), `CrtProcessBuilderAppTests` tripwire. If Q2 picks a sibling `DcmDesignService`: a NEW mirror test for it (the existing one reflects only `ProcessDesignService`, v r7-C3) and a route measurement on the stand before the clio route is registered |
| P-D3 | Docs: `docs/dynamic-case-capture.md` (the stored shape the operation writes, the settings rows and bindings, what the designer adds on its own re-save after M1, the M5/M6 results); `docs/process-builder-architecture.md` (the new handler family and the `EnsureCanManageDcm` gate); package `.ai/specs/ENG-105556-minimal-dcm.md`; package `AGENTS.md:3-14` operations-and-gate paragraph (it says every operation is gated by `CanManageProcessDesign`, which `BuildCase` makes false) [traced pb `AGENTS.md:7-10`]; the mandatory `.codex/workspace-diary.md` entry [traced pb `AGENTS.md:288-297`]; `CONTEXT.md` glossary terms "case" and "stage"; a `docs/adr/` record for the hard-to-reverse choices (route A, rows-first write order, the `CanManageDcm` gate) (pb `docs/agents/domain.md`: glossary and ADRs ship in the task PR) | written; every stand statement dated and tagged measured, everything else traced |

### 3.5 Experimental cut and STOP gate

Experimental cut (section 7), stand-only, claimed and marked "experimental, superseded" at the claim place (D0.7).
Installed on the read-write stand of D0.6 with the owner's yes (an install is a configuration build plus restart
for every user of that stand, v r7-C2). Through a direct call of the operation (clio is not ready yet: a
`call-service` POST to the route with the wrapped body), strictly sequential:

| Measurement | Definition of done |
|---|---|
| M1 | owner opens `<root>[/0]/Nui/ViewModule.aspx?vm=DcmDesigner#case/<uid>`: stages in the planned order, captions from the lookup, no error box, no reset stage; designer re-save keeps one binding per row (Q15) - result recorded with date |
| M2 | `DcmSchemaManagerRequest` right after the call: the case is listed with `IsActive && IsActiveVersion` (g1 open item) |
| M4 | a non-admin user with `CanManageDcm` + `CanManageSolution` + settings-table append: the gate passes and every write succeeds; without one of them the gate refuses before any read (Q5) |
| M5 | measured directly: save a case through the experimental cut, remove it through the manager path (`delete-schema` reaches `DcmSchemaManager.RemoveItemByUId`, facts §3.6), then probe `SysEntityPrcStartEvent` by the `SysSchema` Id and the flow schema by name - listed, and `failed-leftover` wording checked against it. (A pre-created second enabled case cannot reach V2: S7 answers it with `case-exists` / `already-present` before any write, D §5.) The orphan-link probe of D §5 "Failure outcomes" is checked on the same stand by reading `SysDcmSchemaInSettings` rows whose `SysDcmSchemaUId` has no `SysSchema` |
| M6 | both `SysPackageSchemaData` bindings land in the requested package under the caller (r4 open question 1) |

Failure of M1 or M6 = STOP (section 0). M5 residue that the platform does not clean = a knowledge record (section 8.7)
and the `failed-leftover` cleanup list in the guidance, not a stop.

### 3.6 Package PR

the internal `crt-process-builder` repository, base `main`, title as the BMAD PR. Body: what changed, the D0.3 API table, the
STOP-gate results with dates, the claimed cut, test counts on dev-nf and dev-n8, the security surface stated
explicitly (operations 8 -> 9; `EnsureCanManageProcessDesign` call sites unchanged; one new `EnsureCanManageDcm` call
site), "draft until the BMAD PR merges". Gate 1 (full fan-out: rights code and shared repository interface change)
before the draft opens. Pushing the branch, opening the PR and requesting reviewers each wait for the owner's word.

---

## 4. Phase 3: knowledge (clio-knowledge)

Fresh worktree, branch `feature/ENG-105556-minimal-dcm` from a fresh `origin/master`. Pushing and the draft PR wait
for the owner's word.

| Task | What | Definition of done |
|---|---|---|
| KB-1 | New article `guidance/mcp/guides/processes/dynamic-case.md` (Q23): when to use; prerequisites (object published, Lookup column, values exist, editable package); the call and its arguments; the order rule and "pass `stages` when the meaning matters" (display order turns New, In progress, Done into Done, In progress, New, g6 §4) with `dry-run` as the preview; what one call creates; notes N1..N8 and what to tell the user; refusals and remedies; verification (F5, `designerUrl`, existing records start on their next stage change, N3); compile/restart truth per runtime (".NET 8: expected" until M8); transport (lookup values by Id, N4); undo order (D §9), conditional on `settings.status`: the settings row and its binding are removed only when this call reported `created`, never when `reused` (a reused row may carry other cases' link rows, which the delete cascades away); NOT supported yet (D §16); troubleshooting line for the CrtNUI kill switch `DisableEntityStageProgressBarPreprocessing` (D §17); Academy links (D §13). No banner (`Part of the process guide set.` absent: `process-modeling` has no room, r7 §5.3 item 4) | article reviewed against the final clio contract; no Jira key, PR link or `repo#N` (`InternalTaskReferenceTests`); no `<...TBD>` version placeholder (KB `CONTRIBUTING.md`) |
| KB-2 | `bundle-source.json`: full resource entry on the model of `process-send-email-attachments` [measured: keys itemId, title, description, topicId, role, uri, sourcePath, bundlePath, mediaType]: itemId `process-dynamic-case`, `title`, `description` (the catalog purpose an agent reads before choosing a guide), `topicId: creatio.process-dynamic-case`, `role: guidance` (the `topicId` + `role` pair unique), `sourcePath guidance/mcp/guides/processes/dynamic-case.md`, `bundlePath resources/process-dynamic-case.md`, uri `docs://knowledge/com.creatio.clio/process-dynamic-case`, `mediaType: text/plain`, no `legacyUris`, and `process-dynamic-case` APPENDED to the hand-written `PostMigrationGuidance` list in `automation/Clio.Knowledge.Bundle.Tests/GuidanceMigrationTests.cs` (any `role=guidance` resource not on it must declare exactly one legacyUri) [traced KB/automation/Clio.Knowledge.Bundle.Tests/GuidanceMigrationTests.cs:25-45,228-245]; `requirements.itemIds` + `requirements.resourceUris`; `libraryVersion` bumped above master AND every open branch claim, re-checked at merge; no `sequence` field (derived, `_verdicts.md` conflict 5) | manifest tests green |
| KB-3 | `routing.md`: a Business processes row ("dynamic case / DCM / stages from a lookup column / stage progress bar -> `name=process-dynamic-case`") and a Pages pointer ("a stage progress bar needs a case -> `name=process-dynamic-case`") (D §13) | `ProcessGuideCrossReferenceTests` (every `name=` resolves, every process article is routed); routing size under `TheRoutingArticle_ShouldFitInOneGetGuidanceResponse` (r7 §5.2) |
| KB-4 | `process-modeling.md:157-158`: drop "DCM, " from the "carry their own compile obligations" list [traced KB/guidance/mcp/guides/processes/process-modeling.md:157-158] (v r1-C1); byte-neutral or smaller (about 13 B headroom, v r7-C8) | inline budget test green; the new article owns the "a case needs no compile" rule, no duplicate elsewhere |
| KB-5 | Test class `ProcessDynamicCaseGuidanceTests` (precedent `SendEmailAttachmentsGuidanceTests`): no banner, routing row present, size under the inline budget, names `create-dynamic-case`, names the "not supported yet" list | green with `dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj` |
| KB-6 | Fact-check gate: one refute-first agent per claim cluster (rights, compile/restart, order rule, page placement, cleanup), given the source paths and the stand records, forbidden to cite `docs/knowledge/` or this spec folder as evidence; every refutation re-checked by hand; a statement resting on a traced-only claim names its M or is removed | refutations resolved; the fact-check summary in the PR body |
| KB-7 | Agent scenario run on the draft article (the epic's "agent scenario run" sense, r7 §7): a clean-context agent with only shipped guidance and the clio branch build gets the CAADT prompt text (Phase 5) against the read-write stand | transcript kept; a failure is fed back into the article before the KB PR leaves draft |
| KB-8 | Side finding (not in scope, D §13): `guidance/mcp/guides/backend/package-dependencies.md:22-30` says clio adds a dependency by itself; recorded for the owner, not edited here | named in the PR body as a follow-up |
| KB-9 | Guidance sweep beyond `process-modeling.md` [measured grep KB/guidance for EntityStageProgressBar, "progress bar", DCM, "dynamic case"]: one sentence in `guidance/mcp/guides/pages/creation.md:11` saying the `PageWithTabsAndProgressBarTemplate` bar renders only when the object has an enabled case (`name=process-dynamic-case`), within the size gates; re-read `processes/versions.md:36-41` (dynamic case instances) and adjust only if it contradicts the new article; decide whether the `process-digest` routing card gets a "stages / status lifecycle -> process-dynamic-case" row (use-cases §1.2) or state why not, within the 3 KiB `ProcessDigestTests` budget [traced KB/CONTRIBUTING.md:263-277] | size tests green; the digest decision stated in the PR body |

---

## 5. Phase 4: clio

Fresh worktree from a fresh `origin/master`, branch `feature/ENG-105556-minimal-dcm`. Before CL-1 read
`CLIO/docs/agent-instructions/bundled-packages.md`; before any edit, the records of D0.4. `CLIO*` diagnostics stay
clean in modified files; new public members carry `///` docs on the interface; behaviour classes resolved through DI
(no `new`).

| Task | What | Guard that bites / Definition of done |
|---|---|---|
| CL-1 | Rebundle the final cut: `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <package checkout> -Version <claimed>`; `-Version` must be higher than the version in the committed archive (the script refuses otherwise, bundled-packages.md:359-363); the four provenance pins written by the script; IN THE SAME COMMIT by hand: `ExpectedOperationContractCount` 8 -> 9, a NEW pin for `EnsureCanManageDcm` call sites = 1 (the existing gate pin counts only the literal `EnsureCanManageProcessDesign` and stays as is), the constants' arithmetic remarks, `ExpectedSchemaDescriptorModifiedOnUtc` checked (bundled-packages.md:555-585) [traced, judge CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:292,361,373,384]; in the SAME commit the documents that describe the rebundle procedure: a pin row in `docs/agent-instructions/bundled-packages.md:563-575`, clio `AGENTS.md:149-158` ("three" hand-maintained security counts, naming the new one), and the remarks on `ExpectedAuthorizationGateCallSites` (`BundledProcessBuilderPackageTests.cs:298-358`, which today say "Adding a gated operation must raise this count") explaining why a non-`CanManageProcessDesign` gate gets its own pin; rebuild clio afterwards (an install reads the archive from the build output) | `BundledProcessBuilderPackageTests` green; a future rebundler following any of the three documents sees the new pin; commit message names what was run |
| CL-2 | Route: new `KnownRoute.BuildCase` at the next free value after rebase (D0.2), `/rest/ProcessDesignService/BuildCase` in `KnownRoutes` | route tests for .NET Framework (`0/` prefix) and .NET Core |
| CL-3 | `CreateDynamicCaseOptions` (MCP-only, no `[Verb]`), `[RequiresPackage("CrtProcessBuilder", "<claimed cut>", Hint = ...)]` (Q22: the first archive with `BuildCase`, above every deployed build) with the floor rationale paragraph in the style of `CreateBusinessProcessCommand.cs:17-298` (r7 §1.4); kebab-case keys of D §3.1 | `ProcessDesignerRequiresPackageAttributeTests` |
| CL-4 | `DynamicCaseService` (shape of `CreateBusinessProcessService`): wrapped body, validate pass then build pass, envelope parse, "outcome UNKNOWN" wording on an unreadable reply, 404 / non-envelope -> `process-builder-without-build-case` (D §3.2, v r10-C2) | `DynamicCaseServiceTests` (D §14.2 list) |
| CL-5 | `RecordPageResolver` (Q20, D §7.1) over `IRelatedPageAddonService.Get` and a `SysModuleEdit` ESQ fallback; returns a list internally; `DefaultPageAddonReader.ReadWebDefaultPageUId` / `RelatedPageAddonMetadata.TryFindUntypedDefault` (internal, mobile converter) NOT reused (g4 §6) | `RecordPageResolverTests`: untyped default, typed-only `Activity` shape, portal-first `Case` shape, empty add-on + Freedom `SysModuleEdit`, Classic card, explicit override |
| CL-6 | Page checks and fragment (Q17, Q18, Q19, D §6 C3, D §7.2-7.3): view checks on `PageGetCommand.TryGetPage(...).Bundle.ViewConfig`, data-source check on `Bundle.ModelConfig` (`PageMergedConfigResolver` returns no view tree); then `PageUpdateCommand.TryUpdatePage` with `DryRun = true`, `Mode = append` and the exact fragment BEFORE the dependency add and the build pass, its failure mapped to a page refusal; `ProgressBarFragment` is a marker-wrapped append body (`define(...)` with `/**SCHEMA_VIEW_CONFIG_DIFF*/` pairs; app-wizard values, literal entity name, never `recordId`/`value`/`stages`/handlers), the same string in `page.fragment` and `retry.args`; name collision -> `_<8 hex>` suffix on both names; `IPageDataSourceReferenceValidator` then `PageUpdateCommand.TryUpdatePage` in append mode into the default design package, inside the tool's call (tenant lock already held, re-entrant [traced, judge CLIO/clio/Command/McpServer/Tools/BaseTool.cs:216-238]); failure -> `created-page-failed` with `retry` args for `update-page` and the fragment | `ProgressBarPlacementTests` (exact marker-wrapped fragment, forbidden keys absent, bar present -> skip, mini page -> refusal, PDS mismatch -> refusal, name collision -> suffix, append dry-run failure -> refusal before the build pass) |
| CL-7 | Dependencies (Q16): validate pass and dry-run send `AllowMissingDependencies = add-package-dependencies`, the build pass always `false` (fails closed if an add did not take); `IPackageDependencyManager.AddDependencies(P, missing)` between the passes; a storage validation error -> `dependency-cycle`, build pass never called; when the build pass answers anything other than `created` / `already-present`, the added dependencies are removed with `RemoveDependencies(P, added)` [traced CLIO/clio/Package/IPackageDependencyManager.cs:88] (`dependencies.status = removed-after-failure`), or, if that fails, reported as `left-after-failure` with a warning and no "nothing written" claim (D §3.2, §6 C6); `add-package-dependencies=false` -> the server's `dependencies-missing` refusal with the exact `add-package-dependency` calls | `DynamicCaseServiceTests`: both flag values pinned per pass; build-pass refusal after an add -> dependencies removed; `failed-rolled-back` after an add -> removed; removal failure -> `left-after-failure` |
| CL-8 | `CreateDynamicCaseTool` (Q3, Q4, Q28): long-tail, `clio-run-destructive`; annotations `ReadOnly=false`, `Destructive=true`, `Idempotent` per Q12, `OpenWorld=false`; `[McpToolExecution(Location=Worker, Lifetime=PerCall)]`; environment-aware `InternalExecute<CreateDynamicCaseCommand>`; argument handling copied from `CreateBusinessProcessTool` (null args, `[JsonExtensionData]` unknown keys, required mini contract, `stages` as array or JSON string) [traced CLIO/clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs:32,372-395,455-456]; result shape of D §3.3; `WithCompileNotRequiredNote()`; no `[FeatureToggle]` if Q28 (a). **[Description] text** (ASCII, within budget), on the model of the process-designer tools that name their article (`CreateBusinessProcessTool.cs:135,346`): "Creates a minimal dynamic case (DCM) for an object: one stage per value of a lookup column, in a stated order, plus the stage progress bar on the Freedom UI record page. Read get-guidance name=process-dynamic-case first. Steps inside stages, stage conditions and changing an existing case are not supported." | `CreateDynamicCaseToolTests`; `CompileNotRequiredNoteGateTests`; `McpToolArgsWireContractTests`; `ProcessDesignerGoLiveTests` extended with `CreateDynamicCaseTool` and its tool name (no `[FeatureToggle]`, resolvable by default) [traced CLIO/clio.tests/Command/McpServer/ProcessDesignerGoLiveTests.cs:28-48,61,80] |
| CL-9 | Discovery and budgets: one index line (D §3 "Index line"); `PassthroughToolClassificationRegistry` entry; full contract under `MaxToolContractSerializedBytes`; the line index has 120 B left for exactly one line (v r7-C4); the object index (49,209 / 49,408 B) may need a deliberate re-pin, measured on the branch | `ToolContractPayloadBudgetTests`, `ToolContractShortFormTests` green; re-measured numbers in the PR body; ASCII only in the description |
| CL-10 | DI in `BindingsModule.cs` (tool command, service, resolver, fragment provider) | full unit suite (rule 4: `BindingsModule.cs` changed) |
| CL-11 | MCP E2E `clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs` (test plan TC-I-01..TC-I-14; TC-I-12 optional, TC-I-13 `LocalOnly` twin only), `[Category(McpE2ECategories.ProcessDesigner)]`, `[NonParallelizable]`, `MinimumPackageVersion` = the claimed cut, old package -> `ProcessDesignerE2ESupport.IgnoreUnlessProcessBuilderIsRequired` (so the lane fails instead of ignoring, r7 §6.2); interim arrange `LocalOnly` + `[Explicit]` + `McpE2E.Manual` with `sync-schemas` (adding a lookup column publishes, which `clio.mcp.e2e/AGENTS.md:19-38` puts in the developer-local tier) until the seeded object of Phase 5 ships; cleanup in `finally`, strictly sequential (section 11) | `McpE2eSelectionCoverageTests` green; run by hand on the read-write .NET Framework stand and on the .NET 8 stand with Ignored = 0, output in the PR |
| CL-12 | Docs (section 8.2): `clio/docs/commands/install-process-builder.md` and `clio/help/en/install-process-builder.txt` tool lists (the "MCP tools with no CLI verbs" list [traced CLIO/clio/docs/commands/install-process-builder.md:22-24]); `docs/McpCapabilityMap.md` row (no automated guard exists for it, `docs/knowledge/McpServer/mcp-capability-map-has-no-automated-guard.md`) | rows match the tool's annotations and arguments |
| CL-13 | Knowledge records (section 8.7) in this PR | `make check-knowledge` report read |
| CL-14 | `CreateDynamicCaseCommand` behind an interface, registered in DI: runs C1..C8 (D §6) and maps each outcome to `success` / exit code / MCP `isError` / warning text per D §6.1 (`created-page-failed`, `failed-rolled-back`, `failed-leftover`, `already-present`, `refused`, `planned`, unreadable reply) | `CreateDynamicCaseCommandTests` on `BaseCommandTests<CreateDynamicCaseOptions>`, command resolved from DI in setup, substitutes registered in `AdditionalRegistrations`, received calls cleared in teardown (clio `AGENTS.md` "Command tests"; precedent `clio.tests/Command/CreateBusinessProcessCommandTests.cs:17-19`): one test per D §6.1 row, notes and warnings printed |

Validation: `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" --no-build < /dev/null` (full
unit suite: `BindingsModule.cs` and more than three modules change; `< /dev/null` because clio.tests git calls hang on
inherited stdin); the targeted filter
`dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&(Module=McpServer|Module=Command|Module=Common)" --no-build`
for quick loops. The `Validated:` line in the PR body names the full-suite command.

The clio PR stays draft until the KB PR merges and its generation is published. The re-pin of
`clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` (libraryVersion, sequence, availableNames) is
MANDATORY: the CL-8 description names `process-dynamic-case`, and
`WorkspaceTemplateGuidanceDriftTests.UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` fails on
any named guide absent from `availableNames` [traced CLIO/clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs:540-588].
It is a commit made after the KB generation is published (merge order, section 7). If KB publishes after the clio PR
is approved, the re-pin goes into a separate PR instead of a push (approvals are kept).

---

## 6. Phase 5: E2E seed object and CAADT

| Task | What | Who | Definition of done |
|---|---|---|---|
| E-1 | SVN `AutoTestClioMcp` (branch 7.8.0) per Q25 (a): an object with a Lookup column, a lookup with 4 rows whose integer `Position` order differs from display and `CreatedOn` order, a tabbed Freedom UI record page (`PageWithTabsFreedomTemplate`) with a related-page add-on; no DCM | lead prepares the package diff in a local working copy; the OWNER commits | ships with the next ProcessDesigner-lane deploy [measured g5 §2.1]; the E2E fixture switches from `LocalOnly` to the lane in a follow-up clio commit after it ships |
| E-2 | CAADT prompt `creatio-adaclio-testing/prompts/bpms-tools/<name>.yaml`: business-level, no tool names (D §14.4), `expected`, `playwright`, `cleanup` blocks (r7 §7); optional `scheduler-matrix.yaml` entry | lead drafts; push and draft PR on the owner's word | prompt passes the harness rules (business-level, no MCP recipe, `HARNESS/docs/caadt-automation-guide.md:99-110`) |
| E-3 | Playwright spec under `creatio-playwright-tests/tests/_generated/features/caadt/specs/`: reload, bar shows the captions in order; click the second stage -> reload -> the field shows it; set the field and save -> bar marks it; open `vm=DcmDesigner#case/<uid>` -> no error box, stages listed; tags `@tm_bpms-tools`, `@clio-mcp` | lead drafts; owner's word for push/PR | spec green on the .NET Framework stand; a local run against the .NET 8 stand (the matrix has no runtime field, g5 §5), output kept |

---

## 7. Cuts, rebundle, merge order

- **Claim before stamping:** `git ls-remote --tags` of crt-process-builder and the version in clio's committed
  `clio/CrtProcessBuilder/CrtProcessBuilder.gz`; take the first number above both AND above every build deployed on
  the stands this work touches (Q22: the stand already runs a feature build above main, v r10-C2); claim it at the
  D0.7 place. Each claim edit, tag push and branch push happens only on the owner's explicit yes.
- **Cut procedure** (bundled-packages.md "One call"): run `rebundle-process-builder.ps1` from the clio checkout with
  the package checkout on the PRE-stamp commit; `-Version` is required and must go UP; the script stamps
  `descriptor.json` (PackageVersion and ModifiedOnUtc together), runs the package tests, packs, checks the inventory
  and writes the four provenance pins; then commit the restamped descriptor in the package, tag the producing commit
  `crtprocessbuilder-<version>`, push the tag; rebuild clio. If the package branch already committed its own stamp,
  follow "Without the script" from the stamp commit (bundled-packages.md:364-401), never raise `-Version` to get past
  the refusal.
- **Expected cuts:** experimental (STOP gate, stand-only, marked superseded), mid (after gate 1, for the clio branch
  and E2E), final (after review rounds); superseded numbers are never reused. Every rebundle makes every gated
  process tool refuse environments still on the previous archive (v r7-C2): reviewers and stands are brought up
  together, and the release note says so.
- **Installs:** every install on a shared stand needs the owner's yes (configuration build + restart for every
  user). Installing on stand `Creatio` replaces another branch's build (D0.6).
- **Merge order (human merges only):** BMAD docs PR first; then package (tag on the remote) -> clio-knowledge
  (publishes on merge, r7 §5.3 item 6) -> clio (with the mandatory `curated-knowledge-names.json` re-pin to that
  generation) -> CAADT prompt and spec (and the toolkit PR, if D0.9 found one). The SVN seed object (E-1) may land at any
  time; the E2E stays `LocalOnly` until it does. Approvals are kept: no push after an approval without asking (a GHE
  push dismisses approvals).

---

## 8. Policy obligations (clio `AGENTS.md`)

### 8.1 MCP review

| Surface | Result expected | Definition of done |
|---|---|---|
| `clio/Command/McpServer/Tools/ProcessDesigner/` | new `CreateDynamicCaseTool` (CL-8) | annotations match behaviour (destructive; idempotent only per Q12) |
| `clio/Command/McpServer/Prompts/` | **decided now: no `CreateDynamicCasePrompt`.** The tool takes four flat required arguments and no descriptor, so a prompt would only restate the [Description] and the `process-dynamic-case` article, which owns the workflow; the element-level process tools without descriptors have none either [traced ls CLIO/clio/Command/McpServer/Prompts] [inferred]. A prompt is added in the later `describe/modify-dynamic-case` task when a descriptor exists. `PagePrompt.cs` checked for a progress-bar sentence that would now point at the wrong route | reason stated in the PR body; `ProcessDesignerGoLiveTests` prompt list unchanged |
| `clio/Command/McpServer/Resources/` | none expected | "reviewed, no update required" |
| `clio.tests/Command/McpServer/` | CL-4..CL-9 tests | green |
| `clio.mcp.e2e/` | CL-11 fixture (mandatory for a new tool) | green by hand, Ignored = 0 |
| `clio/tpl/**` | templates name no process tool (r7 §3); no change unless a template starts naming the tool | `WorkspaceTemplateGuidanceDriftTests` green |
| `McpToolCompatibilityCatalog` | no tool renamed or removed | none |

PR body: "MCP reviewed" with the surface list above.

### 8.2 Command documentation

The tool is MCP-only (no `[Verb]`), like every process-designer tool (r7 §1.5): `clio/help/en/<command>.txt`,
`clio/docs/commands/<command>.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` -> "docs reviewed, no update
required"; `install-process-builder.md` / `.txt` tool lists and `docs/McpCapabilityMap.md` ARE updated (CL-12);
`clio/tpl/workspace/AGENTS.md` and `clio/tpl/ui-project*/AGENTS.md` describe no process workflow -> reviewed, no
update. `update-page` and `get-page` are reused in code only; their docs do not change (no behaviour change).

### 8.3 ClioRing compatibility gate

`grep -rlni "business-process|update-page|ProcessDesign|dynamic-case"` over `clio-ring` `*.cs` / `*.json` at
`69788ccd5` returned nothing [traced grep 2026-11-08]; r7 §8 found the same. That search cannot see the DISCOVERY
surface Ring does consume: it calls `get-tool-contract` with `detail=index-entries` and parses every entry into
`ClioCatalogEntry` (Name, Purpose, ContractAvailable, Resident, Destructive) [traced CLIO/clio-ring/ClioRing.Ipc/ClioIpcClient.cs:188,200;
CLIO/clio-ring/ClioRing.Ipc/ClioIpcModels.cs:77-95]; the new tool adds an entry there and CL-9 may re-pin the object
index. "Tool discovery/contract output" is a gate trigger [traced CLIO/AGENTS.md:288]. So: add `get-tool-contract` to
the inspection search of `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, `clio-ring/ClioRing.Desktop/actions.json`;
run `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` and the Windows x64 NativeAOT publish
against the branch build; show that `ParseCatalog` accepts one more long-tail destructive entry (a Ring test or the
harness path). Statement in the PR: "ClioRing compatibility reviewed" with the commands and results.

### 8.4 Telemetry vocabulary

No event name in `CanonicalEventNames` / `LegacyAppCreationEventNames` [traced CLIO/clio/Common/Telemetry/TelemetryService.cs:146]
and no wire attribute changes; the CAADT run's flow travels in the free `workflow` field (r7 §8). Statement: "telemetry
vocabulary reviewed: no event name or attribute changed, the collector needs no change". If a reviewer asks for a new
event, that is a collector PR first (four copies, `check-vocabulary-sync.ps1`).

### 8.5 Feature toggle

Q28 (a): none. If the owner picks (b): the same `[FeatureToggle("<key>")]` on `CreateDynamicCaseOptions` AND the
tool class, the KB article declares `requiredFeatures` (allowed because it has no banner, v r7-C8), and the E2E
enables the flag in arrange.

### 8.6 Analyzers, DI, tests

`CLIO*` clean in every modified file; no `new` of behaviour classes; every test AAA + `because:` + `[Description]`,
OS-neutral, no Jira key in names or descriptions; command tests on `BaseCommandTests<TOptions>` where a command
class is tested.

### 8.7 Knowledge-base records (clio `docs/knowledge/`, in the clio PR)

Only facts the code does not say; each with literal clio `applies-to` paths (no globs; the platform facts concern
package code in another repository, so the clio paths are the ones that RELY on them), `ticket: ENG-105556`, date, and
the three paragraphs. One fact per file; an existing record on the same subject is extended, not duplicated.

| Record (slug) | Category | Fact | Evidence | `applies-to` |
|---|---|---|---|---|
| `a-dynamic-case-needs-no-compile-or-restart` | platform | a DCM is interpreted (`IsInterpretable` forced true, empty sources, ignored by `HasCompilableSchemas`); the old guidance sentence was wrong | v r1-C1, v r6-C2 | `clio/Command/McpServer/Tools/ProcessDesigner/CreateDynamicCaseTool.cs`, `clio/Command/CreateDynamicCaseCommand.cs` |
| `the-stage-progress-bar-reads-the-first-active-case-not-dcm-settings` | platform | the 8.x bar takes the stage column from the first enabled, active-version DCM of `entityName`; `SysDcmSettings` matters for the 7.x designer and other panels | `_verdicts.md` conflict 1 | `clio/Command/McpServer/Tools/ProcessDesigner/ProgressBarFragment.cs`, `clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs` |
| `a-case-opens-in-the-designer-only-with-its-settings-link-row` | platform | open-by-UId needs a `SysDcmSchemaInSettings` row whose `SysDcmSchemaUId` equals the opened UId, else "DcmSettingsId not found." | v r2-C4 | `clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs` |
| `stage-clicks-need-full-mesh-connections` | platform | the bar disables stages with no connection from the current one; core and `SaveSchema` fill none | `_verdicts.md` conflict 7 | `clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs` |
| `a-null-stage-colour-crashes-the-freedom-ui-bar` | platform | null crashes, empty does not; designer writes `#0C8701` | conflict 6 | `clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs` |
| `deleting-a-case-keeps-its-dcm-settings-row` | platform | the `SysSchema` delete trigger removes link rows only and nulls `DefaultSchemaUId`; settings row and both bindings stay | g5 §4 | `clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs`, `clio/Command/DataBindingDbCommand.cs` |
| `a-same-name-package-data-binding-replaces-the-data` | platform | a second binding with the same name in the same package silently drops the first one's rows | v r4-C8 | `clio.tests/Common/BundledProcessBuilderPackageTests.cs` |
| EXTEND existing `Tests/process-designer-e2e-runs-on-a-studio-stand.md` (no new record) | Tests | the lane deploys Studio .NET Framework only; a .NET 8 run needs a `StudioNet8` zip and a hand-parameterised deploy | g5 §2; [traced CLIO/docs/knowledge/Tests/process-designer-e2e-runs-on-a-studio-stand.md:1-25] | the existing record's paths plus `clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs` |
| `remove-item-by-uid-residue` (name after M5) | platform | what a DCM delete through the manager leaves (start-event rows, flow schema) | M5, only if residue is measured | `clio/Command/CreateDynamicCaseCommand.cs`, `clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs` |
| `a-clio-only-case-write-was-rejected` | ProcessModel | rejected alternative (Q1 b): clio driving the designer endpoints needs the design-JSON dialect, 4-6 non-atomic writes, and duplicates the package's element handlers for the later steps task | decisions Q1 | `clio/Command/McpServer/Tools/ProcessDesigner/CreateDynamicCaseTool.cs` |

The package keeps its own capture doc, diary entry, glossary and ADR (P-D3); these records are clio-side because
clio's tool, E2E and cleanup rely on them. File names in `applies-to` are the planned ones; they are re-checked against
the branch before the commit (`make check-knowledge` flags a path that does not exist).

---

## 9. Review gates (clio `AGENTS.md` "Code review")

| Gate | When | Scope |
|---|---|---|
| 1 | before each PR opens (BMAD, package, KB, clio, CAADT) | full 3-lens fan-out over the complete diff: quality and maintainability; performance and correctness (write order, compensation, lock, idempotency); security (rights gate before any read, no name probing, settings-table rights, no secret in results or fixtures); KB adds the KB-6 fact-check; the tier and the reason are stated in the PR thread |
| 2 | each commit pushed to an open PR | skip for restamp, rebundle, docs, fixtures; one combined lens for a small single-module diff (< ~50 lines); full fan-out for the guard, the repository interface split, the handler's write order, `BindingsModule.cs`, `clio/Common/**`; only Blocker/High block |
| 3 | before ready-for-review | one comprehensive adversarial review per PR over the whole diff; Medium/Low from gate 2 addressed or recorded as follow-ups; story rows move to `review` |

Findings are read from both the review threads and the review body. Reviewers are requested only when the owner asks.

---

## 10. Stand verification

Every write on any stand (installs, operation calls, E2E, CAADT, data seeding) needs the owner's yes, one step at a
time; schema writes run strictly one after another (a parallel burst takes a .NET Framework app pool down,
`ProcessFileElementToolE2ETests.cs:40-41` per g5 §4). Browser checks are done by the owner; the lead prepares the
URLs and the expected picture. Names of everything this task creates on a stand carry one prefix (`UsrDcmT`) so the
leftovers register can be checked by prefix.

| Step | .NET Framework (read-write stand of D0.6; stand `Creatio` only on the owner's word) | .NET 8 (stand of D0.5) | Definition of done |
|---|---|---|---|
| V-1 | read-only baseline: `describe-environment`, `list-packages filter=CrtProcessBuilder`, `Feature` rows for DCM toggles (v r10-C3), `AutoAddPackageDependenciesInProcesses` (M9), no enabled DCM on the target object | same | recorded with dates |
| V-2 | experimental cut install + STOP gate M1, M2, M4, M5, M6 (section 3.5) | M1, M2, M6 on the final cut | results in the package capture doc |
| V-3 | clio branch build: `create-dynamic-case` `dry-run`, then write, then the equivalent re-run (`already-present`), then a different order (`case-exists`) | same | results match D §14.3 cases 1-4 |
| V-4 | owner in the browser: F5, the bar shows the stages in order (M3); click a stage -> the field changes; change the field and save -> the bar moves; designer by URL opens with no error (M1); the 8.x route too: record page in the Freedom UI page designer -> select the bar -> "Set up cases" (`SectionCasesWizard`) lists the case as active with the stage column and opens it with the stages and no error; `UseDcmForGeneralObject` value from V-1 recorded next to it (traps UIX-4) | same (M8) | screenshots or the owner's written confirmation in the PR |
| V-5 | runtime: insert a record with the first value -> a running case instance; update the column on an existing record -> a case starts (N3); after the final stage a qualifying update starts a new one (M7) | same | instance rows read through `ProcessDesignerStoredReads`-style reads; N3 wording confirmed or corrected |
| V-6 | M10: does a package-dependency add mark anything for compile (`compile-creatio` state, `SysPackage` flags) | same | obligations of the dependency artifact confirmed or a compile note added |
| V-7 | E2E fixture by hand (CL-11), Ignored = 0; CAADT local run (E-3) | same | outputs attached to the clio PR |

Acceptance on .NET 8 is the E2E + CAADT + V-4 on the .NET 8 stand (Q24 (a)); until then the PR bodies, the result
notes and the article say "expected" for .NET 8.

---

## 11. Cleanup

Proposed at the end, run only on the owner's yes, strictly sequential (D §9, g5 §4). Stand `Creatio` answered HTTP
DELETE with 405 in an earlier task of the epic (ENG-95985 "Send email attachments" plan :195, recorded as M-09 in
spec/eng-92710-call-web-service-element measurements), so data rows that `remove-data-binding-row-db` does not cover go
through `execute-dataservice-batch`; the verb is unknown on the read-write and .NET 8 stands (traps STD-4).

| Order | Artifact | Tool |
|---|---|---|
| 1 | `SysDcmSchemaInSettings_<id:N>` binding + its row | `remove-data-binding-row-db` (deletes the live row, then the empty binding) |
| 2 | `SysDcmSettings_<id:N>` binding + its row - ONLY when the call reported `settings.status = created`; skipped when `reused` (deleting the live row cascade-deletes every `SysDcmSchemaInSettings` row under it, including other cases' link rows, D §9) | `remove-data-binding-row-db` |
| 3 | the DCM schema | `delete-schema` remote (fires the delete trigger) |
| 4 | the page replacing schema, if this task created it; else revert the two inserted items with `update-page` | `delete-schema` / `update-page` |
| 5 | package dependencies added by the tool on a test package | `SavePackageProperties` through clio's dependency path, or drop the test package |
| 6 | E2E/LocalOnly objects, lookups and test packages (`UsrDcmT*`) | `delete-schema` (entity delete keeps the table) |
| 7 | the experimental and superseded cuts on shared stands: reinstall the stand's previous build if another branch depends on it (D0.6) | `install-process-builder` from that build, owner's yes |
| 8 | local: worktrees, throw-away spike (D0.3), `<research-dir>` notes after the BMAD PR merges | `git worktree remove`, file delete |

A leftover register (artifact, stand, id, created, removed) is kept in the package capture doc.

---

## 12. Definition of done

| AC | Done when |
|---|---|
| AC1 one call creates the case | `create-dynamic-case` with object + lookup column + package creates the DCM in P with one stage per lookup value in the reported order, `StageRecordId` = row Id, `#0C8701`, n*(n-1) connections, `Enabled`, active version; `SysDcmSettings` + `SysDcmSchemaInSettings` rows and both bindings in P; dry-run writes nothing; equivalent re-run writes nothing; refusals of D §3.2 write nothing (TC-I-01, 02, 06..11, 14) |
| AC2 opens in the designer | M1 passed on both runtimes: stages listed, no error box, no reset stage; designer re-save keeps one binding per row |
| AC3 progress bar | the record page carries the two inserted items in the default design package; after F5 the bar shows the stages; a click changes the lookup value and a field change moves the bar (V-4 on both runtimes; CAADT spec green) |
| AC4 runtimes and obligations | compile/restart not needed and reported per artifact (`compileRequired:false`, `restartRequired:false`, `reloadOpenPages:true`); M10 settled; E2E + CAADT + V-4 on the .NET 8 stand (M8); no "measured" claim for .NET 8 before that |
| AC5 tests | package suite green on dev-nf and dev-n8 plus the first CI run of the package PR; clio full unit suite; KB test project; MCP E2E by hand on both runtimes with Ignored = 0; CAADT prompt + spec green; every id of the BMAD test plan implemented or run: TC-U-PB (= TC-U, TC-P), TC-U-CL (= TC-C), TC-U-KB (= TC-K), TC-I (MCP E2E), TC-I-CA (= CA), TC-I-AR (= AR), TC-I-M (= M1..M10); manual TC-01..TC-13 passed by the owner |
| AC6 guidance | `process-dynamic-case` published, routed from `routing.md` (Business processes + Pages), says when/how and what is not supported yet; `process-modeling.md` sentence corrected; KB-6 fact-check passed; KB-7 agent run passed |
| general | gates 1-3 passed with their tiers stated; clio PR body carries "MCP reviewed", "docs reviewed, no update required" (four CLI targets), the ClioRing statement ("ClioRing compatibility reviewed" with the Ring test and NativeAOT publish results, section 8.3), the telemetry statement, the `Validated:` command and the re-measured budgets; knowledge records in the clio PR and the `make check-knowledge` report read; security pins moved in the rebundle commit; tracker rows moved as section 2 says; story DoD boxes ticked only when true; cleanup done or listed |

---

## 13. Risks

| Risk | Effect | Mitigation |
|---|---|---|
| A reused core member is absent or internal on the CI platform build (8.3.4) | package does not compile in CI although it builds locally | D0.3 before slice A; owner decides any fallback |
| Route-A case fails in the designer (M1) or bindings land elsewhere (M6) | acceptance AC2 fails; settings rows unbound | STOP gate after slice D's experimental cut; Q6 route B (`DcmSchemaDesigner.Save`) is the named fallback |
| `RemoveItemByUId` leaves residue after a failed save (M5) | `failed-leftover` with rows the platform does not clean | schema save LAST (Q7) so the common failures need only row deletes; residue listed in the result and the article |
| No .NET 8 stand (g5 §1) | AC4 cannot close | D0.5 requested on day 0; "expected" wording until M8 |
| Rebundle makes every gated process tool refuse older environments (v r7-C2); the stand runs another branch's build (v r10-C2) | reviewers and other branches blocked | claim above every deployed build (Q22); coordinate installs with their owners; release note |
| Discovery index has room for exactly one line (v r7-C4); object index near its ceiling | budget test red, forced re-pin | one tool only (Q3); re-measure on the branch; a deliberate re-pin stated in the PR if needed |
| `KnownRoute` collision with open branches (`_verdicts.md` conflict 4) | merge conflict or duplicate value | take the next free value after rebase, re-check at merge |
| KB `libraryVersion` collides with a parallel KB PR | re-pin after approval | bump above every open branch claim; re-check at merge |
| Record-page resolution picks a wrong page for typed or portal-first objects (g4) | bar on the wrong page | resolver refuses typed-only sets without `page-schema-name`, role filter, portal flag; checks before any write (Q20) |
| Display-order default yields a reversed business order (g6 §4) | wrong stage order, silently | `orderSource` and captions always reported (N2); guidance tells the agent to pass `stages`; `dry-run` preview |
| A second enabled case appears between check and save on a web farm | two cases start per record (v r1-C8) | process-local lock + post-save V2 check with compensation; farm gap traced only |
| CAADT/SVN changes live in three other repositories | E2E stays `LocalOnly`, CAADT lags the clio merge | E-1..E-3 drafted in parallel with the clio branch; the clio E2E does not depend on them |

---

## 14. Not known yet (and where it is settled)

| Unknown | Settled by |
|---|---|
| Presence of the reused core members on 8.3.4 | D0.3 |
| Route-A designer parity, binding target, `RemoveItemByUId` residue, rights gate vs. real enforcement | M1, M6, M5, M4 (section 3.5) |
| Whether a dependency add carries a compile obligation | M10 (V-6) |
| `AutoAddPackageDependenciesInProcesses` on the stands | M9 (V-1) |
| Every .NET 8 behaviour; web-farm propagation | M8 on the D0.5 stand; farm: traced only, stated so |
| Whether the CrtNUI kill switch `DisableEntityStageProgressBarPreprocessing` is off on the stands | V-1 read; troubleshooting line in the article either way |
| Where cut numbers are claimed | D0.7 |
| Which seed object the E2E uses | D0.8 / Q25 |
| Hours and calendar | [estimate file](eng-105556-minimal-dcm-estimate.md); re-run after Q1..Q29 are answered |
