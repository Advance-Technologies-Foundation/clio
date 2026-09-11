---
description: Every sequence and conditional flow the Creatio platform writes carries a ManagerItemUId palette item (0d8351f6 plain / 573ed909 default / dac675d4 conditional) - measured 9762 for 9762 - and a flow written without one still renders, which is why CrtProcessBuilder omitted it on every plain flow for months
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-95891
date: 2026-09-02
---

**What is true** — a flow's `ManagerItemUId` is the designer palette item it resolves its image and
its allowed-connection rules from, and the platform sets one on **every** flow it writes. Counted
over every `metadata.json` under a `Schemas` directory in `PackageStore`:

| flow | `ManagerItemUId` | n | `FlowType` | `VisualType` |
|---|---|---|---|---|
| plain sequence | `0d8351f6-c2f4-4737-bdd9-6fbfe0837fec` | 7 600 | absent (Sequence is the enum default, and `WriteMetaData` skips defaults) | 1 |
| default | `573ed909-e069-4161-b193-ae8dd9437c68` | 756 | `1` | 1 |
| conditional | `dac675d4-ea84-4e44-9056-38bf918618e9` | 1 406 | — | 1 |

Nine thousand seven hundred and sixty-two flows, nine thousand seven hundred and sixty-two palette
items. Note what separates a plain flow from a DEFAULT one: same CLR class
(`ProcessSchemaSequenceFlow`), same `VisualType`, different palette item and `FlowType` — so
`VisualType` does **not** distinguish them and `FlowType` does.

**Why it is this way** — neither flow class assigns a palette item in its constructor, so it is the
designer client that writes one on every flow a human draws. A flow created through the API gets
none unless the caller sets it.

**What breaks if you ignore it** — nothing visible, which is the trap. `CrtProcessBuilder`'s
`AddSequenceFlow` stamped no palette item from the day it was written; the processes it built opened
in the designer, rendered, ran, and were verified by hand on a stand many times. The docblock that
was supposed to prevent this claimed "the designer cannot resolve the flow to a palette item" — an
overstatement that the first person to test it disproved, after which the whole note stopped being
believed. The defensible statement is the table above: a flow without a palette item is outside
every shape the designer client has ever been exercised against, and it costs one assignment to stay
inside. Do not restate the consequence as a crash; restate the count.

Related: [conditional-flow-rekind-must-be-in-place.md](conditional-flow-rekind-must-be-in-place.md)
— the same four fields, and why the re-kind may not remove-and-add.
