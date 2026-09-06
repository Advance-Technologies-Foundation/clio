# ENG-91853 — comprehensive review gate #2 (pre-PR), 2026-09-06

Run 2026-09-06 by a fresh session over the COMPLETE diff of all three repositories, per the brief in
[eng-91853-gateways-and-flows-pre-pr-review-prompt.md](../eng-91853-gateways-and-flows/eng-91853-gateways-and-flows-pre-pr-review-prompt.md)
and [AGENTS.md § Code review](../../AGENTS.md) gate 1. Four adversarial lenses (package correctness;
layout + clio validator; contracts / guidance / MCP / security; test coverage) plus the reviewer's own
verification: three suites reproduced, the bundled archive byte-compared against the package tip, platform
source read where a claim depended on it, corpus re-measured where a number did, and **six mutations
executed against the real code with a positive control on each pipeline**. The reviewer did not write the
code and implemented nothing — every item below is reported, not fixed.

| Repository | Path | Base | Tip reviewed | Commits |
|---|---|---|---|---|
| clio | `C:/Projects/clio` | `a9deb32bc` (`master`) | `e5ce93f11` | 42 |
| crt-process-builder | `C:/Projects/workspace/ProcessBuilder` | `7e93995` (`main`) | `a82a779` | 17 |
| clio-knowledge (worktree `.worktrees/eng-91853`) | `C:/Projects/clio-knowledge/.worktrees/eng-91853` | `84e2609` (`master`) | `c1a9e69` | 5 |

## Verdict

**One High must be resolved before the pull requests open; no Blocker.** The shipped
`modify-business-process` MCP prompt still describes the product from before this change — it says there
is no clear-condition operation and does not know `setFlow` (H1). Everything else is Medium or Low: two
correctness findings in the clio validator and the package write path where a shape is silently described
one way and runs another (M1, M2), three instruction surfaces that still describe the old product (M3–M5),
an AGENTS.md policy gap on e2e coverage (M6), and **five guards that are correct but unpinned — measured,
not asserted: every one of the five mutations left its whole suite green** (M7–M11).

**What the change gets right is proven, not argued.** Three suites reproduce the handoff exactly; the
archive clio ships is content-identical to the package tip and its restamp is committed; the layout
engine's every documented invariant held under a 20 000-graph fuzz of a faithful port; the previous gate's
two Blockers and six Highs are each pinned by a named test that a mutation reddens; and the element-output
expansion writes exactly the shape all 487 shipped element-output conditions carry.

## Evidence base — what was actually run

