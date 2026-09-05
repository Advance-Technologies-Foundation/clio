# ENG-91853 — manual test prompt

You are testing a Creatio environment through the clio MCP tools. Work only from this prompt.

Environment: `Creatio` — `http://d_krestov_n.tscrm.com:40001` (Supervisor). Any .NET Framework Creatio
reachable through clio will do; the environment must be one where business processes can be created
through the tooling. If the tooling refuses to write somewhere, find somewhere it will — that refusal
is part of what is being tested, so report it and keep going.

Application/section: none. Every process below is standalone and takes its input directly, so no
section has to be configured first. Where a step has to read business data, use the contact and
company records that exist on any Creatio.

For every case below: build what the business asks for, then report what you observe at each level the
case names. Report exactly what you see, including anything that contradicts the expectation. If you
cannot express what the business asked for, say so and say what stopped you — that is a result, not a
failure to complete the task.

Groups:

1. **A single decision point** — one place in a process where the path forks and only one path is
   taken. Covers how rules are attached, how a fallback is expressed, which rule wins when two apply,
   and what happens when none does.
2. **Doing two things at once** — a fork where every path runs, and a point further on that must wait
   for all of them.
3. **Changing a process that already exists** — editing the rules on a process that has already been
   built and may already have run.
4. **The diagram a person reads** — whether a business analyst opening the process can follow it.
5. **Checking a plan before building it** — reviewing a described process for problems before any of
   it is created.
6. **A mistake that must be caught wherever it is expressed** — the same bad shape reaching the tooling
   two different ways, which must be refused both times and for the same reason.

Most cases are observed at three levels — what is **stored** and read back, what is visible at
**design time** when the process is opened, and what happens at **runtime**. Group 5 and TC-05 are the
exception: they observe a plan being checked, which happens before anything is stored, so they stop
short of all three and say so. Do not read a passing check as evidence that the process would work.

---

## Group 1 — a single decision point

## TC-01 — a request is routed by its amount, with one fallback path

Preconditions:

- None. The process takes the request amount as its input.

Business requirement:

- A purchase request arrives with an amount. Requests over 100 must go down the approval path.
  Everything else must go down the fast-track path.
- There must be exactly **one** fallback path, and it must be the path taken whenever no rule matched
  — not a second rule that happens to cover the rest. The business will keep adding rules to this
  decision, and the fallback must keep working without being edited each time.
- The fork must be a single, visible decision point in the diagram, because this is where the business
  wants to see its policy.

Stored — what must be written and read back:

- The approval path carries the rule as it was written; reading the process back returns the same
  expression, not a re-spelled or re-ordered variant of it.
- The fallback path carries no rule at all. It must read back as the fallback, distinguishable from a
  path that simply has no rule on it yet.

Design time — what must be visible when the process is opened in the designer:

- One decision point, with exactly two paths leaving it.
- The fallback path is **visibly marked as the fallback** — a person looking at the diagram can tell
  which path is taken when nothing matched, without opening any settings.
- No stray element, no empty path, and no duplicate of the rule.

Runtime — what must happen when the process runs, and where it is visible:

- Run it with amount **150**: the approval path is taken and the fast-track path is not.
- Run it again with amount **50**: the fast-track path is taken and the approval path is not.
- Both runs complete. The proof is the process log for each run, which must show the decision point and
  then the one path that was actually taken.

## TC-02 — two rules apply to the same request, and the more specific one must win

Preconditions:

- None.

Business requirement:

- The policy grows: requests over 100 go to a manager, and requests over 1000 go to a director.
- A request of 5000 satisfies **both** rules. The business needs it to go to the director.
- The business states its policy in exactly those words and will not accept the rules being rewritten
  into non-overlapping bands (`between 100 and 1000`), because rules are added and removed over time
  and rewriting every neighbouring rule each time is what it is trying to avoid.
- It must be evident from the process itself which rule is considered first. Anyone reading the process
  later has to be able to answer that without running it.

Stored — what must be written and read back:

- Both rules read back as written.
- The order in which the two rules are considered is inspectable in what is read back — reading the
  process must let you say which one is evaluated first.

Runtime — what must happen when the process runs, and where it is visible:

- Amount **5000** → the director path.
- Amount **500** → the manager path.
- Amount **50** → the fallback path.
- The process log for each run shows which path was taken.

## TC-03 — no rule matches and there is no fallback (adversarial)

**Adversarial case — the input is stated verbatim because the reaction is what is under test. Do not
treat this as the shape a process should have.**

Preconditions:

- None.

Business requirement (stated as the input, verbatim):

- Build a process with a decision point whose only outgoing paths carry, verbatim, the rules
  `over 100` and `over 1000`, and **no fallback path at all**.
- Run it with amount **10**, so that neither rule is satisfied.

Runtime — what must happen when the process runs, and where it is visible:

