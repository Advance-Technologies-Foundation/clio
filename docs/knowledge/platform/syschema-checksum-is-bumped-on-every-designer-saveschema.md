---
description: Creatio bumps SysSchema.Checksum on every ClientUnitSchemaDesignerService SaveSchema, which is the only reason the update-page/sync-pages external-modification conflict check works
applies-to:
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/PageBaselineGuard.cs
  - clio.mcp.e2e/PageUpdateToolE2ETests.cs
ticket: ENG-91317
date: 2026-08-19
---

**What is true** — the Creatio server rewrites `SysSchema.Checksum` on every save that goes through
`ClientUnitSchemaDesignerService/SaveSchema`, including a save made by a human in the Freedom UI
designer. clio's whole conflict-detection design rests on that: `get-page` records the checksum as a
baseline, and `PageUpdateOptions.TryCheckForExternalModification` treats a differing checksum as
proof that somebody else saved the schema in between.

**Why it is this way** — it is platform behaviour, not something clio controls. Nothing in this
repository can assert it; the column could in principle be maintained only for package export, in
which case a designer edit would leave it untouched and every conflict check would silently pass.

**What breaks if you ignore it** — the guard degrades to a no-op that still reports success: an agent
overwrites a designer edit exactly as before ENG-91317, and the response says `conflict: false`.
Because the failure is a false negative, no test or log points at it. The in-tree check that would
catch a platform regression is the live conflict scenario in `clio.mcp.e2e/PageUpdateToolE2ETests.cs`
(save out of band, expect a conflict, then `get-page` and retry). That suite needs a real stand and
does not run in CI, so treat this fact as verified by a manual e2e run, not by the build.