| Check | Result |
|---|---|
| clio targeted suite, `-c Release`, `Category=Unit&(Module=ProcessModel\|McpServer\|Command\|Common)` | **10 136 passed, 0 failed, 18 skipped** (6 m 45 s) — matches the handoff exactly |
| Package suite `dotnet test tests/CrtProcessBuilder -c dev-nf` (junction healthy on this host) | **1 237 passed, 0 failed** (7 s) — matches the brief exactly |
| clio-knowledge `automation/Clio.Knowledge.Bundle.Tests -c Release` on the guidance branch | **131 passed, 0 failed** |
| Bundled archive `clio/CrtProcessBuilder/CrtProcessBuilder.gz` decompressed (129 entries) and diffed against `git archive HEAD packages/CrtProcessBuilder` | **content-identical**; the only non-EOL difference is `CrtProcessBuilder.csproj.DotSettings`, which the compressor excludes. Five files differ in line endings only — the new files were written LF while `git archive` under `core.autocrlf=true` emits CRLF; every committed blob is LF |
| Producing-commit pin `bad9e2d7` vs package tip `a82a779` | ancestor; the ONLY commit after it is `a82a779 Restamp to 1.4.0.61`, touching `descriptor.json` alone. The archive's descriptor carries 1.4.0.61 / `/Date(1788659637000)/` = HEAD. The previous gate's B1 is closed |
| **Package mutation runs — 5 executed** (M7, M9, M10, M11 + positive control), each applied to the real source, rebuilt (`compiled=True` checked), full `dev-nf` suite, file restored with `git checkout` | **four GREEN at 1 237/0**; **positive control** (self-loop refusal disabled) **RED** — `AddFlow_SelfLoop_IsRefused` fails, so the pipeline recompiles and the greens are real |
| **clio mutation runs — 2 executed** (M8 + positive control) | M8 **GREEN 51/51** at the owning fixtures and **GREEN 4 806/0** across `Module=ProcessModel\|Module=McpServer` (6 m 43 s, same mutant binary); **positive control** (R15 disabled, rebuilt) **RED** — `Validate_ShouldSurfaceR15Error_ForASelfLoop` fails |
| Corpus re-measurement over `C:/Projects/PackageStore` (reviewer's own scan) | 1 061 non-empty conditions; 487 element-output, **242 of them with `[EntityColumn:`**, 245 without; 445 process-parameter-only; 344 empty/null — agrees with lens A to the unit |
| Lens B corpus scan (1 711 process schemas) | R14 both halves → **0** shipped false positives; R8 exclusive → **0**; R11 → 0; self-loops → 3 shipped (Error consistent with designer and package); conditional flows storing `""` → **0** (341 `null`, 3 absent) |
| Lens B layout fuzz — faithful Python port of `ProcessLayoutEngine`, 20 000 random graphs | 0 shared `(column, lane)` cells, 0 guard binds, 0 negative lanes, idempotent; 11 863 graphs exercised the late-arrival path |
| Platform-source reads | `GetMetaPath()` (`ProcessSchemaParameter.cs:1097`) emits `[Element:{ContainerUId}].[Parameter:{UId}]`; user-task parameters get `ContainerUId = UId` (`ProcessSchemaActivity.cs:312`), event parameters likewise; `FlowSchemaGenerator.cs:124-125` synthesizes a gateway only on `BpmnElementName == "CSF"`, and a default flow's name is the plain `SequenceFlowName` (`ProcessSchemaSequenceFlow.cs:284`); `ProcessInterpretationValidator.GetDefaultValidationRules` has no rule reading flow kinds |
| ClioRing consumer surface | `grep` over `clio-ring/` for every process-designer tool and nested command: **no hits**; positive control (`clio-run`, `list-environments`) found in `clio-ring/ClioRing/ViewModels/*.cs`; `actions.json` names `get-info`, `list-packages`, `restart-web-app`, `show-web-app-list` and the `clio-*` actions only |
| `scripts/check-knowledge-applies-to.py --base a9deb32bc` / `--dead-only` | 28 records touched (advisory); 1 dead path, **pre-existing** (`odata-write-transport-never-throws-on-non-2xx.md` → `ODataResponseError.cs`, absent at the base too); all eight new records' paths exist |
| Stand `Creatio` (`d_krestov_n.tscrm.com:40001`) | `ping` only (read-only). **Nothing else was run on the stand, per the owner's instruction during this session.** The e2e binder test `ValidateProcessGraph_Should_BindEdgeCondition_FromTheWire` therefore remains **never executed** |

---

## HIGH

### H1 — the shipped `modify-business-process` prompt says there is no clear-condition operation and does not know `setFlow`

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
opposite of a plain flow) or reports the edit impossible; an agent adding a branch on modify is steered to
addFlow-plain + setFlowCondition, the two-save window this ticket removed. One MCP server hands out three
answers to one question: the tool description (setFlow exists), the prompt (nothing exists), the guidance
(setFlow is the way).

**Rule:** AGENTS.md § MCP maintenance policy — *"If the command has an MCP prompt, keep the prompt
guidance aligned with the current tool contract"*; `Prompts/*.cs` is a required MCP target. Same class as
the previous gate's H6, fixed in guidance and not in the prompt.
`docs/knowledge/ProcessModel/conditional-flow-rekind-must-be-in-place.md` lists this prompt in its
`applies-to`, and the advisory check reported it as touched.

**Fix:** rewrite the op list and the condition paragraph to match `ModifyBusinessProcessTool.cs`
(setFlow; addFlow kind/condition; clearing = `setFlow kind:"sequence"`, refused when it would drop the
last conditional branch off a still-branching element).

---

## MEDIUM

### M1 — a `default` flow beside no conditional sibling is constructible in one step on both write paths, and it runs as a plain flow

**Where:** package `Graph/FlowKindRules.cs:146-159` (`EnsureAtMostOneDefault` is the only rule about
the `default` kind), `Graph/ProcessGraphBuilder.cs` `AddFlow` / `SetFlow`.

**Claim:** the package refuses REACHING the state "source has a default and other outgoings but no
conditional" by re-kinding away the last conditional (`WouldDropTheLastBranch`), yet lets a caller build
the identical end state directly, with no notice.

