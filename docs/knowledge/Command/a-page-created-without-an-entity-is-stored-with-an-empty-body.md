---
description: a page create-page makes without an entity is stored with body "" while get-page shows a synthesized empty scaffold, so update-page --mode append must merge into that scaffold and carry the fragment's own SCHEMA_DEPS / SCHEMA_ARGS, never save the fragment as the whole body
applies-to:
  - clio/Command/PageCreateOptions.cs
  - clio/Command/PageUpdateOptions.cs
ticket: GH-1839
date: 2026-10-19
---

**What is true** — a page `create-page` makes without `--entity-schema-name` is saved with `body: ""`.
`get-page` shows a synthesized empty scaffold for it, so the empty stored body is invisible from the
read side. `update-page --mode append` on such a page builds the same empty scaffold, merges the
fragment into it, and takes the fragment's own `SCHEMA_DEPS` / `SCHEMA_ARGS` into the scaffold header.

**Why it is this way** — the platform accepts an empty body on save; the scaffold exists only in clio,
so the append has nothing stored to merge into.

**What breaks if you ignore it** — an append that treats the empty stored body as "nothing to merge"
and saves the fragment as the whole body replaces the page with the fragment alone: a
`modelConfigDiff`-only fragment becomes a one-line body with no `define(...)` wrapper while the call
reports `success`. Dropping the fragment's `SCHEMA_DEPS` / `SCHEMA_ARGS` leaves its handlers calling
an `sdk` parameter the saved body does not declare.
