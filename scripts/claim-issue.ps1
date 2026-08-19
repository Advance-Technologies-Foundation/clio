#!/usr/bin/env pwsh
<#
.SYNOPSIS
	Claim a GitHub issue before starting work on it.
.DESCRIPTION
	Assigns the issue to the authenticated gh user and posts a short comment saying that
	work has started. Safe to re-run: an issue already assigned to the current user is left
	untouched and no duplicate comment is posted. An issue assigned to somebody else is
	refused, so two agents cannot silently take the same issue.
.PARAMETER IssueNumber
	Number of the GitHub issue to claim.
.PARAMETER Branch
	Working branch to mention in the comment. Defaults to the current git branch.
.EXAMPLE
	./scripts/claim-issue.ps1 -IssueNumber 1234
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory = $true, Position = 0)]
	[ValidatePattern('^[0-9]+$')]
	[string]$IssueNumber,

	[Parameter(Position = 1)]
	[string]$Branch
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
	[Console]::Error.WriteLine('gh CLI is required but was not found in PATH.')
	exit 3
}

if (-not $Branch) {
	$Branch = (git rev-parse --abbrev-ref HEAD 2>$null)
}

$me = gh api user -q .login
$assignees = @(gh issue view $IssueNumber --json assignees -q '.assignees[].login' | Where-Object { $_ })

if ($assignees -contains $me) {
	Write-Host "Issue #$IssueNumber is already assigned to $me - nothing to do."
	exit 0
}

if ($assignees.Count -gt 0) {
	[Console]::Error.WriteLine("Issue #$IssueNumber is already assigned to: $($assignees -join ', ')")
	[Console]::Error.WriteLine('Refusing to claim work owned by somebody else. Pick another issue, or ask the current assignee to hand it over.')
	exit 1
}

$body = "`u{1F916} An automated agent started working on this issue."
if ($Branch -and $Branch -ne 'HEAD') {
	$body += "`n`nWorking branch: ``$Branch``"
}
$body += "`n`nThe issue is assigned to @$me, who is accountable for the result. Progress will be reported here and in the pull request that references this issue."

# A failed assignment must not abort the claim: the comment still has to be posted.
# On pwsh 7.4+ a non-zero native exit code throws while $ErrorActionPreference is 'Stop',
# so the call needs both a try/catch and the $LASTEXITCODE check.
$assignFailed = $false
try {
	gh issue edit $IssueNumber --add-assignee $me | Out-Null
	if ($LASTEXITCODE -ne 0) { $assignFailed = $true }
}
catch {
	$assignFailed = $true
}

if ($assignFailed) {
	Write-Warning "Could not assign issue #$IssueNumber to $me (insufficient permissions?)."
	$body += "`n`nAssignment could not be set automatically - a maintainer needs to assign this issue to @$me."
}
else {
	Write-Host "Assigned issue #$IssueNumber to $me."
}

gh issue comment $IssueNumber --body $body | Out-Null
Write-Host "Posted the claim comment on issue #$IssueNumber."
