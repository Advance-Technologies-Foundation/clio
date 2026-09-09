---
description: SchemaResourceManager matches resource items by NAME case-insensitively and OVERWRITES on a duplicate instead of throwing, so two flows whose generated names collide (SequenceFlow_<source>_<target> with an underscore in an element name) share one row and one flow shows the other's label
applies-to:
  - clio/Command/ProcessModel/FlowLabelExpectation.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - docs/knowledge/platform/a-flow-rekind-does-not-orphan-its-label.md
ticket: ENG-91853
date: 2026-09-09
---

**What is true** — a schema's localizable resources are assembled by name, and a duplicate name is
resolved by LAST WRITE WINS, silently. `Terrasoft.Core/SchemaResourceManager.cs:337-347`:

```csharp
ResourceItem findItem = rootGroup.Items.Find(item =>
    string.Equals(item.Name, resourceItem.Name, StringComparison.InvariantCultureIgnoreCase));
if (findItem == null) {
    rootGroup.Items.Add(resourceItem);
    continue;
}
if (resourceItem.ItemType == ResourceItemType.String) {
    ...
    findItem.StringValue = resourceItem.StringValue;
}
```

No exception, no notice, and the comparison is case-INSENSITIVE, so names differing only in case
collide too.

**Why it matters for a flow label** — a flow's label is stored under a key derived from the flow's
own generated NAME (`BaseElements.<FlowName>.Caption`), and the toolkit generates that name as
`<Prefix>_<source>_<target>` (`ProcessDesignConstants.SequenceFlowNamePrefix` = `SequenceFlow_`,
plus `ConditionalFlow_` and `DefaultFlow_`). That pattern is not injective when an element name
contains an underscore: `A -> B_C` and `A_B -> C` both yield `SequenceFlow_A_B_C`. The write path's
duplicate guard does not catch it, because it refuses a duplicate endpoint PAIR and these are two
different pairs.

Before labels existed this was only an addressability nuisance — a toolkit flow carried no caption,
so it produced no resource row at all. A label turns the name into a STORAGE KEY, and the collision
becomes one row for two flows: whichever saves last wins, and the other connector draws the wrong
words. Nothing reports it.

**How reachable it is** — not through a toolkit-built process on its own: naming rule N5 prescribes
underscore-free element codes, so clio's own generated names cannot collide. It needs an element
whose name carries an underscore, which means a designer-authored or hand-written name — and those
are exactly the processes a label edit lands on.

**Guarded, since CrtProcessBuilder 1.6.0.10** — `ProcessGraphBuilder.RefuseCollidingResourceKey`
refuses a label WRITE on a flow whose generated name is not unique in the schema, naming the cause
(an element code carrying an underscore). Scoped to a label write deliberately: refusing every
collision would newly reject graphs that work today, since a collision has always been tolerated
while both flows carried no caption, and shipped processes are in that state. So the corruption is
now unreachable through the toolkit — but the PLATFORM behaviour above is unchanged, and anything
else that writes a schema resource is still subject to it.

**What breaks if you ignore it** — below 1.6.0.10, or through any other writer, you go looking for a
bug in the label write path, or in clio's read-back guard, for a label that is drawn on the wrong
connector. Both are working correctly: the
label was stored under a key that another flow also claims, one row up in the platform. The
read-back guard cannot see it either, because it matches flows by endpoint pair and both pairs
resolve to a flow whose caption is whatever survived. Check for a duplicate generated flow name
before suspecting anything else.
