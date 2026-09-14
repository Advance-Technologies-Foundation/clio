# IdentityService uninstall requirements

Status: proposed scope, awaiting the repository planning checkpoint.
Issue: https://github.com/Advance-Technologies-Foundation/clio/issues/1433
Date: 2026-09-12
Source baseline: 08558fc856540b69e341a700984cc5b6f6507277 (freshly fetched origin/master)

## Intended outcome

Remove a dedicated local CRM IdentityService by itself or together with its CRM,
without deleting shared services, unrelated files, or a retained CRM database.

## Verified implementation boundary

- `IdentityServiceDeploymentService.Deploy` creates a separate IIS site and configures
  IdentityService with the CRM database connection. `PersistClioEnvironment` stores
  the token URL and OAuth credentials, but no identity deployment path or IIS ownership.
- `CreatioUninstaller.UninstallByEnvironmentName` resolves the CRM's registered
  `EnvironmentPath`. Its pipeline validates IIS targets, removes unused pools, drops
  the CRM database, removes files, and unregisters last. It has no attached identity scope.
- `IIisScanner` already provides complete inventory, target revalidation, exclusive
  application checks, and shared-pool protection. Reuse these safeguards.
- `UninstallFormViewModel` in Ring dispatches `uninstall-creatio` through `clio-run`.
  Combined removal therefore changes a Ring-consumed command contract.

## Proposed behavior

1. Add `uninstall-identity -e <environment>` and an equivalent destructive MCP tool.
   Require a registered local CRM attachment; do not accept a name or URL as deletion authority.
2. Record the minimum verified attachment during identity deployment: owning CRM,
   identity physical directory, IIS target and URL, plus the information needed to
   identify only the references created for that attachment. Do not store extra secrets.
3. Resolve and validate the complete removal scope before destructive work. Verify
   physical paths, IIS topology, ownership, and conflicting known attachments.
   Old deployments without sufficient ownership evidence remain untouched.
4. Standalone removal preserves CRM and its database. Clear only CRM settings and
   Clio references still matching the removed attachment. Preserve unrelated credentials.
5. Named CRM uninstall includes verified exclusive identity targets in its existing
   reporting and progress flow. Shared, remote, or ambiguous attachments are preserved
   with an explicit explanation; output must never imply they were removed.
6. Retain shared pools and their profiles. Drop the CRM database once in combined
   removal, and never during standalone removal.
7. Keep enough attachment information after interruption for a bounded retry. Missing
   artifacts are acceptable only when ownership can still be verified; a retry must
   never select replacement or newly shared artifacts based on stale names.

## Acceptance and validation

- Given a disposable CRM with its dedicated identity, combined uninstall removes both
  IIS targets and dedicated files/pools and removes the CRM database once.
- Given the same deployment, standalone uninstall preserves CRM, its database, and
  unrelated integrations, and removes only identity and matching references.
- Given a shared pool, unrelated applications and the pool survive either operation.
- Given remote/shared/ambiguous identity ownership, identity is preserved and the
  reason is visible in CLI and MCP results.
- Given no attached identity, existing CRM uninstall behavior remains unchanged.
- Given missing artifacts or an interrupted run, retry stays within the original
  verified scope and reports partial failure accurately.
- NUnit/FluentAssertions coverage exercises these boundaries. MCP E2E covers the
  new tool and changed combined result. A real disposable IIS smoke test proves the
  filesystem, IIS, reference cleanup, and database postconditions.
- Run Ring tests, its relevant contract/harness path, and Windows x64 NativeAOT
  publish because Ring consumes the changed command.
- Review CLI help, command docs/index/anchors, MCP descriptions and discovery,
  shipped templates, and published lifecycle guidance. Any required guidance change
  belongs in a separately versioned clio-knowledge PR.

## Scope and related work

Clio owns the missing executable capability. This excludes AI Studio Identity Platform,
remote identity administration, and removal of any currently running user environment.
Only a newly provisioned disposable environment may be used for destructive validation.

Open issue searches for identity and unassigned uninstall work found no candidate
to group with this repair. #1432 is closed and supplies complementary verification.
#1417 is assigned to another owner and addresses macOS test paths; it remains separate.

## KISS check

The proposed flow is record attachment, prevalidate scope, perform guarded cleanup,
then clear matching references. Reuse the existing IIS scanner, path identity,
reservations, and progress emitter. Do not introduce a second installer framework,
distributed ownership protocol, or generic recovery engine.

The next planning phase must resolve the minimal durable retry state and safe ordering
of CRM reference cleanup before implementation. No implementation or runtime validation
has been performed yet.
