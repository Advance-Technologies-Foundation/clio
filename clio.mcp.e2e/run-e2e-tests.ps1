param(
    [Parameter(Position = 0)]
    [string]$Filter
)

function Get-TestFixtureName {
    param(
        [string]$Value
    )

    $normalizedValue = $Value.Trim()

    if ($normalizedValue -match '\.') {
        return $normalizedValue
    }

    if ($normalizedValue.EndsWith('ToolE2ETests', [System.StringComparison]::Ordinal)) {
        return "Clio.Mcp.E2E.$normalizedValue"
    }

    if ($normalizedValue.EndsWith('E2ETests', [System.StringComparison]::Ordinal)) {
        return "Clio.Mcp.E2E.$normalizedValue"
    }

    return "Clio.Mcp.E2E.$($normalizedValue)ToolE2ETests"
}

function Get-TestFilterExpression {
    param(
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $normalizedValue = $Value.Trim()
    $looksLikeFilterExpression =
        $normalizedValue -match '[()&|=~!]' -or
        $normalizedValue -match '^(FullyQualifiedName|Name|Category|TestCategory|Priority|ClassName)\b'

    if ($looksLikeFilterExpression) {
        return $normalizedValue
    }

    $fixtureName = Get-TestFixtureName -Value $normalizedValue
    return "FullyQualifiedName~$fixtureName"
}

$testArguments = @('.\\clio.mcp.e2e.csproj')
$testFilterExpression = Get-TestFilterExpression -Value $Filter

if ($null -ne $testFilterExpression) {
    $testArguments += @('--filter', $testFilterExpression)
}

Remove-Item -Path .\allure-report\* -Recurse -Force -ErrorAction SilentlyContinue;
Remove-Item -Path .\bin\Debug\net8.0\* -Recurse -Force -ErrorAction SilentlyContinue;
dotnet test @testArguments;
allure generate;
allure serve
