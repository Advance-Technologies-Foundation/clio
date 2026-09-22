---
description: converting a sub-process element to multi-instance in the Creatio designer also sets useBackgroundMode=true, unasked, and de-converting sets it back to false - so a clio-built multi-instance element differs from a designer-built one, and the shipped corpus's background-mode count is a default rather than a choice
applies-to:
  - spec/eng-99856-multi-instance/
ticket: ENG-99856
date: 2026-09-22
---

**What is true** — the designer has **no dedicated control** for making a sub-process element
multi-instance. The conversion is a side effect of mapping a collection into one of the element's
parameters. `ProcessFlowElementPropertiesPage.js` wires it:

```js
t.on("collectionMappingSet",   this._convertElementToMultiInstanceMode, this);
t.on("collectionMappingReset", this._convertElementToSingleInstanceMode, this);
```

and `_convertElementToMultiInstanceMode` ends with

```js
this.set("useBackgroundMode", true);
```

while `_convertElementToSingleInstanceMode` sets it back to `false` and resets `backgroundModePriority`
to `Inherited`. Neither is asked for and neither is announced.

Two consequences, both measured on a stand (2026-09-22, `CrtProcessBuilder 1.6.6.0`):

1. **A clio-built multi-instance element is not identical to a designer-built one.** clio's conversion
   leaves `useBackgroundMode: false`; `describe-business-process` reports it that way on an element this
   feature built.
2. **The shipped corpus's background-mode count is not a corpus of decisions.** 39 of the 61 shipped
   multi-instance elements carry background mode; that is the designer's default arriving with the
   conversion, not 39 authors choosing it. Do not read it as evidence that background mode is the
   recommended shape.

And background mode is not free. On the same stand, three iterations of the same graph took **1282 ms**
with `Parallel` + `useBackgroundMode` against **105 ms** for `Sequential` without it, and the iterations
still did not overlap — each was spaced about 300 ms behind the previous through the background job
queue.

**Why it is this way** — the conversion is modelled as a consequence of the mapping the user just made
rather than as a mode switch, so the designer has to guess the rest of the configuration. Background mode
is the safe guess for an element that may now run many times, and it is cheap to reverse from the
checkbox that is already on the panel.

**What breaks if you ignore it** — two different ways round.

Match the designer and every conversion a caller asks for silently costs an order of magnitude in wall
clock, for a flag they did not mention. Diverge from it, as this feature currently does, and an element
built by clio and an element built by hand differ in a user-visible checkbox — which is the kind of
difference that gets reported as a clio defect long after the conversion, by someone comparing two
elements that were supposed to be the same.

Either is defensible; what is not defensible is picking one without knowing the other exists.
