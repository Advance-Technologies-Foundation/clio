# GitHub test sharding

GitHub Actions runs the existing predicates across four unit workers and three integration
workers. Each worker uses the same `dotnet test clio.tests.csproj` path as the unsharded workflow.
TeamCity does not read this manifest or these scripts, so its unit, integration, and MCP end-to-end
configuration is unchanged.

The first shards select their listed fixtures. The final shard is a catch-all: it runs the base
predicate and excludes fixtures assigned to earlier shards. A newly added fixture therefore runs
exactly once even before the manifest is rebalanced.

## Turn sharding off

Set the GitHub repository variable `TEST_SHARDING_DISABLED` to `true`. Each matrix then collapses
to one worker using the original predicate verbatim:

- Unit: `Category!=Integration`
- Integration: `Category=Integration`

Delete the variable, clear its value, or set it to any value other than `true` to turn sharding
back on. The required checks remain named `Unit Tests` and `Integration Tests` in either mode.

## Rebalance shards

Each worker uploads its TRX file as a 14-day artifact. Download the four unit and three integration
TRX files from a representative GitHub run, then run:

```powershell
./.github/scripts/Rebalance-TestShards.ps1 `
  -UnitTrx ./timings/unit-*.trx `
  -IntegrationTrx ./timings/integration-*.trx
```

The script aggregates elapsed time by NUnit fixture and applies deterministic longest-processing-
time-first balancing. Review and commit the updated `test-shards.json`. Use timings from the same
successful run so the manifest represents one coherent test inventory.

When changing the filter mechanism, run the unsharded switch once and compare its TRX with the
sharded TRX files:

```powershell
./.github/scripts/Compare-TestShardCoverage.ps1 `
  -BaselineTrx ./timings/unit-unsharded.trx `
  -ShardTrx ./timings/unit-*.trx
```

The comparison is multiplicity-aware, so both missing tests and tests selected by multiple shards
fail the check.
