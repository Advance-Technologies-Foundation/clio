# ENG-91853 — Implementation plan

**Jira:** [ENG-91853](https://creatio.atlassian.net/browse/ENG-91853) · Task · component *bpms tools* ·
Major · status **HOME WORK** · reporter Yan Lypnytskyi · assignee Dmitro Krestov
**Ticket estimate:** ~2.5 days (originally ~5 d for all four gateways and the full layout work).
**Split out:** ENG-95889 (inclusive + event-based gateways), ENG-95890 (complex-process layout).
**Blocked by:** [ENG-95891](https://creatio.atlassian.net/browse/ENG-95891) — for S4 only.

Companion documents: [serialization-capture](eng-91853-gateways-and-flows-serialization-capture.md) ·
[platform-reference](eng-91853-gateways-and-flows-platform-reference.md) ·
[traps](eng-91853-gateways-and-flows-traps.md) ·
[layout](eng-91853-gateways-and-flows-layout.md) ·
[validator](eng-91853-gateways-and-flows-validator.md) ·
[test-plan](eng-91853-gateways-and-flows-test-plan.md)

---

## 0. Recommendation in one paragraph

Build two gateway element handlers and **one** flow-creation seam, and make that seam the single place
where a flow's class, `FlowType`, `ManagerItemUId` and `VisualType` are decided together — because those
four fields are read by four different consumers that each fail differently when one is wrong, and three
of them are wrong in the code we ship today. Do **not** model the default branch as a gateway property
(`DefaultUId` is unused in 1 099 packages), do **not** support the activity-result condition dialect
here (it is a documented second dialect that silently overrides the expression — its own ticket), and do
**not** implement R6 (it would reject 60+ shipped gateways). Fix R14 rather than extend it: as written it
calls 45 shipped gateways invalid. Rework the layout engine's Y axis into a lane model — its current
per-column stagger breaks on unequal branch lengths, which is the ticket's own basic case, and collapses
entirely on a retry loop, which is 14 % of real gateway processes. Add a `setFlow` operation so a branch
can be re-kinded in place, because remove-then-add silently changes branch **precedence**. The 2.5-day
estimate holds only if the scope decisions in §2 hold; the honest figure for the full list is **3.5
days**, and §6 shows what to drop to land on 2.5.

---

## 1. What exists, what is missing

| Capability | Today | After |
|---|---|---|
| `exclusiveGateway` / `parallelGateway` as buildable elements | rejected by `ProcessElementFactory` | two `IProcessElementHandler`s |
| `flows[].kind = conditional \| default` | `BuildGraph` throws `NotSupportedException` | built |
| `flows[].condition` | contract field, *"not consumed yet"* | built (via ENG-95891's validator) |
| Re-kinding an existing flow | not possible | `setFlow` operation |
| Removing a flow | `removeFlow` works, but silently picks one of several matches and leaves stale `Outgoings` | fixed (T-8, T-9) |
| Flow `ManagerItemUId` / `VisualType` | **never written** | written per kind |
| Branch-aware Y layout | per-column stagger; breaks on unequal branches and on loops | lane model |
| `describe` on a gateway process | elements listed with runtime type, `buildType: null`, flow `kind` from `FlowType`, **no condition** | `buildType` for both gateways, flow `condition` + `conditionKind` + `name` |
| clio R1–R17 | R14 over-fires; self-loop, one-default, or-gateway-flow-kind, condition-required, deadlock rules missing | fixed and extended |

Nothing needs to change in `CreateBusinessProcessCommand` / `ModifyBusinessProcessCommand`: they pass the
descriptor through as an opaque `JsonObject` (`CreateBusinessProcessCommand.cs:96-110`). All build gating
is server-side, which keeps this ticket concentrated in the `CrtProcessBuilder` package.

---

## 2. Decisions

### D1 — Two gateway handlers, not one handler with two tokens

`ProcessElementFactory.ResolveBuildType` returns `handler.SupportedTypes.FirstOrDefault()`
(`ProcessElementFactory.cs:75-77`). A single handler declaring `{exclusivegateway, parallelgateway}`
would therefore make `describe` report **`exclusivegateway` for a parallel gateway**. The multi-token
`UserTaskElementHandler` is fine because its tokens are aliases for one class plus a `userTaskName`;
gateways are two different classes. One handler per kind, per the package's own doctrine.

### D2 — One flow-creation seam, not a third strategy family

Replace `IProcessGraphBuilder.AddSequenceFlow(schema, source, target)` with
`AddFlow(schema, source, target, kind, condition)` over a single private `CreateFlowElement` switch:

```text
sequence     -> new ProcessSchemaSequenceFlow(schema, Sequence)     BL7 = SequenceFlowUId
conditional  -> new ProcessSchemaConditionalFlow(schema)            BL7 = ConditionalFlowUId   (FlowType set by ctor)
default      -> new ProcessSchemaSequenceFlow(schema, Default)      BL7 = DefFlowUId
every kind   -> VisualType = AutoPolyline ; Name = <kind prefix>_<source>_<target>
```

A third `IProcessOperation`-style strategy family would be over-engineering: the platform fixes the set
at exactly three kinds and has for a decade. The value of a **single** switch is that T-1, T-2, T-3, T-4
and T-10 are all fixed in one auditable place and cannot drift.

### D3 — `default` is a flow kind; `DefaultUId` stays unused

`ProcessSchemaExclusiveGateway.DefaultUId` (`BX1`) occurs **0 times** in 1 099 packages
([capture §2.2](eng-91853-gateways-and-flows-serialization-capture.md#22-the-keys-the-designer-writes-on-a-gateway)).
The package's existing `FlowKinds { sequence, conditional, default }` already matches the platform. No
contract change.

### D4 — No formula logic in this ticket

The condition expression, its validation (`IScriptSession.Validate`) and the parameter-usage scan belong
to ENG-95891. ENG-91853 calls that validation service and stores the string. If ENG-95891's seam is not
merged when S4 starts, S4 stores the condition **unvalidated behind the same call site** and the
validation lands with ENG-95891 — never the reverse (do not write a second validator).

### D5 — Add `setFlow`, because remove-then-add silently re-orders branches

Re-kinding a branch (promote to default, add a condition) is expressible today only as
`removeFlow` + `addFlow`. That changes the flow's **array position**, and array position **is** the
runtime evaluation order (`FlowSchema.cs:747-749`, `FlowConditionalGateway.cs:165-176`) — so a
"cosmetic" re-kind silently changes which branch wins. A `setFlow` operation
(`{source, target, kind?, condition?}`) mutates in place and preserves both the UId and the position.

New token in `ProcessDesignConstants.Operations` + one `AddScoped` line; the executor is untouched, and
`CrtProcessBuilderAppTests.CompositionRoot_RegistersEveryDocumentedOperation` catches a missed
registration.

### D6 — The activity-result condition dialect: read, do not write

`ProcessActivitiesSelectedResults` (`GV2`) is a documented Academy authoring route (*preset conditions —
task results*) used by **337** shipped conditional flows, and it **silently overrides** the expression
(`ProcessSchemaConditionalFlow.cs:214-231`). Supporting the write side means resolving activity result
UIds from the *Activity results* lookup and, for a user dialog, decoding the element's `Buttons`
parameter through `LocalizableParameterValuesList` (`:150-176`) — a chunk of work of its own.

Therefore:

- **write:** expression only; a `condition` write onto a flow with a non-empty `GV2` is **refused**,
  naming the activity-result branching (trap T-5);
- **read:** `describe` reports `conditionKind: expression | result | none` and, for `result`, the source
  activity and its selected result UIds — so a legacy process is never misreported as *"conditional with
  no condition"*;
- **follow-up ticket:** "Branch by activity result (Perform task / User dialog outcomes)", ~1 d,
  referencing this section.

### D7 — Lane-based layout, downward, stability over symmetry

See [layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm). The first-declared
branch keeps the parent lane and subsequent branches go **downward**, so (a) adding a branch does not
move existing ones — which matters because `ProcessModifyHandler` re-lays-out and re-saves on **every**
modify, and (b) top-to-bottom order equals runtime evaluation order, making the invisible precedence rule
readable off the diagram.

### D8 — R6 is not implemented

Rejecting a gateway that both converges and diverges would reject 60+ shipped processes (42 exclusive
gateways are 2-in/2-out). Record the non-decision in `ai-bp-connection-rules.md` so nobody "completes"
the rule set later. See [validator §3](eng-91853-gateways-and-flows-validator.md#3-r6-deliberately-not-implemented).

### D9 — Keep relayout-on-every-modify

Deterministic and idempotent beats clever. The trade-off — an AI edit re-flattens a hand-arranged diagram
— is recorded in the guidance article rather than papered over with a heuristic.
[layout §6](eng-91853-gateways-and-flows-layout.md#6-the-relayout-on-every-modify-question).

### D10 — `describe` reports storage truth, not inferred semantics

Flow `kind` comes from what is stored: the CLR type decides `conditional`, `FlowType` decides `default`.
The 14 legacy plain-sequence flows out of an exclusive gateway therefore read back as `sequence`, even
though the run time treats them as the else-branch (`FlowConditionalGateway.cs:80-83`). That asymmetry
goes in the **guidance article**, not into an invented `effectiveDefault` field — describe stays a
faithful read of the schema.

`DescribeProcessFlow` gains `condition`, `conditionKind` and `name` (the flow's schema `Name`, which is
what appears in process logs). All three additive.

### D11 — Measured geometry constants

`Layout.GatewaySizePx = 55` (corpus: `"55;55"` on every gateway that carries `BN2`) and
`Layout.BranchStep = 130` (corpus median branch separation 129 px). `VerticalStep = 90` is retained for
the collision fallback.

### D12 — The serialization fixes apply to new writes only; no migration

Adding `BL7` and `CI6` changes the bytes of every **future** built flow. Already-saved processes are not
migrated and keep working (both fields are designer-side; the run time reads neither). Consequence for
delivery: the bundled-package SHA-256 / `ModifiedOnUtc` pins in
`clio.tests/Common/BundledProcessBuilderPackageTests.cs` change, so the rebundle in S9 is mandatory, not
optional.

---

## 3. Work packages

### S0 — Baseline (0.1 d)

Read `docs/knowledge/` for the touched modules and `grep docs/knowledge/` for `ProcessSchemaSequenceFlow`,
`ProcessLayoutEngine`, `ProcessGraphValidator`. Branch `feature/ENG-91853-gateways-and-flows`. Confirm
ENG-95891's state, since it decides whether S4 runs now or is stubbed at the call site.

### S1 — Serialization fixes on the existing flow path (0.3 d) — *no dependency, do first*

`ProcessGraphBuilder` / the new `CreateFlowElement`:

- set `ManagerItemUId` per kind (T-1);
- set `VisualType = AutoPolyline` (T-2);
- add `ConditionalFlowNamePrefix` / `DefaultFlowNamePrefix` to `SchemaDefaults` (T-10);
- add `ProcessDesignConstants` entries for the three flow manager UIds and the two gateway UIds — from
  `ProcessSchemaElementManager`'s public statics where possible (they are `public static Guid`), so the
  compiler re-resolves them on a platform upgrade instead of a literal drifting.

A round-trip test asserting the `(class, FlowType, ManagerItemUId, VisualType)` quadruple per kind is the
acceptance gate. This package can ship independently of everything else and immediately improves every
existing built process.

### S2 — Gateway element handlers (0.3 d)

`ExclusiveGatewayElementHandler`, `ParallelGatewayElementHandler`; `ElementTypes.ExclusiveGateway = "exclusivegateway"`,
`ElementTypes.ParallelGateway = "parallelgateway"`; `DefaultSize => new Size(55, 55)`; `CanBuild` on the
concrete class; two `AddScoped` lines in `CrtProcessBuilderApp.Init()`. `IsLogging = true` comes free
from `ProcessElementFactory` (`:56-66`), matching the corpus. Update the factory's hand-written rejection
sentence (T-13 sibling; [validator §5](eng-91853-gateways-and-flows-validator.md#5-closing-the-validate-vs-build-fork-review-follow-up-6)).

### S3 — Flow kinds on the build and modify paths (0.5 d)

- `IProcessGraphBuilder.AddFlow(schema, source, target, kind, condition)`; delete the non-sequence
  rejection in `BuildGraph` (`ProcessGraphBuilder.cs:70-79`).
- Structural rules, server-side (mirroring the client errors in
  [validator §4](eng-91853-gateways-and-flows-validator.md#4-clientserver-parity-after-this-ticket)):
  self-loop refused; at most one default per source; a **diverging** or-gateway's outgoings must be
  conditional or default; a parallel/event-based gateway's outgoings must be plain sequence; a
  `conditional` flow must carry a condition; a diverging or-gateway asked for a single unconditional
  continuation is **normalised to a default flow**.
- `RemoveFlow`: clear `SourceRefUId` / `TargetRefUId` before removing (T-8); throw on an ambiguous match
  instead of `FirstOrDefault` (T-9).
- `AddFlowOperation` / `RemoveFlowOperation` gain the same guards; add `SetFlowOperation` (D5).
- Extend `ValidateStructure` and **rewrite its stale remark** (T-13). Add a retry-loop fixture: 14 % of
  real gateway processes have a back-edge and reachability must keep passing.

### S4 — Condition write path (0.3 d) — *depends on ENG-95891*

`flows[].condition` on build; `setFlow.condition` on modify; refusal when the target flow carries a
non-empty `GV2` (D6). Validation is ENG-95891's service; this package only calls it.

### S5 — Layout lane model (0.5 d)

[layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm). Pure class, no I/O, existing
test fixture — the cheapest quality win per line in the ticket after S1.

### S6 — `describe` read-back (0.3 d)

`DescribeProcessFlow` + `condition` / `conditionKind` / `name`; kind from CLR type for conditional (D10);
`GV2` decoded to `conditionKind: result` plus the source activity and result UIds; the two gateway
`buildType` tokens arrive free from S2's `CanBuild`.

### S7 — clio validator (0.4 d)

R14 arity scope (the fix); R15 self-loop; one-default-per-source; diverging-or-gateway flow kinds; the
optional `condition` field on `ProcessGraphEdgeArg` plus the condition-required error; the parallel-join
deadlock warning; the R7/R9 message rewrite. Plus the R6 non-decision recorded in
`ai-bp-connection-rules.md`. Module `Command` + `ProcessModel` →
`dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`.

### S8 — Documentation, MCP surface, guidance (0.4 d)

1. `ValidateProcessGraphTool` / `CreateBusinessProcessTool` / `ModifyBusinessProcessTool` /
   `DescribeProcessTool` `[Description]` — the new buildable slice, the gateway tokens, `flows[].kind`,
   `flows[].condition`, `setFlow`, and the three rules an agent must obey (one default per source;
   or-gateway outgoings are conditional/default; **flow order is evaluation order**).
2. Prompts: `CreateBusinessProcessPrompt`, `ModifyBusinessProcessPrompt`, `ValidateProcessGraphPrompt`,
   `DescribeProcessPrompt`.
3. `ProcessFlowDescriptor.Kind` / `.Condition` XML docs (currently *"not consumed yet"*).
4. `clio/docs/commands/*.md`, `clio/help/en/*.txt`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` for
   any command whose contract text changed.
5. `clio/tpl/workspace/AGENTS.md` and `clio/tpl/ui-project*/AGENTS.md` **only if** a workflow those
   templates describe changed — guarded by `WorkspaceTemplateGuidanceDriftTests`.
6. **`guidance name=process-modeling`: a pull request in the [clio-knowledge](https://github.com/Advance-Technologies-Foundation/clio-knowledge)
   repository**, not here — plus a `libraryVersion` + `sequence` bump and a re-pin of
   `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`. Content: the gateway/flow
   vocabulary, the evaluation-order rule, the run-time failure with no default branch, the two condition
   dialects, the R13 divergence, and the relayout-on-modify caveat.
7. `spec/backend-designer/backend-designer-manual-qa.md` — TC-C-05 and TC-D-01 invert.
8. `spec/process-design-service/task-list.md` — Task 15 status.
9. Two `docs/knowledge/` records (see §5).

### S9 — Delivery and verification (0.4 d)

- `dotnet build MainSolution.slnx -c dev-n8` in the ProcessBuilder workspace; unit tests there.
- **Rebundle:** `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <ProcessBuilder checkout>
  -Version X.Y.Z.W`. **The version must go up** — clio compares the shipped version against what the
  environment recorded, so an unchanged version reaches new installs only. Re-run
  `BundledProcessBuilderPackageTests` for the SHA-256 / `ModifiedOnUtc` pins (D12).
- **Trap:** an install command resolves the archive from the **build output** directory, so
  `clio compress -d <repo path>` has no effect until clio is rebuilt.
- `clio.mcp.e2e`: extend `CreateBusinessProcessToolE2ETests`, `ModifyBusinessProcessToolE2ETests`,
  `DescribeProcessToolE2ETests`, `ValidateProcessGraphToolE2ETests`; add `setFlow` to
  `ProcessDesignerContractRequiredArgsE2ETests`.
- Stand verification — see §4.

---

## 4. Stand verification (the part no unit test can cover)

Run schema-write operations **sequentially**: a parallel burst trips IIS rapid-fail and downs a
.NET Framework stand's application pool.

| # | Check | How |
|---|---|---|
| V1 | An exclusive split with a conditional + a default flow builds and **opens in the visual designer** with the correct glyphs (dashed default, diamond conditional) | build, then open the process in the designer |
| V2 | Byte-diff against the capture | export the built schema's `metadata.json`, diff the gateway and the three flow kinds against [capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim). `BL7`, `CI4`, `CI5`, `CI6`, `BN2` must match |
| V3 | It **runs**, and the right branch is taken | trigger the process, read `SysProcessLog` / `SysProcessElementLog`; flip the condition input and confirm the other branch |
| V4 | First-`true`-wins is real | two overlapping conditions; confirm the first-declared branch is taken, and that swapping `flows[]` order swaps the outcome |
| V5 | No default + no match fails as documented | expect `MismatchItemsCountException` in the process log |
| V6 | A parallel gateway joins | two branches, both must complete before the join's successor runs |
| V7 | `describe` round-trips | describe → feed the descriptor back into a new `create` → describe again → compare |
| V8 | A retry loop lays out readably | build the `DeleteFilesInTable` topology and open it |
| V9 | No compile needed | after build, the process runs without `compile-creatio`; do **not** infer a compile from a `VwSysProcess` dirty flag |

**The user verifies UI results in the browser themselves** — do not auto-open a browser after a
successful write.

---

## 5. Knowledge records (`docs/knowledge/`, same pull request)

Per the repository policy, write a record only where the code does not say it:

1. **`process-designer/flow-kind-is-four-fields.md`** — a flow's kind is the CLR class **and**
   `FlowType` **and** `ManagerItemUId` **and** `VisualType`; the run time reads only the class, the
   designer reads the rest, and each wrong field fails differently. `applies-to`: the graph builder, the
   flow constants, the layout engine.
2. **`process-designer/branch-precedence-is-array-order.md`** — sibling conditional flows are evaluated
   in insertion order, first `true` wins, nothing encodes it, Academy documents it nowhere; therefore
   `flows[]` order must never be re-sorted and the layout puts the first branch on the top lane.

Both are external/implicit facts whose failure is silent — exactly the policy's criterion. `make
check-knowledge` reports which records the diff touches.

---

## 6. Estimate

| Package | d |
|---|---|
| S0 baseline | 0.1 |
| S1 serialization fixes | 0.3 |
| S2 gateway handlers | 0.3 |
| S3 flow kinds + structural rules + removeFlow/setFlow | 0.5 |
| S4 condition write path | 0.3 |
| S5 layout lane model | 0.5 |
| S6 describe | 0.3 |
| S7 clio validator | 0.4 |
| S8 docs / MCP / guidance | 0.4 |
| S9 delivery + stand verification | 0.4 |
| **Total** | **3.5** |

The ticket says **2.5 d**. The gap is real, not padding: the ticket's own deliverable list did not
anticipate the three existing serialization defects (S1), the R14 correction, or the three new validator
rules. To land on **2.5 d**, drop in this order:

1. the parallel-join deadlock warning (S7) — **0.15 d**, promised in the rules spec but not by this
   ticket;
2. the `condition` field on `ProcessGraphEdgeArg` (S7) — **0.1 d**, the server-side guard is the
   load-bearing half and stays;
3. `describe`'s `GV2` decoding (S6) — **0.2 d**, but then a legacy result-branching flow reads back as
   *conditional with no condition*, which is exactly the misreport D6 exists to prevent. **Not
   recommended.**
4. the layout lane model (S5) — **0.5 d**. This buys the most time and costs the most: it is the ticket's
   third named deliverable, and without it the ticket's own basic case draws flows through elements
   ([layout §2 case B](eng-91853-gateways-and-flows-layout.md#b-split-with-merge-unequal-branch-lengths--overlap)).
   **Only if the ticket is explicitly re-scoped, with the layout moved to ENG-95890.**

Recommendation: **3.0 d** — drop 1 and 2, keep everything else. Or **3.5 d** and note that S1 repays
itself on every process already built.

---

## 7. Sequencing and the ENG-95891 dependency

```text
S0 ─┬─ S1 ──┬─ S2 ── S3 ──┬─ S4*  ──┐
    │       │             │         │
    ├─ S5 ──┘             ├─ S6 ────┼── S8 ── S9
    └─ S7 ────────────────┘         │
                                    └─ (* only S4 needs ENG-95891)
```

S1, S5 and S7 are independent of everything and of each other — start with S1 (it improves shipped
behaviour immediately) and S5 (pure, isolated, fully unit-testable). S4 is the only package that touches
ENG-95891's surface; if it is not merged, implement S4's call site against the interface and let the
validation arrive with ENG-95891.

---

## 8. Out of scope, stated explicitly

| Item | Owner |
|---|---|
| Inclusive (OR) and event-based gateways | ENG-95889 (the event-based one also forces a **compile** — `ProcessInterpretationValidator.cs:96-101`) |
| Nested branching, several merge points, long asymmetric chains | ENG-95890 |
| The formula expression itself: syntax, validation, parameter-usage scan | ENG-95891 |
| The **activity-result** condition dialect (write side) | new follow-up ticket (D6) |
| R6 gateway arity as a rule | not implemented (D8) |
| Polyline routing (`CI10`) | never — `AutoPolyline` hands it to the designer |
| Migrating already-saved processes to carry `BL7` / `CI6` | not done (D12) |
| Gap-healing on `removeElement` | Task 16 of the task list |

---

## 9. Definition of Done

- [ ] Both gateway kinds and all three flow kinds build, modify, save and **run** on a stand (V1–V9).
- [ ] The `(class, FlowType, ManagerItemUId, VisualType)` quadruple asserted per kind by a round-trip
      test, and the metadata byte-diffed against the capture (V2).
- [ ] `describe` reports gateways with a `buildType`, and flows with `kind`, `condition`, `conditionKind`
      and `name`; a `GV2` flow reports `conditionKind: result`.
- [ ] Layout: no overlap for one split level with **unequal** branch lengths, with and without a merge;
      a back-edge lays out left-to-right; idempotent; adding a branch does not move existing branches.
- [ ] R14 produces **no** finding for a converging or-gateway with a single default flow; the self-loop
      rule fires; the new rules fire; client and server agree on every error-severity rule.
- [ ] `removeFlow` refuses an ambiguous match and leaves no stale `Outgoings` / `Incomings`; `setFlow`
      preserves the flow's UId **and** its array position.
- [ ] Targeted tests green, with the filter recorded in the PR:
      `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`
      plus the ProcessBuilder package's own suite.
- [ ] `clio.mcp.e2e` extended for every changed tool (mandatory — mapping-only unit coverage does not
      complete an MCP change).
- [ ] **MCP reviewed** — statement in the PR description naming the tools, prompts and e2e files touched.
- [ ] **ClioRing compatibility reviewed, no Ring-consumed contract changed** — inspected
      `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, `clio-ring/ClioRing.Desktop/actions.json`; Ring's
      tool surface is `clio-deploy-creatio`, `clio-env-info`, `clio-import-iis-environments`,
      `clio-list-packages`, `clio-manage-envs`, `clio-restart`, `clio-uninstall-creatio`, `clio-version`
      — no process-designer tool and no `clio-run` nested process command.
- [ ] Docs and MCP descriptions updated (S8); the guidance article raised as a **clio-knowledge** pull
      request with a `libraryVersion` + `sequence` bump and the curated-names fixture re-pinned.
- [ ] Two `docs/knowledge/` records added (§5).
- [ ] Package rebundled with an **increased** `-Version`; `BundledProcessBuilderPackageTests` pins
      updated; clio rebuilt before any local install verification.
- [ ] Agentic code review: comprehensive fan-out before opening the PR and again before ready-to-merge;
      per-commit triage in between.
- [ ] `spec/sprint-status.yaml` moved `ready-for-dev` → `in-progress` → `review` → `done`; no unchecked
      DoD item at close.
