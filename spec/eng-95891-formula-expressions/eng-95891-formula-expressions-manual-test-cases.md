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

**Three groups.** `TC-01`…`TC-10` cover branch conditions and the refusal set. `TC-11`…`TC-18` cover
formulas in **parameters**, which is the other use site and the one reachable today without gateways
(ENG-91853). `TC-19`–`TC-20` are Group 3: they carry a formula past storage into the RUNTIME and read
the result off the Activity card. The vocabulary they exercise is defined in
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

**Observed 2026-08-29 — the refusal half passes, the acceptance half exposes a guidance defect.**
The `1.5` refusal is exactly as specified: the message names `Amount`, quotes `1.5` **as written** (not the
converted `1.5m`), says `Cannot convert type "Decimal" to "Int32"`, and the designer shows the parameter
still unset. But on the `1 + 1` half, two agents reading the same guidance took **opposite routes**:

* one sent `addMapping` with `targetProcessParameter` + `expression` — the route the server engages with,
  which is how the `1.5` refusal was obtained at all;
* the other read the guidance line that a parameter's `value` is *"a literal constant, not a formula"*,
  concluded no mechanism exists for an arithmetic default on an Integer parameter, **evaluated `1 + 1`
  itself and stored the constant `2`**.

The second is the dangerous one, and it is not the agent being careless — it disclosed the substitution in
its answer. The guidance describes what `value` cannot hold without saying that `addMapping` +
`expression` is how a COMPUTED default is authored, so a reasonable reader concludes the feature is
absent. The result reads as success while the process holds a frozen constant where the author asked for
an expression; nothing recomputes. **Treat a stored constant here as a FAILURE of this case**, and read
the two routes as the guidance defect to fix.

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
1. Give the agent: *"Set the branch condition to compare `[#[PropertyValue:Caption]#]` against an empty
   text."*
2. Read the response, including any warnings.

**Expected result:**
* The operation **succeeds** — an unrecognised macro family must not be refused, or existing Creatio
  processes could not be edited.
* A **warning** is reported saying the macro was stored unchanged and was **not** checked.
* Silence is a defect: the caller must not believe the formula was validated when it was not.
* Many unknown macros in one expression produce a **short, grouped** warning list, not one warning per
  macro.

**Use a REAL family, not invented text.** `[#Wat.Something#]`, which earlier drafts of this case used, is
not a macro family at all — the parser reads it as an expression beginning with `Wat` and the platform's
own pre-save validation refuses it (*"Expression expected (at index 0)"*), which is correct behaviour and
tests nothing. The families that must round-trip are the four real-but-unadvertised ones listed in the
vocabulary spec; `[#[PropertyValue:Caption]#]` is the handiest, and Creatio itself ships it as the default
"Process instance caption".

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
* `Started` stores a `[#SysVariable.CurrentDateTime#]` reference — **not** a C# `DateTime.Now`. This half
  is the point of the case and it passes.
* `Limit` stores a `SysSettings` reference. **Either spelling counts** — `[#SysSettings.MaxFileSize#]` or
  `[#SysSettings.MaxFileSize<Integer>#]`. Both round-trip, the family is accepted unchecked either way
  (its value type cannot be read at design time), and both appear in shipped schemas.
* Earlier wording here required the typed form. Nothing supports that: a rerun produced the untyped one,
  another section of the guide shows it, and no failure mode was ever demonstrated for it. Do not fail the
  case on the spelling.

---

### `TC-15` A lookup value inside a formula

**Preconditions:** A process with a Lookup parameter pointing at `ActivityCategory`.

**Steps:**
1. Give the agent: *"Set the parameter to the `Call` activity category."*

**Expected result:**
* Stored as a **bare record Guid in `value`** (a `ConstValue`) — that is the route the toolkit prefers from
  `CrtProcessBuilder` 1.3.1.1, and the encoding an `ActivityUserTask` category REQUIRES.
* The `[#Lookup.<schema>.<record>#]` macro is correct only for a pre-1.3.1.1 package that rejects the bare
  Guid. Seeing the macro here is not a pass; seeing the bare Guid is.
