# ENG-91853 (flow labels) — manual test prompt

You are testing a Creatio environment through the clio MCP tools. Work only from this prompt.

Environment: `Creatio` — `http://<dev-stand>:40001` (Supervisor). Any .NET Framework Creatio
reachable through clio will do, as long as business processes can be created through the tooling.

Application/section: none. Every process below is standalone and takes its input directly, so no
section has to be configured first.

As of 2026-09-08 that stand carried `CrtProcessBuilder` **1.6.0.9**. That is NOT what the change ships -
the shipped archive is whatever `ExpectedArchiveVersion` pins in
`clio.tests/Common/BundledProcessBuilderPackageTests.cs`, and it has moved several times since this
line was written. Read the installed version off the stand before trusting any result here, never off
this sentence. Nine
of the ten cases run there; TC-08 needs an older package and says so itself.

**Read this before you start, because one case depends on the order.** This suite is about the text a
reader sees written **on the arrows** of a process diagram. The tooling can only write that text if the
environment's process-building package is new enough to store it; on an older package the write is
accepted and the text is silently dropped. **TC-08 tests exactly that, and it can only be observed on
an environment that is still old.** So run TC-08 **first**, before any upgrade. If the environment is
already new enough, say so and report TC-08 as unrunnable rather than inventing a substitute. After
TC-08, if the tooling tells you the environment cannot store this text, bring the package up to the
version clio ships — for this suite that is allowed and expected — and say in the report that you did.

For every case below: build what the business asks for, then report what you observe at each level the
case names. Report exactly what you see, including anything that contradicts the expectation. If you
cannot express what the business asked for, say so and say what stopped you — that is a result, not a
failure to complete the task.

**Work the cases yourself, in order, and write up each one as you finish it.** Do not hand the suite,
or any part of it, to another agent to work in the background: nothing collects that work afterwards.

**BUILD YOUR OWN PROCESSES. Do not adopt one that is already on the stand as a precondition.**
Several processes with label-ish names are already there, left by work that is not this suite — as of
2026-09-08: `UsrBPFlowLabelSpike1` (the spike that proved the mechanism), `UsrBPLabelSmoke` (a smoke
check), and a family of `UsrClioBpFlowLabelE2e…` fixtures from automated runs. Their names read exactly
like what several cases below ask you to build, and TC-03 is where that bites: it says to build the
unlabelled process first if you have to, which is an invitation to reuse one instead. Do not. You do not
know what state they are in, and a case whose precondition you inherited rather than established
measures nothing — leave them alone and leave them in place.

**Build the shape each case describes, and do not reshape it to make an observation easier.** If you
cannot see something, say so; renaming the steps, removing an element or moving the paths elsewhere
turns the case into a different one that measures nothing.

Groups:

1. **A decision a reader can follow without opening anything** — the text put on the branches of a
   process as it is built. This is the group that matters most: it is the whole point of the feature.
2. **A process that already exists** — putting the text on, changing it, and taking it off again,
   without disturbing anything else about the process.
3. **Reading a process back** — what the tooling reports about this text, and one report that is
   ambiguous in a way that matters.
4. **Where the text can be lost with nobody noticing** (adversarial) — two routes by which it
   disappears while every visible signal says the write worked.
5. **Two arrows between the same two places** (adversarial) — what happens when the text cannot be
   addressed to one arrow unambiguously.

Most cases are observed at two levels — what is **stored** and read back, and what is visible at
**design time** when the process is opened. This text has no runtime effect, so runtime is not where
its defects live; the suite's runtime obligation is discharged by TC-01, TC-02 and TC-06, which prove
that a process carrying it still runs and still takes the one right branch. Do not read a stored value
as evidence that a reader can see it.

---

## Group 1 — a decision a reader can follow without opening anything

## TC-01 — two branches a reader can tell apart at a glance

Preconditions:

- None. The process takes the request amount as its input.

Business requirement:

- A request carrying an amount is examined once: amounts over 1000 go for approval, everything else
  goes straight to fulfilment.
