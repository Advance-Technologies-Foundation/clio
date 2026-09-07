param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("unit", "integration")]
    [string] $Suite,

    [string] $ManifestPath = "clio.tests/TestSharding/test-shards.json",

    [switch] $DisableSharding
)

$ErrorActionPreference = "Stop"

if ($DisableSharding) {
    @{
        include = @(
            @{
                name = "$Suite-unsharded"
                shardingDisabled = $true
            }
        )
    } | ConvertTo-Json -Compress -Depth 4
    return
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$suiteDefinition = $manifest.suites.$Suite
if ($null -eq $suiteDefinition) {
    throw "Suite '$Suite' is missing from $ManifestPath."
}

$include = @($suiteDefinition.shards | ForEach-Object {
    @{
        name = $_.name
        shardingDisabled = $false
    }
})

$expectedCount = if ($Suite -eq "unit") { 4 } else { 3 }
if ($include.Count -ne $expectedCount) {
    throw "Suite '$Suite' must define $expectedCount shards, but defines $($include.Count)."
}
$expectedNames = @(1..$expectedCount | ForEach-Object { "$Suite-$_" })
$actualNames = @($include.name | Sort-Object)
if ((Compare-Object $expectedNames $actualNames).Count -gt 0) {
    throw "Suite '$Suite' must use shard names: $($expectedNames -join ', ')."
}

@{ include = $include } | ConvertTo-Json -Compress -Depth 4
