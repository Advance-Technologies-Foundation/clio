# ENG-92707 — Sub-process element: traps

Every entry below is something that does **not** announce itself. "Silent" means the failure produces
no exception, no notice and no log line at the moment it is caused — the damage surfaces somewhere
else, or not at all.

| # | Trap | Silent? | Where it bites |
|---|---|---|---|
| T-1 | Assigning `SchemaUId` on an element not yet in `schema.FlowElements` | No — NRE | Build path |
| T-2 | Re-stamping `CreatedInSchemaUId = schema.UId` (the Pre-configured page rule) | **Yes** | Every value, next sync |
| T-3 | Writing a value on an `Out` / `Internal` parameter | **Yes** | Every sync |
| T-4 | Self-reference (`SchemaUId` == host schema UId) | **Yes** | Parameters never appear |
| T-5 | Assigning `SchemaUId` before the element's own `UId` | **Yes** | Parameters never appear |
| T-6 | Choosing `subprocess` as the token without touching `ManagerMap` | No — hard error | `validate-process-graph` |
| T-7 | Believing a re-sync can observe drift after the fact | **Yes** | Empty drift report |
| T-8 | Retargeting while dependents still map from the element | **Yes at edit time** | Process refuses to START |
| T-9 | Not re-syncing after the callee changed | **Yes** | Runtime delivers nothing |
| T-10 | Forgetting `ManagerItemUId` (`BL7`) | **Yes** | Designer render |
| T-11 | Treating `SchemaUId` as a version pin | **Yes** | Callee republished |
| T-12 | `UseLastSchemaVersion` looks like the version switch | **Yes** | Nothing reads it |
| T-13 | An `is ProcessSchemaSubProcess` identity check | **Yes** | Catches event sub-processes too |
| T-14 | Adding a `subProcess` block to an older package | **Yes** — `success: true` | Block discarded |
| T-15 | A block with no `EnsureBlockMatchesHandler` arm | **Yes** | Block dropped on the wrong type |
| T-16 | Absent `L12` read as `In` | **Yes** | Direction is `Variable` |
| T-17 | Mapping a collection parameter | **Yes** | Element becomes multi-instance |
| T-18 | Leaving the five "not buildable" texts in place | No, but corrosive | Agents refuse to try |
| T-19 | Registering the handler after `UserTaskElementHandler` | No — test tripwire | Composition test |
| T-20 | `ProcessModifyHandler` re-runs layout over the whole schema | **Yes** | Hand-placed positions |
| T-21 | Notices attached only when the operation saved | **Yes** | Drift report lost |
| T-22 | Verifying from the wrong clio build output | **Yes** | Old archive installed |
| T-23 | Rebundling mid-review | No — throws | Reviewer blocked |
| T-24 | A new `[OperationContract]` | No — pinned count | Both sides' tests |
| **T-25** | **Any sync on an ALREADY multi-instance element flattens it** | **Yes** | **61 of 416 shipped elements** |
| T-26 | `Clone()` / the copy constructor fires the setter detached | No — NRE | Copy-paste, `Clone` |
| T-27 | A retarget strands a mapping row on an `IsDynamic` parameter | **Yes** | Saves green, dereferenced later |
| T-28 | `ApplyMetaDataValue` writes `_schemaUId` directly | **Yes** | A whole-schema save does **not** sync |
| T-29 | `odata-read` fails on `VwProcessLib`; `execute-esq` works | **Yes** | Looks like discovery is impossible |

---

## The six that will actually cost a day

### T-25 — a sync on a multi-instance element destroys it — **Blocker**

The self-reference / empty-UId guard does **not** protect a multi-instance element, because it is in
the wrong method. The setter calls the explicit-interface member, which routes through
`SynchronizeParametersInternal` (`ProcessSchemaActivity.cs:373-395`); that method clones the two
collection parameters and the three counters, calls **`Parameters.Clear()` unconditionally**, and only
*then* calls the inner `SynchronizeParameters` where `GetCanSynchronizeParameters()` lives. So on an
element that is already multi-instance, any path that reaches the setter or the interface method
rebuilds the element as `InputRecordCollection` + `OutputRecordCollection` + three counters — the
callee's parameters, and every value mapped into them, are gone.

**61 of the 416 shipped elements (14.7 %) are multi-instance.** A `setElement` that merely touches one
of them flattens it, silently, and `describe` afterwards reports a structurally valid element.

**Do:** make the multi-instance refusal (plan D9) a **pre-condition evaluated before any code path that
can reach the setter or the interface method** — including the D2 drift snapshot and any applier that
runs after `GetDesignInstance`. Refuse on `element.IsMultiInstanceModeEnabled`, not on "the caller
asked to map a collection".

### T-1 — `SchemaUId` before attachment NREs

`ProcessSchemaSubProcess.SynchronizeParameters()` → `ClearParametersSourceValue()` dereferences
`ParentMetaSchema.UId`, and `ParentMetaSchema` is only set when the element joins the schema's element
collection (`MetaItemCollection.InsertItem`). Every existing user-task handler in CrtProcessBuilder
assigns its schema reference **inside `Create()`**, before the element is attached. Copying that shape
for a sub-process NREs the moment the callee declares at least one parameter — which is every real
callee.

