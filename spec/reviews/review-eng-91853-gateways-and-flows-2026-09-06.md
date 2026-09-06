# ENG-91853 — comprehensive review gate #2 (pre-PR), 2026-09-06

> **STATUS: INTERIM — written mid-review so nothing is lost if the session ends.** Sections marked
> `[pending]` are still being measured. Everything not so marked was verified as stated.

Run 2026-09-06 by a fresh session over the COMPLETE diff of all three repositories, per the brief in
[eng-91853-gateways-and-flows-pre-pr-review-prompt.md](../eng-91853-gateways-and-flows/eng-91853-gateways-and-flows-pre-pr-review-prompt.md)
and [AGENTS.md § Code review](../../AGENTS.md) gate 1. The reviewer did not write the code and
implemented nothing: every item below is reported, not fixed.

| Repository | Path | Base | Tip reviewed | Commits |
|---|---|---|---|---|
| clio | `C:/Projects/clio` | `a9deb32bc` (`master`) | `e5ce93f11` | 42 |
| crt-process-builder | `C:/Projects/workspace/ProcessBuilder` | `7e93995` (`main`) | `a82a779` | 17 |
| clio-knowledge (worktree `.worktrees/eng-91853`) | `C:/Projects/clio-knowledge/.worktrees/eng-91853` | `84e2609` (`master`) | `c1a9e69` | 5 |

All three tips and bases were confirmed with `git rev-parse` / `merge-base --is-ancestor` before anything
was read.

## Evidence base — what was actually run

| Check | Result |
|---|---|
| clio targeted suite, `-c Release`, `Category=Unit&(Module=ProcessModel\|McpServer\|Command\|Common)` | **10 136 passed, 0 failed, 18 skipped** (6 m 45 s) — matches the handoff exactly |
| Package suite `dotnet test tests/CrtProcessBuilder -c dev-nf` (junction healthy on this host) | **1 237 passed, 0 failed** (7 s) — matches the brief exactly |
| clio-knowledge `automation/Clio.Knowledge.Bundle.Tests -c Release` on the guidance branch | **131 passed, 0 failed** |
| Bundled archive `clio/CrtProcessBuilder/CrtProcessBuilder.gz` decompressed and diffed against `git archive HEAD packages/CrtProcessBuilder` | **content-identical** (129 entries; the only non-EOL difference is `CrtProcessBuilder.csproj.DotSettings`, which the compressor excludes). Five files differ in line endings only — the four new files were written LF while `git archive` under `core.autocrlf=true` emits CRLF; the committed blobs are LF for all of them (`git ls-files --eol`) |
| Producing-commit pin `bad9e2d7` vs package tip `a82a779` | `bad9e2d7` is an ancestor; the ONLY commit after it is `a82a779 Restamp to 1.4.0.61`, touching `descriptor.json` alone (1.4.0.60 → 1.4.0.61). The archive's descriptor carries 1.4.0.61 / `/Date(1788659637000)/` = HEAD. The restamp IS committed this time (last gate's B1 is closed) |
| `scripts/check-knowledge-applies-to.py --base a9deb32bc` | 28 records touched (advisory); `--dead-only`: 1 dead path, **pre-existing** (`odata-write-transport-never-throws-on-non-2xx.md` → `ODataResponseError.cs`, absent at the base too) |
| Stand `Creatio` (`d_krestov_n.tscrm.com:40001`) | `ping` only (read-only). **Nothing else was run on the stand, per the owner's instruction during this session.** The e2e binder test `ValidateProcessGraph_Should_BindEdgeCondition_FromTheWire` therefore remains **never executed** |
| Platform-source reads | `ProcessSchemaParameter.GetMetaPath()` (`Terrasoft.Core/Process/ProcessSchemaParameter.cs:1097`) confirmed to emit `[Element:{ContainerUId}].[Parameter:{UId}]` when `ContainerUId` is set and is not the schema — the element-output arm of `ConditionParameterNames` rests on the same fact `ProcessMappingService` (stand-proven) already relies on |
| ClioRing consumer surface | `grep` over `clio-ring/` for every process-designer tool and nested command name: **no hits**; positive control (`clio-run`, `list-environments`) found in `clio-ring/ClioRing/ViewModels/*.cs`; `clio-ring/ClioRing.Desktop/actions.json` names `get-info`, `list-packages`, `restart-web-app`, `show-web-app-list` and the `clio-*` actions only |
| Four parallel review lenses (package correctness; layout + clio validator; contracts/guidance/MCP/security; test coverage + mutation candidates) | Lenses A, C, D reported and folded in below. Lens B was killed by a rate limit before it read a file and was relaunched `[pending]` |
| **Package mutation runs — 5 executed**, each applied to the real source, rebuilt (`compiled=True` checked) and run against the full `dev-nf` suite, file restored with `git checkout` | **F1, F4, F5, F6 all GREEN at 1 237/0** — four unpinned guards, measured. **Positive control** (self-loop refusal disabled) → **RED**, 1 failed (`AddFlow_SelfLoop_IsRefused`), so the pipeline recompiles and the four greens are real |
| **clio mutation run — 1 executed** (F2) | Narrow scope `FullyQualifiedName~ProcessGraph`: **GREEN 51/51**. Wide scope `Category=Unit&(Module=ProcessModel\|Module=McpServer)` on the same mutant binary: **GREEN 4 806 passed / 0 failed / 2 skipped** (6 m 43 s). **Positive control** (R15 self-loop check disabled, rebuilt) → **RED**, `Validate_ShouldSurfaceR15Error_ForASelfLoop` fails, so the clio pipeline recompiles and the green is real |

