# ENG-91853 — Test plan

Covers the two harnesses, the mocking recipes (the thing that usually blocks a Creatio package test), and
the case matrix. Repository test policy applies throughout: explicit `Arrange` / `Act` / `Assert`, a
`because:` on **every** assertion, a `[Description]` on **every** test method, nothing OS-specific.

Revised 2026-09-05: cases ENG-95891 already covers are marked **(regression)** — they must keep passing
but need no new work; the new cases are marked **(new)**.

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

**Baseline after ENG-95891: 63 fixtures, ~1 190 `[Test]` / `[TestCase]` entries** (the sprint note records
928 executed unit tests), every fixture `[TestFixture(Category = "UnitTests")]` deriving from
`BaseComposableAppTestFixture : BaseConfigurationTestFixture`. **Do not regress it.**

The fixtures this ticket touches: `ProcessGraphBuilderTests`, `ProcessConditionalFlowTests`,
`ProcessLayoutEngineTests`, `ProcessDescriberTests`, `ProcessElementHandlerTests`,
`ProcessElementFactoryTests`, `ProcessOperationExecutorGraphOpsTests`, `CrtProcessBuilderAppTests`.

```bash
dotnet build MainSolution.slnx -c dev-n8
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf
```

### 1.2 clio — `clio.tests` and `clio.mcp.e2e`

Modules touched: `Command`, `ProcessModel` (mapped to `Command`) and `McpServer`:

```bash
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build
```

MCP end-to-end coverage in `clio.mcp.e2e` is **mandatory** for every changed tool — mapping-only unit
coverage does not complete an MCP change.

---

## 2. Mocking recipes

### 2.1 An in-memory `ProcessSchema` with no live environment

Already solved in the package; reuse it. `ProcessDesignTestSupport.cs` provides:

- **`TestProcessSchema : ProcessSchema`** — no-ops the manager-backed unique-name/caption assignment and
  `InitializeLocalizableValues`, and lets a test inject a `DataValueTypeManager`. Without the injection
  the base getter reaches `AppManagerProvider.GetManager("DataValueTypeManager")` → `AppConnection` (null
  on a hand-built schema) → `NullReferenceException` the moment a parameter type is touched.
- **`ProcessDesignTestSupport.CreateUserConnection()`** — a `TestUserConnection` with workspace, DB, a
  General current user, a `DataValueTypeManager`, a **substituted** `ProcessSchemaManager`, and a
  `DBSecurityEngine` granting every operation.

The layout engine needs neither: it takes no `UserConnection` and does no I/O.
`ProcessLayoutEngineTests` constructs `new ProcessSchema(UserConnection.ProcessSchemaManager)` directly
and pre-sets `Name`/`Caption`/`Size` on each node so the flow-element collection skips manager-backed
auto-naming (`ProcessLayoutEngineTests.cs:22-50`).

### 2.2 Building gateways and the three flow kinds in a test

Mirror the platform's own fixtures (`Terrasoft.Core.Tests/Process/ProcessSchemaBaseTestCase.cs`,
`BaseProcessTestCase.cs`) — and, for anything flow-shaped, prefer the package's own
`ProcessConditionalFlowTests` helpers, which already encode the ENG-95891 conclusions.

```csharp
private static ProcessSchemaExclusiveGateway AddExclusiveGateway(ProcessSchema schema, Guid laneUId, string name) =>
    new ProcessSchemaExclusiveGateway(schema) {
        UId = Guid.NewGuid(), Name = name, Caption = name,
        CreatedInSchemaUId = schema.UId, ContainerUId = laneUId,
        Size = new Size(55, 55)
        // ManagerItemUId is set by the constructor — ASSERT it, do not assign it
    };

private static ProcessSchemaSequenceFlow AddDefaultFlow(ProcessSchema schema,
        ProcessSchemaFlowElement source, ProcessSchemaFlowElement target) {
    var flow = new ProcessSchemaSequenceFlow(schema, ProcessSchemaEditSequenceFlowType.Default) {
        UId = Guid.NewGuid(), Name = "df" + Guid.NewGuid().ToString("N"),
        ManagerItemUId = FlowManagerItems.Default,                       // NOT set by the constructor
        VisualType = ProcessSchemaSequenceFlowVisualType.AutoPolyline
    };
    schema.FlowElements.Add(flow);
    flow.SourceRefUId = source.UId;      // order matters: the setters maintain Outgoings/Incomings
    flow.TargetRefUId = target.UId;
    return flow;
}
```

