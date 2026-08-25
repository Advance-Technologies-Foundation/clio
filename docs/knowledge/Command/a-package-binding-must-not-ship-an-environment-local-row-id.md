---
description: package data bindings are keyed by row Id, so PackageDataBinder.BindRow must never be handed a row this run created or a lookup row whose Id the install target customizes
applies-to:
  - clio/Command/PackageDataBinder.cs
  - clio/Command/Branding/SetBackgroundImageCommand.cs
  - clio/Command/FeatureStateService.cs
ticket: ENG-93848
date: 2026-08-19
---

**What is true** — a binding folder written by `PackageDataBinder` stores the bound row's `Id`, and the
install resolves the row on the target by that same `Id`. A row is therefore deliverable only when
every environment shares its `Id`: a product-shipped definition qualifies, anything created locally
does not. `BindRow` is generic and enforces nothing beyond "the row exists here" — the two callers
carry the checks. `SetBackgroundImageCommand` refuses the `SysImageInTag` membership when this
environment's `ShellBackground` tag `Id` differs from the product one, and `FeatureStateService`
exposes `defineIfMissing` off by default precisely so a caller that also binds the state row cannot
have a definition materialized for it first.

**Why it is this way** — the binding format has no name-based resolution: a row is a `Guid` and
nothing else. Creatio generates a fresh `Guid` for any row created on an environment, including one
clio itself creates a second earlier in the same command.

**What breaks if you ignore it** — the install silently inserts a *second* row for the same logical
entity instead of updating the target's own, so the target keeps its original value and gains a
duplicate. For a feature toggle that means the feature the package was supposed to turn off stays on;
for a lookup membership it means a gallery entry pointing at nothing. Nothing fails: the package
installs, exit code 0, and the divergence is visible only on the target's UI.
