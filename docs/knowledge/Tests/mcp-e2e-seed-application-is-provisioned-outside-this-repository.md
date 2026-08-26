---
description: the AutoTestClioMcp seed application the Sandbox e2e tier converts and reads is pushed onto the stand by the pipeline as external .gz packages - no repository change can add a seeded page, which is why a missing seed scenario Ignores instead of failing
applies-to:
  - clio.mcp.e2e/MobilePageConversionGuideSandboxE2ETests.cs
  - clio.mcp.e2e/PageGetToolE2ETests.cs
  - clio.mcp.e2e/ApplicationToolE2ETests.cs
  - spec/mcp-e2e-tiering/mcp-e2e-tiering-spec.md
ticket: ENG-95573
date: 2026-08-25
---

**What is true** — every `McpE2E.Sandbox` fixture that resolves `ApplicationCode = "AutoTestClioMcp"`
depends on seed data this repository does not contain. The stand bring-up pushes the `AutoTest` and
`AutoTestClioMcp` packages as prebuilt archives (`clio push-pkg <AutoTest.gz>`,
`clio push-pkg <AutoTestClioMcp.gz>` — see `spec/mcp-e2e-tiering/mcp-e2e-tiering-spec.md`), and those
archives live outside the repository. The only test asset the repo owns is
`clio.mcp.e2e/Assets/ClioMcpE2EFixture.gz`, an empty package with no schemas.

**Why it is this way** — the seed application is shared infrastructure for the whole MCP e2e suite and
is versioned with the stand, not with clio. A pull request therefore cannot add "one more seeded page"
to close a scenario gap; the gap is closed by whoever owns the stand provisioning.

**What breaks if you ignore it** — a review comment asking to "add the seed page as part of this PR"
cannot be satisfied, and converting a missing-seed `Assert.Ignore` into `Assert.Fail` makes the
Sandbox step permanently red on every stand whose seed lacks that shape, for a data gap no committer
can fix. The convention is therefore: a missing PRECONDITION Ignores with an explicit seeding
instruction, a missing structure the converter itself guarantees Fails (see the tabAreaLayers branch
in `MobilePageConversionGuideSandboxE2ETests`), and a runtime error always Fails. When a scenario
must be guaranteed in CI regardless of seed data, pin it off-stand in `clio.tests` as well — the
Sandbox test then covers the real MCP path, not the guarantee.
