param(
    [Parameter(Position = 0)]
    [string]$Filter
)

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

    return "FullyQualifiedName~$normalizedValue"
}

$testArguments = @('.\\clio.mcp.e2e.csproj')
$testFilterExpression = Get-TestFilterExpression -Value $Filter

if ($null -ne $testFilterExpression) {
    $testArguments += @('--filter', $testFilterExpression)
}

Remove-Item -Path .\allure-report\* -Recurse -Force -ErrorAction SilentlyContinue;
Remove-Item -Path .\bin\Debug\net10.0\* -Recurse -Force -ErrorAction SilentlyContinue;
dotnet test @testArguments;
allure generate;
allure serve
