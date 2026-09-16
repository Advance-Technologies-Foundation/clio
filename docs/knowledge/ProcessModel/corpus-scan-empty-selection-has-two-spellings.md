---
description: an EMPTY ProcessActivitiesSelectedResults (GV2) ships in two spellings - a bare {} and a $type-only dictionary - so a string test for "{}" reads 83% of the empty ones as a real selection and undercounts by 5x; and 64 metadata.json files under PackageStore are not JSON at all, so every corpus count is a floor
applies-to:
  - docs/knowledge/ProcessModel/shipped-processes-break-the-designers-own-connection-rules.md
  - docs/knowledge/platform/a-result-enumerating-connector-is-edited-as-a-selection-not-a-formula.md
ticket: ENG-91853
date: 2026-09-16
---

**What is true** — two separate traps, both of which make a corpus scan answer confidently and wrong.

**1. An empty `GV2` has two spellings.** Measured over all 1 405 conditional flows in the 7.8.0
`PackageStore` corpus:

| `GV2` shape | flows | of which carry a `CI3` formula |
|---|---|---|
| `$type`-only dictionary — `{"$type":"System.Collections.Generic.Dictionary\`2[...]"}` | 885 | 878 |
| bare `{}` | 183 | 183 |
| a real selection (one key besides `$type`) | 337 | 0 |

Both of the first two rows are **empty**. A test of the shape `gv2.strip() not in ("", "{}", "null")`
classifies the `$type`-only spelling as *has a selection* and silently drops 885 flows — 83% of the
empty ones. Parse the value and count keys other than `$type`.

**1a. A third way to get two different right answers: say which POPULATION.** The same scan returns
**1 061** over every schema and **1 023** when it first drops schemas whose `ManagerName` is not
`ProcessSchemaManager`. The excluded 38 are all conditional flows inside PAGE schemas
(`PageSchemaManager` — `CrtBase/BaseModuleEditPage` 11, `BaseGridPage` 7, `ImportSettingsPage` 7,
`BaseModulePage` 5, and eight more across five schemas; 36 in `CrtBase` and 2 in
`CrtProcessDesigner`), and **all 38 carry a formula**, which is why the two deltas are the
same number: 1 405 → 1 367 conditional flows and 1 061 → 1 023 formula-carrying. Neither figure is
wrong; they answer different questions. Say which population a number is over, every time it is
written down. (This is the same phenomenon as
[`corpus-scan-must-index-nodes-outside-bk4`](corpus-scan-must-index-nodes-outside-bk4.md): page and
entity schemas carry real process behaviour, so "a process schema" is not the same set as "a schema
with a process in it".)

**2. 64 `metadata.json` files under `PackageStore` are not JSON.** They open

```
= MetaData.Schema.UId "238fd3b5-9014-4057-a655-8dfbc820a743"
```

a `key = value` format (`Case/branches/7.8.0/Schemas/Case/metadata.json` is one), and they **do**
contain conditional flows: they pass a substring pre-filter and then fail `json.loads`. A scan that
`continue`s on a parse error skips them without a word.

**2a. And that pre-filter matches CODE BODIES, not only element graphs.** A schema's `metadata.json`
carries its stored C# source, so a bare `"ProcessSchemaConditionalFlow" in raw` also fires on a source
line like `} else if (ManagerName == "ProcessSchemaConditionalFlow") {`. Corpus-wide: **535** files
match the bare substring, **3** contain no fully-qualified
`"Terrasoft.Core.Process.ProcessSchemaConditionalFlow"` element at all, and **1** of those three is
valid JSON — `CrtProcessDesigner/.../BaseProcessParametersEditPage`, which therefore reaches a
histogram with a count of zero and looks like a tenth schema that has none. Pre-filter on the
fully-qualified name, and never count a row whose value is 0.

**Why it is this way** — `GV2` is a serialized .NET dictionary, and the serializer emits the type tag
whether or not there are entries; the bare `{}` is what a different writer produced. Neither is
canonical and nothing converts one into the other. The non-JSON files are an older metadata format
still present in the corpus.

**What breaks if you ignore it** — during ENG-91853 two sessions scanned this corpus for the same
property and got **183** and **1 061**. The gap was entirely trap 1: substituting the string test into
the working scan reproduced 183 exactly, to the unit. Both numbers were then quoted in review as
evidence about a shipped guard, and the disagreement was nearly written into shipped guidance as "two
scans disagree, do not trust a count" — which would have recorded a tooling bug as a property of the
platform.

Trap 2 was in **both** scans and neither noticed, so every count of this corpus made so far is a
floor, not a total.

**The cheapest check runs before any scan at all: grep for a count the tree already asserts.** This
number was already committed in this repository.
`packages/CrtProcessBuilder/Files/src/cs/Formulas/ConditionParameterNames.cs` has said
"932 of the 1 061 conditional flows that carry an expression" since `feae3ff`, 2026-09-06 — the same
corpus and the same property, measured ten days before the argument, by a pass that then built a
feature on the breakdown. `grep -rn "1 061" packages/` would have settled it in one command, and it
would have settled it the RIGHT way round: a pre-existing measurement is a second instrument, so when
a new scan disagrees with a shipped one, the new scan is the suspect. Neither of us ran it.

Then, if there is still a disagreement: dump the **distinct shapes** of the field being counted, the
**parse-failure count**, and the **population** the scan is over. All three are one line each, and any
of them would have ended this in the first exchange rather than the fourth.

Related: [`corpus-scan-must-index-nodes-outside-bk4`](corpus-scan-must-index-nodes-outside-bk4.md) —
the other way this same scan answers plausibly and wrong, and
[`shipped-processes-break-the-designers-own-connection-rules`](shipped-processes-break-the-designers-own-connection-rules.md)
for what the corpus is measured for in the first place. Note the limit of that measurement: a corpus
scan answers *does this state exist*, never *can the designer produce it* — only the designer answers
the second, and conflating them is what made this scan load-bearing when it was not.
