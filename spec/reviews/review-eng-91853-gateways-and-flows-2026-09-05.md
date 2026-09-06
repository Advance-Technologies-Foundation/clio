# ENG-91853 — comprehensive review gate (pre-PR)

Run 2026-09-05 by the verification session over the COMPLETE diff of all three repositories, per
[AGENTS.md § Code review](../../AGENTS.md) gate 1. Six adversarial lenses plus the reviewer's own
verification: corpus measurement, platform-source reads, and **executed mutation runs**.

| Repository | Branch | Base | Commits |
|---|---|---|---|
| `crt-process-builder` `C:/Projects/workspace/ProcessBuilder` | `feature/ENG-91853-gateways-and-flows` | `7e93995` (`main`) | 6 |
| `clio` | `feature/ENG-91853-gateways-and-flows` | `a9deb32bc` (`master`) | 11 |
| `clio-knowledge` (worktree `.worktrees/eng-91853`) | `feature/ENG-91853-gateways-and-flows` | `84e2609` (`master`) | 1 |

## Verdict

**Do not open the pull requests yet.** Two Blockers, six High findings, and **nine guards that no test
can falsify** (measured, not asserted: 11 mutations were applied to the real source, rebuilt and re-run).

**What the change gets right is now proven on a live stand, not argued.** V2-V9 pass: the serialization
quadruple matches the shipped corpus field for field including the default-flow manager item that was
missing; branch precedence really is array order, demonstrated by swapping it and watching the outcome
swap; a parallel join really waits for both branches; describe round-trips byte-identically including
positions; a back-edge no longer collapses the layout; and nothing needs a compile. The layout rewrite,
the in-place re-kind and the R8 deadlock algorithm also held up under adversarial reading.

What blocks the PRs is narrower: three defects reintroduce the exact failure class the ticket exists to
remove, one of them is silent and destructive, and the version bump that the whole release depends on
is sitting uncommitted.

## Evidence base — what was actually run

| Check | Result |
|---|---|
| clio targeted suite, `Category=Unit&(Module=ProcessModel\|McpServer\|Command\|Common)`, `-c Release` | **10134 passed, 0 failed, 18 skipped** (6 m 43 s) — matches the handoff |
| clio mutation runs (3 executed, each rebuilt and re-run) | 1 **refuted** a reported High finding, 2 **confirmed** unfalsifiable guards at full 4806-test scope |
| Corpus scan of `C:/Projects/PackageStore` for R14 and R7/R9 false positives | 11 844 metadata files parsed; results below |
| Platform source read (`ProcessSchemaFlowNode.GetOutgoingsDefFlows`) | verbatim, before the tree went offline |
| **Package suite** `dotnet test tests/CrtProcessBuilder -c dev-nf` | **1213 passed, 0 failed** — the handoff baseline reproduced, after working around the environment (below) |
| **Package mutation runs (8 executed, each rebuilt and re-run)** | **7 confirmed** unfalsifiable, **1 refuted** |
| **Stand verification V1-V9** on a live stand (CrtProcessBuilder 1.4.0.58) | **7 PASS, 1 PASS-with-caveat, 1 left to the user's eyes** — see the last section |

### The package suite: why it first could not run, and how it was unblocked

`SetupDevEnv` (a full Creatio core reinstall) ran on this host during the review and deleted
`C:/Projects/Creatio/TSBpm/Src/Lib`. The test project reaches the platform assemblies through a Windows
**junction** — `.application/net-framework/core-bin` → `.../Terrasoft.WebApp/bin` — so the build failed
with:

```
CSC : error CS1705: Assembly 'UnitTest' ... uses 'Terrasoft.Core, Version=8.3.2.4166' which has a higher
version than referenced assembly 'Terrasoft.Core' with identity 'Terrasoft.Core, Version=1.0.0.0'
```

This is a **third build trap of the same family as the two in the handoff**: it reads as a broken
checkout and is not one. `.application` is gitignored and untracked; the branch's last commit predates
the core replacement. **Nothing in the change causes it.**

The reinstall put the core back at a **new** path — `C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib`
(`Terrasoft.Core` 10.1.37.0) — which leaves the repo's junction dangling. The suite was unblocked
**without touching the checkout or the Projects tree**, by staging a core-lib shim in the scratchpad and
overriding both path properties on the command line:

```
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf   -p:TestCoreLibPath=<scratch>/shim/core-bin -p:CoreLibPath=<scratch>/shim/core-bin
```

