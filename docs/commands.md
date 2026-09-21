# Command semantics

Archive migration is in progress: see [package archive semantics and limits](porting/package-archives.md). Current source and local preview 19 expose 20 operations with ZIP/directory batch support. Preview 17 retains its original single-archive implementation. The batch APIs require a host with Contracts ABI 10.3.0.0. Previews are locally packaged only, not published to NuGet.

Product supports `--list`, `--execute <operation> <url> <user> <netcore|framework> [arguments-json]` and `mcp <url> <user> <netcore|framework>` for stdio MCP. Password comes from CLIO10_PASSWORD. URL must be an HTTP(S) application root ending in slash; embedded credentials, query and fragment are rejected.

Local-only workflows use `--execute <operation> --local [arguments-json]` or `mcp --local`. No placeholder HTTP configuration is needed. HTTP workflows validate their target and credentials when invoked. The shipping product registers the 20 operations listed in the [migration ledger](porting/README.md); PartnerHost is a test-only product adding partner operations through the unchanged adapters.

Restart authenticates then posts to ServiceModel/AppInstallerService.svc/RestartApp on .NET, or 0/ServiceModel/AppInstallerService.svc/UnloadAppDomain on Framework. Flush uses ClearRedisDb. SDK owns authentication/CSRF. Initial login is reused during a sequential session.

2xx returns http-accepted and preserves the response; this is not readiness or universal business success. Post-dispatch transport failure is outcome-unknown; establish server state before repeating side effects. Authentication failure prevents maintenance. Cancellation cannot undo a dispatched server action.

For handled outcomes, CLI returns 0 for acceptance, 1 for rejected/unknown results, 2 for invalid configuration/JSON conversion, 130 for cancellation. MCP marks nonaccepted results as errors. Unexpected exceptions from trusted plugin code are not covered by these handled-result guarantees. Tools are `list-operations` (descriptors and input schemas) and `execute` (operation plus optional arguments). The bridge is conservatively marked destructive. Earlier prototype resident restart/flush tools are replaced: restart is now execute with `{"operation":"restart"}`.

In static mode, a host registers partner workflows through the AddClioCli/AddClioMcp composition callback. No per-command adapter edits are needed. Partner compare-texts takes `{"files":{"left":"<path>","right":"<path>"}}`, calls inspect-text twice with separate arguments and returns a structured comparison. File access uses host permissions; trusted plugins are not sandboxed.

CLIO10_BUNDLES and CLIO10_PRIMITIVE_VERSION control local selection. CLIO10_ALLOW_UNTRUSTED_CERTIFICATE=true is an explicit certificate trust opt-out for developer labs. No legacy settings are modified.

Set `CLIO10_RUNTIME_COMPOSITION=true` with `CLIO10_BUNDLES` to use complete runtime releases. Both `--list` and MCP `list-operations` then discover the selected runtime's workflows. The existing `--execute` and MCP `execute` interfaces invoke them; newly supplied workflows do not need a new resident MCP tool. Register partners inside the runtime in this mode; host registrations are rejected rather than ignored.

Read-only runtime discovery preserves error codes such as `bundle-directory-unavailable` and `no-compatible-bundle`. These errors do not mean an external command ran. Missing runtime configuration exits with code 2 and a configuration message. See [runtime update proof](runtime-update-proof.md) for source/package/interval settings, compatibility limits and the locally installed preview.

## Conventional command syntax

The generic execution form remains supported. The CLI also accepts named operations and schema-driven flags:

```shell
clio ping-app --uri https://your-creatio/ --login username
clio compile -e lab --settings ./clio10-settings.json --modified-items
clio call-service -e lab --settings ./clio10-settings.json --service-path rest/Custom/Method --method POST --input request.json --destination response.json
clio list-packages -e lab --settings ./clio10-settings.json --filter MyPackage
clio get-pkg-version ./MyPackage
clio set-pkg-version ./MyPackage --package-version 1.2.3
clio generate-source-code --help
```

`-e` selects a name in a Clio 10 settings dictionary. Example shape:

```json
{"lab":{"BaseUri":"https://your-creatio/","UserName":"username","Password":"replace-locally","IsNetCore":true}}
```

Protect that file through local permissions; it is not a legacy settings import. Local package operations need no environment or password. Direct `--uri` and settings selection cannot be combined. The adapter rejects unknown, duplicate, or incorrectly typed flags before external execution. For boolean flags use `--flag=false` to disable explicitly. Use `--arguments` for an operation JSON object; individual flags cannot duplicate its keys.

The new service workflows expose `--timeout` in milliseconds. Defaults are 60,000 for call-service, 3,600,000 for source generation, and 100,000 for the other new services. Login uses the SDK's separate authentication timeout. These are request budgets, not a promise that remote work stops on timeout.

Common aliases include `compile`/`build`/`rebuild`, `gsc`, `lcl`, `cs`, `ds`, `gpv`/`spv`, `get-pkg-list`, and `get-app-list`. This is not yet complete legacy syntax compatibility. Results remain structured JSON; old text/table formats and command-specific MCP resident tools have not been recreated. MCP was reviewed: existing discovery and execute tools expose the new workflows, verified through real stdio tests; no resident-tool schema change is required.

