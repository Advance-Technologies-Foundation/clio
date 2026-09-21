param(
    [Parameter(Mandatory)][string]$HostDll,
    [Parameter(Mandatory)][string]$OldBundle,
    [Parameter(Mandatory)][string]$NewBundle,
    [Parameter(Mandatory)][string]$Target,
    [Parameter(Mandatory)][string]$Login,
    [string]$PackageName = 'Custom'
)
$ErrorActionPreference = 'Stop'
# Read-only proof on an explicitly supplied lab. Password is inherited from CLIO10_PASSWORD.
# This stages already published releases; NuGet acquisition is covered by AutomaticUpdateTests.
$proofRoot = Join-Path $PSScriptRoot ('../artifacts/package-runtime-proof-' + [Guid]::NewGuid().ToString('N'))
$cache = [IO.Directory]::CreateDirectory($proofRoot).FullName
$hostPath = (Resolve-Path -LiteralPath $HostDll).Path
$oldPath = (Resolve-Path -LiteralPath $OldBundle).Path
$newPath = (Resolve-Path -LiteralPath $NewBundle).Path
$hostHash = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash
Copy-Item -LiteralPath $oldPath -Destination (Join-Path $cache 'old') -Recurse
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @($hostPath, 'mcp', $Target, $Login, 'netcore')) { $start.ArgumentList.Add($argument) }
$start.Environment['CLIO10_RUNTIME_COMPOSITION'] = 'true'
$start.Environment['CLIO10_BUNDLES'] = $cache
foreach ($name in @('CLIO10_PRIMITIVE_VERSION', 'CLIO10_SETTINGS', 'CLIO10_UPDATE_SOURCE')) { [void]$start.Environment.Remove($name) }
$process = [Diagnostics.Process]::Start($start)
$errors = $process.StandardError.ReadToEndAsync()
function Request([int]$id, [string]$method, [hashtable]$parameters) {
    $process.StandardInput.WriteLine((@{jsonrpc='2.0'; id=$id; method=$method; params=$parameters} | ConvertTo-Json -Depth 20 -Compress))
    $process.StandardInput.Flush()
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        $line = $process.StandardOutput.ReadLineAsync().WaitAsync($deadline - [DateTime]::UtcNow).GetAwaiter().GetResult()
        if ($null -eq $line) { throw 'MCP process ended before replying' }
        $message = $line | ConvertFrom-Json
        if ($message.id -eq $id) {
            if ($message.error) { throw 'MCP request failed' }
            return $message.result
        }
    }
    throw 'MCP request timed out'
}
try {
    $initialPid = $process.Id
    $null = Request 1 'initialize' @{protocolVersion='2025-03-26'; capabilities=@{}; clientInfo=@{name='package-runtime-proof';version='1'}}
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $process.StandardInput.Flush()
    $before = Request 2 'tools/call' @{name='list-operations';arguments=@{}}
    $beforeCatalog = $before.content[0].text | ConvertFrom-Json
    if ($beforeCatalog.Name -contains 'remove-package-dependency') { throw 'Old release already contains the new workflow' }
    # Publish only a complete directory, so discovery never sees a partly copied release.
    $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts')) + [IO.Path]::DirectorySeparatorChar
    $staged = [IO.Path]::GetFullPath((Join-Path $artifactsRoot ('package-runtime-staging-' + [Guid]::NewGuid().ToString('N'))))
    $destination = [IO.Path]::GetFullPath((Join-Path $cache 'new'))
    foreach ($path in @($staged, $destination)) {
        if (-not $path.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Move target escaped artifacts directory' }
    }
    Copy-Item -LiteralPath $newPath -Destination $staged -Recurse
    Move-Item -LiteralPath $staged -Destination $destination
    $after = Request 3 'tools/call' @{name='list-operations';arguments=@{}}
    $afterCatalog = $after.content[0].text | ConvertFrom-Json
    if ($afterCatalog.Name -notcontains 'remove-package-dependency') { throw 'Running host did not discover new workflow' }
    $execution = Request 4 'tools/call' @{name='execute';arguments=@{
        operation='remove-package-dependency';arguments=@{'package-name'=$PackageName;'dependencies'=('Clio10Absent_' + [Guid]::NewGuid().ToString('N'))}
    }}
    $result = $execution.content[0].text | ConvertFrom-Json
    if (-not $result.Accepted -or $result.Payload.changedCount -ne 0 -or @($result.AcceptedSteps).Count -ne 0) {
        throw 'The new workflow did not complete the expected no-op'
    }
    if ($process.HasExited -or $process.Id -ne $initialPid -or (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash -ne $hostHash) {
        throw 'Host process or executable changed'
    }
    $receipt = [ordered]@{hostPid=$initialPid; hostUnchanged=$true; beforeOperations=@($beforeCatalog).Count;
        afterOperations=@($afterCatalog).Count; runtimeVersion=$result.PrimitiveVersion; operation='remove-package-dependency';
        changedCount=$result.Payload.changedCount; acceptedSteps=@($result.AcceptedSteps); cache=$cache}
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $cache 'proof.json')
    $receipt | ConvertTo-Json -Depth 6
}
finally {
    if (-not $process.HasExited) { $process.Kill($true) }
    $process.WaitForExit()
    $null = $errors.GetAwaiter().GetResult()
    $process.Dispose()
}