**Failure:** `Task1 → A` plain, then `{"op":"setFlow","source":"Task1","target":"B","kind":"default"}`
(or the same two flows on the build path) → written; `describe` reports `kind: "default"`. At run time
`FlowSchemaGenerator` sets `HasConditionalSequenceFlow` only from `BpmnElementName == "CSF"`
(`FlowSchemaGenerator.cs:124-125`), and a default flow's `BpmnElementName` is the plain `SequenceFlowName`
(`ProcessSchemaSequenceFlow.cs:284`), so `FillSequenceFlows` (`:144-166`) synthesizes no gateway and adds
both flows as plain — the `default` label decides NOTHING at run time. Which of the two flows then runs is
the source element's own affair, not the label's: **the browser leg measured a user task with two plain
outgoing flows taking ONE path** (run-2 report, "Finding 2"), so the unconditional "every outgoing flow is
taken" wording in R12 and in `FlowKindRules`' docblock is itself falsified for that source kind — either
way the flow described as "the branch taken when nothing matched" is not that. The platform's pre-save gate
has no rule that reads flow kinds (`ProcessInterpretationValidator.GetDefaultValidationRules`); clio's R14
sees it only when the agent runs the advisory plan check, and on modify nothing sees it. The designer offers
the same connection, so the package's "mirrors the designer" charter is met; the asymmetry with
`WouldDropTheLastBranch` is what makes it a finding. A notice (or refusal) when a `default` is written
beside no conditional on a source that has other outgoings is one rule; no test pins either direction today.

### M2 — the R7/R9 "no default flow" warning fires on a diverging or-gateway that has a plain flow, and states a run-time outcome that cannot happen

**Where:** clio `ProcessGraphValidator.cs:239-245` (`if (!hasDefault)`), against `:223-228` on the same
node. Found by lens B, confirmed against the platform source.

**Failure:** `g/exclusiveGateway` with `g→a conditional`, `g→b sequence` → TWO R7 findings on `g`:
*"has a plain sequence flow. At run time it is taken as the default branch"* (true) and *"has no default
flow: if no condition matches at run time the instance stops there and the process log reads 'None of the
conditions were met…'"* (false). `FlowConditionalGateway.cs:80-82,157-160` pre-adds every non-`"CSF"` flow
to the result set and `:95` pre-sets its result to `true`, so the "None of the conditions were met" path
(`ProcessComponentSet.cs:799-801`, `resultConditions.Count == 0`) is unreachable when a plain flow exists.
Measured: fires on all **7 shipped** diverging plain-flow or-gateways (`Compensation/BonusVisaBaseSubProcess`,
`…Compensation1`, `LeadFinance/LeadManagementFinance`, `OldGoogleIntegration/SynchronizeWithGoogleModuleProcess`,
`PRMBase/CreateOrUpdatePartnerParamHistory`, `CrtOpportunityManagement/Presentation780`,
`OpportunityBank/Presentation780Finance`), reachable by describe→validate (describe emits `kind:"sequence"`,
`buildType:"exclusivegateway"`, which `ManagerMap.ResolveDataId` resolves). This is the "agent fixes a
process that was never broken" class the knowledge record names, one severity down.

**Fix:** `if (!hasDefault && !outs.Any(o => o.FlowKind == ProcessFlowKind.Sequence))`. **Unpinned in either
direction:** nothing asserts the COUNT of R7 findings on the `[plain, conditional]` shape —
`ValidateProcessGraphToolTests.cs:223-242` asserts `Contain(plain msg)` + `HasErrors == false`; the
no-default tests use `[cond, cond]`; the inline `NotContain` uses `[cond, default]`.

### M3 — shipped guidance `process-naming` still says gateway elements and default flows are unbuildable, and it is on the critical path

**Where:** guidance `guidance/mcp/guides/processes/naming.md:121-129` (untouched on the branch), rule N10:
*"`flows[]` takes `source` and `target` and nothing else"*; *"build it plain, then `setFlowCondition`"*;
*"Only the LABEL is missing, along with default flows and gateway ELEMENTS (ENG-91853 extends that)"*.
All three false at `c1a9e69`. `process-modeling.md:8-9` tells every agent to read this article *"BEFORE
you name anything"*.

**Failure:** an agent following it never emits `kind: "default"` or a `parallelGateway` and builds every
branch in two saves — the previous gate's M4 one article over. Belongs inside the same 1.13.94 bump.

### M4 — the "88 % of real conditions are now buildable by name" claim counts 242 conditions the name form cannot express

