# ENG-95891 — manual test run R2, 2026-09-01

**Verdict: stored level only. Design time and runtime NOT verified.** Six of six cases PASS, verified
by independent read-back. This run exists to re-measure against the generation the branch actually
ships, after the first run was found to have measured a generation that predated the work under test.

## What changed since the first run

| | R1 | R2 |
|---|---|---|
| Guidance generation | 1.13.54 @ `950a998` | **1.13.65** @ `eb69cf7` (pushed as `tmp/eng95891-kb-1165`) |
| Articles under `guides/processes/` | 3, `process-modeling.md` at 122 KB | **10**, `process-modeling.md` at 20 KB, new `formulas.md` |
| Parameter-reference form taught | no — commit `4feb042` was not an ancestor | **yes** |
| Gates run | identity, positive control | identity, positive control, **content (gate 3)** |
| Stand | same, CrtProcessBuilder 1.4.0.18 | same, cleaned of R1's seven schemas first |

## Efficiency — the reason for the re-run

| Metric | R1 | R2 | Change |
|---|---|---|---|
| Total tool calls | 77 | **44** | −43% |
| Assistant turns | 160 | **82** | −49% |
| `create-business-process` | 10 | **6** | exactly one per process |
| `modify-business-process` | 33 | **10** | −70% |
| `describe-business-process` | 13 | 11 | — |
| `get-guidance` | 3 | 6 | more, smaller reads — the split working as designed |
| `Bash` / `Read` / `Grep` | 10 | **0** | no filesystem access at all |
| Against baseline (~26) | 2.75x | **1.7x** | — |

**D1 is resolved.** The executor wrote `[#[Parameter:{uid}]#]` directly, from the guidance, on the
first attempt. No reverse-engineering, no exhausting of name-based spellings, no harvesting the token
out of a structural mapping read-back. The 33 modifies of R1 were the cost of that search; 10 is what
the work actually takes — and A3's three mappings went in a **single** call, which R1 never managed.

**D3 is resolved.** `process-formulas` and `process-modeling` both return whole in one response. R1
spent 10 filesystem calls grepping a spilled 122 KB article; R2 spent none.

**D2 persists, unchanged.** All 30 process operations still went through `clio-run`, and the executor
still spent 5 `ToolSearch` calls and 2 `get-tool-contract` calls on discovery before it could act. No
resident tool was called even once. This is now the only open efficiency finding, and it is clio's:
tool profile and routing, not guidance.

## Per case — stored level, independent read-back

| Case | Process | Stored | Verdict |
|---|---|---|---|
| TC-A1 | `UsrBpTest_Eng95891A1` | `Total` = `Math.Ceiling([#[Parameter:{c6bb6092…}]#])`, source `Script`; the uid is `Price` | PASS |
| TC-A2 | `UsrBpTest_Eng95891A2` | `Result` = `FormulaUtilities.Mod(A, B)` by uid, source `Script`; `Max` and `Avg` over three uids each confirmed before being overwritten in place | PASS |
| TC-A3 | `UsrBpTest_Eng95891A3` | `D`/`M`/`W` Integer, source `Script`, `DateTimeUtilities.Day` / `.Month` / `.DayOfWeek` over `Due`'s uid, no `Get` prefix | PASS |
| TC-B1 | `UsrBpTest_Eng95891B1` | `3 / 2` refused — names the target, says the result cannot be used as Int32, quotes the expression as written; parameter left at `source: None`; `1 + 1` then stored | PASS |
| TC-B2 | `UsrBpTest_Eng95891B2` | refused with "Process parameter TotalParameter was not found"; `Result` still `source: None`, nothing created | PASS |
| TC-B3 | `UsrBpTest_Eng95891B3` | `1 + 2` stored; verbatim `System.Math.Abs(-1)` refused with "it references System, which does not exist", quoting the expression as written; `Sum` unchanged afterwards | PASS |

Levels not covered by this mode: **design time — not verified**, **runtime — not verified**.

## The short form is settled

R1 stored the long meta-path `[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{uid}]#]`; R2 stored
the short `[#[Parameter:{uid}]#]`. Both round-trip, both read back on the parameter as `source: Script`.
So the long form is what the server writes when a structural mapping is converted — which is why an
agent with no guidance finds that one first — and the short form is what an author writes. TC-11's note
is correct, and this run corroborates the write test in the verification document independently.

## Gates

| Gate | Result |
|---|---|
| 1 — identity | `Revision: eb69cf74…` equals the pinned commit; version 1.13.65, sequence 1013065 |
| 2 — positive control | `routing` and `process-formulas` both returned real content, whole, before launch |
| 3 — content (new) | `4feb042` is an ancestor of the pinned revision; article inventory 10 files; `process-modeling.md` 20 KB |

