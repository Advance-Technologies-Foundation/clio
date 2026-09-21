# Manual test prompt — Sub-process element: selection and parameter sync (ENG-92707)

Every case below is a task written the way a person asks for it: what they want the process to do, not
which element to place or which argument to pass. That is deliberate. The unit suite already proves the
element builds; what nothing below the stand can prove is whether an agent asked in business language
gets there, whether the DESIGNER accepts what was written, and whether the values actually cross at run
time - where a mismatch is skipped with no exception and no log line.

Three cases must name an internal to be testable at all. Each is marked **adversarial** and says why.

Hand the whole of the next section to an AI session with browser access to a stand.

---

## Prompt

````
You are running a manual test pass on a live Creatio stand. Report PASS or FAIL per case with the
evidence you actually saw - a screenshot, or the exact text - never a restatement of the expectation.

## Before you start

PRECONDITION THAT GATES EVERYTHING: the stand's `CrtProcessBuilder` must be **1.6.3.0 or newer**. Check
with `clio list-packages -e <env>` and STOP if it is lower. On an older package every case below is
refused with "Element type 'subProcess' is not supported yet", which is the correct answer on an old
package and proves nothing about this feature. Say so and stop rather than reporting failures.

Each case states its expected result TWICE, separately, and both halves have to be checked:

* **Design time** - what a human sees when the process is opened in the process designer. Open it
  deliberately; do not infer it from a successful write.
* **Runtime** - what happens when the process actually runs.

Each case also declares the observation levels it REACHES: **Stored** (the metadata was written),
**Design time** (the designer opened it), **Runtime** (it ran). A case that passes at Stored level with
no designer ever opened has NOT proved the designer accepts it - say where you stopped.

WHEN READING A PROCESS BACK: on a versioned process `describe-business-process --process-name X`
resolves to the family ROOT, which is not what the designer edits or the runtime executes. Read
`version` / `isActiveVersion` / `activeVersionName` in every response and address the active schema by
`--process-uid` when they disagree.

WHEN CHECKING RUNTIME: `SysProcessLog` carries the root row per process INSTANCE, and a call produces
TWO instances - the caller and the callee. That pair is the first thing to look for. The per-element
trace is `SysProcessElementLog`. A root row with no `CompleteDate` on a process parked at a human step
is normal and is not a fault.

Work in the `Custom` package. Name every process you create with a `UsrTc92707` prefix so the pass can
be cleaned up afterwards.

---

### TC-01 Call an existing process and pass it the order id

**Preconditions:**
A process already exists that takes an order id and decides whether the order is approved. Build one
first if the stand has none: it needs an ordinary manual start, an input for the order id and an output
carrying the decision.

**Steps:**
1. Ask for this, in these words: *"Build a process that, when a record is created, calls the existing
   order-approval process and passes it the order id."*
2. Open the resulting process in the process designer.

**Expected result - design time:**
The diagram shows a call-activity shape (a rectangle with a small + marker) between the trigger and the
end. Opening its properties card shows the called process selected by name, and a parameter grid listing
the called process's own parameters - the order id among them - which nobody typed into the request.
The element's caption reads as the action, not as the process code.

**Expected result - runtime:**
Creating a record starts the caller; `SysProcessLog` shows TWO instances, the caller and the called
process, the callee's started after the caller's. The callee receives the order id: its own instance
carries the value the caller sent, not an empty parameter.

**Reaches:** Stored, Design time, Runtime.

---

### TC-02 Read the decision back out of the called process

**Preconditions:** TC-01's pair.

**Steps:**
1. Ask: *"After the order-approval process finishes, put its decision into a field on the record."*
2. Open the caller in the designer and look at what feeds the update step.
3. Run it.

**Expected result - design time:**
The update step's value is bound to the call activity's output parameter, shown by the parameter's
caption rather than as a raw expression. The call activity's card lists that output with a direction
that marks it as coming OUT of the called process.

**Expected result - runtime:**
The record's field carries the decision the callee produced. If the callee wrote nothing, the field is
empty rather than carrying a stale or default value - and that is a FAIL only if the callee did produce
a decision.

