param(
    [Parameter(Mandatory = $true)]
    [string[]] $UnitTrx,

    [Parameter(Mandatory = $true)]
    [string[]] $IntegrationTrx,

    [string] $ManifestPath = "clio.tests/TestSharding/test-shards.json"
)

$ErrorActionPreference = "Stop"

function Get-FixtureDurations {
    param([Parameter(Mandatory = $true)][string[]] $TrxPath)

    $resolvedPaths = @($TrxPath | ForEach-Object { Resolve-Path -Path $_ } | ForEach-Object Path | Sort-Object -Unique)
    $secondsByFixture = @{}
    foreach ($path in $resolvedPaths) {
        [xml]$trx = Get-Content -LiteralPath $path -Raw
        if ($trx.TestRun.ResultSummary.outcome -ne "Completed" -or
            [int]$trx.TestRun.ResultSummary.Counters.failed -ne 0 -or
            [int]$trx.TestRun.ResultSummary.Counters.error -ne 0 -or
            [int]$trx.TestRun.ResultSummary.Counters.timeout -ne 0 -or
            [int]$trx.TestRun.ResultSummary.Counters.aborted -ne 0 -or
            [int]$trx.TestRun.ResultSummary.Counters.notRunnable -ne 0 -or
            [int]$trx.TestRun.ResultSummary.Counters.disconnected -ne 0) {
            throw "TRX '$path' is not a successful completed test run."
        }
        $namespace = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
        $namespace.AddNamespace("t", $trx.DocumentElement.NamespaceURI)

        $fixtureByTestId = @{}
        foreach ($definition in $trx.SelectNodes("//t:TestDefinitions/t:UnitTest", $namespace)) {
            $fixture = [string]$definition.TestMethod.className
            $fixtureByTestId[$definition.id] = $fixture
            if (-not $secondsByFixture.ContainsKey($fixture)) {
                $secondsByFixture[$fixture] = 0.0
            }
        }

        foreach ($result in $trx.SelectNodes("//t:Results/t:UnitTestResult", $namespace)) {
            $fixture = $fixtureByTestId[[string]$result.testId]
            if ([string]::IsNullOrWhiteSpace($fixture)) {
                continue
            }
            $duration = if ($result.duration) { [TimeSpan]::Parse([string]$result.duration).TotalSeconds } else { 0.0 }
            $secondsByFixture[$fixture] += $duration
        }
    }

    @($secondsByFixture.GetEnumerator() | ForEach-Object {
        [pscustomobject]@{ fixture = $_.Key; seconds = [double]$_.Value }
    })
}

function New-BalancedShards {
    param(
        [Parameter(Mandatory = $true)][object[]] $Fixtures,
        [Parameter(Mandatory = $true)][int] $Count,
        [Parameter(Mandatory = $true)][string] $Prefix
    )

    $bins = @(0..($Count - 1) | ForEach-Object {
        [pscustomobject]@{
            name = "$Prefix-$($_ + 1)"
            fixtures = [System.Collections.Generic.List[string]]::new()
            seconds = 0.0
        }
    })

    foreach ($fixture in $Fixtures | Sort-Object @{ Expression = "seconds"; Descending = $true }, fixture) {
        $target = $bins | Sort-Object seconds, @{ Expression = { $_.fixtures.Count } }, name | Select-Object -First 1
        $target.fixtures.Add([string]$fixture.fixture)
        $target.seconds += [double]$fixture.seconds
    }

    @($bins | ForEach-Object {
        [ordered]@{
            name = $_.name
            estimatedSeconds = [Math]::Round($_.seconds, 3)
            fixtures = @($_.fixtures | Sort-Object)
        }
    })
}

$resolvedUnitTrx = @($UnitTrx | ForEach-Object { Resolve-Path -Path $_ } | ForEach-Object Path | Sort-Object -Unique)
$resolvedIntegrationTrx = @($IntegrationTrx | ForEach-Object { Resolve-Path -Path $_ } | ForEach-Object Path | Sort-Object -Unique)
if ($resolvedUnitTrx.Count -ne 4 -or @($resolvedUnitTrx | Where-Object { (Split-Path -Leaf $_) -notmatch '^unit-[1-4]\.trx$' }).Count -gt 0) {
    throw "Unit timings must contain unit-1.trx through unit-4.trx."
}
if ($resolvedIntegrationTrx.Count -ne 3 -or @($resolvedIntegrationTrx | Where-Object { (Split-Path -Leaf $_) -notmatch '^integration-[1-3]\.trx$' }).Count -gt 0) {
    throw "Integration timings must contain integration-1.trx through integration-3.trx."
}

$manifest = [ordered]@{
    version = 1
    suites = [ordered]@{
        unit = [ordered]@{
            baseFilter = "Category!=Integration"
            shards = New-BalancedShards -Fixtures (Get-FixtureDurations $resolvedUnitTrx) -Count 4 -Prefix "unit"
        }
        integration = [ordered]@{
            baseFilter = "Category=Integration"
            shards = New-BalancedShards -Fixtures (Get-FixtureDurations $resolvedIntegrationTrx) -Count 3 -Prefix "integration"
        }
    }
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
Write-Host "Rebalanced test shards in $ManifestPath. Review and commit the manifest to apply the new distribution."
