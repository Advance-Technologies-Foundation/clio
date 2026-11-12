# ENG-105556 AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page - open decisions

Companion of [eng-105556-minimal-dcm-design.md](eng-105556-minimal-dcm-design.md). Written 2026-11-08 by the judge
of three independent designs (A maximum reuse, B extensibility first, C risk first,
`<research-dir>/design-*.md`). Nothing below is agreed: every question carries the
recommended option, and the owner decides. Evidence tags and path aliases are those of the design file
(`[traced, judge]` = re-read by the judge today; `(v rX-CY)` = `_verdicts.md` row; `(g3)`, `(r6 §2)` = research note).

---

## Q1. Where does the DCM write run?

- **Context.** The DCM schema save must go through `DcmSchemaManager.SaveSchema` (it runs `ActualizeSchemaInfo`:
  start signal, flow schema) (v r1-C1, v r1-C2); the read-back that proves the case is live is an in-memory manager
  read (g1 (b)); the later steps task needs CrtProcessBuilder's user-task and sub-process element handlers
  (v r6-C5, v r6-C6). The owner asked to reuse core and CrtProcessBuilder logic.
- **Options.** (a) new CrtProcessBuilder operation + clio tool; (b) clio-only client of the platform designer
  endpoints (`DcmSchemaManagerService` ContractDcmSchema save, DataService inserts, binding service); (c) a new core
  API; (d) depend on CrtGenAI `GeneratedDcmSaver`.
- **Recommendation.** (a). (b) needs no rebundle but makes clio emit the undocumented design-JSON dialect (g2 §2),
  4-6 non-atomic writes, and is a dead end for steps (the element builders live in the package); (c) cannot ship
  with clio or reach existing environments; (d) breaks `DependsOn []` and inherits six defects (v r4-C2). All three
  designs agree.
- Status: open - owner decides

## Q2. `BuildCase` on `ProcessDesignService` or a sibling `DcmDesignService`?

- **Context.** Both are `[ServiceContract]` classes in the same assembly package [traced, judge PB/EntryPoints/WebService/ProcessDesignService.cs:27-30].
  clio's archive pin counts every `[OperationContract]` (8 -> 9 either way); the gate pin counts only the literal
  `EnsureCanManageProcessDesign` (stays 6), so a new pin for `EnsureCanManageDcm` is needed either way [traced, judge
  CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:292,361,373,384]. The package mirror test reflects only
  `ProcessDesignService` (v r7-C3). The service's doc comment describes a `CanManageProcessDesign`-gated surface.
- **Options.** (a) ninth operation on `ProcessDesignService` (designs A, C); (b) sibling `DcmDesignService` with its own
  route family `/rest/DcmDesignService/*` (design B).