## Verdict `[pending — one HIGH, no Blocker so far]`

**One High must be resolved before the PRs open: the shipped `modify-business-process` MCP prompt still
describes the pre-change product (F0).** Everything else found so far is Medium or Low: coverage gaps
(guards that are correct but only partly pinned), three instruction surfaces that describe the old
product, and one AGENTS.md policy gap on e2e coverage. Mutation-confirmed items are marked; predicted ones
are marked `[predicted]` until the run completes.

---

## Findings

### F0 — HIGH — the shipped `modify-business-process` prompt says there is no clear-condition operation and does not know `setFlow`

**Where:** clio `clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs:27-39` —
**untouched by this branch** (`git diff --stat a9deb32bc..HEAD` on the file is empty). Verified verbatim:

> `op`: `addElement`, `removeElement`, `addFlow`, `removeFlow`, `setFlowCondition`, … (no `setFlow`;
> `addFlow` presented without `kind`/`condition`) … **There is no clear-condition operation**, and
> `removeFlow` + `addFlow` is NOT a substitute for one … To CHANGE a condition call `setFlowCondition`
> again; to make a branch always taken set its condition to `true`.

**Claim:** the prompt contradicts the tool it fronts. `ModifyBusinessProcessTool.cs:44-48` documents
`addFlow kind/condition` and `setFlow` ("changes an EXISTING flow's kind IN PLACE"); the package's
`ProcessGraphBuilder.SetFlow` re-kinds in place and refuses only the last-branch case;
`docs/McpCapabilityMap.md:741` and guidance `branch-conditions.md:97-103` both name `setFlow kind:
"sequence"` as the clear-condition operation.

**Failure:** an agent that loads the prompt (its purpose) and is asked to remove a branch's condition is
told the operation does not exist, and either sets the condition to `true` (an always-taken branch — the
opposite of a plain flow) or reports the edit impossible; an agent adding a branch on modify is steered
to addFlow-plain + setFlowCondition, the two-save window this ticket removed. One MCP server hands out
three answers to one question: the tool description (setFlow exists), the prompt (nothing exists), the
guidance (setFlow is the way).

**Rule:** AGENTS.md § MCP maintenance policy — *"If the command has an MCP prompt, keep the prompt
guidance aligned with the current tool contract"* — and `Prompts/*.cs` is a required MCP target for every
touched command. This is the same class as the previous gate's H6, which was fixed in guidance and not in
the prompt. `docs/knowledge/ProcessModel/conditional-flow-rekind-must-be-in-place.md` even lists this
prompt in its `applies-to`, and the advisory check reported it as touched.

**Fix:** rewrite the op list and the condition paragraph to match `ModifyBusinessProcessTool.cs`
(setFlow; addFlow kind/condition; clearing = `setFlow kind:"sequence"`, refused when it would drop the
last conditional branch off a still-branching element).

### F0a — MEDIUM — shipped guidance `process-naming` still says gateway elements and default flows are unbuildable, and it is on the critical path