**Where:** package `Formulas/ConditionParameterNames.cs:20-22` docblock; clio
`docs/knowledge/ProcessModel/build-path-conditions-name-their-parameters.md` (table row *"element output …
487 … no"*); `CreateBusinessProcessTool.cs` ("88% of real conditions name a parameter") and both
`[RequiresPackage]` comments; guidance `branch-conditions.md` (`[#Element.Parameter#]` only).

**Measured (reviewer's own scan, agreeing with lens A to the unit):** 1 061 non-empty conditions; 487
reference an element output, **242 of those also carry `[EntityColumn:`** — the three-segment *"a column of
the record the element returned"* form; 245 do not; 445 reference a process parameter only. By-name
expressible = (445 + 245) / 1 061 = **65 %**, not 88 %.

**Failure:** the natural spelling of the column form, `[#ReadContact.ResultEntity.Email#] != null`, has
three segments → `TryResolveBody` returns null → stored verbatim → the platform's pre-save gate refuses the
whole build with an unattributed formula error. A refusal, not a silent outcome, but the guidance names no
limit, and for 23 % of real conditions the caller is sent back to the two-step route the feature exists to
remove. The 3-segment pass-through is pinned (`ResolveOnBuild_LeavesAPlatformMacroAlone`); the CLAIM is what
is wrong. Fix: correct the number on every surface and document the column limit — or add an
`[#Element.Parameter.Column#]` arm.

### M5 — `SetFlow`'s authoritative contract docs say an omitted kind means `sequence`; the code refuses it

**Where:** package `Graph/IProcessGraphBuilder.cs` (`SetFlow` `<param name="kind">`: *"empty means
`sequence`"*) and `Contracts/ModifyContracts.cs:76-79` (*"For `addFlow` and `setFlow`: … Omitted means
`sequence`"*). `ProcessGraphBuilder.cs:415-420` refuses a blank kind on `SetFlow` (the previous gate's B2
fix). The interface doc is the authoritative contract under AGENTS.md's C# documentation policy.

**Failure:** a maintainer aligning the implementation to its own contract doc reintroduces the Blocker
(silent destruction of a conditional branch); a wire-contract reader omits `kind` on `setFlow` and gets a
refusal the contract says cannot happen. Fix: state the asymmetry — omitted means `sequence` on `addFlow`
and is refused on `setFlow`.

### M6 — no `clio.mcp.e2e` coverage for the changed build/modify tool contracts (AGENTS.md mandatory rule)

**Where:** `git diff --name-only a9deb32bc..e5ce93f11 -- clio.mcp.e2e/` lists only
`ValidateProcessGraphToolE2ETests.cs`. `CreateBusinessProcessToolE2ETests.cs` has zero occurrences of
`kind`, `gateway` or `condition`; `ModifyBusinessProcessToolE2ETests.cs` exercises `setFlowCondition` only
(pre-existing) — nothing sends `setFlow`, `addFlow.kind`, `flows[].kind`, `flows[].condition`,
`exclusiveGateway` or `parallelGateway` through the real MCP path.

**Rule:** AGENTS.md § MCP maintenance policy — *"Always add or update MCP end-to-end coverage in
clio.mcp.e2e for every new or changed MCP tool. This is mandatory even when the user does not mention E2E
coverage explicitly"*; the plan's own DoD line (`plan.md:384`) is unmet.

**What breaks:** nothing today — the two agent-mode stand runs exercised every one of these paths through
clio's real MCP server and read the results back from the stand. What is missing is the repeatable artefact
the policy asks for; a clio-side serializer change that dropped `kind` would ship green. Medium rather than
High because the process-designer e2e fixtures do not run in CI at all (`project-context.md`), so the gate is
manual either way — but the PR body's `MCP reviewed` sentence must state this gap rather than claim
completeness.

### M7 — `setFlow`'s blank-kind refusal is pinned for `null` only — **MUTATION CONFIRMED GREEN (1 237/0)**

**Where:** package `Graph/ProcessGraphBuilder.cs:415` `if (string.IsNullOrWhiteSpace(kind))`; test
`ProcessFlowKindTests.cs:752 SetFlow_WithABlankKind_IsRefused` passes `null` and nothing else sends `""` or
whitespace to `SetFlow`.

**Mutation executed:** `string.IsNullOrWhiteSpace(kind)` → `kind == null`. Suite green.

**Failure the mutant ships:** `{"op":"setFlow","source":"a","target":"b","kind":""}` against a conditional
flow off an element with no other outgoing → `ParseKind("")` = `sequence`, no notice, `WouldDropTheLastBranch`
false, `ReKindFlow` → plain class. The condition is gone and the call reports success — the B2 class minus
the parallel-split half. The guard is correct; the test is narrower than the guard.
**Fix:** `[TestCase(null)] [TestCase("")] [TestCase("   ")]`.

### M8 — the R7/R9 block's or-gateway type restriction is unpinned — **MUTATION CONFIRMED GREEN (51/51 owning fixtures; 4 806/0 across ProcessModel + McpServer)**

**Where:** clio `ProcessGraphValidator.cs:207`
`if (eventType is not (EventType.ExclusiveGateway or EventType.InclusiveGateway) || outs.Count <= 1) return;`

**Mutation executed:** drop the type half → `if (outs.Count <= 1) return;`. Suite green at both scopes.

**Failure the mutant ships:** two `R9` warnings ("has a plain sequence flow … say so with kind 'default'"
and "has no default flow") on every parallel fork and on every implicit split off an activity; an agent
following the first on a `parallelGateway` walks into an `R11` error and a package refusal.

**Why green:** no test in either validator fixture asserts a warning-free result (grep for
`Findings.Should().BeEmpty` over both: zero hits); every valid-graph test asserts `HasErrors == false` or
`NotContain(Error)`. **Fix:** one `Findings.Should().BeEmpty()` on the genuine AND fork/join graph closes
this class — and, with M2's fix, pins the R7 count on the `[plain, conditional]` shape too.

### M9 — the `setFlow` operation's length bound is the only bound on that path and nothing exercises it — **MUTATION CONFIRMED GREEN (1 237/0)**

**Where:** package `Operations/FlowOperations.cs:99-108`. `ProcessGraphBuilder.SetFlow` → `ApplyConditional`
applies no bound of its own; `AddFlow`'s bound (`:176`) is not on this path. Production call sites of
`EnsureStoredTextIsBounded` confirmed by grep: `FlowOperations.cs:105` (setFlow) and `:177`
(setFlowCondition) are the two operation-level bounds.

**Mutation executed:** the bound's `if` → `if (false)`. Suite green.

**Failure the mutant ships:** `setFlow kind=conditional` with unbounded text reaches the platform's macro
converters at the pre-save gate — the cost the previous gate's M6 named. `SetFlowCondition_ShouldRefuseAnOverlongCondition…`
covers the `setFlowCondition` token, `BuildGraph_WithAnOverlongFlowCondition_IsRefused` covers `AddFlow`;
no test sends `Op = setFlow` with a 2 049-character condition.

### M10 — MEDIUM-LOW — the event-based arm of `EnsureKindSuitsANonDecidingGateway` is exercised nowhere — **MUTATION CONFIRMED GREEN (1 237/0)**

**Where:** package `Graph/FlowKindRules.cs:165` `if (!IsNonDecisionalGateway(source) || kind == Sequence)`.
`ProcessSchemaEventBasedGateway` appears in exactly one test (`GatewayElementHandlerTests`, for `CanBuild`),
never as a flow source.

**Mutation executed:** `IsNonDecisionalGateway(source)` → `source is ProcessSchemaParallelGateway`. Suite green.

**Failure the mutant ships:** a conditional or default flow out of a designer-authored event-based gateway
(modify path) is written; `NormaliseForADecidingGateway` passes it through because the source is not
decisional — stored, never evaluated, the silent class the docblock says this rule exists to refuse.

### M11 — MEDIUM-LOW — `WouldDropTheLastBranch`'s kind guard is unpinned; the mutant turns a valid re-kind into a false refusal — **MUTATION CONFIRMED GREEN (1 237/0)**

**Where:** package `Graph/FlowKindRules.cs:84-86` `if (KindOf(flow) != FlowKinds.Conditional) return false;`

**Mutation executed:** those three lines deleted. Suite green.

**Failure the mutant ships:** `a → b default`, `a → c sequence` (the LeadDistribution shape the R14
exemption exists for), `setFlow a→b kind=sequence` → refused as "the last conditional branch leaving 'a'" —
a false message about a flow that was never conditional. Every `SetFlow(` call site in `ProcessFlowKindTests`
either re-kinds a conditional flow, has no siblings, or exits at the same-kind return.

---

## LOW

### L1 — stale statements on shipped and repository surfaces (each verified)

- `Operations/FlowOperations.cs:121-123` and `ProcessDesignConstants.cs:262-264` still say the build path
  *"cannot resolve"* a condition *"at graph-construction time"* — false since `ConditionParameterNames`.
- `CreateBusinessProcessTool.cs:118-120` and both command comments say *"below [1.4.0.60] the package
  refuses `flows[].kind` and the two gateway type tokens outright"* — .58/.59 accept them; only the name
  expansion is .60. A maintainer lowering the floor "because .58 accepts kind" would be arguing with a false
  comment.
- guidance `perform-task.md:106` — the one surviving *"1 522"* denominator.
- `docs/McpCapabilityMap.md:741` and guidance `process-modeling.md:150-152` list operations without
  `setFlow` while describing it elsewhere.
- `DescribeProcessTool.cs:35` never mentions the new flow `name` field; clio's `DescribedFlow` carries it
  only through `[JsonExtensionData]` and no test pins the flow-level bag.
- `ModifyBusinessProcessTool.cs:44-45` (`addFlow`) does not say a modify-path condition must be a UId
  meta-path; a name there costs one platform refusal round trip.
- `ProcessGraphValidator.cs` R13 comment: *"7 shipped flows are in that state"* — 0 shipped conditional
  flows store `""` (341 store `null`, 3 have no key, and `ProcessDescriber.ReadFlowCondition` maps all three
  to `null`), so the Error is unreachable from shipped content and the count conflates two measures.
- `ProcessLayoutEngine.cs:50-51` class doc *"a merge sits on the mean of the lanes reaching it"* is only
  true when no inbound branch skips — the case-B decision made a skipping branch's lane win.
- `spec/ai-business-process-generation/ai-bp-connection-rules.md:103-110` — the **errors** list still says
  *"a plain sequence flow out of a DIVERGING or-gateway (R7/R9 … 14 shipped gateways are the one-outgoing
  shape)"*, while the R7 bullet 70 lines above says both halves are **warnings** and names the 7 shipped
  diverging counter-examples; the code is a warning. The document disagrees with itself.
- `spec/sprint-status.yaml` story notes: story 2 still says the warning *"names MismatchItemsCountException"*,
  calls the plain-flow finding an *"error"*, and records *"floors 1.4.0.44 -> 1.4.0.58"*; story 3 ends with
  *"REMAINING: activity-connections.md …"*, fixed in guidance `6ea736c`. Statuses (`in-progress` ×3) are
  correct; the narrative is stale.
- `bundle-source.json`'s `process-modeling` description still counts "eight articles" / "nine" (carried
  over; lens C notes `process-modeling.md` does index exactly eight siblings, so the count is defensible).

