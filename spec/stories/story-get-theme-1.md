# Story: get-theme — read an existing theme's content (ENG-93991)

**Feature:** get-theme (theming Area E)
**Jira:** [ENG-93991](https://creatio.atlassian.net/browse/ENG-93991)
**SPEC:** covered by the Area E amendment in `spec/adr/adr-theming.md` (no separate PRD — a single story
extending the established theming family).

## User story

As a branding agent (or a clio user), I can read the content (`theme.css`) and metadata of an existing
custom theme by its id, so that I can modify a single value in a theme that already exists and apply the
change through `update-theme` without blindly overwriting or regenerating the theme (which loses manual
tweaks).

## Acceptance criteria

- [x] A user/agent can retrieve the content of an existing theme by id via a CLI command (`get-theme`)
      and an MCP tool (`get-theme`).
- [x] The returned envelope carries `caption`, `cssClassName`, and `cssContent` usable verbatim as
      `update-theme` inputs (read → edit → update-theme round-trip works).
- [x] A clear, actionable result is returned when the theme id doesn't exist (names the id, points at
      `list-themes`, and names the possibly-missing `CanCustomizeBranding` license when the catalog is
      empty) or has no content (success with empty `cssContent`).
- [x] `--output-file` writes the CSS to a confined path (workspace / OS temp, no overwrite) and omits the
      content from the envelope, feeding `update-theme --css-content-file` directly.
- [ ] The modify-existing-theme flow works end-to-end (live sandbox E2E: create → read → edit → update →
      re-read → delete → not-found). The mechanism this AC depends on — the catalog (and therefore
      `cssFilePath`) is re-resolved on every `get-theme` call rather than cached, so a read right after
      `update-theme` reflects the new cache-busting hash and content — is pinned at the unit tier
      (`GetThemeCommandTests.TryGetTheme_ShouldReReadCatalogAndCss_WhenCalledAgainAfterCssFilePathHashChanges`).
      `ThemingSandboxE2ETests` exercises the same round trip against a live stand but is `McpE2E.Sandbox`
      tier (advisory, non-blocking, `Assert.Ignore`s without a configured stand) and has not been observed
      green on this branch; leave this box open until it has.

## Definition of Done

- [x] `GetThemeCommand` + `GetThemeOptions` + `GetThemeResponse` (`clio/Command/Theming/GetThemeCommand.cs`)
- [x] `GetThemeTool` (`clio/Command/McpServer/Tools/GetThemeTool.cs`), long-tail, flags
      `ReadOnly=false / Destructive=false / Idempotent=true / OpenWorld=false`
- [x] DI + dispatch wiring (`BindingsModule.cs`, `Program.cs`, `CommandHelpCatalog.cs`)
- [x] Docs: `Commands.md`, `help/en/get-theme.txt` (+ `help.txt` index), `docs/commands/get-theme.md`,
      `Wiki/WikiAnchors.txt`
- [x] Capability map: `docs/McpCapabilityMap.md`, and the tool description points at `get-guidance theming`
- [ ] Guidance article: the "Read a theme's content" section and the read-before-update step in the no-code
      flow are owed to the `clio-knowledge` repository (guidance left this repository while this story was
      open, so it is a pull request there with a `libraryVersion` + `sequence` bump, not a change here)
- [x] Unit tests: `GetThemeCommandTests` (Module=Command), `GetThemeToolTests` (Module=McpServer)
- [x] MCP E2E: `GetThemeToolE2ETests` (hermetic discovery + arg validation),
      `GetThemeHappyPathE2ETests` (hermetic happy path — stubbed catalog + theme.css, runs in CI),
      `ThemingVersionFloorContractE2ETests` gated list,
      `ThemingSandboxE2ETests` read → edit → update round-trip (live tier)
- [x] Durable-invocation gate reviewed: under the PR #984 flipped gate (silently-executable =
      `ReadOnly=true` baseline) the `ReadOnly=false` `get-theme` needs no baseline entry — it is
      confirmation-gated on the durable path like `get-schema`
- [x] ADR amendment (`spec/adr/adr-theming.md` Area E) recording the spike evidence and decisions
