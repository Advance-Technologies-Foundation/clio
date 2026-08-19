#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Claim a GitHub issue before starting work on it, exclusively.
.DESCRIPTION
    PowerShell twin of scripts/claim-issue.sh, implementing the same protocol — read that
    file's header for the reasoning. In short:

    Several scheduled agents run against this repository in parallel, often under the SAME
    GitHub identity, so "is this assigned to my login?" cannot arbitrate between them, and
    neither can a check-then-assign or a post-then-read on comments. Arbitration uses the one
    compare-and-swap primitive GitHub offers: a ref update. `git push --force-with-lease=<ref>:`
    with an empty expected value creates the ref only if it does not exist, checked inside the
    server's atomic ref transaction, so exactly one racing run wins.

    The script exits 0 only when the claim ref is ours, the issue is assigned to us and a
    re-read confirms it, and the machine-readable marker comment is present. Anything else
    releases the ref and exits non-zero — an unresolved ownership must never be reported as a
    successful claim. A partial state from an earlier run is repaired, not short-circuited.

    Every native `gh` invocation goes through Invoke-Gh, which fails closed. This matters on
    PowerShell: $ErrorActionPreference = 'Stop' does NOT make a failing native command
    terminating unless $PSNativeCommandUseErrorActionPreference is enabled, which is false by
    default on pwsh 7.6. Both are used here — the preference where the host supports it, plus
    an unconditional $LASTEXITCODE check that does not depend on the host version.
.PARAMETER IssueNumber
    Number of the GitHub issue to claim.
.PARAMETER Branch
    Working branch to record in the claim. Required in practice: claiming happens before the
    branch is created, so HEAD is still the default branch and must not be published as the
    working branch.
.PARAMETER Status
    Print the current claim state for the issue and exit.
.PARAMETER Release
    Release a claim held by this run (see CLIO_CLAIM_ID).
.PARAMETER Force
    With -Release, break a claim held by a different run.
.EXAMPLE
    ./scripts/claim-issue.ps1 -IssueNumber 1234 -Branch feature/1234-do-the-thing
.EXAMPLE
    ./scripts/claim-issue.ps1 -IssueNumber 1234 -Release
.NOTES
    Environment: CLIO_CLAIM_ID pins the identity of one logical run, so a retry converges on
    its own claim instead of being refused by it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^[0-9]+$')]
    [string]$IssueNumber,

    [Parameter(Position = 1)]
    [string]$Branch,

    [switch]$Status,
    [switch]$Release,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
# Enabled where the host knows it; the explicit $LASTEXITCODE checks in Invoke-Gh do not rely on it.
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $global:PSNativeCommandUseErrorActionPreference = $true
}

$ExitLost = 1      # somebody else holds the claim, or ownership is unresolved
$ExitUsage = 2
$ExitPrereq = 3
$MarkerPrefix = '<!-- clio-claim-id:'
$ClaimRef = "refs/claims/issue-$IssueNumber"
$PeekRef = "refs/clio-claim-peek/issue-$IssueNumber"

function Write-Err { param([string[]]$Lines) foreach ($l in $Lines) { [Console]::Error.WriteLine($l) } }

function Get-NormalizedRemote {
    <# owner/name out of any remote URL shape: the last two path segments once ':' is a separator. #>
    param([string]$Url)
    $u = $Url -replace '\.git$', '' -replace '/$', ''
    $u = $u -replace ':', '/'
    $parts = @($u -split '/' | Where-Object { $_ })
    if ($parts.Count -lt 2) { return $u }
    return "$($parts[-2])/$($parts[-1])"
}

function Invoke-Gh {
    <# Runs gh and fails closed. A native failure must never hand the caller an empty string
       that reads as "no assignees" or "no comments". #>
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GhArgs)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # let us inspect the exit code instead of throwing mid-pipe
    try {
        $output = & gh @GhArgs 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) {
        throw "gh $($GhArgs -join ' ') failed with exit code ${code}: $($output -join [Environment]::NewLine)"
    }
    return ($output | ForEach-Object { [string]$_ })
}