### L2 — message and notice inaccuracies on the package write path (lens A, verified)

- `ProcessGraphBuilder.cs:426-439` — `NoticeIfNormalised` runs BEFORE the same-kind early return, so
  `setFlow kind=sequence` on a gateway whose only outgoing is already its default raises *"was written as
  'default' rather than the requested 'sequence'"* about a write that did not happen.
- `FlowKindRules.cs:197-200` — re-kinding the default branch ITSELF to `sequence` beside a conditional
  sibling computes `hasDefault` over siblings that exclude it and advises *"make this one the 'default'
  branch"* — which it already is. Right refusal, wrong sentence.
- `ConditionParameterNames.cs:186-191` — `[#Gateway.X#]` passes through (gateways are not
  `ProcessSchemaParametrizedFlowNode`) and surfaces as the platform's unattributed error, where a user-task
  head with a wrong tail gets the package's own refusal listing the parameters.
- `FlowKindRules.cs:113-119` — a designer-authored self-loop (3 in the corpus) cannot be re-kinded, and the
  refusal says *"cannot connect 'a' to itself"* about a flow that already does.
- Build path: `{source, target, condition}` with `kind` omitted is refused as *"A 'sequence' flow cannot
  carry a 'condition'"* — deliberate and pinned, but the tool contract marks `kind?` optional, so an agent
  will hit a message naming a kind it never wrote.