**Do:** create the element bare, attach it, then assign `SchemaUId` from the applier (the
`PreconfiguredPageApplier` "post-graph" position already exists for exactly this reason — an element
parameter can only be written once the element is on the schema).

### T-2 — the stamp that erases every value

`PreconfiguredPageParameterSync` deliberately re-asserts `existing.CreatedInSchemaUId = schema.UId`,
because for a page the runtime only delivers a value when that stamp equals the process schema UId.
For a sub-process the rule is **inverted**: `ClearParametersSourceValue` keeps a value only when
`CreatedInSchemaUId != SourceValue.ModifiedInSchemaUId`. Stamp both to the host schema and `isChanged`
is false for every parameter, so the next sync — which runs on *every design-time read of the schema*
— clears them all.

**Do:** leave `CreatedInSchemaUId` = the callee's schema UId (what the platform wrote, and what all 420
shipped elements carry), and set `SourceValue.ModifiedInSchemaUId` = the **host** schema UId on every
value the builder writes. Assert both in a unit test, and assert that a second sync preserves the value.

### T-3 — direction decides whether a value can exist at all

`GetIsAssignable` is true only for `In` and `Variable`. An `Out` or `Internal` element parameter has
its `SourceValue` wiped on every sync, feature flag or not
(`FeatureClearSubProcessParametersSourceValue` defaults to `true`). A contract that lets an agent map a
value onto an output parameter accepts the call, returns success, and produces a process where the
value is gone by the next read.

**Do:** report `direction` in `describe`, and refuse a mapping onto a non-assignable direction at write
time with a message that names the direction.

### T-7 — there is no "after" to diff against

`BaseProcessSchemaManager.FindDesignItem` / `GetItemFromMetaData` call `SynchronizeParameters()` on
every read, so by the time the package's `GetDesignInstance` hands over the schema, every sub-process
element has **already** converged. A re-sync implemented as "load, sync, compare" reports nothing,
every time, and looks like it works.

**Do:** snapshot the element's parameter set and its `BK15` rows *before* mutating `SchemaUId`
(or before the save), mutate, then diff the snapshot against the post-state. That is the only window.

### T-8 — the edit is quiet and the start is loud

The classic designer **refuses** a retarget when any other element parameter, process parameter or flow
condition still maps from this element (`canChangeSchema` → `getCanRemoveElement`). The C# setter has
no such guard: it removes the parameters and flips `IsValid = false` on the dependents
(`InvalidateDependentElements`). Nothing throws, `describe` looks fine, and the process then refuses to
**start** with a `ValidateException` from `CheckSchemaHasInvalidElements` — attributed to the process,
not to the edit that caused it.

**Do:** run `ProcessElementDependencyScanner.FindDependentReferences` before a retarget and refuse by
name, mirroring both the designer and ENG-95461's shipped decision that silent dropping is not
acceptable.

---

## The rest, in one line each

**T-4** `GetCanSynchronizeParameters()` returns false when `SchemaUId == BaseProcessSchema.UId`, so a
process calling itself writes an element with zero parameters and no complaint. The designer's
candidate list excludes only the immediately containing process — there is no ancestor or cycle check
anywhere, on either side.

**T-5** The same guard requires a non-empty element `UId`; order the writes `UId` → attach →
`SchemaUId`.

**T-6** clio's `ManagerMap.ResolveDataId` maps only the camelCase diagram data-id `"callactivity"` to
`EventType.SubProcess`. A build token that is not an arm resolves to `EventType.Unknown`, and
`ProcessGraphValidator.CheckUnknownTypes` turns that into a hard **Error** — so `validate-process-graph`
rejects a graph the server builds correctly. Whatever token is chosen, add the arm **and** a
`ManagerMapResolveDataIdTests` case.

**T-9** Runtime binds by parameter **name**, ordinal, ignoring direction; an unmatched name is skipped
with no error and no log. `IsRequired` is never validated for a sub-process on either side. A stale
element runs and delivers nothing.

**T-10** `ProcessSchemaSubProcess`'s constructor sets only `BpmnElementName` — unlike
`ProcessSchemaUserTask` and both gateways it does not stamp `ManagerItemUId` or `DragGroupName`. 417 of
420 shipped elements carry `BL7`; a built one that does not is the odd one out.

**T-11 / T-12** `SchemaUId` is not a pin: `SubProcessProxy.GetInstance` /
`BaseProcessElementFactory.CreateSubProcessInstance` redirect through `GetActiveVersion`, and merely
opening the element's card in the designer re-points `schemaUId` to the callee's actual version.
`UseLastSchemaVersion` (`CK5`) is captioned "Use Last Version of Schema", is serialized, is written by
no UI in `CrtProcessDesigner`, and appears in 3 of 420 shipped elements. Do not model it as the
version knob; do not expose it.

**T-13** `ProcessSchemaEventSubProcess` inherits from `ProcessSchemaSubProcess`. An identity predicate
must also test `TriggeredByEvent` (and, for the embedded case, the presence of `CK2` children),
otherwise the handler claims elements it cannot configure. clio's `ManagerMap` already keeps
`callActivity` and `eventSubProcessExpanded` as separate UIds and separate `EventType` values — keep
that separation.

