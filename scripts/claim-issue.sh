#!/usr/bin/env bash
# Claim a GitHub issue before starting work on it, exclusively.
#
# Several scheduled agents run against this repository in parallel, frequently under the
# SAME GitHub identity, so "is this issue assigned to my login?" cannot arbitrate between
# them and neither can a check-then-assign or a post-then-read on comments — both have a
# window in which two runs decide they won.
#
# Arbitration therefore uses the one compare-and-swap primitive GitHub gives us: a ref
# update. `git push --force-with-lease=<ref>:` (empty expected value) creates <ref> only if
# it does not already exist, and the server applies that check inside the atomic ref
# transaction — so out of any number of racing runs exactly one push succeeds and every
# other one is rejected. The claim lives at refs/claims/issue-<number> and points at a
# commit whose message carries the claim identity, so a loser can report who holds it.
#
# The claim is complete only when all three of these hold, and the script exits 0 only then:
#   1. we own the claim ref,
#   2. the issue is assigned to us AND a re-read confirms it,
#   3. the machine-readable claim marker comment is on the issue.
# Anything else releases the ref and exits non-zero: an unresolved ownership must never be
# reported as a successful claim, because the next agent would read it as a free issue.
# A partial state left behind by an earlier run (assigned but no marker, or the reverse) is
# repaired on the next run rather than short-circuited.
#
# Two rules keep the lock honest, both of them learned the hard way:
#   * Only the invocation that CREATED the ref may auto-release it. A run that finds the ref
#     already carrying its own claim id has ADOPTED somebody's live claim — possibly a worker
#     still running with a replayed CLIO_CLAIM_ID — and must never delete it on its way out.
#   * A failed `git ls-remote` is not "no claim". Every remote read checks its exit code and
#     fails closed, because reporting a wedged claim as absent is the same defect as
#     reporting an unowned issue as claimed, just in the other direction.
#
# The lock and the issue must also be the same repository: the ref lives on the `origin`
# remote while `gh` resolves its own repository (fork checkout, GH_REPO, `gh repo set-default`),
# and if those disagree two forks can each win their own CAS while both act on one upstream
# issue. The two are reconciled once, up front, and every `gh` call is passed --repo explicitly.
#
# Usage:
#   ./scripts/claim-issue.sh <issue-number> [branch]
#   ./scripts/claim-issue.sh --status <issue-number>
#   ./scripts/claim-issue.sh --release <issue-number> [--force]
#
# Environment:
#   CLIO_CLAIM_ID  stable identity for one logical run. Set it when a run may be retried:
#                  a retry carrying the same id converges on its own claim instead of being
#                  refused by it. When unset, a fresh identity is generated per invocation and
#                  recorded under .git so a later --release from the same checkout still works.

set -euo pipefail

readonly EXIT_LOST=1          # somebody else holds the claim, or ownership is unresolved
readonly EXIT_USAGE=2         # bad arguments
readonly EXIT_PREREQ=3        # missing gh / git / not a repository / repository mismatch
readonly CLAIM_MARKER_PREFIX='<!-- clio-claim-id:'

mode=acquire
force_release=0
issue_number=''
branch_arg=''

die_usage() {
    printf '%s\n' "$@" >&2
    printf 'Usage: %s <issue-number> [branch] | --status <issue-number> | --release <issue-number> [--force]\n' "$0" >&2
    exit "$EXIT_USAGE"
}

while (( $# )); do
    case "$1" in
        --status)  mode=status ;;
        --release) mode=release ;;
        --force)   force_release=1 ;;
        -h|--help)
            sed -n '2,48p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        -*) die_usage "Unknown option: $1" ;;
        *)
            if [[ -z "$issue_number" ]]; then issue_number="$1"
            elif [[ -z "$branch_arg" ]]; then branch_arg="$1"
            else die_usage "Unexpected argument: $1"
            fi
            ;;
    esac
    shift
done

