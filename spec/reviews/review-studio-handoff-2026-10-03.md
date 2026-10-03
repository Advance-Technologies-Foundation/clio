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

## Remaining release gate

Full snapshot login, Twin reply and Builder build/deploy/reply are **not proven**.
Recipient model access is still unspecified. Browser/pod OIDC routing and other
application-specific snapshot configuration need validation. Draft PRs must not
be described as a completed environment reproduction. No CRM–Studio wiring,
automatic data migration or automatic whole-resource pruning is included.

KISS check: the flow remains handoff -> Clio input resolution -> operator CR;
source checkout is an independent worktree operation. Native Kubernetes status,
field ownership and garbage collection are reused. There is no second installer
service, embedded Helm engine, build requirement or custom deployment journal.
