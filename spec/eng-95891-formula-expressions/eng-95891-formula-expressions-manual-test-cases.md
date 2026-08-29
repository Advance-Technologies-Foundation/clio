# ENG-95891 — Manual test cases

Written as **business assignments given to an AI agent** — the way the feature is actually used — rather
than as tool calls. The tester gives the agent the assignment verbatim, then checks the process in the
designer and, where applicable, runs it. Behaviour below was verified on a stand carrying
`CrtProcessBuilder 1.4.0.3` or newer.

**Conditions.** A Creatio environment registered in clio with `CrtProcessBuilder 1.4.0.3` or newer
installed (`install-process-builder`), an agent with the clio MCP server connected, and a writable
`Custom` package. Unless a case says otherwise, the agent is given **only the sentence in quotes** — no
tool names, no JSON. That is the point: these cases test whether the shipped guidance is enough for an
agent to get it right unaided.

**Two groups.** `TC-01`…`TC-10` cover branch conditions and the refusal set. `TC-11`…`TC-18` cover
formulas in **parameters**, which is the other use site and the one reachable today without gateways
(ENG-91853). The vocabulary they exercise is defined in
[eng-95891-formula-expressions-supported-vocabulary.md](eng-95891-formula-expressions-supported-vocabulary.md).

---

## Group 1 — branch conditions

### `TC-01` A branch condition is authored, drawn and taken

**Preconditions:** A process with a task followed by two end events (the agent may create it in the same
session).

**Steps:**
1. Give the agent: *"In the process `<name>`, make the branch that leads to `Approved` run only when the
   process parameter `Amount` is greater than 100, and the other branch run otherwise."*
2. Open the process in the Process designer.
3. Run the process twice — once with `Amount = 500`, once with `Amount = 10`.

**Expected result:**
* In the designer: the two outgoing connectors are drawn as **conditional flows**; **no gateway element is
  added to the diagram** — the platform synthesizes it at generation time.
* In the designer: the process opens, saves and validates without errors.
* At runtime: with `Amount = 500` the `Approved` branch executes; with `Amount = 10` the other one does.
  Confirm in **Process log → element log** that only the expected end event ran.
* The agent reports back the condition text it wrote.

---

### `TC-02` Branch order decides the winner when two conditions overlap

**Preconditions:** As TC-01. This case exists because nothing in the process metadata records branch
priority.

**Steps:**
1. Give the agent: *"Add two branches after the task: one that runs when `Amount` is over 100, and one
   that runs when `Amount` is over 1000. Tell me which one wins when both are true."*
2. Run the process with `Amount = 5000`.

**Expected result:**
* At runtime: the branch whose flow was added **first** executes; the second does not, even though both
  conditions are true.
* The agent's answer states that precedence is the order the branches were added, not their specificity.
* Failing that last point is a **documentation defect worth reporting**, even when the runtime behaviour
  is correct — a user who does not know this will build the wrong process.

---

### `TC-03` A formula that cannot produce the target type is refused

**Preconditions:** A process with an **Integer** process parameter named `Amount`.

**Steps:**
1. Give the agent: *"Set the default value of `Amount` to the formula `1.5`."*
2. Read the agent's response.
3. Open the process in the designer.

**Expected result:**
* The operation is **refused**. The message names the target (`Amount`) and the original expression
  (`1.5`), and says the result cannot be used as `Int32`.
* In the designer: the parameter is **unchanged** — no half-applied value, no Script source.
* The same assignment with `1 + 1` instead of `1.5` **succeeds** (integer arithmetic fits an Integer
  parameter).
* Note for the tester: `1.5` into a **Float** parameter is legitimate and must succeed — a Float
  parameter's type is decimal.

---

### `TC-04` A formula referencing a parameter that does not exist is refused, naming it

**Preconditions:** Any process created by the agent.

**Steps:**
1. Give the agent: *"Set the branch condition to compare a process parameter called `Total` — but do not
   create that parameter."*
2. Read the response.

**Expected result:**
* The operation is **refused before anything is written**.
* The message **names the offending reference**, so the agent can correct it without guessing.
* In the designer: the flow is unchanged — still a plain sequence flow with no condition.

---

### `TC-05` A parameter used by a branch condition cannot be deleted

**Preconditions:** A process where a branch condition references the process parameter `Amount` (TC-01
leaves one).

**Steps:**
1. Give the agent: *"Delete the process parameter `Amount`."*
2. Read the response.
3. Open the process in the designer.

**Expected result:**
* The deletion is **refused**, and the message **names the flow** whose condition still uses the parameter
  — for example *"still used by condition on flow 'SequenceFlow_...'"*.
