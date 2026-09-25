# SPEC: Strict keys for process descriptors and modify operations

**Created**: 2026-09-25
**Jira**: [ENG-95244](https://creatio.atlassian.net/browse/ENG-95244) — work package C of the rescope in
[comment 518188](https://creatio.atlassian.net/browse/ENG-95244?focusedCommentId=518188). The rescope has
not been agreed with the reporter yet.
**Size estimate**: M (4 stories)
**Recommended next**: /bmad-spec is sufficient — ADR [adr-descriptor-strict-keys.md](../adr/adr-descriptor-strict-keys.md),
test plan [tp-descriptor-strict-keys.md](../test-plans/tp-descriptor-strict-keys.md)

---

## Why

A misspelled or wrongly-cased key in a `create-business-process` descriptor or in a
`modify-business-process` / `modify-business-process-as-new-version` operation is dropped by the server in
silence, and the call answers success. Measured on 2026-09-24 (clio environment `Creatio`,
CrtProcessBuilder 1.6.6.22): `lable`/`Label`, `sortt`/`Sort` and `parametres` never landed, create returned
exit code 0 with no warning, and a `setFlow` carrying `lable` answered "1 operation(s) applied" and changed
nothing. The agent reports success on a process that fires on every save, runs for every record, or
assigns the step to the wrong user.

## Capabilities

| ID | Intent (WHAT) | Success Signal (HOW WE KNOW) |
|----|--------------|------------------------------|
| CAP-01 | Refuse an unknown or wrongly-cased key in a create descriptor before anything is sent | A descriptor carrying `flows[0].lable` makes `create-business-process` fail naming `flows[0].lable` and `label`; the build route receives no POST |
| CAP-02 | Refuse an unknown or wrongly-cased key in a modify operation, on both modify tools | `operations[0].lable` on a `setFlow` fails `modify-business-process` and `modify-business-process-as-new-version` with the path and `label`; no POST |
| CAP-03 | Name the nearest valid key for each refused key | A case-only mistake names the exact key and says keys are case-sensitive; a typo names the closest key; no near match lists the valid keys at that level |
| CAP-04 | Keep a newer environment usable | When the environment's CrtProcessBuilder is NEWER than the bundled one, the same unknown key produces a warning and the call proceeds |
| CAP-05 | Keep the accepted key set equal to what the bundled package accepts | A unit test derives the key set from `Contracts/*.cs` inside `clio/CrtProcessBuilder/CrtProcessBuilder.gz` and fails on any difference from the checked-in schema |

## Constraints

- **C1**: Case-SENSITIVE check — the server matches keys case-sensitively (measured). Case-insensitive matching is used only to build the hint.
- **C2**: Refusal happens before the POST; nothing is sent and nothing is persisted on a refusal.
- **C3**: Version skew: refuse only when the environment's CrtProcessBuilder is known to be NOT newer than the bundled archive; a newer or undeterminable environment degrades to a warning. An environment BEHIND is already refused by convergence.
- **C4**: The existing per-block read-backs (`AccessRightsBlockExpectation`, `ApprovalBlockExpectation`, `EmailBlockExpectation`, `FlowLabelExpectation`) stay — they also cover an older package that does not know a key.
- **C5**: No extra Creatio round trip on a clean call: the installed version is read only when an unknown key was found.
- **C6**: `create-business-process` and `modify-business-process` contracts are at the `ToolContractPayloadBudgetTests` ceiling — no net growth of their `[Description]`s.
- **C7**: Values are not validated — only keys. A wrong value TYPE is already refused loudly by the server's deserializer.

## Non-goals

- Will NOT: validate value types, enums or required keys.
- Will NOT: normalize a `describe-business-process` read-back into a write descriptor (describe-only fields such as `resultsActivity` are refused like any other unknown key).
- Will NOT: change the CrtProcessBuilder package or rebundle it.
- Will NOT: add a bypass argument to the tools.

## Success Signal

`dotnet test clio.tests --filter "Category=Unit&(Module=ProcessModel|Module=Command|Module=McpServer)"` is
green, and over the real MCP server a create descriptor with `flows[0].lable` is refused naming
`flows[0].lable` and `label`, while the same descriptor with `label` builds.

---

## Companion Notes

- Surfaces: the create descriptor root is `BuildProcessRequest`; for both modify tools clio builds the
  envelope itself (`name`/`uid`/`packageName`) and forwards the caller's `operations`, whose items are
  `ProcessOperationDescriptor`. `ModifyProcessAsNewVersionRequest` has the same `operations` item type.
- The contracts are a closed tree: 49 `[DataContract]` classes (264 keys) reachable from the two roots, no
  dictionary- or object-typed members, one inheritance (`FilterDescriptor : FilterGroupDescriptor`).
- Every existing test and E2E descriptor must pass the new check; one that does not is a real defect the
  check found, not a false refusal to suppress.
- Risk: an environment carrying a package built from the OTHER active line at the SAME version number
  could accept a key the bundle does not. Convergence refuses an environment behind the bundle, so equal
  numbers are the only exposure; documented, not engineered around.
