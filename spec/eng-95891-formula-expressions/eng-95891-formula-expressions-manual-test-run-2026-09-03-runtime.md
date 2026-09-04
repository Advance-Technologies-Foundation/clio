# ENG-95891 — blind runtime run, 2026-09-03

**The two things no earlier run covered.** The 2026-09-02 and 2026-09-03 runs measured what the
shipped stack stores and says, executed by the feature session with full knowledge of the
implementation, and both stopped at the stored level. The 09-03 record names the gap itself: *"A blind
instrument run at this generation is still worth having, and is a separate exercise."* This is that
run, and it also carries formulas past storage into runtime for the first time.

## Setup — all three gates green

| | |
|---|---|
| Stand | `krestov-test`, core 10.0.731.0 |
| Package | **CrtProcessBuilder 1.4.0.52** — stand and branch agree (`ce9234ea5 Rebundle at 1.4.0.52`) |
| clio | built from `feature/ENG-95891-formula-expressions` at `ab20c4deb`, Release/net8.0 |
| Guidance | **1.13.87**, git transport, revision `044877c4`, sequence 1013087 |
| Gate 1 identity | revision equals the one the branch's own fixture pins |
| Gate 3 content | `4feb042` (parameter-reference guidance) is an ancestor; 11 articles, `branch-conditions.md` now split out |
| Gate 2 serving | `process-formulas` returned whole in one response |
| Isolation | `isolated` — no memory, no repository, clio MCP only |
| Executor session | `fe1d2adb-cc64-4cd0-9257-30083349d9f6` |

Gate 3 earned its place again: the pin was still on 1.13.65 from the previous run, while the branch had
moved to 1.13.87. A third stale-generation measurement was one command away.

## Verdicts — 3 of 3, stored AND runtime

Every line below was re-read by this session, not taken from the executor's account.

### TC-C1 — the order total on the task a person receives — **PASS**

- **Stored:** `ConfirmOrderTotal.Recommendation`, `source: "Script"`,
  `"Order total with VAT: " + ([#[Parameter:{738b8445-…}]#] * 1.2).ToString("0")` — a reference to
  `Amount`, recomputed. Not the constant `600` the case declares a failure.
- **Started:** the process ran and parked at the Perform task.
- **Result:** Activity `a0389a1d-…`, `Title = "Order total with VAT: 600"`, status *Not started*.
  Confirmed in the browser: the task card opens with that title, and the task appears in the
  **Business process tasks** panel labelled `BPTest ENG95891 R3 C1`.

### TC-C2 — a price rounded up — **PASS**

- **Stored:** `source: "Script"`,
  `"Rounded price: " + Math.Ceiling([#[Parameter:{75ccb656-…}]#]).ToString("0")`.
- **Result:** `Rounded price: 13` from `Price = 12.3`. Visible in the tasks panel.

### TC-C3 — a deadline three days out — **PASS on the requirement, void as a formula test**

- **Stored:** no formula and no date constant. `Recommendation = "Check the documents"`,
  `Duration = 3`, `DurationPeriod = 2` (days), all `ConstValue`. The due date is not stored at all —
  the platform computes it at task-creation time as `StartDate + Duration`.
- **Result:** Activity `2a8ffa2f-…`, start `2026-09-03T17:52:52`, due `2026-09-06T17:52:52` — exactly
  three days. Visible in the panel as `Check the documents`.

The business requirement is met by the element's own mechanism, and the agent explained why it chose
it. But the case therefore exercised **no formula**, which makes it void as a test of this feature.
That is a defect of the case, not of the agent or the product — see *Prompt defects* below.

## Defects

### D1 — a decimal literal written the documented way produces `1.2mm` (High)

**Owner: CrtProcessBuilder / the platform converter, and the guidance.**

The executor wrote `1.2m` — the ordinary C# decimal literal. The refusal:

    Process validation failed: ConfirmOrderTotal.Recommendation
    [Error while executing expression ""Order total with VAT: " + (AmountParameter * 1.2mm).ToString("0")":
     Formula value error: ')' or operator expected (at index 50).]