**Reaches:** Stored, Design time, Runtime.

---

### TC-03 The called process gains a parameter, and the caller has to pick it up

**Preconditions:** TC-01's pair, saved and working.

**Steps:**
1. Ask: *"Add a comment input to the order-approval process."*
2. Ask, without mentioning the caller's internals: *"Bring the process that calls it up to date."*
3. Open the caller in the designer.

**Expected result - design time:**
The call activity's parameter grid now lists the new comment input beside the ones that were there
before. Nothing else on the card changed: the called process is still the same one, and the value
already mapped into the order id is still there.

**Expected result - runtime:**
The caller still runs and still delivers the order id. The comment arrives at the callee only once
something is mapped into it; until then the callee runs with it empty, and NOTHING anywhere reports
that - which is the point of the case.

**Reaches:** Stored, Design time, Runtime.

---

### TC-04 The called process loses a parameter, and the caller must be told

**Preconditions:** TC-03's state.

**Steps:**
1. Ask: *"Remove the comment input from the order-approval process again."*
2. Ask: *"Bring the caller up to date."*
3. Read the response the agent got, then open the caller in the designer.

**Expected result - design time:**
The comment input is gone from the call activity's parameter grid. The response to step 2 said so IN
WORDS - it named the removed parameter and said anything mapped into it is now dangling. A silent
success here is a FAIL even though the metadata is correct, because a caller who mapped a value into
that parameter has no other way to learn it went.

**Expected result - runtime:**
The caller runs. A value that was mapped into the removed parameter is not delivered, and no error is
raised anywhere - the write is simply skipped. Confirm by looking at the callee's instance parameters.

**Reaches:** Stored, Design time, Runtime.

---

### TC-05 Asking for the same refresh twice changes nothing the second time

**Preconditions:** TC-04's state.

**Steps:**
1. Ask: *"Bring the caller up to date"* again, immediately.
2. Compare the response with the one from TC-04 step 2.

**Expected result - design time:**
The designer shows exactly what it showed after TC-04: the same parameters, the same values, the same
called process. Nothing moved.

**Expected result - runtime:**
Unchanged from TC-04.

**Reaches:** Stored, Design time. Does NOT need a run - if nothing changed, nothing can behave
differently.

---

### TC-06 Point the call at a different process

**Preconditions:** A caller whose call activity's output is used by NOTHING else, plus a second callable
process.

**Steps:**
1. Ask: *"Make that step call the credit-check process instead."*
2. Open the caller in the designer.

**Expected result - design time:**
The card shows the new process selected, and its parameter grid now lists the NEW process's parameters.
The previous process's parameters are gone. The response to step 1 said which parameters went.

**Expected result - runtime:**
The credit-check process starts instead of the previous one, and the previous one does not run at all.

**Reaches:** Stored, Design time, Runtime.

---

### TC-07 Refuse to point the call at a process whose results something else is reading

**Preconditions:** TC-02's state - the call activity's output feeds a later step.

**Steps:**
1. Ask: *"Make that step call the credit-check process instead."*
2. Whatever the answer, open the caller in the designer and then try to START it.

**Expected result - design time:**
The request was REFUSED, and the refusal named the step that reads from this one. The designer shows the
process exactly as it was: the original called process still selected, its parameters still there, the
later step still bound.

**Expected result - runtime:**
The process still starts and still runs end to end. This is the half that matters: had the change gone
through, the process would have saved without an error and then refused to START, with the failure
blamed on the process rather than on the edit.

**Reaches:** Stored, Design time, Runtime.

---

### TC-08 Refuse a process that calls itself

**Preconditions:** Any process.

**Steps:**
1. Ask: *"Add a step to this process that calls this same process again."*

**Expected result - design time:**
The request was REFUSED and the refusal named the process. Nothing was added: opening the designer shows
the diagram unchanged.

**Expected result - runtime:**
Not reached - nothing was written. Note what the failure would have looked like had it been allowed: the
element saves, carries no parameters at all, and nothing complains.

