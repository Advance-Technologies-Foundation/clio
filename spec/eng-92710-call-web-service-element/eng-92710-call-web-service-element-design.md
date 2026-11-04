# ENG-92710 Call web service element: design

Written 2026-11-02, updated 2026-11-03. **Every decision this design
follows was agreed by the owner on 2026-11-03** ([decisions](eng-92710-call-web-service-element-decisions.md), Q1-Q20,
the "Agreed by the owner" line of each; where it differs from the recommendation above it, the agreed line is what this
design follows). Sections carry the Q number they implement. Facts and evidence: [facts](eng-92710-call-web-service-element-facts.md);
target bytes and the comparison rule: [serialization](eng-92710-call-web-service-element-serialization.md); what is
reused: [reuse](eng-92710-call-web-service-element-reuse.md); work order: [plan](eng-92710-call-web-service-element-plan.md)
(task ids P-*, CL-*, KB-* there map to the components C*, K*, KB-* here).

Basis: crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history), clio `origin/master`
`6be5dde28`, clio-knowledge `origin/master` `5f14ceb`, CrtProcessDesigner 7.8.0 + PackageStore corpus, core TSBpm trunk
and `<core>` `468c4c71b` (8.3.3.1722), Creatio Academy, read-only inventory of stand `Creatio`
(10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.131) taken 2026-11-02, and the stand measurements of 2026-11-03
(measurements M-01..M-31, decisions "Measured" blocks: the Q4 BK15 probe, EM0, EM1 with the three test services
`UsrWscEchoService`, `UsrWscDriftService` and `UsrWscDeadService`, EM2 S1, EM3 routes (a) and (b), EM4 S1-S3, the
designer halves of EM7, EM10 and EM11, EM8's current-builder, service-save and designer halves) and EM9 (local
compile). Open: EM5, EM6, every builder half (EM8's is the claimed-cut half, the N7 notice), EM2's read-back on the
final cut; EM12's run half is not measurable under E4. Research notes `r1`..`r10`, gap notes
`g1`..`g5`, verdicts in `_workflow-result.json` (`<research notes, local>/`); a verdict correction
overrides a note, a `g` note supersedes an `r` note.

Paths: `PB/` = `packages/CrtProcessBuilder/Files/src/cs/`, `PBT/` = `tests/UnitTests/CrtProcessBuilder.Tests/`,
`CORE/` = `<core>/TSBpm/Src/Lib/`, `NUI/` = `CORE/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/`,
`PD/` = `CrtProcessDesigner/branches/7.8.0/Schemas/`, `CLIO/`, `KB/` = repository roots.
Tags: **traced** = read in source, a hypothesis about run time until the named EM settles it; **measured** = corpus,
binary, git or stand inventory. EM ids: [measurements](eng-92710-call-web-service-element-measurements.md).

## 1. What the feature does, in one paragraph

An agent adds an element `{"type": "webService", "webService": {"service": ..., "method": ...}}` to a process on create,
`addElement` or `setElement`. The package resolves the service and method the way the platform will at run time (by
manager-item UId and ordinal method name), writes the canonical spelling, and generates the element's parameters itself
by porting the designer's client algorithm: one `<name>_In` per request parameter, one `<name>_Out` per response
parameter, recursive nested items for object arrays and one `ItemValue` child for scalar arrays (always, with no
feature gate, Q3), caption resources, then
the four untagged result parameters `Success`, `Response`, `StatusCode`, `RequestBody` and their BK15 rows (Q3, Q4).
The element is the core `ProcessSchemaWebService` activity, not a user task (Q1). Request values, a composite request
body and the raw `RequestBody` are set through the mappings the package already has, addressed by the stored names;
responses are read by mapping `_Out` parameters into declared process parameters (Q2, Q5, Q6). `describe-business-process`
returns the settings, the resolution state and the whole stored parameter tree, unbound inputs included, never a
service parameter default (Q14, Q16). A method or service change replaces the generated parameters as the designer
does, keeps the UIds of `Success` / `Response` / `StatusCode` so their readers stay valid, and is refused while
anything (another element, a process parameter, a flow condition, a formula) reads any other parameter of the element,
since all of them are regenerated with new UIds (Q11 A3). What only the run can see (the
platform sending no value for a sourceless inner object array whose items are bound, before ENG-55920 "Unable to use
array inside array in web-service business process element"; a failing
endpoint; feature `DisableIgnoreErrorsInWebServiceUserTask` switched on) is a notice or a guidance line.

## 2. Contract

### 2.1 Write: the `webService` block (Q2, Q8, Q9, Q7)

On `create-business-process` `elements[]`, `modify` `addElement.element` and `setElement.elementUpdate`:

```json
{
  "name": "CallEcho",
  "type": "webService",
  "caption": "Call echo service",
  "webService": {
    "service": "UsrWscEchoService",
    "method": "Echo",
    "timeoutSeconds": 30,
    "ignoreErrors": true
  }
}
```

(Stand `Creatio` holds three test services, base URI = the stand root (Q17, EP option E4) except `UsrWscDeadService`
(`http://127.0.0.1:1`, no listener): `UsrWscEchoService` with `UsrWscEcho` (POST, composite body, 3-level response),
`UsrWscGet` and `UsrWscMissing`; `UsrWscDriftService` with `UsrWscDrift`, `UsrWscTc12` and the 2-level response
method `UsrWscOrders2`; `UsrWscDeadService` with `UsrWscDead` (EM1, M-23). `Echo` and all parameter names below are
illustrative, not those methods.)

| Member | Type | Create / addElement | setElement | Rule |
|---|---|---|---|---|
| `service` | string | required | optional | Manager-item UId, else name, else caption; case-insensitive; ambiguity refused (Q9, 4.3). Stored as IX1 = the manager-item UId (`schema.uId`, not a package copy's `realUId`, g5 (a)) |
| `method` | string | required (the designer auto-selects a sole method, `_initDefaultServiceMethod`, r2, measured on `UsrWscDeadService` by EM7, and selects none on a 3-method service, EM2; the builder does not, Q2) | optional | Case-insensitive within the resolved service; the canonical `method.Name` is written to IX2 (g5 (b): run time and the provider look it up ordinally, a wrong case yields an empty parameter set silently) |
| `timeoutSeconds` | int | optional | optional | Whole seconds, the designer's unit; stored IX3 = value x 1000 ms; `0` or absent on create = unset (IX3 omitted = the method's own timeout, which includes retries, r7 A3 verdict); negative refused (the page's `positiveNumberValidator`); EM2 measured 7 s -> IX3 7000 and an unset field -> no IX3 |
| `ignoreErrors` | bool | optional | optional | IX4 `IsIgnoreErrors`, written directly (Q7: EM9 showed the package already fails to compile below core 8.3.2, so the reference adds no floor); `false` = omitted (default) |

Presence rules:

- Create / `addElement`: the block is required for `type: "webService"` (a block-less create is refused, like
  `SubProcessElementHandler.EnsureCalledProcessNamed`); the block on any other type is refused by the strict factory
  gate (`EnsureBlockMatchesHandler`, r4 §1).
- `setElement`: absent members are untouched. `method` alone = another method of the current service. `service` alone
  (no `method`) = refused with the new service's method list, nothing written (TC-U-108). Both equal to the stored
  (canonical) pair = no parameter change (Q11 A). `timeoutSeconds: 0` clears IX3; `ignoreErrors: false` clears IX4.
  A `setElement` that names `webService` but sets no member is refused by the existing "no field set" refusal
  (`PB/Operations/ElementOperations.cs:302-303`, which gains the block name).
- Not in the block: request values (2.3), any service parameter value, a base URI, auth settings, a `resync` flag
  (section 8). No alias names for parameters (Q2 A).

### 2.2 The parameters the block generates (Q3)

Names are what describe returns and what mappings address. Full byte rules: serialization file; summary (g1, r2 §3,
r6 C3 verdict):

| Service parameter | Element parameter | Notes |
|---|---|---|
| request scalar `title` (body, header, query, URL segment, cookie alike) | `title_In`, Tag `title`, In | type: Text, empty or unknown -> MaxSizeText; others by name |
| response scalar `status` | `status_Out`, Tag `status`, Out | never required |
| object array `items` with properties `name`, `parts[]` | `items_In` CompositeObjectList, L18 items `name_In`, `parts_In` (itself CompositeObjectList with its own L18) | recursive, any depth (corpus reaches 3-4, r6); L18 always (Q3, 4.4) |
| scalar array `tags` | `tags_In` ObjectList with one item `tagsItemValue_In`, Tag `tagsItemValue`, caption "Item value" | the child is computed with `isArray: false`, so it is required when `tags` is a required request array; the ObjectList itself never is (measured, EM2: `UsrWscIdsItemValue_In` `L6` true, its parent none) |
| single object `meta {source}` | flattened by the service designer into a scalar such as `MetaSource_In` (Path `$.meta.source`) | there is no non-array object parameter (57/57 Object params are arrays, r1 C8 verdict; Studio forces Is array on for Object, EM1, M-23) |
| - | `Success` (Boolean, Out, value `false`), `Response` (Text, Out), `StatusCode` (Integer, Out), `RequestBody` (Text, In) | untagged, after the service parameters; one BK15 row each (Q4) |

Order: request params in request order, response params in response order, then the four result params (g1 (a);
57/66 corpus elements, the 9 others re-synced later).

### 2.3 Request values through the mapping surface (Q2, Q5, Q10)

Request values use the existing `mappings[]` entries (create) and the `addMapping` operation (modify; there is no
separate set-mapping operation, `PB/ProcessDesignConstants.cs` `Operations`), `ProcessMappingDescriptor` members `elementName`, `elementParameter`, and one source of `value`, `expression`,
`processParameter`, `sourceElement` + `sourceElementParameter`, `sourceColumn`:

```json
"mappings": [
  { "elementName": "CallEcho", "elementParameter": "title_In", "processParameter": "Title" },
  { "elementName": "CallEcho", "elementParameter": "mode_In", "value": "full" },
  { "elementName": "CallEcho", "elementParameter": "items_In",
    "sourceElement": "ReadContacts", "sourceElementParameter": "ResultCompositeObjectList" },
  { "elementName": "CallEcho", "elementParameter": "items_In.name_In",
    "sourceElement": "ReadContacts", "sourceElementParameter": "ResultCompositeObjectList.Name" },
  { "elementName": "CallEcho", "elementParameter": "tags_In.tagsItemValue_In", "processParameter": "Tag" }
]
```

What the mapping engine does with each shape is already decided by `CollectionBindingRules` and needs no change for the
three corpus composite shapes (g3, traced; EM6 runs them):

| Shape | Plan the engine applies today | Run time (traced, EM6) |
|---|---|---|
| leaf of an IN collection <- scalar process parameter / constant / formula, container unbound (the 2 product composite bodies) | no P1 (source is not an item, or no structured source); written as given; container keeps `L8 {}` | the run time builds a ONE-item list from the leaf sources (`GetIsSourceValueDefined` counts nested sources) |
| item <- item of a Read data or WS collection output (single reference) | P1 binds the container to the source collection (innermost first), COL<->COL passes the exact-UId type rule | one row per source row |
| item <- formula reading row items | no rules run; the container must be mapped explicitly, as in the first example above | an unbound container evaluates the formula outside any row (g3 §3; guidance line, not a notice in v1, section 8) |
| COL item <- ObjectList item, ObjectList <- COL | refused by the exact-UId rule (g3 M5) | - |
| a source item deeper than the target | refused by P1's depth rule | - |

`RequestBody` is the escape hatch (Q5): a constant or formula (`expression`, for example a `string.Format(...)`
building JSON, the Gemini pattern, r7 A5 verdict) on `RequestBody`; when non-null it REPLACES the generated body
entirely (traced, `CORE/Terrasoft.Services/WebServiceUserTaskImplementation.cs:22-28`). No new process-parameter type
is added, and v1 neither checks a constant `RequestBody` as JSON nor warns when it and Body parameters are both bound
(Q5, 2026-11-03); the guide says when to use it (KB-1 item 6).

Same-request ordering (Q10): on create, service selection and parameter generation run in the `afterElementsPlaced`
hook (`ProcessBuildHandler.SelectReferencedSources`, the Sub-process / Pre-configured page slot, r4 §1, DQ-32), before
`ApplyDeclarativeContent` runs `mappings[]`, `typeFromElement` parameters and the other blocks, so all of them can
reference the generated names in the same request. On modify, `addElement` generates the parameters before the next
operation of the batch runs, so a later `addMapping` in the same call can target them. A mapping onto a name the method
does not produce is refused by the existing locator; for a web-service element the refusal appends the element's
top-level parameter names (5.1).

### 2.4 Reading the response (Q6, Q7)

- Scalars and the result params: map `<x>_Out`, `Success`, `StatusCode`, `Response` like any element output (into a
  process parameter, another element's input, a condition, a formula). Nothing is special-cased; 21/25 product
  elements never read `Success`/`StatusCode` (r6 §4.3 table; r6's summary says 22/25), but conditions on them work through the existing surface.
- Object arrays (v1, Q6): declare a process `Collection` with `itemProperties` (at most two collection levels,
  `MaxCollectionLevels = 2`), then map items: `targetProcessParameter: "Rows.Cells.Text"` <- `sourceElement: "CallEcho"`,
  `sourceElementParameter: "rows_Out.cells_Out.text_Out"`. P1 binds both container levels; result = the shipped Gemini
  shape byte for byte (g3 §4, traced write, measured target). The designer stores the same shape (EM3 route (b),
  M-25): its container picker offers no web-service object array, but binding an item binds the container itself,
  and every level holds `GS1` 3 with an `[Element:{ws}].[Parameter:{uid}]` token, so the builder binds the container
  as well as the items. The run time renames response keys Tag->Name only while feature `ProcessParameterCollections`
  is on (the DB row and the client value are on on the stand, EM0, M-22; every checked build 7.13-10.2.931 ships it on
  in Web.config, the value the re-keying reads, decisions Q3; the stand's run-time value is EM6's).
- Not reachable in v1 (ENG-92730 "Complex / structured parameter types" follow-up, section 8): a `typeFromElement` mirror of a WS collection output (refused:
  item Tags are not GUIDs), a scalar-array output into a declared parameter (ObjectList is not declarable; COL<-OL
  refused by type), a third collection level, the designer's "Create from element" shape (a `CompositeObject` wrapper
  with flat first-level items, measured EM3 designer half, Q6). C13 makes the two mirror cases refuse with a message
  that points to the declared-Collection route instead of today's "carries no column identity" text and, for ObjectList, instead of a
  silently shapeless parameter (r5 C6 verdict (1), traced).

### 2.5 Read back: `describe-business-process` (Q14, Q12, Q16)

The element gets `buildType: "webservice"` (the lowercase build token, free once a handler `CanBuild`s it) and a `webService` member:

```json
"webService": {
  "service": { "name": "UsrWscEchoService", "uid": "<manager-item UId>" },
  "method": "Echo",
  "timeoutSeconds": 30,
  "ignoreErrors": true,
  "serviceResolved": true,
  "methodResolved": true,
  "parameters": [
    { "name": "title_In", "tag": "title", "direction": "in", "type": "MaxSizeText", "bound": true },
    { "name": "mode_In", "tag": "mode", "direction": "in", "type": "MaxSizeText", "required": true, "bound": true },
    { "name": "tags_In", "tag": "tags", "direction": "in", "type": "ObjectList", "bound": false, "items": [
      { "name": "tagsItemValue_In", "tag": "tagsItemValue", "direction": "in", "type": "MaxSizeText",
        "required": true, "bound": true } ] },
    { "name": "rows_Out", "tag": "rows", "direction": "out", "type": "CompositeObjectList", "items": [ "..." ] },
    { "name": "Success", "direction": "out", "type": "Boolean", "result": true },
    { "name": "Response", "direction": "out", "type": "Text", "result": true },
    { "name": "StatusCode", "direction": "out", "type": "Integer", "result": true },
    { "name": "RequestBody", "direction": "in", "type": "Text", "result": true, "bound": false }
  ],
  "issues": []
}
```

| Member | Kind | Rule |
|---|---|---|
| `service`, `method`, `timeoutSeconds`, `ignoreErrors` | write members read back | `service` falls back to `{ "uid": <raw IX1> }` with no name when unresolved (SubProcessElementHandler.Describe precedent); `timeoutSeconds` = `ms / 1000` reported as a fraction, not rounded (Q8; e.g. 1500 ms -> 1.5; none in the corpus), absent when IX3 is 0; `ignoreErrors` read from IX4 directly (Q7), absent when false |
| `serviceResolved`, `methodResolved` | read-back | `ServiceSchemaManager.FindItemByUId(IX1) != null`; method found ORDINALLY (the run time's lookup, g4 §5); both survive the load sync, so they are reliable on every load path |
| `parameters` | read-back | the element's own tree after load, every parameter including unbound inputs (the generic `parameters[]` filter drops them, `PB/Describe/ProcessDescriber.cs:226`), recursive `items`; per node `name`, `tag`, `direction`, `type` (DataValueType name), `required` (true only), `bound` (inputs: own or nested source defined), `result` (true only, for the four untagged params). Never `caption` values beyond what the generic projection already returns, never a value, never a service parameter default (Q16). A bound input's value (a constant, a formula, `RequestBody` text) is one of the element's stored bindings (process data the caller can already read through describe, M-31), not service data: it comes back through the generic `parameters[]` as today, which the round-trip tests need |
| `issues` | read-back | sentences, only when a stored shape fails or surprises at run time (5.3) |

Rules:

- "Read from the element, never re-derived": on an interpretable process the element describe loads has ALREADY been
  re-synced flat by the platform on load (g4 §2), so a missing method shows as a tree without tagged params plus
  `methodResolved: false` (measured, EM11 designer half: a wrong-case `IX2` element was described with Success /
  Response / StatusCode only); the nested `L18` part is never touched by that sync and shows drift directly. A raw
  pre-sync read (SysSchema blob) is not in v1 (Q12 A; EM8 measured that the prune happens, 3.3).
- Generic `parameters[]` keeps its current projection. Measured on 1.6.6.131 (decisions Q14, the package's raw
  `DescribeProcess`; M-31): every output comes back with its `tag` and its nested tree (`itemProperties`); bound
  inputs come back with their value or token, source and `tag`; unbound inputs and an unbound `RequestBody` are
  omitted; there is no service, method, timeout or ignore-errors member. (A reply without tags or nesting seen the
  same day came from an older MCP clio build, not from the package.) `webService.parameters` is therefore the place
  that adds the unbound inputs and the `bound` / `result` flags, next to the tags and nested items it repeats. Items'
  `direction` comes from the stored `L12`.
- Resubmission: `service` is a string on the write side and an object here, so the guide says to send
  `webService.service.name` back as `service`; the read-back members are stripped before resubmitting (clio's key check
  tolerates them as read-back fields, K2).

### 2.6 Result additions on create / modify (Q10)

No new response member. The existing `warnings` list of `BuildProcess`, `ModifyProcess` and the version responses
(`PB/Contracts/BuildContracts.cs:124`, `ModifyContracts.cs:608`, `VersionContracts.cs:153,244`) carries, per
web-service element generated or regenerated in the request, one info notice listing the generated names compactly
(N1, 5.2), plus any notices of 5.2. The authoritative tree is describe's.

## 3. Modify semantics (Q11, Q12)

### 3.1 Mode selection

| `setElement` / `addElement` content | Mode | Parameters |
|---|---|---|
| `addElement` with the block, or create | Create | generated fresh (C3), BK15 rows written (C8) |
| `setElement`, service and method resolve to the stored pair (after canonicalisation) | Settings | untouched; only IX3 / IX4 change if sent |
| `setElement`, the method or the service differs | Update (retarget) | replaced (3.2) |
| `setElement` with `webService` on a designer-built multi-instance WS element (`MultiInstanceOptions != null`, stored as BP6) or onto a SOAP service | refused (C11, Q20; TC-U-48); describe reports the multi-instance element with an issue (TC-U-79) | untouched |
| any modify of the process, the WS element not named in the request | none | the platform's load sync applies (3.3) |

### 3.2 Retarget (Q11 A3)

1. Resolve the new service and method (C2) and shape the new tree (C3) before any write; every refusal fires here.
2. Use the dependency snapshot the edit pipeline has already captured once at the start of the request
   (`IProcessEditPipeline.Apply`; `PB/Graph/IProcessDependencySnapshot.cs`). The applier calls only
   `FindDependents(...)` and never `Capture`: a mid-batch capture would drop the pre-batch half, the defect class of
   ENG-100041 "Sub-process retarget guard scans the live schema instead of the request dependency snapshot".
3. Ask `IProcessDependencySnapshot.FindDependents(element, survivingParameterUIds: {Success, Response, StatusCode
   UIds}, includeOwner: false)`; the element's own parameters are all replaced, so its own references die with them.
   A dependent is anything (another element, a process parameter, a flow condition, a formula body) that reads ANY
   parameter of the element other than `Success` / `Response` / `StatusCode`: tagged inputs, nested items and
   `RequestBody` included, because all of them are regenerated with new UIds (the package's
   `ProcessElementDependencyScanner` walks `schema.Parameters` too, so a response mapped into a process parameter
   blocks the change; decisions Q11 A3, stated exactly at gate 1). Any dependent -> refuse, naming the dependent and
   the parameter it reads (ENG-100041
   "Sub-process retarget guard scans the live schema instead of the request dependency snapshot" /
   `SubProcessApplier.EnsureNoLiveDependents` pattern, `PB/Elements/SubProcessApplier.cs:753-800`). A same-named
   output of the new method does not keep its readers
   (fresh UId, TC-U-38). `Success`, `Response` and `StatusCode` are NOT checked: they exist on every method with a
   fixed type, and step 5 keeps their UIds, so their readers stay valid (Q11 A3, agreed 2026-11-03 after EM4). The
   designer gives them new UIds and leaves their readers dangling (EM4 S2, S3: the save reports "Schema has errors" and
   stores the process anyway); the capture oracle does not compare UIds, so keeping them does not break parity.
4. Record every INPUT binding that will be dropped, `RequestBody` included, in the write shape (`elementName`,
   `elementParameter`, the source member with its value: a constant's `value`, a formula's `expression`, a process
   parameter, an element output), decoded with `ProcessParameterService.DecodeElementSource`, nested items
   included. These values are the element's stored bindings (process data the caller can already read through
   describe, M-31), not service data, so Q16 does not strip them (4.16).
5. Remove all parameters and the element's BK15 rows, except that `Success`, `Response` and `StatusCode` are
   regenerated with their current UIds; write IX1 / IX2 (`IX3` kept on a method change and reset on a service change,
   `IX4` kept, EM4, M-26); generate and write the new tree and the 4 rows as in Create (the designer re-creates the
   rows too, M-26), each row with a fresh `GT5` (`Guid.NewGuid()`, C8) as on create: the designer keeps the old
   `GT5` values, which the oracle ignores (serialization W10). The element caption follows the designer (M-26): a service change always renames it to
   `<service caption> <N>` (4.4); a method change keeps it.
6. Notice N4 lists the dropped bindings with their values, so one call re-applies those whose target names survived
   (5.2).

The same pair sent again is not a re-sync: it changes nothing (Q11 A). A service that gained or lost parameters is
brought in line by the platform's flat load sync (3.3) and, for nested drift, reported by describe (5.3); a `resync`
member is a follow-up (section 8).

### 3.3 Unrelated modify and the latent loss (Q12 A)

Every design-time load re-syncs every WS element by Name (g4 §0-§3, traced; the prune half measured by EM11 and EM8,
2026-11-03): tagged params the method no longer
produces are removed (dependents get `IsValid = false` only when a parametrized element's `SourceValue` held the UId;
FormulaTask references stay dangling), missing ones are appended flat at the end with no `L18` and a fresh UId on every
load, matched ones are re-typed / re-captioned to the provider's view; an empty IX1 clears everything including the
result params. A modify of ANY element saves that state. The package does not try to prevent it in v1:

- At the end of every `ModifyProcess` / `ModifyProcessAsNewVersion`, each WS element NOT configured in this request
  whose `serviceResolved` or `methodResolved` is false gets notice N7 naming the element, the service or method and
  the consequence. The removed parameters themselves cannot be listed without a raw stored read (g4 §6 Q1); the
  notice says so. EM8 (current-builder half, measured 2026-11-03): one unrelated `addParameter` through current clio
  deleted all 8 tagged params of an element with a wrong-case `IX2` from the database, with no warning, and left
  `IX2` unchanged. Q12 A is agreed (notice, no refusal); B (a refusal with a confirm flag) is a follow-up option.
- The builder writes every top-level parameter so the provider's view of it equals what the package wrote (names,
  Tag, types, IsRequired, `IsValueSerializable = true`, g4 §5), so the load sync is a no-op on a builder-made element
  EXCEPT for a caption that carries the designer's " - Input" / " - Output" suffix: the provider clones the bare caption
  and `SynchronizeParameter` overwrites `Caption.Value` on a match (traced, g4 §1 step 4; r2 §5 measured one drifted
  display value). EM5 measures it; the parity rule names it.
- UIds of flat-appended params are unstable until a save (g4 §4); no notice, guard or mapping keys on such a UId.

### 3.4 Other operations

- `removeElement`: already deletes every BK15 row whose target path holds the element UId
  (`PB/Graph/ProcessGraphBuilder.cs:1915-1922`, g1 (c)); no change.
- `modify-business-process-as-new-version`: the clone serializes the (synced) source instance and replaces the source
  schema UId as text (g4 §3; `PB/Design/ProcessVersionCloneFactory.cs:61-72,106-119`), so `A3`/`A4`/`GS5` on all levels
  and the BK15 `A3`/`A4` follow. No code expected; a clone round-trip test pins it (plan P-D4).
- Raw mapping operations onto the element's params: allowed, they are the request-value surface (2.3). No guard like
  Send email's slot guard is needed: the element owns no parameter that only a block may write.

## 4. Package components

### 4.1 Overview

| # | Component | Kind | Files (expected) | Plan |
|---|---|---|---|---|
| C1 | Contracts: `WebServiceDescriptor`; `ProcessElementDescriptor.WebService`, `ProcessElementUpdateDescriptor.WebService`; `DescribeWebServiceInfo`, `DescribeWebServiceService`, `DescribeWebServiceParameter`, `DescribeProcessElement.WebService` | contracts | `PB/Contracts/ProcessDescriptorContracts.cs`, `ModifyContracts.cs`, `DescribeContracts.cs` | P-B1 |
| C2 | Service catalog / resolver | new | `PB/WebService/IWebServiceCatalog.cs`, `WebServiceCatalog.cs`, `WebServiceResolution.cs` | P-A1 |
| C3 | Parameter shaper (pure) + parameter writer | new | `PB/WebService/IWebServiceParameterShaper.cs`, `WebServiceParameterShaper.cs`, `WebServiceParameterPlan.cs`, `IWebServiceParameterWriter.cs`, `WebServiceParameterWriter.cs` | P-A2 |
| C4 | Platform-sync seam | new | `PB/WebService/IWebServicePlatformSync.cs`, `WebServicePlatformSync.cs` | P-A3 |
| C5 | Generalized element snapshot / diff / drift report | refactor | `PB/Elements/SubProcessApplier.cs` (private `Snapshot`/`Diff`) -> `PB/Elements/ElementParameterSnapshot.cs`, `ElementParameterDiff.cs`; `SubProcessSyncReport.cs` reused | P-A4 |
| C6 | Element handler, identity, token, factory gate, DI | new + change | `PB/Elements/WebServiceElementHandler.cs`, `PB/WebService/WebServiceElementIdentity.cs`, `PB/ProcessDesignConstants.cs` (`ElementTypes.WebService = "webservice"`), `PB/Elements/ProcessElementFactory.cs`, `../CrtProcessBuilderApp.cs` | P-B2 |
| C7 | Applier: Create / Settings / Update modes, retarget guard | new | `PB/WebService/IWebServiceApplier.cs`, `WebServiceApplier.cs`, `WebServiceApplyMode.cs` | P-B3, P-D1, P-D2 |
| C8 | BK15 result-mapping writer | new | `PB/WebService/IWebServiceResultMappingWriter.cs`, `WebServiceResultMappingWriter.cs` | P-B4 |
| C9 | Build hook and modify wiring | change | `PB/Design/ProcessBuildHandler.cs` (`SelectReferencedSources`), `PB/Operations/ElementOperations.cs` (`addElement`, `setElement`, the "no field set" list) | P-B5 |
| C10 | Describer | new | `PB/WebService/IWebServiceDescriber.cs`, `WebServiceDescriber.cs`; called from the handler's `Describe` | P-C1, P-D3 |
| C11 | Guards | new + change | `PB/WebService/WebServiceGuards.cs` (multi-instance and SOAP refusals, `timeoutSeconds` sign, the N3 gate); `PB/Elements/UserTaskElementHandler.cs` (generic-route refusal) | P-C2 (refusals, generic-route and SOAP/multi-instance guards) |
| C12 | Notices and end-of-request rules | new + change | `PB/WebService/WebServiceNotices.cs`, `WebServiceConfigurationRule.cs`; `PB/Elements/ElementConfigurationLedger.cs` registration | P-C3, P-D3 |
| C13 | Mirror refusals for WS outputs | change | `PB/Parameters/ProcessParameterService.cs` (`CloneItemProperties` message, ObjectList mirror source refused) | P-C6 (Q6 v1; ~0.5 agent h, inside estimate WP C) |
| C14 | Package docs | docs | `docs/web-service-element-capture.md`; `docs/process-builder-architecture.md` section 3.5 handler list | P-C5 |

A new folder `PB/WebService/` follows the `Email/` and `FileProcessing/` precedent; the handler stays in `Elements/`
with the others. Note: `PB/WebService/` is a sibling of `PB/EntryPoints/WebService/`, the package's own WCF entry
point (`ProcessDesignService`), not part of it; the similar names are deliberate, and this section is the single source
for the folder (reuse §5.1 follows it). Every behaviour class gets an interface and a scoped DI registration in `CrtProcessBuilderApp.cs`
(repository rule; composition-root tripwire `PBT/CrtProcessBuilderAppTests.cs`). C5 lands in its own commit within
slice A (P-A4), after a characterization commit that pins each sub-process notice text (TC-U-93), with every
`SubProcess*Tests` unchanged, which proves it behaviour-neutral. No new `[OperationContract]`: the two security pins stay 8 operations / 6 gate call sites (r4 H9).

### 4.2 C1: contracts

- `WebServiceDescriptor` `[DataContract]`: `service` (string), `method` (string), `timeoutSeconds` (int?),
  `ignoreErrors` (bool?). Nullable members distinguish absent from `0` / `false` on `setElement`.
- `[DataMember(Name = "webService")]` on `ProcessElementDescriptor` and `ProcessElementUpdateDescriptor`. Before
  CrtProcessBuilder 1.6.6.136 WCF `DataContractJsonSerializer` drops undeclared keys silently; from 1.6.6.136 (clio
  master `f86d8098f`) the server refuses them (`UnknownRequestKeyDetector`). So every member is declared; the
  read-back-only keys are derived automatically because the write and describe members are both keyed `webService`;
  nothing to register (at 1.6.6.136 `ReadBackKeys` is derived from `RootPairs`, which pairs both descriptors with
  `DescribeProcessElement`; TC-U-109 replays a described block through the server key check), and clio mirrors the
  key set for older servers (K2).
- Describe: `DescribeWebServiceInfo {service, method, timeoutSeconds, ignoreErrors, serviceResolved, methodResolved,
  parameters, issues}`, `DescribeWebServiceService {name, uid}`, `DescribeWebServiceParameter {name, tag, direction,
  type, required, bound, result, items}`; `[ExcludeFromCodeCoverage]` like the other DTOs; `EmitDefaultValue = false`
  on the optional members so `required: false` and `result: false` are absent.
- Wire-contract test rows for every new DataMember (`PBT/EntryPoints/WebService/ProcessDesignServiceWireContractTests.cs`);
  operation count unchanged.

### 4.3 C2: service catalog / resolver (Q9, Q16, Q20)

The only class that touches `ServiceSchemaManager`, reached as the platform provider reaches it
(`SchemaManagerProvider.GetManager("ServiceSchemaManager")`, r4 §3); tests substitute it through
`TestUserConnection.SetServiceSchemaManager` (r8 B2).

| Member | Behaviour |
|---|---|
| `ResolveService(string reference)` | 1. parses as a Guid -> `FindItemByUId`; 2. name: exact ordinal match, else the case-insensitive matches over all manager items (workspace-wide, like the designer when `AutoAddPackageDependenciesInProcesses` is on and like sub-process lookup, g5 (a)); two or more that differ only in case -> refused (none in the corpus, defensive, g5 open Q4); 3. caption, case-insensitive, any culture the item exposes; two or more -> refused listing their NAMES. Returns `{UId (manager-item), Name, Caption, PackageUId, Type (REST / SOAP 1.1 / SOAP 1.2)}` |
| `ResolveMethod(service, string reference)` | `ServiceMethods.FindByName` (ordinal), else case-insensitive scan; two case-only twins refused; returns the method (canonical `Name`, request / response `ServiceParameter` trees with `Name`, `Caption`, `DataValueTypeName`, `IsArray`, `IsRequired`, `ItemProperties`) |
| `ListServiceNames()`, `ListMethodNames(service)` | sorted, sanitized (`SafeText.Sanitize`); used only in refusals; the service list gives each name with its caption (Q9, Q10). 32 service schemas and 63 methods in the PackageStore corpus, max 7 per service (g5 (b), measured); stand `Creatio` ships 3 service schemas (`BingSearchApiCall`, `GeminiApiSearch`, `CreatioMarketplaceApi`, measured 2026-11-03) and holds the 3 `UsrWsc*` test services of EM1, all REST, so listing all is cheap. An empty catalog gets its own refusal (5.1) |
| `TryFind(Guid)` | null on a dangling IX1; describe never throws |

Rules: the catalog never reads `DefValue`, `authInfo`, base URI or header values into any DTO the package returns or
logs (Q16; corpus holds literal basic-auth passwords and `Bearer` header defaults, g5 (c), measured). A cross-package
service (the service's package not in the process package's dependency closure) is not refused: run time resolves
workspace-wide and the builder's save adds no dependency (g5 (b), traced; EM10 builder half); notice N6. The designer
does add it (EM10 designer half, measured 2026-11-03: one new `SysPackageDependency` row on the first save of a
process in a package with none); adding it here is a separate ticket (Q9 C). The closure read is the one open API
choice of this component (the platform's package-hierarchy helper, else `SysPackageDependency`).

### 4.4 C3: parameter shaper and writer (Q3)

The shaper is a pure function `(ServiceMethod, ShapeContext) -> WebServiceParameterPlan`, a port of
`NUI/process-webservice-schema.js` (`_getParameterConfig`, `_getItemProperties`, `_isParameterRequired`,
`_initResultParametersMetaData`, the caption-suffix rule). No platform mock is needed to test it (r8 B4).

| Rule | Value |
|---|---|
| name | `<service param Name>` + `_In` (request) / `_Out` (response), at every level |
| Tag (L17) | the service param `Name`; for a scalar array's child `<Name>ItemValue` |
| type | empty, unknown or Text -> MaxSizeText (`c0f04627`); `IsArray` && `Object` -> CompositeObjectList (`651ec16f`); `IsArray` other -> ObjectList (`4b51a8b5`) with one child; other names -> the DataValueType of that name (Integer, Boolean, Float, Date, DateTime, Guid, ...). A non-array `Object` (0 in the corpus; the service designer forbids it) -> refused, 5.1 |
| IsRequired (L6) | `(!IsArray || type == Object) && IsRequired && direction == In`; the ItemValue child evaluated with `isArray: false` |
| nesting (L18) | recursive over `ServiceParameter.ItemProperties` for object arrays; one `ItemValue` child for scalar arrays; always generated, with no feature read (Q3, 2026-11-03). The designer's client gates `L18` on `ProcessParameterCollections` (r2 §3, r7 A6 verdict) through a DB row that a clean 8.2.1+ database lacks (ENG-68545 "No data binding exists for ProcessParameterCollections feature"), so it can emit flat composites; the builder does not copy that defect. Every checked build 7.13-10.2.931 ships the feature on in Web.config / WebHost config, which is what the run-time re-keying reads (decisions Q3); on the stand the DB row and the client value are on (EM0, M-22), the run-time value is EM6's (r3 C6 verdict (5)) |
| stamps | `A3 = A4 = process UId`, `IL2 = element UId`, at every level (g1 (b), 751/751 measured); `L12` on every parameter; `L8` empty, except every Boolean parameter (tagged, nested or Success) gets Value "false" and ModifiedInSchemaUId = process UId, i.e. `{"GS2":"false","GS5":<P>}` (serialization §2.4, I7); `IsValueSerializable = true` |
| captions | the service param caption in every culture it carries; " - Input" / " - Output" appended when the same caption occurs more than once across request and response (top level only: a nested item never gets the suffix, measured by EM2, M-24); `ItemValue` child "Item value" |
| result params | `Success` Boolean Out, `Value = "false"` and `ModifiedInSchemaUId = process UId` (the 61/66 shape `{GS2:"false", GS5}`); `Response` Text (`8b3f29bb`) Out; `StatusCode` Integer Out; `RequestBody` Text In; untagged, no L6 / L18; captions "Success", "Response body", "Http status code", "Request body" in en-US (EM2, M-24); other cultures not measured (EM2 ran under en-US; the corpus has the designing user's culture written under en-US in 9 elements, g1 (b): drift, not a rule) |
| order | request, response, result params |
| duplicate guard | two nested items of one composite parameter sharing a Name or a Tag anywhere in its tree -> notice N9 (the run time builds one flat `Dictionary` with `Add` and throws, r3 C6 verdict (3), g3 §6) |

The writer applies a plan to an attached element: Create (fresh UIds, `Parameters.Add` in plan order, nested items
added to `ItemProperties` before the parent is added, the `MultiInstanceApplier.AParameter` /
`ProcessParameterService.StampAsCreatedIn` idiom), Remove-all (for Update). Captions are set on
`ProcessSchemaParameter.Caption`; the platform serializes them to `BaseElements.<El>.Parameters.<name path>.Caption`
(g1 (b)); the parity test reads the resource rows. The element caption, when the caller gives none, is
`<service caption> <N>` like the designer's `generateItemUniqueName` (r2 §6; measured, EM2: "WSC echo service 1").

### 4.5 C4: platform-sync seam (Q3)

`IWebServicePlatformSync.Synchronize(element)` wraps `ProcessSchemaWebService.SynchronizeParameters()`. Production code
does NOT call it in v1: the load path runs it anyway (3.3), and in the package harness the provider is null
(`CoreApiContainer.Resolve` returns null; the call NREs, r8 C2 verdict). The seam exists for the one probe test
(`SubProcessPlatformProbeTests` pattern; the provider can be bound by reflection before the element is constructed, r8
C2 verdict) that proves on both target frameworks that the platform sync leaves a C3-shaped element unchanged except
for the caption suffix. If the probe shows otherwise, the shaper rule is corrected, not the sync wrapped around.

### 4.6 C5: generalized snapshot / diff

`SubProcessApplier`'s private `Snapshot` / `Diff` are typed to `ProcessSchemaSubProcess` (r4 §2). Lifted into
`ElementParameterSnapshot` / `ElementParameterDiff` over `ProcessSchemaParametrizedFlowNode`, producing the existing
`SubProcessSyncReport` shape (Added / Removed / Renamed / Retyped / ValuesCleared / DanglingReferences). C7 uses it for
the retarget notice and the dropped-binding list; sub-process behaviour and texts stay byte-identical.

### 4.7 C6: handler, identity, factory, DI

- `WebServiceElementHandler : ProcessElementHandlerBase`: `SupportedTypes = { ElementTypes.WebService }`,
  `DefaultSize = (Layout.TaskWidthPx, Layout.TaskHeightPx)` (69 x 55, measured equal to corpus `BN2`), `Create` builds
  `new ProcessSchemaWebService()` (no `(ProcessSchema)` ctor exists, r4 §3), sets `UId`, attaches it, and stamps
  `ManagerItemUId = ProcessSchemaElementManager.WebServiceTaskUId` (`652b598d-...`); the ctor already sets
  `SerializeToDB`, `IsLogging`, `BpmnElementName`. A block-less create is refused (5.1). `CanBuild` / `CanDescribe` =>
  `element is ProcessSchemaWebService`. `Configure` delegates to C7 (Create mode) unless the build hook already ran.
  `Describe` delegates to C10.
- `WebServiceElementIdentity` (like `SubProcessElementIdentity`): `IsWebService(element)`, the ManagerItemUId, the
  result-parameter names, the legacy `WebServiceUserTask` schema UId `e7acb2e5-efc6-41d3-8c02-589ae6b943f1`.
- Factory: one strict `EnsureBlockMatchesHandler(descriptor.WebService != null, "webService", ElementTypes.WebService, ...)`
  line (strict like `subProcess` / `scriptTask`, `PB/Elements/ProcessElementFactory.cs:100-113`).
- DI (`CrtProcessBuilderApp.cs:124-180`): `AddScoped<IProcessElementHandler, WebServiceElementHandler>` placed so no
  earlier handler `CanBuild`s the element (none does: every other handler keys on its own class); scoped registrations
  for C2, C3 (shaper and writer), C4, C7, C8, C10, C11.

### 4.8 C7: applier (Q9, Q11, Q7, Q8)

`IWebServiceApplier.Apply(ProcessSchema schema, ProcessSchemaWebService element, WebServiceDescriptor block,
WebServiceApplyMode mode, IProcessDesignNotices notices)`; `WebServiceApplyMode { Create, Update }` (the
`SubProcessApplyMode` precedent; Settings is Update with an unchanged pair).

1. Guards that need no resolution (C11): multi-instance, `timeoutSeconds` sign.
2. Resolve service and method (C2); SOAP refused (Q20) unless an owner-requested SOAP capture shows the same shape;
   shape the plan (C3). All refusals happen here, before the first write ("untouched on refusal", the Send email
   applier invariant).
3. Create: write IX1 (manager-item UId), IX2 (canonical), IX3, IX4 (directly, Q7); write the plan (C3); write the BK15
   rows (C8); N1, and N3 when the C11 gate applies.
4. Update with a changed pair: section 3.2 (snapshot, retarget guard over C5 and `IProcessDependencySnapshot`,
   dropped-binding list, remove all, then step 3); N4.
5. Update with the same pair: IX3 / IX4 only; no parameter touched.
6. Never calls `SynchronizeParameters()` (4.5). Never writes a service parameter default into `L8` (the designer does
   not either: generated params are `Source = None`); the Boolean `"false"` of C3 is the type default the designer
   stamps, not a service default.

### 4.9 C8: BK15 result-mapping writer (Q4)

The first BK15 rows the package authors (g1 (c): none on `origin/main`). One `ProcessSchemaMapping` per result param,
mirroring the client's `createMappingInfo`: `A2` = element name, `A3 = A4` = process UId, `GT2` =
`[Element:{el}].[Parameter:{p}]`, `GT3` = the param UId, `GT5` = `Guid.NewGuid()`, no `GT4`, `GT1` = a SEPARATE value
object (Success: `Value = "false"` without `ModifiedInSchemaUId`, which gives the measured `{"GS2":"false"}`; the others
empty). Order: set the parameter's `SourceValue` first, then add the row, so the `SourceValue` setter does not alias the
row's value and write `GS5` into Success `GT1`. Later bindings of `RequestBody` through the mapping service keep `GT1`
equal to `L8` for free (the setter, `PB/Mappings/ProcessMappingService.cs:221-235`), which reproduces Gemini. `A2` is
not rewritten when the element is renamed, as the designer does not rewrite it either (Q4). Rows are runtime-neutral
(traced, g1 (c): no run-time, server-sync or client-sync reader depends on them). Measured on stand `Creatio`
2026-11-03 (Q4 probe `UsrWscBk15Probe`): a designer-built element gets exactly these 4 rows in these keys; the
designer's own copy is row-less, a re-save keeps it row-less and leaves the original's 4 rows unchanged (no duplicate,
no rewrite), and the row-less element runs. Q4 A is agreed (write the rows); EM5 checks that a designer re-save leaves
builder-made rows alone too. Should it not, the row-less state is one the designer itself produces and runs, so the
fallback is a flag, not a redesign.

### 4.10 C9: build hook and modify wiring (Q10)

- `ProcessBuildHandler.SelectReferencedSources` (`PB/Design/ProcessBuildHandler.cs:157-205`) gains a branch: for every
  placed `ProcessSchemaWebService` with a block, C7 Create. It runs before `ApplyDeclarativeContent`
  (`:546-605`), so `mappings[]`, `typeFromElement` parameters, other blocks and formulas see the generated names.
- `ElementOperations.addElement` (`PB/Operations/ElementOperations.cs:60-160`): C7 Create, then `ConfigureElement`,
  filter, ledger, as for sub-process. `setElement` (`:250-470`): C7 Update. The "no field set" refusal (`:302-303`)
  names `webService`.
- `ElementConfigurationLedger`: a configuration rule for the kind (C12), since "a kind with no configuration rule yields
  nothing" (`ElementOperations.cs:136-140`).

### 4.11 C10: describer (Q14, Q12)

Reads IX1-IX4 directly; resolves the service name through C2 (`TryFind`; never throws); computes
`methodResolved` ordinally; projects the element's parameter tree as stored after load (2.5); computes the issues
(5.3). Nested drift = the element's `L18` tree compared by NAME with the resolved method's `ItemProperties`, under the
same rules C3 uses (missing item, extra tagged item, type differs); the comparison skips the top level, which the load
sync has already aligned (g4 §5). Runs on every web-service element, including designer-built and corpus ones; the
corpus pattern-A processes are its fixtures (plan P-C1, P-C4).

### 4.12 C11: guards (Q1, Q7, Q13, Q20)

`WebServiceGuards` keeps a job: the multi-instance, SOAP and `timeoutSeconds` refusals and the N3 gate; the
generic-route refusal sits in `UserTaskElementHandler`. The platform-capability guard (a reflection read of
`IsIgnoreErrors`, its fake-absence tests and the "no IX4 on this version" refusal) is dropped by Q7, 2026-11-03: EM9
(local compile) showed `origin/main` already fails on 8.3.1.4498 and 8.1.3.6947 for other reasons, so the package's real
floor is about 8.3.2 and a direct `IsIgnoreErrors` reference adds none (not measured: builds between 8.3.1.4498 and
8.3.2.4199, none a supported target).

| Guard | Where | Behaviour |
|---|---|---|
| IX4 feature gate | one single-purpose reader of `DisableIgnoreErrorsInWebServiceUserTask` (over `Features.GetIsEnabled`), behind an interface with a test fake, as the `FileProcessingFeatureReader` precedent (TC-U-96); the only feature read left (the shared reader for the `L18` gate went with Q3) | `ignoreErrors: true` while the feature is on -> saved, notice N3 (run time ignores the flag, g2 (b) traced; the designer hides the checkbox) |
| generic userTask route | `UserTaskElementHandler`, keyed on the resolved schema UId `e7acb2e5-...`, pattern `RefuseUnconfigurablePageTask` (`PB/Elements/UserTaskElementHandler.cs:382-398`) | refused with a pointer to `type: "webService"` (Q1); today it would build the legacy SOAP stub that 0 processes use (r4 H2) |
| multi-instance | `MultiInstanceOptions != null` on the element (BP6; 0 in the corpus) | `setElement.webService` on a designer-built element carrying BP6 refused (Q20, TC-U-48); describe issue (TC-U-79). On create there is no such block member: clio's key check refuses `webService.multiInstanceOptions` before sending (TC-C-02, TC-I-03), so the package sees no create case |
| SOAP | `Type` from C2 is SOAP 1.1 / 1.2 (0 SOAP calls in the corpus) | refused with notice text unless an owner-requested SOAP capture shows the parameter shape is identical (Q20; every service on stand `Creatio`, the 3 shipped and the 3 test services, is REST; EM2's SOAP save S2 was not requested) |
| non-array Object service param | C3 | refused (unreachable through the service designer) |
| nested array-in-array request | end-of-request rule (C12) over every WS element touched by the request: an IN CompositeObjectList item nested in another collection whose own source is None while a leaf below it has a source (the exact pre-fix defect predicate, r1 C7 verdict (a)) | notice N5; the package cannot cheaply read whether the target carries the ENG-55920 fix, so it warns on every target, names the version line and the remedy (map the container) (Q13); suppressing it on fixed builds is decided after EM12 |

### 4.13 C12: notices and end-of-request rules

`WebServiceNotices` owns every sentence of 5.2 (one place, sanitized names, no service data; only N4 carries values:
the element's dropped stored bindings, process data the caller can already read through describe, M-31). `WebServiceConfigurationRule`
judges at the end of the request every WS element configured in it: required inputs without any source (N8, the
designer's own validation marks such an element invalid, r2 §6), N5, N9; and, on modify, every WS element NOT
configured in it for N7. Notices land in `warnings`.

### 4.14 C13: mirror refusals (Q6 v1)

- A `typeFromElement` mirror whose source is a WS CompositeObjectList output is refused today with "carries no column
  identity" (`PB/Parameters/ProcessParameterService.cs:991-1004`). The message gains, for a WS source only, the route
  that works: "declare a Collection with itemProperties and map the items from '<el>.<param>'".
- A mirror whose source is a WS ObjectList output silently creates a parameter with no shape and no binding (r5 C6
  verdict (1), traced): refused with the same pointer.
- No relaxation of the GUID-Tag rule, no ObjectList type: ENG-92730 (section 8).

### 4.15 C14: package docs

`docs/web-service-element-capture.md` (the `docs/<element>-capture.md` precedent, r4 §5): corpus survey, element keys
IX1-IX4, BP2 key sets, BK15 rows, what the builder writes, load-path drift, the EM results with dates; the handler line
in `docs/process-builder-architecture.md` section 3.5.

### 4.16 Security (Q16)

- No new privilege: saving through the builder needs the process-design rights the designer save needs; the block adds
  no operation, so `EnsureCanManageProcessDesign` coverage is unchanged (8 / 6 pins).
- No SERVICE-side data appears in builder output (describe, refusals, notices, logs, test snapshots of builder
  output): no service parameter default, no `authInfo`, no base-URI credential, no header default. Names and
  captions of services, methods and parameters are not secrets.
- The mapped values (constants, formulas, `RequestBody` text) are the element's stored bindings (process data the
  caller can already read through describe, M-31), not service data: describe returns a bound input's value through
  the generic `parameters[]` as today (the round-trip tests need it), and N4 lists every dropped input binding,
  `RequestBody` included, with its value, so one call re-applies it. The `webService.parameters` tree carries no
  value. The KB article tells agents that a key or token comes from a system setting or a process parameter, never a
  constant (KB-1 item 4).
- Residual risk (recorded here only): a designer-built element may hold a key a human typed as a constant. Describe
  already returns it today, and N4 returns it on a retarget; the KB rule (keys from a system setting or a process
  parameter) binds agents only.
- Fixtures: AC8 / Q16 govern builder OUTPUT. The pattern-A corpus fixtures are copied verbatim from the shipped
  PackageStore corpus into the internal package repository and SHA-pinned, so they are not
  redacted; they may carry the shipped services' non-credential `IU5` defaults. TC-P-08 asserts that they carry no
  credential (no `IQ5` value with `IV2` 1, no Const `Authorization` default). Unit fixtures for a secret-bearing
  service (the corpus's literal basic-auth passwords and `Bearer` defaults) use masked synthetic values; a test
  asserts that none of them reaches any reply.
- The web service's own call runs with the service schema's auth configuration at run time; the builder neither reads
  nor changes it.

## 5. Messages (drafts; the review settles the wording)

All names pass `SafeText.Sanitize`; lists are sorted; no values, except the element's dropped stored bindings in N4
(process data the caller can already read through describe, M-31).

### 5.1 Refusals

| Case | Draft |
|---|---|
| block missing on create | "Element 'CallEcho' has type webService but no webService block. Send webService.service and webService.method." |
| block on another type | existing factory sentence, naming `webService` |
| unknown service | "Element 'CallEcho': no registered web service is named 'UsrEcho'. Registered services: A (caption), B (caption), C (caption). If the service you need is not listed, it has to be registered first in Studio > Web services (base URI, methods, authentication); then repeat the call." (Q10 addition 1) |
| no service registered | "Element 'CallEcho': no web services are registered on this environment. Register the service in Studio > Web services (base URI, methods, authentication), then repeat the call." (Q10 addition 1) |
| ambiguous service | "Element 'CallEcho': 'echo' matches several web services (UsrEcho, UsrECHO). Send the exact name or the service UId." |
| unknown method | "Element 'CallEcho': web service 'UsrWscEchoService' has no method 'Echoo'. Its methods: Echo, Ping." |
| ambiguous method | "... matches several methods of 'UsrWscEchoService' (Echo, ECHO). Send the exact name." |
| `service` alone on setElement (no `method`; nothing written, TC-U-108) | "... webService.service changes the service; send webService.method too. Methods of 'X': ..." |
| SOAP service (Q20) | "Element 'CallEcho': 'X' is a SOAP service; Call web service elements are built here only for REST services. Configure it in the Process Designer." |
| multi-instance element (Q20) | "Element 'CallEcho' is a multi-instance Call web service; it is not configurable through this tool." |
| negative timeout | "webService.timeoutSeconds must be 0 (method default) or a positive number of seconds." |
| non-array object param | "Method 'X.Echo' has an object parameter 'p' that is not an array; such parameters are not supported." |
| retarget with dependents (Q11) | "Element 'CallEcho' cannot switch to 'X.Ping': its parameters are still read by <dependent element, process parameter, flow or formula> (<parameter read>), .... Remove or change those uses first." |
| generic route (Q1) | "User task 'WebServiceUserTask' is a legacy SOAP task. Build a Call web service element with type webService and its webService block instead." |
| mapping onto a name the method does not produce | the existing locator sentence + "Parameters of 'CallEcho': title_In, mode_In, items_In, ..., Success, Response, StatusCode, RequestBody." |
| typeFromElement on a WS output (C13) | "'CallEcho.rows_Out' cannot be mirrored: its items carry service names, not column identities. Declare a Collection with itemProperties and map the items from 'CallEcho.rows_Out'." |

### 5.2 Notices (warnings)

| # | Case | Draft | Fires |
|---|---|---|---|
| N1 | parameters generated (info, Q10) | "CallEcho calls UsrWscEchoService.Echo. Inputs: title_In, mode_In*, tags_In[tagsItemValue_In*], items_In[name_In, parts_In[...]], RequestBody. Outputs: status_Out, rows_Out[...], Success, Response, StatusCode. (* = required)" | each Create and retarget |
| N2 | canonicalised name | "webService.method 'echo' was written as 'Echo'." | Create and retarget only, on a case-insensitive hit; a setElement re-sending the same service and method in different case writes nothing and raises no notice (4.8 step 5) |
| N3 | IX4 feature gate | "ignoreErrors is saved, but feature DisableIgnoreErrorsInWebServiceUserTask is on, so a failing call still stops the process." | `ignoreErrors: true` while the feature is on |
| N4 | retarget (Q11 A3) | "CallEcho now calls X.Ping; its parameters were replaced; Success, Response and StatusCode kept. Dropped input bindings: { \"elementName\": \"CallEcho\", \"elementParameter\": \"title_In\", \"processParameter\": \"Title\" }, { \"elementName\": \"CallEcho\", \"elementParameter\": \"mode_In\", \"value\": \"full\" }, { \"elementName\": \"CallEcho\", \"elementParameter\": \"RequestBody\", \"expression\": \"...\" }, ...; re-add those that still apply." Every dropped input binding, `RequestBody` included, in write shape WITH its value (constants and formulas alike), so one call re-applies those that still apply; the values are the element's stored bindings (process data the caller can already read through describe, M-31), not service data (Q16, 4.16) | each retarget with at least one dropped binding |
| N5 | nested array-in-array (Q13) | "CallEcho: 'items_In.parts_In' has no source of its own while its items are bound. Creatio builds before the platform fix for arrays inside arrays (10.1.37 among them) send no value for this inner list; bind 'items_In.parts_In' to a collection, or build the body in RequestBody." | end of request, per touched element |
| N6 | cross-package service (Q9) | "Service 'X' is in package P2, which package P1 of this process does not depend on. The Process Designer adds that dependency when it saves; this tool does not. The process runs here, but an export of P1 alone fails at run time; add the dependency." (designer half measured, EM10) | Create / retarget |
| N7 | unresolved element on modify (Q12) | "WebService3 calls method 'GetTranscripts', which service 'UsrMsGraph' no longer has. Saving this process lets Creatio remove that element's service parameters (Creatio removes them the next time it loads this process from the database), and references to them stop working. Re-select the method in the Process Designer, or change it here with setElement." | end of every modify, per WS element not configured in the request |
| N8 | required input without a source | "CallEcho: required input 'mode_In' has no value; the Process Designer marks such an element invalid." | end of request |
| N9 | duplicate nested name or tag | "CallEcho: 'items_In' contains two items named 'id_In'; Creatio stops with an error when it runs this element." | Create / retarget |
| N10 | SOAP (only if an owner-requested SOAP capture allows building it) | "X is a SOAP service; the element was built with the REST parameter rules, which a SOAP capture showed are the same." | only if Q20 allows SOAP |

N11 dropped (Q3, 2026-11-03: nested items are always generated, so there is no flat build to report); the other
numbers are kept.

### 5.3 Describe issues

| Stored shape | Issue (draft) |
|---|---|
| `serviceResolved: false` | "The element calls a web service that is not registered here (UId <uid>), so it fails at run time." |
| `methodResolved: false` | "Service 'X' has no method 'M'. Creatio removes the service parameters of this element the next time it loads this process from the database, and a save keeps them removed." (a warm cache still shows the stored set, M-29) |
| method found only case-insensitively | "Method 'echo' differs in case from 'Echo'; Creatio looks methods up exactly, so loading the process removes this element's service parameters and the run stops with an error." (EM11 designer half, measured: such a save answers success, the next load prunes every tagged param, the run fails with `ItemNotFoundException`) |
| nested drift | "'rows_Out' items differ from the service: missing cells_Out; extra oldCol_Out. The Process Designer rebuilds them when the method is re-selected." |
| a result param missing (`Success`, `StatusCode` or `Response`) | "'StatusCode' is missing; Creatio stops with an error when it stores the call result." (run time reads them with `GetByName`, which throws, r9 S3 verdict) |
| `IsValid = false` on the element or a param (BL11) | "Creatio marked this element invalid: a required input is not set." (the designer writes `BL11:false` on the element itself while a required input is unbound; it does not mark a dependent whose read parameter was removed, EM4 M-26) |
| multi-instance | "Multi-instance Call web service: shown as stored, not configurable here." |
| nested array-in-array input | N5 text |
| duplicate nested name / tag | N9 text |
| `RequestBody` absent (52/66 corpus elements predate it) | none: run time reads it optionally (r6 C4 verdict); no issue |

## 6. clio

| # | Change | Where | Guard |
|---|---|---|---|
| K1 | Rebundle the final cut; four PROVENANCE pins; security counts unchanged | `CLIO/clio/CrtProcessBuilder/CrtProcessBuilder.gz`, `CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs` | `BundledProcessBuilderPackageTests` |
| K2 | `WebServiceBlock` key set (`service`, `method`, `timeoutSeconds`, `ignoreErrors`) linked into `Element` and `ElementUpdate`; describe read-back keys `serviceResolved`, `methodResolved`, `issues`, `parameters`; no child key sets (`ProcessBlockKeyCheck` walks children only under write keys, where `service` is a string); an object-valued `webService.service` (a described block sent back as is) is refused before sending with the pointer "send `webService.service.name` as the string" (TC-C-17), since the server would fail the whole request on it | `CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:327-357` | `KeySets_Should_EqualTheMembersOfTheBundledServerContracts`, `ReadBackFields_Should_BeDescribeOnlyMembers` (`service` is on both types with different shapes: it is a shared key, not a read-back field) |
| K3 | `"webService"` in `CreateElementBlockShape` (paid inside the wire-bundle headroom, measured on the branch) | `CLIO/clio/Command/McpServer/Tools/ToolContractShortForm.cs:84-88` | `ToolContractShortFormTests:572`, `MaxInlineWireReplyBytes` |
| K4 | Floors: `[RequiresPackage]` on create / modify / modify-as-new-version moves from the literal at `6be5dde28` (1.6.6.127, history) to the cut claimed in the claim comment; one floor-history paragraph; `webService` added to the floor-reason sentences; the capability is proven by probing the archive for `ProcessElementDescriptor.WebService` on the named type, both ways (bundled-packages.md) | `CreateBusinessProcessCommand.cs:215-293`, `ModifyBusinessProcessCommand.cs:227`, `ModifyProcessAsNewVersionCommand.cs:91`, e2e floor literals | `ProcessDesignerRequiresPackageAttributeTests` |
| K5 | Descriptions (Q15): create type enum gains `webService`; one pointer clause in the `fileProcessing?` shape: `webService? (webService elements only - service + method of a registered REST service; get-guidance name=process-web-service owns the block)`; modify `addElement` and `setElement.elementUpdate` lists; describe read-back clause; `validate-process-graph` buildable list (`ValidateProcessGraphTool.cs:114`); generic-route refusal of `WebServiceUserTask` in `ListUserTasksTool.cs:58-60` | the four process tools + `ListUserTasksTool.cs` | `ToolContractPayloadBudgetTests:315`; e2e TC-I-13 in `ListUserTasksToolE2ETests` (the tool's contract text names `WebServiceUserTask` as refused on the generic route) |
| K6 | Prompts: create / modify / describe process prompts, `ListUserTasksPrompt.cs:32` | `CLIO/clio/Command/McpServer/Prompts/ProcessDesigner/*` | prompt pins |
| K7 | Typed `DescribedWebService`, `DescribedWebServiceService`, `DescribedWebServiceParameter`, each with a `[JsonExtensionData]` bag, mirroring C1 field for field; recount the bag record | `CLIO/clio/Command/ProcessModel/IProcessDescriber.cs`, `CLIO/docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md` | `ServerProcessDescriberTests` |
| K8 | Capability map rows; curated-knowledge re-pin to the generation that publishes `process-web-service` | `CLIO/docs/McpCapabilityMap.md:792-798`, `CLIO/clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` | `WorkspaceTemplateGuidanceDriftTests:539-597` |
| K9 | MCP e2e `WebServiceElementToolE2ETests`: create + describe, same-request mappings (scalar, constant, item path, RequestBody formula), modify retarget with the guard and N4, refusals with nothing created (unknown service / method lists names), the designer-built EM2 fixture read-only, `validate-process-graph` over a described element, no service-side data (service parameter default, `authInfo`, base-URI credential) in any reply while the element's stored bindings are returned by design; `MinimumPackageVersion` = the bundled version; Ignored with build instructions when `UsrWscEchoService`, `UsrWscDriftService` (with `UsrWscOrders2`), `UsrWscDeadService` or the EM2 fixture `UsrWscEm2CaptureA` is absent; strictly sequential | `CLIO/clio.mcp.e2e/` | manual run on stand `Creatio`, net10 then net8 |
| K10 | Knowledge records only for facts the code does not say (load-path re-sync prune, measured by EM8; the package's real compile floor of about 8.3.2, EM9, which is why IX4 is referenced directly; BK15 runtime-neutral) | `CLIO/docs/knowledge/ProcessModel/` | `make check-knowledge` (advisory) |
| K11 | Review-only, stated in the PR body: MCP resources (no process resource), shipped templates `CLIO/clio/tpl/**` (no process-designer tool named), no `McpToolCompatibilityCatalog` entry (no rename), no data-id arm (`webservice` already maps to `EventType.WebServiceTask`, `Schema.cs:1123`) | - | - |

Text budgets (Q15, precedent ENG-92719 "File processing element" Q20 and ENG-95985 "Send email attachments" Q12, r5 C3
verdict): measured headroom at `6be5dde28` is create 39 B, modify 3 B, describe 43 B under the 35,072 B full-contract
ceiling, re-measured on the branch (Q15). Every contract pays its pointer clause by a swap (no ceiling raise, Q15 A): moving a sentence the new or an
existing article already owns out of the description, measured by lowering the ceiling on the branch and reading the
failure message. ASCII only; `"`, `<`, `>`, `&`, `+`, backtick, apostrophe and any non-ASCII character cost 6 B each
under the default encoder (r5 C3 verdict (1)). The tools' first sentences stay unchanged (the `get-tool-contract`
Purpose is distilled from them).

Command docs: the process tools have no CLI verb (r5 §1), so `help/en`, `docs/commands`, `Commands.md` and
`WikiAnchors.txt`: "docs reviewed, no update required". ClioRing: inspect `CLIO/clio-ring/ClioRing.Ipc`,
`CLIO/clio-ring/ClioRing` and `CLIO/clio-ring/ClioRing.Desktop/actions.json` for the process tool names and `clio-run`
commands; the expected statement is "ClioRing compatibility reviewed, no Ring-consumed contract changed" (this change
edits description text and adds a block; the index-entry shape does not change), with the paths; if Ring turns out to
consume a process tool, the Ring test and NativeAOT publish commands run and their results go into the PR. Order: the
clio PR stays draft until the KB generation is published and re-pinned (Q19; the fixture test bites only on a new
`name=`, r5 C5 verdict).

## 7. clio-knowledge

- **KB-1** New article `guidance/mcp/guides/processes/web-service.md`, itemId `process-web-service` (every process
  article is gated at 27,793 chars, r5 §8; its size is measured on the branch by the KB-5 size gates):
  1. Scope: owns the `webService` block of a process element that calls a REGISTERED web service (Studio web services
     integration); writing a C# configuration service is `configuration-webservice`; collections are
     `process-parameters`.
  2. Version gate: use it only when the FULL contract (`get-tool-contract`, `detail: "full"`) names `webService`; the
     floor written as the real floor at merge.
  3. The block: members, units (seconds), defaults, presence rules on `setElement`.
  4. Naming the service and method: exact names are best; case and caption are accepted and canonicalised; refusals
     list the valid names; cross-package services work here but need a package dependency to export.
     Task without details (Q10 addition 2): find the service through the refusal list and ask the user when several
     fit; when none fits, stop (no placeholder process, no Script task in its place) and tell the user that the
     service must be registered in Studio > Web services, naming what that needs (base URL, method, parameters,
     authentication; the user enters credentials); build in two steps (element first, mappings from the returned
     tree); fill a key or token parameter (as `Xgoogapikey` of `GeminiApiSearch`) from a system setting or a process
     parameter, never a constant.
  5. The parameter tree: `_In` / `_Out`, dotted item paths, `ItemValue` children, result params, `RequestBody`; read it
     with describe (`webService.parameters`) after create.
  6. Request values: mappings by stored name; composite bodies (map the container to a collection and its items, or
     bind leaves only for a one-item list); a formula item reading row items needs its container mapped; when to use
     `RequestBody` (a method with no Body parameter, as `GeminiApiSearch`; the inner-list workaround for builds before
     the platform fix for arrays inside arrays; a shape mappings cannot express) and that a non-null value replaces the whole generated body (Q5; the builder does
     not check it as JSON).
  7. Responses: scalars and result params as outputs; object arrays into a declared Collection with item mappings (two
     levels), the container bound as well as its items; in the Process Designer a user maps an item first, which
     binds the container (EM3 route (b)); not supported: mirroring, scalar-array outputs, a third level, the
     designer's "Create from element" wrapper (use a Script task over `Response` or wait for structured parameter
     types).
  8. Errors: `Success` / `StatusCode` / `Response`; an HTTP error status never fails the element; `ignoreErrors`
     (core 8.3.2+, within the package's own floor, EM9) matters only for a transport failure (no HTTP response): on,
     the element completes with Success false and StatusCode 0; off, the element and the process fail (EM7 designer
     half); the `DisableIgnoreErrorsInWebServiceUserTask` gate; `timeoutSeconds` includes retries.
  9. Modify: method / service change replaces parameters, keeps `Success` / `Response` / `StatusCode` and their
     readers (Q11 A3), lists the dropped bindings with their values in write shape so one call re-applies those that
     still apply; a service change always renames the element caption to "<service caption> <N>", a method change
     keeps it; refused while another element, a process parameter, a flow condition or a
     formula reads any parameter of the element other than `Success` / `Response` / `StatusCode` (inputs, nested
     items and `RequestBody` included); unresolved elements and the load-time prune (N7). A designer save of a process whose web
     service element no longer resolves deletes every parameter of that element (result params included) and its
     BK15 rows and leaves the element `BL11:false` (M-30), so the remedy (fix the service, or retarget the element)
     comes before any designer save; describe's `methodResolved: false` is the signal.
  10. Run-time facts: `ProcessParameterCollections` feature for composite values; unbound items are left out of a row;
      before the platform fix for arrays inside arrays an inner object array with no source of its own whose items
      are bound is sent with no value (a mapped inner container is sent); duplicate nested names stop the element.
  11. Read back: describe members, which are read-back only, how to resubmit `service`.
  12. Refusals and notices (one line each).
- **KB-2** `element-catalog.md:136-141` and `element-catalog-details.md:70-71`: `webService` moves from NOT-buildable /
  READ-ONLY to buildable with the floor and a pointer; `ProcessGuideContentPreservationTests.cs:191` READ-ONLY pin
  replaced by the version-floor pin in the same commit (formulaTask / scriptTask precedent, `:181-197`).
- **KB-3** `process-modeling.md:182-185`: drop `webService` from the cannot-create list, paid by a swap (~13 B
  headroom under 19,000 B, measured r5, re-measured on the branch); `routing.md`: one row for `name=process-web-service` that says "call a
  registered web service from a process" vs "write a C# web service" (`configuration-webservice`,
  `configuration-webservice-tests`, `portal-service-routing`, `routing.md:185`), so "call a web service" does not land in
  the backend guides.
- **KB-4** `bundle-source.json`: resource entry with no `legacyUris` (post-migration precedent),
  `requirements.itemIds` and `resourceUris`, the `process-element-catalog` description ("... webService remain
  read-only" goes) and the `process-modeling` description; `libraryVersion` bumped above master and every open branch
  claim, re-checked at merge; the `sequence` is derived from `libraryVersion` and must not be reused (KB
  `CONTRIBUTING.md:158-192`, r5 §8), so no authored `sequence`.
- **KB-5** Tests: `WebServiceGuidanceTests` (block members, floor, FULL-contract gate, routing disambiguation, retired
  READ-ONLY claims swept), `ProcessGuideCrossReferenceTests` (member -> owning article), `GuidanceMigrationTests.PostMigrationGuidance`,
  size gates (`GuidanceInlineReplyBudgetTests`, `ProcessGuideResponseSizeTests`).
- **KB-6** Fact-check: refute-first agents per claim cluster, barred from citing `docs/knowledge/` or this spec folder;
  an article sentence resting on a traced-only claim names its EM or is cut.

## 8. Out of scope and follow-ups

| Item | Why not now | Where |
|---|---|---|
| `typeFromElement` mirror of WS collection outputs (copy non-GUID Tags verbatim) | relaxes a rule every mirror source passes through; ~+15 h with the next two (g3 §5) | ENG-92730; owner may pull it in under Q6 (+estimate) |
| `ObjectList` as a declarable process-parameter type; a third collection level | process-parameter model change | ENG-92730 |
| The designer's "create parameter from element outputs" grouping shape (a `CompositeObject` root with flat items) | CompositeObject not declarable; P1 would refuse it; a rules change for every element (g3 §5 (3)) | ENG-92730 |
| A notice for a formula item whose container is unbound | a new mapping-engine rule (g3 §5 (4)); guidance covers it | follow-up |
| clio discovery tool (`list-web-services` / `describe-web-service`) | refusals list names, describe returns the tree; a tool needs a `KnownRoute`, secret stripping, docs, e2e, index re-pin, a KB article (decisions Q10 option B: +8-12 agent h, +1-2 owner h; g5 sized it at +1-1.5 days) | follow-up only if agents need discovery before the first create (Q10) |
| A package discovery operation | moves the security pins (8 -> 9) (g5 option C) | not planned |
| Registering a web service (base URI, methods, parameters, authentication) | the element calls registered services only, like the Process Designer (Q10); a separate surface with its own security work | ENG-105475 "Register a REST web service through the AI Toolkit" |
| Package dependency auto-add on save | cross-element (sub-process has the same gap); the notice covers v1 (Q9) | separate ticket |
| SOAP methods | 0 in the corpus, none on stand `Creatio`; refused unless an owner-requested SOAP capture shows the same shape (Q20) | follow-up |
| Multi-instance Call web service | 0 in the corpus; refused (Q20) | follow-up |
| A `resync` member (bring nested `L18` in line after a service change without changing the method) | Q11 A makes the same pair a no-op; describe reports nested drift | follow-up (sub-process `resync` precedent) |
| Refusing an unrelated modify of a process with an unresolved WS element (Q12 B), a raw stored read to list the params the load pruned | contract member, budget bytes, a raw metadata read (g4 §6); Q12 A agreed: a notice, no refusal | follow-up (Q12 B); EM8 measured the prune it would prevent |
| Detecting the ENG-55920 fix on the target | the fix exposes no cheap public marker (g2 (c)) | N5 warns on every target (Q13) |
| Clearing the latent prune for sub-process | same mechanism, different element | separate ticket |

## 9. Traced claims this design rests on

| Claim (traced) | Design part that depends on it | Settled by |
|---|---|---|
| The platform load sync is a no-op on a C3-shaped element except the caption suffix | 3.3, 4.5 | C4 probe test, EM5 |
| The designer re-save leaves builder-made BK15 rows alone (designer-made rows measured unchanged on re-save and row-less elements measured to run, Q4 probe) | 4.9 (Q4) | EM5 |
| Builder-made bindings (one-item list, P1 rows, RequestBody) send the expected body; responses land in a declared Collection with `ProcessParameterCollections` on | 2.3, 2.4 | not on stand `Creatio` under EP option E4 (no echo endpoint, Q17): the request bytes rest on core unit tests and the ENG-55920 comment; the declared-Collection response needs a JSON source with the `$.orders` shape that E4 lacks. EM6 is reduced to what E4 shows (result params, StatusCode, Success / ignoreErrors behaviour) unless the owner adds a source |
| A builder-written `IX4` behaves as the designer's (M-28: only a transport failure fails the element when `IX4` is off; an HTTP error status never does) | 4.8, 4.12 | EM7 builder half |
| The builder's save adds no package dependency for a cross-package service (the designer's does: EM10 designer half, measured) | N6 | EM10 builder half |
| On 10.1.37 (no ENG-55920 fix) an inner object array with no source of its own whose items are bound is sent with no value (null), a mapped inner container is sent; whether such a process saves without error is not measured (r1 C7 verdict) | N5 | not measurable on stand `Creatio` under E4 (Q17): rests on core unit tests and the ENG-55920 comment |
| Other-culture result captions | 4.4 | not measured (EM2 ran under en-US) |

Measured on 2026-11-03 and removed from this table: the unrelated-modify prune (EM8 current-builder half, 3.3), the
service save and the designer's own prune (EM8's service-save and designer halves, M-29..M-31: a service save evicts
no cached process and a cold load prunes; a designer save of an unresolved element deletes every parameter and its
BK15 rows, KB-1 item 9), the
package compile floor (EM9, 4.12), the wrong-case method prune and run failure (EM11, 5.3), the designer's captions,
suffix scope and element caption (EM2, 4.4), the declared-Collection response shape (EM3 route (b), 2.4), the
designer's retarget (EM4, 3.2), what `ignoreErrors` does in the designer (EM7, 4.12).
