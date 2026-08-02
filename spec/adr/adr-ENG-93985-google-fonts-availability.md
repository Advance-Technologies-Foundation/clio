# ADR: Google Fonts availability probe drives the theme's web-font import

- **Status:** Accepted
- **Date:** 2026-07-31
- **Feature:** `build-theme` font availability handling
- **Jira:** [ENG-93985](https://creatio.atlassian.net/browse/ENG-93985)
- **Related ADR:** [adr-theming.md](adr-theming.md) — the theming engine this decision plugs into.

---

## Context

`build-theme` emits a Google Fonts CSS2 `@import` for any custom `heading-font`/`body-font`.
That is wrong for a family Google does not publish: the css2 endpoint answers **200 with a
substitute font file** for many such names (verified: `css2?family=Verdana` serves a `/l/`
"lookalike" payload), and a downloaded substitute **shadows the locally installed font** the
user actually meant. The first design routed this through a user-facing
`--local-font-families` flag: the agent asked the user to confirm a family was locally
installed and passed it back so the import was suppressed. PR review (clio
[#993](https://github.com/Advance-Technologies-Foundation/clio/pull/993), toolkit
[#68](https://github.com/Creatio-Platform/creatio-ai-app-development-toolkit/pull/68)) found
the flag redundant and fragile: clio already probes the catalogue for the warning, so it can
make the suppression decision itself; and after ENG-93989 the agent may build without a
separate pre-warning step, so the flag's confirm-then-rebuild round trip no longer fits the
flow.

### The endpoint contract this decision stands on (verified 2026-07-30, re-verified 2026-07-31)

`https://fonts.google.com/metadata/fonts/<Family>` (undocumented, internal to fonts.google.com):

| Probe | Answer | Meaning |
| --- | --- | --- |
| `Roboto`, `PT%20Sans`, `Playfair%20Display` | 200, `application/json` | published |
| `Verdana`, `Calibri` (web-safe, not hosted) | 404 | not published |
| `roboto`, `pt sans`, `Pt Sans` | 404 | **case-sensitive, no server-side correction** |
| `Muli` (renamed to `Mulish` in the catalogue) | 404 | renames look identical to typos |

Falsified alternatives:

- **css2 as the probe** — lies in both directions: 200 + substitute for unpublished families
  (`Verdana` → `/l/` lookalike), and it kept serving genuine `/s/` files for the pre-rename
  `Muli` spelling, so neither answer classifies the family.
- **Title-Case retry heuristic** — `PT Sans` is published while `Pt Sans` 404s, so mechanical
  case-normalization cannot recover misspellings; only knowledge of the actual published names
  can (which the agent has and clio does not).
- **Bundling/downloading the full catalogue** (~2.7 MB metadata) for spelling correction —
  rejected by the feature owner as disproportionate weight for an advisory decision.
- **A `CLIO_FONT_IMPORT_POLICY` escape hatch** for firewalled hosts — rejected by the feature
  owner; the fail-open polarity below covers that case without configuration.

## Decision

1. **Remove `local-font-families`** from the CLI command, the MCP tool, and the docs. The MCP
   tool rejects the argument (all three spellings, via the `ExtensionData` overflow bag, same
   pattern as `GuidanceGetTool`) with a message naming the replacement, so agents built
   against the old contract fail loudly for one release instead of silently.
2. **The probe decides the import.** `GoogleFontsCatalog.LookupAsync` probes the metadata
   endpoint once per ordinal-distinct requested family (concurrently, 3 s budget per probe,
   `AllowAutoRedirect=false`):
   - 200 **with a JSON content type** → `InCatalog` → the `@import` is emitted. The JSON gate
     keeps a consent page, bot check, or SPA shell from reading as "published".
   - 404 → `NotInCatalog` → the `@import` for that family is **suppressed** (the family is
     still applied through the `--crt-font-family-*` tokens) and a warning explains the
     case-sensitivity/rename pitfalls and the local-only rendering consequence.
   - anything else (non-JSON 200, 5xx, timeout, transport failure) → `Unverified` →
     **fail-open**: the `@import` is kept so a Google font keeps working on offline or
     firewalled hosts, and a warning says the family could not be verified and to restyle
     once connectivity is back if it is actually a local font. A probe task that faults
     unexpectedly degrades that family alone to `Unverified`; the probe can never fail the
     build.
3. **Ordinal case-sensitivity end to end.** The probe cache, the suppression list, and the
   builder's family matching all compare ordinally, mirroring the endpoint: a case-folded
   match would fabricate an answer the catalogue never gave (`Roboto` published, `roboto`
   not).
4. **Definitive-only memoization.** `InCatalog`/`NotInCatalog` verdicts are cached per process
   for 5 minutes; `Unverified` is never cached, so a transient outage cannot pin a stale
   verdict inside a long-lived MCP server. The memo lives in a **singleton**
   `IGoogleFontsAvailabilityCache` shared by the transient typed probe clients (the
   `ICurrentUserCultureCache` shape), because a per-instance memo on a transient service would
   never produce a hit across calls.
5. **Spelling correction lives in the agent, not clio.** The toolkit skill and the theming
   guidance tell the agent to probe the exact spelling first, retry once with the published
   name it knows from its own knowledge (casing, renames like `Muli` → `Mulish`), and
   otherwise hand the user `https://fonts.google.com/?query=<family>` as the resolver. The
   agent still asks for explicit confirmation before building with a family that is not on
   Google Fonts; clio's warning then arrives as the expected echo of that confirmation.
6. **The MCP tool is `OpenWorld = true`** and its description discloses the network probe and
   that the emitted CSS can vary with probe outcomes (always disclosed via warnings).

## Consequences

- One build instead of build → warn → confirm → rebuild: the not-published decision no longer
  needs a second `build-theme` call, which is the ENG-93985 goal.
- A firewalled host silently keeps imports for unpublished families (`Unverified` fail-open).
  Accepted: the warning discloses it, and the alternative polarity would break every Google
  font on such hosts. No configuration off-ramp by explicit decision.
- The theme CSS is no longer a pure function of the inputs — probe outcomes can change it.
  The tool description discloses this next to its idempotency claim, and the warnings always
  say which way a family went.
- A middlebox that answers **404** for blocked hosts (instead of the usual 403/5xx/redirect,
  which all map to `Unverified`) would read as `NotInCatalog` and suppress imports for genuinely
  published families for one TTL window. Accepted risk: the 404 arm has no authenticity gate
  (unlike the JSON-gated 200 arm), the warning still discloses the suppression and names the
  probe, and a sentinel cross-check was judged disproportionate for an advisory probe.
- The endpoint is an undocumented contract. The canary test
  (`GoogleFontsCatalogEndpointCanaryTests`, `[Explicit]`/`[Category("Integration")]`)
  re-verifies published/unpublished/case-sensitivity against the live endpoint on demand; it
  passed on 2026-07-31.
- `FontsInput.LocallyInstalledFamilies` is renamed to `SuppressedImportFamilies` and its
  matching is now ordinal (previously case-insensitive): the list now records what the probe
  classified, not what a user typed, so exact spellings are correct there.