- **A business analyst opening this diagram must be able to tell which arrow is which without clicking
  anything, and the words that tell them must sit on the arrows themselves** — not only in the names of
  the steps the arrows lead to, and not only inside an arrow's settings. Use the words the business uses
  for the outcome: `Needs approval` on one, `Within limit` on the other.
- Do not restate the rule in those words. "Over 1000" is already one click away on the arrow; a reader
  needs the outcome, not the arithmetic.

Stored — what must be written and read back:

- Both outgoing arrows read back carrying exactly the text asked for: `Needs approval` and
  `Within limit`.
- The rule itself is still on the approval arrow, unchanged, and separate from that text.

Design time — what must be visible when the process is opened in the designer:

- The words appear **on the connectors**, one on each, close enough to their own arrow that a reader
  can attribute each correctly.
- The step names are unchanged by this: the text on an arrow is additional to them, not a rename of
  them.

Runtime — what must happen when the process runs, and where it is visible:

- Amount **5000** → the approval path only. Amount **50** → the fulfilment path only.
- The process log shows one path per run. A process carrying this text must run exactly as it would
  without it: if adding the text changes which branch is taken, or breaks the run, that is the
  regression this level exists to catch.

---

## TC-02 — the fallback arrow says what it means, and the ordinary arrow stays bare

Preconditions:

- None.

Business requirement:

- A request is routed three ways by amount: over 1000, over 100, and everything else. Each route does
  its own step.
- The three arrows leaving the decision must each carry their outcome in words a reader understands —
  `High value`, `Medium value`, and, on the one that catches everything no rule matched,
  `Everything else`.
- **The arrow that merely continues the process — from its start into the examining place — must carry
  no words at all.** Labelling a plain continuation adds noise: there is nothing for a reader to choose
  between there.

Stored — what must be written and read back:

- Three texts on the three branch arrows, exactly as asked.
- The continuation arrow into the decision reads back with **no** text — and "no text" must be
  distinguishable from "empty text" when read back.

Design time — what must be visible when the process is opened in the designer:

- Three labelled arrows out of the decision and an unlabelled arrow into it.
- `Everything else` is attributable to the fallback arrow specifically, not floating between two.

Runtime — what must happen when the process runs:

- Amount **5000**, then **500**, then **50**: exactly one route per run, matching its own words. Report
  which route ran each time.

---

## Group 2 — a process that already exists

## TC-03 — the words are added to a process that was built without them

Preconditions:

- A process that already exists with a two-way decision by amount and **no** words on either arrow.
  Build it first if you have to, and confirm it has none before you start.

Business requirement:

- The analysts complain the diagram is unreadable. Put `Needs approval` and `Within limit` on the two
  arrows of the existing process, **without changing anything else about it**.

Stored — what must be written and read back:

- The two arrows carry the new text.
- Everything else is as it was: the rule on the ruled arrow, which arrow is the fallback, the steps and
  their positions. Report anything that moved.

Design time — what must be visible when the process is opened in the designer:

- The same diagram as before, now with words on its two arrows.

---

## TC-04 — one arrow's words are corrected and the other is left alone

Preconditions:

- The process from TC-03, with both arrows carrying words.

Business requirement:

- The business decides `Needs approval` reads too much like an instruction and wants `Above the limit`
  instead. The other arrow keeps its words.

Stored — what must be written and read back:

- The corrected arrow reads `Above the limit`. The other still reads exactly `Within limit` — an edit
  that also rewrites the untouched arrow is the defect this case exists to catch.

Design time — what must be visible when the process is opened in the designer:

- Both arrows are still labelled, one of them with the new words.

---

## TC-05 — the words are taken off an arrow, and must be gone rather than blank

Preconditions:

- The process from TC-04.

Business requirement:

- The words on the fallback arrow turn out to be misleading, and the business wants them **removed
  entirely** — that arrow should read as it did before anyone labelled it.

Stored — what must be written and read back:

- Reading the process back, that arrow has **no** text. Not an empty text: **no** text. State plainly
  how the read-back distinguishes the two, and if it cannot, that is the finding.
- The other arrow's text is untouched.