### L3 — validator edge cases (lens B, verified by reading)

- Exclusive gateway with `[default, sequence]`: R14 Error ("requires at least one sibling conditional")
  plus R7 ("say so explicitly with kind 'default'") — following R7 yields the two-defaults R14 Error; at run
  time BOTH non-CSF flows fire (`FlowConditionalGateway.cs:84-89,125-128` removes at most one). Not shipped
  for exclusive gateways.
- R8 and byte-identical duplicate edges: `HashSet<ProcessGraphEdge>` (record equality) collapses them, so
  `xor→and conditional` declared twice yields identical branch sets → no warning although the join needs two
  tokens. Planned-graph only — the package refuses duplicate pairs and the corpus has none.
- R8 is silent on `xor -c→A; xor -d→B; A→B; A→and; B→and` (A-branch `{xor→A}` ⊂ B-branch), although the
  join hangs when `xor` takes `B`. The "through all of them ⇒ not in conflict" justification holds only when
  every branch contains all edges. Warning-level, odd shape (R12 fires on `A`).
- An `Unknown` node type participates in every rule: a typo `endEvnt` yields UNKNOWN plus an R15 Error on
  every other node (pre-existing), and a misspelled or-gateway loses the new R14 exemption (Error on a
  source that merely points at it).
- `CheckSelfLoops`'s `e.Source != null` (`:298`) is unreachable — `CheckMissingNodeFlows` runs first and
  `ContainsKey(null)` throws; per `docs/knowledge/Tests/reachability-…` this is the delete case. The
  pre-existing `ToDictionary(group => group.Key)` in `Validate` throws on a null node name against
  `IProcessGraphValidator`'s "never throws"; the MCP tool's `try/catch` turns it into an error envelope.

### L4 — test-quality items (lens D, verified)

- `Validate_ShouldWarnR7_NamingTheRuntimeException_WhenADivergingGatewayHasNoDefault` performs a second Act
  inside its Assert block — the H3 pin hides inside another scenario's test.
- `ProcessLayoutEngineTests.cs:43` — *"It is set HERE because those handlers do not exist yet"*; they do.
- `Validate_ShouldNotSurfaceR7Error_ForALegacyConvergingGatewayWithOnePlainFlow` is named "Error" for a
  rule that is a warning.
