# PRD: mcp-worker-execution-boundary — deliberately a pointer, not a requirements document

- **Feature:** mcp-worker-execution-boundary
- **Jira:** [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262) (parent: ENG-95286 — Migration tool
  from Classic UI to Freedom UI)
- **Status:** Phase 1 recorded as **satisfied by the Jira issue**, not skipped
- **Date:** 2026-08-18

## Why this file exists

The repository's BMAD pipeline expects a `spec/prd/prd-{feature}.md` for every non-trivial feature, and the
automated gate check looks for the file. This feature does not have a PRD in the ordinary sense, and this
file says so rather than inventing one.

## Why there is no real PRD

**The Jira issue is the PRD.** ENG-95262 was written before the ADR and already carries, in full:

- the requirements,
- the measured evidence (field run `wf_3509d34b-193`, the head-to-head `get-page` comparison, the
  deterministic stub reproduction, the refuted platform hypothesis, the cost measurements),
- the decision and the rejected alternatives,
- eleven implementation constraints, each of which had already broken a naive version of the plan,
- the definition of done.

Writing a `prd-*.md` from that material would be **transcription, not analysis** — a second copy of a
specification that is already complete, guaranteed to drift from the original the first time either is
edited, and adding no decision that was not already made. The reasoning is recorded in the ADR itself, at
[§1.1 "Why this ADR has no PRD"](../adr/adr-mcp-worker-execution-boundary.md).

This is a recorded position, not an oversight. If a future reader disagrees, the thing to do is argue with
§1.1 — not to fill this file with requirements reconstructed after the fact, which would be worse than
nothing: reconstructed requirements read as authoritative while being a later author's guess at an earlier
author's intent.

## Where the actual content lives

| What you are looking for | Where it is |
|---|---|
| Requirements, measured evidence, DoD | The Jira issue, [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262) |
| The design, the eleven binding rules, the rollout, the open questions | [`spec/adr/adr-mcp-worker-execution-boundary.md`](../adr/adr-mcp-worker-execution-boundary.md) |
| Per-tool execution metadata | [`spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-execution-metadata.md`](../mcp-worker-execution-boundary/mcp-worker-execution-boundary-execution-metadata.md) |
| Cross-call state inventory | [`spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-cross-call-state.md`](../mcp-worker-execution-boundary/mcp-worker-execution-boundary-cross-call-state.md) |
| Credential threat model (T-1…T-10, R-1…R-12) | [`spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-credential-threat-model.md`](../mcp-worker-execution-boundary/mcp-worker-execution-boundary-credential-threat-model.md) |
| Stories | `spec/stories/story-mcp-worker-execution-boundary-*.md` |
| Test plan | [`spec/test-plans/tp-mcp-worker-execution-boundary.md`](../test-plans/tp-mcp-worker-execution-boundary.md) |

## The one-paragraph summary, for a reader who landed here first

One Creatio request that never gets an answer makes a long-lived `clio mcp-server` permanently unusable for
that environment — every later call for that environment is cut at the read deadline **without issuing an
HTTP request at all**, and it stays that way after the backend recovers. The decision is to keep the MCP
contract and move the execution boundary: every environment-touching tool call runs in a short-lived child
`clio mcp-server`, and the budget is enforced by **killing the child** rather than by asking the transport to
stop. Read the ADR for why that and not the alternatives.
