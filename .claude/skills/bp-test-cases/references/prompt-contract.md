# Prompt contract

The rules a manual test prompt must satisfy, with the failure each rule prevents.

## Why the rules are strict

The executor session runs with no memory, no repository access, and no sight of the Jira issue. It
has exactly one input — this prompt — and one toolset: the shipped clio MCP surface with its
guidance library. So the prompt is a measuring instrument. Every implementation detail leaked into
it is a detail the guidance library is no longer being tested on.

A prompt that names the elements produces a green run over a guidance library that would have failed
a real user. That is the only way this exercise can silently stop working.

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

## Rule 2 — both observation blocks, every case

A case without both blocks tests half the feature. Serialization defects show up in the designer;
execution defects show up only at runtime; each hides the other.

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

## Template

```markdown
# <ENG-KEY> — manual test prompt

You are testing a Creatio environment through the clio MCP tools. Work only from this prompt.

Environment: <stand alias / URL>
Application/section: <where the scenario lives>

For every case below: build what the business asks for, then report what you observe in the process
designer (design time) and what happens when the process runs (runtime). Report exactly what you
see, including anything that contradicts the expectation.

## TC-01 — <business outcome in one line>

Preconditions:
- <business state that must exist>

Business requirement:
- <what must happen, in the words of the person who needs it>

Design time — what must be visible when the process is opened in the designer:
- <...>

Runtime — what must happen when the process runs, and where it is visible:
- <...>

## TC-02 — <...>
```

## Self-check before publishing

- [ ] No element type, tool, argument, schema, package, or file path is named
- [ ] Every case has both a Design time and a Runtime block
- [ ] Every expected result is falsifiable — a wrong-but-plausible outcome fails it
- [ ] The executor could run this with no repository, no memory, no Jira access
- [ ] Every scenario is supported by the committed diff
- [ ] English, sequential `TC-0X`, one scenario per case