- The run must **not** silently complete, and must **not** silently take one of the two paths.
- A person looking at the process log must be told that the run stopped because nothing matched at
  that decision point, and which decision point it was. Quote the message you actually see.
- Report the state the run is left in — completed, failed, or waiting.

This case stops at runtime behaviour and the message. It is not a judgement about whether the tooling
should have refused to build the process in the first place; report separately if it did refuse, and
what it said.

---

## Group 2 — doing two things at once

## TC-04 — two checks run at the same time and the next step waits for both

Preconditions:

- None. Use the contact and company records that exist on the environment.

Business requirement:

- When a request arrives, two independent checks must run: one looks up the requester's contact
  details, the other looks up their company.
- The two checks **must not wait for each other**. Either may finish first, and the process must not
  depend on which does. The business is explicit that these are independent and must not be chained,
  because a slow company lookup must not hold up the contact lookup.
- The confirmation step that follows **must not start until both checks have finished**.

Design time — what must be visible when the process is opened in the designer:

- A point where the flow splits into the two checks, and a point further on where they come back
  together before the confirmation step.
- Both are visible as points in the diagram; the confirmation step has one path into it, not two.

Runtime — what must happen when the process runs, and where it is visible:

- Run the process. Both checks must appear in the process log as having run.
- The confirmation step must start **after the later of the two checks finished**. The process log
  carries start and finish times for each step — quote them, and show that the confirmation step's
  start is not earlier than either check's finish.
- The process completes.

## TC-05 — a wait-for-all point that can never be reached (adversarial)

**Adversarial case — the shape is stated deliberately because the reaction is what is under test.**

Preconditions:

- None. Nothing needs to be built for this case; it is about reviewing a plan.

Business requirement (stated as the input, verbatim):

- Describe a process in which a decision point sends the flow down **one** of two paths, and both of
  those paths lead to a point that waits for **all** of its incoming paths before continuing.
- Have that plan checked before anything is built.

Expected — what the check must report:

- The check must warn that the wait-for-all point can never continue, because only one of the paths
  feeding it can ever deliver. Quote what it says.
- It must identify which point in the plan is the problem.

This case stops at the check. Do not build the process; a process with this shape does not fail, it
hangs, and that is not worth doing to the environment.

---

## Group 3 — changing a process that already exists

## TC-06 — the threshold changes on a process that has already run

Preconditions:

- The process from **TC-01**, already built and already run at least once.

Business requirement:

- The approval threshold changes from 100 to 250. Change it **on the existing process** — the business
  is not willing to have the process rebuilt, because the previous runs are attached to it and the
  people who read the diagram know its current shape.
- The path must keep its place in the order the rules are considered, and the diagram must be unchanged
  apart from the rule itself.

Stored — what must be written and read back:

- The rule reads back with the new threshold.
- The path is the **same** path — reading the process back shows it in the same position among the
  paths leaving that decision point as it was before the change.

Design time — what must be visible when the process is opened in the designer:

- The diagram is unchanged: same steps, same paths, same positions. Only the rule differs.

Runtime — what must happen when the process runs, and where it is visible:

- Amount **200**, which used to take the approval path, now takes the fallback path.
- Amount **300** takes the approval path.
- Both visible in the process log.

## TC-07 — an existing rule-driven path becomes the fallback

Preconditions:

- A process with a decision point that has two rule-driven paths and one fallback path.

Business requirement:

- The policy is simplified: one of the two rule-driven paths must become the fallback instead — taken
  whenever no other rule matched — and the path that used to be the fallback must now carry a rule.
- Neither path may be redrawn. Both must stay where they are in the diagram and keep their place in the
  order the rules are considered. The business cares about this because the order is its policy.

Stored — what must be written and read back:

- The two paths have swapped roles: the one that was rule-driven now reads back as the fallback, and
  the one that was the fallback now carries a rule.
- Both are still in the same positions among the paths leaving that decision point as before.

Design time — what must be visible when the process is opened in the designer:

- The diagram has the same shape as before. The marking that identifies the fallback has moved to the
  other path, and nothing else has moved.

Runtime — what must happen when the process runs, and where it is visible:

- Pick two amounts that take different paths and confirm in the process log that the routing follows
  the new policy, not the old one.

---

## Group 4 — the diagram a person reads

## TC-08 — a step that repeats until there is nothing left

Preconditions:

- None.

Business requirement:

- A batch of records has to be processed a portion at a time. A step does one portion, and the process
  goes back and does another until there is nothing left, then continues to the finish.
- A business analyst opening this process must be able to follow it.

Design time — what must be visible when the process is opened in the designer:

- The repeat path is visible as a path going back to the earlier step, not merely implied.
- The steps run left to right in the order they happen. **No two steps sit in the same place, and no
  group of steps is stacked into a single column** — a process that folds back on itself must still
  read as a sequence.
