# ADR-ENG-89796 — Page body JavaScript syntax validator: choice of Acornima

- **Status:** Accepted
- **Date:** 2026-06-10
- **Jira:** ENG-89796

## Context

`update-page` and `sync-pages` accept the Freedom UI page body as an opaque string
and persist it to Creatio without parsing it as JavaScript. The only pre-write
check is `PageBodySamplingService`, an LLM-based semantic review of
cross-section references. It does not check syntax, and there is no other
parser stage on the write path.

A real incident produced a body containing `await request.$context.X = Y`
(an `await` expression cannot be an assignment target → `SyntaxError`). `update-page`
returned `success: true, bodyLength: 4626` because the body was never parsed; the
page failed to load only in the browser, in production. The same gap covers every
syntax-level failure (unbalanced braces, broken template literals, mismatched
`SCHEMA_*` marker pairs, etc.) — all deterministic and detectable by a parser in
milliseconds.

## Decision

Use **[Acornima](https://www.nuget.org/packages/Acornima) v1.6.2** as the JavaScript
parser. Introduce `PageBodySyntaxValidator` (MCP tools layer) which wraps
`Acornima.Parser.ParseScript(...)`, catches `SyntaxErrorException`, and returns
`{ line, column, message }` on failure. Invoke it from `PageUpdateTool` and
`PageSyncTool` **before** `PageBodySamplingService` and **before** the
write call to Creatio.

## Why Acornima

- **BSD-3-Clause license** — permissive, OSI-approved, commercial-friendly. Differs
  from MIT only in the additional no-endorsement clause (cannot use contributor
  names to promote derived products without permission); attribution + license-text
  preservation requirements are equivalent. Compatible with the existing clio
  license stack and standard for permissive open-source .NET libraries
  (transitive deps `System.Memory`, `System.Runtime.CompilerServices.Unsafe`
  are MIT — Microsoft BCL).
- **Pure C# / managed** — no native binaries; clean global-dotnet-tool deploy.
- **Maintained** — same author as the predecessor `Esprima.NET` (which is
  archived; its README explicitly states *"This project has been superseded by
  Acornima"*). Active releases through 2024–2026, current 1.6.2.
- **Same API surface as `Esprima.NET`** the task description referred to:
  `Parser.ParseScript` / `ParseModule`, raises `SyntaxErrorException` with
  `LineNumber` / `Column` / `Description`. The migration from Esprima.NET to
  Acornima is a one-line `Directory.Packages.props` change plus the namespace
  rename — no architectural impact.
- **Modern spec compliance** — ES2022+ (top-level `await`, optional chaining,
  private fields, nullish coalescing). Required because Creatio page bodies
  routinely use modern syntax.
- **Used internally by Jint** — the most-deployed managed JS engine in .NET — so
  the parser is battle-tested at scale.

### Empirical Day-0 probe (against the canonical `create-page` fixture)

| Case                                       | Outcome  | Time   | line/col |
|--------------------------------------------|----------|--------|----------|
| Valid `create-page` output (~5 KB)         | ✅ parse  | 33 ms  | —        |
| Incident body (`await X = Y`)              | ❌ reject | 9.7 ms | 6 / 16   |
| Empty body                                 | ✅ parse  | <1 ms  | —        |
| Body with UTF-8 BOM prefix                 | ✅ parse  | <1 ms  | —        |
| Body with mixed line endings (CRLF + LF)   | ✅ parse  | 1 ms   | —        |
| Unbalanced `{`                             | ❌ reject | <1 ms  | 1 / 25   |
| Unterminated template literal              | ❌ reject | <1 ms  | 1 / 15   |
| 50 KB comment-only body                    | ✅ parse  | <1 ms  | —        |

Performance budget `<50 ms / 50 KB` from the AC is comfortably met (multiple
orders of magnitude headroom).

## Rejected alternatives

| Library / Approach            | Reason rejected                                                                                                                                                                |
|-------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Esprima.NET**               | Archived by the same author (also BSD-3-Clause); superseded by Acornima. Functionally equivalent but no longer receives spec / bug-fix updates. The task description recommended it before the archival was public.|
| **Jint** (parse-only mode)    | Pulls the full JavaScript interpreter even when only parsing is needed. Internally uses Acornima already — using Jint adds a layer with no detection benefit.                  |
| **ClearScript** (V8 wrapper)  | Native V8 binaries (~30 MB), platform-specific deploy artifacts, Apache 2.0 + V8 BSD-3 — disproportionate for a syntax-only check in a global dotnet CLI tool.                  |
| **NiL.JS / YantraJS**         | Niche communities, weaker coverage of ES edge cases compared to Acornima; higher risk of false positives / negatives on the Creatio page-body corpus.                          |
| **Node subprocess** (`node -c` or npm `esprima`) | Would make clio depend on a Node runtime on the user's machine — a hard regression for a self-contained .NET tool. Process-spawn cost exceeds the parse cost itself.|
| **Microsoft.JScript**         | Deprecated; .NET Framework only. clio targets .NET 10.                                                                                                                          |
| **Roslyn / C# parsers**       | Parse C#, not JavaScript.                                                                                                                                                       |
| **Hand-written regex scanner**| Explicitly rejected in the task description. JavaScript grammar is context-sensitive (template literals, regex literals, `await`/`yield` context, ASI) and not safely parseable by regex. |

## Consequences

### Positive

- Deterministic syntax check (no model variance, no token cost, precise error
  location) — catches a class `PageBodySamplingService` cannot.
- Aligns the existing validators / converters / handlers guides ("must be
  syntactically valid JavaScript before `sync-pages`") with enforced behavior.
- Tiny dependency footprint (~250 KB managed assembly).

### Negative / Trade-offs

- One additional NuGet dependency to keep current. Mitigated by central
  package management in `Directory.Packages.props`.
- AMD `define(...)` wrapper must parse cleanly via `ParseScript`. Confirmed by
  the Day-0 probe; documented as the chosen parse mode in the validator code.

### Out of scope (deferred follow-up per the task description)

A lint pass over the resulting AST for known agent hallucinations
(`await X = Y`, direct `$context.<Attr> = …` assignment, handlers as object,
validators / converters as array) is **out of scope** for ENG-89796. The
syntax-only check is the deterministic floor; the lint pass is an optional
quality layer to be filed as a separate ticket once the AST plumbing is in
place.
