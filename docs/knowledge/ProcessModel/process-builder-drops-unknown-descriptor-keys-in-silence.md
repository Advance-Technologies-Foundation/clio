---
description: CrtProcessBuilder drops a misspelled or wrongly-cased key in a create descriptor or a modify operation in SILENCE and still reports success (measured 2026-09-24 on 1.6.6.22) - clio now refuses such a key before the POST from a key schema generated out of the bundled archive's contracts, and only warns when the environment's package is newer than the bundle
applies-to:
  - clio/Command/ProcessModel/ProcessDescriptorKeyValidator.cs
  - clio/Command/ProcessModel/ProcessDescriptorKeyGuard.cs
  - clio/Command/ProcessModel/Schemas/process-builder-write-keys.schema.json
  - clio.tests/Command/ProcessModel/ProcessBuilderWriteKeySchemaGenerator.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-95244
date: 2026-09-25
---

**What is true** — measured on clio environment `Creatio` (CrtProcessBuilder 1.6.6.22), each field sent three
ways beside a correct control, then read back with `describe-business-process`:

| Field | Correct key | Typo | Wrong case |
|---|---|---|---|
| Flow label | `label` → stored | `lable` → dropped | `Label` → dropped |
| Read data sort | `sort` → stored | `sortt` → `sort: null` | `Sort` → `sort: null` |
| Process parameter | `parameters` → created | `parametres` → no parameter | — |
| Modify `setFlow` | — | `lable` → "1 operation(s) applied", label still absent | — |

Create returned exit code 0 with no warning from either side. Matching is case-sensitive. The cause is on the
server: no `[DataContract]` in the package implements `IExtensibleDataObject` (its own
`Contracts/VersionContracts.cs` says so), so WCF discards an undeclared member. clio used to forward the
payload as an opaque string.

Since ENG-95244 clio refuses such a key BEFORE the POST in create, modify and modify-as-new-version, naming the
JSON path and the nearest valid key. The key set is `Schemas/process-builder-write-keys.schema.json`,
generated from `Files/src/cs/Contracts/*.cs` INSIDE the bundled archive and pinned to it by
`ProcessBuilderWriteKeySchemaTests`. On an environment whose CrtProcessBuilder is NEWER than the bundle the same
key is only a warning.

**Why it is this way** — the refusal needs the server's key set, and the package is source-only, so the
contract sources clio would enforce and the bytes it ships are the same files. Deriving the set in clio's own
test against those bytes costs no package release and no rebundle, where generating it in the package would
have cost both plus a version bump that refuses every lagging environment
(`spec/adr/adr-descriptor-strict-keys.md` weighs the two). The newer-environment warning exists because the
bundle can be behind a developer build pushed from a package branch; refusing there would block a key the
server may accept.

**What breaks if you ignore it** — a rebundle that adds or renames a `[DataMember]` fails
`ProcessBuilderWriteKeySchemaTests` until the schema is regenerated
(`CLIO_REGENERATE_PROCESS_BUILDER_KEY_SCHEMA=1`); skip that and clio refuses a key the server accepts - a false
refusal, the mirror image of the silent drop, and just as invisible until someone hits it. The per-block
read-backs (`FlowLabelExpectation`, `AccessRightsBlockExpectation`, `ApprovalBlockExpectation`,
`EmailBlockExpectation`) still matter: they catch the OPPOSITE skew, an older package that lacks a key the
bundle has - which convergence normally refuses, but which a green create must never be taken as disproving. A
`describe-business-process` read-back pasted verbatim into a write is now refused wherever it carries a
describe-only field; that is deliberate, not a regression.
