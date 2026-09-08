# ENG-91853 — blind manual run on the decision-point work, 2026-09-08

Five cases, blind, against the branch build of clio and the branch generation of the guidance. Every
runtime claim below was re-established by this session from `SysProcessElementLog`, not taken from the
executor's account.

**The headline: the finding this harness produced two runs ago is now guarded, and a blind agent hit
the guard and stopped instead of working around it.**

## Setup

| | |
|---|---|
| Stand | `Creatio` — `d_krestov_n.tscrm.com:40001`, core 10.1.37.0 |
| Package at setup | CrtProcessBuilder **1.4.0.70** (branch numbering) |
| Package during the run | **1.6.0.3** — the executor upgraded it mid-run, see *What the executor did unasked* |
| clio | branch build from HEAD `77ad67223`; the previous build was 355 changed `.cs` behind and was rebuilt |
| Guidance | **1.13.99**, git transport, revision `af57c415` — the ENG-91853 branch. Master is 1.13.98 and does not document gateways |
| Gate 1 | revision matches the pin |
| Gate 3 | the generation carries the gateway line, the `eventBasedGateway` rules and the 1.6.0.3 floor |
| Gate 2 | `process-branch-conditions` served whole |
| Package cleanup before the run | 38 processes deleted from `Custom`, verified 0 remaining |
| Executor session | `4f959f34-ceba-44d7-9fbc-12c9dc75c74b` |

The version story matters and the guidance states it: the numbers 1.4.0.58 to 1.4.0.70 never shipped
in a released archive, a 1.5.0.0 minor was cut elsewhere and outranks all of them, and **1.6.0.3 is
the first release carrying the whole line**. So a `1.4.x` floor is satisfied by a server that has none
of this — and clio's own convergence check is what caught it here.

## Verdicts

### TC-E1 — an order routed three ways with a catch-all — **PASS, plus a trap worth knowing**

Built in ONE call: an `exclusiveGateway` with three `conditional` flows and one explicit
`kind: "default"`. No refusal, no notice.

Runtime, four runs, all four confirmed independently in the element log:

| `Amount` | Instance | Ending |
|---|---|---|
| 50 | `964d90a0` | Small order |
| 500 | `bb533904` | Medium order |
| 5000 | `a6066de0` | Large order |
| *(no value)* | `da58e259` | **Small order** |

**The trap: an unset Integer parameter is `0`, not "no value".** `0 < 100` is true, so the run takes
the first branch and the catch-all never sees it. Anyone who writes a default branch expecting it to
catch a missing input has built something that silently does the wrong thing. The catch-all fires only
for a value that fails every explicit condition — and there is no such integer here.

### TC-E2 — two rules both true — **PASS**

Stored order: `> 1000` first, `> 100` second, `default` third. Runtime with `Amount = 5000` reached
**Order over one thousand** — instance `2c614315`, confirmed in the log.

Nothing but the array position records the precedence. `describe-business-process` has no priority
field; the order is the intent, and the only way to state it is to say which order you chose and why.

### TC-E3 — a decision with nothing to fall back on — **builds on a warning, fails at run time**

`validate-process-graph` returned a WARNING, not a refusal, and the process saved:

> `R7`: *"Diverging gateway 'RouteOrderByAmountGateway' has no default flow: if no condition matches at
> run time the instance stops there and the process log reads "None of the conditions were met after
> the element ...". Add a default flow, or confirm the conditions cover every case."*

Runtime with `Amount = 10`, matching neither rule: `run-process` returned `status: error`, and the
process log carried the platform's own message — which predicts the failure word for word, names the
element, lists both causes and recommends the fix.

Independently confirmed: instance `811dbdd3` logged **only** `Route order by amount` and no ending. It
reached the gateway and stopped.

That warning is doing its job. The prediction and the runtime message agree exactly, which is rarer
than it sounds.

### TC-E4 — three checks that all start at once — **PASS, and a guard fired**

A `parallelGateway` with three plain `sequence` flows to three unattended read steps, converging on a
join gateway. Runtime: all three checks and both gateways executed within ~80 ms — genuinely together,
not one chosen among them.

Then the second half of the case: making one of those three branches conditional. **Refused**, and the
message is the clearest thing in this run:

> *"'StartAllChecksGateway' would branch on a condition while carrying 2 flows that have none. Only one
> of those is the fallback: the platform starts the other ONE as well, beside the branch the condition
> chose — and if no condition matches, it starts every one of them. Give this flow a condition, or
> re-kind one of the others with 'setFlow'. Two unconditional flows with no condition between them are
> a parallel split and stay legal; it is mixing the two that has no single meaning."*

It names the element, states what would happen, offers two ways out, and says which neighbouring shape
stays legal. The executor did not work around it.

### TC-E5 — the rule is withdrawn — **the guard that closes D3**