[[ "$issue_number" =~ ^[0-9]+$ ]] || die_usage 'An issue number is required.'

command -v git >/dev/null 2>&1 || { echo 'git is required but was not found in PATH.' >&2; exit "$EXIT_PREREQ"; }
command -v gh  >/dev/null 2>&1 || { echo 'gh CLI is required but was not found in PATH.' >&2; exit "$EXIT_PREREQ"; }
git rev-parse --git-dir >/dev/null 2>&1 || { echo 'Not inside a git repository.' >&2; exit "$EXIT_PREREQ"; }

readonly claim_ref="refs/claims/issue-$issue_number"
readonly local_peek_ref="refs/clio-claim-peek/issue-$issue_number"
readonly token_file="$(git rev-parse --git-common-dir)/clio-claims/issue-$issue_number"

# Every `gh` call goes through here, with the repository pinned. A native command that fails
# must never let the caller continue with an empty string that looks like "no assignees" or
# "no comments" — the review found exactly that fail-open path, so a non-zero exit is always
# fatal to the caller.
run_gh() {
    local out status=0 err_file
    err_file="$(mktemp)"
    out="$(gh "$@" 2>"$err_file")" || status=$?
    if (( status != 0 )); then
        printf 'gh %s failed with exit code %s:\n' "$*" "$status" >&2
        cat "$err_file" >&2
        rm -f "$err_file"
        return "$status"
    fi
    rm -f "$err_file"
    printf '%s' "$out"
    return 0
}

gh_issue() {
    local verb="$1"; shift
    run_gh issue "$verb" "$issue_number" --repo "$canonical_repo" "$@"
}

# owner/name out of any remote URL shape: https://host/owner/name.git, git@host:owner/name.git,
# ssh://host/owner/name — the last two path segments once ':' is treated as a separator.
normalize_remote() {
    local url="${1%.git}"
    url="${url%/}"
    url="${url//:/\/}"
    printf '%s/%s' "$(basename "$(dirname "$url")")" "$(basename "$url")"
}

# `gh` decides its own repository (remotes, GH_REPO, a configured default) while the claim ref
# is pushed to `origin`. If those are two different repositories the lock arbitrates one place
# and the issue lives in another, so a fork can win its own CAS and still act upstream.
resolve_repo() {
    canonical_repo="$(run_gh repo view --json nameWithOwner -q .nameWithOwner)"
    [[ -n "$canonical_repo" ]] || { echo 'Could not resolve the GitHub repository for this checkout.' >&2; exit "$EXIT_PREREQ"; }

    local origin_url origin_repo
    # The CONFIGURED url, not `git remote get-url`, which applies url.<base>.insteadOf rewriting —
    # that rewrite is transport, not identity, and would compare a local mirror path here.
    origin_url="$(git config --get remote.origin.url 2>/dev/null || echo '')"
    [[ -n "$origin_url" ]] || { echo "No 'origin' remote: the claim ref has nowhere to live." >&2; exit "$EXIT_PREREQ"; }
    origin_repo="$(normalize_remote "$origin_url")"

    # tr rather than ${var,,}: macOS still ships bash 3.2, where that expansion is a syntax error.
    local origin_lc canonical_lc
    origin_lc="$(printf '%s' "$origin_repo" | tr '[:upper:]' '[:lower:]')"
    canonical_lc="$(printf '%s' "$canonical_repo" | tr '[:upper:]' '[:lower:]')"
    if [[ "$origin_lc" != "$canonical_lc" ]]; then
        {
            echo "The claim lock and the issue would target different repositories, so the claim would not arbitrate anything."
            echo "  gh resolves:      $canonical_repo"
            echo "  origin remote is: $origin_repo ($origin_url)"
            echo "Failing closed. Point origin at $canonical_repo, or set GH_REPO to the repository origin points at."
        } >&2
        exit "$EXIT_PREREQ"
    fi
}

