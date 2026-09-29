---
description: NUnit3TestAdapter treats only TestCategory as a category filter; any other filter selecting more than AssemblySelectLimit (2,000) tests - Category mixed with FullyQualifiedName or Module, and even a bare Category=Unit - is replaced with an empty filter and the whole clio.tests assembly runs
applies-to:
  - .github/scripts/Invoke-TestShard.ps1
  - .github/scripts/Rebalance-TestShards.ps1
  - clio.tests/TestSharding/
  - AGENTS.md
  - Makefile
ticket: 1343
date: 2026-09-25
---

**What is true** — every `clio.tests` filter must spell the category with the VSTest property name
`TestCategory`. The NUnit adapter recognizes only that spelling as a category filter and hands it to the
engine unchanged. A filter it does not recognize - the `Category` alias alone, or combined with
`FullyQualifiedName` or `Module` terms - is replaced with an empty filter once it selects more than the
adapter's 2,000-test `AssemblySelectLimit`: the host runs the whole assembly and reports only the selected tests.
AGENTS.md's single-module, multi-module and full-suite examples and the Makefile test targets are written this way.

**Why it is this way** — NUnit3TestAdapter rebuilds generic filters from discovered tests, but deliberately
falls back to an empty filter above its selection limit. It preserves filters it recognizes as category
filters. See the adapter's `filterinvestigation.md` and `Execution.CheckFilter` behavior.

**What breaks if you ignore it** — nothing in the results shows it. Measured 2026-09-25 on Windows,
`Category=Unit` and `TestCategory=Unit` gave identical TRX name and failure sets and wall times that did not
tell them apart, yet under the alias the engine also ran 136 unselected tests, Integration fixtures and their
fake git included. The cost grows with what is hidden: nominal unit shards run the full assembly (8-15 minutes
hosted instead of roughly two locally), and `Category=Unit&Module=Command` takes 3m42s-8m13s against 21 s for
the identical 4989 results. The unselected tests send no VSTest result events, so under
`--blame-hang-timeout` those stretches read as inactivity and the host is killed and reported as "Test host
process crashed", with whatever its stderr last held as the reason (see
`the-git-process-fixture-must-not-reach-the-clio-tests-output.md`). To check a run, pass
`-- NUnit.DumpXmlTestResults=true`: the dump in `clio.tests/bin/<cfg>/<tfm>/Dump/` shows the filter the engine
received, `<filter/>` when it was dropped.
