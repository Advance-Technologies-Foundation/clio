# ENG-95891 — manual test prompt, runtime group

You are testing a Creatio environment through the clio MCP tools. Work only from this prompt.

Environment: `krestov-test`
Package to build in: `Custom`

Create one process per case, named exactly as the case says. Every case here must end with the process
actually running — building it is only half the case.

Report three things separately for each case:

1. **Stored** — read the process back and quote the expression and its value source exactly.
2. **Started** — the process was started and reached the end, or did not.
3. **Result** — what a person would now see, quoted from what you can read back.

Leave every task you create OPEN. Closing a task lets the process move on and destroys the evidence.

If an operation is refused, quote the refusal verbatim and do not work around it.

## Group 1 — a computed value a person can read

### TC-C1 — the order total on the task a person receives
Process name: `BPTest ENG95891 R3 C1`

Preconditions to build:
- a process with a whole-number parameter `Amount` whose default value is 500;
- a step where a responsible employee has to do something, between the start and the end.

Business requirement:
- the employee must see, as the title of that task, `Order total with VAT: ` followed by the amount
  plus 20 percent — computed from `Amount`, not typed in as a fixed number.

Stored:
- the task title is a computed value. A stored constant `600` is a **failure** even though the number
  is right: it means the total was worked out once instead of being recomputed.

Started:
- the process runs to completion.

Result:
- the task exists and its title reads `Order total with VAT: 600`.

### TC-C2 — a price rounded up, on the same kind of task
Process name: `BPTest ENG95891 R3 C2`

Preconditions to build:
- a process with a decimal parameter `Price` whose default value is 12.3;
- a step where a responsible employee has to do something.

Business requirement:
- the task title must read `Rounded price: ` followed by the price rounded up to the next whole
  number, computed from `Price`.

Stored:
- a computed title referencing `Price`.

Started:
- the process runs to completion.

Result:
- the task title reads `Rounded price: 13`.

## Group 2 — a computed deadline

### TC-C3 — a deadline three days out
Process name: `BPTest ENG95891 R3 C3`

Preconditions to build:
- a process with a step where a responsible employee has to do something.

Business requirement:
- that task must be due three days after the process starts, computed at run time rather than entered
  as a fixed date;
- give the task the title `Check the documents`.

Stored:
- the due date is a computed value, not a date constant.

Started:
- the process runs to completion.

Result:
- the task exists with the title `Check the documents`, and its due date is three days after the date
  the process ran.

## Deliberately not covered

- Formulas that stop at storage — covered by the earlier prompt and by three previous runs; this one
  deliberately does not repeat them.
- Branch conditions and gateways — a different use site.
- What the designer draws: this run is about what happens when the process runs. A separate browser
  pass looks at the designer.
