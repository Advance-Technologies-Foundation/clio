# ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page" - use cases

| | |
|---|---|
| Issue | ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page" (Task; epic ENG-92704 "Create BP via AI Toolkit") |
| Written | 2026-11-08. Local, untracked. Read-only: nothing was saved, installed or run against any stand for this file |
| Purpose | Why products and users build a dynamic case (DCM), what a Freedom UI (8.x) user sees, what the shipped corpus does with DCMs, which minimal real shape the tool mirrors, and which AI Toolkit scenarios this task enables or leaves to later tasks |
| Companions | [design](eng-105556-minimal-dcm-design.md) (chosen design, sections cited as "design §N") · [decisions](eng-105556-minimal-dcm-decisions.md) (Q1..Q29, all open, owner decides) |
| Basis | Academy pages read 2026-11-08 (r9 §9); owner's DCM overview note (33 slides); PackageStore `*/branches/7.8.0` corpus (1,040 packages, 61 DCM schemas); core TSBpm a95349820; creatio-ui 7ca9210bd0e; read-only inventory of stand `Creatio` (r10). Research notes r1..r10, g1..g6 and `_verdicts.md` in `<research-dir>/` (local) |

**Summary.** A dynamic case is a stage model over one lookup column of an object; on Freedom UI its visible face is the
stage progress bar at the top of the record page, and the bar does not render without an enabled, active-version case
for that object (section 2). In the shipped product the "minimal" case is the norm, not a toy: 12 of 13 product DCMs
have no steps at all, and the one with steps is an example app (section 3). The tool therefore mirrors the smallest
shipped flat case (`KnowledgeArticlePublishing`) with three deliberate differences: one stage for EVERY lookup value,
the 8.x colour, and designer-default binding names (section 4). It enables "give this object stages and a bar" in one
call (section 5) and leaves steps, transition rules, alternative final stages, start conditions and editing an existing
case to later tasks, with the seams for them already in the contract (section 6).

**Evidence tags.** `[traced path:line]` source read; `[corpus N/M]` count over the PackageStore corpus; `[measured]`
read-only stand read (query named in the cited note); `[academy URL]`; `[note lines a-b]` the owner's overview note;
`[inferred]` my reasoning. `(v r5-C2)` = the `_verdicts.md` row that carries the full evidence and OVERRIDES its note;
`(r9 §4.3)`, `(g6 §3)` = research-note section. A verdict correction is used here instead of the note wording.

| Alias | Path |
|---|---|
| CORE | `<core>/Src/Lib/` |
| PS | `<PackageStore>/<Package>/branches/7.8.0/` |
| UI | `<creatio-ui>/` |
| PBAR | `UI/libs/studio-enterprise/ui/entity-stage-progress-bar/src/` |
| KB | `<clio-knowledge>/` |
| NOTE | owner's DCM overview note (not published) |

---

## 1. Why DCM is used (business purpose)

### 1.1 What a case is

