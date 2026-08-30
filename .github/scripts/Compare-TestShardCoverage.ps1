param(
    [Parameter(Mandatory = $true)]
    [string] $BaselineTrx,

    [Parameter(Mandatory = $true)]
    [string[]] $ShardTrx
)

$ErrorActionPreference = "Stop"

function Get-TestInventory {
    param([Parameter(Mandatory = $true)][string[]] $TrxPath)

    $inventory = @{}
    foreach ($path in $TrxPath) {
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

$baseline = Get-TestInventory @($BaselineTrx)
$shards = Get-TestInventory $ShardTrx
$allKeys = @($baseline.Keys + $shards.Keys | Sort-Object -Unique)
$differences = @($allKeys | Where-Object { $baseline[$_] -ne $shards[$_] })

if ($differences.Count -gt 0) {
    $sample = $differences | Select-Object -First 10
    throw "Sharded inventory differs from the unsharded baseline for $($differences.Count) test identities. Sample: $($sample -join '; ')"
}

$baselineCount = ($baseline.Values | Measure-Object -Sum).Sum
$shardCount = ($shards.Values | Measure-Object -Sum).Sum
Write-Host "Coverage matches: $baselineCount test occurrences across $($ShardTrx.Count) shard TRX files."
