# ENG-91846 — "Perform task" element: usability status + AI understanding — Implementation plan

Status: plan, ready for implementation.
Author: lead architect. Evidence base: seven parallel research passes + direct re-verification of every load-bearing
claim (see `## 10` for what was re-verified and what was NOT).
Related tickets: ENG-91845 ("Connected to" connections — OUT OF SCOPE here, and **already merged** in both repos:
see §2 E2), Task 6 (value sources).
Tracked as task 8 of `C:/Projects/clio/spec/process-design-service/task-list.md:14`.

> **Read §2 E (current state of the three checkouts) before anything else.** Three of its items change what you do
> first: an uncommitted rebundle stamp in ProcessBuilder, a `clio-knowledge` checkout 7 commits behind the branch
> whose manifest you must version-bump, and the fact that the ticket's "blocked on Task 7" premise is stale.

---

## 1. Task

### Goal

Make the **"Perform task"** element fully usable from the ProcessBuilder (the AI-driven business-process builder),
and make the AI *understand* the element: which parameters it has, when the element is used, and what it produces.

### Acceptance criteria (from the ticket)

| # | AC | Interpretation used by this plan |
|---|---|---|
| AC1 | Support **all params** of the element | Every one of `ActivityUserTask`'s 37 **statically declared** parameters must be reachable through a documented, live-verified write route (or explicitly documented as an output / inert / out-of-scope). The live set on a given environment is `37 + N` — see §4's "the live parameter set is 37 + N" subsection and probe P0. |
| AC2 | Add **guidance about the element** in clio | A per-element "Perform task" section in the curated knowledge library (`clio-knowledge`), reachable via `get-guidance name=process-modeling`. |
| AC3 | **"Connected to" — OUT OF SCOPE** | The 19 "Connected to" lookup parameters belong to ENG-91845. This plan documents the boundary and must not implement connection binding. |

### Estimate and the scope choice it forces

Estimate in the ticket: ~1.5 days. **The full S0–S8 path does not fit that budget, and pretending it does is what
causes the rebundle (S2b) and the deploy loop (S0b) to be skipped.** The must-do path spans three repositories
(ProcessBuilder, clio, clio-knowledge — the last with a cross-repo PR and a version bump computed from
`origin/master`), a sequential live probe matrix against a .NET Framework stand, a server code change that has to
be **rebundled into clio's shipped archive** with four test pins recomputed, a clio DTO change, a full clio unit
suite run (because `clio/Common/` is touched — `AGENTS.md` smart-regression rule 4), and E2E against a real
environment. Each probe iteration in S1/P2a additionally costs a build → deploy → restart → re-authenticate cycle.

**Make the choice explicit before starting. Two honest options:**

- **Option A — ship the 1.5-day slice.** Do **S0, S0b, S1 (probe), S2 (+ S2b rebundle), S7 (guidance), S8 (E2E)**.
  Move **S4/S5** (`isPerformer` / `isRequired` contract fields) into the follow-up alongside **S9**, on D2's own
  argument that they "confirm rather than discover". This is the recommended default: it delivers both ACs.
- **Option B — keep S4/S5 in.** Then restate the estimate as **~3 days** with the rebundle, the pin recomputation,
  the full unit suite and the deploy loop costed in, and say so on the ticket.

Steps marked **STRETCH** are deferrable under either option.

### The ticket's works / does-not-work claims — and their verified status

| Ticket claim | Verified status |
|---|---|
| Add element (`performTask` → `ActivityUserTask`, dedicated-palette specialization) works | **CONFIRMED.** `UserTaskElementHandler.ResolveUserTaskName` maps the alias (`.../Elements/UserTaskElementHandler.cs:100-113`); the palette promotion is a `SysProcessUserTask` read (`:124-134`). |
| Fixed auto-created params present and settable via `addMapping`: Duration, DurationPeriod, StartIn, StartInPeriod, RemindBefore, RemindBeforePeriod, ShowInScheduler, ShowExecutionPage, IsActivityCompleted | **CONFIRMED** for the 8 Integer/Boolean ones. `IsActivityCompleted` is settable but is a runtime **output** — see G7. |
| "ActivityPriority lookup" settable by constant | **CONTRADICTED BY CODE.** `ProcessParameterValueValidator.ValidateConstantValue` rejects *any* plain `value` on a Lookup parameter before it ever inspects it (`.../Parameters/ProcessParameterValueValidator.cs:62-68`), and a unit test pins that rejection (`tests/CrtProcessBuilder/ProcessMappingServiceTests.cs:437-456`). The ticket most likely observed the **shipped default** (`ab96fa02-7fe6-df11-971b-001d60e938c6`) in describe output and read it as a successful write. **Must be re-tested (probe P1) before the works/not-works table is published.** |
| Read-back via `describe-process` works | **CONFIRMED but misleading** — describe shows 11 of 37 parameters. See G1. |
| Performer / Owner "not among auto-synced params" | **FALSE.** `OwnerId` *is* auto-created on every element. Assigning `ProcessSchemaUserTask.SchemaUId` triggers `SynchronizeParameters()`, which clones **every** declared parameter with no filtering (`Terrasoft.Core/Process/ProcessSchemaActivity.cs:291-322`). `OwnerId` is simply invisible to describe because it ships no default. See G2. |
| Task subject / "What needs to be done" — to verify | It is the `Recommendation` parameter. **Write path is UNVERIFIED at runtime** — see G3 / probe P3. |
| Value sources beyond constant / process parameter — blocked on Task 6 | **CONFIRMED as a boundary**, but partially false as a *blocker*: `expression` already accepts arbitrary macros with zero validation (`.../Mappings/ProcessMappingService.cs:122-126`), so lookup/date/system-variable values are reachable today. What Task 6 owns is *first-class, validated* source fields. See G5. |

### Two corrections to the incoming research (resolved by direct extraction)

1. One researcher wrote "only **nine** parameters carry a ConstValue default" then listed ten. Direct extraction of
   `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas/ActivityUserTask/metadata.json` gives
   **exactly 10** entries with `L8.GS1 == 1`. The describe-visible set is those 10 **plus** `ActivityResult`
   (`L13 = true`) = **11**. Use 10 / 11, not 9 / 11.
2. Researchers disagreed on whether the workspace `CrtProcessBuilder` matches the deployed copy under
   `Terrasoft.WebApp.Loader/.../Pkg/CrtProcessBuilder`. **Treat `C:/Projects/workspace/ProcessBuilder` as the only
   source of truth**; the deployed tree is a build artefact of unknown vintage. Diff before assuming (see R6).

---

## 2. Context you must load first

Read these before writing any code. Grouped by repo, ordered by how load-bearing they are.

### A. ProcessBuilder workspace — `C:/Projects/workspace/ProcessBuilder/`

| Path | Why |
|---|---|
| `packages/CrtProcessBuilder/Files/src/cs/Elements/UserTaskElementHandler.cs` | The entire `performTask` create path in 140 lines: alias resolution, the single `SchemaUId` assignment that triggers parameter sync, the palette promotion. |
| `packages/CrtProcessBuilder/Files/src/cs/Parameters/ProcessParameterValueValidator.cs` | The **only** constant-value gate. Lines 62-68 (Lookup hard reject) and 74-80 (Date/Time hard reject) are the code this ticket most likely has to change. |
| `packages/CrtProcessBuilder/Files/src/cs/Mappings/ProcessMappingService.cs` | `BuildSourceValue` (99-141) — the single parse point for all four `addMapping` sources; `EnsureSingleSource` (147-167); target resolution (63-86). |
| `packages/CrtProcessBuilder/Files/src/cs/Describe/ProcessDescriber.cs` | `ReadElementParameters` (129-140) — the three-clause filter that hides 26 of 37 parameters. Read the XML doc above it: the filter is *intentional*. |
| `packages/CrtProcessBuilder/Files/src/cs/Parameters/ProcessParameterService.cs` | `ToDescribeParameter` (92-113) — the projection every described parameter goes through; where `isPerformer` would be added. |
| `packages/CrtProcessBuilder/Files/src/cs/ProcessDesignConstants.cs` | `ElementTypes` (216-224), `UserTasks.PerformTask = "ActivityUserTask"` (~236), and the `Connections` block (~112-145) that documents the **ActivityCategory-must-be-ConstValue** trap and the OwnerId/Owner name mismatch. |
| `packages/CrtProcessBuilder/Files/src/cs/Catalog/UserTaskCatalog.cs` | `GetUserTasks` (42-63) returns name+UId only — the discovery surface that does *not* exist today. |
| `packages/CrtProcessBuilder/Files/src/cs/Contracts/ListUserTasksContracts.cs` | `UserTaskInfo` (35-51) — the DTO a parameter catalog would extend. |
| `packages/CrtProcessBuilder/Files/src/cs/Contracts/DescribeContracts.cs` | `DescribeProcessRequest` (13-28), `DescribeProcessElement`, `DescribeProcessParameter` (~298-353) — the exact wire shape the AI receives. |
| `packages/CrtProcessBuilder/Files/src/cs/EntryPoints/WebService/ProcessDesignService.cs` | The complete endpoint surface. **Note `ListUserTasks()` at :67-74 takes NO request parameter** — that constrains D2. |
| `packages/CrtProcessBuilder/Files/src/CrtProcessBuilderApp.cs` | Composition root. Element handlers ~106-110, operations ~140-155, connections knowledge layer ~156-172. Any new class is registered here and pinned by tests. |
| `packages/CrtProcessBuilder/Files/src/cs/Connections/ConnectionCapability.cs` | The **existing precedent** for per-user-task knowledge (name-keyed table + DI + consulted by read and write). The pattern to copy — or consciously not to. |
| `tests/CrtProcessBuilder/ProcessMappingServiceTests.cs` | Where element-parameter write behaviour is tested. `AddUserTask`/`AddElementParameter` (26-72) is the canonical way to hand-build a typed element parameter. Lines 401-456 pin the three constant rejections this ticket may revise. |
| `tests/CrtProcessBuilder/ProcessDescriberTests.cs` | Lines 143-178 pin "unbound non-output inputs are omitted". Changing the describe filter means changing this test. |
| `tests/CrtProcessBuilder/BaseComposableAppTestFixture.cs` | The fixture base (UserConnection, `MockEntitySchemaWithColumns`, `SetUpTestData`, `CreateDescriber`, `CreateProcessOperations`). |
| `tests/CrtProcessBuilder/ProcessDesignTestSupport.cs` | `TestProcessSchema` + `CreateUserConnection()` — required for any test that assigns a parameter **value** on an in-memory schema. |
| `tests/CrtProcessBuilder/UserTaskElementHandlerCreateTests.cs` | Template for a Perform-task create test, incl. the **two-arg `ExecuteReader` trap** for a parameterized `Select` (41-55). |
| `tests/CrtProcessBuilder/Connections/ConnectionCapabilityTests.cs` | The `new ProcessSchemaParameter(Substitute.For<DataValueType>(…))` recipe at 70-82. Note the `Connections/` folder segment. |
| `.codex/workspace-diary.md` | Append-only. Read the last ~10 entries; append one at the end of this work (mandatory per repo AGENTS). **Line 484 (addendum 16) closes G10** — read it before re-opening the mapping-`Name` question. |
| `CLAUDE.md` (lines ~38-45) | Build-configuration rule: only the frameworks present in `.application/` are buildable. This host has **only `net-framework`**, so only `-c dev-nf`. |
| `CLAUDE.md` (lines 54-97) | **Deploy workflow — mandatory Step 0 is `get-fsm-mode`.** FSM ON and FSM OFF need *different* command sequences, and `compile-creatio` in FSM silently overwrites a good filesystem build from the stale DB copy. Also records that clio auth dies after `restart-by-environment-name`. Drives S0b. |
| `CLAUDE.md` (lines 101-110) | Build the **solution** (`dotnet build MainSolution.slnx -c dev-nf`), not the package csproj — the solution is the unit that covers the package plus any `.esproj`. On Windows pass `MainSolution.slnx` **without** a leading `.\`. |

### B. Creatio platform reference (read-only)

| Path | Why |
|---|---|
| `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas/ActivityUserTask/metadata.json` | **Authoritative** declaration of all 37 parameters: `L1` type, `L8` default, `L9`/`L16` reference schema, `L13` IsResult, `L14` IsPerformer, `L17` tag. Ground truth for §4. |
| `./captures/performtask-designer-capture.md` | **What the designer actually writes** for this element, from a real exported process. Outranks inference wherever the two disagree — see §2 E7. Read before §4 and §6. |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core.Process/UserTaskActivityHandler.cs` | The runtime write path, and the two mechanisms that decide what lands on the Activity: `SetPerformer` (`:65-77`) writes the owner column; `GetActivitySchemaColumns` (`:79-87`) filters to `EntityColumnValue`-tagged params **by column name**; `SetColumnValuesFromParameters` (`:104-117`); `Create` (`:233-267`), where the call order is `:249` then `:253`. |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core.Process/PerformerAssignment/AssignmentOptionsInitializer.cs` | `Init()` (`:198-219`) — the `BP7`-present vs `BP7`-absent branch; `:97-99` the empty-performer coercion. Why `OwnerId` alone suffices (D3). |
| `.../Resources/ActivityUserTask.ProcessUserTask/resource.en-US.xml` | EN captions + `Group` (General vs "Connected to"). The human-facing names the guidance must map to codes. |
| `.../Schemas/ActivityUserTask/ActivityUserTask.cs` | Runtime: `NewDate` (58-71, the period enum), `CreateActivity` (142-158, incl. `PriorityId = ActivityPriority` at **:152**), `GetActivityTitle` (**88-90** — a one-line delegation), the two `InformationOnStep` read paths (**:110** in `WriteExecutionData` and **:119-120** in `GetExecutionData`, the latter reached only when `UseProcessPerformerAssignment` is OFF — the check is at **:115**), `ActivityUserTaskSchemaExtension.GetResultParameterAllValues` (189-213, with the `Source == ConstValue` gate at **194-196**) — the decisive proof that `ActivityCategory` must be a plain-Guid **ConstValue** — and `SynchronizeDynamicParameters` (**216-219**), which derives extra connection parameters per environment. |
| `.../Schemas/ProcessUserTaskUtilities/ProcessUserTaskUtilities.cs` | The real implementations behind `ActivityUserTask`'s one-liners: `GetActivityTitle` (**577-590**) — `return (titleValue ?? GetSchemaElementCaption(processElement)).Value?.Truncate(500);`, i.e. the caption fallback and the 500-char truncation both live here — and `SynchronizeActivityConnectionParameters`, the per-environment dynamic-parameter source (see §4). |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core/Process/ProcessSchemaParameter.cs` | Meta short-code map `L1..L19` (124-143; `IsRequired = L6` at :129, `IsPerformer = L14` at :136), the `ProcessSchemaParameterValueSource` enum (16-26), `Direction` default = `Variable` (416), `IsPerformer` (**425-429, a plain auto-property with a public setter**), `IsRequired` (**524-528, likewise settable**). Both matter for the S4 fixture — see §8.1. |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs` | **How a `ConstValue` actually becomes code.** Non-class parameter (a Lookup's CLR type is `Guid`): field initializer `GeneratorUtilities.GenerateValue(sourceValue.Value, null, parameterType, 4)` (**756-763**). `TextDataValueType` / `LocalizableStringDataValueType`: getter `GetLocalizableString("<resourceManager>", "<sourceValue.ResourceItemName>")` (**641-650**) — it reads the schema RESOURCE and never touches `Value`. These two branches are the real evidence behind D4 and G3 respectively. |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core/DataValueType.cs` | `LookupDataValueType : GuidDataValueType` (**1967**), `IsLookup => true` (**1985**) — so a Lookup's `ValueType` *is* `typeof(Guid)`. Type-UId constants: `TextDataValueTypeUId` (**:129**) vs `LocalizableStringDataValueTypeUId` (**:168-169**) — the distinction that decides which generator branch above applies. |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core/Process/ParameterConstValuesValidationRule.cs` | The platform's own `ConstValue` check (**73-86**): for every `Source == ConstValue` parameter it calls `ValueProvider.GetParameterValue` and reports the exception as a validation error. Gated by `GlobalAppSettings.FeatureUseParameterConstValuesValidationRule` (`GlobalAppSettings.cs:139`, registered at `ProcessInterpretationValidator.cs:272-273`). This is what probe P2 exercises server-side. |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core/Process/ProcessSchemaActivity.cs` | `SynchronizeParameters` (587-599), `FillNewSchemaParameters` (291-306), `GetCanSynchronizeParameters` (324-326) — **the UId-ordering trap**. |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core/Process/ProcessSchemaUserTask.cs` | The `SchemaUId` setter that fires the sync (106-115); `GetSchemaParameters` (243-246). |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core/Process/ProcessEnum.cs:212-219` | `ProcessDurationPeriod`: `Minutes=0, Hours=1, Days=2, Weeks=3, Months=4`. |
| `C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Core/Process/ProcessSchemaPerformerAssignmentOptions.cs` | `BP7` / `JH1`/`JH2`/`JH3` — the richer performer model that this ticket **defers** (D3). |
| `C:/Projects/UnitTests/ProcessDesigner.UnitTests/ActivityUserTask_Tests.cs` | The core team's authoritative Perform-task fixture; pins the exact `CreateActivity` field set and the performer surface. |

### C. clio — `C:/Projects/clio/`

| Path | Why |
|---|---|
| `clio/Command/ProcessModel/IProcessDescriber.cs` (`DescribedParameter` at 460-508) | The **only** typed model on this surface. An undeclared server field is silently dropped. |
| `clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs` | The `addMapping` contract text the AI reads at tool-selection time (`[Description]`, ~28-108). |
| `clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs` | The read-back contract text. **The "unbound element inputs are omitted — absence does not mean the parameter does not exist" sentence is ALREADY in the shipped `[Description]` at :29** — do not add it again (S6). |
| `clio/Command/McpServer/Tools/ListUserTasksTool.cs` | The sixth relevant tool — it lives **outside** `Tools/ProcessDesigner/` but is in scope for this ticket's AI-understanding AC, and it *is* `[FeatureToggle("process-designer")]`-gated (:12). |
| `clio/Command/McpServer/Prompts/ProcessDesigner/` | **Four** prompts: `CreateBusinessProcessPrompt.cs` (already instructs `list-user-tasks`, :26), `DescribeProcessPrompt.cs`, `ModifyBusinessProcessPrompt.cs`, `ValidateProcessGraphPrompt.cs`. Plus `Prompts/ListUserTasksPrompt.cs` one level up. All are mandatory review targets (S6). |
| `clio/Command/ListUserTasksCommand.cs` | Posts a literal `"{}"` body (:59) — proof the `ListUserTasks` operation is no-arg on the wire. Its options class carries `[RequiresPackage]` (:15). |
| `clio/Common/ServiceUrlBuilder.cs` | `ProcessDesignService` routes at 294-298 (`BuildProcess` 49, `ListUserTasks` 50, **`ProcessBuilderPing` 62 at :296**, `DescribeProcess` 51, `ModifyProcess` 52); highest `KnownRoute` today is `ProcessBuilderPing = 62` (:232), so a new route is **63**. |
| `clio/CrtProcessBuilder/CrtProcessBuilder.gz` | **The archive clio ships.** Every S2/S4/S9 server change is invisible to clio users until this is rebundled — see S2b. |
| `clio/Common/BundledPackages.cs`, `BundledPackageCatalog.cs`, `BundledPackageConvergence.cs` | Package identity, the version read out of the archive, and the convergence rule that refuses a gated call when the environment's package predates the shipped archive. **The convergence rule — not a `[RequiresPackage]` version literal — is how the skew in S2b/§10 R16 is surfaced to the user.** |
| `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | Four pins that go red on any rebundle: `ExpectedArchiveSha256` (:111), `ExpectedArchiveVersion = "1.1.0.0"` (:137), `ExpectedDescriptorModifiedOnUtc` (:163), `ExpectedSchemaDescriptorModifiedOnUtc` (:178). `[Category("Unit")] [Property("Module", "Common")]` (:38-39) — **not** covered by a `Module=McpServer` filter. |
| `clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs` | Asserts the four process-designer gates are **presence-only** (`requirement.Version.Should().BeNullOrEmpty()`, :59, :93) and that `get-process-signature` carries none (:71-77). Also pins the convergence refusal (:125-195). **Do not add a version literal** — it is deliberately absent. |
| `docs/agent-instructions/bundled-packages.md` | Mandatory reading before touching the archive (per `AGENTS.md`). Carries the three silent-failure platform facts (UId identity, `ModifiedOnUtc` gating the recorded version, installed≠compiled) and the build-output trap. |
| `clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs` | Already builds `performTask` elements and maps a `Duration` element parameter (~803-825). The fixture to extend. |
| `clio.mcp.e2e/Support/Configuration/ProcessDesignerE2EGate.cs` | The skip gate + `McpE2E.ProcessDesigner` category every new E2E must use. |
| `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` | Pins library `1.13.19` / sequence `34`; `process-modeling` sits in `featureGatedNames`. Re-pin if a new generation publishes. |
| `docs/McpCapabilityMap.md` (§11, ~676-706) | In-repo capability narrative for this surface — a mandatory doc target on any contract change. |
| `AGENTS.md` | The MCP maintenance policy, the guidance-lives-in-clio-knowledge rule, and the ClioRing compatibility gate. |