**Where:** guidance `guidance/mcp/guides/processes/naming.md:121-129` (untouched on the branch), rule N10:
*"`flows[]` takes `source` and `target` and nothing else"*; *"build it plain, then `setFlowCondition`"*;
*"Only the LABEL is missing, along with default flows and gateway ELEMENTS (ENG-91853 extends that)"*.
All three are false at `c1a9e69`. `process-modeling.md:8-9` tells every agent to read this article
*"BEFORE you name anything"*, so it sits on the critical path of every build.

**Failure:** an agent following it never emits `kind: "default"` or a `parallelGateway` and builds every
branch in two saves — the previous gate's M4 one article over. Belongs inside the same 1.13.94 bump.

### F0b — MEDIUM — `SetFlow`'s authoritative contract docs say an omitted kind means `sequence`; the code refuses it

**Where:** package `Graph/IProcessGraphBuilder.cs` (`SetFlow` `<param name="kind">`: *"empty means
`sequence`"*) and `Contracts/ModifyContracts.cs:76-79` (*"For `addFlow` and `setFlow`: … Omitted means
`sequence`"*). `ProcessGraphBuilder.cs:415-420` refuses a blank kind on `SetFlow` (the previous gate's B2
fix). The interface doc is the authoritative contract under AGENTS.md's C# documentation policy.

**Failure:** a maintainer aligning the implementation to its own contract doc reintroduces the Blocker
(silent destruction of a conditional branch); a wire-contract reader omits `kind` on `setFlow` and gets a
refusal the contract says cannot happen. Fix: state the asymmetry — omitted means `sequence` on `addFlow`
and is refused on `setFlow`.

### F0c — LOW (verified) — stale package docblocks and one floor rationale

- `Operations/FlowOperations.cs:121-123` and `ProcessDesignConstants.cs:262-264` still say the build path
  *"cannot resolve"* a condition *"at graph-construction time"* — false since `ConditionParameterNames`.
- `CreateBusinessProcessTool.cs:118-120` and both command comments say *"below [1.4.0.60] the package
  refuses `flows[].kind` and the two gateway type tokens outright"* — .58/.59 accept them; only the name
  expansion is .60. No wrong action follows (the floor blocks anyway), but a maintainer lowering the floor
  "because .58 accepts kind" would be arguing with a false comment.
- guidance `perform-task.md:106` — the one surviving *"1 522"* denominator.
- `docs/McpCapabilityMap.md:741` and guidance `process-modeling.md:150-152` list operations without
  `setFlow` while describing it elsewhere.
- `DescribeProcessTool.cs:35` never mentions the new flow `name` field; clio's `DescribedFlow` carries it
  only through `[JsonExtensionData]` and no test pins the flow-level bag.
- `ModifyBusinessProcessTool.cs:44-45` (`addFlow`) does not say a modify-path condition must be a UId
  meta-path; a name there costs one platform refusal round trip.

### F0d — MEDIUM — a `default` flow beside no conditional sibling is constructible in one step on both write paths, and it runs as a plain flow

**Where:** package `Graph/FlowKindRules.cs:146-159` (`EnsureAtMostOneDefault` is the only rule about the
`default` kind), `Graph/ProcessGraphBuilder.cs` `AddFlow` / `SetFlow`.

**Claim:** the package refuses REACHING the state "source has a default and other outgoings but no
conditional" by re-kinding away the last conditional (`WouldDropTheLastBranch`), yet lets a caller build the
identical end state directly, with no notice.

**Failure:** `Task1 → A` plain, then `{"op":"setFlow","source":"Task1","target":"B","kind":"default"}` (or
the same two flows on the build path) → written; `describe` reports `kind: "default"`. At run time
`FlowSchemaGenerator` sets `HasConditionalSequenceFlow` only from `BpmnElementName == "CSF"`
(`FlowSchemaGenerator.cs:124-125`, read from platform source) and a default flow's `BpmnElementName` is the
plain `SequenceFlowName` (`ProcessSchemaSequenceFlow.cs:284`), so no gateway is synthesized and BOTH flows
are taken — the "default" is a plain flow with a misleading label, the describes-one-way-runs-another class
this branch refuses everywhere else. The platform's pre-save gate has no rule that reads flow kinds
(`ProcessInterpretationValidator.GetDefaultValidationRules`), and clio's R14 sees it only when the agent
runs the advisory plan check — on modify nothing sees it. The designer offers the same connection, so the
package's "mirrors the designer" charter is met; the asymmetry with `WouldDropTheLastBranch` is what makes
it a finding. A notice (or refusal) when a `default` is written beside no conditional on a source that has
other outgoings is one rule; no test pins either direction today.

