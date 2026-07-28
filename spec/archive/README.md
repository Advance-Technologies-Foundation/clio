# Archived BMAD features

Features whose Jira ticket is Closed/Done and whose implementing PR(s) are merged. Kept for
historical reference; `spec/sprint-status.yaml` still tracks their story rows (status: done)
with paths pointing here.

| Feature | Jira | PR | Archived |
|---|---|---|---|
| browser-session-handoff | [ENG-91234](https://creatio.atlassian.net/browse/ENG-91234) | [#691](https://github.com/Advance-Technologies-Foundation/clio/pull/691) | 2026-07-12 |
| detect-external-schema-changes | [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317) | [#699](https://github.com/Advance-Technologies-Foundation/clio/pull/699), [#723](https://github.com/Advance-Technologies-Foundation/clio/pull/723) | 2026-07-12 |
| read-column-default-values | [ENG-91318](https://creatio.atlassian.net/browse/ENG-91318) | [#700](https://github.com/Advance-Technologies-Foundation/clio/pull/700) | 2026-07-12 |
| user-profile-language-detection | [ENG-91044](https://creatio.atlassian.net/browse/ENG-91044) | [#701](https://github.com/Advance-Technologies-Foundation/clio/pull/701) | 2026-07-12 |
| update-page-designer-presence-push | [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317) | [#699](https://github.com/Advance-Technologies-Foundation/clio/pull/699) | 2026-07-12 |
| mcp-progress-heartbeat | [ENG-91274](https://creatio.atlassian.net/browse/ENG-91274) | [#688](https://github.com/Advance-Technologies-Foundation/clio/pull/688) | 2026-07-12 |
| mcp-e2e-noenvironment-parallelization | [ENG-92558](https://creatio.atlassian.net/browse/ENG-92558) | [#804](https://github.com/Advance-Technologies-Foundation/clio/pull/804) | 2026-07-12 |
| mcp-lazy-schema | [ENG-90312](https://creatio.atlassian.net/browse/ENG-90312), [ENG-92761](https://creatio.atlassian.net/browse/ENG-92761) | [#743](https://github.com/Advance-Technologies-Foundation/clio/pull/743), [#751](https://github.com/Advance-Technologies-Foundation/clio/pull/751), [#821](https://github.com/Advance-Technologies-Foundation/clio/pull/821) | 2026-07-12 |

Not archived: `theming-native-build` / `theming-agent` (shared `spec/adr/adr-theming.md` still has
an open `deferred` story, story-theming-native-build-7); `product-telemetry`, `create-app-section-response-deadline`,
`ENG-89796-page-body-syntax-validator`, `ENG-90883-backend-process-designer` (ADR-only, no PRD/stories/test-plan
were ever generated for them, so there's nothing to move alongside the ADR — left in `spec/adr/`).
