# GitHub test sharding

GitHub Actions runs the existing predicates across four unit workers and three integration
workers. Each worker uses the same `dotnet test clio.tests.csproj` path as the unsharded workflow.
TeamCity does not read this manifest or these scripts, so its unit, integration, and MCP end-to-end
configuration is unchanged. Separate hosted jobs build the product for NET8 and run
`Creatio.ConflictResolver.Tests` once, concurrently with all test workers.

The standalone compatibility jobs are unchanged when sharding is disabled; only the `clio.tests`
matrix collapses to a single worker.

The first shards select their listed fixtures. The final shard is a catch-all: it runs the base
predicate and excludes fixtures assigned to earlier shards. A newly added fixture therefore runs
exactly once even before the manifest is rebalanced.

## Turn sharding off

Set the GitHub repository variable `TEST_SHARDING_DISABLED` to `true`. Each matrix then collapses
to one worker using the original predicate verbatim:

- Unit: `TestCategory!=Integration`
- Integration: `TestCategory=Integration`

Use `TestCategory`, not the `Category` alias, in the committed base filters. The NUnit adapter
recognizes `TestCategory` as a category filter and preserves it when a shard selects more than its
2,000-test assembly selection limit; a generic mixed property/name filter can otherwise degrade to
an empty filter and run the whole assembly.

Delete the variable, clear its value, or set it to any value other than `true` to turn sharding
back on. The required checks remain named `Unit Tests` and `Integration Tests` in either mode.

## Rebalance shards

Each worker uploads its TRX file as a 14-day artifact. Download the four unit and three integration
TRX files from a representative GitHub run, then run:

```powershell
./.github/scripts/Rebalance-TestShards.ps1 `
  -UnitTrx ./timings/unit-[1-4].trx `
  -IntegrationTrx ./timings/integration-[1-3].trx `
  -UnitFixedSeconds 0,0,0,0
```

The script aggregates elapsed time by NUnit fixture and applies deterministic longest-processing-
time-first balancing. It uses the TRX run's wall time as well as individual result durations, so
test-run overhead is represented even when NUnit executes fixtures in parallel. `UnitFixedSeconds`
accounts for work outside `clio.tests`. NET8 compatibility and ConflictResolver tests run in their
own jobs, so neither belongs in these values.
Replace those values with timings from the same representative run as the TRX files. Review and
commit the updated `test-shards.json` so the manifest represents one coherent test inventory.
The unit distribution uses successful hosted TRX artifacts from GitHub run `33689464318`. The
integration distribution remains calibrated from successful hosted run `33317199418`.

When changing the filter mechanism, run the unsharded switch once and compare its TRX with the
sharded TRX files:

```powershell
./.github/scripts/Compare-TestShardCoverage.ps1 `
  -BaselineTrx ./timings/unit-unsharded.trx `
  -ShardTrx ./timings/unit-[1-4].trx
```

The comparison is multiplicity-aware, so both missing tests and tests selected by multiple shards
fail the check.