### F0e — MEDIUM — the "88 % of real conditions are now buildable by name" claim counts 242 conditions the name form cannot express

**Where:** package `Formulas/ConditionParameterNames.cs:20-22` docblock; clio
`docs/knowledge/ProcessModel/build-path-conditions-name-their-parameters.md` (table row *"element output …
487 … no"*); `CreateBusinessProcessTool.cs` ("88% of real conditions name a parameter") and both
`[RequiresPackage]` comments; guidance `branch-conditions.md` (`[#Element.Parameter#]` only).

**Measured (re-run by the reviewer over `C:/Projects/PackageStore`, agreeing with lens A to the unit):**
1 061 non-empty conditions; 487 reference an element output, **242 of those also carry `[EntityColumn:`**
— the three-segment *"a column of the record the element returned"* form; 245 do not; 445 reference a
process parameter only. By-name expressible = (445 + 245) / 1 061 = **65 %**, not 88 %.

**Failure:** the natural spelling of the column form, `[#ReadContact.ResultEntity.Email#] != null`, has
three segments → `TryResolveBody` returns null → stored verbatim → the platform's pre-save gate refuses the
whole build with an unattributed formula error. A refusal, not a silent outcome, but the guidance names no
limit, and for 23 % of real conditions the caller is sent back to the two-step route the feature exists to
remove. The 3-segment pass-through itself is pinned (`ResolveOnBuild_LeavesAPlatformMacroAlone`); the
CLAIM is what is wrong. Fix: correct the number on every surface and document the column limit — or add an
`[#Element.Parameter.Column#]` arm.

### F0f — LOW (verified, from lens A) — four message/notice inaccuracies on the package write path

- `ProcessGraphBuilder.cs:426-439` — `NoticeIfNormalised` runs BEFORE the same-kind early return, so
  `setFlow kind=sequence` on a gateway whose only outgoing is already its default raises *"was written as
  'default' rather than the requested 'sequence'"* about a write that did not happen.
- `FlowKindRules.cs:197-200` — re-kinding the default branch ITSELF to `sequence` beside a conditional
  sibling computes `hasDefault` over siblings that exclude it, and advises *"make this one the 'default'
  branch"* — which it already is. Right refusal, wrong sentence.
- `ConditionParameterNames.cs:186-191` — `[#Gateway.X#]` passes through (gateways are not
  `ProcessSchemaParametrizedFlowNode`) and surfaces as the platform's unattributed error, where a user-task
  head with a wrong tail gets the package's own refusal listing the parameters.
- `FlowKindRules.cs:113-119` — a designer-authored self-loop (3 in the corpus) cannot be re-kinded, and the
  refusal says *"cannot connect 'a' to itself"* about a flow that already does.
- Build path: `{source, target, condition}` with `kind` omitted is refused as *"A 'sequence' flow cannot
  carry a 'condition'"* — deliberate and pinned, but the tool contract marks `kind?` optional, so an agent
  will hit a message naming a kind it never wrote.

### F1 — MEDIUM — `setFlow`'s blank-kind refusal is pinned for `null` only — **MUTATION CONFIRMED GREEN (1 237/0)**

**Where:** package `Graph/ProcessGraphBuilder.cs:415` `if (string.IsNullOrWhiteSpace(kind))`;
test `tests/CrtProcessBuilder/ProcessFlowKindTests.cs:752 SetFlow_WithABlankKind_IsRefused` passes `null`
and nothing else sends `""` or whitespace to `SetFlow`.

**Mutation:** `string.IsNullOrWhiteSpace(kind)` → `kind == null`.

**Failure the mutant ships:** `{"op":"setFlow","source":"a","target":"b","kind":""}` against a
conditional flow off an element with no other outgoing flow → `ParseKind("")` = `sequence`,
`NoticeIfNormalised` returns at its blank check (no notice), `WouldDropTheLastBranch` is false (no
siblings), `ReKindFlow` → plain class. The condition is gone and the call reports success — the B2 class
from the previous gate, minus the parallel-split half. The guard as written is correct; only the test
is narrower than the guard.

**Fix:** `[TestCase(null)] [TestCase("")] [TestCase("   ")]` on that test.

### F2 — MEDIUM — the R7/R9 block's or-gateway type restriction is unpinned — **MUTATION CONFIRMED GREEN (51/51 owning fixtures; 4 806/0 across ProcessModel + McpServer)**

**Where:** clio `ProcessGraphValidator.cs:207`
`if (eventType is not (EventType.ExclusiveGateway or EventType.InclusiveGateway) || outs.Count <= 1) return;`

**Mutation:** drop the type half → `if (outs.Count <= 1) return;`.

**Failure the mutant ships:** two `R9` warnings ("has a plain sequence flow … say so with kind 'default'"
and "has no default flow") on every parallel fork and on every implicit split off an activity. An agent
following the first on a `parallelGateway` walks into an `R11` error and a package refusal.

**Why green:** measured by reading — no test in `ValidateProcessGraphToolTests.cs` or
`ProcessGraphValidatorTests.cs` asserts a warning-free result; every valid-graph test asserts
`HasErrors == false` or `NotContain(Error)`, and the genuine AND-fork test asserts `NotContain(R8)` only
(grep for `Findings.Should().BeEmpty` over both fixtures: zero hits).

**Fix:** one `Findings.Should().BeEmpty()` on the genuine AND fork/join graph closes this class.

### F3 — MEDIUM — no `clio.mcp.e2e` coverage for the changed build/modify tool contracts (AGENTS.md mandatory rule)

**Where:** `git diff --name-only a9deb32bc..e5ce93f11 -- clio.mcp.e2e/` lists only
`ValidateProcessGraphToolE2ETests.cs`. `CreateBusinessProcessToolE2ETests.cs` has zero occurrences of
`kind`, `gateway` or `condition`; `ModifyBusinessProcessToolE2ETests.cs` exercises `setFlowCondition`
only (pre-existing) — nothing sends `setFlow`, `addFlow.kind`, `flows[].kind`, `flows[].condition`,
`exclusiveGateway` or `parallelGateway` through the real MCP path.

**Rule:** AGENTS.md § MCP maintenance policy — *"Always add or update MCP end-to-end coverage in
clio.mcp.e2e for every new or changed MCP tool. This is mandatory even when the user does not mention
E2E coverage explicitly"* and *"Treat unit tests in clio.tests as necessary but insufficient for MCP tool
changes"*. Both `create-business-process` and `modify-business-process` changed (descriptions, the
1.4.0.60 floor, and the contract they document).

