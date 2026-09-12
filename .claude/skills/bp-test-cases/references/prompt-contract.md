# Prompt contract

The rules a manual test prompt must satisfy, with the failure each rule prevents.

## Why the rules are strict

The run debugs three things: `CrtProcessBuilder`, clio, and the knowledge library. The executor
session runs with no memory, no repository access, and no sight of the Jira issue. It has exactly
one input — this prompt — and one toolset: the shipped clio MCP surface with its guidance library.

So the prompt is a measuring instrument. Every implementation detail leaked into it is a detail
those three are no longer being tested on: naming the element skips whatever the guidance failed to
explain, and naming the tool skips whatever its description failed to convey.

A prompt that names the elements produces a green run over a product that would have failed a real
user. That is the only way this exercise can silently stop working.

## Rule 1 — business language, not construction

State what the business needs to happen. The executor decides how to express it.

| Bad (leads the executor) | Good (states the need) |
|---|---|
| Create a process with an exclusive gateway and two conditional flows | When an order is registered, orders above the approval threshold must go to a manager for approval, and the rest must proceed straight to fulfilment |
| Add a formula element that computes `Amount * 1.2` | The order total the customer sees must include VAT, calculated from the net amount |
| Use `modify-business-process` with `elementType=UserTask` | A responsible employee must receive a task to check the documents before the order is confirmed |

Forbidden in the prompt text: element type names, MCP tool names, argument names, schema names,
UIds, package names, code identifiers, and file paths.

Permitted, because the executor cannot invent them: the environment/stand name, the application or
section the scenario lives in, business field names as a user sees them in the UI, and concrete
business values (amounts, thresholds, dates) needed to make the expected result checkable.

**Carve-out — adversarial cases state their input verbatim.** When the case tests a refusal, an error
message, or tolerance of a wrong form, the exact input *is* the test and must be given literally:
"set it using, verbatim, `System.Math.Abs(-1)`". Rule 1 governs cases that test **discovery** — can
the agent find the right form unaided. It does not govern cases that test **reaction** to a form the
tester chose. Mark such a case as adversarial so a reader does not mistake it for a leaked
implementation detail, and keep the two kinds separate: a case cannot test discovery and reaction at
once, because the verbatim input destroys the discovery.

## Rule 2 — three observation levels, at least one per case

There are three places a defect becomes visible, and each hides the others:

1. **Stored** — what the toolkit wrote and reads back. Catches serialization: a wrong field, a wrong
   spelling, a meta-path in the wrong form. Observable without opening anything.
2. **Design time** — what a person sees after opening the process in the designer. Catches what
   serializes cleanly but renders wrong, and what the designer refuses to save.
3. **Runtime** — what happens when the process actually runs. Catches what stores and renders
   correctly and still does not execute, or executes as the wrong branch.

**Every case declares at least one level and is explicit about where it stops.** A storage-level case
is legitimate and often the only thing reachable before a dependency lands — but it must say so, so
nobody reads it as proof the feature works.

**Runtime coverage is mandatory for the suite, not for each case.** A suite whose every case stops at
what is stored has not tested the feature at all: state which cases carry a value all the way through
to something a user sees, and if none do, say why and what blocks it.

**Known platform behaviour.** A case may expect an outcome that is wrong-but-known — the designer
cannot render a formula in a given panel, a save raises a spurious required-field warning. Label it
explicitly as platform behaviour that must **not** be filed as a defect, and state the neighbouring
outcome that *would* be a regression (typically: the value being lost rather than merely unshown).
Without that pairing the label becomes a blanket excuse and the case stops detecting anything.

What each level asks for, concretely:

**Stored** — the expression, value or reference as it is written and read back. Quote the exact form
expected, because a plausible-but-wrong spelling is the defect this level exists to catch.

**Design time** — what a person sees after opening the process in the designer. Be specific enough
to fail on a wrong-but-plausible result:
- the shape of the diagram a business analyst would expect (branches, their order, where they rejoin)
- captions and labels as they must read for a human, not internal names
- what must be visible in element settings when opened
- what must **not** be there — a stray element, an empty branch, a duplicated condition

**Runtime** — what must happen when the process actually runs, and the trace that proves it:
- the observable business outcome (a record changed, a task created, a value computed)
- which branch was taken and, when it matters, which was not
- where the proof is visible to a person — the record, the task list, the process log
- for negative cases: the error the user sees, and the state the data is left in

## Rule 3 — self-contained

