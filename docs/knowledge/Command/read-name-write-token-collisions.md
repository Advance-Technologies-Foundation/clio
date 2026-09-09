---
description: Three type names a clio read surface reports resolve to a DIFFERENT type on write - Float writes Decimal2, Date and Time write DateTime - so echoing a read value back into create-entity-schema or modify-entity-schema-column silently creates the wrong column
applies-to:
  - clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio/Command/McpServer/Tools/EntitySchemaTool.cs
ticket: ENG-93202
date: 2026-09-09
---

**What is true** — the read vocabulary and the write vocabulary share three tokens that mean **different
data-value types**. Sending a read value straight back is therefore not universally safe:

| read reports | for code | `TryResolveDataValueType` gives | i.e. |
|---|---|---|---|
| `Float` | 5 (unbounded float) | 32 | `Decimal2`, a 0.01-scaled decimal |
| `Date` | 8 | 7 | `DateTime` |
| `Time` | 9 | 7 | `DateTime` |

`Date`/`Time` are a documented, deliberate collapse — Creatio stores both as DateTime and the aliases exist
so date-only intent is accepted rather than hard-rejected (issue #949). **`Float` is not a collapse and was
not documented**: the platform's own enum names code 5 `FLOAT`, but clio's write alias
`["float"] = "decimal2"` claims that same token for code 32.

`EntitySchemaDesignerSupportTests.GetNameOrOrdinal_Should_Not_Resolve_To_A_Different_Type_On_Write` pins the
set to exactly these three, so a fourth cannot appear unnoticed. Note the forward guard alone cannot see
them: it iterates `SupportedDataValueTypes.Values`, the *writable* codes, and 5/8/9 are read-only.

Separately, the canonical names of the read-only codes (`Enum`, `HashText`, `Collection`, `Entity`,
`StageIndicator`, `FileLocator`, `CustomObject`, `Mapping`, `MetadataText`, `ObjectList`,
`CompositeObject`, `LocalizableString`, …) are **rejected** by the write tools — clio cannot create those
columns at all.

**Why it is this way** — neither side can be changed from here. `Float` is the platform's own enum member
name for code 5 and is matched **case-sensitively** by `SimpleToFullFilterConverter`, so renaming the
canonical spelling breaks filter parameter typing. The `float`/`decimal` → `decimal2` alias is documented
in the shipped `type` contract and pinned by
`TryResolveDataValueType_Should_Resolve_Decimal_As_Float`, so removing it breaks a released write
contract. Fixing it needs a deliberate contract decision, not a drive-by rename.

**What breaks if you ignore it** — an agent reads a column as `Float` and, following a contract that says
read names are accepted back, sends `Float` to `create-entity-schema`: it gets a Decimal(0.01) column, no
error, no warning. The sharper path is `schema-sync`: `AreColumnTypesEquivalent("Float", "Float")` resolves
BOTH sides to 32, so a genuine code-5-versus-32 divergence is reported as equivalent and no modify is
issued — while a desired state that echoes clio's own read output of an existing code-5 column compares
requested 32 against existing 5, decides "not equivalent", and **mutates a live column's type** on what was
meant to be an idempotent replay. This is why the `data-type` and `type` tool descriptions must keep naming
the three exceptions instead of promising that read output is writable.

Related: [[data-value-type-vocabularies]].
