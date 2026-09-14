# ENG-91853 (flow labels) — manual test run, 2026-09-09

Run `080eaf6f-aae2-47f3-ac38-f48900f0a0c3`, blind `claude -p`, clio MCP only, started **04:12:33Z**.

| | |
|---|---|
| clio | rebuilt from `feature/ENG-91853-flow-labels`, binary carries `FlowLabelExpectation` and, at the time, a `1.6.0.8` constant in its warning text. Calling that a "floor" was wrong even then — the enforced floor is `[RequiresPackage] 1.6.0.3` — and the constant has since been REMOVED: a review showed no caller can be below it, because clio refuses an environment running a package older than the archive it ships. The warnings now name the cause qualitatively and carry no version |
| Guidance library | re-pinned 1.13.99 → `74a34f33` / **1.13.100**; the served `process-branch-conditions` article verified to contain LABEL BOTH ARMS and the corpus figures |
| Stand | the dev stand, core 10.1.37.0, `CrtProcessBuilder` **1.6.0.9** |
| Suite | `…-flow-labels-manual-test-prompt.md` at `76fe8b2fb`, plus a run header (env alias, `BPLabel TC<NN>` naming, no package operations, Activity completion permitted and to be listed, refusals to be quoted verbatim) |

**Which archive this evidence covers.** The stand carried `CrtProcessBuilder` **1.6.0.9** when this run
was taken, and the manifest records that. The branch has rebundled several times since and now ships a
later cut, so this report is evidence about 1.6.0.9 and is NOT re-readable as evidence about the
shipped archive. Two things close part of the gap and neither replaces a re-run: the flow-label MCP
e2e cases were re-run against the shipped archive after it was installed on the same stand, and
convergence now refuses an environment below the bundled version outright, so a stand still on 1.6.0.9
cannot even be reached by the write commands until it is reinstalled. Re-running the full suite against
the shipped cut is deliberately out of scope for this document; it is a new run with its own manifest.

Nine of ten cases ran; TC-08 needs a package older than the capability and was correctly reported
unrunnable. **Every claim below was re-verified from the stand after the run**, through the branch
binary, not taken from the executor's report.

## Verified

**Nothing was adopted.** The precondition trap the suite warns about leaves no trace in a run's own
record, so it was checked against the clock rather than the transcript — `VwProcessLib.CreatedOn` for
every `BPLabel*` process:

| process | created | verdict |
|---|---|---|
| `UsrBPLabelSmoke` | 2026-09-08T18:16:01Z | pre-existing, untouched |
| `UsrBPLabelTC01` | 2026-09-09T04:16:09Z | built by this run |
| `UsrBPLabelTC02` | 04:17:57Z | built by this run |
| `UsrBPLabelTC03` | 04:19:12Z | built by this run |
| `UsrBPLabelTC06` | 04:20:54Z | built by this run |
| `UsrBPLabelTC09` | 04:22:28Z | built by this run |

All five case processes post-date the 04:12:33Z start. No `UsrBPFlowLabelSpike1`, `UsrBPLabelSmoke` or
`UsrClioBpFlowLabelE2e…` fixture was reused as a precondition.

**Labels land, and on the right arrows.** Read back from the stand:

- TC-01 — `conditional → 'Needs approval'`, `default → 'Within limit'`, both plain flows unlabelled.
- TC-02 — `'High value'`, `'Medium value'`, `'Everything else'` on the three branch arms; the
  continuation into the gateway carries none.

**Cleared means gone, not blank** — the claim TC-05 and TC-06 turn on, and the one worth checking in
raw JSON rather than through a parser. `UsrBPLabelTC06` after the clear:

```json
{ "source": "AmountExaminedGateway", "target": "SendForApproval", "kind": "conditional",
  "condition": "…[Parameter:{6f61dafc…}]#] > 250", "branchesOnActivityResult": false,
  "name": "ConditionalFlow_AmountExaminedGateway_SendForApproval" }
{ "source": "AmountExaminedGateway", "target": "SendForFulfilment", "kind": "default",
  "branchesOnActivityResult": false,
  "name": "DefaultFlow_AmountExaminedGateway_SendForFulfilment" }
```

