# Package file content and runtime-only delivery

`show-package-file-content` lists or reads files materialized for a compiled Creatio package. This is a remote ClioGate operation, not inspection of a local package archive. The target must already have ClioGate 2.0.0.47 or newer installed.

## Use

```powershell
clio show-package-file-content --environment dev --package MyPackage
clio show-package-file-content --environment dev --package MyPackage --file Files/cs/MyClass.cs
```

Use a registered environment from the explicitly configured Clio 10 settings file. As with other service commands, `--timeout` is an optional positive request deadline in milliseconds (default 100000). Discover the active runtime's schema with `show-package-file-content --help`, `--list`, or MCP `list-operations`. MCP invokes the same operation through its existing `execute` tool, with `package` and optional `file` arguments.

Omit `file` (or supply a blank value, following legacy behavior) to get `package-name`, normalized and sorted `files`, and `count` in the result payload. Supply a package-relative file path to get `package-name`, `file-path`, verbatim `content`, and `content-length`. The legacy `content-length` field counts UTF-16 code units, not bytes. JSON presentation belongs to the adapter. Absolute paths and dot traversal fail before authentication. Authentication, HTTP and invalid response failures return nonaccepted structured results. There are no workflow-level retries or automatic ClioGate installation.

The legacy aliases `show-files`/`files`, human-readable legacy console output, and the resident MCP tool's optional generated-project enrichment are not ported. The new command is available through the stable MCP wrapper; no new resident tool is added. Live Creatio validation remains pending. Tests use controlled responses and real loopback HTTP through the async Creatio client.

## Boundary exercised

Only Composition adds a workflow and its discovery registration. It uses the existing runtime-owned HTTP interface. There are no changes to Core, host Contracts, Primitives, the CLI parser or MCP adapter. The earlier V1/V2/V3 fixture proof separately covers introducing and breaking a feature interface; this real port does not need an artificial new interface.

The immutable installed preview 20 host was tested with the production runtime 10.8.0.0, then a NuGet V3 loopback feed advertised the newly published 10.9.0.0 runtime package. The same live MCP client discovered the previously absent operation and executed both listing and content reads. Its process remained alive (PID 25324); the installed host SHA256 remained `004EE30D2396FBA21E191698F266CCD68017E6FFF3E8B2CF27F81252A18F63AA`. A fresh process used cached 10.9 with the feed stopped.

Artifacts are local, not public NuGet releases:

- `artifacts/runtime-packages/Clio10.RuntimeBundle.10.9.0.nupkg`
- `artifacts/published-runtime-check/10.9.0/`
- `artifacts/production-runtime-10.9-proof.log`

Reproduce the explicit packaged-release test from the repository root:

```powershell
$env:CLIO10_TEST_PRODUCT = "$PWD/artifacts/tool-preview20/.store/clio/10.0.0-preview.20/clio/10.0.0-preview.20/tools/net10.0/any/Clio10.dll"
$env:CLIO10_TEST_RELEASE_BASELINE = "$PWD/artifacts/published-runtime-check/10.8.0"
$env:CLIO10_TEST_RELEASE_PACKAGE = "$PWD/artifacts/runtime-packages/Clio10.RuntimeBundle.10.9.0.nupkg"
dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -m:1 --filter FullyQualifiedName~Production_runtime_adds_package_file_command
```

The test intentionally retains its temporary cache and prints its path for inspection. No live Creatio environment is modified. This demonstrates runtime delivery and wrapper discovery, not automatic agent decisions to rediscover, resident MCP schema refresh, host replacement or old-runtime unloading.

## Review and regression

Claude review `rev_3d0d0fb5311c432b` found no P1/P2 issues and accepted the same-connection proof. Two P3 suggestions would change legacy semantics: renaming `content-length` to bytes and rejecting blank `file`. Both were declined for this port; explicit Unicode length and blank-input tests now pin the intended behavior. Claude read the files and proof log but did not execute tests. The full review is retained in `artifacts/reviews/rev_3d0d0fb5311c432b.md`.

The full solution run passed 312 tests (`artifacts/runtime-10.9-regression.log`). The subsequent focused run passed all 14 package-file composition cases, including the two added blank-input cases. Two added normal MCP product cases passed (`artifacts/package-file-mcp-tests.log`), and the explicit installed-host update proof passed separately. No production code changed after publishing runtime 10.9. The installed old CLI also discovered the new runtime's help schema (`artifacts/runtime-10.9-old-cli-help.log`).

KISS check: this adds one workflow using existing HTTP services and registration. It adds no loader, compatibility mechanism, host dependency, new NuGet dependency or invented primitive API.

The new/breaking feature-interface fixture was also rerun against the same installed preview 20 host and passed (MCP PID 10256; `artifacts/runtime-10.9-installed-interface-proof.log`). It proves an old in-flight workflow stays pinned while subsequent calls use new and then incompatible feature-interface revisions, all within one host process.
