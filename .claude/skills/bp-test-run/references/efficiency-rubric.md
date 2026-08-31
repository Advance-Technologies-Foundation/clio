# Efficiency rubric

How to read the executor's transcript and turn it into fixable findings.

## Input

`<scratch>\<uuid>.stream.json` — the captured event stream, or a `creatio-development:share-session`
export when the raw capture is missing. Work from the tool-call events: name, arguments, result
status, and order. Order matters as much as count.

## The question being asked

Not "did it succeed" — phase 4 answers that. The question is: **given only the shipped guidance and
tool descriptions, did the agent take a direct path?** Every detour is a defect somewhere upstream,
and almost never in the agent.

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

Guidance articles live in `Advance-Technologies-Foundation/clio-knowledge` under `guidance/`, one
Markdown file per article, indexed by `bundle-source.json`. A fix there is a pull request in that
repository and needs a `libraryVersion` + `sequence` bump — a library whose content changed under a
reused sequence is rejected by clio.

Tool descriptions, tool profile membership, and the routing pointer live in this repository under
`clio/Command/McpServer/`. Changing either is an MCP surface change and pulls in the MCP review
policy in `AGENTS.md`.
