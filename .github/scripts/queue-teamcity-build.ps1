# Queues the TeamCity MCP e2e build for a clio branch from a self-hosted runner.
#
# ASCII only: Windows PowerShell 5.1 reads this file as ANSI unless it carries a BOM,
# so a non-ASCII character (an em dash, an arrow) turns into an "Unexpected token"
# parse error at run time - see PR #832.
#
# Consumed environment (set by .github/workflows/teamcity-mcp-e2e.yml):
#   TC_URL, TC_TOKEN, BUILD_TYPE, HEAD_REF, HEAD_SHA, PR_NUMBER,
#   START_DELAY_SECONDS, RUNNER_LABEL
#
# Step outputs:
#   runner - the runner this attempt ran on, so the gate job can name it.
#   queued - "true" when a build for HEAD_REF is queued (by this attempt or by a
#            sibling attempt), "false" when this runner has no route to TeamCity.
#
# Exit code is 0 for "no route" on purpose: that is a runner problem which a sibling
# attempt on an in-network runner can still cover, and the gate job is what turns
# "no attempt got through" into a failure. A genuine TeamCity error (bad token,
# rejected request, no build id) still throws and fails this job.
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

$startDelaySeconds = 0
if (-not [int]::TryParse($env:START_DELAY_SECONDS, [ref]$startDelaySeconds)) { $startDelaySeconds = 0 }
if ($startDelaySeconds -gt 0) {
  Write-Host "Waiting $startDelaySeconds s so a faster sibling attempt can queue the build first."
  Start-Sleep -Seconds $startDelaySeconds
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
try { Add-Type -AssemblyName System.Net.Http -ErrorAction Stop } catch { }
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

# 2. Is a build for this branch already queued? Both attempts run against the same
# branch, so without this check two healthy runners would queue two ~45-min
# full-Creatio builds. A queued build found here counts as success for this attempt.
$queueUri = "$env:TC_URL/app/rest/buildQueue" `
  + "?locator=buildType:$($env:BUILD_TYPE),count:100" `
  + "&fields=build(id,webUrl,properties(property(name,value)))"
$existing = Invoke-RestMethod -Method Get -Uri $queueUri -Headers $authHeaders -TimeoutSec 60
foreach ($queuedBuild in @($existing.build)) {
  $branchProperty = @($queuedBuild.properties.property) | Where-Object { $_.name -eq 'BranchNameClio' }
  if ($null -ne $branchProperty -and $branchProperty.value -eq $env:HEAD_REF) {
    Write-Host "A build for BranchNameClio=$env:HEAD_REF is already queued (#$($queuedBuild.id)): $($queuedBuild.webUrl)"
    Write-Host 'Not queueing a second one.'
    Set-QueuedOutput 'true'
    exit 0
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
