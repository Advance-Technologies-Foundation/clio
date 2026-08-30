# PR Delivery Flow

Use this workflow when the user asks to:
- update a branch from `master`
- create a new working branch from `master`
- create or update a pull request
- monitor and fix PR feedback
- check build status, Sonar/quality gates, or AI review comments
- merge a pull request after checks are green
- create a release after merge

Goal: finish the full delivery loop without leaving hidden follow-up work in GitHub.

## 1. Prepare the branch

1. Confirm the current branch, worktree cleanliness, and upstream branch.
2. If the user asks to update the branch from `master`, fetch `origin/master` first.
3. If the branch is already a non-`main`/non-`master` working branch, stay on it unless the user explicitly asks for a new branch.
4. If the user asks for a new branch, create it from `origin/master`, not from the current feature branch, then push it and verify the upstream tracking branch.
5. Choose update strategy deliberately:
   - prefer `merge origin/master` for shared branches or when history must stay stable
   - prefer `rebase origin/master` only when rewriting branch history is acceptable
6. If the update creates conflicts, resolve them before doing anything PR-related.

## 2. Create or refresh the PR

1. Check whether a PR for the branch already exists.
2. If no PR exists, create one with:
   - a clear title
   - a body that explains what changed in friendly English
   - explicit notes about tests, docs review, and MCP review when relevant
3. If a PR already exists and the branch changed materially, update the PR description if the current text no longer matches reality.

## 3. Monitor PR feedback

Always inspect all three sources below:

1. Review comments and review threads
   - fetch flat PR review comments
   - fetch review thread state explicitly, preferably including `isResolved` and `isOutdated`
   - treat AI comments the same as human comments until validated
2. Checks and quality gates
   - inspect `gh pr checks --required` to identify the merge gates for the current head
   - inspect other checks only for useful diagnostics; pending or failing advisory checks do not block merge
   - inspect GitHub Actions runs and logs for failures
   - wait for the Sonar analysis attached to the latest head, then inspect its PR new-issue list directly
   - require zero unresolved or accepted new Sonar issues; a green Sonar quality gate alone does not satisfy this policy
3. Mergeability state
   - read PR merge state from GitHub

### Sonar new-issue check

Always run the bundled read-only checker from the repository root:

```powershell
python .codex/skills/pr-delivery-flow/scripts/check_sonar_new_issues.py --pr <pr>
```

The checker resolves the current PR head through authenticated `gh`, requires a
successful `SonarCloud Code Analysis` check on that exact SHA, queries every
page of Sonar PR issues with statuses `OPEN`, `CONFIRMED`, and `ACCEPTED`, and
then re-reads the head to detect a concurrent push. Public projects are queried
anonymously. When `SONAR_TOKEN` is present, it is sent only as a Bearer header
and is never printed.

- Exit `0`: analysis completed on the stable latest head and no new issues remain.
- Exit `1`: new issues remain; every issue is printed with its rule, severity, status, file, line, and message.
- Exit `2`: the result could not be verified, including a missing/pending/failed analysis, API/authentication error, malformed response, incomplete pagination, or head change.

Only exit `0` satisfies the delivery gate. On exit `1`, fix the code, push, wait
for Sonar on the new head, and run the checker again. On exit `2`, do not infer
zero issues from the green badge; report that the gate could not be verified.
Do not mark an issue Accepted or False Positive merely to clear the gate; such
classification requires explicit user approval and evidence.

## 4. Validate and fix findings

For every actionable comment or failing check:

1. Verify whether the finding is still valid against the latest branch head.
2. If valid, implement the fix.
3. Run the smallest useful local verification first, then broader verification when needed.
4. Push the fix and wait only for the fresh required PR checks on the new head commit.
5. Re-read the PR head SHA from GitHub after the push and make sure subsequent checks/comments are evaluated against that latest head, not against an older green run.

Do not treat old feedback as closed just because the code was updated locally.
Do not assume that a new push automatically closes old review threads.

## 5. Reply to comments and resolve threads

This step is mandatory.

After a fix is pushed:

1. Reply in each addressed review thread with a short explanation of what changed.
2. Resolve the thread in GitHub.
3. Re-check unresolved review threads.
4. If a thread is `outdated` but still unresolved, reply and resolve it anyway unless there is a clear reason not to.

The task is not complete until unresolved actionable review threads are zero.

## 6. Re-check final PR state

Before merging, confirm all of the following on the latest PR head:

- required checks are green
- Sonar/quality gate is green
- Sonar's direct PR issue query for the latest head returns zero unresolved or accepted new issues
- unresolved actionable review threads are zero
- no new AI review comments were added after the last push
- PR merge state is clean

If any of these are false, continue the loop from section 3.

Do not wait for TeamCity or any other advisory check unless it is currently returned by `gh pr checks --required` or the user explicitly asks for it. Repository documentation calling a check advisory is useful context, but the live required-check result is the merge authority.

## 7. Polling guidance

When the repository uses self-hosted runners or slow required quality tools:

1. Expect checks to sit in `QUEUED` or `IN_PROGRESS` for a while.
2. Poll required run status deliberately instead of assuming a hang.
3. Do not poll advisory TeamCity jobs merely to delay delivery.
4. If a required job completes successfully, re-read the required PR checks and quality tools once more before merging.

## 8. Merge and verify

1. Once the pull request is ready, validation and review are complete, and no known blocker remains, enable GitHub auto-merge whenever the repository allows it. Auto-merge may be armed while required checks are still pending; never arm it while the PR is a draft.
2. Use the repository-approved merge method, for example `gh pr merge <number> --auto --merge`, and verify that auto-merge is enabled.
3. Wait only for the required gates. Do not wait for advisory checks such as TeamCity unless they are live required gates or the user explicitly requested them.
4. If auto-merge is unavailable, merge manually only after the final required-gate re-check passes.
5. Verify on GitHub that the PR state is actually `MERGED`.
6. Record the merge commit SHA.
7. If the user asked for release follow-up, continue with the release flow only after merge verification succeeds.

## 9. Optional release follow-up

If the user asks for a release after merge:

1. Fetch tags and identify the next release version deliberately.
2. Create the release on the verified merge commit SHA, not on a stale local branch head.
3. Verify that the release is published and that the release workflow started.
4. If release notes are autogenerated, rewrite them into a clear human-friendly summary when the user asks.

## 10. Definition of done

Do not say the PR is done if any of the following are still pending:

- an AI or human review thread is unresolved
- a required check is pending or failing
- Sonar/quality gate has unresolved blocking findings
- Sonar's direct PR issue query contains any unresolved or accepted new issue, even when the quality gate is green
- the PR checks are green but belong to an older head commit than the latest pushed fix
- the PR was "ready to merge" but not actually merged yet

Done means:

- code fixed
- comments answered
- threads resolved
- checks green
- PR merged