**Reaches:** Stored (as a non-write), Design time.

---

### TC-09 Refuse to edit a step that runs the called process once per record

**Adversarial** - this case cannot be written in business language, because the state it needs is one a
person cannot ask for through this contract at all. It has to be arranged in the designer by hand.

**Preconditions:** In the designer, on a copy of a caller, map a COLLECTION into the call activity so it
runs once per item. Save.

**Steps:**
1. Ask: *"Change that step to call the credit-check process."*
2. Ask: *"Read this process back and tell me what that step does."*

**Expected result - design time:**
Step 1 was REFUSED and the refusal said the step runs once per item of a collection and has to be edited
in the designer. The designer shows the step unchanged - still multi-instance, still calling the original
process, its collections and counters intact. Step 2's answer said the step is multi-instance BEFORE
being asked to change it, so the refusal in step 1 was predictable rather than surprising.

**Expected result - runtime:**
The process still runs once per item, as it did before.

**Reaches:** Stored, Design time, Runtime.

---

### TC-10 A renamed parameter on the called process delivers nothing, silently

**Adversarial** - the case tests the absence of an error, so it has to name the parameter being renamed.

**Preconditions:** TC-01's pair, running and delivering the order id.

**Steps:**
1. In the designer, RENAME the called process's order-id input. Change nothing on the caller.
2. Run the caller.
3. Read the callee's instance parameters.
4. Now ask: *"Bring the caller up to date"*, and run it again.

**Expected result - design time:**
After step 1 the caller's card still shows the OLD parameter name - nothing told anybody. After step 4
the card shows the NEW name, the value mapped into it is still there, and the response to step 4 said
the parameter was renamed and that the mapping followed it.

**Expected result - runtime:**
After step 2 the callee starts and its renamed input is EMPTY. There is no exception, no warning, and
nothing in `SysProcessLog` or `SysProcessElementLog` marks it - the caller reports success. That silence
is the whole business case for the refresh. After step 4 the value arrives again.

**Reaches:** Stored, Design time, Runtime.

---

### TC-11 Refuse to write a value onto something the called process produces

**Adversarial** - it names a parameter's direction, which is an internal.

**Preconditions:** TC-01's pair.

**Steps:**
1. Ask: *"Set the approval decision on that call step to Yes before it runs."*

**Expected result - design time:**
REFUSED, and the refusal said the parameter is an output of the called process and named its direction,
and that the way to use it is to read FROM it. The designer shows the parameter still carrying no value.

**Expected result - runtime:**
Not reached - nothing was written. Had it been accepted, the platform would have stored the value and
then cleared it on the next time anybody opened the process, with no error at any point.

**Reaches:** Stored (as a non-write), Design time.

---

### TC-12 Call a process that cannot be entered by a call

**Preconditions:** A process that starts ONLY on a record signal.

**Steps:**
1. Ask: *"Add a step that calls the process which runs when a contact is created."*

**Expected result - design time:**
REFUSED, and the refusal said the process has no simple manual start and therefore cannot be called.
Nothing was added.

**Expected result - runtime:**
Not reached.

**Reaches:** Stored (as a non-write), Design time.

---

### TC-13 Two processes share a display name

**Preconditions:** Two callable processes whose CAPTIONS are identical and whose codes differ.

**Steps:**
1. Ask: *"Add a step that calls the 'Send notification' process."*

**Expected result - design time:**
REFUSED, and the refusal listed both candidate processes by code so the next request can name one.
Nothing was added, and in particular NEITHER of the two was picked.

**Expected result - runtime:**
Not reached. Note what silently picking the first one would cost: the wrong process would run and no
error would appear at any point.

**Reaches:** Stored (as a non-write), Design time.

---

## Reporting

For each case report, in this order: PASS/FAIL, the observation levels you actually reached, the
evidence, and - for any case you could not run - what stopped you. A case reported PASS at Stored level
with the designer never opened is reported as "Stored only", not as PASS.
````