The shim is the new core's `bin` plus three assemblies it does not carry:
`Terrasoft.Configuration.dll` (from `.application/net-framework/bin`), `System.Net.Http.Json` 8.0.1, and
**`System.Text.Json` 8.0.0.5** — the last one matters: with the core's own 8.0.0.0 the suite reports
**234 failures**, all one `FileNotFoundException`, which is an artefact of the shim and not a result.
With 8.0.0.5 in place the suite is **1213 passed / 0 failed**, exactly the handoff's baseline and
exactly its test count, so the mutation runs below are measured against a reproduced baseline.

**Both overrides are needed**: the test project reads `TestCoreLibPath`, the package project reads
`CoreLibPath`, and both default to the dead junction. Worth recording — it is the cheapest way back to a
green package suite while the environment is in flux.

---

## BLOCKERS

### B1 — The version restamp that produced the bundled archive exists in no commit

**Where:** `C:/Projects/workspace/ProcessBuilder/packages/CrtProcessBuilder/descriptor.json`
(uncommitted working-tree change), against `clio/CrtProcessBuilder/CrtProcessBuilder.gz`.

The package branch's working tree carries an **uncommitted** edit:

```diff
-    "PackageVersion": "1.4.0.57",
+    "PackageVersion": "1.4.0.58",
-    "ModifiedOnUtc": "/Date(1788526445000)/",
+    "ModifiedOnUtc": "/Date(1788602649000)/",
```

Those are exactly the values inside the archive clio commits (verified by decompressing
`CrtProcessBuilder.gz`) and exactly what `clio.tests/Common/BundledProcessBuilderPackageTests.cs` pins.
Commit `571fbb1` — the pinned producing commit — still says `1.4.0.57`.

The pin naming a pre-restamp commit is **by design**; that file's own remarks settle it and it is not
the finding. The finding is the next sentence of those remarks:

> the next cut's clean-tree gate refuses a dirty tree, and the pinned `ModifiedOnUtc` would otherwise
> exist in no commit at all.

**What breaks:** merge the package PR as it stands and `main` records `1.4.0.58` nowhere. clio decides
an environment is behind by comparing the archive's version against the recorded one, so a later
rebundle from `main` re-cuts **1.4.0.57** — an unchanged version reaches new installs only, and nobody
who already has the package is ever offered gateways. Silent; it surfaces as "the feature just isn't
there".

**Fix:** commit the restamp on the package branch before opening its PR.

### B2 — `setFlow` silently destroys a conditional branch, and does it when `kind` is omitted

**Where:** `packages/CrtProcessBuilder/Files/src/cs/Graph/ProcessGraphBuilder.cs:396-431`,
`Graph/FlowKindRules.cs:74-77`, `Operations/FlowOperations.cs:99-113`.

`{"op":"setFlow","source":"Task1","target":"Reject"}` — no `kind`, no `condition` — against a flow that
is a conditional branch:

1. `SetFlowOperation.Apply` passes `operation.Kind` (null) straight through; it never requires it.
2. `FlowKindRules.ParseKind(null)` returns **`sequence`** — a blank kind means "plain", not "unspecified".
3. `NoticeIfNormalised` returns at its first line because `requestedKind` is blank — **no notice**.
4. `KindOf(flow) == "conditional" != "sequence"` → `ReKindFlow` replaces it with a plain
   `ProcessSchemaSequenceFlow`. **The condition is gone.** The call reports success.

If that was the last conditional flow leaving the element, `FlowSchemaGenerator.FillSequenceFlows` stops
synthesizing the exclusive gateway and **every** outgoing flow is taken — an exclusive choice becomes a
parallel split.

Not hypothetical. The sibling operation refuses the same end state and says why, citing a stand
measurement, in `FlowOperations.cs:154-167`:

> Do NOT remove the flow and add a plain one instead: if it is the last conditional flow leaving that
> element, the platform stops synthesizing the exclusive gateway and every outgoing flow is then taken.

and its comment: *"Measured on a stand: an approval path became unreachable for every input while
describe still reported kind:'sequence' on both flows, which reads exactly like the condition was
cleared as asked."*

The branch opens a route around that refusal. In-place re-kinding **does** preserve UId and array
position, so the "lands last" half of the hazard is genuinely fixed — but gateway synthesis depends only
on a conditional flow still being present, so that half applies in full. `grep` confirms **no**
last-conditional-flow guard exists anywhere in `FlowKindRules` or `ProcessGraphBuilder`.

**Fix (two parts, both needed):**

