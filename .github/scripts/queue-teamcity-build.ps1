# Queues the TeamCity MCP e2e build for a clio branch from a self-hosted runner.
#
# ASCII only: Windows PowerShell 5.1 reads this file as ANSI unless it carries a BOM,
# so a non-ASCII character (an em dash, an arrow) turns into an "Unexpected token"
# parse error at run time - see PR #832.
#
# Consumed environment (set by .github/workflows/teamcity-mcp-e2e.yml):
#   TC_URL, TC_TOKEN, BUILD_TYPE, HEAD_REF, HEAD_SHA, PR_NUMBER, RUNNER_LABEL
#
# Step outputs:
#   runner - the runner this attempt ran on, so the gate job can name it.
#   queued - "true" when a build for HEAD_REF is queued or already running,
#            "false" when this runner has no route to TeamCity.
#
# Exit code is 0 for "no route" on purpose: that is a runner problem which the next
# attempt job - scheduled on whatever runner is free - can still cover, and the gate
# job is what turns "no attempt got through" into a failure. A genuine TeamCity error
# (bad token, rejected request, no build id) still throws and fails this job, which
# stops the retry chain instead of repeating a request that will not work.
$ErrorActionPreference = 'Stop'

$runnerName = if ([string]::IsNullOrWhiteSpace($env:RUNNER_LABEL)) { $env:COMPUTERNAME } else { $env:RUNNER_LABEL }
Add-Content -Path $env:GITHUB_OUTPUT -Value "runner=$runnerName"
Write-Host "Runner: $runnerName"

function Set-QueuedOutput([string]$Value) {
  Add-Content -Path $env:GITHUB_OUTPUT -Value "queued=$Value"
}

if ([string]::IsNullOrWhiteSpace($env:TC_TOKEN)) {
  throw 'TEAMCITY_TOKEN secret is not set - cannot trigger TeamCity.'
}

$authHeaders = @{ Authorization = "Bearer $env:TC_TOKEN"; Accept = 'application/json' }

# 1. Preflight WITHOUT the token. teamcity-rnd.bpmonline.com resolves to the TeamCity
# server only from inside the corporate network; from anywhere else public DNS sends it
# to the bpmonline.com web tier, whose nginx answers /app/rest/... with a bare 404. Only
# the real server sets the TeamCity-Node-Id response header (it is present on the 401
# that an unauthenticated /app/rest/server returns), so that header - not the status
# code - is the discriminator. Proving the target BEFORE attaching the Authorization
# header is what keeps the corporate CI token from being posted to a public host.
#
# HttpClient rather than Invoke-WebRequest: the probe EXPECTS a non-2xx (an
# unauthenticated /app/rest/server answers 401), and Invoke-WebRequest turns that into a
# terminating error whose type differs between Windows PowerShell 5.1
# (System.Net.WebException) and PowerShell 7 (Microsoft.PowerShell.Commands.
# HttpResponseException), with the response headers exposed differently on each.
# HttpClient never throws on a status code, so this reads the same on both hosts.
try {
  Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
}
catch {
  # Expected on PowerShell 7, where System.Net.Http is already part of the loaded framework and
  # Add-Type refuses it. Not fatal, and not silent either: the New-Object below is what actually
  # decides whether the type is available, and it throws with its own message if it is not.
  Write-Host "::debug::Add-Type System.Net.Http was not needed: $($_.Exception.Message)"
}
$targetIsTeamCity = $false
$probeStatus = 'no response'
$httpClient = New-Object System.Net.Http.HttpClient
try {
  $httpClient.Timeout = [TimeSpan]::FromSeconds(30)
  $probe = $httpClient.GetAsync("$env:TC_URL/app/rest/server").GetAwaiter().GetResult()
  $probeStatus = [int]$probe.StatusCode
  $targetIsTeamCity = $probe.Headers.Contains('TeamCity-Node-Id')
  $probe.Dispose()
}
catch {
  $probeStatus = "request failed: $($_.Exception.Message)"
}
finally {
  $httpClient.Dispose()
}
Write-Host "Preflight (no token sent): $env:TC_URL/app/rest/server -> $probeStatus, TeamCity-Node-Id header present: $targetIsTeamCity"

if (-not $targetIsTeamCity) {
  $targetHost = ([uri]$env:TC_URL).Host
  $resolved = 'unresolved'
  try {
    $resolved = (([System.Net.Dns]::GetHostAddresses($targetHost)) | ForEach-Object { $_.IPAddressToString }) -join ', '
  }
  catch {
    $resolved = "DNS lookup failed: $($_.Exception.Message)"
  }
  Write-Host "::warning::Runner '$runnerName' has no route to TeamCity - $targetHost resolves to $resolved, which did not answer as TeamCity. No token was sent and no build was queued from this runner."
  Set-QueuedOutput 'false'
  exit 0
}

