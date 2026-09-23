---
description: A FIRST multi-instance conversion that hands an element straight to the platform rebuild destroys every value mapped onto it and strands its mapping rows - the parameters must be moved into the collections first; once they are there the rebuild is non-destructive, and its counter self-heal opens with ProcessSchema.SystemUserConnection so it cannot run in a unit fixture at all
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-99856-multi-instance/
ticket: ENG-99856
date: 2026-09-21
---

**What is true** — two properties of `ProcessSchemaActivity.SynchronizeParametersInternal`, both measured
while writing ENG-99856's tests, and each of which broke a probe built on the opposite assumption.

**1. The rebuild is not destructive over the callee's contract — but ONLY once the collections already
carry it.** On a FIRST conversion, with two freshly minted empty collections, it is destructive in a way
that is easy to miss and reports success: `LoadCollectionParameters` loads nothing, `Parameters.Clear()`
then drops every parameter the element had **with its `SourceValue`**, and `FillNewSchemaParameters`
re-creates each from the callee under a **fresh** `UId`
(`CreateElementParameterFromUserTaskSchemaParameter`, `:308-320`) while adding a second mapping row
beside the stranded original — the bare `Parameters.Clear()` on that path is not the `ClearParameters()`
variant that removes rows. **A tool converting an element must therefore MOVE its parameters into the two
collections first**, which is what the designer's `_fillCollectionParameters` does and what makes
`GetHasNotContainedTargetParameter` (`:227-236`) find each existing row's target and return false.

With the collections populated, the round trip is non-destructive, and the usual description of it is a
misreading of the lines: `:378-379` take **clones** of the two collections;
`:387-388` clear the **clones'** item properties; `:390` `FillCollectionParameters` refills them from the
synchronized `Parameters`; `:393-394` attach the clones. What mutates the cached graph in place is
`Parameters.Clear()` at `:385` and `:391`.

Nor does the ordinary diff inside it prune what you might expect. `GetRemovedSchemaParameters`
(`:253-270`) removes a parameter only when it has no mapping **and** `CreatedInSchemaUId == SchemaUId` —
the **callee's** schema — or when its mapping source is gone **and** `!target.IsDynamic`. `IsDynamic` is
"created in the CALLER's schema", so anything a tool or the platform stamps with the caller survives.
Measured: an item property added to a converted element's input collection with the caller's stamp is
still there after a full rebuild.

What the rebuild DOES drop is an extra **root** parameter, because `Parameters.Clear()` runs and only the
five are re-loaded. That is the observable to use when you need to prove a rebuild ran.

**2. The counter self-heal cannot run without a system connection.** `CreateIntegerParameter` (`:430-431`)
opens with `UserConnection userConnection = ProcessSchema.SystemUserConnection;`. A schema standing on a
substituted `ProcessSchemaManager` — which is how every package unit fixture builds one — does not supply
it, so the self-heal path throws `NullReferenceException` **inside the platform** rather than doing
anything. The path itself is real: `TryCopyParameter` (`:458-461`) returns null for a missing counter, and
for a dangling UId exactly as for `Guid.Empty`, so both shapes reach it identically.

**Why it is this way** — the platform is protecting a round trip, not rebuilding from scratch:
`ProcessSchema.SynchronizeParameters` walks every parametrized element on **every design-time read**, so a
genuinely destructive rebuild would have destroyed all 61 shipped multi-instance elements long ago. And
`CreateIntegerParameter` needs a connection because it resolves `DataValueTypeManager` through one.

**What breaks if you ignore it** — for the first fact, a caller's configuration disappears on conversion
with a success reported and no notice: the values are gone, the element looks correctly converted, and the
duplicate mapping rows are invisible from any read API. That shipped in one commit of ENG-99856 and was
caught by review rather than by a test, because every test built its element fresh and had no value to
lose.

For the rest, you write a test whose premise is wrong and then "fix" production to satisfy it. Both
happened here:

- A probe that added an item property and expected the rebuild to discard it failed, and the tempting
  conclusion was that the applier's ordering guarantee was not holding. It was; the probe was measuring a
  property the platform does not have.
- A probe that removed a counter to watch it be re-created NRE'd inside `CreateIntegerParameter`, which
  reads as a defect in the code under test. It is a property of the fixture.

The second one has a design consequence, not just a testing one: **a story that must assert "a
two-root-parameter element is not refused" has to assert it at the applier's own boundary** — that the
applier does not refuse — and leave the platform's synthesis to a stand measurement. There is no unit-level
route to it.