function Invoke-GitQuiet {
    <# Returns $true on success, $false on failure — for the git calls whose failure is a
       meaningful outcome (the compare-and-swap push, the release). #>
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git @GitArgs 2>&1 | Out-Null
        return ($LASTEXITCODE -eq 0)
    }
    finally { $ErrorActionPreference = $previous }
}

function Get-RemoteClaimSha {
    <# Returns the sha, or '' when the ref is genuinely absent. THROWS when the remote could not
       be read: stderr suppression plus an unchecked $LASTEXITCODE is how a network or auth
       failure came back as "not claimed", which then reports a live claim as cleaned up. #>
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & git ls-remote origin $ClaimRef 2>&1
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) {
        throw "Could not read $ClaimRef from origin (exit ${code}): $($lines -join ' '). Failing closed - an unreadable claim is not an absent claim."
    }
    $first = @($lines | Where-Object { $_ -match '\S' }) | Select-Object -First 1
    if (-not $first) { return '' }
    return (([string]$first) -split '\s+')[0]
}

$script:ClaimObjectFetched = $false
function Get-ClaimField {
    param([string]$Field, [string]$Sha)
    if (-not $script:ClaimObjectFetched) {
        if (-not (Invoke-GitQuiet 'fetch' '--quiet' '--no-tags' 'origin' "+${ClaimRef}:${PeekRef}")) { return $null }
        $script:ClaimObjectFetched = $true
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $body = & git log -1 --format=%B $Sha 2>$null } finally { $ErrorActionPreference = $previous }
    $match = @($body | Where-Object { $_ -like "${Field}: *" }) | Select-Object -First 1
    if (-not $match) { return $null }
    return $match.Substring($Field.Length + 2)
}

function Format-RemoteClaim {
    param([string]$Sha)
    $id = Get-ClaimField -Field 'claim-id' -Sha $Sha
    $who = Get-ClaimField -Field 'claimant' -Sha $Sha
    $br = Get-ClaimField -Field 'branch' -Sha $Sha
    $at = Get-ClaimField -Field 'created-at' -Sha $Sha
    $unknown = '<unknown>'
    return "claim-id=$(if ($id) { $id } else { '<unreadable>' }) claimant=$(if ($who) { $who } else { $unknown }) branch=$(if ($br) { $br } else { $unknown }) created-at=$(if ($at) { $at } else { $unknown }) ref=$Sha"
}

function Remove-Claim {
    param([string]$ExpectedSha)
    return (Invoke-GitQuiet 'push' '--quiet' "--force-with-lease=${ClaimRef}:${ExpectedSha}" 'origin' ":$ClaimRef")
}

foreach ($tool in @('git', 'gh')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Err "$tool is required but was not found in PATH."
        exit $ExitPrereq
    }
}
if (-not (Invoke-GitQuiet 'rev-parse' '--git-dir')) {
    Write-Err 'Not inside a git repository.'
    exit $ExitPrereq
}

# `gh` decides its own repository (remotes, GH_REPO, a configured default) while the claim ref is
# pushed to `origin`. If those disagree, the lock arbitrates one repository and the issue lives in
# another - two forks can each win their own CAS and both act on one upstream issue.
try {
    $CanonicalRepo = (Invoke-Gh 'repo' 'view' '--json' 'nameWithOwner' '-q' '.nameWithOwner' | Select-Object -First 1)
}
catch {
    Write-Err $_.Exception.Message
    exit $ExitPrereq
}
if (-not $CanonicalRepo) {
    Write-Err 'Could not resolve the GitHub repository for this checkout.'
    exit $ExitPrereq
}
$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
# The CONFIGURED url, not `git remote get-url`, which applies url.<base>.insteadOf rewriting -
# that rewrite is transport, not identity, and would compare a local mirror path here.
try { $originUrl = (& git config --get remote.origin.url 2>$null | Select-Object -First 1) } finally { $ErrorActionPreference = $previous }
if (-not $originUrl) {
    Write-Err "No 'origin' remote: the claim ref has nowhere to live."
    exit $ExitPrereq
}
$originRepo = Get-NormalizedRemote -Url $originUrl
if ($originRepo.ToLowerInvariant() -ne $CanonicalRepo.ToLowerInvariant()) {
    Write-Err @(
        'The claim lock and the issue would target different repositories, so the claim would not arbitrate anything.',
        "  gh resolves:      $CanonicalRepo",
        "  origin remote is: $originRepo ($originUrl)",
        "Failing closed. Point origin at $CanonicalRepo, or set GH_REPO to the repository origin points at."
    )
    exit $ExitPrereq
}