Four warnings, each already paid for once:

1. **Add the flow to the schema before setting `SourceRefUId` / `TargetRefUId`.** The setters resolve the
   endpoint through `ProcessSchema.GetBaseElementByUId` and mutate its `Outgoings` / `Incomings`.
2. **Never set `FlowType = Conditional` on a plain `ProcessSchemaSequenceFlow`** — the platform's
   design-time helper does an unguarded downcast (traps T-3).
3. **Do not copy the platform's `CreateProcessSchemaConditionalFlow` verbatim** — it sets
   `ManagerItemUId = SequenceFlowUId` on a conditional flow (`BaseProcessTestCase.cs:358-368`), i.e. the
   wrong item, and its tests pass anyway. That is trap T-1.
4. **`Outgoings` is a keyed collection.** Re-attaching an element carrying the same `UId` throws
   `ItemAlreadyExistException` from inside the platform — which is why a re-kind must detach with
   `SourceRefUId = Guid.Empty` first.

### 2.3 Asserting serialization without a live save

The four fields are plain properties, so assert on the object graph:

```csharp
flow.Should().BeOfType<ProcessSchemaSequenceFlow>(because: "…");
flow.FlowType.Should().Be(ProcessSchemaEditSequenceFlowType.Default, because: "…");
flow.ManagerItemUId.Should().Be(FlowManagerItems.Default, because: "…");
flow.VisualType.Should().Be(ProcessSchemaSequenceFlowVisualType.AutoPolyline, because: "…");
```

For the **metadata bytes** — what the ticket's "verified vs captures" asks for — this is a **stand check**
(V2): export the built `metadata.json` and diff against
[capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim).
Do not fake it with a hand-written expected JSON string; the capture is the oracle.

### 2.4 The clio validator needs no mocks

`ProcessGraphValidator.Validate(ProcessGraph)` is pure over records. `ValidateProcessGraphToolTests`
covers the tool; the rule tests go straight against the validator.

---

## 3. Case matrix

**P** package unit test · **C** clio unit test · **E** MCP e2e · **S** stand check.

### 3.1 Serialization

| # | Case | Expect | | Where |
|---|---|---|---|---|
| TC-01 | plain sequence flow | class, `FlowType = Sequence`, `BL7 = Sequence`, `AutoPolyline` | (regression) | P |
| TC-02 | conditional flow | `ProcessSchemaConditionalFlow`, `Conditional`, `BL7 = Conditional`, `AutoPolyline` | (regression) | P |
| TC-03 | **default flow** | `ProcessSchemaSequenceFlow`, `Default`, `BL7 = Default`, `AutoPolyline` | **(new)** | P |
| TC-04 | flow names | `SequenceFlow_` / `ConditionalFlow_` / `DefaultFlow_` prefixes on newly created flows | **(new)** | P |
| TC-05 | a re-kind keeps the original name | `setFlow` does not rename | **(new)** | P |
| TC-06 | `StrokeColor` | `FF939598` with no explicit assignment | (regression) | P |
| TC-07 | metadata byte-diff vs the capture | `BL7`, `CI4`, `CI5`, `CI6`, `BN2` match | **(new)** | S (V2) |

### 3.2 Gateway elements

| # | Case | Expect | | Where |
|---|---|---|---|---|
| TC-08 | `type: exclusiveGateway` | `ProcessSchemaExclusiveGateway`, `ExclusiveGatewayUId`, `55×55`, `IsLogging = true` | new | P |
| TC-09 | `type: parallelGateway` | `ProcessSchemaParallelGateway`, `ParallelGatewayUId`, `55×55` | new | P |
| TC-10 | `ResolveBuildType` | exclusive → `exclusivegateway`, parallel → `parallelgateway` — **the D1 regression guard** (one handler with two tokens fails this) | new | P |
| TC-11 | token casing | `ExclusiveGateway`, `exclusivegateway`, `EXCLUSIVEGATEWAY` all resolve | new | P |
| TC-12 | composition root | both handlers registered; the operation tripwire still passes | new | P |
| TC-13 | factory rejection message | no longer claims gateways are unbuildable | new | P |

