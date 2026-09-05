# ENG-91853 — Handoff to the verification session (review gate + stand)

Written 2026-09-05, at the end of the implementation sessions. Everything is implemented and
unit-green in all three repositories. **Nothing is pushed and no pull request exists.**

What is left is exactly two things, and both are "verify what was built" rather than "build":

1. the **comprehensive review gate** AGENTS.md requires before a PR is opened;
2. the **stand verification** V1–V9 from [plan §4](eng-91853-gateways-and-flows-plan.md), which no unit
   test can cover.

Then three pull requests, one per repository, each into that repository's default branch.

---

## 1. The diffs to review

| Repository | Branch | Base | Commits | Review with |
|---|---|---|---|---|
| `crt-process-builder` (`C:/Projects/workspace/ProcessBuilder`) | `feature/ENG-91853-gateways-and-flows` | `7e93995` (**`main`**, not master) | 6 | `git diff 7e93995 HEAD` |
| `clio` (`C:/Projects/clio`) | `feature/ENG-91853-gateways-and-flows` | `a9deb32bc` (`master`) | 10 | `git diff a9deb32bc HEAD` |
| `clio-knowledge` | `feature/ENG-91853-gateways-and-flows` | `84e2609` (`master`) | 1 | `git diff 84e2609 HEAD` |

`clio-knowledge` must be worked in its linked worktree `.worktrees/eng-91853` — that repository's
`AGENTS.md` makes the root checkout coordination-only.

---

## 2. Read these three pieces first — nobody but the author has looked at them

Three prior review rounds ran (layout, server core, clio validator) and each found real defects. But a
review round's own FIXES are written after the reviewers have finished, so the following are covered by
nothing except the author's own mutation tests. They are the highest-value targets in the change:

1. **`CheckParallelJoinDeadlock` + `DivergesIntoTwoBranches` + `TraverseBackwardEdges`**
   (`clio/Command/ProcessModel/ProcessGraphValidator.cs`). New graph logic, rewritten in commit
   `025bbdc4d` because the first version matched a common ANCESTOR instead of a divergence and warned on
   almost every real graph. It is the most intricate code in the whole change.
2. **`ApplyConditional` / `SetFlow` / `SetFlowCondition`** in `ProcessGraphBuilder.cs` (package,
   `571fbb1`). Restructured so that `setFlow kind=conditional` on a default branch stops being a dead
   end and `setFlowCondition` stops bypassing the authoring rules. The three-way entry-point split is
   new.
3. **`RemoveElement`'s endpoint detach** (package, `571fbb1`). The same fix as `RemoveFlow`'s, applied
   to the second of the two paths that need it.

---

## 3. Settled by measurement — do not spend a round re-deriving these

The ticket's own DO-NOT list is in
[implementation-prompt](eng-91853-gateways-and-flows-implementation-prompt.md); it still holds. On top
of it, these were raised by a reviewer during implementation and **refuted against source or by
running the code**:

- **A duplicate element `UId` crashing the layout / the builder.** Cannot reach either:
  `MetaItemCollection.InsertItem` throws `ItemAlreadyExistException` on a duplicate, so
  `schema.FlowElements` cannot hold one.
- **A flow with a dangling endpoint being constructible.**
  `ProcessSchemaSequenceFlow.set_TargetRefUId` calls `GetBaseElementByUId`, which throws
  `ItemNotFoundException`. The reachable shape is the reverse — the element is removed AFTER its flows —
  and that is what the test builds.
- **Layout performance.** Derived cost at the largest shipped 7.8.0 process (~300 elements) is ~0.2 ms
  against a modify call measured in tens to hundreds of milliseconds. Nothing was optimised deliberately.
- **`VisualType` being what makes the metadata say AutoPolyline.** It is not:
  `ProcessSchemaSequenceFlow.WriteMetaData` passes the LITERAL, so `CI6` is `1` whatever the object
  holds. Recorded in `docs/knowledge/platform/sequence-flow-visualtype-is-written-as-a-literal.md`.
- **Rule-id reuse breaking a consumer.** Checked across `clio` and `clio-ring`: nothing filters, counts
  or keys by rule id; every consumer uses `Contains`/`Where` with an explicit severity.

### One design conflict is OPEN and is the owner's call, not the reviewer's

