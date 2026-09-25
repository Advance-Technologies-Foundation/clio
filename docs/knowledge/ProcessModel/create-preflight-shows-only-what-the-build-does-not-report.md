---
description: create-business-process runs the R1-R20 validator as an ADVISORY pre-flight and shows only the findings the server is silent about (R8, R7/R9 with no default, R13 off an event, R17) - every error, and the warnings the build also reports (R12, plain-flow R7/R9, R13 with no condition, UNBUILDABLE), are filtered out by ReportedByBuild; modify runs no pre-flight at all
applies-to:
  - clio/Command/ProcessModel/ProcessDescriptorPreflight.cs
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio/Command/ProcessModel/IProcessGraphValidator.cs
  - clio/Command/CreateBusinessProcessCommand.cs
ticket: ENG-95244
date: 2026-09-25
---

**What is true** — before the BuildProcess POST, `CreateBusinessProcessService` maps the descriptor's
`elements[]`/`flows[]` to a `ProcessGraph` and writes one `Pre-flight <rule>` warning per finding that is
a WARNING and not `ProcessGraphFinding.ReportedByBuild`. Nothing it finds blocks the build. In practice
that is R8 (parallel join behind a choice), R7/R9 with no default branch, R13 off an event, and R17.

Two of the shapes one would expect the pre-flight to announce are deliberately NOT shown, because the
server refuses them on create itself — measured in the bundled 1.6.6.23 source, not assumed:

- **R13 with an omitted condition** — `FlowKindRules.EnsureConditionMatchesKind` throws "requires a
  non-empty 'condition'" on create and on modify (only a flow with `results` escapes it).
- **UNBUILDABLE** — `ProcessElementFactory.Create` throws "Element type '…' is not supported yet" for
  every type no handler claims.

R12 and the plain-flow half of R7/R9 are build NOTICES (`ReportImplicitParallelSplits`, and the
normalisation of a lone plain flow into the gateway's default), so they are filtered for the same reason.
Every ERROR is filtered by severity: an error is by the validator's own rule a shape the build refuses.

The mapper reads the descriptor the way `ProcessGraphBuilder` does — element names trimmed and matched
case-insensitively, flow kind trimmed and case-insensitive, `userTaskName` deciding the element for any
user-task token when the validator knows that schema name — and returns NO lines for a descriptor it cannot
read that way (unknown kind, non-string condition, non-array `flows`), nor for one the server refuses before
it looks at a flow (two names differing only in case, an unknown or unbuildable element type). It never
throws: a failing check, or a graph above 500 elements / 1000 flows (R8 is super-linear and runs before the
POST), produces one "skipped" line instead; output is capped at 20 lines plus a count.

**Why it is this way** — the server became the gate before this pre-flight existed: `ValidateStructure`
(R1/R2/R3/R15) on create, `FlowKindRules` on both paths, and the platform's own validation, with nothing
persisted on a refusal. A clio refusal would only move a server refusal one round trip earlier, and would
block the builds where the two rule sets diverge in the server's favour (`ProcessValidateBuildDivergenceTests`
in the package pins those). What clio can add is the risk the server never reports - chiefly R8, where the
instance hangs in Running with no error at all.

**What breaks if you ignore it** — drop the `ReportedByBuild` filter and a refused create shows the
same problem twice in two phrasings, clio's first; a caller fixes clio's wording and is then refused by
the server's. Make the pre-flight blocking and a descriptor the server accepts (a conditional flow off an
event, which ships in the product) is refused by clio. Wire it into modify and the 37 shipped processes in
[start-event-arity-is-enforced-on-create-not-modify](start-event-arity-is-enforced-on-create-not-modify.md)
become uneditable for any change. And map a flow endpoint case-sensitively and a default flow spelled
`decide` off a gateway named `Decide` disappears from the graph, producing a false "no default" R7 on a
gateway the server builds with one. When a rule's server-side behaviour changes, move its
`ReportedByBuild` flag in the same change - a flag that says "the build reports it" after the build
stopped reporting it hides a silent risk. The flags are pinned per variant by `ProcessGraphValidatorTests.WarningDispositions`; a NEW warning rule defaults to `ReportedByBuild=false`, i.e. it is shown on every create, so it belongs in that table in the change that adds it.