**What breaks:** nothing today — the two agent-mode stand runs exercised every one of these paths through
clio's real MCP server and read the results back from the stand, so the behaviour is proven. What is
missing is the repeatable artefact the policy asks for; the next contract regression on these tools has
no e2e that would notice. Medium rather than High because the process-designer e2e fixtures do not run
in CI at all (the package is not on the CI stand — `project-context.md`), so the gate is manual either
way; but the PR body's `MCP reviewed` sentence has to state this gap explicitly rather than claim
completeness.

### F4 — MEDIUM — the `setFlow` operation's length bound is the only bound on that path and nothing exercises it — **MUTATION CONFIRMED GREEN (1 237/0)**

**Where:** package `Operations/FlowOperations.cs:99-108` (the `EnsureStoredTextIsBounded` block in
`SetFlowOperation.Apply`). `ProcessGraphBuilder.SetFlow` → `ApplyConditional` applies no bound of its own;
`AddFlow`'s bound (`ProcessGraphBuilder.cs:176`) is not on this path. Production call sites of
`EnsureStoredTextIsBounded` confirmed by grep: `FlowOperations.cs:105` (setFlow) and `:177`
(setFlowCondition) are the two operation-level bounds.

**Mutation:** delete the block.

**Failure the mutant ships:** `setFlow kind=conditional` with unbounded text reaches the platform's macro
converters at the pre-save gate — the exact cost the previous gate's M6 named.