* What this case really checks is that the agent **resolves the right record** — `ActivityCategory` has more
  than one row named "Call", differing by `ActivityType`. It must say which it chose and why.

**This case was inverted until 2026-08-30, and the correction came from the runs.** It demanded the macro
and called the bare Guid a trap. Two independent agents stored the bare Guid, cited the version rationale
back, and were right: the guide says so two hundred lines above the macro table. A case written from a
specification can be wrong about the product — this one was, and so was the guidance "fix" made to satisfy
it (reverted in library 1.13.55).

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
1. Give the agent: *"Set `Note` to the formula `[#[PropertyValue:Caption]#]`."*
2. Read the response, including warnings.
3. Open the process in the designer and look at the parameter.

**Expected result:**
* Same contract as TC-10 but at the **other use site**: the mapping **succeeds**, and a warning says the
  macro was stored unchecked.
* The parameter's source reads back as `Script`, and its value is the expression **verbatim** — the
  platform, not the toolkit, decides what it means.
* In the designer: the parameter shows the macro RESOLVED to its human-readable form — `[#Process name#]`
  for this one. That is the proof the round-trip is real and not merely tolerated.

**Verified 2026-08-29.** The warning reads: *"The 'expression' mapping for target 'Note' uses the
unrecognised macro family '[PropertyValue:Caption]'. It is stored unchanged and was NOT checked."* — and
the designer renders `[#Process name#]`. See TC-10 on why the input must be a real family: an invented
`[#Wat.Something#]` is refused by the platform's own validation, and that refusal is correct.

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

## Round-2 results — 2026-08-30, against the corrected guidance

The reference rule reached the agent for the first time (the guidance fix, served through `get-guidance`).
Group 2 became runnable, and running it found three defects in the shipped feature that no unit or E2E
test had caught. Six agent sessions, one per case, each fresh and with no memory.

| Case | Result | What it showed |
|---|---|---|
| `TC-11` | pass | `Math.Ceiling`/`Math.Abs` stored; designer renders `RoundUp`/`Module` |
| `TC-12` | pass | designer renders `Maximum` / `Minimum` / `Average` / `RemainderAfterDivision` |
| `TC-13` | **FAIL** | the three-segment element-column reference is mishandled — see below |
| `TC-14` | pass | `[#SysVariable.CurrentDateTime#]` correct; either `SysSettings` spelling is valid — the case was wrong to require one |
| `TC-15` | pass | a bare record Guid as `ConstValue` — which is the PREFERRED route; the case was inverted |
| `TC-16` | pass | `DateTimeUtilities.Day/Month/DayOfWeek`, no `Get` prefix |
| `TC-18` | **FAIL** | the delete guard misses MAPPING references; the platform catches it and leaks a blob |

**TC-13 — the validator drops the column half.** An element-column reference
`[#[Element:{e}].[Parameter:{p}].[EntityColumn:{c}]#] + 1` is refused with *"Formula value error: Invalid
Operation (at index 15)"*. The reference is well formed and the corpus carries 318 of them, so the refusal
is wrong. Cause: `ProcessFormulaValidator.SubstituteParameterReferences` replaces the WHOLE `[# … #]` token
with one placeholder typed from the PARAMETER (`ResultEntity`, an object) — the `[EntityColumn:…]` segment
is swallowed, so the engine sees `object + 1`. The message names neither the column nor the real problem.
Fix: type the placeholder from the referenced COLUMN when the third segment is present.

**TC-15 — the lookup macro is unreachable in practice.** Asked for "the Call activity category", the agent
stored a bare Guid as a `ConstValue` rather than `[#Lookup.{schemaUId}.{recordId}#]`. It disclosed the
substitution, so this is not carelessness: the macro needs TWO Guids the agent has no documented way to
resolve, while the mapping guidance separately says a Guid into a lookup target is allowed. The value type-
checks, saves, and then reads back as a record nobody can see. The guidance must either give the resolution
route or say the macro is not authorable.

