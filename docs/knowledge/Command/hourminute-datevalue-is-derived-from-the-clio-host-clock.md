---
description: the HourMinute datePart filter's UTC dateValue carrier is computed from the clio process clock (DateTimeOffset.Now) in SimpleToFullFilterConverter, not from the target environment's timezone
applies-to:
  - clio/Command/BusinessRules/Filters/Esq/SimpleToFullFilterConverter.cs
  - clio/Command/BusinessRules/Filters/Esq/EsqEnvelopeDtos.cs
date: 2026-08-19
---

**What is true** — `SimpleToFullFilterConverter.BuildDatePartCompareFilter` emits a HourMinute
time-of-day comparison as a pair: a local, quote-wrapped ISO `value` that the Freedom UI lookup control
renders, and a UTC `dateValue` the query uses. Both are built from `_now()`, which defaults to
`DateTimeOffset.Now` — the **clio process's** local offset (`_now = nowProvider ?? (() =>
DateTimeOffset.Now)`). The offset of the target Creatio environment is never consulted.

**Why it is this way** — the date part of the carrier is arbitrary (HourMinute extraction ignores it),
so the converter only needs *some* date whose local and UTC forms agree. The host clock is the only one
available offline, and the `nowProvider` seam exists so tests can pin a fixed offset. The code comment
explains why the two forms stay self-consistent; it does not say whose clock defines "local".

**What breaks if you ignore it** — a filter authored from a machine in a different timezone than the
environment still *renders* correctly, because the displayed `value` comes from the same host clock that
produced the mismatched `dateValue`. There is no error and nothing looks wrong in the designer. If you
are investigating a time-of-day filter that reads right but selects the wrong records, check the offset
of the machine that created the rule before suspecting the converter, and consider sourcing the
environment's timezone into `nowProvider` rather than editing the carrier format.
