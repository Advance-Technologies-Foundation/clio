param(
    [Parameter(Mandatory = $true)]
    [string] $BaselineTrx,

    [Parameter(Mandatory = $true)]
    [string[]] $ShardTrx
)

$ErrorActionPreference = "Stop"

function Get-TestInventory {
    param([Parameter(Mandatory = $true)][string[]] $TrxPath)

    $resolvedPaths = @($TrxPath | ForEach-Object { Resolve-Path -Path $_ } | ForEach-Object Path | Sort-Object -Unique)
    $inventory = @{}
    foreach ($path in $resolvedPaths) {
        [xml]$trx = Get-Content -LiteralPath $path -Raw
        foreach ($result in @($trx.TestRun.Results.UnitTestResult)) {
            $key = "$($result.testId)|$($result.testName)"
            if (-not $inventory.ContainsKey($key)) {
                $inventory[$key] = 0
            }
            $inventory[$key]++
        }
    }
    $inventory
}

$resolvedBaselineTrx = @(Resolve-Path -Path $BaselineTrx | ForEach-Object Path | Sort-Object -Unique)
$resolvedShardTrx = @($ShardTrx | ForEach-Object { Resolve-Path -Path $_ } | ForEach-Object Path | Sort-Object -Unique)
$baseline = Get-TestInventory $resolvedBaselineTrx
$shards = Get-TestInventory $resolvedShardTrx
$allKeys = @($baseline.Keys + $shards.Keys | Sort-Object -Unique)
$differences = @($allKeys | Where-Object { $baseline[$_] -ne $shards[$_] })

if ($differences.Count -gt 0) {
    $sample = $differences | Select-Object -First 10
    throw "Sharded inventory differs from the unsharded baseline for $($differences.Count) test identities. Sample: $($sample -join '; ')"
}

$baselineCount = ($baseline.Values | Measure-Object -Sum).Sum
$shardCount = ($shards.Values | Measure-Object -Sum).Sum
Write-Host "Coverage matches: $baselineCount test occurrences across $($resolvedShardTrx.Count) shard TRX files."
