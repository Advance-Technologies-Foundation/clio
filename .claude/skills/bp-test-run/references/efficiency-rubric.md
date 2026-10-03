# Efficiency rubric

How to read the executor's transcript and turn it into fixable findings.

## Input

`<scratch>\<uuid>.stream.json` — the captured event stream, or a `creatio-development:share-session`
export when the raw capture is missing. Work from the tool-call events: name, arguments, result
status, and order. Order matters as much as count.

## The question being asked

Not "did it succeed" — phase 4 answers that, and functional defects in `CrtProcessBuilder` surface
there. The question here is: **given only the shipped guidance and tool descriptions, did the agent
take a direct path?** Every detour is a defect in clio or in the knowledge library, and almost never
in the agent.

This is the half of the debugging session that a functional pass cannot see. A feature that works
but takes eleven calls to reach is shipping broken for every user who is not the person who built
it.

## Signals

Each row is a pattern to search for, what it means, and who owns the fix.

| Signal | What it indicates | Owner |
|---|---|---|
| The same read repeated with identical arguments | The first result was not retained or not trusted; often the result shape is unclear | Tool result shape / description |
| `get-guidance` called **after** the first mutating action, or not at all | The trigger line in the tool `[Description]` did not fire | Tool description + routing article |
| Several sequential writes where one batched call exists | The batch capability is undocumented or unfindable | Guidance article |
| A retry after a validation error that a prior read would have prevented | Preconditions are not stated where the agent looks | Guidance article |
| `clio-run` used where a resident MCP tool exists | Tool is long-tail when it should be resident, or the routing table omits it | Tool profile / routing article |
| `get-tool-contract` fetched after a failure | The description alone was insufficient to call the tool correctly | Tool description |
| Repeated `list-*` calls hunting for a name | No documented discovery path from business term to identifier | Guidance article |
| A long exploratory stretch before the first productive call | The routing guide did not lead from the task to the right guide | Routing article |
| Success reached by a path the guidance does not describe | The guidance is behind the implementation — it worked by luck | Guidance article |
| The agent asked the prompt for information it should have discovered | Prompt leaked implementation, or discovery is genuinely impossible | Prompt, or tool surface |

## Before attributing anything to the knowledge library

Check which generation actually served, and whether it contains the guidance you came to test:
`info-knowledge` gives the version and revision, and the owning repository tells you whether the
relevant commit is an ancestor of it. A run measures the **active** generation, whatever any branch
contains.

Skip this and the rubric works perfectly against the wrong library: every signal fires, every finding
looks real, and the fix goes to an article that was already corrected. Three positions have to be
kept apart, because only the third is a defect anyone can act on — closed in the served generation,
closed in the repository's main branch, closed only on an unmerged branch.

## A probe that can only come back one way is not evidence

When a finding rests on a probe you wrote, confirm the probe can produce both answers before trusting
the one it gave. A check that fails for its own reasons — a shell variable that is read-only and
expands to something else, a grep whose pattern cannot match the shape it is looking for — reports the
defect you suspected and is indistinguishable from a real result. Prefer a probe that names the exact
symbol or field, and verify a negative control.

## Not findings

Do not report these — they inflate the list and bury the real defects:

- Reads that were genuinely needed to make a decision.
- One retry after a transient environment error.
- A longer path that the guidance explicitly prescribes. That is a guidance design question, not an
  execution defect; raise it separately if the prescribed path is wrong.
- Raw turn or token count on its own. It is context, not a finding.

## Hazard to check separately

A burst of **parallel** schema writes is not an efficiency win. On IIS-hosted .NET Framework stands,
rapid-fail protection can take the application pool down, and the run then fails for reasons that
have nothing to do with the feature. If the transcript shows one, check the stand's health before
recording any FAIL, and treat the burst itself as a guidance gap — the sequential rule should have
been visible to the agent.

## Baseline

State, per case, the **minimum call sequence** a well-guided agent would use, then compare. Without a
stated baseline "12 calls" means nothing. The baseline is written by whoever knows the intended path
— usually the author of the change under test — and belongs in the report so the next run can be
compared against the same yardstick.

## Reporting

One row per finding, most costly first:

```markdown
| # | Signal | Evidence (call indices) | Cost | Owner | Proposed fix |
|---|---|---|---|---|---|
| 1 | get-guidance after first write | calls 3-7 | 4 wasted calls, wrong element type on first attempt | `modify-business-process` description | Add explicit trigger line naming the gateway guide |
```

Then the totals: calls made vs baseline, per case.

A finding without an owner and a proposed fix is an observation. Either give it both, or leave it
out of the table and mention it in prose as something to watch.

## Where fixes land

Owners in the table above resolve to two of the three components under test: **clio** (tool
descriptions, tool profile membership, the routing pointer — all under `clio/Command/McpServer/` in
this repository) and the **knowledge library** (articles under `guidance/` in `clio-knowledge`,
indexed by `bundle-source.json`).

`CrtProcessBuilder` rarely owns an efficiency finding; its defects show up functionally in phase 4.
The exception worth watching: when a tool's error text is unusable because the package returned
something unusable, the finding is the package's, not the tool's.

The routing table for all three destinations is in the skill's *Where each defect goes* section —
keep it there, not duplicated here.