- Merged AAA sections ("Act & Assert") in `GatewayElementHandlerTests.CanBuild_OwnClass_IsClaimedByExactlyOneHandler`,
  `Factory_ResolvesBothGatewayTokens`, `ProcessElementFactoryTests.ResolveBuildType_ShouldReturnNull_ForUnbuildableElement`.
- Local factory fixtures in `ProcessConditionalFlowTests` and `ProcessOperationExecutorGraphOpsTests` still
  omit the two gateway handlers (harmless today).
- Four `CarryOperatorState` fields (`Size`, `IsExpanded`, `CreatedInOwnerSchemaUId`, `OwnerSchemaManagerName`)
  are pinned by a text oracle only, never by value; the two gateway token literals are asserted nowhere as
  literals.
- `ProcessGraphValidator.cs` gained a UTF-8 BOM (base had none; its five siblings all carry one).

---

## Claims checked and REFUTED / confirmed pinned

- **"The bundled archive may not match the package tip"** — refuted by byte comparison.
- **"The described flow's new `name` field is unpinned"** — refuted: `ProcessFlowKindTests.cs:867`.
- **"Element-parameter `GetMetaPath()` may emit the bare form because `ContainerUId` is unset"** — refuted:
  `ContainerUId = UId` on creation (`ProcessSchemaActivity.cs:312`, `ProcessSchemaEvent.cs:206`); **487 of
  487** shipped element-output conditions carry exactly the emitted prefix+shape.
- **"A persisted field is lost by a re-kind in some direction"** — refuted field by field against
  `WriteMetaData` of every class in the chain (MetaItem A3/A4/A5, BaseProcessSchemaElement, ProcessSchemaBaseElement
  BL3–BL9, ProcessSchemaFlowElement BN1/BN2, ProcessSchemaSequenceFlow CI1–CI12, ProcessSchemaConditionalFlow
  GV2/GV3); a non-null caption survives under its original resource key.
- **"Element parameter UIds are regenerated on save"** — refuted (sync creates only name-missing parameters).
- **"The RefUId setter can throw during detach"** — refuted for both call sites; the reverse order would.
- **"Nested or unterminated `[#` can loop or lose text"** — refuted by trace (pinned).
- **"Case-only duplicate names make the case-insensitive resolution pick the wrong one"** — refuted (both
  duplicate checks are OrdinalIgnoreCase).
- **"R14 / R8 (exclusive) raise false positives on shipped content"** — refuted: 0 / 0 over 1 711 schemas.
- **"A layout invariant can be violated"** — refuted: 20 000-graph fuzz, no shared cell, no guard bind, no
  negative lane, idempotent; every `PinTerminalColumns` inversion degrades without throwing.
- **"Surfaces still name `MismatchItemsCountException` / the `1.4.0.44` floor / 'Gateways are not
  buildable'"** — refuted for user-facing surfaces (history only), except `naming.md` (M3).
- **Previous gate's "process-modeling at 98.8 % / 327 headroom"** — not reproduced: STJ-exact emulation of
  the bundle test's oracle gives **96.6 % / 943 chars**; `perform-task` 94.5 %, `data-elements` 93.0 %.
- **Previous gate's P1–P8, H3, M1, M2, B2, H1, H2, M6, M7 — each confirmed pinned by a named test:**
  P1 `Apply_BranchPreferringAClaimedCorridor_TakesTheNextFreeLaneInstead`; P2 `Apply_MergeWhoseLaneAndTheOneBelowAreTaken_MovesUpNotDown`;
  P3 `RemoveElement_ShouldDetachTheFlowFromTheSurvivingTarget` + `RemoveFlow_DetachesTheFlowFromBothEndpoints`;
  P4 `SetFlow_NormalisedKind_RaisesANotice`; P6 `AddFlow_OutOfAnInclusiveGateway_FollowsTheDecidingGatewayRules`;
  P7 `Describe_ShouldReadTheFlowKindFromTheClrType_NotTheEnum`; P8 `AddFlow_WithAPaddedMixedCaseKind_IsAccepted`;
  H3 second half of `Validate_ShouldWarnR7_NamingTheRuntimeException_WhenADivergingGatewayHasNoDefault`;
  M1 `Validate_ShouldSurfaceR14Error_WhenASourceHasTwoDefaultFlows`; M2 `Validate_ShouldWarnR8_WhenAnOrGatewayFeedsTheJoinDirectly`;
  B2 `SetFlow_WithABlankKind_IsRefused` (null arm) + `SetFlow_AwayFromTheLastConditionalOnABranchingElement_IsRefused`;
  H1 `Validate_ShouldWarnR7_WhenADivergingGatewayHasAPlainFlow`; H2 `Validate_ShouldNotSurfaceR14_WhenAPlainSiblingLeadsIntoAGateway`;
  M6 `BuildGraph_WithAnOverlongFlowCondition_IsRefused`; D1 ordering
  `BuildProcess_ShouldResolveAConditionNamingAParameterAddedByTheDeclarativePhase`;
  `SetFlowCondition_OutOfAParallelGateway_IsRefusedLikeSetFlow` (the old bypass is closed).