**TC-18 — the delete guard is half a guard.** Removing a parameter still used by a mapping formula is
refused, but by the PLATFORM, not by the toolkit:
`Process validation failed: Invalid value for the parameter "Largest". Internal error:
"{ErrorType:2,ErrorData:{ParameterUId:"585de4ad-…"}}"`. `IProcessParameterService.FindParameterUsages` scans
flow CONDITIONS (added for this ticket) but not mapping EXPRESSIONS, so the case's own defect criterion is
met. The caller gets a UId instead of the parameter name and no route to a fix.

**TC-14's caveat is a guidance defect, not an agent error.** The macro table shows
`[#SysSettings.Code#]` as the example and mentions the typed form only as a parenthetical, so the agent
wrote what it was shown. The primary example should be the modern typed form.

---

---

## Group 3 — a formula reaching the runtime

`TC-01`...`TC-18` stop at what is STORED. These two carry a formula all the way through: authored, saved,
evaluated by the process engine, and read off the Activity card a user actually sees. They exist because
the branch cases cannot do it - a branch condition off a Perform task cannot be observed until somebody
closes the task, while an Activity carries its computed values the moment it is created.

### `TC-19` A computed subject reaches the Activity card

**Preconditions:** none beyond a writable `Custom` package.

**Steps:**
1. Give the agent: *"In `<process>`, make the Perform task's subject read `Order total with VAT: <the
   amount plus 20 percent>` - computed from the `Amount` parameter, not typed in as a constant. Then
   start the process."* Precondition for the agent to build: a process shaped start -> Perform task ->
   end, with an Integer parameter `Amount` defaulting to `500`.
2. Open the Activity from the **Business process tasks** panel on the right of the shell.

**Expected result:**
* `describe-business-process` reports the binding with `"source": "Script"` - a live formula. A `value`
  source holding `600` is a FAILURE even though the number is right: the agent evaluated it instead of
  the engine. Say "computed, not a constant" in the assignment, or this is what you get (see `TC-03`).
* The Activity's subject reads **`Order total with VAT: 600`**, on the card, not merely in a read-back.
* The task is left OPEN. Closing it lets the process move on and there is nothing left to inspect.

### `TC-20` A typed formula, and the timezone it is evaluated in

**Preconditions:** as `TC-19`.

**Steps:**
1. Give the agent: *"Set the Perform task's due date to the end of the current working day - computed,
   not a constant - and put the day of the week into the task subject. Then start the process."*
2. Open the Activity card and read **Start** and **Due**.

**Expected result:**
* The subject carries the weekday as TEXT. `DayOfWeek` is an enum, so the expression needs `.ToString()`;
  an agent that omits it is refused, which is correct.
* There is no due-date parameter on a Perform task. Due date is `StartDate + Duration`, so the only route
  is a computed `Duration` (with `DurationPeriod`). An agent that reports "no due date parameter exists"
  has read the platform correctly, not failed.
* **The formula is evaluated in the SERVER's zone and the card renders in the PROFILE's zone.** Check the
  arithmetic rather than the wall clock: on the run of 2026-08-30 the card showed Start `11:31 PM` and Due
  `2:59 AM`, i.e. 208 minutes, which the expression `(18:00 - now)` yields only if the engine saw `14:32`
  - nine hours from what the card displayed. A rule phrased "until 18:00" therefore means 18:00 SERVER
  time, silently. Any case that anchors on "today", "end of day" or "now" must state which zone it means.

---

## Round-3 results — 2026-08-30, the branch group and the runtime group

`TC-01`...`TC-10` had never been run, `TC-03` and `TC-10` excepted. This round ran the rest of Group 1
plus the two new runtime cases. One agent session per case, fresh, memory disabled, driven through the
clio MCP server against a stand carrying `CrtProcessBuilder 1.4.0.8` and a locally served guidance
library at `1.13.55`.

| Case | Result | What it showed |
|---|---|---|
| `TC-01` | pass | conditional flows drawn, NO gateway on the diagram, designer save clean |
| `TC-02` | **FAIL** | no condition referencing a parameter could be stored - see below |
| `TC-04` | pass | refused before writing, names `Total`, echoes the expression, flow untouched |
| `TC-05` | pass | refused, names BOTH flows, readable - not the raw platform blob the case warns about |
| `TC-06` | pass | empty condition refused and the reason given; flow left plain, not always-true |
| `TC-07` | pass | `1 + 1` refused as `Cannot convert type "Int32" to "Boolean"` |
| `TC-08` | pass | condition stored and read back unchanged off a Perform task |
| `TC-09` | **not run** | needs a package older than 1.4.0.3; the stand is at 1.4.0.8 and the downgrade guard refuses |
| `TC-19` | pass | `Order total with VAT: 600` on the Activity card, from a live `Script` binding |
| `TC-20` | pass | weekday in the subject; exposed the server/profile timezone split above |

**`TC-02` failed on the guidance, not on the feature - and the article's own example is why.** The agent
tried `Amount > 1000`, `[#Amount#] > 1000`, `[Amount] > 1000`, `[#Parameter.Amount#] > 1000` and a bare
`TestFlag`, and never tried the form that works. It was not guessing: the "Conditional flows and branch
conditions" section of `process-modeling` illustrates precedence with **`Amount > 100` and
`Amount > 1000`**, spelled by NAME - while `REFERENCING A PARAMETER`, ninety lines earlier in a different
section, states that a bare name is always refused. The agent copied the example in front of it. It then
concluded, reasonably and wrongly, that the environment's validator disagreed with clio's documentation;
`TC-01` had stored such conditions on the same stand and the same package version minutes earlier.

Every Group-1 agent saw that example. Only the ones that ALSO reached the rule got it right:

| | saw `Amount > 100` | saw the rule | outcome |
|---|---|---|---|
| `TC-01` | yes | yes | correct UId meta-path first try |
| `TC-02` | yes | no | five wrong forms, objective not reached |
| `TC-04` | yes | no | bare name - enough for a case that only needs a refusal |

`TC-08`'s agent found the contradiction unaided and named it in its own report.

**The delivery defect underneath it.** `get-guidance name=process-modeling` returns ~113 KB on ONE line.
It exceeded the tool-result token cap in **every** agent session of every round - 16 of 16 earlier logs and
all 8 of this one - so the result is spilled to a file and each agent greps its own guidance back out.
Across the nine runs that cost 119 of 319 tool calls (37%), against 92 (28%) that acted on a process at
all, and a further 47 (14%) spent in `ToolSearch` loading deferred tool schemas. The
consequence is not merely cost: each agent reads a self-selected FRAGMENT, and `TC-02` shows the outcome
of an operation turning on which fragment that was. The MCP server instructions make this guide mandatory
reading before every operation.

**Smaller things the runs kept repeating.**

* **No tool starts a process.** `TC-19`'s agent reached `ProcessEngineService.svc/RunProcess` through
  `clio call-service`; `TC-20`'s agent looked for a supported route, found none, and stopped and asked.
  Same toolset, same task, opposite outcomes - and every runtime case needs that detour.
* **No tool returns a designer link.** Four agents produced four different invented URL schemes;
  `/0/ProcessModuleEdit.aspx?id=...` answers `HttpException`. The working form is
  `/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<uid>` and it appears nowhere in the guidance.
* **A Perform task's subject is the `Recommendation` parameter**, which becomes `Activity.Title`. Not
  `Subject`, not `Title`. Discovered by the agent; absent from the guidance.
* The stand enforces a `Usr` schema-name prefix, so every process is created twice - once refused, once
  accepted. The refusal is clear and every agent recovered from it unaided.

## What these cases deliberately do not cover

* **Designer-only rendering** beyond what TC-08 states. Whether a formula field is editable in a given
  element's property panel is a platform behaviour that changes between releases.
* **Gateways and default flows** — ENG-91853.
* **The four accepted-but-not-advertised macro families** (`BooleanValue`, `PropertyValue`, `ColumnValue`,
  `SamplingColumnValue`). They must round-trip, which unit and E2E tests already assert; an agent is not
  expected to author them.
