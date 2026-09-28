# SPEC: Page localization (ENG-90576)

**Jira**: [ENG-90576 — Add ability to localize a page](https://creatio.atlassian.net/browse/ENG-90576)
**ADR**: [adr-page-localization.md](../adr/adr-page-localization.md)
**Stories**: [1](../stories/story-page-localization-1.md) · [2](../stories/story-page-localization-2.md) ·
[3](../stories/story-page-localization-3.md)
**Test plan**: [tp-page-localization.md](../test-plans/tp-page-localization.md)
**Date**: 2026-09-26

## Problem

A coding agent that built a Freedom UI page in the default culture cannot add translations of that page's captions
in other cultures through clio. `update-page` / `sync-pages` write `en-US` only and require a body; the shipped
guidance sends the agent to "the native localization workflow". Two silent failures make ad-hoc workarounds
unsafe: the platform stores nothing and answers `success:true` for a culture that is not in `SysCulture`, and a
page save that omits a culture from a page-owned key deletes that culture's value (ADR F3, F5).

## User

A coding agent (through MCP) or a developer (through the CLI) localizing a Creatio application it has built.

## Capabilities

- CAP-01 — Translate the captions of one page into one culture per call: own and inherited `localizableStrings`
  keys (field/button captions, tab / expansion-panel / field-group titles, custom list-column titles,
  tooltips, placeholders, messages) and the page title (schema `caption`).
- CAP-02 — Never change the default culture or any culture other than the one requested; re-running with the same
  values changes nothing; adding a second culture keeps the first.
- CAP-03 — Report coverage for a culture without saving: keys still without a value, keys whose value equals the
  `en-US` text.
- CAP-04 — Refuse a culture the environment does not have, naming the Languages section; accept an inactive
  culture with a warning that tells how to activate it.
- CAP-05 — Touch only the page schema asked for; never create a replacing schema to hold translations.
- CAP-06 — Entity captions (object title, column titles) keep adding cultures without removing others, and get the
  same culture check as pages.

## Constraints

- One new CLI verb and one long-tail MCP tool, both `localize-page` (ADR D1). No change to `update-page`,
  `sync-pages`, `get-page` behaviour (ADR D10).
- Culture list comes from `SysCulture` via DataService (ADR D4); `clio sql` is not used.
- Every write is verified by reading it back (ADR D5).
- Follow AGENTS.md: kebab-case options, DI for behaviour classes, `KnownRoute` for fixed endpoints, `///` docs,
  AAA tests with `because` and `[Description]`, docs + MCP review, E2E coverage for the new tool.

## Non-goals

- Section titles in the application navigation (ADR OQ-1).
- Machine translation inside clio; the agent supplies the translated text.
- Backend `LocalizableStrings` / culture XML files (covered by
  [adr-localization-ready-packages.md](../adr/adr-localization-ready-packages.md)).
- Activating a culture or changing a user's language.
- Changing how column caption maps are merged (ADR D9: the server already preserves cultures).

## Success criteria

1. On a live stand, a page built in `en-US` gets `es-ES` values for every custom key and its title through
   `localize-page`; `en-US` values are byte-identical before and after; a repeat call reports everything
   `unchanged` and sends no save; a later `de-DE` call leaves `es-ES` intact (TC-E2E-01..03).
2. `fi-FI` (not in `SysCulture`) fails before any write with the Languages-section message; `es-ES` while inactive
   is written with the activation warning (TC-E2E-04, TC-U-*).
3. After activating `es-ES` and switching the user language, the page renders the Spanish captions and a key left
   untranslated shows the English fallback (TC-M-01).
4. Entity caption maps with a culture absent from `SysCulture` fail with the same message instead of being dropped
   (story 3).
5. The `clio-knowledge` guidance pull request listed in the ADR lands before or with the release.
