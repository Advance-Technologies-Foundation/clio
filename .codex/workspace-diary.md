
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

## 2026-08-25 – ENG-91846: Perform task — CrtProcessBuilder 1.3.0.5 rebundle, MCP contract, E2E net
Context: clio side of ENG-91846 (Perform task usability + AI understanding). The server change lives in
cli-process-builder (ProcessParameterValueValidator: a Lookup constant that parses as a non-empty Guid is
accepted as ConstValue; Guid.Empty is refused as referencing no record; a non-Guid gets a bare-Guid-first
message with the [#Lookup…#] expression as the named fallback). The full live probe matrix is in THAT
repo's diary — the most reusable artefact of the ticket.
Decision — rebundle: `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath C:/Projects/cli-process-builder
-Version 1.3.0.5 -Configuration Release -Framework net10.0`. Three pins refreshed from the produced archive
(sha256 AE2B0E30…, version, descriptor ModifiedOnUtc); the schema-descriptor pin is verified-not-refreshed
by design. The final cut is a SAME-VERSION re-cut done by hand (the runbook's unreleased-branch case: a
call-site comment fix in ProcessMappingService changed an in-archive source after 1.3.0.5 was stamped, so
only ModifiedOnUtc moved - /Date(1787824843000)/); the sha-pin prose carried a FILL-ME-ON-COMMIT slot until
the producing package commit existed. No `[RequiresPackage]` version literal (presence-only is pinned and deliberate); the skew guard
is the convergence detector armed by the version bump. Interim stamps 1.3.0.2–1.3.0.4 existed only inside
this branch and were never released, so 1.3.0.5 is the version every surface documents as the route minimum.
Discovery — the sha-pin's SUMMARY prose names the producing package-repo commit and the rebundle script
does NOT refresh it (it refreshes constants only): it sat naming a 1.2.0.0-era commit through several
rebundles until checked by hand. Update that prose on every rebundle - carry a FILL-ME-ON-COMMIT slot while
the producing commit does not exist yet, and fill it in the clio commit that lands the bytes.
Decision — version story on every surface (two tools, prompt, capability map, guidance): the two thresholds
are separated. "The route ships from CrtProcessBuilder 1.3.0.5" is the fixed feature minimum; "this clio
refuses any environment older than the version it BUNDLES" is the moving convergence threshold, left as a
rule because the refusal itself names both versions at run time. An older clio (no such gate) surfaces the
old package's [#Lookup…#]-macro rejection instead — either refusal means the environment is behind.
MCP surface: [Description] edits in ModifyBusinessProcessTool + CreateBusinessProcessTool and a trigger
line in ModifyBusinessProcessPrompt (bare-Guid route, both refusal surfaces, Guid.Empty); the stale
"Lookup DEFAULT-value macro rules" guide-contents phrase reworded. Reviewed, no update required:
DescribeProcessTool (the absence-is-not-non-existence sentence already shipped), ValidateProcessGraphTool,
GetProcessSignatureTool (deliberately ungated), ListUserTasksTool, CreateBusinessProcessPrompt,
DescribeProcessPrompt, ValidateProcessGraphPrompt, ListUserTasksPrompt, all of Resources/.
E2E — five new tests in ModifyBusinessProcessToolE2ETests, the only regression net this repo has for the
opaque .gz: the perform-task parameter families (all three scheduling pairs, both booleans, the performer
expression route, Recommendation, InformationOnStep, plus bare-Guid ActivityCategory AND ActivityPriority,
every written parameter asserted through the typed describe model); outputs-as-mapping-sources into Guid
process parameters with the [Element:{uid}] metapath read-back plus a clean validate-process-graph shape
(built with the production ProcessGraphNodeArg/ProcessGraphEdgeArg types, not local duplicates); the
non-Guid negative (message still carries 'expression' and '[#Lookup'); the Guid.Empty negative; the
type-incompatible-mapping negative onto the performer lookup. The asserted GUID literals are base-seed
rows safe on every stand: the runtime itself hardcodes "To do" as the category fallback, and "High" is
chosen BECAUSE it is not the shipped Medium default, so the read-back discriminates a write.
Discovery — Activity.AllowedResult derives from outgoing CONDITIONAL flows, not from the category; the
category-driven allowed-results list (GetResultParameterAllValues, ConstValue-only) surfaces on the task
page/designer, so that column can never verify the degradation the ConstValue rule prevents.
Discovery — ShowInScheduler HAS a designer control: the "Show in calendar" checkbox inherited from
BaseUserTaskPropertiesPage (grepping only the ActivityUserTask pages misses it — the resource string
ShowInSchedulerCaption lives on the base page). An earlier claim here and in the guidance said the
opposite; both fixed. Evidence: a designer export carries ShowInScheduler with ModifiedInSchemaUId = the
PROCESS schema, i.e. moved by a human through the UI.
Discovery — a running `clio mcp-server` holds file locks on its build output, so `dotnet build` of
clio/clio.tests/clio.mcp.e2e fails with MSB3021; the MCP E2E harness also leaks one such process per test
session (follow-up filed) — stop strays before building.
ClioRing compatibility reviewed, no Ring-consumed contract changed: grep for the five process-designer tool
names over clio-ring/ClioRing.Ipc, clio-ring/ClioRing, clio-ring/ClioRing.Desktop/actions.json is empty.
Docs: McpCapabilityMap §11 carries the lookup-value change; §4 unchanged. CLI docs: none required (MCP-only
tools, no [Verb]) — stated, not omitted. curated-knowledge-names.json NOT re-pinned (clio still consumes
generation 1.13.43; the guidance lands via the clio-knowledge PR, which must merge only AFTER this rebundle
releases — release-skew rule R16). ai-bp-element-catalog row fixed (invented "Who performs" → OwnerId, the
unsupported required-flag claim dropped, branching credited to ENG-91853, ENG-91846 records the limitation).
Files: clio/CrtProcessBuilder/CrtProcessBuilder.gz, clio.tests/Common/BundledProcessBuilderPackageTests.cs,
clio/Command/McpServer/Tools/ProcessDesigner/{ModifyBusinessProcessTool,CreateBusinessProcessTool}.cs,
clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs,
clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs, docs/McpCapabilityMap.md,
spec/ai-business-process-generation/ai-bp-element-catalog.md
Impact: the shipped archive, the validator relaxation and the guidance move together; an environment behind
1.3.0.5 gets the convergence refusal naming both versions instead of a misleading "parameter unsettable";
the E2E suite guards both accepted parameters and both refusals of the route it ships.

## 2026-08-27 – ENG-91846: performer assignment ships (D3 closed by user decision), reference-existence guard
Context: the user overruled the plan's D3 deferral — "assigned to the sales team" must work in THIS ticket.
The server side (shared UserTaskPerformerApplier extracted from the Send email performer, the
ActivityUserTask-only gate, the Lookup reference-existence guard) lives in cli-process-builder — see THAT
repo's diary (2026-08-27 entry) for the mechanism evidence, incl. the designer-built specimen that proved
BP7 → Activity.OwnerRole with an EMPTY Owner.
Decision — contract: performTask elements take a top-level `performer` block ({type:user|manager|role,
contact?, role?, showPage?}) on create/addElement/setElement — the SAME shape as email.performer, so an
agent learns one vocabulary; describe returns it top-level for ActivityUserTask only (re-appliability is
the read-back contract), while a Send email element keeps it inside its email block. Tool [Description]s
carry the honest-team-assignment sentence (the Activity carries the role in OwnerRole, Owner stays EMPTY,
never fake a team by writing a role id into OwnerId) and the CallUserTask-by-name refusal.
Decision — DTO: DescribedElement gains a typed `performer` member; DescribedEmailPerformer renamed
DescribedPerformer (one wire shape, two report sites). Typed, not extension-bag — the four-dropped-fields
incident is the standard here.
Decision — the wrong-entity OwnerId hole (live-proven: addMapping OwnerId=<SysAdminUnit id> was ACCEPTED
into the Contact-typed parameter) closes in the same cut that opens the real team route: the validator now
requires a Lookup constant to exist in the parameter's reference object, and the tools document the
refusal.
Decision — the version RAISED to 1.3.0.6 (user decision: the ticket ships as if nothing below .6 existed).
The in-place 1.3.0.5 re-cuts had made equal version numbers meaningless to the convergence check, so a
reviewer's stand or checkout could hold stale bytes undetectably; the raise re-arms the detector — every
1.3.0.5 environment now gets the update path instead of the equal-version short-circuit. Cut by the
canonical rebundle-process-builder.ps1 (it also refreshed the three pin constants itself). Every shipped
"CrtProcessBuilder <version>" literal moved to 1.3.0.6, and a NEW guard —
ToolContractVersionLiterals_ShouldMatchTheBundledArchiveVersion in BundledProcessBuilderPackageTests —
pins the tool-description and prompt literals to the archive version, so the next bump fails that test
until every shipped literal moves with the pin (the reviewer's "hand-restated in ~13 places" finding, the
in-repo half; the guidance repo's literals remain unbound — cross-repo asserts have no home). The sha-pin
prose names the producing package commit 354de2d7 and records both prescribed cross-checks as RUN, not
assumed: its descriptor's ModifiedOnUtc equals the pin, and all 114 archive entries equal that commit's
checkout rendering byte for byte (the only committed file absent is the .DotSettings clioignore excludes).
Knowledge base: two records updated in this PR (the capability-map guard record notes the new partial
version-literal pin; the mcp-server lock record widened to Release outputs + the e2e harness leak) and two
platform records added (ShowInScheduler checkbox lives on BaseUserTaskPropertiesPage;
Activity.AllowedResult derives from conditional flows). The other six records the diff touches were
reviewed — facts unchanged, no update required.
Files: clio/CrtProcessBuilder/CrtProcessBuilder.gz, clio.tests/Common/BundledProcessBuilderPackageTests.cs,
clio/Command/McpServer/Tools/ProcessDesigner/{ModifyBusinessProcessTool,CreateBusinessProcessTool}.cs,
clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs,
clio/Command/ProcessModel/IProcessDescriber.cs, clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs,
docs/McpCapabilityMap.md, docs/knowledge/{McpServer,process,platform}/*
Impact: the user prompt "create a call task for the sales team on new order" is buildable and honest —
category Call + performer role, with the agent told to say the Activity's TYPE stays Task and the role
lands in OwnerRole.

## 2026-08-27 – ENG-91846: role existence closes the last silent path; ships as 1.3.0.7
Context: review found the reference-existence guard did not reach `performer.role`, and the hole was
reproduced live (a random Guid accepted, stored, and read back as a normal team assignment). The server
fix lives in cli-process-builder — see that repo's diary for the VwSysRole measurement and why the name
route moved there too.
Decision — clio side: both tool descriptions and the capability map now STATE the check (a role is
verified to exist on either route, so an arbitrary Guid or a user's own SysAdminUnit id is refused rather
than written into `OwnerRole`, which does not control integrity). The reviewer's alternative — documenting
the absence of validation — was the worse half of the choice offered: the block advertises ids, so the
honest fix is the check, not a caveat.
Decision — the version raised again, to 1.3.0.7, for the reason the .6 raise existed: reviewers and
testers are actively installing, and only a raise makes an older cut detectably stale.
Tests — the E2E gap the reviewer named is closed by a live negative (a non-existent role Guid through
`setElement.performer`, asserting the refusal names the value AND that describe reports no performer), and
the reviewer's second Minor by two environment-independent describer unit tests for the TOP-LEVEL
`performer` DTO (present, and absent-stays-null).
Note — the brittle-substring finding on the refusal assertions stands as a trade-off, not a fix: the
server returns prose, not an error code, so a message assertion is the only way to pin that the refusal
stays actionable. Each such test also asserts the BEHAVIOUR (nothing persisted), which is what would
survive a rewording.
Files: clio/CrtProcessBuilder/CrtProcessBuilder.gz, clio.tests/Common/BundledProcessBuilderPackageTests.cs,
clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs,
clio/Command/McpServer/Tools/ProcessDesigner/{ModifyBusinessProcessTool,CreateBusinessProcessTool}.cs,
clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs,
clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs, docs/McpCapabilityMap.md
The sha-pin prose names the producing package commit 53bbee69 and records both prescribed cross-checks as
RUN: its descriptor's ModifiedOnUtc equals the pin, and all 114 archive entries equal that commit's
checkout rendering byte for byte (only the clioignore'd .DotSettings is absent).
Impact: every route that can name a role now proves it exists before storing it.

## 2026-08-27 – ENG-91846: review round on the performer, and the delivery version becomes 1.3.1.0
Context: the performer review raised three caller-outcome findings; the server-side answers live in
cli-process-builder (see that repo's diary for what was refuted by measurement and what was accepted).
Decision — clio side: both tool descriptions now say a `contact` takes a bare Contact record Guid as well as
a formula, and that a role NAME matching more than one role is refused so the caller passes the id.
Decision — the delivery ships as 1.3.1.0, a MINOR raise, on the reviewer's recommendation. The digit is the
message: ENG-91846 adds a capability (a task can be assigned to a team), while every 1.3.0.x stamp before it
was another pass at the same fix. It also spares the in-place alternative's caveat — that anyone holding the
previous cut would have had to reinstall by hand, because the convergence check reads version numbers, not
bytes. Every shipped literal moved with it, which the version-literal guard enforces.
The producing package commit is 948cae8f; the sha-pin prose names it, and both provenance cross-checks ran
against it (descriptor stamp via `git show`, and the entry-by-entry byte comparison: 114/114 archive entries
match that commit's checkout rendering, none missing, none extra).
Files: clio/CrtProcessBuilder/CrtProcessBuilder.gz, clio.tests/Common/BundledProcessBuilderPackageTests.cs,
clio/Command/McpServer/Tools/ProcessDesigner/{ModifyBusinessProcessTool,CreateBusinessProcessTool}.cs
Impact: the contract text matches what the package now accepts on both performer fields.

## 2026-08-28 – ENG-91846: re-review Medium lands, the delivery re-cut as 1.3.1.1
Context: d-krestov's re-review at package commit 948cae8f converted approval to changes-requested: one Medium
to fix (the performer routes resolved a parameter's reference by UId alone, skipping the name-typed
population the validator's own fallback exists for), caption-is-write-once and S107 to fix or ticket. The
server-side fix and both ticket decisions live in the package repo's diary; both are TICKETED with the
refutation arguments recorded (caption = a resource-materialization write needing its own probe round; the
S107 handler move would trade an E2E-pinned loud refusal for silent dropping — the applier has four
consumption sites).
Decision — clio ships the fix as 1.3.1.1, a PATCH: the 1.3.1.0 capability's reach widened, no new
capability. 1.3.1.0 was never released, so it joins the branch-internal stamps and every shipped
"ships from CrtProcessBuilder <version>" literal re-baselines to 1.3.1.1 — the first version to exist
outside the branch. The version-literal guard enforced the sweep (two tool descriptions, the modify prompt);
the capability map and the knowledge guide moved with them by hand (6 literals in the guide, 0 leftovers).
Contract TEXT is otherwise unchanged: the name-typed fix makes documented behavior reach a wider population,
it does not change what the contract says.
Files: clio/CrtProcessBuilder/CrtProcessBuilder.gz (1.3.1.1, stamp /Date(1787902471000)/, sha 16FAB395…),
clio.tests/Common/BundledProcessBuilderPackageTests.cs (pins script-refreshed; the sha-pin prose names the
producing package commit 2ce4ae34, and both provenance cross-checks ran against it — the descriptor stamp
via `git show`, and the entry-by-entry byte comparison: 114/114 archive entries match that commit's
checkout rendering, none missing, none extra),
clio/Command/McpServer/{Tools/ProcessDesigner/*,Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs},
docs/McpCapabilityMap.md.
Impact: archive, pins, and every version literal agree on 1.3.1.1; EOL audit 112/112 CRLF (114 entries with
the two binaries), Common 1137, full unit 9706, knowledge producer 102.

## 2026-08-28 – ENG-91846: the synthesized review lands the missing fail-closed floor and the missing E2E shapes
Context: Alexandr-Kravchuk's six-lens review, changes-requested: one Blocker (the performer /
reference-existence guard is a security-character server change gated only by description literals and
convergence — no [RequiresPackage] floor), one Major (type:manager and the bare-Guid contact route untested
in E2E, create-time inline performer untested), four Minors.
Decision — the Blocker is accepted WITHOUT argument, because the repository's own rule already mandated it:
docs/agent-instructions/bundled-packages.md, "a server-side change with a SECURITY character needs a
literal, always … convergence warns and proceeds when it cannot read the archive … only the literal fails
closed." Create/Modify options move from the 1.2.0.1 email floor to 1.3.1.1 (the released version carrying
the performer block and the existence guard; the email floor is subsumed), the pin test renames off its
email-specific premise, and the D8 comment updates. Verified live: the E2E gate passed against the stand's
recorded 1.3.1.1 with the floor active.
Discovery — the knowledge record "requirespackage-version-floor-cannot-refuse-more-than-convergence" was
WRONG in exactly the two states that matter: its subset arithmetic (floor ⊆ convergence, from F <= B) holds
only while convergence is healthy, and TryGetConvergenceRefusal warn-and-allows on an unreadable archive
(BundledPackageConvergence.cs:80) and on a suffixed bundled version (:90). In those degraded states
convergence refuses NOTHING and the floor is the only fail-closed gate. Record rewritten and renamed to
requirespackage-version-floor-survives-convergence-degraded-modes.md; the reviewer's Blocker text is what
exposed the gap.
Decision — the Major lands as two live E2E tests plus one promoted constant: manager-from-bare-contact-Guid
(modify path; asserts the composed [#Lookup…#] macro, the manager token, showPage=false parity) and
create-time inline performer (user + bare Guid through CreateBusinessProcessTool's OWN deserialization
path). The managerless RUNTIME error is recorded as an ACCEPTED coverage gap in the test description — it
surfaces only when the process runs, and the suite verifies design-time contracts. The Supervisor CONTACT
seed id (410006e1-…) was measured on the stand before pinning: the Supervisor SysAdminUnit is a DIFFERENT
id (7f3b869f-…), so the constant pair also witnesses that the existence guard distinguishes the tables.
Fixture now 34 tests.
Files: clio/Command/{ModifyBusinessProcessCommand,CreateBusinessProcessCommand}.cs,
clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs,
clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs,
docs/knowledge/Common/requirespackage-version-floor-survives-convergence-degraded-modes.md (renamed).
Impact: an environment older than 1.3.1.1 is refused outright on create/modify — not warned — even when
convergence has degraded; every newly documented performer contract shape is exercised live on both entry
points. Command+Common 4975 green.
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