Design time — what must be visible when the process is opened in the designer:

- The designer draws that connector bare — no empty label box, no leftover artefact, no stray
  whitespace where the words used to be.

---

## TC-06 — the words and the rule are independent of each other

Preconditions:

- A process with a two-way decision whose ruled arrow carries both a rule and the words
  `Above the limit`.

Business requirement:

- The threshold changes from 1000 to 250. The words on the arrow must not change — they describe the
  outcome, not the number.
- Then, separately, the words are removed. The rule must survive that untouched, and the process must
  still route by it.

Stored — what must be written and read back:

- After the threshold change: the new threshold, the same words.
- After the words are removed: no words, and the threshold still exactly as it was left.

Runtime — what must happen when the process runs, and where it is visible:

- After both edits, run it with **300** and with **200**, and report which path each took. If removing
  the words changed the routing, that is the regression.

---

## Group 3 — reading a process back

## TC-07 — an arrow with no words, on an environment that may not report words at all (adversarial)

**Adversarial case — this one tests what you conclude, not what you build.**

Preconditions:

- Any process with at least one arrow that carries no words.

Business requirement:

- Someone hands you this process and asks a simple question: **does that arrow have words on it or
  not?**

Expected — what must happen:

- Reading the process back reports nothing for that arrow. **That report has two meanings** — the arrow
  really has no words, or this environment cannot report words at all — and they are the same result on
  the wire.
- **Say which of the two it is, and say how you established it.** An answer of "the arrow has no words"
  with nothing behind it is wrong even when it happens to be true, and it is the wrong answer this case
  exists to catch.

---

## Group 4 — where the text can be lost with nobody noticing (adversarial)

## TC-08 — the words are sent to an environment too old to store them (adversarial)

**Adversarial case — run this BEFORE any upgrade, or not at all.** The precondition is stated
literally because the reaction is what is under test.

Preconditions:

- The environment's process-building package (`CrtProcessBuilder`) **predates the flow label** — it does
  not declare the member, so an arrow cannot carry words. The member first shipped in 1.6.0.8, but do
  NOT use that number as the test: a higher-numbered cut from a branch line that never carried the
  member also qualifies, and reading the version as the criterion would wrongly rule it out. Report the
  version you found and whether labels come back at all. If the package carries the member, **stop this
  case** and report it as unrunnable on this environment; do not downgrade anything.

- **This case is very likely unrunnable anywhere, by construction.** Both write commands and describe
  carry `[RequiresPackage]` for this package, and clio refuses an environment running one older than the
  archive it ships — so on nearly every route a pre-label package is rejected before the guard under
  test could ever run. What remains reachable is the capability case above. If you cannot produce the
  state, say so and move on; do not manufacture it.

**Measured 2026-09-08, after the change was verified: no environment in reach can run this case, and
that is expected rather than a hole.** `Creatio` is at **1.6.0.9** — this feature's own verification
upgraded it three times during the day, and a version installed on a stand is spent. The two other
process-builder stands (`eng-91853`, `pb-stand`) answer **404**; they were temporary and have been
reclaimed. So run this case only on a stand that predates the change — a freshly provisioned one, or
one whose `CrtProcessBuilder` was installed by an older clio — and otherwise report it unrunnable and
move on.

What that costs is small, and worth knowing so the gap is not over-read: this is the one case in the
suite whose behaviour is already pinned automatically. Two unit tests per write path assert exactly
this warning — that the read-back happens for a labels-only payload, and that the text naming the
flow, the CAUSE and `install-process-builder` reaches the caller. Those tests now assert the text
carries NO version number at all, so do not read its absence as a regression. What TC-08 adds is the real
server's silence underneath it, not the warning itself.

Business requirement:

- Build the TC-01 process, with `Needs approval` and `Within limit` on its two arrows.

Expected — what must happen:

- The build **succeeds**. The words are **not stored** — an older package discards them and answers
  success anyway.
- **The tooling must tell you.** A build that reports plain success and leaves you with two identical
  bare arrows is the failure this case exists to catch: quote whatever warning or note came back, and
  say whether it named which words did not land.