- refuse a blank `kind` on `setFlow` the way `SetFlowConditionOperation` refuses a blank `condition`
  (`ParseKind`'s blank→`sequence` default is right for `addFlow` and wrong for a re-kind);
- raise a notice — or refuse — when a re-kind away from `conditional` removes the last conditional flow
  leaving that element.

---

## HIGH

### H1 — The NEW R7/R9 error rejects at least 7 shipped or-gateways, and its justifying comment is false

**Where:** `clio/Command/ProcessModel/ProcessGraphValidator.cs:205-209`. **Found by this gate.**

The new rule raises an **Error** when a diverging or-gateway has any plain sequence flow. Its comment
justifies the arity scope with:

> 14 shipped exclusive gateways do carry a single plain sequence flow, **all of them with exactly ONE
> outgoing**, i.e. legacy converging gateways from an older designer

Measured over the corpus, that is not true. **Seven** or-gateways have out-degree > 1 *and* a plain
sequence flow, so all seven now raise an Error:

```
Compensation/BonusVisaBaseSubProcess                ExclusiveGateway2   [sequence, conditional]
Compensation/BonusVisaBaseSubProcessCompensation1   ExclusiveGateway2   [sequence, conditional]
CrtOpportunityManagement/Presentation780            InclusiveGateway1   [sequence, sequence]
LeadFinance/LeadManagementFinance                   ExclusiveGateway1   [sequence, conditional]
OldGoogleIntegration/SynchronizeWithGoogleModule..  ExclusiveGateway1   [conditional, sequence]
OpportunityBank/Presentation780Finance              InclusiveGateway1   [sequence, sequence]
PRMBase/CreateOrUpdatePartnerParamHistory           ExclusiveGateway1   [sequence, conditional]
```

Verified element-by-element on the first: `SequenceFlow4` is a plain `ProcessSchemaSequenceFlow`, `CI4`
absent, manager item `0d8351f6-...` — a genuine plain flow beside a conditional one.

These processes **run correctly**: at run time `FlowConditionalGateway.GetIsDefSequenceFlow` treats any
outgoing flow whose `BpmnElementName != "CSF"` as the default branch (README finding 3). The rule
elevates a *designer-palette* restriction to an Error about graphs the platform executes fine — which is
finding 6 of the ticket's own README, reintroduced in a new rule.

**Reachable how:** `describe-business-process` → `validate-process-graph` is the plan's own V7 round
trip, and describe's `flows[{source,target,kind}]` maps straight onto `edges` with `flow-kind`.

**Fix:** demote to Warning, or scope it the way R14 was scoped. Either way correct the comment — someone
will build on that count.

### H2 — R14's error still rejects a shipped process, in exactly the shape the platform implements

**Where:** `ProcessGraphValidator.cs:180`. Raised by the validator lens; **independently confirmed here.**

The arity fix narrowed the over-rejection from 45 gateways to **one**, not to zero. Corpus scan:

```
CrtLeadOppMgmtApp/LeadDistribution   ReadDataUserTask1 (ProcessSchemaUserTask)   out-degree 2
    DefaultSequenceFlow4   kind=default    CI4=1   BL7=573ed909-...  -> IntermediateCatchSignal1
    SequenceFlow1          kind=sequence   CI4=0   BL7=0d8351f6-...  -> ExclusiveGateway3
```

`hasDefault && !hasConditional && outs.Count > 1 && defaults.Count == 1` → **Error**.

That shape is not an accident. `Terrasoft.Core/Process/ProcessSchemaFlowNode.cs:107-123`
(`GetOutgoingsDefFlows`) has a branch written for it and nothing else:

```csharp
bool hasCondition = outgoings.Any(flow => flow.FlowType == Conditional);
foreach (ProcessSchemaSequenceFlow sequenceFlow in outgoings) {
    if (sequenceFlow.FlowType == Default) { yield return sequenceFlow; }
    if (!hasCondition && sequenceFlow.FlowType == Sequence && GetIsGateway(sequenceFlow.TargetRef)) {
        // ... yield the TARGET GATEWAY's default flows ...
    }
}
```

— "my own default, no conditional of my own, and a plain flow into a gateway". `Error` is documented as
"a rule violation the live designer would reject"; it does not.

**Fix:** exempt the case where a plain sibling targets a gateway (mirrors the platform line exactly), or
demote this half of R14 to Warning. Both existing R14 tests route the plain sibling to an *activity*, so
they stay green either way. Note the fix needs the node map threaded into `CheckDefaultFlowRules`, which
today receives only `(node, eventType, outs, findings)`.

### H3 — The R7/R9 `!hasDefault` guard is unfalsifiable — **measured**

**Where:** `ProcessGraphValidator.cs:214`.

**Mutation executed:** `if (!hasDefault)` → `if (true)`. **Result: green** — 56/56 in the four fixtures
that own this code, and **4804 passed / 0 failed** across the whole
`Category=Unit&(Module=ProcessModel|Module=McpServer)` scope (4806 tests).

Under the mutant the "add a default flow" warning fires on **every** diverging or-gateway — including the
canonical conditional+default shape the designer produces and this ticket's own guidance recommends.
That is the definition of a warning that fires on the common case, and no test would notice.

Every test containing a diverging or-gateway *with* a default asserts either `Contain(...)` or
`HasErrors == false` / `NotContain(Error)`; a spurious **warning** changes neither.

**Fix:** one assertion —
`Findings.Should().NotContain(f => f.RuleId == "R7" && f.NodeName == "split", because: ...)`.

### H4 — `create-business-process`'s contract never names the two gateway element tokens

**Where:** `clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs:30`.

```
elements[] ({name ..., type:startEvent|signalStart|endEvent|userTask|sendEmail (aliases readData/performTask), ...
```

That closed enumeration is the only list of accepted element types on any MCP surface, and it omits
`exclusiveGateway` and `parallelGateway` — the two tokens the whole `[RequiresPackage]` bump to 1.4.0.58
exists to guarantee. They appear only inside the *flows* paragraph, as prose about flow rules.

A careful agent reads that enumeration as closed and concludes gateways are not buildable. For an
exclusive split it will fall back to a conditional flow off an activity, which works. For a **parallel
split there is no fallback at all** — the platform synthesizes an exclusive gateway only — so the
headline half of this ticket becomes undiscoverable.

Mitigating: the factory's rejection lists supported tokens dynamically and matches case-insensitively, so
a guessed `exclusiveGateway` does work and a wrong guess is recoverable.

### H5 — The same description contradicts itself about building a condition

**Where:** `CreateBusinessProcessTool.cs:112`, against lines 88-96 of the same string.

Line 112 still says:

> (The floor is shared with modify-business-process, whose conditional-branch refusals carry the rest of
> it; **a condition cannot be built here**.)

Lines 88-96 of the same description say `flows[] ({source, target, kind?, condition?})` and *"Declare the
branch here rather than building the flow plain and setting its condition afterwards: the two-step route
saves the process once with a flow that does not yet branch."*

An agent handed both either falls back to the two-step route this ticket was written to eliminate, or
emits `condition` expecting a refusal that will not come. The same paragraph also leaves the
pre-existing rationale for `.44` sitting under a floor that now reads `1.4.0.58`, so the number is
stated with no reason attached.

### H6 — Shipped guidance says there is no clear-condition operation; this branch creates one

**Where:** `clio-knowledge .../guidance/mcp/guides/processes/branch-conditions.md:71-75`.

> That second point is why **there is no clear-condition operation** ... To make a branch unconditional,
> set its condition to `true` and leave the kind alone.

`setFlow kind="sequence"` is exactly a clear-condition operation, and the same article introduces
`setFlow` 42 lines earlier as the way to change a kind "in either direction". An agent asked to remove a
branch condition now has a documented-as-nonexistent route that produces B2's silent parallel split.

This is the guidance half of B2 and should be fixed with it.

---

## MEDIUM

### M1 — R14's `defaults.Count == 1` clause is unfalsifiable — **measured**

`ProcessGraphValidator.cs:180`. **Mutation executed:** drop `&& defaults.Count == 1`. **Green** — 56/56 in the owning fixtures and
4804/0 across the full ProcessModel+McpServer scope.
As written, a source with two defaults plus a plain flow and no conditional silently loses the
sibling-conditional diagnostic; nothing pins either direction. Fix: add
`NotContain(f => f.Message.Contains("sibling conditional"))` to
`Validate_ShouldSurfaceR14Error_WhenASourceHasTwoDefaultFlows`.

### M2 — R8 misses the commonest hand-authored deadlock

`ProcessGraphValidator.cs:311` — `TraverseBackwardEdges(edge.Source, incoming)` walks from the inbound
edge's **source** and never includes the inbound edge itself. So for

```
s -> xor(exclusive) ;  xor -conditional-> A ;  xor -default-> and(parallel) ;  A -> and ;  and -> e
```

the branch `xor->and` projects to the empty set at `xor`, is dropped by the `Count > 0` filter, no pair
forms, and **no warning is raised** — although the join can never fire whichever way the gateway goes.
Verified by reading. The proposed fix (seed `walked` with the inbound edge) was measured by one lens
against the whole corpus as adding only 3 warnings, all inclusive-gateway, all in `ProcessTests` — i.e.
exactly the inclusive over-warning the code already declares intentional.

### M3 — No `clio.mcp.e2e` coverage for the new wire-level `condition` — mandatory per AGENTS.md

`git diff --stat a9deb32bc HEAD -- clio.mcp.e2e/` is **empty**, while `ProcessGraphEdgeArg` gained
`[property: JsonPropertyName("condition")]`. Verified: unit tests construct the record **positionally in
C#** (`ValidateProcessGraphToolTests.cs:42`), so the JSON binder is never exercised; and the e2e helper
at `ValidateProcessGraphToolE2ETests.cs:244` builds `{source, target, flow-kind}` with no `condition`
key. The MCP SDK binder skips unmapped members silently, so a renamed property drops the value with
nothing going red. AGENTS.md: *"Always add or update MCP end-to-end coverage ... mandatory even when the
user does not mention E2E coverage explicitly."* The plan's own DoD carries this line unchecked.

### M4 — Shipped guidance `activity-connections.md` now contradicts the change on five points

Untouched by the guidance branch, and it is the article `branch-conditions.md:57` routes to for R1-R17:

- `:182-183` — "R4-R6, **R8** and R16 are semantic or **not yet enforced** — verify those yourself."
  R8 now ships as a warning (confirmed: `R8` is a new rule id on this branch).
- `:185-186` — "conditional flows ARE in that slice, **gateway ELEMENTS and default flows are not**."
  Both are buildable now.
- `R14` — "Default flow is legal only if >=1 conditional flow leaves the same element" — no longer what
  the validator enforces, and the builder itself produces a lone default off a gateway.
- R7 and R14 are missing the two new errors this branch adds.

The clio-side spec copy (`spec/ai-business-process-generation/ai-bp-connection-rules.md`) **was** updated;
only the shipped article was not, and only the shipped one reaches users. One line, inside the `1.13.94`
bump already made.

### M5 — `branch-conditions.md`'s if/else-out-of-a-gateway advice produces a hard refusal

`:52-56` — "Out of a GATEWAY element it is different — rule 1 above applies, and the plain flow is
written as a `default` one" — and `:64` "Give every branching element a plain sibling."
`FlowKindRules.NormaliseForADecidingGateway` normalises a plain flow **only when it is the gateway's only
outgoing flow** (`siblings.Count == 0`); a second one is refused and the whole `create-business-process`
aborts. The article's own precedence advice ("Add the most specific FIRST") pushes the ordering that
fails. Recoverable — the refusal names the fix — but it costs a round trip.

### M6 — The build path applies no length bound to `flows[].condition`

All three modify-path operations bound it (`ProcessFormulaValidator.EnsureStoredTextIsBounded`); the
newly-opened build path does not. Before this change `BuildGraph` refused `condition` outright, so no
bound was needed. Exposure is 1 000 flows times unbounded text, handed to the platform's macro converters
at the pre-save gate — the exact cost the bound exists to prevent, per its own comment.

### M7 — A test now uses a *buildable* kind as its example of an unbuildable one

`tests/CrtProcessBuilder/ProcessElementFactoryTests.cs:173-179` (untouched by the branch):

```csharp
// Act + Assert — a gateway has no handler
CreateFactory().ResolveBuildType(new ProcessSchemaExclusiveGateway(CreateSchema())).Should().BeNull(
```

The comment is now false. The test stays green only because its local `CreateFactory()` (`:22-28`) omits
the two new handlers, so the tripwire no longer proves what it claims. Switch the subject to
`ProcessSchemaInclusiveGateway`.

### M8 — `docs/McpCapabilityMap.md` now states the opposite of shipped behaviour

Only line 741 (`modify-business-process`) was updated. Left stale:

- `:740` — "The buildable slice is ... joined by **plain sequence flows**... **A CONDITIONAL branch is
  not buildable here** — a non-sequence `flows[].kind` is still refused."
- `:743` — "enforced subset: R1-R3, R7, R9-R15, R17" (omits R8); `edges` given without `condition`.
- `:744` — describe's `flows [{source,target,kind}]` (missing `name`, `condition`,
  `branchesOnActivityResult`).