function Invoke-GhIssue {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$IssueArgs)
    $verb = $IssueArgs[0]
    $rest = @($IssueArgs | Select-Object -Skip 1)
    return (Invoke-Gh (@('issue', $verb, $IssueNumber, '--repo', $CanonicalRepo) + $rest))
}

# ── -Status ───────────────────────────────────────────────────────────────
if ($Status) {
    try {
        $sha = Get-RemoteClaimSha
        if (-not $sha) { Write-Host "Issue #$IssueNumber is not claimed ($ClaimRef does not exist)." }
        else { Write-Host "Issue #$IssueNumber is claimed: $(Format-RemoteClaim -Sha $sha)" }
        $assignees = @(Invoke-GhIssue 'view' '--json' 'assignees' '-q' '.assignees[].login' | Where-Object { $_ })
        Write-Host "Assignees: $(if ($assignees.Count) { $assignees -join ' ' } else { '<none>' })"
    }
    catch {
        Write-Err $_.Exception.Message
        exit $ExitLost
    }
    exit 0
}

# The identity of this run. When CLIO_CLAIM_ID is unset the generated value is recorded under
# .git on a successful claim, so the documented plain -Release from the same checkout knows what it
# owns - otherwise every release would generate a new id, compare it to the stored one and refuse.
$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try { $gitCommonDir = (& git rev-parse --git-common-dir 2>$null | Select-Object -First 1) } finally { $ErrorActionPreference = $previous }
if (-not $gitCommonDir) { $gitCommonDir = '.git' }
$TokenFile = Join-Path (Join-Path $gitCommonDir 'clio-claims') "issue-$IssueNumber"

$claimId = $env:CLIO_CLAIM_ID
$claimIdSource = 'env'
if (-not $claimId -and $Release -and (Test-Path -LiteralPath $TokenFile)) {
    $claimId = (Get-Content -LiteralPath $TokenFile -Raw).Trim()
    $claimIdSource = 'recorded'
}
if (-not $claimId) {
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $claimId = "$([System.Net.Dns]::GetHostName())-$PID-$stamp-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $claimIdSource = 'generated'
}

# ── -Release ──────────────────────────────────────────────────────────────
if ($Release) {
    try { $sha = Get-RemoteClaimSha }
    catch {
        Write-Err $_.Exception.Message
        exit $ExitLost
    }
    if (-not $sha) {
        Remove-Item -LiteralPath $TokenFile -Force -ErrorAction SilentlyContinue
        Write-Host "Issue #$IssueNumber is not claimed - nothing to release."
        exit 0
    }
    $ownerId = Get-ClaimField -Field 'claim-id' -Sha $sha
    if ($ownerId -ne $claimId -and -not $Force) {
        $lines = @(
            "Issue #$IssueNumber is claimed by somebody else: $(Format-RemoteClaim -Sha $sha)",
            "Refusing to release a claim this run does not own (claim id from: $claimIdSource)."
        )
        if ($claimIdSource -eq 'generated') {
            $lines += 'No claim was recorded for this checkout, so the holder is another checkout or another machine.'
            $lines += 'Release it from there, or pass its CLIO_CLAIM_ID.'
        }
        $lines += 'If the holder is gone (check created-at above), break it deliberately with -Release -Force.'
        Write-Err $lines
        exit $ExitLost
    }
    if (Remove-Claim -ExpectedSha $sha) {
        Remove-Item -LiteralPath $TokenFile -Force -ErrorAction SilentlyContinue
        Write-Host "Released the claim on issue #$IssueNumber."
        exit 0
    }
    Write-Err "Could not release the claim on issue #$IssueNumber - it changed under us, re-run to see the current holder."
    exit $ExitLost
}

