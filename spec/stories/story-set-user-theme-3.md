# Story 3: MCP end-to-end coverage for `set-user-theme`

**Feature**: set-user-theme
**Jira**: ENG-93302
**FR coverage**: FR-1..FR-5 (E2E verification)
**SPEC**: [spec-set-user-theme.md](../prd/spec-set-user-theme.md)
**Status**: ready-for-dev
**Size**: S
**Depends on**: story-set-user-theme-2

## As a
clio maintainer

## I want
`clio.mcp.e2e` coverage exercising the full apply/reset cycle against a live environment

## So that
the MCP policy's mandatory E2E requirement is met and the contract can't silently regress

## Design
- New scenario(s) in `clio.mcp.e2e` following the existing theme E2E patterns:
  1. `create-theme` (unique caption) → `set-user-theme` by caption →
     read back `SysUserProfile.Theme` equals the theme's cssClassName.
  2. `set-user-theme --reset` → read-back shows empty value.
  3. Unknown theme → error envelope lists available themes; profile unchanged.
  4. Cleanup: `delete-theme` + reset in teardown so the shared E2E user's profile
     is left untouched.
- Environment prerequisites documented in the test: Creatio 10.0+, `ChangeTheme`
  feature enabled, `CanCustomizeBranding` license, E2E user in a role granted
  `CanChangeOwnTheme` (default: All employees). Skip with a clear message when
  the version gate rejects the environment.
- Long-running remote calls: follow the heartbeat pattern from
  `ApplicationToolE2ETests` if any step can exceed TeamCity's inactivity timeout.

## Acceptance Criteria
- [ ] AC-01 — Apply, reset, and unknown-theme scenarios pass against the E2E environment.
- [ ] AC-02 — Teardown restores the E2E user's profile theme and deletes the created theme.
- [ ] AC-03 — Tests follow the repo test style policy (AAA, `because`, `[Description]`, cross-OS).

## Tests
This story *is* the tests: `clio.mcp.e2e` run on both target frameworks.
