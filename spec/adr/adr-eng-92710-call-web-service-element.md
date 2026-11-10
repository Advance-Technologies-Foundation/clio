# ADR: Call web service element (ENG-92710)

**Status**: Accepted. The owner agreed Q1-Q20 on 2026-11-03 (decisions file of the analysis); this ADR records the
eight that shape the architecture (Q1, Q2, Q3, Q4, Q6, Q7, Q10, Q11) and names the related agreed decisions Q5, Q8,
Q9, Q12, Q13, Q14, Q16 and Q20.
**Date**: 2026-11-03
**Jira**: ENG-92710 Call web service element (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-92710-call-web-service-element.md](../prd/spec-eng-92710-call-web-service-element.md)
**Stories**: [1](../stories/story-eng-92710-call-web-service-element-1.md) (build + describe),
[2](../stories/story-eng-92710-call-web-service-element-2.md) (modify semantics and drift)
**Test plan**: [tp-eng-92710-call-web-service-element.md](../test-plans/tp-eng-92710-call-web-service-element.md)
**Analysis**: [`spec/eng-92710-call-web-service-element/`](../eng-92710-call-web-service-element/README.md); the
[decisions file](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-decisions.md) argues every
question below with its sources, the [design file](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-design.md)
holds the package components C1-C14 and the clio changes K1-K11, the
[measurements file](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-measurements.md) owns
EM0-EM12 and the measured facts M-01..M-59 (M-01..M-31 on 2026-11-03, M-32..M-35 on 2026-11-05, M-36..M-52 on 2026-11-09, M-53..M-59 on 2026-11-10), and C-01..C-09.

`CLIO/` = the clio repository root. Sources outside the public repositories (the
CrtProcessBuilder package and its tests, CrtProcessDesigner and the other shipped packages, the core platform) are
cited by file or type name, or by the platform PR number; no repository paths of non-public repositories. Options
are compared on clean-slate cost and platform constraints; existing prototype or draft code is not an argument for or
against any option (owner rule). Every stand measurement below was taken on stand `Creatio` (Creatio 10.1.37, .NET
Framework, CrtProcessBuilder 1.6.6.131) on 2026-11-02 or 2026-11-03, except the SOAP capture EM2 S2-S4 (M-32..M-35),
taken on 2026-11-05 with an experimental stand-only cut installed (designer saves; the builder was not involved), and
M-36..M-52 on 2026-11-09 on stands `eng105744-web02` and `eng92710-web03`, and M-53..M-59 on 2026-11-10 on stands
`eng92710-web03` (M-53..M-58) and `eng92710-web04` (M-59) (the final-cut campaign, and the re-measure
of the service refusal, AR-1 and EM8 step 8 on the claimed cut).

---

## Context

