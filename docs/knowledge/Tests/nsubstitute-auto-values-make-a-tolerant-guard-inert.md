---
description: NSubstitute returns "" (not null) for an unstubbed string member, so a guard that degrades tolerantly on missing input is silently inert across a whole fixture - stub the dependency in Setup, and mutate nullable-string stubs explicitly with .Returns((string?)null)
applies-to:
  - clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs
date: 2026-08-19
---

**What is true** — an unstubbed `string`-returning substitute member answers `string.Empty`, not
`null`. A guard written to tolerate missing input therefore never sees "missing" in a fixture that
never stubs the dependency: it takes its tolerant branch on every case, including the regression test
written for the workflow it exists to protect, and every test passes. The cross-package column-name
guard in `RemoteEntitySchemaColumnManager` was inert this way for its entire fixture because
`_runtimeEntitySchemaReader.GetByName` was unstubbed and every add exited through the
unknown-runtime branch; the fix was arranging the reader in `SetupLoadedSchema` so the fixture is
production-shaped.

**Why it is this way** — auto-values are an NSubstitute convenience: a substitute must return
*something* for an unconfigured member, and for `string` that something is the empty string.

**What breaks if you ignore it** — two concrete failure modes. Adding a `string?` probe to a service
does **not** automatically expose the tests that never stub it; they keep passing on the
empty-string default, so a deletion-based mutation check gives a false all-clear. And a guard whose
input is never arranged is guaranteed green while guarding nothing — which is how a Blocker survived
a three-lens review of the code it was in. Stub the dependency in `Setup`, and to prove a null path
write `.Returns((string?)null)` explicitly rather than removing the stub.
