# ENG-91853 — Implementation plan

**Jira:** [ENG-91853](https://creatio.atlassian.net/browse/ENG-91853) · Task · component *bpms tools* ·
Major · status **To Do** · reporter Yan Lypnytskyi · assignee Dmitro Krestov
**Ticket estimate:** ~2.5 days. **Split out:** ENG-95889 (inclusive + event-based gateways),
ENG-95890 (complex-process layout). **Predecessor:** ENG-95891 — **merged in all three repositories**
(clio `master`, crt-process-builder `main`, clio-knowledge `master`).

Revised 2026-09-05 against the delivered ENG-95891. Companion documents:
[serialization-capture](eng-91853-gateways-and-flows-serialization-capture.md) ·
[platform-reference](eng-91853-gateways-and-flows-platform-reference.md) ·
[traps](eng-91853-gateways-and-flows-traps.md) ·
[layout](eng-91853-gateways-and-flows-layout.md) ·
[validator](eng-91853-gateways-and-flows-validator.md) ·
[test-plan](eng-91853-gateways-and-flows-test-plan.md)

---

## 0. Recommendation in one paragraph

ENG-95891 removed roughly a third of what this ticket was going to cost: the flow-serialization fields
are stamped, the ambiguous-endpoint refusal exists, the condition read-back exists, and the in-place
re-kind that preserves a flow's `UId` *and* its array index is written and reviewed — so the remaining
work is two gateway element handlers, a declarative `flows[].kind` path that reuses that re-kind rather
than forking it, a lane-based Y layout, six validator rules on the clio side, and the documentation tail.
Do **not** re-add a condition validator (an ADR deleted it after measuring that the platform's own
pre-save gate refuses a bad condition). Do **not** model the default branch as a gateway property
(`DefaultUId` is unused in 1 099 packages). Do **not** implement R6 (it would reject 60+ shipped
gateways), and **fix** R14 rather than extend it (as written it calls 45 shipped gateways invalid). The
layout engine is the one part with real algorithmic risk and is fully independent of everything else —
start it first or in parallel. Honest estimate **2.9 d**, which is close enough to the ticket's 2.5 that
no re-scoping conversation is needed.

---

## 1. What exists, what is missing

| Capability | Today | After |
|---|---|---|
| `exclusiveGateway` / `parallelGateway` as buildable elements | rejected by `ProcessElementFactory` | two `IProcessElementHandler`s |
| `flows[].kind = conditional \| default` | `BuildGraph` throws `NotSupportedException` | built |
| `flows[].condition` | refused outright, pointing at the two-step recipe | built |
| Conditional branch without a gateway | **works** — `setFlowCondition` (ENG-95891) | unchanged; the declarative path joins it |
| Promoting a flow to **default** | not possible at all | new — reusing the ENG-95891 re-kind |
| Flow `ManagerItemUId` / `VisualType` | **stamped** for sequence + conditional | plus `FlowManagerItems.Default` |
| Ambiguous `(source, target)` | **refused** (`FindTheFlowBetween`) | unchanged |
| `removeFlow` endpoint detach | **missing** (T-8) | fixed |
| Branch-aware Y layout | per-column stagger; breaks on unequal branches and on loops | lane model |
| `describe` on a gateway | listed with runtime type, `buildType: null` | `buildType` for both gateways; flow `name` added |
| `describe` flow condition | **`condition` + `branchesOnActivityResult`** | unchanged (prompt still to do) |
| clio R1–R17 | R14 over-fires; five rules missing | fixed and extended |
| `DescribeProcessPrompt` | reverted to master **on purpose** | introduces both new fields together |

`CreateBusinessProcessCommand` / `ModifyBusinessProcessCommand` need no change — they pass the descriptor
through as an opaque `JsonObject`. All build gating is server-side.

---

## 2. Decisions

### D1 — Two gateway handlers, not one handler with two tokens

`ProcessElementFactory.ResolveBuildType` returns `handler.SupportedTypes.FirstOrDefault()`, so a single
handler declaring `{exclusivegateway, parallelgateway}` would make `describe` report
**`exclusivegateway` for a parallel gateway**. The multi-token `UserTaskElementHandler` is fine because
its tokens are aliases for one class; gateways are two classes. One handler per kind.

### D2 — One flow-creation seam, and it reuses the ENG-95891 re-kind

Extend `IProcessGraphBuilder` with `AddFlow(schema, source, target, kind, condition)` over a single
private `CreateFlowElement` switch:

```text
sequence     -> new ProcessSchemaSequenceFlow(schema, Sequence)     BL7 = FlowManagerItems.Sequence
conditional  -> new ProcessSchemaConditionalFlow(schema)            BL7 = FlowManagerItems.Conditional
default      -> new ProcessSchemaSequenceFlow(schema, Default)      BL7 = FlowManagerItems.Default   (new)
every kind   -> VisualType = AutoPolyline ; Name = <kind prefix>_<source>_<target>
```

For **changing** an existing flow's kind, do **not** write a second clone: `SetFlowCondition` already
carries `UId`, the `FlowElements` index, `CreatedInSchemaUId`, a cloned caption, all six geometry fields
and the container state, and it documents why each one is there
([traps T-17](eng-91853-gateways-and-flows-traps.md#t-17--a-re-kind-that-regenerates-the-flow-or-moves-it-in-flowelements)).
Extract its clone body into a helper parameterised by target class + `FlowType` + manager item, and call
it from both. A forked copy will drift, and the fields it drops are all silent.

A third `IProcessOperation`-style strategy family would be over-engineering: the platform fixes the set at
three kinds and has for a decade.

### D3 — `default` is a flow kind; `DefaultUId` stays unused

`BX1` occurs **0 times** in 1 099 packages. The package's `FlowKinds { sequence, conditional, default }`
already matches. No contract change.

### D4 — No condition validation in this ticket, and none re-added

`spec/adr/adr-collapse-formula-validation-onto-platform-rule.md` deleted the package's formula validator
after measuring that `ParameterValuesValidationRule` runs the flow-schema generator, which builds the
synthetic Boolean `Source=Script` parameter from a flow condition — so the platform's pre-save gate
refuses a malformed condition on its own. The declarative `flows[].condition` path inherits that for free.
**Re-introducing a validator would re-litigate a measured decision.**

### D5 — How a flow's kind is changed after creation

`setFlowCondition` exists and covers *plain → conditional* plus overwriting a condition. Two gaps this
ticket must close, both reachable through gateways:

- **plain → default** (marking the else-branch of an existing split);
- **conditional → plain / default** (demoting a branch).

Recommendation: **extend `setFlowCondition` into `setFlow`** — `{source, target, kind?, condition?}` — with
`setFlowCondition` kept as an alias so nothing shipped breaks, rather than adding a third flow operation.
Rationale: the operations share `FindTheFlowBetween`, the clone helper and every refusal, and a caller
asking "make this the default branch" is doing the same thing as "give this branch a condition". Keep the
existing refusals (default branch cannot carry a condition; a result-branching flow refuses a condition)
and add the symmetric ones.

**Never implement a kind change as remove + add**: it regenerates the `UId` and appends the flow to
`FlowElements`, which silently moves it to last in evaluation order
([traps T-7](eng-91853-gateways-and-flows-traps.md#t-7--the-order-the-toolkit-inserts-flows-in-silently-decides-which-branch-wins)).

### D6 — The activity-result condition dialect: read, do not write

Unchanged from the first revision, and now half-delivered. ENG-95891 refuses a condition write onto such
a flow and reports `branchesOnActivityResult`. This ticket adds the **prompt** half (handed over by name)
and keeps the write side out. Follow-up ticket: *"Branch by activity result (Perform task / User dialog
outcomes)"*, ~1 d, referencing
[capture §3.6](eng-91853-gateways-and-flows-serialization-capture.md#36-gv2--the-activity-result-condition-dialect).

### D7 — Lane-based layout, downward, stability over symmetry

[layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm). The first-declared branch
keeps the parent lane; subsequent branches go **downward**, so adding a branch does not move existing
ones — which matters because the layout re-runs on every modify — and top-to-bottom order equals runtime
evaluation order.

### D8 — R6 is not implemented

Rejecting a gateway that both converges and diverges would reject 60+ shipped processes. Record the
non-decision in `ai-bp-connection-rules.md`.

### D9 — Keep relayout-on-every-modify

Deterministic and idempotent beats clever; the trade-off goes in the guidance article
([layout §6](eng-91853-gateways-and-flows-layout.md#6-the-relayout-on-every-modify-question)).

### D10 — `describe` reports storage truth

Already implemented the right way by ENG-95891 (`MapFlowKind` reads the CLR type). The 14 legacy plain
sequence flows out of an exclusive gateway therefore read back as `sequence` even though the run time
treats them as the else-branch; that asymmetry goes in the **guidance article**, not into an invented
`effectiveDefault` field. This ticket adds only the flow's `name` (it appears in process logs, so a reader
can correlate).

### D11 — Measured geometry constants

`Layout.GatewaySizePx = 55`, `Layout.BranchStep = 130`. `VerticalStep = 90` retained for the collision
fallback.

### D12 — No migration of already-saved processes

Adding `FlowManagerItems.Default` changes the bytes of **future** default flows only. Nothing existing is
rewritten, and both fields are designer-side.

### D13 — Branch, PR and session strategy

**Three repositories, therefore three pull requests** — that is forced, not chosen:

| Repository | Contents |
|---|---|
| `crt-process-builder` (`C:/Projects/workspace/ProcessBuilder`) | gateway handlers, flow kinds, structural rules, `setFlow`, layout, describe |
| `clio` | validator rules, MCP tool descriptions + prompts, docs, rebundle + pins, e2e |
| `clio-knowledge` | the `process-modeling` / `process-formulas` guidance article |

**Branch from each repository's own default branch — ENG-95891 is merged everywhere.** Verified
2026-09-05:

| Repository | Default branch | Merge commit |
|---|---|---|
| `clio` | **`master`** | contains `09898af82` |
| `crt-process-builder` | **`main`** — *not* `master` | `7e93995 Merge pull request #42` |
| `clio-knowledge` | **`master`** | `84e2609 Merge pull request #122` |

So there is **no stacked-branch problem and no rebase step**: everything this ticket consumes —
`FlowManagerItems`, `FindTheFlowBetween`, the `SetFlowCondition` clone body,
`DescribeProcessFlow.Condition` — is on the default branch. Branch name in each:
`feature/ENG-91853-gateways-and-flows`; each PR targets its own default branch.

Two checks before starting, because a stale local checkout is the likely trap here:

- the local `clio-knowledge` working copy was **216 commits behind** `origin/master` at the time of
  writing, and its `bundle-source.json` still read `libraryVersion 1.13.25` while clio's fixture pins
  **1.13.92**. Pull before touching guidance.
- the local `clio` and `ProcessBuilder` checkouts were both still sitting on
  `feature/ENG-95891-formula-expressions` with zero commits ahead of the default branch. Start the new
  branch from the freshly fetched default branch, not from where the working copy happens to be.

**One PR per repository, not more.** The server change is one coherent capability; splitting the layout
into its own PR would force a second rebundle, a second version bump and a second round of clio pin
refreshes for no reviewer benefit. Structure the *commits* so the layout is a self-contained series
(easy to review in isolation, easy to split later if a reviewer asks).

**Four sessions, not one.** The cut points are where the mental model changes, and each ends
compiling + unit-green:

| Session | Scope | Reads first |
|---|---|---|
| **A** — layout | S4 only. One production file, one test file. **Do this first or in parallel** — it is the only part with algorithmic risk, and it cannot conflict with anything else. | `layout` |
| **B** — server core | S1 + S2 + S3 + S5. Gateways, flow kinds, rules, describe. | `capture`, `platform-reference`, `traps` |
| **C** — clio | S6 + S7 + the rebundle half of S8. Validator, MCP surface, docs, pins. | `validator`, `traps` (T-14) |
| **D** — verification | the stand half of S8 (V1–V9), the guidance PR, the review gates, the three PRs. | `test-plan`, `plan §4` |

Sequencing: A ∥ B, then C, then D. One session cannot hold three repositories plus stand verification —
ENG-95891 ran to roughly sixty commits and thirty archive restamps on one branch, and its context did not
fit either.

---

## 3. Work packages

### S1 — The default-flow serialization triple (0.15 d)

`FlowManagerItems.Default` (`573ed909-e069-4161-b193-ae8dd9437c68`, currently prose-only in
`ProcessDesignConstants`, with the reason it was left out — remove that reason in the same edit);
`SchemaDefaults.ConditionalFlowNamePrefix` / `DefaultFlowNamePrefix` (T-10). Acceptance: a round-trip test
asserting the `(class, FlowType, ManagerItemUId, VisualType)` quadruple **per kind**.

### S2 — Gateway element handlers (0.3 d)

`ExclusiveGatewayElementHandler`, `ParallelGatewayElementHandler`;
`ElementTypes.ExclusiveGateway = "exclusivegateway"`, `ElementTypes.ParallelGateway = "parallelgateway"`;
`Layout.GatewaySizePx = 55` and `DefaultSize => new Size(55, 55)`; `CanBuild` on the concrete class; two
`AddScoped` lines. `IsLogging = true` comes free from `ProcessElementFactory`. Update the factory's
hand-written rejection sentence.

### S3 — Flow kinds, structural rules, `setFlow` (0.6 d)

- `AddFlow(schema, source, target, kind, condition)`; delete both `NotSupportedException`s in
  `BuildGraph` (flow kind, and `condition`).
- Extract the `SetFlowCondition` clone body into a shared helper; add the *plain → default* and
  *conditional → plain/default* transitions; rename the operation to `setFlow` with `setFlowCondition`
  kept as an alias (D5).
- Server-side rules mirroring the client errors
  ([validator §4](eng-91853-gateways-and-flows-validator.md#4-clientserver-parity-after-this-ticket)):
  self-loop refused; at most one default per source; a **diverging** or-gateway's outgoings must be
  conditional or default; a parallel gateway's outgoings must be plain; `kind: conditional` must carry a
  condition; a single unconditional continuation out of an or-gateway is **normalised** to a default flow.
- `RemoveFlow`: detach the endpoints before removing (T-8).
- Extend `ValidateStructure` and **rewrite its stale remark** (T-13); add a retry-loop fixture.

### S4 — Layout lane model (0.5 d)

[layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm) and its test list. Pure
class, no I/O, existing fixture.

### S5 — `describe` (0.15 d)

Flow `name`; both gateway `buildType` tokens arrive free from S2's `CanBuild`. `condition`,
`branchesOnActivityResult` and CLR-type kind mapping already exist.

### S6 — clio validator (0.4 d)

R14 arity scope (**the fix**); R15 self-loop; one-default-per-source; diverging-or-gateway flow kinds; the
optional `condition` field on `ProcessGraphEdgeArg` plus the condition-required error; the parallel-join
deadlock warning; the R7/R9 message rewrite; the R6 non-decision recorded in `ai-bp-connection-rules.md`.

```bash
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build
```

### S7 — Documentation, MCP surface, guidance (0.4 d)

The nine items in
[validator §5](eng-91853-gateways-and-flows-validator.md#5-closing-the-validate-vs-build-fork-review-follow-up-6).
Two deserve naming here:

- **`DescribeProcessPrompt`** — introduce `condition` **and** `branchesOnActivityResult` together.
  ENG-95891 reverted its own edit to this file so that this ticket ships both at once; shipping one
  repeats the mistake that revert undid.
- **Guidance** — a pull request in **clio-knowledge**, with a `libraryVersion` + `sequence` bump and a
  re-pin of `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`.

### S8 — Delivery and verification (0.4 d)

- `dotnet build MainSolution.slnx -c dev-n8`; the package's own suite (baseline: 928 unit tests green
  after ENG-95891 — do not regress it).
- **Rebundle:** `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version X.Y.Z.W`.
  The version **must go up** (current archive: **1.4.0.57**). Refresh
  `BundledProcessBuilderPackageTests` pins, and raise the `[RequiresPackage]` literals on
  create/modify — currently **1.4.0.44** — to the version that first ships gateways. Note the repo's own
  pinned lesson: a floor is a **capability floor**, so credit it with the change that justifies it.
- **Trap:** an install command resolves the archive from the **build output** directory, so
  `clio compress -d <repo path>` has no effect until clio is rebuilt.
- `clio.mcp.e2e`: extend `CreateBusinessProcessToolE2ETests`, `ModifyBusinessProcessToolE2ETests`,
  `DescribeProcessToolE2ETests`, `ValidateProcessGraphToolE2ETests`; add `setFlow` to
  `ProcessDesignerContractRequiredArgsE2ETests`.
- Stand verification — §4.

---

## 4. Stand verification (the part no unit test can cover)

Run schema-write operations **sequentially**: a parallel burst trips IIS rapid-fail and downs a
.NET Framework stand's application pool.

| # | Check | How |
|---|---|---|
| V1 | An exclusive split with a conditional + a default flow builds and **opens in the visual designer** with the correct glyphs (dashed default, diamond conditional) | build, then open the process |
| V2 | Byte-diff against the capture | export the built `metadata.json`; `BL7`, `CI4`, `CI5`, `CI6`, `BN2` must match [capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim) |
| V3 | It **runs**, and the right branch is taken | trigger it; read `SysProcessLog` / `SysProcessElementLog`; flip the input and confirm the other branch |
| V4 | First-`true`-wins is real | two overlapping conditions; confirm the first-declared branch wins, and that swapping `flows[]` order swaps the outcome |
| V5 | No default + no match fails as documented | expect `MismatchItemsCountException` in the process log |
| V6 | A parallel gateway joins | two branches; both must complete before the join's successor runs |
| V7 | `describe` round-trips | describe → feed the descriptor into a new `create` → describe again → compare |
| V8 | A retry loop lays out readably | build the `DeleteFilesInTable` topology and open it |
| V9 | No compile needed | the process runs without `compile-creatio`; do not infer a compile from a `VwSysProcess` dirty flag |

**The user verifies UI results in the browser themselves** — do not auto-open a browser after a
successful write.

---

## 5. Knowledge records (`docs/knowledge/`, same pull request)

1. **`process-designer/flow-kind-is-four-fields.md`** — a flow's kind is the CLR class **and** `FlowType`
   **and** `ManagerItemUId` **and** `VisualType`; the run time reads only the class, the designer reads
   the rest, and each wrong field fails differently.
2. **`process-designer/branch-precedence-is-array-order.md`** — sibling conditional flows are evaluated in
   `FlowElements` insertion order, first `true` wins, nothing encodes it, `Outgoings` is *not* in the
   precedence chain; therefore `flows[]` order must never be re-sorted and a kind change must preserve the
   index.

Both are implicit facts whose failure is silent — the policy's criterion. `make check-knowledge` reports
which records the diff touches.

---

## 6. Estimate

| Package | d |
|---|---|
| S1 default-flow serialization triple | 0.15 |
| S2 gateway handlers | 0.30 |
| S3 flow kinds + rules + `setFlow` + `RemoveFlow` detach | 0.60 |
| S4 layout lane model | 0.50 |
| S5 describe | 0.15 |
| S6 clio validator | 0.40 |
| S7 docs / MCP / prompt / guidance | 0.40 |
| S8 delivery + stand verification | 0.40 |
| **Total** | **2.90** |

Against the ticket's **2.5 d** that is within noise — the first revision estimated 3.5 d, and ENG-95891
absorbed the difference. If a cut is still wanted: the parallel-join deadlock warning (0.15) and the
optional `condition` field on the validator's edge argument (0.10) are the two items promised by the rules
spec rather than by this ticket. **Do not** cut the layout — it is the ticket's third named deliverable,
and without it the ticket's own basic case draws flows through elements.

---

## 7. Out of scope, stated explicitly

| Item | Owner |
|---|---|
| Inclusive (OR) and event-based gateways | ENG-95889 (the event-based one also forces a **compile**) |
| Nested branching, several merge points, long asymmetric chains | ENG-95890 |
| The formula expression itself: syntax, validation, parameter-usage scan | ENG-95891 (**done**; the validator was deliberately deleted — D4) |
| The **activity-result** condition dialect, write side | new follow-up ticket (D6) |
| R6 gateway arity as a rule | not implemented (D8) |
| Polyline routing (`CI10`) | never — `AutoPolyline` hands it to the designer |
| Migrating already-saved processes | not done (D12) |
| Gap-healing on `removeElement` | Task 16 of the task list |

---

## 8. Definition of Done

- [ ] Both gateway kinds and all three flow kinds build, modify, save and **run** on a stand (V1–V9).
- [ ] The `(class, FlowType, ManagerItemUId, VisualType)` quadruple asserted per kind, and the metadata
      byte-diffed against the capture (V2).
- [ ] `describe` reports gateways with a `buildType`, and flows with `kind`, `condition`,
      `branchesOnActivityResult` and `name`.
- [ ] `setFlow` changes a flow's kind **in place**, preserving its `UId` **and** its `FlowElements` index;
      `setFlowCondition` still works as an alias.
- [ ] Layout: no overlap for one split level with **unequal** branch lengths, with and without a merge; a
      back-edge lays out left-to-right; idempotent; adding a branch does not move existing branches.
- [ ] R14 produces **no** finding for a converging or-gateway with a single default flow; the self-loop
      rule fires; the new rules fire; client and server agree on every error-severity rule.
- [ ] `removeFlow` leaves no stale `Outgoings` / `Incomings`.
- [ ] Package unit suite green and not regressed below the 928-test ENG-95891 baseline; clio targeted
      filter green, recorded in the PR.
- [ ] `clio.mcp.e2e` extended for every changed tool.
- [ ] **MCP reviewed** — statement naming the tools, prompts and e2e files touched.
- [ ] **ClioRing compatibility reviewed, no Ring-consumed contract changed** — inspected
      `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, `clio-ring/ClioRing.Desktop/actions.json`; Ring's
      tool surface is `clio-deploy-creatio`, `clio-env-info`, `clio-import-iis-environments`,
      `clio-list-packages`, `clio-manage-envs`, `clio-restart`, `clio-uninstall-creatio`, `clio-version`
      — no process-designer tool and no `clio-run` nested process command.
- [ ] Docs and MCP descriptions updated; `DescribeProcessPrompt` gains **both** fields; guidance raised as
      a clio-knowledge PR with a `libraryVersion` + `sequence` bump and the curated-names fixture re-pinned.
- [ ] Two `docs/knowledge/` records added (§5).
- [ ] Package rebundled with an **increased** `-Version` above 1.4.0.57; pins updated; `[RequiresPackage]`
      floors raised with the reason credited; clio rebuilt before any local install verification.
- [ ] Agentic code review: comprehensive fan-out before opening each PR and again before ready-to-merge.
- [ ] `spec/sprint-status.yaml` rows added for all three repositories and moved to `done` at close.
- [ ] **The spec folder is committed.** The 2026-08-27 version of these documents was lost because it was
      left untracked through a merge; the Jira attachment was the only surviving copy.
