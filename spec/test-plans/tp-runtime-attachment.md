# Attachment verification

- Unit: dependency failures before provider mutation; unsupported provider; invalid workspace; attach receipt ownership; detach uses saved provider; ambiguous runtime selection; remote UID replacement refusal.
- Integration: real CLI help and missing-dependency failure; live local-to-runtime and runtime-to-local file changes; detach preserves local/remote files and runtime.
- Run full unit suite because Program and DI registration change.
- Review MCP and ClioRing compatibility; new commands are host-only and no existing contract changes.

## Validation evidence

- Built successfully for .NET 8 and .NET 10.
- All 11 attachment unit tests passed. The full Unit run passed 13,991 tests and skipped 25; its sole failure was the new host-only file inventory. After regenerating that inventory, the failing coverage test passed on rerun. Commands: `dotnet test clio.tests/clio.tests.csproj --filter Category=Unit`, followed by focused `RuntimeAttachmentTests` and `UnreachableProductFiles_ShouldMatchThePinnedList` filters.
- Validated against a disposable Creatio environment: attach, source changes in both directions, workspace settings delivery, detach preserving runtime files and FSM links, and same-workspace reattachment.
- Verified a conflicting existing package is rejected and subsequent detach preserves that package.
- Confirmed missing Mutagen fails before runtime preparation. Installed Mutagen 0.18.1 was used for live validation.
- Runtime FSM configuration and effective status were checked separately; configuration alone does not prove that a restart has taken effect. Package export completed through the existing Clio HTTP service.
- Agentic review completed; endpoint errors, concurrent link creation and failed-attachment cleanup findings were resolved.
- MCP reviewed, no update required: attach/detach are host-side bootstrap operations, deliberately absent from the remote server tool surface. The unreachable-product-file inventory explicitly records that boundary.
- ClioRing compatibility reviewed, no Ring-consumed contract changed. Inspected `clio-ring/ClioRing/Services/ClioAdapter.cs` and `IClioAdapter.cs`; environment-listing and existing command outputs are unchanged.
- KISS check: provider discovery/preparation, one local receipt and an existing sync engine; no additional transport, server or deployment controller.

Known preview boundaries: existing SSH alias required; runtime FSM must already be prepared; automatic agent MCP configuration and adoption of existing runtime package directories are not implemented. New workspace packages can be linked; existing or foreign package paths fail safely.
