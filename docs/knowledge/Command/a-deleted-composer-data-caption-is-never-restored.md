---
description: the platform never restores a Timeline composer's data.caption once it is removed from viewConfigDiff, so deleting the key is silent data loss, not a validation workaround
applies-to:
  - clio/Command/SchemaValidationService.cs
ticket: "#1298"
date: 2026-09-03
---

**What is true** — `crt.EmailComposer` and `crt.FeedComposer` carry their own descriptor under
`values.data` (`uId`, `schemaType`, `typeName`, `caption`), and the platform writes `data.caption`
as the plain literals `"Email"` / `"Feed"`. If that key is removed from the page body and the page
is saved, the platform does **not** put it back: a read-back shows the composer descriptor
permanently without a caption.

**Why it is this way** — the descriptor is stored data, not a defaulted runtime value. Freedom UI
round-trips whatever the schema holds; there is no server-side re-seeding step for a composer that
was saved with a partial descriptor.

**What breaks if you ignore it** — clio's localizable-text rule used to reject `data.caption`, and
"just delete the caption" makes validation pass, so it reads like a fix. It is not: the composer
loses its caption on the stand for good, and no later save restores it. That is why the validator
skips the `data` descriptor of a node that declares a component `type` instead of asking authors to
edit or drop the key.