Everything the executor cannot discover must be present: the stand, how to reach the relevant
section, the business data to use or create, and the acceptance thresholds. Everything it *can*
discover through the tools must be absent — that discovery is what is under test.

## Rule 4 — no memory reliance

The prompt must not depend on any fact you know from previous sessions unless that fact is written
into it. This applies to the authoring session too: if a detail comes from your recollection rather
than from the diff, the issue, or the guidance library, verify it before it goes in.

## Rule 5 — one scenario per case, honest coverage

Cover what the diff supports: the happy path, the branch/negative case, and the boundary the change
actually introduced. Do not pad with scenarios the code does not implement — a failing case that was
never in scope wastes a stand run and reads as a defect.

## Rule 5b — a case must ask for something only the feature can express

A case is void as a test of the feature when the business requirement it states has a legitimate
answer that does not touch the feature at all. It will still pass, and the pass will mean nothing.

Measured: a case asked for a task "due three days after the process starts, computed rather than
entered as a fixed date". The agent set the element's own `Duration = 3 days`, stored no formula and no
date constant, and the platform computed the deadline at task-creation time. The requirement was met,
the runtime result was correct, the agent explained its choice — and not one formula was exercised.

So when drafting, ask of every case: *what is the cheapest correct way to satisfy this sentence, and
does it go through the thing under test?* If a builder can honestly satisfy it another way, the case is
measuring the platform's convenience features, not your change. Tighten the requirement until the
feature is the only honest route — a computed value that no element setting produces, an expression
over two parameters, a result that has to be recomputed rather than set once.

This is not the same as leading the executor. Rule 1 forbids naming the mechanism; this rule requires
choosing an OUTCOME the mechanism is the only way to reach. State the harder outcome, still in business
terms, and let the agent find the route.

## Rule 6 — the suite has a shape, and states what it leaves out

A flat list of cases cannot be reviewed: nobody can tell whether it is complete.

**Group cases by use site.** A capability usually has more than one place it is used, and the groups
are what make coverage legible — "conditions on flows" and "values in parameters" fail differently
and need different preconditions. Say in the preamble what each group covers and which groups are
reachable today.

**Declare what is deliberately not covered, and why.** This section is load-bearing, not politeness:
it is what answers "why is there no coverage of X?" without a reviewer having to ask. Legitimate
reasons include a dependency that has not landed, behaviour that changes between platform releases,
and surface that automated tests already assert and an agent is not expected to author. An omission
with no stated reason reads as an oversight, and gets re-litigated every review round.

Both sections belong in the prompt file, not only in the run report — the executor's coverage is
bounded by the prompt, so the boundary has to travel with it.

## Template

```markdown
# <ENG-KEY> — manual test prompt

You are testing a Creatio environment through the clio MCP tools. Work only from this prompt.

Environment: <stand alias / URL>
Application/section: <where the scenario lives>

For every case below: build what the business asks for, then report what you observe at each level the
case names. Report exactly what you see, including anything that contradicts the expectation.

Groups: <group 1 - use site, what it covers> / <group 2 - the other use site>

## Group 1 - <use site>

## TC-01 — <business outcome in one line>

Preconditions:
- <business state that must exist>

Business requirement:
- <what must happen, in the words of the person who needs it>

Stored — what must be written and read back:
- <exact expected form, or omit this block>

Design time — what must be visible when the process is opened in the designer:
- <... or omit this block>

Runtime — what must happen when the process runs, and where it is visible:
- <... or omit this block, saying what blocks it>

## TC-02 — <...>

## Deliberately not covered

- <what, and the reason>
```

## Self-check before publishing

- [ ] No element type, tool, argument, schema, package, or file path is named — except in a case
      explicitly marked adversarial, where the verbatim input is the test
- [ ] Every case names at least one observation level and is explicit about where it stops
- [ ] At least one case carries a value through to runtime, or the suite says what blocks it
- [ ] Any wrong-but-known outcome is labelled platform behaviour, paired with the neighbouring
      outcome that would be a regression
- [ ] Cases are grouped by use site, and the suite states what it deliberately does not cover
- [ ] Every expected result is falsifiable — a wrong-but-plausible outcome fails it
- [ ] No case can be satisfied honestly without going through the feature under test
- [ ] The executor could run this with no repository, no memory, no Jira access
- [ ] Every scenario is supported by the committed diff
- [ ] English, sequential `TC-0X`, one scenario per case
