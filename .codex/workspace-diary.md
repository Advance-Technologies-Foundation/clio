
## 2026-08-19 16:10 – Schema-level export/import: the platform already had it, and its signature moves between cores
Context: issue #1113 — no way to move ONE schema between environments; `pull-pkg`/`push-pkg` carries the whole package, which on a customer production site risks overwriting customization that exists only there. Addons (`AddonSchemaManager`) were the worst case: writable via `create-page-business-rules`, readable by nothing.
Decision: do NOT build per-designer round-trips (one contract per schema kind, and entity schemas / processes have no safe `GetSchema`/`SaveSchema` pair), and do NOT read the file-system representation (`Pkg/<Package>/Schemas/…` is populated only under File Design Mode — the exact heavyweight operation the feature exists to avoid, and cliogate's `PackageExplorer` is rooted at `…/Files` anyway). Instead delegate to the platform's own `Terrasoft.Core.SchemaImporter`, which is type-agnostic and already serialises metadata + properties + localizable values in one self-describing JSON document. Three new cliogate routes (`FindSchemaLayers`, `ExportSchema`, `ImportSchema`) + `export-schema` / `import-schema` CLI verbs and MCP tools.
Discovery — THE API IS NOT STABLE ACROSS CORES, and the failure mode is opaque. CreatioSDK 8.1.4 exposes `SchemaImporter.ImportSchemaToWorkspace(string, Guid, UserConnection)` as a public static; a 10.1.473 stand exposes NO public static of that name at all and serves the operation only through the explicit implementation of `ISchemaImporter.ImportSchema(string, Guid)` — an interface whose TYPE is public but whose MEMBERS are inaccessible outside the core assembly, so it cannot be referenced early-bound either and has to be recovered from `typeof(SchemaImporter).GetInterfaces()`. `ExportSchema(Guid, SystemUserConnection)` is still a public static on both, hence static-first-then-interface dispatch rather than one or the other.
Discovery — a missing member surfaces as WCF `Request Error`, not an exception you can catch. The `MissingMethodException` is raised while the CALLING method is JITTED, so an in-method try/catch never sees it and the endpoint answers with an opaque HTML error page. Isolating each platform call in its own `[MethodImpl(MethodImplOptions.NoInlining)]` method moves the failure inside the caller's try block and turns it into a readable message — that single change is what made the whole investigation tractable. Generalisation: when a cliogate endpoint fails with `Request Error` and nothing in the logs, suspect a JIT-time member/type resolution failure against the target core, not your request body.
Discovery — `ClassFactory.Get(Type)` is a trap. The non-generic overload routes through `GetInstance<T>` with `T` bound to `object` and fails with `Error creating an instance of the "System.Object" class`. Resolve a runtime-only service type via `MakeGenericMethod` on the generic `Get<T>(params ConstructorArgument[])` instead.
Discovery — `SysSchema` has no `SysWorkspaceId` column on 8.x; a workspace filter is not just unnecessary, it breaks the query.
Discovery — a package pushed with `push-pkg` is locked against schema writes even after `unlock-package`; it also needs `InstallType = 0` (an `UPDATE` via `execute-sql-script` works — `SELECT` is blocked by `CustomQuery.ExecuteReader` security) plus an app restart before `create-schema` succeeds in it.
Files: cliogate/Files/cs/CreatioApiGateway.cs, cliogate/Files/cs/Dto/SchemaTransferDto.cs, clio/Command/SchemaTransfer/*, clio/Command/{Export,Import}SchemaCommand.cs, clio/Command/McpServer/Tools/{Export,Import}SchemaTool.cs, clio/Common/ServiceUrlBuilder.cs, spec/prd/prd-schema-level-export-import.md, spec/adr/adr-schema-level-export-import.md
Impact: any schema kind the platform can export now round-trips as a small reviewable folder instead of a multi-megabyte package. The reflection-dispatch helper is reusable for any other cliogate call into a core API that drifts.

## 2026-08-19 17:50 – Schema export/import verified on a stand, and three traps it took to get there
Context: continuation of the entry above; the feature was implemented but unverified end to end.
Discovery — `-p` IS ALREADY TAKEN. `EnvironmentOptions` binds `-p` to `--password`, so a second `[Option('p', "package-name", …)]` makes CommandLine reject the WHOLE verb with `Sequence contains more than one matching element` — before `Execute` runs, so no in-command try/catch can explain it. The command works fine as long as you omit the colliding flag, which makes it look like an argument-value problem rather than a duplicate short name. Both new verbs now take `--package-name` only. Worth checking any new short option against `CommandLineOptions.cs` first.
Discovery — the platform payload is NOT RFC-valid JSON. `SchemaImporter.ExportSchema` embeds the schema metadata as a JSON *string* containing raw CR/LF control characters. `System.Text.Json` refuses it (Python's `json.loads` does too, without `strict=False`), so the bundle's human-readable projections were silently skipped on every real export while the "best-effort" catch swallowed it. Switched the projection parser to Newtonsoft, which accepts it; 28 per-culture resource files then appeared for a real addon.
Discovery — reflection hides the real error. Invoking the platform importer through `MethodInfo.Invoke` wraps every platform failure in `TargetInvocationException` ("Exception has been thrown by the target of an invocation."), including the ones a caller most needs — e.g. `Unable to save changes for item "X". It is either created by third-party publisher or installed from the file archive`. The error builder now unwraps the chain before reporting.
Discovery — `ClassFactory.Get(Type)` is not the non-generic form of `Get<T>()`. It routes through `GetInstance<T>` with `T` bound to `object` and fails with `Error creating an instance of the "System.Object" class`. Resolve a runtime-only service type via `MakeGenericMethod` on the generic `Get<T>(params ConstructorArgument[])`.
Verified on `sae_m_seeenu_15888720_0820` (cliogate 2.0.0.46, .NET Framework, core 10.1): `FindSchemaLayers` lists layers and disambiguates; `export-schema` produced a full addon bundle (`ActivityBusinessRule` — descriptor + metadata + properties + 28 per-culture resource files); `import-schema --dry-run` reported REPLACE / CREATE / refused NEW LAYER correctly; a delete-then-import round trip recreated `UsrClioTransferProbe` WITH ITS ORIGINAL UId (`2c5cd215-1994-4758-9314-a9c866f0dcbe`), which is the property that makes repeated transfers safe. Importing a Creatio-owned addon into a customer package is refused by the platform itself ("third-party publisher"); that refusal now surfaces verbatim.
Operational note: `push-pkg` of cliogate on a stand that is simultaneously serving other clio requests can wedge for 10+ minutes and then fail in `UploadFile`. Deploy serially, with nothing else touching the environment.
Files: clio/Command/{Export,Import}SchemaCommand.cs, clio/Command/SchemaTransfer/SchemaBundleStore.cs, cliogate/Files/cs/CreatioApiGateway.cs, clio/help/en/{export,import}-schema.txt, clio/docs/commands/{export,import}-schema.md, spec/adr/adr-schema-level-export-import.md
Impact: the issue's scenario is now one command each way, with a dry run in between, instead of a 3.7 MB package push.

## 2026-08-21 14:20 – odata-create reports its side effect
Context: an E2E run of the process-designer MCP surface exposed a create defect. Three
`odata-create` calls into `MailboxSyncSettings` each reported `failed: 1` while every row was in
fact inserted, so the caller retried and produced three duplicate mailboxes.
Decision: model the side effect the way this repo already models `section-created` — nullable bool
plus `retry-guidance` — and reserve `false` for rows rejected locally, before any request leaves
clio. Every server-side failure is `null` (unknown), never "not created".
Discovery: a Creatio OData POST can return an error AFTER the row is written (a post-insert entity
event handler that throws), so a failed POST does NOT imply no side effect. Separately: the curated
contract in `ToolContractGetTool.cs` lists output fields by hand and does not follow the response
record, so it went stale until updated explicitly — a build cannot catch that drift.
Files: clio/Command/McpServer/Tools/ODataCreateTool.cs,
clio/Command/McpServer/Tools/ODataCreateBatchResponse.cs,
clio/Command/McpServer/Tools/ToolContractGetTool.cs
Impact: a consumer can distinguish an unverified insert from a verified failure instead of silently
duplicating rows on the natural retry.

## 2026-08-26 - ENG-92715 Open edit page element: clio side
Context: clio consumer surface for the new `openEditPage` element type shipped by CrtProcessBuilder (see the cli-process-builder diary entry of the same date for the server half and the platform discoveries behind it).
Decision: declare the describe block as a TYPED DTO rather than leaving it to the element's extension bag. Note the inconsistency this exposes - the Modify data `changeData` block is still undeclared and reaches callers through `[JsonExtensionData]`; worth aligning, deliberately out of this story's scope.
Discovery:
- `ManagerMap.ResolveDataId` needed the token listed EXPLICITLY: "openeditpage" does NOT end with the "usertask" suffix the fallback arm matches on, so without an entry a VALID graph resolves to EventType.Unknown and validate-process-graph rejects it. Pinned by two test cases - one for the camelCase data-id (covered by the suffix) and one for the build token (needs the explicit entry). The same trap already applies to "sendemail".
- ClioRing compatibility reviewed: no Ring-consumed contract changed. Inspected clio-ring/ClioRing.Ipc, clio-ring/ClioRing and clio-ring/ClioRing.Desktop (incl. actions.json) - none reference create/modify/describe-business-process or the openEditPage block; Ring's actions only carry environment entries. ClioRing.Tests 156/156 green against the changed contract.
- The mandatory NativeAOT publish gate could NOT be completed on this machine: `dotnet publish clio-ring/ClioRing.Desktop -r win-x64 -p:PublishAot=true` fails at the NATIVE LINK step with "Platform linker not found ... Desktop Development for C++ workload". The managed/ILC stage DID run and produced the assembly with ZERO IL2026/IL3050 (zero IL#### diagnostics at all), so nothing indicates an AOT regression - but the gate stays formally incomplete until it is run where the C++ toolchain exists (or in CI).
Files: clio/Command/ProcessModel/Schema.cs, clio/Command/ProcessModel/IProcessDescriber.cs, clio/Command/McpServer/Tools/ProcessDesigner/{Create,Modify,Describe}BusinessProcessTool.cs, clio/Command/McpServer/Prompts/ProcessDesigner/{Create,Modify,Describe}*Prompt.cs, spec/ai-business-process-generation/ai-bp-element-catalog.md, clio.tests/Command/ProcessModel/{ManagerMapResolveDataIdTests,ServerProcessDescriberTests}.cs, clio.mcp.e2e/{Create,Modify}BusinessProcessToolE2ETests.cs
Impact: clio unit suites green (7291 passed across the Command/McpServer modules; ServerProcessDescriber 24/24; ManagerMap 53/53). Four E2E tests written and compiling but NOT RUN - they are gated and, more importantly, cannot pass until the package is REBUNDLED into clio (the install path reads the archive from build output, so an un-rebundled clio ships the old archive and the server rejects the element type). Still open in this repo: rebundle + SHA-256/ModifiedOnUtc/version pins + [RequiresPackage] floors, then run the four E2E on a stand - that run is also the only proof of the page-candidate ESQ and the SysSchema name resolution, which the unit tests substitute away.

## 2026-08-26 – openEditPage performer on the clio side
Context: the Open edit page element gained "Who performs the task?" / "Show page automatically" (ENG-92715, field
moved in from ENG-94917 by reusing the Send email machinery server-side).
Decision: added `DescribedOpenEditPagePerformer` as its OWN DTO rather than reusing `DescribedEmailPerformer` — the
two elements document different rules (Send email offers the field only in manual mode), so a shared DTO would make
the read-back's doc comments wrong for one of them.
Discovery: `performer: null` in a read-back means UNASSIGNED — the designer's own initial state — not "the server
does not support it"; the tool `[Description]` and the guidance article both say so explicitly, because the
ambiguity would otherwise push a caller into writing an assignment nobody asked for.
Files: clio/Command/ProcessModel/IProcessDescriber.cs,
clio/Command/McpServer/Tools/ProcessDesigner/{Create,Modify,Describe}*.cs,
clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs,
spec/ai-business-process-generation/ai-bp-element-catalog.md,
clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs,
clio.mcp.e2e/CreateBusinessProcessToolE2ETests.cs
Impact: describe/create/modify all carry the field; validated with
`dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"` (7291 passed).

## 2026-08-26 – openEditPage logActivity on the clio side
Context: the Open edit page element gained the "Log activity" block (three scheduling pairs + the gate + the
calendar flag). `ActivityPriority` stays out until GHE PR 36 relaxes the Lookup-constant validator.
Decision: `DescribedActivityInterval` reports `value`, the decoded `unit` AND the raw `period`. The raw integer
travels so an unrecognized period stays visible instead of being swallowed into a null unit.
Discovery (test-filter trap): `ServerProcessDescriberTests` carries `[Property("Module", "ProcessModel")]`, so the
usual `Module=Command|Module=McpServer` filter does NOT run it — two earlier "7291 passed" runs never exercised the
describer tests they were quoted as validating. Correct filter for process-designer work:
`Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel)` — 7405 passed.
Sequencing recorded here so it is not rediscovered: the rebundle must WAIT for clio PR #1190 (it moves
`ExpectedArchiveVersion` to 1.3.0.5 plus the SHA-256/ModifiedOnUtc pins this branch also moves — same three
constants, guaranteed conflict), and clio-knowledge PR #90 edits the same `process-modeling.md` adding an
"Element: Perform task" section.
Files: clio/Command/ProcessModel/IProcessDescriber.cs,
clio/Command/McpServer/Tools/ProcessDesigner/{Create,Modify,Describe}*.cs,
clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs,
spec/ai-business-process-generation/ai-bp-element-catalog.md,
clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs,
clio.mcp.e2e/CreateBusinessProcessToolE2ETests.cs
Impact: create/modify/describe carry the block; the E2E is the only proof both members of each pair actually land.

## 2026-08-27 – Priority was NOT blocked; corrected
Context: I had deferred the activity `Priority` to GHE PR 36 (the Lookup-constant validator relaxation). Challenged
on it, the claim did not survive inspection.
Discovery: the block was a CONSISTENCY argument, not a technical one, and it was wrong twice over.
(1) Binders never go through `ProcessParameterValueValidator` — `AssignConstValue` writes `SourceValue` directly, and
already does so for `PageSchemaId`/`ObjectSchemaId`. The validator guards the generic `addMapping`/`setParameter`
paths only.
(2) The package ALREADY stores two different lookup encodings by design: `RoleId` as a `[#Lookup…#]` Script macro
(the performer card reads a macro) and, now, `ActivityPriority` as a BARE record Guid ConstValue. The decisive
evidence is the designer's own card: `ProcessUserTaskActivityEditSchema.js` `_initActivityPriority` reads the raw
parameter value and matches it against the `ActivityPriority` record Ids, so a macro matches nothing and the REQUIRED
field renders empty. The runtime is indifferent — `OpenEditPageUserTask.cs` just feeds it to
`UserTaskActivityInfo.PriorityId`. The ConstValue-only sensitivity ENG-91846 hit is specific to `ActivityCategory`,
whose allowed-results derivation reads `SourceValue.Value`.
Rule recorded on the constant: each lookup is stored the way the CARD that displays it reads it back — not one
package-wide convention.
Files: packages/CrtProcessBuilder/Files/src/cs/Elements/ActivityLogBinder.cs, ProcessDesignConstants.cs,
Contracts/{ProcessDescriptorContracts,DescribeContracts}.cs,
tests/CrtProcessBuilder/Elements/OpenEditPageConfigBinderTests.cs
Impact: 864 package tests green. Nothing in this feature now waits on PR 36.

## 2026-08-27 – Page-element routing: Open edit page is the DEFAULT, not one of three equals
Context: an E2E scenario (recruiter fills in a new employee's card) turns on the agent choosing Open edit page over
Pre-configured page / Auto-generated page. The existing routing rule listed the three symmetrically — one sentence
each, no priority, no tie-breaker — which is exactly the shape that loses a coin-flip.
Decision: rewrote it as a single question with a default. "Is a user filling in COLUMNS of a record?" → Open edit
page, no further deliberation; the other two require a POSITIVE signal (no record whose columns are edited / a
specific named page). Two consequences stated because neither is inferable:
(1) the alternatives are NOT buildable, so mis-routing produces no process at all rather than a different one —
    the failure is total, not stylistic;
(2) the element choice must NOT be handed back as a question (the scenario's baseline AC forbids it); asking which
    OBJECT or COLUMN is meant stays fine, and a defensible tie is resolved by picking Open edit page and stating the
    interpretation in one line.
Added a further tell: a request that also wants a note/hint shown on the page is Open edit page — `recommendation`
and `hint` exist nowhere else in this contract.
Files: clio-knowledge guidance/mcp/guides/processes/process-modeling.md,
clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs,
clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs,
spec/ai-business-process-generation/ai-bp-element-catalog.md (both alternative rows now marked NOT buildable)
Impact: 7406 unit tests + the 7 WorkspaceTemplateGuidanceDrift guards green. Env note: a partial .NET update landed
mid-session (AspNetCore 10.0.11 without NETCore.App 10.0.11) and aborted every test run until the runtime finished
installing — not a code failure, worth recognizing rather than re-diagnosing.

## 2026-08-27 – First real E2E run: three test defects, one of them hiding a contract defect
Context: the eleven openEditPage E2E tests had never been executed. Deployed the package to eng-92715-0905 and ran
them: 8/11.
1. A modify test died with NullReference because `setElement` had been REFUSED and the test read it as success.
   Root cause worth remembering: clio-run reports a refused edit with exit-code 1 INSIDE the payload while
   `isError` stays null, so `result.IsError.Should().NotBeTrue()` passes for a refusal. This file already had
   `ModifyExpectingSuccessAsync`, which asserts the "edited (" line — every new test now goes through it. Never
   assert MCP success through IsError alone.
   The refusal it hid was a real contract defect, fixed in the package: an object-bound block could not be set on
   an update without re-sending the page.
2. An assertion matched "Supply 'defaultValues'" while the tool envelope escapes apostrophes as ' — the
   refusal was correct and the assertion could never match. Match quote-free fragments, and assert the reason as
   well as the field.
3. A create-side test still expected showPage true for a ROLE performer — stale since the semantics changed to
   "showPage follows the performer". Corrected with the reason.
Also: `McpE2E.Sandbox.EnvironmentName` in clio.mcp.e2e/appsettings.json is how the suite picks its environment.
The file is TRACKED, so it was set for the run and reverted — a machine-specific env name does not belong in the
repo. Re-set it before any future run.
Result: 11/11 green on the stand.
