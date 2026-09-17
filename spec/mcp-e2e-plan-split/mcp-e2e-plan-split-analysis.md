# Would decomposing clio into layers let the change detector run only part of the e2e suite?

Measured on `master` at `7b39de32d` (2026-09-17), against the 1299 `.cs` files under `clio/`
and the 207 fixture files of `clio.mcp.e2e`.

## The question

> Как ты думаешь мы сможем улучшить change detector что бы не запускать все тесты а только их часть?
> Если мы разложим клио по слоям может нам удастся запускать только часть? Это огромный refactoring
> но нам это поможет расти.

The detector shipped with [#1571](https://github.com/Advance-Technologies-Foundation/clio/pull/1571)
turned 3 of 60 merged pull requests into subsets. The hypothesis under test is that the remaining 57
are full runs because clio's types are entangled, and that separating them into layers would let the
detector see a small blast radius.

## Method

A reference graph over `clio/`. A node is a **type**, not a file; an edge `B -> A` means A's text
names a type declared in B. The graph also carries implementation-to-interface edges and the
dependency-injection pairs from `BindingsModule.cs`, because a consumer that injects `IFoo` never
spells `Foo` out. Name matching over-approximates, so the measured connectivity is an upper bound on
what the compiler would see, never an under-count.

"Precise" below means a file whose transitive consumers reach at most 20 of the 226 MCP tool files,
so a subset filter is worth composing.

## What the measurement says

**1. The coupling is diffuse, not concentrated.**

| Experiment | Files that stay precise |
|---|---|
| Baseline, file-level graph | 450 of 1299 |
| Cut the single most-used type (`BaseTool`) | 457 |
| Cut all 22 types named by 60 or more files | 464 |

Removing `ILogger`, `Command`, `EnvironmentSettings`, `IApplicationClient`, `IFileSystem`,
`IServiceUrlBuilder`, `Package` and 15 more from the graph together moves 14 files of 1299. There is
no small set of types whose extraction unlocks the detector, because the paths are redundant: cutting
one leaves several others. A worked example, from a response record to the whole tree:

```
ODataWriteResponse -> ClioRunTool -> SensitiveErrorTextRedactor (64) -> CommandExecutionResult (46)
                   -> EnvironmentSettings (147) -> ILogger (54) -> IFileSystem (24) -> everything
```

**2. The granularity win that layering promises was available without touching the source.**

809 of 1299 files declare more than one top-level type. While the graph node was a file, a narrow
helper inherited the consumer set of whatever wide type shared its file. Moving the node from file to
type raised precision from 450 to 532 files (35% to 41%) with no change to clio at all. That is the
refactoring's main mechanical benefit, already taken.

**3. For the files that remain imprecise, running everything is correct.**

`clio/Common/ConsoleLogger.cs` reaches 225 of 226 tool files because every MCP session logs.
`EnvironmentSettings` reaches 147 files because every environment-bound tool reads it. A layered
clio would make those dependencies explicit and directed; it would not make them disappear, and the
set of tests able to observe a regression in them would stay the same. Selection cannot shrink a
blast radius that is genuinely wide.

## Conclusion

Decomposing clio into layers is not justified **by this goal**. It would not raise the detector's
precision materially, because the connectivity is redundant rather than hub-shaped, and because the
shared infrastructure it would separate is genuinely executed by almost every e2e test. It may be
justified by other goals - build time, ownership, testability of the CLI in isolation - and this
measurement says nothing about those.

What did pay, in this pull request, is making the detector see what is already there:

- the graph node is a type rather than a file;
- an implementation is linked to its interface, so a service resolved from the container is not
  mistaken for dead code;
- a CLI verb is linked to the MCP tool published under the same name (132 of 243 verbs), so a change
  to a command selects the fixtures of its tool instead of looking uncovered;
- the composition root is classified from its changed lines rather than treated as always-global;
- documentation and generated help are discarded instead of queueing a Creatio deploy.

## Known limits of the measurement

- Reachability is textual. It over-approximates edges, so a subset is wider than the compiler would
  require; it cannot see reflection at all, which is why a file declaring a `[ResolvedDynamically]`
  type still forces a full run.
- "At most 20 tool files" is a threshold chosen to match `maxSubsetFixtures`, not a property of the
  code. A different threshold moves the percentages but not the shape: the distribution is bimodal,
  a file either reaches almost nothing or almost everything.
- The pull-request figures replay historical diffs against today's tree, so they read as "what
  today's rules would do", not as replayed history.