* The message is readable. A raw platform blob such as `Internal error: {ErrorType:2,...}` is a **defect**
  — it means the toolkit's own guard missed the reference and the platform caught it instead.
* In the designer: the parameter is still present and the condition still shows.

---

### `TC-06` An empty condition is refused rather than silently meaning "always"

**Preconditions:** A process with a conditional branch.

**Steps:**
1. Give the agent: *"Remove the condition from the branch to `Approved`, so it has no condition any more."*
2. Read the response.

**Expected result:**
* The agent does **not** store an empty condition. Either it refuses and explains that an empty condition
  is stored as `true` (an always-taken branch), or it removes the flow and adds a plain one.
* At runtime: no branch silently becomes always-taken.
* A success message combined with a still-conditional, always-firing branch is a **defect**.

---

### `TC-07` A condition that is not a yes/no answer is refused

**Preconditions:** Any process with a branch.

**Steps:**
1. Give the agent: *"Set the branch condition to `1 + 1`."*
2. Read the response.

**Expected result:**
* The operation is **refused**, saying the result cannot be used as `Boolean`.
* This must be refused even though older Creatio engines would have coerced a number to true/false — the
  interpreted engine does not.

---

### `TC-08` A branch off a single Perform task: the condition works but the designer cannot show it

**Preconditions:** A process shaped `start → Perform task → two end events`, with a formula condition on
each branch. **This is a known platform behaviour, not a bug to file** — the case exists so QA recognises
it.

**Steps:**
1. Give the agent: *"After the Perform task, branch to `Approved` when `Amount` is over 100, otherwise to
   `Rejected`."*
2. Open the process in the designer and open the properties of the `Approved` connector.
3. Save the process from the designer.
4. Ask the agent: *"Read the process back and tell me the branch conditions."*

**Expected result:**
* In the designer: the connector's properties show the **results editor** ("What is the result of an
  element ...?" with the task's results listed), **not** a formula field. The formula is not visible or
  editable there.
* In the designer: saving raises *"Required fields of some elements are not filled in…"* naming that
  connector. Saving anyway is allowed.
* After the save, the agent's read-back still returns **both conditions unchanged** — the designer does
  **not** erase them.
* At runtime: the branches still evaluate as authored.
* If a condition is **lost** after the designer save, that is a regression and must be reported.

---

### `TC-09` An environment with an older package is told to update, not left half-working

**Preconditions:** An environment carrying a `CrtProcessBuilder` older than 1.4.0.3.

**Steps:**
1. Give the agent: *"Set a branch condition in process `<name>` on that environment."*
2. Read the response.

**Expected result:**
* The operation is **refused up front**, and the message names both versions and points at
  `install-process-builder`.
* The formula is **not** stored unchecked. An older server has no validation, so silently accepting here
  is the failure this gate exists to prevent.
* After running `install-process-builder`, the same assignment succeeds.

---

### `TC-10` An unfamiliar macro is stored and reported, not rejected

**Preconditions:** Any process with a branch.

**Steps:**
1. Give the agent: *"Set the branch condition to `[#Wat.Something#] != null`."*
2. Read the response, including any warnings.

**Expected result:**
* The operation **succeeds** — an unrecognised macro family must not be refused, or existing Creatio
  processes could not be edited.
* A **warning** is reported saying the macro was stored unchanged and was **not** checked.
* Silence is a defect: the caller must not believe the formula was validated when it was not.
* Many unknown macros in one expression produce a **short, grouped** warning list, not one warning per
  macro.

---

## Group 2 — formulas in parameters

These need no gateway, so they are runnable today. Each expects the agent to pick the right function from
the guided set without being told its name — that is what makes them a test of the guidance rather than of
the validator.

### `TC-11` The guided function set is reachable by description alone

**Preconditions:** A process with a **Float** parameter `Total` and a Float parameter `Price`.

**Steps:**
1. Give the agent, one at a time: *"Set `Total` to the price rounded up to the nearest whole number."* →
   *"…rounded down."* → *"…rounded to the nearest."* → *"…to the absolute value of the price."*
2. After each, read the process back and note the stored expression.

**Expected result:**
* Each succeeds, over a reference to `Price`.
* The C# spelling is **acceptable**: `Math.Ceiling(...)` is stored and the designer renders it as
  `RoundUp(...)`. Observed on a stand — the conversion runs in both directions, so writing the C# name is
  not by itself a defect. What matters is that the expression **references the parameter**, not that it
  uses the designer's spelling.
* **Known blocker (2026-08-29).** No agent has yet managed the reference. Five plausible spellings were
  tried against a live stand and every one was refused: `Math.Ceiling(Price)`, `Math.Ceiling([Price])`,
  `[#Price#]`, a bare `Price`, and `[#Process parameters.Price#]`. Only a literal (`Math.Ceiling(1.5)`)
  was accepted. The working form is the full meta-path
  `[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{guid}]#]`, which nobody guesses. **Until the
  guidance carries it, this case fails on the reference and everything after it is untestable.**

