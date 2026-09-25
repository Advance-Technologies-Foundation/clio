---
description: ManagerMap.ResolveDataId is the one place that accepts BOTH the camelCase diagram data-ids and the lowercase build tokens; a package-side token with no arm resolves to EventType.Unknown and validate-process-graph rejects a graph the server builds correctly - the whole server token list is now pinned against the bundled archive, and a new token also needs ManagerMap.IsBuildable to accept its kind
applies-to:
  - clio/Command/ProcessModel/Schema.cs
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707
date: 2026-09-25
---

**What is true** — `ManagerMap.ResolveDataId` maps `"callactivity"` (the diagram-js data-id) to
`EventType.SubProcess`, and since ENG-92707 it carries `"subprocess"` on the same arm. The
suffix arm it falls through to matches only names ending in `usertask`. Anything unmatched returns
`EventType.Unknown`, and `ProcessGraphValidator.CheckUnknownTypes` turns that into a hard **Error**.

The trap was found one token at a time: `sendemail`, `approval`, `openeditpage`, `readdata`,
`changedata`, `changeaccessrights` and `performtask`, then `adddata`, `preconfiguredpage` and
`subprocess`, and last `deletedata` (ENG-95244), which was still Unknown on master after the comment
beside the arm had called `preconfiguredpage` "the last build token still missing". Since ENG-95244,
`ManagerMapResolveDataIdTests` holds the server's WHOLE token list (`ServerBuildTokens`, copied from
`ProcessDesignConstants.ElementTypes`), asserts every entry resolves and is buildable, and compares the
copy with the `ElementTypes` constants read out of the bundled `CrtProcessBuilder.gz`.

A second decision now rides on the same classification: `ManagerMap.IsBuildable(EventType)` is the one
place clio decides which kinds create/modify can build, and the validator's `UNBUILDABLE` warning reads it.

**Why it is this way** — two vocabularies meet here. The canvas emits camelCase data-ids; create,
modify and `describe`'s `buildType` use lowercase collapsed tokens. `ResolveDataId` is deliberately
the one place that accepts both, so the validate → build → describe loop round-trips whichever
spelling a surface emits. A token added to `ProcessDesignConstants.ElementTypes` on the package side
is invisible to it until someone adds the arm - which is why the pin compares against the archive rather
than trusting the copy.

**What breaks if you ignore it** — `validate-process-graph` reports a hard `UNKNOWN` Error on a graph
the server builds correctly, and the prescribed agent flow is validate-then-build. The agent therefore
refuses to build something that would have worked, and the failure names the element type rather than
the missing arm, so it reads as "this element is not supported" — the belief each feature exists to
remove. A rebundle that adds a token now fails
`ServerBuildTokens_ShouldMatchTheBundledPackage_WhenTheArchiveIsRebundled`; add the token to
`ServerBuildTokens`, the `ResolveDataId` arm, and — if its kind is new — the `IsBuildable` case, in that
same change. Adding a kind to `IsBuildable` WITHOUT a server token is the opposite mistake: the validator
then stops marking an element the server still refuses.

Keep `callActivity` and `eventSubProcessExpanded` separate while you are there: they carry different
UIds and different `EventType` values, and an event sub-process is an embedded region, not a call to
another process - and only the first is buildable.
