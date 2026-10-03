# Portable Studio implementation review and evidence

Scope: independent Clio deploy/checkout/status plus the operator's prepared-resource
contract. Clio branch is stacked on `feature/runtime-attachment` (PR #1735).

## Review

Full local agentic review ran across intent, KISS, bugs, security, testing,
performance and quality because the change crosses host execution, cluster RBAC
and persistent configuration. Findings were addressed, followed by focused bugs
and security rechecks with no remaining High/Medium findings.

Fable consultation and review through Collab:

- `rev_f90ca5f756644752`: design consultation; prepared resources and native apply.
- `rev_c9a2f25668344d6c`: operator review. Reproduced the POST-to-SSA conflict live,
  fixed authoritative apply with ownership/UID/version guards, isolated per-CR
  failures, removed secret hash annotations and clarified RBAC limits.
- `rev_7e0887862ee24a3d`: Clio review. Fixed status/spec resource-version race,
  made fixed KEDA RBAC opt-in for Studio, restricted handoff operator images to the
  bundled release, accepted empty checkout directories and sanitized malformed
  scalar failures.
- `rev_181084e5ef7b4054`: narrow operator recheck confirmed no Blocker/High issue.
  Its additional field-pruning finding was reproduced. Repeat force-apply alone
  did not solve it on Rancher. Conditional transfer of only this manager's native
  managedFields to Apply did; other managers and UID/version checks are retained.
  A focused local security recheck found no blocking issue in that repair.

## Validation

- Full Clio Unit suite: 14,975 passed, 25 skipped, zero failures (.NET 10).
- Focused Studio/OperatorInstaller/OperatorBundle suite: 43 passed, including
  real Git worktrees, dirty retry, wrong origin/HEAD and symlink rejection.
- Real Studio MCP process contract: nine passed, covering feature gating, host
  opt-in and malformed input behavior without a cluster.
- Clio .NET 8 build passed. Operator suite: 265 passed.
- Rancher probes: initial submission, phased Job completion, generated-secret
  reuse, unchanged Job identity, changed revision, KEDA Ready, bootstrapper
  binding finalizer cleanup and submission alongside 16 concurrent status writes.
- Independent actual checkout: 20 source entries at their recorded full commits.
- Prepared snapshot: 110 resources passed actual input expansion, operator plan
  validation and Kubernetes server-side dry-run with synthetic inputs.
- Existing CRM/shared infrastructure: original UID/spec hashes retained and all
  three CRM instances Ready. Omen was inspected read-only.

A final live operator revision also removed an omitted ConfigMap key while changing
another value and returning Ready; this verifies the native field-ownership
transfer through the actual controller, not only a standalone API experiment.

## Runtime acceptance completed (2026-10-03)

The prepared Omen handoff was installed on Rancher Desktop through the new
Clio/operator path. After portability repairs in the handoff, the 119-resource
subscription variant reached Ready. All source images were pulled from the
public registry; deployment did not build the platform or check out source.

- Fresh bootstrap-user PKCE login, callback and Studio session: HTTP 200.
- Real model request: `PORTABLE_MODEL_OK`.
- Twin sandbox runtime: non-mock run completed with `PORTABLE_TWIN_OK`.
- Builder created a new deterministic agent, passed its tests and seven review
  lenses, built/pushed its image, deployed it to sandbox and returned
  `PORTABLE_BUILDER_OK` through Twin. Its scope was released after verification.
- Repeat submission retained generated inputs, Secret/Job/PVC identities and
  existing CRM/shared-infrastructure specifications; three CRM instances stayed Ready.
- Builder runner network tests allowed Studio and denied direct access to Builder,
  PostgreSQL and the Kubernetes API.

The user authorized a read-only export of model/registry credentials from Omen.
Credentials stayed outside Git and the shareable profile. The temporary account
export has no refresh token and expires on October 8; recipients supply their own
model/account credentials. The final handoff includes idempotent storage and tenant
bootstrap Jobs, local identity routing, explicit Builder namespace and an
operator-managed runner NetworkPolicy using the Builder's supported external-policy
setting. These are producer handoff fixes, not application image rebuilds.

Boundaries: validation covers text Studio/Twin/Builder. Voice/LiveKit and existing
Omen project/data transfer were not validated or reproduced. CRM-to-Studio wiring,
automatic database migration and automatic whole-resource pruning remain excluded.
The preview image is not yet a published operator release. This was an installation
onto fresh local databases with iterative handoff repairs, followed by repeat
validation; it was not a second pristine-cluster replay of the final profile.

KISS check: the flow remains handoff -> Clio input resolution -> operator CR;
source checkout is an independent worktree operation. Native Kubernetes status,
field ownership and garbage collection are reused. There is no second installer
service, embedded Helm engine, build requirement or custom deployment journal.
