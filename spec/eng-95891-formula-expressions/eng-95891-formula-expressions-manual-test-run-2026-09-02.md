# ENG-95891 — manual run, 2026-09-02, at the SHIPPED package version

**Why this run exists.** The earlier runs (2026-09-01, R1 and R2) were made against
`CrtProcessBuilder 1.4.0.18`, and the intervening versions changed refusal behaviour — the
activity-result branch guard, unrecognised macro families on a *new* condition, the platform-grammar
element segment. Evidence from .18 did not cover the shipped package, so the stand was updated and
the stored-level cases re-run.

**What this run covers, and what moved after it.** It ran at **1.4.0.37**. The branch then merged
`origin/main` and rebundled as **1.4.0.38**, so this record is one version behind the shipped
archive. That is stated rather than glossed, and the delta was measured rather than assumed:
`git diff` over `Formulas/`, `Graph/` and `Mappings/` between the two cut commits touches ONE file,
`ProcessElementDependencyScanner.cs` — the element-retarget guard, which none of these six cases
exercises. The formula validator, the mapping service and the condition path are byte-identical
between .37 and .38, so the verdicts below still describe the shipped behaviour. The stand itself was
subsequently moved to 1.4.0.38 for the MCP E2E tier, which passes 69/69 there.

## Setup

| | |
|---|---|
| Stand | `krestov-test`, core 10.0.731.0 |
| Package installed | **CrtProcessBuilder 1.4.0.37**, verified with `list-packages` after install |
| clio | built from `feature/ENG-95891-formula-expressions` at `b37a80e1a` |
| Archive | the one this branch ships, SHA-256 `68761a22…`, contents verified by unpacking |
| Driver | `mcp-server` over stdio JSON-RPC, **one request at a time** |
| Scope | what is STORED. Designer rendering and runtime execution are the browser pass, per the prompt |

Writes go through the advertised executor, not the tool directly: a long-tail tool that writes
durable state refuses a direct call because the host cannot show its own confirmation. `create` is
additive → `clio-run`; `modify` can overwrite → `clio-run-destructive`. Read-only `describe` runs
directly and says it would rather be called through the executor.

## Verdicts — 6 of 6 at the stored level

| Case | Verdict | What was stored, verbatim |
|---|---|---|
| TC-A1 rounded price | **PASS** | `source=Script`, `Math.Ceiling([#[Parameter:{12ebc617-…}]#])` |
| TC-A2 largest / average / remainder | **PASS** | three writes, each `source=Script`: `FormulaUtilities.Max(…)`, `.Avg(…)`, `.Mod(…)`, all over parameter references |
| TC-A3 parts of a date | **PASS** | `DateTimeUtilities.Day(…)`, `.Month(…)`, `.DayOfWeek(…)`, each over the same `Due` reference, each into an Integer target |
| TC-B1 fractional into a whole number | **PASS** | refused: *"its result cannot be used as Int32. Cannot convert type \"Decimal\" to \"Int32\" Expression: '1.5'."* — then `1 + 1` succeeded |
| TC-B2 a reference that does not exist | **PASS** | refused, naming the token: *"references '[#[Parameter:{c0ffee00-…}]#]', which is not a parameter of this process. Add the parameter first, or correct…"*; the target stayed unset (`source=None`) |
| TC-B3 a function that does not exist | **PASS** | `1 + 2` succeeded; the verbatim `System.Math.Abs(-1)` refused: *"it references 'System', which does not exist. Only process parameters, system variables, system settings and the Creatio form…"* |

## Two things worth recording precisely

**The name form is refused by the PLATFORM, not by us — and the guidance already says so.** The first
pass authored `[#PriceParameter#]` and every write failed the platform's own pre-save gate with
`Formula value error: Expression expected (at index 13)` — index 13 being exactly where the macro
starts. `process-formulas` states the rule outright: a process parameter is referenced by its UId
meta-path, `[#[Parameter:{uid}]#]`, "and never by its name". So the authoring flow is
create → describe (to learn the UIds) → author, which is what this run did on the second pass. The
refusal is correct behaviour with a message that names the offending index; it is recorded here
because the failure mode is one an author hits first.

**TC-B1's pre-state was not empty.** The processes already existed from the first pass, so `create`
returned *"A process named … already exists"* (correct, and the message says what to do instead), and
`Amount` already carried `1 + 1` when the `1.5` write was refused. So this run confirms the refusal
did not land, but it does **not** demonstrate a refusal against a virgin parameter — the earlier runs
cover that shape. Stated rather than glossed, because "left unchanged" reads stronger than what was
measured.

## Not covered here

* **Designer rendering and runtime execution.** The prompt puts both in a browser pass afterwards, and
  this run stops at what is stored. TC-01's diagram check (conditional connectors drawn, no gateway
  element added) and the TC-19/TC-20 runtime cases still need a human.
* **AC4's mapping half** rests on that pass: the describe projection of a process parameter needs a
  populated `DataValueTypeManager` the unit harness does not provide.
