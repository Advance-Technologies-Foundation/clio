#!/usr/bin/env bash
# Behaviour tests for the issue-claim protocol (scripts/claim-issue.sh and its PowerShell twin).
#
# Every case reproduces one of the fail-open scenarios found in review and asserts the outcome
# flipped. `gh` is replaced by scripts/tests/fake-gh; `origin` is a real local bare repository
# reached through a `url.<local>.insteadOf` rewrite of a github.com URL, so the compare-and-swap
# that arbitrates the claim is exercised for real while the remote still normalizes to the
# owner/name that `gh` reports. Every case runs against the implementation named on the command
# line — including the race, which must hold for both.
#
# Negative cases assert their documented diagnostic as well as the exit code: "exited non-zero"
# alone cannot tell a deliberate refusal from an accident.
#
# Usage: ./scripts/tests/claim-issue.tests.sh [sh|ps1]     (default: sh)
set -uo pipefail

impl="${1:-sh}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fake_gh="$repo_root/scripts/tests/fake-gh"
issue=4242
work_branch='feature/4242-do-the-thing'
origin_repo='acme/widgets'
origin_url="https://github.com/$origin_repo.git"

if [[ "$impl" == ps1 ]] && ! command -v pwsh >/dev/null 2>&1; then
    echo "SKIP: pwsh is not installed, cannot test the PowerShell implementation." >&2
    exit 0
fi

passed=0
failed=0
current_case=''

# Named so the same assertion label is not repeated as a literal across the cases.
readonly LABEL_COMMENTS='claim comments'
readonly LABEL_ASSIGNEES='assignees'
readonly LABEL_CLAIM_REF='claim ref released'
readonly LABEL_CLAIM_REF_KEPT='claim ref still held'
readonly LABEL_DIAGNOSTIC='diagnostic'

pass() {
    local message="$1"
    printf '  ok   %s\n' "$message"
    passed=$((passed + 1))
    return 0
}

fail() {
    local message="$1"
    printf '  FAIL %s\n' "$message" >&2
    failed=$((failed + 1))
    return 0
}

expect_eq() {
    local what="$1" expected="$2" actual="$3"
    if [[ "$expected" == "$actual" ]]; then
        pass "$current_case: $what == $expected"
    else
        fail "$current_case: $what expected '$expected', got '$actual'"
    fi
    return 0
}

# Asserts the run explained itself. A refusal that prints nothing recognizable is indistinguishable
# from an accidental failure, which is how the first version of this protocol looked healthy.
expect_diagnostic() {
    local needle="$1"
    if grep -qiF "$needle" "$sandbox/last.err" "$sandbox/last.out" 2>/dev/null; then
        pass "$current_case: $LABEL_DIAGNOSTIC mentions '$needle'"
    else
        fail "$current_case: $LABEL_DIAGNOSTIC does not mention '$needle'"
    fi
    return 0
}

# ── sandbox ───────────────────────────────────────────────────────────────
sandbox=''
new_sandbox() {
    local case_name="$1"
    current_case="$case_name"
    printf '\n%s\n' "$current_case"
    sandbox="$(mktemp -d)"
    mkdir -p "$sandbox/bin" "$sandbox/state"
    # Copied, not symlinked: a Windows checkout cannot be relied on for symlinks.
    cp "$fake_gh" "$sandbox/bin/gh"
    chmod +x "$sandbox/bin/gh"
    git init --quiet --bare "$sandbox/origin.git"
    git init --quiet --initial-branch=master "$sandbox/work"
    git -C "$sandbox/work" -c user.email=t@t -c user.name=t commit --quiet --allow-empty -m init
    # The remote reads as the GitHub repository gh reports, while git talks to the local bare repo.
    git -C "$sandbox/work" remote add origin "$origin_url"
    git -C "$sandbox/work" config "url.$sandbox/origin.git.insteadOf" "$origin_url"
    git -C "$sandbox/work" push --quiet origin master
    return 0
}

drop_sandbox() {
    [[ -n "$sandbox" ]] && rm -rf "$sandbox"
    sandbox=''
    return 0
}

