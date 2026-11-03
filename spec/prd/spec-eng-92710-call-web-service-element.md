# SPEC: Call web service element (ENG-92710)

**Jira**: ENG-92710 Call web service element (Story; epic ENG-92704 Create BP via AI Toolkit)
**ADR**: [adr-eng-92710-call-web-service-element.md](../adr/adr-eng-92710-call-web-service-element.md)
**Stories**: [1](../stories/story-eng-92710-call-web-service-element-1.md) (build + describe: package slices A-C,
KB, clio), [2](../stories/story-eng-92710-call-web-service-element-2.md) (modify semantics and drift: slice D and its
KB and clio lines)
**Test plan**: [tp-eng-92710-call-web-service-element.md](../test-plans/tp-eng-92710-call-web-service-element.md)
**Analysis**: [`spec/eng-92710-call-web-service-element/`](../eng-92710-call-web-service-element/README.md) - README,
facts, usage, reuse, serialization, design, decisions (Q1-Q20), measurements (EM0-EM12), plan, test plan, estimate,
the text of the follow-up ENG-105475 Register a REST web service through the AI Toolkit and the Jira description. This SPEC restates what implementation needs; the analysis
holds the evidence and the source anchors.
**Status**: Accepted. All twenty owner questions (Q1-Q20) were agreed by the owner on 2026-11-03; Q3, Q5, Q7, Q10,
Q13 and Q17 differ from the first recommendation. The description of ENG-92710 Call web service element and its 9 acceptance criteria were written
to the issue on 2026-11-03 after the owner's yes and read back
([jira-description-draft](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-jira-description-draft.md),
decisions Q18); the checklist field was not touched.
**Created**: 2026-11-03
**Size estimate**: two stories, delivered as one feature PR per repository (crt-process-builder, then clio-knowledge,
then clio) after this docs-only clio PR. The /bmad-spec form is used because the ticket has fewer than 5 stories
(AGENTS.md); an ADR is added because Q1, Q2, Q3, Q4, Q6, Q7, Q10 and Q11 fix the element identity, the contract on
the existing mapping surface, a package-owned parameter generator and the first package-authored mapping rows, each
against rejected alternatives (plan section 2). Hours, lines, PRs and cuts: the
[estimate](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-estimate.md).

