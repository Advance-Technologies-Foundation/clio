# ADR — theme-color-advisor (theming-agent interactive layer)

**Status:** Accepted (design), not yet implemented
**Date:** 2026-07-01
**Feature:** theming-agent (interactive palette conversation over the existing native theme engine)
**Supersedes/extends:** builds on `adr-theming-native-build.md` (the deterministic engine + `build-theme` + theme CRUD are shipped). This ADR adds the interactive orchestration layer.

## Context

The `theme-agent` package (analysed in `spec/theming-agent/theming-agent-gap-analysis.md`) describes an
ideal, conversational theming agent: an LLM orchestrator that, step by step, calls a deterministic color
engine and branches on its results (contrast triage, adapted primary, secondary preview, accent A/B/C,
preview, manual validation, warnings). clio already ported that engine **bit-exact** into `Clio.Theming`,
but exposes it only as a single one-shot `build-theme`. The engine primitives (`ContrastRatio`,
`DistanceOklab`, `AccentCandidates`, `SuggestAdaptedPrimary500`, `DeriveSecondary`, `GenerateScale`,
`ColorNormalizer`) are internal and not reachable by an orchestrating agent, so the interactive flow is
undeliverable today.

Coverage is split across exactly two places: **clio** (this ADR) and **one adac skill**
`creatio-theme-orchestrator` (already rewritten to consume this design; it owns the conversation, intake,
and policy). The host client owns web fetch, vision, swatch rendering, and the Google-font existence check.

## Decision

Add **one** new read-only MCP tool, **`theme-color-advisor`**, that projects the internal `Clio.Theming`
engine into a structured, action-dispatched **decision packet** the skill re-calls whenever a color input
changes. The full, code-grounded contract is
**`spec/theming-agent/theme-color-advisor-contract.md`** (produced + adversarially verified against the
real engine); it is the authoritative spec for this feature and this ADR adopts it.

Key decisions (confirmed with the product owner 2026-07-01):

1. **One fat, verdict-returning tool**, not granular per-function tools (6–8 round-trips per theme is
   token-disqualifying) and not an all-at-once batch planner (it inverts the "collect primary first" intake).
2. **Verdicts, not raw numbers.** The tool returns `pass`/`warn`/`strong`, boolean gates, precomputed
   `similarityBand` / `isBest` / counts, and tool-owned warning codes + threshold-free messages. Every
   threshold (3:1 contrast, 0.10/0.07 OKLab similarity) lives in clio; the LLM never compares to a threshold.
3. **Stateless + offline + read-only.** `ReadOnly=true/Destructive=false/Idempotent=true/OpenWorld=false`,
   `[FeatureToggle("theming")]`, flat `ComponentInfoTool`-style, action-dispatched by an `operation` arg.
   No `environmentName` (would force a network call, breaking `OpenWorld=false`); `preview` takes an offline
   `version` only. The skill holds all conversation state and re-passes the full input set on each call.
4. **No metadata persistence in v1** (`sourcePrimaryHex`/choices/`acceptedWarnings`/`algorithmVersion`) — the
   adac server flow (`create-theme-by-environment`) sends no `theme.json`, so there is no vehicle; skill-side
   state only.
5. **Theme name:** 50 chars is a soft recommendation in guidance/skill; the hard max (250) is already enforced
   in `create-theme`/`update-theme`. No new length rejection.
6. **Apply/default:** single final confirmation = build + create; making the theme the default is a separate,
   explicit question (it changes the look for everyone) unless the user asked up front. Owned by the skill.
7. **Non-text scope:** advisor verdicts are non-text WCAG 3:1 ("usable as a brand/UI color on white"); the
   stricter 4.5:1 text contrast stays a build-time concern in `TextTokenResolver`.

## Consequences

- Engine changes required in `clio/Theming` (all internal, same-assembly — no `InternalsVisibleTo`, no
  visibility widening): `ColorNormalizer.TryNormalize`; a 3-state adapted-primary outcome carrying
  original contrast; a `MeetsMinContrastOnWhite` predicate; accent similarity thresholds + `ClassifySimilarityBand`;
  a `SelectBestValidAccent` that filters on both gates (the existing `ChooseBestAccent` filters contrast-only
  and has a degenerate fallback — must not be reused); `IThemeTemplateProvider.TryGetPaletteDefault`
  (offline success/error -500 defaults); a public offline `ResolveCompatibleVersion(string)`. Verdict/
  warning-code types are tool-owned, not engine.
- New MCP surface: `theme-color-advisor` tool + `ThemeColorAdvisorResult` POCO (mirrors `BuildThemeResult`
  null-omission discipline). Unit tests + mandatory `clio.mcp.e2e`. `ThemingGuidanceResource` rewritten to
  drive the Stage 1–4 flow naming the advisor for every decision; routing row updated.
- Feature stays dark under `[FeatureToggle("theming")]` until the surface is complete.

## Build order

engine changes (`Clio.Theming`) + tests → `theme-color-advisor` tool + DTOs + unit tests + e2e → rewrite
`ThemingGuidanceResource`. The adac skill (`creatio-theme-orchestrator`) is already updated to this contract.

## Open follow-ups (deferred, from the contract)

Threshold calibration on real brands; the specific Academy article URL; metadata persistence if a future
flow needs re-editing from source colors.