### 3.3 Flow kinds and structural rules

| # | Case | Expect | | Where |
|---|---|---|---|---|
| TC-14 | exclusive split: 1 conditional + 1 default | builds and saves | new | P, E, S(V1) |
| TC-15 | exclusive split: 2 conditional, no default | builds; **R7 warning** client-side | new | P, C |
| TC-16 | converging exclusive gateway, single default outgoing | builds; **no R14 finding** — the 45-gateway regression | new | P, C |
| TC-17 | converging or-gateway asked for one unconditional continuation | **normalised to a default flow** | new | P |
| TC-18 | diverging exclusive gateway with a plain sequence outgoing | refused (server) / error (client) | new | P, C |
| TC-19 | conditional or default flow out of a parallel gateway | refused / **R11 error** | new | P, C |
| TC-20 | two default flows from one source | refused / error, naming both | new | P, C |
| TC-21 | `kind: conditional` with no condition | **refused** — never silently `"true"` | new | P |
| TC-22 | self-loop on build | refused | new | P |
| TC-23 | self-loop via `addFlow` | refused | new | P |
| TC-24 | describing a process that already contains a self-loop | succeeds | new | P |
| TC-25 | conditional flow off a **user task**, no gateway | builds (485 shipped instances) | (regression) | P |
| TC-26 | retry loop (`DeleteFilesInTable` topology) | passes `ValidateStructure` and R15 | new | P, C |
| TC-27 | duplicate `(source, target)` on `addFlow` | refused | (regression) | P |
| TC-28 | `removeFlow` with two matching flows | throws naming both | (regression) | P |
| TC-29 | `removeFlow` then read `SourceRef.Outgoings` | the removed flow is **gone** (T-8) | **(new)** | P |
| TC-30 | `removeElement` on a gateway | its conditional and default flows go too | new | P |
| TC-31 | `setFlow` plain → default | UId preserved **and** `FlowElements` index preserved | **(new)** | P |
| TC-32 | `setFlow` conditional → default | condition cleared; index preserved | **(new)** | P |
| TC-33 | `setFlow` sets a condition on a `GV2` flow | refused, naming the result branching | (regression) | P |
| TC-34 | `setFlow` sets a condition on a default flow | refused | (regression) | P |
| TC-35 | `setFlowCondition` still works as an alias | same behaviour as before | **(new)** | P, E |
| TC-36 | a re-kind preserves operator state | caption (cloned, not shared), stroke colour, `CI7`/`CI8`/`CI9`/`CI11`/`CI12`, container, size | **(new)** | P |
| TC-37 | `flows[]` order preserved into `schema.FlowElements` | insertion order matches declaration order | **(new)** | P |

### 3.4 Layout