- Then read the process back and confirm the arrows carry nothing, so the warning is corroborated
  rather than taken on trust.

---

## TC-09 — a labelled fallback arm is turned into a ruled one (adversarial)

**Adversarial case — the act is stated verbatim because the reaction is what is under test.**

Preconditions:

- A process whose decision has a ruled arm and a fallback arm, and whose **fallback** arm carries the
  words `Everything else`.

Business requirement:

- The business now wants that fallback arm to carry a rule of its own — amounts under 10 — and a
  different arm to become the fallback. Express that as a change to the arm itself, in place: the arm
  that was the fallback becomes a ruled arm.

Expected — what must happen:

- After the change, **the words `Everything else` are still on that same arrow**, exactly once.
- Read the process back and report every arrow's words. Two things would be regressions: the words
  vanishing, and the words appearing on two arrows at once.

**Known behaviour — do not file as a defect:** the words are stored in a way that depends on the
arrow's internal name, and that name is re-derived when an arm changes kind. The words being carried
across anyway is the expected outcome; it is worth checking precisely because the mechanism gives no
reason to assume it.

---

## Group 5 — two arrows between the same two places (adversarial)

## TC-10 — words addressed to an arrow that is not unique (adversarial)

**Adversarial case — the shape is stated verbatim because the reaction is what is under test.**

Preconditions:

- One step with **two** separate arrows leaving it that both arrive at the **same** next step. Build it
  if the tooling lets you; if it refuses to build that shape at all, quote the refusal and stop — that
  is the result.

Business requirement:

- Put the words `Retry` on "the arrow from the first step to the second".

Expected — what must happen:

- The tooling must **refuse**, and the refusal must say that more than one arrow matches, and how many.
  Quote it.
- Silently labelling one of the two is the failure this case exists to catch: it reports success while
  leaving the other arrow bare, and nothing in the result says which one got the words.
- Read the process back afterwards and confirm **neither** arrow was labelled.

---

## Deliberately not covered

- **The words at runtime.** They are text on a diagram and have no runtime meaning; nothing in a
  process log or a record shows them. TC-01, TC-02 and TC-06 therefore use runtime only to prove that
  a process carrying them still routes correctly, which is the only runtime claim available.
- **A second display language for the same words.** The text is localizable, so the same arrow can in
  principle read differently for a reader in another language. This suite exercises one language and
  makes no claim about a second.
- **Where the words sit on a folded-back diagram.** A return arrow — one that goes back to an earlier
  place — is currently drawn over the forward arrow rather than on a free row of its own, so its words
  can land on top of another connector. That is a **known, open** layout gap in this change, stated in
  its pull request; it is not to be filed again from this suite.
- **Words on the branches out of a wait-for-all fork.** Every branch there starts, so there is nothing
  for a reader to choose between and nothing for words to disambiguate. Labelling them is against the
  norm this change documents, not a case.
- **AUTHORING words in the designer by hand** — and note this bullet has shrunk. Reading a
  designer-authored label back is **no longer uncovered**: it does not need one to be created,
  because the shipped product is full of them. On a stock stand, hundreds of flow-caption resource
  rows predate this feature, and two shipped processes have been checked end to end -
  `AccountLeadConversionScoreUpdate` and `AddContact` - with the resource row, clio's read-back and
  the drawn canvas all agreeing, twice, by two parties. See the run report. What is left here is only
  a person TYPING one, which needs a human at a browser and proves nothing about the read path that
  the above does not already prove. If you do it anyway, two things will save you an hour:
  the designer opens at `/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<schemaUId>` and NOTHING
  else resolves — a guessed shell hash route leaves the whole app stuck on its splash screen for the
  rest of the session — and a connector's words can be read straight out of the canvas with
  `[...document.querySelectorAll('div.foreign-text')].map(e => e.textContent.trim())`, which needs no
  screenshot and does not care how small the window is. Both are written up in
  `docs/knowledge/platform/the-process-designer-has-one-url-and-guessing-wedges-the-shell.md`.
