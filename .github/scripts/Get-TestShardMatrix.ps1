param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("unit", "integration")]
    [string] $Suite,

    [string] $ManifestPath = "clio.tests/TestSharding/test-shards.json",

    [switch] $DisableSharding
)

$ErrorActionPreference = "Stop"

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$suiteDefinition = $manifest.suites.$Suite
if ($null -eq $suiteDefinition) {
    throw "Suite '$Suite' is missing from $ManifestPath."
}

if ($DisableSharding) {
    @{
        include = @(
            @{
                name = "$Suite-unsharded"
                shardingDisabled = $true
            }
        )
    } | ConvertTo-Json -Compress -Depth 4
    exit 0
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
if (@($include.name | Group-Object | Where-Object Count -gt 1).Count -gt 0) {
    throw "Suite '$Suite' contains duplicate shard names."
}

@{ include = $include } | ConvertTo-Json -Compress -Depth 4