Write-Host "Preflight passed: $env:TC_URL answered as TeamCity."

# 2. Is a build for this exact commit already queued or running? The retry chain is
# sequential, so it cannot itself produce a duplicate; this guards the other route to
# one - a re-run of this workflow, or an automation re-running a red check, while the
# build it queued before is still going (that produced two concurrent full-Creatio
# builds on run 31489635011).
# The match key is the COMMIT, not the branch. Both states have to be inspected -
# with moveToTop and a free agent a build leaves the queue within seconds, so a
# queue-only check reads as "nothing there". But a RUNNING build has already checked
# its sources out, so it cannot stand in for a newer commit: keying on the branch
# would silently let a push be reported by an older build. The commit travels in the
# build comment (BranchNameClio carries only the branch), so that is what is compared.
# workflow_dispatch has no commit, and falls back to matching the branch.
$branchFields = 'build(id,webUrl,state,comment(text),properties(property(name,value)))'
$lookups = @(
  "$env:TC_URL/app/rest/buildQueue?locator=buildType:$($env:BUILD_TYPE),count:100&fields=$branchFields",
  "$env:TC_URL/app/rest/builds?locator=buildType:$($env:BUILD_TYPE),running:true,count:50&fields=$branchFields"
)
$matchesThisRun = {
  param($candidate)
  $branchProperty = @($candidate.properties.property) | Where-Object { $_.name -eq 'BranchNameClio' }
  if ($null -eq $branchProperty -or $branchProperty.value -ne $env:HEAD_REF) { return $false }
  if ([string]::IsNullOrWhiteSpace($env:HEAD_SHA)) { return $true }
  $commentText = ''
  if ($null -ne $candidate.comment) { $commentText = [string]$candidate.comment.text }
  return $commentText.Contains($env:HEAD_SHA)
}
foreach ($lookupUri in $lookups) {
  $found = Invoke-RestMethod -Method Get -Uri $lookupUri -Headers $authHeaders -TimeoutSec 60
  foreach ($existingBuild in @($found.build)) {
    if (& $matchesThisRun $existingBuild) {
      Write-Host "A build for this commit on $env:HEAD_REF is already $($existingBuild.state) (#$($existingBuild.id)): $($existingBuild.webUrl)"
      Write-Host 'Not queueing a second one.'
      Set-QueuedOutput 'true'
      exit 0
    }
  }
}

# 3. Queue it. The e2e config resolves its checkout from BranchNameClio
# (root branch = refs/heads/%BranchNameClio%). This works for same-repo PR heads (they
# exist as refs/heads/<head.ref>); fork PRs are filtered out by the job-level `if:` in
# the workflow, so BranchNameClio is always a branch that exists in the main repo.
$shaSuffix = if ([string]::IsNullOrWhiteSpace($env:HEAD_SHA)) { '' } else { " @ $env:HEAD_SHA" }
$body = @{
  buildType  = @{ id = $env:BUILD_TYPE }
  properties = @{ property = @(
    @{ name = 'BranchNameClio';                        value = $env:HEAD_REF }
    @{ name = 'DeployCreatioBuild';                    value = 'true' }
    @{ name = 'env.McpE2E__AllowDestructiveMcpTests';  value = 'true' }
    @{ name = 'ProductName';                           value = 'Studio' }
  ) }
  comment    = @{ text = "clio PR #$env:PR_NUMBER ($env:HEAD_REF$shaSuffix) - MCP e2e via GitHub Actions" }
} | ConvertTo-Json -Depth 6

# moveToTop: jump the queue so the advisory e2e starts promptly rather than waiting
# behind unrelated builds (needs "Reorder builds in queue").
$response = Invoke-RestMethod -Method Post -Uri "$env:TC_URL/app/rest/buildQueue?moveToTop=true" `
  -Headers $authHeaders -ContentType 'application/json' -Body $body -TimeoutSec 60

# A 2xx with an unexpected body must not read as success - require a build id.
if (-not $response.id) {
  throw 'TeamCity accepted the request but returned no build id - the trigger likely failed.'
}

Set-QueuedOutput 'true'
Write-Host "Queued TeamCity build #$($response.id) on BranchNameClio=$env:HEAD_REF"
Write-Host "Build: $($response.webUrl)"
Write-Host "TeamCity's Commit Status Publisher will post the result onto the PR head commit."
