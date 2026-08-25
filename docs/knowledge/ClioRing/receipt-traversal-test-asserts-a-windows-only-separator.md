---
description: ClioRing.Tests ResolveContainedReceiptPath_..._WhenRunKeyIsHostile fails on macOS and Linux because its "x\..\..\evil" key is a single legal POSIX filename, so the Release test gate is never fully green off Windows
applies-to:
  - clio-ring/ClioRing.Tests/DeploymentReceiptTests.cs
date: 2026-08-19
---

**What is true** — `ResolveContainedReceiptPath_ShouldRejectEscapeAndNeverResolveOutsideFolder_WhenRunKeyIsHostile`
feeds `@"x\..\..\evil"` among its escaping run keys and asserts the resolver returns `null`. On macOS and
Linux `\` is an ordinary filename character, so that key contains no separator, resolves to
`<logs>/deploy-x\..\..\evil.ndjson` directly inside the folder, and is legitimately accepted. Verified by
running the fixture on macOS: 1 failed / 1 passed, with the resolver returning the contained path.

**Why it is this way** — the key list was written against Windows path semantics; the sibling
forward-slash cases (`"x/../../evil"`, `Path.Combine(...)`) already cover the platform-independent
behaviour, and the backslash case adds nothing on POSIX.

**What breaks if you ignore it** — the mandatory Ring gate
`dotnet test clio-ring/ClioRing.Tests -c Release` reports one red on every non-Windows run, which trains
readers to wave through a security-relevant fixture's failure. It also violates the repository test-style
rule that every test must be executable on macOS, Linux and Windows. The correct response is to fix the
test — guard the backslash key behind a Windows check, or drop it, since it proves nothing the
forward-slash keys do not — not to carry it as an accepted baseline.
