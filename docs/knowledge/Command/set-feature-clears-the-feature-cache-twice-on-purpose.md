---
description: FeatureCommand.Execute calls ClearCache unconditionally even though FeatureStateService.SetFeatureState already clears it after a write - the duplicate ClearFeaturesCacheForAllUsers GET is deliberate, do not scope it to the write branches
applies-to:
  - clio/Command/FeatureCommand.cs
  - clio/Command/FeatureStateService.cs
date: 2026-08-19
---

**What is true** — `FeatureCommand.Execute` calls `ClearCache(options.Code)` on both non-web-service
paths, after the write and regardless of whether the write applied (the deprecated
`--use-feature-web-service` branch returns before it). `FeatureStateService.SetFeatureState` also
calls its own private `ClearCache` — but only inside its two mutating branches (a state row was
created, or an existing row's `FeatureState` differed). So a successful per-role `set-feature`
issues the `/rest/FeatureService/ClearFeaturesCacheForAllUsers/<base64>` GET twice. That is the
accepted shape, not an oversight.

**Why it is this way** — when the row already holds the requested state, `SetFeatureState` takes
neither mutating branch and clears nothing. Scoping the command-level call to the write branches
(the obvious de-duplication) was tried and reverted: it dropped the invalidation exactly in the
no-op case, which is the case where a stale server-side feature cache is most likely to be the
reason the caller ran the command at all. The clear is an idempotent GET, so paying for it twice is
the cheaper side of the trade.

**What breaks if you ignore it** — remove or condition the `ClearCache` call in `Execute` and
`clio set-feature` returns exit 0 having changed nothing observable for a feature whose stored state
already matched: the platform keeps serving the cached value, and no message says so. The failure is
silent and looks like the platform ignoring clio.