## The AGENTS.md sentences

- **MCP reviewed — update required.** `ModifyBusinessProcessPrompt.cs` must be rewritten (H1); e2e
  coverage for the changed `create-business-process` and `modify-business-process` contracts is owed (M6),
  and the validate binder e2e exists but has never been executed; the describe tool description should
  name the flow `name` field (L1). Aligned: the three tool descriptions, the describe/validate prompts,
  `clio.tests`, the curated-knowledge fixture (`1.13.94` / `1013094000`, derived by
  `BundleBuilder.DeriveSequence` from the version — `bundle-source.json` has no `sequence` field); no MCP
  Resource exists for these tools (`Resources/` grep clean); `clio/tpl/**` names no process tool (grep
  clean), so the drift oracle is unaffected.
- **ClioRing compatibility reviewed, no Ring-consumed contract changed.** Inspected
  `clio-ring/ClioRing.Ipc/*.cs` (`clio-run` dispatches only `deploy-creatio` and `describe-environment`, plus
  `list-environments`, `get-tool-contract`, `mcp-server`), `clio-ring/ClioRing/*.cs`, and
  `clio-ring/ClioRing.Desktop/actions.json` — none names `validate-process-graph`,
  `create-business-process`, `modify-business-process`, `describe-business-process`, `list-user-tasks` or
  `install-process-builder`, directly or nested.
- **Docs reviewed, no CLI update required.** `create-business-process` and `modify-business-process` carry
  no `[Verb]` and none of their options types appears in `Program.cs`: they are MCP-only and not
  `[FeatureToggle]`-gated, so `clio/help/en`, `clio/docs/commands`, `Commands.md` and `WikiAnchors.txt` owe
  nothing (`install-process-builder`, the family's only CLI verb, is documented and unaffected). Their
  documentation surfaces are the tool descriptions, the prompts, `docs/McpCapabilityMap.md` and the guidance
  — which is where H1, M3 and L1 live.

## Reconciliation with the browser leg, which ran while this gate was in progress

The owner ran `/bp-test-run ENG-91853 --mode browser` in parallel and appended it to the run-2 report
(`…-manual-test-run-2026-09-06b.md`, from "Browser leg — 2026-09-06"). It was not an input to this gate and
nothing here was run on the stand; it is read here only to keep the two documents consistent:

- **Runtime is verified and passes for every case that declares it** — a build-path condition written as
  a name expands AND evaluates; exclusive gateways choose one branch; default branches are taken when nothing
  matches; parallel joins wait for every arm (two and three); three routes converge on a shared step that
  runs once. This closes the "not verified" the brief listed as open, on the runtime side.
- **Design time passes TC-01 and FAILS TC-08**: a back edge is drawn on top of the forward flow as a
  double-headed straight line. That is a CrtProcessBuilder layout finding (connector routing is delegated
  to the platform's router; placement is correct) and is the owner's to triage — it is not in this gate's
  list because it was not observable from code or storage, which is precisely what the browser leg is for.
- **Finding 2 there (a user task with two plain flows took ONE path) qualifies M1 above and contradicts
  the unconditional wording of clio's R12 and `FlowKindRules`' docblock** ("EVERY outgoing flow is taken").
  The generator-level statement in M1 stands (no gateway is synthesized, both flows are added); the
  runtime fan-out is source-kind-dependent and belongs to the spawned R12 task, whose premise has to be
  corrected before it is implemented.

## After this gate

Resolve H1 before any PR opens; the Mediums are one-liners for the most part (five `TestCase`/`BeEmpty`
additions, three doc sentences, one condition in `CheckDefaultFlowRules`) and are worth taking in the same
round, since every fix of a Medium here was caught by a mutation the suite could not.

Three pull requests, one per repository, into `main` (package) / `master` (clio, guidance). The clio PR body
must name package commit `bad9e2d7` as the producing commit of the 1.4.0.61 archive (with the restamp
`a82a779` on top), state that the branch carries the still-open #1288 (`docs/bp-manual-test-skills`, merge
`709a12a21` — `gh pr view 1288` reports `OPEN`, not on `master`), and reference this file, the 2026-09-05
gate and both run reports. The browser leg (`/bp-test-run ENG-91853 --mode browser --env Creatio`) remains
outstanding and was deliberately not touched here.
