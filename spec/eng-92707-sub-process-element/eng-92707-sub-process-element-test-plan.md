# ENG-92707 — Sub-process element: test plan

## 1. Where the coverage lives

| Level | Project | What it can prove |
|---|---|---|
| P — package unit | `ProcessBuilder/tests/UnitTests/CrtProcessBuilder.Tests` | Handler, identity, reader, applier, guards, the sync report, the stamps |
| C — clio unit | `clio.tests` (`Module=ProcessModel`, `Module=McpServer`) | `ManagerMap` arm, `SubProcessBlockExpectation`, describe DTO round-trip |
| E — MCP end-to-end | `clio.mcp.e2e` | The tool contract over the wire against a live stand |
| S — stand only | manual run, recorded | Save-time platform validation, designer rendering, runtime binding |

The S boundary is fixed and known: `SaveSchema` and the platform's pre-save validation cannot be
exercised below it, and no unit test will catch a self-reference, a recursion or an uncompiled callee
that the platform refuses at save. Do not claim otherwise in the DoD.

## 2. Mocking recipe — a sub-process element in memory

The platform's own fixture is the reference:
`C:/Projects/Creatio2/TSBpm/Src/Lib/Terrasoft.Core.Tests/Process/ProcessSchemaSubProcess.Tests.cs`
(9 test methods; 6 of them genuinely about the sub-process). **It cannot be referenced.** It derives
from `BaseProcessTestCase`, which is absent from both DLLs `CrtProcessBuilder.Tests` references
(`Libs/UnitTest.dll`, `Libs/Terrasoft.TestFramework.dll`). Adapt it into
`ProcessDesignTestSupport.cs`, exactly as that file already did for the ProcessDesigner helpers.

The shape to adapt (from the platform fixture, condensed):

```csharp
// substitute the manager and make the callee resolvable by UId
var manager = Substitute.ForPartsOf<ProcessSchemaManager>();
var schemaManagerProvider = Substitute.For<SchemaManagerProvider>(appConnection);
userConnection.Workspace.SchemaManagerProvider = schemaManagerProvider;
manager.SchemaManagerProvider = schemaManagerProvider;
schemaManagerProvider.GetManager(Arg.Any<string>()).Returns(manager);

var callee = Substitute.ForPartsOf<ProcessSchema>(manager);
callee.UId = Guid.NewGuid();
var managerItem = Substitute.For<ISchemaManagerItem<ProcessSchema>>();
manager.Add(managerItem);
manager.FindItemByUId(Arg.Any<Guid>()).Returns(managerItem);
manager.GetInstanceFromMetaData(Arg.Any<Guid>()).Returns(callee);
manager.GetInstanceByUId(Arg.Any<Guid>()).Returns(callee);

// the element, ATTACHED, with a UId, and only then pointed at the callee  (traps T-1, T-5)
var host = Substitute.ForPartsOf<ProcessSchema>(manager); host.UId = Guid.NewGuid();
var element = new ProcessSchemaSubProcess(host) { ParentMetaSchema = host, UId = Guid.NewGuid(), Name = "SubProcess1" };
// attach to host.FlowElements here
element.SchemaUId = callee.UId;   // <-- the platform's diff runs on this line
```

Three details a writer gets wrong:

* `ProcessSchemaSubProcess.Schema` resolves through the element's `BaseProcessSchema.SchemaManagerProvider`
  (`ProcessSchemaSubProcess.cs:163`) — the provider must be reachable from the element's own schema,
  not only from `UserConnection`.
* Production code calls the **generic** `managerProvider.GetManager<ProcessSchemaManager>()`; the
  generic is non-virtual and delegates to the string overload, which is what to stub.
* `GlobalAppSettings.FeatureClearSubProcessParametersSourceValue` is an `internal static` **property**
  (`GlobalAppSettings.cs:381`) and `Terrasoft.Core`'s friend list does not include
  `CrtProcessBuilder.Tests` — a package-side test cannot flip it. Test the default (`true`) behaviour
  only.

Prefer the **reader seam** (`ISubProcessReader`, plan S2) over the manager substitution wherever the
test is about our logic rather than the platform's: substituting the reader needs no schema manager at
all, which is how every `PreconfiguredPage*` fixture stays fast.

## 3. Conventions that are enforced, not advisory

* `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` on every package fixture.
  `CiContractGuardTests` reflects over the assembly and fails the build naming any fixture without a CI
  category — and a fixture without one is **skipped by the Jenkins runner while the stage stays green**.
* `[Description("...")]` on every test method; explicit `Arrange` / `Act` / `Assert`; FluentAssertions
  with `because:` on load-bearing assertions.
* Package source and tests are C# 7.3 — no C# 8+ syntax.
* Build the package suite with `-c dev-nf` (or an exported `Configuration=dev-nf`), from the main
  checkout, **not** a git worktree, and without `-p:CoreLibPath` / `-p:TestCoreLibPath` overrides.
* clio side: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=ProcessModel|Module=McpServer)" --no-build`.
  A filter that selects nothing has exited 0 in the past — check the executed count, not the exit code.

## 4. Case matrix

### The sync — the seven cases, adapted from `PreconfiguredPageParameterSyncTests`

