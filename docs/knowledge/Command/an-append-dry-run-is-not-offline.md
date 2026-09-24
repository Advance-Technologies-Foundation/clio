---
description: update-page --dry-run in append mode fetches the schema and runs the real merge, so it is not offline; the dry-run branch must stay AFTER body resolution or the projection and the body detectors silently regress
applies-to:
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/PageBodyMerger.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/PageInsertDowngradeDetector.cs
  - clio/Command/PageInertOperationDetector.cs
ticket: GH-1150
date: 2026-08-31
---

**What is true** — `PageUpdateCommand.TryUpdatePageCore`'s `options.DryRun` branch sits **after**
context resolution, and `TryCompleteDryRun` for `mode: append` loads the schema
(`TryLoadSchemaForSave`) and runs the real merge (`TryResolveBodyToWrite`). So an append dry run:

- performs one designer `GetSchema` round trip and is **not** offline. It was never offline:
  `TryResolveContext` issues a `SysSchema` `SelectQuery`, `GetDesignPackageUId` and
  `GetParentSchemas` before either mode is chosen, the baseline guard adds a conditional checksum
  row, and the MCP layer probes the platform version, so `GetSchema` is the fifth or sixth call on
  that path;
- can **fail**, with the same error the save would produce (a full-config current body, for
  instance). Every dry-run failure carries `dryRun: true` and the schema name, so it stays
  distinguishable from a failed real save;
- runs the SAME body checks the save runs, against the projected final body: the inert-operation
  detector (so it sees pairs formed between the caller's fragment and the server's body), the
  insert-downgrade detector, and the save's own authoritative widget-caption gate — the last
  reported as a warning rather than a refusal, because a dry run's job is to say what would happen,
  not to refuse. Severity is the only difference between the two paths;
- returns `appendProjection` — the counts, the replaced labels, and **three separate loss channels**
  (`droppedOperations` from the server body, `collapsedIncomingOperations` from the caller's own
  fragment, and `viewConfigDiffApplied: false` when the merged array cannot be written back at all).
  Two of the three warn. The collapsed-incoming channel deliberately does NOT: the fragment is the
  caller's own and they can read it, so a warning about their own input would be noise. It is still
  counted, because without it the reported totals cannot be reconciled and a real loss stays
  invisible.

**The dry-run stamp has two owners, not one.** `TryUpdatePage` stamps every failure on its way out
rather than at each failure site, because most exits — body-file load, required-field, common-input,
context resolution, external-modification, input validation — return before the mode is ever
branched on, and `TryUpdatePageCore`'s catch returns a bare envelope. The MCP `update-page` tool then
has its **own** pre-execution exits (body-file load, empty body, the full-config append rejection, JS
syntax, content rules, AST lint) that never call the command at all, so the command's stamp cannot
reach them. Both call `PageUpdateResponse.MarkDryRunFailure`; a third entry point must too. The guard
is `options.DryRun` — stamping unconditionally would make every failed SAVE claim the safety of a dry
run.

**`PageInsertDowngradeDetector` runs on the append path but cannot fire there.** It lives in the
shared `TryPrepareWrite`, which the replace save also uses and where it CAN fire. Firing needs the
prior body to introduce a component with an `insert` that the final body drops for a transform, and
an append never produces that shape: a current `insert X` is only ever replaced by an incoming entry
of the same identity (another `insert X`), and every non-matching current entry is carried over. Do
not read its presence on the append path as coverage.