---

### `TC-12` Aggregate functions over several arguments

**Preconditions:** A process with Float parameters `A`, `B`, `C` and `Result`.

**Steps:**
1. Give the agent: *"Set `Result` to the largest of `A`, `B` and `C`."*
2. Repeat with *"the smallest"*, then *"the average"*, then *"the remainder of dividing `A` by `B`"*.

**Expected result:**
* Stored expressions are `Maximum(...)`, `Minimum(...)`, `Average(...)`, `RemainderAfterDivision(A, B)`.
* Each references the parameters by macro, not by bare name — a bare `A` does not resolve at runtime.
* A refusal here means the guidance does not carry the aggregate names; report it.

---

### `TC-13` A formula reads another element's output parameter

**Preconditions:** A process with a **Read data** element that reads one Contact, followed by a task, and
a Float process parameter `Total`.

**Steps:**
1. Give the agent: *"Set `Total` from the record the Read data element found — take its `Age` and add 1."*
2. Read the process back.

**Expected result:**
* The stored expression carries an **element output parameter** meta-path
  (`[#[Element:{…}].[Parameter:{…}]#]`), not a process-parameter one.
* The agent reports which element it read from.
* The process saves and validates in the designer.

---

### `TC-14` System variable and system setting references

**Preconditions:** A process with a **Date/Time** parameter `Started` and a Float parameter `Limit`.

**Steps:**
1. Give the agent: *"Set `Started` to the current date and time."*
2. Give the agent: *"Set `Limit` from the system setting whose code is `MaxFileSize`."*

**Expected result:**
* `Started` stores a `[#SysVariable.CurrentDateTime#]` reference — **not** a C# `DateTime.Now`.
* `Limit` stores a `[#SysSettings.MaxFileSize<Integer>#]`-shaped reference.
* Note for the tester: the legacy form `[#SysSettings.MaxFileSize#]` (no type) also exists in the field
  and must round-trip; the agent is expected to write the modern one.

---

### `TC-15` A lookup value inside a formula

**Preconditions:** A process with a Lookup parameter pointing at `ActivityCategory`.

**Steps:**
1. Give the agent: *"Set the parameter to the `Call` activity category."*

**Expected result:**
* Stored as `[#Lookup.<schema>.<record>#]`, with both halves resolving.
* The agent does **not** invent a raw Guid constant: an arbitrary Guid written into a lookup column
  passes every type check and then reads back as a value nobody can see.

---

### `TC-16` Date functions

**Preconditions:** A process with a Date/Time parameter `Due` and Integer parameters `D`, `M`, `W`.

**Steps:**
1. Give the agent: *"From `Due`, set `D` to the day, `M` to the month and `W` to the day of the week."*

**Expected result:**
* Stored expressions are `Day(...)`, `Month(...)`, `DayOfWeek(...)` over a reference to `Due` — the
  platform spells them **without** a `Get` prefix.
* Each result fits its Integer target; a type refusal here is a defect in the guidance's type advice.

---

### `TC-17` An unfamiliar macro in a parameter is stored with a warning

**Preconditions:** A process with a Text parameter `Note`.

**Steps:**
1. Give the agent: *"Set `Note` to the formula `[#Wat.Something#]`."*
2. Read the response, including warnings.

**Expected result:**
* Same contract as TC-10 but at the **other use site**: the mapping **succeeds**, and a warning says the
  macro was stored unchecked.
* The parameter's source reads back as `Script`, and its value is the expression **verbatim** — the
  platform, not the toolkit, decides what it means.

---

### `TC-18` A parameter used by a mapping formula cannot be deleted

**Preconditions:** A process where `Total` is set from a formula referencing `Amount` (TC-11 or TC-12
leaves one).

**Steps:**
1. Give the agent: *"Delete the process parameter `Amount`."*
2. Read the response.

**Expected result:**
* The deletion is **refused**, and the message names the **mapping** that still uses the parameter.
* This is the mapping-side twin of TC-05; both must be refused, and a difference between them is a defect.
* In the designer: the parameter is still present and the formula still shows.

---

## What these cases deliberately do not cover

* **Designer-only rendering** beyond what TC-08 states. Whether a formula field is editable in a given
  element's property panel is a platform behaviour that changes between releases.
* **Gateways and default flows** — ENG-91853.
* **The four accepted-but-not-advertised macro families** (`BooleanValue`, `PropertyValue`, `ColumnValue`,
  `SamplingColumnValue`). They must round-trip, which unit and E2E tests already assert; an agent is not
  expected to author them.