# Runs the implementation under test, writing stdout/stderr to the given files.
run_impl() {
    local out_file="$1" err_file="$2" branch="$3"; shift 3
    local rc=0
    if [[ "$impl" == ps1 ]]; then
        local args=(-NoProfile -File "$repo_root/scripts/claim-issue.ps1" -IssueNumber "$issue")
        [[ -n "$branch" ]] && args+=(-Branch "$branch")
        args+=("$@")
        ( cd "$sandbox/work" && PATH="$sandbox/bin:$PATH" FAKE_GH_STATE="$sandbox/state" \
            pwsh "${args[@]}" ) >"$out_file" 2>"$err_file" || rc=$?
    else
        local args=("$issue")
        [[ -n "$branch" ]] && args+=("$branch")
        args+=("$@")
        ( cd "$sandbox/work" && PATH="$sandbox/bin:$PATH" FAKE_GH_STATE="$sandbox/state" \
            bash "$repo_root/scripts/claim-issue.sh" "${args[@]}" ) >"$out_file" 2>"$err_file" || rc=$?
    fi
    echo "$rc"
    return 0
}

# The PowerShell switches and the bash flags differ, so a case names the mode rather than the flag.
mode_args() {
    local mode="$1"
    case "$mode" in
        release)       [[ "$impl" == ps1 ]] && echo '-Release'          || echo '--release' ;;
        release-force) [[ "$impl" == ps1 ]] && echo '-Release -Force'   || echo '--release --force' ;;
        status)        [[ "$impl" == ps1 ]] && echo '-Status'           || echo '--status' ;;
        *) echo '' ;;
    esac
    return 0
}

claim() {
    local branch="${1-}"; shift || true
    local rc
    rc="$(run_impl "$sandbox/last.out" "$sandbox/last.err" "$branch" "$@")"
    # Diagnostics on any non-zero run — including the cases that are supposed to fail, since what
    # matters is that they failed for the documented reason and not by accident.
    if [[ "$rc" != 0 ]]; then
        {
            printf '       [exit %s] stdout:\n' "$rc"
            sed 's/^/         | /' "$sandbox/last.out"
            printf '       [exit %s] stderr:\n' "$rc"
            sed 's/^/         | /' "$sandbox/last.err"
        } >&2
    fi
    echo "$rc"
    return 0
}

claim_mode() {
    local mode="$1"
    # shellcheck disable=SC2086  # mode_args deliberately expands to one or two separate flags
    claim '' $(mode_args "$mode")
    return 0
}

# grep -c exits 1 on zero matches, so the count is taken through a pipe that swallows it.
count_lines() {
    local file="$1"
    if [[ ! -f "$file" ]]; then
        echo 0
        return 0
    fi
    grep -c . "$file" 2>/dev/null | head -1 || true
    return 0
}

claim_count() {
    count_lines "$sandbox/state/comments"
    return 0
}

assignee_count() {
    count_lines "$sandbox/state/assignees"
    return 0
}

claim_ref_exists() {
    if git -C "$sandbox/work" ls-remote origin "refs/claims/issue-$issue" 2>/dev/null | grep -q .; then
        echo yes
    else
        echo no
    fi
    return 0
}

# ── 1. Two synchronized claimers: exactly one winner ──────────────────────
# Review: "two synchronized Bash claimers both assigned, commented, and exited 0" — and then, on
# the redesign, that this leg raced bash even when asked for ps1, so the property could regress
# in the PowerShell twin behind a green job.
new_sandbox "1. two racing claimers produce exactly one winner ($impl)"
mkdir -p "$sandbox/barrier"
run_racer() {
    local id="$1" out="$2"
    local rc=0
    if [[ "$impl" == ps1 ]]; then
        ( cd "$sandbox/work" && PATH="$sandbox/bin:$PATH" FAKE_GH_STATE="$sandbox/state" \
            FAKE_GH_BARRIER="$sandbox/barrier" FAKE_GH_BARRIER_COUNT=2 CLIO_CLAIM_ID="$id" \
            pwsh -NoProfile -File "$repo_root/scripts/claim-issue.ps1" \
                -IssueNumber "$issue" -Branch "$work_branch" ) >"$out" 2>&1 || rc=$?
    else
        ( cd "$sandbox/work" && PATH="$sandbox/bin:$PATH" FAKE_GH_STATE="$sandbox/state" \
            FAKE_GH_BARRIER="$sandbox/barrier" FAKE_GH_BARRIER_COUNT=2 CLIO_CLAIM_ID="$id" \
            bash "$repo_root/scripts/claim-issue.sh" "$issue" "$work_branch" ) >"$out" 2>&1 || rc=$?
    fi
    echo "$rc" >"$out.rc"
    return 0
}
run_racer racer-a "$sandbox/a" & pid_a=$!
run_racer racer-b "$sandbox/b" & pid_b=$!
wait "$pid_a" "$pid_b"
rc_a="$(cat "$sandbox/a.rc")"; rc_b="$(cat "$sandbox/b.rc")"
winners=0; [[ "$rc_a" == 0 ]] && winners=$((winners + 1)); [[ "$rc_b" == 0 ]] && winners=$((winners + 1))
expect_eq 'winners' 1 "$winners"
expect_eq "$LABEL_COMMENTS" 1 "$(claim_count)"
expect_eq "$LABEL_ASSIGNEES" 1 "$(assignee_count)"
if grep -qi 'already claimed' "$sandbox/a" "$sandbox/b"; then pass "$current_case: the loser said the issue is already claimed"
else fail "$current_case: the loser did not report an existing claim"; fi
drop_sandbox

