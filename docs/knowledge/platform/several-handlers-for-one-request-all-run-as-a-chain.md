---
description: several handlers for the same request in one Freedom UI page schema are legal and all run as a chain in schema order, so the append merge replacing every current handler of a request loses code - why update-page reports appendProjection.removedHandlers
applies-to:
  - clio/Command/PageBodyMerger.cs
ticket: ENG-107305
date: 2026-11-24
---

**What is true** — measured on a stand (core 10.2.959) on 2026-11-23 for ENG-107260 (c): two handlers for
the same `request` in one page schema both run, as a chain in the order they appear: the first wraps the
second (H1 before, H2 before, platform, H2 after, H1 after). A handler that does not call `next` skips every
later handler for that request without any error.

**Why it is this way** — page handlers are a chain of responsibility: each one receives `next` and the
runtime does not deduplicate entries by `request`. "One handler per request" was never a platform rule; it
came only from how clio's append merge treats handlers.

**What breaks if you ignore it** — `PageBodyMerger.MergeHandlersRaw` replaces every current handler whose
`request` the fragment declares, by design, so a re-sent handler does not run twice. On a body that chained
two handlers for a request, an append therefore removes code that ran. `appendProjection.removedHandlers`
(`hadDuplicates: true`) is the only place the caller learns this; dropping that report brings back the silent
loss. Treating "one handler per request" as a platform rule (as a migration orchestrator once did) is wrong.