`docs/knowledge/McpServer/mcp-capability-map-has-no-automated-guard.md` — a record this diff touches —
predicts exactly this: one pinned sentence is not a guard on the document.

### M9 — `spec/sprint-status.yaml` story 3 understates the branch

Story 3 is `ready-for-dev`, but its work is committed: the guidance worktree carries `cd0602e`
(`libraryVersion` 1.13.94, both articles) and the curated-fixture re-pin landed on this branch as
`ed670759c`. BMAD requires `in-progress` once started. Stories 1 and 2 are correctly `in-progress`.

### M10 — `DescribeProcessPrompt` ships a corpus figure this ticket contradicts

`DescribeProcessPrompt.cs:45` — "337 of the **1 522** conditional flows". This ticket measures **1 406**
conditional flows; `1 522` is carried over from the ENG-95891 runbook. Three code comments in the package
repeat it (`ProcessGraphBuilder.cs:350`, `:420`, `ProcessConditionalFlowTests.cs`), while the two guidance
articles and `ExclusiveGatewayElementHandler.cs` use 1 406. One PR should not ship two denominators for
the same corpus.

---

## LOW / observations

- **Stray `//`** at the end of prose comment lines: `CreateBusinessProcessCommand.cs:61`,
  `ModifyBusinessProcessCommand.cs:69`. Reads as a botched merge, and it sits where a reviewer reads the
  floor rationale.