# Fails (non-zero) when the remote could not be read, which is NOT the same as an absent ref.
remote_claim_sha() {
    local out status=0
    out="$(git ls-remote origin "$claim_ref" 2>/dev/null)" || status=$?
    (( status == 0 )) || return 1
    printf '%s' "$out" | awk 'NR==1 {print $1}'
    return 0
}

# Callers propagate the status deliberately (`|| exit $?`): an `exit` inside a command
# substitution would only leave the subshell.
require_remote_claim_sha() {
    local sha
    if ! sha="$(remote_claim_sha)"; then
        {
            echo "Could not read $claim_ref from origin (network or auth failure)."
            echo "Failing closed: an unreadable claim is not an absent claim, and treating it as absent would"
            echo "either hand the issue to a second agent or report a live claim as cleaned up."
        } >&2
        return "$EXIT_LOST"
    fi
    printf '%s' "$sha"
    return 0
}

# Reads the claim payload out of the commit the claim ref points at. The commit has an empty
# tree, so fetching it is cheap even on a shallow clone; it is fetched at most once per run.
claim_object_fetched=0
fetch_claim_object() {
    if (( claim_object_fetched == 0 )); then
        git fetch --quiet --no-tags origin "+$claim_ref:$local_peek_ref" >/dev/null 2>&1 || return 1
        claim_object_fetched=1
    fi
}

remote_claim_field() {
    local field="$1" sha="$2"
    fetch_claim_object || return 1
    git log -1 --format=%B "$sha" 2>/dev/null | sed -n "s/^$field: //p" | head -1
}

describe_remote_claim() {
    local sha="$1" id claimant created branch
    id="$(remote_claim_field claim-id "$sha" || true)"
    claimant="$(remote_claim_field claimant "$sha" || true)"
    branch="$(remote_claim_field branch "$sha" || true)"
    created="$(remote_claim_field created-at "$sha" || true)"
    printf 'claim-id=%s claimant=%s branch=%s created-at=%s ref=%s\n' \
        "${id:-<unreadable>}" "${claimant:-<unknown>}" "${branch:-<unknown>}" "${created:-<unknown>}" "$sha"
    return 0
}

release_claim() {
    local expected_sha="$1"
    git push --quiet --force-with-lease="$claim_ref:$expected_sha" origin ":$claim_ref" >/dev/null 2>&1
}

resolve_repo

# ── --status ──────────────────────────────────────────────────────────────
if [[ "$mode" == status ]]; then
    sha="$(require_remote_claim_sha)" || exit $?
    if [[ -z "$sha" ]]; then
        echo "Issue #$issue_number is not claimed ($claim_ref does not exist)."
    else
        echo "Issue #$issue_number is claimed: $(describe_remote_claim "$sha")"
    fi
    assignees="$(gh_issue view --json assignees -q '.assignees[].login')"
    printf 'Assignees: %s\n' "$(tr '\n' ' ' <<<"${assignees:-<none>}")"
    exit 0
fi

# The identity of this run. When CLIO_CLAIM_ID is unset the generated value is recorded under
# .git on a successful claim, so the documented plain `--release` from the same checkout still
# knows what it owns — otherwise every release would generate a new id, compare it against the
# stored one and refuse, which is exactly what review reproduced.
claim_id="${CLIO_CLAIM_ID:-}"
claim_id_source=env
if [[ -z "$claim_id" && "$mode" == release && -r "$token_file" ]]; then
    claim_id="$(cat "$token_file")"
    claim_id_source=recorded
fi
if [[ -z "$claim_id" ]]; then
    claim_id="$(hostname -s 2>/dev/null || echo host)-$$-$(date -u +%Y%m%dT%H%M%SZ)-$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')"
    claim_id_source=generated
fi
readonly claim_id claim_id_source

