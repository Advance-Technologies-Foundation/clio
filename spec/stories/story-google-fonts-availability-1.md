# Story 1: `build-theme` decides the web-font `@import` from a Google Fonts availability probe

**Feature**: google-fonts-availability
**Jira**: ENG-93985
**ADR**: [adr-ENG-93985-google-fonts-availability.md](../adr/adr-ENG-93985-google-fonts-availability.md)
**Test plan**: [tp-google-fonts-availability.md](../test-plans/tp-google-fonts-availability.md)
**Status**: review
**Size**: M
**Depends on**: —
**Paired with**: toolkit PR #68 (`skills/creatio-branding-orchestrator/SKILL.md`), which merges only after the clio release

## As a
no-code creator branding an app through the theming flow

## I want
`build-theme` to decide by itself whether a requested font family can be downloaded from Google Fonts

## So that
a family Google does not publish is never fetched — the css2 endpoint answers 200 with a look-alike
substitute that **shadows the locally installed font** — and I am told what happened instead of having
to confirm, in advance, something I cannot know.

## Context

The first design routed this through a `--local-font-families` flag: the agent asked the user to
confirm a family was locally installed, then passed it back so the import was suppressed. Review of
PR #993 rejected that: clio already probes the catalogue for its warning, so it can make the
suppression decision itself, and after ENG-93989 the agent may build in a single call, leaving no
place for a confirm-then-rebuild round trip.

## Acceptance criteria

- **AC-1** A family the catalogue does not publish (404) gets **no** `@import`; the family is still
  applied through the `--crt-font-family-*` tokens, and a warning states the case-sensitivity/rename
  pitfalls and the local-only rendering consequence.
- **AC-2** A family that cannot be verified (any non-200/non-404 outcome, timeout, transport failure)
  **keeps** its `@import` (fail-open) and produces a "could not verify" warning.
- **AC-3** A published family (200 with a JSON content type) keeps its `@import`. A non-JSON 200
  (consent page, bot check, SPA shell) is Unverified, never InCatalog.
- **AC-4** `--local-font-families` is gone from the CLI, the MCP tool, the docs and the guidance. The
  MCP tool rejects the argument in all three spellings via the `ExtensionData` overflow bag with a
  message naming the replacement.
- **AC-5** The `build-theme` MCP tool is `OpenWorld = true` and its description discloses the probe
  and that the emitted CSS can vary with probe outcomes.
- **AC-6** A font family that breaks the name contract (grammar or the 100-character cap, applied to
  the trimmed and whitespace-collapsed name) fails with `INVALID_FONT_FAMILY` **before** any network
  request, and never reaches the probe URL or the availability cache.
- **AC-7** The probe runs **outside** the MCP shared execution lock, and the verdicts are passed into
  the build so it never re-probes inside the lock. The request's **shape** validation — css-class-name
  and the version/environment pair — runs before the probe, so error precedence is unchanged and those
  rejections cost no outbound request. Colour parsing and the workspace/package existence checks stay
  inside the build, so a request failing on those still probes first; that is accepted, not a gap.
- **AC-8** The theming guidance, the CLI docs/help, the MCP argument descriptions and the toolkit
  skill all state the same family-name contract and the same post-factum warning handling.

## Out of scope

- Spelling correction inside clio (no bundled catalogue, no Title-Case retry heuristic — falsified by
  `PT Sans` 200 / `Pt Sans` 404). The agent corrects from its own knowledge; the user is the final
  resolver via `https://fonts.google.com/?query=<family>`.
- Any configuration off-ramp for the fail-open polarity (explicitly rejected by the feature owner).

## Definition of done

- Unit suite green, including the availability matrix, the probe-once/no-probe canaries and the DI
  lifetime + probe-client-guard tests.
- `[Explicit]` live-endpoint canary re-verified against fonts.google.com.
- ADR recorded with the verified endpoint contract and the accepted risks.