**T-14 / T-15** CrtProcessBuilder's `DataContractJsonSerializer` silently ignores undeclared JSON
members, so a `subProcess` block sent to a package that predates it is **discarded while the call
answers `success: true`**. That is precisely the condition the repo's `*BlockExpectation` guards exist
for. Separately, a declared block with no `EnsureBlockMatchesHandler` arm is dropped when it rides on
the wrong element type — the same silent shape, one layer in.

**T-16** The metadata writer skips a `Direction` equal to its default, and the default is `Variable`.
An absent `L12` therefore means bidirectional, not input.

**T-17** Multi-instance is **derived**, never declared: mapping a single incoming parameter to a data
collection converts a one-shot call into an N-instance loop, and the element's `BP2` then stops
mirroring the callee entirely. Only one collection may be mapped per element.

**T-18** **Five** agent-facing surfaces currently assert sub-processes are not buildable — the
`NotSupportedException` message tail in `ProcessElementFactory` ("… and sub-processes are not buildable
yet."), the `create-business-process` / `modify-business-process` `[Description]` token lists,
`ValidateProcessGraphTool.cs:50` ("Still NOT buildable: … sub-processes"), `docs/McpCapabilityMap.md`
(twice on one line), and five clio-knowledge articles that document `callActivity` as read-only.
Leaving any of them makes the shipped capability unreachable by the agent it was built for.

**T-19** `ProcessElementFactory.ResolveBuildType` takes the **first** `CanBuild` match, so a specific
handler must be registered before the generic `UserTaskElementHandler`.
`CrtProcessBuilderAppTests.CompositionRoot_RegistersEveryElementHandler` pins both the set and the
order and will redden — this one at least fails loudly.

**T-20** `ProcessModifyHandler` re-runs `_layoutEngine.Apply(schema)` over the whole schema on every
modify, overwriting hand-arranged positions.

**T-21** Notices reach the response only when the operation saved. A re-sync whose entire point is the
drift report returns nothing if a later operation in the same batch fails.

**T-22 / T-23** An install resolves the bundled `.gz` from the **build output**, so
`clio compress -d <repo path>` verifies nothing until clio is rebuilt; and `RequiredPackageChecker`
throws on a convergence refusal, so rebundling mid-review blocks the reviewer, not the author. Raise
`-Version` deliberately and land the package side first.

**T-24** The `[OperationContract]` count on `ProcessDesignService` is pinned at exactly **7**, and the
authorization-gate call-site count at exactly **5**
(`clio.tests/Common/BundledProcessBuilderPackageTests.cs:343` and `:331`). Neither is refreshed by
`rebundle-process-builder.ps1` — both are hand-maintained, and the package side moves first. A re-sync
must ride the existing modify path, not a new endpoint. *(An earlier draft of this document said 5 and
3; those were the numbers two research passes reported, and both were wrong. Read the file.)*

**T-26** `ProcessSchemaSubProcess(ProcessSchemaSubProcess source)` assigns `SchemaUId = source.SchemaUId`
at `:46`, i.e. the copy constructor fires the setter on an element that is not attached to anything —
so `Clone()` hits T-1 for the same reason a naive `Create()` does. Note also that
`ProcessSchemaBaseElement(ProcessSchema)` sets only `ProcessSchema`, never `ParentMetaSchema`
(`:60-63` vs `:69-72`), which is why `BaseProcessSchema` is `(ProcessSchema ?? ParentMetaSchema)` but
`ClearParametersSourceValue` still dereferences `ParentMetaSchema.UId` with no null guard.

**T-27** On a retarget, `GetRemovedSchemaParameters` removes the mapping row only in the
`source == null && !target.IsDynamic` arm. An owner-created (`IsDynamic`) element parameter therefore
keeps a row whose `SourceSchemaUId` names the **previous** callee, and `UpdateParameters` then
dereferences `mappingInfo.Source` on it. The schema saves green; nothing on the clio side looks for it
— `ProcessGraphValidator` has no parameter or mapping rule at all.

**T-29** **`odata-read` does not work on `VwProcessLib`** — it answers
`success:false, "The operation was canceled"`, while `execute-esq` over the same view returns the rows
fine. The view is how an agent discovers which processes it may call, so the OData dead end is the
difference between "discovery is impossible" and "discovery is one call". An earlier draft of this
analysis drew the wrong conclusion from exactly this.

**T-28** `ApplyMetaDataValue` writes the **field**, not the property:
`case SchemaUIdPropertyName: _schemaUId = reader.GetGuidValue(); break;` (`ProcessSchemaSubProcess.cs:214-216`).
So deserializing metadata — and a whole-schema save through
`Terrasoft.Nui.ServiceModel.WebService/ProcessSchemaManagerService.Post(ContractProcessSchema)`, which is
how the designer saves — **bypasses the setter and does not synchronize**. The sync on that path comes
later, from the read-time `FindDesignItem` / `GetItemFromMetaData` call. Do not assume "it was saved, so
it was synced".
