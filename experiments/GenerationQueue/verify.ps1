# Reproduce the local fixture evidence; does not publish or touch Creatio.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$probeRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $probeRoot '../..')).Path
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$bundle = Join-Path $probeRoot "evidence/$stamp"
New-Item -ItemType Directory -Path $bundle | Out-Null
$artifactRoot = Join-Path $probeRoot 'artifacts'
$before = @(Get-ChildItem -LiteralPath $artifactRoot -Directory -Recurse -ErrorAction SilentlyContinue | ForEach-Object FullName)
$previousMutation = $env:QUEUE_PROBE_MUTATION
$exitCodes = [ordered]@{}
Push-Location $repoRoot
try {
    Remove-Item Env:\QUEUE_PROBE_MUTATION -ErrorAction SilentlyContinue
    dotnet --info | Set-Content (Join-Path $bundle 'dotnet-info.txt')
    git rev-parse HEAD | Set-Content (Join-Path $bundle 'base-commit.txt')
    git status --short | Set-Content (Join-Path $bundle 'worktree-status.txt')
    $project = Join-Path $probeRoot 'Tests/Tests.csproj'
    dotnet test $project -c Release --logger 'trx;LogFileName=normal.trx' --results-directory $bundle *> (Join-Path $bundle 'normal.log')
    $exitCodes.normal = $LASTEXITCODE
    if ($exitCodes.normal -ne 0) { throw 'Normal suite failed. See normal.log.' }

    $env:QUEUE_PROBE_MUTATION = 'unsafe-fallback'
    dotnet test $project -c Release --no-build --filter 'Name=Drain_timeout_does_not_retag_new_requests' --logger 'trx;LogFileName=mutation.trx' --results-directory $bundle *> (Join-Path $bundle 'mutation.log')
    $exitCodes.mutation = $LASTEXITCODE
    [xml]$mutation = Get-Content -LiteralPath (Join-Path $bundle 'mutation.trx') -Raw
    if ($exitCodes.mutation -eq 0 -or $mutation.TestRun.ResultSummary.Counters.failed -ne '1' -or
        $mutation.TestRun.Results.UnitTestResult.Output.ErrorInfo.Message -notmatch 'NotStarted') {
        throw 'Mutation was not rejected by the intended queue-state oracle.'
    }

    Remove-Item Env:\QUEUE_PROBE_MUTATION
    dotnet test $project -c Release --no-build --filter 'Name=Drain_timeout_does_not_retag_new_requests' --logger 'trx;LogFileName=restored.trx' --results-directory $bundle *> (Join-Path $bundle 'restored.log')
    $exitCodes.restored = $LASTEXITCODE
    if ($exitCodes.restored -ne 0) { throw 'Restored safety rule did not pass.' }
}
finally {
    if ($null -eq $previousMutation) { Remove-Item Env:\QUEUE_PROBE_MUTATION -ErrorAction SilentlyContinue }
    else { $env:QUEUE_PROBE_MUTATION = $previousMutation }
    $exitCodes | ConvertTo-Json | Set-Content (Join-Path $bundle 'exit-codes.json')
    $runDirs = @(Get-ChildItem -LiteralPath $artifactRoot -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notin $before -and (Test-Path -LiteralPath (Join-Path $_.FullName 'supervisor-pid.txt')) })
    foreach ($dir in $runDirs) {
        $relative = [IO.Path]::GetRelativePath($artifactRoot, $dir.FullName)
        $target = Join-Path $bundle "sessions/$relative"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Get-ChildItem -LiteralPath $dir.FullName -File | Copy-Item -Destination $target
    }
    $sourceFiles = @('Directory.Build.props', 'Directory.Packages.props',
        'experiments/GenerationQueue/Probe/Probe.csproj', 'experiments/GenerationQueue/Probe/Program.cs',
        'experiments/GenerationQueue/Tests/Tests.csproj', 'experiments/GenerationQueue/Tests/QueueTests.cs',
        'experiments/GenerationQueue/verify.ps1', 'experiments/GenerationQueue/README.md')
    $sourceFiles | ForEach-Object {
        [pscustomobject]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot $_) -Algorithm SHA256).Hash }
    } | ConvertTo-Json | Set-Content (Join-Path $bundle 'source-hashes.json')
    Pop-Location
    Write-Output "Evidence: $bundle"
}
Write-Output 'PASS: normal suite, rejected mutation, restored rule; see raw logs and transcripts.'
