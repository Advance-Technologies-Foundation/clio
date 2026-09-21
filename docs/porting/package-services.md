# Package service migration

Behavior reference: Clio 8 master `11293657cb326cb3b488c891ca9e973b5aea8c14`.

| Legacy evidence | Port |
|---|---|
| `Command/AddPackageDependencyCommand.cs`, `RemovePackageDependencyCommand.cs`, `Package/PackageDependencyManager.cs` | `Composition/Packages/PackageDependencyWorkflows.cs` |
| `Package/WorkspacePackageDto.cs`, `Package/Responses/PackagePropertiesResponse.cs` | Lossless package JSON read/edit/save; portable result data |
| `Package/PackageActivator.cs`, `PackageDeactivator.cs`, activation response DTOs | `Composition/Packages/PackageActivationWorkflows.cs` |

## External contract

Package methods are POSTs under `ServiceModel/PackageService.svc/`, with `0/` for Framework environments. The primitive supplies transport and scoped SDK authentication. Composition owns method choice and body shape.

- `GetPackageProperties`: bare quoted package UId; expects `success:true` and `package` with matching `uId`.
- `SavePackageProperties`: bare complete package object. Sending a sparse descriptor can overwrite unrelated metadata in Creatio; preserve unknown fields and numeric values when editing `dependsOnPackages`.
- `ActivatePackage`: bare quoted **name**; outer success alone is insufficient. Inspect `packagesActivationResults` and retain successful package receipts even when another entry fails or is malformed.
- `DeactivatePackage`: bare quoted **UId**, resolved from the installed package catalog.

Dependency matching is case-insensitive by name for lookup/removal and by UId for duplicate additions. An existing dependency's version is retained. Addition without a version uses the installed version. Removal without a match does not save; legacy addition still saves an already-present dependency. Save responses can request compilation; that is returned to the caller, not started implicitly.

## Placement and compatibility

These are workflows over the existing HTTP capability, with no new shared API, NuGet package, Core routing rule, or primitive implementation. The parent invokes `list-packages` as a sequential child; Core retains the same environment, gate, identity and bundle. The runtime update ABI remains Contracts 10.1.0.0. CLI aliases and positional conversion stay in the CLI adapter. MCP uses existing discovery and execution tools.

The intent is reusable package operations. The flow is lookup, validate, change, return a structured outcome. There is no additional mediator, workflow engine or synchronization mechanism. Same-Core serialization does not prevent a different process from changing package properties between read and save.

## Verification boundaries

Composition tests cover both platform routes, exact quoted bodies, unknown metadata retention, duplicate identities, explicit versions, missing packages, malformed properties, no-op removal, rejected/lost saves and partial activation. CLI and MCP tests exercise the real async SDK against loopback HTTP services. A dedicated Creatio lab additionally verified catalog lookup and a no-op dependency removal on `Custom`, returning `changedCount:0`, existing dependency `CrtCore`, and no save receipt. That live check does not prove the mutation or activation paths against every Creatio version.

Package archive work remains separate. Creatio `.gz` is a gzip stream of little-endian length-prefixed UTF-16 paths and file bytes, not a tar archive. A correct port must preserve that wire format, stream data, and validate extraction paths before publishing files. Legacy `validation-pkg` is a stub; it must not be counted as a working validation capability.

## Review and known presentation limitation

Claude review `rev_206509f2923b4f01` found no blocking or P1/P2 defects. Its missing-target and Framework-activation coverage finding was accepted and the tests added. Its P3 observation about rejected save diagnostics remains a documented parity gap: the existing shared service reader returns `service-rejected` without remote error prose. Accepted saves expose `saveResult`, including compilation and validation details; rejected saves do not. Changing that shared error/redaction policy requires a separate deliberate change across all service workflows and managed callers, rather than a package-only bypass. No second unchanged review was requested.