| # | Case | Expect | Lvl |
|---|---|---|---|
| TC-01 | Callee has a parameter the element lacks | Added, with a `BK15` row, at the callee's ordinal | P |
| TC-02 | Callee lost a parameter the element carries | Removed, and its `BK15` row removed | P |
| TC-03 | Callee renamed a parameter | Matched through the mapping row, element parameter UId unchanged, value kept | P |
| TC-04 | Callee retyped a parameter | Type refreshed; stored value not carried across the change | P |
| TC-05 | Nothing changed | Second run reports **no drift** (idempotency, AC3) | P |
| TC-06 | Callee unreadable | Nothing removed; `inSync` is `null`, not `false` | P |
| TC-07 | `IsInSync` read-only agrees with what `Sync` would do | Same predicate, no mutation | P |

### The stamps — D5 / T-2

| # | Case | Expect | Lvl |
|---|---|---|---|
| TC-08 | After a build, a synced parameter's `CreatedInSchemaUId` | == the **callee's** schema UId | P |
| TC-09 | After the builder writes a value, `SourceValue.ModifiedInSchemaUId` | == the **host** schema UId | P |
| TC-10 | Value survives a second sync | Still present (this is the regression TC-08/09 exist to prevent) | P |
| TC-11 | A value on an `Out` parameter | Cleared by the platform — assert the refusal instead (TC-15) | P |

### The refusals — D7

| # | Case | Expect | Lvl |
|---|---|---|---|
| TC-12 | `processUId` == the host process | Refused, message names the process | P |
| TC-13 | Retarget while another element maps from a parameter of this element | Refused, references listed | P |
| TC-14 | Callee name/UId does not resolve | Refused; "not found" distinguished from "could not read" | P |
| TC-15 | Mapping onto an `Out` / `Internal` parameter | Refused, message names the direction | P |
| TC-16 | Mapping a collection parameter (multi-instance, D9) | Refused, message says multi-instance is unsupported | P |
| TC-17 | Callee does not begin with a Simple start event (R16, D8) | **Refusal**, not a warning — see the note below | P |

### Write ordering — T-1 / T-5

| # | Case | Expect | Lvl |
|---|---|---|---|
| TC-18 | Assign `SchemaUId` on a detached element whose callee has a parameter | Reproduces the NRE — pins why the applier runs post-graph | P |
| TC-19 | Element written with no `UId` yet | Sync is a no-op — pins the ordering | P |

### Serialization and read-back

| # | Case | Expect | Lvl |
|---|---|---|---|
| TC-20 | Built element carries `ManagerItemUId` `49eafdbb-…` and size 69x55 | Present (T-10) | P |
| TC-21 | `describe` returns callee name + UId, `inSync`, and per-parameter `direction` / `isRequired` | Present | P |
| TC-22 | Round trip: build → graph → describe over an in-memory schema | Element and parameters survive | P |
| TC-23 | `ManagerMap.ResolveDataId("subprocess")` | `EventType.SubProcess`, not `Unknown` (T-6) | C |
| TC-24 | `validate-process-graph` over a graph containing the element | No `UNKNOWN` finding | C |
| TC-25 | ~~`SubProcessBlockExpectation`~~ | **Dropped by D2a** — the block binds to `type:"subProcess"` and an older server refuses the unknown type loudly, so no expectation is owed | — |
| TC-26 | `DescribedSubProcess` outbound re-serialization | Pins the wire names, inbound-only assertions do not (T-14 note) | C |
| TC-27 | `create-business-process` with a `subProcess` block against a live stand | Element built, describe confirms | E |
| TC-28 | `modify-business-process` `setElement` with `resync: true` | Drift reported | E |

**TC-17 reads the wrong half of the plan, and the code follows the other one.** §3 D8 decides
"R16 can be a hard refusal in the applier and an Error in `ProcessGraphValidator`", backed by a corpus
measurement of zero violations among the 269 resolvable callees; S3 step 2 and this row said "warn". The
applier throws, which is D8. The validator half did NOT ship — a planned graph carries no reference to the
called process at all, so the rule fires in the build path only (DQ-2). Row corrected rather than the code.

**TC-07 is still not implemented.** `IsInSync` (`SubProcessElementIdentity.MirrorsCallee`, one-directional)
and `SubProcessSyncReport.IsUnchanged` (which also counts `Removed`) answer different questions about an
element that carries a parameter the callee dropped. That state may well be unreachable through a
design-time read, because the platform converges the element before this package sees it — which is the
same argument DQ-10 makes about the drift report — so the case is left for a stand rather than pinned by a
unit test that would have to construct a state the platform does not produce.

### Stand only

TC-29…TC-36 are V1–V8 in the [plan](eng-92707-sub-process-element-plan.md) §5. Each must declare which
observation level it reaches — **Stored** (metadata written), **Design time** (the designer opens it),
**Runtime** (the process ran) — and where it stops. A case that passes at Stored level with no
designer ever opened has not proved the designer accepts it; nine of ten flow-label cases did exactly
that on ENG-91853, and the run's own report said so.

## 5. What is deliberately not covered

* Multi-instance beyond TC-16's refusal (D9).
* Event / embedded sub-process (out of scope).
* `UseLastSchemaVersion` (D10).
* Recursion depth. Nothing in the platform, the designer or Academy documents a limit; the only guard
  anywhere is the designer excluding the immediately containing process from the list. If a cycle guard
  is wanted it is new work, not a test.