No `label` key at all — not `"label": ""`. And the threshold moved to `> 250` while the label was
untouched, then the label went and the threshold stayed: TC-06's independence claim holds in both
directions.

**The re-kind carries the label, once.** `UsrBPLabelTC09`: the arm that was the fallback is now
`ConditionalFlow_AmountExaminedGateway_SendForFulfilment` — the name re-derived from the new kind — and
`'Everything else'` is still on it, exactly once. The arm that became the fallback carries no label.
That is the behaviour `a-flow-rekind-does-not-orphan-its-label.md` records, now measured through the
tooling rather than on the resource rows.

**Runtime: seven instances, all Completed, one path each.** `SysProcessElementLog`:

| process | input | elements logged |
|---|---|---|
| TC-01 | 5000 | Amount examined → Send for approval → Request sent for approval |
| TC-01 | 50 | Amount examined → Send for fulfilment → Request fulfilled |
| TC-02 | 5000 | Amount examined → Escalate high value request → High value request escalated |
| TC-02 | 500 | Amount examined → Review medium value request → Medium value request reviewed |
| TC-02 | 50 | Amount examined → Process request as usual → Request processed |
| TC-06 | 300 | Amount examined → Send for approval → Request sent for approval |
| TC-06 | 200 | Amount examined → Send for fulfilment → Request fulfilled |

Labels change nothing about routing, which is the only runtime claim available for them. TC-06's two
runs are the load-bearing pair: they happened *after* the label was cleared and still routed by the
`> 250` rule.

**TC-10's refusal is real and quoted correctly.** Reproduced independently by sending the same
two-arrows-one-pair shape through the build path; nothing was created:

> A sequence flow already connects 'DoFirstStep' to 'DoSecondStep'. A second one between the same pair
> could not be addressed afterwards - both would carry the same name, and naming the pair would no
> longer say which one to act on.

Word for word what the executor reported. Note the shape is refused *at build*, so the label could
never be attempted — which is a stronger answer than the case expected (it anticipated a refusal when
addressing the label) and is what the case's own escape clause tells the runner to report.

**TC-08 handled correctly.** The stand is 1.6.0.9, so the case is unrunnable; the executor checked the
version first, did not downgrade or substitute, and additionally probed the two other process-builder
stands named in the suite — both refuse with "does not appear to be a Creatio application", consistent
with having been reclaimed.

**TC-07 answered the way the case demands.** It did not merely report "no label": it established that
this environment *does* report labels, by pointing at the labels round-tripped elsewhere in the same
run on the same stand, and only then concluded that an absent field here means "no label". That is the
distinction the case exists to force, and the answer would not have been available on an older package.

## Not verified, and one overstatement

- **No design-time level was observed anywhere in this run.** The executor said so plainly for TC-01 —
  no designer was opened, and its design-time claim is inferred from the stored per-flow structure.
  That honesty is right, and it means the whole suite ran at Stored + Runtime only. Whether a reader
  actually *sees* the words on the connectors — the entire point of the feature — remains unobserved
  by this run. The browser pass the suite already asks for now owes more than the designer-authored
  case: it owes the design-time level for TC-01, TC-02, TC-05 and TC-09.
- **TC-03's "byte-for-byte identical, nothing else moved" is unverifiable after the fact.** No pre-edit
  snapshot exists on my side, so I can neither confirm nor refute it; the post-edit state is consistent
  with the claim.
- **The report's sentence "All ten processes below are named `BPLabel TC<NN>`" is wrong.** Five
  processes exist, which is what the cases require — TC-04 and TC-05 edit TC-03's process by design,
  TC-07 reads TC-06's flow, TC-08 built nothing, TC-10 was refused. The behaviour is correct; only the
  sentence overstates.

## Verdict

