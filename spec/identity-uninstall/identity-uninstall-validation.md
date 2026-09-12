# Identity uninstall validation

Validated on Windows, 2026-09-12, against freshly fetched master
`08558fc856540b69e341a700984cc5b6f6507277`.

## Automated checks

- Focused identity/settings/IIS/combined/manifest tests: 403 passed.
- `dotnet test clio.tests/clio.tests.csproj -c Debug --filter "Category=Unit"`:
  12,736 passed, zero failures, 25 existing platform/explicit skips.
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter
  "FullyQualifiedName~UninstallIdentityToolE2ETests|FullyQualifiedName~DeployIdentityToolE2ETests"`:
  six passed, zero skipped. Real stdio MCP discovery, worker invocation, empty and invalid attachments.
- `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`: 157 passed.
  Includes typed stage adapter unknown-field tolerance and ordered replay. Provider and Ring
  committed stage-contract fixtures remain byte-identical; the new manifest stage is additive.
- `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64
  --self-contained true -p:PublishAot=true`: passed without IL2026/IL3050 warnings.
- No new CLIO analyzer diagnostics in modified code. Existing MobileDiffApplyValidator warnings remain.

## Disposable IIS proof

Created a new exclusive Creatio 10.1.585 PostgreSQL instance with the Clio lab script, using an
isolated CLIO_HOME. No existing registered instance was removed.

1. Deployed custom-named identity with `--no-app`; read back its resolved attachment from appsettings.
2. CLI standalone uninstall succeeded. Authenticated `get-info` still returned the CRM version and
   PostgreSQL engine, proving the retained CRM/database remained usable.
3. Redeployed identity with OAuth. Deployment verified discovery, issued a token, and accepted that
   token in a real CRM bearer request before saving credentials.
4. Ran `IdentityUninstallLiveE2ETests` with explicit destructive opt-in and the private smoke home:
   real MCP standalone removal cleared matching local credentials and emptied the attachment;
   CRM still responded; MCP no-app redeployment succeeded; MCP combined removal succeeded and
   removed both folders and the environment registration. One test passed, zero skipped.
5. Fresh native IIS readback found neither lab site nor either dedicated pool; both directories
   were absent. Unit tests independently assert exactly one CRM database drop and no drop in
   identity artifact cleanup.

The opt-in smoke fixture requires `CLIO_IDENTITY_SMOKE_HOME`,
`McpE2E__AllowDestructiveMcpTests=true` and `McpE2E__Sandbox__EnvironmentName` for an exclusively
owned disposable CRM that already has a deployed identity. It permanently removes that CRM.

## Review disposition

The full agentic fan-out covered intent, simplicity, quality, bugs, security, performance and tests.
Accepted findings were fixed: one empty predicate, terminal failure on post-reservation revalidation,
virtual-directory sharing checks, removal of duplicate adjacent scans, and the three missing
deployment/order/persisted-retry tests. Narrow security and testing rechecks found no remaining findings.
Claude agreed with the design; the final implementation review is recorded in the pull request.

KISS check: the environment owns one optional attachment. Deployment records it; removal validates
and consumes it. Existing settings updates, reservations, IIS protection and CRM stages are reused.
There is no independent registry, database discovery or new recovery framework.

Docs and MCP reviewed and updated. ClioRing compatibility reviewed: its uninstall form and action
dispatch were inspected alongside the IPC adapter and the commands above. The guidance companion is
Advance-Technologies-Foundation/clio-knowledge#164; topic names are unchanged, so the pinned topic-name
fixture and workspace templates need no change.
