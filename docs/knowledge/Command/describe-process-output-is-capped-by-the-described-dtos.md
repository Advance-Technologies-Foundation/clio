---
description: describe-process re-serializes typed DTOs, so the DTO is the ceiling on what a caller sees - and that ceiling is UNEVEN now: on a type with a [JsonExtensionData] bag an undeclared server field survives but is NOT addressable by name, which reverses the deploy-probe advice and hides a missing behaviour behind a complete-looking read-back
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/Command/DescribeProcessCommand.cs
ticket: ENG-92127
date: 2026-08-19
---

**What is true** — `describe-process` is not a passthrough. The server response is deserialized into
the closed DTO graph in `clio/Command/ProcessModel/IProcessDescriber.cs`
(`JsonSerializer.Deserialize<DescribeProcessResultEnvelope>`) and the command re-serializes that
object graph to stdout (`DescribeProcessCommand.cs`). The DTO, not the server, is the ceiling on
what a caller can see.

**The ceiling is UNEVEN, and it did not use to be.** This record originally said a server field with
no matching property is "dropped without a warning" on `DescribedElement` / `DescribedParameter` /
`DescribedFlow`. That is now true of exactly one of those three — `DescribedParameter`.

`described-filter-types-have-no-json-overflow-bag.md` OWNS the membership list and is the place to
read or update it — do not restate it here, and recount it against the file rather than trusting a
list, because it has moved once already. The short of it: the graph root, `DescribedElement`,
`DescribedFlow` and the element configuration blocks have a bag; `DescribedParameter`,
`DescribedConnection`, `DescribedSignal` and the filter types do not.

**A typed property is still required, for two reasons that survive the bag.** First, a bag keeps a
field ALIVE but not ADDRESSABLE: nothing can read it by name, so any guard or caller that needs the
value — `FlowLabelExpectation` reading `DescribedFlow.Label` to tell whether a label landed, the
parameter-delete guard scanning a flow's `Condition` — needs the property. A `JsonElement` in a
dictionary is not that. Second, the `WhenWritingNull` omission that lets clio stay silent about a
field an older server never sent is a property-level behaviour; a bagged field has no such control.

**Why it is this way** — the typed graph is what gives the command its stable, documented JSON shape.
The bags were added so that a newer `CrtProcessBuilder` reporting a new field does not need a clio
release before a caller can see it at all; they are a floor under losslessness, not a substitute for
declaring what you read.

**What breaks if you ignore it** — the original failure and its inverse, and now you have to know
which type you are standing on.

On a BAGLESS type the old trap stands unchanged: pick a newly added server field as the indicator
that a rebuilt package is live on a stand and it is invisible whatever is deployed, so a successful
deploy reads as a failed one. ENG-92127 burned roughly eight deploy attempts on exactly that. Probe
with a field the DTO already carries.

On a BAGGED type the mistake reverses. A newly added server field now DOES appear in the output, so
"the field is in describe, therefore clio supports it" is false — the value can be in the payload
while every guard and code path that should act on it sees nothing, because none of them can address
it. That is worse than the original, which at least failed visibly: here the read-back looks
complete and the behaviour is missing.