**An append body must carry at least one marker pair the merge can read.** Marker-integrity
validation is deliberately skipped in append mode, because the incoming body is a fragment and may
omit sections. That skip must not be all-or-nothing: with no readable pair, a body still clears the
syntax gate (a bare JSON array is valid JavaScript), `PageBodyMerger.ReadJsonArray` then returns an
empty `JArray` for every absent marker, the merge becomes a no-op, and the call reports `success`
with `incomingOperationCount: 0` — the caller's whole fragment discarded and described as a clean
no-change. `ValidateAppendFragmentIsRecognizable` rejects that. Keep the rule at ONE recognized pair:
anything stricter re-imposes the completeness requirement append exists to relax. Its recognized set
excludes `SCHEMA_DEPS` and `SCHEMA_ARGS` — required markers the merge never reads from an incoming
body, so accepting them reopens the same silent discard — and deliberately includes the full-config
spellings, which the merge also does not read, so that such a body reaches
`UsesUnsupportedFullConfigForm` and keeps its precise "use `--mode replace`" message instead of the
generic one.

**The superseded-drop sentences are deliberately uncapped**, unlike the three named lists
(`MaxNamedOperations`, 25). The named lists are display labels and can be truncated because the
counts still report the true scale. Each sentence instead names a different component the caller must
go and re-read, and because `droppedOperations` is itself capped, the sentences are the only place a
component past that cap is named at all. Capping both would leave a response saying "30 were dropped"
while five of those components appear nowhere in it.

`appendProjection` covers `viewConfigDiff` only; the XML docs on `PageAppendProjection` carry the
reasoning. The uncovered sibling is handlers — `MergeHandlersRaw` can drop a duplicated current
handler — which the DTO documents rather than reporting zeros for.

**`mode: replace` is narrower than "offline", and saying otherwise misleads.** `TryResolveContext`
runs before either mode is chosen and already reaches the server, so a replace dry run is not an
offline operation. What it skips is `TryCompleteDryRun`'s designer `GetSchema` of the current body —
precisely what `TryUpdatePage_WhenDryRun_SkipsDesignerServiceCalls` asserts for its body without parent references, and no more. Explicit web parent references additionally require the inherited designer hierarchy, including on replace dry runs (GH-1640). Do not
widen that test's name into a claim that the path runs without a server; a caller who plans an
offline workflow on it will find one that cannot run. The cost of that guarantee is the one
divergence left: a replace dry run's caption check resolves only against the explicitly passed
resources, so it is weaker than the save's. Closing it would mean fetching the current body there
too, which is a product decision.

**Why it is this way** — a dry run exists to answer "what will this write do?", and in append mode
that question cannot be answered without the server's body: the written body is a function of both
sides. The projection is produced as a by-product of the one real merge (`PageBodyMerger.Merge`'s
`out PageAppendProjection` overload) rather than by a predictor, because a second implementation of
the merge identity would be free to disagree with the save — and a dry run that confidently predicts
the wrong outcome is worse than one that predicts nothing.

The nearby comment in `PageUpdateTool.ResolveSyntaxFailure` — "a body that cannot parse triggers no
Creatio I/O even in dry-run" — is still true and is a different claim: that path rejects an
unparseable body before `TryUpdatePage` is ever reached.

**What breaks if you ignore it** — moving the dry-run return back above `TryCompleteDryRun`, or
short-circuiting it "because a dry run should not hit the network", restores the GH-1150 defect
silently: `update-page --mode append --dry-run` reports `success` with no projection, the
inert-operation check inspects the incoming fragment instead of the body that would be saved (so
every pair formed with the server's body goes unreported until the real write), and an append the
save will reject passes the check that existed to catch it. Nothing fails loudly — the response
simply stops saying anything, which is the exact shape of the original bug report.

The same failure mode applies to the projection's own honesty, and this is the subtler trap. Two of
the three loss channels exist because reporting only `droppedOperations` is not enough: a fragment
carrying one identity twice silently keeps the last spelling (`collapsedIncomingOperations`), and a
web body with no `SCHEMA_VIEW_CONFIG_DIFF` marker pair discards the merged array entirely while the
counts still describe it (`viewConfigDiffApplied`). If you add a fourth way for the merge to lose an
operation, it must land in this projection AND in a warning — a projection that is silent about a
loss is worse than no projection, because the caller now trusts it.
