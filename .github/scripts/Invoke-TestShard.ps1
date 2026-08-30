param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("unit", "integration")]
    [string] $Suite,

    [Parameter(Mandatory = $true)]
    [string] $ShardName,

    [string] $ManifestPath = "clio.tests/TestSharding/test-shards.json",

    [string] $ResultsDirectory = "TestResults",

    [string] $AssemblyPath = "clio.tests/bin/Release/net10.0/clio.tests.dll",

    [switch] $DisableSharding
)

$ErrorActionPreference = "Stop"

function ConvertTo-FilterTerm {
    param([Parameter(Mandatory = $true)][string] $Fixture)

    if ($Fixture.Contains('"') -or $Fixture.Contains("&") -or $Fixture.Contains("|")) {
        throw "Fixture '$Fixture' contains a character that cannot be safely used in a VSTest filter."
    }

    "FullyQualifiedName~$Fixture."
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$suiteDefinition = $manifest.suites.$Suite
if ($null -eq $suiteDefinition) {
    throw "Suite '$Suite' is missing from $ManifestPath."
}
$expectedBaseFilter = if ($Suite -eq "unit") { "Category!=Integration" } else { "Category=Integration" }
if ($suiteDefinition.baseFilter -ne $expectedBaseFilter) {
    throw "Suite '$Suite' must preserve the original predicate '$expectedBaseFilter'."
}

$shards = @($suiteDefinition.shards)
$expectedShardCount = if ($Suite -eq "unit") { 4 } else { 3 }
if ($shards.Count -ne $expectedShardCount) {
    throw "Suite '$Suite' must define $expectedShardCount shards, but defines $($shards.Count)."
}
$duplicateFixtures = @($shards.fixtures | Group-Object | Where-Object Count -gt 1)
if ($duplicateFixtures.Count -gt 0) {
    throw "Suite '$Suite' assigns fixtures to more than one shard: $($duplicateFixtures.Name -join ', ')."
}

$filter = [string]$suiteDefinition.baseFilter
if (-not $DisableSharding) {
    $shard = $shards | Where-Object name -eq $ShardName
    if ($null -eq $shard) {
        throw "Shard '$ShardName' is missing from suite '$Suite'."
    }

    $shardIndex = [array]::IndexOf($shards, $shard)
    if ($shardIndex -eq $shards.Count - 1) {
        $assignedEarlier = @($shards[0..($shards.Count - 2)].fixtures)
        if ($assignedEarlier.Count -gt 0) {
            $exclusions = $assignedEarlier | ForEach-Object { (ConvertTo-FilterTerm $_) -replace '~', '!~' }
            $filter = "($filter)&$($exclusions -join '&')"
        }
    }
    else {
        $terms = @($shard.fixtures | ForEach-Object { ConvertTo-FilterTerm $_ })
        if ($terms.Count -eq 0) {
            throw "Non-final shard '$ShardName' has no fixtures."
        }
        $filter = "($filter)&($($terms -join '|'))"
    }
}

New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$settingsPath = Join-Path $ResultsDirectory "$ShardName.runsettings"
$escapedFilter = [System.Security.SecurityElement]::Escape($filter)
@"
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <RunConfiguration>
    <TestCaseFilter>$escapedFilter</TestCaseFilter>
  </RunConfiguration>
</RunSettings>
"@ | Set-Content -LiteralPath $settingsPath -Encoding utf8

$arguments = @(
    "vstest",
    $AssemblyPath,
    "--Settings:$settingsPath",
    "--Logger:trx;LogFileName=$ShardName.trx",
    "--ResultsDirectory:$ResultsDirectory"
)

Write-Host "Running $ShardName with base predicate '$($suiteDefinition.baseFilter)' (sharding disabled: $DisableSharding)."
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet vstest failed for shard '$ShardName' with exit code $LASTEXITCODE."
}
