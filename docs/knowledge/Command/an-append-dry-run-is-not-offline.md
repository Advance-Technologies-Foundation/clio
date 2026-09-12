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

- performs one designer `GetSchema` round trip. Note the magnitude honestly: an append dry run was
  **never** offline — `TryResolveContext` already issued a `SysSchema` `SelectQuery`, `GetDesignPackageUId`
  and `GetParentSchemas`, and the MCP layer already probed the platform version. This is the fourth or
  fifth call, roughly **+25% dry-run latency**, not a transition from local to networked;
- can **fail**, with the same error the save would produce (a full-config current body, for instance),
  where it previously returned `success: true`. The failure response stamps `dryRun: true` so it stays
  distinguishable from a failed real save;
- runs `PageInertOperationDetector` against the **projected final body**, so it sees pairs formed between
  the caller's fragment and the server's body. `PageInsertDowngradeDetector` is deliberately NOT called
  here: it cannot fire on this path. It needs the prior body to introduce a component with an `insert`
  that the final body drops for a transform, and an append never produces that shape — a current
  `insert X` is only ever replaced by an incoming entry of the same identity (another `insert X`), and
  every non-matching current entry is carried over. Calling it would be dead code implying coverage it
  cannot give;
- returns `appendProjection` — the counts, the replaced labels, and **three separate loss channels**
  (`droppedOperations` from the server body, `collapsedIncomingOperations` from the caller's own
  fragment, and `viewConfigDiffApplied: false` when the merged array cannot be written back at all).
  Two of the three warn. The collapsed-incoming channel deliberately does NOT: the fragment is the
  caller's own and they can read it, so a warning about their own input would be noise — the same rule
  the superseded-drop warning was set with. It is still counted, because without it the reported totals
  cannot be reconciled and a real loss stays invisible.

`appendProjection` covers `viewConfigDiff` only; the XML docs on `PageAppendProjection` carry the
reasoning. The uncovered sibling is handlers — `MergeHandlersRaw` can drop a duplicated current
handler — which the DTO documents rather than reporting zeros for.

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
silently: `update-page --mode append --dry-run` reports `success` with no projection, the inert-operation
check inspects the incoming fragment instead of the body that would be saved (so every pair formed with
the server's body goes unreported until the real write), and an append the save will reject passes the
check that existed to catch it. Nothing fails loudly — the response simply stops saying anything, which
is the exact shape of the original bug report.

The same failure mode applies to the projection's own honesty, and this is the subtler trap. Two of the
three loss channels exist because the first version of this fix reported only `droppedOperations` while
asserting in four places that it was the only way an append loses an operation. It was not: a fragment
carrying one identity twice silently keeps the last spelling (`collapsedIncomingOperations`), and a web
body with no `SCHEMA_VIEW_CONFIG_DIFF` marker pair discards the merged array entirely while the counts
still described it (`viewConfigDiffApplied`). Both were caught by review, not by tests. If you add a
fourth way for the merge to lose an operation, it must land in this projection AND in a warning — a
projection that is silent about a loss is worse than no projection, because the caller now trusts it.