### D. clio-knowledge — `C:/Projects/clio-knowledge/` (guidance)

| Path | Why |
|---|---|
| `guidance/mcp/guides/processes/process-modeling.md` | 478 lines, **11 sections**, headings use `== Title ==` (NOT markdown `#`). Zero mentions of any Perform-task parameter. This is the file to extend. |
| `bundle-source.json` | Manifest entry for `process-modeling` at lines 1071-1086; `libraryVersion` + `sequence` at 6-7. Local checkout reads `1.13.16` / `31` and is **7 commits behind origin/master** — pull first. |
| `CONTRIBUTING.md:63-91` | Publishing rule: PR to master, bump **both** `libraryVersion` and `sequence`, producer contract suite green, merge auto-releases. |

### E. Current state of the three checkouts (verified 2026-08-14 — re-check before starting)

Measured with `git status -sb` / `git log` in each repo. This is working-tree state, so it decays; the point is
what it tells you about the *starting position*, and three of these items change what you do first.

| Repo | Branch | Tree | Position |
|---|---|---|---|
| `C:/Projects/workspace/ProcessBuilder/` | `main` | **dirty** — `packages/CrtProcessBuilder/descriptor.json` modified | in sync with `origin/main` |
| `C:/Projects/clio-knowledge/` | `master` | clean | **7 commits behind** `origin/master` |
| `C:/Projects/clio/` | `master` | clean (only untracked `spec/` folders, incl. this plan) | in sync with `origin/master` |

**E1 — The one uncommitted change is a rebundle stamp, and it is a trap.** The sole diff in ProcessBuilder is
`descriptor.json` `ModifiedOnUtc`: `/Date(1786550573000)/` → `/Date(1786624789000)/` (2026-08-14 12:39 UTC), with
`PackageVersion` untouched. That is the marker `clio set-pkg-version` leaves — a leftover from a rebundle that was
never committed. Per `C:/Projects/clio/docs/agent-instructions/bundled-packages.md`, `ModifiedOnUtc` — not
`PackageVersion` — is what decides whether an environment rewrites its recorded version, so this line is load-bearing.
**Decide before you branch:** either commit it with your rebundle (S2b already bumps the version, which supersedes it),
or `git checkout` it and let `rebundle-process-builder.ps1` restamp. Do not carry it silently into an unrelated commit.

**E2 — ENG-91845 is MERGED in both repos, so the ticket's "blocked" premise is stale.** The ticket says "Connected to"
is *blocked on Task 7* and value sources are *blocked on Task 6*. For connections that is no longer true:

- ProcessBuilder `main` carries `b79837b`, `32f2946`, `960fe88` (`feature/eng-91845-*`, all merged) — `setConnections`
  / `clearConnections` are implemented (`Operations/ConnectionOperations.cs`, `Connections/EntityConnectionBinder.cs:100`).
- clio `master` carries the matching side: `82947ba0c`, `31cec1a0e`, plus rebundles `6eaa9ed9b` / `514d064f2`. The
  shipped archive `clio/CrtProcessBuilder/CrtProcessBuilder.gz` is dated 2026-08-13 and already contains it.
- Crucially, `Connections/ConnectionCapability.cs:118` registers
  `new AllowListEntry(UserTasks.PerformTask, EffectivenessRule.Always)` — the *only* one of the six allow-listed user
  tasks (`:118-123`) with no `CreateActivity` gate, because this element creates its Activity unconditionally. The
  rationale is spelled out at `:20-27`, and `Always` is consumed at `:229`. So "Connected to" on this element is the
  best-supported case in the package, not a blocked one.

This does **not** widen AC3: implementing connection binding stays out of scope. What changes is the *verification*
obligation — the ticket's own closing line ("once Task 7 lands, 'Connected to' + performer covered and re-verified")
is now due. Add one read-only probe to §7 S1: run `setConnections` on a `performTask` element, then `describe-process`,
and record the result as the AC3 boundary evidence. If it works, say so in the status deliverable rather than
repeating "blocked on Task 7".

