# ENG-91853 — manual test prompt, decision points

You are testing a Creatio environment through the clio MCP tools. Work only from this prompt.

Environment: `Creatio`
Package to build in: `Custom`

Create one process per case, named exactly as the case says.

Report each case under three headings, separately:

1. **Stored** — read the process back and quote what was written, exactly. Include the kind of every
   flow leaving a decision point.
2. **What the tools said** — quote any refusal or notice verbatim. A notice that an operation was
   accepted but changed is as important as a refusal.
3. **Runtime** — where the case says to run it, start it and report which path actually ran and how
   you established that.

If an operation is refused, quote the refusal and do NOT work around it — the refusal is the result.

## Group 1 — a decision with several outcomes

### TC-E1 — an order routed three ways, with a catch-all
Process name: `BPTest R5 E1`

Preconditions to build:
- a process with a whole-number parameter `Amount`;
- four possible endings: small, medium, large, and one for anything else.

Business requirement:
- an order under 100 ends as small, from 100 to 999 as medium, 1000 or more as large, and anything
  that matches none of those ends at the catch-all;
- the process must finish on its own, without waiting for a person.

Runtime: run it four times — `Amount` = 50, 500, 5000, and with no value at all — and report which
ending each run reached.

### TC-E2 — two rules that are both true
Process name: `BPTest R5 E2`

Preconditions to build:
- as above, with two endings plus a catch-all.

Business requirement:
- one ending applies to orders over 100, another to orders over 1000. For an order of 5000 both rules
  are true, and the business needs to know which one the process takes and what decides it.

Stored: report whether anything in the process records the priority between the two.

Runtime: run with `Amount` = 5000 and report which ending ran and why.

### TC-E3 — a decision with nothing to fall back on
Process name: `BPTest R5 E3`

Business requirement:
- a decision point with two outcomes, one for orders over 100 and one for orders over 1000, and
  deliberately NO catch-all.

Report what happens when you try to build this, and — if it builds — run it with `Amount` = 10, an
amount that matches neither rule, and report the outcome.

## Group 2 — steps that all start at once

### TC-E4 — three checks that must run together
Process name: `BPTest R5 E4`

Business requirement:
- when the process starts, three checks must ALL begin at the same time — not one chosen among them —
  and the process ends after they are all under way.

Then, separately, try to make ONE of those three start only for orders over 100, and report exactly
what the tools answer.

## Group 3 — taking a decision back

### TC-E5 — the rule is withdrawn
Process name: `BPTest R5 E5`

Preconditions to build:
- a process where a decision sends orders over 100 one way and everything else another.

Business requirement:
- the business withdraws the rule: that path should no longer be conditional at all.

Report what the tools answer. If the request is refused, quote the refusal and stop — do not remove
and re-add the path to force it through. If it succeeds, read the process back, then run it with
`Amount` = 10 and report which ending ran.

## Deliberately not covered

- Formulas inside conditions — the vocabulary is covered by its own test set.
- Anything a person must complete by hand: every case here finishes unattended.
