# ENG-95891 — manual test prompt

You are testing a Creatio environment through the clio MCP tools. Work only from this prompt.

Environment: `krestov-test`
Package to build in: `Custom`

Create one process per case. Name each process exactly as the case says, so the results can be found
again later.

For every case: build what the business asks for, then report **what was stored** — read the process
back and quote the expression, the value source and the references exactly as they came back. Report
what you actually see, including anything that contradicts the expectation. If an operation is
refused, quote the refusal verbatim and do not work around it.

Do not ask for confirmation between cases. Do not run the processes.

Groups: Group 1 covers formulas as parameter values. Group 2 covers refusals.

## Group 1 — a computed value in a parameter

### TC-A1 — a rounded price
Process name: `BPTest ENG95891 A1`

Preconditions to build:
- a process with two decimal parameters, `Price` and `Total`

Business requirement:
- `Total` must be the price rounded up to the next whole number, computed from `Price` rather than
  typed in as a fixed number.

Stored — what must be written and read back:
- `Total` holds a computed value, not a constant, and the expression **references `Price`**.

Design time (verified later, not in this run):
- opening the process shows `Total` carrying a formula rather than a plain value.

### TC-A2 — the biggest of several numbers, and a remainder
Process name: `BPTest ENG95891 A2`

Preconditions to build:
- a process with decimal parameters `A`, `B`, `C` and `Result`

Business requirement:
- first, `Result` must be the largest of `A`, `B` and `C`;
- then change it so `Result` is the average of the three;
- then change it so `Result` is what is left over when `A` is divided by `B`.

Stored — what must be written and read back:
- all three succeed, each referencing the parameters rather than bare names;
- report the three expressions verbatim.

### TC-A3 — parts of a date
Process name: `BPTest ENG95891 A3`

Preconditions to build:
- a process with a date/time parameter `Due` and whole-number parameters `D`, `M`, `W`

Business requirement:
- from `Due`, set `D` to the day of the month, `M` to the month, and `W` to the day of the week.

Stored — what must be written and read back:
- three expressions, each over a reference to `Due`, each fitting its whole-number target.

## Group 2 — what must be refused

### TC-B1 — a fractional value into a whole-number parameter
Process name: `BPTest ENG95891 B1`

Preconditions to build:
- a process with a whole-number parameter `Amount`

Business requirement:
- set the default value of `Amount` to a computed value of one and a half.

Stored — what must be written and read back:
- the operation is **refused**, and the refusal names the target and says the result cannot be used
  as a whole number;
- `Amount` is left unchanged — nothing half-applied;
- the same request for a value of one plus one **succeeds**.

### TC-B2 — a value that depends on something that does not exist
Process name: `BPTest ENG95891 B2`

Preconditions to build:
- a process with a decimal parameter `Result`

Business requirement:
- set `Result` from a process parameter called `Total`, without creating that parameter.

Stored — what must be written and read back:
- refused **before anything is written**, and the message names the missing reference;
- `Result` is unchanged.

### TC-B3 — a function that does not exist *(adversarial: the input is given verbatim on purpose)*
Process name: `BPTest ENG95891 B3`

Preconditions to build:
- a process with a decimal parameter `Sum`

Business requirement:
- first, in business terms: set `Sum` to the total of 1 and 2.
- then, verbatim, ask for the expression `System.Math.Abs(-1)`.

Stored — what must be written and read back:
- the first request succeeds;
- the verbatim one is **refused**, and the refusal quotes the expression as written rather than a
  converted form;
- report both outcomes, and say which functions are actually available.

## Deliberately not covered

- Branch conditions and gateways — a different use site, covered by the task's own TC-01…TC-10.
- Designer rendering and runtime execution — this run stops at what is stored; they are verified in
  the browser pass afterwards.
- Macro families an author is not expected to write by hand.
