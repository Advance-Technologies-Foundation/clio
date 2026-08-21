---
name: clio-issue-workflow
description: Coordinate a GitHub issue submitted to Advance-Technologies-Foundation/clio from visible claim through repository diagnosis and repair. Use when the user asks to take, triage, fix, implement, or resolve a Clio issue across clio, clio-knowledge, or a referenced example repository. Do not mutate GitHub or files during brainstorming or review-only requests.
---

# Clio Issue Workflow

Keep the workflow visible in GitHub and small enough to explain as:

`claim -> investigate -> repair and verify`

Treat the issue number or URL as required input. The original Clio issue remains the coordination point even when another repository owns the defect.

## Route the request

- For `take`, `triage`, `fix`, `implement`, or `resolve`, start with the `claim-clio-issue` skill; claiming always precedes investigation.
- Continue with the `investigate-clio-issue` skill to prove the failure boundary and identify every affected repository.
- Use the `repair-clio-issue` skill only when the user authorized implementation. A triage-only request stops after publishing the diagnosis and any authorized downstream issue relationships.
- For brainstorming, planning, explanation, or review-only requests, stay read-only and do not claim the issue.

Do not duplicate the phase procedures here. Use the phase skills as the source of truth.

## GitHub visibility model

Use GitHub's existing primitives only:

- Assignee: who coordinates the issue.
- `Mitigation stage` issue field: `Investigating`, `Fixing`, `QA`, or `Waiting for human approval`.
- Development: the linked branch and later the draft pull request.
- Relationships: the original issue is `blocked by` an issue in another repository when that downstream issue owns work required to resolve the report.

Do not introduce claim records, leases, receipts, lock files, custom refs, or a separate state store.

The original issue's `Mitigation stage` is authoritative for the overall workflow. A closed issue needs no `Done` field value.

### Canonical stage field

`Mitigation stage` is a pre-provisioned native organization-level GitHub issue field, not a GitHub Projects field. Issue-handling agents must never create, delete, or reconfigure this organization field.

The provisioning contract is:

- Organization: `Advance-Technologies-Foundation`.
- Name: `Mitigation stage`.
- Type: `single_select`.
- Visibility: `all` (Public).
- Options, in order: `Investigating`, `Fixing`, `QA`, `Waiting for human approval`.
- Pinned issue types: `Bug` and `Task`.

Pinning is an administrator-facing presentation setting and is not returned by the issue-field REST list response. Treat it as provisioning guidance, not as a runtime readiness gate.

Before the first GitHub write in every workflow, perform one read-only readiness check. Stop without assigning, branching, or commenting unless the field exists with the required type, visibility, and all four exact option names. Report the mismatched property; do not try to repair organization settings.

Read and update it through GitHub's issue-field REST API:

1. Send `X-GitHub-Api-Version: 2026-03-10` on every issue-field request.
2. `GET /orgs/Advance-Technologies-Foundation/issue-fields`; select the exact field name and verify the provisioning contract. Resolve the field id dynamically; do not hardcode it.
3. `POST /repos/OWNER/REPO/issues/NUMBER/issue-field-values` with only `{"issue_field_values":[{"field_id":FIELD_ID,"value":"STAGE"}]}`. Do not use `PUT`, which replaces all issue-field values.
4. `GET /repos/OWNER/REPO/issues/NUMBER/issue-field-values` and verify the stored value.

If the field, option, permission, or API support is missing, report the exact failure. Do not silently substitute a label or a Projects field, and do not claim the stage changed when verification failed.

## Completion

Report the original issue, assignee, current stage, linked branch or PR, owning repositories, blocking downstream issues, validation state, and any genuine human decision still required.

Do not add coordination artifacts beyond the assignee, stage field, Development link, issue relationships, and normal issue or PR comments required to explain a blocker.
