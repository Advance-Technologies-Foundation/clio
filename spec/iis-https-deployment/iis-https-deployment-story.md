# IIS HTTPS deployment story

Status: done
Issue: #887

As a Windows developer, I want clio to use an installed machine certificate for IIS deployments when available, so HTTPS works for both .NET Framework and .NET 8 without making certificate absence block local development.

## Work

- Implement certificate catalog, eligibility, pin preference, and deterministic fallback.
- Implement `pin-certificate`, persistence, schema refresh, CLI registration, and documentation.
- Create exclusive IIS bindings, attach the certificate, and transform .NET Framework configuration.
- Route actual scheme through registration, receipts, and launch.
- Add MCP `useHttps` and ClioRing Local HTTPS selection.
- Add cross-platform unit/contract tests and safe non-deploying MCP stdio E2E.
- Run explicit disposable validation with both approved archives and verify cleanup.

## Definition of done

- [x] AC-01 through AC-09 in the PRD are satisfied.
- [x] Targeted/full unit gates, safe MCP E2E, Ring tests, and NativeAOT publish pass.
- [x] NetFramework and Net8 disposable targets are removed from settings, IIS, disk, and database.
- [x] Command docs, deploy docs, command index, Wiki anchors, schema, MCP guidance, and workspace templates are reviewed and aligned.
- [x] Comprehensive agentic review has no unresolved Blocker or High finding.
