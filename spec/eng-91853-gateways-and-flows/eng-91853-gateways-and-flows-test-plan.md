# ENG-91853 — Test plan

Covers the two harnesses, the mocking recipes (the thing that usually blocks a Creatio package test),
and the full case matrix. Repository test policy applies throughout: explicit `Arrange` / `Act` /
`Assert`, a `because:` on **every** assertion, a `[Description]` on **every** test method, and nothing
OS-specific.

---

## 1. The two harnesses

### 1.1 The package — `CrtProcessBuilder.Tests`

`C:/Projects/workspace/ProcessBuilder/tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj`, `net472`.

| Component | Version |
|---|---|
| NUnit | 4.4.0 |
| NUnit3TestAdapter | 6.1.0 |
| Microsoft.NET.Test.Sdk | 18.0.1 |
| NSubstitute | 5.3.0 |
| FluentAssertions | pinned `[7.2.0]` |
| coverlet.msbuild | 6.0.4 |

It references Creatio's own harness binaries from `tests/CrtProcessBuilder/Libs/` — `UnitTest.dll`,
`Terrasoft.TestFramework.dll`, `Creatio.FeatureToggling.TestKit*.dll`, `Atf.Repository.Mock.dll` — plus
~30 `Terrasoft.*` reference assemblies from `.application/net-framework/core-bin`.

Baseline: **44 fixtures**, every one `[TestFixture(Category = "UnitTests")]`, all deriving from
`BaseComposableAppTestFixture : BaseConfigurationTestFixture`.

```bash
dotnet build MainSolution.slnx -c dev-n8
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf
```

### 1.2 clio — `clio.tests` and `clio.mcp.e2e`

Per the repository's smart-regression policy, the modules this ticket touches are `Command`,
`ProcessModel` (mapped to `Command`) and `McpServer`:

```bash
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build
```

MCP end-to-end coverage in `clio.mcp.e2e` is **mandatory** for every changed tool — mapping-only unit
coverage does not complete an MCP change.

---

## 2. Mocking recipes

### 2.1 An in-memory `ProcessSchema` with no live environment

The package already solves this and the solution must be reused, not re-invented.
`ProcessDesignTestSupport.cs` provides:

- **`TestProcessSchema : ProcessSchema`** — no-ops the manager-backed unique-name/caption assignment and
  `InitializeLocalizableValues`, and lets a test inject a `DataValueTypeManager`. Without the injection
  the base getter reaches `AppManagerProvider.GetManager("DataValueTypeManager")` → `AppConnection`
  (null on a hand-built schema) → `NullReferenceException` the moment a parameter type is read.
- **`ProcessDesignTestSupport.CreateUserConnection()`** — a `TestUserConnection` with workspace, DB,
  a General current user, a `DataValueTypeManager`, a **substituted** `ProcessSchemaManager`, and a
  `DBSecurityEngine` that grants every operation.

The layout engine needs neither: it takes no `UserConnection` and does no I/O.
`ProcessLayoutEngineTests` constructs `new ProcessSchema(UserConnection.ProcessSchemaManager)` directly
and pre-sets `Name`/`Caption`/`Size` on each node so the flow-element collection skips its
manager-backed auto-naming (`ProcessLayoutEngineTests.cs:22-50`).

### 2.2 Building gateways and the three flow kinds in a test

Mirror the platform's own fixtures, which are the reference implementation
(`Terrasoft.Core.Tests/Process/ProcessSchemaBaseTestCase.cs`, `BaseProcessTestCase.cs`):

```csharp
private static ProcessSchemaExclusiveGateway AddExclusiveGateway(ProcessSchema schema, Guid laneUId, string name) =>
    new ProcessSchemaExclusiveGateway(schema) {
        UId = Guid.NewGuid(), Name = name, Caption = name,
        CreatedInSchemaUId = schema.UId, ContainerUId = laneUId,
        Size = new Size(55, 55)
        // ManagerItemUId is set by the constructor — assert it, do not assign it
    };

private static ProcessSchemaConditionalFlow AddConditionalFlow(ProcessSchema schema,
        ProcessSchemaFlowElement source, ProcessSchemaFlowElement target, string condition) {
    var flow = new ProcessSchemaConditionalFlow(schema) {
        UId = Guid.NewGuid(), Name = "cf" + Guid.NewGuid().ToString("N"),
        ConditionExpression = condition,
        ManagerItemUId = ProcessSchemaElementManager.ConditionalFlowUId,   // NOT set by the constructor
        VisualType = ProcessSchemaSequenceFlowVisualType.AutoPolyline
    };
    schema.FlowElements.Add(flow);
    flow.SourceRefUId = source.UId;      // order matters: the setters maintain Outgoings/Incomings
    flow.TargetRefUId = target.UId;
    return flow;
}

private static ProcessSchemaSequenceFlow AddDefaultFlow(ProcessSchema schema, /* … */) =>
    // plain class + FlowType.Default + DefFlowUId — see the capture
    new ProcessSchemaSequenceFlow(schema, ProcessSchemaEditSequenceFlowType.Default) { … };
```

