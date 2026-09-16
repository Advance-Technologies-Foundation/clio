---
description: a containers pair whose mobile side the probed template lacks emits a merge the applier refuses the WHOLE array over, but dropping that pair instead also drops every element under it - the walk descends into the twin, so the pair is the subtree's only route onto the page
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
ticket: ENG-95827
date: 2026-09-15
---

**What is true** — a `containers` pair whose `mobile` side is neither provided by the probed mobile
template nor created by a `declaredElements` entry still emits a `merge` on that name. A merge
resolves by `name` alone, and the applier validates every operation before applying any, so such an
operation makes it refuse the **whole** `viewConfigDiff`, not just that one entry. Nothing in the
response says why.

Dropping the pair instead of emitting it looks like the obvious fix. It is not: in `WalkElements`
the twin branch is also what **descends** into the source element's children
(`WalkElements(ctx, items, twinMobileName, ...)`). `continue`-ing out of that branch takes the
entire subtree with it — on a paired *container*, every field and button under it silently stops
converting.

**Why it is this way** — a pair is the walk's only route from a web container to its mobile
counterpart, so "do not emit this pair" and "do not convert what is inside it" are the same branch.
Master's ENG-94838 chose to report such pairs in the `constraints` prose and emit the merge anyway;
this branch deleted `constraints`, so today the situation is not reported at all.

Making the drop safe means separating the two decisions — suppress the twin's own operation while
still walking its children into some resolved parent — which is a real change to the walk, not a
guard at the emission site.

**What breaks if you ignore it** — adding the guard at the emission site passes a targeted test and
breaks the fixtures that pair a container onto a name the harness did not bother to declare
(`FeedTabContainer -> FeedContainer` with only `Feed` in `mobileTemplateTypesByName`, and four
more). In production the same shape occurs whenever the probe map is incomplete for any reason, and
the symptom is not an error: the conversion succeeds and the page comes back missing everything that
lived in that container.