The converter appends its own `m` to a fractional literal, so an author who writes the suffix gets
**`1.2mm`**. The quoted expression proves it: the text shown is not the text written.

`process-formulas` already documents that "a fractional literal gains an `m`" — but only as an
explanation of how refusals *quote* an expression back. Nothing warns that writing the suffix yourself
breaks the formula. The agent read that article and still hit this, which is the definition of a
guidance gap rather than an agent error.

Cost here was one call: the agent dropped the suffix and the plain `1.2` saved. That is the best case.
The worst is an author who concludes decimals are unsupported.

### D2 — `odata-read` refuses six of ten calls on argument shape (Medium)

**Owner: clio — tool contract discoverability.**

Two successive mismatches, three calls each, before anything succeeded:

| Written | Refusal |
|---|---|
| `select: "Id,Title,StatusId,…"` | `argument 'select' … must be an array` |
| `filter: "substringof('…', Title)"` | `Argument 'filter' is unsupported because raw filter strings are not accepted. Use a structured filter, for example: filters: …` |

Both are the conventional OData spellings, which is exactly why they were guessed. The messages are
good — the second even carries an example — but they arrive one round trip too late, three times over,
because the agent was fixing all three cases in a batch. Six wasted calls out of forty-two, 14%.

### D3 — the long-tail discovery gap, unchanged (Low here)

One `ToolSearch` for the process tools returned `No matching deferred tools found`. Down from three
attempts in the previous run, but the cause is the same one already reported: the process surface is
absent from the resident profile and nothing points at `clio-run`.

## Efficiency

42 calls, 69 assistant turns, for three processes built, started and verified. A well-guided baseline
is about 19 (3 guidance, 1 prefix, 3 create, 3 modify, 3 describe, 3 run, 3 read-back) — so **2.2x**,
and 9 of the 42 were refusals.

Where the calls went: 7 `get-guidance` (up from 6 in the last run — the library split into 11 articles
and routing now sends to sub-guides), 28 dispatches through `clio-run`/`clio-run-destructive`
(3 create, 4 modify, 5 describe, 3 `run-process`, 10 `odata-read`, plus culture, prefix and task-list
lookups), 3 `ToolSearch`, 2 `get-tool-contract`.

**Zero wasted calls on the formula vocabulary.** Every refusal was either the `1.2mm` converter defect,
the `odata-read` argument shape, or the tool-discovery gap. Nothing was spent hunting for how to
reference a parameter — the discovery that cost 21 calls two runs ago now costs none.

## Prompt defects — mine, not the product's

1. **The prompt contradicts itself.** Each case says "the process runs to completion" while the
   standing instruction says to leave every task open. Completing the task is what would let the
   process reach its end event. The executor spotted the contradiction, chose the standing instruction,
   and said so — the right call, and it should not have had to make it.
2. **TC-C3 admitted a non-formula solution.** "A deadline three days out, computed rather than a fixed
   date" is honestly satisfied by `Duration = 3 days`. A case meant to exercise formulas must ask for
   something only a formula can express.

## A flaw in my own analysis method

My first failure count said 5 of 42. The shape-independent recount says **9**: refusals that come back
as `{"success": false}` or `invalid-parameter-type` carry no non-zero `exit-code`, and a detector keyed
to one envelope shape silently under-reports. Every count in this report uses the corrected detector.

## Observation, not a defect

The same activity reads as `2026-09-04T05:52:43Z` through the executor's read and `2026-09-03T17:52:43`
through mine, and the tasks panel renders it as "Tomorrow at 5:52 AM". Twelve hours apart, consistent
within each path. Recorded because a runtime assertion about a *date* would need this settled first;
TC-C3's three-day delta is unaffected, being a difference.

## State

Three processes remain on `krestov-test` in `Custom` (`BPTest ENG95891 R3 C1`…`C3`), each parked at its
Perform task with the task open — that is the evidence, and completing a task destroys it.

Still in test configuration on this machine: guidance pinned to `044877c4` (1.13.87) as a git source,
the `knowledge-allow-unsequenced` flag enabled, `appsettings.json.bpskills-backup` holding the released
configuration, and a leftover `tmp/eng95891-kb-1165` branch in clio-knowledge from the previous run.
