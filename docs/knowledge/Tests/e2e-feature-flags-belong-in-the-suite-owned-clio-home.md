---
description: the e2e suite's feature surface is decided in the suite-owned shared home and nowhere else - it is CLEARED there, so a gated fixture gates itself and skips; a fixture that builds its own home from the machine's appsettings silently loses the corrected sandbox URL, the sanitised knowledge root and the credential hardening
applies-to:
  - clio.mcp.e2e/McpSharedHomeSetUpFixture.cs
  - clio.mcp.e2e/MobilePageConversionGuideSandboxE2ETests.cs
  - clio.mcp.e2e/Support/Configuration/TemporaryClioSettingsOverride.cs
ticket: gh-1382, ENG-94638
date: 2026-09-04
---

**What is true** — a tool carrying `[FeatureToggle(...)]` is not registered on the MCP surface while its
flag is off, and flags live in `appsettings.json` in the clio home. An e2e fixture that lets the child
server read the machine's home therefore inherits whatever the last person or job to run clio there left
behind. The place to decide it is `McpSharedHomeSetUpFixture`, which already builds the one home the whole
suite runs against; `TestConfiguration.Load()` hands that home to every fixture through
`ProcessEnvironmentVariables["CLIO_HOME"]`. A fixture decides it there, not in its own
`ConfigureMcpServerSettings`.

Since ENG-94638 that fixture CLEARS the inherited map rather than adding to it, and it folds every casing
of the `features` key before writing the canonical one - clio binds the map case-insensitively into a
dictionary its constructor pre-initialises, so assigning the lowercase key alone would leave a `Features`
sibling's flags in force. No fixture sets a flag today: the tools clio still gates gate THEMSELVES and skip
(`WatchCompilationE2EGate`, `McpWorkerModeE2ETests`), which is what makes an empty map the deterministic
input they are written against. A fixture that needs a gated tool advertised must add it here - and must
fold the casings - not build its own home.

**Why it is this way** — the suite-owned copy is not merely "a home": it is repaired. `reg-web-app` in
`ClioCliCommandRunner.ReRegisterSandboxEnvironmentAsync` corrects the sandbox environment's stale URL
*into that home only*; the `knowledge` node is rewritten to a root inside it so the child cannot write the
developer's live knowledge store, and the curated source it disables is what keeps a GitHub-release
bootstrap from becoming a hidden startup prerequisite; the directory and the credential-bearing settings
file are chmod 700/600 and the file is deleted on teardown, throwing if it survives. A per-fixture home
built from the machine's settings has none of that. Note also that
`TemporaryClioSettingsOverride.GetClioAppSettingsPath` resolves the path by spawning
`clio info --settings-file`, so it returns the *machine's* home unless it is given
`settings.ProcessEnvironmentVariables` — the runner process never sets `CLIO_HOME` itself.

**What breaks if you ignore it** — the failure is silent and wears the mask of a seeding problem. The
fixture's own MCP calls go to a home holding the stale `dev` URL while `ping-app`, which runs through
`ClioCliCommandRunner`, uses the corrected one: the environment reports reachable, `list-apps` returns
nothing, and every test skips with "seeded application was not found — install the seed package". That is
issue #1382's own symptom, re-created under a wrong diagnosis. The knowledge root and the credential copy
fail quietly in the other direction: the "isolated" home writes into the real store, and a run killed
before teardown leaves the machine's environment logins and passwords in a world-readable temp directory.