# ── acquire ───────────────────────────────────────────────────────────────
# The marker comment names the working branch, and the policy is to claim BEFORE creating the
# branch — so the branch cannot be read off HEAD, which is still the default branch then.
$defaultBranch = (Invoke-Gh 'repo' 'view' '--repo' $CanonicalRepo '--json' 'defaultBranchRef' '-q' '.defaultBranchRef.name' | Select-Object -First 1)
if (-not $Branch) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $Branch = (& git rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1) } finally { $ErrorActionPreference = $previous }
}
if (-not $Branch -or $Branch -eq 'HEAD' -or $Branch -eq $defaultBranch) {
    Write-Err @(
        "Refusing to claim issue #$IssueNumber without a working branch name.",
        "Claiming happens before the branch is created, so HEAD is still '$(if ($Branch) { $Branch } else { '<detached>' })' and would be",
        'published as the working branch in the claim comment. Pass the branch you are about to create:',
        "    ./scripts/claim-issue.ps1 -IssueNumber $IssueNumber -Branch <planned-branch-name>"
    )
    exit $ExitUsage
}

$me = (Invoke-Gh 'api' 'user' '-q' '.login' | Select-Object -First 1)
if (-not $me) {
    Write-Err 'Could not resolve the authenticated gh user.'
    exit $ExitLost
}

# Only the invocation that CREATED the ref cleans it up. A run that adopted an existing claim
# (same CLIO_CLAIM_ID) may be racing the worker that created it - a replayed id is not proof of
# ownership - so deleting that ref here would hand a live claim to a third agent.
$createdClaim = $false
$claimComplete = $false
$claimSha = $null

function Complete-Run {
    param([int]$Code)
    if ($Code -ne 0 -and $createdClaim -and -not $claimComplete) {
        if (Remove-Claim -ExpectedSha $claimSha) {
            Write-Err "Released the claim ref on issue #$IssueNumber - the claim did not complete, so the issue stays free."
        }
        else {
            Write-Err "WARNING: could not release $ClaimRef after a failed claim. Run: ./scripts/claim-issue.ps1 -IssueNumber $IssueNumber -Release"
        }
    }
    exit $Code
}

# An empty FILE rather than piping '' into --stdin: the PowerShell pipeline appends a newline,
# and a one-byte tree object is corrupt, so `git hash-object -t tree` rejects it. A temporary
# empty file also avoids the /dev/null vs NUL split.
$emptyTree = $null
$emptyFile = [System.IO.Path]::GetTempFileName()
$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $emptyTree = (& git hash-object -w -t tree $emptyFile 2>$null | Select-Object -First 1)
}
finally {
    $ErrorActionPreference = $previous
    Remove-Item -LiteralPath $emptyFile -Force -ErrorAction SilentlyContinue
}
if (-not $emptyTree) {
    Write-Err 'Could not create the empty tree object needed for the claim commit.'
    exit $ExitLost
}

$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
# A nonce unique to THIS invocation, so the claim commit is never byte-identical to one already on
# the ref. Without it, a same-CLIO_CLAIM_ID retry inside the same second builds the same commit, and
# pushing the value a ref already holds is a no-op that git reports as success - the lease is never
# evaluated, so the retry looked like it had created the claim and then released somebody's live one.
$attemptNonce = "$PID-$([guid]::NewGuid().ToString('N'))"
$env:GIT_AUTHOR_NAME = 'clio-claim'
$env:GIT_AUTHOR_EMAIL = 'clio-claim@localhost'
$env:GIT_AUTHOR_DATE = $now
$env:GIT_COMMITTER_NAME = 'clio-claim'
$env:GIT_COMMITTER_EMAIL = 'clio-claim@localhost'
$env:GIT_COMMITTER_DATE = $now
$claimSha = (& git commit-tree $emptyTree `
        -m "clio-claim on issue #$IssueNumber" `
        -m "claim-id: $claimId" `
        -m "claimant: $me" `
        -m "branch: $Branch" `
        -m "created-at: $now" `
        -m "attempt: $attemptNonce" | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or -not $claimSha) {
    Write-Err 'Could not create the claim commit object.'
    exit $ExitLost
}

