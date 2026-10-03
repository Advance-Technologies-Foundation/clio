# ADR: portable Studio installation and independent source checkout

Status: accepted under the user's implementation authorization, 2026-10-03.
PRD: [portable Studio](../prd/prd-studio-handoff.md).

## Decision

Use versioned JSON (`creatio-studio-handoff/v1`) as a transport-neutral handoff.
Clio owns input resolution, local Git worktrees, and submission through kubectl.
The operator owns a `CreatioAiStudio` resource and cluster reconciliation. Do not
introduce a standalone installer service or require Git/build tools for deploy.

The handoff has `name`, `sources`, `inputs`, `operator` and `deployment` sections.
Each source has name, URL, branch, full commit SHA and relative workspace path.
Checkout validates only schema/name/sources, never deployment or required inputs.
Git caches live in `.studio-repositories/<name>.git` beneath the requested root.
Worktrees are detached at the recorded commit. The branch establishes fetch and
provenance; no existing checkout is reset. Reruns verify the existing worktree's
Git identity and commit. Dirty worktrees are preserved; a mismatched target fails.
Reject unsafe paths, aliases/collisions, credential-bearing URLs and reparse-point
ancestors; invoke Git through argument lists, never an interpolated shell.

Deploy resolves string placeholders `${inputName}` in the deployment JSON tree,
never by evaluating code. Inputs can provide a value, declare required/secret,
or explicitly request a generated local password/key. Overrides use a JSON file
to avoid secrets in process arguments. Missing inputs are emitted together before
any cluster write. Generated values are persisted only in the installation's
Kubernetes Secret and reused. The resolved deployment travels as a Secret; the
custom resource contains its reference and hash, never credentials.

Prepared Kubernetes resources travel with the handoff in ordered phases. This
preserves the existing charts as producer inputs without installing Helm or Git
inside the operator. The operator validates the complete plan, ownership, allowed
resource kinds and namespace boundaries before applying a phase. Native resource
status supplies readiness, including migration Job completion. Job identities are
derived from content; completed identical Jobs are reused and changed migration
content gets a new Job. No automatic uninstall or database upgrade is introduced. Shared external service
bindings can be checked by explicit prerequisite Jobs; their resources are not
in the plan. Dedicated services remain the default where compatibility is unproven.

The Studio API is a bounded application deployment surface, not an arbitrary
cluster installer: no CRDs, cluster-admin bindings or foreign namespace adoption
in a handoff. Required cluster capabilities are explicit prerequisites. Studio's
environment provisioning permissions must be supplied through a narrowly scoped
operator-supported bootstrap contract, not a general privilege-escalation path.

Use the existing Clio runtime process/host guards and operator queue. Add host MCP
adapters for checkout/deploy/status. Deploy submits an operation; status can be
polled without holding an MCP call through image pulls and migrations.

## Alternatives

- Clone and run metarepo setup: rejected; it builds images and makes source access
  mandatory for demo recipients.
- New installer executable: rejected; duplicates Clio's existing entry point.
- Helm jobs as a second deployment engine: defer unless real chart hooks cannot
  be represented as explicit resource phases. Keep producer rendering separate.
- Automatically share PG18/Redis: rejected until required capabilities are proven.

## Validation

Unit tests cover independent paths, missing inputs, unsafe paths, retries,
ownership, resource readiness and secret-safe failures. Integration tests use
local Git repositories for real worktree semantics. A disposable Rancher Studio
installation validates the actual Omen-derived artifact, never Omen mutations.


KEDA uses one fixed, reproducibly generated dependency bundle. Clio's ordinary
operator bootstrap omits its fixed roles; Studio opts in when the plan requests
KEDA. The handoff cannot select arbitrary cluster controller code: an optional
operator digest must match Clio's bundled release, and compatible installed
controllers are not implicitly downgraded.

Resource creation uses POST to fail on ownership races. Updates verify the Studio
UID label and controller owner reference (retained PVCs use the label), then use
native authoritative SSA with UID/resourceVersion preconditions. A conditional native managedFields patch first transfers only this controller's
POST/Update field set to Apply so removed fields are pruned. Authoritative apply
repairs declared fields; neither step bypasses object identity/version checks. The CR's bootstrapper finalizer revokes its fixed
cluster binding before deletion finishes. Unchanged Ready installations skip the
short poll and participate in the ordinary janitor's full reconciliation.
