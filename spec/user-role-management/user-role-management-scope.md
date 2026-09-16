# Agent-facing user and role management

Coordination: https://github.com/Advance-Technologies-Foundation/clio/issues/968

## Required outcome

An agent can inspect and manage Creatio users, organizational and functional roles,
manager roles, membership, licenses and access rules through supported administration
contracts, with documented verification and meaningful failures.

The user explicitly expanded the original functional-role defect to the whole
administration workflow, including unlocking users and changing passwords. Partial
increments must not close the coordination issue.

## Delivery parts

| Part | Required behavior | Proof |
|---|---|---|
| Discovery and roles | List/read users and roles; create/update/delete roles; inspect and change organizational hierarchy | Native editor agrees with persisted identity, type and parent; reject invalid parents and ambiguous identities |
| Users and credentials | Create/update/deactivate/reactivate/delete users; inspect lock state; unlock; change password | Disposable user can authenticate with changed credentials; invalid password rejected; unrelated attributes preserved; no secret output |
| Membership and managers | Add/remove direct members; create/read/remove manager role; manage its members; add/remove organizational-functional associations | Direct and effective memberships distinguished; hierarchy and manager page agree; effects verified after actualization |
| Licenses and access | Inspect/assign/remove available licenses; inspect/grant/revoke delegation and operation permissions | Available-capacity checks, direction of grants, effective behavior and revocation verified; do not conflate delegation, operation permissions and record rights |
| Guidance and acceptance | Update existing rights guidance and route to one canonical administration owner; CLI/MCP contracts and E2E coverage | A fresh agent can execute the complete lifecycle using shipped guidance and tooling without local source access |

LDAP synchronization, external identity credentials, technical users and external users
must be explicitly classified from source and runtime evidence. Do not silently treat
these as ordinary password users or claim untested support.

## Implementation principle

Use typed operations backed by native Creatio services, registered service URLs,
IApplicationClient and existing environment-aware MCP adapters. Reuse existing
record-rights and query capabilities. Do not introduce a generic privileged SQL
executor, custom authorization engine or orchestration protocol.

The implemented surface is manage-user, manage-role, manage-license and manage-access, each with a
separate read-only MCP inspection tool. Responses need
endpoint-specific interpretation: HTTP 200 alone does not establish success. A
failed batch can have partial effects, so verification must identify what changed.

## Verification boundary

Use a dedicated issue-968 environment and disposable fixtures. Preserve evidence
for native request/response shape, persisted state, effective behavior and reopened
administration UI. Never modify existing real user credentials for tests. Test
permission-denied calls with an ordinary disposable account as well as admin success.

Claude participates in diagnosis, contract/design challenge and stable final review.
Repository agentic review, provider unit/E2E tests, relevant Ring compatibility and
guidance publication checks remain required before delivery.