Layout §4's **case B** is marked ✔ in its verification table and is **not fixed**; it cannot be fixed by
placement. Three of the ticket's commitments conflict for that one shape, and connector routing is out
of scope. The measurement, the three options and a recommendation are in
[layout-addendum §2](eng-91853-gateways-and-flows-layout-addendum.md). A reviewer should not treat it as
a defect to fix; it needs a decision.

---

## 4. What the three prior rounds already found and fixed

So a reviewer can tell a NEW finding from one already actioned. Each of these is fixed in the branch:

**Layout (session A):** the corridor reservation was unfalsifiable — deleting it left the whole fixture
green; the case that pins it was found by enumerating small single-source graphs. Banker's rounding
aligned a merge with its split only on an EVEN lane, which a second start event is enough to reach;
changed to floor.

**Server core (session B):** `setFlow kind=conditional` on a default branch was a dead end whose message
answered a different question, and on a deciding gateway there was no route at all;
`setFlowCondition` bypassed `FlowKindRules` entirely, so a conditional flow out of a PARALLEL gateway
was silently accepted, which turns an AND-split into an XOR-split at generation time; T-8's detach was
applied to `RemoveFlow` and missed in `RemoveElement`. Plus: the normalisation of a lone unconditional
flow into a default was silent and now raises a notice, and `SetFlowOperation.Apply` had ZERO coverage,
so both of its type-safe argument mistakes were green.

**clio validator (session C):** a red test in `clio.tests/Command/ProcessModel/` shipped in `96061c532`
because the targeted filter run was `Command|McpServer|Common` and the changed file maps to
`Module=ProcessModel`; the deadlock warning matched ancestry rather than divergence; the or-gateway
arity filter written beside the fix was unfalsifiable and was removed.

**Pattern worth carrying into the gate:** three separate times in this change, code that no test could
falsify survived a first review. Ask of every guard: *what mutation would redden a test?*

---

## 5. Running the tests

**Package** — `dev-nf`, not `dev-n8`: `.application/net-core` is absent on this host, so `dev-n8` fails
with ~900 missing-reference errors that read like a broken checkout.

```
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf
```
→ **1213 pass, 0 fail** (baseline at the start of the ticket: 1149). This repository has **no CI at
all**, so a local run is the only gate the package ever gets.

**clio** — `-c Release`, not Debug: a running MCP server holds the Debug output open and the build fails
with `MSB3027` naming a file rather than a culprit. See
`docs/knowledge/process/a-running-mcp-server-locks-the-debug-clio-binary.md`.

```
dotnet test clio.tests/clio.tests.csproj -c Release \
  --filter "Category=Unit&(Module=ProcessModel|Module=McpServer|Module=Command|Module=Common)"
```
→ **10134 pass, 0 fail**. `Module=ProcessModel` is in that list deliberately; leaving it out is the
mistake that shipped a red test.

---

## 6. Stand verification (V1–V9)

The full table is [plan §4](eng-91853-gateways-and-flows-plan.md#4-stand-verification-the-part-no-unit-test-can-cover).
Three constraints that are not in it:

- **Run schema-write operations SEQUENTIALLY.** A parallel burst trips IIS rapid-fail and downs a
  .NET Framework stand's application pool.
- **Only `clio/bin/Release/net10.0` carries the new 1.4.0.58 archive.** The rebundle script rebuilds one
  output; `Debug/net10.0`, `Debug/net8.0` and `Release/net8.0` still ship **1.4.0.57**, which does not
  have gateways. An install run from any of them will verify the wrong package. Debug could not be the
  one rebuilt, because of the lock above.
- **The user verifies UI results in the browser themselves** — do not auto-open a browser after a
  successful write.

Two checks are worth doing even before the rest, because they are where this change is most likely to
be wrong in a way no unit test sees: **V2** (byte-diff the built `metadata.json` against
[capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim)
— `BL7`, `CI4`, `CI5`, `CI6`, `BN2`) and **V4** (first-`true`-wins: two overlapping conditions, then swap
`flows[]` order and confirm the outcome swaps).

---

## 7. Then the pull requests

One per repository, into that repository's default branch — `main` for `crt-process-builder`, `master`
for the other two. The clio PR body should name the package commit its bundled bytes came from
(`571fbb1`), which the rebundle commit message already records.