- No two step shapes overlap.

**Known platform behaviour — do not file as a defect:** a path that skips ahead over other steps may
have its *connector line* drawn across those steps. Connector routing is not something the tooling
controls. What **would** be a regression, and must be reported: two step **shapes** overlapping each
other, or several steps collapsed into the same column so the sequence cannot be read.

This case stops at design time. It says nothing about whether the repeat logic is correct — that is
TC-01's and TC-02's business, not this one's.

## TC-09 — the order the rules are considered is readable from the diagram

Preconditions:

- None.

Business requirement:

- A decision point has three paths leaving it: two carry rules and the third is the fallback. The
  business considers them in a definite order and wants that order readable from the diagram without
  opening anything — the path considered first is the one drawn topmost, the next below it, and the
  fallback last.

Design time — what must be visible when the process is opened in the designer:

- Three paths leave the decision point, arranged top to bottom in the order they are considered.
- State which path is topmost and confirm it is the one considered first.
- The three paths are on three separate levels; two of them do not sit on the same line.

This case stops at design time. Whether the topmost rule actually wins at run time is TC-02.

---

## Group 5 — checking a plan before building it

## TC-10 — a legitimate shape must not be reported as invalid

Preconditions:

- None. Nothing is built; this is a review of a described plan.

Business requirement (stated as the input, verbatim):

- Describe a plan in which several paths come back together at a single point, and that point has
  exactly **one** path leaving it, which is the fallback path.
- Have the plan checked.

Expected — what the check must report:

- The check must **not** call this invalid. A point where paths merge and continue by a single onward
  path is an ordinary shape, and the tooling itself produces it.
- Report every finding the check returns, including warnings, and say for each whether you think it is
  about this shape or about something else in the plan.

This case stops at the check. It is a regression guard: this shape was previously reported as an error.

---

## Group 6 — a mistake that must be caught wherever it is expressed

## TC-11 — a step is wired back into itself (adversarial)

**Adversarial case — the shape is stated verbatim because the reaction is what is under test. It is
stated once and tried two ways on purpose: the same mistake must be caught whichever way it arrives.**

Preconditions:

- None.

Business requirement (stated as the input, verbatim):

- A step has to run again after it finishes, so express it the naive way: **a path leaving that step
  and arriving back at the same step**, with nothing in between.
- Try this **twice**, in whichever order you like:
  1. build it, and
  2. describe the same shape as a plan and have the plan checked.

Expected — what must happen, both times:

- **Building it must be refused.** Quote the refusal. It must say what is wrong and it must tell you
  what to do instead — a person who wanted a repeat has to be able to get one from what the message
  says, not by guessing.
- **The plan check must report it as an error**, not merely a warning, and must identify the step.
- The two must agree. A shape that is refused when built and accepted when checked, or the reverse,
  is a defect in itself — report the disagreement as the finding, not just the two outcomes.

This case stops at the refusal and the finding; nothing is built, so there is no stored or runtime
observation to make. If the build is **not** refused, that is the result — then say what was stored,
and do not run the process.

---

## Deliberately not covered

- **Inclusive (OR) and event-based decision points.** They are a separate piece of work
  (ENG-95889) and nothing in this release makes them buildable. A case would fail for the right
  reason and tell nobody anything new.
- **Readable diagrams for processes with several nested decision points.** Also separate work
  (ENG-95890). This release commits to one fork and its merge being readable; TC-08 and TC-09 test
  exactly that and no more. A deeply nested process may lay out poorly and that is not in scope.
- **Branching on the result of a preceding task** (the "which button was it completed with" style of
  branch). This release can *read* such a branch but not author one; the write side is a follow-up.
  If you meet one while testing, report what you see and do not try to change it.
- **Whether a rule expression is itself valid.** The platform validates expressions when the process is
  saved, and that surface has its own coverage. These cases use rules simple enough that a refusal
  would be about the branching, not the expression. If an expression is refused, quote the refusal —
  it is a result — but do not go on to explore expression validation.
- **Connector line routing.** The tooling places steps; it does not route the lines between them. See
  the known-behaviour note on TC-08.
- **Performance.** Nothing here is timed. Process log timestamps are used in TC-04 only to establish
  ordering, never duration.
- **Removing one of two paths that connect the same pair of steps.** Reading a process back now names
  each path, but no operation accepts that name as a handle, so the surgical case has no route through
  the tooling to test. What to do about it is an open question on this issue rather than shipped
  behaviour, and a case would be testing a decision that has not been made.
- **What happens to a decision point when its last rule-carrying path is taken away.** The consequence
  is documented on the surfaces a person reads, but whether the tooling should refuse, warn, or simply
  report the resulting shape is an open question on this issue. A case here would report which option
  was chosen, not whether the product works.