- **`BundledProcessBuilderPackageTests.cs`** provenance prose still names branch
  `feature/ENG-95891-formula-expressions`; `571fbb1` lives only on the ENG-91853 branch.
- **`Layout.VerticalStep`** (`ProcessDesignConstants.cs:937`) is now referenced only by its own doc
  comment. A dead constant kept so a comment can point at it.
- **`ModifyContracts.cs:67-95`** — `condition` still documented as "For `setFlowCondition`: ... Required
  and non-empty"; on `addFlow`/`setFlow` it is required only for `kind: conditional` and **refused**
  otherwise. `source`/`target` still scoped to `addFlow`/`removeFlow`.
- **The "alias" claim** — `ProcessDesignConstants.cs:268-274` and `ModifyContracts.cs:56` call
  `setFlowCondition` an alias of `setFlow`; they are not, and `SetFlowConditionOperation.Apply` silently
  discards a supplied `kind`.
- **`ProcessDescriptorContracts.cs:23-28`** — the `type` member's XML doc enumerates element tokens and
  was not extended with the two gateways, though the factory's rejection message was.
- **`bundle-source.json`** `process-modeling` description still says it routes to "eight" articles /
  "nine" items; the processes folder now declares eleven.
- **`process-modeling.md` is at 98.8 % of the guidance response-size budget** (~327 characters of
  headroom). It is the entry-point article every future capability must be announced in.
