#!/usr/bin/env bash
# Behaviour tests for the issue-claim protocol (scripts/claim-issue.sh and its PowerShell twin).
#
# Every case reproduces one of the fail-open scenarios found in review and asserts the outcome
# flipped. `gh` is replaced by scripts/tests/fake-gh; `origin` is a real local bare repository,
# so the compare-and-swap that arbitrates the claim is exercised for real rather than mocked.
#
# Usage: ./scripts/tests/claim-issue.tests.sh [sh|ps1]     (default: sh)
set -uo pipefail

impl="${1:-sh}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fake_gh="$repo_root/scripts/tests/fake-gh"
issue=4242
work_branch='feature/4242-do-the-thing'

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

# ── sandbox ───────────────────────────────────────────────────────────────
sandbox=''
new_sandbox() {
    local case_name="$1"
    current_case="$case_name"
    printf '\n%s\n' "$current_case"
    sandbox="$(mktemp -d)"
    mkdir -p "$sandbox/bin" "$sandbox/state"
    ln -s "$fake_gh" "$sandbox/bin/gh"
    git init --quiet --bare "$sandbox/origin.git"
    git init --quiet --initial-branch=master "$sandbox/work"
    git -C "$sandbox/work" -c user.email=t@t -c user.name=t commit --quiet --allow-empty -m init
    git -C "$sandbox/work" remote add origin "$sandbox/origin.git"
    git -C "$sandbox/work" push --quiet origin master
    return 0
}

drop_sandbox() {
    [[ -n "$sandbox" ]] && rm -rf "$sandbox"
    sandbox=''
    return 0
}

# Runs the implementation under test inside the sandbox and echoes its exit code.
claim() {
    local branch="${1-}"; shift || true
    local rc=0
    if [[ "$impl" == ps1 ]]; then
        local args=(-NoProfile -File "$repo_root/scripts/claim-issue.ps1" -IssueNumber "$issue")
        [[ -n "$branch" ]] && args+=(-Branch "$branch")
        args+=("$@")
        ( cd "$sandbox/work" && PATH="$sandbox/bin:$PATH" FAKE_GH_STATE="$sandbox/state" \
            pwsh "${args[@]}" ) >"$sandbox/last.out" 2>"$sandbox/last.err" || rc=$?
    else
        local args=("$issue")
        [[ -n "$branch" ]] && args+=("$branch")
        args+=("$@")
        ( cd "$sandbox/work" && PATH="$sandbox/bin:$PATH" FAKE_GH_STATE="$sandbox/state" \
            bash "$repo_root/scripts/claim-issue.sh" "${args[@]}" ) >"$sandbox/last.out" 2>"$sandbox/last.err" || rc=$?
    fi
    # Diagnostics on any non-zero run — including the cases that are supposed to fail, since what
    # matters is that they failed for the documented reason and not by accident.
    if (( rc != 0 )); then
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
# Review: "two synchronized Bash claimers both assigned, commented, and exited 0".
new_sandbox '1. two racing claimers produce exactly one winner'
mkdir -p "$sandbox/barrier"
run_racer() {
    local id="$1" out="$2"
    local rc=0
    ( cd "$sandbox/work" && PATH="$sandbox/bin:$PATH" FAKE_GH_STATE="$sandbox/state" \
        FAKE_GH_BARRIER="$sandbox/barrier" FAKE_GH_BARRIER_COUNT=2 CLIO_CLAIM_ID="$id" \
        bash "$repo_root/scripts/claim-issue.sh" "$issue" "$work_branch" ) >"$out" 2>&1 || rc=$?
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
drop_sandbox

# ── 2. A second agent sharing the same GitHub identity is refused ──────────
# Review: "a second agent sharing the existing GitHub identity exited 0".
new_sandbox '2. a second agent on the same login is refused'
expect_eq 'first run' 0 "$(CLIO_CLAIM_ID=agent-one claim "$work_branch")"
export CLIO_CLAIM_ID=agent-two
rc="$(claim "$work_branch")"
unset CLIO_CLAIM_ID
expect_eq 'second run on the same login' 1 "$rc"
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
# Review: "two assignment-denied Bash retries both exited 0 and posted duplicate fallback comments".
new_sandbox '4. a denied assignment fails closed and leaves the issue free'
export FAKE_GH_DENY_ASSIGN=1
expect_eq 'first attempt' 1 "$(CLIO_CLAIM_ID=denied-one claim "$work_branch")"
expect_eq 'second attempt' 1 "$(CLIO_CLAIM_ID=denied-two claim "$work_branch")"
unset FAKE_GH_DENY_ASSIGN
expect_eq 'fallback comments' 0 "$(claim_count)"
expect_eq "$LABEL_CLAIM_REF" no "$(claim_ref_exists)"
drop_sandbox

# ── 5. A failing gh call is fatal, not silently successful ────────────────
# Review: "PowerShell exited 0 after simulated gh issue view, assignment, and comment failures".
new_sandbox '5. a failing gh call is fatal'
export FAKE_GH_FAIL_VIEW=1
expect_eq 'ownership read failure' 1 "$(CLIO_CLAIM_ID=view-fail claim "$work_branch")"
unset FAKE_GH_FAIL_VIEW
expect_eq 'assignees after a failed read' 0 "$(assignee_count)"
expect_eq "$LABEL_CLAIM_REF" no "$(claim_ref_exists)"

export FAKE_GH_FAIL_ASSIGN=1
expect_eq 'assignment failure' 1 "$(CLIO_CLAIM_ID=assign-fail claim "$work_branch")"
unset FAKE_GH_FAIL_ASSIGN
expect_eq 'comments after a failed assignment' 0 "$(claim_count)"
drop_sandbox

# ── 6. Assignment succeeded, comment failed: the retry repairs the marker ──
# Review: "after assignment succeeds but commenting fails, retry exits early on self-assignment
# without repairing the missing marker".
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
# Review: "Following this instruction from master therefore publishes Working branch: master".
new_sandbox '7. claiming from the default branch without a branch name is refused'
expect_eq 'exit code' 2 "$(claim '')"
expect_eq "$LABEL_ASSIGNEES" 0 "$(assignee_count)"
expect_eq "$LABEL_COMMENTS" 0 "$(claim_count)"
expect_eq 'no claim ref' no "$(claim_ref_exists)"
drop_sandbox

# ── 8. The bash entrypoint parses as checked out ──────────────────────────
# Review: the Windows working-tree copy was CRLF and `bash -n` exited 2 on it.
current_case='8. the bash entrypoint is pinned to LF and parses'
printf '\n%s\n' "$current_case"
eol_attr="$(git -C "$repo_root" check-attr eol -- scripts/claim-issue.sh | awk '{print $NF}')"
expect_eq 'gitattributes eol' lf "$eol_attr"
if grep -q $'\r' "$repo_root/scripts/claim-issue.sh"; then fail "$current_case: the working-tree copy contains CR"
else pass "$current_case: the working-tree copy has no CR"; fi
if bash -n "$repo_root/scripts/claim-issue.sh"; then pass "$current_case: bash -n on the checked-out file"
else fail "$current_case: bash -n on the checked-out file"; fi

printf '\n%s implementation: %s passed, %s failed\n' "$impl" "$passed" "$failed"
(( failed == 0 )) || exit 1
