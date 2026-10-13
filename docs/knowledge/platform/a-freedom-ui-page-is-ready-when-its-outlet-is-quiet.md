---
description: the Freedom UI DOM contract page-smoke relies on - Shell up (crt-root, #loading-animation hidden), the page inside crt-navigation-schema-outlet, crt-mask visibility as the loading signal, element-name on every page control - and why readiness is a quiet period, not an event
applies-to:
  - clio/Command/RuntimeVerification/FreedomUiPageScripts.cs
  - clio/Command/RuntimeVerification/PageSmokeRunner.cs
ticket: ENG-103032
date: 2026-10-13
---

**What is true** — Creatio publishes no "page ready" event a headless client can wait for. page-smoke uses the
contract the Creatio Playwright TestKit (`creatio-playwright-testkit`, `BaseFreedomUI8xHost`,
`CrtSchemaOutlet`, `CrtMask`, `docs/explanations/ui/element-identification.md`) waits on:

- the Shell is up when `crt-root` exists and the splash `#loading-animation` is hidden;
- a Freedom UI page renders inside `crt-navigation-schema-outlet`; on a Freedom page `crt-7x-schema` and
  `.schema-wrap` are present but HIDDEN, and a VISIBLE one means a Classic UI page - presence alone is never a
  signal, only visibility;
- a loading mask is a visible `crt-mask .crt-mask-container` (the TestKit waits 300 ms for one to appear);
- every page control is a `crt-*` component with its view element name in `element-name`, and in
  `data-item-marker`/`data-qa` when the `UseViewElementDataItemMarker` feature is on. Sub-parts (the inner
  `input`, `mat-form-field`) carry no name, so a field is the outermost field component, and a `crt-button`
  inside a field (a combobox's clear button) is not a page button.

page-smoke calls the page ready when all of that holds AND the set of visible named elements in the outlet
has not changed for 1.5 s.

**Why it is this way** — the page loads in stages (Shell, then the schema, then its data sources, then
lazy containers), and each stage can briefly look finished; the TestKit itself chains several waits. A
quiet period over the named elements absorbs those gaps without knowing each component's own loading
state.

**What breaks if you ignore it** — waiting for `document.readyState` or for the first named element reads
the page while its data is still loading (fields empty, buttons missing); treating a hidden
`crt-7x-schema` as "Classic page" refuses every Freedom page; reading `crt-button` across the whole document
lists the Shell's own toolbar buttons and lets a click hit one. Verified against a DOM fixture, the TestKit source
and, on 2026-10-13, a live Studio sandbox (.NET Framework): `Tasks_FormPage` in add mode was ready in about 14 s and
reported its header buttons by `element-name` and 16 fields with labels, required flags and values. A page schema
that does not exist never leaves the splash: the ready wait times out and `consoleErrors` carries
`Script error for "<page>"` plus a 404 for `<page>.js`.