# ── --release ─────────────────────────────────────────────────────────────
if [[ "$mode" == release ]]; then
    sha="$(require_remote_claim_sha)" || exit $?
    if [[ -z "$sha" ]]; then
        rm -f "$token_file"
        echo "Issue #$issue_number is not claimed — nothing to release."
        exit 0
    fi
    owner_id="$(remote_claim_field claim-id "$sha" || true)"
    if [[ "$owner_id" != "$claim_id" && "$force_release" -eq 0 ]]; then
        {
            echo "Issue #$issue_number is claimed by somebody else: $(describe_remote_claim "$sha")"
            echo "Refusing to release a claim this run does not own (claim id from: $claim_id_source)."
            if [[ "$claim_id_source" == generated ]]; then
                echo "No claim was recorded for this checkout, so the holder is another checkout or another machine."
                echo "Release it from there, or pass its CLIO_CLAIM_ID."
            fi
            echo "If the holder is gone (check created-at above), break it deliberately with --release --force."
        } >&2
        exit "$EXIT_LOST"
    fi
    if release_claim "$sha"; then
        rm -f "$token_file"
        echo "Released the claim on issue #$issue_number."
        exit 0
    fi
    echo "Could not release the claim on issue #$issue_number — it changed under us, re-run to see the current holder." >&2
    exit "$EXIT_LOST"
fi

# ── acquire ───────────────────────────────────────────────────────────────
# The marker comment names the working branch, and the policy is to claim BEFORE creating
# the branch — so the branch cannot be discovered from HEAD, which at that point is still
# the default branch. It has to be passed in (or already checked out).
default_branch="$(run_gh repo view --repo "$canonical_repo" --json defaultBranchRef -q .defaultBranchRef.name)"
branch="$branch_arg"
if [[ -z "$branch" ]]; then
    branch="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '')"
fi
if [[ -z "$branch" || "$branch" == HEAD || "$branch" == "$default_branch" ]]; then
    {
        echo "Refusing to claim issue #$issue_number without a working branch name."
        echo "Claiming happens before the branch is created, so HEAD is still '${branch:-<detached>}' and would be"
        echo "published as the working branch in the claim comment. Pass the branch you are about to create:"
        echo "    $0 $issue_number <planned-branch-name>"
    } >&2
    exit "$EXIT_USAGE"
fi

me="$(run_gh api user -q .login)"
[[ -n "$me" ]] || { echo 'Could not resolve the authenticated gh user.' >&2; exit "$EXIT_LOST"; }

claim_complete=0
created_claim=0
claim_sha=''

# Only the invocation that CREATED the ref cleans it up. A run that adopted an existing claim
# (same CLIO_CLAIM_ID) may be racing the worker that created it — a replayed id is not proof of
# ownership — so deleting that ref here would hand a live claim to a third agent.
on_exit() {
    local rc=$?
    if (( rc != 0 && created_claim == 1 && claim_complete == 0 )); then
        if release_claim "$claim_sha"; then
            echo "Released the claim ref on issue #$issue_number — the claim did not complete, so the issue stays free." >&2
        else
            echo "WARNING: could not release $claim_ref after a failed claim. Run: $0 --release $issue_number" >&2
        fi
    fi
}
trap on_exit EXIT

empty_tree="$(git hash-object -w -t tree /dev/null)"
now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
# A nonce unique to THIS invocation, so the claim commit is never byte-identical to one already on
# the ref. Without it, a same-CLIO_CLAIM_ID retry inside the same second builds the same commit, and
# pushing the value a ref already holds is a no-op that git reports as success — the lease is never
# evaluated, so the retry looked like it had created the claim and then released somebody's live one.
attempt_nonce="$$-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
claim_sha="$(
    GIT_AUTHOR_NAME='clio-claim' GIT_AUTHOR_EMAIL='clio-claim@localhost' GIT_AUTHOR_DATE="$now" \
    GIT_COMMITTER_NAME='clio-claim' GIT_COMMITTER_EMAIL='clio-claim@localhost' GIT_COMMITTER_DATE="$now" \
    git commit-tree "$empty_tree" \
        -m "clio-claim on issue #$issue_number" \
        -m "claim-id: $claim_id" \
        -m "claimant: $me" \
        -m "branch: $branch" \
        -m "created-at: $now" \
        -m "attempt: $attempt_nonce"
)"

