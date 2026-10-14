# Story 4: Refuse collection parameters as filter values

**Feature**: eng-92719-file-processing-element (side Sub-task)
**Jira**: not created yet. A new Sub-task of ENG-92719 File processing element (epic ENG-92704 Create BP via AI
Toolkit), titled "Refuse collection parameters as filter values"; Q9 (agreed 2026-10-07) creates it on day 0, and its
key (`<KEY>` below) is assigned on creation. Not created as of 2026-10-14.
**PRD**: [prd-eng-92719-file-processing-element.md](../prd/prd-eng-92719-file-processing-element.md) (scope: side
Sub-tasks)
**ADR**: [adr-eng-92719-file-processing-element.md](../adr/adr-eng-92719-file-processing-element.md) - D22
**Test plan**: [tp-eng-92719-file-processing-element.md](../test-plans/tp-eng-92719-file-processing-element.md) - the
story 4 rows (TC-U-101..TC-U-106, TC-I-101, TC-M-51)
**Status**: deferred (moves to ready-for-dev when its Sub-task exists and its slot opens: a gap while story 1's package
PR is a draft, never inside a final review window of the main track, because every side-lane triple restamps)
**Size**: S (about 4-6 h for its triple, inference; outside the ENG-92719 estimate)
**Repo**: crt-process-builder, then clio (the rebundle), then clio-knowledge: its own triple, titled
`<KEY> Refuse collection parameters as filter values`
**Depends on**: nothing functional; it touches only the process filter service, not the file element
**Blocks**: nothing; it does not gate the closure of ENG-92719 File processing element (decisions D-5, Q9)

---

## Goal

A filter condition compares one value. Today the builder accepts a collection parameter as the right-hand value of a
filter condition with no type check: the process saves green and fails when the element runs. Refuse it when the
filter is written, and name the route that does work.

## Why it is a separate Sub-task

- Measured as M19 on 2026-10-02 (CrtProcessBuilder 1.6.6.54): a Read data element `RD2` filtered by
  `Id = RD1.ResultCompositeObjectList` was accepted at build, and the run failed at `RD2` with
  `System.ArgumentException: No mapping exists from object type Terrasoft.Common.CompositeObjectList... to a known
  managed provider native type`. Loud at run time, silent at build.
- Still so on 1.6.6.96 (source): `PB/Filters/ProcessFilterService.cs` is unchanged since 1.6.6.85; ENG-102110 Process
  filters: a [#...#]-wrapped expression is accepted by setFilter but never evaluated at runtime validates the filter
  `expression` but adds no collection-type check; since 1.6.6.75 the `expression` spelling resolves through the same two
  structured branches as `processParameter` and `elementParameter`, so one guard in `ResolveReference`
  (`PB/Filters/ProcessFilterService.cs:533-595`, anchor of 2026-10-13) covers all three spellings.
- It is not file-specific: the file work only made it visible ("Id in CreatedObjectFileIds" is not a platform feature,
  decisions D22), so it gets its own PR triple and revert path, off the critical path.

## Acceptance (proposed; confirmed when the Sub-task is created)

- [ ] A filter condition whose right-hand value resolves to a collection (a process parameter of type
      `CompositeObjectList`, or an element output collection such as `ResultCompositeObjectList` or
      `CreatedObjectFileIds`) is refused when the filter is written (`setFilter`, the filter of `addElement`, the build
      path), for all three spellings `ResolveReference` resolves: `processParameter`, `elementParameter` and
      `expression`. The message names the parameter, says that a filter condition compares one value, and names the
      per-item route (a multi-instance sub-process over the collection).
- [ ] Every non-collection right-hand value keeps today's behaviour, pinned by the existing filter tests run unchanged.
- [ ] A filter already stored with such a value is not rewritten by a request that does not write that filter
      (inference: the guard runs only where a filter is resolved; to be confirmed when refined).
- [ ] On a stand carrying the cut, the M19 shape is refused at build; it was accepted on 1.6.6.54.
- [ ] The process filter guidance states the rule where it teaches the filter right-hand vocabulary (the article is
      chosen when refined, within its size budget; no Jira key in the text).
- [ ] Floor: decided when refined. The refusal adds no contract member; by the ADR D26 rule the clio floor moves only if
      clio starts advertising the refusal, and an older server keeps accepting the shape silently (inference).

## Test sketch

Package unit cases in `PBT/` (the filter-service tests): one per spelling (TC-U-101 `processParameter`, TC-U-102
`elementParameter`, TC-U-103 `expression`), each asserting the refusal and an unchanged schema, and TC-U-104, the
regression set of scalar values; TC-U-105 (criterion 3): a modify that does not write a filter already stored with a
collection value leaves it byte-identical; TC-U-106 (criterion 5, clio-knowledge): a pin test of the guidance
sentence. One MCP e2e refusal case (TC-I-101) if clio advertises the rule. One stand case
(TC-M-51): the M19 recipe re-run on the cut, using Read data as the consumer, never Modify or Delete data (a filter that
is evaluated wrongly there would touch every record). Ids are this story's rows of the test plan; they are refined with
the criteria.

## Definition of Done

- [ ] The Sub-task created in Jira and its key written into this file, its `spec/sprint-status.yaml` row and the PR
      titles; criteria confirmed with the owner and the status moved to ready-for-dev.
- [ ] The repository rules of story 1 apply unchanged: package fixture categories, AAA, `because:`, `[Description]`;
      the restamp claimed in the shared cut claim; the floor rule; the MCP, docs and ClioRing review statements;
      review gates 1, 2 and 3; only a human merges, package -> clio -> knowledge; never inside a final review window of
      the main track.
- [ ] Status flips in the clio PR of this triple: `in-progress` when its draft opens, `review` when it leaves draft;
      `done` rides in the next clio PR that touches `spec/sprint-status.yaml`.