- **Knowledge-record scoping** — `flow-kind-is-four-fields.md`'s "the platform's writer ignores
  `VisualType`" is verified for `WriteMetaData` and `WriteUIData`, but `WriteUIPropertyData` does pass
  `VisualType.ToString()`. Nothing persisted changes, so the operative claim holds; the blanket phrasing
  does not.
- **`DivergesIntoTwoBranches`** mixes index spaces (`index` from the filtered sequence, `Skip` applied to
  the unfiltered list). Traced correct today — every unordered pair is still compared and the extras are
  self-comparisons or symmetric duplicates — but correct by accident.
- **`CheckSelfLoops`'s `e.Source != null`** is unreachable: `CheckMissingNodeFlows` runs first and
  `ContainsKey(null)` throws. It advertises a null-tolerance the validator does not have, against
  `IProcessGraphValidator`'s "Never throws on malformed input".
- **R8's message** says the instance "will hang"; inside a loop `FlowTokens` accumulates across visits and
  the join can eventually fire. "can hang" is accurate.
- **`ins.Count < 2 || orGateways.Count == 0`** (`:302`) is a pure fast path with no behavioural effect,
  three lines below a comment explaining that a filter no test could distinguish "is the shape of code
  that rots". Either annotate it as a deliberate fast path or drop it.
- **`AddSequenceFlow`** now has zero production call sites; it survives as a test-fixture entry point.

---

## Package-side mutation runs — EXECUTED, 7 of 8 confirmed

Baseline reproduced at **1213 passed / 0 failed**. Each mutation below was applied to the real source,
rebuilt, and re-run against the full suite; the tree was restored after each.

