---
description: crt.GaugeWidget never validates min/max or its threshold keys - it only shifts values by min, so an impossible scale renders a broken dial with no error anywhere
applies-to:
  - clio/Command/SchemaValidationService.cs
  - clio/Command/McpServer/Tools/GaugeWidgetValidation.cs
ticket: ENG-95576
date: 2026-08-26
---

**What is true** — `crt.GaugeWidget` performs no validation of its own scale. Its config setter does
exactly two arithmetic things: it computes the rendered axis as `config.max - config.min`, and it
re-keys every `config.thresholds` entry by the same offset (`+|min|` when `min` is negative,
`-min` otherwise). Nothing checks that `min < max`, that a threshold key is numeric, or that a key
falls inside `[min, max]`.

Two further consequences of the same code path are easy to miss: every threshold key is also emitted
as a **scale label** (a marker), and `config.max` is appended as the final label — so the threshold
map doubles as the dial's tick marks.

**Why it is this way** — the widget treats the scale as authored data, not as input to be checked.
It is populated by the interface designer, whose properties panel only ever produces a consistent
`min`/`max`/`thresholds` triple, so the runtime never had a reason to defend against an inconsistent
one. An agent authoring the page body directly has no such guard rail.

**What breaks if you ignore it** — the failure is entirely silent. `min >= max` yields a broken
axis; a key outside the range yields a band that is never painted *plus* a stray label on the dial;
a non-numeric key is dropped. The page saves, compiles and renders, and no error surfaces in the
browser, in the designer, or in any platform response — the only symptom is a dial that looks wrong
to a human. This is why `SchemaValidationService.ValidateGaugeWidgetConfig` enforces the scale
rules **without** the component registry and does not fail open the way the chart walk does: the
rules are decidable from the page body alone, and nothing downstream will catch them.
