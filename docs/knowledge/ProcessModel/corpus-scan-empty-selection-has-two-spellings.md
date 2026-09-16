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

**2. 64 `metadata.json` files under `PackageStore` are not JSON.** They open

```
= MetaData.Schema.UId "238fd3b5-9014-4057-a655-8dfbc820a743"
```

a `key = value` format (`Case/branches/7.8.0/Schemas/Case/metadata.json` is one), and they **do**
contain conditional flows: they pass a substring pre-filter and then fail `json.loads`. A scan that
`continue`s on a parse error skips them without a word.

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

Practical rule: before arguing about a count, dump the **distinct shapes** of the field being counted
and the number of files that failed to parse. Both are one line, and both would have ended this in the
first exchange rather than the third.

Related: [`corpus-scan-must-index-nodes-outside-bk4`](corpus-scan-must-index-nodes-outside-bk4.md) —
the other way this same scan answers plausibly and wrong, and
[`shipped-processes-break-the-designers-own-connection-rules`](shipped-processes-break-the-designers-own-connection-rules.md)
for what the corpus is measured for in the first place. Note the limit of that measurement: a corpus
scan answers *does this state exist*, never *can the designer produce it* — only the designer answers
the second, and conflating them is what made this scan load-bearing when it was not.
