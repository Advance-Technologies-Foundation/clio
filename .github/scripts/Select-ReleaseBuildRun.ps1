<#
.SYNOPSIS
    Picks the `Build` workflow run that the release workflow trusts for the tagged commit.

.DESCRIPTION
    Used by .github/workflows/reliase-to-nuget.yml. Given the `workflow_runs` array of
    GET /repos/{owner}/{repo}/actions/runs?head_sha=<sha>, returns the newest run of
    .github/workflows/build.yml that was started by a `push` to `master`, or nothing when there is none.

    Only a master push run counts. A `pull_request` run for the same SHA skips every real build and
    test job (they are gated on the event), so accepting one would let a green-looking run with zero
    executed tests satisfy the release gate. A run from another branch did not test what master holds.

    The newest matching run is authoritative: a re-run supersedes an earlier failed attempt.

    Kept free of network access so clio.tests (ReleaseBuildRunSelectorTests) can run it in-process
    on hand-made runs. Must stay Windows PowerShell 5.1 compatible: the release job uses `shell: powershell`.

.PARAMETER WorkflowRuns
    The `workflow_runs` items from the GitHub REST API (objects with path, event, head_branch, created_at).
#>
param(
    [AllowNull()]
    [AllowEmptyCollection()]
    [object[]] $WorkflowRuns
)

$ErrorActionPreference = "Stop"

$buildRuns = @($WorkflowRuns | Where-Object {
    $_.path -eq '.github/workflows/build.yml' -and $_.event -eq 'push' -and $_.head_branch -eq 'master'
})

if ($buildRuns.Count -eq 0) {
    return
}

$buildRuns | Sort-Object { [datetime]$_.created_at } -Descending | Select-Object -First 1