| # | Mutation | Result | What it means |
|---|---|---|---|
| **P1** | `FirstFreeSpanLane` → `=> preferred` | **1213/0 GREEN** | **The second unfalsifiable layout phase**, sibling of the corridor reservation caught in addendum §3 — written in the same commit and not pinned. The rule is live: it is what stops a branch taking a lane whose corridor is already claimed. |
| **P2** | delete `FindFreeLane`'s upward arm (`:358-361`) | **1213/0 GREEN** | The only rule in the engine that can place a node *above* its own lane, in tension with the class doc's stability argument, and nothing exercises it. |
| **P3** | delete `flow.TargetRefUId = Guid.Empty;` (`ProcessGraphBuilder.cs:602`) | **1213/0 GREEN** | Half the T-8 detach — the surviving **target**'s `Incomings` — is unpinned. The `Outgoings` half is pinned; this is the half added in `571fbb1`. Leaves a flow with `SourceRefUId == Guid.Empty` in a live `Incomings`, and `ProcessSchemaGenerator` dereferences `SourceRef` there. |
| **P4** | delete `NoticeIfNormalised(...)` from `SetFlow` (`:404`) | **1213/0 GREEN** | Only `AddFlow`'s notice is pinned. A normalised kind on `setFlow` can go silent — against the method's own "a successful edit is never silently different from what was requested". |
| **P5** | make `replacement.CreatedInSchemaUId = ...` unconditional (`:535`) | **1213/0 GREEN** | **The most consequential of the eight.** Writes `Guid.Empty` over the backfill, so every re-kinded flow reads `IsInherited == true`; `ProcessSchema.HideInheritedElement` is hardcoded `true`, so **the connector disappears from the designer canvas** while describe still reports it and the save reports success. The guard's own comment names this outcome exactly. |
| **P6** | `IsDecisionalGateway` → `element is ProcessSchemaExclusiveGateway` | **1213/0 GREEN** | The docblock says the base-class match exists **for** the designer-authored inclusive gateway on the modify path. Nothing tests that, so the deliberate part of the decision is unprotected. |
| **P7** | `KindOf` tests the enum before the class | **RED — 1 failed** | **REFUTED.** `Describe_ShouldReadTheFlowKindFromTheClrType_NotTheEnum` catches it. Class-first ordering is properly pinned. |
| **P8** | delete `.Trim().ToLowerInvariant()` (`FlowKindRules.cs:77`) | **1213/0 GREEN** | `"Conditional"` from an LLM caller would be refused as "Unknown flow kind". Low impact — a clear refusal, not a wrong outcome — but untested. |

**The pattern the handoff warned about held.** Three pieces of unfalsifiable code had already shipped in
this ticket before this gate ran. Seven more are listed above, plus two on the clio side (H3, M1) — nine
in total across the change. Every one of them is *correct code*; what is missing is any test that would
notice if it stopped being correct. P1, P3 and P5 are the ones worth a test before the PRs open.

## Claims raised and REFUTED by this gate

- **"The two `Count > 0` filters in `DivergesIntoTwoBranches` are unfalsifiable."** Reported as High.
  **Mutation executed** (both filters removed): **RED** —
  `Validate_ShouldNotWarnR8_ForAParallelSectionInsideARetryLoop` fails. The filters are pinned.
- **"`KindOf`'s class-first ordering is unfalsifiable."** **Mutation executed** (enum tested first):
  **RED** — `Describe_ShouldReadTheFlowKindFromTheClrType_NotTheEnum` fails.
- **"The package test failure means a broken checkout or a defect in the change."** It is a core
  reinstall that deleted the junction target. Confirmed by process inspection and by commit times, and
  settled for good by reproducing the 1213/0 baseline on the relocated core.
- The handoff's settled list (duplicate UId, dangling endpoint, layout performance, the `VisualType`
  literal, rule-id reuse) was re-checked where cheap and **not** re-litigated.

## The open owner decision — surfaced, not fixed

`layout-addendum §2` **case B** stands as written: for a column-skipping branch whose target has more
than one predecessor, the reserved corridor stays empty and the connector still crosses the other
branch's elements. Three of the ticket's commitments conflict for that shape, and connector routing is
out of scope. Option 2 in the addendum (a merge with a column-skipping inbound branch takes that branch's
lane) is the recorded recommendation. **This needs a decision from the owner, not a fix from a
reviewer** — and if the answer is option 1, `layout §4`'s verification table must lose row B's tick.

## Stand verification V1-V9 — EXECUTED

Stand: **`Creatio`** = `http://d_krestov_n.tscrm.com:40001` (the freshly built local one; the remote
`ts1-infr-web01:88/studioenu_15979382_0905` the user first named answers **HTTP 500** and is unusable).
Core 10.1.37, .NET Framework 4.8, MSSQL, `IsDemoMode: true` — demo mode did **not** block process
execution. `CrtProcessBuilder` **1.4.0.58** installed from `clio/bin/Release/net10.0` and confirmed by
`list-packages`. All schema writes were issued **sequentially**.

Processes could not be built into `CrtProcessBuilder` ("installed from the file archive" — locked); the
`Custom` package was used.

