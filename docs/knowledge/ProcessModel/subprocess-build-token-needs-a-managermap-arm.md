---
description: ManagerMap.ResolveDataId is the one place that accepts BOTH the camelCase diagram data-ids and the lowercase build tokens; a package-side token with no arm resolves to EventType.Unknown and validate-process-graph rejects a graph the server builds correctly
applies-to:
  - clio/Command/ProcessModel/Schema.cs
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707
date: 2026-09-16
---

**What is true** — `ManagerMap.ResolveDataId` maps `"callactivity"` (the diagram-js data-id) to
`EventType.SubProcess`, and since ENG-92707 it carries `"subprocess"` on the same arm. The
suffix arm it falls through to matches only names ending in `usertask`. Anything unmatched returns
`EventType.Unknown`, and `ProcessGraphValidator.CheckUnknownTypes` turns that into a hard **Error**.

The file records this exact trap for seven earlier tokens in its own comment
(`Schema.cs:1125-1136`): `sendemail`, `approval`, `openeditpage`, `readdata`, `changedata`,
`changeaccessrights` and `performtask` all had to be listed explicitly for the same reason.

**Why it is this way** — two vocabularies meet here. The canvas emits camelCase data-ids; create,
modify and `describe`'s `buildType` use lowercase collapsed tokens. `ResolveDataId` is deliberately
the one place that accepts both, so the validate → build → describe loop round-trips whichever
spelling a surface emits. A token added to `ProcessDesignConstants.ElementTypes` on the package side
is invisible to it until someone adds the arm.

**What breaks if you ignore it** — `validate-process-graph` reports a hard `UNKNOWN` Error on a graph
the server builds correctly, and the prescribed agent flow is validate-then-build. The agent therefore
refuses to build something that would have worked, and the failure names the element type rather than
the missing arm, so it reads as "sub-processes are not supported" — which is exactly the belief the
feature exists to remove. Add the arm **and** a `[TestCase]` in
`clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs` beside the existing `callActivity`
case, in the same change that introduces the token.

Keep `callActivity` and `eventSubProcessExpanded` separate while you are there: they carry different
UIds and different `EventType` values, and an event sub-process is an embedded region, not a call to
another process.