The Process Designer's "Call web service" element is the core flow element `ProcessSchemaWebService :
ProcessSchemaActivity` (run time `ProcessWebService`, manager item `652b598d-cdd5-49d5-a86d-aafac88213f3`), not a user
task. The package has no web-service code: describe on 1.6.6.131 types the element `ProcessSchemaWebService` with no
`buildType`, returns the outputs with their tags and nested items and the bound inputs only (unbound inputs and an
unbound `RequestBody` are omitted), and no service, method, timeout or ignore-errors member (M-04, M-21, M-31). The
generic `userTask` route would build CrtProcessDesigner's `WebServiceUserTask`, a legacy
SOAP stub that no process uses, without complaint.

The element stores four own keys (IX1 service manager-item UId, IX2 method name, IX3 timeout in ms, IX4
`IsIgnoreErrors`, each omitted at its default). Its parameters are made by the designer CLIENT, not the server: tagged
`<name>_In` / `<name>_Out` parameters (Tag = the service parameter name), recursive nested items for object arrays, one
`ItemValue` child for scalar arrays, caption resources, then four untagged result parameters (`Success`, `Response`,
`StatusCode`, `RequestBody`) and one BK15 (`ProcessSchemaMapping`) row per result parameter (C-01..C-06; M-24). The
platform's `ProcessSchemaWebService.SynchronizeParameters` builds only flat top-level parameters (no nested items, no
`ItemValue`, no result parameters, no BK15), its provider is internal and null in the package's test harness, and it
re-runs on EVERY design-time load, matching by name: a case-only method slip saves green, is pruned on the next load
and fails the run (M-16), and an unrelated modify through the current builder permanently deleted the 8 tagged
parameters of such an element with no warning (M-18). The run time reads `Success`, `StatusCode` and `Response` by
name and throws when they are missing.

The package already has what most of the feature needs: a mapping service that resolves dotted item paths on any
`ProcessSchemaParametrizedFlowNode` and writes the corpus byte shape for the three composite request shapes, a
Sub-process family to copy (external-schema selection in the build's `afterElementsPlaced` hook, Create / Update apply
modes, the `IProcessDependencySnapshot` retarget guard), and a ported designer algorithm with a read-only twin
(`PreconfiguredPageParameterSync`).

## Decisions

### D1 (Q1). The builder builds the core `ProcessSchemaWebService`; the legacy `WebServiceUserTask` route is refused

A new `WebServiceElementHandler` (`CanBuild` / `CanDescribe` => `element is ProcessSchemaWebService`) creates the element,
stamps its manager item (the constructor does not, the Sub-process precedent) and delegates to the applier (design
C6, C7). `{"type": "userTask", "userTaskName": "WebServiceUserTask"}` is refused on the generic route, keyed on the
resolved schema UId, with a pointer to `type: "webService"` (design C11, pattern `RefuseUnconfigurablePageTask`).

Rejected:
- **B. Build `ProcessSchemaWebService`, leave the legacy route open.** 0 h, but keeps a trap that produces an
  unrunnable SOAP stub next to the real element.
- **C. Build `WebServiceUserTask`.** Fails the serialization-parity criterion by construction: no designer capture of it
  exists, it is not the Academy element, and 0 processes reference it.

Consequences: element type token `webservice`; the strict factory gate (`EnsureBlockMatchesHandler`) refuses only a
block riding a type whose handler does not claim it, and `WebServiceElementHandler` itself refuses a block-less
`type: "webService"` create; DI registration with the composition-root tripwire.

Evidence: 66/66 corpus elements carry `BL1 = ...ProcessSchemaWebService`, `BL7 = 652b598d` (decisions Q1); M-04 (the
stand's two processes); EM2 / M-24 (the designer element on the test service has the expected element keys).

### D2 (Q2, with Q5 and Q8). Contract: a `webService {service, method, timeoutSeconds?, ignoreErrors?}` block; request values through the existing mapping surface

The block sits on `create-business-process` `elements[]`, on `addElement.element` and on `setElement.elementUpdate`
(design 2.1). It selects the service and method and sets the two element settings; nothing else. Request values,
including a composite request body and the raw `RequestBody`, go through the existing `mappings[]` / `addMapping`
surface on the element's OWN generated parameters, addressed by the stored names describe returns (`title_In`,
`items_In.name_In`); there are no alias names. `timeoutSeconds` is whole seconds (the designer's unit), stored as
IX3 = seconds x 1000, with 0 or absent = unset (Q8). A composite request body is either nested item mappings, as the
mapping engine already writes them, or `RequestBody` raw text (constant or formula), which replaces the generated
body when non-null; no new process-parameter type, no JSON check and no RequestBody-vs-Body notice in v1 (Q5).

Rejected:
- **B. An inline `request: {<serviceParamName>: <source>}` map inside the block.** A second binder beside the mapping
  service, a second clio key set, more description bytes; bare service names are not unique across directions and
  depths.
- **C. Aliases (`Body` for `Body_In`).** Two spellings in guidance and describe, ambiguity rules for In/Out pairs of
  one service name.
- For Q8, **`timeoutMs`** (the storage unit): agents read the designer and the Academy in seconds, so a x1000 slip is
  likely.

Consequences: new DataMembers on `ProcessElementDescriptor`, `ProcessElementUpdateDescriptor` and the describe
contracts (design C1); clio's `ProcessBlockKeyCheck` gains the `WebServiceBlock` key set (K2), still needed for
servers older than CrtProcessBuilder 1.6.6.136, where WCF drops an undeclared member silently; from 1.6.6.136
(rebundled on clio master `f86d8098f`, 2026-11-03) the server itself refuses undeclared request keys
(`UnknownRequestKeyDetector`); the read-back-only keys are derived automatically because the write and describe
members are both keyed `webService`; nothing to register, and TC-U-109 replays a described block through the server
key check; the mapping
rules do not change (P-B6 pins the three corpus shapes, TC-U-56..64, 66, 67).

Evidence: M-24 (EM2: element keys, `IX3` 7000 for 7 s, the stored bytes of a constant); M-23 (EM1: what Studio
stores for every request shape, including a single object flattened into scalars, C-08); EM6 ran a builder-made
binding in the final-cut campaign (M-38; re-run on the shipping build 2026-11-10, M-55), reduced under the agreed endpoint option E4 (no echo) to the
result parameters, so the request wire bytes rest on the core unit tests and the parity suite (decisions Q17); EM6-B's
trace measured the one-item list and an unmapped `RequestBody` as null (M-39).

### D3 (Q3). The package generates the parameters itself: a pure port of the designer algorithm, nested items always, no feature gate

`WebServiceParameterShaper` is a pure function from the service method's `ServiceParameter` tree to the element's
parameter plan (names, Tags, types, IsRequired, nested items, `ItemValue` children, captions with the " - Input" /
" - Output" suffix where a top-level caption is shared, the four result parameters in the designer's order, Value
`"false"` on every Boolean); a writer applies the plan (design C3). The platform sync is not called by production
code; it is tolerated afterwards because it is a no-op on a correct top-level shape, which a probe test behind a
`IWebServicePlatformSync` seam pins (design C4). Nested items are emitted always: no `ProcessParameterCollections`
read, no feature-reader seam, no flat-build notice N11.

Rejected:
- **B. Platform `SynchronizeParameters`, then a package pass adding nested items, `ItemValue` children, captions and
  result parameters.** Still ports every hard part, adds a dependency on an internal provider that is null in tests,
  and splits one shape across two writers.
- **C. Platform sync only.** Fails serialization parity and the run: no `Success` / `StatusCode` / `Response`.
- **The designer's own nested-items gate** (emit flat composites with notice N11 while `ProcessParameterCollections`
  is off; the original recommendation). Every checked build from 7.13 to 10.2.931 ships the feature on in the
  configuration the run time reads; a clean 8.2.1+ database lacks the DB row the client reads, which is the open
  defect ENG-68545 "No data binding exists for ProcessParameterCollections feature". The builder does not copy it.

Consequences: serialization parity becomes a property of one pure, platform-free component; the shaper is tested against the
corpus pairs (pattern A) and the stand captures (pattern B). A builder-made element whose top level differs from the
platform's view would be re-synced on the next load, so the shaper follows the platform where they disagree (EM5
"If not" rows). TC-U-100 guards against reintroducing the gate.

Evidence: M-24 (EM2: the designer element matches every expected shape row: BP2 order, stamps, required rule, types,
`ItemValue`, result parameters; the suffix applies to a top-level pair, not to nested items); C-01, C-03, C-05, C-06;
M-16 and M-18 (what the load sync does to a shape it does not recognise); M-22 (EM0: `ProcessParameterCollections` on);
M-37 (EM5: the STOP gate passed; see Consequences).

### D4 (Q4). The package writes the four BK15 rows of the result parameters

One `ProcessSchemaMapping` row per result parameter, as the designer's `createMappingInfo` writes them: key set
`BL1 UId A2 A3 A4 GT2 GT3 GT5 GT1`, no `GT4`, `A3 = A4` = process UId, Success `GT1 = {"GS2":"false"}` without
`GS5` (the parameter value is set first, then the row added, so the row does not alias it). They are the first BK15
rows the package authors, behind one writer (design C8). `A2` is not rewritten when the element is renamed, as the
designer does not rewrite it. A retarget re-creates them with new row UIds and a fresh `GT5` (`Guid.NewGuid()`, C8),
as on create; the designer keeps `GT5` (M-26), which WSCR's W10 ignores.

Rejected:
- **B. Skip the rows and record a runtime-neutral divergence in the parity rule.** Safe on the designer and run-time
  sides (the designer's own copy/paste makes, keeps and runs row-less elements, M-13), but the comparator carries an
  exception forever and builder output differs from a fresh designer build.

Consequences: `ProcessGraphBuilder.RemoveElement` already deletes rows that target an element; nothing else reads
them (traced). The single writer means the rows can be switched off by a flag if EM5 had shown the designer duplicating
or rewriting builder-made rows (it did not, M-37); the row-less state is one the designer itself produces.

Evidence: M-13 (Q4 probe: 4 rows of the C-04 shape on a designer element; a designer copy is row-less, a re-save
neither duplicates nor rewrites, the row-less element runs); M-24 (EM2: the same 4 rows on the test service); M-26
(EM4: a method or service change re-creates the rows with new row UIds and the same `GT5`); C-04; EM5 measured the same for a
builder-made element (experimental cut 2026-11-04; M-37).

### D5 (Q6). A composite response is read through a declared Collection (two levels) with item mappings; mirrors, ObjectList and deeper levels are out of scope

v1 reads an object-array output by declaring a process `Collection` with `itemProperties` (at most two collection
levels) and mapping its items from the element's `_Out` items; the existing pairing rule binds both container levels
and writes the shipped Gemini shape. The `typeFromElement` mirror of a web-service CompositeObjectList output keeps
its refusal, which gains a pointer to the declared-Collection route; the mirror of a web-service ObjectList output,
which today silently creates a parameter with no shape, is refused with the same pointer (design C13, plan P-C6).

Rejected:
- **B. Also relax the mirror for web-service outputs (copy Tags verbatim) and add a declarable ObjectList type.**
  Relaxes a rule every mirror source passes through and needs owner review rounds on a rule shared with Read data
  mirrors. It would still not reproduce the designer's own "Create from element" shape, a `CompositeObject` wrapper
  with flat first-level items (M-15), which needs a declarable CompositeObject.
- **C. Scalars only.** Fails the composite-response criterion.

Consequences: ENG-92730 "Complex / structured parameter types" owns the mirror, ObjectList, three or more levels and
the "Create from element" wrapper. C13 touches the shared parameter service, so its commit takes the full review
fan-out. The guidance tells the agent to bind the container as well as the items (the designer's bytes).

Evidence: M-15 (EM3 route (a): the designer's "Create from element" is a CompositeObject wrapper with flat
first-level items and nested lists dropped); M-25 (EM3 route (b): the designer's declared-Collection route binds the
container through an item, stores type 651ec16f and `[Element:{ws}].[Parameter:{uid}]` tokens at every level). The
run-time response read into a declared Collection is not measurable under E4 (EM6) unless the owner adds a JSON source.

### D6 (Q7). `ignoreErrors` is written to IX4 directly; Success / StatusCode / Response are ordinary outputs

`ignoreErrors: true` writes `IsIgnoreErrors` with a direct member reference; `false` or absent stores nothing. No
reflection guard, no version check, no declared platform floor. The one feature read that stays is notice N3: while
`DisableIgnoreErrorsInWebServiceUserTask` is on, the run time ignores the flag, so the builder saves it and says so
(design 4.12). One single-purpose reader with a test fake reads that feature, as the File processing element's
`FileProcessingFeatureReader` does (TC-U-96). The result parameters need no special handling: they are mapped and read in conditions like any output.

Rejected:
- **A as first recommended: expose the flag behind a reflection guard** (refuse `true` where the member is absent).
  Not needed: the package already fails to compile below core 8.3.2 for other reasons, so the reference adds no floor.
- **B. Declare a platform floor of 8.3.2.3590 and reference IX4 directly.** No floor mechanism exists; introducing
  one locks every older target out of the whole package for no gain.
- **C. Leave IX4 out of v1.** Loses designer parity on the checkbox; agents fall back to Success checks only.

Consequences: the guard component, its fake-absence test and TC-U-49/50/51/76 are dropped; the KB article states the
package's real floor (about 8.3.2) and that an HTTP error status never fails the element. Builds between 8.3.1.4498
and 8.3.2.4199 are not measured; none is a supported target.

Evidence: EM9 / M-14 (local compile: `origin/main` fails on 8.3.1.4498 and 8.1.3.6947 with or without the IX4
reference, builds on 8.3.2.4199); M-07 (the member's presence by build); M-28 (EM7 designer half: a 404 completes the
element with Success false and the process continues whatever IX4 is; a refused connection continues only with IX4
true); M-40 (EM7 builder half, final-cut campaign: the builder's element behaves the same); M-22
(`DisableIgnoreErrorsInWebServiceUserTask` off on the stand); C-07.

### D7 (Q10). No discovery operation and no clio discovery tool; registered services only

The agent learns the parameters from the build: the create / modify result carries notice N1 listing the generated
names, describe returns the tree (Q14), and mappings in the same request may reference the generated parameters
(selection and generation run in the `afterElementsPlaced` hook on create, before `mappings[]`, `typeFromElement` and
other blocks; on modify, `addElement` generates before the next operation; design C9). The element, like the Process
Designer, calls only services registered in Studio > Web services: the unknown-service refusal lists codes with
captions and, in the claimed cut, each service's methods, bounded (finding K-1, measurements section 0: the first 30 services with at most 8
methods each, services 31-100 by name and caption, the rest counted; when the list is cut, a service not shown is used
by its exact name or UId), and says a missing service must be registered first; an environment with no registered service has its own
refusal (design 5.1). The KB article gets a "task without details" section (find the service through the refusal,
ask when several fit, stop when none fits, build in two steps, fill a key or token from a system setting or a process
parameter, never a constant).

Rejected:
- **B. A clio `list-web-services` / `describe-web-service` MCP tool.** Needs a `KnownRoute`, secret stripping (the
  designer's service read returns the service's authentication settings; the builder never calls it and never echoes
  it), docs, e2e, an index re-pin and a KB article, and
  it shows the SERVICE tree, not the element names the builder accepts. A follow-up only if agents demonstrably need
  discovery before the first create.
- **C. A new package read operation.** B plus a security-pin move (8 -> 9 operation contracts), a guard-deny test and
  a rebundle.

Consequences: the two security pins stay 8 operations / 6 gate call sites; registering a service is ENG-105475
"Register a REST web service through the AI Toolkit". Agent scenario runs AR-1 (service not named) and AR-2 (service
not registered: the agent must stop) are the check that no discovery is needed. Both pass: AR-1
finds service and method through one refusal (M-48; two on the draft article before K-1, M-45), AR-2 stops (M-46).

Evidence: no EM; the designer's service list is `VwWebServiceV2` with no "new service" entry (checked on the stand,
decisions Q10); M-47 (the unknown-service refusal lists every service with its methods) and M-48 (AR-1 finds service
and method through one refusal); M-03 (the stand registers 3 services, so a refusal can list them all; the corpus has 32 service
schemas with at most 7 methods each, decisions Q9).

### D8 (Q11, A3). A method or service change replaces the generated parameters; `Success` / `Response` / `StatusCode` keep their UIds (`RequestBody`, the fourth result parameter, is regenerated); the retarget guard covers every other parameter of the element

`setElement` with a different method or service (after canonicalisation) runs the Update mode (design 3.2):
resolve and shape first, so every refusal fires before a write; use the dependency snapshot the edit pipeline already
captured once at the start of the request (`IProcessEditPipeline.Apply`): the applier calls only
`IProcessDependencySnapshot.FindDependents(element, survivingParameterUIds: {Success, Response, StatusCode UIds},
includeOwner: false)` and never `Capture`, since a mid-batch capture would drop the pre-batch half (the defect class
of ENG-100041 "Sub-process retarget guard scans the live schema instead of the request dependency snapshot"); it
refuses, naming the reader, if anything (another element, a process parameter, a flow
condition, a formula body) reads ANY parameter of the element other than `Success` / `Response` / `StatusCode`:
tagged inputs, nested items and `RequestBody` included, because all of them are regenerated with new UIds (the
package's `ProcessElementDependencyScanner` walks `schema.Parameters` as well as the elements; a same-named parameter
of the new method does not keep its readers: it has a fresh UId; `includeOwner` is false because the element's own
parameters are all replaced, so its own references die with them). This is the agreed A3 stated exactly: A3 said
"tagged output", and a reader of a regenerated input dangles the same way. Then record every dropped input binding in
the write shape WITH its value, constants and formulas alike, `RequestBody` included; remove and regenerate every
parameter and the BK15 rows, except that `Success`, `Response` and `StatusCode` are regenerated with their CURRENT
UIds; keep IX3 on a method change and reset it on a service change, keep IX4; like the designer (M-26), a service
change always renames the element caption to `<service caption> <N>`, and a method change keeps the caption
(TC-U-35, 36); notice N4 lists the dropped bindings with their values so one call restores those
that still apply. The same service and
method sent again, in any letter case, is a no-op with no notice; a timeout or ignore-errors change alone touches
nothing else; `service` without `method` is refused with the new service's method list, nothing written.

Rejected:
- **A1. Replace with a guard over every output, result parameters included** (the agreed A before EM4). A condition on
  `Success` right after the element is the common case, so it would refuse nearly every real retarget.
- **A2. Allow like the designer and report the dangling readers.** The designer stores a process its own save calls
  "Schema has errors"; the builder would itself write a process that fails validation.
- **B. Merge by name** (keep parameters and bindings whose name survives). Diverges from the designer and keeps
  bindings whose type may no longer fit.
- **C. Refuse a change; require remove + add.** Every dependent flow and mapping is lost with the removed element.

Consequences: the package has no reference-rewrite helper, so A3 keeps the three UIds instead of rewriting readers.
The capture oracle does not compare UIds, so the kept UIds do not break parity with EM4's capture (TC-P-12). The
guard is stricter than the designer, which allows the change and leaves the readers dangling (M-26); readers of
`Success`, `Response` and `StatusCode` never block. The Sub-process `Snapshot` / `Diff` is lifted into an
element-generic snapshot / diff over `ProcessSchemaParametrizedFlowNode` (design C5), behind a characterization commit
that pins every sub-process notice text first (TC-U-93). Tests: TC-U-33..39, 107, 108; TC-P-12; TC-I-05.

Evidence: M-20, M-26 (EM4: the designer replaces every parameter, result parameters included, with new UIds,
re-creates the 4 rows, keeps `IX3` / `IX4` on a method change, resets `IX3` and keeps `IX4` on a service change, and
leaves the dependent Formula task and conditional flow pointing at removed UIds, unmarked, while the save reports a
schema error and stores the process); M-27 (the designer's formula window resolves an output by caption and can bind a
nested input of the same caption).

## Related agreed decisions

- **Q5 and Q8** are recorded inside D2.
- **Q9 - resolution and canonical names.** Service by manager-item UId, then code, then caption; method by code
  within the service; both case-insensitive, ambiguity refused, the canonical spelling written to IX1 / IX2; refusals
  list codes and captions (service refusals; method refusals list codes only, F-3); the unknown-service refusal also
  gives each listed service's methods, bounded (K-1, measured M-47), and the ambiguous one lists the matching names
  with their methods, no caption (TC-U-17). One component (design C2) is the only reader of `ServiceSchemaManager`. A service
  outside the process package's dependency closure saves with notice N6 and no dependency added; adding it like the
  designer is a separate ticket for every element. Evidence: M-16 (EM11: a case-only slip saves green, is pruned on
  load and fails the run), M-17 (EM10: the designer adds the dependency); M-42 and M-43 (the EM10 and EM11 builder halves in the
  final-cut campaign: no dependency added, canonical `IX2`; re-run on the shipping build 2026-11-10, M-57).
- **Q12 - drift and latent loss: notice, no refusal.** Describe reports `serviceResolved`, `methodResolved` and nested
  drift as issues; at the end of every modify, each web-service element whose service or method does not resolve,
  configured in the request or not (owner, 2026-11-05), gets notice N7 saying the platform load prunes its parameters.
  When something reads a pruned parameter, the platform's validation refuses the modify before the end of the
  request; that refusal carries N7 after the platform's text, the named element first with the repair order (a missing method
  is restored in Studio > Web services and the request repeated; a missing, unregistered service is restored by
  installing the package that carries it, not by registering it again in Studio, which would give it another UId) and
  without the re-select advice, at most 3 notices, nothing written (findings F-2, F-5, measurements section 0; M-51, M-52). A refusal with a confirm
  flag (Q12 B) is a follow-up. Evidence: M-18 (EM8 current-builder half: the loss is real and silent); M-29, M-30
  (EM8 service-save and designer halves: a service save writes nothing and evicts no cached process; a designer save
  of the process deletes every parameter of the unresolved element, result params included, and its BK15 rows, and
  leaves it `BL11:false`).
- **Q13 - notice N5 on every target.** An IN collection nested in another collection with no source of its own while a
  leaf below it has one is sent with no value before the platform's nested-list request fix ENG-55920 Unable to use
  array inside array in web-service business process element (core #1119, fixVersion 10.1; absent in 10.1.37, M-08).
  The fix state is not readable cheaply, so N5 fires on every target and names the remedy (map the container). Its run half (EM12) is not measurable under E4 and rests on the core unit tests
  and the ENG-55920 comment.
- **Q14 - describe read-back.** A `webService` member (`service` as the write-side name string, `serviceUId`, method,
  `timeoutSeconds`, `ignoreErrors`, `serviceResolved`, `methodResolved`, `parameters`, `issues`) with the element's stored parameter tree,
  unbound inputs and `RequestBody` included, read from the element and never re-derived from the service (design C10,
  2.5); `buildType: "webservice"`; the tree carries no value. Evidence: M-21 and M-31 (what describe returns today:
  bound inputs only), M-16 (describe reads the synced view). Every write member keeps its write shape on read and
  describe-only data has its own read-back key (`serviceUId`), as in the other blocks, so a described block replays
  unchanged (the owner's decision in review, 2026-11-04; the agreed `service {name, uid}` is superseded).
- **Q16 - no secrets (as agreed, option A).** No service-side data (service parameter default, `authInfo`, base-URI
  credential) in builder output (describe, refusals, notices, logs, test snapshots of builder output); the element's
  stored bindings (process data the caller can already read through describe, M-31), not service data, are returned
  by design. Describe returns a bound input's value through the generic
  `parameters[]` as today, which the round-trip tests TC-U-68, TC-U-78 and TC-I-06 need; the `webService.parameters`
  tree carries no value. Names and captions are not secrets. The KB article tells
  agents that keys and tokens come from a system setting or a process parameter, never a constant. Fixtures: the
  pattern-A corpus fixtures are copied verbatim from the shipped package corpus into the internal package repository
  and may carry the shipped services' non-credential `IU5` defaults; they are not redacted (their SHA pins), and
  TC-P-08 asserts that they carry no credential (no `IQ5` value with `IV2` 1, no constant `Authorization` default).
  Evidence: M-19 (the designer element carries no service default; the default lives only in the service schema).
- **Q20 - multi-instance refused; SOAP built like REST (D, 2026-11-05).** 0 multi-instance and 0 SOAP calls in the
  corpus (C-09). The owner-requested SOAP capture (EM2 S2-S4) showed the designer builds a SOAP element with the REST
  shape and no SOAP-only field, so SOAP services take every path as REST does, with notice N10 naming the element
  (evidence: M-33..M-35; a non-array complex type is unmeasured and the shaper refuses it anyway). Known gap: no builder-made SOAP element has been saved, re-opened in the Process Designer or run on a stand; the only evidence is offline parity against the designer capture (EM2 S2-S4, M-32..M-35). The package
  refuses `setElement` with a `webService` block on a designer-built element that carries `BP6` and describes such
  an element with an issue; a `webService.multiInstanceOptions` key on create never reaches the server, because
  clio's key check refuses it.

## Consequences

- New package components (design 4.1): C1 contracts, C2 service catalog / resolver, C3 shaper and writer, C4
  platform-sync seam (probe only), C5 generalized snapshot / diff (refactor first, behaviour-neutral), C6 handler and
  factory gate, C7 applier (Create / Settings / Update), C8 BK15 writer, C9 build hook and modify wiring, C10
  describer, C11 guards, C12 notices and end-of-request rules, C13 mirror refusals, C14 package docs, in a new
  `WebService` folder of the package source. Every behaviour class gets an interface and a scoped DI registration. No new
  `[OperationContract]`: the security pins stay 8 / 6.
- clio K1-K11 (design 6): rebundle, block key set, short-form block shape, floors, descriptions paid by swaps with no
  ceiling raise (Q15), prompts, typed `DescribedWebService`, capability map and curated re-pin, MCP e2e, knowledge
  records; MCP resources and `clio/tpl` reviewed only. Expected ClioRing statement: no Ring-consumed contract changed,
  to be confirmed by inspecting `CLIO/clio-ring/ClioRing.Ipc`, `CLIO/clio-ring/ClioRing` and
  `CLIO/clio-ring/ClioRing.Desktop/actions.json`.
- clio-knowledge: a new article `process-web-service`, routed from `element-catalog.md` (catalog flip from read-only to
  buildable with a version-floor pin), `process-modeling.md` and `routing.md` (call a registered web service vs write a
  C# configuration service).
- Out of scope, with owners: ENG-92730 "Complex / structured parameter types" (mirror, ObjectList, three or more
  levels, the "Create from element" wrapper); ENG-105475 "Register a REST web service through the AI Toolkit"; a
  discovery tool, a `resync` member, the package-dependency auto-add, the Q12 B refusal (follow-ups; design 8).
- Residual risk, retired: the STOP gate EM5 ran after package slice B on the experimental cut (2026-11-04) and again in
  the final-cut campaign (2026-11-09, M-37): the designer opens and re-saves a builder-made element without a rewrite,
  a duplicate BK15 row or a lost Success value, and an unrelated builder modify leaves the element node and rows
  byte-identical; only the top-level caption suffix follows the last writer (a server load drops it, a designer save
  puts it back; excluded from the oracle rule, AD1). EM5 was not repeated on the shipping build (the later changes touch only
  refusal texts and the validator).