# Compare-and-swap: an empty expected value means "the ref must not exist yet", so this
# push is the single-winner election. Exactly one racing run gets a successful push.
if git push --quiet --force-with-lease="$claim_ref:" origin "$claim_sha:$claim_ref" >/dev/null 2>&1; then
    created_claim=1
    mkdir -p "$(dirname "$token_file")"
    printf '%s' "$claim_id" >"$token_file"
    echo "Claimed issue #$issue_number (claim-id $claim_id)."
else
    existing_sha="$(require_remote_claim_sha)" || exit $?
    if [[ -z "$existing_sha" ]]; then
        {
            echo "Could not create $claim_ref on origin and no claim exists there."
            echo "The push was rejected — most likely this token cannot write refs outside refs/heads/*."
            echo "Failing closed: an unarbitrated claim is worse than no claim. Ask a maintainer to assign"
            echo "issue #$issue_number to you manually, or grant ref write access."
        } >&2
        exit "$EXIT_LOST"
    fi
    owner_id="$(remote_claim_field claim-id "$existing_sha" || true)"
    if [[ "$owner_id" != "$claim_id" ]]; then
        {
            echo "Issue #$issue_number is already claimed: $(describe_remote_claim "$existing_sha")"
            echo "Refusing to work on an issue somebody else claimed. Pick another issue, or ask the holder to hand it over."
        } >&2
        exit "$EXIT_LOST"
    fi
    claim_sha="$existing_sha"
    echo "Issue #$issue_number already carries this run's claim id ($claim_id) — converging on the remaining steps."
    echo "Not treating it as freshly created: if another invocation replayed this id, its claim must survive our exit."
fi

# Ownership. Additive assignment is not arbitration (the claim ref already did that), but the
# assignee is what a human reads, so it has to actually be set — and confirmed by a re-read,
# because a denied assignment can still exit 0 through some gh/permission combinations.
assignees="$(gh_issue view --json assignees -q '.assignees[].login')"
if ! grep -qx "$me" <<<"$assignees"; then
    gh_issue edit --add-assignee "$me" >/dev/null
    assignees="$(gh_issue view --json assignees -q '.assignees[].login')"
fi
if ! grep -qx "$me" <<<"$assignees"; then
    {
        echo "Issue #$issue_number could not be assigned to $me (insufficient permissions?)."
        echo "Current assignees: $(tr '\n' ' ' <<<"$assignees")"
        echo "Failing closed instead of reporting a claim: ownership is unresolved, so another agent must not"
        echo "be told this issue is free. Ask a maintainer to assign it to @$me and re-run."
    } >&2
    exit "$EXIT_LOST"
fi
echo "Issue #$issue_number is assigned to $me."

# The marker comment is the human-visible half of the claim, and it is keyed by claim id so a
# repeated run repairs a missing marker instead of posting a duplicate.
existing_comments="$(gh_issue view --json comments -q '.comments[].body')"
if grep -qF "$CLAIM_MARKER_PREFIX $claim_id " <<<"$existing_comments"; then
    echo "The claim comment for claim-id $claim_id is already on issue #$issue_number."
else
    comment_body="$CLAIM_MARKER_PREFIX $claim_id -->
🤖 An automated agent started working on this issue.

Working branch: \`$branch\`

The issue is assigned to @$me, who is accountable for the result. Progress will be reported here and in the pull request that references this issue.

The exclusive claim is held at \`$claim_ref\`; it is released with \`./scripts/claim-issue.sh --release $issue_number\`."
    gh_issue comment --body "$comment_body" >/dev/null
    echo "Posted the claim comment on issue #$issue_number."
fi

claim_complete=1
echo "Issue #$issue_number is claimed exclusively: ref $claim_ref, assignee $me, branch $branch."
