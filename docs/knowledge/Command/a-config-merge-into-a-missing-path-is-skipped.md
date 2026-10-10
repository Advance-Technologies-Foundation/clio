---
description: a viewModelConfigDiff/modelConfigDiff merge whose path does not exist is skipped by the Creatio differ (it never creates the missing key) - the save succeeds and the operation silently has no effect, so update-page warns since GH-1753 and the warning must stay advisory; a merge the runtime throws on (single-value target, missing or null values) rejects the save
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
That happens only when the body has a merge other than `path: []` with object `values` (a merge
without `path`, or `path: []` with non-object `values`, also triggers it), and the read is shared with the
`parentName` check through `EditableSchemaContext.InheritedHierarchy`. A read failure becomes a "could
not check" warning, never a failed save. That includes a timeout surfacing as `TaskCanceledException`,
since no cancellation token reaches this path. A falsy value at the path (`null`, `false`, `0`, `""`)
counts as missing, as in the client's `!itemInfo.item` test. Before GH-1753 the clone threw an
`InvalidCastException` there.

Not every merge the runtime fails to apply is skipped. The client `_merge` runs in strict mode, and
these cases were checked with the `deepmerge` package creatio-ui uses. A path that ends on a non-falsy
single value (`5`, `"s"`, `true`) throws `TypeError` when the merged keys are set on the primitive.
The same happens when the first path segment matches an element `_id` and the rest of the path does
not resolve, because the target is then `undefined`. The page fails to build. Missing or `null`
`values` on a target that resolves throws in `Object.keys`. On a target that does not resolve, the
merge is skipped before `values` is read. Even with empty `values` (`{}`, or alias exclusion that
removes every key), an `undefined`/`null` target throws (`deepmerge` reads `Object.keys` of it) and a
non-empty string target throws (the index keys cannot be written back); `5`, `true` and `""` with
empty `values` are no-ops. The clone throws `JsonDiffApplierException` in all of these cases and
carries the cause in `MergeFailure` (`ValuesMissing`, `TargetNotObject`, `TargetUnresolved`). The
detector replays the merges one at a time to name the merge, takes the reason from `MergeFailure`
instead of guessing it from the operation's shape, and reports it as an error, which rejects the save
(the response still carries the body's other warnings); a dry run lists it as a warning. A throw
without a `MergeFailure` is not a merge cause this check knows, so it becomes "could not check". When
an operation that is not a merge throws, the merge findings of that section are still reported, plus
one warning that the section has a throwing operation; each section is checked on its own. A path that ends on an array is different:
the client sets the keys on the array, where they are lost, and still returns `true`. So the merge is
not in `UnresolvedMerges`. The clone reports it through the `ArrayTargetMerges` sink, and it is an
advisory warning. A merge whose `values` is an array or a string is applied with index keys (`"0"`,
`"1"`, ...); one with a number or a boolean applies nothing. Both are advisory warnings.

**What breaks if you ignore it** — replace the replay with a shortcut, such as checking the merge
against the body alone or against `get-page`'s bundle of the CURRENT page: it reports merges that
the parents satisfy, or misses merges that only an earlier merge in the same body satisfies (array
order matters inside the merge group). Promote the skipped-merge warning to an error, and saving any page that
already carries a stale merge becomes impossible. Demote a throwing merge to a warning, and clio saves
a body that breaks the page at runtime. Drop the sink, and the issue's false
`success:true` comes back with no failing test except the ones that pin this record.
