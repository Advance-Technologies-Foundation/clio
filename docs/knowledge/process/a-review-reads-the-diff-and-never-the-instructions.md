---
description: every gate this repository has reads the diff - review agents, CI, the drift oracles - so an instruction file that the diff never touches can go stale under a correct change and stay wrong indefinitely; the failure is invisible because the code is right and the article is what a maintainer follows
applies-to:
  - docs/agent-instructions/
  - AGENTS.md
  - docs/McpCapabilityMap.md
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — a change can be correct, reviewed, tested and merged, and leave a document that
*describes* the changed thing quietly wrong. Nothing catches it. Review agents read the diff. CI runs
against the diff. Even the guard fixtures that exist for exactly this class —
`WorkspaceTemplateGuidanceDriftTests`, the `McpCapabilityMap` pinned sentence — are scoped to a named
list of files, so a document outside that list drifts freely. The document then keeps instructing
people, because an instruction file is not something anybody re-derives; it is what you consult
*instead of* re-deriving.

ENG-91853 produced three instances in one ticket, all found by accident rather than by a gate:

- **`docs/agent-instructions/bundled-packages.md`** said *"raising it costs nothing to maintain … the
  version was also the `[RequiresPackage]` floor, so raising it forced a refusal on every environment
  until upgraded … Both the floor and that reason are gone."* True when written. Then
  `IBundledPackageConvergence` reintroduced the same refusal for triggered requirements, in a change
  that had no reason to open this article. The stale sentence was read three times and acted on three
  times before anyone checked it.
- **`docs/McpCapabilityMap.md`** drifted back to describing the product before this ticket — a
  conditional branch "not buildable here", a slice without gateways, no clear-condition operation.
  The one line that stayed current was the single sentence a test pins, which is the whole lesson in
  miniature.
- **`CreateBusinessProcessTool`'s own `[Description]`** told agents to call a long-tail tool. A tool
  description is the authoritative contract, and it is the one guidance surface with no oracle at all.

**Why it is this way** — a diff is the unit every gate is built on, because it is what a change *is*.
Documentation drift is the opposite shape: the defect is created by a change **elsewhere**, and lands
on a file the change never opened. There is nothing in the diff to look at.

**What breaks if you ignore it** — the failure mode is worse than an ordinary stale comment, because
an instruction file is trusted *procedurally*. A stale comment is read by someone already looking at
the code and can be weighed against it. A stale article is read by someone who is deliberately not
looking at the code, which is what the article is for. In the case above it produced three unnecessary
rebundles, each one a configuration build and a restart on the stand under test.

**What to do about it.** Two habits and one build-out:

1. **When a change alters a RULE rather than a behaviour** — a gate's blast radius, a floor's meaning,
   what a version bump costs — grep the instruction corpus for the rule, not for the code. The
   sentence to search for is the one a maintainer would act on, and it will not contain your symbol
   names.
2. **When you follow an instruction and it turns out to be expensive or surprising, check it against
   the code before complying.** That is how the `bundled-packages.md` defect surfaced: someone asked
   why the procedure was being repeated, and the answer was in an article nobody had reason to open.
3. **Widen the oracles that already exist.** The drift tests know how to decide "is this token still
   true"; they are scoped to three template files. Extending the input set is cheaper than inventing
   a new gate, and it is the only one of the three that does not depend on anyone remembering.

**Do not confuse this with the code-review gates.** Those are working as designed and finding real
defects; the point is only that their design cannot reach here.

**And do not write off AGENTS.md's triggers either — measured, they got two of these three right.** The
triggers are aimed at FILES, and the diagnosis is different for each instance, which matters because two
of the three are fixable by sharpening a trigger that already exists rather than by inventing a gate:

| Instance | Trigger | What failed |
|---|---|---|
| `bundled-packages.md` | **present, and exact** — AGENTS.md names `BundledPackageConvergence.cs` in the list that says to read this article | the **verb**. It says *read*, and reading an article in order to FOLLOW it is not the act of checking it still holds |
| `McpCapabilityMap.md` | **absent** — it appears nowhere in AGENTS.md; the required MCP targets are `Tools\*.cs`, `Prompts\*.cs`, `Resources\*.cs`, `clio.tests`, `clio.mcp.e2e`, `clio	pl\**` | nothing fires at all |
| `CreateBusinessProcessTool`'s `[Description]` | **present** — `Tools\*.cs` is a required target and fires on every edit | the **question**. The rule asks to keep the description aligned with *"the current command behavior"* — this tool's own. Whether a tool it NAMES still exists, or is still resident, is a different question nothing asks |

So the honest form of the limitation is narrower than "triggers fire where attention already is": a
trigger cannot fire on a file the diff never opens, and that is instance 2 alone. Instances 1 and 3
landed on a file the trigger DID name, and what was missing was the instruction to **reconcile** rather
than to follow. Sharpening those two is the cheapest mitigation available, and unlike the habits below
it depends on nobody remembering anything — which is why it belongs above them.

Changing AGENTS.md's trigger list and verbs is a repository-wide decision affecting every agent that
works here, so it is recorded as a finding rather than made as a side effect of the ticket that found
it.