Three warnings, each already paid for once:

1. **Set `SourceRefUId` / `TargetRefUId` after adding the flow to the schema.** The setters resolve the
   endpoint through `ProcessSchema.GetBaseElementByUId` and mutate its `Outgoings` / `Incomings`
   (`ProcessSchemaSequenceFlow.cs:128-152`); if the endpoints are not in the schema yet they throw.
2. **Never set `FlowType = Conditional` on a plain `ProcessSchemaSequenceFlow`** — the platform's own
   design-time helper does an unguarded downcast (traps T-3).
3. **Do not copy the platform's `CreateProcessSchemaConditionalFlow` verbatim** — it sets
   `ManagerItemUId = SequenceFlowUId` on a conditional flow (`BaseProcessTestCase.cs:358-368`), i.e. the
   wrong item. Its tests pass anyway, which is precisely trap T-1.

### 2.3 Asserting serialization without a live save

The four fields under test are plain properties, so the cheap assertion is on the object graph:

```csharp
flow.Should().BeOfType<ProcessSchemaConditionalFlow>(because: "…");
flow.FlowType.Should().Be(ProcessSchemaEditSequenceFlowType.Conditional, because: "…");
flow.ManagerItemUId.Should().Be(ProcessSchemaElementManager.ConditionalFlowUId, because: "…");
flow.VisualType.Should().Be(ProcessSchemaSequenceFlowVisualType.AutoPolyline, because: "…");
```

