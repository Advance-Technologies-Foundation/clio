---
description: MobileDiffApplyValidator.ApplyViewConfigDiff applies the diff against new JArray(), so parentName/propertyName on an inherited node (Scaffold, MainContainer) is never checked - NeedsResolvedBase covers path diffs only
applies-to:
  - clio/Command/McpServer/Tools/MobileDiffApplyValidator.cs
  - clio/Command/McpServer/Tools/MobilePageValidation.cs
  - clio/Command/PageModels.cs
ticket: ENG-95429
date: 2026-08-19
---

**What is true** — the mobile pre-flight differ check runs `new JsonDiffApplier().Apply(new JArray(), operations)`,
i.e. against an EMPTY base. Its not-a-container / missing-parent oracle therefore only sees parents that the same
`viewConfigDiff` declares. An insert that targets a slot on a node the page INHERITS from its template
(`Scaffold`, `MainContainer`, any template container) cannot be resolved at all, so the check reports nothing about
it. This is asymmetric with the path diffs: `NeedsResolvedBase` returns true only for `viewModelConfigDiff` /
`modelConfigDiff`, which do get a real merged base, and a `viewConfigDiff`-only body is deliberately excluded.

**Why it is this way** — there is no base to hand it. `PageGetResponse` carries `BaseViewModelConfig` and
`BaseModelConfig` but no `BaseViewConfig`, so the merged view config never reaches the validator. Closing the gap
means making `NeedsResolvedBase` return true for `viewConfigDiff`-only bodies, which changes the read profile of
`sync-pages` (an extra resolved-base read inside the sync path) and can start rejecting bodies that pass today.
That trade was judged not worth it while the rule set was being stabilised, so the gap is deliberate, not an oversight.

**What breaks if you ignore it** — a green `validate-page` on a mobile body is NOT evidence that its inserts have
real parents. If you assume the check is complete and build another rule on top of it (or tell a caller that a
passing validation means the diff applies), a diff whose parents all come from the template will pass validation
and still author nothing. Before "fixing" it, expect the two costs above; a naive flip of `NeedsResolvedBase` is
the predictable wrong move.