**E3 — Pull `clio-knowledge` before touching guidance.** The 7 incoming commits (`5ab0df7` … `3cf2a96`) are the
custom-MCP-tool reference; they touch `bundle-source.json`, which is exactly the file §9 tells you to bump. Editing
the stale local copy guarantees a conflict on a version-bumped manifest. This is the same conclusion §9 reaches from
the fixture pin (local `1.13.16`/`31` vs clio's pinned `1.13.19`/`34`) — the git position confirms it independently.

**E4 — No ENG-91846 work exists yet.** `git log --all --grep=91846` is empty in both repos, and there is no branch.
Sibling branch names in ProcessBuilder are `feature/eng-91845-connections-reviewed`,
`feature/ENG-91843-process-parameters`, `feature/ENG-91848-signal-tracked-columns`, so use
**`feature/eng-91846-perform-task-status`**. You are starting clean; nothing to resume.

**E5 — Two in-repo docs already describe this element and both are wrong.** Neither is listed elsewhere in this plan
as a change target; both must be reconciled against §4 as part of AC2:

| File | Current content | Problem |
|---|---|---|
| `C:/Projects/clio/spec/ai-business-process-generation/ai-bp-element-catalog.md:67` | Row lists `caption`, `ActivityCategory` (required), `Who performs`, `StartIn`, `Duration`, `InformationOnStep`, `Recommendation` | Names 7 of 17 non-connection parameters; invents a `Who performs` field that has no such internal name (it is `OwnerId`); marks `ActivityCategory` required, which §4 does not support — **no** parameter in the shipped metadata carries `L6` (`IsRequired`); `ActivityCategory`'s required-ness is a client-side designer rule, not a metadata fact. The only flag `OwnerId` carries beyond the ordinary set is `L14` (`IsPerformer`). |
| `C:/Projects/clio/spec/process-design-service/task-list.md:343` + `:14` | Task 8 row, estimate 1.5 d | Must carry the works/does-not-work verdict this ticket produces. `44d953325` (ENG-92706, Send email) is the precedent for exactly this edit — copy its shape: state what is partially implemented, name the unmet criteria explicitly, and do not silently attribute a descope to the Jira description. |

**E7 — A designer-authored capture is now in this folder, and it outranks inference.**
`captures/performtask-designer-capture.md` records what the Creatio Process Designer *actually writes* for a Perform
task, from a real exported process (`UsrCreateTaskForContactAndUserAccount`, 10.1.480.0) plus the properties-panel
screenshot. **Read it before §4 and §6.** It settled five things this plan previously carried as inference or as
UNVERIFIED:

| It proves | Effect on the plan |
|---|---|
| The designer stores a Lookup constant as a **bare Guid** `ConstValue` (`ActivityCategory`, `ActivityPriority`) | **D4 upgraded from "gated on P2" to a confirmed defect.** Ship the relaxation; P2 now measures only the runtime leg. |
| `OwnerId` carries `IsPerformer: true` (also in the shipped metadata), and the platform resolves the performer through that flag **on the `BP7`-absent path the builder produces** | **D3 confirmed with positive evidence** rather than "the fallback appears to exist". Note the captured element itself does *not* exercise that path — it has `BP7`, so it resolves via `PerformerParameterUId`. |
| An empty `OwnerId` yields the **current user**, never "unassigned" | New guidance obligation (D3). |
| The one designer-authored element we have carries an element-level `PerformerAssignmentOptions` (`BP7`) the builder never writes, and creates `RoleId` **per process** (no `RoleId` exists in `ActivityUserTask`'s shipped metadata) | D3's out-of-scope call is right, and now demonstrably so. It also means a builder-made element is **not byte-identical** to a designer-made one — see R-CAP below. Whether the designer *always* emits `BP7` is **unverified from one sample**; the platform maintains `BP7`-absent branches (`AssignmentOptionsInitializer.cs:208-218`, `ProcessActivity.cs:1060`) that would be dead code if it did, so legacy processes likely lack it. |
| `Recommendation` written as `Source: 1` with **no** `Value`; caption is the title | Consistent with D5. Does **not** settle where a *non-empty* value would live — P3 stands. |

**R-CAP (new risk).** A builder-made Perform task omits `BP7`. The runtime tolerates that (capture §4), but nothing
has verified how the **designer UI** renders such an element when a human opens it — whether "Who performs the task?"
shows User, or blank, or resets on save. Probe P5 already opens the process in the designer; extend it to record the
performer field's state and whether re-saving in the UI injects `BP7`. If it does, decide explicitly whether the
builder should write `BP7` for `AssignmentType.User` as a one-line, in-scope addition — it is three fields on an
existing element property, materially cheaper than the Role work D3 defers, and it would remove the divergence.

**E6 — A referenced doc does not exist.** `task-list.md` points at
`spec/process-design-service/process-design-service-state.md` as the "state doc" from many task sections (e.g. `:46`,
`:285`, `:296`, `:326`). `find C:/Projects/clio -name process-design-service-state.md` returns nothing — the folder
holds only `task-list.md`, `data-source-filters-*.md`, `captures/`, and the sample descriptor. Out of scope to author,
but do not plan to update it, and do not treat its absence as a sign you are looking in the wrong place.

---

## 3. How it works today

### End-to-end flow

```
AI agent
  │  MCP tool call (feature-gated: clio experimental --name process-designer --enable)
  ▼
clio  ModifyBusinessProcessTool / CreateBusinessProcessTool / DescribeProcessTool
  │   args are OPAQUE JSON STRINGS — clio models no descriptor and no operation
  │   ModifyBusinessProcessCommand.cs:104-147 → JsonArray shape check only
  ▼
POST /rest/ProcessDesignService/{BuildProcess|ModifyProcess|DescribeProcess|ListUserTasks}
  │   body {"request": …}; reply {"<Op>Result": {...}}
  │   ServiceUrlBuilder.cs:294-298 (KnownRoute 49-52, plus ProcessBuilderPing = 62 at :296)
  ▼
CrtProcessBuilder  ProcessDesignService  (thin WCF transport; opens one DI scope per call)
  ▼
ProcessDesigner.ModifyProcess → ProcessModifyHandler.ModifyProcess (opens design session, loops ops)
  ▼
ProcessOperationExecutor.Apply  (case-insensitive token → one IProcessOperation strategy)
  ├── AddElementOperation   → IProcessGraphBuilder.PlaceNewElement
  │      └── ProcessElementFactory.Create (lower-cases type, dispatches to the ONE handler that claims it)
  │             └── UserTaskElementHandler.Create              [Elements/UserTaskElementHandler.cs:65-77]
  │                   1. ResolveUserTaskName: "performtask" → "ActivityUserTask"
  │                   2. ProcessUserTaskSchemaManager.FindInstanceByName(name)
  │                   3. new ProcessSchemaUserTask(schema) { UId = Guid.NewGuid() }   ← UId FIRST
  │                   4. userTask.SchemaUId = taskSchema.UId                          ← FIRES THE SYNC
  │                   5. if (HasDedicatedPaletteElement) ManagerItemUId = taskSchema.UId
  │
  └── AddMappingOperation   → IProcessMappingService.AddMapping
         └── ProcessSchemaElementLocator.ResolveElementParameter (by NAME, case-insensitive, NO allow-list)
         └── ProcessMappingService.BuildSourceValue               [Mappings/ProcessMappingService.cs:99-141]
                sourceElement+param / processParameter → Script + [#metapath#]
                expression                             → Script, stored VERBATIM, zero validation
                value                                  → ConstValue, after ValidateConstantValue
  ▼
ProcessSchemaValidator.EnsureValidForSave  (platform GetProcessValidationResult; fail-closed)
  ▼
SaveSchema
```

Read-back:

```
DescribeProcess → ProcessDescriber
     ReadElementParameters(element)                 [Describe/ProcessDescriber.cs:129-140]
       filter:  p.IsResult
             || p.Direction == Out
             || p.SourceValue.Source != None
       → ProcessParameterService.ToDescribeParameter [Parameters/ProcessParameterService.cs:92-113]
       → DescribeProcessParameter { name, caption, description, uid, type,
                                    direction, isResult, referenceSchema, source, value }
```

### The two mechanisms that decide everything

**1. Parameter auto-creation is entirely the platform's.** The handler creates no parameters. The single line
`userTask.SchemaUId = taskSchema.UId` (`Elements/UserTaskElementHandler.cs:72`) invokes the platform setter
(`Terrasoft.Core/Process/ProcessSchemaUserTask.cs:106-115`), which calls `SynchronizeParameters()` →
`FillNewSchemaParameters` (`ProcessSchemaActivity.cs:291-306`). That iterates the referenced user-task schema's own
`Parameters` collection and clones **every** entry onto the element with a fresh UId + a `ProcessSchemaMapping` row.
There is no hardcoded list, no direction filter, no `IsRequired` gate. All 37 land on the element.

> **Ordering trap (load-bearing).** `GetCanSynchronizeParameters()` returns false when `UId.IsEmpty()`
> (`ProcessSchemaActivity.cs:324-326`). The handler sets `UId` in the object initializer *before* assigning
> `SchemaUId`. Reversing those two lines silently produces an element with **zero** parameters.

**2. The write path has no allow-list; the read path has a filter.** `addMapping` resolves the target purely by
name (`ProcessSchemaElementLocator.cs:65-79`), so **all 37 parameters are writable today**. What is not visible is
26 of them, because `ReadElementParameters` requires `IsResult || Direction == Out || Source != None`, and no
`ActivityUserTask` parameter declares a direction (`L12` is absent from every entry in `metadata.json`), so every
one reads back as the default. The filter therefore collapses to `IsResult || has a shipped default` → exactly 11.

**That set of 11 is, byte for byte, the ticket's "works" list.** The ticket measured describe visibility and
reported it as capability.

---

## 4. The Perform task element — the 37 statically declared parameters

`ActivityUserTask`, UId `b5c726f2-af5b-4381-bac6-913074144308`, caption **"Perform task"**, manager
`ProcessUserTaskSchemaManager`, package `CrtProcessDesigner`, palette caption "Task", position 1
(`Data/SysProcessUserTask/data.json`). Not deprecated (`FK2 = 1` = `General`, and absent from
`UserTasks.DeprecatedNames`).

All 37 rows below were extracted **programmatically** from
`C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas/ActivityUserTask/metadata.json`
(`MetaData.Schema.FJ1`) and cross-joined with
`Resources/ActivityUserTask.ProcessUserTask/resource.en-US.xml`. Unless a Notes cell says otherwise, every row is
**CONFIRMED by code/metadata evidence**.

### The live parameter set is 37 + N, and N is environment-dependent

**37 is the STATIC metadata count, not the live count.** `ActivityUserTaskSchemaExtension.SynchronizeDynamicParameters`
calls `ProcessUserTaskUtilities.SynchronizeActivityConnectionParameters(userConnection, target)`
(`.../Schemas/ActivityUserTask/ActivityUserTask.cs:216-219`), which **derives one connection parameter per Activity
lookup column present on that specific environment**. The builder's own constants document exactly this mechanism:
the derived tag is `$"{host.Name}{ConnectionTagSuffix}"` (`ProcessDesignConstants.cs:81-87`), and the Connections
doc-block records that "an environment carries one `SysSchema` row per package that EXTENDS Activity — nine on a
measured stand" (`ProcessDesignConstants.cs:57-72`).

Consequences that must not be papered over:

- The live `ProcessUserTaskSchema.Parameters` set is **`37 + N`**, where `N` is the number of Activity lookup
  columns on the environment that are not among the 19 shipped connections. A custom Activity lookup column
  therefore shows up as an **additional connection parameter** — ENG-91845 territory, out of scope for writes here,
  but in scope for *counting* and for the guidance's "absence is not non-existence" claim.
- **AC1 cannot be closed against a fixed list.** This is precisely why D1 says parameter *data* is read from the
  schema at runtime. The table below is the shipped baseline every environment has, not a closed set.
- **Probe P0 (§7 S1) measures the delta** on the target stand and records it. Until P0 runs, treat any count in this
  document as "the shipped set; an environment may have more".

Type-UId decoder: `95c6e6c4…`=LocalizableString, `8b3f29bb…`=Text, `6b6b74e2…`=Integer,
`90b65bf8…`=Boolean, `b295071f…`=Lookup, `23018567…`=Guid.

**Direction column:** every one of the 37 is `Variable` at the platform level (no `L12` anywhere in the metadata;
default is `Variable` per `ProcessSchemaParameter.cs:416`). Because the *element* read-back reports the enum
default, describe currently prints `In` for all of them. Rather than repeat "Variable / reads back as In" 37 times,
the column records the **role** the element actually plays for that parameter: **IN** (you set it), **OUT** (the
runtime sets it), **CONN** (a "Connected to" link — ENG-91845).

**"Supported by builder today?"** means: can it be written through `addMapping` *right now*, before this ticket's
changes.

| Parameter | Caption (EN) | Type / lookup | Role | Default | Set by designer as | Supported today? | Notes |
|---|---|---|---|---|---|---|---|
| `Recommendation` | Recommendation | LocalizableString | IN | — | ConstValue **stored in the schema RESOURCE**, not in `GS2` | ⚠️ **write accepted, runtime UNVERIFIED** | The task subject / designer label "What should be done?". Feeds `GetActivityTitle()` → `Activity.Title` (truncated 500). Falls back to the **element caption** when empty. The validator lets a plain `value` through (LocalizableString hits the unguarded tail at `ProcessParameterValueValidator.cs:93`), but the designer writes `BaseElements.<el>.Parameters.Recommendation.Value` in the process resource file, and the code generator's LocalizableString `ConstValue` branch reads that resource by `ResourceItemName` and never touches `Value` (`ProcessSchemaGeneratorNew.cs:641-650`) — so the expected outcome is that the write **does not** take effect. **Probe P3a.** |
| `ActivityCategory` | Category | Lookup → `ActivityCategory` | IN | — (runtime falls back to `F51C4643-58E6-DF11-971B-001D60E938C6` "To do") | **plain-Guid ConstValue — CONFIRMED by capture** (`{"Source":1,"Value":"f51c4643-…"}`) | ❌ **blocked → unblocked by D4** | Client-side required. **MUST be ConstValue**: `ActivityUserTaskSchemaExtension.GetResultParameterAllValues` reads `SourceValue.Value` only when `Source == ConstValue` (`ActivityUserTask.cs:194-196`, inside the method at 189-213); a `[#Lookup…#]` macro sets the column and *silently degrades the allowed-results list*. Blocked because `ValidateConstantValue` rejects all Lookup constants (`:62-68`). Listed in `ProcessDesignConstants.Connections.NonConnectionColumnNames`. **This is the strongest single argument for G4.** |
| `OwnerId` | Owner | Lookup → `Contact` | IN | — (empty ⇒ **current user's contact**, not null) | Script macro `[#SysVariable.CurrentUserContact#]` — **CONFIRMED by capture**; `BP7` written alongside it | ⚠️ **`expression` today; bare Guid once D4 lands** | **`IsPerformer = true`** (`L14`, the only parameter carrying it — shipped metadata). The runtime uses that flag on the `BP7`-absent path the builder produces (`ProcessActivity.cs:1049`, `AssignmentOptionsInitializer.cs:209` → `ProcessSchemaParameterCollection.cs:127-137`); the captured designer element does **not** exercise it, having `BP7` (`AssignmentOptionsInitializer.cs:93`). Designer label "Who performs the task?". Written to `Activity.OwnerId` by `UserTaskActivityHandler.SetPerformer` (`:65-77`), **not** by the generic column copy (host column is `Owner`, not `OwnerId` — see `ProcessDesignConstants.cs` Connections block). Type-compatible sources: Lookup→Contact or any Guid-typed parameter; Lookup→`SysAdminUnit` is **rejected** by `ParameterTypeCompatibility` (`:106-117`). |
| `Duration` | Duration | Integer | IN | `20` | ConstValue | ✅ | `Activity.DueDate = StartDate + Duration`. |
| `DurationPeriod` | Duration (period) | Integer | IN | `0` | ConstValue | ✅ | `ProcessDurationPeriod`: **0=Minutes, 1=Hours, 2=Days, 3=Weeks, 4=Months** (`Terrasoft.Core/Process/ProcessEnum.cs:212-219`). |
| `StartIn` | Start executing in | Integer | IN | `0` | ConstValue | ✅ | `Activity.StartDate = now(user tz) + StartIn`. |
| `StartInPeriod` | Start executing in (period) | Integer | IN | `0` | ConstValue | ✅ | Same enum as above. |
| `RemindBefore` | Remind owner in advance | Integer | IN | `0` | ConstValue | ✅ | Non-zero + a performer ⇒ `RemindToOwner = true`, `RemindToOwnerDate = StartDate − offset`. |
| `RemindBeforePeriod` | Remind owner in advance (period) | Integer | IN | `0` | ConstValue | ✅ | Same enum. |
| `ShowInScheduler` | Show in calendar | Boolean | IN | `false` | ConstValue | ✅ | Tagged `EntityColumnValue`; in `NonConnectionColumnNames`. **Has no control on the Perform task properties page** (only `CallUserTaskPropertiesPage` renders it) — settable programmatically only. |
| `ShowExecutionPage` | Show execution page automatically | Boolean | IN | `true` | ConstValue | ✅ | Opens the task page to the current user when the step starts. |
| `InformationOnStep` | Information on step | LocalizableString | IN | — | ConstValue (resource) | ⚠️ same caveat as `Recommendation` — **Probe P3b** | Designer label "Hint for user"; surfaced behind the info button on the activity page. Read by the runtime on **two** paths: `GetParameterValue("InformationOnStep", string.Empty)` in `WriteExecutionData` (`ActivityUserTask.cs:110`) and through the parameter object in `GetExecutionData` (`:119-120`), the latter only when `UseProcessPerformerAssignment` is **OFF** (check at `:115`) — so R1's flag state decides which path a probe exercises. |
| `ActivityPriority` | Priority | Lookup → `ActivityPriority` | IN | `ab96fa02-7fe6-df11-971b-001d60e938c6` (Normal/Medium) | **plain-Guid ConstValue** (that is exactly how the default ships) | ❌ **blocked** (ticket's claim is wrong) | The shipped default *is* `Source=ConstValue` + a bare Guid — a shape `ValidateConstantValue` refuses to write. **Reaches the Activity through the EXPLICIT `UserTaskActivityInfo.PriorityId = ActivityPriority` assignment in `CreateActivity` (`ActivityUserTask.cs:152`), NOT through the `EntityColumnValue` copy loop** — it carries no `L17` tag and its resource file has no `.Group` item, yet it is demonstrably live. Its `GS5` (`ModifiedInSchemaUId`) points at the `UserQuestionUserTask` schema UId, which looks like platform copy-paste provenance — **UNVERIFIED** whether that has any effect (R7). |
| `ActivityResult` | Result of activity | Guid | **OUT** | — | — | n/a (read) | **`IsResult = true`** — the element's result. Set from `Activity.ResultId` on completion. Allowed values = `ActivityResult` joined via `ActivityCategoryResultEntry` for the **ConstValue** `ActivityCategory`. **Visible in describe today.** |
| `CurrentActivityId` | Task Id | Guid | **OUT** | — | — | n/a (read) | The created Activity's Id. Invisible in describe until bound (it has no default and `IsResult` is false) — a genuine annoyance for downstream mapping; see G6. |
| `IsActivityCompleted` | Activity completed | Boolean | **OUT** | `false` | ConstValue | ⚠️ writable but **do not** | The runtime sets it `false` at create and `true` at completion (`UserTaskActivityHandler.cs:288-292`). Writable only because it ships a default. Guidance must say: never set. |
| `ExecutionContext` | Execution context | Text | **OUT/internal** | — | — | n/a | The only parameter with `IsValueSerializable = false`. Technical. |
| `QueueItem` | Queue item | Lookup → `VwQueueItem` | ? | — | — | ⚠️ **UNVERIFIED — do not publish a verdict until R8 is done** | It carries **no** `EntityColumnValue` (`L17`) tag and its resource file has only `Parameters.QueueItem.Caption` (no `.Group`), so `SetColumnValuesFromParameters` cannot copy it. **That alone does NOT make it inert** — `ActivityPriority` has exactly the same untagged, ungrouped shape and is demonstrably live via the explicit `PriorityId` assignment. The real basis for calling it inert is that, unlike `ActivityPriority`, **no explicit consumer has been found**. That is a negative claim from an incomplete search, so it stays UNVERIFIED until R8's grep is done. Do not document as usable, and do not document as "no effect" either. |
| `Lead` | Lead | Lookup → `Lead` | **CONN** | — | Script + `[#Lookup…#]` via `setConnections` | ENG-91845 | Group "Connected to". |
| `Account` | Account | Lookup → `Account` | **CONN** | — | idem | ENG-91845 | Shown by default in the designer's Connected-to area. |
| `Contact` | Contact | Lookup → `Contact` | **CONN** | — | idem | ENG-91845 | Shown by default. **Not** the performer — that is `OwnerId`. |
| `Opportunity` | Opportunity | Lookup → `Opportunity` | **CONN** | — | idem | ENG-91845 | |
| `Invoice` | Invoice | Lookup → `Invoice` | **CONN** | — | idem | ENG-91845 | |
| `Document` | Document | Lookup → `Document` | **CONN** | — | idem | ENG-91845 | |
| `Incident` | Incident | Lookup → `Incident` | **CONN** | — | idem | ENG-91845 | |
| `Case` | Case | Lookup → `Case` | **CONN** | — | idem | ENG-91845 | |
| `Order` | Order | Lookup → `Order` | **CONN** | — | idem | ENG-91845 | |
| `Requests` | Request | Lookup → `Requests` | **CONN** | — | idem | ENG-91845 | |
| `Listing` | Listings | Lookup → `Listing` | **CONN** | — | idem | ENG-91845 | |
| `Property` | Properties | Lookup → `Property` | **CONN** | — | idem | ENG-91845 | |
| `Contract` | Contract | Lookup → `Contract` | **CONN** | — | idem | ENG-91845 | |
| `Project` | Project | Lookup → `Project` | **CONN** | — | idem | ENG-91845 | Also the designer's *client-appended* column (`Connections.ClientAppendedColumnName`). |
| `Problem` | Problem | Lookup → `Problem` | **CONN** | — | idem | ENG-91845 | |
| `Change` | Change | Lookup → `Change` | **CONN** | — | idem | ENG-91845 | |
| `Release` | Release | Lookup → `Release` | **CONN** | — | idem | ENG-91845 | |
| `Application` | Application | Lookup → `Application` | **CONN** | — | idem | ENG-91845 | |
| `FinApplication` | Application | Lookup → `FinApplication` | **CONN** | — | idem | ENG-91845 | Caption collides with `Application` — key on the code, never the caption. |

**Counts (use these, they are extracted, not estimated — but they describe the SHIPPED set; an environment may
carry more, see "the live parameter set is 37 + N" above):** 37 statically declared · 10 with a ConstValue default ·
1 `IsResult` · 1 `IsPerformer` · **0 with `L6` (`IsRequired`)** · 22 tagged `EntityColumnValue`, of which **19** are
group "Connected to" and 3 (`ActivityCategory`, `OwnerId`, `ShowInScheduler`) are group "General" and are **not**
connections · 11 visible in describe today. Two parameters — `QueueItem` and `ActivityPriority` — are untagged AND
ungrouped; that shape says nothing about whether they are live (see their Notes cells).

### What the element produces at run time (for the guidance "what it does" section)

An **Activity** row of type Task (`ActivityConsts.TaskTypeUId`) with: `Title` ← `Recommendation` (or element
caption), `StartDate` ← now + `StartIn`, `DueDate` ← StartDate + `Duration`, `PriorityId` ← `ActivityPriority`,
`OwnerId` ← resolved performer (empty ⇒ current user's contact), `RemindToOwner`/`RemindToOwnerDate` when
`RemindBefore ≠ 0`, `AllowedResult` derived from the element's **outgoing conditional flows**, plus every
`EntityColumnValue`-tagged parameter copied by column name (`UserTaskActivityHandler.cs:233-267`).

> **Two delivery mechanisms, not one.** The column-copy loop reaches only the 22 `EntityColumnValue`-tagged
> parameters. `Title`, `TypeId`, `StartDate`/`DueDate`, the reminder offsets **and `PriorityId`** arrive instead
> through the explicit `UserTaskActivityInfo` object built in `CreateActivity` (`ActivityUserTask.cs:142-158`;
> `PriorityId = ActivityPriority` at :152). So "untagged" does **not** imply "unreachable" — do not let the guidance
> or the QueueItem argument rest on that inference.

The element then
**pauses the process** (`InternalExecute` returns false) until the user completes the activity with a result; on
completion `ActivityResult` ← `Activity.ResultId` and `IsActivityCompleted` ← `true`.

---

## 5. Gap analysis

### G1 — Describe hides 26 of 37 parameters, so the AI cannot see what it can set
**Missing:** an AI that creates a Perform task and calls `describe-business-process` sees 11 parameters and has no
way to learn the other 26 exist.
**Root cause:** `packages/CrtProcessBuilder/Files/src/cs/Describe/ProcessDescriber.cs:129-140` —
`IsResult || Direction == Out || Source != None`. No `ActivityUserTask` parameter declares a direction, so the
middle clause never fires; the filter collapses to "has a default or is the result".
**Impact:** the AI cannot set the performer or the subject because it does not know they exist. This is the
single root cause behind three of the ticket's four "does not work" bullets.
**Scope:** **IN SCOPE**, but resolved by guidance + (stretch) a discovery endpoint, **not** by widening the filter
— see D2.

### G2 — Performer/Owner is invisible, and its only working route is an unvalidated raw macro
**Missing:** a documented, type-safe way to say "this task is performed by X".
**Root cause:** two compounding facts. (a) `OwnerId` has no default ⇒ invisible per G1. (b) It is a Lookup, so a
plain `value` is rejected (`Parameters/ProcessParameterValueValidator.cs:62-68`); the only route left is
`expression`, which is stored verbatim with **zero** validation (`Mappings/ProcessMappingService.cs:122-126`).
**Impact:** the AI must guess both the parameter name and the macro syntax, with no error if it guesses wrong —
the mapping saves and the task silently defaults to the current user at run time
(`ProcessActivity.cs:1059-1064`).
**Scope:** **IN SCOPE** for the legacy `OwnerId` path. Role / Employee's-manager assignment (`BP7`) is **OUT** — D3.

### G3 — The task subject (`Recommendation`) write path is unproven end to end
**Missing:** confirmation that `addMapping { elementParameter: "Recommendation", value: "Call the client" }`
actually produces `Activity.Title = "Call the client"`.
**Root cause:** the designer stores this constant in the **process schema resource**
(`BaseElements.<el>.Parameters.Recommendation.Value`), not in the parameter's `GS2`
(evidence: `Pkg/Custom/Resources/UsrBpWithTask2.Process/resource.en-US.xml`). Whether the server `SaveSchema` path
materializes a resource item from a `ProcessSchemaParameterValue.Value` on a LocalizableString parameter is
**unverified**, and the code evidence points the *wrong* way:

- *For* it working: `ProcessSchemaParameterValue.Value` routes localizable types to `LocalizableValue` rather than
  `MetaDataValue` (`Terrasoft.Core/Process/ProcessSchemaParameterValue.cs:208-230`), so a plain `value` at least
  lands somewhere sane in memory.
- **Against it working (decisive, and stronger):** the code generator's `ConstValue` branch for a
  `TextDataValueType` / `LocalizableStringDataValueType` parameter emits
  `return _f ?? (_f = GetLocalizableString("<resourceManagerName>", "<sourceValue.ResourceItemName>"))`
  (`Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:641-650`). **The compiled property reads the schema
  RESOURCE via `ResourceItemName` — a separate property (`ProcessSchemaParameterValue.cs:243`) that the builder
  never sets — and never touches `Value` at all.** Compare the non-class branch (`:756-763`), which *does* emit
  `sourceValue.Value` as a field initializer. A `Recommendation` written as a plain `value` with no
  `ResourceItemName` therefore most likely compiles to a lookup of a resource item that does not exist.

**Expected outcome of P3 is therefore FAILURE.** Plan for the caption-only branch as the default and treat a P3
pass as the pleasant surprise (D5).
**Impact:** if it silently no-ops, every AI-built Perform task gets its Title from the element caption instead,
and the AI has no signal.
**Scope:** **IN SCOPE** — probe P3 decides whether this is a docs item or a code item.

### G4 — `ValidateConstantValue` blanket-rejects Lookup constants, which makes `ActivityCategory` unsettable *correctly*
**Missing:** the ability to write the encoding the platform itself uses.
**Root cause:** `Parameters/ProcessParameterValueValidator.cs:62-68` rejects *any* plain `value` on a Lookup target
before inspecting it. But:
- the shipped metadata stores `ActivityPriority` as `Source=ConstValue` + a bare Guid;
- `ActivityUserTaskSchemaExtension.GetResultParameterAllValues` reads `ActivityCategory.SourceValue.Value`
  **only when `Source == ConstValue`** (`ActivityUserTask.cs:194-196`) — a macro is the *wrong* encoding here;
- **a Lookup IS a Guid at the type level**: `LookupDataValueType : GuidDataValueType`
  (`Terrasoft.Core/DataValueType.cs:1967`, `IsLookup => true` at `:1985`), so `ValueType == typeof(Guid)`;
- **and the code generator materializes a Lookup `ConstValue` exactly as a Guid literal**: for a non-class
  parameter it emits the field initializer `GeneratorUtilities.GenerateValue(sourceValue.Value, null,
  parameterType, 4)` (`Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:756-763`), which for
  `parameterType == typeof(Guid)` is `new Guid("…")`. This is the citation that governs an ELEMENT `ConstValue`.
  (Do **not** cite `ProcessParameterValueInitializer` here: its only production construction site is
  `ProcessComponentSet.cs:1386`, and `InitializeParameterValues` (`:116-132`) iterates
  `_initialValues.SerializedValues` — the values handed in at PROCESS START. It never reads a schema `ConstValue`.)

So the guard is stricter than the platform, and for `ActivityCategory` there is **no** correct route today:
`value` is refused, and `expression` silently degrades the allowed-results list. The package's own constants file
documents this trap while the validator prevents obeying it.
**Impact:** Task category — a *required* field per Academy — cannot be set correctly by the AI. Priority likewise.
**Scope:** **IN SCOPE.** This is the one genuine functional defect in AC1. Gated on probe P2 — see D4.

### G5 — Value sources beyond constant / process parameter
**Missing:** first-class, validated fields for system variables, system settings, lookup records, date constants.
**Root cause:** `BuildSourceValue` writes only `ConstValue` and `Script`; the platform enum has eight members
(`Terrasoft.Core/Process/ProcessSchemaParameter.cs:16-26`). Everything else is reachable only as raw text through
`expression`.
**Impact:** no validation, no discoverability, no error on a malformed macro.
**Scope:** **OUT — belongs to Task 6.** ENG-91846 documents the `expression` escape hatch and the exact macro
strings; it does not add source fields.

### G6 — Outputs `CurrentActivityId` / `ActivityResult` discoverability is asymmetric
**Missing:** `CurrentActivityId` (the created Activity's Id — Academy explicitly documents it as an outgoing
parameter) is invisible in describe, while `ActivityResult` is visible.
**Root cause:** `ActivityResult` carries `IsResult = true`; `CurrentActivityId` carries nothing, and neither
declares `Direction = Out`.
**Impact:** the AI cannot discover the handle it needs to map the created activity into a downstream element.
**Scope:** **IN SCOPE for guidance** (name it explicitly). Changing metadata is not ours to do; changing the
describe filter is D2's "no".

### G7 — Writable outputs invite meaningless writes
**Missing:** any signal that `IsActivityCompleted` is an output.
**Root cause:** it ships a default (so it is visible), the write path has no direction gate, and
`DescribeProcessParameter` does not carry enough to distinguish it (its `isResult` is false and its `direction`
reads back as the enum default).
**Impact:** an AI plausibly sets `IsActivityCompleted = true` to mean "auto-complete"; the runtime overwrites it at
create time and the write does nothing.
**Scope:** **IN SCOPE for guidance**; optionally mitigated by surfacing `isPerformer`/`isRequired` (D2).

### G8 — No parameter-discovery surface anywhere
**Missing:** any endpoint that answers "what parameters does user task X have?" *before* an element is created.
**Root cause:** `Catalog/UserTaskCatalog.cs:42-63` returns `{name, uid}` only; `ListUserTasksResponse` carries
nothing else (`Contracts/ListUserTasksContracts.cs:35-51`); the service exposes only five operations.
**Impact:** for the **shipped** Perform task, guidance closes this. For **custom** user tasks it remains open.
**Scope:** **STRETCH** (S9). Guidance covers the AC; the generic endpoint is a separate capability.

### G9 — `ClioRing` compatibility gate not yet evaluated
**Missing:** confirmation that no ClioRing-consumed contract changes.
**Root cause:** not inspected in this research pass.
**Scope:** **IN SCOPE as a checklist item** — see S10 / DoD.

### G10 — Auto-created mappings carry `Name = null` — **CLOSED, not a gap**
**Status: RESOLVED before this ticket started.** The question was raised in `.codex/workspace-diary.md:493`
(addendum 9) and **closed in the same file at line 484** (addendum 16): `ProcessSchemaMapping` declares
`[DesignModeProperty]` only for `Source` / `TargetMetaPath` / `TargetUId` / `SourceSchemaUId` /
`SourceParameterUId` (`GT1`–`GT5`); `Name` comes from the base `ProcessSchemaBaseElement`, is not in the mapping's
own meta set, and **no reader exists** — no `mapping.Name`, no `Mappings.FindByName` / `GetByName` /
`ExistsByName` anywhere in `Terrasoft.Core`, `Terrasoft.Core.Process` or PackageStore. `Name = null` on
package-created mappings is a **cosmetic metadata diff** versus a designer-authored schema, not a functional risk.
**Scope:** **OUT.** Do not re-open it, and do not list it as a purpose of any probe.

---

## 6. Design decisions

> These are settled. Do not re-litigate them during implementation. Each is argued from clean-slate cost and
> constraint — "it already exists" is never a reason here.

### D1 — Parameter *data* is discovered dynamically; parameter *semantics* live in guidance. No curated per-element parameter table in code.

**Decision.** Any code that needs to know a user task's parameters reads them from the user-task schema at runtime
(`ProcessUserTaskSchema.Parameters`, already materialized onto the element by the platform sync). No
`ActivityUserTask`-specific parameter list is hardcoded in `CrtProcessBuilder`. The *rules* that cannot be derived
from metadata — that `ActivityCategory` must be `ConstValue`, that the period fields are a 0-4 enum, that
`Recommendation` becomes the Activity title, that `IsActivityCompleted` is an output — live in the guidance
article.

**Alternatives considered.**
- *A curated `IUserTaskParameterCatalog` keyed on the task name*, mirroring `IConnectionCapability`. Carries
  encoding rules in code and can be unit-tested.
- *Fully dynamic, including the rules* — derive everything from metadata.

**Rationale.** The two things have different correctness domains and therefore belong in different places.
Parameter *data* must be right for **any** user task on **any** environment, including custom ones and future
platform versions; a hardcoded table is wrong the moment someone ships a custom task or the platform adds a
parameter, and it duplicates a source of truth that is already queryable at zero cost. Parameter *semantics*
cannot be derived from metadata at all — nothing in `L1..L19` says "ConstValue or the result list degrades" — so a
dynamic source can never carry them; they need prose, and prose costs nothing to ship and is consumed by exactly
the audience that needs it (the model). Putting the rules in code would additionally mean a package redeploy for
every wording fix, against a guidance PR that ships in hours. The `ConnectionCapability` precedent is not a
counter-argument: it exists because a *refusal decision* (`setConnections` is rejected on `false`) must be
enforced server-side. Nothing here is a refusal decision.

### D2 — Do **not** widen `ProcessDescriber.ReadElementParameters`. Close the discovery gap with guidance, and (stretch) with a per-task catalog. Add `isPerformer` to the parameter contract.

**Decision.** `ReadElementParameters` keeps its `IsResult || Out || Source != None` filter unchanged. Discovery is
answered by the guidance article (S7) and, as a stretch, by extending the user-task catalog (S9). Separately, add
`isPerformer` (and `isRequired`) to `DescribeProcessParameter` and to clio's `DescribedParameter` — additive, zero
payload cost on rows already emitted.

**Alternatives considered.**
- *(a) Emit all element parameters unconditionally.* Adds ~26 rows per Perform task to **every** describe
  response, 19 of them "Connected to" lookups that the connections work deliberately excluded for exactly this
  reason (the diary records 3164 of 3208 shipped connection parameters as unbound). A 10-element process would
  gain hundreds of null rows. It also inverts the filter's documented contract — "how each parameter is *bound*" —
  and breaks a pinned test (`tests/CrtProcessBuilder/ProcessDescriberTests.cs:143-178`).
- *(b) An `includeAllParameters` flag on `DescribeProcessRequest`.* Cheap, but it only helps **after** the element
  exists. The AI needs to know the surface **while planning**, which is when it has no element to describe.
- *(c) Emit unbound parameters except connection-tagged ones.* Requires the describer to consult
  `IEntityConnectionCatalog` per parameter on the hot read path, entangles the read path with connections
  ownership (ENG-91845), and still cannot express which of the survivors are outputs.

**Rationale.** The question "what is bound on this element?" and the question "what *can* be set on this kind of
element?" are different questions with different cardinality — the first is per-element and small, the second is
per-schema and constant. Answering the second by inflating the first makes every response pay for a fact that is
identical across all of them. The constant fact belongs where constant facts belong: in the model's context, once,
via guidance — which is also the only channel that can carry the encoding rules (D1) that no field on
`DescribeProcessParameter` could express anyway.

### D3 — Performer is modelled as the `OwnerId` element parameter. `PerformerAssignmentOptions` (`BP7`), Role and Employee's-manager assignment are OUT OF SCOPE.

**Decision.** ENG-91846 supports exactly one performer model: bind the `OwnerId` element parameter, via
`processParameter` (a Lookup→Contact process parameter), via `sourceElement`+`sourceElementParameter`, or via
`expression` with `[#SysVariable.CurrentUserContact#]` / `[#Lookup.{ContactSchemaUId}.{contactId}#]`. Assignment to
a **role** or to an **employee's manager** is deferred to a follow-up ticket.

**Alternatives considered.**
- *Implement `BP7` now.* It is element-level metadata with three fields (`JH1` performer parameter UId, `JH2` role
  parameter UId, `JH3` assignment type) — `Terrasoft.Core/Process/ProcessSchemaPerformerAssignmentOptions.cs:22-24`.

**Rationale.** Three independent costs, each of which alone would break the 1.5-day budget.
(1) **It is not a parameter**, so it cannot ride `addMapping`; it needs a new descriptor field or operation, which
means a contract change across `CrtProcessBuilder` → clio DTOs → MCP tool description → E2E → capability map.
(2) **Role assignment requires creating a parameter that does not exist.** `ActivityUserTask` declares no role
parameter; the designer lazily creates an element parameter named `RoleId` (Lookup→`SysAdminUnit`) client-side
(`Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-activity-schema.js:202-256`),
and `AssignmentOptionsInitializer` throws `NullOrEmptyException` on a Role assignment whose role parameter is empty
(`AssignmentOptionsInitializer.cs:106-113`). No server-side helper for this exists anywhere in the inspected tree —
the builder would have to re-implement client JavaScript in C#.
**Now confirmed by the designer capture** (§2 E7): the exported `RoleId` parameter carries
`CreatedInSchemaUId = 36696f7b-…`, the **process** schema — not `b5c726f2-…` (`ActivityUserTask`). It is created
per-process, so it genuinely does not exist until something creates it.
(3) **It is feature-flag dependent.** The whole surface is gated by `UseProcessPerformerAssignment`, whose state
on the target stands is unknown (R1). Shipping a capability that works on some environments and silently does
nothing on others is worse than not shipping it.

Against that, the `OwnerId` path is **functionally complete for the dominant use case**: when `BP7` is absent the
runtime reads the `IsPerformer` parameter directly and falls back to the current user's contact
(`Terrasoft.Core.Process/PerformerAssignment/AssignmentOptionsInitializer.cs:92-136`;
`ProcessActivity.cs:1059-1064`). "Assign this task to a specific person, or to whoever started the process" is
delivered at the cost of one guidance paragraph.

**Guidance must state the limitation explicitly** so the AI does not claim role assignment works.

**Two behaviours the capture settled that the guidance must also carry.**

- **An unset performer is not an unassigned task.** `GetPerformer()` coerces an empty `OwnerId` to
  `UserConnection.CurrentUser.ContactId` (`Terrasoft.Core/Process/ProcessActivity.cs:481-485`), and the
  `AssignmentType.User` path coerces it identically (`AssignmentOptionsInitializer.cs:97-99`). There is no "nobody"
  state. An agent that omits `OwnerId` has silently assigned the task to whoever started the process — that is a
  choice, and the guidance must present it as one.
- **Only `SetPerformer` ever writes the Activity owner** (`UserTaskActivityHandler.cs:249`, before the column-copy
  loop at `:253`). The copy loop cannot reach it: it matches Activity *column* names against *parameter* names
  (`:85`), and Activity's owner column is named `Owner` while the parameter is `OwnerId` — the mismatch the
  package's own constants file documents (`ProcessDesignConstants.cs:127-129`). So `OwnerId` being
  `EntityColumnValue`-tagged is a red herring; the tag is not what delivers the performer.

### D4 — Relax the Lookup-constant rejection to "a parseable Guid is accepted". **The defect is CONFIRMED by the designer capture; P2 now measures only the runtime leg.**

**Decision.** In `ProcessParameterValueValidator.ValidateConstantValue`, replace the blanket Lookup rejection with:
if the value parses as a `Guid`, accept it and store `Source = ConstValue`; otherwise keep the current error and
its `[#Lookup…#]` instruction. Date/DateTime/Time rejection is untouched.

> **The premise question is now settled by artifact, not inference.** A human-authored process exported from the
> designer (§2 E7, `captures/performtask-designer-capture.md` §5) stores `ActivityCategory` as
> `{"Source": 1, "Value": "f51c4643-58e6-df11-971b-001d60e938c6"}` — `ConstValue` with a **bare Guid**, no
> `[#Lookup…#]` macro — and `ActivityPriority` identically. The validator's own doc-comment
> (`ProcessParameterValueValidator.cs:58-61`) asserts a Lookup constant is "never … a plain `ConstValue`". That
> assertion is false for this element, demonstrated by the designer's own output. **Proceed on the assumption the
> relaxation ships** — the designer persisting this exact shape is strong (not conclusive) evidence that the runtime
> resolves it too, since the platform's own output would otherwise be broken. The only thing that still stops S2 is
> P2's third outcome below.
>
> What the capture does *not* prove is the runtime leg — it shows the shape the platform writes and accepts, not a
> logged run. P2 is therefore no longer a go/no-go on the design; it is the runtime confirmation, and the third
> row of the table below is the only outcome that would still stop S2.

**The P2 outcome table still governs, with the middle row now very unlikely.** Take the branch; do not improvise.

**All three P2 outcomes have a prescribed branch. Take the branch; do not improvise.**

| P2 result | Action |
|---|---|
| Saves + reads back + **resolves at run time** | Ship the relaxation for that parameter. |
| **Refused / does not save** | Keep the guard. Guidance says the parameter is not settable, with the observed error text. **Treat this outcome as suspicious** — the designer demonstrably persists this exact shape, so a refusal points at something in *our* write path rather than at a platform rule. Investigate before documenting it as a limitation. |
| **Saves and reads back but has NO runtime effect** | **BLOCKING for S2 on that parameter — do NOT ship the relaxation for it.** A write that persists a dead value is strictly *worse* than the current hard rejection, which at least tells the caller to use the macro. Keep the rejection, keep the `expression` instruction, and record the parameter in the guidance as not-settable **with the observed evidence**. A save-succeeds/runtime-fails result is a failure, not a partial pass. |

If P2 succeeds for `ActivityCategory` but fails (in either failing sense) for `OwnerId`, narrow the relaxation to
the ConstValue-reading parameters and document `expression` as the route for the rest.

**Alternatives considered.**
- *Keep the guard, document `expression` only.* Cannot work: `ActivityCategory` read by
  `GetResultParameterAllValues` **only** honours `ConstValue`, so `expression` is the *wrong* encoding and
  degrades the allowed-results list. Under this alternative a required field has no correct route at all.
- *Special-case `ActivityCategory` (and `ActivityPriority`) by name.* Arbitrary — the same is true for any lookup
  whose designer editor is an enum combo, and it re-introduces exactly the hardcoded per-element knowledge D1
  rejects. It also does not answer why a bare Guid would be invalid for the general case when the runtime parses
  it as one.
- *Accept a Guid and additionally set `DisplayValue`.* Worth doing as part of the change if P2 shows the designer
  renders a blank field otherwise; treat as a P2 observation, not a separate decision.

**Rationale.** The guard's stated purpose (its own XML doc, `:58-61`) is "the runtime does not resolve one, so a
plain value would persist a default that silently does nothing". That premise is contradicted by the code that
actually governs an element `ConstValue`: a Lookup's CLR type **is** `Guid`
(`LookupDataValueType : GuidDataValueType`, `Terrasoft.Core/DataValueType.cs:1967`), and the schema code generator
emits a non-class `ConstValue` as a field initializer built by
`GeneratorUtilities.GenerateValue(sourceValue.Value, null, parameterType, 4)`
(`Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:756-763`) — i.e. `new Guid("…")` for a Lookup. The platform
additionally ships `ActivityPriority` in exactly that shape, and its own
`ParameterConstValuesValidationRule` (`:73-86`, gated by `GlobalAppSettings.FeatureUseParameterConstValuesValidationRule`)
validates such constants by *resolving* them rather than refusing them. A guard whose premise is contradicted by
the code generator and which blocks the platform's own encoding is a defect, not a policy.
The Guid-parse condition keeps every protection that mattered — a typo, a display name, a half-macro are all still
rejected with the same helpful message.

**Risk accepted and mitigated:** relaxing this makes it *possible* to write a "Connected to" lookup as a bare Guid,
bypassing the `setConnections` validation path. Mitigation is prescriptive guidance, not a code gate — the write
path deliberately has no per-parameter allow-list, and adding one for this would recreate D1's rejected coupling.

### D5 — The subject is the element `caption`; a `Recommendation` mapping is the branch P3 has to EARN.

**Decision.** Guidance instructs: **always give the element a meaningful `caption`** — that is the supported,
code-evidenced way to name the task. `GetActivityTitle()` falls back to the element caption when `Recommendation`
is empty (`ActivityUserTask.cs:88-90` → `ProcessUserTaskUtilities.GetActivityTitle`, whose real body is at
`ProcessUserTaskUtilities.cs:577-590`: `return (titleValue ?? GetSchemaElementCaption(processElement)).Value?.Truncate(500);`
— the caption fallback and the 500-char truncation both live there). A caption that reads as a task instruction
therefore produces a correct Activity title with no further work.

**The default expectation is that the `Recommendation` mapping does NOT work** — see G3: the generator's
`ConstValue` branch for a LocalizableString reads the schema resource via `ResourceItemName`
(`ProcessSchemaGeneratorNew.cs:641-650`), a property the builder never sets. Guidance therefore ships
caption-only unless **probe P3 positively demonstrates** the constant reaching `Activity.Title`; only then is the
`Recommendation` `addMapping` added as the primary instruction with the caption as belt-and-braces. If P3 fails as
expected, guidance states plainly that `Recommendation` cannot be set from the builder yet, and a follow-up ticket
owns LocalizableString resource materialization.

**Alternatives considered.** *Auto-copy `caption` into `Recommendation` server-side in `UserTaskElementHandler`.*
Rejected: it makes the element handler element-aware (the handler today has exactly one per-task branch, the
palette check), it silently overwrites a value the caller may set later in the same operation batch, and it
diverges from what the designer produces for a hand-built process.

### D6 — Guidance is a new section inside `process-modeling.md` in `clio-knowledge`, not a new article.

**Decision.** Add `== Element: Perform task (ActivityUserTask) ==` to
`C:/Projects/clio-knowledge/guidance/mcp/guides/processes/process-modeling.md`, inserted **after**
`== Parameters / mapping / formulas ==` and **before** `== Activity connections ("Connected to") ==` (today
line 310). Bump `libraryVersion` and `sequence` in `bundle-source.json`. No new manifest entry, no new curated
name, no re-pin of `curated-knowledge-names.json` unless clio's pinned generation is also being moved.

**Alternatives considered.** *A separate article `process-element-perform-task`.* Costs: a new `bundle-source.json`
entry with its own `requiredFeatures`, a routing-article update so the AI can find it, a new entry in clio's
pinned `curated-knowledge-names.json` fixture (which today lists **no** per-element process names, so it would be
the first — a precedent for ~30 more articles), and an extra `get-guidance` round trip for content that is
meaningless without the descriptor/mapping conventions the host article establishes three sections earlier.

**Rationale.** One canonical owner per concept. The per-element content is *dependent* content: every worked
example in it is an `addMapping` call whose shape is defined in the preceding section. Splitting dependent content
across articles forces the reader to hold two documents and forces us to maintain a cross-reference. The
article is 478 lines; one more section is well within a single-read budget.

### D7 — The first implementation step is a **live probe matrix**, not code.

**Decision.** S1 is a scripted, recorded set of MCP calls against a real stand that establishes, per parameter
family, whether the write route works end to end. No production code is written before its result is in.

**Rationale.** The ticket's own status line is "Partially working … not covered yet", and this research surfaced
one claim in the ticket that contradicts the code (`ActivityPriority` by constant), one platform behaviour that
contradicts the package's guard (D4), and one write path nobody has ever executed (`Recommendation`, G3). Three of
the four remaining implementation decisions branch on facts that cost under an hour to measure and that no amount
of code reading will settle — `SaveSchema` resource materialization and runtime lookup resolution are simply not
determinable from source. Writing the guidance table first would mean publishing a works/not-works matrix that is
partly inherited from a ticket we have already shown to be wrong on one row.

---

## 7. Implementation steps

Two tracks. **Server** = `C:/Projects/workspace/ProcessBuilder/`. **clio** = `C:/Projects/clio/` +
`C:/Projects/clio-knowledge/`.

**Ordering constraints:**
- **S0b (deploy loop) gates S1.** You cannot run P2a without a way to put a modified validator on the stand.
- **S1 (probe) gates S2, S3 and S7.** Nothing else may start until the probe matrix is recorded.
- S2 and S4 are independent → parallelisable after S1.
- **S2b (rebundle) is blocked by S2 and S4 and gates S8.** Any server change that clio users must receive goes
  through the archive; an E2E run against a freshly installed environment is meaningless without it.
- S7 (guidance) needs S1's results; its *draft* (§9) can be written in parallel and corrected from the matrix.
  **S7 must not merge before S2b**, or the guidance instructs a call the shipped package still refuses — see S2b's
  release-skew note.
- S5/S6 (clio contract) only run if S4 lands.
- S8, S10, S11 are closing steps.

| Step | Track | Blocked by | In the 1.5-day slice (§1 Option A)? |
|---|---|---|---|
| S0 baseline | both | — | yes |
| S0b establish the deploy loop | live stand | S0 | yes |
| S1 probe matrix | live stand | S0b | yes |
| S2 lookup-constant relaxation | server | S1/P2 | yes |
| S2b rebundle + pin update | clio | S2 (and S4 if it lands) | yes |
| S3 subject decision | server/docs | S1/P3 | yes (docs-only either way) |
| S4 `isPerformer`/`isRequired` | server | S0 | **no — defer** |
| S5 clio `DescribedParameter` | clio | S4 | **no — defer** |
| S6 MCP tool description touch-up | clio | S2, S4 | yes (the one S2 sentence only) |
| S7 guidance | clio-knowledge | S1, S2b | yes |
| S8 E2E | clio | S2b, S7 | yes |
| S9 catalog (**STRETCH**) | server+clio | S1 | no |
| S10 gates & docs | both | S2-S8 | yes |
| S11 diary + status | both | S10 | yes |

---

### S0 — Baseline: build, test, and diff the deployed package

**Files:** none changed.

```powershell
# ProcessBuilder — confirm the workspace is green before touching it.
# Build the SOLUTION, not the package csproj (CLAUDE.md:101-110): the solution is the unit that covers the
# package plus any .esproj. On Windows pass MainSolution.slnx WITHOUT a leading .\ — some shells mangle it.
dotnet build   C:/Projects/workspace/ProcessBuilder/MainSolution.slnx -c dev-nf
dotnet test    C:/Projects/workspace/ProcessBuilder/tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf --filter "Category=UnitTests"
```

> **Path trap (an earlier draft of this plan had it wrong and the first command failed with MSB1009).** There is
> **no** `packages/CrtProcessBuilder/CrtProcessBuilder.csproj`. The only two matching projects in the workspace are
> `packages/CrtProcessBuilder/**Files/**CrtProcessBuilder.csproj` and
> `tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj`. Build the solution and the question does not arise.

Then diff the workspace package against the deployed copy, because one researcher flagged them as possibly
divergent (R6):

```powershell
git -C C:/Projects/workspace/ProcessBuilder status -sb
# Spot-check the file most likely to have drifted:
diff (Get-Content C:/Projects/workspace/ProcessBuilder/packages/CrtProcessBuilder/Files/src/cs/ProcessDesignConstants.cs) `
     (Get-Content "C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.WebApp.Loader/Terrasoft.WebApp/Terrasoft.Configuration/Pkg/CrtProcessBuilder/Files/src/cs/ProcessDesignConstants.cs")
```

**Acceptance signal:** unit suite green (previous recorded run: 590/590 on `dev-nf`); the deployed/workspace
divergence is either "none" or written down. **Only `-c dev-nf` builds on this host** —
`C:/Projects/workspace/ProcessBuilder/.application/` contains only `net-framework` and a `placeholder.txt`.

---

### S0b — Establish the deploy loop (**gates S1**)

**Files:** none changed. Record the outcome in the probe-matrix header.

You cannot run probe P2a without putting a modified `ProcessParameterValueValidator` on a stand, and
`C:/Projects/workspace/ProcessBuilder/CLAUDE.md:54-97` makes mode detection a **mandatory Step 0** —
*"Always determine the mode before deploying."* Skipping it is not a shortcut, it is a silent data-loss risk.

**1. Detect the mode.**

```powershell
# clio MCP / CLI, once per environment per session
get-fsm-mode -e <env>          # returns mode: "on" | "off"
```

Write the answer into the probe matrix header. Every subsequent deploy in S1/S2 follows the matching column:

| FSM | Deploy sequence after a C# change | Explicitly do NOT |
|---|---|---|
| **ON** | `dotnet build C:/Projects/workspace/ProcessBuilder/MainSolution.slnx -c dev-nf` → `restart-by-environment-name`. Nothing else. The workspace **is** the filesystem the app loads; the restart is what loads the freshly built DLL. | **Never `push-workspace` or `compile-creatio`** — `compile-creatio` rebuilds from the **stale DB copy** and silently overwrites your good filesystem build (`CLAUDE.md:70-71`). |
| **OFF** | `push-workspace` → `compile-creatio` → `restart-by-environment-name`. | — |

Two adjacent FSM rules worth knowing even though this ticket changes no schema: a schema change made through an
MCP call needs `pkg-to-file-system` (**2fs**) afterwards so it lands in the workspace; a schema edited directly on
the filesystem needs `pkg-to-db` (**2db**) to reach the running runtime (`CLAUDE.md:76-77`).

**2. Know the post-restart auth cliff.** After `restart-by-environment-name`, clio's session to the configuration
service usually expires: schema MCP calls start returning the **HTML login page**, surfacing as the parse error
`'<' is an invalid start of a value`. `get-fsm-mode` keeps working (different endpoint), which makes the failure
look selective and confusing (`CLAUDE.md:91-97`). **Re-establish the clio session immediately after every restart,
before resuming the probe sequence** — otherwise a mid-matrix restart poisons every subsequent row.

**3. The P2a redeploy loop, stated once so S1 can just reference it:**

```
edit ProcessParameterValueValidator.cs
  → dotnet build MainSolution.slnx -c dev-nf
  → deploy per the FSM column above
  → restart-by-environment-name
  → RE-ESTABLISH THE CLIO SESSION
  → re-run the probe
```

**Acceptance signal:** the mode is recorded, one full loop has been executed end to end (even with a no-op edit),
and the re-authentication step is confirmed to work.

---

### S1 — Live probe matrix (**gates everything**)

**Files:** none changed. Record results in the diary (S11) and in §4's "Supported today?" column.

Prerequisites: `clio experimental --name process-designer --enable`; a stand with `CrtProcessBuilder` installed
(`clio list-packages -e <env> | grep CrtProcessBuilder`).

Create one throwaway process with a single `performTask` element, then run each probe as a separate
`modify-business-process` call and a `describe-business-process` read-back.

> **Do not run these in a parallel burst.** Schema-write MCP operations against a .NET Framework stand must be
> sequential; a burst trips IIS rapid-fail and downs the app pool.

| Probe | What to send | Question answered | Decides |
|---|---|---|---|
| **P0** | Dump the **live** parameter set of the created `performTask` element (read `ProcessUserTaskSchema.Parameters` server-side, or enumerate every name that `addMapping` resolves) and diff it against §4's 37 | Is the live set `37 + N`, and what is `N` on this stand? Which extra names appear, and are they all `SynchronizeActivityConnectionParameters`-derived connections? | **AC1 closure** — §4's "live parameter set is 37 + N"; the guidance's "absence is not non-existence" claim |
| **P1** | `addMapping` on `ActivityPriority` with `value: "ab96fa02-7fe6-df11-971b-001d60e938c6"` | Does the ticket's "ActivityPriority lookup settable by constant" claim hold? | Correctness of the published works table |
| **P2a** | `addMapping` on `ActivityCategory` with `value: "F51C4643-58E6-DF11-971B-001D60E938C6"` — **requires the relaxed validator running server-side**, so run S0b's redeploy loop (edit → build → deploy per FSM mode → restart → re-auth → re-probe) with S2's change, or with a scratch build that bypasses the guard | Does a plain-Guid ConstValue on a Lookup save, read back as `source: ConstValue`, and produce the right Activity category + allowed-results list at run time? | **D4 / S2** |
| **P2b** | Same on `OwnerId` with a real Contact Id | Same three questions for the performer | **D4 / S2** narrowing |
| **P2c** | `addMapping` on `OwnerId` with `expression: "[#SysVariable.CurrentUserContact#]"` | Does the documented fallback route actually work? | Guidance wording for G2 |
| **P3a** | `addMapping` on `Recommendation` with `value: "Call the client about the renewal"`, then run the process | Does the constant persist, and is `Activity.Title` correct — or does it fall back to the element caption as G3 predicts? | **D5 / S3** |
| **P3b** | `addMapping` on `InformationOnStep` with a distinctive constant, then run the process | Same question for the **second** LocalizableString parameter — verify the info-button text on the activity page, or the `informationOnStep` field of the element's execution data | **§4's `InformationOnStep` row** (currently ⚠️; DoD AC1 requires no ⚠️ remains) |
| **P4** | `describe-business-process` after each write | Does a written parameter become visible (`Source != None`)? | Confirms G1 is discoverability-only, not a write defect |
| **P5** | Open the built process in the Creatio Process Designer UI. **Also record the "Who performs the task?" field's state, then re-save from the UI and re-export the schema to see whether `PerformerAssignmentOptions` (`BP7`) gets injected.** | Does the designer render the element correctly (category combo populated, owner field filled, no console errors)? And does a builder-made element — which omits `BP7`, unlike every designer-made one (§2 E7) — survive a round trip through the UI? | R3, R5, **R-CAP** |
| **P6** | Build `performTask` (`task1`) → a second element, then `addMapping` on the **downstream** element with `sourceElement: "task1"`, `sourceElementParameter: "CurrentActivityId"`; repeat with `"ActivityResult"` | Can an OUTPUT be used as a mapping **SOURCE**? Does the mapping save, read back with the `[Element:{uid}]` metapath, and resolve at run time? | **§4's OUT rows / G6 / the §9.2 "Map it into a later element" instruction** |
| **P7** | `setConnections` on the `performTask` element binding one host-entity column (e.g. `Contact`) to a process parameter, then `describe-business-process` | Does connection binding on THIS element work now that ENG-91845 has merged (§2 E2)? Does `describe` return it in the re-appliable shape, and is `writesConnectionsAtRuntime` reported `true`? | **AC3 boundary evidence.** Read-only w.r.t. this ticket's code — it verifies someone else's merged work so the status deliverable can stop saying "blocked on Task 7" |
| **PV** | `validate-process-graph` after **each probe group** | Does the platform's own validation accept what we just wrote? | Catches a wrongly-encoded category, an incompatible mapping type, or an unset client-side-required field — exactly the failure classes this ticket is about |

**P3b's read path depends on a feature flag.** `InformationOnStep` is read twice in the runtime:
`GetParameterValue("InformationOnStep", string.Empty)` in `WriteExecutionData` (`ActivityUserTask.cs:110`) and
through the parameter object in `GetExecutionData` (`:119-120`) — the latter **only when
`UseProcessPerformerAssignment` is OFF** (the check is at `:115`). Record the flag's state (R1) next to the P3b
result, or the observation is not interpretable.

**P7 exists because the ticket's premise is stale, not because connections are in scope.** ENG-91845 merged in both
repos (§2 E2), and `Connections/ConnectionCapability.cs:118` gives `PerformTask` the `Always` effectiveness rule — no
`CreateActivity` gate, because this element creates its Activity unconditionally. The ticket's own closing line makes
re-verification due once Task 7 lands. P7 discharges that in one call and produces the evidence AC3 is closed with.
If P7 fails, that is an **ENG-91845 regression** — file it there; do not fix it here.

**P6 is the least-certain path in the matrix and the one the guidance currently asserts without evidence.** The
source parameter is invisible in describe (G6), and resolution runs through `ProcessSchemaElementLocator` by name
plus `ParameterTypeCompatibility` — neither exercised for an unbound, defaultless, non-`IsResult` parameter.
Shipping the "map `CurrentActivityId` into a later element" instruction without P6 is precisely the failure mode
D7 exists to prevent.

For P2/P3/P6 runtime verification use the record-triggered process recipe (signal start → run → read
`SysProcessLog` and the created `Activity` row) rather than trusting the designer preview.

**Acceptance signal:** a filled probe table with, for each row, `saved? / read-back? / runtime effect? /
validate-process-graph verdict` and the raw error text where a call failed. **P0's delta (`N`, and the names) is
recorded explicitly**, because AC1 cannot be closed without it.

---

### S2 — Relax the Lookup-constant rejection (server) — **only if P2 passes**

**Files:**
- `C:/Projects/workspace/ProcessBuilder/packages/CrtProcessBuilder/Files/src/cs/Parameters/ProcessParameterValueValidator.cs`
- `C:/Projects/workspace/ProcessBuilder/tests/CrtProcessBuilder/ProcessMappingServiceTests.cs`
- `C:/Projects/workspace/ProcessBuilder/tests/CrtProcessBuilder/ProcessParameterServiceTests.cs`

**Change.** **KEEP the `if (dataValueType.IsLookup)` branch at `:62-68` and change its BODY** to an early accept:

```csharp
if (dataValueType.IsLookup) {
    if (Guid.TryParse(value, out _)) {
        return;                       // a bare record Guid is the platform's own encoding — accept as ConstValue
    }
    throw new ArgumentException(/* the EXISTING macro-instruction message, unchanged */);
}
```

- value parses as `Guid` → accept, store `Source = ConstValue`.
- value does not parse → keep the current `ArgumentException` **verbatim**, with the
  `[#Lookup.{referenceObjectSchemaUId}.{recordId}#]` instruction, because for a non-Guid the caller genuinely does
  need the macro form.

> **Do NOT "simply let the Lookup branch fall through" to the scalar `Guid` case at `:90-91`.** It would compile
> and it would even accept the right values — but a **non**-Guid then hits `new Guid(value)`, throws
> `FormatException`, and the catch at `:94-98` rewraps it as the GENERIC message
> `"Value 'x' is not valid for parameter 'y' of type Lookup."` — which contains **neither `expression` nor
> `[#Lookup`**. That fails this step's own acceptance signal, the revised test in §8.1, and the two currently
> pinned assertions `.WithMessage("*expression*[#Lookup*")` at `ProcessMappingServiceTests.cs:455` and
> `ProcessParameterServiceTests.cs:674`. The early-return form above is the only shape that satisfies all of them.

- Update the XML doc (`:38-44` and `:58-61`) — its stated premise ("the runtime does not resolve one") is the
  factual error being corrected. Replace it with the citations that actually govern an element `ConstValue`:
  `LookupDataValueType : GuidDataValueType` (`Terrasoft.Core/DataValueType.cs:1967`) and the generator's non-class
  branch `GeneratorUtilities.GenerateValue(sourceValue.Value, null, parameterType, 4)`
  (`ProcessSchemaGeneratorNew.cs:756-763`), plus the `ActivityCategory` ConstValue requirement
  (`ActivityUserTask.cs:194-196`).
- **DisplayValue — note what is already there before writing anything.** `Mappings/ProcessMappingService.cs:135`
  **already** assigns `sourceValue.DisplayValue = descriptor.Value;` in the `value` branch (`:127-137`). For a
  bare-Guid lookup constant that means the designer is handed the **raw Guid string** as the display text — which
  is very likely exactly the "blank/ugly field" symptom P2 would report. So the real work, *if* P2 shows it, is not
  "also set DisplayValue" but: resolve the referenced record's **primary display column** through
  `_userConnection.EntitySchemaManager` (the reference object is already resolvable —
  `ProcessMappingService.ResolveReferenceObjectUId`, `:213-222`) and assign that resolved caption to `DisplayValue`
  instead of the raw value.

**Do NOT** touch the Date/DateTime/Time branch (`:74-80`) — no evidence was gathered that the platform stores plain
date constants, and the guard's own comment notes the runtime path there is internal and feature-flag-governed.

**Acceptance signal:** the two pinned rejection tests are *revised* (not deleted) to assert the new split — a bare
Guid on a Lookup is accepted and stored as `ConstValue`; a non-Guid string on a Lookup is still rejected with the
macro instruction. `-c dev-nf` unit suite green.

---

### S3 — Settle the two LocalizableString parameters (`Recommendation`, `InformationOnStep`) — outcome of P3a/P3b

**Files:** depends on P3.

- **P3a fails** (the EXPECTED outcome per G3/D5 — the constant does not materialize and `Activity.Title` falls back
  to the element caption) → no code in this ticket. Guidance documents **caption-only** as the supported way to
  name the task, states plainly that `Recommendation` cannot be set from the builder yet, and a follow-up ticket is
  filed for LocalizableString resource materialization on the `SaveSchema` path. **Do not attempt resource-item
  writing inside this ticket** — it touches the schema save path, which is shared by every element.
- **P3a passes** (the branch that has to be earned) → no code change either. Guidance then documents
  `Recommendation` as the subject with a worked `addMapping`, plus the "always set a meaningful caption"
  belt-and-braces (D5).
- **P3b** decides the same question independently for `InformationOnStep`. Record the
  `UseProcessPerformerAssignment` state (R1) alongside it, since the flag selects which of the two runtime read
  paths is exercised (`ActivityUserTask.cs:110` vs `:119-120`, gated at `:115`).

**Acceptance signal:** §4's `Recommendation` **and** `InformationOnStep` rows are both marked CONFIRMED one way or
the other — neither may keep its ⚠️, because the DoD requires the column to be ⚠️-free — and the guidance draft in
§9 is amended to match.

---

### S4 — Surface `isPerformer` and `isRequired` on the parameter contract (server)

**Files:**
- `C:/Projects/workspace/ProcessBuilder/packages/CrtProcessBuilder/Files/src/cs/Contracts/DescribeContracts.cs`
  (`DescribeProcessParameter`, ~298-353)
- `C:/Projects/workspace/ProcessBuilder/packages/CrtProcessBuilder/Files/src/cs/Parameters/ProcessParameterService.cs`
  (`ToDescribeParameter`, 92-113)
- `C:/Projects/workspace/ProcessBuilder/tests/CrtProcessBuilder/ProcessParameterServiceDescribeTests.cs`

**Change.** Add two `[DataMember]`s — `isPerformer` and `isRequired` — populated from
`ProcessSchemaParameter.IsPerformer` / `.IsRequired` (both already carried and copied by the platform's
`SynchronizeParameter`, `ProcessSchemaActivity.cs:532-544`). Purely additive; existing callers are unaffected.

**Known limitation to write into the XML doc:** on `ActivityUserTask` **no** parameter declares `L6`, so
`isRequired` is `false` for all 37 — client-side requiredness (e.g. `ActivityCategory`) is not visible at this
layer. And `OwnerId` is only visible once bound (G1), so `isPerformer` confirms rather than discovers. Ship it
anyway: it is the only non-name-based way to identify the performer slot on a custom user task, and it costs two
fields.

**Acceptance signal:** a describe of a Perform task whose `OwnerId` is bound reports `isPerformer: true` on that row.

---

### S2b — Rebundle the shipped `CrtProcessBuilder` archive and re-pin its tests (**clio; gates S8 and the S7 merge**)

**This step is not optional and it is not a formality.** clio **bundles** `CrtProcessBuilder` inside its own
distribution: the committed archive is `C:/Projects/clio/clio/CrtProcessBuilder/CrtProcessBuilder.gz`. Every S2 /
S4 / S9 server change lives in that archive or nowhere.

**Read first, as `clio/AGENTS.md` mandates:** `C:/Projects/clio/docs/agent-instructions/bundled-packages.md`. It
carries the three platform facts whose failure modes are **silent** (a package is matched by `UId` — never change
it; the descriptor's `ModifiedOnUtc`, not `PackageVersion`, decides whether the recorded version is rewritten at
all; and for a source-only package "installed" and "compiled" are different states no database read distinguishes).

**Files:**
- `C:/Projects/clio/clio/CrtProcessBuilder/CrtProcessBuilder.gz` (regenerated, not hand-edited)
- `C:/Projects/clio/clio.tests/Common/BundledProcessBuilderPackageTests.cs` (four pins)

**Command — one call does the whole procedure:**

```powershell
pwsh C:/Projects/clio/rebundle-process-builder.ps1 `
  -PackageRepoPath C:/Projects/workspace/ProcessBuilder `
  -Version 1.1.1.0
```

**`-Version` is required and MUST go UP.** The currently pinned version is `1.1.0.0`
(`BundledProcessBuilderPackageTests.cs:137`), so `1.1.1.0` is the minimum. This is not bookkeeping: clio reads the
shipped version out of the archive and compares it against the version the environment recorded, so **an unchanged
version reaches new installs only and nobody who already has the package is ever asked to update** — the S2
relaxation would exist in the repository and reach no user. There is no version constant to keep in step (see
`spec/adr/adr-bundled-package-version-source-of-truth.md`); raising it costs nothing.

**Then recompute and update all four pins** in `clio.tests/Common/BundledProcessBuilderPackageTests.cs` — the
rebundle script computes them from the archive it just produced:

| Pin | Line |
|---|---|
| `ExpectedArchiveSha256` | :111 |
| `ExpectedArchiveVersion` (`"1.1.0.0"` → `"1.1.1.0"`) | :137 |
| `ExpectedDescriptorModifiedOnUtc` | :163 |
| `ExpectedSchemaDescriptorModifiedOnUtc` | :178 |

> **The trap that invalidates any local verification, and it applies to S1's probes and S8's E2E alike:** an
> install command resolves the bundled archive from the **BUILD OUTPUT** directory, not from the repository. So
> `clio compress -d <repo path>` — and the rebundle — have **no effect on any probe or E2E run until clio itself
> is rebuilt**. Rebuild clio before re-probing, or you will measure the old package and conclude the change did
> nothing.

**Release skew — the reason S7 must not merge first.** A clio-knowledge merge **auto-releases**
(`CONTRIBUTING.md:63-91`), so the moment S7 lands, every clio user is told to set `ActivityCategory` with a
bare-Guid `value`. Environments still on the pre-S2 package will reject exactly that call.

**The mechanism that surfaces this to the user already exists and it is NOT a `[RequiresPackage]` version
literal.** `IBundledPackageConvergence` refuses a gated process-designer call when the environment's recorded
version predates the version in clio's bundled archive, with a message naming **both** versions and the install
hint (`clio/Common/BundledPackageConvergence.cs`; pinned by
`clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs:125-195`). Bumping the archive to `1.1.1.0` is
therefore what arms the detector. **Do not add a version literal to `[RequiresPackage]`:** the four
process-designer gates are deliberately presence-only, the same test file asserts
`requirement.Version.Should().BeNullOrEmpty()` (`:59`, `:93`), and the version ADR explains why a delivery policy
must not be restated in a place that cannot track the archive.

Additionally, in §9.2 state the **minimum CrtProcessBuilder version** next to the `ActivityCategory` instruction
and quote the exact error an older server returns, so an AI facing a stale environment recognises skew instead of
concluding the parameter is unsettable.

**Acceptance signal:** `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` green — the **full**
unit suite, not a module filter, because `clio/Common/` is touched (`AGENTS.md` smart-regression rule 4) and
`BundledProcessBuilderPackageTests` is `Module=Common`. clio rebuilt before any re-probe.

---

### S5 — Mirror the new fields in clio's typed read model

**Files:** `C:/Projects/clio/clio/Command/ProcessModel/IProcessDescriber.cs` (`DescribedParameter`, 460-508).

**Change.** Add `bool? IsPerformer` (`[JsonPropertyName("isPerformer")]`) and `bool? IsRequired`. **Nullable**, with
the same XML-doc convention the existing `IsResult` uses (`:489-495`): "Omitted when the server (an older
`CrtProcessBuilder`) does not report it." An undeclared field is dropped silently — this step is mandatory if S4
lands, or the server work is invisible to the AI.

**Acceptance signal:** a describe round trip carries the flags, and the regression filter actually covers the
changed module. `Module=McpServer` alone does **not**: `clio/Command/ProcessModel/` is not under
`clio/Command/McpServer/`, and the fixture that exercises `DescribedParameter` deserialization is
`clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs`, which carries `[Property("Module",
"ProcessModel")]` (`:19`). `DescribeProcessCommandTests.cs` is `Module=Command` (`:14`). Run:

```powershell
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=McpServer|Module=ProcessModel|Module=Command)"
```

---

### S6 — MCP tool + prompt review (clio)

**Change — keep it to a trigger line, not a duplicate of the guide.** Per `AGENTS.md`, guide content must not be
duplicated in tool descriptions. The MCP maintenance policy also requires an explicit statement per target, even
where nothing changes.

**Tools — exactly ONE real edit.** The other two edits an earlier draft proposed are already shipped; adding them
again would duplicate text and risk tripping `McpGuidanceForcingTests`:

| File | Action |
|---|---|
| `Tools/ProcessDesigner/ModifyBusinessProcessTool.cs` (`[Description]`, ~28-108) | **The one real edit.** If S2 landed, correct the `addMapping` sentence about lookup constants: a bare record Guid is now accepted; a non-Guid still needs `[#Lookup…#]`. |
| `Tools/ProcessDesigner/DescribeProcessTool.cs` (`[Description]`, `:29`) | **No change.** The sentence *"unbound element inputs are omitted — absence does not mean the parameter does not exist"* is **already present verbatim**. Record: "MCP tool descriptions reviewed: the absence-is-not-non-existence sentence is already present." |
| `Tools/ProcessDesigner/ModifyBusinessProcessTool.cs` — guide pointer | **No change.** *"read the 'Modifying an existing process' rules in `get-guidance name=process-modeling`"* is already present (`:107`). Do not add a second pointer. |
| `Tools/ListUserTasksTool.cs` | In scope for the ticket's AI-understanding AC (the ticket names `list-user-tasks` explicitly). Review; edit only if S9 lands. It **is** `[FeatureToggle("process-designer")]`-gated (`:12`), unlike `GetProcessSignatureTool`. |
| `Tools/ProcessDesigner/{CreateBusinessProcessTool, ValidateProcessGraphTool, GetProcessSignatureTool}.cs` | Review, expect no change, state it. Note `GetProcessSignatureTool` carries **no** `[FeatureToggle]` — do not "fix" that. |

**Prompts — four in `Prompts/ProcessDesigner/` plus one alongside, all mandatory review targets:**

| File | Action |
|---|---|
| `Prompts/ProcessDesigner/DescribeProcessPrompt.cs` | **Must move with the describe contract if S4/S5 land** — the prompt's account of what a described parameter carries becomes incomplete the moment `isPerformer`/`isRequired` are emitted. |
| `Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs` | Already instructs *"call `list-user-tasks` … to discover valid `userTaskName` values"* (`:26`). Add the per-element parameter-contract pointer if S9 changes what `list-user-tasks` returns; otherwise state "reviewed, no update required". |
| `Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs` | Align with the S2 lookup-constant change. |
| `Prompts/ProcessDesigner/ValidateProcessGraphPrompt.cs` | Review (probe PV now exercises this tool); expect no change, state it. |
| `Prompts/ListUserTasksPrompt.cs` | Review; edit only if S9 lands. |

**Acceptance signal:** `McpGuidanceForcingTests` still green (it asserts required phrases inside tool
`[Description]` text); `WorkspaceTemplateGuidanceDriftTests` green; a per-target statement exists in the PR for
every file above, including the "no update required" ones.

---

### S7 — Guidance section (clio-knowledge) — **the AC2 deliverable**

**Files:**
- `C:/Projects/clio-knowledge/guidance/mcp/guides/processes/process-modeling.md`
- `C:/Projects/clio-knowledge/bundle-source.json`

**Change.** See §9 for the paste-ready draft and the exact version-bump procedure. **Pull `origin/master` first** —
the local checkout is 7 commits behind and reads `1.13.16` / sequence `31`, while clio's fixture already pins
`1.13.19` / sequence `34`. Compute the bump from `origin/master`, never from the local file.

> **Do not merge S7 before S2b.** A clio-knowledge merge auto-releases, so guidance telling the AI to write a
> bare-Guid `ActivityCategory` reaches every clio user immediately, while the S2 relaxation reaches only
> environments whose `CrtProcessBuilder` has been updated. Bumping the bundled archive first (S2b) is what arms
> the convergence detector that tells a user their package is behind. State the minimum package version in the
> guidance next to the instruction — see S2b.

**Acceptance signal:** the clio-knowledge producer contract suite (`PublishedGenerationTests`) green; a PR open
against `master` with **both** `libraryVersion` and `sequence` bumped.

---

### S8 — E2E coverage (clio) — **mandatory per `AGENTS.md`**

**Files:** `C:/Projects/clio/clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs`

**Change.** Extend the existing Perform-task fixtures (there is already a `performTask` element and a `Duration`
element-parameter mapping at ~803-825) with the parameter families this ticket certifies:
- the four Integer scheduling pairs + the two Booleans, asserted through the typed `DescribeProcessResult`
  (never substring matching the envelope — the fixture's own rationale at ~830-845);
- `OwnerId` via `expression` (P2c's confirmed route);
- `ActivityCategory` via `value` **if and only if** S2 landed;
- `Recommendation` **if and only if** P3a passed;
- **an OUTPUT used as a mapping SOURCE** (P6): `performTask` → a downstream element, with the downstream mapping
  taking `sourceElement: "task1"`, `sourceElementParameter: "CurrentActivityId"`; assert the mapping reads back
  with the `[Element:{uid}]` metapath. This is the assertion that backs §9.2's "Map it into a later element"
  instruction — do not ship the instruction without it;
- **`validate-process-graph` on a fully-configured Perform task** — one positive asserting it validates clean, and
  one negative asserting the validator's message for a **type-incompatible mapping** (the Lookup→`SysAdminUnit`
  onto Lookup→`Contact` case §8.1 also pins as a unit test). The save path runs
  `ProcessSchemaValidator.EnsureValidForSave` fail-closed (`Design/ProcessModifyHandler.cs:88`,
  `Design/ProcessBuildHandler.cs:81`), so platform validation is exactly what catches a wrongly-encoded category
  or an unset client-side-required field;
- a negative: a non-Guid `value` on a Lookup target still fails with the macro instruction.

Use `[Category(ProcessDesignerE2EGate.CategoryName)]` and the `ArrangeAsync` gate.

> **Rebuild clio first.** An install command resolves the bundled archive from the **build output** directory, so
> an E2E run against a freshly installed environment measures the OLD package until clio is rebuilt after S2b.

**Acceptance signal:** the new tests pass against a real stand and skip cleanly when the `process-designer` feature
is off.

---

### S9 — **STRETCH** — per-user-task parameter catalog (server + clio)

Only if S1-S8 land inside budget. Closes G8 for *custom* user tasks, which guidance cannot.

**Design constraint discovered during verification:** `ProcessDesignService.ListUserTasks()` takes **no request
parameter** (`EntryPoints/WebService/ProcessDesignService.cs:67-74`) and clio posts a literal `"{}"`
(`clio/Command/ListUserTasksCommand.cs:59`). Two options:
- **(i)** Add an optional `ListUserTasksRequest { userTaskName, includeParameters }`. With
  `BodyStyle = Wrapped`, an old client posting `{}` should deserialize to a `null` request → treat as "current
  behaviour". **Backward compatibility here is UNVERIFIED — probe it before choosing this option.**
- **(ii)** Add a new `DescribeUserTask` operation + `KnownRoute = 63` (`ProcessBuilderPing = 62` is the current
  maximum, `clio/Common/ServiceUrlBuilder.cs:232`) + a new `describe-user-task` MCP tool.

(i) is cheaper (no new route, no new MCP tool, no new capability-map entry) but rests on an unverified WCF
behaviour. (ii) is safe but pulls in the full MCP maintenance checklist. **Do not start S9 without first probing
(i).**

**Neither option is free of the maintenance policy — enumerate it rather than gesturing at it.** Option (i)
modifies `ListUserTasksOptions`, which **is a command options class**, and `clio/AGENTS.md` lists "command options
classes (for example classes with `[Verb]`, `[Option]`, `[Value]` attributes)" as a trigger for the mandatory doc
**and** MCP review path. So the checklist below applies to (i) as well as (ii):

- `[FeatureToggle("process-designer")]` on **any** new `[McpServerToolType]` — every ProcessDesigner tool carries
  it, and so does `ListUserTasksTool.cs:12`. (`GetProcessSignatureTool` is the deliberate exception; do not copy it.)
- Registration through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`clio/Command/McpServer/`). **Never**
  `WithToolsFromAssembly` / `WithResourcesFromAssembly` / `WithPromptsFromAssembly`, and never a `Type[]` overload
  of `WithTools` / `WithResources` / `WithPrompts` — that binds the SDK's generic overload and registers nothing.
- A `McpToolCompatibilityCatalog` entry (`clio/Command/McpServer/McpToolCompatibilityCatalog.cs`) if any tool name
  changes or is removed — never leave an old name dangling.
- **Two** E2E files, matching the repo's existing split: a behavioural `*ToolE2ETests.cs` and a
  `*ContractToolE2ETests.cs` (the pattern already exists — `GetProcessSignatureContractToolE2ETests.cs`,
  `InstallProcessBuilderContractToolE2ETests.cs`, `GenerateProcessModelContractToolE2ETests.cs`).
- Unit coverage in `clio.tests/Command/McpServer/ListUserTasksToolTests.cs` (and `UserTaskToolTests.cs` if the
  shared surface moves).
- The doc targets the options-class change triggers, including `docs/McpCapabilityMap.md` **§4 "User Task
  Engineering"** (line 447) — see S10.

Whichever is chosen, the payload per parameter is: `name, caption, type, referenceSchema, isPerformer, isResult,
isRequired, group, tag, defaultSource, defaultValue` — read dynamically from `ProcessUserTaskSchema.Parameters`
(D1), never from a hardcoded table.

Composition-root tripwires that must be updated for any new class:
`tests/CrtProcessBuilder/CrtProcessBuilderAppTests.cs:87-162`.

---

### S10 — Gates, docs and compatibility

**Files:**
- `C:/Projects/clio/docs/McpCapabilityMap.md` **§11 "Business Process Modeling"** (line 676, ~676-706) — update if
  any tool contract text or DTO changed.
- `C:/Projects/clio/docs/McpCapabilityMap.md` **§4 "User Task Engineering"** (line 447) — the section covering
  `list-user-tasks`, the tool S9 changes and the tool the ticket names as part of the AI-understanding surface.
  Mandatory doc target **if S9 lands**; review and state the result either way.
- `C:/Projects/clio/clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` — re-pin **only** if the
  clio-knowledge generation clio consumes is being moved.
- `C:/Projects/workspace/ProcessBuilder/docs/process-builder-architecture.md` — record the D4 validator change.

**MCP resources review (policy requires a per-target statement).** `clio/Command/McpServer/Resources/` contains
`BaseResource.cs`, `ClearRedisResources.cs`, `GetHelpResources.cs`, `KnowledgeGuidanceResourceAdapter.cs`,
`MultiSourceKnowledgeResource.cs`, `RestartResource.cs`. None is process-designer-specific; the expected outcome
is *"MCP resources reviewed, no update required"* — but state it explicitly rather than omitting the target.

**ClioRing compatibility gate (G9).** Per `AGENTS.md` this is mandatory, not optional. Determine the live consumer
surface by searching for tool calls and nested command names:

```powershell
Select-String -Path C:/Projects/clio/clio-ring/ClioRing.Ipc/*,C:/Projects/clio/clio-ring/ClioRing/*,C:/Projects/clio/clio-ring/ClioRing.Desktop/actions.json `
  -Pattern "describe-business-process|modify-business-process|create-business-process|list-user-tasks|validate-process-graph" -Recurse
```

If nothing matches, state in the PR: *"ClioRing compatibility reviewed, no Ring-consumed contract changed"* and
cite the inspected paths. If something matches, run the full gate from `AGENTS.md` including the Windows x64
NativeAOT publish.

**No CLI docs are required** for the in-scope MCP tools — `CreateBusinessProcessTool`, `DescribeProcessTool`,
`ModifyBusinessProcessTool`, `ValidateProcessGraphTool`, `GetProcessSignatureTool` (in
`Tools/ProcessDesigner/`) and `ListUserTasksTool` (in `Tools/`). Their options types carry no `[Verb]`, so there is
no `clio/help/en/*.txt`, no `clio/docs/commands/*.md` and no `WikiAnchors.txt` entry, and the "required
documentation targets" policy does not apply. **State *"docs reviewed, no update required"* explicitly** — the
policy requires the statement, not merely the absence of an edit.

> **The tool set is not uniform — do not reason about "the five process-designer tools" as one thing.**
> `GetProcessSignatureTool` carries **no** `[FeatureToggle]` while the other four in that folder do, and
> `GetProcessSignatureCommand.cs:17` deliberately carries **no** `[RequiresPackage]` (it reads the built-in
> DataService, not `ProcessDesignService`; both absences are pinned by
> `ProcessDesignerRequiresPackageAttributeTests`). `ListUserTasksTool` lives outside the folder but **is** gated.
> Anyone checking feature-gate coverage or the ClioRing grep surface must use the individual list above.

---

### S11 — Diary and sprint status

- Append one entry to `C:/Projects/workspace/ProcessBuilder/.codex/workspace-diary.md` in the mandated format
  (Context / Decision / Discovery / Files / Impact). **Record the S1 probe matrix verbatim** — it is the most
  reusable artefact this ticket produces.
- Append the clio-side counterpart to `C:/Projects/clio/.codex/workspace-diary.md`.
- Update `C:/Projects/clio/spec/process-design-service/task-list.md:343` (task 8) and, if the story is tracked
  there, `spec/sprint-status.yaml`.

---

## 8. Test plan

### 8.1 Server unit tests — `C:/Projects/workspace/ProcessBuilder/tests/CrtProcessBuilder/`

Framework: **NUnit 4.4.0**, **FluentAssertions pinned `[7.2.0]`**, **NSubstitute 5.3.0**. No Moq, no AutoFixture.

Style rules for this repo (they differ from clio — do not copy clio's):
- `[TestFixture(Category = "UnitTests")]` at **class** level. **Not** `[Category("Unit")]`, and no `Module`
  property. (Note: `.claude/agents/coder.md` in that repo says `[Category("Unit")]` — that instruction is a
  verbatim copy of clio's agent file and is **wrong for this project**. Follow the 42 existing fixtures.)
- AAA blocks explicit; `[Description("…")]` on every test; a `because` on **every** assertion.
- Name as `MethodName_ShouldX_WhenY`.

| Test file | Fixture base | What to assert |
|---|---|---|
| `ProcessMappingServiceTests.cs` (revise 401-456) | `BaseComposableAppTestFixture` | **S2**: a bare Guid `value` on a Lookup target is accepted and stored as `Source = ConstValue` with that exact value; a non-Guid `value` on a Lookup target is still rejected **and the message still contains `expression` and `[#Lookup`** — the existing assertion at `:455` is `.WithMessage("*expression*[#Lookup*")`. Only the early-return form prescribed in S2 satisfies this; a fall-through to the scalar `Guid` branch produces the generic catch message at `:94-98` and fails here. Keep the DateTime rejection assertions untouched. |
| `ProcessMappingServiceTests.cs` (new) | idem | `expression: "[#SysVariable.CurrentUserContact#]"` on a Lookup→Contact target stores `Source = Script` with the text verbatim (documents P2c's route). |
| `ProcessMappingServiceTests.cs` (new) | idem | A Lookup→`SysAdminUnit` source mapped onto a Lookup→`Contact` target is rejected — pins the reason role assignment cannot be faked through `OwnerId` (D3). |
| `ProcessParameterServiceDescribeTests.cs` (new) | idem | **S4**: `ToDescribeParameter` emits `isPerformer` / `isRequired` from the source parameter, and both are `false` (not null) for a plain parameter. **Construction is a plain property assignment** — see the recipe note below; no partial substitute is needed. |
| `ProcessParameterServiceTests.cs` (revise) | idem | The lookup-default constant case for **process** parameters follows the same new rule as mappings (the two share `ValidateConstantValue`). |
| `UserTaskElementHandlerCreateTests.cs` (new, optional) | standalone | Regression pin for the **UId-ordering trap**: an element created by the handler has a non-empty `UId` *before* `SchemaUId` is assigned. Cheap insurance against a future refactor silently zeroing the parameter set. |

#### Mocking recipe for the hard platform types

**The one that will bite you: auto-synced parameters are NOT observable in a unit test.**
`ProcessSchemaUserTask.SchemaUId`'s setter (`Terrasoft.Core/Process/ProcessSchemaUserTask.cs:106-115`) calls
`SynchronizeParameters()`, whose **`base.SynchronizeParameters()`** does the actual cloning
(`ProcessSchemaActivity.cs:589-600` → `FillNewSchemaParameters`, `:291-306`). That reads `GetSchemaParameters()`,
which on `ProcessSchemaUserTask` is `Schema == null ? new ProcessSchemaParameterCollection() : Schema.Parameters`
(`ProcessSchemaUserTask.cs:243-246`) — **so on a substituted schema with no parameter collection, nothing is
cloned and the loop iterates an empty set.** (The `InstanceFactory.GetSchemaExtension()` branch at
`ProcessSchemaUserTask.cs:283-285` is *not* the cause: it runs only when `FeatureProcessParameterCollections` is on
**and** `manager.UseSchemaInstanceFromMetaData`, and it handles dynamic parameters only.) Therefore:

- **Hand-build element parameters** using the established `AddUserTask` / `AddElementParameter` helpers
  (`ProcessMappingServiceTests.cs:38-72`). Set `ContainerUId = task.UId` so `GetMetaPath()` emits the
  `[Element:{uid}]` segment.
- **Prove the real sync only on a stand / in E2E.** Do not chase it in a unit test.
- (Unexplored option, do not spend budget on it speculatively: `Substitute.ForPartsOf<ProcessUserTaskSchema>` with
  a stubbed `SynchronizeParameters(schemaElement)` that copies a parameter list — that virtual *is* stubbable, but
  no fixture in the repo does it.)

Other recipes, all with an existing in-repo example:

```csharp
// User connection for a standalone fixture
UserConnection connection = ProcessDesignTestSupport.CreateUserConnection();   // ProcessDesignTestSupport.cs:68-79

// An in-memory schema that can hold parameter VALUES (the base NREs without this seam)
var schema = new TestProcessSchema(...);                                       // ProcessDesignTestSupport.cs:19-53

// Substitute the user-task schema manager (also wires Workspace.SchemaManagerProvider)
ProcessUserTaskSchemaManager manager = connection.SetupProcessUserTaskSchemaManager();
                                                    // Terrasoft.TestFramework/SubstituteUtilities.cs:588-600

// A user-task schema: substitute for UId control, or REAL when only Name/UsageType matter
var taskSchema = Substitute.For<ProcessUserTaskSchema>(manager);
taskSchema.UId.Returns(schemaUId);                  // UserTaskElementHandlerCreateTests.cs:32-39
var real = new ProcessUserTaskSchema(manager) { Name = "ActivityUserTask", UsageType = ... };
                                                    // Elements/UserTaskDeprecationPolicyTests.cs:55-58

// A typed element parameter without routing through the schema's null AppManagerProvider
var p = new ProcessSchemaParameter(Substitute.For<DataValueType>(dataValueTypeManager));
                                                    // Connections/ConnectionCapabilityTests.cs:70-82

// S4's flags need NO special ceremony: IsPerformer and IsRequired are plain auto-properties with public
// setters on ProcessSchemaParameter (:425-429 and :524-528). Assign them directly — no metadata write, no
// Substitute.ForPartsOf. This matters for IsRequired in particular: NO ActivityUserTask parameter declares
// L6, so the fixture must SYNTHESIZE a required parameter to test the true case at all.
p.IsPerformer = true;
p.IsRequired  = true;

// DB read: pair a SQL-text stub with a reader stub
UserConnection.DBEngine.GetQuerySqlText(ArgExt.Select("SysProcessUserTask")).Returns("Select SysProcessUserTask");
UserConnection.DBExecutor.ExecuteReader(ArgExt.Contains("Select SysProcessUserTask")).Returns(table.CreateDataReader());
// TRAP: a PARAMETERIZED select (one with a Where) hits ExecuteReader(string, QueryParameterCollection).
// Stubbing the single-arg overload silently returns no rows.   UserTaskElementHandlerCreateTests.cs:41-55

// Feed a hand-built schema into the describer (note the `out` parameter)
repository.LoadForDescribe(default, default, out _).ReturnsForAnyArgs(ci => { ci[2] = Guid.Empty; return schema; });
                                                    // ProcessDescriberTests.cs:45-57
```

**Command:**
```powershell
dotnet test C:/Projects/workspace/ProcessBuilder/tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf --filter "Category=UnitTests"
```
`-c dev-n8` will **fail on this host** (`.application/net-core` is absent). Confirm which configuration CI runs
before claiming cross-framework coverage (R9).

### 8.2 clio unit tests

| Test file | Module trait | What |
|---|---|---|
| `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` | `McpServer` | Safety flags + arg mapping unchanged after S6. |
| `clio.tests/Command/McpServer/ModifyBusinessProcessToolTests.cs` | `McpServer` | Description text change does not break the `Fake<X>Command` + `IToolCommandResolver` pattern. |
| `clio.tests/Command/McpServer/ListUserTasksToolTests.cs` | `McpServer` | In scope if S9 lands (and its options class changes even under option (i)). |
| `clio.tests/Command/McpServer/UserTaskToolTests.cs` | `McpServer` | Same — the adjacent user-task surface. |
| `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` | **`ProcessModel`** | **S5**: `DescribedParameter` deserialization of the new nullable flags. **Not covered by a `Module=McpServer` filter.** |
| `clio.tests/Command/DescribeProcessCommandTests.cs` | **`Command`** | The command-level round trip. Likewise outside `Module=McpServer`. |
| `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | **`Common`** | **S2b**: the four archive pins. `[Category("Unit")] [Property("Module", "Common")]` at `:38-39` — a `Module=McpServer` filter **never runs it**, which is exactly how a rebundle ships red. |
| `clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs` | `Command` | Guards the presence-only `[RequiresPackage]` shape and the convergence refusal. Must stay green — do not "fix" it by adding a version literal. |
| `WorkspaceTemplateGuidanceDriftTests` / `McpGuidanceForcingTests` | `McpServer` | Must stay green after any `[Description]` edit. |

**Two commands, not one.** The scoped run during development:

```powershell
dotnet test C:/Projects/clio/clio.tests/clio.tests.csproj `
  --filter "Category=Unit&(Module=McpServer|Module=ProcessModel|Module=Command|Module=Common)"
```

And, **mandatorily once S2b touches the bundled archive / catalog under `clio/Common/`** — `AGENTS.md`
smart-regression rule 4 makes any `clio/Common/` change a full-suite trigger:

```powershell
dotnet test C:/Projects/clio/clio.tests/clio.tests.csproj --filter "Category=Unit"
```

### 8.3 E2E

```powershell
clio experimental --name process-designer --enable
dotnet test C:/Projects/clio/clio.mcp.e2e/clio.mcp.e2e.csproj --filter "TestCategory=McpE2E.ProcessDesigner"
```

### 8.4 Live / manual verification checklist (a real stand)

Run **sequentially**. Record every response.

> **Two preconditions from S0b, both of which break this checklist mid-run if ignored.**
> (1) `get-fsm-mode` first, and use the matching deploy sequence — in FSM, `compile-creatio` rebuilds from the
> stale DB copy and silently overwrites a good filesystem build (`ProcessBuilder/CLAUDE.md:70-71`).
> (2) **clio auth dies after `restart-by-environment-name`**: schema MCP calls begin returning the HTML login page,
> surfacing as `'<' is an invalid start of a value`, while `get-fsm-mode` keeps working and masks the cause
> (`CLAUDE.md:91-97`). Any restart inside this deliberately sequential run therefore poisons every following step
> unless the clio session is re-established immediately. Plan restarts, and re-authenticate right after each one.

1. `list-environments` → pick the target; `get-info -e <env>` → connectivity.
2. `list-packages -e <env>` → confirm `CrtProcessBuilder` is installed and note its version. **After S2b, this
   must read the rebundled version** (`1.1.1.0`); if it still reads `1.1.0.0`, clio was not rebuilt before the
   install — the archive is resolved from the build output, not the repo.
3. `list-user-tasks` → confirm `ActivityUserTask` is present. (Note it also lists retired schemas with no marker.)
4. `create-business-process` with a descriptor: `startEvent` → `performTask` (name `task1`, **caption
   "Call the client about the renewal"**) → `endEvent`, plus a process parameter `PerformerContact`
   (`typeFromElement: "task1"`, `typeFromElementParameter: "OwnerId"` — the supported way to get a guaranteed
   mapping-compatible Lookup→Contact parameter, `Parameters/ProcessParameterService.cs:54-67`).
5. `describe-business-process` → **expect exactly 11 parameters** on `task1`: the 10 defaulted ones plus
   `ActivityResult`. `buildType` reads back as **`usertask`**, *not* `performtask` (`ResolveBuildType` returns
   `SupportedTypes.FirstOrDefault()`); identify the element by `buildType` **+** `userTaskName`.
6. `modify-business-process`, one call per group, checking `describe` after each:
   - scheduling: `Duration = 2`, `DurationPeriod = 2` (Days), `StartIn = 1`, `StartInPeriod = 1` (Hours),
     `RemindBefore = 30`, `RemindBeforePeriod = 0` (Minutes);
   - flags: `ShowExecutionPage = true`, `ShowInScheduler = true`;
   - subject / hint: `Recommendation` (P3a) and `InformationOnStep` (P3b) — record the
     `UseProcessPerformerAssignment` state (R1) alongside the second;
   - performer: `OwnerId` ← `processParameter: "PerformerContact"`, then re-test with
     `expression: "[#SysVariable.CurrentUserContact#]"`;
   - category / priority: `ActivityCategory`, `ActivityPriority` by `value` (P1/P2).
6b. **`validate-process-graph` after each group in step 6.** Record the verdict in the probe matrix. The save path
   already runs `ProcessSchemaValidator.EnsureValidForSave` fail-closed
   (`Design/ProcessModifyHandler.cs:88`), so a clean save is not the same signal as a clean validation of the
   assembled graph — platform validation is what catches a category written in the wrong encoding, an incompatible
   mapping type, or an unset client-side-required field, which are the failure classes this ticket is about.
6c. **Map an OUTPUT into a downstream element (P6):** add a second element, then `addMapping` on it with
   `sourceElement: "task1"`, `sourceElementParameter: "CurrentActivityId"` (and separately `"ActivityResult"`).
   Confirm the mapping saves, reads back with the `[Element:{uid}]` metapath, and resolves when the process runs.
7. `describe-business-process` → every written parameter must now appear with `source` and `value`
   (this is what proves G1 is discoverability-only). **Also reconcile the live parameter set against §4's 37 (P0)**
   and record the delta `N`.
8. **Open the process in the Creatio Process Designer UI.** Confirm: the element renders as the dedicated
   "Perform task" (not the generic "User task" container), the Category combo shows a real category, "Who performs
   the task?" is populated, Start-in / Duration / Remind read correctly in the embedded activity module, and no
   client console errors.
9. **Run it.** Trigger the process, then verify the created `Activity` row: `Title`, `OwnerId`, `TypeId` (Task),
   `StartDate`, `DueDate`, `PriorityId`, `RemindToOwner`. Complete the activity with a result and confirm the
   process advances and `ActivityResult` / `IsActivityCompleted` are populated.
10. Negative: `addMapping` to a misspelled parameter → expect
    `Element 'task1' has no parameter 'Ownerid2'.` (`ProcessSchemaElementLocator.cs:65-79`).

**Two documented ways this can look broken but is not** (Academy FAQ): a misconfigured WebSocket makes visual steps
never open although the log shows the process running; and a background step posts a notification and runs nothing
until the user opens it from the *Business process tasks* tab.

---

## 9. Guidance / documentation changes

### 9.1 Where it goes, exactly

| Item | Value |
|---|---|
| Repo | `C:/Projects/clio-knowledge` (branch `master`; **local checkout is 7 commits behind — pull first**) |
| File | `guidance/mcp/guides/processes/process-modeling.md` |
| Insert point | New section after `== Parameters / mapping / formulas ==` and **before** `== Activity connections ("Connected to") ==` (line 310 in the current local revision) |
| Heading style | `== Title ==` — **the file does not use markdown `#` headings.** Bullets are `-` at top level, `*` nested. |
| Manifest | `bundle-source.json` — the `process-modeling` entry is at lines 1071-1086; **do not change it** (no new item) |
| Version bump | **Both** `libraryVersion` and `sequence` at lines 6-7, computed from `origin/master` after pulling |
| Publish | PR to `master`; producer contract suite (`PublishedGenerationTests`) must be green; merge auto-releases (`CONTRIBUTING.md:63-91`) |

**Version bump trap.** Reusing a `sequence` with different content makes clio **reject the whole library** —
not just the changed article. The local file reads `1.13.16` / `31`; clio's pinned fixture already reads
`1.13.19` / `34`. Compute the bump from `origin/master`.

**Drift tests, and what they do *not* cover.**
`clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` validates the three shipped
`clio/tpl/**/AGENTS.md` templates and `McpServerInstructions.Text` against a resident-or-bridged oracle, and checks
every `get-guidance name=X` they reference against the pinned
`clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`. **Guidance article bodies are explicitly out
of scope of every test in both repos.** So there is no automated check on the content below — the live probe
matrix (S1) is the only thing standing between this section and a confidently wrong table. Treat §9.2 as
**provisional until S1 fills it in**, and correct every ⚠️ before opening the guidance PR.

Also note `process-modeling` sits in the fixture's `featureGatedNames`, which means a shipped `AGENTS.md` template
may **not** name it — do not "helpfully" add a reference to it in `clio/tpl/**`.

**Stale-instruction warning.** `C:/Projects/clio/.ai/skills/clio-guidance-development/SKILL.md:19-27, 154-168`
still tells you to update `GuidanceCatalog` and `RoutingGuidanceResource`. **Neither type exists in clio any more.**
Ignore those steps. (Fixing that skill text is a separate cleanup — R10.)

### 9.2 Draft section — paste-ready

> Amend the ⚠️ lines from the S1 probe matrix before publishing.

```text
== Element: Perform task (userTask / performTask -> ActivityUserTask) ==
- WHAT IT IS: the "Perform task" element. Type alias `performTask` (equivalently `userTask` with
  `userTaskName: "ActivityUserTask"`). It creates an Activity of type Task, assigns it to a person, and then
  PAUSES the process until that person completes the activity with a result. It is the way to put a human step
  inside an automated flow.
- USE IT FOR: call a client, review a document, follow up, prepare paperwork, a manual check — any step where the
  process must wait for a person to act outside the process.
- DO NOT USE IT FOR approvals. Creatio has a dedicated Approval element that creates an Approval record (not an
  Activity), emails approver and author, supports delegation, and branches on the verdict. Perform task has no
  approved/rejected semantics. Approval is not buildable from clio yet — say so rather than emulating it with a
  task.
- WHAT IT PRODUCES: one Activity row — Title, Owner, Category, Priority, Start date (= now + StartIn),
  Due date (= start + Duration), reminder, and any "Connected to" links. It appears in the performer's
  "Business process tasks" tab. The next element runs only after the activity is completed WITH A RESULT.
- READ-BACK CAVEAT: describe-business-process shows an element parameter only when it is BOUND (or is a result).
  A fresh Perform task therefore shows only 11 parameters out of the full set it actually carries. Absence from
  describe does NOT mean the parameter does not exist — every parameter in the table below is settable by name
  with `addMapping`. The element's full set is also not a fixed number: the platform derives one extra connection
  parameter per Activity lookup column that exists on YOUR environment, so a custom column adds a parameter.
- IDENTIFY IT IN describe OUTPUT by `buildType: "usertask"` PLUS `userTaskName: "ActivityUserTask"`. It never
  reads back as `performTask`.

--- Parameters you set (addMapping, target = elementName + elementParameter) ---
  Recommendation      LocalizableString. The task subject ("What should be done?"). Becomes the Activity Title.
                                                                                                     [see NOTE-1]
  OwnerId             Lookup -> Contact. THE PERFORMER ("Who performs the task?").                   [see NOTE-2]
  ActivityCategory    Lookup -> ActivityCategory. Task category. Required in the designer.           [see NOTE-3]
                      "To do" = F51C4643-58E6-DF11-971B-001D60E938C6 (also the runtime default).
                      REQUIRES CrtProcessBuilder >= <MIN-VERSION, fill in from S2b>. An older package rejects
                      the bare-Guid form with: "Value '...' is not valid for parameter 'ActivityCategory' of
                      type Lookup: a Lookup constant is a formula token, not a plain value. Set it via a
                      mapping 'expression' instead..." — that error means YOUR ENVIRONMENT IS BEHIND, not that
                      the parameter is unsettable. Update the package.
  ActivityPriority    Lookup -> ActivityPriority. Default = ab96fa02-7fe6-df11-971b-001d60e938c6 (Normal).
                      Same minimum-version note as ActivityCategory.
  Duration            Integer, default 20.  Planned duration.        DueDate = StartDate + Duration
  DurationPeriod      Integer, default 0.   Unit for Duration.
  StartIn             Integer, default 0.   Delay before the task starts. StartDate = now + StartIn
  StartInPeriod       Integer, default 0.   Unit for StartIn.
  RemindBefore        Integer, default 0.   Remind the owner this long before the start. 0 = no reminder.
  RemindBeforePeriod  Integer, default 0.   Unit for RemindBefore.
  ShowExecutionPage   Boolean, default true.  Open the task page automatically for the current user.
  ShowInScheduler     Boolean, default false. Show the task in the Activities calendar.
  InformationOnStep   LocalizableString. Designer label "Hint for user" — shown behind the info button.
                                                                                                     [see NOTE-1]

  ALL THREE *Period PARAMETERS USE THE SAME ENUM:  0=minutes  1=hours  2=days  3=weeks  4=months

--- Parameters the RUNTIME sets — read them, never write them ---
  ActivityResult      Guid. The element's RESULT (the activity's result). Visible in describe from the start.
  CurrentActivityId   Guid. The created Activity's Id.
                      It is INVISIBLE in describe until bound — the name above is the only way to find it.
                      [AMEND FROM PROBE P6: the sentence "map it into a later element to reach the same
                       activity" ships ONLY if P6 demonstrated that an unbound, defaultless, non-result
                       parameter resolves as a mapping SOURCE. If P6 fails, say the id exists but cannot be
                       consumed downstream from a clio-built process, and why.]
  IsActivityCompleted Boolean. The runtime sets false at creation and true at completion.
                      It looks writable (it ships a default) — setting it does NOTHING. Do not.
  ExecutionContext    Technical. Ignore.

--- Out of scope for parameter mapping ---
  The "Connected to" lookups are CONNECTIONS. Bind them with the `setConnections` op — see "Activity connections"
  below — NOT with addMapping. THE SHIPPED SET IS THESE 19 (Lead, Account, Contact, Opportunity, Invoice,
  Document, Incident, Case, Order, Requests, Listing, Property, Contract, Project, Problem, Change, Release,
  Application, FinApplication) — AN ENVIRONMENT MAY HAVE MORE: the platform derives one connection parameter per
  Activity lookup column, so a custom column appears as an extra one.
  Careful: ActivityCategory, OwnerId and ShowInScheduler look like connections (same internal tag) but are
  ORDINARY parameters and must be set with addMapping.
  QueueItem: do not use it. [AMEND FROM R8 — do NOT ship the phrase "has no effect" until R8's grep is done;
   the earlier basis for that claim was wrong, since ActivityPriority has the identical untagged/ungrouped shape
   and IS live.]

NOTE-1 (the two localizable-string parameters, Recommendation and InformationOnStep): these are NOT plain Text.
  A designer-authored constant on a localizable-string parameter lives in the PROCESS SCHEMA RESOURCE, and the
  compiled property reads it from there by resource-item name — not from the value the builder writes. So a
  `value` written through addMapping may be accepted and still not appear at run time.
  ALWAYS give the element a meaningful `caption`: the Activity Title falls back to the element caption when
  Recommendation is empty, which makes a good caption a free and reliable safety net.
  [WARNING - AMEND FROM PROBES P3a/P3b: the EXPECTED result is that these constants do NOT persist. Ship
   caption-only naming unless the probes positively show otherwise.]

NOTE-2 (the performer): OwnerId is Lookup -> Contact. Three working ways to set it:
  * a process parameter: create it with `typeFromElement` + `typeFromElementParameter: "OwnerId"` so the types
    are guaranteed compatible, then map it in;
  * another element's Contact/Guid output parameter;
  * `expression: "[#SysVariable.CurrentUserContact#]"` for "whoever started the process".
  A Lookup -> SysAdminUnit source is REJECTED (incompatible reference object) — you cannot assign a role this way.
  ASSIGNING TO A ROLE OR TO AN EMPLOYEE'S MANAGER IS NOT SUPPORTED from clio yet: those live in an element-level
  performer-assignment object, not in a parameter. Say so; do not fake it.
  Leaving OwnerId unset is legal — at run time the task falls to the current user's contact.

NOTE-3 (ActivityCategory): it MUST be a constant, not a formula. The element's allowed-results list is computed
  from the category ONLY when the category's source is ConstValue; writing it as a `[#Lookup...#]` expression sets
  the column and SILENTLY degrades the result list to the default.
  [AMEND FROM PROBE P2: if a bare record Guid in `value` is accepted AND takes effect at run time, that is the
   way — state the minimum CrtProcessBuilder version alongside it. If it is rejected, OR if it saves but has no
   runtime effect, this parameter cannot be set correctly yet — say so explicitly rather than suggesting the
   expression form.]

--- Worked example: "Call the client, due in 2 days, assigned to the process starter" ---
1) create-business-process
   { "name": "UsrCallClient", "caption": "Call client",
     "elements": [
       { "name": "start",  "type": "startEvent" },
       { "name": "callTask", "type": "performTask", "caption": "Call the client about the renewal" },
       { "name": "finish", "type": "endEvent" } ],
     "flows": [ { "from": "start", "to": "callTask" }, { "from": "callTask", "to": "finish" } ],
     "parameters": [
       { "name": "PerformerContact", "caption": "Performer",
         "typeFromElement": "callTask", "typeFromElementParameter": "OwnerId" } ] }

2) modify-business-process  (operations, in this order)
   [ { "op": "addMapping", "elementName": "callTask", "elementParameter": "Recommendation",
       "value": "Call the client about the renewal" },
     { "op": "addMapping", "elementName": "callTask", "elementParameter": "OwnerId",
       "expression": "[#SysVariable.CurrentUserContact#]" },
     { "op": "addMapping", "elementName": "callTask", "elementParameter": "Duration",       "value": "2" },
     { "op": "addMapping", "elementName": "callTask", "elementParameter": "DurationPeriod", "value": "2" },
     { "op": "addMapping", "elementName": "callTask", "elementParameter": "StartIn",        "value": "0" },
     { "op": "addMapping", "elementName": "callTask", "elementParameter": "RemindBefore",       "value": "30" },
     { "op": "addMapping", "elementName": "callTask", "elementParameter": "RemindBeforePeriod", "value": "0" },
     { "op": "addMapping", "elementName": "callTask", "elementParameter": "ActivityCategory",
       "value": "F51C4643-58E6-DF11-971B-001D60E938C6" } ]

3) describe-business-process -> every parameter you bound now appears with its source and value.
   The ones you did NOT bind stay hidden. That is expected; it is not a failure.
```

### 9.3 In-repo doc targets (clio)

- `C:/Projects/clio/docs/McpCapabilityMap.md` **§11 "Business Process Modeling"** (line 676) — only if a tool
  contract or DTO changed.
- `C:/Projects/clio/docs/McpCapabilityMap.md` **§4 "User Task Engineering"** (line 447) — the `list-user-tasks`
  section; mandatory if S9 lands, reviewed and stated either way.
- No `clio/help/en/*.txt`, no `clio/docs/commands/*.md`, no `WikiAnchors.txt`: the six in-scope tools
  (`CreateBusinessProcessTool`, `DescribeProcessTool`, `GetProcessSignatureTool`, `ModifyBusinessProcessTool`,
  `ValidateProcessGraphTool`, `ListUserTasksTool`) are MCP-only — their options types register no `[Verb]`.
  **State *"docs reviewed, no update required"* in the PR anyway** — the policy requires the statement.

---

## 10. Risks and open questions

Everything the research could **not** confirm, each with a concrete resolution.

| # | Risk / question | Why it matters | How to resolve |
|---|---|---|---|
| **R1** | Is the platform feature `UseProcessPerformerAssignment` ON on the target stands? | It flips the performer surface between the plain `OwnerId` parameter and the `BP7` options object + a lazily created `RoleId` parameter. No `Feature`/`AdminUnitFeatureState` data row for it exists in `CrtProcessDesigner/Data`, so it is defined elsewhere or DB-only. | Probe at runtime on each stand (feature service / `SysFeature` read). D3 is designed to be correct either way — but the *observed designer UI* in check 8 of §8.4 will differ, so know the answer before calling a UI difference a bug. |
| **R1b** | Is `GlobalAppSettings.UsePerformerCultureInUserTask` ON? | A **third** performer-related flag, distinct from R1's `UseProcessPerformerAssignment`, and the one that decides what the `BP7`-absent path — the path the builder produces — actually does. OFF: `AssignmentOptionsInitializer.Init()` returns at `:208-211`, runtime options stay null, and `GetAssignmentOptions()` yields the `AssignmentType.None` / `GetPerformer()` fallback (`ProcessActivity.cs:1060-1063`). ON: `Init()` takes `:212-218`, building options with `AssignmentType.User` and running `InitCultureForUser` (`:144-148`). **The performer outcome is the same either way** — both read the `IsPerformer` parameter and coerce empty to the current user — so D3 holds regardless; the flag only changes culture resolution. | Read the setting on each stand and record it next to R1. Do not interpret a culture difference between a builder-made and a designer-made task as a defect without it. |
| **R2** | Does a plain-Guid `ConstValue` on a Lookup parameter resolve at run time for `OwnerId`, not just for `ActivityCategory`? | The whole of D4/S2. Source evidence is strong for `ActivityCategory` (`GetResultParameterAllValues` reads it that way, `ActivityUserTask.cs:194-196`), for `ActivityPriority` (shipped that way), and at the type/codegen level (`LookupDataValueType : GuidDataValueType`, `DataValueType.cs:1967`; the non-class `ConstValue` field initializer at `ProcessSchemaGeneratorNew.cs:756-763`) — but nobody has run it. | **Probe P2** (§7 S1), with **all three D4 branches** in play: refused / resolves / **saves-but-no-runtime-effect (blocking)**. If it fails for `OwnerId`, narrow the relaxation and document `expression` for the performer. |
| **R3** | Does a `Recommendation` (or `InformationOnStep`) constant written by the builder produce the right `Activity.Title` / hint text? | The designer stores these in the process schema RESOURCE, not in `GS2`. If the builder's write no-ops, every AI-built task is silently mis-titled. **The code evidence says it WILL no-op**: the generator's `ConstValue` branch for `TextDataValueType`/`LocalizableStringDataValueType` emits `GetLocalizableString(resourceManager, sourceValue.ResourceItemName)` (`ProcessSchemaGeneratorNew.cs:641-650`) — reading the resource by `ResourceItemName` (`ProcessSchemaParameterValue.cs:243`), a property the builder never sets, and never touching `Value`. | **Probes P3a (`Recommendation`) and P3b (`InformationOnStep`)** — write, save, run, read `Activity.Title` and the info-button text. Record `UseProcessPerformerAssignment` (R1) with P3b: it selects the read path (`ActivityUserTask.cs:110` vs `:119-120`). Drives S3; plan for failure. |
| **R4** | Was the ticket's "ActivityPriority settable by constant" observed, or inferred from the shipped default? | It determines whether the published works/not-works table inherits a false row. | **Probe P1.** Code says it cannot work today (`ProcessParameterValueValidator.cs:62-68` + the pinned test). |
| **R5** | Does the Process Designer UI render a builder-produced Perform task correctly? | The builder writes `BP2` element parameters with fresh UIds and `BK15` mapping rows; nobody verified the designer accepts a partial/omitted mapping set. | Check 8 of §8.4 — open the process in the designer and watch the browser console. |
| **R6** | Does `C:/Projects/workspace/ProcessBuilder` match the deployed `Terrasoft.WebApp.Loader/.../Pkg/CrtProcessBuilder`? | Researchers disagreed. Planning against the wrong baseline wastes the whole ticket. | **S0** diff. Treat the workspace as authoritative regardless. |
| **R7** | `ActivityPriority`'s default carries `GS5 = fe10dd95-2d61-4aa1-8111-9fb23b032603` — the `UserQuestionUserTask` schema UId, not `ActivityUserTask`. | Looks like platform copy-paste provenance, but if `ModifiedInSchemaUId` participates in default resolution it could affect a rewrite. | Read `ProcessSchemaParameterValue`'s use of `GS5` in `Terrasoft.Core/Process/ProcessSchemaParameterValue.cs`; confirm behaviourally in P1. |
| **R8** | `QueueItem` — legacy or wired through a different path? **UNVERIFIED; do not publish a verdict.** | The column-copy loop cannot reach it (no `EntityColumnValue`/`L17` tag, and no `.Group` resource item). **But that shape does NOT imply inert**: `ActivityPriority` (metadata entry 36) is *equally* untagged and ungrouped, and it is demonstrably live via the explicit `UserTaskActivityInfo.PriorityId` assignment (`ActivityUserTask.cs:152`). So the earlier "unlike all other lookups" argument was simply false, and the guidance line "QueueItem is legacy and has no effect" currently rests on a premise the metadata refutes. | Grep `QueueItem` across `CrtProcessDesigner` **schemas AND the runtime handler** (`Terrasoft.Core.Process/UserTaskActivityHandler.cs`), the same way `ActivityPriority`'s consumer was found. Only if **no explicit consumer exists** may the guidance say "ignore it" — and it should say "no known consumer", not "no effect". **Blocks the §9.2 QueueItem line.** |
| **R9** | Which configuration does CI build/test — `dev-nf`, `dev-n8`, or both? | Only `dev-nf` is buildable on this host (`.application/net-core` is absent). If CI runs `dev-n8`, new tests must be validated there too. | Read the ProcessBuilder CI definition; state the answer in the PR. |
| **R10** | `.ai/skills/clio-guidance-development/SKILL.md` still instructs updating `GuidanceCatalog` / `RoutingGuidanceResource`, which no longer exist in clio. | Will actively misdirect whoever does S7. | Out of scope here; file a cleanup ticket. Flagged in §9.1 so this ticket is not derailed. |
| **R11** | ClioRing: does it consume any process-designer tool? | `AGENTS.md` makes the compatibility gate mandatory; it was not inspected. | The grep in **S10**. |
| **R12** | Does `ListUserTasks()` stay wire-compatible if a request parameter is added (old clients post `"{}"`)? | Decides option (i) vs (ii) in the **STRETCH** S9. | Probe on a stand before starting S9. Do not assume. |
| **R13** | ~~Auto-created `ProcessSchemaMapping` rows carry `Name = null`.~~ **CLOSED — not a risk.** | Raised in `.codex/workspace-diary.md:493` (addendum 9), **closed at line 484 (addendum 16)**: `Name` is not in the mapping's own `[DesignModeProperty]` meta set (`GT1`–`GT5` cover only `Source`/`TargetMetaPath`/`TargetUId`/`SourceSchemaUId`/`SourceParameterUId`) and **no reader exists** anywhere in `Terrasoft.Core`, `Terrasoft.Core.Process` or PackageStore. A cosmetic metadata diff, not a functional risk. | Nothing to do. Do not re-open; do not list it as a probe purpose. |
| **R14** | Does the process advance when the activity is **cancelled** rather than completed? | Academy documents completion "with a result" and is silent on cancellation. Guidance must not claim what it does not know. | Live test: cancel the activity, watch `SysProcessLog`. If unresolved, the guidance simply does not mention cancellation. |
| **R15** | Can outgoing flows branch on the activity result in practice? | `AllowedResult` on the created Activity is derived from the element's outgoing **conditional** flows (`ProcessActivity.cs:1307-1325`) — but conditional flows are **not buildable** from clio today. | Confirm in the designer. If conditional flows remain unbuildable, guidance must state that `ActivityResult` is readable but not branchable from a clio-built process. **This may be the single biggest practical limitation and it belongs in the guidance.** |
| **R16** | **Release skew: guidance auto-releases, the package does not.** A clio-knowledge merge ships instantly to every clio user; the S2 validator relaxation reaches only environments whose `CrtProcessBuilder` has been updated. | Without a gate, the AI is told to make a call that half the installed base rejects, and reads the rejection as "the parameter is unsettable". | **S2b** — bump the bundled archive (`1.1.0.0` → `1.1.1.0`), which arms the existing `IBundledPackageConvergence` refusal naming both versions (`clio/Common/BundledPackageConvergence.cs`, pinned by `ProcessDesignerRequiresPackageAttributeTests.cs:125-195`). Merge S7 **after** S2b, and state the minimum version + the exact stale-server error in §9.2. **Do NOT add a `[RequiresPackage]` version literal** — presence-only is asserted at `:59` and `:93` and is a deliberate ADR position. |
| **R17** | **`N`, the environment-dependent parameter tail.** `SynchronizeActivityConnectionParameters` (`ActivityUserTask.cs:216-219`) derives one connection parameter per Activity lookup column on the environment, so the live set is `37 + N`. | AC1 ("support all params") cannot be closed against §4's fixed table, and the guidance's parameter list is a baseline rather than a complete set. | **Probe P0** (§7 S1) — dump the live set, diff it against the 37, record `N` and the names, classify each extra as connection-derived (ENG-91845) or otherwise. |
| **R18** | **Can an element OUTPUT be consumed as a mapping SOURCE?** `CurrentActivityId` / `ActivityResult` into a downstream element. | §9.2 currently instructs it; nothing has ever exercised it. The source parameter is invisible in describe (G6) and resolution runs through `ProcessSchemaElementLocator` (by name) plus `ParameterTypeCompatibility`, neither tested for an unbound, defaultless, non-`IsResult` parameter. Shipping an unverified write instruction is the exact failure mode D7 exists to prevent. | **Probe P6** + an E2E in `ModifyBusinessProcessToolE2ETests.cs`. Gate the §9.2 sentence on the result. |
| **R19** | Does a fully-configured Perform task pass **`validate-process-graph`**, and what does the validator say for a type-incompatible mapping? | The tool exists and is in scope (`clio/Command/McpServer/Tools/ProcessDesigner/ValidateProcessGraphTool.cs`, gated, own `[RequiresPackage]` at `:101`) and the save path is fail-closed on `EnsureValidForSave` — but no probe, checklist step or E2E ever invoked it. Platform validation is what catches a wrongly-encoded category or an incompatible mapping. | **Probe PV** after each probe group, **§8.4 step 6b**, and two E2E assertions (positive + type-incompatible negative). |

**Explicitly unverified and deliberately excluded from scope:** `BP7` / role / manager assignment (D3);
LocalizableString resource materialization (S3's failure branch); first-class value sources (Task 6);
"Connected to" binding (ENG-91845).

---

## 11. Definition of Done

### AC1 — support all params of the element

- [ ] The S1 probe matrix is complete and recorded in the diary, with per-parameter-family
      `saved / read-back / runtime effect / validate-process-graph verdict` results.
- [ ] **Every one of the 37 STATICALLY DECLARED parameters has a settled disposition** — **settable** (with the
      documented route), **output** (documented as read-only), **inert** (`ExecutionContext`; `QueueItem` only if
      R8 resolved it), or **out of scope** (the 19 shipped connections → ENG-91845) — **AND the live-vs-static
      delta from probe P0 is recorded and classified** (`N`, the names, and why each extra one exists). AC1 cannot
      be closed against a fixed list: `SynchronizeActivityConnectionParameters` makes the live set `37 + N`.
- [ ] §4's "Supported by builder today?" column contains no ⚠️ — every row is CONFIRMED by probe or by code.
      This explicitly includes **both** LocalizableString rows (`Recommendation` via P3a, `InformationOnStep` via
      P3b) and the `QueueItem` row (via R8).
- [ ] The ticket's incorrect `ActivityPriority` claim is corrected in the ticket itself, not only here.
- [ ] If P2 passed: `ValidateConstantValue` accepts a parseable Guid on a Lookup target **via the early-return
      form** (the branch is kept, only its body changes); a non-Guid is still rejected with a message containing
      `expression` and `[#Lookup`; both behaviours are unit-pinned.
- [ ] **No parameter shipped under a "saves but no runtime effect" P2 result** — that outcome is blocking, not a
      partial pass (D4).
- [ ] `ActivityCategory` is settable in the encoding the platform actually reads (`ConstValue`) — or the
      guidance says plainly that it is not yet, with the reason.
- [ ] `OwnerId` is settable through at least one route verified end to end (value reaches `Activity.OwnerId`).
- [ ] `Recommendation` is either verified end to end, or the guidance documents caption-only naming and a
      follow-up ticket exists. Same for `InformationOnStep`.
- [ ] **Outputs verified as mapping SOURCES (P6):** `CurrentActivityId` and `ActivityResult` either resolve into a
      downstream element (pinned by an E2E) **or** the guidance says they do not and why. The §9.2 "map it into a
      later element" sentence ships only in the first case.

### AC2 — add guidance about the element in clio

- [ ] `== Element: Perform task (userTask / performTask -> ActivityUserTask) ==` is merged into
      `guidance/mcp/guides/processes/process-modeling.md` on `clio-knowledge` `master`.
- [ ] Every ⚠️/`[AMEND FROM PROBE …]` marker in the §9.2 draft has been resolved before merge.
- [ ] Both `libraryVersion` and `sequence` bumped in `bundle-source.json`, computed from `origin/master`.
- [ ] clio-knowledge producer contract suite green.
- [ ] The guidance covers: what the element is, when to use it, when **not** to (Approval), what it produces,
      the full parameter table with period-enum values, the performer routes **and their limits**, the
      output-parameter warning, the connections boundary, and one worked create+addMapping example.
- [ ] `curated-knowledge-names.json` re-pinned **only if** clio's consumed generation moved.

### AC3 — "Connected to" out of scope

- [ ] No connection-binding code was written.
- [ ] The guidance states the boundary explicitly, including the three tagged-but-not-connection parameters
      (`ActivityCategory`, `OwnerId`, `ShowInScheduler`).
- [ ] **P7 run and recorded.** ENG-91845 is merged (§2 E2), so the status deliverable states whether
      `setConnections` on `performTask` actually works today — it must NOT repeat the ticket's stale
      "blocked on Task 7". A P7 failure was filed against ENG-91845, not fixed here.

### In-repo docs that already describe this element (§2 E5)

- [ ] `spec/ai-business-process-generation/ai-bp-element-catalog.md:67` reconciled against §4: the invented
      `Who performs` field replaced by `OwnerId`, the unsupported `ActivityCategory` (required) claim dropped or
      substantiated, and the parameter list either completed or explicitly marked as a subset with a pointer to
      the guidance.
- [ ] `spec/process-design-service/task-list.md:343` (and the `:14` index line) carries this ticket's
      works/does-not-work verdict, following the `44d953325` (ENG-92706) precedent: name the unmet criteria
      explicitly, and do not attribute a descope to a Jira description that was never amended.
- [ ] Confirmed that `spec/process-design-service/process-design-service-state.md` is still absent (§2 E6) and
      that nothing in this change references it as if it existed.

### Engineering gates

- [ ] The §1 scope choice (Option A vs Option B) is stated explicitly on the ticket, with the estimate matching it.
- [ ] The pre-existing uncommitted `descriptor.json` `ModifiedOnUtc` edit (§2 E1) was resolved deliberately —
      either superseded by the S2b rebundle or reverted — and did not ride along in an unrelated commit.
- [ ] `clio-knowledge` was pulled from `origin/master` **before** the guidance edit (§2 E3).
- [ ] `get-fsm-mode` was run and recorded, and the deploy sequence used matches the mode (S0b). No
      `compile-creatio` was run against an FSM-ON environment.
- [ ] `dotnet build C:/Projects/workspace/ProcessBuilder/MainSolution.slnx -c dev-nf` green (the **solution**, not
      a package csproj — `packages/CrtProcessBuilder/CrtProcessBuilder.csproj` does not exist).
- [ ] `dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf --filter "Category=UnitTests"` green.
- [ ] **`pwsh rebundle-process-builder.ps1 -PackageRepoPath C:/Projects/workspace/ProcessBuilder -Version 1.1.1.0`
      run (S2b), and all FOUR pins in `clio.tests/Common/BundledProcessBuilderPackageTests.cs` updated** —
      `ExpectedArchiveSha256` (:111), `ExpectedArchiveVersion` (:137), `ExpectedDescriptorModifiedOnUtc` (:163),
      `ExpectedSchemaDescriptorModifiedOnUtc` (:178). The version went **up**.
- [ ] **clio rebuilt after the rebundle**, before any probe or E2E run that installs the package (the archive is
      resolved from the build output, not the repo).
- [ ] `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=McpServer|Module=ProcessModel|Module=Command|Module=Common)"` green.
- [ ] **`dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` (FULL suite) green** — mandatory
      because `clio/Common/` is touched (`AGENTS.md` smart-regression rule 4).
- [ ] E2E extended in `clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs` and passing against a stand
      (mandatory per `AGENTS.md`, not optional), including the **P6 output-as-source** mapping and the
      **`validate-process-graph`** positive + type-incompatible negative.
- [ ] MCP review statement in the PR **per target**: the six tools, the five prompts (incl.
      `DescribeProcessPrompt.cs` and `CreateBusinessProcessPrompt.cs`), and
      `clio/Command/McpServer/Resources/` — each either "changed: …" or *"reviewed, no update required"*.
- [ ] No `[RequiresPackage]` version literal was added (presence-only is pinned and deliberate); the skew is
      handled by the convergence detector armed by the S2b version bump.
- [ ] ClioRing gate statement in the PR: the grep result and either the full gate output or
      *"ClioRing compatibility reviewed, no Ring-consumed contract changed"* with the inspected paths.
- [ ] `docs/McpCapabilityMap.md` **§11 (line 676)** and, if S9 landed, **§4 "User Task Engineering" (line 447)**
      updated — or *"docs reviewed, no update required"*.
- [ ] Diary entries appended in **both** `C:/Projects/workspace/ProcessBuilder/.codex/workspace-diary.md` and
      `C:/Projects/clio/.codex/workspace-diary.md`, including the probe matrix.
- [ ] `spec/process-design-service/task-list.md` task 8 updated.
- [ ] Pre-PR agentic code review run over the full diff; all Blocker/High findings resolved.