For the **metadata bytes** — which is what the ticket's "verified vs captures" asks for — use a
`DataWriter` round trip if one is reachable from the test assembly; otherwise this is a **stand check**
(V2 in [plan §4](eng-91853-gateways-and-flows-plan.md#4-stand-verification-the-part-no-unit-test-can-cover)):
export the built schema's `metadata.json` and diff `BL7`, `CI4`, `CI5`, `CI6`, `BN2` against
[capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim).
Do not fake this with a hand-written expected JSON string — the capture is the oracle.

### 2.4 The clio validator needs no mocks

`ProcessGraphValidator.Validate(ProcessGraph)` is pure over records. Existing
`ValidateProcessGraphToolTests` covers the tool; the rule tests go straight against the validator.

---

## 3. Case matrix

Legend — **P** package unit test · **C** clio unit test · **E** MCP e2e · **S** stand check.

### 3.1 Serialization (S1)

| # | Case | Expect | Where |
|---|---|---|---|
| TC-01 | plain sequence flow | class `ProcessSchemaSequenceFlow`, `FlowType = Sequence`, `BL7 = SequenceFlowUId`, `VisualType = AutoPolyline` | P |
| TC-02 | conditional flow | class `ProcessSchemaConditionalFlow`, `FlowType = Conditional`, `BL7 = ConditionalFlowUId`, `AutoPolyline` | P |
| TC-03 | default flow | class `ProcessSchemaSequenceFlow`, `FlowType = Default`, `BL7 = DefFlowUId`, `AutoPolyline` | P |
| TC-04 | flow names | prefixes `SequenceFlow_` / `ConditionalFlow_` / `DefaultFlow_` | P |
| TC-05 | `StrokeColor` | `FF939598` without an explicit assignment (regression guard on the class default) | P |
| TC-06 | metadata byte-diff vs the capture | `BL7`, `CI4`, `CI5`, `CI6`, `BN2` match | S (V2) |

### 3.2 Gateway elements (S2)

| # | Case | Expect | Where |
|---|---|---|---|
| TC-07 | `type: exclusiveGateway` | `ProcessSchemaExclusiveGateway`, `ManagerItemUId = ExclusiveGatewayUId`, `Size = 55×55`, `IsLogging = true` | P |
| TC-08 | `type: parallelGateway` | `ProcessSchemaParallelGateway`, `ParallelGatewayUId`, `55×55` | P |
| TC-09 | `ResolveBuildType` | an exclusive gateway resolves to `exclusivegateway`, a parallel one to `parallelgateway` — **the regression guard for D1** (one handler with two tokens would fail this) | P |
| TC-10 | token casing | `ExclusiveGateway`, `exclusivegateway`, `EXCLUSIVEGATEWAY` all resolve | P |
| TC-11 | composition root | both handlers registered; the operation tripwire still passes | P |
| TC-12 | factory rejection message | no longer claims gateways are unbuildable | P |

### 3.3 Flow kinds and structural rules (S3)

| # | Case | Expect | Where |
|---|---|---|---|
| TC-13 | exclusive split: 1 conditional + 1 default | builds and saves | P, E, S(V1) |
| TC-14 | exclusive split: 2 conditional, no default | builds; **R7 warning** from the client validator | P, C |
| TC-15 | converging exclusive gateway, single default outgoing | builds; **no R14 finding** — the 45-shipped-gateway regression | P, C |
| TC-16 | converging or-gateway asked for one unconditional continuation | **normalised to a default flow**, not a plain sequence flow | P |
| TC-17 | diverging exclusive gateway with a plain sequence outgoing | **refused** (server) / **error** (client) | P, C |
| TC-18 | conditional or default flow out of a parallel gateway | **refused** / **R11 error** | P, C |
| TC-19 | two default flows from one source | **refused** / error, naming both | P, C |
| TC-20 | `kind: conditional` with no condition | **refused** — never silently `"true"` | P |
| TC-21 | self-loop `T → T` on build | **refused** | P |
| TC-22 | self-loop via `addFlow` on modify | **refused** | P |
| TC-23 | describing a process that already contains a self-loop | succeeds, no exception | P |
| TC-24 | conditional flow off a **user task**, no gateway | builds (485 shipped instances; the ENG-95891 shape) | P |
| TC-25 | retry loop (`DeleteFilesInTable` topology) | passes `ValidateStructure` and R15 reachability | P, C |
| TC-26 | `removeFlow` with two flows matching `(source, target)` | **throws naming both**, does not delete arbitrarily | P |
| TC-27 | `removeFlow` then read `SourceRef.Outgoings` | the removed flow is **gone** from the collection | P |
| TC-28 | `removeElement` on a gateway | its conditional and default flows are removed too | P |
| TC-29 | `setFlow` re-kinds a flow | UId preserved **and** array position preserved | P |
| TC-30 | `setFlow` sets a condition on a flow carrying `GV2` | **refused**, naming the activity-result branching | P |
| TC-31 | `flows[]` order is preserved into `schema.FlowElements` | insertion order matches declaration order | P |

### 3.4 Layout (S5)

Full list in [layout §7](eng-91853-gateways-and-flows-layout.md#7-testing). Headlines:

| # | Case | Expect | Where |
|---|---|---|---|
| TC-32 | split + merge, equal branches | distinct Y per branch; merge between them | P |
| TC-33 | split + merge, **unequal** branches | the long branch's second node keeps its lane (**defect L1**) | P |
| TC-34 | split **without** merge, unequal branches | each branch keeps its lane (**the 48 % shape**) | P |
| TC-35 | back-edge | six distinct columns on the `DeleteFilesInTable` topology (**defect L2**) | P |
| TC-36 | merge sharing a column with an unrelated node | merge still aligned with its split (**defect L3**) | P |
| TC-37 | gateway size and centring | 55×55; a 31-px event on the same lane is centre-aligned | P |
| TC-38 | three-way split | lanes assigned in flow **declaration** order, first branch on top | P |
| TC-39 | idempotence | two consecutive `Apply` calls give identical positions | P |
| TC-40 | stability | adding a branch does not move existing branches | P |

### 3.5 `describe` (S6)

| # | Case | Expect | Where |
|---|---|---|---|
| TC-41 | gateway read-back | `type` = runtime class, `buildType` = the gateway token, `position`, `managerItemUId` | P, E |
| TC-42 | conditional flow | `kind: conditional`, `condition` = the stored text, `conditionKind: expression`, `name` | P, E |
| TC-43 | default flow | `kind: default`, `condition: null`, `conditionKind: none` | P, E |
| TC-44 | `GV2` flow | `conditionKind: result` + the source activity and result UIds; `condition: null` | P |
| TC-45 | conditional flow whose `FlowType` was left at `Sequence` | `kind: conditional` anyway — the CLR type wins (D10) | P |
| TC-46 | legacy plain sequence flow out of an exclusive gateway | `kind: sequence` — storage truth, not inferred semantics (D10) | P |
| TC-47 | full round trip | describe → `create` from the described descriptor → describe → equal | E, S(V7) |

### 3.6 clio validator (S7)

| # | Case | Expect | Where |
|---|---|---|---|
| TC-48 | converging or-gateway, single default | **no finding** (R14 arity scope) | C |
| TC-49 | diverging gateway, default with no conditional sibling | R14 **error** | C |
| TC-50 | diverging exclusive gateway, no default | R7 **warning**, message naming `MismatchItemsCountException` | C |
| TC-51 | self-loop edge | R15 **error** | C |
| TC-52 | two default flows from one source | **error** | C |
| TC-53 | diverging or-gateway with a plain sequence outgoing | **error**; single-outgoing case ⇒ no finding | C |
| TC-54 | `flow-kind: conditional` with no `condition` | **error** (needs the new optional edge field) | C |
| TC-55 | parallel join fed by branches of one exclusive split | **warning**, never an error | C |
| TC-56 | conditional flow from a start event | R13 **error** — deliberate, stricter than the platform | C |
| TC-57 | retry loop | no reachability finding | C |
| TC-58 | gateway that both converges and diverges (2-in/2-out) | **no finding** — R6 is not implemented (D8) | C |
| TC-59 | tool description | names the new buildable slice | C |

### 3.7 Client/server parity

| # | Case | Expect | Where |
|---|---|---|---|
| TC-60 | for every **error**-severity client rule, the server refuses the same graph | parity table in [validator §4](eng-91853-gateways-and-flows-validator.md#4-clientserver-parity-after-this-ticket) is exhaustive | P + C, one test per row |
| TC-61 | for every client **warning**, the server still builds | no warning is accidentally promoted to a refusal | P |

### 3.8 Stand checks

V1–V9 in [plan §4](eng-91853-gateways-and-flows-plan.md#4-stand-verification-the-part-no-unit-test-can-cover).
The four that no unit test can substitute for:

- **V3/V4 — branch selection and first-`true`-wins.** Trigger the process, read
  `SysProcessLog` / `SysProcessElementLog`, flip the input, confirm the other branch; then swap the
  `flows[]` order and confirm the outcome swaps. This is the only proof that trap T-7 is real and that
  the layout's lane order matches it.
- **V5 — no default, nothing matches.** Expect `MismatchItemsCountException` in the process log.
- **V6 — parallel join.** Both branches must complete before the join's successor runs.
- **V1/V8 — the diagram.** Correct glyphs for conditional and default flows (the T-1/T-2 payoff), and a
  readable retry loop.

Run schema-write operations **sequentially** — a parallel burst trips IIS rapid-fail and downs a
.NET Framework stand's application pool. **The user checks UI results in the browser themselves**; do
not auto-open a browser after a successful write.

---

## 4. Coverage the matrix deliberately does not have

| Gap | Why | Mitigation |
|---|---|---|
| Metadata **bytes** in a unit test | the writer needs machinery the test assembly does not reach | stand check V2, diffed against the capture |
| Designer **rendering** of a default-flow glyph | no automated designer harness | stand check V1, human-verified |
| `GV2` **write** side | out of scope (D6) | read-back tests TC-44; the write path is refused, TC-30 |
| Inclusive / event-based gateways | ENG-95889 | R11/R10 tests already exist and stay green |
| Nested branching layout | ENG-95890 | the phase-5 collision fallback keeps it non-overlapping; no assertion beyond "does not throw" |
| Corpus re-mining as a CI gate | 19 718 files, minutes of I/O, needs a PackageStore checkout | the mining is documented and reproducible ([capture §8](eng-91853-gateways-and-flows-serialization-capture.md#8-reproducing-the-mining)); re-run by hand on a platform upgrade |