# Compare-and-swap: an empty expected value means "the ref must not exist yet", checked inside
# the server's atomic ref transaction. Exactly one racing run gets a successful push.
if (Invoke-GitQuiet 'push' '--quiet' "--force-with-lease=${ClaimRef}:" 'origin' "${claimSha}:${ClaimRef}") {
    $createdClaim = $true
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TokenFile) | Out-Null
    Set-Content -LiteralPath $TokenFile -Value $claimId -NoNewline
    Write-Host "Claimed issue #$IssueNumber (claim-id $claimId)."
}
else {
    try { $existingSha = Get-RemoteClaimSha }
    catch {
        Write-Err $_.Exception.Message
        exit $ExitLost
    }
    if (-not $existingSha) {
        Write-Err @(
            "Could not create $ClaimRef on origin and no claim exists there.",
            'The push was rejected - most likely this token cannot write refs outside refs/heads/*.',
            'Failing closed: an unarbitrated claim is worse than no claim. Ask a maintainer to assign',
            "issue #$IssueNumber to you manually, or grant ref write access."
        )
        exit $ExitLost
    }
    $ownerId = Get-ClaimField -Field 'claim-id' -Sha $existingSha
    if ($ownerId -ne $claimId) {
        Write-Err @(
            "Issue #$IssueNumber is already claimed: $(Format-RemoteClaim -Sha $existingSha)",
            'Refusing to work on an issue somebody else claimed. Pick another issue, or ask the holder to hand it over.'
        )
        exit $ExitLost
    }
    $claimSha = $existingSha
    Write-Host "Issue #$IssueNumber already carries this run's claim id ($claimId) - converging on the remaining steps."
    Write-Host 'Not treating it as freshly created: if another invocation replayed this id, its claim must survive our exit.'
}

try {
    # Ownership. Additive assignment is not arbitration (the claim ref already did that), but the
    # assignee is what a human reads, so it must actually be set - and confirmed by a re-read,
    # because a denied assignment can still report success on some gh/permission combinations.
    $assignees = @(Invoke-GhIssue 'view' '--json' 'assignees' '-q' '.assignees[].login' | Where-Object { $_ })
    if ($assignees -notcontains $me) {
        Invoke-GhIssue 'edit' '--add-assignee' $me | Out-Null
        $assignees = @(Invoke-GhIssue 'view' '--json' 'assignees' '-q' '.assignees[].login' | Where-Object { $_ })
    }
    if ($assignees -notcontains $me) {
        Write-Err @(
            "Issue #$IssueNumber could not be assigned to $me (insufficient permissions?).",
            "Current assignees: $($assignees -join ' ')",
            'Failing closed instead of reporting a claim: ownership is unresolved, so another agent must not',
            "be told this issue is free. Ask a maintainer to assign it to @$me and re-run."
        )
        Complete-Run -Code $ExitLost
    }
    Write-Host "Issue #$IssueNumber is assigned to $me."

    # The marker comment is the human-visible half of the claim, keyed by claim id so a repeated
    # run repairs a missing marker instead of posting a duplicate.
    $marker = "$MarkerPrefix $claimId "
    $comments = @(Invoke-GhIssue 'view' '--json' 'comments' '-q' '.comments[].body')
    if (($comments -join "`n").Contains($marker)) {
        Write-Host "The claim comment for claim-id $claimId is already on issue #$IssueNumber."
    }
    else {
        $body = @(
            "$MarkerPrefix $claimId -->",
            '🤖 An automated agent started working on this issue.',
            '',
            "Working branch: ``$Branch``",
            '',
            "The issue is assigned to @$me, who is accountable for the result. Progress will be reported here and in the pull request that references this issue.",
            '',
            "The exclusive claim is held at ``$ClaimRef``; it is released with ``pwsh ./scripts/claim-issue.ps1 -IssueNumber $IssueNumber -Release``."
        ) -join "`n"
        Invoke-GhIssue 'comment' '--body' $body | Out-Null
        Write-Host "Posted the claim comment on issue #$IssueNumber."
    }
}
catch {
    Write-Err $_.Exception.Message
    Complete-Run -Code $ExitLost
}

$claimComplete = $true
Write-Host "Issue #$IssueNumber is claimed exclusively: ref $ClaimRef, assignee $me, branch $Branch."
exit 0