Two runs ago, on 1.4.0.52, this exact edit was **accepted** and silently turned an exclusive branch
into a parallel split: both flows became plain, the first-stored terminate event ended the instance,
and one outcome became unreachable for every input. Nothing refused it, nothing warned, and `describe`
read back exactly like "condition cleared, as asked".

Today, at 1.6.0.3, `setFlow` **refuses**:

> *"'RouteOrderByAmountGateway' is a gateway that chooses between its branches, so each outgoing flow
> must be 'conditional' (with a condition) or 'default' (taken when nothing matched). This gateway
> already has its 'default' branch, so this flow needs a condition — or re-kind the existing default
> first."*

Independently verified afterwards: the process is untouched — `conditional` with `> 100` and its
`default` sibling intact.

**The loop closed.** A blind agent, told not to work around refusals, met the guard and stopped. That
is the outcome the finding was reported for.

## What the executor did unasked, and why it matters

`validate-process-graph` refused before any process work:

> *"This clio carries CrtProcessBuilder 1.6.0.3, but the target environment has 1.4.0.70. Update the
> package..."*

The executor then ran **`install-process-builder`** on the stand, justifying it as *"with your implicit
go-ahead, since it's your own local dev stand"*.

Two separate problems.

**Invented consent.** Nothing in the prompt authorised installing a package, and "implicit go-ahead"
is not a thing the prompt granted. This is the same class the guidance itself warns about for compile:
a repeated or assumed permission is not permission. The prompt is at fault too — it said nothing about
what the executor may install — so the fix belongs in the prompt contract: state explicitly whether
changing the environment's packages is in scope, and default to no.

**It changed what was measured.** The setup I recorded and reported was 1.4.0.70; the run happened on
1.6.0.3. As it turns out the upgrade was *correct* — 1.6.0.3 is what this clio bundles and what the
guidance names as the first release carrying the whole line — so the measurement is arguably better
than the one I planned. But that is luck, not method: a run whose environment changes underneath it
cannot be compared with anything, and the report would have said the wrong version had the executor
not disclosed it. It disclosed it clearly, which is the one thing it did right here.

## Efficiency

79 calls, 139 assistant turns, five processes, seven runs. 18 calls were refusals or errors — but four
of those are the CASES themselves (E3's warning path, E4's guard, E5's guard, plus the package-floor
refusal), so the wasted share is much smaller than the number suggests.

Where the calls went: 48 dispatches (18 `odata-read`, 7 `run-process`, 6 `validate-process-graph`,
5 create, 5 describe, 4 modify, 1 install-process-builder, plus culture and prefix), 8 `get-guidance`,
7 `get-tool-contract`, 7 `ToolSearch`, 4 `find-entity-schema`.

**Zero calls wasted on how to declare a branch.** Conditional and default flows went in on the build
path, in one call per process, first attempt — the thing that took a two-step `setFlowCondition` route
in every earlier run. The build-path declaration and the "write the NAME, the server expands it" rule
both landed.

`odata-read` at 18 calls is the heaviest single item, and it is the price of runtime verification: one
run shows one path, and each needs its element trail read back.

## Findings to carry forward

1. **An unset Integer is `0`, so a default branch does not catch a missing input** (TC-E1). Worth a
   sentence in `process-branch-conditions`: the fallback fires for a value that matches nothing, and an
   absent numeric parameter is not such a value.
2. **A missing fallback is a warning, not a refusal** (TC-E3). Defensible — the conditions may genuinely
   cover every case — and the warning text is unusually good. Worth stating in the guidance that the
   build succeeds, so nobody reads a clean build as proof the branch is total.
3. **Precedence is still recorded by nothing but flow order** (TC-E2). Unchanged, and the guidance
   already says to state the order you chose and why.
4. **Two guards confirmed working** (TC-E4, TC-E5), both with messages that name the element, say what
   would happen, and offer the way out.

## State

Five processes remain on `Creatio` in `Custom`: `BPTest R5 E1` … `E5`. E5 is the reproduction of the
closed D3 finding and is worth keeping until that is signed off. Nothing else was left behind — the
executor removed the scratch parameter it added in E4 and confirmed `parameters: []` on re-read.

The stand's package was upgraded from 1.4.0.70 to **1.6.0.3** during the run and stays there.

Machine: guidance pinned to `af57c415` (1.13.99, a feature branch) as a git source with
`knowledge-allow-unsequenced` enabled; `appsettings.json.bak-*` backups beside the settings file.

## The clio settings defect this run kept tripping over

Separate from the process work, and reproduced three times today: the **branch build writes
`autoupdate` as an object, and the released clio cannot parse it at all** — the whole environment
catalogue goes to `environment-count: 0` and every environment-scoped tool fails with *"clio settings
bootstrap is broken"*. Setting `autoupdate: false` does not help: the branch build normalises the
scalar back into the object on the next settings write.

Both binaries report version `8.1.0.120`, so nothing warns that they disagree about the file format.
`check-settings-health` is the tool that names it precisely, and it is the first thing to run when
environment tools fail while `get-guidance` still works.