Nine cases run, nine consistent with the stand, one correctly declared unrunnable. No product defect
surfaced by this suite. The gap this verdict originally named — that nothing had been seen in a
designer — is closed by the design-time pass appended below, for the four cases it named. What
remains uncovered is only a label a PERSON typed in the designer by hand, which no tooling-only run
can create.

## Design-time pass, 2026-09-09, closing the level this run left open

The run above closes with "nothing here was seen in a designer", which is the level the whole feature
exists for: a stored caption is not evidence that a reader sees words on a connector. This section
closes it for the four cases the report names — TC-01, TC-02, TC-05 and TC-09 — on the same stand and
the same processes, opened in the real designer.

**Method, and why it is not a screenshot.** The designer draws a connector label as a
`div.foreign-text` inside its canvas SVG, so the text and the node COUNT are both readable from the
DOM:

```js
const t = [...document.querySelectorAll('div.foreign-text')];
({ drawn: t.map(e => e.textContent.trim()).filter(Boolean),
   emptyBoxes: t.filter(e => !e.textContent.trim()).length })
```

That is deliberate rather than lazy. It answers a question a screenshot cannot — whether a cleared
label leaves an EMPTY node behind — and it does not depend on the browser window being big enough to
render a diagram, which on this machine it was not.

### What is drawn

| case | process | flow labels drawn | element captions | empty nodes |
|---|---|---|---|---|
| TC-01 | `BPLabel TC01` | `Needs approval`, `Within limit` | 6 | — |
| TC-02 | `BPLabel TC02` | `High value`, `Medium value`, `Everything else` | 8 | — |
| TC-05 | `BPLabel TC03` | `Above the limit` (one) | 6 | **0** of 7 nodes |
| TC-09 | `BPLabel TC09` | `Everything else` (exactly once) | 6 | **0** |

Each matches that process's `SysLocalizableValue` rows exactly — same texts, same count. So the
resource row and the drawn label agree, which is the one link neither the stored level nor the runtime
level can establish.

Three results worth stating individually:

- **TC-02's bare continuation draws nothing.** Three branch labels and eight element captions, eleven
  nodes, and the unlabelled `Request received → Amount examined` flow contributes none of them. The
  norm the feature documents — label the branches, leave an ordinary continuation bare — is what the
  diagram shows.
