[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $EnvironmentName,

    [Parameter(Mandatory = $true)]
    [uri] $EnvironmentUrl,

    [string] $SeedKeyPrefix = "LOCAL-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))",

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [ValidateSet("net8.0", "net10.0")]
    [string] $Framework = "net8.0",

    [string] $DatabaseProvider,

    [string] $LogFileName = "clio-mcp-e2e-local.trx"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
if ($EnvironmentName -notmatch '^issue-[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw "Destructive local E2E requires a dedicated issue-* environment name."
}

$workspacePath = "F:\Projects\Issue-Workspaces\$EnvironmentName"
$phaseOnePath = Join-Path $workspacePath "Phase1.yaml"
if (-not (Test-Path -LiteralPath $phaseOnePath -PathType Leaf)) {
    throw "Phase 1 workspace marker was not found: $phaseOnePath"
}

$clioSettingsPath = Join-Path $env:LOCALAPPDATA "creatio\clio\appsettings.json"
if (-not (Test-Path -LiteralPath $clioSettingsPath -PathType Leaf)) {
    throw "Clio settings were not found: $clioSettingsPath"
}
$clioSettings = Get-Content -LiteralPath $clioSettingsPath -Raw | ConvertFrom-Json
$environmentProperty = $clioSettings.Environments.PSObject.Properties[$EnvironmentName]
if ($null -eq $environmentProperty) {
    throw "Clio environment '$EnvironmentName' is not registered."
}
$registeredEnvironment = $environmentProperty.Value
if ($null -eq $registeredEnvironment -or [string]::IsNullOrWhiteSpace($registeredEnvironment.Uri)) {
    throw "Clio environment '$EnvironmentName' is not registered."
}
$registeredUri = [uri] $registeredEnvironment.Uri
$expectedUrl = $EnvironmentUrl.AbsoluteUri.TrimEnd("/")
$registeredUrl = $registeredUri.AbsoluteUri.TrimEnd("/")
if (-not $registeredUrl.Equals($expectedUrl, [StringComparison]::OrdinalIgnoreCase)) {
    throw "EnvironmentUrl must exactly match the registered URI for '$EnvironmentName'."
}
if ($EnvironmentUrl.Scheme -ne [Uri]::UriSchemeHttps -and -not $EnvironmentUrl.IsLoopback) {
    throw "Destructive local E2E requires HTTPS unless the registered environment is loopback."
}

$lockRoot = "F:\Projects\Issue-Workspaces\.locks"
[IO.Directory]::CreateDirectory($lockRoot) > $null
$hasher = [Security.Cryptography.SHA256]::Create()
try {
    $endpointHash = [BitConverter]::ToString(
        $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($registeredUrl.ToLowerInvariant())))
        .Replace("-", "").ToLowerInvariant()
}
finally {
    $hasher.Dispose()
}
$lockPath = Join-Path $lockRoot "clio-local-e2e-$endpointHash.lock"
$lockStream = $null
try {
    $lockStream = [IO.File]::Open($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $lockText = [Text.Encoding]::UTF8.GetBytes("$([Environment]::UserName) $PID $([DateTimeOffset]::UtcNow.ToString('O'))")
    $lockStream.Write($lockText, 0, $lockText.Length)
    $lockStream.Flush()
}
catch {
    $lockStream?.Dispose()
    throw "Could not acquire exclusive E2E ownership lock '$lockPath'. Remove it only after confirming no E2E run owns this workspace. $($_.Exception.Message)"
}

$variableNames = @(
    "McpE2E__AllowDestructiveMcpTests",
    "McpE2E__ClioProcessPath",
    "McpE2E__Sandbox__EnvironmentName",
    "McpE2E__Sandbox__EnvironmentUrl",
    "McpE2E__Sandbox__SeedKeyPrefix",
    "McpE2E__Sandbox__DatabaseProvider"
)
$previousValues = @{}
foreach ($name in $variableNames) {
    $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

try {
    $env:McpE2E__AllowDestructiveMcpTests = "true"
    Remove-Item Env:McpE2E__ClioProcessPath -ErrorAction SilentlyContinue
    $env:McpE2E__Sandbox__EnvironmentName = $EnvironmentName
    $env:McpE2E__Sandbox__EnvironmentUrl = $EnvironmentUrl.AbsoluteUri.TrimEnd("/")
    $env:McpE2E__Sandbox__SeedKeyPrefix = $SeedKeyPrefix
    if ([string]::IsNullOrWhiteSpace($DatabaseProvider)) {
        Remove-Item Env:McpE2E__Sandbox__DatabaseProvider -ErrorAction SilentlyContinue
    }
    else {
        $env:McpE2E__Sandbox__DatabaseProvider = $DatabaseProvider
    }

    Push-Location $repoRoot
    try {
        dotnet build .\clio.mcp.e2e\clio.mcp.e2e.csproj -c $Configuration -f $Framework
        if ($LASTEXITCODE -ne 0) {
            throw "clio.mcp.e2e build failed with exit code $LASTEXITCODE."
        }

        dotnet test .\clio.mcp.e2e\clio.mcp.e2e.csproj -c $Configuration -f $Framework --no-build `
            --logger "trx;LogFileName=$LogFileName"
        if ($LASTEXITCODE -ne 0) {
            throw "clio.mcp.e2e failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    foreach ($name in $variableNames) {
        [Environment]::SetEnvironmentVariable($name, $previousValues[$name], "Process")
    }
    $lockStream.Dispose()
    [IO.File]::Delete($lockPath)
}