**Why green:** `ProcessConditionalFlowTests.SetFlowCondition_ShouldRefuseAnOverlongCondition_WithoutTouchingTheFlow`
covers the `setFlowCondition` token, `ProcessGraphBuilderTests.BuildGraph_WithAnOverlongFlowCondition_IsRefused`
covers `AddFlow`; no test sends `Op = setFlow` with a 2 049-character condition.

### F5 — MEDIUM-LOW — the event-based arm of `EnsureKindSuitsANonDecidingGateway` is exercised nowhere — **MUTATION CONFIRMED GREEN (1 237/0)**

**Where:** package `Graph/FlowKindRules.cs:165` `if (!IsNonDecisionalGateway(source) || kind == Sequence)`.
`ProcessSchemaEventBasedGateway` appears in exactly one test (`GatewayElementHandlerTests`, for
`CanBuild`), never as a flow source.

**Mutation:** `IsNonDecisionalGateway(source)` → `source is ProcessSchemaParallelGateway`.

**Failure the mutant ships:** a conditional or default flow out of a designer-authored event-based
gateway (reachable on the modify path) is written; `NormaliseForADecidingGateway` passes it through
because the source is not decisional — stored, never evaluated, the silent class the docblock says this
rule exists to refuse.

### F6 — MEDIUM-LOW — `WouldDropTheLastBranch`'s kind guard is unpinned; the mutant turns a valid re-kind into a false refusal — **MUTATION CONFIRMED GREEN (1 237/0)**

**Where:** package `Graph/FlowKindRules.cs:84-86` `if (KindOf(flow) != FlowKinds.Conditional) return false;`

**Mutation:** delete those three lines.

**Failure the mutant ships:** `a → b default`, `a → c sequence` (the LeadDistribution shape the R14
exemption exists for), `setFlow a→b kind=sequence` → refused as "the last conditional branch leaving
'a'", a false message about a flow that was never conditional. Every `SetFlow(` call site in
`ProcessFlowKindTests` either re-kinds a conditional flow, has no siblings, or exits at the same-kind
return.

### F7 — LOW — `spec/ai-business-process-generation/ai-bp-connection-rules.md` contradicts itself about R7/R9's severity

**Where:** clio `ai-bp-connection-rules.md`, the **errors** list (`:103-110`) says *"a plain sequence
flow out of a DIVERGING or-gateway (R7/R9, same arity scope — 14 shipped gateways are the one-outgoing
shape)"*, while the R7 bullet 70 lines above says both halves are **warnings** and names the 7 shipped
diverging counter-examples. The code is a warning (`ProcessGraphValidator.cs:221`). The errors list is
the pre-demotion text, and it repeats the "14 … one-outgoing" count the previous gate showed to be false
for 7 of them. Not shipped to users, but this spec is what `docs/knowledge/…/shipped-processes-break-…`
lists in `applies-to`, so the next reader is sent to a document that disagrees with itself.

### F8 — LOW — `spec/sprint-status.yaml` story notes describe superseded states

Story 2's note still says R7/R9's warning *"now names MismatchItemsCountException"* (it now quotes the
process-log line), calls the plain-flow finding *"the or-gateway plain-flow error"* (it is a warning),
and records *"Rebundled 1.4.0.58 … floors 1.4.0.44 -> 1.4.0.58"* (bundled 1.4.0.61, floor 1.4.0.60).
Story 3's note ends with *"REMAINING: activity-connections.md still states five things this change made
false"* — fixed in guidance commit `6ea736c`. Statuses (`in-progress` ×3) are correct for BMAD; the
narrative is stale.

### F9 — LOW — `bundle-source.json`'s `process-modeling` description still counts "eight articles" / "nine"

Carried over from the previous gate's observation; the description text was rewritten in this diff and
the counts were left as they were. Cosmetic.

### F10 — LOW — test-quality items (from the coverage lens, verified by reading)

- `Validate_ShouldWarnR7_NamingTheRuntimeException_WhenADivergingGatewayHasNoDefault` performs a second
  Act (`Validate(...)`) inside its Assert block — the H3 pin hides inside another scenario's test.
- `ProcessLayoutEngineTests.cs:43` — *"It is set HERE because those handlers do not exist yet"*; they do.
- `Validate_ShouldNotSurfaceR7Error_ForALegacyConvergingGatewayWithOnePlainFlow` is named "Error" for a
  rule that is a warning and asserts no R7 at all.