Baselines (2026-11-02, probed 2026-11-03): CrtProcessBuilder 1.6.6.131 (the package's main branch), clio
`origin/master` `6be5dde28`, clio-knowledge `origin/master` `5f14ceb` (`libraryVersion` 1.15.176), CrtProcessDesigner
7.8.0 and the shipped package corpus, the core platform source, and stand `Creatio` (a registered clio environment;
Creatio 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.131). The drift check against these is plan step 0.4.
Drift recorded since: clio master `f86d8098f` (2026-11-03) rebundled CrtProcessBuilder 1.6.6.136, whose server refuses
undeclared request keys (`UnknownRequestKeyDetector`, `ReadBackKeys`); the read-back-only keys are derived
automatically because the write and describe members are both keyed `webService`; nothing to register (see Contract).
Basis labels: *traced* (read in code; a run-time claim with this label is a hypothesis until an EM measures it),
*measured* (observed on stand `Creatio`, in a binary or counted over the shipped corpus, with the date).

clio and clio-knowledge paths are relative to their repository roots. Sources outside the public repositories (the
CrtProcessBuilder package and its tests, CrtProcessDesigner and the other shipped packages, the core platform) are
cited by file or type name, or by the platform PR number; no repository paths of non-public repositories.

---

## Why

An agent building a business process through `create-business-process` / `modify-business-process` cannot put a Call
web service element into it. The package has no web-service code: `describe-business-process` on 1.6.6.131 reports the
element as `ProcessSchemaWebService` with `buildType: null`, returns its bound inputs only (unbound inputs and an
unbound `RequestBody` are omitted, M-31) and no service, method, timeout or ignore-errors member (measured 2026-11-03,
decisions Q14), and the knowledge element catalog marks `webService` read-only. Worse, the
generic `userTask` route would build CrtProcessDesigner's `WebServiceUserTask` today without complaint: a legacy SOAP
stub that no process uses (decisions Q1).

The obvious fix (let the platform's `ProcessSchemaWebService.SynchronizeParameters` generate the parameters) does not
work: it builds flat top-level parameters only (no nested items, no result parameters, no mapping rows), and the run
time reads `Success`, `StatusCode` and `Response` by name and throws when they are missing (traced, decisions Q3).

A data-loss path also exists today: every design-time load re-syncs each web-service element by name, so a modify of
anything else in a process permanently deletes the tagged parameters of an element whose method no longer resolves,
with no warning (measured 2026-11-03, EM8 current-builder half, decisions Q12); a designer save of such a process
deletes every parameter of the element, result params included, and its BK15 rows (M-30).

## What the platform requires (the contract the package must meet)

- **The element is the core flow element** `ProcessSchemaWebService` (ManagerItemUId
  `652b598d-cdd5-49d5-a86d-aafac88213f3`; 66/66 corpus elements), not a user task (decisions Q1).
- **Four element keys**, each omitted at its default: `IX1` the service's manager-item UId, `IX2` the method name,
  `IX3` the timeout in milliseconds (the designer edits seconds: 7 s stored 7000, EM2), `IX4` `IsIgnoreErrors` (core
  8.3.2.3590+; the package already fails to compile below about 8.3.2, EM9).
- **The designer client, not the server, makes the parameter shape**: one tagged `<name>_In` / `<name>_Out` per service
  parameter (Tag = the service parameter name), recursive nested items for object arrays, one `ItemValue` child for a
  scalar array, captions in resources (" - Input" / " - Output" only on a caption shared by top-level parameters,
  EM2), Value "false" on every Boolean, then the untagged `Success`, `Response`, `StatusCode`, `RequestBody` and one
  `ProcessSchemaMapping` (BK15) row per result parameter (Q4 probe and EM2, measured 2026-11-03).
- **Every design-time load re-syncs by name, and looks methods up ordinally**: a wrong-case method saves green, the next
  load prunes every tagged parameter and the run fails with `ItemNotFoundException` (EM11 designer half, measured).
- **Services resolve workspace-wide**; the designer's save adds a package dependency, the builder's save adds none
  (EM10 designer half measured; the builder half is traced).
- **`ignoreErrors` matters only for transport failures**: an HTTP 404 completes the element with `Success` false and
  the process continues either way; a refused connection fails the element and the process unless `IX4` is set (EM7
  designer half, measured 2026-11-03).
- **Before the platform's nested-list fix ENG-55920 Unable to use array inside array in web-service business process
  element (core #1119, fixVersion 10.1), absent on 10.1.37**, an inner object
  list with no source of its own whose items are mapped is sent with no value (traced; not measurable on stand
  `Creatio` under Q17).
- **Service metadata can hold secrets** (corpus: literal basic-auth passwords, `Bearer` header defaults); a
  designer-built element copies no service default (measured 2026-11-03, decisions Q16).

Full key-by-key target and the comparison rule WSCR: the
[serialization](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-serialization.md) file;
components C1-C14 and the message drafts: the
[design](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-design.md).

## Scope

In scope (the CrtProcessBuilder package, clio, clio-knowledge):

- A `webService {service, method, timeoutSeconds?, ignoreErrors?}` block on create (`elements[]`), `addElement` and
  `setElement` that builds `ProcessSchemaWebService` for a service registered in Studio > Web services (Q1, Q2, Q8).
- Resolution: service by UId, then code, then caption; method by code; case-insensitive, canonical spelling written,
  ambiguity refused; refusals list codes and captions; a cross-package service saves with a notice (Q9, Q10).
- A package-owned port of the designer's parameter generator, nested items always emitted, no feature gate; the 4
  BK15 rows written (Q3, Q4).
- Request values, a composite request body and the raw `RequestBody` through the existing mapping surface, addressed by
  the stored names, in the same request (Q2, Q5, Q10); a composite response read into a declared Collection of at most
  two levels with item mappings (Q6).
- `describe-business-process` returns a `webService` member and the stored parameter tree, unbound inputs included
  (Q14); drift issues and a notice on a modify that lets the platform prune an unresolved element (Q12).
- Modify: a service or method change replaces the generated parameters as the designer does, keeps the UIds of
  `Success` / `Response` / `StatusCode`, lists the dropped input bindings in the write shape with their values (notice
  N4) and is refused while anything (another element, a process parameter, a flow condition, a formula body) reads
  any other parameter of the element, tagged inputs, nested items and `RequestBody` included, because all of them are
  regenerated with new UIds (`IProcessDependencySnapshot.FindDependents(element, survivingParameterUIds: {Success,
  Response, StatusCode UIds}, includeOwner: false)`; Q11 A3).
- Refusals and notices of design section 5; no service-side data (service parameter default, `authInfo`, base-URI
  credential) in builder output; the element's stored bindings are returned by design (Q16).
- clio: key check, short-form block, floors, descriptions and prompts (paid by swaps, Q15), typed describe, capability
  map, curated knowledge re-pin, MCP e2e. clio-knowledge: new article `process-web-service`, catalog flip, routing row.

Out of scope (deliberately not built, design section 8):

- Registering a web service (base URI, methods, authentication): ENG-105475 "Register a REST web service through the AI
  Toolkit". The Process Designer also works only with registered services (decisions Q10).
- Mirroring a web-service output into a new parameter (`typeFromElement`), ObjectList (scalar-array) parameters, three
  or more declared levels and the designer's "Create from element" CompositeObject wrapper: ENG-92730 "Complex /
  structured parameter types". In v1 the two web-service mirror cases refuse with a pointer to a declared Collection
  (Q6, design C13).
- A discovery tool or a new package operation (Q10); the security pins stay 8 operations / 6 gate call sites.
- SOAP services and multi-instance Call web service elements: refused with a message (Q20).
- A JSON check of a constant `RequestBody` and a `RequestBody`-vs-Body notice (Q5); the feature gate on nested items
  and its notice N11 (Q3); a reflection guard for `IX4` (Q7).
- Adding the package dependency on save like the designer (Q9 C, a separate ticket for every element); refusing an
  unrelated modify of a process with an unresolved element (Q12 B); a `resync` member (design section 8).

## Contract

Write (create `elements[]`, `addElement.element`, `setElement.elementUpdate`; one applier for all three routes):

```json
{
  "name": "CallEcho",
  "type": "webService",
  "webService": { "service": "UsrWscEchoService", "method": "UsrWscEcho", "timeoutSeconds": 30, "ignoreErrors": true }
}
```

```json
"mappings": [
  { "elementName": "CallEcho", "elementParameter": "UsrWscReq_In", "processParameter": "PText" },
  { "elementName": "CallEcho", "elementParameter": "UsrWscOrders_In.UsrWscOrdNo_In", "value": "A-1" },
  { "elementName": "CallEcho", "elementParameter": "RequestBody", "expression": "<formula building JSON>" }
]
```

- `service` and `method` are required on create and `addElement`; a block-less `type: "webService"` and the block on
  any other type are refused. `timeoutSeconds`: whole seconds, stored x1000; `0` or absent = the method's own timeout.
  `ignoreErrors: false` = omitted.
- `setElement`: absent members are untouched; `method` alone = another method of the current service; `service` alone
  is refused with the new service's method list, nothing written; the stored pair sent again, in any letter case,
  writes nothing; `timeoutSeconds: 0` clears `IX3`, `ignoreErrors: false` clears `IX4`. Like the designer (M-26), a
  service change always renames the element caption to `<service caption> <N>`; a method change keeps the caption
  (TC-U-35, 36).
- Request values are never in the block: they are mappings onto the element's own parameters by the names describe
  returns (`<code>_In`, dotted item paths, `RequestBody`); no alias names. A non-null `RequestBody` replaces the
  generated body. Responses are read by mapping `_Out` parameters and `Success` / `StatusCode` / `Response` like any
  element output; an object array into a declared Collection (<= 2 levels) with item mappings, binding the container
  as well as the items (EM3 route (b)).
- Not added: a request map inside the block, a base URI, auth settings, a service parameter value, a new operation.

Read back (`describe-business-process`, the element's `webService` member; design 2.5):

```json
"webService": {
  "service": { "name": "UsrWscEchoService", "uid": "<manager-item UId>" },
  "method": "UsrWscEcho", "timeoutSeconds": 30, "ignoreErrors": true,
  "serviceResolved": true, "methodResolved": true,
  "parameters": [ { "name": "UsrWscReq_In", "tag": "UsrWscReq", "direction": "in", "type": "MaxSizeText",
                    "required": true, "bound": true }, "..." ],
  "issues": []
}
```

`serviceResolved`, `methodResolved`, `issues` and `parameters` are read-back fields clio tolerates; `service` is a
string on write and an object here, so the guide says to send `service.name` back. From 1.6.6.136 the server refuses
undeclared request keys; the read-back-only keys are derived automatically because the write and describe members
are both keyed `webService`; nothing to register, and a described block replays through the server key check
(TC-U-109). The tree is read from
the element, never re-derived from the service, and carries no value and no service default; the element's stored
bindings (process data the caller can already read through describe, M-31), not service data, come back through the
generic `parameters[]` as today. The element also gets `buildType: "webservice"`. Create and modify add no response
member: notices go to the existing `warnings` list.

## Acceptance criteria

The 9 criteria written to ENG-92710 Call web service element on 2026-11-03, in substance; the bold label is this SPEC's.

- **AC1 - block and request values.** The `webService` configuration (service, method, optional timeout in whole
  seconds, optional ignore-errors flag) is accepted on create, `addElement` and `setElement`. Request parameters,
  including a composite request body and the raw request body, are set through the existing mappings on the element's
  own parameters, addressed by the names `describe-business-process` returns.
- **AC2 - resolution and refusals.** An unknown or ambiguous service or method fails with a message listing the
  available services (codes and captions), or the methods of the resolved service; a missing service is reported as
  needing registration in Studio > Web services. Names are matched case-insensitively and stored in their canonical
  spelling.
- **AC3 - inputs and response.** Inputs are mapped in through the existing mappings. The response is read into process
  parameters; a composite response is read through a declared Collection parameter (up to two levels) with item
  mappings.
- **AC4 - errors.** The ignore-errors flag is stored on the element; Success, Http status code and Response body are
  available as element outputs.
- **AC5 - parity.** Server serialization matches a designer-built Call web service capture, including the element's
  mapping rows.
- **AC6 - service or method change.** Changing the service or method replaces the element's parameters as the
  designer does; Success, Http status code and Response body keep their identity, so steps that read them stay valid.
  The change lists the bindings it drops, with their values, and is refused while another element or a process
  parameter still reads a parameter that would be regenerated.
- **AC7 - read back and drift.** `describe-business-process` returns the service, method, timeout, ignore-errors flag
  and the element's parameter tree, including unbound inputs, and reports an unresolved service or method as an issue.
  A modify that lets the platform remove the parameters of such an element says so.
- **AC8 - no service data.** No data stored in the web service itself (parameter default values, authentication
  settings, credentials in the base URI) is returned in any builder output. The element's own bindings are returned
  the way describe returns them today; the guidance tells agents to take keys and tokens from a system setting or a
  process parameter, never a constant.
- **AC9 - tests, docs, guidance, MCP.** Tests, docs, the guidance article and the MCP surface are updated.

AC -> design -> proof -> story (plan section 9 has one Definition of Done row per AC; ids are the
[test plan](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-test-plan.md)'s and the
[measurements](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-measurements.md)'):

| AC | Design | Proof | Story |
|---|---|---|---|
| AC1 | block (Q2, Q8), generator (Q3), same-request ordering (Q10), mapping surface (Q5) | TC-U-27..32, 56..62, 67, 87; TC-P-03, 09, 10; TC-I-01; EM2 | 1 |
| AC2 | resolver C2 (Q9, Q10), canonical `IX1` / `IX2` | TC-U-15..26, 105; TC-I-03, 04; EM11 and EM10 builder halves | 1 |
| AC3 | item mappings and `RequestBody` (Q5), declared Collection (Q6), C13 mirror refusals | TC-U-56..66, 101; TC-P-06, 10, 11; TC-I-02, 08; EM3, EM6 | 1 |
| AC4 | `IX4` written directly (Q7), N3, result params as ordinary outputs | TC-U-29, 66, 96; TC-I-09; EM7 | 1 |
| AC5 | shaper port (Q3), BK15 writer (Q4), WSCR rule | TC-U-01..14, 41..45, 106; TC-P-01..11, 13, 14; EM2, EM3, EM5 | 1 |
| AC6 | replace, result-param UIds kept, retarget guard, N4 (Q11 A3) | TC-U-33..39, 46, 107, 108; TC-P-12; TC-I-05; EM4 | 2 |
| AC7 | describe member and stored tree (Q14); issues and notice N7 (Q12) | TC-U-69..75, 77..80, 99, 102..104, 109; TC-U-53, 54; TC-I-06, 07; EM8 | 1 (read back), 2 (issues, N7) |
| AC8 | catalog and describer never read defaults or auth (Q16, design 4.16) | TC-U-11, 25, 75; TC-U-35, 37, 53; TC-P-08; TC-I-07; TC-K-12 | 1, 2 |
| AC9 | clio K1-K11, KB-1..KB-7, package docs C14 | TC-C-01..16; TC-K-01..13; all TC-I; AR-1, AR-2 | 1, 2 |

## Gates and measurements

EM status as the measurements file records it on 2026-11-03 (its section 0 bullets "Status later on 2026-11-03" and,
later still, "EM8's service-save and designer halves: done 2026-11-03", and the gate table, section 4).

- **No package code before this SPEC and the ADR exist on the BMAD branch** (AGENTS.md); they need not be merged for
  slice A. This docs PR merges before the package PR leaves draft (Q19).
- **Done, 2026-11-03:** EM0 (M-22), EM1 (M-19, M-23: `UsrWscEchoService`, `UsrWscDriftService`, `UsrWscDeadService`),
  EM2 S1 (M-24, the AC5 oracle), EM3 routes (a) and (b) (M-15, M-25), EM4 (M-20, M-26, M-27), EM9 (local, M-14), and
  the designer halves of EM7 (M-28), EM10 (M-17) and EM11 (M-16); EM8's current-builder half (M-18) and its
  service-save and designer halves (M-29, M-30, M-31); the Q4 BK15 probe (M-13). Measured facts M-01..M-31.
- **STOP gate (EM5)** right after slice B, on an experimental cut: the designer opens and re-saves a builder-made
  element with no byte change. On failure work stops, the diff goes to the owner, Q4 (and possibly Q3) is re-opened
  and the estimate is redone before slice C. Re-run on the final cut.
- **Claimed cut, before the PRs leave draft:** EM6 (reduced by EP option E4: result params, StatusCode, Success), the
  builder halves of EM7, EM10, EM11 and EM12, and EM8's claimed-cut half (the notice N7, the only EM8 half still
  open). EM12's run half is not measurable on stand `Creatio` under E4 and rests on the core unit tests and the comment
  on ENG-55920, the platform's nested-list fix (Q13, Q17).
- **Final cut:** EM2's read-back step and the EM5 re-run against the final archive.
- Every stand write, install, run and cleanup waits for the owner's explicit yes, one step at a time.

## Constraints

- No future CrtProcessBuilder cut number appears in these documents; numbers live in the claim comment on ENG-92710 Call web service element
  (Q19), the tag and `descriptor.json`. Versions appear only as history ("measured on 1.6.6.131").
- The stand is named only as `Creatio` (a registered clio environment); its address is not written here.
- Budgets (Q15): full tool contracts at `6be5dde28` have create 39 B, modify 3 B and describe 43 B under the
  35,072 B ceiling; `process-modeling` about 13 B under its reply budget (measured r5, facts file). Every added
  clause is paid by a swap, no ceiling raise, ASCII only;
  CL-5 re-measures on the branch with the ceilings lowered.
- No new `[OperationContract]`: the security pins stay 8 operations / 6 authorization gate call sites (Q10).
- Merge order (human merges only): this docs PR, then package (tag on the remote) -> knowledge (published) -> clio
  (Q19). The clio PR stays draft until the knowledge generation is published and re-pinned.

## Success signal

An agent asks for a process that calls a registered REST service: one `create-business-process` call builds the
element with its request values mapped and its result read into process parameters; `describe-business-process`
returns the `webService` member and the stored tree; sending the described block back through
`modify-business-process` writes nothing; a designer re-save of the element changes no byte; on stand `Creatio` the
process runs and `Success` / `StatusCode` / `Response` arrive in process parameters.

## Risks

See the plan section 10: the platform load re-sync that prunes an unresolved element (Q12), ENG-55920 (the platform's nested-list fix) absent on
10.1.37 (N5), no echo endpoint under E4 (the request bytes rest on unit tests), EM5 failing (STOP gate), budgets at
the ceiling, scope creep from ENG-92730 Complex / structured parameter types, many review rounds for an external-schema element, secrets in service metadata
(Q16), a foreign cut on `main` or a KB `libraryVersion` collision, and the local core-bin (10.1.37) being newer than
CI's 8.3.4.