- **Recommendation.** (a): same transport, route family, wire and mirror tests, zero unverified routing; update the
  service doc comment to name both gates. (b) is cleaner for the later describe/modify/step-palette operations and
  keeps rights apart, but its route is unmeasured (B's own O1) and it needs a new mirror test; choose it if the owner
  expects several DCM operations soon.
- Status: open - owner decides

## Q3. One tool, two tools, or a documented sequence?

- **Context.** Acceptance allows "one clio MCP call (or a short documented sequence)". The discovery index has
  18,312 of 18,432 B used: one more line fits, two do not (v r7-C4). A DCM mode inside `create-business-process` would
  hit its 16 B contract headroom and the descriptor drift tests (v r7-C4).
- **Options.** (a) one tool `create-dynamic-case` doing case + bar, with `add-progress-bar` and `dry-run`; (b) two
  tools (case, bar); (c) case tool + documented `update-page` call for the bar; (d) a mode of `create-business-process`.
- **Recommendation.** (a), with (c) kept as the retry path (`page.retry` carries the exact `update-page` arguments and
  the fragment). `dry-run` runs every check and writes nothing. All three designs agree.
- Status: open - owner decides

## Q4. Tool name

- **Context.** "Case" alone collides with the `Case` object; Academy calls it a dynamic case [note]; the process
  family is `create-business-process`.
- **Options.** `create-dynamic-case`; `create-dcm`; `create-case`.
- **Recommendation.** `create-dynamic-case`, leaving `describe-dynamic-case` / `modify-dynamic-case` for later.
- Status: open - owner decides

## Q5. Up-front rights gate

- **Context.** The DCM save requires General + `CanManageDcm` [traced, judge CORE/Terrasoft.Core/DcmProcess/DcmSchemaManager.cs:132-139];
  the package-data binder requires General + `CanManageSolution` [traced, judge CORE/Terrasoft.Core/Packages/PackageElementUtilities.cs:1820-1825;
  CORE/Terrasoft.Core/DB/DBSecurityEngine.cs:5344-5347]; CrtBase grants All employees read-only operation rights on
  `SysDcmSettings` [traced, judge PS CrtBase/Data/SysEntitySchemaOperationRight_SysDcmSettings/data.json], checkable
  through public `GetEntitySchemaOperationsRightLevel` [traced, judge DBSecurityEngine.cs:3707]. CrtProcessBuilder
  saves with the caller's connection, so a missing right fails late (v r6-C3).
- **Options.** (a) General + `CanManageDcm` + `CanManageSolution` + append on both settings tables; (b) (a) without
  the append check; (c) `CanManageDcm` only; (d) also require `CanManageProcessDesign`.
- **Recommendation.** (a) (design C): every right a write of this call needs, checked before any read; (c) fails
  inside the binder (W3) after the settings and link rows are written, so it needs row compensation (under the
  schema-first order of Q7 (b) it would fail after the save) [traced, judge PackageElementUtilities.cs:1820-1824];
  (d) is stricter than the platform. M4 confirms on a non-admin user.
- Status: open - owner decides

## Q6. Save route

- **Context.** Route A = object model + `DcmSchemaManager.SaveSchema(item, uc, true, true)`; route B =
  `new DcmSchemaDesigner(uc).Save(ContractDcmSchema)` with a readable-to-design JSON transform, which also runs the
  internal `ReinitializeSystemElements` and dependency auto-add. For an element-less DCM the two give the same stored
  shape (g2 §4); `SystemElements` stays empty (v r8-C12).
- **Options.** (a) route A behind an `IDcmSchemaSaver` seam; (b) route B now.
- **Recommendation.** (a). Route B is the upgrade path for step conditions, or route A plus a public-API rebuild of
  `SystemElements` (v r8-C12) - decided by the steps task (Q27).
- Status: open - owner decides

## Q7. Write order

- **Context.** The schema save has side effects the platform does not roll back (start-event row through the system
  connection, flow schema in its own storage) (v r1-C2); whether `RemoveItemByUId` removes them is unmeasured (M5).
  The link row's `SysDcmSchemaUId` is a plain Guid column [traced, judge PS CrtBase/Schemas/SysDcmSchemaInSettings/metadata.json:21-28];
  no server listener acts on either settings table [traced, judge grep]; bindings are removable through
  `PackageElementUtilities.DeletePackageSchemaData` [traced, judge PackageElementUtilities.cs:1685-1697]. The 7.x
  designer saves the schema first and then the rows (v r2-C6).
- **Options.** (a) settings row -> link row -> bindings + read-back -> schema save LAST -> verify (design C);
  (b) schema save -> rows -> bindings (designs A, B, the designer and GeneratedDcmSaver).
- **Recommendation.** (a): the common failures (rights, binding validation) then need only row deletes, and the only
  residue-prone write happens once everything else is in place. A leftover settings row (and its binding) is a state
  the platform produces after a DCM delete; a leftover LINK row is not, because the SysSchema after-delete trigger
  removes link rows and only nulls `SysDcmSettings.DefaultSchemaUId` (g5 §4) [traced, judge
  PS CrtBase/SqlScripts/trg_TRSysSchemaADMSSql/trg_TRSysSchemaADMSSql.sql:58-64].
- **Residue weighed.** (a) can leave an orphan `SysDcmSchemaInSettings` row pointing at a never-saved UId when the
  process dies between W2/W3 and W4 or when row compensation itself fails (traps FAIL-3: it stays until cleaned by
  hand); the Cases wizard reads the link table for its default-case list (v r9-C7), so such a row can surface there
  as a dangling entry [inferred]. (b) can leave start-event / flow-schema residue after a failed post-save step, and
  whether `RemoveItemByUId` clears it is unmeasured (M5). The orphan link row is a plain data row, found by one
  read (`SysDcmSchemaInSettings` whose `SysDcmSchemaUId` has no `SysSchema`) and removed by the documented undo;
  the start-event residue sits in a table written through the system connection and is not reachable by the
  documented undo [inferred]. (a) is therefore still recommended, with the orphan-link probe added to the
  `failed-leftover` report (design §5) and to M5's evidence.
- Status: open - owner decides

## Q8. Default stage order when `stages` is not given

- **Context.** There is no platform-wide lookup order (v r9-C4, v r10-C10); nothing orders stages from the lookup;
  the bar shows stages in the DCM array order (v r3-C6). Name ascending matches 3/14 product DCMs; an order column is
  exact in 4/7 and tied in 2/7 (g6 §3).
- **Options.** (a) explicit list > `order-column` > first integer column of `Position`, `StageOrder` (+`StageInnerOrder`),
  `Number`, `StageNumber` > display value ascending; `CreatedOn`, `Id` as tie-breaks; always report `orderSource`
  + captions + tie warning; (b) require `stages` always; (c) `CreatedOn`.
- **Recommendation.** (a) (all three designs), with the guidance telling the agent to pass `stages` whenever the
  business order matters and `dry-run` as a preview.
- Status: open - owner decides

## Q9. How the lookup values are read; caption culture

- **Context.** The designer treats a stage record the user cannot see as unknown and resets it, blocking its own save
  (v r2-C5). Stage captions are a design-time copy that the designer and the bar re-read from the lookup (v r3-C6,
  v r2-C1). `SelectAnalyzer.HasOrderByItem` exists for pinning a `Select` ORDER BY (v r8-C4).
- **Options.** (a) ESQ under the caller's rights and profile culture, bounded read, ordering in the pure planner
  (design A); (b) `Terrasoft.Core.DB.Select` with ORDER BY pinned by `HasOrderByItem` (design B); (c) (b) for order +
  ESQ by Id for captions (design C).
- **Recommendation.** (a): only rows the caller can see become stages; captions are in the caller's culture; the
  ordering rule is unit-tested without query mocks. Trade-off: culture-aware .NET compare may differ from database
  collation in edge cases; the result always states the order used.
- Status: open - owner decides

## Q10. May an explicit `stages` list name a subset?

- **Context.** Scope: "one stage per lookup value". 5 of 14 product schemas (5 of 13 DCMs) use a subset; 9 of 14 schemas
  cover every value (8 listed in r5 §3.2 plus the OmniChatCaseCustom1 version of OmniChatCase) (r5 §3.2, §2.1; v r5-C2). A record holding an
  omitted value lands in `DefaultStage` and shows no current stage (v r1-C7).
- **Options.** (a) the list must name every value exactly once (a permutation); (b) subset allowed, omitted values
  warned.
- **Recommendation.** (a) (design C): matches the scope; a subset is an additive option later.
- Status: open - owner decides

## Q11. Stage count bounds

- **Context.** Product DCMs have 3-7 stages, the largest test DCM 32 (r5 §2.3). One stage is the final stage, and a
  final stage without steps ends the case at once [note "What happens on a stage change"] (r1 §8.2). An empty lookup
  hides the bar (v r3-C3). Connections grow as n*(n-1).
- **Options.** (a) 2..20 hard (design C); (b) 1..30 (design A); (c) 1..50 hard, warning above 10 (design B).
- **Recommendation.** (a) as one constant; raising it later is a constant change.
- Status: open - owner decides

## Q12. Re-run on an object that already has an equivalent case

- **Context.** The platform starts every enabled, active-version matching DCM, so a second case must never be
  created (v r1-C8). A page failure after a successful case write needs a retry path.
- **Options.** (a) equivalent (same object, column, ordered `StageRecordId` list, link row present) -> `already-present`,
  no write, continue to the page step; different -> `case-exists` (design B); (b) any enabled case -> `case-exists`,
  page retry only through `update-page` (designs A, C).
- **Recommendation.** (a): the whole call becomes safely retryable and the tool can be annotated idempotent; it
  writes nothing, so it is not "modifying an existing DCM".
- Status: open - owner decides

## Q13. Existing `SysDcmSettings` row for the object

- **Context.** The designer keeps one settings row per section/object; changing its stage column deactivates cases
  (r2 §1.2, v r2-C3); a filter column on a reused row fails the designer's filter validation (design C R5); a deleted
  DCM leaves its settings row behind (g5 §4). GeneratedDcmSaver always inserts a new row (r4 §7 item 3).
- **Options.** (a) none -> create; one with the same column and no filter column -> reuse; anything else ->
  `settings-conflict`; (b) refuse whenever a row exists; (c) always insert a new row.
- **Recommendation.** (a); (c) creates duplicates that `GetDcmStageColumnName` resolves arbitrarily.
- Status: open - owner decides

## Q14. Settings row form and `Filters`

- **Context.** Object form (`SysSchemaUId` = entity) is the 8.x product shape and GeneratedDcmSaver's; section form
  uses `SysModuleEntity` (v r5-C8, v r10-C5). `Filters = "[{\"columnUId\":null}]"` is designer output, not required
  (35/44 rows) (v r5-C8).
- **Options.** (a) object form + the `Filters` literal; (b) object form, `Filters` null; (c) section form.
- **Recommendation.** (a) for designer parity.
- Status: open - owner decides

## Q15. Package-data binding names

- **Context.** Same binding name in the same package silently replaces the data (v r4-C8). Corpus: 66/97 bindings use
  the designer default `<Schema>_<record Id without dashes>`, 31 are hand-named; GeneratedDcmSaver binds under bare
  names.
- **Options.** (a) `SysDcmSettings_<id:N>` / `SysDcmSchemaInSettings_<id:N>`; (b) `<Schema>_<DcmName>`.
- **Recommendation.** (a): unique per row and the name a later designer save reuses (M1 checks the re-save).
- Status: open - owner decides

## Q16. Missing package dependencies

- **Context.** The required set is computed exactly (column-defining package, object and lookup root packages,
  CrtCoreBase, CrtBase) and 61/61 shipped DCMs satisfy it; the platform's DCM designer adds missing ones on save; the
  applier is internal; clio has `IPackageDependencyManager.AddDependencies` over `PackageService.svc/SavePackageProperties`,
  which reports cycles (g3). clio's entity-designer precedent only reports candidates, because there the set is
  guessed from a failure (g3). Whether a dependency add marks anything for compile is unmeasured (M10).
- **Options.** (a) clio adds the missing set before the build pass, opt-out `add-package-dependencies=false`, refuse
  on a cycle (design B); (b) add always (design A); (c) refuse with the exact `add-package-dependency` calls (design C).
- **Recommendation.** (a): one call meets the acceptance wording in the common case, the side effect is reported in
  `dependencies.added`, and (c) stays one flag away. The validate pass sends `AllowMissingDependencies =
  add-package-dependencies`, the build pass always `false`; dependencies added before a build pass that does not create
  the case are removed again with `IPackageDependencyManager.RemoveDependencies` (`left-after-failure` if that fails)
  (design §3.2, §4, §6 C6).
- Status: open - owner decides

## Q17. Package that receives the page change

- **Context.** clio's `update-page` writes to the default design package (creating a replacing schema if none); a
  replacing schema in a package that cannot see every page layer breaks silently (g3 §4).
- **Options.** (a) default design package, warn when it differs from P (designs A, B); (b) P when its closure covers
  every page layer, else the default design package (design C); (c) always P.
- **Recommendation.** (a): safe by construction and identical to `update-page`; (b) adds a layer-closure check for
  little gain.
- Status: open - owner decides

## Q18. How clio writes the page

- **Context.** The agent-body guards (AST lint, baseline guard, run-process-button signature, data-source validator)
  live in the 836-line `PageUpdateTool`, not in `PageUpdateCommand` [traced, judge CLIO/clio/Command/McpServer/Tools/PageUpdateTool.cs:29-31,132-141,239,531]
  (v r7-C6). The fragment is a clio constant. The tool already holds the tenant `McpToolExecutionLock` (re-entrant)
  [traced, judge BaseTool.cs:216-238].
- **Options.** (a) `IPageDataSourceReferenceValidator` + `PageUpdateCommand.TryUpdatePage` in append mode inside the
  tool's call (designs A, C); (b) extract an `IPageFragmentWriter` from `PageUpdateTool` used by both tools (design B).
- **Recommendation.** (a): no change to `update-page`; the constant fragment (a marker-wrapped append body) is pinned
  by a unit test, and an append DRY RUN of it runs before any write so the save's own gates refuse before the case
  exists (design §6 C3). (b) is worth it when a second caller needs the full guard chain.
- Status: open - owner decides

## Q19. Progress-bar flags and whether to expose them

- **Context.** The AI app wizard writes `saveOnChange: true`, `askUserToChangeSchema: false`; the template uses
  `false`/`true`; product pages split 4 true / 8 false. `true` persists a click at once (reverts on failure, saves an
  unsaved new record); `false` marks the page dirty. Both satisfy the acceptance text (v r9-C6, v r3-C8).
- **Options.** (a) wizard values, no argument; (b) wizard values + a `save-on-change` argument (design C);
  (c) template values.
- **Recommendation.** (a): fewer knobs; an argument is additive later.
- Status: open - owner decides

## Q20. Typed record pages, mini pages, Classic pages

- **Context.** Typed page sets (`Activity`) have no untyped default; mini pages use a `crt.GridContainer`
  `MainContainer`; there is no product precedent for the bar on mini pages; a Classic-only object has no Freedom page
  (g4 §1, §4, §5, §7).
- **Options.** (a) typed -> require `page-schema-name`; mini -> `placement-unsupported`; Classic ->
  `record-page-not-freedom`; all refused BEFORE any write; (b) add the bar to every typed page; (c) accept caller
  `parent-name`/`index` for any layout.
- **Recommendation.** (a); (c) is a later argument.
- Status: open - owner decides

## Q21. Bind the stage lookup values to the package

- **Context.** `StageRecordId`s are baked into the flow; another environment needs the same value Ids; the designer
  binds only new or changed values (`<Lookup>_Dcm_Data`) (r1 §8.4, v r2-C6).
- **Options.** (a) do not bind, note N4 (designs B, C); (b) bind values not bound anywhere.
- **Recommendation.** (a): the values belong to the lookup's owner package; rebinding them in P can overwrite a
  same-name binding (v r4-C8).
- Status: open - owner decides

## Q22. Package version and the `[RequiresPackage]` floor

- **Context.** The stand runs CrtProcessBuilder 1.6.6.142, an unmerged feature build that is not main (v r10-C2);
  `RequiredPackageChecker` compares versions and cannot express "has `BuildCase`" (r7 §1.4); every rebundle makes all
  gated tools refuse environments on the previous archive (v r7-C2).
- **Options.** (a) cut the release above every deployed build; floor = that version; clio maps a 404 / non-envelope
  reply to `process-builder-without-build-case`; (b) next version after main, floor = presence only.
- **Recommendation.** (a) (design C), plus the release note on convergence refusals.
- Status: open - owner decides

## Q23. Knowledge-base placement and naming

- **Context.** Files under `guidance/mcp/guides/processes/` carry no prefix while their itemIds do
  (`send-email-attachments.md` -> `process-send-email-attachments`) [traced, judge KB/bundle-source.json:141,345,1884].
  A processes article needs a routing `name=` token and no banner, or `process-modeling` (about 13 B left) must
  index it (v r7-C8). `process-modeling.md:157-158` wrongly lists DCM among schemas with compile obligations
  [traced, judge].
- **Options.** (a) `processes/dynamic-case.md`, itemId `process-dynamic-case`, no banner, a Business processes
  routing row + a Pages pointer, `process-modeling` sentence corrected byte-neutrally, `libraryVersion` bump only;
  (b) a new `cases/` area.
- **Recommendation.** (a). Designs A and C named the itemId `dynamic-case`, design B named the file
  `process-dynamic-case.md`; both deviate from the convention.
- Status: open - owner decides

## Q24. .NET 8 evidence and definition of done

- **Context.** The DCM code has no `#if`; the binder is bound on both hosts (v r1-C1; r4 §4.2); no .NET 8 stand is
  reachable and the ProcessDesigner lane cannot run .NET 8 without a `StudioNet8` build (g5 §1-2). Package unit tests
  run net8.0 locally under `dev-n8` (v r8-C6).
- **Options.** (a) owner provisions a `StudioNet8` stand; E2E + CAADT run there by hand; "done" waits for it;
  (b) a TeamCity lane variant (`BuildProductNet8`, never run); (c) accept code-level parity.
- **Recommendation.** (a); results say "expected" for .NET 8 until then.
- Status: open - owner decides

## Q25. E2E arrange

- **Context.** `AutoTestClioMcp` is deployed on every ProcessDesigner-lane stand [measured g5 §2.1]; stand
  `Creatio` has no clean sample object (v r10-C9).
- **Options.** (a) seeded object + 4-value lookup with `Position` + tabbed Freedom record page + related-page add-on
  in SVN `AutoTestClioMcp`, with a `LocalOnly` fixture (`sync-schemas`) until it ships; (b) an existing Studio object
  (`Webhook.Status` is a candidate, unmeasured on the lane); (c) `LocalOnly` only.
- **Recommendation.** (a) (all three designs).
- Status: open - owner decides

## Q26. Name collision

- **Context.** The object-model save does not check name uniqueness itself (v r1-C5); GeneratedDcmSaver uses a bare
  `{Entity}Case` (r4 §7 item 1).
- **Options.** (a) default name auto-suffixed (`Case2`...), explicit name refused when taken; (b) always refuse;
  (c) always suffix.
- **Recommendation.** (a) (designs B, C).
- Status: open - owner decides

## Q27. Steps approach for the later task (recorded now so this task does not pre-empt it)

- **Context.** A step is a process element wrapped by `DcmSchemaElement`; the wrapper's ctor and `ProcessFlowElement`
  setter are internal; the public routes are `new DcmSchemaElement()` + `ReadMetaData`, or the designer save (v r6-C7,
  v r1-C6). The element handlers are typed on `ProcessSchema` (712 occurrences, 166 files) (v r6-C5); a stored DCM step
  differs from a process element in several keys (v r6-C6).
- **Options.** (a) host-process + metadata wrap: build with the existing `IProcessElementFactory` against a transient
  `ProcessSchema`, then wrap through `ReadMetaData` (design B); (b) generalise the handlers to `BaseProcessSchema`
  (design C); (c) designer route B with a design-JSON transform.
- **Recommendation.** Spike (a) first in the steps task; the minimal design keeps all three open (section 12 of the
  design).
- Status: open - owner decides

## Q28. Feature toggle

- **Context.** Process-designer tools ship ungated; a `[FeatureToggle]` would hide the tool on CLI, MCP and docs and
  would require the KB article to declare `requiredFeatures`, which the no-banner choice permits (v r7-C8).
- **Options.** (a) ship visible; (b) toggle on the options and tool classes until the .NET 8 run exists.
- **Recommendation.** (a); "done" is gated by Q24 instead. Locked in by adding the tool to `ProcessDesignerGoLiveTests`
  (no `[FeatureToggle]`, resolvable by default; test plan TC-C-32).
- Status: open - owner decides

## Q29. Stage shape defaults

- **Context.** Full-mesh `StageConnections` are mandatory for a clickable bar (connections gate clicks; core and
  `SaveSchema` do not fill them) (`_verdicts.md` conflict 7; v r5-C6). A null colour crashes the 8.x bar; the designer
  saves `#0C8701`; `#54AA4D` / `#8ECB60` render as `#0C8701` (conflict 6; v r3-C5). Using the record Id as the stage
  UId collides with derived system-element UIds once steps get conditions (r4 §7 item 6). `IsDefault` is not persisted
  (v r4-C3).
- **Options.** (a) fresh stage UIds, `StageRecordId` = row Id, names `Stage1..N`, colour `#0C8701`, full mesh,
  `Enabled = true`, no `IsDefault`; (b) as (a) with a caller colour argument; (c) GeneratedDcmSaver's shape
  (`#54AA4D`, record Id as UId).
- **Recommendation.** (a); a `color` key is additive later.
- Status: open - owner decides

---

## Appendix A. Score table of the three designs

Scale 1 (weak) to 5 (strong). "Correctness" = agreement with the source and the verdicts, including the facts the
judge re-read today.

| Criterion | A maximum reuse | B extensibility first | C risk first |
|---|---|---|---|
| Correctness against evidence | 4 - uses the public `FindEnabledDcmSchemas` and correctly calls `GetIsActiveVersion(item)` internal; misses the KB itemId convention; schema-first order leaves the unmeasured residue risk; subset `stages` drifts from scope | 3 - lists `GetIsActiveVersion` as public (only the Guid overload is); sibling-service route unmeasured; file name `process-dynamic-case.md` off convention; `Select` ignores record rights the designer enforces | 4 - most precise on rights (traced the settings-table grant) and side effects; relies on the internal `GetIsActiveVersion(item)` overload; KB itemId convention missed |
| Reuse of core + CrtProcessBuilder | 5 - core's own enabled-DCM query, `IPackageCatalog` split, existing page command and dependency manager | 4 - re-implements the enabled-DCM scan; reuses page guards through an extraction | 4 - re-implements the scan; refuses instead of reusing the dependency manager |
| Extensibility for stage steps | 4 - object stages, saver seam, planner, dependency inputs, step-builder route | 5 - describe-shaped response, composer split, compile assessor, page component list, concrete steps-factory plan | 4 - object stages, saver seam, obligations object, element-handler generalisation path |
| Risk coverage | 3 - binding read-back, validate pass, three-outcome wording; no post-save competing-case check, no settings-table rights | 4 - post-save re-check, idempotent re-run, compensation; schema-first order | 5 - 46-item register, rows-first order, verification as the bar reads it, settings-table rights, "no BuildCase" mapping, runtime E2E |
| Cost (clean-slate; higher = cheaper) | 5 - about 1,250 package + 850 clio lines, no refactor of existing tools | 3 - sibling service + mirror test + `PageUpdateTool` extraction + describer/assessor | 4 - single pass, more refusal codes, a verifier, an extra E2E case |
| **Total (of 25)** | **21** | **19** | **21** |

Choice: A and C share the same architecture (operation on `ProcessDesignService`, page in clio, one tool); A is the
base because the owner's first constraint is reuse and A is the cheapest; C's risk mechanisms are additive inside that
placement and are all grafted; B contributes the shapes that keep the later steps task open without paying for its
refactors now. The judge's resolution of each factual disagreement is in section 1 of the design file.

## Appendix B. Decision index

| Q | Topic | Recommended |
|---|---|---|
| Q1 | where the write runs | CrtProcessBuilder op + clio tool |
| Q2 | op placement | on `ProcessDesignService` |
| Q3 | tool shape | one tool, `dry-run`, `update-page` retry |
| Q4 | name | `create-dynamic-case` |
| Q5 | rights | General + CanManageDcm + CanManageSolution + settings-table append |
| Q6 | save route | A behind `IDcmSchemaSaver` |
| Q7 | write order | rows, link, bindings, schema LAST, verify |
| Q8 | default order | g6 precedence, always reported |
| Q9 | lookup read | ESQ under user rights + pure planner |
| Q10 | explicit list | permutation |
| Q11 | bounds | 2..20 |
| Q12 | equivalent re-run | `already-present` |
| Q13 | existing settings row | reuse same column without filter, else refuse |
| Q14 | settings form | object form + `Filters` literal |
| Q15 | binding names | `<Schema>_<id:N>` |
| Q16 | dependencies | add with opt-out, refuse on cycle |
| Q17 | page package | default design package |
| Q18 | page write path | `PageUpdateCommand` + data-source validator |
| Q19 | bar flags | wizard values, no argument |
| Q20 | typed / mini / Classic pages | refuse before any write |
| Q21 | bind lookup values | no, note N4 |
| Q22 | version / floor | above every deployed build |
| Q23 | KB | `processes/dynamic-case.md`, itemId `process-dynamic-case` |
| Q24 | .NET 8 | owner-provisioned stand gates "done" |
| Q25 | E2E arrange | seeded `AutoTestClioMcp` + LocalOnly interim |
| Q26 | name collision | suffix default, refuse explicit |
| Q27 | steps approach (later) | spike host-process wrap |
| Q28 | feature toggle | none |
| Q29 | stage shape | fresh UIds, `#0C8701`, full mesh, Enabled |
