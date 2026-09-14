---
description: clio has three data-value-type name vocabularies on purpose (canonical read, get-app-info display, designer write) - which consumer demands which, why DisplayName is the platform client enum and is mixed-case, and why an unmodelled code must degrade to its ordinal and never to "Text"
applies-to:
  - clio/Common/CreatioDataValueType.cs
  - clio/Common/DataForge/DataForgeContextService.cs
  - clio/Command/ApplicationInfoService.cs
ticket: ENG-93202
date: 2026-09-09
---

**What is true** — a Creatio data-value-type code has **three** string spellings in clio, each because a
different consumer demands it:

| vocabulary | code 32 | code 6 | owner | who demands it |
|---|---|---|---|---|
| canonical | `Float2` | `Money` | `CreatioDataValueTypeInfo.Name` | the kind classifier (`GetKind`/`IsNumeric`/`IsFilterable`) resolves names through `ByName`, keyed by this column. Emitted by `dataforge-get-table-columns` and `dataforge-context`. |
| display | `FLOAT2` | `Money` | `CreatioDataValueTypeInfo.DisplayName` | `get-app-info` only |
| designer write | `decimal2` (readback `Float`) | `currency2` (readback `Currency2`) | `EntitySchemaDesignerSupport.SupportedDataValueTypes` + `GetFriendlyTypeName` | `create-entity-schema` / `modify-entity-schema-column` input, and `get-entity-schema-column-properties` readback |

`Name` is the platform **server** enum spelling. `DisplayName` is the platform **client** enum
`Terrasoft.DataValueType` from `sysenums.js` — verified against a 10.0 stand: 35 of the 49 values are the
client member name character for character (`FLOAT2`, `SHORT_TEXT`, `IMAGELOOKUP`, `MAXSIZE_TEXT`), and the
other 14 differ only in case or an underscore (`Guid`/`GUID`, `DateTime`/`DATE_TIME`, `Color`/`COLOR`, and
the rest of the primitives). So the display vocabulary is **mixed-case, not uniformly SCREAMING_SNAKE**, and
that inconsistency is the shipped `get-app-info` output — do not "normalize" one of those 14 rows to match
its neighbours.

Both name columns live in the **same** 49-row table, so they cannot drift in code coverage. The table holds
codes 0, 1 and 4-50; **codes 2 and 3 are absent because the platform enum does not define them** (same
stand check: the client enum has exactly 49 members).

`TryResolveDataValueType` normalises **case-insensitively and strips non-alphanumerics**, so `FLOAT2`,
`Float2` and `float_2` are one input token. That is why a single alias entry (`["float2"] = "decimal2"`)
covers both read vocabularies, and why a new read-surface name needs an **alias**, not a key.

**Why it is this way** — the three consumers genuinely disagree and no single vocabulary satisfies all of
them. Measured at ENG-93202: of the 30 codes clio can write, the designer's friendly names are accepted by
the write path 30/30 but **14 do not resolve to their own code through the canonical name index** — 13 are
absent from it outright (so `IsNumeric("Decimal1")` is `false`), and `Float` is present but keyed to code
**5**, so looking up the friendly name of code 32 answers for a different type. The canonical names are
49/49 resolvable by the kind classifier by construction but needed 12 aliases to be accepted on write. The canonical vocabulary won for the read surfaces because it covers all
49 codes, whereas `GetFriendlyTypeName` is scoped to *writable* types — its guard test iterates
`SupportedDataValueTypes.Values` — and a read surface sees all 49. The display vocabulary survives only so
`get-app-info` output does not churn.

**What breaks if you ignore it**

* **A fallback that names a real type turns a gap into a wrong answer.** The Data Forge mapper used to own
  a private 23-entry table falling back to `"Text"`. It lacked 26 codes including every decimal and money
  scale, so a Decimal(0.01) column (code 32) reported `Text`; because `Text` is a real type the caller
  could not tell, and filtered `UsrAmount > 1000` as a lexicographic string comparison. Any resolver on
  this table must degrade to the **ordinal** — visibly not a type name, and it names the code clio failed
  to model.
* **Collapsing the read surfaces onto `GetFriendlyTypeName` silently regresses five live types.** Codes
  5 `Float`, 8 `Date`, 9 `Time`, 11 `Enum` and 23 `HashText` are readable but not writable, so they hit
  that switch's default branch and read back as `"5"`, `"8"`, `"9"`, `"11"`, `"23"`. 19 of 49 codes are
  unnamed by it. This is observable on a stand today: `get-entity-schema-properties` reports
  `CurrencyRate.StartDate` (code 8) as the raw string `"8"`.
* **`TryGet(Guid)` is last-wins across the four currency scales.** Codes 6, 48, 49 and 50 all carry the
  same `CurrencyUId`, so `BuildByUId` keeps only the last row and a UId lookup answers `Money3` for any
  currency column. `CreatioArtifactMergeService` labels merge-conflict questions from that lookup, and it
  compares persisted `TypeUId` strings — so a `Money` (2 decimals) versus `Money0` (0 decimals) divergence
  is invisible to it and merges silently. Pre-existing; do not assume a UId identifies a scale.

Related: [[read-name-write-token-collisions]],
[[process-parameter-type-compatibility-is-kind-level]].
