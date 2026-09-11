---
description: EnsureAtMostOneDefault runs before NormaliseForADecidingGateway and has no gateway exclusion, so an explicit kind:default off a gateway is refused by IT, not by the normalisation - and its remedy sentence is correct off an ordinary element while being a silent no-op off a gateway
applies-to:
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — two guards in `FlowKindRules` refuse a second default, and which one a caller hits
depends on how they asked, not on what the source is:

- `EnsureAtMostOneDefault` fires when the caller writes `kind: default` **explicitly** and a default
  sibling exists. It runs *before* `NormaliseForADecidingGateway` and carries **no gateway exclusion**,
  so it answers gateways and ordinary elements alike.
- `NormaliseForADecidingGateway` fires only when the kind is omitted or `sequence` off a deciding
  gateway — the normalisation path.

An author adding a fallback names it, so the **explicit** route is the likely one, and it is answered by
the first guard. Both guards therefore have to name a remedy that works.

The remedy is `setFlow` with `kind: conditional` on the existing default. The route that looks right and
is not — "re-kind the existing default to plain to free the slot" — fails **differently by source type**,
measured rather than reasoned:

| source | re-kind the default to `sequence` | kind afterwards |
|---|---|---|
| deciding gateway | reports success | still `default` — nothing written |
| ordinary element | reports success | `sequence`, and the default the caller wanted is then refused by `EnsureADefaultHasSomethingToFallBackFrom` |

Off a gateway a flow is not its own sibling, so the scan finds no default, the normalisation sends the
plain flow straight back to `default`, and the no-op check returns before `NoticeIfNormalised`. Neither
arm reaches the state the caller asked for; one says nothing about it.

**Why it is this way** — the guard order is deliberate: `EnsureAtMostOneDefault` is an arity rule that
must hold before any normalisation rewrites the requested kind, or the normalisation would accept a
second default. And the no-op-before-notice ordering is itself a fix — the old order announced a
normalisation for a write that never happened — so neither is reversible. What falls out is that the
same remedy sentence has to serve two guards, and it is **conditionally correct**: sound off an ordinary
element, a silent trap off a gateway. That is why it survived review twice. A sentence that is simply
wrong gets caught; one that is wrong only on one arm reads as fine to anyone testing the other.

**What breaks if you ignore it** — you fix the copy you were shown and ship the copy callers reach. That
is what happened here: a blind manual run traced the silent no-op to the normalisation message, the
remedy there was corrected, and `EnsureAtMostOneDefault` kept the ambiguous wording — so the *explicit*
`kind: default` caller, the likelier one, still received advice that reports success and writes nothing.
Three string assertions passed throughout, because each read a message rather than following it.

The sentence now lives in one place, `FreeTheDefaultSlot()`, named by both refusals, so a third
correction cannot land in one copy. If you add a third guard that refuses because the default slot is
taken, name that helper instead of writing the advice again.

Two tests pin it, and they are worth keeping in the shape they are: one **follows** the remedy off a
gateway (the route must work *and* the refused edit must then succeed — an earlier shape freed the slot
for the next call to refuse anyway), and one pins the warned-about route as the silent no-op it claims,
so the warning cannot outlive the behaviour it describes. Mutation-checked: restoring the ambiguous
sentence reddens the first.

Related: [[conditional-flow-rekind-must-be-in-place]] for why the re-kind replaces the object, and
[[three-defences-and-no-oracle-is-an-unpinned-rule]] for the assertion shape that let this through.