- **TC-05 is the strongest result here, and it needed the node count rather than the text.** Seven
  `foreign-text` nodes, **zero** empty. The cleared arm contributes no node at all — not a blank one.
  That is exactly what the case demanded ("gone rather than blank... no empty label box, no leftover
  artefact, no stray whitespace") and it is not observable from the text list alone, which is why the
  probe counts.
- **TC-09's label is drawn once.** `Everything else` appears exactly one time on the canvas, on the
  arm whose name was re-derived by the re-kind, and nothing appears on the arm that became the
  fallback. The knowledge record measured this on the resource rows and the run measured it through
  the tooling; this is the same fact at the level a reader actually experiences.

### A trap for whoever does the next browser pass

**The designer does not reload on a hash-only change, and it does not tell you.** Navigating from one
`…?vm=SchemaDesigner#process/<uidA>` to `#process/<uidB>` leaves the PREVIOUS process on screen: the
address bar shows the new UId, `document.title` still names the old process, and the DOM still holds
the old labels. This pass nearly recorded TC-01's two labels as TC-02's three — the URL had changed,
the read succeeded, and the answer was about a different process.

`location.reload()` after the hash change fixes it. Read `document.title` as the check that you are
looking at what you think you are: it carries the process caption, so it falsifies the mistake
directly. Every row in the table above was taken after a reload and with the title verified.

Loading takes about 40 seconds per process on this stand — the first read at 20 seconds returned an
empty list, which looks exactly like "no labels are drawn". Wait, then read the title.


### Re-taken by a second party, 2026-09-09

The table above was measured once, by the session that wrote it, and its own trap note says that pass
nearly recorded one process's labels as another's. So it was taken again, independently, in the same
real Chrome, with `document.title` verified before every read:

| case | process | title read | nodes | drawn | empty |
|---|---|---|---|---|---|
| TC-01 | `BPLabel TC01` | `BPLabel TC01` | 8 | `Needs approval`, `Within limit` + 6 element captions | 0 |
| TC-02 | `BPLabel TC02` | `BPLabel TC02` | 11 | `High value`, `Medium value`, `Everything else` + 8 captions | 0 |
| TC-05 | `BPLabel TC03` | `BPLabel TC03` | 7 | `Above the limit` + 6 captions | **0** |
| TC-09 | `BPLabel TC09` | `BPLabel TC09` | 7 | `Everything else` **×1** + 6 captions | 0 |

Identical to the first pass in every cell. The design-time level is therefore measured twice by two
parties, and TC-05's zero-empty-nodes result — the one the case turns on — is confirmed rather than
single-sourced.

**And one thing only a picture answers, which the DOM query cannot.** TC-02 was also looked at.
`High value` sits squarely on its own horizontal connector and is immediately attributable. The other
two are drawn near connector segments that run close together on a three-way fan: `Medium value` sits
between the horizontal run above it and the drop below, and `Everything else` on the lower horizontal
run. Nothing is mispositioned and no label is attached to the wrong arrow — but attribution for the
second and third branch is by *following the line*, not by proximity alone.

That is a remark about **connector routing**, not about labels: the words are where their flow is, and
the flow is where the layout put it. It belongs with the layout work — the same place as the pinned
lane rules and the open back-edge overlap — and not against this change. Stated here because a
design-time pass that only counts DOM nodes cannot see it, and because "the analyst can tell which
arrow is which at a glance" is the business requirement TC-01 and TC-02 are written from.


### A label a PERSON typed, read back by clio — the item the suite calls uncoverable

The suite's "deliberately not covered" list says a designer-authored label cannot be created by a
tooling-only run and needs a browser pass. True as stated — but it does not have to be *created*: the
shipped product is full of them. On this stand, `BaseElements.%Flow%.Caption` in `en-US` matches
**867 rows across 249 schemas**, of which exactly **17** are clio's — the ones written by this
change's own runs today, identifiable by the `<Prefix>Flow_<source>_<target>` underscore shape the
toolkit generates. The rest predate the feature and none of it was written by clio.

867 / 249 reproduces on both sides of this pass — same predicate, `COUNT(*)` and
`COUNT(DISTINCT SysSchemaId)` — so the upper bound is two-party. **Do not read it as a census**, and
three separate reasons why are worth keeping apart, because each has its own remedy.

**A first count of this put it at "100+ across 81 schemas", and that was a page size read as a
population.** The read used `odata-read` with `top: 100` and came back `count: 100` — where `count` is
documented, in the tool's own contract, as *the number of records returned in this page*. A real total
needs `count: true`, which returns `total-count`. So the published figure was the limit the caller had
chosen, handed back and mistaken for a measurement, and "81 schemas" was the distinct count *within*
that page. It is a referent trap of the kind this report keeps finding — an accurately named field read
as answering a question it does not answer — and the remedy is one argument, not a better classifier.

**And the tool describes that field twice, in two surfaces, with two different precisions.** Checking
the contract claim rather than accepting it turned up the reason this is easier to walk into than it
sounds. `get-tool-contract` — the surface that was read — says *"Number of records returned in this
page"* (`ToolContractGetTool.cs:2125`), which is unambiguous. The live response schema of `odata-read`
itself says *"Number of records returned."* (`ODataReadTool.cs:614`), which is not: "returned" reads
as "returned by the query" as readily as "returned in this response". The unambiguous sentence about
paging — *"Response count remains the number of records in this page"* — sits on the **`count` request
parameter**, which a caller who never passes `count: true` has no reason to read.

So the person who read the contract read the right thing and was told the truth; a person who reads
the tool's own output schema is told something weaker, and the clarification is filed behind an
argument they are not using. `next-link` is the reliable tell in the meantime — it is present in the
response whenever records exist beyond `top`. Raised as a separate item; it is a one-line alignment in
a tool this change does not otherwise touch.

**Separately, and this one would corrupt a correct total too:** the designer has used at least
**three** naming styles for a flow — `ConditionalSequenceFlow5` (476 rows), `ConditionalFlow1` (149),
and descriptive names like `ConditionalFlowLeadUndefined` or `QualifiedAccountExistsSequenceFlow` (the
remainder). Any classifier built from the styles you happen to have seen silently drops the ones you
have not, so this survives fixing the pagination and needs the corpus to discover rather than the
contract.

**And the predicate over-matches in the other direction.** `%Flow%` catches things that are not flows
at all: an element literally named `CancelFlow`, and
`DeleteCashflow.Parameters.IsMatchConditions.Caption`, where the match lands inside the word
*Cashflow*. So 867 is an upper bound on designer-authored flow labels however it is counted — upper
bound is the honest word, and no amount of pagination discipline makes it a census.

None of which the claim below depends on. What matters is that there are **hundreds**, that clio did
not write them, and that clio reads them correctly.

Joined to their schemas (`SysSchema.Id = SysLocalizableValue.SysSchemaId`, via SQL because `SysSchema`
is not readable through OData) and then read back through `describe-business-process`:

| shipped process | stored in resources | reported by clio |
|---|---|---|
| `AccountLeadConversionScoreUpdate` | `MQLs found`, `No MQLs found` | `default → 'MQLs found'`, `conditional → 'No MQLs found'` |
| `AddContact` | `Has LinkedIn`, `Has Facebook` | both `conditional`, same text |

Identical, and this is the direction that had never been tested: every other result in this report is
clio reading back what clio wrote. Here clio reads what a **human authored in the designer**, on
content that predates the feature — which is precisely what TC-07 relies on when it says `describe` is
the only way to learn that a designer's label exists.

Worth noting from the same row set: `AccountLeadConversionScoreUpdate` carries its label on the
**default** arm (`MQLs found`) and on the conditional arm (`No MQLs found`). That is the shipped
product doing what the guidance tells callers to do — label both arms, name the outcome — and an
instance of the 25.5% of default flows that carry a label.

**Method note, because it cost a wrong conclusion first.** `SysLocalizableValue.SysSchemaId` is
`SysSchema.Id` — the row id — and `VwProcessLib.Id` is the schema **UId**. They are different columns
of different tables. Feeding a `SysSchemaId` to `describe-business-process` as `process-uid` returns a
bare `Object reference not set to an instance of an object`, which reads like a defect in describe and
is not one: it is the wrong identifier, and the same wrong-referent shape this repository already has a
record for. Nothing was filed.

**And seen drawn — the item is now closed end to end.** `AddContact` opened in the designer, title
`Add one contact to account (sub process)` (the same caption clio reported, so it is the right
process), canvas read:

```
nodes: 5   empty: 0
drawn: Has LinkedIn, Has Facebook, Add linkedin, Add Facebook, Add contact
```

So for content **no tooling wrote**, all three levels agree: the resource row, clio's read-back, and
what the designer draws. That is the full chain the suite says it cannot reach, and it did not need a
label to be created — only one to be found.

**Re-taken independently, both halves.** The message that reported this section said the shipped
labels were verified in the resources and through clio but **not yet on a canvas** — the browser
session was believed expired. The section said otherwise, and the section is right: the session was
alive. Both halves re-measured from scratch rather than accepted.

`AccountLeadConversionScoreUpdate`, read back through `describe-business-process`:

```
SequenceFlow3             sequence      label None
SequenceFlow5             sequence      label None
DefaultSequenceFlow3      default       label 'MQLs found'
ConditionalSequenceFlow5  conditional   label 'No MQLs found'
SequenceFlow9             sequence      label None
```

Matching its resource rows exactly — and note the three plain flows report `None`, not `''`, on
content clio never touched. **That closes a loop nothing in this report planned.** Every other place
the null-versus-empty distinction is observed here — TC-05 above, the raw-schema check in the
manifest — is clio reading back its own write, so it could in principle have been an artefact of the
writer rather than a property of the read path. This one is not: nobody wrote these three flows
through the toolkit, and they still come back absent rather than blank. `AddContact` re-opened in the designer, title
`Add one contact to account (sub process)` verified before reading: **5 nodes, 0 empty**, drawn
`Has LinkedIn`, `Has Facebook`, `Add linkedin`, `Add Facebook`, `Add contact`. Identical to the
first reading and to the two resource rows.

So the full chain on human-authored content is now measured twice by two parties, which is the same
bar the rest of this report holds itself to.

**A useful comparison came with it.** `AddContact` is hand-laid-out by whoever shipped it, and its two
branches fan **symmetrically** — one up to `Add Facebook`, one down to `Add linkedin` — with each label
sitting over its own segment. Attribution is instant, and the reason is the geometry: the two
connectors leave the activity in opposite directions. Our generated three-way fan stacks its branches
downward instead, which is what puts the second and third labels near segments that run close
together. Same feature, same drawing code for the label; different fan geometry. It is one more piece
of evidence that the remark above belongs to connector routing rather than to labels, and it shows
what the target looks like.

**The geometry is measurable — and the comparison does not end where it looks like it ends.** Element
positions come back from `describe-business-process`, so the two fans can be compared in numbers
rather than described. `AddContact` straddles its source:

```
AddDataUserTask3   442;77     ← 95 above
AddDataUserTask1   307;172      the branching activity
AddDataUserTask2   442;264    → 92 below
```

Symmetric to within three pixels. `BPLabel TC02` stacks from its source instead:

```
AmountExaminedGateway      240;173     the gateway
EscalateHighValueRequest   420;173   →   0   same row
ReviewMediumValueRequest   420;303   → +130
ProcessRequestAsUsual      420;433   → +260
```

Which explains the observation exactly, **including the part of it that looked arbitrary**: the first
branch shares the gateway's own row, so its connector is a clean horizontal run and `High value` is
unambiguous. That is why one of the three read well and two did not. Branches two and three both
descend before running horizontally, so their descending segments share one corridor and a label on
each lands near the other's line. Which label reads well is not arbitrary — it follows from three
numbers.

**But do not read `AddContact` as the target, which is where the sentence above overreaches.** The
downward stack is a decision with reasons on the record — [layout §"Why branches go **downward**
rather than being centred on the parent"](eng-91853-gateways-and-flows-layout.md) — and both survive
this measurement:

1. **A centred fan moves branches that already exist.** The layout re-runs on every modify and saves,
   so with lanes `−1, 0, +1` a single `addFlow` reshuffles the diagram and a reviewer sees noise
   instead of a change. Downward assignment leaves every previously placed branch where it was.
2. **Top-to-bottom order *is* runtime evaluation order.** The runtime takes the first `true` in array
   order, phase 1 preserves declaration order, and nothing in the schema encodes precedence — so the
   vertical stack is the only place a human can read it. Straddling destroys that.

And labels **strengthen** the second reason rather than weakening it. Before this change the vertical
order encoded precedence between arrows a reader could not name; now the arrows say `High value`,
`Medium value`, `Everything else` top to bottom, and the reading is "these are tried in this order"
in words. The corpus agrees on the other half too: `dy = 0` is the most common single branch offset
(379 of 974 measured targets), i.e. one branch keeping the parent's row — which is what
`EscalateHighValueRequest` at `420;173` does.

So the residue is **not** the fan direction. It is that two descending branches share a corridor, which
is connector routing and label placement along a segment — and it stays with ENG-95890. What the
`AddContact` comparison actually establishes is that the label is drawn where its flow is, in both
geometries, which is a statement about this change and a clean one.
