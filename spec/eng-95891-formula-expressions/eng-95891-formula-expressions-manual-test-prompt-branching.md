# ENG-95891 — manual test prompt, branching group

You are testing a Creatio environment through the clio MCP tools. Work only from this prompt.

Environment: `krestov-test`
Package to build in: `Custom`

Create one process per case, named exactly as the case says.

Report each case under three headings, separately:

1. **Stored** — read the process back and quote what was written, exactly.
2. **Design time** — say what you can and cannot determine without a browser. Do not guess at what a
   designer would draw; if you cannot see it, say so.
3. **Runtime** — start the process with the values the case names and report which path actually ran,
   with the evidence you used.

If an operation is refused, quote the refusal verbatim and do not work around it.

## Group 1 — a decision the process makes by itself

### TC-D1 — the order takes the right path
Process name: `BPTest ENG95891 R4 D1`

Preconditions to build:
- a process with a whole-number parameter `Amount`;
- two possible outcomes, one meaning the order was approved and one meaning it was not.

Business requirement:
- when an order is registered, an order above 100 must reach the approved outcome and anything else
  must reach the other one;
- the process must finish on its own, without waiting for a person.

Stored:
- the decision is recorded on the process, and the read-back shows which outcome each path leads to.

Runtime:
- started with `Amount = 500` the process ends at the approved outcome; started with `Amount = 10` it
  ends at the other. Say how you established which one ran.

### TC-D2 — which rule wins when two of them are true
Process name: `BPTest ENG95891 R4 D2`

Preconditions to build:
- as above, with two outcomes.

Business requirement:
- one path must apply to orders above 100 and the other to orders above 1000. Both rules are true for
  an order of 5000, and the business needs to know which one the process will take.

Stored:
- report whether anything in what you can read back records the priority between the two paths.

Runtime:
- started with `Amount = 5000`, report which outcome ran, and state the rule that decides it.
- If nothing in the stored process records that priority, say so plainly. A person building this
  process needs to know what determines the answer.

### TC-D3 — a path with no condition at all
Process name: `BPTest ENG95891 R4 D3`

Preconditions to build:
- the process from TC-D1, or one shaped like it.

Business requirement:
- the business changed its mind: the approved path should no longer carry any condition.

Stored:
- report what happened. If the request is refused, quote the refusal. If it succeeds, read the process
  back and report exactly what the path now carries.

Runtime:
- start the process with `Amount = 10` — an amount that must NOT reach the approved outcome — and
  report which outcome ran.

## Group 2 — a decision after a human step

### TC-D4 — a decision taken after someone does something
Process name: `BPTest ENG95891 R4 D4`

*This case is expected to expose known platform behaviour. Report what you observe; do not treat an
awkward result as a defect on its own.*

Preconditions to build:
- a process shaped: start, then a step where a responsible employee has to do something, then two
  outcomes.

Business requirement:
- after the employee finishes, an order above 100 must reach the approved outcome and anything else
  the other one.

Stored:
- both paths carry their decision, and the read-back quotes them.

Runtime:
- leave the task OPEN and do not complete it. Report that the process is parked at the step.

Then, and this is the part that matters:
- read the process back a second time and confirm both decisions are **still there, unchanged**.
  A decision that has disappeared is a regression and must be reported as one.

## Group 3 — a computed date and the clock

### TC-D5 — the same moment, told twice
Process name: `BPTest ENG95891 R4 D5`

Preconditions to build:
- a process with a step where a responsible employee has to do something, due at a moment computed
  when the process runs rather than entered as a fixed date.

Business requirement:
- the task must be due exactly two days after the process starts.

Stored:
- quote what was written for the deadline.

Runtime:
- start the process, leave the task open, then report the deadline **twice**: as the stored record
  gives it, and as a person reading the interface would see it. State whether the two agree, and if
  they do not, report both values and the difference rather than picking one.

## Deliberately not covered

- Formulas stored on process parameters — measured four times already; this prompt is about decisions
  on paths.
- The wording of refusals for bad formulas — measured directly at this package version.
- An environment carrying an older package: this harness installs the current one and cannot produce
  that state.
