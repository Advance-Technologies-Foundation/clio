# ENG-95891 — manual test run, 2026-09-01

**Verdict: stored level only. Design time and runtime NOT verified.** Six of six cases PASS at the
stored level. Nothing here says the feature works for a user: no process was opened in the designer
and none was executed. That is mode `browser`, and it has not run.

## Run header

| | |
|---|---|
| Mode | `agent` |
| Isolation | `isolated` (no API key; the executor still loaded the global CLAUDE.md, user skills, plugins, hooks) |
| Stand | `krestov-test` — http://d_krestov_n.tscrm.com:1026, core 10.0.731.0 |
| Package | CrtProcessBuilder **1.4.0.18** (stand == branch; install skipped by design) |
| clio | `feature/ENG-95891-formula-expressions` @ `77a8ff1ba`, built locally |
| Guidance | **local**, 1.13.54, git transport, revision `950a998…`, sequence 1013054 |
| Positive control | `get-guidance name=routing` and `name=process-modeling` both returned real content before launch |
| Executor session | `916fc5e0-5432-4de9-8b73-a8aeb3d36216` |
| Package on stand | `Custom`; six processes created, left unrun |

## Baseline

A well-guided agent needs about **28** calls: 3 guidance reads, 1 schema-prefix, 6 creates, 12
mappings (A1=1, A2=3, A3=3, B1=2, B2=1, B3=2), 6 read-backs.

**Actual: 77 calls — 2.75x baseline.** 58 through `clio-run` (10 create, 33 modify, 13 describe, 2
misc), 3 `get-guidance`, 2 `get-tool-contract`, 2 `ToolSearch`, 5 `Bash`, 3 `Grep`, 2 `Read`. No tool
result came back flagged as an error.

## Per case — stored level, verified by independent read-back

Read back by this session through the same build, not taken from the executor's account.

| Case | Process | Stored | Verdict |
|---|---|---|---|
| TC-A1 | `UsrPrice_ComputeTotal` | `Total` source `Script`, `Math.Ceiling` over the long uid meta-path of `Price` | PASS |
| TC-A2 | `UsrNumbers_ComputeResult` | `Result` source `Script`, final value `FormulaUtilities.Mod(A, B)` by uid; `Max` and `Avg` over three uids were confirmed by the executor read-back before being overwritten | PASS |
| TC-A3 | `UsrDue_ExtractDateParts` | `D`, `M`, `W` Integer, source `Script`, `DateTimeUtilities.Day` / `.Month` / `.DayOfWeek` over the uid of `Due` — no `Get` prefix | PASS |
| TC-B1 | `UsrAmount_SetDefault` | fractional value refused, naming the target and saying the result cannot be used as Int32, quoting the expression; parameter untouched after the refusal; the integer form then stored | PASS |
| TC-B2 | `UsrResult_SetFromTotal` | refused with "Process parameter Total was not found"; `Result` still `source: None`, no `Total` parameter created | PASS |
| TC-B3 | `UsrSum_ComputeTotal` | the business form stored as `1+2`; the verbatim namespace-qualified call refused with "it references System, which does not exist", quoting the expression as written | PASS |

Levels not covered by this mode: **design time — not verified**, **runtime — not verified**.

## Defects

### D1 — a parameter reference inside `expression` is not reachable from the guidance (High)

**Owner: knowledge library.** Possibly also the tool description.

Every by-name form the executor tried inside an `expression` mapping was refused: the parameter
caption, its schema name, dotted variants and raw GUIDs all came back as "does not exist", and the
bracketed friendly form does not even parse ("Expression expected (at index 0)"). The working token is
the long uid meta-path the server itself writes.

The executor found it only by building a structured `processParameter` mapping — which *is* name-based
— and reading the stored value back to harvest the token.

Checked against the pinned article (`process-modeling.md`, 1093 lines, library 1.13.54): it teaches
friendly `[[param:Name]]` macros **for email bodies** (line 85), lists `processParameter`
("a process parameter by name") as a mapping source, and describes `expression` as "a raw formula"
(line 664) — with nothing about how a parameter appears inside one, and no warning that a bare name is
rejected there.

Cost: 10 creates for 6 processes and 33 modifies for 12 intended mappings — roughly 25 wasted calls,
the bulk of the overshoot.

Note for the task's own TC-11: its note claims the SHORT bracketed form works and "is what the
guidance now teaches". In the library pinned here the article carries **neither** form, and the
executor's working form is the long one. Whether the short form still works was not tested — that
needs a mutation this run did not make.

### D2 — every process operation went through `clio-run`, none through a resident tool (Medium)

**Owner: clio — tool profile / routing.**

58 of 77 calls were `clio-run` dispatches. The executor had already loaded
`create-business-process`, `modify-business-process` and `describe-business-process` through
`ToolSearch`, then still routed everything through the generic executor. Every read-only
`describe-business-process` therefore arrives wrapped in a tool the host marks destructive and must
confirm.

### D3 — the guidance article does not fit in-band (Medium)

**Owner: knowledge library.**

`get-guidance name=process-modeling` returned about 121 KB; the harness spilled it to a file, and the
executor then spent 10 calls grepping the article on disk to answer questions it had already "read". A
guide that cannot be consumed in one response is consumed by search, and search finds only what it
queries for — D1 is what that misses. Splitting is reportedly in flight (clio-knowledge PR #110,
merged after the commit pinned here).

## Harness defect found by this run

**`--strict-mcp-config` restricts MCP servers, not built-in tools.** The executor had `Bash`, `Read`
and `Grep` and could have read the clio repository or the guidance checkout directly, which would have
invalidated the measurement. Verified after the fact that it did not: all filesystem access stayed
inside its own scratch directory, grepping the spilled guidance article. The harness must either pass
`--disallowedTools` for filesystem tools or verify this every run rather than assume it. Recorded
against `bp-test-run`.

## Teardown

Not yet performed — the local guidance wiring and the `knowledge-allow-unsequenced` flag are still in
place, and the six processes are still on the stand as input to mode `browser`.

## Next

    /bp-test-run ENG-95891 --mode browser --env krestov-test