# ── 2. A second agent sharing the same GitHub identity is refused ──────────
new_sandbox '2. a second agent on the same login is refused'
expect_eq 'first run' 0 "$(CLIO_CLAIM_ID=agent-one claim "$work_branch")"
export CLIO_CLAIM_ID=agent-two
rc="$(claim "$work_branch")"
unset CLIO_CLAIM_ID
expect_eq 'second run on the same login' 1 "$rc"
expect_diagnostic 'already claimed'
expect_eq "$LABEL_COMMENTS" 1 "$(claim_count)"
drop_sandbox

# ── 3. A retry of the same run converges instead of refusing itself ────────
new_sandbox '3. a retry carrying the same claim id converges'
export CLIO_CLAIM_ID=stable-run
expect_eq 'first run' 0 "$(claim "$work_branch")"
expect_eq 'retry' 0 "$(claim "$work_branch")"
unset CLIO_CLAIM_ID
expect_eq "$LABEL_COMMENTS" 1 "$(claim_count)"
drop_sandbox

# ── 4. Assignment denied: fail closed, no fallback comment, issue stays free ─
new_sandbox '4. a denied assignment fails closed and leaves the issue free'
export FAKE_GH_DENY_ASSIGN=1
expect_eq 'first attempt' 1 "$(CLIO_CLAIM_ID=denied-one claim "$work_branch")"
expect_diagnostic 'could not be assigned'
expect_eq 'second attempt' 1 "$(CLIO_CLAIM_ID=denied-two claim "$work_branch")"
unset FAKE_GH_DENY_ASSIGN
expect_eq 'fallback comments' 0 "$(claim_count)"
expect_eq "$LABEL_CLAIM_REF" no "$(claim_ref_exists)"
drop_sandbox

# ── 5. A failing gh call is fatal, not silently successful ────────────────
new_sandbox '5. a failing gh call is fatal'
export FAKE_GH_FAIL_VIEW=1
expect_eq 'ownership read failure' 1 "$(CLIO_CLAIM_ID=view-fail claim "$work_branch")"
expect_diagnostic 'failed'
unset FAKE_GH_FAIL_VIEW
expect_eq "$LABEL_ASSIGNEES" 0 "$(assignee_count)"
expect_eq "$LABEL_CLAIM_REF" no "$(claim_ref_exists)"

export FAKE_GH_FAIL_ASSIGN=1
expect_eq 'assignment failure' 1 "$(CLIO_CLAIM_ID=assign-fail claim "$work_branch")"
unset FAKE_GH_FAIL_ASSIGN
expect_eq "$LABEL_COMMENTS" 0 "$(claim_count)"
drop_sandbox

# ── 6. Assignment succeeded, comment failed: the retry repairs the marker ──
new_sandbox '6. a missing marker is repaired by the next run'
export FAKE_GH_FAIL_COMMENT=1
expect_eq 'run with a failing comment call' 1 "$(CLIO_CLAIM_ID=partial-one claim "$work_branch")"
unset FAKE_GH_FAIL_COMMENT
expect_eq 'assignee survived' 1 "$(assignee_count)"
expect_eq 'marker missing' 0 "$(claim_count)"
expect_eq 'repair run' 0 "$(CLIO_CLAIM_ID=partial-two claim "$work_branch")"
expect_eq 'marker posted exactly once' 1 "$(claim_count)"
drop_sandbox

# ── 7. The default branch is never published as the working branch ────────
new_sandbox '7. claiming from the default branch without a branch name is refused'
expect_eq 'exit code' 2 "$(claim '')"
expect_diagnostic 'without a working branch name'
expect_eq "$LABEL_ASSIGNEES" 0 "$(assignee_count)"
expect_eq "$LABEL_COMMENTS" 0 "$(claim_count)"
expect_eq 'no claim ref' no "$(claim_ref_exists)"
drop_sandbox