- Merged AAA sections ("Act & Assert") in `GatewayElementHandlerTests.CanBuild_OwnClass_IsClaimedByExactlyOneHandler`,
  `Factory_ResolvesBothGatewayTokens`, `ProcessElementFactoryTests.ResolveBuildType_ShouldReturnNull_ForUnbuildableElement`.
- Local factory fixtures in `ProcessConditionalFlowTests` and `ProcessOperationExecutorGraphOpsTests`
  still omit the two gateway handlers (harmless today — none of their tests builds a gateway).
- Four `CarryOperatorState` fields (`Size`, `IsExpanded`, `CreatedInOwnerSchemaUId`,
  `OwnerSchemaManagerName`) are pinned by a text oracle only, never by value.
- The two gateway token literals (`"exclusivegateway"`, `"parallelgateway"`) are asserted nowhere as
  literals — every test goes through the constant, while clio's shipped descriptions name the tokens.

### F11 — LOW — `CheckSelfLoops`'s `e.Source != null` is unreachable (carried over)

`ProcessGraphValidator.cs:298`. `CheckMissingNodeFlows` runs first and `Dictionary.ContainsKey(null)`
throws, so a null source never reaches this line. Our own code makes it unreachable, which per
`docs/knowledge/Tests/reachability-not-corpus-absence-…` is the delete case. Same for the pre-existing
`ToDictionary(group => group.Key)` in `Validate` — a null node name throws there, against
`IProcessGraphValidator`'s "never throws on malformed input"; the MCP tool's `try/catch` turns it into an
error envelope. Pre-existing, not this branch's.

### F12 — LOW — `ProcessGraphValidator.cs` gained a UTF-8 BOM

The base file had none; the branch's version starts with `EF BB BF`. Consistent with its five siblings in
`clio/Command/ProcessModel/` (all BOM-prefixed), so not wrong — noted because it shows up as a changed
first line in every future blame.

---

## Claims checked and REFUTED / confirmed pinned

- **"The described flow's new `name` field is unpinned"** (my own suspicion) — refuted:
  `ProcessFlowKindTests.cs:867 described.Name.Should().Be("ConditionalFlow_a_b")`.
- **"The bundled archive may not match the package tip"** — refuted by byte comparison (above).
- **The previous gate's P1–P8, H3, M1, M2, B2, H1, H2, M6, M7** — each confirmed pinned by a named test
  (coverage lens, verified by reading the tests): P1 `Apply_BranchPreferringAClaimedCorridor_TakesTheNextFreeLaneInstead`;
  P2 `Apply_MergeWhoseLaneAndTheOneBelowAreTaken_MovesUpNotDown`; P3 `RemoveElement_ShouldDetachTheFlowFromTheSurvivingTarget`
  + `RemoveFlow_DetachesTheFlowFromBothEndpoints`; P4 `SetFlow_NormalisedKind_RaisesANotice`;
  P6 `AddFlow_OutOfAnInclusiveGateway_FollowsTheDecidingGatewayRules`; P7 `Describe_ShouldReadTheFlowKindFromTheClrType_NotTheEnum`;
  P8 `AddFlow_WithAPaddedMixedCaseKind_IsAccepted`; H3 second half of
  `Validate_ShouldWarnR7_NamingTheRuntimeException_WhenADivergingGatewayHasNoDefault`; M1
  `Validate_ShouldSurfaceR14Error_WhenASourceHasTwoDefaultFlows`; M2 `Validate_ShouldWarnR8_WhenAnOrGatewayFeedsTheJoinDirectly`;
  B2 `SetFlow_WithABlankKind_IsRefused` (null arm) + `SetFlow_AwayFromTheLastConditionalOnABranchingElement_IsRefused`;
  H1 `Validate_ShouldWarnR7_WhenADivergingGatewayHasAPlainFlow`; H2 `Validate_ShouldNotSurfaceR14_WhenAPlainSiblingLeadsIntoAGateway`;
  M6 `BuildGraph_WithAnOverlongFlowCondition_IsRefused`; D1 ordering
  `BuildProcess_ShouldResolveAConditionNamingAParameterAddedByTheDeclarativePhase`.
- **`SetFlowCondition` is under the source-element rules** — pinned by
  `SetFlowCondition_OutOfAParallelGateway_IsRefusedLikeSetFlow`.