Gate 3 is the gate whose absence produced R1's two false diagnoses. It is what made this run worth
running rather than repeating.

## State left behind

- Six R2 processes on `krestov-test` in `Custom`, unrun — the input for the design-time and runtime
  pass, which still has not happened for formulas.
- R1's seven schemas were deleted before this run, verified by a read-back that reports them missing.
- Guidance is pinned to `tmp/eng95891-kb-1165`; the flag `knowledge-allow-unsequenced` is enabled.
  Teardown after the browser pass: disable the flag, restore `appsettings.json.bpskills-backup`,
  reinstall the released library. The temporary clio-knowledge branch can then be deleted.

## Next

    /bp-test-run ENG-95891 --mode browser --env krestov-test

---

# Browser pass — 2026-09-01, mode `browser`

Appended to this run because it verifies what this run created. Manifest checked first: the stand it
names, `krestov-test`, is the stand addressed.

Logged in through the stand's stored `Supervisor` profile button, so no credential passed through the
agent.

## Design time — verified

**All six processes are present and Active** in Process library (`BPTest ENG95891 R2 A1` through `B3`,
package Custom).

**TC-A1** — the only design-time expectation the prompt declared. Diagram: start `Calculation
requested` → end `Calculation done`, one sequence flow, both captions business-readable rather than
`ProcessStart`/`ProcessEnd`. Parameters panel:

| Parameter | Type icon | Rendered value |
|---|---|---|
| `Price` | Float | *Select value* (empty) |
| `Total` | Float | **`RoundUp([#Price#])`** |

The stored value is `Math.Ceiling([#[Parameter:{c6bb6092…}]#])`. The designer resolves the UId
meta-path back to the friendly name **and** renders the .NET function under the designer's own
spelling. That is exactly what `process-formulas` predicts, and this is the first observation of the
conversion running in that direction. **PASS** — `Total` carries a formula, not a plain value.

**TC-A3** — same shape, parameters:

| Parameter | Type icon | Rendered value |
|---|---|---|
| `Due` | Date/Time | *Select value* (empty) |
| `D` | Integer | `Day([#Due#])` |
| `M` | Integer | `Month([#Due#])` |
| `W` | Integer | `DayOfWeek([#Due#])` |

Three Integer targets, three date helpers without a `Get` prefix, each over `Due`. **PASS.**

## Runtime — one data point, not a verdict

`BPTest ENG95891 R2 A3` was started from the designer (`Successfully started`) and appears in the
Process log as **Completed**, start and end at 9/1/2026 10:09 PM, package Custom, owner Supervisor.

So a stored formula is **evaluated by the engine**, not merely persisted — the first time anything in
this exercise reached runtime. It is not a case verdict: the prompt declared no runtime expectations,
and the input parameters are empty, so nothing asserts *what* the formulas computed.

## Findings from this pass

**1. A false blocker, avoided — and worth recording.** Navigating directly to
`#ProcessSchemaDesigner/<uid>` fails: the console shows `Script error for "ProcessSchemaDesigner"`
under a cluster of `Unsatisfied version 22.0.8 … required =21.2.17` shared-singleton errors from
`process-designer-component`, `voice-to-text`, `two-factor` and `error-list-dialog`. The designer was
about to be reported broken on this stand.

It is not. Opening the same process the way a user does — Process library, click, **Open** — loads it
fine at `?vm=SchemaDesigner#process/<uid>`, in a new tab. The Angular version errors are present and
non-fatal. The route was wrong, not the stand. This is the rubric's own rule paying off: a probe that
can only fail one way is not evidence.

**2. The prompt cannot support a runtime pass, and that is a defect of the prompt.** It declares one
design-time expectation and zero runtime ones, because it was written for the stored level. To make a
browser pass meaningful the suite needs cases that carry a computed value to something a person sees —
the pattern the task's own TC-19 uses, a computed subject reaching an Activity card. Recorded as a
prompt defect, and it feeds `/bp-test-cases --revise`.

**3. The designer does not reload on a hash change.** Replacing the process UId in the URL keeps the
previously loaded schema, title included; only F5 loads the new one. Harmless for a human, a trap for
automation.

**4. The stand carries a `Compilation error` badge** in the shell header, pre-existing and unrelated to
these processes. Worth knowing before drawing conclusions from anything that needs compiled
configuration.

## Verdict after both passes

Stored: 6/6 PASS. Design time: 2 processes inspected, both PASS, covering the only expectation the
prompt declared. Runtime: reached once, completed, nothing asserted about the computed values.
