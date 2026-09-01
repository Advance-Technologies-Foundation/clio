---
description: update-page --dry-run in append mode fetches the schema and runs the real merge, so it is not offline and not free; the dry-run branch must stay AFTER body resolution or the projection and the body detectors silently regress
applies-to:
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/PageBodyMerger.cs
  - clio/Command/PageInsertDowngradeDetector.cs
  - clio/Command/PageInertOperationDetector.cs
ticket: GH-1150
date: 2026-08-31
---

**What is true** — `PageUpdateCommand.TryUpdatePage`'s `options.DryRun` branch sits **after**
`TryProjectDryRun`, which for `mode: append` loads the schema (`TryLoadSchemaForSave`) and runs the
real merge (`TryResolveBodyToWrite`). So an append dry run:

- performs one designer `GetSchema` round trip — it is **not** offline and not free;
- can **fail**, with the same error the save would produce (a full-config current body, for instance),
  where it previously returned `success: true`;
- runs `PageInsertDowngradeDetector` and `PageInertOperationDetector` against the **projected final
  body**, so it sees pairs formed between the caller's fragment and the server's body;
- returns `appendProjection` — the counts plus the replaced and dropped operation labels.

`appendProjection` covers `viewConfigDiff` only, and the XML docs on `PageAppendProjection` say why
in full. The gap worth knowing: `MergeHandlersRaw` drops **every** current handler whose `request`
appears in the incoming fragment (`RemoveHandlersWithRequests`), so a current body carrying one
`request` twice keeps neither and the fragment contributes one — the same shape of quiet loss, in a
section the projection does not read. Widening it requires giving that raw-text merge a structured
identity first; do not "fix" it by reporting zeros for handlers, which reads as coverage.

`mode: replace` is deliberately excluded: it writes the body verbatim, so it stays exactly as offline
and as cheap as before. `sync-pages` pins `replace` and never reaches the merger, so it is unaffected.

**Why it is this way** — a dry run exists to answer "what will this write do?", and in append mode
that question cannot be answered without the server's body: the written body is a function of both
sides. The projection is produced as a by-product of the one real merge (`PageBodyMerger.Merge`'s
`out PageAppendProjection` overload) rather than by a predictor, because a second implementation of
the merge identity would be free to disagree with the save — and a dry run that confidently predicts
the wrong outcome is worse than one that predicts nothing.

The nearby comment in `PageUpdateTool.ResolveSyntaxFailure` — "a body that cannot parse triggers no
Creatio I/O even in dry-run" — is still true and is a different claim: that path rejects an unparseable
body before `TryUpdatePage` is ever reached.

**What breaks if you ignore it** — moving the dry-run return back above `TryProjectDryRun`, or
short-circuiting it "because a dry run should not hit the network", restores the GH-1150 defect
silently: `update-page --mode append --dry-run` reports `success` with no projection, its two body
detectors inspect the incoming fragment instead of the body that would be saved (so every pair formed
with the server's body goes unreported until the real write), and an append the save will reject passes
the check that existed to catch it. Nothing fails loudly — the response simply stops saying anything,
which is the exact shape of the original bug report.