| # | Check | Result |
|---|---|---|
| **V1** | Opens in the designer with correct glyphs | **for the user** — `UsrEng91853V2` is built and waiting; glyph check is a browser judgement |
| **V2** | Byte-diff of the built metadata against capture §6 | **PASS** — see below |
| **V3** | It runs, and the right branch is taken; flip the input | **PASS** — `Amount=150` → `BIG branch` (conditional), `Amount=50` → `SMALL branch` (default) |
| **V4** | First-`true`-wins, and swapping `flows[]` swaps the outcome | **PASS** — see below |
| **V5** | No default + no match fails as documented | **PASS with a caveat** — see below |
| **V6** | A parallel gateway joins | **PASS** — `Split` → `Branch A` (done .736) and `Branch B` (done .755) → `Join` at **.762**, then `After join`. The join fired only after both branches completed |
| **V7** | `describe` round-trips | **PASS** — describe → create → describe is identical on every element field (including `managerItemUId` **and `position`**) and every flow field (`kind`, `condition`, `branchesOnActivityResult`) |
| **V8** | A retry loop lays out readably | **PASS (measurable half)** — see below; the visual judgement is the user's |
| **V9** | No compile needed | **PASS** — every build returned `"note":"compile-creatio not required"` and each process ran immediately without `compile-creatio` |

### V2 — every field matches the capture, including the one that was missing

Built `UsrEng91853V2` (start → exclusive gateway → conditional + default), pulled the package back with
`pull-pkg` and parsed `Schemas/UsrEng91853V2/metadata.json`:

```
Gw1   ProcessSchemaExclusiveGateway   BL7=bd9f7570-6c97-4f16-90e5-663a190c6c7c   BN2='55;55'

SequenceFlow_Start1_Gw1    sequence      BL7=0d8351f6-...  CI4=absent  CI5='FF939598'  CI6=1  CI3='null'
ConditionalFlow_Gw1_EndA   conditional   BL7=dac675d4-...  CI4=2       CI5='FF939598'  CI6=1  CI3='1 > 0'
DefaultFlow_Gw1_EndB       default       BL7=573ed909-...  CI4=1       CI5='FF939598'  CI6=1  CI3='null'
```

Every one of `BL7`, `CI4`, `CI5`, `CI6`, `BN2` matches
[capture §6](../eng-91853-gateways-and-flows/eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim).
The default flow is the **plain** class with `FlowType = Default` and the default-flow manager item —
i.e. `FlowManagerItems.Default`, the gap README finding 1 identified, is correct on a real stand.

### V4 — branch precedence is array order, proven by swapping it

Two conditional flows, **both conditions true**, same targets, differing only in declaration order:

| Declaration order | Element log says |
|---|---|
| `EndFirst` declared first | `Choose` → **`FIRST declared`** |
| `EndSecond` declared first (`UsrEng91853V4Sw`) | `Choose` → **`SWAP was-second`** |

The outcome followed the array position and nothing else. This confirms both halves at once: first-true-wins
at run time, and that the builder **preserves `flows[]` insertion order** — the DO-NOT rule holds in
practice, not just by inspection.

### V5 — it fails as designed, but the message clio promises is not the message the operator sees

`UsrEng91853V5` (two false conditions, no default) suspends, and `SysProcessLog.ErrorDescription` reads:

> None of the conditions were met after the element "Choose". The business process execution has been
> suspended and cannot continue. Possible causes — The conditional element in your business process does
> not have a default outgoing flow. — All outgoing flows of the branching element have conditions that
> evaluated to false.

The behaviour is exactly what R7/R9 warns about. **But the warning's own wording is** *"the process
instance fails with `MismatchItemsCountException`"*, and that string appears nowhere in what the operator
reads. On this core the platform surfaces a friendly, actionable message instead. Worth one edit to the
warning text — name the observable symptom, not an internal type nobody will find. (New finding; **Low**,
folded into the H1/M-series text edits.)

### V8 — the back-edge no longer starves the column assignment

`UsrEng91853V8` is a retry loop with a genuine back edge (`GwRetry --conditional--> GwMore`):

```
X=60   Y=185  Start1   startevent
X=240  Y=173  GwMore   exclusivegateway
X=420  Y=173  Work     usertask
X=600  Y=173  GwRetry  exclusivegateway
X=780  Y=185  End1     endevent
```

Five elements, **five distinct columns**, in flow order. That is the direct refutation of the defect
README finding 7 traces on the old engine — *"four of six elements in one column, because a back-edge
starves its Kahn queue"*. The 173/185 split is the gateway's 55x55 box against a 31x31 event, not two
lanes.

### Left on the stand

Six processes in the `Custom` package: `UsrEng91853V2`, `V3`, `V4`, `V4Sw`, `V5`, `V6`, `V7`, `V8`.
`V2` and `V8` are the ones to open for V1 and V8's visual half. Delete the rest at will.