Ping preserves legacy platform defaults: .NET probes the application root; Framework posts to `0/ping`. An explicit endpoint is honored on either platform. It reports `http-reachable` after successful authentication and a page or redirect response, which does not mean the application is ready. Composition never follows the redirect. Other service workflows still reject redirects.

`call-service` accepts well-formed XML (including OData metadata) and preserves raw response text in destination files. DTD/external-entity XML and HTML documents are rejected. New Unix destination files are mode 0600; explicitly adjust permissions if another account needs to read an export. Existing file modes are retained. Localized package descriptor metadata remains readable when changing a version.

## Package dependencies and activation

```shell
clio add-pkg-dep -e lab --settings ./clio10-settings.json --package-name Custom --dependencies Base,Other:1.2
clio remove-pkg-dep -e lab --settings ./clio10-settings.json --package-name Custom --dependencies Other
clio activate-pkg Custom -e lab --settings ./clio10-settings.json
clio deactivate-pkg Custom -e lab --settings ./clio10-settings.json
```

The canonical dependency operations are `add-package-dependency` and `remove-package-dependency`; both also accept `add-pkg-dependency`/`remove-pkg-dependency`. Their `dependencies` argument is a comma-separated string, including for MCP `execute`. Addition accepts `name:version`, defaults to the installed version, and preserves an existing dependency's version and metadata. Removal ignores an optional version suffix and does not save when nothing matches. Both preserve all unrelated package properties when saving. `saveResult` carries the platform's `compilationRequired` and validation details; compilation is not automatic. A lost or cancelled save response is `outcome-unknown`, with no workflow retry.

Activation aliases are `apkg`, `activate-package`, and `enable-package`; deactivation aliases are `dpkg`, `deactivate-package`, and `disable-package`. Both accept positional package name or `--package-name`, and a positive `--timeout` in milliseconds (default 100,000). Activation sends the name; deactivation resolves and sends the installed UId. A failed individual activation makes the overall result nonaccepted while `AcceptedSteps` retains successful activations. This deliberately corrects the legacy command's success exit code after an individual activation failure.

All four workflows use only the existing HTTP capability and shared Contracts ABI 10.1.0.0. Catalog children borrow their parent's Core session and selected bundle. No primitive, Core, or resident MCP contract change is needed. Calls from different processes can still race; this port does not claim server-side concurrency control.

## Package archives

```shell
clio compress ./Example -d ./Example.gz --skip-pdb
clio extract ./Example.gz -d ./output
clio extract ./Example.gz -d ./output --overwrite
clio compress ./packages -p First,Second -d ./packages.zip
clio extract ./packages.zip -d ./output
clio extract ./gzip-directory -d ./output
```

Compression selects standard package directories and `descriptor.json`, including a single `branches/<version>` layout. Workspace, package and nested `clioignore` rules filter that content. `-s` is the short skip-PDB option. Without `-d`, the archive path is the package path plus `.gz`.

`--packages`/`-p` creates a ZIP of the named package directories, with one root gzip entry per package. Without a source directory, it uses the working directory; without `-d`, it uses the legacy timestamped ZIP filename. Hidden legacy `--Packages`, `--SkipPdb` and `--DestinationPath` spellings remain supported.

Extraction requires an existing output parent (default: working directory) and creates `output/Example` for `Example.gz`. ZIP input selects root gzip entries; directory input selects immediate gzip files. Existing packages require `--overwrite`; each entire old package is replaced after all selected archives validate. Corrupt or cancelled validation preserves old files. Publication can partially succeed if a later filesystem rename fails: `AcceptedSteps` and the `packages` payload identify completed packages, while `failedDestination` identifies the stopped publication. Do not automatically retry such a result.

An unavailable output directory returns `archive-destination-unavailable`. All ZIP names, including ignored metadata, must satisfy the portable-name rules; reserved device names, trailing spaces and unsafe directory markers are rejected.

`retainedBackup` reports backup cleanup that needs attention after successful publication. `archive-recovery-required` reports a failed publication whose automatic restoration also failed, with the preserved backup path. See [archive semantics](porting/package-archives.md) for the full limits and intentional differences from Clio 8.

MCP uses `execute` with `operation: "generate-pkg-zip"` and arguments `package-path`, optional `destination-path`, optional `skip-pdb`, and optional comma-separated `packages`; extraction uses `operation: "extract-pkg-zip"` with `archive-path`, optional `destination-path`, and optional `overwrite`. Both operate locally without Creatio credentials. No dedicated resident archive tools are added.

## Directory comparison

Use `clio compare-directories --left ./before --right ./after` for a read-only recursive SHA-256 comparison. `added` is right-only, `removed` is left-only, and `changed` means content differs at the same relative path. See [full semantics and errors](compare-directories.md). Available through the existing generic MCP `execute` tool without a Creatio environment.
