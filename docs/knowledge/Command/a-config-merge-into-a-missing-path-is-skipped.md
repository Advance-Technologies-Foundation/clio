---
description: a viewModelConfigDiff/modelConfigDiff merge whose path does not exist is skipped by the Creatio differ (it never creates the missing key) - the save succeeds and the operation silently has no effect, so update-page warns since GH-1753 and the warning must stay advisory
applies-to:
  - clio/Command/PageUnresolvedMergeDetector.cs
  - clio/Command/PageBundleBuilder.cs
  - clio/Command/JsonPathDiffApplier.cs
  - clio/Command/JsonDiffApplier.cs
  - clio/Command/PageUpdateOptions.cs
ticket: GH-1753
date: 2026-10-07
---

**What is true** — the client differ for `viewModelConfigDiff` / `modelConfigDiff`
(`json-path-applier.service.ts`, cloned by `JsonPathDiffApplier`) resolves a `merge` by walking its
`path` with lodash `get` through the config produced by the parent schemas plus the merges that come
before it in the same body. A missing segment makes `_merge` return `false`. `_applyOperations`
discards that list, so the merge is skipped without a trace and the missing key is NOT created.
Verified on a live 8.x stand, with the outcome read from `get-page`'s bundle: `path: ["dataSources",
"NewDS"]`, `path: ["dependencies"]` on a page without dependencies, `path: ["attributes", "NewAttr"]`
and a missing nested key under a parent-layer data source (`config.sortingConfig`) were all saved and
none took effect. `path: ["dataSources"]` with the new key inside `values`, `path: []`, and paths
whose every segment exists all applied. The parent layer itself is irrelevant; only segment existence
matters. clio-knowledge guidance (`related-data/list.md`) still recommends `path: ["dependencies"]`,
which is inert whenever no layer has declared `dependencies` yet.

The runtime does not start from an empty config. The client `BaseSchemaBuilderService.getEmptySchemaPart`
puts an empty `attributes` object into `viewModelConfig` and a `PageParameters` data source
(`crt.PageParametersDataSource`) into `modelConfig`, before the first schema is applied. So
`path: ["attributes"]` and `path: ["dataSources"]` apply even on a `BlankPageTemplate` chain where no
schema declares them. This was checked in the browser: a label and a grid bound to such merges
rendered their values. A `path: ["attributes", "New"]` merge on the same page rendered an empty label,
so a missing path is skipped at runtime too, not only in clio's clone. The detector starts from the same
seed. **`get-page`'s bundle (`PageBundleBuilder`) does not** — it shows no `attributes` / `dataSources` on
such a page — so the bundle is not a reliable oracle for whether these two top-level paths exist.

**Why it is this way** — the behaviour is the platform's, so clio cannot make the operation apply.
`PageUnresolvedMergeDetector` replays the inherited chain and the body through the same applier, and
the applier's `JsonApplierOperationsOptions.UnresolvedMerges` sink makes the discarded list visible.
The sink only observes it. The result is an advisory `warnings` entry on `update-page`, on its dry
runs and on `sync-pages`. It is not a rejection, by deliberate choice: an existing page can carry such
an operation from an earlier save or a parent change, and refusing it would block every unrelated
save of that page. The check needs the parent schemas. On the normal path it reuses the hierarchy
that context resolution already read. On the `target-schema-uid` path it reads them itself
(`GetDesignPackageUId` + `GetParentSchemas`), and on the create-replacing path with `GetParentSchemas`.
That happens only when the body has a merge with a non-root path, and the read is shared with the
`parentName` check through `EditableSchemaContext.InheritedHierarchy`. A read failure becomes a "could
not check" warning, never a failed save. That includes a timeout surfacing as `TaskCanceledException`,
since no cancellation token reaches this path. A falsy value at the path (`null`, `false`, `0`, `""`)
counts as missing, as in the client's `!itemInfo.item` test. Before GH-1753 the clone threw an
`InvalidCastException` there. A path that ends on a non-falsy single value or an array still makes the
clone throw `InvalidCastException`, while the runtime merges nothing into it. The detector then replays
the merges one at a time to name that merge with its own reason. A merge whose `values` is not an
object is reported separately and left out of the replay.

**What breaks if you ignore it** — replace the replay with a shortcut, such as checking the merge
against the body alone or against `get-page`'s bundle of the CURRENT page: it reports merges that
the parents satisfy, or misses merges that only an earlier merge in the same body satisfies (array
order matters inside the merge group). Promote the warning to an error, and saving any page that
already carries a stale merge becomes impossible. Drop the sink, and the issue's false
`success:true` comes back with no failing test except the ones that pin this record.
