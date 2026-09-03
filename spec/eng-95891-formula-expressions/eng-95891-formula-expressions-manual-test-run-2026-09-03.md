# ENG-95891 — manual run, 2026-09-03, at CrtProcessBuilder 1.4.0.45

**Why this run exists.** The package stopped validating formulas itself, so what a refusal SAYS
changed. Every earlier run (2026-09-01 R1/R2 at 1.4.0.18, 2026-09-02 at 1.4.0.37) recorded the
previous contract: refusals in the package's own wording, produced by its own pre-checks. That
evidence no longer describes the shipped archive, which is why the tier was re-run rather than
carried forward.

**What kind of run this is — read this before comparing it to R1/R2.** It is **not** a blind
instrument run. R1 and R2 put an isolated agent with no repository access in front of the prompt and
measured the *guidance library* through it; the agent was the instrument. This run measures one
narrower thing: **what the shipped stack now stores and now says**, executed by the feature session
with full knowledge of the implementation. It settles the stale-evidence problem and nothing else. A
blind instrument run at this generation is still worth having, and is a separate exercise — nothing
here substitutes for it.

## Setup

| | |
|---|---|
| Stand | `krestov-test`, core 10.0.731.0 |
| Package installed | **CrtProcessBuilder 1.4.0.45**, via `install-process-builder`, confirmed with `list-packages` |
| Compiled | yes — the install log carries `Compiling configuration dll` → `Configuration build finished`, then the health probe answered. For a source-only package "installed" and "compiled" are different states, and no database read distinguishes them |
| clio | `feature/ENG-95891-formula-expressions`, built Release/net10.0 |
| Archive | SHA-256 `4571CDAFA5014BBF7324639432475ECA0C72B2704706CF908BF83D7A9FF6B3F4`, from package repo `b8f2d45` |
| Driver | `mcp-server` over stdio JSON-RPC, **one request at a time** — a parallel burst trips IIS rapid-fail and downs the .NET Framework app pool |
| Route | writes through the advertised executor (`clio-run` for create, `clio-run-destructive` for modify), reads via `describe` |
| Scope | what is STORED. Designer rendering and runtime execution are the browser pass, per the prompt |

Process codes carry a `Usr` prefix and an `R45` suffix (`UsrBPTestE95891A1R45` …). The prefix is the
stand's own rule — `The "BPTestE95891A1R45" code of the "…" object must start with the "Usr" prefix` —
and the suffix keeps this run's processes distinct from the three earlier runs' rather than deleting
theirs. This is a deviation from the prompt's literal naming; recorded rather than glossed.

## Verdicts — 6 of 6 at the stored level

| Case | Verdict | What was stored / said, verbatim |
|---|---|---|
| TC-A1 rounded price | **PASS** | `Total` → `source=Script`, `Math.Ceiling([#[Parameter:{45ddc06b-…}]#])` — a reference to `Price`, not a constant |
| TC-A2 largest / average / remainder | **PASS** | three successive writes, each `source=Script`, each over parameter references: `FormulaUtilities.Max(A, B, C)`, `.Avg(A, B, C)`, then `.Mod(A, B)`. The target holds the LAST one — `addMapping` onto a bound target overwrites in place, as the tool contract says |
| TC-A3 parts of a date | **PASS** | `DateTimeUtilities.Day(…)`, `.Month(…)`, `.DayOfWeek(…)`, all three over the same `Due` reference, all three into `Integer` targets, applied in one 3-operation edit |
| TC-B1 fractional into a whole number | **PASS** | refused: `Process validation failed: Amount [Error while executing expression "1.5m": Formula value error: Cannot convert type "Decimal" to "Int32"]`. `Amount` read back with **no source at all** — nothing half-applied. The same request as `1 + 1` then succeeded and stored `1 + 1` |
| TC-B2 depends on something absent | **PASS** | two routes, both refused, `Result` untouched (`source=None`) after both. As a `processParameter` source: `Process parameter 'Total' was not found.` — clio's own pre-check, before anything is written. As an expression over a UId that is not in the process: `Process validation failed: Invalid value for the parameter "Result". It references the process parameter 11111111-1111-1111-1111-111111111111, which is not in this process. Add the parameter first, or correct the reference.` |
| TC-B3 a function that does not exist | **PASS** | `1 + 2` stored. The verbatim `System.Math.Abs(-1)` refused: `Process validation failed: Sum [Error while executing expression "System.Math.Abs(-1)": Formula value error: Parameter "System" not found]` |

## What this run establishes that the earlier ones could not

Three claims in the rewritten descriptions and guidance were live promises with no evidence at the
shipped version. All three now have it:

1. **The type-mismatch refusal quotes the expression as the platform's CONVERTER left it.** Written
   `1.5`, quoted back `"1.5m"`. That is the sentence in both tool descriptions warning a caller not
   to conclude the wrong formula was validated — measured here rather than argued.
2. **The unknown-identifier arm is the platform's wording, not the deleted validator's.**
   `Parameter "System" not found`, not `it references 'System', which does not exist`. This is the
   assertion an earlier E2E had pinned to the package's text; it would have gone red on a stand.
3. **The dangling-reference blob really does reach the caller as a sentence.**
   `{ErrorType:2,ErrorData:{ParameterUId:"…"}}` never appears; `PlatformValidationMessage` turns it
   into a named reference plus a remedy, on the MAPPING path as well as the condition path.

Also confirmed, and worth stating because it is the whole point of the collapse: **a refusal names
the parameter (`Amount`, `Sum`, `Result`) and aborts the entire edit**, and the parameter is left
exactly as it was. Six refusals in this run, six untouched read-backs.

## Observations — not verdict-changing

- **A doubled full stop in one composed message.** The dangling-reference refusal ends
  `…or correct the reference..` The platform's own text is `Internal error: "{blob}".` — with a
  period AFTER the closing quote — and `PlatformValidationMessage`'s regex consumes the blob and the
  quote but not that period, so the rewritten sentence's own period lands next to it. Cosmetic: the
  text the guidance table promises is still a substring, so nothing that matches on it breaks. Left
  alone deliberately — a package edit costs a rebundle, a new archive and a reinstall, which is out
  of proportion to one character. The fix, when the package is next opened for another reason, is to
  let the pattern absorb an optional trailing period.
- **Two contract shapes cost a round trip each, and both refusals were good.** `operations` and
  `descriptor` are JSON **strings**, not JSON values (`argument 'operations' … must be a string`),
  and a mapping's target keys are **flat** on the mapping — `targetProcessParameter`, or
  `elementName` + `elementParameter` — not nested under a `target` object. The refusal names the
  accepted shape in both cases. Both write tools' descriptions read
  `target {elementName, elementParameter} or {targetProcessParameter}`, which names the alternatives
  but can be read as a nested object — the one place in this run where a description invited the
  mistake it then refused. Both now say the keys are flat, and the CREATE path was measured rather
  than assumed to match `modify`: a nested `target` in `mappings[]` is refused with the same
  sentence and nothing is created, while the flat form builds.
- **`direction` takes `In`/`Out`/`Variable`/`Internal`**, and the refusal lists them. Six creates
  failed on `Input`/`Output` and nothing was created — the whole descriptor is refused before any
  schema is written.

## Not covered

- Designer rendering and runtime execution — the browser pass, unchanged from every earlier run.
- Branch conditions and gateways — a different use site; measured directly in
  `eng-95891-formula-expressions-save-gate-probe.md`, which carries the verbatim condition refusals
  at this same archive.
- A blind instrument run of the guidance library at this generation, per the framing above.
