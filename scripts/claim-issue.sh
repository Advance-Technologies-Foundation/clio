#!/usr/bin/env bash
# Claim a GitHub issue before starting work on it.
#
# Assigns the issue to the authenticated gh user and posts a short comment saying
# that work has started. Safe to re-run: an issue already assigned to the current
# user is left untouched and no duplicate comment is posted. An issue assigned to
# somebody else is refused, so two agents cannot silently take the same issue.
#
# Usage: ./scripts/claim-issue.sh <issue-number> [branch-name]

set -euo pipefail

issue_number="${1:-}"
if [[ -z "$issue_number" || ! "$issue_number" =~ ^[0-9]+$ ]]; then
	echo "Usage: $0 <issue-number> [branch-name]" >&2
	exit 2
fi

branch="${2:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '')}"

if ! command -v gh >/dev/null 2>&1; then
	echo "gh CLI is required but was not found in PATH." >&2
	exit 3
fi

me="$(gh api user -q .login)"
assignees="$(gh issue view "$issue_number" --json assignees -q '.assignees[].login')"

if grep -qx "$me" <<<"$assignees"; then
	echo "Issue #$issue_number is already assigned to $me — nothing to do."
	exit 0
fi

if [[ -n "$assignees" ]]; then
	echo "Issue #$issue_number is already assigned to: $(tr '\n' ' ' <<<"$assignees")" >&2
	echo "Refusing to claim work owned by somebody else. Pick another issue, or ask the current assignee to hand it over." >&2
	exit 1
fi

comment_body="🤖 An automated agent started working on this issue."
if [[ -n "$branch" && "$branch" != "HEAD" ]]; then
	comment_body+=$'\n\n'"Working branch: \`$branch\`"
fi
comment_body+=$'\n\n'"The issue is assigned to @$me, who is accountable for the result. Progress will be reported here and in the pull request that references this issue."

if gh issue edit "$issue_number" --add-assignee "$me" >/dev/null; then
	echo "Assigned issue #$issue_number to $me."
else
	echo "Could not assign issue #$issue_number to $me (insufficient permissions?)." >&2
	comment_body+=$'\n\n'"Assignment could not be set automatically — a maintainer needs to assign this issue to @$me."
fi

gh issue comment "$issue_number" --body "$comment_body" >/dev/null
echo "Posted the claim comment on issue #$issue_number."
