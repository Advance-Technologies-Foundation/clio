---
description: the action-target probe reads the MobileRelatedPage add-on with a CHEAPER request shape than RelatedPageAddonService (Guid.Empty parent, the source page's package), and because GetSchema auto-provisions the descriptor a mis-addressed read returns "{}" - which classifies as Missing, not blank
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/MobileActionTargetProbe.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideModels.cs
  - clio/Command/RelatedPages/RelatedPageAddonService.cs
ticket: ENG-94839
date: 2026-09-09
---

**What is true** — nobody has observed the probe's cheap add-on request shape returning `Resolved` on a
live stand; the only stand evidence on this path is three objects reported absent. And the shape cannot
fail loudly: `GetSchema` **auto-provisions** the descriptor when it does not exist, so a mis-addressed read
plausibly returns a fresh `{}` or `{"Pages":[]}` body, which is not blank and therefore classifies as
`Missing` — pinned by `ClassifyRelatedPageMetadata_NoPagesKey_IsMissing`. Only a genuinely BLANK body
degrades to `Unknown`.

**Why it is this way** — resolving the object's own package and parent schema costs an entity-schema
designer round trip per object, on top of the add-on read, in a tool whose point is to be a cheap
best-effort probe. `MobileActionTargetProbe.StripsBindingOnMissing` is the compensating control: an
`entity-default-mobile-page` verdict is REPORTED and never removes an action, precisely because this read
cannot distinguish "the object declares no default mobile page" from "I asked the wrong question".

**What breaks if you ignore it** — extend `StripsBindingOnMissing` to the object kind and every
`crt.CreateRecordRequest` / `crt.UpdateRecordRequest` on a converted page silently loses its action
whenever the cheap read is wrong: the button is still there and does nothing, and the developer finds out
in the mobile app. Verify the request shape against an object that HAS a configured default mobile page
before treating this verdict as proof, and delete this record when you do.
