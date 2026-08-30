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

    $secondsByFixture = @{}
    foreach ($path in $TrxPath) {
        [xml]$trx = Get-Content -LiteralPath $path -Raw
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

$manifest = [ordered]@{
    version = 1
    suites = [ordered]@{
        unit = [ordered]@{
            baseFilter = "Category!=Integration"
            shards = New-BalancedShards -Fixtures (Get-FixtureDurations $UnitTrx) -Count 4 -Prefix "unit"
        }
        integration = [ordered]@{
            baseFilter = "Category=Integration"
            shards = New-BalancedShards -Fixtures (Get-FixtureDurations $IntegrationTrx) -Count 3 -Prefix "integration"
        }
    }
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
Write-Host "Rebalanced test shards in $ManifestPath. Review and commit the manifest to apply the new distribution."
