---
description: NUnit3TestAdapter replaces a non-category filter selecting more than AssemblySelectLimit tests with an empty filter, so mixed Category and FullyQualifiedName shard filters can silently run the whole assembly
applies-to:
  - .github/scripts/Invoke-TestShard.ps1
  - .github/scripts/Rebalance-TestShards.ps1
  - clio.tests/TestSharding/
ticket: 1343
date: 2026-09-03
---

**What is true** — shard base predicates must use the VSTest property name `TestCategory`. The NUnit
adapter recognizes that property as a category filter and preserves the full category/name expression.
With the `Category` alias, a shard selecting more than the adapter's 2,000-test `AssemblySelectLimit`
can be classified as a generic filter and replaced with an empty filter, meaning run everything.

**Why it is this way** — NUnit3TestAdapter rebuilds generic filters from discovered tests, but deliberately
falls back to an empty filter above its selection limit. It preserves filters it recognizes as category
filters. See the adapter's `filterinvestigation.md` and `Execution.CheckFilter` behavior.

**What breaks if you ignore it** — every nominal unit shard silently executes the full `clio.tests`
assembly. TRX files still look plausible because the same long non-parallel fixture can stall each run
at a different result count, while hosted duration grows from roughly two minutes locally to 8-15 minutes.
