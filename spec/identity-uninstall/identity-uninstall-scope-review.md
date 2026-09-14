# Claude scope review

Date: 2026-09-12
Review: rev_37f022ef473b4fe4 (Claude, claude-fable-5-1)
Reviewed master: 08558fc856540b69e341a700984cc5b6f6507277
Target: identity-uninstall-requirements.md; no implementation exists.

The worktree was fast-forwarded to freshly fetched origin/master before review.
The earlier review rev_1887c89e42ad41b5 was cancelled because its baseline was stale.

## Verdict

Claude found the overall approach minimal but recommended resolving the following
safety requirements before proceeding to design. Codex verified the source evidence.

## Accepted requirements corrections

1. Ownership persistence must cover partial deployment and `--no-app`, independently
   of successful OAuth-client creation. Deployment currently returns at
   `IdentityServiceDeploymentService.cs:424` without reaching persistence at line 441.
   A failed discovery or client verification can likewise leave artifacts unrecorded.
2. Preserving a shared identity must also protect its database dependency. Deployment
   writes the CRM connection string into identity configuration at lines 669-670.
   A combined uninstall must stop before destructive stages when it would drop a
   database required by a preserved identity. Mere preservation of IIS/files is insufficient.
3. Standalone removal must complete and verify matching CRM reference cleanup while
   the required authentication still works, before removing identity. Codex closed
   Claude's stated authentication uncertainty: `BindingsModule.cs:1414-1424` chooses
   OAuth client credentials when ClientId exists (unless a supplied AccessToken takes
   precedence). Do not silently fall back to passwords. Combined removal need not
   rewrite CRM settings in the database it is about to remove.
4. Read actual IIS target identity back before recording ownership. Deployment currently
   reuses same-named sites/pools without comparing their physical path or bindings
   (`IdentityServiceDeploymentService.cs:760-777`). Reject conflicting topology and
   dangerous path overlap with CRM or unrelated registered deployments.
5. Complete identity cleanup before CRM database/file deletion. Existing CRM uninstall
   refuses a missing CRM directory (`CreatioUninstaller.cs:387-389`) and requires its
   connection file (lines 422-432), so simply appending identity cleanup creates a retry
   trap. Preserve bounded recovery information and coordinate deployment/uninstall with
   the existing environment and path reservations.

## Qualifications and remaining design choices

- Database server/name equality is useful corroboration, not proof of exclusive use.
  A path/site/URL tuple also does not prove that a service has no other consumers.
  Define the ownership trust boundary explicitly and refuse ambiguous deletion.
- Claude suggested persisting immediately after CreateIisSite succeeds. That does not
  cover failures after file extraction or during partial IIS creation. Require recovery
  coverage for those states too; choose the minimal persistence mechanism in the ADR.
- Claude's claim that omitting a future attachment field from EnvironmentSettings.Merge
  necessarily drops an existing record is not established: Merge mutates an existing
  object and does not clear unspecified properties. Registration/serialization paths
  still require deliberate attachment preservation and clearing tests.
- Claude observed Ring's pipeline is manifest-driven. Accept that an additive stage
  need not break its schema; reject reducing the mandatory compatibility gate to text
  tests. AGENTS.md requires Ring tests, relevant harness validation and NativeAOT publish
  for changes to its consumed uninstall command. Confirmation must show the actual scope.
- Decide feature-toggle parity with the existing `deploy-identity` key. Explicitly state
  that standalone removal preserves existing OAuth client/user rows and database tables
  unless their deletion is separately justified; clearing references is not full rollback.

## Verification limits

This was source-based scope review only. No IIS mutation, database operation, build,
runtime test, implementation, or GitHub publication was performed in this review turn.
Identity migration table contents were not inspected and are not used as evidence.
Implementation remains pending the planning decision.