| Fact | Evidence |
|---|---|
| A case is a workflow with business value for a customer or partner; Academy's examples are opening a bank account and an insurance payment claim | [academy https://academy.creatio.com/docs/user/bpm_tools/dynamic_case_setup/case_designer_workflows/overview] [note lines 23-40] |
| When to use it: there are several ways to reach the goal and none can be predicted in advance | [academy https://academy.creatio.com/docs/8.x/no-code-customization/bpm-tools/dynamic-case-setup/case-designer-workflows/case-designer] [note lines 23-40] |
| UI names: "Cases", "Case Designer"; DCM = Dynamic Case Management | [note lines 23-40] |
| Terminology trap: the `Case` object (customer request, the Case management app since 8.0.8) is NOT a DCM case; a DCM case is a stage schema on any object with a lookup stage column. This is why the tool is named `create-dynamic-case`, not `create-case` (Q4) | [note lines 95-113] [academy https://academy.creatio.com/guides/resources/release-notes/808-atlas-release-notes] |
| Anatomy: case = object + stage lookup column + optional start condition + ordered stages + steps inside stages (example: Opportunity / Stage / "Category = Medium business") | [note lines 117-137] |
| Under the hood a case is a process: `DcmSchema` derives from `BaseProcessSchema`, is always interpreted and generates no C#; the save turns it into a flow schema run by the ordinary process engine | [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:48] [note lines 251-260] (v r1-C1) |

### 1.2 Case versus business process (when an agent should pick which)

| Aspect | Business process (BPMN) | Case (DCM) |
|---|---|---|
| Model | graph of events, gateways, tasks | stages, steps inside stages |
| Order of actions | fixed by the diagram | chosen by the user |
| Who moves it | the engine along flows | the user, by changing the record's stage |
| Going back | only if drawn | allowed by default |
| Start | signal, timer, manual | automatically, by a condition on the object |

[note lines 45-62] (r9 §1.1). The two combine (a case step can start a BPMN sub-process), but Academy recommends
disabling a section business process that drives the same stage column before running the case
[academy https://academy.creatio.com/guides/no-code-customization/bpm-tools/dynamic-case-setup/case-designer-workflows/execute-case].
The tool does not detect such a process; the guidance carries the warning (design §16).

Consequence for routing [inferred]: "the record moves through statuses and users pick the next status" routes to
`create-dynamic-case`; "when X happens, do Y then Z" stays with `create-business-process`. The guidance article and its
routing rows (Q23) carry that split.

### 1.3 Where the product uses it

Every product DCM sits on the lifecycle or status column of a core object: lead qualification (`Lead.QualifyStatus`),
sales stages (`Opportunity.Stage`), service requests (`Case.Status`), orders, contracts, bank cards and accounts,
financial products, knowledge-article publishing, omnichannel chats, loan applications [corpus, r5 §2.1]
[note lines 95-113]. That is 11 distinct objects (r5 §2.1 table) and 11 distinct stage lookups (g6 §2) [corpus].

---

## 2. What an 8.x (Freedom UI) user sees

### 2.1 The progress bar is the case's face

| Behaviour | Evidence |
|---|---|
| `crt.EntityStageProgressBar` ("Progress bar" in the toolbox) arrived in 8.0.8; 8.0.9 retired the Action dashboard for new pages and added Next steps | [academy https://academy.creatio.com/guides/resources/release-notes/808-atlas-release-notes] [academy https://academy.creatio.com/guides/resources/release-notes/809-atlas-release-notes] |
| The page config is minimal (`type`, `entityName`, optional `saveOnChange`, `askUserToChangeSchema`); a runtime preprocessor creates every attribute and handler, and overwrites hand-wired ones | [note lines 580-604] (v r3-C2) [corpus 0/24 pages wire them] |
| No lookup-only mode: stages, stage column and current-stage binding come from the first enabled, active-version DCM of `entityName`; without one the bar hides itself. The case is the enabling artefact for the bar, not an add-on | (v r3-C3, v r9-C1) [traced PBAR/runtime/components/entity-stage-progress-bar.component.ts:368-381] |
| Stages shown in DCM array order with DCM colours; caption = the lookup's primary display value, falling back to the stage caption; the last stage is final | (v r3-C6, v r9-C2) [traced PBAR/runtime/handlers/entity-stage-progress-bar-load-data.handler.ts:293-311] |
| A stage is clickable only along a `StageConnection` from the current stage; zero connections = a frozen bar; a null colour crashes the bar | `_verdicts.md` conflicts 6, 7 (v r3-C4, v r3-C5) |
| A click writes `{value, displayValue}` into `<PDS>.<column>`; with `saveOnChange: true` the record is saved at once (reverted on failure), with `false` the page turns dirty until Save. A change in the field moves the bar; the bar never saves on a field change | (v r3-C7, v r9-C6) [traced PBAR/runtime/handlers/entity-stage-progress-bar-stage-changed.handler.ts:47-63] |
| The stage change is a record save, not a service call; the server wakes the case through the stage-column signal. A direct database UPDATE does not wake it | [note lines 628-636] |
| The client caches the DCM list per browser tab: a case created while a tab is open is invisible there until F5 (note N1) | (v r3-C1) [traced UI/libs/studio-enterprise/data-access/common-api/src/lib/dcm/dcm-schema-manager-service/dcm-schema-manager.service.ts:111-135 per r3 §0 item 7] |
| List pages need no edit: once the case exists, a grid column showing the stage column renders as a stage slider | [traced UI/libs/studio-enterprise/feature/components-preprocessors/src/lib/data-grid/stage/data-grid-stage-cell-view-preprocessor.ts:75-164 per r3 §9] [inferred: not asserted by the E2E yet] |

### 2.2 How a user builds a case by hand today (what the tool replaces)

1. Put the Progress bar on a Freedom UI page, open its properties, click **Set up cases**
   [academy https://academy.creatio.com/guides/no-code-customization/customization-tools/ui-and-business-logic-customization/UI-designer].
   There is no Angular case designer: the panel opens the classic `SectionCasesWizard` in a new window; the DCM designer
   proper is `Nui/ViewModule.aspx?vm=DcmDesigner#case/<schemaUId>` (v r9-C8) [note lines 608-626].
2. Pick the stage column; fill title, code (Latin letters and digits only) and package
   [academy https://academy.creatio.com/docs/8.x/no-code-customization/bpm-tools/dynamic-case-setup/case-designer-workflows/case-designer].
3. Add stages ONE AT A TIME and bind each to a lookup value: the designer starts with one invalid "Stage 1" and never
   fills stages from the lookup (v r2-C1, v r9-C3)
   [academy https://academy.creatio.com/guides/no-code-customization/8.3/bpm-tools/dynamic-case-setup/case-designer-workflows/set-up-case-stages].
   Defaults the designer applies: all stages connected to all, manual transition, colour saved as `#0C8701` (v r9-C5).
4. Save; the designer writes the settings and link rows and binds them (and new or changed lookup values) to the
   package (v r2-C6).

"One stage per lookup value, in one call" is therefore a NEW convenience; the only server-side precedent that builds
stages is the AI app generator `GeneratedDcmSaver`, which invents NEW lookup rows from AI captions rather than
binding an existing lookup (v r9-C3) [traced PS CrtGenAI/Schemas/GeneratedDcmSaver/GeneratedDcmSaver.cs:77-90,133-149].

### 2.3 What a case does at run time when its stages are empty

| Event | Behaviour | Evidence |
|---|---|---|
| New record inserted | a case instance starts (insert start signal); a record with an empty or non-stage value lands in `DefaultStage` and the bar shows no current stage (note N3) | (v r1-C7) [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:361 per r1 §8.3] |
| Existing record | no instance at case save; one starts on the first save that changes the stage column | (v r1-C7) |
| Move forward / back | nothing to block (no required steps) and nothing to cancel or recreate (no tasks) | [note lines 214-231] [inferred, r9 §4.4] |
| Final stage selected | a final stage without steps ends the instance at once; the next qualifying update starts a new one | [note lines 214-231] (v r1-C7) |
| Automatic transition | never: a stage gets an automatic transition only when it has elements | [traced CORE/Terrasoft.Core/DcmProcess/DcmFlowSchemaGenerator.cs:308-321 per v r5-C4] |

User-visible result of a step-less case: "a lookup editor drawn as a bar", ordered and coloured by the case, plus case
instances the platform tracks for each record [inferred, r9 §4.4]. Two related facts the tool does not change:
the stage column's default value (GeneratedDcmSaver sets it to the first stage; here that would be an entity change,
design §16; no Q covers recommending it in guidance, so it is an open point for the owner), and the 8.x bar's stage
history, which depends on feature `EnableHistoryDCM` (r3 §3.1) and was not measured.

---

## 3. Corpus statistics (PackageStore `*/branches/7.8.0`)

Signal: `"ManagerName": "DcmSchemaManager"` in `descriptor.json` [traced r5 §1]. 61 DCM schemas: 14 product schemas
in 12 packages, 47 test schemas [corpus 61; r5 §2].

### 3.1 Objects and stages (product)

| Package / schema | Object | Stage column -> lookup | Stages (of them alternative) | Steps | Settings row form |
|---|---|---|---|---|---|
| CoreLead / LeadCase (disabled, 7.x) | Lead | QualifyStatus -> QualifyStatus | 7 (2) | 0 | section |
| CrtLeadOppMgmtApp / LeadManagementCase | Lead | QualifyStatus -> QualifyStatus | 4 (1) | 0 | object |
| Opportunity / OpportunityCase (disabled, 7.x) | Opportunity | Stage -> OpportunityStage | 6 (1) | 0 | section |
| CrtLeadOppMgmtApp / OpportunityManagementCase | Opportunity | Stage -> OpportunityStage | 6 (1) | 0 | object |
| CrtCaseManagmentObject / CaseCase | Case | Status -> CaseStatus | 7 (2) | 0 | both |
| CrtContract / ContractCase | Contract | State -> ContractState | 5 (1) | 0 | object |
| CrtOrder / OrderCase | Order | Status -> OrderStatus | 5 (2) | 0 | object |
| CrtFinservAccountAndCard / CardCase | BankCard | BankCardStatus -> BankCardStatus | 4 (3) | 0 | object |
| CrtFinservAccountAndCard / Case_FinancialAccount | BankAccount | Status -> BankAccountStatus | 3 (2) | 0 | object |
| CrtFinservProduct / ProductCase | Product | ProductStatus -> ProductStatus | 3 (2) | 0 | object |
| CrtKnowledgeManagementVersions / KnowledgeArticlePublishing | KnowledgeBase | Status -> ArticlePublicationStatus | 3 (0) | 0 | object |
| FinAppLending / FinApplicationCase ("(example)") | FinApplication | Stage -> FinAppStage | 7 (2) | 4 | section + Filters |
| OmnichannelMessaging / OmniChatCase | OmniChat | Status -> OmnichannelChatStatus | 6 (3) | 0 | section |
| OmnichannelAiIntegration / OmniChatCaseCustom1 (a version of OmniChatCase) | OmniChat | same | 6 (3) | 0 | inherits |

[corpus, r5 §2.1; disabled flags r5 §4.5; version r5 §4.4]

| Statistic | Value | Evidence |
|---|---|---|
| Product DCMs without steps | 12 of 13 DCMs (14 schemas, one is a version); 10 of 11 enabled DCMs; the one with steps is a vertical example app | (v r5-C2) [corpus] |
| Product stage counts (schemas) | 3 x3, 4 x2, 5 x2, 6 x4, 7 x3; all 61: 1 x16 ... 32 x1 (the 32-stage case is a test) | [corpus, r5 §2.3]; feeds Q11 bounds 2..20 |
| Every lookup value is a stage (1:1) | 9 of 14 product schemas (8 listed in r5 §3.2 plus the OmniChatCaseCustom1 version); 5 of 14 schemas (5 of 13 DCMs) use a subset, e.g. LeadCase 7 and LeadManagementCase 4 of 11 QualifyStatus values | [corpus 9/14, r5 §3.2, §2.1; v r5-C2]; feeds Q10 |
| Product schemas with alternative stages | 13 of 14 (only KnowledgeArticlePublishing is linear) | [corpus, r5 §2.1 table] |
| Full-mesh `StageConnections` | 10 of 14 product schemas; 39 of 45 multi-stage DCMs overall; restricted on purpose in LeadCase, CaseCase, OmniChatCase(+Custom1) | (v r5-C2, v r5-C6) [corpus] |
| Disabled DCMs next to an enabled replacement on the same object and column | 2 (LeadCase, OpportunityCase) | [corpus, r5 §4.5] (v r1-C8); feeds note N5 |
| Stage lookups with an integer order column | 5 of 11 (`Position`, `Number`, `StageNumber`, `StageOrder`+`StageInnerOrder`); 6 have none | [corpus 5/11] (g6 §2, v r9-C4); feeds Q8 |
| Default order reproduced | an order column reproduces the shipped order exactly in 4/7, up to ties in 2/7, not at all in 1/7; display value ascending in 3/14 | [corpus, g6 §3] |
| Stage colours (238 stages) | `#8ECB60` 75, `#54AA4D` 59, `#0C8701` 36, ...; none empty. All render as `#0C8701` in 8.x except the explicit others | [corpus 238/238] (`_verdicts.md` conflict 6); feeds Q29 |
| Stage names | always `Stage<N>`, but N is not the position | [corpus 238/238, r5 §3.3] |
| Stage UId equal to `StageRecordId` | 0 of 238 (GeneratedDcmSaver is the only writer that reuses the record Id) | [corpus 0/238, r5 §5.1]; feeds Q29 |
| `Transitions` | empty in 60 of 60 DCMs that serialise it | [corpus, r5 §4.2] |
| Base schema (`BK8`) | the DCM base `51b280eb-...` in 59 of 61; the 2 others are versions | [corpus 59/61, r5 §4.4] |
| Settings rows | 44 `SysDcmSettings` bindings in 31 packages; object form (`SysSchemaUId`) 17 of 44 rows, pure 8.x product shape 9 rows in 7 packages; `Filters = "[{\"columnUId\":null}]"` in 35 of 44 | (v r5-C8) [corpus]; feeds Q13, Q14 |
| Binding names | 66 designer-default `<Schema>_<id without dashes>`, 31 hand-named | (v r2-C6) [corpus]; feeds Q15 |

### 3.2 Steps where they exist (the later task's population)

| Statistic | Value | Evidence |
|---|---|---|
| DCMs with steps | 19 of 61, 81 steps; 18 of the 19 are test DCMs | [corpus 19/61, r5 §4.1, §2.2] |
| Step kinds | every step is `DcmSchemaElement` wrapping a process element: user task 56 (`ActivityUserTask` 50, `ApprovalUserTask` 3, `EmailTemplateUserTask` 3), sub-process 25 | [corpus 81/81] (v r5-C3) |
| Step palette | `SysDcmUserTask`: Activity, Approval, OpenEditPage, EmailTemplate, ChangeAdminRights (+AppValidation in FinAppLending) | (v r5-C3) |
| Where step data lives | `Stage.Elements`, schema `BK15` mappings, `Transitions`, `SystemElements`, resources; `SystemElements` is rebuilt only by the internal `ReinitializeSystemElements` | (v r5-C3, v r1-C6) |

So "user tasks and sub-processes inside stages", the owner's next step, is exactly the shape the corpus uses when it
has steps at all; both live inside `Stage.Elements` and need no change to the stage, connection or settings model the
minimal case writes [inferred from r5 §4.1, §9]. Approach for that task: Q27.

### 3.3 Pages with the bar

| Statistic | Value | Evidence |
|---|---|---|
| Page schemas with `crt.EntityStageProgressBar` | 24 files, 25 instances; 12 product pages (one mobile), 11 test files, the `PageWithTabsAndProgressBarTemplate` template | [corpus 24 files, r5 §7] |
| Product DCM objects with a Freedom page carrying the bar | every one, and vice versa | [corpus, r5 §7] |
| Stage column on the page | never: the bar resolves the case by object | [corpus 25/25] (v r5-C9) |
| `saveOnChange` | product pages: `true` 4 of 12 (Cases, ExternalCases, Orders, Contracts); counts over all instances differ by one between notes (v r10-C7 "true 10/25"), so neither value is a convention | [corpus 4/12] (v r9-C6, v r10-C7); feeds Q19 |
| Layout | product pages: `ProgressBarContainer` (`crt.FlexContainer`) at index 0 of `MainContainer`, bar named `ProgressBar`; only 8/25 instances use exactly that container | [traced PS CrtLeadOppMgmtApp/Schemas/Leads_FormPage/Leads_FormPage.js:49-60] (v r10-C7) |
| Page templates | web bars sit on `PageWithTabsFreedomTemplate` descendants or the progress-bar template; none on area, left-area or mini pages | [corpus 24 files, g4 §4]; feeds Q20 |
| AI app wizard | inserts the container + bar with `saveOnChange: true`, `askUserToChangeSchema: false`, only on tabbed pages, only when the GenAI path generated a case | (v r3-C8) [traced UI/libs/studio-enterprise/feature/application-management/src/lib/services/page-content-generator/page-content-generators/page-with-tabs/page-with-tabs-freedom-content-generator.ts:57-109] |

### 3.4 Stand `Creatio` (read-only, .NET Framework)

One DCM (`OmniChatCase`, section-form settings row), one page with the bar (`Chats_FormPage`), all DCM feature toggles
the product enables by default ON for All employees (incl. `UseDcmForGeneralObject`, so a case can sit on an object
with no classic section), CrtProcessBuilder 1.6.6.142 (an unmerged feature build), no reachable .NET 8 stand
[measured r10 §3-§7] (v r10-C2, v r10-C3, v r10-C5). No clean sample object exists on the stand (v r10-C9); the E2E
seeds its own (Q25). `OmniChatCase` is NOT a template to copy: 1 connection (frozen bar), alternative stages, stage
permissions (v r10-C6).

---

## 4. The minimal real DCM shape we mirror

Reference: `CrtKnowledgeManagementVersions/KnowledgeArticlePublishing`: 8.x-created (8.1.4.2889), 3 stages, linear,
no steps, full mesh, object-form settings, a Freedom page with the bar. It is the smallest flat, unfiltered, step-less
case in a SHIPPED package (16 test DCMs are smaller) (v r5-C10) [traced PS CrtKnowledgeManagementVersions/Schemas/KnowledgeArticlePublishing/metadata.json:14-96].

| Element | KnowledgeArticlePublishing | `create-dynamic-case` writes (design §5 S13, W1-W3) | Q / evidence |
|---|---|---|---|
| Manager, base | `DcmSchemaManager`, `BK8` = DCM base | same (`CreateSchema` with a null base = `DefSchema`) | design §1 last row |
| Object, stage column | `EntitySchemaUId`, `StageColumnUId` | same, from the RUNTIME object schema | design §5 S5-S6 |
| Enabled, version | enabled, version 0, active | same, read back after the save | design §5 V1; Q12 |
| Stages | one per used lookup value (here all 3), array order = business order | one per lookup value, ALL values (permutation if explicit), order by Q8 rule, always reported | Q8, Q10 |
| Stage name / UId / record | `Stage<N>`, fresh UId, `StageRecordId` = row Id | `Stage1..N`, fresh UId, `StageRecordId` = row Id | Q29 [corpus 0/238 UId = record Id] |
| Stage colour | `#54AA4D` | `#0C8701` (what the designer saves; `#54AA4D` renders the same) | Q29; conflict 6 |
| Stage caption | resource copy of the lookup display value | display value in the caller's culture | Q9 (v r5-C5) |
| Connections | full mesh (6) | full mesh n*(n-1) | Q29; conflict 7 |
| `Elements`, `Transitions`, `SystemElements`, `BK15` | empty | empty; `steps` refused with `steps-not-supported` | design §3.1, §12 |
| `BK37` NotificationCaption | present (60/61) | written by the server when `UseProcessRemindings` is on (true in shipped configs) | (g2 §3) [corpus 60/61] |
| `IJ10` UseSystemSecurityContext | true (40/61) | not set (a 7.x client default; affects only script-task rights) | (g2 §3) [corpus 40/61] |
| `SysDcmSettings` | object form, `Filters` literal | object form, `Filters` literal; reused only when same column and no filter column | Q13, Q14 |
| `SysDcmSchemaInSettings` | present (needed to open the case in the designer) | present | (v r2-C4) |
| Binding names | hand-named (`SysDcmSettings_KnowledgeArticle`) | designer default `<Schema>_<id:N>` | Q15 |
| Stage lookup values bound | part of the reference package set (v r5-C10) | NOT bound; note N4 | Q21 |
| Page | `KnowledgeBase_EditPage`, `saveOnChange: false` | wizard fragment, `saveOnChange: true`, `askUserToChangeSchema: false`, default design package | Q17, Q19 |

Deliberately NOT mirrored from the wider product shape: alternative stages and unsuccessful final stages (13/14 product
schemas use them), restricted connections (4/14), filters (1 product DCM), subsets of a lookup (5/14 schemas). Each is a later,
additive option (section 6).

Equivalence to the designer route: for a step-less case the object-model save (route A) and the designer save give the
same stored shape (g2 §4); opening the result in the designer with no error is traced, not measured (M1, design §17).

---

## 5. AI Toolkit scenarios this task enables

Each scenario is one `create-dynamic-case` call unless stated; arguments and refusals in design §3.

| Id | Scenario (what the user asks the agent) | How the tool serves it | Q / note |
|---|---|---|---|
| U1 | "Track <object> through its Status and show the stages on the record page" for an object whose status lookup has an order column | default order from `Position` / `StageOrder`(+`StageInnerOrder`) / `Number` / `StageNumber`; case + settings + bindings + bar; result states `orderSource` and captions | Q8, N1, N2 |
| U2 | Same, where the business order is not encoded in the lookup ("New, In progress, Done") | the agent passes `stages` as an ordered list naming every value once; without it display-value order would read "Done, In progress, New" | Q8, Q10 (g6 §4) |
| U3 | End-to-end app build: create the app or section, create the status lookup and its values (existing tools), then the case and the bar | the case step needs the object PUBLISHED with the lookup column in its runtime schema; values must exist before the call | design §3.1 `entity-schema-name`; Q9 |
| U4 | "Show me what you would create" | `dry-run`: every check and the full plan (stages, order, name, dependencies, page, fragment), nothing written | Q3 |
| U5 | Page step failed after the case was saved (for example a page save error) | case kept; result carries the exact `update-page` retry arguments; a full re-run answers `already-present` for the case and continues to the page | Q3, Q12 |
| U6 | Object whose record page set is typed (the `Activity` shape) or whose default page cannot be resolved | refused before any write with candidates; the agent re-calls with `page-schema-name` | Q20 |
| U7 | Case only, the bar handled separately (mini page, custom layout) | `add-progress-bar: false`; the bar later through `update-page` with the documented fragment | Q3, Q20 |
| U8 | Object already has only DISABLED cases (the Lead/Opportunity pattern) | new case created; note N5 names the disabled ones, left untouched | (v r1-C8) |
| U9 | The case and the object's lookup belong to different packages | missing dependencies computed and added to P (opt-out), reported in `dependencies.added` | Q16, N8 |
| U10 | Ship the package to another environment | case, settings and link rows travel with P; lookup values are referenced by Id and must exist there with the same Ids (note N4) | Q21 |
| U11 | Continue by hand: open the case in the designer and add steps manually | result carries `designerUrl`; steps are then the user's own edit, outside the tool | design §3.3; M1 |
| U12 | Agent on a .NET 8 environment | same code path; the result and guidance say "expected", not "measured", until the owner-provisioned run | Q24 |

Refusals the agent will meet in these scenarios and what they mean for the user [design §3.2]: `case-exists` (the
object already has an enabled case that differs: editing it is out of scope); `settings-conflict` (another stage column
or a start condition is configured for the object); `stage-list-incomplete` (an explicit list that omits a value: a
subset is not supported, Q10); `lookup-too-small` / `lookup-too-large` (outside 2..20, Q11); `column-not-lookup`;
`record-page-not-freedom` (a Classic-only object, remedy: create a Freedom page and its related-page add-on);
`rights-missing` (a case needs `CanManageDcm`, granted by default only to System administrators, v r6-C3; Q5).

Edge worth stating to users [inferred]: a lookup shared between cases in the product (`OpportunityStage` 12 rows with 6
used, `QualifyStatus` split 7/4 across two cases) is never used one-stage-per-value there (g6 §3); on such objects the
tool refuses anyway (`case-exists`), and on a custom shared lookup the permutation rule (Q10) makes every value a stage.

---

## 6. Scenarios this task explicitly does NOT enable (later tasks)

Ordered by how soon an AI Toolkit user is expected to hit them (r9 §8.2) [inferred]. "Seam" = what the minimal design
already fixes so the later task is additive (design §12).

| Id | Scenario | Why not now | Seam / where it goes |
|---|---|---|---|
| L1 | Steps inside stages: Perform task (call, meeting, to-do), Approval, Send email, Open page, sub-process, change access rights; required vs optional steps | out of scope (Jira); the owner's next task | stages are objects on the wire, `steps` refused with `steps-not-supported`; `IDcmStepFactory` over the existing process element handlers, spiked first (Q27); per-artifact obligations ready for a compilable user task |
| L2 | Next steps component on the record page (`crt.NextSteps`), which the app wizard adds next to the bar | meaningless without steps | the record-page target list in the resolver (design §7.1 item 4) |
| L3 | Automatic transitions (after required / after all steps), result-based stage change | need steps | contract member later; route B save or a `SystemElements` rebuild (Q6) |
| L4 | Restricted transitions (possible next / previous stages), alternative and unsuccessful final stages, grouping, "stage is successful", per-stage colours, stage permissions | out of scope (Jira); used by 13/14 product schemas | additive keys on the stage object (`color` already reserved as unknown, design §3.1) |
| L5 | Start conditions; several cases per object; typed (per-type) cases | out of scope (Jira) | settings row form and `Filters` handled now only as "none" (Q13, Q14) |
| L6 | Editing an existing case: add, rename, reorder or remove stages, change the lookup, new version vs current version | out of scope (Jira); version control and running instances apply (Academy case version control) | describe-shaped `case` block returned now (design §3.3), to be read back by a later `describe-dynamic-case` / `modify-dynamic-case` (Q4 names reserved) |
| L7 | Activate / deactivate / delete a case | not requested | undo is a documented sequence of existing tools (design §9) |
| L8 | Subset of lookup values as stages | scope says one stage per value | Q10 option (b) is additive |
| L9 | Creating or binding stage lookup values; setting the stage column's default value | entity and data changes, not case changes | Q21; design §16 |
| L10 | Execution introspection: which stage and instance a record is in, stage history | not requested | separate read tool [inferred] |
| L11 | Classic (7.x), mobile and mini pages | ONLY 8.x web pages matter (owner) | refusals `record-page-not-freedom` / `placement-unsupported` (Q20) |
| L12 | Detecting a business process that drives the same column | not reliably detectable | guidance warning (Academy execute-case) |

---

## 7. Unknowns and where they are settled

| Unknown | Settled by |
|---|---|
| A tool-made case opens in the DCM designer with the expected stages and no error | M1 (design §14.5) |
| The bar renders after F5; a click persists; a field change moves the bar | M3, E2E case 2, CAADT (design §14.3-14.4) |
| New record starts a case; existing record starts on stage change; restart after the final stage | M7 |
| Any .NET 8 behaviour | M8 on an owner-provisioned stand (Q24) |
| Whether guidance should advise setting the stage column default to the first stage (avoids `DefaultStage` for new records) | not covered by Q1..Q29; owner; recommendation in README §3 |
| Whether `EnableHistoryDCM` stage history is on by default on 8.x stands | not measured (r3 §3.1); V-1 read, recommendation in README §3 |
| Culture-aware display ordering vs database collation | Q9; the result always states the order used |