- **Element-output expansion format** — `GetMetaPath()` read from platform source; the arm is not
  stand-verified for CONDITIONS specifically (run 2 used a process parameter), but it is the identical
  call `ProcessMappingService` makes for `sourceElement` mappings, which are stand-proven. Lens A added:
  user-task parameters are created with `ContainerUId = UId` (`ProcessSchemaActivity.cs:309-313`), event
  parameters likewise (`ProcessSchemaEvent.cs:201-208`), and **487 of 487** shipped element-output
  conditions carry exactly the emitted prefix+shape — so the write is byte-shape-correct.
- **Lens A's refutations, each with platform-source evidence:** no persisted field is lost by a re-kind in
  either direction (`WriteMetaData` of `MetaItem`, `BaseProcessSchemaElement`, `ProcessSchemaBaseElement`,
  `ProcessSchemaFlowElement`, `ProcessSchemaSequenceFlow` CI1–CI12, `ProcessSchemaConditionalFlow` GV2/GV3
  checked field by field); a non-null caption survives under its original resource key; element parameter
  UIds are stable across save (sync creates only name-missing parameters); the RefUId setter cannot throw
  during detach in the order used, and the reverse order would; nested or unterminated `[#` cannot loop or
  lose text; the new describe `name` reaches clio through `DescribedFlow`'s `[JsonExtensionData]`; handler
  ordering cannot shadow (every `CanBuild` is concrete-class or explicitly exclusive) and both handlers plus
  `SetFlowOperation` are pinned against the real container.

## The AGENTS.md sentences `[pending lens C for the contract/doc pass]`

- **MCP reviewed — update required.** `ModifyBusinessProcessPrompt.cs` must be rewritten (F0); the
  describe tool description should name the flow `name` field (F0c); e2e coverage for the changed
  `create-business-process` and `modify-business-process` contracts is owed (F3), and the validate binder
  e2e exists but has never been executed. Aligned: the three tool descriptions, the describe/validate
  prompts, `clio.tests`, the curated-knowledge fixture; no MCP Resource exists for these tools
  (`Resources/` grep clean); `clio/tpl/**` names no process tool (grep clean), so the drift oracle is
  unaffected. Guidance budget (from the bundle test's own oracle, STJ-exact): `process-modeling` at
  96.6 % (943 chars headroom), `perform-task` 94.5 %, `data-elements` 93.0 % — the previous gate's
  "98.8 % / 327" was not reproduced.
- **ClioRing compatibility reviewed, no Ring-consumed contract changed.** Inspected
  `clio-ring/ClioRing.Desktop/actions.json`, `clio-ring/ClioRing/`, `clio-ring/ClioRing.Ipc/` — none names
  `validate-process-graph`, `create-business-process`, `modify-business-process`,
  `describe-business-process`, `list-user-tasks` or `install-process-builder`, directly or as a nested
  `clio-run` command.
- **Docs for the two commands** — `create-business-process` and `modify-business-process` carry no
  `[Verb]` and are not wired in `Program.cs`: they are MCP-only, so `clio/help/en`, `clio/docs/commands`,
  `Commands.md` and `WikiAnchors.txt` have nothing to document (only `install-process-builder` has CLI
  docs). Their documentation surfaces are the tool `[Description]`s, the two prompts and
  `docs/McpCapabilityMap.md`, all of which this diff updates.

## Still to do in this review `[pending]`

1. Lens A (package correctness), Lens B (layout + validator), Lens C (contracts/guidance/MCP/security) —
   fold in their findings.
2. Execute the mutations for F1, F2, F4, F5, F6 and record RED/GREEN (package: full `dev-nf` suite per
   mutation; clio: `Module=ProcessModel|Module=McpServer` scope). Restore each file with `git checkout`,
   never `mv`, so the rebuild is not skipped.
3. Finalise the verdict.

## After this gate (unchanged from the brief)

Three pull requests, one per repository, into `main` (package) / `master` (clio, guidance). The clio PR
body must name package commit `bad9e2d7` as the producing commit of the 1.4.0.61 archive (with the
restamp `a82a779` on top), state that the branch carries the still-open #1288
(`docs/bp-manual-test-skills`, merge `709a12a21`) — `gh pr view 1288` reports `OPEN`, not on `master` —
and reference this file, the 2026-09-05 gate and both run reports.