Full list in [layout §7](eng-91853-gateways-and-flows-layout.md#7-testing). Headlines, all new:

| # | Case | Expect | Where |
|---|---|---|---|
| TC-38 | split + merge, equal branches | distinct Y per branch; merge between them | P |
| TC-39 | split + merge, **unequal** branches | the long branch keeps its lane (**L1**) | P |
| TC-40 | split **without** merge, unequal | each branch keeps its lane (**the 48 % shape**) | P |
| TC-41 | back-edge | six distinct columns on the `DeleteFilesInTable` topology (**L2**) | P |
| TC-42 | merge sharing a column | merge still aligned with its split (**L3**) | P |
| TC-43 | gateway size and centring | 55×55; a 31-px event on the same lane is centre-aligned | P |
| TC-44 | three-way split | lanes in flow **declaration** order, first branch on top | P |
| TC-45 | idempotence | two consecutive `Apply` calls give identical positions | P |
| TC-46 | stability | adding a branch does not move existing branches | P |

### 3.5 `describe`

| # | Case | Expect | | Where |
|---|---|---|---|---|
| TC-47 | gateway read-back | `type`, `buildType`, `position`, `managerItemUId` | **(new)** | P, E |
| TC-48 | conditional flow | `kind: conditional`, `condition`, `branchesOnActivityResult: false` | (regression) | P, E |
| TC-49 | default flow | `kind: default`, `condition: null` | **(new)** | P, E |
| TC-50 | `GV2` flow | `branchesOnActivityResult: true`, condition text still reported | (regression) | P |
| TC-51 | conditional flow whose `FlowType` is `Sequence` | `kind: conditional` anyway — the CLR type wins | (regression) | P |
| TC-52 | legacy plain sequence out of an exclusive gateway | `kind: sequence` — storage truth (D10) | **(new)** | P |
| TC-53 | flow `name` | reported, matches the schema element name | **(new)** | P, E |
| TC-54 | full round trip | describe → `create` → describe → equal | new | E, S(V7) |

### 3.6 clio validator — all new

| # | Case | Expect | Where |
|---|---|---|---|
| TC-55 | converging or-gateway, single default | **no finding** (R14 arity scope) | C |
| TC-56 | diverging gateway, default with no conditional sibling | R14 **error** | C |
| TC-57 | diverging exclusive gateway, no default | R7 **warning** naming `MismatchItemsCountException` | C |
| TC-58 | self-loop edge | R15 **error** | C |
| TC-59 | two default flows from one source | error | C |
| TC-60 | diverging or-gateway with a plain sequence outgoing | error; single-outgoing ⇒ no finding | C |
| TC-61 | `flow-kind: conditional` with no `condition` | error (needs the new optional edge field) | C |
| TC-62 | parallel join fed by branches of one exclusive split | **warning**, never an error | C |
| TC-63 | conditional flow from a start event | R13 **error** — deliberate, stricter than the platform | C |
| TC-64 | retry loop | no reachability finding | C |
| TC-65 | gateway that both converges and diverges (2-in/2-out) | **no finding** — R6 not implemented (D8) | C |
| TC-66 | tool description | names the new buildable slice | C |

### 3.7 Client/server parity

| # | Case | Expect | Where |
|---|---|---|---|
| TC-67 | every **error**-severity client rule → the server refuses the same graph | the parity table in [validator §4](eng-91853-gateways-and-flows-validator.md#4-clientserver-parity-after-this-ticket) is exhaustive | P + C, one test per row |
| TC-68 | every client **warning** → the server still builds | no warning accidentally promoted to a refusal | P |

### 3.8 Stand checks

V1–V9 in [plan §4](eng-91853-gateways-and-flows-plan.md#4-stand-verification-the-part-no-unit-test-can-cover).
The four no unit test can substitute for:

- **V3/V4 — branch selection and first-`true`-wins.** Trigger the process, read
  `SysProcessLog` / `SysProcessElementLog`, flip the input, confirm the other branch; then swap `flows[]`
  order and confirm the outcome swaps. The only proof that trap T-7 is real and that the layout's lane
  order matches it.
- **V5 — no default, nothing matches.** Expect `MismatchItemsCountException` in the process log.
- **V6 — parallel join.** Both branches must complete before the join's successor runs.
- **V1/V8 — the diagram.** Correct glyphs for conditional and default flows (the T-1 payoff), and a
  readable retry loop.

Run schema-write operations **sequentially** — a parallel burst trips IIS rapid-fail and downs a
.NET Framework stand's application pool. **The user checks UI results in the browser themselves.**

---

## 4. Coverage the matrix deliberately does not have

| Gap | Why | Mitigation |
|---|---|---|
| Metadata **bytes** in a unit test | the writer needs machinery the test assembly does not reach | stand check V2 against the capture |
| Designer **rendering** of a default-flow glyph | no automated designer harness | stand check V1, human-verified |
| `GV2` **write** side | out of scope (D6) | read-back TC-50; the write path is refused, TC-33 |
| Inclusive / event-based gateways | ENG-95889 | R11/R10 tests already exist and stay green |
| Nested branching layout | ENG-95890 | the phase-5 collision fallback keeps it non-overlapping; assert only "does not throw" |
| Corpus re-mining as a CI gate | 19 718 files, minutes of I/O, needs a PackageStore checkout | documented and reproducible ([capture §8](eng-91853-gateways-and-flows-serialization-capture.md#8-reproducing-the-mining)); re-run by hand on a platform upgrade |