# ── 8. The documented plain release works after a generated claim id ──────
# Review: acquisition generated a random id, the separate release generated another one, so the
# comparison always refused and the ref stayed behind — while the docs advertise plain release.
new_sandbox '8. the documented plain release works after a generated claim id'
expect_eq 'acquire with a generated id' 0 "$(claim "$work_branch")"
expect_eq 'claim ref taken' yes "$(claim_ref_exists)"
expect_eq 'plain release' 0 "$(claim_mode release)"
expect_eq 'claim ref gone' no "$(claim_ref_exists)"
expect_eq 'release when nothing is claimed' 0 "$(claim_mode release)"
drop_sandbox

# ── 9. An adopted claim is never auto-released ────────────────────────────
# Review: a same-id retry that then failed a GitHub call deleted the FIRST worker's live ref,
# letting a third agent claim an issue somebody was still working on.
new_sandbox '9. a failing retry does not delete an adopted live claim'
export CLIO_CLAIM_ID=shared-id
expect_eq 'first worker claims' 0 "$(claim "$work_branch")"
export FAKE_GH_FAIL_VIEW=1
expect_eq 'overlapping retry fails' 1 "$(claim "$work_branch")"
unset FAKE_GH_FAIL_VIEW
unset CLIO_CLAIM_ID
expect_eq "$LABEL_CLAIM_REF_KEPT" yes "$(claim_ref_exists)"
export CLIO_CLAIM_ID=third-agent
expect_eq 'a third agent is still refused' 1 "$(claim "$work_branch")"
unset CLIO_CLAIM_ID
drop_sandbox

# ── 10. An unreadable remote is not an absent claim ───────────────────────
# Review: `-Release` with a broken origin printed "nothing to release" and exited 0, so a live
# claim could be wedged while cleanup reported success.
new_sandbox '10. an unreadable remote fails closed instead of reporting no claim'
expect_eq 'acquire' 0 "$(CLIO_CLAIM_ID=wedged claim "$work_branch")"
# Break the transport while leaving the repository identity intact: re-point the insteadOf rewrite
# at a path that does not exist. Changing the origin URL instead would trip the mismatch check in
# case 11 and never reach the remote read this case is about.
git -C "$sandbox/work" config --unset "url.$sandbox/origin.git.insteadOf"
git -C "$sandbox/work" config "url.$sandbox/gone.git.insteadOf" "$origin_url"
export CLIO_CLAIM_ID=wedged
expect_eq 'release against a broken remote' 1 "$(claim_mode release)"
expect_diagnostic 'not an absent claim'
expect_eq 'status against a broken remote' 1 "$(claim_mode status)"
unset CLIO_CLAIM_ID
drop_sandbox

# ── 11. The lock and the issue must be the same repository ────────────────
# Review: the ref goes to `origin` while gh resolves its own repository, so two forks could each
# win their own CAS and both act on one upstream issue.
new_sandbox '11. a repository mismatch between origin and gh fails closed'
export FAKE_GH_NWO='upstream/widgets'
expect_eq 'exit code' 3 "$(CLIO_CLAIM_ID=forked claim "$work_branch")"
expect_diagnostic 'different repositories'
unset FAKE_GH_NWO
expect_eq 'no claim ref' no "$(claim_ref_exists)"
expect_eq "$LABEL_ASSIGNEES" 0 "$(assignee_count)"
drop_sandbox

# ── 12. Every shell file in the harness parses as checked out ─────────────
# Review: the bash entrypoint materialized as CRLF on a Windows checkout and `bash -n` exited 2;
# then that the extensionless `fake-gh` helper was still unpinned, so WSL failed on `bash\r`.
current_case='12. the shell files are pinned to LF and parse'
printf '\n%s\n' "$current_case"
for f in scripts/claim-issue.sh scripts/tests/claim-issue.tests.sh scripts/tests/fake-gh; do
    eol_attr="$(git -C "$repo_root" check-attr eol -- "$f" | awk '{print $NF}')"
    expect_eq "gitattributes eol for $f" lf "$eol_attr"
    if grep -q $'\r' "$repo_root/$f"; then fail "$current_case: $f contains CR in the working tree"
    else pass "$current_case: $f has no CR in the working tree"; fi
    if bash -n "$repo_root/$f"; then pass "$current_case: bash -n on $f"
    else fail "$current_case: bash -n on $f"; fi
done

printf '\n%s implementation: %s passed, %s failed\n' "$impl" "$passed" "$failed"
(( failed == 0 )) || exit 1
